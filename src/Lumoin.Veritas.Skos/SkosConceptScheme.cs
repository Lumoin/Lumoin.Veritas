using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Skos;

/// <summary>
/// A SKOS concept scheme loaded from a graph store, providing efficient access
/// to concepts, labels, and hierarchical relations.
/// </summary>
/// <remarks>
/// <para>
/// A concept scheme is an aggregation of one or more SKOS concepts. This type
/// loads a scheme from the graph via the <see cref="StorageDelegates.MatchTriplesAsync"/>
/// delegate and builds in-memory indices for fast traversal without repeated
/// graph queries.
/// </para>
/// <para>
/// The scheme is immutable after loading. For live graph data, reload using
/// <see cref="LoadAsync"/>.
/// </para>
/// <para>
/// Identifiers are typed as <see cref="TermId"/> rather than raw
/// <see cref="long"/>. SKOS concepts and vocabulary predicates are IRIs in
/// well-formed data, but this loader does not validate the kind of each term —
/// it takes whatever appears as subject/object of the relevant triples. The
/// typing choice reflects that defensive posture.
/// </para>
/// </remarks>
public sealed class SkosConceptScheme
{
    private readonly TermDictionary dictionary;
    private readonly TermId schemeId;

    //Concept set: all concept IDs belonging to this scheme.
    private readonly IReadOnlySet<TermId> conceptIds;

    //Broader relation index: concept ID → set of broader concept IDs.
    private readonly IReadOnlyDictionary<TermId, IReadOnlyList<TermId>> broader;

    //Narrower relation index: concept ID → set of narrower concept IDs.
    private readonly IReadOnlyDictionary<TermId, IReadOnlyList<TermId>> narrower;

    //Top concepts: concepts that are direct top concepts of the scheme.
    private readonly IReadOnlyList<TermId> topConceptIds;

    //Labels: concept ID → list of (predicate ID, literal) pairs.
    private readonly IReadOnlyDictionary<TermId, IReadOnlyList<(TermId PredicateId, Literal Label)>> labels;

    private SkosConceptScheme(
        TermDictionary dictionary,
        TermId schemeId,
        IReadOnlySet<TermId> conceptIds,
        IReadOnlyDictionary<TermId, IReadOnlyList<TermId>> broader,
        IReadOnlyDictionary<TermId, IReadOnlyList<TermId>> narrower,
        IReadOnlyList<TermId> topConceptIds,
        IReadOnlyDictionary<TermId, IReadOnlyList<(TermId PredicateId, Literal Label)>> labels)
    {
        this.dictionary = dictionary;
        this.schemeId = schemeId;
        this.conceptIds = conceptIds;
        this.broader = broader;
        this.narrower = narrower;
        this.topConceptIds = topConceptIds;
        this.labels = labels;
    }

    /// <summary>
    /// Gets the IRI of this concept scheme.
    /// </summary>
    public string SchemeIri => ((NamedNode)dictionary.Resolve(schemeId)).Iri.ToString();

    /// <summary>
    /// Gets the number of concepts in this scheme.
    /// </summary>
    public int ConceptCount => conceptIds.Count;

    /// <summary>
    /// Gets the top-level concepts of this scheme (those with no broader concept within the scheme).
    /// </summary>
    public IEnumerable<string> TopConcepts =>
        topConceptIds.Select(ResolveIri);

    /// <summary>
    /// Gets all concept IRIs in this scheme.
    /// </summary>
    public IEnumerable<string> Concepts =>
        conceptIds.Select(ResolveIri);

    /// <summary>
    /// Returns the broader concepts of the given concept IRI.
    /// </summary>
    /// <param name="conceptIri">The concept IRI to look up.</param>
    /// <returns>The IRIs of broader concepts, or an empty sequence if none.</returns>
    public IEnumerable<string> GetBroader(string conceptIri)
    {
        ArgumentNullException.ThrowIfNull(conceptIri);

        TermId conceptId = dictionary.GetIdOrDefault(new NamedNode(Utf8Strings.From(conceptIri)));
        if(conceptId.IsNone || !broader.TryGetValue(conceptId, out IReadOnlyList<TermId>? broaderIds))
        {
            return [];
        }

        return broaderIds.Select(ResolveIri);
    }

    /// <summary>
    /// Returns the narrower concepts of the given concept IRI.
    /// </summary>
    /// <param name="conceptIri">The concept IRI to look up.</param>
    /// <returns>The IRIs of narrower concepts, or an empty sequence if none.</returns>
    public IEnumerable<string> GetNarrower(string conceptIri)
    {
        ArgumentNullException.ThrowIfNull(conceptIri);

        TermId conceptId = dictionary.GetIdOrDefault(new NamedNode(Utf8Strings.From(conceptIri)));
        if(conceptId.IsNone || !narrower.TryGetValue(conceptId, out IReadOnlyList<TermId>? narrowerIds))
        {
            return [];
        }

        return narrowerIds.Select(ResolveIri);
    }

    /// <summary>Resolves a concept identifier to its IRI string.</summary>
    /// <param name="id">The term identifier to resolve.</param>
    /// <returns>The resolved IRI string.</returns>
    private string ResolveIri(TermId id)
    {
        return ((NamedNode)dictionary.Resolve(id)).Iri.ToString();
    }

    /// <summary>
    /// Returns all ancestors of the given concept by transitively following <c>skos:broader</c>.
    /// </summary>
    /// <param name="conceptIri">The concept IRI to start from.</param>
    /// <returns>All ancestor concept IRIs in breadth-first order, excluding the start concept.</returns>
    public IEnumerable<string> GetAllBroader(string conceptIri)
    {
        ArgumentNullException.ThrowIfNull(conceptIri);

        TermId startId = dictionary.GetIdOrDefault(new NamedNode(Utf8Strings.From(conceptIri)));
        if(startId.IsNone)
        {
            return [];
        }

        return TraverseTransitive(startId, broader);
    }

    /// <summary>
    /// Returns all descendants of the given concept by transitively following <c>skos:narrower</c>.
    /// </summary>
    /// <param name="conceptIri">The concept IRI to start from.</param>
    /// <returns>All descendant concept IRIs in breadth-first order, excluding the start concept.</returns>
    public IEnumerable<string> GetAllNarrower(string conceptIri)
    {
        ArgumentNullException.ThrowIfNull(conceptIri);

        TermId startId = dictionary.GetIdOrDefault(new NamedNode(Utf8Strings.From(conceptIri)));
        if(startId.IsNone)
        {
            return [];
        }

        return TraverseTransitive(startId, narrower);
    }

    /// <summary>
    /// Returns all preferred labels for the given concept, optionally filtered by language.
    /// </summary>
    /// <param name="conceptIri">The concept IRI.</param>
    /// <param name="languageTag">
    /// An optional BCP47 language tag to filter by (e.g. <c>"en"</c>, <c>"fi"</c>).
    /// When <c>null</c>, all preferred labels are returned.
    /// </param>
    /// <returns>The preferred label literals matching the filter.</returns>
    public IEnumerable<Literal> GetPrefLabels(string conceptIri, string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(conceptIri);
        return GetLabels(conceptIri, SkosVocabulary.Core.PrefLabel, languageTag);
    }

    /// <summary>
    /// Returns all alternative labels for the given concept, optionally filtered by language.
    /// </summary>
    /// <param name="conceptIri">The concept IRI.</param>
    /// <param name="languageTag">An optional BCP47 language tag filter.</param>
    /// <returns>The alternative label literals matching the filter.</returns>
    public IEnumerable<Literal> GetAltLabels(string conceptIri, string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(conceptIri);
        return GetLabels(conceptIri, SkosVocabulary.Core.AltLabel, languageTag);
    }

    /// <summary>
    /// Loads a SKOS concept scheme from the given graph store.
    /// </summary>
    /// <param name="schemeIri">The IRI of the concept scheme to load.</param>
    /// <param name="match">The match delegate for querying the graph.</param>
    /// <param name="dictionary">The term dictionary for the graph.</param>
    /// <param name="pool">Pool for interning UTF-8 term strings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The loaded concept scheme, or <c>null</c> if the scheme IRI is not found.</returns>
    public static async ValueTask<SkosConceptScheme?> LoadAsync(
        string schemeIri,
        StorageDelegates.MatchTriplesAsync match,
        TermDictionary dictionary,
        Utf8StringPool pool,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemeIri);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(pool);

        //Resolve the scheme IRI to its dictionary ID.
        NamedNode schemeNode = new(pool.Intern(schemeIri));
        TermId schemeId = dictionary.GetIdOrDefault(schemeNode);
        if(schemeId.IsNone)
        {
            return null;
        }

        //Resolve predicate IDs we need for graph queries.
        TermId inSchemeId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.InScheme.Span)));
        TermId hasTopConceptId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.HasTopConcept.Span)));
        TermId topConceptOfId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.TopConceptOf.Span)));
        TermId broaderId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.Broader.Span)));
        TermId narrowerId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.Narrower.Span)));
        TermId prefLabelId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.PrefLabel.Span)));
        TermId altLabelId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.AltLabel.Span)));
        TermId hiddenLabelId = dictionary.GetIdOrDefault(new NamedNode(pool.Intern(SkosVocabulary.Core.HiddenLabel.Span)));

        //Collect all concepts in the scheme via skos:inScheme.
        HashSet<TermId> conceptIds = [];
        if(!inSchemeId.IsNone)
        {
            await foreach(EncodedTriple triple in match(TermId.None, inSchemeId, schemeId, cancellationToken).ConfigureAwait(false))
            {
                conceptIds.Add(triple.Subject);
            }
        }

        //Collect top concepts via skos:hasTopConcept on the scheme.
        HashSet<TermId> topConceptIds = [];
        if(!hasTopConceptId.IsNone)
        {
            await foreach(EncodedTriple triple in match(schemeId, hasTopConceptId, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                topConceptIds.Add(triple.Object);
                conceptIds.Add(triple.Object);
            }
        }

        //Collect top concepts via skos:topConceptOf on concepts.
        if(!topConceptOfId.IsNone)
        {
            await foreach(EncodedTriple triple in match(TermId.None, topConceptOfId, schemeId, cancellationToken).ConfigureAwait(false))
            {
                topConceptIds.Add(triple.Subject);
                conceptIds.Add(triple.Subject);
            }
        }

        //Build broader/narrower indices restricted to concepts in this scheme.
        //Use HashSet values to deduplicate — graphs may have both skos:broader and skos:narrower
        //triples stating the same relation, and we derive the inverse from each direction too.
        Dictionary<TermId, HashSet<TermId>> broaderIndex = [];
        Dictionary<TermId, HashSet<TermId>> narrowerIndex = [];

        if(!broaderId.IsNone)
        {
            await foreach(EncodedTriple triple in match(TermId.None, broaderId, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                if(!conceptIds.Contains(triple.Subject))
                {
                    continue;
                }

                if(!broaderIndex.TryGetValue(triple.Subject, out HashSet<TermId>? broaderSet))
                {
                    broaderSet = [];
                    broaderIndex[triple.Subject] = broaderSet;
                }

                broaderSet.Add(triple.Object);

                //Derive the inverse narrower relation.
                if(!narrowerIndex.TryGetValue(triple.Object, out HashSet<TermId>? narrowerSet))
                {
                    narrowerSet = [];
                    narrowerIndex[triple.Object] = narrowerSet;
                }

                narrowerSet.Add(triple.Subject);
            }
        }

        if(!narrowerId.IsNone)
        {
            await foreach(EncodedTriple triple in match(TermId.None, narrowerId, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                if(!conceptIds.Contains(triple.Subject))
                {
                    continue;
                }

                if(!narrowerIndex.TryGetValue(triple.Subject, out HashSet<TermId>? narrowerSet))
                {
                    narrowerSet = [];
                    narrowerIndex[triple.Subject] = narrowerSet;
                }

                narrowerSet.Add(triple.Object);
            }
        }

        //Build label index.
        Dictionary<TermId, List<(TermId PredicateId, Literal Label)>> labelIndex = [];
        TermId[] labelPredicateIds = [prefLabelId, altLabelId, hiddenLabelId];

        foreach(TermId predicateId in labelPredicateIds)
        {
            if(predicateId.IsNone)
            {
                continue;
            }

            await foreach(EncodedTriple triple in match(TermId.None, predicateId, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                if(!conceptIds.Contains(triple.Subject))
                {
                    continue;
                }

                if(dictionary.Resolve(triple.Object) is not Literal literal)
                {
                    continue;
                }

                if(!labelIndex.TryGetValue(triple.Subject, out List<(TermId, Literal)>? labelList))
                {
                    labelList = [];
                    labelIndex[triple.Subject] = labelList;
                }

                labelList.Add((predicateId, literal));
            }
        }

        //Derive top concepts as those with no broader concept in the scheme if none were explicitly declared.
        if(topConceptIds.Count == 0)
        {
            foreach(TermId conceptId in conceptIds)
            {
                if(!broaderIndex.ContainsKey(conceptId))
                {
                    topConceptIds.Add(conceptId);
                }
            }
        }

        Dictionary<TermId, IReadOnlyList<TermId>> broaderReadOnly = new(broaderIndex.Count);
        foreach(KeyValuePair<TermId, HashSet<TermId>> entry in broaderIndex)
        {
            TermId[] values = new TermId[entry.Value.Count];
            entry.Value.CopyTo(values);
            broaderReadOnly[entry.Key] = values;
        }

        Dictionary<TermId, IReadOnlyList<TermId>> narrowerReadOnly = new(narrowerIndex.Count);
        foreach(KeyValuePair<TermId, HashSet<TermId>> entry in narrowerIndex)
        {
            TermId[] values = new TermId[entry.Value.Count];
            entry.Value.CopyTo(values);
            narrowerReadOnly[entry.Key] = values;
        }

        TermId[] topConceptArray = new TermId[topConceptIds.Count];
        topConceptIds.CopyTo(topConceptArray);

        Dictionary<TermId, IReadOnlyList<(TermId, Literal)>> labelReadOnly = new(labelIndex.Count);
        foreach(KeyValuePair<TermId, List<(TermId, Literal)>> entry in labelIndex)
        {
            labelReadOnly[entry.Key] = entry.Value.AsReadOnly();
        }

        return new SkosConceptScheme(
            dictionary,
            schemeId,
            conceptIds,
            broaderReadOnly,
            narrowerReadOnly,
            topConceptArray,
            labelReadOnly);
    }

    private IEnumerable<string> TraverseTransitive(
        TermId startId,
        IReadOnlyDictionary<TermId, IReadOnlyList<TermId>> index)
    {
        HashSet<TermId> visited = [startId];
        Queue<TermId> frontier = new();
        frontier.Enqueue(startId);

        while(frontier.Count > 0)
        {
            TermId current = frontier.Dequeue();
            if(!index.TryGetValue(current, out IReadOnlyList<TermId>? related))
            {
                continue;
            }

            foreach(TermId related1 in related)
            {
                if(visited.Add(related1))
                {
                    frontier.Enqueue(related1);
                    yield return ((NamedNode)dictionary.Resolve(related1)).Iri.ToString();
                }
            }
        }
    }

    private IEnumerable<Literal> GetLabels(string conceptIri, Utf8String predicateIri, string? languageTag)
    {
        TermId conceptId = dictionary.GetIdOrDefault(new NamedNode(Utf8Strings.From(conceptIri)));
        if(conceptId.IsNone || !labels.TryGetValue(conceptId, out IReadOnlyList<(TermId PredicateId, Literal Label)>? conceptLabels))
        {
            return [];
        }

        //Find the predicate ID by scanning term entries that match the IRI bytes.
        //External term identifiers are 1-based (0 reserved for TermId.None);
        //the scan walks the dictionary's external id range.
        uint predicateIdRaw = 0;
        for(uint i = 1; i <= (uint)dictionary.Count; i++)
        {
            if(dictionary.Resolve(i) is NamedNode node && node.Iri.Span.SequenceEqual(predicateIri.Span))
            {
                predicateIdRaw = i;
                break;
            }
        }

        if(predicateIdRaw == 0)
        {
            return [];
        }

        TermId capturedPredicateId = TermId.FromEncoded(predicateIdRaw);
        return FilterLabels(conceptLabels, capturedPredicateId, languageTag);
    }

    /// <summary>Yields the labels whose predicate matches <paramref name="predicateId"/> and whose language matches the optional <paramref name="languageTag"/>.</summary>
    /// <param name="conceptLabels">The concept's predicate-tagged labels.</param>
    /// <param name="predicateId">The label predicate to match.</param>
    /// <param name="languageTag">The language tag to match, or <see langword="null"/> for any language.</param>
    /// <returns>The matching labels, in stored order.</returns>
    private static IEnumerable<Literal> FilterLabels(IReadOnlyList<(TermId PredicateId, Literal Label)> conceptLabels, TermId predicateId, string? languageTag)
    {
        foreach((TermId PredicateId, Literal Label) entry in conceptLabels)
        {
            if(entry.PredicateId != predicateId)
            {
                continue;
            }

            Literal label = entry.Label;
            if(languageTag is null
                || (label.Language is { } lang && lang.ToString().Equals(languageTag, StringComparison.OrdinalIgnoreCase)))
            {
                yield return label;
            }
        }
    }
}
