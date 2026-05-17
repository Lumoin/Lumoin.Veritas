using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Encoding;

/// <summary>
/// A <see cref="TermId"/> that is known to refer to a <see cref="TripleTerm"/>
/// (RDF 1.2).
/// </summary>
/// <remarks>
/// <para>
/// Triple terms are new in RDF 1.2 — they allow a triple itself to appear as a
/// term (typically as the object of <c>rdf:reifies</c>). Algorithms that
/// handle triple terms distinctly (reification analysis, SHACL
/// <c>sh:reifierShape</c> evaluation, some SPARQL patterns) benefit from
/// carrying <see cref="TripleTermId"/> rather than raw <see cref="TermId"/>.
/// </para>
/// <para>
/// Construction and widening semantics match <see cref="IriId"/>.
/// </para>
/// </remarks>
/// <param name="Value">The underlying <see cref="TermId"/>.</param>
[DebuggerDisplay("TripleTermId({Value.Encoded})")]
public readonly record struct TripleTermId(TermId Value)
{
    /// <summary>The raw encoded identifier.</summary>
    public uint Encoded => Value.Encoded;

    /// <summary>
    /// Validates that <paramref name="termId"/> resolves to a
    /// <see cref="TripleTerm"/> and returns it as a <see cref="TripleTermId"/>.
    /// </summary>
    public static TripleTermId From(TermId termId, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is not TripleTerm)
        {
            throw new InvalidOperationException(
                $"TermId {termId.Encoded} does not resolve to a TripleTerm; actual kind: {term.GetType().Name}.");
        }

        return new TripleTermId(termId);
    }

    /// <summary>
    /// Attempts to narrow <paramref name="termId"/> to a
    /// <see cref="TripleTermId"/> without throwing.
    /// </summary>
    public static bool TryFrom(TermId termId, TermDictionary dictionary, out TripleTermId tripleTermId)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is TripleTerm)
        {
            tripleTermId = new TripleTermId(termId);
            return true;
        }

        tripleTermId = default;
        return false;
    }

    /// <summary>
    /// Wraps <paramref name="termId"/> as a <see cref="TripleTermId"/> without
    /// validating the underlying term kind. Caller asserts by construction.
    /// </summary>
    public static TripleTermId FromUnchecked(TermId termId) => new(termId);

    /// <summary>Implicit widening from <see cref="TripleTermId"/> to <see cref="TermId"/>.</summary>
    public static implicit operator TermId(TripleTermId tripleTermId) => tripleTermId.Value;

    /// <inheritdoc/>
    public override string ToString() => $"TripleTermId({Encoded})";
}
