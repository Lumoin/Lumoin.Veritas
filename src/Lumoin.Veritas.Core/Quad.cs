using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core;

/// <summary>
/// A quad (subject, predicate, object, graph) using full <see cref="RdfTerm"/> references.
/// </summary>
/// <remarks>
/// <para>
/// This type is used at API boundaries: JSON-LD expansion output, SPARQL query results,
/// N-Quads serialization input. For internal graph operations, use <see cref="EncodedQuad"/>
/// with <see cref="TermId"/> handles from a <see cref="TermDictionary"/>.
/// </para>
/// <para>
/// The quad corresponds to a statement in an RDF dataset as defined in
/// <see href="https://www.w3.org/TR/rdf12-concepts/#section-dataset">RDF 1.2 Concepts §4</see>.
/// </para>
/// <para>
/// The subject must be a <see cref="NamedNode"/>, <see cref="BlankNode"/>, or <see cref="TripleTerm"/>.
/// The predicate must be a <see cref="NamedNode"/>.
/// The object can be any <see cref="RdfTerm"/>.
/// The graph can be a <see cref="NamedNode"/>, <see cref="BlankNode"/>, or <c>null</c>
/// for the default graph.
/// </para>
/// <para>
/// For source-tracked emission — where each quad carries a reference
/// to the producer-assigned position it came from — see
/// <see cref="Lumoin.Veritas.Core.Sourcing.EmittedQuad"/>. Producers
/// that participate in provenance, witness-chain construction, or
/// cross-replica synchronisation emit <c>EmittedQuad</c> wrappers
/// alongside the bare <see cref="Quad"/> stream.
/// </para>
/// </remarks>
/// <param name="Subject">The subject of the statement. Must be a named node, blank node, or triple term.</param>
/// <param name="Predicate">The predicate of the statement. Must be a named node.</param>
/// <param name="Object">The object of the statement. Can be any RDF term.</param>
/// <param name="Graph">The named graph, or <c>null</c> for the default graph.</param>
[DebuggerDisplay("{Subject} {Predicate} {Object} {Graph}")]
public sealed record Quad(RdfTerm Subject, NamedNode Predicate, RdfTerm Object, RdfTerm? Graph = null)
{
    /// <summary>
    /// Validates that the quad conforms to the RDF data model.
    /// </summary>
    /// <returns><c>true</c> if the subject is a named node, blank node, or triple term; otherwise, <c>false</c>.</returns>
    public bool IsValid()
    {
        return Subject is NamedNode or BlankNode or TripleTerm;
    }

    /// <summary>
    /// Encodes this quad into <see cref="EncodedQuad"/> form using the given <see cref="TermDictionary"/>.
    /// </summary>
    /// <param name="dictionary">The dictionary that maps terms to <see cref="TermId"/> handles.</param>
    /// <returns>An <see cref="EncodedQuad"/> with encoded identifiers for each position.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public EncodedQuad Encode(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        TermId subject = dictionary.GetOrAdd(Subject);
        //Using the kind-specific overload avoids re-resolving to validate "is it a NamedNode?".
        IriId predicate = dictionary.GetOrAdd(Predicate);
        TermId @object = dictionary.GetOrAdd(Object);
        TermId graph = Graph is not null ? dictionary.GetOrAdd(Graph) : EncodedQuad.DefaultGraph;

        return new EncodedQuad(subject, predicate, @object, graph);
    }

    /// <summary>
    /// Decodes an <see cref="EncodedQuad"/> back to a <see cref="Quad"/> using the given dictionary.
    /// </summary>
    /// <param name="encoded">The encoded quad.</param>
    /// <param name="dictionary">The dictionary that maps identifiers back to terms.</param>
    /// <returns>A <see cref="Quad"/> with full <see cref="RdfTerm"/> references.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static Quad Decode(EncodedQuad encoded, TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        RdfTerm subject = dictionary.Resolve(encoded.Subject);
        NamedNode predicate = (NamedNode)dictionary.Resolve(encoded.Predicate);
        RdfTerm @object = dictionary.Resolve(encoded.Object);
        RdfTerm? graph = encoded.Graph != EncodedQuad.DefaultGraph
            ? dictionary.Resolve(encoded.Graph)
            : null;

        return new Quad(subject, predicate, @object, graph);
    }
}
