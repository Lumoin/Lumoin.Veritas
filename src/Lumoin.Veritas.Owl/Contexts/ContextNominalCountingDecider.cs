using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape N clash reason family — the nominal counterpart of the ground clash reasons: stable leading identifiers the statistics assembly and the battery discriminate on.</summary>
internal static class NominalClashReasons
{
    /// <summary>The clash for a forced-merge collapse of a told-distinct pair: the congruence closure of the told and forced equalities merged two individuals a told <c>DifferentIndividuals</c> axiom separates — a refutation of every model, anonymous elements included.</summary>
    /// <param name="first">The first collapsed individual.</param>
    /// <param name="second">The second collapsed individual.</param>
    /// <returns>The named reason.</returns>
    public static string NominalForcedMergeCollapse(Utf8String first, Utf8String second)
    {
        return $"NominalForcedMergeCollapse({first}|{second})";
    }

    /// <summary>The clash for a counted told-distinct clique exceeding its cap: the anchor's counted successor population carries a pairwise told-distinct clique larger than the told max-cardinality bound.</summary>
    /// <param name="anchor">The cap anchor whose population clashed.</param>
    /// <returns>The named reason.</returns>
    public static string NominalCountingPigeonhole(Utf8String anchor)
    {
        return $"NominalCountingPigeonhole({anchor})";
    }
}

/// <summary>
/// The Shape N window measurement the census-first recognizer's
/// clausification-time pass reads on every nominal-jurisdiction module —
/// computed with the source, filler, and dedup disciplines applied BEFORE any
/// boundary comparison, so the battery's near-miss rows can pin the measured
/// quantity independently of the comparison's outcome.
/// </summary>
/// <param name="CountedPopulation">The largest counted successor population any ROLE-ADMISSIBLE cap anchor read — sources enumerated, told-Same deduplicated, qualified-filler filtered, the cap role textually the funnel role; zero when no admissible pairing was evaluated.</param>
/// <param name="DistinctCliqueSize">The largest pairwise told-distinct clique found inside a counted population within the window; zero when none was measured.</param>
/// <param name="CapBound">The cap bound <c>k</c> paired with <see cref="CountedPopulation"/>; zero when no cap was evaluated.</param>
/// <param name="ChainHopSilences">The funnel-chain walks abandoned at <see cref="ContextNominalCountingDecider.FunnelChainHopBound"/> with frontier remaining — each a named silence, never a verdict over an unwalked chain.</param>
/// <param name="PopulationSilences">The cap anchors whose counted population exceeded <see cref="ContextNominalCountingDecider.CountedPopulationBound"/> — each a named silence, never a verdict over an unsearched space.</param>
internal readonly record struct NominalCountingWindow(
    int CountedPopulation,
    int DistinctCliqueSize,
    int CapBound,
    int ChainHopSilences,
    int PopulationSilences)
{
    /// <summary>The empty window: no funnel-and-cap pair was evaluated.</summary>
    public static NominalCountingWindow Empty => default;
}

/// <summary>The Shape N decider's outcome: the clash reason when a sound told clash was found (independent of whether the face is lit — the caller gates propagation), and the window measurement the census carries unconditionally.</summary>
/// <param name="ClashReason">The named clash reason, or <see langword="null"/> when the face is silent on the module.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct NominalCountingOutcome(string? ClashReason, NominalCountingWindow Window);

/// <summary>
/// The enumeration-CSP habitat decider's clash-only face (face one) with its
/// congruence-closure core: a from-scratch pre-engine substrate over the told
/// structure a nominal-jurisdiction module's root facts carry — told
/// <c>SameIndividual</c> and <c>DifferentIndividuals</c> axioms, told class
/// and object-property assertions — beside the told funnel and cap axiom
/// shapes. Sound-or-silent and told-only: the four forced-merge kinds are
/// exactly the proven ones — (a) told same-individuals, (b) singleton one-of
/// membership with a told member, (c) the total-collapse funnel with a
/// single-member filler under an unqualified at-most-one cap, and (d)
/// enumeration membership under a member set collapsed by told sameness, a
/// literal single member, or a kind-(c) collapse — with told
/// different-individuals as the clash monitor, and the counting comparison
/// evaluated per cap axiom in the fixed order: source enumeration, qualified
/// filler filter, empty-filler tautology check, role identity, then the
/// integer comparison. Saturation-derived facts never feed a merge or a
/// count. Every bound is a named window constant; outside any bound the face
/// is silent and ordinary saturation owns the module.
/// </summary>
internal static class ContextNominalCountingDecider
{
    /// <summary>
    /// The counted-population ceiling: the clique search is exact up to this
    /// many deduplicated counted successors of one cap anchor and SILENT
    /// above it. Derivation (algorithmic, with the cost formula the battery
    /// pins): the descending-size clique sweep enumerates at most
    /// 2^16 = 65,536 index combinations across all sizes, each checked over
    /// at most C(16,2) = 120 told-distinct pairs — under eight million
    /// elementary operations at the bound, and the value matches the ground
    /// rider's clique ceiling so the two counting faces share one boundary
    /// discipline, distinct from the repairing face's own wider windows sized
    /// by its habitat.
    /// </summary>
    public const int CountedPopulationBound = 16;

    /// <summary>
    /// The funnel-chain hop ceiling: the told subclass walk from
    /// <c>owl:Thing</c> follows at most this many named-class hops before the
    /// funnel shape and is SILENT beyond it. Derivation (empirical, corpus
    /// maximum with margin): every measured habitat funnel is direct — zero
    /// hops in the pinned battery rows and the census candidates — so the
    /// bound carries a sixteenfold margin over the deepest told chain the
    /// battery constructs, at a walk cost linear in the module's subclass
    /// axioms per hop.
    /// </summary>
    public const int FunnelChainHopBound = 16;

    /// <summary>
    /// Runs the clash-only face and the window measurement over the module's
    /// told structure. The measurement always runs — the census ships
    /// unconditionally — and the clash reason is computed whenever a matched
    /// funnel-and-cap pair admits one; the caller gates whether the reason
    /// propagates into a decision, so a dark face changes no behavior.
    /// </summary>
    /// <param name="module">The nominal-jurisdiction module.</param>
    /// <returns>The outcome: a clash reason or silence, plus the measurement.</returns>
    public static NominalCountingOutcome Run(ReasoningModule module)
    {
        //The census ships unconditionally: the chain walk's silences are
        //measured even on a cap-free module, and the counting measurement runs
        //even when a forced-merge collapse already decided the clash — the
        //collapse reason keeps precedence, the numbers still land.
        ToldStructure told = CollectToldStructure(module);
        int chainSilences = ResolveChainFunnels(told);
        if(told.Caps.Count == 0 || told.Funnels.Count == 0)
        {
            return new NominalCountingOutcome(null, new NominalCountingWindow(0, 0, 0, chainSilences, 0));
        }

        List<(FunnelShape Funnel, CapShape Cap)> pairs = PairFunnelsWithCaps(told);
        if(pairs.Count == 0)
        {
            return new NominalCountingOutcome(null, new NominalCountingWindow(0, 0, 0, chainSilences, 0));
        }

        UnionFind toldSame = BuildToldSameClosure(told);
        UnionFind forced = BuildForcedMergeClosure(told, toldSame, pairs);

        string? collapseReason = FindForcedMergeCollapse(told, forced);
        int populationSilences = 0;
        int measuredPopulation = 0;
        int measuredClique = 0;
        int measuredBound = 0;
        string? countingReason;
        using(VeritasMemoryPool<int> pool = new())
        {
            countingReason = RunCountingComparison(told, toldSame, forced, pairs, pool, ref measuredPopulation, ref measuredClique, ref measuredBound, ref populationSilences);
        }

        return new NominalCountingOutcome(
            collapseReason ?? countingReason,
            new NominalCountingWindow(measuredPopulation, measuredClique, measuredBound, chainSilences, populationSilences));
    }

    /// <summary>One matched funnel: the funnel role's IRI and the deduplicated interned member set of its one-of filler.</summary>
    /// <param name="Role">The funnel role's IRI.</param>
    /// <param name="Members">The deduplicated interned members of the funnel's one-of.</param>
    private sealed record FunnelShape(Utf8String Role, HashSet<int> Members);

    /// <summary>One matched cap: the deduplicated interned anchor set, the cap role's IRI, the bound, and the qualification filler.</summary>
    /// <param name="Anchors">The deduplicated interned members of the cap's anchor one-of.</param>
    /// <param name="Role">The cap role's IRI.</param>
    /// <param name="Bound">The cap bound <c>k</c>.</param>
    /// <param name="Filler">The qualification filler; <see langword="null"/> for an unqualified cap.</param>
    private sealed record CapShape(HashSet<int> Anchors, Utf8String Role, int Bound, OwlClassExpression? Filler);

    /// <summary>The told structure one axiom pass collects — the same told content the root facts carry, in interned form, plus the told funnel, cap, chain, and one-of-bound axiom shapes.</summary>
    private sealed class ToldStructure
    {
        /// <summary>The interned individual names, by id.</summary>
        public List<Utf8String> Names { get; } = [];

        /// <summary>The interning table from individual IRI to id.</summary>
        public Dictionary<Utf8String, int> Ids { get; } = [];

        /// <summary>The told same-individual pairs.</summary>
        public List<(int First, int Second)> SameEdges { get; } = [];

        /// <summary>The told different-individuals lists, named members only.</summary>
        public List<List<int>> DifferentLists { get; } = [];

        /// <summary>The told named-class assertions as (class IRI, individual id).</summary>
        public List<(Utf8String Class, int Individual)> ClassAssertions { get; } = [];

        /// <summary>The told object-property assertions as (role IRI, source id, target id).</summary>
        public List<(Utf8String Role, int Source, int Target)> Edges { get; } = [];

        /// <summary>The matched funnels: the direct ones from intake, joined by the chain-resolved ones.</summary>
        public List<FunnelShape> Funnels { get; } = [];

        /// <summary>The matched caps.</summary>
        public List<CapShape> Caps { get; } = [];

        /// <summary>The told one-of bounds <c>C ⊑ {members}</c> over a named class — the kind-(b) and kind-(d) premise shapes, as (class IRI, interned members).</summary>
        public List<(Utf8String Class, List<int> Members)> OneOfBounds { get; } = [];

        /// <summary>The chain openings: the named classes told directly below <c>owl:Thing</c>.</summary>
        public List<Utf8String> ThingLinks { get; } = [];

        /// <summary>The named-class subclass links, from subclass IRI to its told named superclasses.</summary>
        public Dictionary<Utf8String, List<Utf8String>> NamedLinks { get; } = [];

        /// <summary>The funnel steps anchored on a named class, from class IRI to the funnel shapes told directly above it.</summary>
        public Dictionary<Utf8String, List<FunnelShape>> NamedFunnelSteps { get; } = [];
    }

    /// <summary>Interns an individual name, returning its id.</summary>
    /// <param name="told">The told structure.</param>
    /// <param name="name">The individual's IRI.</param>
    /// <returns>The interned id.</returns>
    private static int Intern(ToldStructure told, Utf8String name)
    {
        if(told.Ids.TryGetValue(name, out int id))
        {
            return id;
        }

        id = told.Names.Count;
        told.Names.Add(name);
        told.Ids.Add(name, id);

        return id;
    }

    /// <summary>Collects the told structure in one pass over the module's axioms, matching the funnel, cap, chain, and one-of-bound shapes through the recognizer's shared predicates.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The collected structure.</returns>
    private static ToldStructure CollectToldStructure(ReasoningModule module)
    {
        ToldStructure told = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlSameIndividualAxiom { First: NamedNode first, Second: NamedNode second }):
                {
                    told.SameEdges.Add((Intern(told, first.Iri), Intern(told, second.Iri)));
                    break;
                }
                case(OwlDifferentIndividualsAxiom different):
                {
                    List<int> members = [];
                    for(int i = 0; i < different.Individuals.Count; i++)
                    {
                        if(different.Individuals[i] is NamedNode named)
                        {
                            members.Add(Intern(told, named.Iri));
                        }
                    }

                    if(members.Count >= 2)
                    {
                        told.DifferentLists.Add(members);
                    }

                    break;
                }
                case(OwlClassAssertionAxiom { Class: OwlClassReference assertedClass, Individual: NamedNode individual }):
                {
                    told.ClassAssertions.Add((assertedClass.Class.Iri, Intern(told, individual.Iri)));
                    break;
                }
                case(OwlObjectPropertyAssertionAxiom { Source: NamedNode source, Target: NamedNode target } assertion):
                {
                    told.Edges.Add((assertion.Property.Iri, Intern(told, source.Iri), Intern(told, target.Iri)));
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    CollectSubClassShape(told, subClass);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return told;
    }

    /// <summary>Matches one subclass axiom against the cap, funnel, chain, and one-of-bound shapes, interning what matches. A funnel under any boolean combinator never matches — the shared predicates match top-level shapes only.</summary>
    /// <param name="told">The told structure.</param>
    /// <param name="subClass">The subclass axiom.</param>
    private static void CollectSubClassShape(ToldStructure told, OwlSubClassOfAxiom subClass)
    {
        if(ContextHabitatRecognizer.TryMatchCapShape(subClass, out OwlObjectOneOf? anchors, out NamedNode? capRole, out int bound, out OwlClassExpression? filler))
        {
            HashSet<int> anchorSet = [];
            for(int i = 0; i < anchors!.Individuals.Count; i++)
            {
                anchorSet.Add(Intern(told, ((NamedNode)anchors.Individuals[i]).Iri));
            }

            told.Caps.Add(new CapShape(anchorSet, capRole!.Iri, bound, filler));

            return;
        }

        if(ContextHabitatRecognizer.TryMatchFunnelShape(subClass.SuperClass, out NamedNode? funnelRole, out OwlObjectOneOf? members))
        {
            HashSet<int> memberSet = [];
            for(int i = 0; i < members!.Individuals.Count; i++)
            {
                memberSet.Add(Intern(told, ((NamedNode)members.Individuals[i]).Iri));
            }

            FunnelShape funnel = new(funnelRole!.Iri, memberSet);
            if(ContextHabitatRecognizer.IsThingReference(subClass.SubClass))
            {
                told.Funnels.Add(funnel);
            }
            else if(subClass.SubClass is OwlClassReference stepClass && ContextHabitatRecognizer.IsChainNodeClass(subClass.SubClass))
            {
                if(!told.NamedFunnelSteps.TryGetValue(stepClass.Class.Iri, out List<FunnelShape>? steps))
                {
                    steps = [];
                    told.NamedFunnelSteps.Add(stepClass.Class.Iri, steps);
                }

                steps.Add(funnel);
            }

            return;
        }

        if(ContextHabitatRecognizer.IsThingReference(subClass.SubClass) && ContextHabitatRecognizer.IsChainNodeClass(subClass.SuperClass))
        {
            told.ThingLinks.Add(((OwlClassReference)subClass.SuperClass).Class.Iri);

            return;
        }

        if(ContextHabitatRecognizer.IsChainNodeClass(subClass.SubClass) && ContextHabitatRecognizer.IsChainNodeClass(subClass.SuperClass))
        {
            Utf8String from = ((OwlClassReference)subClass.SubClass).Class.Iri;
            if(!told.NamedLinks.TryGetValue(from, out List<Utf8String>? targets))
            {
                targets = [];
                told.NamedLinks.Add(from, targets);
            }

            targets.Add(((OwlClassReference)subClass.SuperClass).Class.Iri);

            return;
        }

        if(subClass is { SubClass: OwlClassReference boundedClass, SuperClass: OwlObjectOneOf oneOfBound }
            && ContextHabitatRecognizer.IsChainNodeClass(subClass.SubClass)
            && oneOfBound.Individuals.Count >= 1)
        {
            List<int> members2 = [];
            for(int i = 0; i < oneOfBound.Individuals.Count; i++)
            {
                if(oneOfBound.Individuals[i] is not NamedNode named)
                {
                    return;
                }

                members2.Add(Intern(told, named.Iri));
            }

            told.OneOfBounds.Add((boundedClass.Class.Iri, members2));
        }
    }

    /// <summary>
    /// Resolves the chain funnels: an iterative breadth-first walk from
    /// <c>owl:Thing</c> over the told named-class subclass links, at most
    /// <see cref="FunnelChainHopBound"/> hops deep, appending every funnel
    /// shape told directly above a reached class. Each hop passes through
    /// exactly one named class — any other superclass shape simply carries no
    /// link, so a disjunctive or otherwise guarded hop is never followed.
    /// </summary>
    /// <param name="told">The told structure; resolved funnels append to <see cref="ToldStructure.Funnels"/>.</param>
    /// <returns>The number of walks abandoned at the hop bound with frontier remaining.</returns>
    private static int ResolveChainFunnels(ToldStructure told)
    {
        if(told.ThingLinks.Count == 0)
        {
            return 0;
        }

        HashSet<Utf8String> visited = [];
        Queue<(Utf8String Class, int Depth)> frontier = new();
        foreach(Utf8String opening in told.ThingLinks)
        {
            if(visited.Add(opening))
            {
                frontier.Enqueue((opening, 1));
            }
        }

        int silences = 0;
        while(frontier.Count > 0)
        {
            (Utf8String current, int depth) = frontier.Dequeue();
            if(told.NamedFunnelSteps.TryGetValue(current, out List<FunnelShape>? steps))
            {
                told.Funnels.AddRange(steps);
            }

            if(!told.NamedLinks.TryGetValue(current, out List<Utf8String>? targets))
            {
                continue;
            }

            foreach(Utf8String target in targets)
            {
                if(visited.Contains(target))
                {
                    continue;
                }

                if(depth >= FunnelChainHopBound)
                {
                    silences++;
                    continue;
                }

                visited.Add(target);
                frontier.Enqueue((target, depth + 1));
            }
        }

        return silences;
    }

    /// <summary>Pairs every funnel with every cap whose anchor one-of is over the SAME individual set as the funnel's one-of — the member-set identity clause of the cap admission; the role identity check runs later, in the fixed comparison order.</summary>
    /// <param name="told">The told structure.</param>
    /// <returns>The matched pairs.</returns>
    private static List<(FunnelShape Funnel, CapShape Cap)> PairFunnelsWithCaps(ToldStructure told)
    {
        List<(FunnelShape, CapShape)> pairs = [];
        foreach(FunnelShape funnel in told.Funnels)
        {
            foreach(CapShape cap in told.Caps)
            {
                if(funnel.Members.SetEquals(cap.Anchors))
                {
                    pairs.Add((funnel, cap));
                }
            }
        }

        return pairs;
    }

    /// <summary>Builds the told-Same closure — the alias structure the source enumeration deduplicates and aliases through; told sameness only, never a forced merge.</summary>
    /// <param name="told">The told structure.</param>
    /// <returns>The union-find over interned individuals.</returns>
    private static UnionFind BuildToldSameClosure(ToldStructure told)
    {
        UnionFind toldSame = new(told.Names.Count);
        foreach((int first, int second) in told.SameEdges)
        {
            toldSame.Union(first, second);
        }

        return toldSame;
    }

    /// <summary>
    /// Builds the forced-merge closure over the four proven kinds: (a) told
    /// same-individuals; (c) the total collapse of a matched single-member
    /// funnel under an unqualified at-most-one cap of the same role, which
    /// merges every interned individual with the funnel member; (b) and (d)
    /// the one-of bounds <c>C ⊑ {members}</c> whose member set is a literal
    /// singleton, pairwise told-Same, or collapsed by kind (c), merging every
    /// told <c>C</c>-member with the set's representative. No merge ever
    /// reads a saturation-derived fact, and the member-set collapse test
    /// reads told sameness and the kind-(c) flag only.
    /// </summary>
    /// <param name="told">The told structure.</param>
    /// <param name="toldSame">The told-Same closure the collapse test reads.</param>
    /// <param name="pairs">The matched funnel-and-cap pairs.</param>
    /// <returns>The union-find over interned individuals.</returns>
    private static UnionFind BuildForcedMergeClosure(ToldStructure told, UnionFind toldSame, List<(FunnelShape Funnel, CapShape Cap)> pairs)
    {
        UnionFind forced = new(told.Names.Count);
        foreach((int first, int second) in told.SameEdges)
        {
            forced.Union(first, second);
        }

        bool totalCollapse = false;
        int collapseMember = 0;
        foreach((FunnelShape funnel, CapShape cap) in pairs)
        {
            if(funnel.Members.Count == 1 && cap.Bound == 1 && IsUnqualifiedFiller(cap.Filler) && funnel.Role.Equals(cap.Role))
            {
                totalCollapse = true;
                foreach(int member in funnel.Members)
                {
                    collapseMember = member;
                }

                break;
            }
        }

        if(totalCollapse)
        {
            for(int i = 0; i < told.Names.Count; i++)
            {
                forced.Union(i, collapseMember);
            }
        }

        foreach((Utf8String boundedClass, List<int> members) in told.OneOfBounds)
        {
            if(!IsCollapsedMemberSet(toldSame, members, totalCollapse))
            {
                continue;
            }

            int representative = members[0];
            foreach((Utf8String assertedClass, int individual) in told.ClassAssertions)
            {
                if(assertedClass.Equals(boundedClass))
                {
                    forced.Union(individual, representative);
                }
            }
        }

        return forced;
    }

    /// <summary>Whether a cap's filler leaves it unqualified: no filler at all, or the explicit <c>owl:Thing</c> — the two spellings of the same unrestricted count.</summary>
    /// <param name="filler">The cap's qualification filler.</param>
    /// <returns><see langword="true"/> for an unqualified cap.</returns>
    private static bool IsUnqualifiedFiller(OwlClassExpression? filler)
    {
        return filler is null || ContextHabitatRecognizer.IsThingReference(filler);
    }

    /// <summary>Whether a one-of bound's member set is collapsed to a single element by a licensed collapse: a literal single member, pairwise told sameness, or the kind-(c) total collapse — never a derived equality.</summary>
    /// <param name="toldSame">The told-Same closure.</param>
    /// <param name="members">The interned member set.</param>
    /// <param name="totalCollapse">Whether the kind-(c) total collapse fired.</param>
    /// <returns><see langword="true"/> when the set is collapsed.</returns>
    private static bool IsCollapsedMemberSet(UnionFind toldSame, List<int> members, bool totalCollapse)
    {
        if(members.Count == 1 || totalCollapse)
        {
            return true;
        }

        int representative = toldSame.Find(members[0]);
        for(int i = 1; i < members.Count; i++)
        {
            if(toldSame.Find(members[i]) != representative)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The clash monitor: whether the forced-merge closure collapsed any told-distinct pair — the congruence-closure backbone that refutes every model when it fires.</summary>
    /// <param name="told">The told structure.</param>
    /// <param name="forced">The forced-merge closure.</param>
    /// <returns>The named clash reason, or <see langword="null"/>.</returns>
    private static string? FindForcedMergeCollapse(ToldStructure told, UnionFind forced)
    {
        foreach(List<int> members in told.DifferentLists)
        {
            for(int i = 0; i < members.Count; i++)
            {
                for(int j = i + 1; j < members.Count; j++)
                {
                    if(forced.Find(members[i]) == forced.Find(members[j]))
                    {
                        return NominalClashReasons.NominalForcedMergeCollapse(told.Names[members[i]], told.Names[members[j]]);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The counting comparison, evaluated INDEPENDENTLY per matched
    /// cap axiom and per anchor, in the fixed order: (1) source enumeration —
    /// the single-member funnel's universal edge covering every interned
    /// individual, the told edges of the funnel role from the anchor, and
    /// told-Same aliases deduplicated by representative; (2) the qualified
    /// filler filter — a named filler keeps only members PROVABLY inside it,
    /// a told filler membership on any told-Same or forced-merge alias; any
    /// other filler shape keeps none, and the residual is silent; (3) the
    /// empty-filler tautology check — an <c>owl:Nothing</c> filler skips the
    /// cap entirely (checked ahead of the filter, which is equivalent: no
    /// individual carries a told <c>owl:Nothing</c> membership, so the
    /// filter would empty the population and the cap could never clash
    /// either way); (4) the role identity check — the cap role must be
    /// textually the funnel role, and the window numbers report only
    /// role-admissible pairings; only then (5) the integer comparison of the
    /// largest told-distinct clique against the bound. A filtered clique and
    /// a bound are never reused across cap axioms.
    /// </summary>
    /// <param name="told">The told structure.</param>
    /// <param name="toldSame">The told-Same closure the source dedup aliases through.</param>
    /// <param name="forced">The forced-merge closure the provable-membership filter reads.</param>
    /// <param name="pairs">The matched funnel-and-cap pairs.</param>
    /// <param name="pool">The buffer pool the clique sweeps rent from.</param>
    /// <param name="measuredPopulation">The largest counted population measured, updated in place.</param>
    /// <param name="measuredClique">The largest told-distinct clique measured, updated in place.</param>
    /// <param name="measuredBound">The cap bound paired with the reported population, updated in place.</param>
    /// <param name="populationSilences">The population-window silences, updated in place.</param>
    /// <returns>The named clash reason, or <see langword="null"/>.</returns>
    private static string? RunCountingComparison(
        ToldStructure told,
        UnionFind toldSame,
        UnionFind forced,
        List<(FunnelShape Funnel, CapShape Cap)> pairs,
        VeritasMemoryPool<int> pool,
        ref int measuredPopulation,
        ref int measuredClique,
        ref int measuredBound,
        ref int populationSilences)
    {
        HashSet<(int First, int Second)> distinct = BuildToldDistinctPairs(told, toldSame);
        foreach((FunnelShape funnel, CapShape cap) in pairs)
        {
            foreach(int anchor in cap.Anchors)
            {
                List<int> population = EnumerateCountedPopulation(told, toldSame, funnel, anchor);
                if(cap.Filler is not null && !ContextHabitatRecognizer.IsThingReference(cap.Filler))
                {
                    if(cap.Filler is OwlClassReference fillerReference && fillerReference.Class.Iri.Equals(OwlVocabulary.Nothing))
                    {
                        continue;
                    }

                    population = FilterByProvableFillerMembership(told, toldSame, forced, population, cap.Filler);
                }

                if(!cap.Role.Equals(funnel.Role))
                {
                    continue;
                }

                if(population.Count > measuredPopulation)
                {
                    measuredPopulation = population.Count;
                    measuredBound = cap.Bound;
                }

                if(population.Count > CountedPopulationBound)
                {
                    populationSilences++;
                    continue;
                }

                int clique = LargestDistinctClique(population, distinct, pool);
                if(clique > measuredClique)
                {
                    measuredClique = clique;
                }

                if(clique > cap.Bound)
                {
                    return NominalClashReasons.NominalCountingPigeonhole(told.Names[anchor]);
                }
            }
        }

        return null;
    }

    /// <summary>Builds the told-distinct pair set over told-Same representatives: every unordered pair of every told different-individuals list, both orderings, resolved through the alias closure.</summary>
    /// <param name="told">The told structure.</param>
    /// <param name="toldSame">The told-Same closure.</param>
    /// <returns>The symmetric pair set.</returns>
    private static HashSet<(int First, int Second)> BuildToldDistinctPairs(ToldStructure told, UnionFind toldSame)
    {
        HashSet<(int, int)> distinct = [];
        foreach(List<int> members in told.DifferentLists)
        {
            for(int i = 0; i < members.Count; i++)
            {
                for(int j = i + 1; j < members.Count; j++)
                {
                    int first = toldSame.Find(members[i]);
                    int second = toldSame.Find(members[j]);
                    distinct.Add((first, second));
                    distinct.Add((second, first));
                }
            }
        }

        return distinct;
    }

    /// <summary>
    /// Enumerates one anchor's counted population from the licensed sources
    /// only: the universal funnel edge — every interned individual — when the
    /// funnel's one-of is a literal single member (a multi-member funnel is a
    /// disjunction and attributes nobody), and the told edges of the funnel
    /// role whose source is the anchor by term; targets deduplicate by
    /// told-Same representative, which is exactly the told-Same alias source.
    /// </summary>
    /// <param name="told">The told structure.</param>
    /// <param name="toldSame">The told-Same closure.</param>
    /// <param name="funnel">The matched funnel.</param>
    /// <param name="anchor">The cap anchor.</param>
    /// <returns>The deduplicated population, as told-Same representatives.</returns>
    private static List<int> EnumerateCountedPopulation(ToldStructure told, UnionFind toldSame, FunnelShape funnel, int anchor)
    {
        HashSet<int> representatives = [];
        if(funnel.Members.Count == 1 && funnel.Members.Contains(anchor))
        {
            for(int i = 0; i < told.Names.Count; i++)
            {
                representatives.Add(toldSame.Find(i));
            }
        }

        foreach((Utf8String role, int source, int target) in told.Edges)
        {
            if(source == anchor && role.Equals(funnel.Role))
            {
                representatives.Add(toldSame.Find(target));
            }
        }

        return [.. representatives];
    }

    /// <summary>Filters a counted population to the members PROVABLY inside a qualified filler (told/forced): a named filler keeps a representative exactly when some told-Same or forced-merge alias carries a told assertion of that class — a forced merge is an entailed equality over the proven kinds, so the membership transfers in every model; any other filler shape keeps none, and the residual stays silent — never clashed.</summary>
    /// <param name="told">The told structure.</param>
    /// <param name="toldSame">The told-Same closure the population's representatives are drawn from.</param>
    /// <param name="forced">The forced-merge closure the provable-membership test aliases through.</param>
    /// <param name="population">The unfiltered population, as told-Same representatives.</param>
    /// <param name="filler">The cap's qualification filler.</param>
    /// <returns>The filtered population.</returns>
    private static List<int> FilterByProvableFillerMembership(ToldStructure told, UnionFind toldSame, UnionFind forced, List<int> population, OwlClassExpression filler)
    {
        if(filler is not OwlClassReference named)
        {
            return [];
        }

        HashSet<int> insideFiller = [];
        foreach((Utf8String assertedClass, int individual) in told.ClassAssertions)
        {
            if(assertedClass.Equals(named.Class.Iri))
            {
                insideFiller.Add(forced.Find(individual));
            }
        }

        List<int> filtered = [];
        foreach(int representative in population)
        {
            if(insideFiller.Contains(forced.Find(representative)))
            {
                filtered.Add(representative);
            }
        }

        return filtered;
    }

    /// <summary>Finds the largest pairwise told-distinct clique inside a counted population: a descending-size sweep of the shared combination odometer, exact within the population window.</summary>
    /// <param name="population">The counted population, as told-Same representatives.</param>
    /// <param name="distinct">The told-distinct pair set over representatives.</param>
    /// <param name="pool">The buffer pool the odometer rents from.</param>
    /// <returns>The largest clique size; zero for an empty population.</returns>
    private static int LargestDistinctClique(List<int> population, HashSet<(int First, int Second)> distinct, VeritasMemoryPool<int> pool)
    {
        for(int size = population.Count; size >= 2; size--)
        {
            using CombinationIndexEnumerator combinations = CombinationIndexEnumerator.Create(pool, population.Count, size);
            while(combinations.MoveNext())
            {
                if(IsDistinctClique(population, combinations.Current, distinct))
                {
                    return size;
                }
            }
        }

        return population.Count > 0 ? 1 : 0;
    }

    /// <summary>Whether the indexed population subset is pairwise told-distinct.</summary>
    /// <param name="population">The counted population.</param>
    /// <param name="indices">The subset's indices.</param>
    /// <param name="distinct">The told-distinct pair set.</param>
    /// <returns><see langword="true"/> when every pair is told-distinct.</returns>
    private static bool IsDistinctClique(List<int> population, ReadOnlySpan<int> indices, HashSet<(int First, int Second)> distinct)
    {
        for(int i = 0; i < indices.Length; i++)
        {
            for(int j = i + 1; j < indices.Length; j++)
            {
                if(!distinct.Contains((population[indices[i]], population[indices[j]])))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>A minimal union-find over dense ids: iterative find with path halving, union by size — the congruence-closure core's carrier, no undo, rebuilt per decision.</summary>
    private sealed class UnionFind
    {
        /// <summary>The parent link per element; an element is a root when it parents itself.</summary>
        private int[] Parents { get; }

        /// <summary>The tree size per root.</summary>
        private int[] Sizes { get; }

        /// <summary>Initialises the structure with every element its own root.</summary>
        /// <param name="count">The element count.</param>
        public UnionFind(int count)
        {
            Parents = new int[count];
            Sizes = new int[count];
            for(int i = 0; i < count; i++)
            {
                Parents[i] = i;
                Sizes[i] = 1;
            }
        }

        /// <summary>Finds the element's root, halving the path as it walks.</summary>
        /// <param name="element">The element.</param>
        /// <returns>The root.</returns>
        public int Find(int element)
        {
            int current = element;
            while(Parents[current] != current)
            {
                Parents[current] = Parents[Parents[current]];
                current = Parents[current];
            }

            return current;
        }

        /// <summary>Merges the two elements' classes, attaching the smaller tree under the larger.</summary>
        /// <param name="first">The first element.</param>
        /// <param name="second">The second element.</param>
        public void Union(int first, int second)
        {
            int firstRoot = Find(first);
            int secondRoot = Find(second);
            if(firstRoot == secondRoot)
            {
                return;
            }

            if(Sizes[firstRoot] < Sizes[secondRoot])
            {
                (firstRoot, secondRoot) = (secondRoot, firstRoot);
            }

            Parents[secondRoot] = firstRoot;
            Sizes[firstRoot] += Sizes[secondRoot];
        }
    }
}
