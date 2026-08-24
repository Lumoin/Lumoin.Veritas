using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// An unordered pair of data properties asserted mutually disjoint — the two
/// spaces share no value, so no single data value may be a value of both.
/// </summary>
/// <param name="First">The first property IRI of the pair.</param>
/// <param name="Second">The second property IRI of the pair.</param>
internal readonly record struct DisjointDataPropertyPair(Utf8String First, Utf8String Second);

/// <summary>
/// The per-module data-property RBox: the reflexive-transitive super-property
/// closure, the functional properties, the disjoint property pairs, and the
/// asserted range list per property. Built once from a module's data-property
/// axioms and consumed by <see cref="DataRestrictionConsistency"/> to decide the
/// concrete-domain obligations a node carries against the property hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// The box is immutable once built. It closes only the data-property axiom
/// types that shape value obligations — <c>SubDataPropertyOf</c>,
/// <c>EquivalentDataProperties</c> (as sub-property edges in both directions),
/// <c>FunctionalDataProperty</c>, <c>DisjointDataProperties</c>, and
/// <c>DataPropertyRange</c> — and ignores every other axiom.
/// </para>
/// <para>
/// The <see cref="Empty"/> singleton carries no closure, no functional
/// property, no disjoint pair, and no range: every accessor answers the
/// property-in-isolation case, so a sidecar call against the empty box reduces
/// to the same-property universal check with no hierarchy, pooling, or
/// disjointness constraint.
/// </para>
/// </remarks>
internal sealed class DataPropertyBox
{
    /// <summary>The transitive super-property set of each property, excluding the property itself.</summary>
    private Dictionary<Utf8String, HashSet<Utf8String>> StrictSupersByProperty { get; }

    /// <summary>The properties asserted functional.</summary>
    private HashSet<Utf8String> FunctionalPropertySet { get; }

    /// <summary>The asserted range list of each property.</summary>
    private Dictionary<Utf8String, List<OwlDataRange>> RangesByProperty { get; }

    /// <summary>The unordered disjoint pairs, expanded pairwise over each <c>DisjointDataProperties</c> list.</summary>
    public IReadOnlyList<DisjointDataPropertyPair> DisjointPairs { get; }

    /// <summary>The properties asserted functional, for iteration over the functional pools.</summary>
    public IReadOnlyCollection<Utf8String> FunctionalProperties => FunctionalPropertySet;

    /// <summary>Constructs a box from its closed slots.</summary>
    /// <param name="strictSupersByProperty">The transitive super-property set of each property, excluding the property itself.</param>
    /// <param name="functionalPropertySet">The functional properties.</param>
    /// <param name="disjointPairs">The disjoint property pairs.</param>
    /// <param name="rangesByProperty">The asserted range list of each property.</param>
    private DataPropertyBox(Dictionary<Utf8String, HashSet<Utf8String>> strictSupersByProperty, HashSet<Utf8String> functionalPropertySet, List<DisjointDataPropertyPair> disjointPairs, Dictionary<Utf8String, List<OwlDataRange>> rangesByProperty)
    {
        StrictSupersByProperty = strictSupersByProperty;
        FunctionalPropertySet = functionalPropertySet;
        DisjointPairs = disjointPairs;
        RangesByProperty = rangesByProperty;
    }

    /// <summary>The box carrying no data-property axioms — every new sidecar path no-ops on it.</summary>
    public static DataPropertyBox Empty { get; } = new([], [], [], []);

    /// <summary>
    /// Builds the box from a module's axioms, walking exactly
    /// <c>SubDataPropertyOf</c>, <c>EquivalentDataProperties</c>,
    /// <c>FunctionalDataProperty</c>, <c>DisjointDataProperties</c>, and
    /// <c>DataPropertyRange</c>, then closing the sub-property edges to a
    /// transitive super-property set per property.
    /// </summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The immutable box; <see cref="Empty"/>-equivalent when the module carries no data-property axiom.</returns>
    public static DataPropertyBox Build(IReadOnlyList<OwlAxiom> axioms)
    {
        Dictionary<Utf8String, HashSet<Utf8String>> directSupers = [];
        HashSet<Utf8String> functional = [];
        List<DisjointDataPropertyPair> disjointPairs = [];
        Dictionary<Utf8String, List<OwlDataRange>> ranges = [];

        foreach(OwlAxiom axiom in axioms)
        {
            switch(axiom)
            {
                case OwlSubDataPropertyOfAxiom sub:
                {
                    AddEdge(directSupers, sub.SubProperty.Iri, sub.SuperProperty.Iri);

                    break;
                }

                case OwlEquivalentDataPropertiesAxiom equivalent:
                {
                    AddEdge(directSupers, equivalent.First.Iri, equivalent.Second.Iri);
                    AddEdge(directSupers, equivalent.Second.Iri, equivalent.First.Iri);

                    break;
                }

                case OwlFunctionalDataPropertyAxiom functionalAxiom:
                {
                    functional.Add(functionalAxiom.Property.Iri);

                    break;
                }

                case OwlDisjointDataPropertiesAxiom disjoint:
                {
                    AddDisjointPairs(disjoint.Operands, disjointPairs);

                    break;
                }

                case OwlDataPropertyRangeAxiom range:
                {
                    RangesOf(ranges, range.Property.Iri).Add(range.Range);

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        Dictionary<Utf8String, HashSet<Utf8String>> closure = CloseSupers(directSupers);

        return new DataPropertyBox(closure, functional, disjointPairs, ranges);
    }

    /// <summary>The transitive super-properties of a property, excluding the property itself.</summary>
    /// <param name="property">The property IRI.</param>
    /// <returns>The strict super-property set, empty when the property has no asserted super.</returns>
    public IReadOnlyCollection<Utf8String> StrictSupers(Utf8String property)
    {
        return StrictSupersByProperty.TryGetValue(property, out HashSet<Utf8String>? supers) ? supers : Array.Empty<Utf8String>();
    }

    /// <summary>
    /// Whether a candidate is <paramref name="property"/> itself or one of its
    /// transitive super-properties — the reflexive membership test <c>candidate ∈ Supers(property)</c>.
    /// </summary>
    /// <param name="property">The property whose super-set is tested.</param>
    /// <param name="candidate">The candidate super-property.</param>
    /// <returns><see langword="true"/> when <c>property ⊑* candidate</c>.</returns>
    public bool IsSuperOrSelf(Utf8String property, Utf8String candidate)
    {
        return property.Equals(candidate) || (StrictSupersByProperty.TryGetValue(property, out HashSet<Utf8String>? supers) && supers.Contains(candidate));
    }

    /// <summary>Whether a property is asserted functional.</summary>
    /// <param name="property">The property IRI.</param>
    /// <returns><see langword="true"/> when the property is functional.</returns>
    public bool IsFunctional(Utf8String property)
    {
        return FunctionalPropertySet.Contains(property);
    }

    /// <summary>
    /// The reflexive sub-property closure of a property — the property itself
    /// together with every property that has it as a transitive super-property
    /// (<c>d′ ⊑* property</c>). These are the sources a <c>DataPropertyDomain</c>
    /// on the property fires over: a demand on any of them types its owner with
    /// the domain class.
    /// </summary>
    /// <param name="property">The super-property IRI.</param>
    /// <param name="sourcesToAppendTo">The sub-closure sources, appended to; the property itself is always the first appended.</param>
    public void CollectSubClosureSources(Utf8String property, List<Utf8String> sourcesToAppendTo)
    {
        sourcesToAppendTo.Add(property);
        foreach(KeyValuePair<Utf8String, HashSet<Utf8String>> entry in StrictSupersByProperty)
        {
            if(entry.Value.Contains(property))
            {
                sourcesToAppendTo.Add(entry.Key);
            }
        }
    }

    /// <summary>The asserted ranges of a property.</summary>
    /// <param name="property">The property IRI.</param>
    /// <returns>The asserted range list, empty when the property has no asserted range.</returns>
    public IReadOnlyList<OwlDataRange> Ranges(Utf8String property)
    {
        return RangesByProperty.TryGetValue(property, out List<OwlDataRange>? ranges) ? ranges : Array.Empty<OwlDataRange>();
    }

    /// <summary>Records a direct sub-property edge <c>sub ⊑ super</c>.</summary>
    /// <param name="directSupers">The direct-super adjacency, mutated in place.</param>
    /// <param name="sub">The sub-property IRI.</param>
    /// <param name="super">The super-property IRI.</param>
    private static void AddEdge(Dictionary<Utf8String, HashSet<Utf8String>> directSupers, Utf8String sub, Utf8String super)
    {
        if(!directSupers.TryGetValue(sub, out HashSet<Utf8String>? supers))
        {
            supers = [];
            directSupers[sub] = supers;
        }

        supers.Add(super);
    }

    /// <summary>The mutable range bucket of a property, created on first contact.</summary>
    /// <param name="ranges">The per-property range index, mutated in place.</param>
    /// <param name="property">The property IRI.</param>
    /// <returns>The mutable range list.</returns>
    private static List<OwlDataRange> RangesOf(Dictionary<Utf8String, List<OwlDataRange>> ranges, Utf8String property)
    {
        if(!ranges.TryGetValue(property, out List<OwlDataRange>? list))
        {
            list = [];
            ranges[property] = list;
        }

        return list;
    }

    /// <summary>Expands an n-ary disjoint list into its unordered pairs.</summary>
    /// <param name="operands">The mutually disjoint properties.</param>
    /// <param name="pairsToAppendTo">The pair list, appended to.</param>
    private static void AddDisjointPairs(IReadOnlyList<NamedNode> operands, List<DisjointDataPropertyPair> pairsToAppendTo)
    {
        for(int first = 0; first < operands.Count; first++)
        {
            for(int second = first + 1; second < operands.Count; second++)
            {
                pairsToAppendTo.Add(new DisjointDataPropertyPair(operands[first].Iri, operands[second].Iri));
            }
        }
    }

    /// <summary>
    /// Closes the direct-super adjacency to a transitive super-property set per
    /// property by an iterative worklist over each source, excluding the source
    /// itself so an equivalence cycle does not list a property as its own super.
    /// </summary>
    /// <param name="directSupers">The direct-super adjacency.</param>
    /// <returns>The transitive strict-super closure.</returns>
    private static Dictionary<Utf8String, HashSet<Utf8String>> CloseSupers(Dictionary<Utf8String, HashSet<Utf8String>> directSupers)
    {
        Dictionary<Utf8String, HashSet<Utf8String>> closure = [];
        foreach(KeyValuePair<Utf8String, HashSet<Utf8String>> entry in directSupers)
        {
            Utf8String source = entry.Key;
            HashSet<Utf8String> reached = [];
            Stack<Utf8String> work = new(entry.Value);
            while(work.Count > 0)
            {
                Utf8String current = work.Pop();
                if(current.Equals(source) || !reached.Add(current))
                {
                    continue;
                }

                if(directSupers.TryGetValue(current, out HashSet<Utf8String>? nexts))
                {
                    foreach(Utf8String next in nexts)
                    {
                        if(!next.Equals(source) && !reached.Contains(next))
                        {
                            work.Push(next);
                        }
                    }
                }
            }

            closure[source] = reached;
        }

        return closure;
    }
}
