using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Loader-scoped state handed to <see cref="ShapePopulation.PopulateAsync"/>
/// for every shape in the load. Assembled once by
/// <see cref="ShapeLoader"/> and reused across every per-shape
/// invocation, so the cost of vocabulary interning and constraint-registry
/// indexing is paid once per load rather than once per shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Storage-agnostic.</b> The context does not reference any
/// particular triple-store implementation. Population queries storage
/// only through a <see cref="StorageDelegates.MatchTriplesAsync"/>
/// delegate passed alongside the context; the context itself carries
/// only pre-resolved identifiers, lookup maps, and shared memoization
/// state. A future hypertrie-backed store changes the
/// <c>MatchTriplesAsync</c> implementation and does not touch the
/// context or the population routine.
/// </para>
/// <para>
/// <b>Constraint-component indexing.</b>
/// <see cref="ComponentsByPrimaryParameter"/> is built from the
/// caller-supplied constraint registry by translating each
/// <see cref="ConstraintComponentInfo.PrimaryParameter"/>
/// <see cref="Utf8String"/> into an <see cref="IriId"/> via the shared
/// <see cref="Dictionary"/>. The resulting map lets the population
/// routine dispatch factories by integer-key lookup rather than
/// byte-content string compare on every triple's predicate.
/// <see cref="KnownParameterIds"/> is the flat set of all parameter
/// ids known to the registry (primary and companion), used to
/// short-circuit the classification loop: triples whose predicate
/// isn't in the set aren't constraint-parameter triples and can
/// skip the factory-lookup path.
/// </para>
/// </remarks>
internal sealed class ShapePopulationContext
{
    /// <summary>
    /// Initializes a fully-resolved population context.
    /// </summary>
    public ShapePopulationContext(
        TermDictionary dictionary,
        ShaclPopulationIds shaclIds,
        RdfListIds rdfListIds,
        PathVocabularyIds pathVocabulary,
        RdfsVocabularyIds rdfsVocabulary,
        IReadOnlyDictionary<IriId, ConstraintComponentInfo> componentsByPrimaryParameter,
        IReadOnlyDictionary<IriId, ImmutableArray<IriId>> companionParametersByPrimaryParameter,
        IReadOnlySet<IriId> knownParameterIds,
        ShapeLoaderOptions options,
        ConcurrentDictionary<(string, string?, bool), Regex> patternMemo)
    {
        Dictionary = dictionary;
        ShaclIds = shaclIds;
        RdfListIds = rdfListIds;
        PathVocabulary = pathVocabulary;
        RdfsVocabulary = rdfsVocabulary;
        ComponentsByPrimaryParameter = componentsByPrimaryParameter;
        CompanionParametersByPrimaryParameter = companionParametersByPrimaryParameter;
        KnownParameterIds = knownParameterIds;
        Options = options;
        PatternMemo = patternMemo;
    }

    /// <summary>Shared term dictionary.</summary>
    public TermDictionary Dictionary { get; }

    /// <summary>SHACL vocabulary ids for population.</summary>
    public ShaclPopulationIds ShaclIds { get; }

    /// <summary>RDF-list vocabulary ids for walking parameter-valued lists.</summary>
    public RdfListIds RdfListIds { get; }

    /// <summary>Path-operator vocabulary ids for the path parser.</summary>
    public PathVocabularyIds PathVocabulary { get; }

    /// <summary>RDFS vocabulary ids carried through to each parameter bag.</summary>
    public RdfsVocabularyIds RdfsVocabulary { get; }

    /// <summary>
    /// Map from primary-parameter <see cref="IriId"/> to the
    /// <see cref="ConstraintComponentInfo"/> that declares it. Used by
    /// the population routine to dispatch factories for each
    /// primary-parameter occurrence.
    /// </summary>
    public IReadOnlyDictionary<IriId, ConstraintComponentInfo> ComponentsByPrimaryParameter { get; }

    /// <summary>
    /// Map from primary-parameter <see cref="IriId"/> to the pre-resolved
    /// companion-parameter <see cref="IriId"/> list for that component.
    /// Does not include the primary itself. Lets the population routine
    /// build a companion view for each factory invocation by integer-id
    /// iteration only, with no per-invocation interning.
    /// </summary>
    public IReadOnlyDictionary<IriId, ImmutableArray<IriId>> CompanionParametersByPrimaryParameter { get; }

    /// <summary>
    /// Set of every parameter <see cref="IriId"/> known to the
    /// constraint registry (primary or companion). The population
    /// routine uses this to separate constraint-parameter triples from
    /// every other kind of triple during classification.
    /// </summary>
    public IReadOnlySet<IriId> KnownParameterIds { get; }

    /// <summary>Per-load options forwarded to every parameter bag.</summary>
    public ShapeLoaderOptions Options { get; }

    /// <summary>Shared regex-compilation memo forwarded to every parameter bag.</summary>
    public ConcurrentDictionary<(string, string?, bool), Regex> PatternMemo { get; }
}
