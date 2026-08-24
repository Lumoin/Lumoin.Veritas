namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// How many supplied values a signature parameter consumes during the greedy match: exactly one, zero or
/// one, or one or more.
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
internal enum SignatureQuantifier
{
    /// <summary>The parameter consumes exactly one matching value (a required positional parameter).</summary>
    One = 0,

    /// <summary>The parameter consumes zero or one matching value (an optional parameter, the <c>?</c> or <c>-</c> modifier).</summary>
    Optional,

    /// <summary>The parameter consumes one or more matching values (the <c>+</c> modifier).</summary>
    OneOrMore
}
