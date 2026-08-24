using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Encoding;

/// <summary>
/// A <see cref="TermId"/> that is known to refer to a <see cref="BlankNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// Blank nodes are document-scoped identifiers that do not persist beyond the
/// RDF document in which they appear. They may appear as subject or object
/// positions of triples but not as predicates. Algorithms that must
/// distinguish blank nodes from other term kinds (e.g. canonicalization)
/// benefit from carrying <see cref="BlankNodeId"/> rather than raw
/// <see cref="TermId"/>.
/// </para>
/// <para>
/// Construction and widening semantics match <see cref="IriId"/>: validating
/// <see cref="From(TermId, TermDictionary)"/>, unsafe
/// <see cref="FromUnchecked(TermId)"/>, implicit widening to
/// <see cref="TermId"/>. The raw <see cref="uint"/> value is reached
/// explicitly via <see cref="Encoded"/>.
/// </para>
/// </remarks>
/// <param name="Value">The underlying <see cref="TermId"/>.</param>
[DebuggerDisplay("BlankNodeId({Value.Encoded})")]
public readonly record struct BlankNodeId(TermId Value)
{
    /// <summary>The raw encoded identifier.</summary>
    public uint Encoded => Value.Encoded;

    /// <summary>
    /// Validates that <paramref name="termId"/> resolves to a
    /// <see cref="BlankNode"/> and returns it as a <see cref="BlankNodeId"/>.
    /// </summary>
    /// <param name="termId">The identifier to narrow.</param>
    /// <param name="dictionary">The dictionary used to resolve <paramref name="termId"/>.</param>
    /// <returns>A validated <see cref="BlankNodeId"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resolved term is not a <see cref="BlankNode"/>.
    /// </exception>
    public static BlankNodeId From(TermId termId, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is not BlankNode)
        {
            throw new InvalidOperationException(
                $"TermId {termId.Encoded} does not resolve to a BlankNode; actual kind: {term.GetType().Name}.");
        }

        return new BlankNodeId(termId);
    }

    /// <summary>
    /// Attempts to narrow <paramref name="termId"/> to a <see cref="BlankNodeId"/>
    /// without throwing.
    /// </summary>
    public static bool TryFrom(TermId termId, TermDictionary dictionary, out BlankNodeId blankNodeId)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        RdfTerm term = dictionary.Resolve(termId.Encoded);
        if(term is BlankNode)
        {
            blankNodeId = new BlankNodeId(termId);
            return true;
        }

        blankNodeId = default;
        return false;
    }

    /// <summary>
    /// Wraps <paramref name="termId"/> as a <see cref="BlankNodeId"/> without
    /// validating the underlying term kind. Caller asserts by construction.
    /// </summary>
    public static BlankNodeId FromUnchecked(TermId termId) => new(termId);

    /// <summary>Implicit widening from <see cref="BlankNodeId"/> to <see cref="TermId"/>.</summary>
    public static implicit operator TermId(BlankNodeId blankNodeId) => blankNodeId.Value;

    /// <inheritdoc/>
    public override string ToString() => $"BlankNodeId({Encoded})";
}
