using System.Collections.Generic;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// Validates a synchronous built-in's supplied arguments against its parsed signature, producing the
/// argument list the built-in body runs against. The validator is the single home for the three signature
/// behaviours: context substitution (an absent <c>-</c> argument is filled from the invocation-site focus,
/// type-checked as T0411), array singleton-wrapping (a scalar supplied to an <c>a</c> parameter is wrapped
/// in a one-element plain array), and type checking (a value the signature cannot match raises T0410, and an
/// array-element subtype mismatch raises T0412). The bodies keep only their domain logic and the
/// undefined-passthrough guard.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal static class JsonataSignatureValidator
{
    /// <summary>
    /// Validates the supplied arguments against the parsed signature and builds the effective argument list:
    /// the backtracking match decides how many values each parameter consumes; the per-parameter build then
    /// substitutes the context for an absent <c>-</c> parameter, fills an absent plain-optional parameter
    /// with undefined, singleton-wraps a scalar for an array parameter, and checks the array-element subtype.
    /// </summary>
    /// <param name="signature">The parsed function signature.</param>
    /// <param name="arguments">The supplied argument values in positional order.</param>
    /// <param name="contextFocus">The invocation-site focus, substituted for an absent context parameter.</param>
    /// <returns>The effective argument list the built-in body runs against.</returns>
    /// <exception cref="JsonataErrorException">The arguments do not match (T0410), the context is incompatible (T0411), or an array element is the wrong subtype (T0412).</exception>
    public static JsonataValue[] Validate(JsonataSignature signature, IReadOnlyList<JsonataValue> arguments, JsonataValue contextFocus)
    {
        IReadOnlyList<SignatureParam> parameters = signature.Parameters;
        if(parameters.Count == 0)
        {
            return AsArray(arguments);
        }

        SignatureType[] suppliedSymbols = ComputeSymbols(arguments);
        int[] consumed = Match(parameters, suppliedSymbols);

        return Build(parameters, arguments, consumed, contextFocus);
    }

    /// <summary>Computes the single accepted-symbol bit each supplied value contributes.</summary>
    /// <param name="arguments">The supplied argument values.</param>
    /// <returns>The per-argument symbol bits.</returns>
    private static SignatureType[] ComputeSymbols(IReadOnlyList<JsonataValue> arguments)
    {
        SignatureType[] symbols = new SignatureType[arguments.Count];
        for(int i = 0; i < arguments.Count; i++)
        {
            symbols[i] = GetSymbol(arguments[i]);
        }

        return symbols;
    }

    /// <summary>Maps a value to the single signature symbol bit that describes it.</summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The value's symbol bit.</returns>
    private static SignatureType GetSymbol(JsonataValue value)
    {
        return value.Kind switch
        {
            JsonataValueKind.Function => SignatureType.Function,
            JsonataValueKind.Null => SignatureType.Null,
            JsonataValueKind.Array => SignatureType.Array,
            JsonataValueKind.Object => SignatureType.Object,
            JsonataValueKind.String => SignatureType.String,
            JsonataValueKind.Number => SignatureType.Number,
            JsonataValueKind.Boolean => SignatureType.Boolean,
            _ => SignatureType.Missing
        };
    }

    /// <summary>
    /// Matches the supplied symbols against the parameter sequence, honouring each parameter's quantifier and
    /// accepted set, and records how many symbols each parameter consumed. The match is a bounded iterative
    /// backtracking search (an explicit stack, no recursion): each parameter tries its largest feasible
    /// consume count first and shrinks on a dead end, so an optional parameter can yield a value to a later
    /// required parameter whose accepted set overlaps it (the behaviour a backtracking regex gives). The whole
    /// supplied string must be consumed and every required parameter filled, else T0410 is raised at the
    /// best-prefix position.
    /// </summary>
    /// <param name="parameters">The signature parameters.</param>
    /// <param name="suppliedSymbols">The per-argument symbol bits.</param>
    /// <returns>The per-parameter consumed count.</returns>
    /// <exception cref="JsonataErrorException">The arguments do not match the signature (T0410).</exception>
    private static int[] Match(IReadOnlyList<SignatureParam> parameters, SignatureType[] suppliedSymbols)
    {
        int[] consumed = new int[parameters.Count];
        int bestPrefix = 0;
        Stack<MatchState> stack = new();
        stack.Push(BuildState(parameters, suppliedSymbols, paramIndex: 0, argCursor: 0));
        while(stack.Count > 0)
        {
            MatchState state = stack.Peek();
            if(state.ArgCursor > bestPrefix)
            {
                bestPrefix = state.ArgCursor;
            }

            if(state.ParamIndex == parameters.Count)
            {
                if(state.ArgCursor == suppliedSymbols.Length)
                {
                    return consumed;
                }

                stack.Pop();

                continue;
            }

            if(state.NextTaken < state.MinTaken)
            {
                //Every consume count for this parameter has been exhausted; backtrack to the previous one.
                stack.Pop();

                continue;
            }

            int taken = state.NextTaken;
            state.NextTaken--;
            consumed[state.ParamIndex] = taken;
            stack.Push(BuildState(parameters, suppliedSymbols, state.ParamIndex + 1, state.ArgCursor + taken));
        }

        throw ArgumentMismatch(bestPrefix, suppliedSymbols.Length);
    }

    /// <summary>
    /// Builds the search state for one parameter: how many symbols it could maximally consume from the cursor
    /// (its feasible upper bound, tried first) and the minimum its quantifier requires.
    /// </summary>
    /// <param name="parameters">The signature parameters.</param>
    /// <param name="suppliedSymbols">The per-argument symbol bits.</param>
    /// <param name="paramIndex">The parameter index this state matches.</param>
    /// <param name="argCursor">The argument cursor entering this parameter.</param>
    /// <returns>The search state.</returns>
    private static MatchState BuildState(IReadOnlyList<SignatureParam> parameters, SignatureType[] suppliedSymbols, int paramIndex, int argCursor)
    {
        if(paramIndex >= parameters.Count)
        {
            return new MatchState(paramIndex, argCursor, nextTaken: 0, minTaken: 0);
        }

        SignatureParam param = parameters[paramIndex];
        int minimum = param.Quantifier == SignatureQuantifier.Optional ? 0 : 1;
        int hardMaximum = param.Quantifier == SignatureQuantifier.OneOrMore ? suppliedSymbols.Length : 1;
        int feasible = 0;
        while(feasible < hardMaximum && argCursor + feasible < suppliedSymbols.Length && Accepts(param, suppliedSymbols[argCursor + feasible]))
        {
            feasible++;
        }

        return new MatchState(paramIndex, argCursor, feasible, minimum);
    }

    /// <summary>Determines whether a parameter accepts a supplied symbol.</summary>
    /// <param name="param">The parameter.</param>
    /// <param name="symbol">The supplied symbol bit.</param>
    /// <returns><see langword="true"/> when the parameter's accepted set contains the symbol.</returns>
    private static bool Accepts(SignatureParam param, SignatureType symbol)
    {
        return (param.TypeSet & symbol) != 0;
    }

    /// <summary>
    /// Builds the effective argument list by walking the parameters with an argument cursor: a parameter that
    /// consumed nothing either substitutes the type-checked context (a <c>-</c> parameter) or pushes
    /// undefined (a plain optional); a parameter that consumed values either singleton-wraps each scalar for
    /// an array parameter (checking the element subtype) or passes each value through.
    /// </summary>
    /// <param name="parameters">The signature parameters.</param>
    /// <param name="arguments">The supplied argument values.</param>
    /// <param name="consumed">The per-parameter consumed count from the match.</param>
    /// <param name="contextFocus">The invocation-site focus, substituted for an absent context parameter.</param>
    /// <returns>The effective argument list.</returns>
    /// <exception cref="JsonataErrorException">The context is incompatible (T0411) or an array element is the wrong subtype (T0412).</exception>
    private static JsonataValue[] Build(IReadOnlyList<SignatureParam> parameters, IReadOnlyList<JsonataValue> arguments, int[] consumed, JsonataValue contextFocus)
    {
        List<JsonataValue> built = [];
        int argIndex = 0;
        for(int p = 0; p < parameters.Count; p++)
        {
            SignatureParam param = parameters[p];
            if(consumed[p] == 0)
            {
                argIndex = BuildAbsent(param, contextFocus, argIndex, built);

                continue;
            }

            for(int c = 0; c < consumed[p]; c++)
            {
                JsonataValue arg = arguments[argIndex];
                built.Add(BuildPresent(param, arg, argIndex));
                argIndex++;
            }
        }

        return [.. built];
    }

    /// <summary>
    /// Builds the value for a parameter that consumed nothing without advancing the argument cursor (a
    /// consumed-zero parameter reads no supplied value, so the cursor stays on the next parameter's first
    /// argument): a context parameter substitutes the focus into the effective list, raising T0411 when the
    /// focus's type is outside the context set (the undefined "nothing" always satisfies, since every context
    /// set admits the missing symbol); a plain optional parameter contributes nothing, so a later parameter's
    /// matched values keep their positions in the effective list rather than being shifted by an injected
    /// placeholder. A trailing parameter the caller does not bind reads as undefined regardless.
    /// </summary>
    /// <param name="param">The parameter.</param>
    /// <param name="contextFocus">The invocation-site focus.</param>
    /// <param name="argIndex">The current argument cursor.</param>
    /// <param name="built">The effective argument list being built.</param>
    /// <returns>The argument cursor, unchanged (a consumed-zero parameter reads no argument).</returns>
    /// <exception cref="JsonataErrorException">The context value's type is incompatible with the context parameter (T0411).</exception>
    private static int BuildAbsent(SignatureParam param, JsonataValue contextFocus, int argIndex, List<JsonataValue> built)
    {
        if(param.IsContext)
        {
            SignatureType contextSymbol = GetSymbol(contextFocus);
            if((param.ContextTypeSet & contextSymbol) == 0)
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.ContextIncompatible, null, $"Context value is not a compatible type with argument {argIndex + 1} of function.");
            }

            built.Add(contextFocus);
        }

        return argIndex;
    }

    /// <summary>
    /// Builds the value for one symbol a parameter consumed: an array parameter singleton-wraps a scalar in a
    /// one-element plain array (checking the element subtype first), and any other parameter passes the value
    /// through unchanged. A missing value flows through untouched.
    /// </summary>
    /// <param name="param">The parameter.</param>
    /// <param name="arg">The supplied value.</param>
    /// <param name="argIndex">The supplied value's position.</param>
    /// <returns>The built value.</returns>
    /// <exception cref="JsonataErrorException">An array element is the wrong subtype (T0412).</exception>
    private static JsonataValue BuildPresent(SignatureParam param, JsonataValue arg, int argIndex)
    {
        if(!param.IsArray)
        {
            return arg;
        }

        if(arg.Kind == JsonataValueKind.Undefined)
        {
            return arg;
        }

        if(param.Subtype != '\0')
        {
            CheckSubtype(param.Subtype, arg, argIndex);
        }

        if(arg.Kind == JsonataValueKind.Array)
        {
            return arg;
        }

        return JsonataValue.Array([arg]);
    }

    /// <summary>
    /// Checks the array-element subtype of an array argument: a non-empty array must have every element of the
    /// subtype symbol; an empty array passes vacuously; a non-array scalar passes only when its own symbol is
    /// the subtype (it is about to be singleton-wrapped). A mismatch raises T0412.
    /// </summary>
    /// <param name="subtype">The required element subtype letter.</param>
    /// <param name="arg">The supplied value.</param>
    /// <param name="argIndex">The supplied value's position.</param>
    /// <exception cref="JsonataErrorException">An element is not the required subtype (T0412).</exception>
    private static void CheckSubtype(char subtype, JsonataValue arg, int argIndex)
    {
        SignatureType subtypeSymbol = SymbolForLetter(subtype);
        if(arg.Kind != JsonataValueKind.Array)
        {
            if(GetSymbol(arg) != subtypeSymbol)
            {
                throw WrongElementType(subtype, argIndex);
            }

            return;
        }

        foreach(JsonataValue element in arg.AsArray)
        {
            if(GetSymbol(element) != subtypeSymbol)
            {
                throw WrongElementType(subtype, argIndex);
            }
        }
    }

    /// <summary>Maps a subtype letter to the single symbol bit a conforming element must carry.</summary>
    /// <param name="letter">The subtype letter.</param>
    /// <returns>The symbol bit.</returns>
    private static SignatureType SymbolForLetter(char letter)
    {
        return letter switch
        {
            'n' => SignatureType.Number,
            's' => SignatureType.String,
            'b' => SignatureType.Boolean,
            'l' => SignatureType.Null,
            'o' => SignatureType.Object,
            'a' => SignatureType.Array,
            'f' => SignatureType.Function,
            _ => SignatureType.Missing
        };
    }

    /// <summary>Builds the T0410 argument-mismatch error at the first unmatched position.</summary>
    /// <param name="argCursor">The count of successfully-consumed arguments.</param>
    /// <param name="suppliedCount">The number of supplied arguments.</param>
    /// <returns>The argument-mismatch error.</returns>
    private static JsonataErrorException ArgumentMismatch(int argCursor, int suppliedCount)
    {
        int index = argCursor < suppliedCount ? argCursor + 1 : suppliedCount;

        return new JsonataErrorException(WellKnownJsonataErrors.ArgumentMismatch, null, $"Argument {index} of function does not match function signature.");
    }

    /// <summary>Builds the T0412 wrong-element-type error, pluralising the subtype letter.</summary>
    /// <param name="subtype">The required element subtype letter.</param>
    /// <param name="argIndex">The supplied value's position.</param>
    /// <returns>The wrong-element-type error.</returns>
    private static JsonataErrorException WrongElementType(char subtype, int argIndex)
    {
        return new JsonataErrorException(WellKnownJsonataErrors.WrongElementType, null, $"Argument {argIndex + 1} of function must be an array of {PluralSubtype(subtype)}.");
    }

    /// <summary>Pluralises a subtype letter for the T0412 message.</summary>
    /// <param name="subtype">The subtype letter.</param>
    /// <returns>The pluralised element-type name.</returns>
    private static string PluralSubtype(char subtype)
    {
        return subtype switch
        {
            'n' => "numbers",
            's' => "strings",
            'o' => "objects",
            'a' => "arrays",
            'b' => "booleans",
            'f' => "functions",
            _ => "values"
        };
    }

    /// <summary>Copies a read-only argument list into a fresh array, the form the built-in delegate runs against.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The arguments as an array.</returns>
    private static JsonataValue[] AsArray(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue[] array = new JsonataValue[arguments.Count];
        for(int i = 0; i < arguments.Count; i++)
        {
            array[i] = arguments[i];
        }

        return array;
    }

    /// <summary>
    /// One frame of the iterative backtracking match: the parameter being matched, the argument cursor
    /// entering it, the next consume count to try (counted down from the feasible maximum to the minimum), and
    /// the minimum its quantifier requires. A mutable reference type so the decrement of the next-to-try count
    /// persists on the stacked frame as the search backtracks.
    /// </summary>
    private sealed class MatchState
    {
        /// <summary>Initializes a match frame.</summary>
        /// <param name="paramIndex">The parameter index this frame matches.</param>
        /// <param name="argCursor">The argument cursor entering this parameter.</param>
        /// <param name="nextTaken">The next consume count to try (the feasible maximum first).</param>
        /// <param name="minTaken">The minimum consume count the quantifier requires.</param>
        public MatchState(int paramIndex, int argCursor, int nextTaken, int minTaken)
        {
            ParamIndex = paramIndex;
            ArgCursor = argCursor;
            NextTaken = nextTaken;
            MinTaken = minTaken;
        }

        /// <summary>Gets the parameter index this frame matches.</summary>
        public int ParamIndex { get; }

        /// <summary>Gets the argument cursor entering this parameter.</summary>
        public int ArgCursor { get; }

        /// <summary>Gets or sets the next consume count to try, counted down from the feasible maximum to the minimum.</summary>
        public int NextTaken { get; set; }

        /// <summary>Gets the minimum consume count the quantifier requires.</summary>
        public int MinTaken { get; }
    }
}
