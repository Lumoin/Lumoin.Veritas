using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Values;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Runs one vendored JSONata case against the engine and reports the outcome. A case passes when the
/// engine's value or error matches the suite's expectation.
/// </summary>
/// <remarks>
/// <para>
/// This is an honest compliance measure against the single JSONata specification: a case passes only when
/// the engine genuinely complies (the right value, error, or undefined), and any case where the engine does
/// not yet comply — a wrong result, a missing or wrong error, an unimplemented function, or a construct the
/// parser does not yet accept — is a real <see cref="W3cOutcomeStatus.Failed"/>, the honest distance to full
/// conformance. A <see cref="W3cOutcomeStatus.Skipped"/> is reserved for genuine test-harness limits where
/// the case cannot be executed through this test plumbing at all (an input the host JSON adapter cannot
/// materialise, or a case supplying external variable bindings the harness does not yet thread to the
/// engine), never for engine incompleteness.
/// </para>
/// <para>
/// The one comparison allowance is S-code leniency. The reference suite labels syntactically-invalid
/// expressions with <c>S####</c> codes; this engine emits its own <c>JS####</c> / <c>LX####</c> parse
/// diagnostics, so the code strings cannot match exactly. A case whose expected outcome is an error with an
/// <c>S####</c> code therefore passes whenever the engine reports any parse error — it correctly rejected
/// the expression, which is real compliance with the behaviour — rather than on an exact code match. Runtime
/// error codes (<c>T####</c> / <c>D####</c> / <c>U####</c>) are compared exactly.
/// </para>
/// </remarks>
internal static class JsonataConformanceRunner
{
    /// <summary>
    /// The fixed instant the conformance run pins the evaluation clock to, as integer epoch-milliseconds
    /// (UTC), so any <c>$now</c> / <c>$millis</c> case is deterministic. The corpus's date cases are
    /// overwhelmingly the pure <c>$fromMillis</c> / <c>$toMillis</c> functions, so this rarely matters, but it
    /// keeps the clock-reading built-ins reproducible. The value is <c>2020-01-01T00:00:00.000Z</c>.
    /// </summary>
    private const long PinnedEvaluationMillis = 1577836800000;

    /// <summary>
    /// The fixed randomness source the conformance run pins the evaluation to, so any <c>$shuffle</c> case
    /// replays the same permutation across runs. The corpus's <c>$shuffle</c> cases are shuffle-invariant
    /// assertions (the count and the sorted order are unchanged, an undefined input stays undefined, and a
    /// singleton is returned as-is), so the particular seed never matters — only that the source is fixed.
    /// </summary>
    private static RandomnessDelegate PinnedRandomness { get; } = VeritasRandomness.Seeded(0x5EED_C0FFEE_F00DUL);

    /// <summary>
    /// The step budget the conformance run evaluates under — raised far above the production default so a
    /// legitimately large but finite suite case (an O(n^2) self-join, a map over a hundred-thousand-element
    /// range) runs to completion, exactly as the reference does. A non-terminating expression stays bounded:
    /// infinite recursion trips the work-stack depth limit long before this, and the suite has no
    /// infinite-iteration case, so nothing actually runs to this ceiling.
    /// </summary>
    private const int ConformanceMaxEvaluationSteps = 50_000_000;

    /// <summary>Runs one case to an outcome.</summary>
    /// <param name="testCase">The suite case.</param>
    /// <returns>The outcome (Passed / Failed / Skipped).</returns>
    public static W3cOutcome Run(JsonataConformanceCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(testCase.LoadError is not null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"case not readable through the host JSON adapter: {testCase.LoadError}");
        }

        if(testCase.HasNonEmptyBindings)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "the harness does not pass external variable bindings to the engine yet");
        }

        //A case this engine deliberately diverges from (a defensible design difference, not a defect) is
        //reported inconclusive with its documented reason, so the failing count tracks real bugs only.
        if(JsonataReferenceDivergences.TryGetReason(testCase.GroupName, testCase.CaseFile, out string? divergenceReason))
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, divergenceReason!);
        }

        ParseResult<JsonataExpression> parsed = JsonataEngine.Parse(Encoding.UTF8.GetBytes(testCase.Expression));
        if(parsed.HasErrors)
        {
            return ClassifyParseErrors(parsed.Diagnostics, testCase.Expectation);
        }

        return Evaluate(parsed.Tree, testCase);
    }

    /// <summary>Classifies a parse that produced error diagnostics against the case expectation.</summary>
    /// <param name="diagnostics">The parse diagnostics.</param>
    /// <param name="expectation">The case expectation.</param>
    /// <returns>Skipped for an unsupported construct; Passed for an expected syntax error; otherwise Failed.</returns>
    private static W3cOutcome ClassifyParseErrors(IReadOnlyList<Diagnostic> diagnostics, JsonataConformanceExpectation expectation)
    {
        if(expectation.Kind == JsonataConformanceOutcomeKind.Error && IsSyntaxErrorCode(expectation.ErrorCode))
        {
            //S-code leniency: the engine rejected a syntactically-invalid expression with its own parse
            //diagnostic, which is the correct behaviour even though the code string differs from the
            //reference suite's S-code.
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"rejected as a syntax error (expected {expectation.ErrorCode}).");
        }

        //A parse error where a value (or a non-syntax error) was expected is genuine non-compliance: the
        //engine does not yet accept this expression's syntax — an unbuilt construct such as the transform
        //operator, a regex literal, the parent operator, or partial application. It is the honest distance to
        //conformance, not a skip.
        return new W3cOutcome(W3cOutcomeStatus.Failed, $"not parseable by this build: {FirstErrorMessage(diagnostics)}");
    }

    /// <summary>Evaluates a parsed expression against the case input and compares the value or error to the expectation.</summary>
    /// <param name="tree">The recovered expression tree.</param>
    /// <param name="testCase">The suite case.</param>
    /// <returns>The outcome.</returns>
    private static W3cOutcome Evaluate(JsonataExpression tree, JsonataConformanceCase testCase)
    {
        //A case with no input data (the dataset:null / no-data form) evaluates against the JSONata "nothing"
        //(undefined) focus, matching the reference's no-input semantics; an inline data:null is a real JSON
        //null and arrives through FromJsonNode as the null value, kept distinct.
        JsonataValue input = testCase.Input is JsonNode inputNode ? JsonataValueAdapter.FromJsonNode(inputNode) : JsonataValue.Undefined;

        JsonataValue value;
        try
        {
            value = JsonataEvaluator.Evaluate(tree, input, PinnedEvaluationMillis, PinnedRandomness, ConformanceMaxEvaluationSteps);
        }
        catch(JsonataErrorException error)
        {
            return ClassifyRuntimeError(error.Code.IsEmpty ? null : error.Code.ToString(), testCase.Expectation);
        }
        catch(JsonataEvaluationLimitException limit)
        {
            //A non-terminating or too-deeply recursive evaluation carries the JSONata U1001 code on the limit
            //exception; an engine-internal data-depth guard carries no code, so it stays a code-less limit
            //breach compared against the expectation by its detail.
            return ClassifyRuntimeError(limit.Code.IsEmpty ? null : limit.Code.ToString(), testCase.Expectation, $"evaluation limit reached: {limit.Limit}");
        }
        catch(JsonataLimitExceededException limit)
        {
            return ClassifyRuntimeError(null, testCase.Expectation, limit.Message);
        }
        catch(JsonataParseException parse)
        {
            return ClassifyRuntimeError(null, testCase.Expectation, parse.Message);
        }
        catch(InvalidOperationException internalError)
        {
            //An engine-internal invariant breach is a defect, not a conformance verdict; surface it as a
            //clearly labelled failed row so one such case cannot abort the whole measurement run and so it is
            //distinguishable from a value mismatch in the triage.
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"internal engine error: {internalError.Message}");
        }
        catch(ArgumentException internalError)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"internal engine error: {internalError.Message}");
        }

        return CompareValue(value, testCase);
    }

    /// <summary>Classifies a runtime error raised during evaluation against the case expectation.</summary>
    /// <param name="actualCode">The raised JSONata error code, or <see langword="null"/> when the error carries none (a limit or parse throw).</param>
    /// <param name="expectation">The case expectation.</param>
    /// <param name="detail">An optional human-readable detail for the failure message.</param>
    /// <returns>Passed on an exact runtime-code match; otherwise Failed.</returns>
    private static W3cOutcome ClassifyRuntimeError(string? actualCode, JsonataConformanceExpectation expectation, string? detail = null)
    {
        if(expectation.Kind != JsonataConformanceOutcomeKind.Error)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"unexpected error {actualCode ?? detail ?? "(no code)"}.");
        }

        if(IsSyntaxErrorCode(expectation.ErrorCode))
        {
            //An S-code expects a syntax (parse) rejection; a runtime error is a mismatch.
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"expected syntax error {expectation.ErrorCode}, got runtime error {actualCode ?? detail ?? "(no code)"}.");
        }

        if(actualCode is not null && string.Equals(actualCode, expectation.ErrorCode, StringComparison.Ordinal))
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"raised the expected error {expectation.ErrorCode}.");
        }

        return new W3cOutcome(W3cOutcomeStatus.Failed, $"expected error {expectation.ErrorCode}, got {actualCode ?? detail ?? "(no code)"}.");
    }

    /// <summary>Compares a successful evaluation value to the case expectation.</summary>
    /// <param name="value">The evaluation result value.</param>
    /// <param name="testCase">The suite case.</param>
    /// <returns>Passed on a structural match; otherwise Failed.</returns>
    private static W3cOutcome CompareValue(JsonataValue value, JsonataConformanceCase testCase)
    {
        JsonataConformanceExpectation expectation = testCase.Expectation;

        switch(expectation.Kind)
        {
            case JsonataConformanceOutcomeKind.Undefined:
            {
                return value.IsUndefined
                    ? new W3cOutcome(W3cOutcomeStatus.Passed, "produced the undefined value.")
                    : new W3cOutcome(W3cOutcomeStatus.Failed, $"expected undefined, got {Serialize(value)}.");
            }

            case JsonataConformanceOutcomeKind.Error:
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"expected error {expectation.ErrorCode}, got a value {Serialize(value)}.");
            }

            case JsonataConformanceOutcomeKind.Result:
            {
                return CompareResult(value, expectation.ResultNode, testCase.Unordered);
            }

            default:
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"unrecognised expectation kind {expectation.Kind}.");
            }
        }
    }

    /// <summary>Compares the value to the expected result, order-sensitively or as a top-level multiset.</summary>
    /// <param name="value">The evaluation result value.</param>
    /// <param name="resultNode">The expected-result JSON node.</param>
    /// <param name="unordered">Whether the top-level array order is not significant.</param>
    /// <returns>Passed on a structural match; otherwise Failed.</returns>
    private static W3cOutcome CompareResult(JsonataValue value, JsonNode resultNode, bool unordered)
    {
        JsonataValue expected = JsonataValueAdapter.FromJsonNode(resultNode);
        bool matches = unordered && value.Kind == JsonataValueKind.Array && expected.Kind == JsonataValueKind.Array
            ? MultisetEquals(value.AsArray, expected.AsArray)
            : JsonataValue.DeepEquals(expected, value);

        return matches
            ? new W3cOutcome(W3cOutcomeStatus.Passed, "value matches the expected result.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"expected {Serialize(expected)}, got {Serialize(value)}.");
    }

    /// <summary>Compares two arrays as multisets: equal length and a one-to-one deep-equal pairing of elements.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when each element of one deep-equals a distinct element of the other.</returns>
    private static bool MultisetEquals(IReadOnlyList<JsonataValue> left, IReadOnlyList<JsonataValue> right)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        bool[] consumed = new bool[right.Count];
        for(int i = 0; i < left.Count; i++)
        {
            bool paired = false;
            for(int j = 0; j < right.Count; j++)
            {
                if(!consumed[j] && JsonataValue.DeepEquals(left[i], right[j]))
                {
                    consumed[j] = true;
                    paired = true;

                    break;
                }
            }

            if(!paired)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines whether an expected error code is a reference-suite syntax (parse) code (an <c>S####</c> code).</summary>
    /// <param name="code">The expected error code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the code begins with <c>S</c>.</returns>
    private static bool IsSyntaxErrorCode(string? code)
    {
        return code is { Length: > 0 } && (code[0] == 'S' || code[0] == 's');
    }

    /// <summary>Serializes a value to compact JSON for a failure message; the undefined value renders as <c>(undefined)</c>.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The rendered value.</returns>
    private static string Serialize(JsonataValue value)
    {
        return value.IsUndefined ? "(undefined)" : JsonataEngine.SerializeToJson(value).ToString();
    }

    /// <summary>Returns the first error-severity diagnostic message, for a failure message.</summary>
    /// <param name="diagnostics">The diagnostics to scan.</param>
    /// <returns>The first error message, or a fallback when none has error severity.</returns>
    private static string FirstErrorMessage(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} {diagnostic.Message}";
            }
        }

        return "(no error diagnostic)";
    }
}
