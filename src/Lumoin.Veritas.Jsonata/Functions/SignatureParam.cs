namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// One parsed parameter of a function signature: its accepted-symbol set, how many values it consumes, and
/// the modifier-derived flags (context substitution, array singleton-wrapping, and an optional array-element
/// subtype).
/// </summary>
/// <param name="TypeSet">The set of supplied-value symbols the parameter accepts.</param>
/// <param name="Quantifier">How many supplied values the parameter consumes during the greedy match.</param>
/// <param name="IsContext">Whether the <c>-</c> context-substitution modifier applies: an absent value is filled from the invocation-site focus.</param>
/// <param name="ContextTypeSet">The accepted-symbol set captured before the <c>-</c> modifier relaxed the parameter to optional, against which a substituted context value is type-checked.</param>
/// <param name="IsArray">Whether the <c>a</c> array type applies: a supplied scalar is wrapped in a one-element plain array.</param>
/// <param name="Subtype">The array-element subtype symbol letter from an <c>a&lt;...&gt;</c> parameter, or <c>'\0'</c> when none.</param>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal readonly record struct SignatureParam(
    SignatureType TypeSet,
    SignatureQuantifier Quantifier,
    bool IsContext,
    SignatureType ContextTypeSet,
    bool IsArray,
    char Subtype);
