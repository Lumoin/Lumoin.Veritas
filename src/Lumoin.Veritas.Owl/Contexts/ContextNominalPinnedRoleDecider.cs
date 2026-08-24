using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape D clash reason family — the nominal-pinned-role counterpart of the sibling faces' clash reasons: a stable leading identifier the statistics assembly and the battery discriminate on.</summary>
internal static class NominalPinnedRoleClashReasons
{
    /// <summary>The clash for a reverse-denied told edge under a diagonal-pinned role: told inverse-functionality plus a told self-loop at every member of the role's told nominal range pins the role's extension into the identity diagonal, so a told edge outside the pinned loops collapses its endpoints together and the told denial of its exact reverse is contradicted.</summary>
    /// <param name="role">The pinned role whose denied diagonal edge clashed.</param>
    /// <returns>The named reason.</returns>
    public static string NominalPinnedRoleDeniedDiagonalEdge(Utf8String role)
    {
        return $"NominalPinnedRoleDeniedDiagonalEdge({role})";
    }
}

/// <summary>
/// The Shape D window measurement the census-first recognizer's
/// pre-clausification pass reads on every nominal-pinned-role-jurisdiction
/// module — computed identically dark and lit, with the member deduplication
/// applied BEFORE any boundary comparison, so the battery's near-miss rows can
/// pin the measured quantity independently of the comparison's outcome.
/// </summary>
/// <param name="MemberCount">The largest resolved range's distinct named one-of members <c>k</c>, deduplicated by interned IRI; zero when no range resolution survived the namedness discipline.</param>
/// <param name="PinnedEdgeCount">The told self-loops the reported resolution consumed — the clashing resolution's full member cover, or the largest recognized resolution's covered members on a silence; zero when no resolution sat inside the member window.</param>
/// <param name="DeniedEdgeCount">The told denials recognized module-wide — top-level complements of a has-value over a plain role with a named carrier and a named denied value, the concept form of an edge denial.</param>
/// <param name="MemberSilences">The range resolutions skipped for carrying more than <see cref="ContextNominalPinnedRoleDecider.NominalPinnedRoleMemberBound"/> distinct members — a named silence, never a verdict over an unscanned member window; zero otherwise.</param>
internal readonly record struct NominalPinnedRoleWindow(
    int MemberCount,
    int PinnedEdgeCount,
    int DeniedEdgeCount,
    int MemberSilences)
{
    /// <summary>The empty window: no candidate pinned role was recognized.</summary>
    public static NominalPinnedRoleWindow Empty => default;
}

/// <summary>The Shape D decider's outcome: the closed-form refutation when every jurisdiction condition held inside the window, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The closed-form verdict — <see langword="false"/> for the denied-diagonal-edge refutation — or <see langword="null"/> when the face is silent on the module. The face has no certify direction, so <see langword="true"/> never occurs.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct NominalPinnedRoleOutcome(bool? Consistent, NominalPinnedRoleWindow Window)
{
    /// <summary>The named clash reason on a refutation; <see langword="null"/> on every silent outcome.</summary>
    public string? ClashReason { get; init; }

    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static NominalPinnedRoleOutcome SilentWith(NominalPinnedRoleWindow window)
    {
        return new NominalPinnedRoleOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's nominal-pinned-role clash face (face
/// nineteen): a tier-1 CLOSED FORM over the told axiom surfaces of a
/// diagonal-pinned role module. The Diagonal Pinning Lemma carries it: a told
/// <c>InverseFunctionalObjectProperty</c> over a plain role, a told range over
/// the SAME plain role resolving to a one-of of named individuals — inline, or
/// through EXACTLY ONE told hop reading only the class-to-one-of direction of
/// an equivalence or of a subclass axiom with the named class in SUBCLASS
/// position — and a told self-loop at EVERY deduplicated member pin the role's
/// extension into the identity diagonal in every model: any edge's target is
/// some member, the member's self-loop makes source and member share a
/// successor, and inverse-functionality collapses them. Collisions among the
/// members' denotations only shrink the pinned set, so no unique-name
/// assumption is used. A told edge with named endpoints over the same plain
/// role that is NOT itself one of the pinned self-loops, beside a told
/// concept-form denial of EXACTLY its reverse — a top-level complement of the
/// has-value reading, the shape the refutation walk's skolemized arms emit —
/// then has no model, five told-axiom steps end to end with zero branching,
/// zero enumeration, and zero equality search. The face is CLASH-ONLY: a
/// pinned extension without a reverse-denied edge proves nothing about the
/// surrounding module, so the face is silent and ordinary saturation owns the
/// verdict. Sound-or-silent and told-only, with a MONOTONE jurisdiction:
/// unrecognized axioms are IGNORED rather than rejecting the module, because
/// extra axioms only shrink the model class and can never rescue a refuted
/// subset. Every unmet condition inside the recognized shapes — a
/// characteristic other than inverse-functional, an inverse property
/// expression in any role position, a reversed or chained range hop, a domain
/// axiom, an anonymous one-of member, one member without its told self-loop, a
/// denial standing on a pinned self-loop, a ground-form denial — leaves the
/// module to ordinary saturation. The member ceiling is a named window
/// constant; outside it the face is silent with the measured numbers already
/// on the record.
/// </summary>
internal static class ContextNominalPinnedRoleDecider
{
    /// <summary>
    /// The one-of member ceiling: the pinning totality scan covers at most this
    /// many distinct named members per range resolution and the resolution is
    /// SKIPPED above it. Derivation (engineering, with the cost formula the
    /// battery pins): the totality check is at most sixteen set probes per
    /// resolution, and the value matches the counting faces' shared sixteen
    /// ceiling — the counted-population, ground-clique, partition-anchor,
    /// gadget-atom, pair-assignment, and spy-point member bounds — so every
    /// counting-family pre-engine face carries one boundary discipline. The
    /// member count is a deduplicated set size and the comparison is a single
    /// integer read, so no arithmetic is performed anywhere in the face and no
    /// overflow story exists. Collecting the told shapes is one linear pass
    /// bounded by the module's own axiom count rather than by this constant.
    /// </summary>
    public const int NominalPinnedRoleMemberBound = 16;

    /// <summary>Measures the Shape D census window without deciding anything: the largest resolved member count, the reported resolution's consumed self-loops, the recognized denials, and the member-window silences — computed identically dark and lit, so the census ships unconditionally. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement; all-zero when no candidate role was recognized.</returns>
    public static NominalPinnedRoleOutcome Measure(ReasoningModule module)
    {
        return TryCollectTemplate(module, out NominalPinnedRoleTemplate? template)
            ? NominalPinnedRoleOutcome.SilentWith(EvaluateTemplate(template).Window)
            : NominalPinnedRoleOutcome.SilentWith(NominalPinnedRoleWindow.Empty);
    }

    /// <summary>
    /// Runs the nominal-pinned-role clash face: the told-shape collection, the
    /// per-resolution pinning totality scan inside the member window, and the
    /// reverse-exact edge-and-denial match that refutes the module. The
    /// measurement lands first in every case, so a window or totality silence
    /// still carries the numbers, and the face returns <see langword="false"/>
    /// or silence only — never a consistency certificate.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the closed-form refutation with its measurement, or silence.</returns>
    public static NominalPinnedRoleOutcome Run(ReasoningModule module)
    {
        if(!TryCollectTemplate(module, out NominalPinnedRoleTemplate? template))
        {
            return NominalPinnedRoleOutcome.SilentWith(NominalPinnedRoleWindow.Empty);
        }

        NominalPinnedRoleEvaluation evaluation = EvaluateTemplate(template);
        if(evaluation.ClashRole is null)
        {
            return NominalPinnedRoleOutcome.SilentWith(evaluation.Window);
        }

        return new NominalPinnedRoleOutcome(false, evaluation.Window)
        {
            ClashReason = NominalPinnedRoleClashReasons.NominalPinnedRoleDeniedDiagonalEdge(evaluation.ClashRole.Iri),
        };
    }

    /// <summary>One told range over a plain role: the role's interned IRI and the raw range expression, resolved against the one-hop table at evaluation time.</summary>
    /// <param name="Role">The range axiom's plain role IRI.</param>
    /// <param name="Target">The raw range class expression.</param>
    private readonly record struct NominalPinnedRoleRange(Utf8String Role, OwlClassExpression Target);

    /// <summary>One told edge with named endpoints over a plain role — the self-loops the pinning consumes and the fresh edges the clash reads, in one list scanned per resolution.</summary>
    /// <param name="Role">The edge's plain role IRI.</param>
    /// <param name="Source">The source individual's interned IRI.</param>
    /// <param name="Target">The target individual's interned IRI.</param>
    private readonly record struct NominalPinnedRoleEdge(Utf8String Role, Utf8String Source, Utf8String Target);

    /// <summary>One told concept-form edge denial: a named carrier typed with the top-level complement of a has-value over a plain role and a named value — the denial of the edge from the carrier to the value.</summary>
    /// <param name="Role">The denied edge's plain role IRI.</param>
    /// <param name="Carrier">The asserted individual's interned IRI — the denied edge's source.</param>
    /// <param name="Denied">The excluded value's interned IRI — the denied edge's target.</param>
    private readonly record struct NominalPinnedRoleDenial(Utf8String Role, Utf8String Carrier, Utf8String Denied);

    /// <summary>The evaluation one template yields: the census window, and the role whose pinned diagonal a reverse-denied edge clashed against.</summary>
    /// <param name="Window">The census window.</param>
    /// <param name="ClashRole">The clashing candidate role; <see langword="null"/> when every resolution stayed silent.</param>
    private readonly record struct NominalPinnedRoleEvaluation(NominalPinnedRoleWindow Window, NamedNode? ClashRole);

    /// <summary>One range resolution's reading: whether every member was named, the deduplicated member count, the members covered by told self-loops, and whether a reverse-denied non-loop edge clashed under a full cover.</summary>
    /// <param name="AllNamed">Whether every one-of member was a named individual; an anonymous member drops the resolution whole, measurement included.</param>
    /// <param name="MemberCount">The deduplicated member count <c>k</c>.</param>
    /// <param name="PinnedCovered">The members carrying a told self-loop over the candidate role, counted only inside the member window.</param>
    /// <param name="Clashes">Whether the fully pinned resolution stood beside a told non-loop edge whose exact reverse a told denial excludes.</param>
    private readonly record struct NominalPinnedRoleResolution(bool AllNamed, int MemberCount, int PinnedCovered, bool Clashes);

    /// <summary>The collected told shapes: the candidate roles in first-seen order, the plain-role ranges, the one-hop class-to-one-of table, the per-role self-loop sets, the named-endpoint edges, and the concept-form denials.</summary>
    /// <param name="CandidateRoles">The plain roles a told inverse-functional characteristic nominated, deduplicated by interned IRI in first-seen order.</param>
    /// <param name="Ranges">The told ranges over plain roles.</param>
    /// <param name="OneOfHops">The one-hop resolutions: a named class to every one-of a told equivalence pairs it with or a told subclass axiom bounds it by, the class in subclass position only.</param>
    /// <param name="SelfLoops">The told self-loops: a plain role IRI to the named individuals looping on it.</param>
    /// <param name="Edges">The told edges with named endpoints over plain roles, self-loops included.</param>
    /// <param name="Denials">The told concept-form edge denials.</param>
    private sealed record NominalPinnedRoleTemplate(
        List<NamedNode> CandidateRoles,
        List<NominalPinnedRoleRange> Ranges,
        Dictionary<Utf8String, List<OwlObjectOneOf>> OneOfHops,
        Dictionary<Utf8String, HashSet<Utf8String>> SelfLoops,
        List<NominalPinnedRoleEdge> Edges,
        List<NominalPinnedRoleDenial> Denials);

    /// <summary>
    /// Evaluates every candidate role's range resolutions in first-seen order:
    /// the member measurement and window silences land for every resolution,
    /// the largest recognized in-window resolution is reported on a silence,
    /// and the FIRST fully pinned resolution standing beside a reverse-denied
    /// non-loop edge carries the clash. The scan always completes, so the
    /// window is identical whether or not a clash was found earlier in it.
    /// </summary>
    /// <param name="template">The collected template.</param>
    /// <returns>The evaluation.</returns>
    private static NominalPinnedRoleEvaluation EvaluateTemplate(NominalPinnedRoleTemplate template)
    {
        int memberCount = 0;
        int memberSilences = 0;
        int recognizedMembers = 0;
        int recognizedPinned = 0;
        NamedNode? clashRole = null;
        int clashMembers = 0;
        for(int candidate = 0; candidate < template.CandidateRoles.Count; candidate++)
        {
            NamedNode role = template.CandidateRoles[candidate];
            for(int range = 0; range < template.Ranges.Count; range++)
            {
                if(!template.Ranges[range].Role.Equals(role.Iri))
                {
                    continue;
                }

                if(template.Ranges[range].Target is OwlObjectOneOf inline)
                {
                    FoldResolution(EvaluateResolution(template, role, inline), role, ref memberCount, ref memberSilences, ref recognizedMembers, ref recognizedPinned, ref clashRole, ref clashMembers);
                }
                else if(template.Ranges[range].Target is OwlClassReference reference
                    && template.OneOfHops.TryGetValue(reference.Class.Iri, out List<OwlObjectOneOf>? hops))
                {
                    for(int hop = 0; hop < hops.Count; hop++)
                    {
                        FoldResolution(EvaluateResolution(template, role, hops[hop]), role, ref memberCount, ref memberSilences, ref recognizedMembers, ref recognizedPinned, ref clashRole, ref clashMembers);
                    }
                }
            }
        }

        NominalPinnedRoleWindow window = new(
            memberCount,
            clashRole is null ? recognizedPinned : clashMembers,
            template.Denials.Count,
            memberSilences);

        return new NominalPinnedRoleEvaluation(window, clashRole);
    }

    /// <summary>Folds one resolution's reading into the running evaluation: an anonymous-dropped resolution contributes nothing, an empty one-of is silence, an over-window count charges the member silence, a recognized in-window resolution competes for the reported largest, and the first clashing resolution binds the clash role.</summary>
    /// <param name="reading">The resolution's reading.</param>
    /// <param name="role">The candidate role the resolution was evaluated for.</param>
    /// <param name="memberCount">The largest resolved member count so far.</param>
    /// <param name="memberSilences">The member-window silences charged so far.</param>
    /// <param name="recognizedMembers">The largest recognized in-window member count so far.</param>
    /// <param name="recognizedPinned">The covered members of the largest recognized resolution so far.</param>
    /// <param name="clashRole">The first clashing role, bound once.</param>
    /// <param name="clashMembers">The first clashing resolution's member count, bound once.</param>
    private static void FoldResolution(
        NominalPinnedRoleResolution reading,
        NamedNode role,
        ref int memberCount,
        ref int memberSilences,
        ref int recognizedMembers,
        ref int recognizedPinned,
        ref NamedNode? clashRole,
        ref int clashMembers)
    {
        if(!reading.AllNamed || reading.MemberCount == 0)
        {
            return;
        }

        memberCount = Math.Max(memberCount, reading.MemberCount);
        if(reading.MemberCount > NominalPinnedRoleMemberBound)
        {
            memberSilences++;

            return;
        }

        if(reading.MemberCount > recognizedMembers)
        {
            recognizedMembers = reading.MemberCount;
            recognizedPinned = reading.PinnedCovered;
        }

        if(reading.Clashes && clashRole is null)
        {
            clashRole = role;
            clashMembers = reading.MemberCount;
        }
    }

    /// <summary>
    /// Evaluates one range resolution for one candidate role: the members are
    /// deduplicated by interned IRI, ONE anonymous member drops the resolution
    /// whole, an over-window count is measured and returned unscanned, the
    /// pinning totality demands a told self-loop at EVERY member — one uncovered
    /// member leaves the diagonal unpinned and the lemma false, so the
    /// resolution is silent — and a fully pinned resolution clashes exactly when
    /// a told edge over the same role that is NOT one of the pinned self-loops
    /// stands beside a told denial of its exact reverse: the denial's carrier is
    /// the edge's target and its excluded value is the edge's source. A denial
    /// standing on a pinned self-loop is an ordinary told contradiction the
    /// engine owns, and a denial beside its own edge is not this face's shape.
    /// </summary>
    /// <param name="template">The collected template.</param>
    /// <param name="role">The candidate role.</param>
    /// <param name="oneOf">The resolved one-of.</param>
    /// <returns>The resolution's reading.</returns>
    private static NominalPinnedRoleResolution EvaluateResolution(NominalPinnedRoleTemplate template, NamedNode role, OwlObjectOneOf oneOf)
    {
        HashSet<Utf8String> members = [];
        for(int index = 0; index < oneOf.Individuals.Count; index++)
        {
            if(oneOf.Individuals[index] is not NamedNode member)
            {
                return new NominalPinnedRoleResolution(false, 0, 0, false);
            }

            members.Add(member.Iri);
        }

        if(members.Count == 0 || members.Count > NominalPinnedRoleMemberBound)
        {
            return new NominalPinnedRoleResolution(true, members.Count, 0, false);
        }

        int covered = 0;
        if(template.SelfLoops.TryGetValue(role.Iri, out HashSet<Utf8String>? loops))
        {
            foreach(Utf8String member in members)
            {
                if(loops.Contains(member))
                {
                    covered++;
                }
            }
        }

        if(covered != members.Count)
        {
            return new NominalPinnedRoleResolution(true, members.Count, covered, false);
        }

        for(int edge = 0; edge < template.Edges.Count; edge++)
        {
            NominalPinnedRoleEdge told = template.Edges[edge];
            if(!told.Role.Equals(role.Iri) || (told.Source.Equals(told.Target) && members.Contains(told.Source)))
            {
                continue;
            }

            for(int denial = 0; denial < template.Denials.Count; denial++)
            {
                NominalPinnedRoleDenial deny = template.Denials[denial];
                if(deny.Role.Equals(role.Iri) && deny.Carrier.Equals(told.Target) && deny.Denied.Equals(told.Source))
                {
                    return new NominalPinnedRoleResolution(true, members.Count, covered, true);
                }
            }
        }

        return new NominalPinnedRoleResolution(true, members.Count, covered, false);
    }

    /// <summary>
    /// Collects the told shapes in ONE pass over the module's axioms: the
    /// inverse-functional characteristics over plain roles, the ranges over
    /// plain roles, the one-hop class-to-one-of resolutions read ONLY in the
    /// class-to-one-of direction, the named self-loops and named-endpoint
    /// edges, and the concept-form denials. Every unrecognized axiom is
    /// IGNORED rather than rejecting the module — the refutation is monotone,
    /// so a clash over a recognized subset condemns the whole module and no
    /// closed-world admission is needed. Domain axioms, derived facts, data
    /// properties, ground-form negative assertions, and inverse property
    /// expressions in any role position are never collected. The one rejection
    /// is the absence of any candidate role at all, which leaves nothing to
    /// measure.
    /// </summary>
    /// <param name="module">The module to collect from.</param>
    /// <param name="template">The collected template; <see langword="null"/> when no candidate role was recognized.</param>
    /// <returns><see langword="true"/> when at least one candidate role was recognized.</returns>
    private static bool TryCollectTemplate(ReasoningModule module, [NotNullWhen(true)] out NominalPinnedRoleTemplate? template)
    {
        template = null;

        List<NamedNode> candidateRoles = [];
        HashSet<Utf8String> candidateSet = [];
        List<NominalPinnedRoleRange> ranges = [];
        Dictionary<Utf8String, List<OwlObjectOneOf>> oneOfHops = [];
        Dictionary<Utf8String, HashSet<Utf8String>> selfLoops = [];
        List<NominalPinnedRoleEdge> edges = [];
        List<NominalPinnedRoleDenial> denials = [];
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional, Property: OwlObjectPropertyReference candidate }):
                {
                    if(candidateSet.Add(candidate.Named.Iri))
                    {
                        candidateRoles.Add(candidate.Named);
                    }

                    break;
                }
                case(OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference rangeRole } range):
                {
                    ranges.Add(new NominalPinnedRoleRange(rangeRole.Named.Iri, range.Range));
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalence):
                {
                    CollectEquivalenceHop(equivalence, oneOfHops);
                    break;
                }
                case(OwlSubClassOfAxiom { SubClass: OwlClassReference hopClass, SuperClass: OwlObjectOneOf hopOneOf }):
                {
                    RecordHop(oneOfHops, hopClass.Class.Iri, hopOneOf);
                    break;
                }
                case(OwlObjectPropertyAssertionAxiom { Source: NamedNode source, Target: NamedNode target } assertion):
                {
                    CollectEdge(assertion, source, target, selfLoops, edges);
                    break;
                }
                case(OwlClassAssertionAxiom { Individual: NamedNode carrier, Class: OwlObjectComplementOf { Operand: OwlObjectHasValue { Property: OwlObjectPropertyReference deniedRole, Individual: NamedNode denied } } }):
                {
                    denials.Add(new NominalPinnedRoleDenial(deniedRole.Named.Iri, carrier.Iri, denied.Iri));
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        if(candidateRoles.Count == 0)
        {
            return false;
        }

        template = new NominalPinnedRoleTemplate(candidateRoles, ranges, oneOfHops, selfLoops, edges, denials);

        return true;
    }

    /// <summary>Collects the one-hop resolution a told equivalence supplies: a named class paired with a one-of, in either operand order, since an equivalence supplies the class-to-one-of direction by definition. Any other operand shape carries no hop.</summary>
    /// <param name="axiom">The told equivalence axiom.</param>
    /// <param name="hopsToAppendTo">The one-hop table the resolution is recorded into.</param>
    private static void CollectEquivalenceHop(OwlEquivalentClassesAxiom axiom, Dictionary<Utf8String, List<OwlObjectOneOf>> hopsToAppendTo)
    {
        if(axiom.First is OwlClassReference firstClass && axiom.Second is OwlObjectOneOf secondOneOf)
        {
            RecordHop(hopsToAppendTo, firstClass.Class.Iri, secondOneOf);
        }
        else if(axiom.Second is OwlClassReference secondClass && axiom.First is OwlObjectOneOf firstOneOf)
        {
            RecordHop(hopsToAppendTo, secondClass.Class.Iri, firstOneOf);
        }
    }

    /// <summary>Records one class-to-one-of hop. The subclass route records only the named class in SUBCLASS position — reading a <c>SubClassOf(oneOf, class)</c> in reverse would bound the range from below rather than above and is unsound, so no such hop is ever recorded.</summary>
    /// <param name="hopsToAppendTo">The one-hop table.</param>
    /// <param name="hopClass">The named class's interned IRI.</param>
    /// <param name="oneOf">The one-of the class resolves to.</param>
    private static void RecordHop(Dictionary<Utf8String, List<OwlObjectOneOf>> hopsToAppendTo, Utf8String hopClass, OwlObjectOneOf oneOf)
    {
        if(!hopsToAppendTo.TryGetValue(hopClass, out List<OwlObjectOneOf>? resolutions))
        {
            resolutions = [];
            hopsToAppendTo[hopClass] = resolutions;
        }

        resolutions.Add(oneOf);
    }

    /// <summary>Collects one told edge with named endpoints: every edge lands in the edge list the clash scan reads, and an edge whose source and target intern equal additionally lands in the role's self-loop set the pinning totality consumes.</summary>
    /// <param name="axiom">The told object-property assertion.</param>
    /// <param name="source">The named source individual.</param>
    /// <param name="target">The named target individual.</param>
    /// <param name="selfLoopsToAppendTo">The per-role self-loop sets.</param>
    /// <param name="edgesToAppendTo">The edge list.</param>
    private static void CollectEdge(OwlObjectPropertyAssertionAxiom axiom, NamedNode source, NamedNode target, Dictionary<Utf8String, HashSet<Utf8String>> selfLoopsToAppendTo, List<NominalPinnedRoleEdge> edgesToAppendTo)
    {
        edgesToAppendTo.Add(new NominalPinnedRoleEdge(axiom.Property.Iri, source.Iri, target.Iri));
        if(!source.Iri.Equals(target.Iri))
        {
            return;
        }

        if(!selfLoopsToAppendTo.TryGetValue(axiom.Property.Iri, out HashSet<Utf8String>? loops))
        {
            loops = [];
            selfLoopsToAppendTo[axiom.Property.Iri] = loops;
        }

        loops.Add(source.Iri);
    }
}
