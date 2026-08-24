using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// The encoded identifiers of the RDF and RDFS vocabulary terms needed to drive
/// RDFS inference. Resolve these once against a <see cref="Core.TermDictionary"/>
/// and reuse the struct across inference calls.
/// </summary>
/// <remarks>
/// <para>
/// Passed to <see cref="RdfsInference.InferAsync(RdfsVocabularyIds, Core.StorageDelegates.MatchTriplesAsync, System.Threading.CancellationToken)"/>.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-schema/">RDF 1.2 Schema</see>.
/// </para>
/// <para>
/// Each member is typed as <see cref="IriId"/> rather than a generic
/// <see cref="TermId"/> because RDF and RDFS vocabulary terms are IRIs
/// by definition. Obtain the values through
/// <see cref="Core.TermDictionary.GetOrAdd(Core.NamedNode)"/>, which returns
/// <see cref="IriId"/> directly.
/// </para>
/// </remarks>
/// <param name="RdfType">The encoded identifier for <c>rdf:type</c>.</param>
/// <param name="RdfsSubClassOf">The encoded identifier for <c>rdfs:subClassOf</c>.</param>
/// <param name="RdfsSubPropertyOf">The encoded identifier for <c>rdfs:subPropertyOf</c>.</param>
/// <param name="RdfsDomain">The encoded identifier for <c>rdfs:domain</c>.</param>
/// <param name="RdfsRange">The encoded identifier for <c>rdfs:range</c>.</param>
public readonly record struct RdfsVocabularyIds(
    IriId RdfType,
    IriId RdfsSubClassOf,
    IriId RdfsSubPropertyOf,
    IriId RdfsDomain,
    IriId RdfsRange);
