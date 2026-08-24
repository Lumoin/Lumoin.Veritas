using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Sparql;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Dispatches one <see cref="W3cTestCase"/> against a
/// caller-supplied reader and reports the
/// <see cref="W3cOutcome"/>.
/// </summary>
/// <remarks>
/// <para>
/// The runner is syntax-agnostic — each suite class supplies a
/// reader delegate appropriate for that suite. The runner walks
/// the case according to its
/// <see cref="W3cTestCase.Type"/>: it parses the input (and the
/// expected file when one is declared) and compares.
/// </para>
/// <para>
/// Comparison for evaluation cases uses N-Triples / N-Quads as
/// the expected format (W3C convention) and the supplied reader
/// for the input format. Quad-set equivalence is checked under
/// blank-node isomorphism: two quad sets that differ only in
/// the choice of blank-node labels are treated as equal.
/// </para>
/// </remarks>
internal static class W3cTestRunner
{
    /// <summary>
    /// Reads a stream of UTF-8 input bytes into a quad stream.
    /// The runner supplies the input and observes any exception.
    /// </summary>
    /// <param name="input">The input pipe over the fixture file's bytes.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The parsed quads.</returns>
    internal delegate IAsyncEnumerable<Quad> InputReader(PipeReader input, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the test case.
    /// </summary>
    /// <param name="testCase">The test case to run.</param>
    /// <param name="reader">The reader to use for the test's input format.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>The outcome.</returns>
    public static async Task<W3cOutcome> RunAsync(
        W3cTestCase testCase,
        InputReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(reader);

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Test fixture file not found: {testCase.InputPath}");
        }

        switch(testCase.Type)
        {
            case W3cTestType.PositiveSyntax:
            {
                return await RunPositiveSyntaxAsync(testCase, reader, cancellationToken).ConfigureAwait(false);
            }

            case W3cTestType.NegativeSyntax:
            {
                return await RunNegativeSyntaxAsync(testCase, reader, cancellationToken).ConfigureAwait(false);
            }

            case W3cTestType.Evaluation:
            {
                return await RunEvaluationAsync(testCase, reader, expectEqual: true, cancellationToken).ConfigureAwait(false);
            }

            case W3cTestType.NegativeEvaluation:
            {
                return await RunEvaluationAsync(testCase, reader, expectEqual: false, cancellationToken).ConfigureAwait(false);
            }

            case W3cTestType.PositiveC14N:
            {
                return await RunPositiveC14NAsync(testCase, reader, cancellationToken).ConfigureAwait(false);
            }

            case W3cTestType.Unknown:
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Unrecognised test type IRI '{testCase.RawTypeIri}'.");
            }

            default:
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Unhandled test type {testCase.Type}.");
            }
        }
    }

    private static async Task<W3cOutcome> RunPositiveSyntaxAsync(
        W3cTestCase testCase,
        InputReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            int counted = await DrainAsync(testCase.InputPath, reader, cancellationToken).ConfigureAwait(false);
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Parsed {counted} quad(s).");
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Reader threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<W3cOutcome> RunNegativeSyntaxAsync(
        W3cTestCase testCase,
        InputReader reader,
        CancellationToken cancellationToken)
    {
        try
        {
            int counted = await DrainAsync(testCase.InputPath, reader, cancellationToken).ConfigureAwait(false);
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Reader accepted invalid input and produced {counted} quad(s); expected a parse failure.");
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception ex) when(IsParseException(ex))
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Reader rejected with {ex.GetType().Name} as expected.");
        }
        catch(Exception ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Reader threw unexpected exception type {ex.GetType().FullName}: {ex.Message}");
        }
    }

    private static async Task<W3cOutcome> RunEvaluationAsync(
        W3cTestCase testCase,
        InputReader reader,
        bool expectEqual,
        CancellationToken cancellationToken)
    {
        if(testCase.ExpectedPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, "Evaluation test missing mf:result fixture reference.");
        }

        if(!File.Exists(testCase.ExpectedPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected fixture file not found: {testCase.ExpectedPath}");
        }

        List<Quad> actualQuads;
        try
        {
            actualQuads = await CollectAsync(testCase.InputPath, reader, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Reader threw {ex.GetType().Name} on input: {ex.Message}");
        }

        List<Quad> expectedQuads;
        try
        {
            expectedQuads = await CollectAsync(
                testCase.ExpectedPath,
                static (stream, ct) => NQuadsReader.ReadAsync(stream, pool: null, ct),
                cancellationToken).ConfigureAwait(false);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Reader threw {ex.GetType().Name} on expected file: {ex.Message}");
        }

        bool equal = QuadSetIsomorphism.AreIsomorphic(actualQuads, expectedQuads);
        bool pass = equal == expectEqual;
        string compareNote = $"actual={actualQuads.Count} quads, expected={expectedQuads.Count} quads, isomorphic={equal}";

        if(pass)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, compareNote);
        }

        string reason = expectEqual
            ? $"Actual quads do not match expected quads: {compareNote}"
            : $"Actual quads unexpectedly match expected quads: {compareNote}";

        return new W3cOutcome(W3cOutcomeStatus.Failed, reason);
    }

    private static async Task<W3cOutcome> RunPositiveC14NAsync(
        W3cTestCase testCase,
        InputReader reader,
        CancellationToken cancellationToken)
    {
        if(testCase.ExpectedPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, "Canonicalisation test missing mf:result fixture reference.");
        }

        if(!File.Exists(testCase.ExpectedPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected fixture file not found: {testCase.ExpectedPath}");
        }

        List<Quad> parsed;
        try
        {
            parsed = await CollectAsync(testCase.InputPath, reader, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Reader threw {ex.GetType().Name} on input: {ex.Message}");
        }

        string actualCanonical;
        try
        {
            //N-Triples / N-Quads canonicalization is per-statement lexical canonicalization in
            //document order, distinct from the RDFC-1.0 dataset canonicalization that sorts lines
            //and relabels blank nodes.
            actualCanonical = RdfCanonicalizer.SerializeStatements(parsed);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Canonicaliser threw {ex.GetType().Name}: {ex.Message}");
        }

        string expectedText = await File.ReadAllTextAsync(testCase.ExpectedPath, cancellationToken).ConfigureAwait(false);
        string expectedCanonical = NormaliseLineEndings(expectedText);
        string actualCanonicalNormalised = NormaliseLineEndings(actualCanonical);

        if(string.Equals(actualCanonicalNormalised, expectedCanonical, StringComparison.Ordinal))
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Canonical output matched ({actualCanonicalNormalised.Length} bytes).");
        }

        return new W3cOutcome(
            W3cOutcomeStatus.Failed,
            $"Canonical output mismatch (actual {actualCanonicalNormalised.Length} bytes, expected {expectedCanonical.Length} bytes).");
    }

    private static string NormaliseLineEndings(string value)
    {
        //RDFC-1.0 mandates LF terminators; vendored fixtures sometimes carry CRLF after a Windows checkout.
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static async Task<int> DrainAsync(string path, InputReader reader, CancellationToken cancellationToken)
    {
        using FileStream stream = File.OpenRead(path);
        PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        int count = 0;
        await foreach(Quad q in reader(pipe, cancellationToken).ConfigureAwait(false))
        {
            _ = q;
            count++;
        }

        return count;
    }

    private static async Task<List<Quad>> CollectAsync(string path, InputReader reader, CancellationToken cancellationToken)
    {
        using FileStream stream = File.OpenRead(path);
        PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        List<Quad> result = [];
        await foreach(Quad q in reader(pipe, cancellationToken).ConfigureAwait(false))
        {
            result.Add(q);
        }

        return result;
    }

    private static bool IsParseException(Exception ex)
    {
        //Parse failures we accept: the syntax-specific parse exception types and the general I/O-style format failure shapes
        //the readers raise (UTF-8 decoding errors, etc.). Any exception not in this set is reported as unexpected.
        return ex is TurtleParseException
            || ex is NQuadsParseException
            || ex is SparqlParseException
            || ex is FormatException
            || ex is ArgumentException
            || ex is InvalidOperationException
            || ex is System.Text.DecoderFallbackException;
    }
}
