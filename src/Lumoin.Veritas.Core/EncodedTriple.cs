using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core;

/// <summary>
/// A triple encoded as three <see cref="TermId"/> handles referencing a
/// <see cref="TermDictionary"/>.
/// </summary>
/// <remarks>
/// <para>
/// At 24 bytes per triple, one billion triples consume approximately 24 GB.
/// All graph operations work on these encoded forms rather than on full
/// <see cref="RdfTerm"/> instances.
/// </para>
/// <para>
/// The fields are typed as <see cref="TermId"/> rather than raw
/// <see cref="long"/> so that callers pass encoded-term handles through the
/// type system rather than arbitrary integers. <see cref="TermId"/> wraps a
/// single <see cref="long"/> with no runtime overhead; the on-the-wire layout
/// of this struct is three adjacent 64-bit integers.
/// </para>
/// <para>
/// <b>Constructing from raw storage.</b> Code reading triples from a backend
/// where identifiers are raw <see cref="long"/>s uses
/// <see cref="FromEncoded(long, long, long)"/> to make the "these are encoded
/// ids, wrap them" boundary explicit.
/// </para>
/// <para>
/// Corresponds to an RDF triple as defined in
/// <see href="https://www.w3.org/TR/rdf12-concepts/#section-triples">RDF 1.2 Concepts §3.6</see>,
/// encoded via dictionary compression for efficient storage and pattern matching.
/// </para>
/// </remarks>
/// <param name="Subject">The subject term identifier.</param>
/// <param name="Predicate">The predicate term identifier.</param>
/// <param name="Object">The object term identifier.</param>
[DebuggerDisplay("S={Subject.Encoded} P={Predicate.Encoded} O={Object.Encoded}")]
public readonly record struct EncodedTriple(TermId Subject, TermId Predicate, TermId Object)
{
    /// <summary>
    /// Creates an <see cref="EncodedTriple"/> from three raw encoded identifiers.
    /// </summary>
    /// <param name="subject">The subject encoded identifier.</param>
    /// <param name="predicate">The predicate encoded identifier.</param>
    /// <param name="object">The object encoded identifier.</param>
    /// <returns>An <see cref="EncodedTriple"/> wrapping the identifiers.</returns>
    /// <remarks>
    /// Call this at the boundary between raw-<see cref="long"/> storage
    /// (e.g. an on-disk index) and the typed pipeline. Inside the pipeline,
    /// where <see cref="TermId"/> values already exist, use the constructor.
    /// </remarks>
    public static EncodedTriple FromEncoded(long subject, long predicate, long @object)
        => new(TermId.FromEncoded(subject), TermId.FromEncoded(predicate), TermId.FromEncoded(@object));
}
