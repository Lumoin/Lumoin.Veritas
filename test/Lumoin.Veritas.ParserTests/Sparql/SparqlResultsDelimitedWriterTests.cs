using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for the unified <see cref="SparqlResultsDelimitedWriter"/>: the exact CSV and TSV serialized shapes,
/// unbound-field handling, CSV quoting, the ASK rejection, and that the streaming line producer reproduces the
/// materialized output.
/// </summary>
[TestClass]
internal sealed class SparqlResultsDelimitedWriterTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    private static NamedNode Iri(string iri) => new(Utf8Strings.From(iri));

    private static Literal Typed(string value, string datatype) => new(Utf8Strings.From(value), Iri(datatype));

    private static Literal Lang(string value, string language) => new(Utf8Strings.From(value), new NamedNode(Vocabulary.Rdf.LangString), Utf8Strings.From(language));

    private static SparqlBinding Bind(string variable, RdfTerm value) => new(new SparqlVariable(Utf8Strings.From(variable)), value);

    private static List<Utf8String> Vars(params string[] names)
    {
        List<Utf8String> variables = new(names.Length);
        foreach(string name in names)
        {
            variables.Add(Utf8Strings.From(name));
        }

        return variables;
    }

    private static SparqlResultSet SampleSelect()
    {
        return SparqlResultSet.ForSelect(
            Vars("s", "age", "name", "b"),
            [
                new SparqlSolution([Bind("s", Iri("http://example/a")), Bind("age", Typed("30", Xsd + "integer")), Bind("name", Lang("Bob", "en")), Bind("b", new BlankNode(Utf8Strings.From("x")))]),
                new SparqlSolution([Bind("s", Iri("http://example/a")), Bind("name", Lang("Bob", "en")), Bind("b", new BlankNode(Utf8Strings.From("x")))])
            ]);
    }

    /// <summary>TSV writes a ?-prefixed header and Turtle term values, with an unbound variable as an empty field.</summary>
    [TestMethod]
    public void TsvWritesTurtleTermsAndEmptyForUnbound()
    {
        string tsv = SparqlResultsDelimitedWriter.WriteToString(SampleSelect(), SparqlDelimitedResultsFormat.Tsv);

        string expected =
            "?s\t?age\t?name\t?b\n"
            + "<http://example/a>\t\"30\"^^<http://www.w3.org/2001/XMLSchema#integer>\t\"Bob\"@en\t_:x\n"
            + "<http://example/a>\t\t\"Bob\"@en\t_:x\n";
        Assert.AreEqual(expected, tsv);
    }

    /// <summary>CSV writes a bare header and lossy values (CRLF rows), with an unbound variable as an empty field.</summary>
    [TestMethod]
    public void CsvWritesLossyValuesAndEmptyForUnbound()
    {
        string csv = SparqlResultsDelimitedWriter.WriteToString(SampleSelect(), SparqlDelimitedResultsFormat.Csv);

        string expected =
            "s,age,name,b\r\n"
            + "http://example/a,30,Bob,_:x\r\n"
            + "http://example/a,,Bob,_:x\r\n";
        Assert.AreEqual(expected, csv);
    }

    /// <summary>CSV quotes a field containing a comma and doubles embedded quotes (RFC 4180); TSV escapes inside the Turtle string instead.</summary>
    [TestMethod]
    public void CsvQuotesFieldsNeedingEscaping()
    {
        SparqlResultSet results = SparqlResultSet.ForSelect(Vars("v"), [new SparqlSolution([Bind("v", Typed("a,\"b\"", Xsd + "string"))])]);

        Assert.AreEqual("v\r\n\"a,\"\"b\"\"\"\r\n", SparqlResultsDelimitedWriter.WriteToString(results, SparqlDelimitedResultsFormat.Csv));
        Assert.AreEqual("?v\n\"a,\\\"b\\\"\"\n", SparqlResultsDelimitedWriter.WriteToString(results, SparqlDelimitedResultsFormat.Tsv));
    }

    /// <summary>Both formats reject an ASK (boolean) result, which has no tabular form.</summary>
    [TestMethod]
    public void AskResultsAreRejected()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => SparqlResultsDelimitedWriter.WriteToString(SparqlResultSet.ForAsk(true), SparqlDelimitedResultsFormat.Tsv));
        Assert.ThrowsExactly<NotSupportedException>(() => SparqlResultsDelimitedWriter.WriteToString(SparqlResultSet.ForAsk(true), SparqlDelimitedResultsFormat.Csv));
    }

    /// <summary>The streaming line producer yields the header first and reproduces the materialized serialization.</summary>
    [TestMethod]
    public async Task StreamingLinesReproduceMaterializedOutput()
    {
        SparqlResultSet results = SampleSelect();
        StringBuilder streamed = new();
        int lineCount = 0;
        string? firstLine = null;
        await foreach(string line in SparqlResultsDelimitedWriter.WriteLinesAsync(results.Variables, Stream(results.Solutions), SparqlDelimitedResultsFormat.Tsv, TestContext.CancellationToken).ConfigureAwait(false))
        {
            firstLine ??= line;
            streamed.Append(line);
            lineCount++;
        }

        Assert.AreEqual(3, lineCount);
        Assert.AreEqual("?s\t?age\t?name\t?b\n", firstLine);
        Assert.AreEqual(SparqlResultsDelimitedWriter.WriteToString(results, SparqlDelimitedResultsFormat.Tsv), streamed.ToString());
    }

    /// <summary>The byte-native <see cref="PipeWriter"/> overload writes the same bytes (both formats) as the materialized serialization.</summary>
    [TestMethod]
    public async Task WriteAsyncToPipeReproducesMaterializedOutput()
    {
        SparqlResultSet results = SampleSelect();

        await AssertPipeMatchesString(results, SparqlDelimitedResultsFormat.Tsv).ConfigureAwait(false);
        await AssertPipeMatchesString(results, SparqlDelimitedResultsFormat.Csv).ConfigureAwait(false);
    }

    /// <summary>Writes a result set through the <see cref="PipeWriter"/> overload and asserts the bytes equal the materialized text, captured over an in-memory pipe with no intermediate stream.</summary>
    /// <param name="results">The result set to serialize.</param>
    /// <param name="format">The delimited format to emit.</param>
    /// <returns>The asynchronous assertion.</returns>
    private async Task AssertPipeMatchesString(SparqlResultSet results, SparqlDelimitedResultsFormat format)
    {
        Pipe pipe = new();
        await SparqlResultsDelimitedWriter.WriteAsync(results, pipe.Writer, format, TestContext.CancellationToken).ConfigureAwait(false);

        ReadResult read = await pipe.Reader.ReadAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Utf8String expected = Utf8Strings.From(SparqlResultsDelimitedWriter.WriteToString(results, format));

        Assert.IsTrue(read.IsCompleted);
        Assert.IsTrue(SequenceEquals(read.Buffer, expected.Span));

        pipe.Reader.AdvanceTo(read.Buffer.End);
    }

    /// <summary>Determines whether a byte sequence equals an expected span, compared segment by segment with no contiguous copy.</summary>
    /// <param name="sequence">The byte sequence, such as a pipe's read buffer.</param>
    /// <param name="expected">The expected bytes.</param>
    /// <returns><see langword="true"/> when the sequence equals <paramref name="expected"/>.</returns>
    private static bool SequenceEquals(ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> expected)
    {
        if(sequence.Length != expected.Length)
        {
            return false;
        }

        int offset = 0;
        foreach(ReadOnlyMemory<byte> segment in sequence)
        {
            if(!segment.Span.SequenceEqual(expected.Slice(offset, segment.Length)))
            {
                return false;
            }

            offset += segment.Length;
        }

        return true;
    }

    /// <summary>Adapts a solution list to an async sequence for the streaming writer.</summary>
    /// <param name="solutions">The solutions.</param>
    /// <returns>The solutions as an async sequence.</returns>
    private static async IAsyncEnumerable<SparqlSolution> Stream(IReadOnlyList<SparqlSolution> solutions)
    {
        foreach(SparqlSolution solution in solutions)
        {
            yield return solution;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
