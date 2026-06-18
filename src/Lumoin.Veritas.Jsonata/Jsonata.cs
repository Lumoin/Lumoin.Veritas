using System;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Lexer;
using Lumoin.Veritas.Jsonata.Parser;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// The entry point to the JSONata query and transformation engine.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "The engine entry point is intentionally named for the language it implements, matching the package and namespace.")]
public static class Jsonata
{
    /// <summary>
    /// Parses a JSONata expression into an AST, bridging lexer and parser diagnostics into one bag.
    /// </summary>
    /// <param name="source">The JSONata expression source, as UTF-8 bytes.</param>
    /// <param name="pool">The pool to intern token payloads and identifiers into; a private pool is created when <see langword="null"/> (the result's interned values keep it alive).</param>
    /// <returns>The parse result: the expression tree (always non-null, possibly carrying error nodes), the accumulated diagnostics, and whether any has error severity.</returns>
    /// <remarks>
    /// Malformed input is recovered, never thrown: lexical errors surface as <c>LX####</c> diagnostics
    /// (bridged first, so they sort ahead of the parser's) and syntax errors as <c>JS####</c>
    /// diagnostics with an <see cref="ErrorExpression"/> standing in at the failure point.
    /// </remarks>
    public static ParseResult<JsonataExpression> Parse(ReadOnlyMemory<byte> source, Utf8StringPool? pool = null)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        DiagnosticBag diagnostics = new();
        JsonataLexer lexer = new(source, effectivePool);
        JsonataParser parser = new(lexer.Tokenize(), effectivePool, diagnostics);

        //The whole-stream ctor enumerated the lexer, so its diagnostics are complete; bridge them into
        //the bag (lexical errors first) before draining the parser's via ParseToResult.
        BridgeLexerDiagnostics(lexer, diagnostics);

        return parser.ParseToResult();
    }

    /// <summary>
    /// Evaluates a JSONata expression against a JSON input node and returns the normalized result value.
    /// </summary>
    /// <param name="expression">The JSONata expression source, as UTF-8 bytes.</param>
    /// <param name="input">The input JSON document the expression is evaluated against, supplied through the backend-agnostic node seam.</param>
    /// <param name="pool">The pool to intern the expression's token payloads into; a private pool is created when <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock the evaluation's instant is captured from for the date built-ins <c>$now</c> / <c>$millis</c>; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="randomness">The randomness source the entropy built-in <c>$shuffle</c> draws its swap indices from; <see cref="VeritasRandomness.System"/> when <see langword="null"/>.</param>
    /// <returns>The normalized result value (undefined when the expression matched nothing).</returns>
    /// <exception cref="JsonataParseException">The expression could not be parsed; the first error diagnostic is carried in the message.</exception>
    /// <remarks>
    /// <para>
    /// The engine stays free of any concrete JSON parser: the caller supplies the input as a
    /// <see cref="JsonNode"/> (for example via <c>StjJsonAdapter.Parse</c>), which is bridged into the
    /// engine's value model by <see cref="JsonataValueAdapter.FromJsonNode"/>.
    /// </para>
    /// <para>
    /// The instant is captured once here and stays constant for the whole evaluation, so repeated <c>$now</c>
    /// / <c>$millis</c> reads in one expression are identical. This seam is the only clock read in the engine;
    /// a fixed <see cref="TimeProvider"/> makes the date built-ins deterministic.
    /// </para>
    /// <para>
    /// The randomness source is captured once here in the same way: <c>$shuffle</c> draws every swap index
    /// from it, so a fixed <see cref="RandomnessDelegate"/> makes <c>$shuffle</c> deterministic. This is the
    /// only entropy read in the engine.
    /// </para>
    /// </remarks>
    public static JsonataValue Evaluate(ReadOnlyMemory<byte> expression, JsonNode input, Utf8StringPool? pool = null, TimeProvider? timeProvider = null, RandomnessDelegate? randomness = null)
    {
        JsonataExpression tree = ParseOrThrow(expression, pool);
        JsonataValue value = JsonataValueAdapter.FromJsonNode(input);
        long evaluationMillis = (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();

        return JsonataEvaluator.Evaluate(tree, value, evaluationMillis, randomness ?? VeritasRandomness.System);
    }

    /// <summary>
    /// Evaluates a JSONata expression against a JSON input document and serializes the result to UTF-8
    /// JSON, parsing the input through a host-supplied JSON parser so the engine references no concrete
    /// JSON library.
    /// </summary>
    /// <param name="expression">The JSONata expression source, as UTF-8 bytes.</param>
    /// <param name="input">The input JSON document the expression is evaluated against, as UTF-8 bytes.</param>
    /// <param name="parseJson">The JSON parser the host supplies (for example <c>StjJsonAdapter.Parse</c>).</param>
    /// <param name="pool">The pool to intern the expression's token payloads into; a private pool is created when <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock the evaluation's instant is captured from for the date built-ins <c>$now</c> / <c>$millis</c>; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="randomness">The randomness source the entropy built-in <c>$shuffle</c> draws its swap indices from; <see cref="VeritasRandomness.System"/> when <see langword="null"/>.</param>
    /// <returns>The evaluation result serialized as UTF-8 JSON; empty when the result is the undefined value.</returns>
    /// <exception cref="JsonataParseException">The expression could not be parsed; the first error diagnostic is carried in the message.</exception>
    public static ReadOnlyMemory<byte> Evaluate(ReadOnlyMemory<byte> expression, ReadOnlyMemory<byte> input, ParseJsonDelegate parseJson, Utf8StringPool? pool = null, TimeProvider? timeProvider = null, RandomnessDelegate? randomness = null)
    {
        ArgumentNullException.ThrowIfNull(parseJson);

        JsonNode node = parseJson(new Utf8String(input));
        JsonataValue result = Evaluate(expression, node, pool, timeProvider, randomness);

        return SerializeToJson(result).Memory;
    }

    /// <summary>Serializes a JSONata result value to UTF-8 JSON text (RFC 8259).</summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The UTF-8 JSON text; empty for the undefined value.</returns>
    public static Utf8String SerializeToJson(JsonataValue value)
    {
        return JsonataJsonWriter.Serialize(value);
    }

    /// <summary>Parses an expression, throwing <see cref="JsonataParseException"/> on the first error diagnostic.</summary>
    /// <param name="expression">The expression source.</param>
    /// <param name="pool">The intern pool, or <see langword="null"/> for a private pool.</param>
    /// <returns>The parsed expression tree.</returns>
    /// <exception cref="JsonataParseException">The parse produced an error diagnostic.</exception>
    private static JsonataExpression ParseOrThrow(ReadOnlyMemory<byte> expression, Utf8StringPool? pool)
    {
        ParseResult<JsonataExpression> parsed = Parse(expression, pool);
        if(parsed.HasErrors)
        {
            foreach(Diagnostic diagnostic in parsed.Diagnostics)
            {
                if(diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    throw new JsonataParseException(diagnostic.Message.ToString(), diagnostic.Span);
                }
            }

            throw new JsonataParseException("The JSONata expression could not be parsed.");
        }

        return parsed.Tree;
    }

    /// <summary>Bridges the lexer's internal diagnostics into the shared parse-level bag.</summary>
    /// <param name="lexer">The lexer whose <see cref="JsonataLexer.Diagnostics"/> are drained.</param>
    /// <param name="diagnostics">The bag to append the bridged diagnostics to.</param>
    private static void BridgeLexerDiagnostics(JsonataLexer lexer, DiagnosticBag diagnostics)
    {
        foreach(JsonataLexDiagnostic lexDiagnostic in lexer.Diagnostics)
        {
            diagnostics.Add(JsonataLexDiagnosticBridge.ToDiagnostic(lexDiagnostic));
        }
    }
}
