using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core;


/// <summary>
/// A quad encoded as four <see cref="TermId"/> handles referencing a
/// <see cref="TermDictionary"/>.
/// </summary>
/// <remarks>
/// <para>
/// The graph identifier enables named graph support. A graph identifier
/// equal to <see cref="DefaultGraph"/> (the <see cref="TermId.None"/>
/// sentinel, encoded value <c>0</c>) conventionally represents the default
/// graph. Sixteen bytes per quad: four adjacent 32-bit unsigned integers.
/// </para>
/// <para>
/// Corresponds to a quad in an RDF dataset as defined in
/// <see href="https://www.w3.org/TR/rdf12-concepts/#section-dataset">RDF 1.2 Concepts §4</see>.
/// </para>
/// </remarks>
/// <param name="Subject">The subject term identifier.</param>
/// <param name="Predicate">The predicate term identifier.</param>
/// <param name="Object">The object term identifier.</param>
/// <param name="Graph">The graph term identifier. Use <see cref="DefaultGraph"/> for the default graph.</param>
[DebuggerDisplay("S={Subject.Encoded} P={Predicate.Encoded} O={Object.Encoded} G={Graph.Encoded}")]
public readonly record struct EncodedQuad(TermId Subject, TermId Predicate, TermId Object, TermId Graph)
{
    /// <summary>
    /// The conventional identifier for the default graph: the
    /// <see cref="TermId.None"/> sentinel (encoded value <c>0</c>).
    /// </summary>
    public static TermId DefaultGraph { get; } = TermId.None;

    /// <summary>
    /// Creates an <see cref="EncodedQuad"/> from four raw encoded identifiers.
    /// </summary>
    public static EncodedQuad FromEncoded(uint subject, uint predicate, uint @object, uint graph)
        => new(
            TermId.FromEncoded(subject),
            TermId.FromEncoded(predicate),
            TermId.FromEncoded(@object),
            TermId.FromEncoded(graph));

    /// <summary>
    /// Returns the triple portion of this quad, discarding the graph identifier.
    /// </summary>
    /// <returns>An <see cref="EncodedTriple"/> with the same subject, predicate, and object.</returns>
    public EncodedTriple AsTriple() => new(Subject, Predicate, Object);
}