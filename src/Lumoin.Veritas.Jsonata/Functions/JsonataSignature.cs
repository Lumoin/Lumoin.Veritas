using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// A parsed JSONata function signature: the ordered parameter descriptors recovered from a signature string
/// <c>&lt;params:return&gt;</c> plus the original definition text. The return type is parsed-past and not
/// retained — validation only consults the parameter list. A signature is parsed once at registry build via
/// <see cref="Parse"/> and reused for every invocation.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal sealed class JsonataSignature
{
    /// <summary>The maximum number of parameters a signature may declare, bounding the iterative parse loop.</summary>
    private const int MaxParameters = 64;

    /// <summary>Initializes a parsed signature from its parameters and the original definition text.</summary>
    /// <param name="parameters">The ordered parameter descriptors.</param>
    /// <param name="definition">The original signature string.</param>
    private JsonataSignature(IReadOnlyList<SignatureParam> parameters, string definition)
    {
        Parameters = parameters;
        Definition = definition;
    }

    /// <summary>Gets the ordered parameter descriptors.</summary>
    public IReadOnlyList<SignatureParam> Parameters { get; }

    /// <summary>Gets the original signature string.</summary>
    public string Definition { get; }

    /// <summary>
    /// Gets the number of synthesized per-element arguments a higher-order caller delivers to this built-in:
    /// the count of required (non-optional) parameters, but at least one, so the element value always reaches
    /// the built-in while a trailing optional or context parameter is not forced to consume the synthesized
    /// index or array argument.
    /// </summary>
    public int HigherOrderArity => Math.Max(1, CountRequired(Parameters));

    /// <summary>
    /// Parses a signature string <c>&lt;params:return&gt;</c> into its parameter descriptors over a bounded
    /// iterative char-walk. Each base type letter starts a parameter and advances; the modifiers
    /// (<c>-</c>/<c>?</c>/<c>+</c>) and an <c>&lt;...&gt;</c> subtype mutate the just-created parameter. The
    /// walk stops at the <c>:</c> return-type separator or the closing <c>&gt;</c>.
    /// </summary>
    /// <param name="signature">The signature string, including the enclosing angle brackets.</param>
    /// <returns>The parsed signature.</returns>
    /// <exception cref="JsonataParseException">The signature declares more than <see cref="MaxParameters"/> parameters.</exception>
    /// <exception cref="JsonataErrorException">A subtype follows a non-container parameter (S0401) or a bracket nests in a union (S0402).</exception>
    public static JsonataSignature Parse(string signature)
    {
        List<SignatureParam> parameters = [];
        int index = signature.Length > 0 && signature[0] == '<' ? 1 : 0;
        while(index < signature.Length)
        {
            char symbol = signature[index];
            if(symbol is ':' or '>')
            {
                break;
            }

            index = StepSignature(signature, index, parameters);
            if(parameters.Count > MaxParameters)
            {
                throw new JsonataParseException("The JSONata function signature declares too many parameters.");
            }
        }

        return new JsonataSignature(parameters, signature);
    }

    /// <summary>
    /// Consumes one signature element at the cursor: a base type letter (appending a new parameter), a union
    /// <c>(...)</c> (appending a parameter whose set is the listed letters' union), a modifier
    /// (<c>-</c>/<c>?</c>/<c>+</c>, mutating the previous parameter), or an <c>&lt;...&gt;</c> subtype
    /// (mutating the previous parameter).
    /// </summary>
    /// <param name="signature">The signature string.</param>
    /// <param name="index">The cursor at the element to consume.</param>
    /// <param name="parameters">The parameter list being built.</param>
    /// <returns>The cursor past the consumed element.</returns>
    /// <exception cref="JsonataErrorException">A subtype follows a non-container parameter (S0401) or a bracket nests in a union (S0402).</exception>
    private static int StepSignature(string signature, int index, List<SignatureParam> parameters)
    {
        char symbol = signature[index];

        return symbol switch
        {
            '(' => AppendUnion(signature, index, parameters),
            '<' => ApplySubtype(signature, index, parameters),
            '-' or '?' or '+' => ApplyModifier(symbol, index, parameters),
            _ => AppendType(symbol, index, parameters)
        };
    }

    /// <summary>Appends a parameter for a base type letter, contributing that letter's accepted-symbol set.</summary>
    /// <param name="symbol">The base type letter.</param>
    /// <param name="index">The cursor at the type letter.</param>
    /// <param name="parameters">The parameter list being built.</param>
    /// <returns>The cursor past the type letter.</returns>
    private static int AppendType(char symbol, int index, List<SignatureParam> parameters)
    {
        SignatureType set = TypeSetFor(symbol);
        parameters.Add(new SignatureParam(set, SignatureQuantifier.One, IsContext: false, ContextTypeSet: SignatureType.None, IsArray: symbol == 'a', Subtype: '\0'));

        return index + 1;
    }

    /// <summary>Appends a parameter for a union <c>(letters)</c>, whose accepted-symbol set is the listed letters' union plus the missing symbol.</summary>
    /// <param name="signature">The signature string.</param>
    /// <param name="index">The cursor at the opening parenthesis.</param>
    /// <param name="parameters">The parameter list being built.</param>
    /// <returns>The cursor past the closing parenthesis.</returns>
    /// <exception cref="JsonataErrorException">A bracket nests in the union (S0402).</exception>
    private static int AppendUnion(string signature, int index, List<SignatureParam> parameters)
    {
        SignatureType set = SignatureType.Missing;
        int cursor = index + 1;
        while(cursor < signature.Length && signature[cursor] != ')')
        {
            char letter = signature[cursor];
            if(letter == '<')
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.BracketInUnion, null, "A JSONata signature union must not contain a parameterised type.");
            }

            set |= TypeSetFor(letter);
            cursor++;
        }

        parameters.Add(new SignatureParam(set, SignatureQuantifier.One, IsContext: false, ContextTypeSet: SignatureType.None, IsArray: false, Subtype: '\0'));

        return cursor + 1;
    }

    /// <summary>
    /// Applies a modifier to the previous parameter: <c>-</c> marks context substitution (snapshotting the
    /// pre-relaxation set and relaxing to optional), <c>?</c> relaxes to optional, and <c>+</c> requires one
    /// or more.
    /// </summary>
    /// <param name="symbol">The modifier character.</param>
    /// <param name="index">The cursor at the modifier.</param>
    /// <param name="parameters">The parameter list being built.</param>
    /// <returns>The cursor past the modifier.</returns>
    private static int ApplyModifier(char symbol, int index, List<SignatureParam> parameters)
    {
        if(parameters.Count == 0)
        {
            return index + 1;
        }

        SignatureParam previous = parameters[^1];
        SignatureParam mutated = symbol switch
        {
            '-' => previous with { IsContext = true, ContextTypeSet = previous.TypeSet, Quantifier = SignatureQuantifier.Optional },
            '?' => previous with { Quantifier = SignatureQuantifier.Optional },
            _ => previous with { Quantifier = SignatureQuantifier.OneOrMore }
        };

        parameters[^1] = mutated;

        return index + 1;
    }

    /// <summary>
    /// Applies an <c>&lt;...&gt;</c> subtype to the previous parameter, which must be an array or function
    /// parameter; the subtype's first symbol is stored (only array-element subtypes are enforced at
    /// validation).
    /// </summary>
    /// <param name="signature">The signature string.</param>
    /// <param name="index">The cursor at the opening angle bracket.</param>
    /// <param name="parameters">The parameter list being built.</param>
    /// <returns>The cursor past the closing angle bracket.</returns>
    /// <exception cref="JsonataErrorException">The previous parameter is not an array or function parameter (S0401).</exception>
    private static int ApplySubtype(string signature, int index, List<SignatureParam> parameters)
    {
        if(parameters.Count == 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.SubtypeOnNonContainer, null, "A JSONata signature parameterised type must follow an array or function parameter.");
        }

        SignatureParam previous = parameters[^1];
        bool isContainer = previous.IsArray || (previous.TypeSet & SignatureType.Function) != 0;
        if(!isContainer)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.SubtypeOnNonContainer, null, "A JSONata signature parameterised type must follow an array or function parameter.");
        }

        int closing = FindClosingBracket(signature, index);
        char subtype = index + 1 < closing ? signature[index + 1] : '\0';
        if(previous.IsArray)
        {
            parameters[^1] = previous with { Subtype = subtype };
        }

        return closing + 1;
    }

    /// <summary>Finds the index of the <c>&gt;</c> matching the <c>&lt;</c> at the open index over an iterative depth counter.</summary>
    /// <param name="signature">The signature string.</param>
    /// <param name="open">The index of the opening angle bracket.</param>
    /// <returns>The index of the matching closing angle bracket, or the end of the string when unbalanced.</returns>
    private static int FindClosingBracket(string signature, int open)
    {
        int depth = 0;
        for(int cursor = open; cursor < signature.Length; cursor++)
        {
            char symbol = signature[cursor];
            if(symbol == '<')
            {
                depth++;
            }
            else if(symbol == '>')
            {
                depth--;
                if(depth == 0)
                {
                    return cursor;
                }
            }
        }

        return signature.Length;
    }

    /// <summary>Counts the required (non-optional) parameters of a signature.</summary>
    /// <param name="parameters">The signature parameters.</param>
    /// <returns>The number of parameters whose quantifier is not optional.</returns>
    private static int CountRequired(IReadOnlyList<SignatureParam> parameters)
    {
        int required = 0;
        foreach(SignatureParam param in parameters)
        {
            if(param.Quantifier != SignatureQuantifier.Optional)
            {
                required++;
            }
        }

        return required;
    }

    /// <summary>Maps a base type letter to the accepted-symbol set it contributes.</summary>
    /// <param name="symbol">The base type letter.</param>
    /// <returns>The accepted-symbol set.</returns>
    private static SignatureType TypeSetFor(char symbol)
    {
        return symbol switch
        {
            's' => SignatureType.String | SignatureType.Missing,
            'n' => SignatureType.Number | SignatureType.Missing,
            'b' => SignatureType.Boolean | SignatureType.Missing,
            'l' => SignatureType.Null | SignatureType.Missing,
            'o' => SignatureType.Object | SignatureType.Missing,
            'a' => SignatureType.String | SignatureType.Number | SignatureType.Boolean | SignatureType.Null | SignatureType.Array | SignatureType.Object | SignatureType.Function | SignatureType.Missing,
            'f' => SignatureType.Function,
            'j' => SignatureType.String | SignatureType.Number | SignatureType.Boolean | SignatureType.Null | SignatureType.Array | SignatureType.Object | SignatureType.Missing,
            _ => SignatureType.String | SignatureType.Number | SignatureType.Boolean | SignatureType.Null | SignatureType.Array | SignatureType.Object | SignatureType.Function | SignatureType.Missing
        };
    }
}
