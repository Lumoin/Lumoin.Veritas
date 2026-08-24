// W3C RDF Dataset Canonicalization (RDFC-1.0) test suite — https://github.com/w3c/rdf-canon
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/RdfCanon/. See that folder's ATTRIBUTION.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IoPath = System.IO.Path;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One entry from the vendored W3C rdf-canon manifest: its type, the input dataset, the expected canonical output
/// (for an eval test), and the hash algorithm the test pins.
/// </summary>
/// <param name="Name">The test's <c>mf:name</c>.</param>
/// <param name="TypeLocalName">The local name of the <c>rdfc:</c> test type (<c>RDFC10EvalTest</c> / <c>RDFC10MapTest</c> / <c>RDFC10NegativeEvalTest</c>).</param>
/// <param name="InputPath">The <c>mf:action</c> input N-Quads file.</param>
/// <param name="ExpectedPath">The <c>mf:result</c> expected canonical N-Quads file, or <see langword="null"/> (a negative test declares none).</param>
/// <param name="HashAlgorithm">The <c>rdfc:hashAlgorithm</c> token (<c>SHA256</c> by default, a few tests pin <c>SHA384</c>).</param>
internal sealed record W3cRdfCanonCase(string Name, string TypeLocalName, string InputPath, string? ExpectedPath, string HashAlgorithm);

/// <summary>
/// Dispatches a W3C rdf-canon test: canonicalizes the input dataset with <see cref="RdfCanonicalizer.Canonicalize"/>
/// and compares the result to the expected canonical N-Quads byte-for-byte (line-endings normalized to LF).
/// </summary>
/// <remarks>
/// <para>
/// All three rdf-canon test types execute: <c>rdfc:RDFC10EvalTest</c> compares the canonical N-Quads,
/// <c>rdfc:RDFC10MapTest</c> compares the issued-identifier map from <see cref="RdfCanonicalizer.CanonicalizeWithMap"/>,
/// and <c>rdfc:RDFC10NegativeEvalTest</c> expects a poison graph to be rejected with
/// <see cref="RdfCanonicalizationException"/>. A wrong canonical output, a wrong map, or a missing rejection is a
/// failure (red), so the failing count is the honest distance to full RDFC-1.0 conformance and may only ratchet down.
/// </para>
/// </remarks>
internal static class W3cRdfCanonRunner
{
    /// <summary>Runs one rdf-canon test case.</summary>
    /// <param name="testCase">The manifest-declared case.</param>
    /// <param name="cancellationToken">A token to cancel reading and canonicalization.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="testCase"/> is <see langword="null"/>.</exception>
    public static async Task<W3cOutcome> RunAsync(W3cRdfCanonCase testCase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Input file not found: {testCase.InputPath}");
        }

        List<Quad> quads = [];
        try
        {
            byte[] inputBytes = await File.ReadAllBytesAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false);
            await foreach(Quad quad in NQuadsReader.ReadAsync(inputBytes, pool: null, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }
        }
        catch(NQuadsParseException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Input N-Quads did not parse: {ex.Message}");
        }

        HashDelegate hash = testCase.HashAlgorithm == "SHA384" ? SHA384.HashData : SHA256.HashData;

        return testCase.TypeLocalName switch
        {
            "RDFC10NegativeEvalTest" => RunNegative(quads, hash),
            "RDFC10MapTest" => await RunMapAsync(testCase, quads, hash, cancellationToken).ConfigureAwait(false),
            "RDFC10EvalTest" => await RunEvalAsync(testCase, quads, hash, cancellationToken).ConfigureAwait(false),
            _ => new W3cOutcome(W3cOutcomeStatus.Skipped, $"Unhandled rdf-canon test type '{testCase.TypeLocalName}'.")
        };
    }

    /// <summary>Runs a <c>RDFC10EvalTest</c>: canonicalize and compare byte-for-byte to the expected N-Quads.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="quads">The parsed input quads.</param>
    /// <param name="hash">The hash function.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outcome.</returns>
    private static async Task<W3cOutcome> RunEvalAsync(W3cRdfCanonCase testCase, List<Quad> quads, HashDelegate hash, CancellationToken cancellationToken)
    {
        if(testCase.ExpectedPath is null || !File.Exists(testCase.ExpectedPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected canonical N-Quads not found: {testCase.ExpectedPath}");
        }

        string actual = RdfCanonicalizer.Canonicalize(quads, hash);
        string expected = NormalizeLineEndings(await File.ReadAllTextAsync(testCase.ExpectedPath, cancellationToken).ConfigureAwait(false));

        return string.Equals(actual, expected, StringComparison.Ordinal)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, $"Canonical output matches ({quads.Count} input quad(s)).")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"Canonical output differs from expected ({CountLines(actual)} vs {CountLines(expected)} line(s)).");
    }

    /// <summary>Runs a <c>RDFC10MapTest</c>: compare the issued-identifier map to the expected JSON (input label → <c>c14nN</c>).</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="quads">The parsed input quads.</param>
    /// <param name="hash">The hash function.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outcome.</returns>
    private static async Task<W3cOutcome> RunMapAsync(W3cRdfCanonCase testCase, List<Quad> quads, HashDelegate hash, CancellationToken cancellationToken)
    {
        if(testCase.ExpectedPath is null || !File.Exists(testCase.ExpectedPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected issued-identifier map not found: {testCase.ExpectedPath}");
        }

        RdfCanonicalizationResult result = RdfCanonicalizer.CanonicalizeWithMap(quads, hash);
        Dictionary<string, string> expected = ParseFlatJsonMap(await File.ReadAllTextAsync(testCase.ExpectedPath, cancellationToken).ConfigureAwait(false));

        if(result.IssuedIdentifiers.Count != expected.Count)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Issued-identifier map size differs (actual {result.IssuedIdentifiers.Count} vs expected {expected.Count}).");
        }

        foreach((string label, string canonical) in expected)
        {
            if(!result.IssuedIdentifiers.TryGetValue(label, out string? actualCanonical) || !string.Equals(actualCanonical, canonical, StringComparison.Ordinal))
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"Issued identifier for ?{label} differs (expected {canonical}, got {(actualCanonical ?? "<unmapped>")}).");
            }
        }

        return new W3cOutcome(W3cOutcomeStatus.Passed, $"Issued-identifier map matches ({expected.Count} blank node(s)).");
    }

    /// <summary>Runs a <c>RDFC10NegativeEvalTest</c>: a poison graph must be rejected with <see cref="RdfCanonicalizationException"/>.</summary>
    /// <param name="quads">The parsed input quads.</param>
    /// <param name="hash">The hash function.</param>
    /// <returns>The outcome.</returns>
    private static W3cOutcome RunNegative(List<Quad> quads, HashDelegate hash)
    {
        try
        {
            _ = RdfCanonicalizer.Canonicalize(quads, hash);
        }
        catch(RdfCanonicalizationException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, "Poison graph rejected as expected.");
        }

        return new W3cOutcome(W3cOutcomeStatus.Failed, "Expected the poison graph to be rejected, but canonicalization completed.");
    }

    /// <summary>Parses a flat JSON object of string→string entries (the issued-identifier map fixture; values are simple labels with no escapes).</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The label → canonical-identifier map.</returns>
    private static Dictionary<string, string> ParseFlatJsonMap(string json)
    {
        List<string> tokens = [];
        int index = 0;
        while(index < json.Length)
        {
            if(json[index] == '"')
            {
                int start = ++index;
                while(index < json.Length && json[index] != '"')
                {
                    index++;
                }

                tokens.Add(json[start..index]);
            }

            index++;
        }

        Dictionary<string, string> map = [];
        for(int pair = 0; pair + 1 < tokens.Count; pair += 2)
        {
            map[tokens[pair]] = tokens[pair + 1];
        }

        return map;
    }

    /// <summary>Normalizes CRLF / lone-CR to LF (a checkout artifact on the vendored expected files), matching the canonical RDFC-1.0 line form.</summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The text with LF line endings.</returns>
    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    /// <summary>Counts the non-empty lines of a canonical serialization, for the failure message.</summary>
    /// <param name="canonical">The canonical N-Quads.</param>
    /// <returns>The non-empty line count.</returns>
    private static int CountLines(string canonical)
    {
        int count = 0;
        foreach(string line in canonical.Split('\n'))
        {
            if(line.Length > 0)
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// An MSTest <see cref="ITestDataSource"/> yielding one row per entry in the vendored rdf-canon manifest. The
/// manifest uses the <c>rdfc:</c> test vocabulary, distinct from the <c>mf:</c>/<c>sht:</c>/<c>qt:</c> vocabularies
/// the shared <see cref="W3cManifestLoader"/> understands, so this source parses it directly.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class W3cRdfCanonDataAttribute: Attribute, ITestDataSource
{
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RdfcNamespace = "https://w3c.github.io/rdf-canon/tests/vocab#";
    private const string MfNamespace = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";

    /// <inheritdoc/>
    public IEnumerable<object[]> GetData(MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(methodInfo);

        string manifestPath = W3cCorpusPath.For("RdfCanon", string.Empty, "manifest.ttl");
        List<object[]> rows = [];
        foreach(W3cRdfCanonCase testCase in LoadCases(manifestPath))
        {
            rows.Add([testCase]);
        }

        return rows;
    }

    /// <inheritdoc/>
    public string? GetDisplayName(MethodInfo methodInfo, object?[]? data)
    {
        return data is [W3cRdfCanonCase testCase, ..] ? $"{testCase.Name} ({testCase.TypeLocalName})" : null;
    }

    /// <summary>Parses the rdf-canon manifest into its declared cases.</summary>
    /// <param name="manifestPath">The absolute path to <c>manifest.ttl</c>.</param>
    /// <returns>The cases, one per typed test entry.</returns>
    private static List<W3cRdfCanonCase> LoadCases(string manifestPath)
    {
        byte[] bytes = File.ReadAllBytes(manifestPath);
        string baseIri = new Uri(IoPath.GetFullPath(manifestPath)).AbsoluteUri;
        List<Quad> quads = CollectQuads(bytes, baseIri);

        //Index each subject's predicate → object values; the typed test entries are the subjects carrying an
        //rdf:type in the rdfc: namespace.
        Dictionary<string, Dictionary<string, List<RdfTerm>>> bySubject = [];
        foreach(Quad quad in quads)
        {
            string subjectKey = TermKey(quad.Subject);
            string predicate = quad.Predicate.Iri.ToString();
            if(!bySubject.TryGetValue(subjectKey, out Dictionary<string, List<RdfTerm>>? properties))
            {
                properties = [];
                bySubject[subjectKey] = properties;
            }

            if(!properties.TryGetValue(predicate, out List<RdfTerm>? objects))
            {
                objects = [];
                properties[predicate] = objects;
            }

            objects.Add(quad.Object);
        }

        List<W3cRdfCanonCase> cases = [];
        foreach(Dictionary<string, List<RdfTerm>> properties in bySubject.Values)
        {
            if(TypeLocalName(properties) is not string typeLocalName)
            {
                continue;
            }

            if(FirstNamedNode(properties, MfNamespace + "action") is not string inputPath)
            {
                continue;
            }

            string name = FirstLiteral(properties, MfNamespace + "name") ?? typeLocalName;
            string? expectedPath = FirstNamedNode(properties, MfNamespace + "result");
            string hashAlgorithm = FirstLiteral(properties, RdfcNamespace + "hashAlgorithm") ?? "SHA256";
            cases.Add(new W3cRdfCanonCase(name, typeLocalName, inputPath, expectedPath, hashAlgorithm));
        }

        cases.Sort(static (a, b) => string.CompareOrdinal(a.InputPath, b.InputPath));

        return cases;
    }

    /// <summary>Returns the local name of the subject's <c>rdfc:</c> test type, or <see langword="null"/> when it is not a typed test.</summary>
    /// <param name="properties">The subject's predicate → objects map.</param>
    /// <returns>The type local name, or <see langword="null"/>.</returns>
    private static string? TypeLocalName(Dictionary<string, List<RdfTerm>> properties)
    {
        if(!properties.TryGetValue(RdfType, out List<RdfTerm>? types))
        {
            return null;
        }

        foreach(RdfTerm type in types)
        {
            if(type is NamedNode named && named.Iri.ToString() is string iri && iri.StartsWith(RdfcNamespace, StringComparison.Ordinal))
            {
                return iri[RdfcNamespace.Length..];
            }
        }

        return null;
    }

    /// <summary>Returns the local file path of the first named-node object of a predicate, or <see langword="null"/>.</summary>
    /// <param name="properties">The subject's predicate → objects map.</param>
    /// <param name="predicate">The predicate IRI.</param>
    /// <returns>The resolved local path, or <see langword="null"/>.</returns>
    private static string? FirstNamedNode(Dictionary<string, List<RdfTerm>> properties, string predicate)
    {
        if(properties.TryGetValue(predicate, out List<RdfTerm>? objects))
        {
            foreach(RdfTerm term in objects)
            {
                if(term is NamedNode named)
                {
                    return new Uri(named.Iri.ToString()).LocalPath;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the lexical value of the first literal object of a predicate, or <see langword="null"/>.</summary>
    /// <param name="properties">The subject's predicate → objects map.</param>
    /// <param name="predicate">The predicate IRI.</param>
    /// <returns>The literal value, or <see langword="null"/>.</returns>
    private static string? FirstLiteral(Dictionary<string, List<RdfTerm>> properties, string predicate)
    {
        if(properties.TryGetValue(predicate, out List<RdfTerm>? objects))
        {
            foreach(RdfTerm term in objects)
            {
                if(term is Literal literal)
                {
                    return literal.Value.ToString();
                }
            }
        }

        return null;
    }

    /// <summary>A stable string key for a subject term (IRI for a named node, label for a blank node).</summary>
    /// <param name="term">The subject term.</param>
    /// <returns>The key.</returns>
    private static string TermKey(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => "<" + named.Iri.ToString() + ">",
            BlankNode blank => "_:" + blank.Label.ToString(),
            _ => term.ToString() ?? string.Empty
        };
    }

    /// <summary>Reads the manifest Turtle into quads. The in-memory Turtle core is synchronous, so the discovery-time data source consumes it without any task machinery.</summary>
    /// <param name="bytes">The manifest UTF-8 bytes.</param>
    /// <param name="baseIri">The manifest's base IRI (its file URL).</param>
    /// <returns>The parsed quads.</returns>
    private static List<Quad> CollectQuads(byte[] bytes, string baseIri)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [];
        foreach(Quad quad in TurtleReader.Read(bytes, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseIri))
        {
            quads.Add(quad);
        }

        return quads;
    }
}
