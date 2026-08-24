using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// The TBox extracted from a triple set, with its closures
/// precomputed: per-property strict superproperty sets, effective
/// domain and range typings (closed over both the property and the
/// class hierarchies), and per-class strict superclass sets.
/// Loaded fully in memory — schemas are small relative to the
/// instance data they describe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closure semantics.</b> "Strict" sets exclude the key itself:
/// <see cref="SuperClassesOf"/> for <c>c</c> yields every
/// <c>d ≠ c</c> with <c>c rdfs:subClassOf+ d</c>. The effective
/// domain types of a property <c>p</c> are every class <c>D′</c>
/// such that some <c>q</c> with <c>p rdfs:subPropertyOf* q</c>
/// declares <c>rdfs:domain D</c> and <c>D rdfs:subClassOf* D′</c> —
/// so one lookup answers the composed rdfs7→rdfs2→rdfs9 chain.
/// Ranges mirror domains.
/// </para>
/// <para>
/// <b>Cycles.</b> Subclass and subproperty cycles are legal RDFS;
/// the closure walks deduplicate on a visited set, so a cycle
/// yields each member of the strongly connected component (other
/// than the key itself) exactly once and terminates.
/// </para>
/// <para>
/// Lookups for terms the schema never mentions return empty — the
/// instance-data fast path costs one failed dictionary probe per
/// rule family.
/// </para>
/// </remarks>
[DebuggerDisplay("RdfsSchema Classes={strictSuperClasses.Count} Properties={strictSuperProperties.Count}")]
public sealed class RdfsSchema
{
    private static readonly TermId[] EmptyTerms = [];

    private readonly Dictionary<TermId, TermId[]> strictSuperClasses;

    private readonly Dictionary<TermId, TermId[]> strictSuperProperties;

    private readonly Dictionary<TermId, TermId[]> domainTypes;

    private readonly Dictionary<TermId, TermId[]> rangeTypes;

    /// <summary>The vocabulary term identifiers this schema was extracted with.</summary>
    public RdfsVocabularyTerms Terms { get; }

    /// <summary>
    /// <c>true</c> when the schema declares nothing — no subclass,
    /// subproperty, domain, or range statements were found, so
    /// derivation is a no-op.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            return strictSuperClasses.Count == 0
                && strictSuperProperties.Count == 0
                && domainTypes.Count == 0
                && rangeTypes.Count == 0;
        }
    }

    private RdfsSchema(
        RdfsVocabularyTerms terms,
        Dictionary<TermId, TermId[]> strictSuperClasses,
        Dictionary<TermId, TermId[]> strictSuperProperties,
        Dictionary<TermId, TermId[]> domainTypes,
        Dictionary<TermId, TermId[]> rangeTypes)
    {
        Terms = terms;
        this.strictSuperClasses = strictSuperClasses;
        this.strictSuperProperties = strictSuperProperties;
        this.domainTypes = domainTypes;
        this.rangeTypes = rangeTypes;
    }

    /// <summary>
    /// Every class strictly above <paramref name="class"/> in the
    /// subclass hierarchy; empty when the schema declares none.
    /// </summary>
    /// <param name="class">The class to look up.</param>
    /// <returns>The strict superclass set.</returns>
    public IReadOnlyList<TermId> SuperClassesOf(TermId @class)
    {
        return strictSuperClasses.TryGetValue(@class, out TermId[]? supers) ? supers : EmptyTerms;
    }

    /// <summary>
    /// Every property strictly above <paramref name="property"/> in
    /// the subproperty hierarchy; empty when the schema declares
    /// none.
    /// </summary>
    /// <param name="property">The property to look up.</param>
    /// <returns>The strict superproperty set.</returns>
    public IReadOnlyList<TermId> SuperPropertiesOf(TermId property)
    {
        return strictSuperProperties.TryGetValue(property, out TermId[]? supers) ? supers : EmptyTerms;
    }

    /// <summary>
    /// The effective domain typings of <paramref name="property"/> —
    /// every class a subject of the property is entailed to be an
    /// instance of, closed over the property and class hierarchies.
    /// </summary>
    /// <param name="property">The property to look up.</param>
    /// <returns>The closed domain-type set.</returns>
    public IReadOnlyList<TermId> DomainTypesOf(TermId property)
    {
        return domainTypes.TryGetValue(property, out TermId[]? types) ? types : EmptyTerms;
    }

    /// <summary>
    /// The effective range typings of <paramref name="property"/> —
    /// every class an object of the property is entailed to be an
    /// instance of, closed over the property and class hierarchies.
    /// </summary>
    /// <param name="property">The property to look up.</param>
    /// <returns>The closed range-type set.</returns>
    public IReadOnlyList<TermId> RangeTypesOf(TermId property)
    {
        return rangeTypes.TryGetValue(property, out TermId[]? types) ? types : EmptyTerms;
    }

    /// <summary>
    /// Extracts the TBox from <paramref name="triples"/> and
    /// computes its closures.
    /// </summary>
    /// <param name="triples">The triples to scan; schema statements are recognised by the predicates in <paramref name="terms"/>.</param>
    /// <param name="terms">The resolved vocabulary term identifiers.</param>
    /// <returns>The extracted schema with closures precomputed.</returns>
    public static RdfsSchema Extract(IEnumerable<EncodedTriple> triples, RdfsVocabularyTerms terms)
    {
        System.ArgumentNullException.ThrowIfNull(triples);

        Dictionary<TermId, List<TermId>> directSuperClasses = [];
        Dictionary<TermId, List<TermId>> directSuperProperties = [];
        Dictionary<TermId, List<TermId>> directDomains = [];
        Dictionary<TermId, List<TermId>> directRanges = [];

        foreach(EncodedTriple triple in triples)
        {
            TermId predicate = triple.Predicate;

            if(predicate == terms.SubClassOf)
            {
                AddEdge(directSuperClasses, triple.Subject, triple.Object);
            }
            else if(predicate == terms.SubPropertyOf)
            {
                AddEdge(directSuperProperties, triple.Subject, triple.Object);
            }
            else if(predicate == terms.Domain)
            {
                AddEdge(directDomains, triple.Subject, triple.Object);
            }
            else if(predicate == terms.Range)
            {
                AddEdge(directRanges, triple.Subject, triple.Object);
            }
        }

        Dictionary<TermId, TermId[]> superClasses = CloseTransitively(directSuperClasses);
        Dictionary<TermId, TermId[]> superProperties = CloseTransitively(directSuperProperties);
        Dictionary<TermId, TermId[]> domains = ComposeEffectiveTypes(directDomains, superProperties, superClasses);
        Dictionary<TermId, TermId[]> ranges = ComposeEffectiveTypes(directRanges, superProperties, superClasses);

        return new RdfsSchema(terms, superClasses, superProperties, domains, ranges);
    }

    private static void AddEdge(Dictionary<TermId, List<TermId>> edges, TermId from, TermId to)
    {
        if(!edges.TryGetValue(from, out List<TermId>? targets))
        {
            targets = [];
            edges[from] = targets;
        }

        targets.Add(to);
    }

    //Computes the strict transitive closure of every key in the
    //direct-edge map via an explicit work-list walk — no recursion.
    //A key's closure excludes the key itself unless a cycle leads
    //back to it through another node, in which case the key still
    //does not appear (visited-set seeding removes it).
    private static Dictionary<TermId, TermId[]> CloseTransitively(Dictionary<TermId, List<TermId>> directEdges)
    {
        Dictionary<TermId, TermId[]> closure = new(directEdges.Count);
        HashSet<TermId> visited = [];
        Stack<TermId> worklist = new();
        List<TermId> reached = [];

        foreach(KeyValuePair<TermId, List<TermId>> entry in directEdges)
        {
            visited.Clear();
            worklist.Clear();
            reached.Clear();

            visited.Add(entry.Key);

            for(int i = 0; i < entry.Value.Count; i++)
            {
                if(visited.Add(entry.Value[i]))
                {
                    worklist.Push(entry.Value[i]);
                    reached.Add(entry.Value[i]);
                }
            }

            while(worklist.Count > 0)
            {
                TermId current = worklist.Pop();

                if(!directEdges.TryGetValue(current, out List<TermId>? next))
                {
                    continue;
                }

                for(int i = 0; i < next.Count; i++)
                {
                    if(visited.Add(next[i]))
                    {
                        worklist.Push(next[i]);
                        reached.Add(next[i]);
                    }
                }
            }

            closure[entry.Key] = [.. reached];
        }

        return closure;
    }

    //Builds the effective domain (or range) typing per property:
    //the union of the direct declarations on the property and on
    //every strict superproperty, with each declared class expanded
    //through the class closure.
    private static Dictionary<TermId, TermId[]> ComposeEffectiveTypes(
        Dictionary<TermId, List<TermId>> directDeclarations,
        Dictionary<TermId, TermId[]> superProperties,
        Dictionary<TermId, TermId[]> superClasses)
    {
        //Every property that either declares directly or inherits a
        //declaration from a superproperty participates.
        HashSet<TermId> properties = [.. directDeclarations.Keys];

        foreach(KeyValuePair<TermId, TermId[]> entry in superProperties)
        {
            for(int i = 0; i < entry.Value.Length; i++)
            {
                if(directDeclarations.ContainsKey(entry.Value[i]))
                {
                    properties.Add(entry.Key);

                    break;
                }
            }
        }

        Dictionary<TermId, TermId[]> result = new(properties.Count);
        HashSet<TermId> effective = [];

        foreach(TermId property in properties)
        {
            effective.Clear();

            CollectDeclaredTypes(directDeclarations, superClasses, property, effective);

            if(superProperties.TryGetValue(property, out TermId[]? supers))
            {
                for(int i = 0; i < supers.Length; i++)
                {
                    CollectDeclaredTypes(directDeclarations, superClasses, supers[i], effective);
                }
            }

            result[property] = [.. effective];
        }

        return result;
    }

    //Adds the classes `declarer` declares (plus their strict
    //superclasses) to the effective set.
    private static void CollectDeclaredTypes(
        Dictionary<TermId, List<TermId>> directDeclarations,
        Dictionary<TermId, TermId[]> superClasses,
        TermId declarer,
        HashSet<TermId> effective)
    {
        if(!directDeclarations.TryGetValue(declarer, out List<TermId>? declared))
        {
            return;
        }

        for(int i = 0; i < declared.Count; i++)
        {
            TermId @class = declared[i];
            effective.Add(@class);

            if(superClasses.TryGetValue(@class, out TermId[]? supers))
            {
                for(int j = 0; j < supers.Length; j++)
                {
                    effective.Add(supers[j]);
                }
            }
        }
    }
}
