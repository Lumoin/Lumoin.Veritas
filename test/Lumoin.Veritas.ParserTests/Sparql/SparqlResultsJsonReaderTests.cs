using System.Buffers;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lumoin.Veritas.Rdf.Json;
using TripleTerm = Lumoin.Veritas.Core.TripleTerm;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultsJsonReader"/>: parsing the SPARQL 1.1 Query Results JSON serialization of
/// <c>SELECT</c> (variables and the binding value forms) and <c>ASK</c> results, including RDF-1.2 triple terms.
/// </summary>
[TestClass]
internal sealed class SparqlResultsJsonReaderTests
{
    /// <summary>Reads the result set from the JSON text.</summary>
    /// <param name="json">The SPARQL Results JSON.</param>
    /// <returns>The parsed result set.</returns>
    private static SparqlResultSet Read(string json)
    {
        return SparqlResultsJsonReader.Read(Utf8Strings.From(json).Memory);
    }

    /// <summary>Returns the value bound to a variable in a solution.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variable">The variable name.</param>
    /// <returns>The bound term.</returns>
    private static RdfTerm Value(SparqlSolution solution, string variable)
    {
        Assert.IsTrue(solution.TryGetValue(new SparqlVariable(Utf8Strings.From(variable)), out RdfTerm value), $"Expected ?{variable} to be bound.");

        return value;
    }

    /// <summary>A <c>SELECT</c> result's head variables are read in declared order.</summary>
    [TestMethod]
    public void ReadsSelectHeadVariablesInOrder()
    {
        SparqlResultSet result = Read(
            """
            { "head": { "vars": [ "x", "p" ] }, "results": { "bindings": [] } }
            """);

        Assert.IsFalse(result.IsBoolean);
        Assert.HasCount(2, result.Variables);
        Assert.AreEqual("x", result.Variables[0].ToString());
        Assert.AreEqual("p", result.Variables[1].ToString());
    }

    /// <summary>Each binding value form (uri, typed literal, language literal, plain literal, bnode) parses to the matching RDF term.</summary>
    [TestMethod]
    public void ReadsBindingValueForms()
    {
        SparqlResultSet result = Read(
            """
            {
              "head": { "vars": [ "iri", "num", "lang", "plain", "b" ] },
              "results": { "bindings": [
                {
                  "iri":   { "type": "uri", "value": "http://example/a" },
                  "num":   { "type": "literal", "value": "42", "datatype": "http://www.w3.org/2001/XMLSchema#integer" },
                  "lang":  { "type": "literal", "value": "hi", "xml:lang": "en" },
                  "plain": { "type": "literal", "value": "plain" },
                  "b":     { "type": "bnode", "value": "b0" }
                }
              ] }
            }
            """);

        Assert.HasCount(1, result.Solutions);
        SparqlSolution solution = result.Solutions[0];

        Assert.AreEqual(new NamedNode(Utf8Strings.From("http://example/a")), Value(solution, "iri"));

        Literal number = (Literal)Value(solution, "num");
        Assert.AreEqual("42", number.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", number.Datatype.Iri.ToString());

        Literal lang = (Literal)Value(solution, "lang");
        Assert.AreEqual("hi", lang.Value.ToString());
        Assert.AreEqual("en", lang.Language?.ToString());
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", lang.Datatype.Iri.ToString());

        Literal plain = (Literal)Value(solution, "plain");
        Assert.AreEqual("plain", plain.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", plain.Datatype.Iri.ToString());

        Assert.AreEqual(new BlankNode(Utf8Strings.From("b0")), Value(solution, "b"));
    }

    /// <summary>Multi-byte UTF-8 in an IRI and a literal value is copied straight from the reader, byte-for-byte.</summary>
    [TestMethod]
    public void ReadsMultiByteUtf8Values()
    {
        SparqlResultSet result = Read(
            """
            {
              "head": { "vars": [ "iri", "lit" ] },
              "results": { "bindings": [
                { "iri": { "type": "uri", "value": "http://example.org/café" },
                  "lit": { "type": "literal", "value": "café ☕", "xml:lang": "fr" } }
              ] }
            }
            """);

        SparqlSolution solution = result.Solutions[0];
        Assert.AreEqual("http://example.org/café", ((NamedNode)Value(solution, "iri")).Iri.ToString());

        Literal literal = (Literal)Value(solution, "lit");
        Assert.AreEqual("café ☕", literal.Value.ToString());
        Assert.AreEqual("fr", literal.Language?.ToString());
        Assert.AreSequenceEqual(System.Text.Encoding.UTF8.GetBytes("café ☕"), literal.Value.Span.ToArray());
    }

    /// <summary>The legacy <c>typed-literal</c> type is read as a datatyped literal.</summary>
    [TestMethod]
    public void ReadsLegacyTypedLiteral()
    {
        SparqlResultSet result = Read(
            """
            {
              "head": { "vars": [ "n" ] },
              "results": { "bindings": [
                { "n": { "type": "typed-literal", "value": "7", "datatype": "http://www.w3.org/2001/XMLSchema#integer" } }
              ] }
            }
            """);

        Literal number = (Literal)Value(result.Solutions[0], "n");
        Assert.AreEqual("7", number.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", number.Datatype.Iri.ToString());
    }

    /// <summary>A triple-term binding value parses to a <see cref="TripleTerm"/>.</summary>
    [TestMethod]
    public void ReadsTripleTermBinding()
    {
        SparqlResultSet result = Read(
            """
            {
              "head": { "vars": [ "t" ] },
              "results": { "bindings": [
                { "t": { "type": "triple", "value": {
                    "subject":   { "type": "uri", "value": "http://example/s" },
                    "predicate": { "type": "uri", "value": "http://example/p" },
                    "object":    { "type": "literal", "value": "o" }
                } } }
              ] }
            }
            """);

        TripleTerm triple = (TripleTerm)Value(result.Solutions[0], "t");
        Assert.AreEqual(new NamedNode(Utf8Strings.From("http://example/s")), triple.Subject);
        Assert.AreEqual("http://example/p", triple.Predicate.Iri.ToString());
        Assert.AreEqual("o", ((Literal)triple.Object).Value.ToString());
    }

    /// <summary>An <c>ASK</c> result parses to its boolean answer.</summary>
    [TestMethod]
    public void ReadsAskBoolean()
    {
        SparqlResultSet trueResult = Read("""{ "head": {}, "boolean": true }""");
        SparqlResultSet falseResult = Read("""{ "head": {}, "boolean": false }""");

        Assert.IsTrue(trueResult.IsBoolean);
        Assert.IsTrue(trueResult.Boolean!.Value);
        Assert.IsFalse(falseResult.Boolean!.Value);
    }

    /// <summary>The <see cref="System.IO.Stream"/> overload (the stream-source production path, e.g. an HTTP response body) parses the same result as the byte overload.</summary>
    [TestMethod]
    public void ReadsFromStream()
    {
        const string json =
            """
            {
              "head": { "vars": [ "iri", "n" ] },
              "results": { "bindings": [
                { "iri": { "type": "uri", "value": "http://example/a" },
                  "n":   { "type": "literal", "value": "42", "datatype": "http://www.w3.org/2001/XMLSchema#integer" } }
              ] }
            }
            """;

        using ReadOnlyMemoryStream stream = new(Utf8Strings.From(json).Memory);
        SparqlResultSet result = SparqlResultsJsonReader.Read(stream);

        Assert.HasCount(2, result.Variables);
        Assert.HasCount(1, result.Solutions);
        Assert.AreEqual(new NamedNode(Utf8Strings.From("http://example/a")), Value(result.Solutions[0], "iri"));

        Literal number = (Literal)Value(result.Solutions[0], "n");
        Assert.AreEqual("42", number.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", number.Datatype.Iri.ToString());
    }

    /// <summary>The <see cref="System.IO.Stream"/> overload rejects a null stream.</summary>
    [TestMethod]
    public void ReadFromNullStreamThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SparqlResultsJsonReader.Read((Stream)null!));
    }

    /// <summary>The byte-sequence overload (the pipe path) parses the same result as the byte overload, even across multiple segments.</summary>
    [TestMethod]
    public void ReadsFromSegmentedSequence()
    {
        ReadOnlyMemory<byte> json = Utf8Strings.From(
            """
            {
              "head": { "vars": [ "iri", "n" ] },
              "results": { "bindings": [
                { "iri": { "type": "uri", "value": "http://example/a" },
                  "n":   { "type": "literal", "value": "42", "datatype": "http://www.w3.org/2001/XMLSchema#integer" } }
              ] }
            }
            """).Memory;

        ReadOnlySequence<byte> sequence = Segmented(json, json.Length / 2);
        Assert.IsFalse(sequence.IsSingleSegment, "The test must exercise the multi-segment path.");

        SparqlResultSet result = SparqlResultsJsonReader.Read(sequence);

        Assert.HasCount(2, result.Variables);
        Assert.HasCount(1, result.Solutions);
        Assert.AreEqual(new NamedNode(Utf8Strings.From("http://example/a")), Value(result.Solutions[0], "iri"));

        Literal number = (Literal)Value(result.Solutions[0], "n");
        Assert.AreEqual("42", number.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", number.Datatype.Iri.ToString());
    }

    /// <summary>Builds a two-segment <see cref="ReadOnlySequence{T}"/> over the bytes, split at <paramref name="firstSegmentLength"/>.</summary>
    /// <param name="bytes">The bytes to wrap.</param>
    /// <param name="firstSegmentLength">The length of the first segment.</param>
    /// <returns>A two-segment sequence over the bytes.</returns>
    private static ReadOnlySequence<byte> Segmented(ReadOnlyMemory<byte> bytes, int firstSegmentLength)
    {
        SequenceSegment first = new(bytes[..firstSegmentLength]);
        SequenceSegment second = first.Append(bytes[firstSegmentLength..]);

        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    /// <summary>A linked <see cref="ReadOnlySequenceSegment{T}"/> for assembling a multi-segment test sequence.</summary>
    private sealed class SequenceSegment: ReadOnlySequenceSegment<byte>
    {
        /// <summary>Initializes a segment over <paramref name="memory"/>.</summary>
        /// <param name="memory">The segment's bytes.</param>
        public SequenceSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        /// <summary>Appends a following segment after this one and returns it.</summary>
        /// <param name="memory">The next segment's bytes.</param>
        /// <returns>The appended segment.</returns>
        public SequenceSegment Append(ReadOnlyMemory<byte> memory)
        {
            SequenceSegment next = new(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }
}
