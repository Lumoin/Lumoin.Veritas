using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape S clash reason family — the spy-point counterpart of the nominal clash reasons: a stable leading identifier the statistics assembly and the battery discriminate on.</summary>
internal static class SpyPointClashReasons
{
    /// <summary>The clash for a told domain bound below the told demand: the funnel drives the whole domain into the union of the one-of members' capped successor sets, and a told minimum-cardinality demand asks for more elements than that union admits.</summary>
    /// <param name="funnelRole">The funnel role whose cap sum was outrun.</param>
    /// <returns>The named reason.</returns>
    public static string SpyPointDomainPigeonhole(Utf8String funnelRole)
    {
        return $"SpyPointDomainPigeonhole({funnelRole})";
    }
}

/// <summary>
/// The Shape S window measurement the census-first recognizer's
/// pre-clausification pass reads on every spy-point-jurisdiction module —
/// computed with the member deduplication applied BEFORE any boundary
/// comparison, so the battery's near-miss rows can pin the measured quantity
/// independently of the comparison's outcome.
/// </summary>
/// <param name="MemberCount">The largest recognized funnel's distinct named one-of members <c>n</c>, deduplicated by individual identity; zero when no funnel was recognized.</param>
/// <param name="CapBound">The tightest recognized domain bound — the sum of the per-member minimum caps over one funnel and one cap role, in long arithmetic; zero when no funnel and cap role paired.</param>
/// <param name="DemandBound">The effective demand beside <see cref="CapBound"/> — the largest told minimum-cardinality demand, never below the nonempty domain's own demand of one; zero when no funnel and cap role paired.</param>
/// <param name="MemberSilences">The funnels skipped for carrying more than <see cref="ContextSpyPointDecider.SpyPointMemberBound"/> distinct members — a named silence, never a verdict over an unsummed member window; zero otherwise.</param>
internal readonly record struct SpyPointWindow(
    int MemberCount,
    long CapBound,
    long DemandBound,
    int MemberSilences)
{
    /// <summary>The empty window: no spy-point funnel was recognized.</summary>
    public static SpyPointWindow Empty => default;
}

/// <summary>The Shape S decider's outcome: the closed-form refutation when every jurisdiction condition held inside the window, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The closed-form verdict — <see langword="false"/> for the domain-bound pigeonhole refutation — or <see langword="null"/> when the face is silent on the module. The face has no certify direction, so <see langword="true"/> never occurs.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct SpyPointOutcome(bool? Consistent, SpyPointWindow Window)
{
    /// <summary>The named clash reason on a refutation; <see langword="null"/> on every silent outcome.</summary>
    public string? ClashReason { get; init; }

    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static SpyPointOutcome SilentWith(SpyPointWindow window)
    {
        return new SpyPointOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's spy-point domain-bound clash face
/// (face nine): a tier-1 CLOSED FORM over the told axiom surfaces of a
/// spy-point encoding — <c>owl:Thing</c> subsumed by an existential over a role
/// <c>f</c> into a one-of of named members, every member carrying an unqualified
/// told max-cardinality cap on a role inverse to <c>f</c> (a told
/// <c>InverseObjectProperties</c> partner of a plain <c>f</c>, or the inverted
/// role itself under an inline <c>ObjectInverseOf</c>), beside a told
/// unqualified minimum-cardinality demand at an asserted individual. The funnel
/// drives EVERY domain element into the union of the members' capped successor
/// sets, so the domain is bounded by the sum of the told caps — collisions among
/// the members' denotations only shrink that union, no unique-name assumption
/// used — while the demand forces at least <c>m</c> elements and the nonempty
/// domain forces at least one. With <c>k</c> the tightest cap sum,
/// <c>max(1, m) &gt; k</c> refutes every model by pigeonhole. The face is
/// CLASH-ONLY: <c>max(1, m) &lt;= k</c> proves nothing about the surrounding
/// module, so the face is silent and ordinary saturation owns the verdict.
/// Sound-or-silent and told-only, with a MONOTONE jurisdiction: unrecognized
/// axioms are IGNORED rather than rejecting the module, because extra axioms
/// only shrink the model class and can never rescue a refuted subset. Every
/// unmet condition inside the recognized shapes — a qualified cap, a cap on a
/// role no told inverse links to the funnel, a cap carried by a non-member, an
/// uncapped member, an anonymous member, a data-side demand, a demand more than
/// one subclass hop from its asserted class — leaves the module to ordinary
/// saturation. The member ceiling is a named window constant; outside it the
/// face is silent with the measured numbers already on the record.
/// </summary>
internal static class ContextSpyPointDecider
{
    /// <summary>
    /// The one-of member ceiling: the domain bound is summed exactly up to this
    /// many distinct named members per funnel and the funnel is SKIPPED above it.
    /// Derivation (engineering, with the cost formula the battery pins): the sum
    /// is at most sixteen dictionary probes and sixteen long additions per
    /// funnel and cap role, and the value matches the counting faces' shared
    /// sixteen ceiling — the counted-population, ground-clique, partition-anchor,
    /// gadget-atom, and pair-assignment bounds — so every counting-family
    /// pre-engine face carries one boundary discipline; the repairing face
    /// carries its own wider windows sized by its habitat. Collecting the told
    /// shapes is one linear pass bounded by the module's own axiom count
    /// rather than by this constant.
    /// </summary>
    public const int SpyPointMemberBound = 16;

    /// <summary>Measures the Shape S census window without deciding anything: the largest recognized funnel's distinct member count, the tightest domain bound, the effective demand beside it, and the member-window silences the bound would charge — computed identically dark and lit, so the census ships unconditionally. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement; all-zero when no funnel was recognized.</returns>
    public static SpyPointOutcome Measure(ReasoningModule module)
    {
        return TryCollectTemplate(module, out SpyPointTemplate? template)
            ? SpyPointOutcome.SilentWith(MeasureTemplate(template).Window)
            : SpyPointOutcome.SilentWith(SpyPointWindow.Empty);
    }

    /// <summary>
    /// Runs the spy-point domain-bound clash face: the told-shape collection, the
    /// per-funnel cap summation inside the member window, and then the single
    /// integer comparison that refutes the module. The measurement lands first in
    /// every case, so a window or totality silence still carries the numbers, and
    /// the face returns <see langword="false"/> or silence only — never a
    /// consistency certificate.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the closed-form refutation with its measurement, or silence.</returns>
    public static SpyPointOutcome Run(ReasoningModule module)
    {
        if(!TryCollectTemplate(module, out SpyPointTemplate? template))
        {
            return SpyPointOutcome.SilentWith(SpyPointWindow.Empty);
        }

        SpyPointMeasurement measurement = MeasureTemplate(template);
        if(measurement.TightestRole is null || measurement.Window.DemandBound <= measurement.Window.CapBound)
        {
            return SpyPointOutcome.SilentWith(measurement.Window);
        }

        return new SpyPointOutcome(false, measurement.Window)
        {
            ClashReason = SpyPointClashReasons.SpyPointDomainPigeonhole(measurement.TightestRole.Iri),
        };
    }

    /// <summary>One recognized funnel: the role the existential drives the domain along, whether the told spelling was an inline inverse, and the funnel's distinct named one-of members in first-seen order.</summary>
    /// <param name="Role">The funnel role — the plain role under a forward existential, or the inverted role under an inline <c>ObjectInverseOf</c>.</param>
    /// <param name="RoleIsInverse">Whether the told funnel spelled the role as an inline <c>ObjectInverseOf</c>, in which case the cap role is the inverted role itself.</param>
    /// <param name="Members">The distinct named one-of members, deduplicated by individual identity in first-seen order.</param>
    private readonly record struct SpyPointFunnel(NamedNode Role, bool RoleIsInverse, List<Utf8String> Members);

    /// <summary>The measurement one template yields: the census window, and the funnel role whose cap sum is the tightest recognized domain bound.</summary>
    /// <param name="Window">The census window.</param>
    /// <param name="TightestRole">The funnel role behind <see cref="SpyPointWindow.CapBound"/>; <see langword="null"/> when no funnel and cap role paired, in which case no comparison is available.</param>
    private readonly record struct SpyPointMeasurement(SpyPointWindow Window, NamedNode? TightestRole);

    /// <summary>The collected told shapes: the recognized funnels, the told inverse-role relation both directions, the per-member per-role minimum caps, and the largest told minimum-cardinality demand.</summary>
    /// <param name="Funnels">The recognized funnels.</param>
    /// <param name="InverseRoles">The told inverse-role relation, recorded in both argument orders over plain roles only.</param>
    /// <param name="Caps">The recognized caps: member individual to cap role to the MINIMUM told bound over every cap recognized for that pair.</param>
    /// <param name="DemandBound">The largest told minimum-cardinality demand <c>m</c>; zero when no demand shape was recognized, which the effective demand of one still covers.</param>
    private sealed record SpyPointTemplate(
        List<SpyPointFunnel> Funnels,
        Dictionary<Utf8String, List<Utf8String>> InverseRoles,
        Dictionary<Utf8String, Dictionary<Utf8String, int>> Caps,
        int DemandBound);

    /// <summary>
    /// Reads the window off a collected template: the largest recognized
    /// funnel's member count, the member-window silences, and the tightest
    /// domain bound over every (funnel, cap role) pair whose every member
    /// carries a recognized cap — the cap totality the union bound needs, since
    /// one uncapped seat leaves the domain unbounded. An inline-inverse funnel
    /// has the inverted role as its single cap role; a plain funnel has every
    /// told inverse partner, each evaluated independently. The sum runs in long
    /// arithmetic, so sixteen maximal int caps cannot overflow the bound.
    /// </summary>
    /// <param name="template">The collected template.</param>
    /// <returns>The measurement.</returns>
    private static SpyPointMeasurement MeasureTemplate(SpyPointTemplate template)
    {
        int memberCount = 0;
        int memberSilences = 0;
        long tightestBound = 0;
        NamedNode? tightestRole = null;
        for(int index = 0; index < template.Funnels.Count; index++)
        {
            SpyPointFunnel funnel = template.Funnels[index];
            memberCount = Math.Max(memberCount, funnel.Members.Count);
            if(funnel.Members.Count > SpyPointMemberBound)
            {
                memberSilences++;
                continue;
            }

            if(funnel.RoleIsInverse)
            {
                if(TrySumCaps(template, funnel, funnel.Role.Iri, out long inlineBound) && (tightestRole is null || inlineBound < tightestBound))
                {
                    tightestBound = inlineBound;
                    tightestRole = funnel.Role;
                }

                continue;
            }

            if(!template.InverseRoles.TryGetValue(funnel.Role.Iri, out List<Utf8String>? partners))
            {
                continue;
            }

            for(int partner = 0; partner < partners.Count; partner++)
            {
                if(TrySumCaps(template, funnel, partners[partner], out long partnerBound) && (tightestRole is null || partnerBound < tightestBound))
                {
                    tightestBound = partnerBound;
                    tightestRole = funnel.Role;
                }
            }
        }

        long demand = Math.Max(1L, template.DemandBound);
        SpyPointWindow window = new(
            memberCount,
            tightestRole is null ? 0L : tightestBound,
            tightestRole is null ? 0L : demand,
            memberSilences);

        return new SpyPointMeasurement(window, tightestRole);
    }

    /// <summary>Sums one funnel's per-member caps for one cap role in long arithmetic, demanding a recognized cap on EVERY member: a single uncapped seat leaves the union bound unproven and silences the pairing.</summary>
    /// <param name="template">The collected template.</param>
    /// <param name="funnel">The funnel whose members are summed.</param>
    /// <param name="capRole">The cap role.</param>
    /// <param name="bound">The summed domain bound; zero when a member was uncapped.</param>
    /// <returns><see langword="true"/> when every member carried a cap for the role.</returns>
    private static bool TrySumCaps(SpyPointTemplate template, SpyPointFunnel funnel, Utf8String capRole, out long bound)
    {
        bound = 0;
        for(int index = 0; index < funnel.Members.Count; index++)
        {
            if(!template.Caps.TryGetValue(funnel.Members[index], out Dictionary<Utf8String, int>? roleCaps)
                || !roleCaps.TryGetValue(capRole, out int memberBound))
            {
                bound = 0;

                return false;
            }

            bound += memberBound;
        }

        return true;
    }

    /// <summary>
    /// Collects the told shapes in ONE pass over the module's axioms: the
    /// <c>owl:Thing</c> funnels into a named one-of, the told inverse-role pairs
    /// over plain roles, the caps on both told routes, and the demands on both
    /// told routes. Every unrecognized axiom is IGNORED rather than rejecting the
    /// module — the refutation is monotone, so a clash over a recognized subset
    /// condemns the whole module and no closed-world admission is needed. The one
    /// rejection is the absence of any funnel at all, which leaves nothing to
    /// measure.
    /// </summary>
    /// <param name="module">The module to collect from.</param>
    /// <param name="template">The collected template; <see langword="null"/> when no funnel was recognized.</param>
    /// <returns><see langword="true"/> when at least one funnel was recognized.</returns>
    private static bool TryCollectTemplate(ReasoningModule module, [NotNullWhen(true)] out SpyPointTemplate? template)
    {
        template = null;

        List<SpyPointFunnel> funnels = [];
        Dictionary<Utf8String, List<Utf8String>> inverseRoles = [];
        Dictionary<Utf8String, Dictionary<Utf8String, int>> caps = [];
        Dictionary<Utf8String, int> classDemands = [];
        HashSet<Utf8String> assertedClasses = [];
        int demandBound = 0;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlSubClassOfAxiom subClass):
                {
                    CollectFunnel(subClass, funnels);
                    CollectSubClassCap(subClass, caps);
                    CollectClassDemand(subClass, classDemands);
                    break;
                }
                case(OwlClassAssertionAxiom assertion):
                {
                    CollectAssertionCap(assertion, caps);
                    CollectAssertionDemand(assertion, assertedClasses, ref demandBound);
                    break;
                }
                case(OwlInverseObjectPropertiesAxiom inverse):
                {
                    CollectInversePair(inverse, inverseRoles);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        if(funnels.Count == 0)
        {
            return false;
        }

        foreach(Utf8String assertedClass in assertedClasses)
        {
            if(classDemands.TryGetValue(assertedClass, out int hopDemand))
            {
                demandBound = Math.Max(demandBound, hopDemand);
            }
        }

        template = new SpyPointTemplate(funnels, inverseRoles, caps, demandBound);

        return true;
    }

    /// <summary>
    /// Collects one told funnel: <c>owl:Thing</c> subsumed by a TOP-LEVEL
    /// existential whose filler is a one-of of at least one member, every member
    /// NAMED. A funnel under an intersection, union, or any other combinator
    /// bounds a subset rather than the domain and never matches; a single
    /// anonymous member drops the funnel WHOLE, since an anonymous member can
    /// carry no told cap and a partial union bounds nothing. The members are
    /// deduplicated by individual identity, so a repeated name is one seat in the
    /// union rather than two summands.
    /// </summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="funnelsToAppendTo">The funnel list the recognized funnel is appended to.</param>
    private static void CollectFunnel(OwlSubClassOfAxiom axiom, List<SpyPointFunnel> funnelsToAppendTo)
    {
        if(!ContextHabitatRecognizer.IsThingReference(axiom.SubClass)
            || axiom.SuperClass is not OwlObjectSomeValuesFrom { Filler: OwlObjectOneOf oneOf } existential
            || oneOf.Individuals.Count == 0)
        {
            return;
        }

        List<Utf8String> members = [];
        HashSet<Utf8String> distinct = [];
        for(int index = 0; index < oneOf.Individuals.Count; index++)
        {
            if(oneOf.Individuals[index] is not NamedNode member)
            {
                return;
            }

            if(distinct.Add(member.Iri))
            {
                members.Add(member.Iri);
            }
        }

        funnelsToAppendTo.Add(new SpyPointFunnel(existential.Property.Property, existential.Property.IsInverse, members));
    }

    /// <summary>Collects the told inverse-role relation in BOTH argument orders, over plain roles only: an inline <c>ObjectInverseOf</c> argument would need role normalization this face does not perform, so it carries no link. A self-inverse pairing records one link, the symmetric role the union bound reads unchanged.</summary>
    /// <param name="axiom">The told inverse-properties axiom.</param>
    /// <param name="inverseRolesToAppendTo">The relation the pair is recorded into.</param>
    private static void CollectInversePair(OwlInverseObjectPropertiesAxiom axiom, Dictionary<Utf8String, List<Utf8String>> inverseRolesToAppendTo)
    {
        if(axiom.First is not OwlObjectPropertyReference first || axiom.Second is not OwlObjectPropertyReference second)
        {
            return;
        }

        LinkInverseDirection(inverseRolesToAppendTo, first.Named.Iri, second.Named.Iri);
        LinkInverseDirection(inverseRolesToAppendTo, second.Named.Iri, first.Named.Iri);
    }

    /// <summary>Records one direction of the told inverse-role relation, skipping a partner the direction already carries.</summary>
    /// <param name="inverseRolesToAppendTo">The relation.</param>
    /// <param name="role">The role the direction is keyed on.</param>
    /// <param name="partner">The partner role.</param>
    private static void LinkInverseDirection(Dictionary<Utf8String, List<Utf8String>> inverseRolesToAppendTo, Utf8String role, Utf8String partner)
    {
        if(!inverseRolesToAppendTo.TryGetValue(role, out List<Utf8String>? partners))
        {
            partners = [];
            inverseRolesToAppendTo[role] = partners;
        }

        for(int index = 0; index < partners.Count; index++)
        {
            if(partners[index].Equals(partner))
            {
                return;
            }
        }

        partners.Add(partner);
    }

    /// <summary>Collects cap route (b): a told subclass axiom from a one-of to an UNQUALIFIED max-cardinality over a plain role caps every NAMED member the one-of lists. A qualified cap bounds only the filler's successors and leaves the rest of the domain uncounted, so it never matches.</summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="capsToAppendTo">The cap relation.</param>
    private static void CollectSubClassCap(OwlSubClassOfAxiom axiom, Dictionary<Utf8String, Dictionary<Utf8String, int>> capsToAppendTo)
    {
        if(axiom.SubClass is not OwlObjectOneOf oneOf || !TryReadCap(axiom.SuperClass, out NamedNode? capRole, out int bound))
        {
            return;
        }

        for(int index = 0; index < oneOf.Individuals.Count; index++)
        {
            if(oneOf.Individuals[index] is NamedNode member)
            {
                RecordCap(capsToAppendTo, member.Iri, capRole.Iri, bound);
            }
        }
    }

    /// <summary>Collects cap route (a): a told class assertion typing a NAMED individual with an UNQUALIFIED max-cardinality over a plain role caps that individual. An anonymous carrier can never be a named funnel member, so it carries no cap.</summary>
    /// <param name="axiom">The candidate class assertion.</param>
    /// <param name="capsToAppendTo">The cap relation.</param>
    private static void CollectAssertionCap(OwlClassAssertionAxiom axiom, Dictionary<Utf8String, Dictionary<Utf8String, int>> capsToAppendTo)
    {
        if(axiom.Individual is not NamedNode carrier || !TryReadCap(axiom.Class, out NamedNode? capRole, out int bound))
        {
            return;
        }

        RecordCap(capsToAppendTo, carrier.Iri, capRole.Iri, bound);
    }

    /// <summary>Whether a class expression is the cap shape: a TOP-LEVEL unqualified max-cardinality restriction of nonnegative bound over a PLAIN named role. An inverse cap role would need role normalization this face does not perform.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="capRole">The cap role; <see langword="null"/> when the shape did not match.</param>
    /// <param name="bound">The told cap bound <c>k</c>; zero when the shape did not match.</param>
    /// <returns><see langword="true"/> on the exact cap shape.</returns>
    private static bool TryReadCap(OwlClassExpression expression, [NotNullWhen(true)] out NamedNode? capRole, out int bound)
    {
        capRole = null;
        bound = 0;
        if(expression is not OwlObjectCardinality { Kind: OwlCardinalityKind.Max, Property: OwlObjectPropertyReference reference } cap
            || !ContextHabitatRecognizer.IsUnqualifiedFiller(cap.Filler)
            || cap.Cardinality < 0)
        {
            return false;
        }

        capRole = reference.Named;
        bound = cap.Cardinality;

        return true;
    }

    /// <summary>Records one told cap, keeping the MINIMUM bound over every cap recognized for the same member and role — the tightest told bound is the one the union bound may rely on.</summary>
    /// <param name="capsToAppendTo">The cap relation.</param>
    /// <param name="member">The capped individual.</param>
    /// <param name="capRole">The cap role.</param>
    /// <param name="bound">The told bound.</param>
    private static void RecordCap(Dictionary<Utf8String, Dictionary<Utf8String, int>> capsToAppendTo, Utf8String member, Utf8String capRole, int bound)
    {
        if(!capsToAppendTo.TryGetValue(member, out Dictionary<Utf8String, int>? roleCaps))
        {
            roleCaps = [];
            capsToAppendTo[member] = roleCaps;
        }

        roleCaps[capRole] = roleCaps.TryGetValue(capRole, out int recorded) ? Math.Min(recorded, bound) : bound;
    }

    /// <summary>Collects the second half of demand route (a): a told subclass axiom from a NAMED class to a TOP-LEVEL unqualified minimum-cardinality restriction, keyed on the class so an asserted individual one hop away resolves it. Deeper chains are not walked, and a qualified minimum is filler-disciplined out.</summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="classDemandsToAppendTo">The per-class demand relation.</param>
    private static void CollectClassDemand(OwlSubClassOfAxiom axiom, Dictionary<Utf8String, int> classDemandsToAppendTo)
    {
        if(axiom.SubClass is not OwlClassReference reference || !TryReadDemand(axiom.SuperClass, out int bound))
        {
            return;
        }

        classDemandsToAppendTo[reference.Class.Iri] = classDemandsToAppendTo.TryGetValue(reference.Class.Iri, out int recorded)
            ? Math.Max(recorded, bound)
            : bound;
    }

    /// <summary>Collects demand route (b) — a told class assertion typing an individual DIRECTLY with a minimum-cardinality restriction — and the first half of route (a), the named class an individual is typed with. The carrier may be named or anonymous: a blank-node individual is an ordinary domain element in every model, so its demand forces the same successors.</summary>
    /// <param name="axiom">The candidate class assertion.</param>
    /// <param name="assertedClassesToAppendTo">The named classes some individual is typed with.</param>
    /// <param name="demandBound">The largest demand recognized so far.</param>
    private static void CollectAssertionDemand(OwlClassAssertionAxiom axiom, HashSet<Utf8String> assertedClassesToAppendTo, ref int demandBound)
    {
        if(axiom.Individual is not NamedNode and not BlankNode)
        {
            return;
        }

        if(axiom.Class is OwlClassReference reference)
        {
            assertedClassesToAppendTo.Add(reference.Class.Iri);

            return;
        }

        if(TryReadDemand(axiom.Class, out int bound))
        {
            demandBound = Math.Max(demandBound, bound);
        }
    }

    /// <summary>Whether a class expression is the demand shape: a TOP-LEVEL unqualified minimum-cardinality restriction of nonnegative bound over any object-property expression — the successor argument is never inspected, since a minimum over an inverse role forces the same count of domain elements. The structural type excludes data cardinalities, which bound literals rather than domain elements.</summary>
    /// <param name="expression">The candidate expression.</param>
    /// <param name="bound">The told demand <c>m</c>; zero when the shape did not match.</param>
    /// <returns><see langword="true"/> on the exact demand shape.</returns>
    private static bool TryReadDemand(OwlClassExpression expression, out int bound)
    {
        bound = 0;
        if(expression is not OwlObjectCardinality { Kind: OwlCardinalityKind.Min } demand
            || !ContextHabitatRecognizer.IsUnqualifiedFiller(demand.Filler)
            || demand.Cardinality < 0)
        {
            return false;
        }

        bound = demand.Cardinality;

        return true;
    }
}
