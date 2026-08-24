using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// One raw-spelled role edge <c>S(o, o′)</c> of the per-constant root-tier index: a
/// directioned role symbol and a
/// target individual id, listed under the source individual. The edge is stored under
/// the literal spelling the fact landed in; the ≈-class surface
/// resolves spellings at read time, never at storage.
/// </summary>
/// <param name="RoleSymbol">The directioned role symbol.</param>
/// <param name="Target">The interned target individual id.</param>
internal readonly record struct RootRoleEdge(int RoleSymbol, int Target);

/// <summary>
/// The shared home-slot individual-key resolution of the root-tier co-occurrence spine:
/// a root-context term keys an
/// individual when it is the central variable of a nominal-root context — resolving to
/// the context's home individual, because the entry translation respells the home
/// constant central — or a named-individual term, resolving to its interned id. A
/// function-bearing or variable term keys none. Both the ≈-class feed and the
/// per-constant index key by this one rule, so a spelling is resolved one way only.
/// </summary>
internal static class RootTermResolution
{
    /// <summary>Resolves a root-context term to the individual it keys, or reports it keys none.</summary>
    /// <param name="term">The term standing in a keyed slot.</param>
    /// <param name="homeIndividual">The context's home individual, or <c>-1</c> for the single root and every ordinary context.</param>
    /// <param name="individual">The resolved individual id, when the term keys one.</param>
    /// <returns><see langword="true"/> when the term keys an individual.</returns>
    public static bool TryResolveIndividual(DlTerm term, int homeIndividual, out int individual)
    {
        if(term.IsCentral && homeIndividual >= 0)
        {
            individual = homeIndividual;

            return true;
        }

        if(term.IsIndividual)
        {
            individual = term.IndividualId;

            return true;
        }

        individual = -1;

        return false;
    }
}

/// <summary>
/// The root-tier ≈-class surface: a
/// monotone union-find over interned individual ids, maintained per module run, fed at
/// exactly one site — an unconditional single-literal EQUALITY head landing on a root
/// context through the engine's clause-landing path. Classes only merge, never split;
/// unions keep the lower id as a class's stable representative (union by id order);
/// finds are iterative and path-compressed (an explicit two-pass loop, never call-stack
/// recursion). The surface is dark this step: it registers merges and answers class
/// reads, but no production consumer resolves through it yet — the vr key join and the
/// per-constant data obligations are its later
/// consumers. Off-root-derived equalities relay as <c>A → A</c> tautologies and never
/// reach this feed; that scope is closed later by the backstop, not widened here.
/// </summary>
internal sealed class RootApproxClasses
{
    /// <summary>Each individual id's parent id; a class representative is its own parent. Grown lazily, so an id never touched by a merge reads as its own singleton class.</summary>
    private List<int> Parents { get; } = [];

    /// <summary>The number of individual ids the surface has grown to cover — the highest merged-or-queried id plus one; zero until the first feed.</summary>
    public int NodeCount
    {
        get
        {
            return Parents.Count;
        }
    }

    /// <summary>Whether a landed clause feeds the ≈-class surface and, when it does, the two individual ids to merge — the sole feed decision: the clause is unconditional (empty body) and single-literal with an EQUALITY head whose two sides both resolve to individuals under <see cref="RootTermResolution"/>. A conditional (nonempty body) or disjunctive (multi-literal head) equality is not a decided merge and never feeds; a function-bearing or variable side does not resolve, so the clause does not feed.</summary>
    /// <param name="clause">The landed clause, in its stored spelling.</param>
    /// <param name="homeIndividual">The landing context's home individual, or <c>-1</c> for the single root and every ordinary context.</param>
    /// <param name="first">The first individual id to merge, when the clause feeds.</param>
    /// <param name="second">The second individual id to merge, when the clause feeds.</param>
    /// <returns><see langword="true"/> when the clause is a feedable equality with both sides resolved.</returns>
    public static bool TryResolveMerge(DlClause clause, int homeIndividual, out int first, out int second)
    {
        first = -1;
        second = -1;
        if(clause.BodyLength != 0 || clause.Head.Length != 1)
        {
            return false;
        }

        DlLiteral head = clause.Head[0];
        if(head.Kind != DlLiteralKind.Equality)
        {
            return false;
        }

        return RootTermResolution.TryResolveIndividual(head.First, homeIndividual, out first)
            && RootTermResolution.TryResolveIndividual(head.Second, homeIndividual, out second);
    }

    /// <summary>The class representative of an individual id — the lowest id merged with it — compressing the path to the representative on the way. An id past the grown range reads as its own representative.</summary>
    /// <param name="individual">The individual id to resolve.</param>
    /// <returns>The class representative id.</returns>
    public int Find(int individual)
    {
        EnsureCovers(individual);
        int representative = individual;
        while(Parents[representative] != representative)
        {
            representative = Parents[representative];
        }

        int walk = individual;
        while(Parents[walk] != representative)
        {
            int next = Parents[walk];
            Parents[walk] = representative;
            walk = next;
        }

        return representative;
    }

    /// <summary>Merges the classes of two individual ids, keeping the lower id as the representative (union by id order); a no-op when they already share a class. Monotone — a merge never splits an existing class.</summary>
    /// <param name="first">A member of the first class.</param>
    /// <param name="second">A member of the second class.</param>
    public void Union(int first, int second)
    {
        int firstRoot = Find(first);
        int secondRoot = Find(second);
        if(firstRoot == secondRoot)
        {
            return;
        }

        if(secondRoot < firstRoot)
        {
            (firstRoot, secondRoot) = (secondRoot, firstRoot);
        }

        Parents[secondRoot] = firstRoot;
    }

    /// <summary>Whether two individual ids share a ≈-class.</summary>
    /// <param name="first">The first individual id.</param>
    /// <param name="second">The second individual id.</param>
    /// <returns><see langword="true"/> when both resolve to the same representative.</returns>
    public bool SameClass(int first, int second)
    {
        return Find(first) == Find(second);
    }

    /// <summary>Appends the individual ids sharing an id's ≈-class — the class's spellings a read-time union walks — to a reusable buffer, the queried id included. Dark this step: no production consumer reads the enumeration yet.</summary>
    /// <param name="individual">The individual id whose class is enumerated.</param>
    /// <param name="spellingsToAppendTo">The buffer the class's individual ids are appended to.</param>
    public void AppendClassMembers(int individual, List<int> spellingsToAppendTo)
    {
        int representative = Find(individual);
        for(int candidate = 0; candidate < Parents.Count; candidate++)
        {
            if(Find(candidate) == representative)
            {
                spellingsToAppendTo.Add(candidate);
            }
        }
    }

    /// <summary>Grows the parent list to cover an id, seeding every new slot as its own singleton representative.</summary>
    /// <param name="individual">The id the list must cover.</param>
    private void EnsureCovers(int individual)
    {
        while(Parents.Count <= individual)
        {
            Parents.Add(Parents.Count);
        }
    }
}

/// <summary>
/// The per-constant root-tier index: a
/// per-individual projection of a root context's unconditional single-literal facts in
/// three families — concept memberships <c>B(o)</c>, role edges <c>S(o, o′)</c>, and
/// data-demand markers <c>D(o)</c> as a retraction-aware live count. Facts are stored
/// RAW-SPELLED, under the individual key the head literal names (the home slot resolves
/// the central variable to the context's home individual through
/// <see cref="RootTermResolution"/>); ≈-resolution happens at read time through the
/// ≈-class surface, so the index carries one mechanism with no duplication. Every
/// family is CLEAN-ON-TOMBSTONE — a retracted fact leaves no readable trace, riding the
/// same at-most-one-live-clause-per-unconditional-head invariant the
/// <c>UnconditionalHeads</c> set relies on — so a tombstoned spelling never ghosts into
/// a spurious merge or a pooled demand. The <c>D(o)</c> family is a required new
/// counterpart, not a reuse of <c>Context.DataDemands</c>, which is blind to
/// constant-spelled root demands. The index is minted lazily with the first projected
/// fact, so a nominal-free module — whose contexts are never root — allocates none.
/// Dark this step: the key join and the per-constant data obligations are its consumers.
/// </summary>
internal sealed class RootConstantIndex
{
    /// <summary>The concept symbols asserted of each individual — the <c>B(o)</c> family. At most one live clause carries each unconditional head <c>B(o)</c>, so a symbol reads present exactly while its clause is live.</summary>
    private Dictionary<int, HashSet<int>> ConceptSymbolsByIndividual { get; } = [];

    /// <summary>The role edges leaving each source individual — the <c>S(o, o′)</c> family, targets listed. At most one live clause carries each unconditional head <c>S(o, o′)</c>, so an edge reads present exactly while its clause is live.</summary>
    private Dictionary<int, List<RootRoleEdge>> RoleEdgesBySource { get; } = [];

    /// <summary>The live count of each individual's data-demand markers — the <c>D(o)</c> family, keyed by (individual, marker): a projection increments, a retraction decrements, a positive count reads live (the <c>Context.DataDemands</c> count&gt;0 discipline, per constant).</summary>
    private Dictionary<(int Individual, int Marker), int> DataDemandCounts { get; } = [];

    /// <summary>Projects an unconditional single-literal head under its individual key: a concept head whose symbol is a data-demand marker files under <c>D(o)</c>; any other concept head under <c>B(o)</c>; a role head under <c>S(o, o′)</c> when both ends resolve. A head whose keyed slot does not resolve to an individual files nothing.</summary>
    /// <param name="head">The clause's single head literal, in its stored spelling.</param>
    /// <param name="homeIndividual">The context's home individual, or <c>-1</c>.</param>
    /// <param name="dataDemandMarkers">The concept atoms counted as data-demand markers.</param>
    public void Project(DlLiteral head, int homeIndividual, IReadOnlySet<int> dataDemandMarkers)
    {
        if(head.Kind == DlLiteralKind.Concept && RootTermResolution.TryResolveIndividual(head.First, homeIndividual, out int subject))
        {
            if(dataDemandMarkers.Contains(head.Symbol))
            {
                DataDemandCounts[(subject, head.Symbol)] = DataDemandCount(subject, head.Symbol) + 1;
            }
            else
            {
                AddConceptMembership(subject, head.Symbol);
            }

            return;
        }

        if(head.Kind == DlLiteralKind.Role
            && RootTermResolution.TryResolveIndividual(head.First, homeIndividual, out int source)
            && RootTermResolution.TryResolveIndividual(head.Second, homeIndividual, out int target))
        {
            AddRoleEdge(source, head.Symbol, target);
        }
    }

    /// <summary>Retracts a tombstoned unconditional single-literal head from its individual key (clean-on-tombstone) — the exact inverse of <see cref="Project"/> by the same stored spelling: the demand count decrements, the concept symbol or role edge drops.</summary>
    /// <param name="head">The tombstoned clause's single head literal, in its stored spelling.</param>
    /// <param name="homeIndividual">The context's home individual, or <c>-1</c>.</param>
    /// <param name="dataDemandMarkers">The concept atoms counted as data-demand markers.</param>
    public void Retract(DlLiteral head, int homeIndividual, IReadOnlySet<int> dataDemandMarkers)
    {
        if(head.Kind == DlLiteralKind.Concept && RootTermResolution.TryResolveIndividual(head.First, homeIndividual, out int subject))
        {
            if(dataDemandMarkers.Contains(head.Symbol))
            {
                DecrementDataDemand(subject, head.Symbol);
            }
            else
            {
                RemoveConceptMembership(subject, head.Symbol);
            }

            return;
        }

        if(head.Kind == DlLiteralKind.Role
            && RootTermResolution.TryResolveIndividual(head.First, homeIndividual, out int source)
            && RootTermResolution.TryResolveIndividual(head.Second, homeIndividual, out int target))
        {
            RemoveRoleEdge(source, head.Symbol, target);
        }
    }

    /// <summary>Appends an individual's concept memberships <c>B(o)</c>, raw-spelled, to a reusable buffer; nothing when the individual has no live membership.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="symbolsToAppendTo">The buffer the concept symbols are appended to.</param>
    public void AppendConceptMemberships(int individual, List<int> symbolsToAppendTo)
    {
        if(ConceptSymbolsByIndividual.TryGetValue(individual, out HashSet<int>? symbols))
        {
            foreach(int symbol in symbols)
            {
                symbolsToAppendTo.Add(symbol);
            }
        }
    }

    /// <summary>Appends an individual's outgoing role edges <c>S(o, o′)</c>, raw-spelled, to a reusable buffer; nothing when the individual has no live edge.</summary>
    /// <param name="individual">The source individual key.</param>
    /// <param name="edgesToAppendTo">The buffer the role edges are appended to.</param>
    public void AppendRoleTargets(int individual, List<RootRoleEdge> edgesToAppendTo)
    {
        if(RoleEdgesBySource.TryGetValue(individual, out List<RootRoleEdge>? edges))
        {
            edgesToAppendTo.AddRange(edges);
        }
    }

    /// <summary>The live count of an individual's data-demand marker <c>D(o)</c>; zero when no live clause carries the demand.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="marker">The data-demand marker concept atom.</param>
    /// <returns>The live count, zero when absent.</returns>
    public int DataDemandCount(int individual, int marker)
    {
        return DataDemandCounts.TryGetValue((individual, marker), out int count) ? count : 0;
    }

    /// <summary>Adds a concept symbol to an individual's membership set, creating the set on first use.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="symbol">The concept atom.</param>
    private void AddConceptMembership(int individual, int symbol)
    {
        if(!ConceptSymbolsByIndividual.TryGetValue(individual, out HashSet<int>? symbols))
        {
            symbols = [];
            ConceptSymbolsByIndividual[individual] = symbols;
        }

        symbols.Add(symbol);
    }

    /// <summary>Removes a concept symbol from an individual's membership set — clean-on-tombstone; a no-op when the set is absent.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="symbol">The concept atom.</param>
    private void RemoveConceptMembership(int individual, int symbol)
    {
        if(ConceptSymbolsByIndividual.TryGetValue(individual, out HashSet<int>? symbols))
        {
            symbols.Remove(symbol);
        }
    }

    /// <summary>Adds a role edge under its source individual, creating the target list on first use.</summary>
    /// <param name="source">The source individual key.</param>
    /// <param name="roleSymbol">The directioned role symbol.</param>
    /// <param name="target">The target individual id.</param>
    private void AddRoleEdge(int source, int roleSymbol, int target)
    {
        if(!RoleEdgesBySource.TryGetValue(source, out List<RootRoleEdge>? edges))
        {
            edges = [];
            RoleEdgesBySource[source] = edges;
        }

        edges.Add(new RootRoleEdge(roleSymbol, target));
    }

    /// <summary>Removes a role edge under its source individual — clean-on-tombstone; the first matching (role, target) entry, exact since at most one live clause carries the head.</summary>
    /// <param name="source">The source individual key.</param>
    /// <param name="roleSymbol">The directioned role symbol.</param>
    /// <param name="target">The target individual id.</param>
    private void RemoveRoleEdge(int source, int roleSymbol, int target)
    {
        if(RoleEdgesBySource.TryGetValue(source, out List<RootRoleEdge>? edges))
        {
            edges.Remove(new RootRoleEdge(roleSymbol, target));
        }
    }

    /// <summary>Decrements an individual's data-demand marker count — clean-on-tombstone; a no-op when no count is recorded.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="marker">The data-demand marker concept atom.</param>
    private void DecrementDataDemand(int individual, int marker)
    {
        if(DataDemandCounts.TryGetValue((individual, marker), out int count))
        {
            DataDemandCounts[(individual, marker)] = count - 1;
        }
    }
}
