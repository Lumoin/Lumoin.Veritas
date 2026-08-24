using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The streaming oracle for <see cref="SparqlResultsXmlReader.ReadStreaming"/>: the forward-streaming reader, which
/// reads and discards each <c>&lt;result&gt;</c> row as it completes, must produce the same result set as the buffered
/// <see cref="SparqlResultsXmlReader.Read"/> — over the streaming-specific shapes (ASK, an empty result set, an
/// all-unbound row, a multi-row set sharing a blank-node label, a nested triple term) and over every vendored
/// <c>.srx</c> fixture in the SPARQL corpus. Equivalence is the value/structural comparison the SPARQL evaluation
/// suite uses (a blank-node-isomorphic, order-insensitive multiset), plus the head variable list.
/// </summary>
[TestClass]
internal sealed class SparqlResultsStreamingOracleTests
{
    private const string Header = "<?xml version=\"1.0\"?><sparql xmlns=\"http://www.w3.org/2005/sparql-results#\">";

    /// <summary>A multi-row SELECT with every leaf binding form streams to the same result set it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForMultiRowSelect()
    {
        string srx = Header +
            "<head><variable name=\"iri\"/><variable name=\"num\"/><variable name=\"lang\"/><variable name=\"plain\"/><variable name=\"b\"/></head>" +
            "<results>" +
            "<result><binding name=\"iri\"><uri>http://example/a</uri></binding><binding name=\"num\"><literal datatype=\"http://www.w3.org/2001/XMLSchema#integer\">42</literal></binding></result>" +
            "<result><binding name=\"lang\"><literal xml:lang=\"en\">hi</literal></binding><binding name=\"plain\"><literal>plain</literal></binding></result>" +
            "<result><binding name=\"b\"><bnode>b0</bnode></binding></result>" +
            "</results></sparql>";

        AssertSameResultSet(srx);
    }

    /// <summary>An <c>ASK</c> result (no <c>&lt;results&gt;</c> container) streams to the same boolean it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForAsk()
    {
        AssertSameResultSet(Header + "<head/><boolean>true</boolean></sparql>");
        AssertSameResultSet(Header + "<head/><boolean>false</boolean></sparql>");
    }

    /// <summary>A present-but-empty result set streams to the same (empty) result set it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForEmptyResults()
    {
        AssertSameResultSet(Header + "<head><variable name=\"x\"/><variable name=\"p\"/></head><results></results></sparql>");
    }

    /// <summary>A <c>&lt;result&gt;</c> row with no bindings (an all-unbound row) streams to the same result set it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForUnboundRow()
    {
        AssertSameResultSet(Header + "<head><variable name=\"x\"/></head><results><result></result></results></sparql>");
    }

    /// <summary>A blank-node label shared across two rows streams to a result set equivalent to the buffered one (blank-node identity is decided structurally, so per-row detach must not perturb it).</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForBlankNodeSharedAcrossRows()
    {
        string srx = Header +
            "<head><variable name=\"b\"/></head>" +
            "<results>" +
            "<result><binding name=\"b\"><bnode>shared</bnode></binding></result>" +
            "<result><binding name=\"b\"><bnode>shared</bnode></binding></result>" +
            "</results></sparql>";

        AssertSameResultSet(srx);
    }

    /// <summary>A nested triple-term binding value streams to the same result set it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForTripleTerm()
    {
        string srx = Header +
            "<head><variable name=\"t\"/></head>" +
            "<results><result><binding name=\"t\"><triple>" +
            "<subject><uri>http://example/s</uri></subject>" +
            "<predicate><uri>http://example/p</uri></predicate>" +
            "<object><triple><subject><uri>http://example/s2</uri></subject><predicate><uri>http://example/p2</uri></predicate><object><literal>o</literal></object></triple></object>" +
            "</triple></binding></result></results></sparql>";

        AssertSameResultSet(srx);
    }

    /// <summary>A document with content after the closing root is rejected by both readers (the streaming fold enforces the same single-root well-formedness as the buffered read).</summary>
    [TestMethod]
    public void StreamingAndBufferedBothRejectTrailingContentAfterRoot()
    {
        AssertBothThrow(Header + "<head><variable name=\"x\"/></head><results><result><binding name=\"x\"><uri>http://e/a</uri></binding></result></results></sparql><junk/>");
    }

    /// <summary>A document whose root is left unclosed is rejected by both readers (the streaming fold enforces the same open-element balance as the buffered read).</summary>
    [TestMethod]
    public void StreamingAndBufferedBothRejectUnclosedRoot()
    {
        AssertBothThrow(Header + "<head><variable name=\"x\"/></head><results><result><binding name=\"x\"><uri>http://e/a</uri></binding></result>");
    }

    /// <summary>Asserts a structurally malformed document is rejected with a <see cref="FormatException"/> by both the buffered and the streaming reader (the SPARQL-results reader throws rather than recording diagnostics).</summary>
    /// <param name="srx">The malformed SPARQL Results XML text.</param>
    private static void AssertBothThrow(string srx)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(srx);

        Assert.ThrowsExactly<FormatException>(() => SparqlResultsXmlReader.Read(bytes));
        Assert.ThrowsExactly<FormatException>(() => SparqlResultsXmlReader.ReadStreaming(bytes));
    }

    /// <summary>Every vendored <c>.srx</c> fixture in the SPARQL corpus streams to the same result set it buffers to (or both readers reject it identically).</summary>
    [TestMethod]
    public void StreamingMatchesBufferedOverTheSrxCorpus()
    {
        string sparqlRoot = W3cCorpusPath.LibraryDirectory("Sparql");
        Assert.IsTrue(Directory.Exists(sparqlRoot), $"the SPARQL corpus directory '{sparqlRoot}' must exist.");

        List<string> mismatches = [];
        int compared = 0;
        foreach(string file in Directory.EnumerateFiles(sparqlRoot, "*.srx", SearchOption.AllDirectories))
        {
            byte[] bytes = File.ReadAllBytes(file);
            string name = Path.GetFileName(file);
            string? bufferedError = TryParse(bytes, streaming: false, out SparqlResultSet? buffered);
            string? streamingError = TryParse(bytes, streaming: true, out SparqlResultSet? streaming);

            if(bufferedError is not null || streamingError is not null)
            {
                if(!string.Equals(bufferedError, streamingError, StringComparison.Ordinal))
                {
                    mismatches.Add($"{name}: buffered={bufferedError ?? "ok"}, streaming={streamingError ?? "ok"}");
                }

                continue;
            }

            compared++;
            if(!ResultSetsMatch(streaming!, buffered!))
            {
                mismatches.Add($"{name}: streaming result set differs from buffered.");
            }
        }

        Assert.IsEmpty(mismatches, "streaming diverged from buffered for: " + string.Join("; ", mismatches));
        Assert.IsGreaterThan(0, compared, "expected at least one .srx fixture to compare.");
    }

    /// <summary>Asserts a SPARQL Results XML document reads to the same result set buffered and streaming.</summary>
    /// <param name="srx">The SPARQL Results XML text.</param>
    private static void AssertSameResultSet(string srx)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(srx);
        SparqlResultSet buffered = SparqlResultsXmlReader.Read(bytes);
        SparqlResultSet streaming = SparqlResultsXmlReader.ReadStreaming(bytes);

        Assert.IsTrue(ResultSetsMatch(streaming, buffered), "the streaming result set differs from the buffered result set.");
    }

    /// <summary>Whether two result sets agree on boolean-ness, the head variable list (in order), and the solution multiset under one blank-node bijection.</summary>
    /// <param name="streaming">The streaming reader's result set.</param>
    /// <param name="buffered">The buffered reader's result set.</param>
    /// <returns><see langword="true"/> when the result sets are equivalent.</returns>
    private static bool ResultSetsMatch(SparqlResultSet streaming, SparqlResultSet buffered)
    {
        return streaming.IsBoolean == buffered.IsBoolean
            && VariablesEqual(streaming, buffered)
            && SparqlResultComparer.AreEquivalent(streaming, buffered, ordered: false);
    }

    /// <summary>Whether two result sets declare the same head variables in the same order.</summary>
    /// <param name="left">The first result set.</param>
    /// <param name="right">The second result set.</param>
    /// <returns><see langword="true"/> when the variable lists are equal.</returns>
    private static bool VariablesEqual(SparqlResultSet left, SparqlResultSet right)
    {
        if(left.Variables.Count != right.Variables.Count)
        {
            return false;
        }

        for(int i = 0; i < left.Variables.Count; i++)
        {
            if(!left.Variables[i].ToString().Equals(right.Variables[i].ToString(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Parses the bytes through the buffered or streaming reader, returning <see langword="null"/> on success or the rejected exception's type name on a recognised parse failure.</summary>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="streaming">Whether to use the streaming reader rather than the buffered reader.</param>
    /// <param name="result">The parsed result set, or <see langword="null"/> when parsing failed.</param>
    /// <returns><see langword="null"/> on success, or the exception type name on failure.</returns>
    private static string? TryParse(ReadOnlyMemory<byte> bytes, bool streaming, out SparqlResultSet? result)
    {
        try
        {
            result = streaming ? SparqlResultsXmlReader.ReadStreaming(bytes) : SparqlResultsXmlReader.Read(bytes);

            return null;
        }
        catch(Exception exception) when(exception is FormatException or TripleTermDepthLimitException or InvalidOperationException)
        {
            result = null;

            return exception.GetType().Name;
        }
    }
}
