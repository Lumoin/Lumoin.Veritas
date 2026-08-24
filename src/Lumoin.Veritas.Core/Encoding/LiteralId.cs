using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Encoding;

/// <summary>
/// A <see cref="TermId"/> that is known to refer to a <see cref="Literal"/>.
/// </summary>
/// <remarks>
/// <para>
/// Literals may appear only in the object position of a triple (and, for
/// RDF 1.2, in the object position of a triple term). SHACL constraints that
/// compare literal values (<c>sh:minInclusive</c>, <c>sh:pattern</c>, etc.)
/// often want to carry <see cref="LiteralId"/> so that the constraint
/// evaluator can assume the term is a literal without re-checking.
/// </para>
/// <para>
/// Construction and widening semantics match <see cref="IriId"/>.
/// </para>
/// </remarks>
/// <param name="Value">The underlying <see cref="TermId"/>.</param>
[DebuggerDisplay("LiteralId({Value.Encoded})")]
public readonly record struct LiteralId(TermId Value)
{
    /// <summary>The raw encoded identifier.</summary>
    public uint Encoded => Value.Encoded;

    /// <summary>
    /// Validates that <paramref name="termId"/> resolves to a
    /// <see cref="Literal"/> and returns it as a <see cref="LiteralId"/>.
    /// </summary>
    public static LiteralId From(TermId termId, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is not Literal)
        {
            throw new InvalidOperationException(
                $"TermId {termId.Encoded} does not resolve to a Literal; actual kind: {term.GetType().Name}.");
        }

        return new LiteralId(termId);
    }

    /// <summary>
    /// Attempts to narrow <paramref name="termId"/> to a <see cref="LiteralId"/>
    /// without throwing.
    /// </summary>
    public static bool TryFrom(TermId termId, TermDictionary dictionary, out LiteralId literalId)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is Literal)
        {
            literalId = new LiteralId(termId);
            return true;
        }

        literalId = default;
        return false;
    }

    /// <summary>
    /// Wraps <paramref name="termId"/> as a <see cref="LiteralId"/> without
    /// validating the underlying term kind. Caller asserts by construction.
    /// </summary>
    public static LiteralId FromUnchecked(TermId termId) => new(termId);

    /// <summary>Implicit widening from <see cref="LiteralId"/> to <see cref="TermId"/>.</summary>
    public static implicit operator TermId(LiteralId literalId) => literalId.Value;

    /// <inheritdoc/>
    public override string ToString() => $"LiteralId({Encoded})";
}
