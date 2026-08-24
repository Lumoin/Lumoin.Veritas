using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The Shape P window measurement the census-first recognizer's
/// pre-clausification pass reads on every partition-jurisdiction module —
/// computed with the anchor deduplication applied BEFORE any boundary
/// comparison, so the battery's near-miss rows can pin the measured quantity
/// independently of the comparison's outcome.
/// </summary>
/// <param name="AnchorCount">The distinct pairwise-disjoint anchors <c>m</c> the existential fillers name, deduplicated by class identity; zero when the jurisdiction rejected the module.</param>
/// <param name="RestrictionCount">The existential conjunct count <c>n</c> of the template intersection; zero when the jurisdiction rejected the module.</param>
/// <param name="CapBound">The unqualified max-cardinality conjunct's bound <c>k</c>; zero when the jurisdiction rejected the module.</param>
/// <param name="AnchorSilences">One when <see cref="AnchorCount"/> exceeded <see cref="ContextPartitionCountingDecider.PartitionAnchorBound"/> — a named silence, never a verdict over an unchecked anchor clique; zero otherwise.</param>
/// <param name="RestrictionSilences">One when <see cref="RestrictionCount"/> exceeded <see cref="ContextPartitionCountingDecider.PartitionRestrictionBound"/> — a named silence; zero otherwise.</param>
internal readonly record struct PartitionCountingWindow(
    int AnchorCount,
    int RestrictionCount,
    int CapBound,
    int AnchorSilences,
    int RestrictionSilences)
{
    /// <summary>The empty window: no partition template was admitted.</summary>
    public static PartitionCountingWindow Empty => default;
}

/// <summary>The Shape P decider's outcome: the closed-form verdict when every jurisdiction condition held inside the windows, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The closed-form verdict — <see langword="true"/> for the witnessed model, <see langword="false"/> for the pigeonhole refutation — or <see langword="null"/> when the face is silent on the module.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct PartitionCountingOutcome(bool? Consistent, PartitionCountingWindow Window)
{
    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static PartitionCountingOutcome SilentWith(PartitionCountingWindow window)
    {
        return new PartitionCountingOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's partition-counting faces (faces three
/// and four): a tier-1 CLOSED FORM over the told axiom surfaces of a
/// set-partition counting module — a named class equivalent to an intersection
/// of <c>n</c> existential restrictions and exactly one unqualified
/// max-cardinality restriction <c>k</c>, all over one named object property,
/// whose existential fillers each carry exactly one anchor drawn from a
/// COMPUTED pairwise-disjointness relation, over an ABox of one individual
/// typed by the defined class. With <c>m</c> the distinct anchors, <c>m &gt; k</c>
/// refutes every model by pigeonhole — the anchors' disjointness forces
/// <c>m</c> distinct successors above the told cap, no unique-name assumption
/// used — and <c>m &lt;= k</c> is witnessed by the explicit model that gives each
/// distinct anchor one successor and routes every compound requirement to its
/// anchor's successor. The partition space the ordinary saturation churns
/// against is never visited: no propagation, no enumeration, no partition
/// search. Sound-or-silent and told-only: the jurisdiction is a closed-world
/// whole-module admission, and ANY unmet condition — an unrecognized axiom
/// kind, a dual-anchor or anchor-free filler, a second or qualified cardinality
/// conjunct, a broken disjointness chain, a reused defined class, an ABox
/// extra — leaves the module to ordinary saturation. Every bound is a named
/// window constant; outside any bound the face is silent with the measured
/// numbers already on the record.
/// </summary>
internal static class ContextPartitionCountingDecider
{
    /// <summary>
    /// The anchor ceiling: the pairwise disjointness check is exact up to this
    /// many distinct anchors and SILENT above it. Derivation (engineering, with
    /// the cost formula the battery pins): the check compares at most
    /// C(16,2) = 120 anchor pairs against the told-link relation, and the value
    /// matches the counting faces' shared clique ceiling so every counting
    /// face outside the repairing family carries one boundary discipline; the
    /// repairing face carries its own wider windows sized by its habitat.
    /// Building the relation itself is one linear pass over the module's told
    /// chain axioms, bounded by the module rather than by this constant.
    /// </summary>
    public const int PartitionAnchorBound = 16;

    /// <summary>
    /// The existential-conjunct ceiling: the template admits at most this many
    /// existential restrictions and is SILENT above it. Derivation (empirical,
    /// corpus maximum with margin): the deepest measured template carries ten
    /// existential conjuncts, so the bound clears the corpus at zero cost and
    /// keeps both partition windows on the counting-family's shared sixteen
    /// discipline, distinct from the repairing face's own wider windows.
    /// </summary>
    public const int PartitionRestrictionBound = 16;

    /// <summary>Measures the Shape P census window without deciding anything: the distinct anchors, the existential conjunct count, the cap bound, and the two window-exceeded silences the bounds would charge — computed identically dark and lit, so the census ships unconditionally.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement; all-zero when the jurisdiction rejects the module.</returns>
    public static PartitionCountingOutcome Measure(ReasoningModule module)
    {
        return TryCollectTemplate(module, out PartitionTemplate? template)
            ? PartitionCountingOutcome.SilentWith(MeasureWindow(template))
            : PartitionCountingOutcome.SilentWith(PartitionCountingWindow.Empty);
    }

    /// <summary>
    /// Runs the partition-counting faces: the closed-world jurisdiction
    /// admission, the window checks, the pairwise anchor-disjointness check
    /// inside the anchor window, and then the single integer comparison that
    /// answers the module. The measurement lands first in every case, so a
    /// window or disjointness silence still carries the numbers.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: a closed-form verdict with its measurement, or silence.</returns>
    public static PartitionCountingOutcome Run(ReasoningModule module)
    {
        if(!TryCollectTemplate(module, out PartitionTemplate? template))
        {
            return PartitionCountingOutcome.SilentWith(PartitionCountingWindow.Empty);
        }

        PartitionCountingWindow window = MeasureWindow(template);
        if(window.AnchorSilences > 0 || window.RestrictionSilences > 0)
        {
            return PartitionCountingOutcome.SilentWith(window);
        }

        if(!AnchorsArePairwiseDisjoint(template))
        {
            return PartitionCountingOutcome.SilentWith(window);
        }

        return new PartitionCountingOutcome(template.Anchors.Count <= template.CapBound, window);
    }

    /// <summary>The admitted partition template: the deduplicated anchors in first-seen order, the computed symmetric disjointness relation the anchors are checked against, and the two told numbers the decision compares.</summary>
    /// <param name="Anchors">The distinct anchors <c>m</c>, one per existential filler, deduplicated in first-seen order.</param>
    /// <param name="Disjointness">The symmetric told-link disjointness relation over named classes.</param>
    /// <param name="RestrictionCount">The existential conjunct count <c>n</c>.</param>
    /// <param name="CapBound">The unqualified max-cardinality bound <c>k</c>.</param>
    private sealed record PartitionTemplate(
        IReadOnlyList<Utf8String> Anchors,
        IReadOnlyDictionary<Utf8String, HashSet<Utf8String>> Disjointness,
        int RestrictionCount,
        int CapBound);

    /// <summary>Reads the window off an admitted template: the three measured numbers and the two boundary silences the bounds charge.</summary>
    /// <param name="template">The admitted template.</param>
    /// <returns>The window measurement.</returns>
    private static PartitionCountingWindow MeasureWindow(PartitionTemplate template)
    {
        return new PartitionCountingWindow(
            template.Anchors.Count,
            template.RestrictionCount,
            template.CapBound,
            template.Anchors.Count > PartitionAnchorBound ? 1 : 0,
            template.RestrictionCount > PartitionRestrictionBound ? 1 : 0);
    }

    /// <summary>
    /// The closed-world jurisdiction admission over the module's ENTIRE axiom
    /// set, in one pass plus the template walk: exactly one class equivalence
    /// between a named class and an intersection, exactly one class assertion
    /// typing one individual to that class, told subclass axioms that are all
    /// disjointness links, and otherwise only declarations, imports, and
    /// annotation axioms — the non-logical content that asserts no class or
    /// property constraint. The template's conjuncts must be existential
    /// restrictions plus exactly one unqualified max-cardinality restriction
    /// over the SAME named object property, each existential filler a named
    /// class or an intersection of named classes carrying exactly one anchor,
    /// none of them the defined class. Anything else rejects.
    /// </summary>
    /// <param name="module">The module to admit or reject.</param>
    /// <param name="template">The admitted template; <see langword="null"/> on rejection.</param>
    /// <returns><see langword="true"/> when every jurisdiction condition outside the windows held.</returns>
    private static bool TryCollectTemplate(ReasoningModule module, [NotNullWhen(true)] out PartitionTemplate? template)
    {
        template = null;

        OwlEquivalentClassesAxiom? equivalence = null;
        OwlClassAssertionAxiom? assertion = null;
        int equivalences = 0;
        int assertions = 0;
        Dictionary<Utf8String, HashSet<Utf8String>> disjointness = [];
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                    or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom):
                {
                    break;
                }
                case(OwlEquivalentClassesAxiom candidate):
                {
                    equivalences++;
                    equivalence = candidate;
                    break;
                }
                case(OwlClassAssertionAxiom candidate):
                {
                    assertions++;
                    assertion = candidate;
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    if(!TryLinkDisjointness(subClass, disjointness))
                    {
                        return false;
                    }

                    break;
                }
                default:
                {
                    return false;
                }
            }
        }

        if(equivalences != 1 || assertions != 1 || equivalence is null || assertion is null)
        {
            return false;
        }

        if(!TrySplitTemplate(equivalence, out OwlClassReference? definedReference, out OwlObjectIntersectionOf? intersection))
        {
            return false;
        }

        Utf8String definedClass = definedReference.Class.Iri;
        if(disjointness.ContainsKey(definedClass))
        {
            return false;
        }

        if(assertion.Class is not OwlClassReference assertedClass || !assertedClass.Class.Iri.Equals(definedClass))
        {
            return false;
        }

        List<OwlClassExpression> fillers = [];
        if(!TryReadConjuncts(intersection, fillers, out int capBound))
        {
            return false;
        }

        List<Utf8String> anchors = [];
        HashSet<Utf8String> distinctAnchors = [];
        List<Utf8String> fillerAnchors = [];
        for(int i = 0; i < fillers.Count; i++)
        {
            fillerAnchors.Clear();
            if(!TryReadFillerAnchors(fillers[i], definedClass, disjointness, fillerAnchors) || fillerAnchors.Count != 1)
            {
                return false;
            }

            if(distinctAnchors.Add(fillerAnchors[0]))
            {
                anchors.Add(fillerAnchors[0]);
            }
        }

        template = new PartitionTemplate(anchors, disjointness, fillers.Count, capBound);

        return true;
    }

    /// <summary>Splits the told equivalence into its defined named class and its template intersection; an equivalence whose sides are not exactly one named non-constant class and one intersection rejects.</summary>
    /// <param name="equivalence">The told equivalence axiom.</param>
    /// <param name="definedClass">The defined named class; <see langword="null"/> on rejection.</param>
    /// <param name="intersection">The template intersection; <see langword="null"/> on rejection.</param>
    /// <returns><see langword="true"/> on the template equivalence shape.</returns>
    private static bool TrySplitTemplate(OwlEquivalentClassesAxiom equivalence, [NotNullWhen(true)] out OwlClassReference? definedClass, [NotNullWhen(true)] out OwlObjectIntersectionOf? intersection)
    {
        if(equivalence is { First: OwlClassReference first, Second: OwlObjectIntersectionOf secondSide } && ContextHabitatRecognizer.IsChainNodeClass(first))
        {
            definedClass = first;
            intersection = secondSide;

            return true;
        }

        if(equivalence is { First: OwlObjectIntersectionOf firstSide, Second: OwlClassReference second } && ContextHabitatRecognizer.IsChainNodeClass(second))
        {
            definedClass = second;
            intersection = firstSide;

            return true;
        }

        definedClass = null;
        intersection = null;

        return false;
    }

    /// <summary>Reads the template intersection's conjuncts: at least one existential restriction and EXACTLY ONE unqualified max-cardinality restriction with a nonnegative bound, every conjunct over the same named object property. A qualified cardinality, a second cardinality conjunct, a cardinality of another kind, an inverse role, and any other conjunct kind all reject.</summary>
    /// <param name="intersection">The template intersection.</param>
    /// <param name="fillersToAppendTo">The existential fillers, in conjunct order.</param>
    /// <param name="capBound">The cardinality bound <c>k</c>; zero on rejection.</param>
    /// <returns><see langword="true"/> when the conjuncts form the template.</returns>
    private static bool TryReadConjuncts(OwlObjectIntersectionOf intersection, List<OwlClassExpression> fillersToAppendTo, out int capBound)
    {
        capBound = 0;
        NamedNode? role = null;
        int caps = 0;
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            switch(intersection.Operands[i])
            {
                case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole } existential):
                {
                    if(!ContextHabitatRecognizer.SharesTemplateRole(ref role, existentialRole.Property))
                    {
                        return false;
                    }

                    fillersToAppendTo.Add(existential.Filler);
                    break;
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Property: OwlObjectPropertyReference capRole } cap):
                {
                    if(!ContextHabitatRecognizer.IsUnqualifiedFiller(cap.Filler) || !ContextHabitatRecognizer.SharesTemplateRole(ref role, capRole.Property))
                    {
                        return false;
                    }

                    caps++;
                    capBound = cap.Cardinality;
                    break;
                }
                default:
                {
                    return false;
                }
            }
        }

        if(fillersToAppendTo.Count == 0 || caps != 1 || capBound < 0)
        {
            capBound = 0;

            return false;
        }

        return true;
    }

    /// <summary>Reads one existential filler's anchors: the filler is a named class or an intersection of named classes, none of them the defined class or an OWL constant, and a class the computed disjointness relation mentions is an anchor. A nested restriction, a one-of, a complement, and every other filler shape reject.</summary>
    /// <param name="filler">The existential filler.</param>
    /// <param name="definedClass">The template's defined class, barred from every filler.</param>
    /// <param name="disjointness">The computed disjointness relation.</param>
    /// <param name="anchorsToAppendTo">The anchors this filler names.</param>
    /// <returns><see langword="true"/> when the filler lies within the admitted grammar.</returns>
    private static bool TryReadFillerAnchors(OwlClassExpression filler, Utf8String definedClass, IReadOnlyDictionary<Utf8String, HashSet<Utf8String>> disjointness, List<Utf8String> anchorsToAppendTo)
    {
        if(filler is OwlClassReference single)
        {
            return TryReadFillerClass(single, definedClass, disjointness, anchorsToAppendTo);
        }

        if(filler is not OwlObjectIntersectionOf intersection || intersection.Operands.Count == 0)
        {
            return false;
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(intersection.Operands[i] is not OwlClassReference member || !TryReadFillerClass(member, definedClass, disjointness, anchorsToAppendTo))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Admits one named class inside a filler and records it when the disjointness relation mentions it.</summary>
    /// <param name="reference">The filler's class reference.</param>
    /// <param name="definedClass">The template's defined class, barred from every filler.</param>
    /// <param name="disjointness">The computed disjointness relation.</param>
    /// <param name="anchorsToAppendTo">The anchors this filler names.</param>
    /// <returns><see langword="true"/> when the class is admitted.</returns>
    private static bool TryReadFillerClass(OwlClassReference reference, Utf8String definedClass, IReadOnlyDictionary<Utf8String, HashSet<Utf8String>> disjointness, List<Utf8String> anchorsToAppendTo)
    {
        Utf8String iri = reference.Class.Iri;
        if(!ContextHabitatRecognizer.IsChainNodeClass(reference) || iri.Equals(definedClass))
        {
            return false;
        }

        if(disjointness.ContainsKey(iri))
        {
            anchorsToAppendTo.Add(iri);
        }

        return true;
    }

    /// <summary>
    /// Links one told subclass axiom into the symmetric disjointness relation:
    /// the axiom must place a named class under the complement of a named class
    /// or of a non-empty union of named classes — the told shape that entails
    /// pairwise disjointness directly, with no transitive step. A class placed
    /// under the complement of a union containing itself is told empty, which
    /// no witness model may assume away, so it rejects.
    /// </summary>
    /// <param name="subClass">The told subclass axiom.</param>
    /// <param name="relationToAppendTo">The symmetric relation the links append to.</param>
    /// <returns><see langword="true"/> when the axiom is a disjointness link.</returns>
    private static bool TryLinkDisjointness(OwlSubClassOfAxiom subClass, Dictionary<Utf8String, HashSet<Utf8String>> relationToAppendTo)
    {
        if(subClass is not { SubClass: OwlClassReference sub, SuperClass: OwlObjectComplementOf complement } || !ContextHabitatRecognizer.IsChainNodeClass(sub))
        {
            return false;
        }

        switch(complement.Operand)
        {
            case(OwlClassReference singleton):
            {
                return TryLinkDisjointPair(relationToAppendTo, sub.Class.Iri, singleton);
            }
            case(OwlObjectUnionOf union):
            {
                if(union.Operands.Count == 0)
                {
                    return false;
                }

                for(int i = 0; i < union.Operands.Count; i++)
                {
                    if(union.Operands[i] is not OwlClassReference member || !TryLinkDisjointPair(relationToAppendTo, sub.Class.Iri, member))
                    {
                        return false;
                    }
                }

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Records one told disjoint pair in both directions.</summary>
    /// <param name="relationToAppendTo">The symmetric relation.</param>
    /// <param name="left">The class the told axiom places under the complement.</param>
    /// <param name="right">The complemented class reference.</param>
    /// <returns><see langword="true"/> when the pair is a named, non-reflexive disjointness.</returns>
    private static bool TryLinkDisjointPair(Dictionary<Utf8String, HashSet<Utf8String>> relationToAppendTo, Utf8String left, OwlClassReference right)
    {
        Utf8String rightIri = right.Class.Iri;
        if(!ContextHabitatRecognizer.IsChainNodeClass(right) || rightIri.Equals(left))
        {
            return false;
        }

        LinkDirection(relationToAppendTo, left, rightIri);
        LinkDirection(relationToAppendTo, rightIri, left);

        return true;
    }

    /// <summary>Records one direction of a told disjoint pair.</summary>
    /// <param name="relationToAppendTo">The symmetric relation.</param>
    /// <param name="from">The relation's key.</param>
    /// <param name="to">The partner to record.</param>
    private static void LinkDirection(Dictionary<Utf8String, HashSet<Utf8String>> relationToAppendTo, Utf8String from, Utf8String to)
    {
        if(!relationToAppendTo.TryGetValue(from, out HashSet<Utf8String>? partners))
        {
            partners = [];
            relationToAppendTo.Add(from, partners);
        }

        partners.Add(to);
    }

    /// <summary>Whether every anchor pair is disjoint under the computed relation — every pair checked directly, no chain-shape assumption; a missing link between two used anchors is a silence, never a refutation over an unproven distinctness.</summary>
    /// <param name="template">The admitted template, inside the anchor window.</param>
    /// <returns><see langword="true"/> when the anchors are pairwise disjoint.</returns>
    private static bool AnchorsArePairwiseDisjoint(PartitionTemplate template)
    {
        for(int i = 0; i < template.Anchors.Count; i++)
        {
            if(!template.Disjointness.TryGetValue(template.Anchors[i], out HashSet<Utf8String>? partners))
            {
                return false;
            }

            for(int j = i + 1; j < template.Anchors.Count; j++)
            {
                if(!partners.Contains(template.Anchors[j]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
