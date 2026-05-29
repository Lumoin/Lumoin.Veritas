using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Loads a SHACL shape graph into a <see cref="ShapeRegistry"/>. The
/// entry point for the SHACL loader pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pipeline.</b> The loader runs two passes over the shape graph:
/// </para>
/// <list type="number">
///   <item><description><b>Discovery</b> (<see cref="ShapeDiscovery.DiscoverAsync"/>): identifies every shape in the graph and classifies each as a node shape or property shape. Returns a mutable <see cref="ShapeBuilder"/> per shape.</description></item>
///   <item><description><b>Population</b> (<see cref="ShapePopulation.PopulateAsync"/>): for each builder, walks the shape's triples, classifies them into targets / metadata / constraint parameters, resolves any RDF lists referenced from constraint parameters, and invokes the matching constraint-component factories. Fills in the builder in place.</description></item>
/// </list>
/// <para>
/// After both passes complete, each builder is sealed via
/// <see cref="ShapeBuilder.Build"/> and added to a
/// <see cref="Dictionary{TKey, TValue}"/> keyed by shape id. The
/// dictionary is wrapped as a <see cref="ShapeRegistry"/> and returned.
/// </para>
/// <para>
/// <b>Storage-agnostic.</b> The only storage touchpoint is the
/// <see cref="StorageDelegates.MatchTriplesAsync"/> delegate. The
/// loader works identically against the current sorted-array
/// <c>InMemoryGraphStore</c> and against any future hypertrie-backed
/// store: both satisfy the same three-position access-pattern
/// contract.
/// </para>
/// <para>
/// <b>Extensibility.</b> <paramref name="LoadAsync"/> accepts a
/// <see cref="IReadOnlyList{ConstraintComponentInfo}"/> of registered
/// constraint components. By default callers pass
/// <see cref="ShaclBuiltInComponents.All"/>; they can extend this
/// with custom typed constraints or runtime-defined
/// <see cref="Constraints.DynamicConstraint"/> components by appending
/// additional entries to the list.
/// </para>
/// </remarks>
public static class ShapeLoader
{
    /// <summary>
    /// Loads every shape reachable in the shape graph and returns a
    /// populated <see cref="ShapeRegistry"/>.
    /// </summary>
    /// <param name="shapeGraphMatch">
    /// Triple-match delegate over the shape graph. Single-pattern
    /// queries only; no joins.
    /// </param>
    /// <param name="dictionary">
    /// Shared term dictionary. The loader interns every SHACL
    /// vocabulary IRI and every registered parameter IRI into this
    /// dictionary before discovery begins.
    /// </param>
    /// <param name="registeredComponents">
    /// The constraint components recognized by this load. Use
    /// <see cref="ShaclBuiltInComponents.All"/> for standard SHACL 1.2,
    /// and append custom or dynamic components as needed.
    /// </param>
    /// <param name="options">
    /// Optional per-load configuration (regex resolver and so on).
    /// Defaults to a fresh <see cref="ShapeLoaderOptions"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A fully-populated shape registry.</returns>
    public static async Task<ShapeRegistry> LoadAsync(
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        IReadOnlyList<ConstraintComponentInfo> registeredComponents,
        ShapeLoaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapeGraphMatch);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(registeredComponents);

        options ??= new ShapeLoaderOptions();

        //Resolve every vocabulary IRI we'll need into the term
        //dictionary. These are the only dictionary-mutating operations
        //in the load pipeline; everything downstream uses only
        //id-keyed lookups.
        ShaclDiscoveryIds discoveryIds = BuildDiscoveryIds(dictionary);
        ShaclPopulationIds populationIds = BuildPopulationIds(dictionary);
        RdfListIds rdfListIds = BuildRdfListIds(dictionary);
        PathVocabularyIds pathVocabulary = BuildPathVocabularyIds(dictionary);
        RdfsVocabularyIds rdfsVocabulary = BuildRdfsVocabularyIds(dictionary);

        //Discover SPARQL-based constraint components (SHACL-SPARQL §6) declared in the shape graph and register
        //them alongside the built-ins, so a shape providing a component's parameters gets a
        //SparqlComponentConstraint. Their parameter paths join the known constraint-parameter ids below.
        IReadOnlyList<ConstraintComponentInfo> discoveredComponents = await SparqlComponentLoader.DiscoverAsync(
            shapeGraphMatch, dictionary, populationIds, cancellationToken).ConfigureAwait(false);
        List<ConstraintComponentInfo> allComponents = [.. registeredComponents, .. discoveredComponents];

        //Index the registered components by primary parameter id and
        //build the flat set of every parameter id the loader should
        //recognize as a constraint parameter.
        (IReadOnlyDictionary<IriId, ConstraintComponentInfo> componentsByPrimary,
         IReadOnlyDictionary<IriId, ImmutableArray<IriId>> companionsByPrimary,
         IReadOnlySet<IriId> knownParameterIds,
         IReadOnlyList<IriId> allConstraintParameterIds) = IndexComponents(dictionary, allComponents);

        //Discovery. Discovery's vocabulary struct is minimal — just
        //rdf:type — whereas the full RdfsVocabularyIds carries the
        //broader set used by population-time factories. Wrap.
        RdfsDiscoveryIds rdfsDiscoveryIds = new(rdfsVocabulary.RdfType);

        //sh:sparql is not a registered constraint-component primary (its constraint is parsed from a
        //sub-graph, not a parameter scalar — see ShapePopulation), so it is absent from
        //allConstraintParameterIds. Add it here so a node bearing sh:sparql is discovered as a shape.
        List<IriId> discoveryConstraintIds = [.. allConstraintParameterIds, populationIds.Sparql];

        Dictionary<TermId, ShapeBuilder> builders = await ShapeDiscovery.DiscoverAsync(
            shapeGraphMatch,
            discoveryIds,
            discoveryConstraintIds,
            rdfsDiscoveryIds,
            rdfListIds,
            cancellationToken).ConfigureAwait(false);

        //Population. One bag, one factory invocation, once per primary
        //occurrence; shared per-load regex memo across all bags.
        ConcurrentDictionary<(string, string?, bool), Regex> patternMemo = new();

        ShapePopulationContext context = new(
            dictionary,
            populationIds,
            rdfListIds,
            pathVocabulary,
            rdfsVocabulary,
            componentsByPrimary,
            companionsByPrimary,
            knownParameterIds,
            options,
            patternMemo);

        foreach(ShapeBuilder builder in builders.Values)
        {
            await ShapePopulation.PopulateAsync(
                builder,
                shapeGraphMatch,
                context,
                cancellationToken).ConfigureAwait(false);
        }

        //Seal.
        Dictionary<TermId, Shape> shapes = [];
        foreach(ShapeBuilder builder in builders.Values)
        {
            shapes[builder.Id] = builder.Build();
        }

        return ShapeRegistry.FromDictionary(shapes);
    }

    private static ShaclDiscoveryIds BuildDiscoveryIds(TermDictionary dictionary)
    {
        IriId path = dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Path));
        IriId nodeShape = dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.NodeShape));
        IriId propertyShape = dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.PropertyShape));
        IriId shape = dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Shape));
        IriId shapeClass = dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.ShapeClass));
        IriId propertyPredicate = dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Property));

        List<IriId> targetPredicates =
        [
            dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetClass)),
            dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetNode)),
            dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetSubjectsOf)),
            dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetObjectsOf)),
        ];

        List<IriId> shapeReferencePredicates =
        [
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Node)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Not)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.And)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Or)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Xone)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.QualifiedValueShape)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.ReifierShape)),
            dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.MemberShape)),
        ];

        return new ShaclDiscoveryIds(
            path,
            nodeShape,
            propertyShape,
            shape,
            shapeClass,
            propertyPredicate,
            targetPredicates,
            shapeReferencePredicates);
    }

    private static ShaclPopulationIds BuildPopulationIds(TermDictionary dictionary)
    {
        return new ShaclPopulationIds(
            Path: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Path)),
            TargetClass: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetClass)),
            TargetNode: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetNode)),
            TargetSubjectsOf: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetSubjectsOf)),
            TargetObjectsOf: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetObjectsOf)),
            Severity: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Severity)),
            Deactivated: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Deactivated)),
            Message: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Message)),
            Violation: dictionary.GetOrAdd(new NamedNode(ShaclSeverityVocabulary.Violation)),
            Warning: dictionary.GetOrAdd(new NamedNode(ShaclSeverityVocabulary.Warning)),
            Info: dictionary.GetOrAdd(new NamedNode(ShaclSeverityVocabulary.Info)),
            Sparql: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Sparql)),
            Select: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Select)),
            Prefixes: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Prefixes)),
            Declare: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Declare)),
            Prefix: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Prefix)),
            Namespace: dictionary.GetOrAdd(new NamedNode(ShaclConstraintVocabulary.Namespace)),
            RdfsClass: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Class)));
    }

    private static RdfListIds BuildRdfListIds(TermDictionary dictionary)
    {
        IriId first = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.First));
        IriId rest = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Rest));
        TermId nil = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Nil));

        return new RdfListIds(first, rest, nil);
    }

    private static PathVocabularyIds BuildPathVocabularyIds(TermDictionary dictionary)
    {
        return new PathVocabularyIds(
            InversePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.InversePath)),
            AlternativePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.AlternativePath)),
            ZeroOrMorePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.ZeroOrMorePath)),
            OneOrMorePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.OneOrMorePath)),
            ZeroOrOnePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.ZeroOrOnePath)));
    }

    private static RdfsVocabularyIds BuildRdfsVocabularyIds(TermDictionary dictionary)
    {
        return new RdfsVocabularyIds(
            RdfType: dictionary.GetOrAdd(new NamedNode(Vocabulary.Rdf.Type)),
            RdfsSubClassOf: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SubClassOf)),
            RdfsSubPropertyOf: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SubPropertyOf)),
            RdfsDomain: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Domain)),
            RdfsRange: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Range)));
    }

    private static (IReadOnlyDictionary<IriId, ConstraintComponentInfo>,
                    IReadOnlyDictionary<IriId, ImmutableArray<IriId>>,
                    IReadOnlySet<IriId>,
                    IReadOnlyList<IriId>)
        IndexComponents(
            TermDictionary dictionary,
            IReadOnlyList<ConstraintComponentInfo> registeredComponents)
    {
        Dictionary<IriId, ConstraintComponentInfo> componentsByPrimary = [];
        Dictionary<IriId, ImmutableArray<IriId>> companionsByPrimary = [];
        HashSet<IriId> knownParameterIds = [];

        foreach(ConstraintComponentInfo info in registeredComponents)
        {
            IriId primaryId = dictionary.GetOrAdd(new NamedNode(info.PrimaryParameter));

            //Skip duplicate registrations of the same component silently;
            //the last one wins. Callers who want strict-mode behaviour
            //can pre-check their list.
            componentsByPrimary[primaryId] = info;
            knownParameterIds.Add(primaryId);

            ImmutableArray<IriId>.Builder companions = ImmutableArray.CreateBuilder<IriId>();
            foreach(Utf8String parameterIri in info.AllParameters)
            {
                IriId parameterId = dictionary.GetOrAdd(new NamedNode(parameterIri));
                knownParameterIds.Add(parameterId);

                if(!parameterId.Equals(primaryId))
                {
                    companions.Add(parameterId);
                }
            }

            companionsByPrimary[primaryId] = companions.ToImmutable();
        }

        //Discovery wants the list of primary constraint-parameter ids
        //so it can search for shapes by constraint-parameter occurrences.
        //Companion parameters never appear alone on a shape (they
        //always accompany a primary), so including them here would
        //just duplicate discovery hits.
        List<IriId> primaryConstraintParameterIds = [.. componentsByPrimary.Keys];

        return (componentsByPrimary, companionsByPrimary, knownParameterIds, primaryConstraintParameterIds);
    }
}
