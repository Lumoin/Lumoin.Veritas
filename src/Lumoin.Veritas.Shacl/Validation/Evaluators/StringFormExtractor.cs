using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Extracts the SHACL "string form" of an <see cref="RdfTerm"/>.
/// </summary>
/// <remarks>
/// <para>
/// SHACL's <c>sh:minLength</c>, <c>sh:maxLength</c>, and
/// <c>sh:singleLine</c> all operate on the string form of a value
/// node. The string form is defined as the lexical value for
/// literals and the IRI string for IRI nodes; blank nodes have no
/// string form and therefore always fail these constraints.
/// </para>
/// <para>
/// Returning <see langword="null"/> for blank nodes lets the calling
/// evaluator treat "no string form" as a guaranteed mismatch via
/// idiomatic <see cref="string.Length"/> / contains-line-break checks
/// gated on a non-null result.
/// </para>
/// </remarks>
internal static class StringFormExtractor
{
    /// <summary>
    /// Returns the string form of <paramref name="term"/>, or
    /// <see langword="null"/> if the term has no string form (blank
    /// node).
    /// </summary>
    public static string? Extract(RdfTerm term)
        => term switch
        {
            Literal literal => literal.Value.ToString(),
            NamedNode named => named.Iri.ToString(),
            _ => null,
        };
}
