using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Values;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The dynamic-evaluation built-in <c>$eval</c>: it parses a string as a JSONata expression and evaluates the
/// result against an optional context (the current focus when none is supplied). A parse error in the
/// supplied expression raises D3120; a runtime error raised while evaluating it is wrapped as D3121.
/// </summary>
/// <remarks>
/// The nested evaluation runs in a fresh context seeded from the chosen input and the enclosing evaluation's
/// captured instant and randomness source, so the built-ins (and the date instant and <c>$shuffle</c> entropy)
/// are visible to the evaluated expression; it does not inherit the enclosing binding frame, so a variable
/// bound outside the <c>$eval</c> string is not visible inside it. This is a fragment-relative divergence from
/// the reference, which evaluates in the enclosing environment. See
/// <see href="https://docs.jsonata.org/other-functions">the JSONata other-functions reference</see>.
/// </remarks>
internal static class JsonataEvalFunctions
{
    /// <summary>The context-aware <c>$eval</c> built-in, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataContextualBuiltinFunction> ContextualAll { get; } =
    [
        new JsonataContextualBuiltinFunction(Utf8Strings.From("eval"), InvokeEval, JsonataSignature.Parse("<sx?:x>"))
    ];

    /// <summary>
    /// <c>$eval(expr[, context])</c>: parses the string <c>expr</c> as a JSONata expression and evaluates it
    /// against <c>context</c>, or the current focus when <c>context</c> is undefined. An undefined <c>expr</c>
    /// yields undefined; a parse error raises D3120; a runtime error during the evaluation is wrapped as D3121.
    /// </summary>
    /// <param name="arguments">The argument list; the expression string is the first argument, the optional context the second.</param>
    /// <param name="context">The enclosing evaluation context, whose focus is the default input and whose captured instant and randomness source the nested evaluation inherits.</param>
    /// <returns>The result of evaluating the parsed expression, or undefined for an undefined expression argument.</returns>
    /// <exception cref="JsonataErrorException">The expression cannot be parsed (code D3120) or its evaluation raised a runtime error (code D3121).</exception>
    private static JsonataValue InvokeEval(IReadOnlyList<JsonataValue> arguments, JsonataContext context)
    {
        JsonataValue expression = arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
        if(expression.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        //An undefined supplied context defaults to the current focus, matching the reference's
        //input-as-default-context behaviour.
        JsonataValue input = arguments.Count > 1 && !arguments[1].IsUndefined ? arguments[1] : context.Focus;

        ParseResult<JsonataExpression> parsed = JsonataEngine.Parse(Encoding.UTF8.GetBytes(expression.AsString));
        if(parsed.HasErrors)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.EvalSyntax, null, "Syntax error in the expression passed to the function $eval.");
        }

        try
        {
            return JsonataEvaluator.Evaluate(parsed.Tree, input, context.EvaluationMillis, context.Randomness);
        }
        catch(JsonataErrorException)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.EvalRuntime, null, "Dynamic error evaluating the expression passed to the function $eval.");
        }
    }
}
