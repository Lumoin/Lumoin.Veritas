using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The subset-subsumption primitives of the redundancy relation <c>∈̂</c>
/// (KR 2016, Definition 4, Horn
/// slice; <see href="https://arxiv.org/abs/1602.04498"/>): a clause is contained
/// up to redundancy in a context when some live clause has a body and head that
/// are subsets of it. Body and head spans are canonical (sorted, de-duplicated),
/// so a subset test is a single linear merge-walk with no allocation.
/// </summary>
internal static class ClauseRedundancy
{
    /// <summary>Whether every literal of <paramref name="candidateSubset"/> occurs in <paramref name="superset"/>, both canonical ascending spans, by a linear merge-walk.</summary>
    /// <param name="candidateSubset">The span tested for inclusion.</param>
    /// <param name="superset">The span tested as the container.</param>
    /// <returns><see langword="true"/> when the first span is a subset of the second.</returns>
    public static bool IsSubset(ReadOnlySpan<DlLiteral> candidateSubset, ReadOnlySpan<DlLiteral> superset)
    {
        int subsetIndex = 0;
        int supersetIndex = 0;
        while(subsetIndex < candidateSubset.Length)
        {
            if(supersetIndex >= superset.Length)
            {
                return false;
            }

            int comparison = candidateSubset[subsetIndex].CompareTo(superset[supersetIndex]);
            if(comparison == 0)
            {
                subsetIndex++;
                supersetIndex++;
            }
            else if(comparison > 0)
            {
                supersetIndex++;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="subsumer"/> subsumes <paramref name="candidate"/> under Definition 4: its body and head are both subsets of the candidate's.</summary>
    /// <param name="subsumer">The clause tested as the stronger (more general) clause.</param>
    /// <param name="candidate">The clause tested as the subsumed clause.</param>
    /// <returns><see langword="true"/> when the subsumer's body and head are subsets of the candidate's.</returns>
    public static bool Subsumes(DlClause subsumer, DlClause candidate)
    {
        return Subsumes(subsumer.Body, subsumer.Head, candidate.Body, candidate.Head);
    }

    /// <summary>Whether a subsumer's canonical body and head spans subsume a candidate's under Definition 4 — the span face the containment probe runs on a conclusion that has not been built into a clause; the clause face forwards to it, so provenance plays no part in either.</summary>
    /// <param name="subsumerBody">The subsumer's canonical body span.</param>
    /// <param name="subsumerHead">The subsumer's canonical head span.</param>
    /// <param name="candidateBody">The candidate's canonical body span.</param>
    /// <param name="candidateHead">The candidate's canonical head span.</param>
    /// <returns><see langword="true"/> when both subsumer spans are subsets of the matching candidate spans.</returns>
    public static bool Subsumes(ReadOnlySpan<DlLiteral> subsumerBody, ReadOnlySpan<DlLiteral> subsumerHead, ReadOnlySpan<DlLiteral> candidateBody, ReadOnlySpan<DlLiteral> candidateHead)
    {
        return IsSubset(subsumerBody, candidateBody) && IsSubset(subsumerHead, candidateHead);
    }
}

/// <summary>
/// The span-shaped lookup key of a would-be clause: its canonical body and head
/// spans and the <see cref="DlClause.Origin"/> a materialisation would carry.
/// The exact-duplicate fast check reads it through the live set's alternate
/// lookup, so a conclusion that survives no gate is never built. Body and head
/// alone carry the key's identity — <see cref="Origin"/> is provenance, read
/// only where the key materialises a clause, exactly as
/// <see cref="DlClause.Equals(DlClause)"/> ignores it.
/// </summary>
internal readonly ref struct DlClauseSpanKey
{
    /// <summary>Initialises the key over a conclusion's canonical spans and the origin a materialisation would stamp.</summary>
    /// <param name="body">The canonical body span.</param>
    /// <param name="head">The canonical head span.</param>
    /// <param name="origin">The source-axiom index a materialisation stamps.</param>
    public DlClauseSpanKey(ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin)
    {
        Body = body;
        Head = head;
        Origin = origin;
    }

    /// <summary>The canonical body span.</summary>
    public ReadOnlySpan<DlLiteral> Body { get; }

    /// <summary>The canonical head span.</summary>
    public ReadOnlySpan<DlLiteral> Head { get; }

    /// <summary>The source-axiom index a materialisation from this key stamps; never part of the key's identity.</summary>
    public int Origin { get; }
}

/// <summary>
/// The live clause set's comparer: the object face delegates to
/// <see cref="DlClause"/>'s own equality and hash, and the alternate face answers
/// the same questions for a <see cref="DlClauseSpanKey"/> that has not been built
/// into a clause. The alternate hash reproduces <see cref="DlClause.GetHashCode"/>
/// literal for literal — the body length, then every body literal, then every head
/// literal — so a key and the clause it would materialise land in the same bucket;
/// the alternate equality compares body and head only, mirroring
/// <see cref="DlClause.Equals(DlClause)"/>, so a content-identical clause of a
/// different origin is recognised as the exact duplicate it is rather than falling
/// through to the subsumption scan. The comparer is stateless and shared through
/// <see cref="Instance"/>.
/// </summary>
internal sealed class DlClauseSpanComparer: IEqualityComparer<DlClause>, IAlternateEqualityComparer<DlClauseSpanKey, DlClause>
{
    /// <summary>The shared stateless comparer every live clause set is constructed with.</summary>
    public static DlClauseSpanComparer Instance { get; } = new();

    /// <summary>Whether two clauses are equal, by the clause's own body-and-head equality.</summary>
    /// <param name="x">The first clause.</param>
    /// <param name="y">The second clause.</param>
    /// <returns><see langword="true"/> when both are the same clause content.</returns>
    public bool Equals(DlClause? x, DlClause? y)
    {
        return x is null ? y is null : x.Equals(y);
    }

    /// <summary>The clause's own hash code.</summary>
    /// <param name="obj">The clause.</param>
    /// <returns>The hash code.</returns>
    public int GetHashCode(DlClause obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        return obj.GetHashCode();
    }

    /// <summary>Whether a span key and a stored clause hold the same body and head; the key's origin plays no part, matching the clause's own equality.</summary>
    /// <param name="alternate">The span key.</param>
    /// <param name="other">The stored clause.</param>
    /// <returns><see langword="true"/> when the spans match the clause's body and head.</returns>
    public bool Equals(DlClauseSpanKey alternate, DlClause other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return alternate.Body.SequenceEqual(other.Body) && alternate.Head.SequenceEqual(other.Head);
    }

    /// <summary>The hash of a span key, reproducing the clause hash of the clause the key would materialise: the body length, then every body literal, then every head literal.</summary>
    /// <param name="alternate">The span key.</param>
    /// <returns>The hash code.</returns>
    public int GetHashCode(DlClauseSpanKey alternate)
    {
        HashCode hash = new();
        hash.Add(alternate.Body.Length);
        for(int i = 0; i < alternate.Body.Length; i++)
        {
            hash.Add(alternate.Body[i]);
        }

        for(int i = 0; i < alternate.Head.Length; i++)
        {
            hash.Add(alternate.Head[i]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Materialises the clause a span key stands for, stamping the key's origin — the alternate lookup's insertion face.</summary>
    /// <param name="alternate">The span key.</param>
    /// <returns>The clause over the key's canonical spans.</returns>
    public DlClause Create(DlClauseSpanKey alternate)
    {
        return DlClause.FromCanonicalSpans(alternate.Body, alternate.Head, alternate.Origin);
    }
}

/// <summary>
/// One context of the context structure <c>D = ⟨V, E, S, core⟩</c> (KR 2016
/// Section 4): a fixed core, the live clause set <c>S_v</c>, and the indexes the
/// saturation rules read. Clauses are stored by dense id; removal tombstones the
/// id (the id's slot stays, marked not live), so a dequeued worklist event that
/// names a removed clause is skipped and index lists may carry stale ids that
/// readers filter by <see cref="IsLive"/>. Every head-keyed premise index is
/// keyed on the clause's SELECTED head literal — the unique maximal literal
/// under the total selection order, computed by the engine at insertion —
/// because ordered resolution resolves a clause only through its selected
/// literal, so premise lookup by selected literal is complete (Table 2's
/// <c>∆ᵢ ⋡ Aᵢσ</c> side conditions). The selected-literal index, the
/// unconditional single-literal-head set, the role-head-by-shape index, the
/// Pred-eligible body-atom index, and the per-function selected-head presence
/// count are the premise-lookup structures the Hyper, Succ, Pred, and Eq rules
/// consume. The redundancy relation has three structures of its own — the
/// head-empty clauses keyed by their first body literal, and the whole-head and
/// whole-body occurrence indexes — so the containment test and the backward
/// subsumption sweep draw candidates from a posting keyed by a literal the
/// subsuming or subsumed clause must carry, instead of scanning the live set. The
/// root-exchange blocking relation has one of its own for the same reason: only a
/// clause whose head carries a predecessor-trigger equality can block, so those
/// ids are posted and the relation reads the posting rather than the clause list.
/// </summary>
internal sealed class Context
{
    /// <summary>An empty id list shared by every absent index lookup, so a miss allocates nothing.</summary>
    private static IReadOnlyList<int> NoClauses { get; } = [];

    /// <summary>The context's id — its index in the owning <see cref="ContextStructure"/>.</summary>
    public int Id { get; }

    /// <summary>Whether this context is root-CLASS (arXiv:1805.01396 Definition 5): the distinguished single root <c>vr</c>, or a per-individual nominal-root context <c>v_o</c> under the fragmented topology — the empty-core contexts outside strategy control where ground nominal reasoning concentrates; their clause heads select under the root-class term order and their literals range over the root-class universe of the engine's topology. Distinguished by this flag, never by core shape — the trivial context's core is empty too.</summary>
    public bool IsRoot { get; }

    /// <summary>The individual this nominal-root context is home to under <see cref="RootContextTopology.PerIndividualRoots"/> — the constant the central variable denotes there via the context clause <c>⊤ → x ≈ o</c> — or <c>-1</c> for the single root <c>vr</c> and every ordinary context.</summary>
    public int HomeIndividual { get; }

    /// <summary>The context's core atoms (KR 2016 Definition 5): empty for the trivial context and the root context, a single concept atom for a query or cautious-successor context. Fixed at creation.</summary>
    public IReadOnlyList<DlLiteral> CoreAtoms { get; }

    /// <summary>The clauses ever inserted, by id; a tombstoned id keeps its slot but is not live.</summary>
    private List<DlClause> ClausesById { get; } = [];

    /// <summary>The liveness plane of the clause ids: the bit at an id is set while its clause is live and cleared by a tombstone. A mutable record is held in a field rather than a property, whose getter would hand out a copy and lose every write.</summary>
    private GrowableBitVector liveById;

    /// <summary>The live clause ids, in no significant order — the dense enumeration the tag-join absorber scan walks, and the sequence the backward-subsumption sweep restores its index-drawn ids to, so a scan that must visit the live set costs the LIVE clause count rather than the total ever inserted. <see cref="Insert"/> appends; <see cref="Tombstone"/> swap-removes in O(1) using <see cref="LiveIdSlot"/>.</summary>
    private List<int> LiveIds { get; } = [];

    /// <summary>The index of each clause id within <see cref="LiveIds"/>, or <c>-1</c> for a tombstoned id — parallel to <see cref="liveById"/>, so a tombstone finds its <see cref="LiveIds"/> slot without a search and the swap-remove stays O(1)-amortized.</summary>
    private List<int> LiveIdSlot { get; } = [];

    /// <summary>The live clauses, for the exact-duplicate fast check that fronts the redundancy test. Constructed with <see cref="DlClauseSpanComparer.Instance"/>, whose object face delegates to the clause's own equality and hash, so membership is what the default comparer answers; the comparer's alternate face additionally admits the span lookup. The set is added to, removed from, and probed, and never enumerated, so it carries no order a comparer change could perturb.</summary>
    private HashSet<DlClause> LiveSet { get; } = new(DlClauseSpanComparer.Instance);

    /// <summary>The live set's span-keyed lookup, taken once per context so the exact-duplicate probe pays no per-call comparer resolution; it tracks the set's contents for the context's life.</summary>
    private HashSet<DlClause>.AlternateLookup<DlClauseSpanKey> LiveSpanLookup { get; }

    /// <summary>The live clause ids keyed by their SELECTED head literal (empty-head clauses do not appear); the Hyper and Pred premise lookup and the Succ K2 test read this. Lists may carry tombstoned ids.</summary>
    private Dictionary<DlLiteral, List<int>> SelectedLiteralIndex { get; } = [];

    /// <summary>The head atoms of the live empty-body SINGLE-literal clauses (the unconditional heads <c>⊤→A</c>, KR 2016 Table 2's K1 pattern); the Succ K1 test reads this by plain membership. A disjunctive unconditional head is not a decided atom and never joins. At most one live clause maps to each head, so removal deletes the entry exactly.</summary>
    private HashSet<DlLiteral> UnconditionalHeads { get; } = [];

    /// <summary>The live clause ids whose SELECTED head literal is a role atom with the central variable in the keyed argument position (the other argument being <c>y</c> or a function term); the Hyper role-premise completion with an unresolved neighbour reads this. Lists may carry tombstoned ids.</summary>
    private Dictionary<(int RoleSymbol, bool CentralFirst), List<int>> RoleHeadByShape { get; } = [];

    /// <summary>The live Pred-eligible clause ids (head empty or a single <c>Pr(O)</c> atom) indexed by each of their body atoms; the Pred site-2 lookup by inverted sigma-image reads this. Lists may carry tombstoned ids.</summary>
    private Dictionary<DlLiteral, List<int>> PredEligibleBodyIndex { get; } = [];

    /// <summary>The live Pred-eligible clause ids in insertion order; the Pred site-1/site-3 sweeps read this. May carry tombstoned ids.</summary>
    private List<int> PredEligibleIds { get; } = [];

    /// <summary>The number of live clauses whose SELECTED head literal bears each Succ-trigger term — <c>f(x)</c> in an ordinary context, <c>f(o)</c> in the root context (Table 2: <c>∆ ⋡ A</c> and <c>A</c> contains <c>f(x)</c>, or <c>f(o)</c> on <c>vr</c>), keyed by the packed term; the Succ stale-trigger check reads it, so an eliminated trigger clause retracts a pending Succ candidate.</summary>
    private Dictionary<DlTerm, int> LiveFunctionHeadCounts { get; } = [];

    /// <summary>The live clause ids whose BODY contains each ground literal — the Join rule's premise-one lookup (<c>A ∧ Γ → ∆</c> with <c>A</c> ground): a landed maximal ground head literal resolves against every clause whose body carries it. Lists may carry tombstoned ids, which readers filter by <see cref="IsLive"/>. Empty for every nominal-free module (no ground literal ever enters a clause there).</summary>
    private Dictionary<DlLiteral, List<int>> GroundBodyIndex { get; } = [];

    /// <summary>The live clause ids whose SELECTED head literal is a ground or mixed role atom, keyed by (role symbol, the anchor term standing in the x-slot of a Hyper image, whether the anchor is the first argument) — the root-context Hyper join's free-neighbour lookup (in <c>vr</c>, <c>σ(x) ∈ Σo</c>, so the slot lookup anchors on the constant rather than the central variable). Lists may carry tombstoned ids. Populated only in the root context.</summary>
    private Dictionary<(int RoleSymbol, DlTerm Anchor, bool AnchorFirst), List<int>> GroundRoleHeadByAnchor { get; } = [];

    /// <summary>The live clause ids whose SELECTED head literal is a broadened successor-trigger ground shape — a ground role atom <c>S(o, o′)</c> or a constant equality <c>o ≈ o′</c> — keyed by the literal; the Succ K1/K2 computation reads these alongside the materialized trigger templates (the broadened <c>Su</c>). Lists may carry tombstoned ids.</summary>
    private Dictionary<DlLiteral, List<int>> GroundSuccessorTriggerHeads { get; } = [];

    /// <summary>The distinct ground BODY literals mentioning each individual across the context's clauses — the Join bridge dispatch enumerates a constant's premise-one shapes through it when an <c>x ≈ o</c> premise lands.</summary>
    private Dictionary<int, List<DlLiteral>> GroundBodyLiteralsByIndividual { get; } = [];

    /// <summary>The data-demand marker concept atoms (the clausifier's descriptor keys) whose central-variable heads this context counts in <see cref="DataDemands"/>; empty for a module carrying no admitted data restriction.</summary>
    private IReadOnlySet<int> DataDemandMarkers { get; }

    /// <summary>The push-provenance plane of the clause ids under the license-scoped Eq widening. Held in a field for the same reason <see cref="liveById"/> is.</summary>
    private GrowableBitVector pushProvenance;

    /// <summary>The broadcast-image containment plane, keyed by an image's position in the engine's broadcast list — an index space of its own, separate from the three clause-id planes.</summary>
    private GrowableBitVector broadcastImagesHeld;

    /// <summary>The origin-bit provenance plane of the clause ids under the origin-bit relay guard.</summary>
    private GrowableBitVector derivedUnderChoiceProvenance;

    /// <summary>The number of ids currently tagged <c>DerivedUnderChoice</c> in <see cref="derivedUnderChoiceProvenance"/> — incremented by <see cref="SetDerivedUnderChoice"/> on a false-to-true flip and decremented by <see cref="ClearDerivedUnderChoice"/> on a true-to-false flip, so it stays exactly the live tag population. Backs the <see cref="HasDerivedUnderChoiceTags"/> O(1) fast-path guard, sparing the absorption-origin join its subsumption scan on every run that tags nothing.</summary>
    private int DerivedUnderChoiceTagCount { get; set; }

    /// <summary>Whether any id is currently tagged <c>DerivedUnderChoice</c> — the provenance record holds a buffer AND its maintained tag count is positive. Reads <see langword="false"/> on every run that never tags, whose record is never written, and after the last tag is cleared, so a caller can skip the absorption-origin subsumption scan in O(1) when no choice-riding clause exists.</summary>
    public bool HasDerivedUnderChoiceTags => !derivedUnderChoiceProvenance.IsEmpty && DerivedUnderChoiceTagCount > 0;

    /// <summary>The number of live clauses whose SINGLE head literal is the data-demand marker <c>D(x)</c>, per marker atom: <see cref="Insert"/> increments, <see cref="Tombstone"/> decrements by the removed clause's own head, and the saturation engine's data-obligation rule reads the live-demand set off it. A marker inside a disjunctive head is not a decided demand and never counts; a retracted demand clause drops its count, so a demand tombstoned by a same-descriptor subsumer stops contributing to the sidecar decision.</summary>
    private Dictionary<int, int> DataDemands { get; } = [];

    /// <summary>The per-constant root-tier index of this context's unconditional single-literal facts, or <see langword="null"/> on a non-root context and until the first projected root fact — so a nominal-free module, whose contexts are never root, allocates none. Maintained in the same <see cref="Insert"/> / <see cref="Tombstone"/> cycle as the other per-context indexes, riding the same unconditional-single-literal-head guard as <see cref="UnconditionalHeads"/>. Dark this step: the key join and the per-constant data obligations are its consumers.</summary>
    private RootConstantIndex? RootIndex { get; set; }

    /// <summary>The live clause ids whose SELECTED head literal mentions each rewritable non-variable term — a function term <c>f(x)</c>, a named individual <c>o</c>, or a root term <c>f(o)</c> — in a rewrite-eligible slot, keyed by the packed term; the Eq rule's given-equality dispatch enumerates the rewrite targets through it — Eq rewrites only the selected literal, so mention indexing beyond it would serve no premise. Lists may carry tombstoned ids, which readers filter by <see cref="IsLive"/>.</summary>
    private Dictionary<DlTerm, List<int>> TermMentionIndex { get; } = [];

    /// <summary>The live clause ids whose SELECTED head literal is an equality, keyed by each side that can act as the rewrite SOURCE <c>s1</c> (Table 2 Eq: <c>t1 ⋡ s1</c> and a variable is never a rewrite occurrence): the oriented maximal side of a comparable equality, the constant side of an unoriented variable-versus-individual equality. Lists may carry tombstoned ids, which readers filter by <see cref="IsLive"/>.</summary>
    private Dictionary<DlTerm, List<int>> EqualityByFromSide { get; } = [];

    /// <summary>The live clause ids of the HEAD-EMPTY clauses, keyed by their FIRST body literal; the redundancy containment test reads it. A head-empty subsumer's body is a subset of the tested clause's body, so its first body literal is necessarily one of that clause's OWN body literals — the keys the containment walk probes. The empty clause (head and body both empty) has no first body literal, so it is registered in no index at all and the containment test answers it through <see cref="HasEmptyClause"/> instead. Lists may carry tombstoned ids, which readers filter by <see cref="IsLive"/>.</summary>
    private Dictionary<DlLiteral, List<int>> EmptyHeadByFirstBodyLiteral { get; } = [];

    /// <summary>The live clause ids keyed by EVERY literal of their head (empty-head clauses do not appear) — the whole-head counterpart of <see cref="SelectedLiteralIndex"/>, which keys only the maximal literals; the backward-subsumption sweep reads it. A clause subsumed by the arriving one carries every literal of that clause's head, so any single one of them is a complete probe key and the shortest posting suffices. Lists may carry tombstoned ids, which readers filter by <see cref="IsLive"/>.</summary>
    private Dictionary<DlLiteral, List<int>> HeadOccurrenceIndex { get; } = [];

    /// <summary>The live clause ids keyed by EVERY literal of their body (empty-body clauses do not appear); the backward-subsumption sweep reads it for a HEAD-EMPTY arriving clause, whose subsumed clauses carry every literal of its body. Lists may carry tombstoned ids, which readers filter by <see cref="IsLive"/>.</summary>
    private Dictionary<DlLiteral, List<int>> BodyOccurrenceIndex { get; } = [];

    /// <summary>The entries this context registered into <see cref="HeadOccurrenceIndex"/> — one per head literal of every inserted clause, so the index's MAINTAINED cost is readable against what the backward-subsumption sweep consults from it. A tombstone retires no entry, exactly as the postings themselves keep tombstoned ids.</summary>
    public long HeadOccurrenceEntriesRegistered { get; private set; }

    /// <summary>The entries this context registered into <see cref="BodyOccurrenceIndex"/> — one per body literal of every inserted clause.</summary>
    public long BodyOccurrenceEntriesRegistered { get; private set; }

    /// <summary>The backward-subsumption sweeps that reached the posting path — one per sweep of a clause with a head or a body. The empty clause's own sweep walks the live list directly, probing neither occurrence index, and charges nothing here.</summary>
    public long SurvivorSweepProbes { get; private set; }

    /// <summary>The posting entries the backward-subsumption sweeps walked — the CONSULTED cost of the two occurrence indexes; a sweep whose probe found no posting at all walks zero.</summary>
    public long SurvivorSweepPostingEntriesWalked { get; private set; }

    /// <summary>The distinct keys <see cref="HeadOccurrenceIndex"/> holds — the maintained key breadth, read beside the entry count. Keys probed is deliberately not recorded: that would need a set of its own rather than a counter.</summary>
    public int HeadOccurrenceDistinctKeys
    {
        get
        {
            return HeadOccurrenceIndex.Count;
        }
    }

    /// <summary>The distinct keys <see cref="BodyOccurrenceIndex"/> holds.</summary>
    public int BodyOccurrenceDistinctKeys
    {
        get
        {
            return BodyOccurrenceIndex.Count;
        }
    }

    /// <summary>The clause ids whose head carries at least one predecessor-trigger equality, in insertion (id) order; the root-exchange blocking relation reads it. A clause blocks only when one of its head literals is such an equality, so this posting covers every possible blocker and the relation draws its candidates from it instead of the whole clause list. Registered off the WHOLE head span rather than the maximal subset, since the relation reads every head literal of a candidate. May carry tombstoned ids, which readers filter by <see cref="IsLive"/>.</summary>
    private List<int> PredecessorMergeHeadIds { get; } = [];

    /// <summary>The individual ids carrying a registered EMPTY-BODY clause whose MAXIMAL head literal is the oriented central-individual equality <c>x ≈ o</c>, held in ASCENDING individual-id order and deduplicated; the Join bridge sweep over an abstract premise enumerates through it instead of the whole individual census. Append-only under tombstone like every other posting — a registered individual whose clauses all died stays registered and yields zero work, exactly as a selected-literal miss after a tombstone does, because the sweep re-probes <see cref="SelectedHeadClauses"/> and filters by <see cref="IsLive"/>. Clauses arrive in derivation order, so the ascending order is held by a sorted insert rather than by construction.</summary>
    private List<int> BridgeIndividualIds { get; } = [];

    /// <summary>The comparer ordering clause ids by their position in <see cref="LiveIds"/>, constructed once per context over the <see cref="LiveIdSlot"/> list whose reference is fixed for the context's life; the backward-subsumption sweep sorts its freshly appended range with it, so collected ids arrive in live-list order whatever order the probed posting registered them in.</summary>
    private IComparer<int> LiveIdSlotOrder { get; }

    /// <summary>The index of the FIRST maximal literal within each clause's head span, by clause id (<c>-1</c> for an empty head) — computed by the engine at insertion. Sufficient without the full maximal set: a clause whose maximal set contains an equality or function-bearing literal has a SINGLETON maximal set (the rest stratum is totally ordered and dominates the band/Pr bottom), so every Eq-site readback is exact; and <see cref="Tombstone"/>'s counted retractions concern only function-bearing, marker, and sole-literal heads, which are likewise singleton cases.</summary>
    private List<int> SelectedIndexById { get; } = [];

    /// <summary>The number of live clauses in <c>S_v</c>.</summary>
    public int LiveCount { get; private set; }

    /// <summary>Whether the empty clause (empty body and empty head) is live — the local collapse witness that reads as inconsistency in the trivial context and as an unsatisfiable-class subsumption elsewhere. Once present it is never removed.</summary>
    public bool HasEmptyClause { get; private set; }

    /// <summary>Initialises a context with its id, fixed core, root-class distinction, home individual, and the data-demand marker set it counts live heads of.</summary>
    /// <param name="id">The context id.</param>
    /// <param name="coreAtoms">The core atoms.</param>
    /// <param name="isRoot">Whether this is a root-class context.</param>
    /// <param name="homeIndividual">The nominal-root context's home individual, or <c>-1</c> for the single root and every ordinary context.</param>
    /// <param name="dataDemandMarkers">The data-demand marker concept atoms whose central-variable heads the context counts.</param>
    public Context(int id, IReadOnlyList<DlLiteral> coreAtoms, bool isRoot, int homeIndividual, IReadOnlySet<int> dataDemandMarkers)
    {
        Id = id;
        CoreAtoms = coreAtoms;
        IsRoot = isRoot;
        HomeIndividual = homeIndividual;
        DataDemandMarkers = dataDemandMarkers;
        LiveIdSlotOrder = new LiveIdSlotOrderComparer(LiveIdSlot);
        LiveSpanLookup = LiveSet.GetAlternateLookup<DlClauseSpanKey>();
    }

    /// <summary>The number of id slots ever allocated, live or tombstoned; the read-off scan walks ids below this.</summary>
    public int ClauseCapacity
    {
        get
        {
            return ClausesById.Count;
        }
    }

    /// <summary>Whether the clause at an id is live. The id is one <see cref="Insert"/> returned, so it is inside the liveness record by construction and an id outside it is an invariant violation.</summary>
    /// <param name="id">The clause id.</param>
    /// <returns><see langword="true"/> when the id is live.</returns>
    public bool IsLive(int id)
    {
        return liveById[id];
    }

    /// <summary>The clause at an id, live or tombstoned.</summary>
    /// <param name="id">The clause id.</param>
    /// <returns>The clause.</returns>
    public DlClause At(int id)
    {
        return ClausesById[id];
    }

    /// <summary>The number of clause slots ever created in this context, live or tombstoned — the id-space bound a whole-context scan iterates.</summary>
    public int ClauseCount => ClausesById.Count;

    /// <summary>Whether a core atom is present — the Succ <c>K2\core_v</c> subtraction reads this.</summary>
    /// <param name="atom">The concept atom.</param>
    /// <returns><see langword="true"/> when the atom is a core atom.</returns>
    public bool CoreContains(DlLiteral atom)
    {
        for(int i = 0; i < CoreAtoms.Count; i++)
        {
            if(CoreAtoms[i].Equals(atom))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any live clause's head atom bears the Succ-trigger term — <c>f(x)</c> in an ordinary context, <c>f(o)</c> in the root context — the Succ trigger's recheck against eliminations.</summary>
    /// <param name="trigger">The packed trigger term.</param>
    /// <returns><see langword="true"/> when a live trigger-bearing head remains.</returns>
    public bool HasLiveFunctionHead(DlTerm trigger)
    {
        return LiveFunctionHeadCounts.TryGetValue(trigger, out int count) && count > 0;
    }

    /// <summary>The clause ids whose BODY contains the ground literal — the Join premise-one lookup; readers skip tombstoned ids.</summary>
    /// <param name="groundLiteral">The ground body literal.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> GroundBodyClauses(DlLiteral groundLiteral)
    {
        return GroundBodyIndex.TryGetValue(groundLiteral, out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>Appends the distinct ground body literals mentioning an individual — the Join bridge dispatch's premise-one shapes for a landed <c>x ≈ o</c>.</summary>
    /// <param name="individual">The individual id.</param>
    /// <param name="literalsToAppendTo">The buffer the literals are appended to.</param>
    public void CollectGroundBodyLiteralsMentioning(int individual, List<DlLiteral> literalsToAppendTo)
    {
        if(GroundBodyLiteralsByIndividual.TryGetValue(individual, out List<DlLiteral>? literals))
        {
            literalsToAppendTo.AddRange(literals);
        }
    }

    /// <summary>Records a ground body literal under each individual it mentions, deduplicated.</summary>
    /// <param name="literal">The ground body literal.</param>
    private void RecordGroundBodyIndividuals(DlLiteral literal)
    {
        RecordGroundBodyIndividual(literal, literal.First);
        if(literal.Kind != DlLiteralKind.Concept && !literal.Second.Equals(literal.First))
        {
            RecordGroundBodyIndividual(literal, literal.Second);
        }
    }

    /// <summary>Records a ground body literal under one slot's individual, when the slot carries one.</summary>
    /// <param name="literal">The ground body literal.</param>
    /// <param name="slot">The slot term.</param>
    private void RecordGroundBodyIndividual(DlLiteral literal, DlTerm slot)
    {
        int individual = slot.Kind switch
        {
            DlTermKind.Individual or DlTermKind.FunctionOfIndividual => slot.IndividualId,
            _ => -1,
        };

        if(individual < 0)
        {
            return;
        }

        if(!GroundBodyLiteralsByIndividual.TryGetValue(individual, out List<DlLiteral>? literals))
        {
            literals = [];
            GroundBodyLiteralsByIndividual[individual] = literals;
        }

        if(!literals.Contains(literal))
        {
            literals.Add(literal);
        }
    }

    /// <summary>The clause ids whose SELECTED head literal is a role atom of the given symbol with the anchor term in the keyed argument position — the root-context Hyper free-slot lookup; readers skip tombstoned ids and read the binding from the selected literal's other argument.</summary>
    /// <param name="roleSymbol">The directioned role symbol.</param>
    /// <param name="anchor">The individual standing in the x-slot of the Hyper image.</param>
    /// <param name="anchorFirst">Whether the anchor is the first argument.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> GroundRoleHeads(int roleSymbol, DlTerm anchor, bool anchorFirst)
    {
        return GroundRoleHeadByAnchor.TryGetValue((roleSymbol, anchor, anchorFirst), out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>Appends every broadened successor-trigger ground literal (<c>S(o, o′)</c> or <c>o ≈ o′</c>) that heads at least one LIVE clause as a selected literal — the Succ K1/K2 ground pass reads these beside the materialized trigger templates.</summary>
    /// <param name="literalsToAppendTo">The buffer the live ground trigger heads are appended to.</param>
    public void CollectLiveGroundSuccessorTriggerHeads(List<DlLiteral> literalsToAppendTo)
    {
        foreach(KeyValuePair<DlLiteral, List<int>> entry in GroundSuccessorTriggerHeads)
        {
            for(int i = 0; i < entry.Value.Count; i++)
            {
                if(liveById[entry.Value[i]])
                {
                    literalsToAppendTo.Add(entry.Key);

                    break;
                }
            }
        }
    }

    /// <summary>The number of live clauses whose single head is the data-demand marker <c>marker(x)</c> — the retraction-aware live-demand count the data-obligation rule reads.</summary>
    /// <param name="marker">The data-demand marker concept atom.</param>
    /// <returns>The live count, zero when no live clause carries the marker head.</returns>
    public int DataDemandCount(int marker)
    {
        return DataDemands.TryGetValue(marker, out int count) ? count : 0;
    }

    /// <summary>Appends every data-demand marker with a live clause carrying its head (count greater than zero) to a reusable buffer — the context's current live-demand set.</summary>
    /// <param name="markersToAppendTo">The buffer the live demand markers are appended to.</param>
    public void CollectLiveDataDemands(List<int> markersToAppendTo)
    {
        foreach(KeyValuePair<int, int> entry in DataDemands)
        {
            if(entry.Value > 0)
            {
                markersToAppendTo.Add(entry.Key);
            }
        }
    }

    /// <summary>Whether this context carries a per-constant root-tier index — allocated lazily on the first projected root fact, so an ordinary or nominal-free context reads <see langword="false"/>. The zero-touch observation reads it.</summary>
    public bool HasRootConstantIndex
    {
        get
        {
            return RootIndex is not null;
        }
    }

    /// <summary>Appends an individual's concept memberships <c>B(o)</c> stored raw in this root context's per-constant index; nothing when the context has no index. The ≈-resolution is a read-time union the engine layers on top, never a storage rewrite.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="symbolsToAppendTo">The buffer the concept symbols are appended to.</param>
    public void AppendRootConceptMemberships(int individual, List<int> symbolsToAppendTo)
    {
        RootIndex?.AppendConceptMemberships(individual, symbolsToAppendTo);
    }

    /// <summary>Appends an individual's outgoing role edges <c>S(o, o′)</c> stored raw in this root context's per-constant index; nothing when the context has no index.</summary>
    /// <param name="individual">The source individual key.</param>
    /// <param name="edgesToAppendTo">The buffer the role edges are appended to.</param>
    public void AppendRootRoleTargets(int individual, List<RootRoleEdge> edgesToAppendTo)
    {
        RootIndex?.AppendRoleTargets(individual, edgesToAppendTo);
    }

    /// <summary>The live count of an individual's data-demand marker <c>D(o)</c> in this root context's per-constant index; zero when the context has no index or no live spelling of the demand.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="marker">The data-demand marker concept atom.</param>
    /// <returns>The live count, zero when absent.</returns>
    public int RootDataDemandCount(int individual, int marker)
    {
        return RootIndex?.DataDemandCount(individual, marker) ?? 0;
    }

    /// <summary>Whether a head atom is an unconditional head <c>⊤→atom</c> of a live clause (the Succ K1 plain-membership test).</summary>
    /// <param name="atom">The head atom.</param>
    /// <returns><see langword="true"/> when a live empty-body clause has that head.</returns>
    public bool UnconditionalContains(DlLiteral atom)
    {
        return UnconditionalHeads.Contains(atom);
    }

    /// <summary>The clause ids whose SELECTED head literal equals the key; readers skip tombstoned ids.</summary>
    /// <param name="literal">The selected head literal.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> SelectedHeadClauses(DlLiteral literal)
    {
        return SelectedLiteralIndex.TryGetValue(literal, out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>The clause ids whose SELECTED head literal is a role atom with the central variable in the keyed position; readers skip tombstoned ids and read the other argument from the selected literal.</summary>
    /// <param name="roleSymbol">The directioned role symbol.</param>
    /// <param name="centralFirst">Whether the central variable is the first argument.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> RoleHeadClauses(int roleSymbol, bool centralFirst)
    {
        return RoleHeadByShape.TryGetValue((roleSymbol, centralFirst), out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>The SELECTED head literal of the clause at an id — the unique order-maximal literal the engine computed at insertion, the one literal every rule resolves the clause through. The clause must carry a non-empty head.</summary>
    /// <param name="id">The clause id.</param>
    /// <returns>The selected head literal.</returns>
    public DlLiteral SelectedLiteral(int id)
    {
        return ClausesById[id].Head[SelectedIndexById[id]];
    }

    /// <summary>The Pred-eligible clause ids whose body contains the given atom (the Pred site-2 lookup); readers skip tombstoned ids.</summary>
    /// <param name="bodyAtom">The body atom.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> PredEligibleWithBody(DlLiteral bodyAtom)
    {
        return PredEligibleBodyIndex.TryGetValue(bodyAtom, out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>The clause ids whose SELECTED head literal mentions the rewritable term in a rewrite-eligible slot (the Eq given-equality dispatch's target lookup); readers skip tombstoned ids.</summary>
    /// <param name="term">The packed rewritable term — a function term, a named individual, or a root term.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> SelectedHeadMentions(DlTerm term)
    {
        return TermMentionIndex.TryGetValue(term, out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>The clause ids whose SELECTED head literal is an equality that can rewrite FROM the given side term (the Eq given-target dispatch's rewriting-equality lookup); readers skip tombstoned ids.</summary>
    /// <param name="fromSide">The packed rewrite-source term.</param>
    /// <returns>The clause ids, possibly with tombstoned entries.</returns>
    public IReadOnlyList<int> EqualitiesFromSide(DlTerm fromSide)
    {
        return EqualityByFromSide.TryGetValue(fromSide, out List<int>? ids) ? ids : NoClauses;
    }

    /// <summary>All Pred-eligible clause ids in insertion order (the Pred site-1/site-3 sweeps); readers skip tombstoned ids.</summary>
    public IReadOnlyList<int> PredEligibleClauses
    {
        get
        {
            return PredEligibleIds;
        }
    }

    /// <summary>All clause ids whose head carries at least one predecessor-trigger equality, in insertion order — the complete candidate source of the root-exchange blocking relation, since no other clause can block; readers skip tombstoned ids.</summary>
    internal IReadOnlyList<int> PredecessorMergeHeadClauses
    {
        get
        {
            return PredecessorMergeHeadIds;
        }
    }

    /// <summary>The individual ids carrying a registered empty-body maximal <c>x ≈ o</c> clause, ascending — the complete candidate source of the Join bridge sweep over an abstract premise, since an individual absent here has no such clause and the sweep's inner walk would skip every entry it drew; readers re-probe the selected-literal postings and filter by liveness.</summary>
    internal IReadOnlyList<int> BridgeIndividuals
    {
        get
        {
            return BridgeIndividualIds;
        }
    }

    /// <summary>Whether a clause is contained up to redundancy in <c>S_v</c> (<c>∈̂</c>): the exact-duplicate fast path, the live empty clause, then the index-drawn candidates a live subsumer must appear among — the selected-literal postings of the clause's own head literals, and the head-empty postings of its own body literals. The three arms cover every subsumer shape, so the answer is the whole live set's, at the cost of the drawn postings rather than the live count.</summary>
    /// <param name="clause">The clause tested for containment.</param>
    /// <returns><see langword="true"/> when a live clause subsumes it.</returns>
    public bool ContainsUpToRedundancy(DlClause clause)
    {
        return ContainsUpToRedundancy(clause, out _);
    }

    /// <summary>Whether a clause is contained up to redundancy in <c>S_v</c> (<c>∈̂</c>), reporting WHICH arm answered: <paramref name="exactDuplicate"/> is <see langword="true"/> only for the exact-duplicate fast path, and <see langword="false"/> for the live empty clause, for both index walks, and for a clause that is not contained at all. A forwarder over the span core, which both clause faces reach, so the clause and span answers are one test.</summary>
    /// <param name="clause">The clause tested for containment.</param>
    /// <param name="exactDuplicate">Whether the containing clause is an exact duplicate rather than a strictly more general subsumer.</param>
    /// <returns><see langword="true"/> when a live clause subsumes it.</returns>
    internal bool ContainsUpToRedundancy(DlClause clause, out bool exactDuplicate)
    {
        return ContainsUpToRedundancy(clause.Body, clause.Head, clause.Origin, out exactDuplicate);
    }

    /// <summary>Whether a would-be clause given by its canonical body and head spans is contained up to redundancy in <c>S_v</c> (<c>∈̂</c>) — the core the two clause faces forward to, so a conclusion is probed before anything is built. The three arms are the clause face's: the exact-duplicate fast path, now answered through the live set's span-keyed lookup; the live empty clause; and the index-drawn candidates a live subsumer must appear among. <paramref name="exactDuplicate"/> reports the fast path alone.</summary>
    /// <param name="body">The candidate's canonical body span.</param>
    /// <param name="head">The candidate's canonical head span.</param>
    /// <param name="origin">The source-axiom index the candidate would carry; provenance, never part of the containment answer.</param>
    /// <param name="exactDuplicate">Whether the containing clause is an exact duplicate rather than a strictly more general subsumer.</param>
    /// <returns><see langword="true"/> when a live clause subsumes it.</returns>
    internal bool ContainsUpToRedundancy(ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin, out bool exactDuplicate)
    {
        if(LiveSpanLookup.Contains(new DlClauseSpanKey(body, head, origin)))
        {
            exactDuplicate = true;

            return true;
        }

        exactDuplicate = false;
        if(HasEmptyClause)
        {
            //The live empty clause subsumes every clause and keys no posting, so the
            //index walks below cannot see it. It is never removed once live: only a
            //strict subsumer could tombstone it, and nothing strictly subsumes the
            //empty clause.
            return true;
        }

        //A subsumer with a non-empty head has every head literal of its own inside
        //this clause's head, its registered maximal literals among them, so walking
        //the selected-literal postings of THIS clause's head literals reaches every
        //non-empty-head subsumer.
        for(int i = 0; i < head.Length; i++)
        {
            if(SelectedLiteralIndex.TryGetValue(head[i], out List<int>? selectedIds) && HasSubsumerAmong(selectedIds, body, head))
            {
                return true;
            }
        }

        //A head-empty subsumer has a body that is a subset of this clause's body, so
        //its first body literal is one of this clause's own body literals.
        for(int i = 0; i < body.Length; i++)
        {
            if(EmptyHeadByFirstBodyLiteral.TryGetValue(body[i], out List<int>? emptyHeadIds) && HasSubsumerAmong(emptyHeadIds, body, head))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any live clause of a posting list subsumes the candidate given by its canonical spans; tombstoned ids are filtered and a candidate whose body or head is longer than the probed spans is skipped before the merge-walk, since a longer canonical span is never a subset of a shorter one.</summary>
    /// <param name="candidateIds">The posting list of candidate subsumer ids, possibly with tombstoned entries.</param>
    /// <param name="body">The probed candidate's canonical body span.</param>
    /// <param name="head">The probed candidate's canonical head span.</param>
    /// <returns><see langword="true"/> when a live candidate subsumes the probed spans.</returns>
    private bool HasSubsumerAmong(List<int> candidateIds, ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head)
    {
        for(int i = 0; i < candidateIds.Count; i++)
        {
            int id = candidateIds[i];
            if(!liveById[id])
            {
                continue;
            }

            DlClause candidate = ClausesById[id];
            if(candidate.BodyLength > body.Length || candidate.Head.Length > head.Length)
            {
                continue;
            }

            if(ClauseRedundancy.Subsumes(candidate.Body, candidate.Head, body, head))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the clause at an id carries the push-provenance tag under the license-scoped Eq widening — a clause that arrived through a push landing or concluded from a pushed premise, joined onto a surviving subsumer on absorption. An id outside the tag record — including every id of a run that never tags, whose record is never written and therefore holds no buffer — reads untagged.</summary>
    /// <param name="id">The clause id.</param>
    /// <returns><see langword="true"/> when the clause is tagged pushed.</returns>
    public bool IsPushed(int id)
    {
        return pushProvenance.GetOrDefault(id);
    }

    /// <summary>Tags the clause at an id as push-provenanced, allocating the tag record on first use and extending it to cover the id; the ids the extension spans read untagged, so no gap is walked.</summary>
    /// <param name="id">The clause id.</param>
    public void SetPushed(int id)
    {
        pushProvenance.Set(id);
    }

    /// <summary>Whether the broadcast image at a position is contained here up to redundancy — the record the ordinary Pred arm reads before eliding an offer whose conclusion is that image itself. The key is the image's position in the engine's broadcast list, an index space of its own. A position outside the record — including every position of a run that broadcast nothing here, and every position of a root-class context, which receives no broadcast — reads unheld.</summary>
    /// <param name="broadcastIndex">The image's position in the broadcast list.</param>
    /// <returns><see langword="true"/> when the image's content is contained here.</returns>
    public bool HoldsBroadcastImage(int broadcastIndex)
    {
        return broadcastImagesHeld.GetOrDefault(broadcastIndex);
    }

    /// <summary>Records that the broadcast image at a position resolved as inserted or contained here, allocating the record on first use and extending it to cover the position; the positions the extension spans read unheld, so no gap is walked.</summary>
    /// <param name="broadcastIndex">The image's position in the broadcast list.</param>
    public void RecordBroadcastImageHeld(int broadcastIndex)
    {
        broadcastImagesHeld.Set(broadcastIndex);
    }

    /// <summary>Whether the clause at an id carries the <c>DerivedUnderChoice</c> origin tag — its derivation dropped a disjunct that did not participate in the inference, so its truth is not invariant across the live resolutions of that disjunct. An id outside the tag record — including every id of a run that never tags — reads <c>DecidedUnderNoChoice</c>.</summary>
    /// <param name="id">The clause id.</param>
    /// <returns><see langword="true"/> when the clause is tagged <c>DerivedUnderChoice</c>.</returns>
    public bool IsDerivedUnderChoice(int id)
    {
        return derivedUnderChoiceProvenance.GetOrDefault(id);
    }

    /// <summary>Tags the clause at an id as <c>DerivedUnderChoice</c>, allocating the tag record on first use and extending it to cover the id; the ids the extension spans read <c>DecidedUnderNoChoice</c>, so no gap is walked. Idempotent: the tag count advances only on a false-to-true flip, so a repeated set on an already-tagged id leaves <see cref="DerivedUnderChoiceTagCount"/> exact.</summary>
    /// <param name="id">The clause id.</param>
    public void SetDerivedUnderChoice(int id)
    {
        if(derivedUnderChoiceProvenance.Set(id))
        {
            DerivedUnderChoiceTagCount++;
        }
    }

    /// <summary>Clears the <c>DerivedUnderChoice</c> tag at an id toward <c>DecidedUnderNoChoice</c> — the absorption-upgrade direction, when a choice-free duplicate absorbs a previously choice-tainted clause; a no-op at an id the tag record does not cover, the record's unwritten state included. The tag count retreats only on a true-to-false flip, so a clear of an already-decided id leaves <see cref="DerivedUnderChoiceTagCount"/> exact.</summary>
    /// <param name="id">The clause id.</param>
    public void ClearDerivedUnderChoice(int id)
    {
        if(derivedUnderChoiceProvenance.Clear(id))
        {
            DerivedUnderChoiceTagCount--;
        }
    }

    /// <summary>Finds the first live clause that absorbs the given clause up to redundancy — the exact duplicate or the subsuming survivor the containment gate stopped at — so the license-scoped tag join can OR an absorbed pushed derivation's tag onto it. Called only on the tag-join path after the containment gate answered contained, where a live absorber therefore exists.</summary>
    /// <param name="clause">The absorbed clause.</param>
    /// <param name="absorbingId">The absorbing live clause's id.</param>
    /// <returns><see langword="true"/> when a live absorber was found.</returns>
    public bool TryFindLiveAbsorber(DlClause clause, out int absorbingId)
    {
        for(int i = 0; i < LiveIds.Count; i++)
        {
            if(ClauseRedundancy.Subsumes(ClausesById[LiveIds[i]], clause))
            {
                absorbingId = LiveIds[i];

                return true;
            }
        }

        absorbingId = -1;

        return false;
    }

    /// <summary>Appends the live clause ids strictly subsumed by <paramref name="clause"/> (the backward-Elim removals) to a reusable buffer, in live-list order. A subsumed clause carries every literal of the clause's head — or, for a head-empty clause, of its body — so ONE occurrence posting is a complete candidate source and the shortest is probed; the appended range is then sorted by live-list position, so the sequence is the whole-live-list scan's whatever order the posting registered its ids in.</summary>
    /// <param name="clause">The newly added clause that may subsume weaker clauses.</param>
    /// <param name="subsumedIdsToAppendTo">The buffer the strictly-subsumed ids are appended to.</param>
    public void CollectStrictlySubsumed(DlClause clause, List<int> subsumedIdsToAppendTo)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        ReadOnlySpan<DlLiteral> body = clause.Body;
        if(head.Length == 0 && body.Length == 0)
        {
            //The empty clause subsumes every live clause and keys no posting, so its
            //own sweep walks the live list — already in live-list order, so no
            //reordering follows. It runs at most once per context: nothing passes the
            //containment gate after the empty clause lands.
            for(int i = 0; i < LiveIds.Count; i++)
            {
                int id = LiveIds[i];
                if(ClauseRedundancy.Subsumes(clause, ClausesById[id]))
                {
                    subsumedIdsToAppendTo.Add(id);
                }
            }

            return;
        }

        SurvivorSweepProbes++;
        int startIndex = subsumedIdsToAppendTo.Count;
        List<int>? candidateIds = head.Length > 0
            ? ShortestPosting(HeadOccurrenceIndex, head)
            : ShortestPosting(BodyOccurrenceIndex, body);

        AppendSubsumedFrom(candidateIds, clause, subsumedIdsToAppendTo);

        int appended = subsumedIdsToAppendTo.Count - startIndex;
        if(appended > 1)
        {
            subsumedIdsToAppendTo.Sort(startIndex, appended, LiveIdSlotOrder);
        }
    }

    /// <summary>The shortest posting list an index holds for a clause's literals, or <see langword="null"/> when one literal keys no posting at all — no clause then carries every literal, so the candidate set is empty. Any single literal is a complete probe key; the shortest posting is the cheapest one to walk.</summary>
    /// <param name="index">The occurrence index probed.</param>
    /// <param name="literals">The canonical literal span whose keys are probed.</param>
    /// <returns>The shortest posting list, or <see langword="null"/> when a literal is absent from the index.</returns>
    private static List<int>? ShortestPosting(Dictionary<DlLiteral, List<int>> index, ReadOnlySpan<DlLiteral> literals)
    {
        List<int>? shortest = null;
        for(int i = 0; i < literals.Length; i++)
        {
            if(!index.TryGetValue(literals[i], out List<int>? ids))
            {
                return null;
            }

            if(shortest is null || ids.Count < shortest.Count)
            {
                shortest = ids;
            }
        }

        return shortest;
    }

    /// <summary>Appends the live clauses of a posting list that the given clause subsumes; tombstoned ids are filtered and a candidate whose body or head is shorter than the clause's is skipped before the merge-walk, since a longer canonical span is never a subset of a shorter one.</summary>
    /// <param name="candidateIds">The posting list of candidate subsumed ids, possibly with tombstoned entries; <see langword="null"/> when no candidate exists.</param>
    /// <param name="clause">The newly added clause that may subsume weaker clauses.</param>
    /// <param name="subsumedIdsToAppendTo">The buffer the subsumed ids are appended to.</param>
    private void AppendSubsumedFrom(List<int>? candidateIds, DlClause clause, List<int> subsumedIdsToAppendTo)
    {
        if(candidateIds is null)
        {
            return;
        }

        SurvivorSweepPostingEntriesWalked += candidateIds.Count;
        for(int i = 0; i < candidateIds.Count; i++)
        {
            int id = candidateIds[i];
            if(!liveById[id])
            {
                continue;
            }

            DlClause candidate = ClausesById[id];
            if(candidate.BodyLength < clause.BodyLength || candidate.Head.Length < clause.Head.Length)
            {
                continue;
            }

            if(ClauseRedundancy.Subsumes(clause, candidate))
            {
                subsumedIdsToAppendTo.Add(id);
            }
        }
    }

    /// <summary>Inserts a clause into <c>S_v</c> and updates every premise-lookup index off its MAXIMAL head literals (rules fire once per maximal literal, so premise lookup registers each); the caller has already established the clause is not contained up to redundancy and collected the maximal set under the selection order. Only the selected-literal index and the role-shape index can gain multiple entries — a multi-literal maximal set consists of band/Pr atoms, which never bear function terms, never head data-demand markers, and never form sole-literal unconditional heads. The three redundancy indexes register off the WHOLE spans instead: every head literal, every body literal, and a head-empty clause's first body literal, since a subsumption relation constrains the whole span rather than the selected literal.</summary>
    /// <param name="clause">The clause to insert.</param>
    /// <param name="isPredEligible">Whether the clause is Pred-eligible (head empty, or every head literal a <c>Pr(O)</c> atom).</param>
    /// <param name="decidedUnderNoChoice">Whether the clause's derivation is choice-free (the origin bit): only a <c>DecidedUnderNoChoice</c> unconditional single-literal head is projected into <see cref="UnconditionalHeads"/> and <see cref="RootIndex"/> — a <c>DerivedUnderChoice</c> head is withheld, since a not-actually-unconditional head must not seed a clash or a root-tier index entry.</param>
    /// <param name="maximalIndexes">The indexes of the maximal head literals within the head span, in head order; empty for an empty head.</param>
    /// <returns>The new clause id.</returns>
    public int Insert(DlClause clause, bool isPredEligible, bool decidedUnderNoChoice, List<int> maximalIndexes)
    {
        int id = ClausesById.Count;
        ClausesById.Add(clause);
        liveById.Append(true);
        LiveIdSlot.Add(LiveIds.Count);
        LiveIds.Add(id);
        SelectedIndexById.Add(maximalIndexes.Count > 0 ? maximalIndexes[0] : -1);
        LiveSet.Add(clause);
        LiveCount++;

        ReadOnlySpan<DlLiteral> head = clause.Head;
        if(head.Length == 0)
        {
            if(clause.BodyLength == 0)
            {
                HasEmptyClause = true;
            }
            else
            {
                AddToIndex(EmptyHeadByFirstBodyLiteral, clause.Body[0], id);
            }
        }
        else
        {
            if(clause.BodyLength == 0 && head.Length == 1 && decidedUnderNoChoice)
            {
                //A DerivedUnderChoice single-literal head is withheld from both the
                //unconditional-head set and the root-tier index: its truth is
                //not invariant across the dropped disjunct's live resolutions, so
                //recording it as unconditional would seed a spurious clash and a
                //root-tier index entry the read-off must not trust. The engine arms the
                //RootEqualityRidesAChoice latch on the equality flavour of this withhold.
                UnconditionalHeads.Add(head[0]);
                if(IsRoot)
                {
                    RootIndex ??= new RootConstantIndex();
                    RootIndex.Project(head[0], HomeIndividual, DataDemandMarkers);
                }
            }

            //The backward-subsumption sweep probes a whole-head occurrence key, not a
            //maximal one: a subsumed clause carries every literal of the arriving
            //clause's head, maximal or not, so every head literal is registered. The
            //blocking relation likewise reads every head literal of a candidate, so the
            //merge-equality flag is accumulated over the same whole-head walk and the id
            //is posted once after it.
            bool hasMergeEqualityHead = false;
            for(int h = 0; h < head.Length; h++)
            {
                AddToIndex(HeadOccurrenceIndex, head[h], id);
                HeadOccurrenceEntriesRegistered++;
                hasMergeEqualityHead |= IsPredecessorTriggerEquality(head[h]);
            }

            if(hasMergeEqualityHead)
            {
                PredecessorMergeHeadIds.Add(id);
            }

            for(int m = 0; m < maximalIndexes.Count; m++)
            {
                DlLiteral selected = head[maximalIndexes[m]];
                AddToIndex(SelectedLiteralIndex, selected, id);
                if(clause.BodyLength == 0 && IsBridgeEqualityHead(selected))
                {
                    RegisterBridgeIndividual(selected.First.IsIndividual ? selected.First.IndividualId : selected.Second.IndividualId);
                }

                if(selected.Kind == DlLiteralKind.Role)
                {
                    if(selected.First.IsCentral || selected.Second.IsCentral)
                    {
                        AddToIndex(RoleHeadByShape, (selected.Symbol, selected.First.IsCentral), id);
                    }

                    if(selected.First.IsIndividual)
                    {
                        AddToIndex(GroundRoleHeadByAnchor, (selected.Symbol, selected.First, true), id);
                    }

                    if(selected.Second.IsIndividual)
                    {
                        AddToIndex(GroundRoleHeadByAnchor, (selected.Symbol, selected.Second, false), id);
                    }
                }

                if(TryGetHeadSuccTrigger(selected, out DlTerm trigger))
                {
                    LiveFunctionHeadCounts[trigger] = LiveFunctionHeadCounts.TryGetValue(trigger, out int count) ? count + 1 : 1;
                }

                if(head.Length == 1 && IsDataDemandHead(selected))
                {
                    DataDemands[selected.Symbol] = DataDemands.TryGetValue(selected.Symbol, out int demandCount) ? demandCount + 1 : 1;
                }

                IndexTermMentions(selected, id);
                if(selected.Kind == DlLiteralKind.Equality)
                {
                    IndexEqualityFromSides(selected, id);
                }

                if(IsBroadenedSuccessorTriggerHead(selected))
                {
                    AddToIndex(GroundSuccessorTriggerHeads, selected, id);
                }
            }
        }

        ReadOnlySpan<DlLiteral> bodyLiterals = clause.Body;
        for(int i = 0; i < bodyLiterals.Length; i++)
        {
            AddToIndex(BodyOccurrenceIndex, bodyLiterals[i], id);
            BodyOccurrenceEntriesRegistered++;
            if(IsGroundLiteral(bodyLiterals[i]))
            {
                AddToIndex(GroundBodyIndex, bodyLiterals[i], id);
                RecordGroundBodyIndividuals(bodyLiterals[i]);
            }
        }

        if(isPredEligible)
        {
            PredEligibleIds.Add(id);
            ReadOnlySpan<DlLiteral> body = clause.Body;
            for(int i = 0; i < body.Length; i++)
            {
                AddToIndex(PredEligibleBodyIndex, body[i], id);
            }
        }

        return id;
    }

    /// <summary>Projects a live clause's unconditional single-literal head into <see cref="UnconditionalHeads"/> and <see cref="RootIndex"/> after the fact — the absorption-upgrade re-offer, run once when a <c>DerivedUnderChoice</c> clause is cleared to <c>DecidedUnderNoChoice</c> by a choice-free absorbing duplicate and its insert-time projection was therefore withheld. Called AT MOST ONCE per id: the upgrade is a one-shot <c>DerivedUnderChoice</c>-to-<c>DecidedUnderNoChoice</c> flip, so the non-idempotent data-demand increment inside <see cref="RootConstantIndex.Project"/> cannot double-count.</summary>
    /// <param name="id">The live clause id whose head is now decided and must be projected.</param>
    public void ProjectUnconditionalHead(int id)
    {
        DlClause clause = ClausesById[id];
        ReadOnlySpan<DlLiteral> head = clause.Head;
        if(clause.BodyLength == 0 && head.Length == 1)
        {
            UnconditionalHeads.Add(head[0]);
            if(IsRoot)
            {
                RootIndex ??= new RootConstantIndex();
                RootIndex.Project(head[0], HomeIndividual, DataDemandMarkers);
            }
        }
    }

    /// <summary>Whether a literal is ground — every occupied argument slot is a named individual or a function of one; the Join premise-one body shape and the Pred ground-conjunct <c>Ci</c> classification.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns><see langword="true"/> for a ground literal.</returns>
    public static bool IsGroundLiteral(DlLiteral literal)
    {
        return literal.Kind == DlLiteralKind.Concept
            ? literal.First.IsGround
            : literal.First.IsGround && literal.Second.IsGround;
    }

    /// <summary>Whether a literal is one of the extended <c>Pr</c> equality shapes — <c>x ≈ y</c>, <c>x ≈ o</c>, or <c>y ≈ o</c> in either storage orientation; an inequality never qualifies, and neither does an equality between two named individuals, which has no variable side. The shape the Pred source-eligibility screen admits and the shape the root-exchange blocking relation's merge literal must carry, so one definition serves both and <see cref="Insert"/>'s posting registration.</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> for a predecessor-trigger equality.</returns>
    internal static bool IsPredecessorTriggerEquality(DlLiteral literal)
    {
        if(literal.Kind != DlLiteralKind.Equality)
        {
            return false;
        }

        bool firstVariable = literal.First.Kind is DlTermKind.Central or DlTermKind.Context;
        bool secondVariable = literal.Second.Kind is DlTermKind.Central or DlTermKind.Context;

        return (firstVariable && (secondVariable || literal.Second.IsIndividual))
            || (secondVariable && literal.First.IsIndividual);
    }

    /// <summary>Whether a SELECTED head literal is a central-individual equality <c>x ≈ o</c> in either storage orientation — the second-premise shape of the Join bridge, whose probe key is built in exactly this form, so an empty-body clause holding it maximal is a bridge premise and its individual is posted.</summary>
    /// <param name="selected">The selected head literal.</param>
    /// <returns><see langword="true"/> for a central-individual equality.</returns>
    private static bool IsBridgeEqualityHead(DlLiteral selected)
    {
        return selected.Kind == DlLiteralKind.Equality
            && ((selected.First.IsCentral && selected.Second.IsIndividual) || (selected.Second.IsCentral && selected.First.IsIndividual));
    }

    /// <summary>Records an individual in <see cref="BridgeIndividualIds"/> by SORTED INSERT, so the posting stays ascending and deduplicated whatever order derivation registers its clauses in; a clause carrying several maximal <c>x ≈ o</c> literals registers each individual once, and a repeat registration of a posted individual is a no-op.</summary>
    /// <param name="individual">The individual id of the equality's constant side.</param>
    private void RegisterBridgeIndividual(int individual)
    {
        int position = BridgeIndividualIds.BinarySearch(individual);
        if(position < 0)
        {
            BridgeIndividualIds.Insert(~position, individual);
        }
    }

    /// <summary>Whether a selected head literal is a broadened successor-trigger ground shape — a ground role atom <c>S(o, o′)</c> or a constant equality <c>o ≈ o′</c> (the broadened <c>Su</c> members the K1/K2 ground pass reads).</summary>
    /// <param name="selected">The selected head literal.</param>
    /// <returns><see langword="true"/> for a broadened ground trigger head.</returns>
    private static bool IsBroadenedSuccessorTriggerHead(DlLiteral selected)
    {
        return selected.Kind switch
        {
            DlLiteralKind.Role => selected.First.IsIndividual && selected.Second.IsIndividual,
            DlLiteralKind.Equality => selected.First.IsIndividual && selected.Second.IsIndividual,
            _ => false,
        };
    }

    /// <summary>Records an equality head under each side that can act as the Eq rewrite SOURCE <c>s1</c> (Table 2: <c>t1 ⋡ s1</c>, and a variable is never a rewrite occurrence): the oriented maximal side of a comparable equality, the constant side of an unoriented variable-versus-individual equality.</summary>
    /// <param name="selected">The equality selected head literal, canonically stored.</param>
    /// <param name="id">The clause id.</param>
    private void IndexEqualityFromSides(DlLiteral selected, int id)
    {
        if(ContextTermOrder.IsRewriteSourceSide(selected.First, selected.Second))
        {
            AddToIndex(EqualityByFromSide, selected.First, id);
        }

        if(ContextTermOrder.IsRewriteSourceSide(selected.Second, selected.First))
        {
            AddToIndex(EqualityByFromSide, selected.Second, id);
        }
    }

    /// <summary>Tombstones a clause: marks the id not live, drops it from the live set, and retracts the counted entries by the SAME selected literal <see cref="Insert"/> recorded; selected-literal, occurrence, and body-atom index lists keep the stale id for readers to filter.</summary>
    /// <param name="id">The clause id to remove.</param>
    public void Tombstone(int id)
    {
        DlClause clause = ClausesById[id];
        liveById.Clear(id);
        int slot = LiveIdSlot[id];
        int lastId = LiveIds[^1];
        LiveIds[slot] = lastId;
        LiveIdSlot[lastId] = slot;
        LiveIds.RemoveAt(LiveIds.Count - 1);
        LiveIdSlot[id] = -1;
        LiveSet.Remove(clause);
        LiveCount--;

        ReadOnlySpan<DlLiteral> head = clause.Head;
        if(head.Length > 0)
        {
            DlLiteral selected = head[SelectedIndexById[id]];
            if(clause.BodyLength == 0 && head.Length == 1)
            {
                UnconditionalHeads.Remove(selected);
                RootIndex?.Retract(selected, HomeIndividual, DataDemandMarkers);
            }

            if(TryGetHeadSuccTrigger(selected, out DlTerm trigger))
            {
                LiveFunctionHeadCounts[trigger]--;
            }

            if(head.Length == 1 && IsDataDemandHead(selected))
            {
                DataDemands[selected.Symbol]--;
            }
        }
    }

    /// <summary>Whether a head atom is a data-demand marker on the central variable — the <c>marker(x)</c> shape the demand count tracks.</summary>
    /// <param name="atom">The clause's single head atom.</param>
    /// <returns><see langword="true"/> when the atom is a counted data-demand marker head.</returns>
    private bool IsDataDemandHead(DlLiteral atom)
    {
        return atom.Kind == DlLiteralKind.Concept && atom.First.IsCentral && DataDemandMarkers.Contains(atom.Symbol);
    }

    /// <summary>Records a clause id under each distinct rewritable term its SELECTED head literal mentions in a rewrite-eligible slot, in the mention index the Eq rule reads: every non-variable slot of a concept or role atom, and each (in)equality side <c>s2</c> not strictly dominated by its other side (the published <c>t2 ⊁ s2</c> — so the constant side of an unoriented <c>x ≈ o</c> IS a rewrite position while the minimal side of an oriented equality is not).</summary>
    /// <param name="headAtom">The clause's selected head literal.</param>
    /// <param name="id">The clause id.</param>
    private void IndexTermMentions(DlLiteral headAtom, int id)
    {
        bool firstEligible = headAtom.Kind switch
        {
            DlLiteralKind.Concept or DlLiteralKind.Role => !headAtom.First.IsVariable,
            _ => ContextTermOrder.IsRewritableSide(headAtom.First, headAtom.Second),
        };

        bool secondEligible = headAtom.Kind switch
        {
            DlLiteralKind.Concept => false,
            DlLiteralKind.Role => !headAtom.Second.IsVariable,
            _ => ContextTermOrder.IsRewritableSide(headAtom.Second, headAtom.First),
        };

        if(firstEligible)
        {
            AddToIndex(TermMentionIndex, headAtom.First, id);
        }

        if(secondEligible && !headAtom.Second.Equals(headAtom.First))
        {
            AddToIndex(TermMentionIndex, headAtom.Second, id);
        }
    }

    /// <summary>The Succ-trigger term a selected head literal carries — a function term <c>f(x)</c> or a root term <c>f(o)</c> in the first argument of any literal kind (a comparable equality or inequality stores its maximal side there), or the second argument of a role atom.</summary>
    /// <param name="atom">The selected head literal.</param>
    /// <param name="trigger">The packed trigger term when the literal bears one.</param>
    /// <returns><see langword="true"/> when the literal carries a Succ-trigger term.</returns>
    private static bool TryGetHeadSuccTrigger(DlLiteral atom, out DlTerm trigger)
    {
        if(IsSuccTriggerTerm(atom.First))
        {
            trigger = atom.First;

            return true;
        }

        if(atom.Kind == DlLiteralKind.Role && IsSuccTriggerTerm(atom.Second))
        {
            trigger = atom.Second;

            return true;
        }

        trigger = default;

        return false;
    }

    /// <summary>Whether a term is a Succ-trigger term — a Skolem function term over the central variable or over a named individual.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> for <c>f(x)</c> or <c>f(o)</c>.</returns>
    private static bool IsSuccTriggerTerm(DlTerm term)
    {
        return term.Kind is DlTermKind.Function or DlTermKind.FunctionOfIndividual;
    }

    /// <summary>Appends an id to the list under a key, creating the list on first use.</summary>
    /// <typeparam name="TKey">The index key type.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="key">The key.</param>
    /// <param name="id">The clause id.</param>
    private static void AddToIndex<TKey>(Dictionary<TKey, List<int>> index, TKey key, int id) where TKey : notnull
    {
        if(!index.TryGetValue(key, out List<int>? ids))
        {
            ids = [];
            index[key] = ids;
        }

        ids.Add(id);
    }

    /// <summary>Orders clause ids by their position in the context's live id list, read through the slot list the context maintains — a named comparer rather than a comparison delegate, so the backward-subsumption sweep's range sort allocates nothing and captures nothing.</summary>
    private sealed class LiveIdSlotOrderComparer: IComparer<int>
    {
        /// <summary>The context's slot list, whose reference is fixed for the context's life while its contents track every insert and tombstone.</summary>
        private List<int> LiveIdSlot { get; }

        /// <summary>Initialises the comparer over the context's slot list.</summary>
        /// <param name="liveIdSlot">The context's slot list.</param>
        public LiveIdSlotOrderComparer(List<int> liveIdSlot)
        {
            LiveIdSlot = liveIdSlot;
        }

        /// <summary>Compares two live clause ids by their live-list positions.</summary>
        /// <param name="x">The first clause id.</param>
        /// <param name="y">The second clause id.</param>
        /// <returns>A signed comparison of the two ids' live-list positions.</returns>
        public int Compare(int x, int y)
        {
            return LiveIdSlot[x].CompareTo(LiveIdSlot[y]);
        }
    }
}

/// <summary>
/// A directed function edge <c>⟨u, v, f⟩ ∈ E</c> (KR 2016 Section 4): the Succ
/// rule creates it from context <paramref name="Source"/> to context
/// <paramref name="Target"/> over Skolem function symbol <paramref name="Function"/>,
/// and the Pred rule reads it in both directions.
/// </summary>
/// <param name="Source">The predecessor context <c>u</c>.</param>
/// <param name="Function">The Skolem function symbol <c>f</c>.</param>
/// <param name="Target">The successor context <c>v</c>.</param>
internal readonly record struct ContextEdge(int Source, int Function, int Target);

/// <summary>
/// The context structure <c>D = ⟨V, E, S, core⟩</c> (KR 2016 Section 4): the
/// contexts, the deduplicated function edges with outgoing and incoming
/// multi-maps, and the registry keyed by core so the cautious strategy reuses a
/// context with exactly the core it targets (Definition 6). Cautious cores are
/// empty or a single atom, so the registry keys single-atom cores by their atom
/// id and holds a dedicated slot for the trivial (empty-core) context. This type
/// owns the shape of the structure; the saturation rules and their bookkeeping
/// live in <see cref="ContextSaturationEngine"/>.
/// </summary>
internal sealed class ContextStructure
{
    /// <summary>The contexts by id.</summary>
    private List<Context> Contexts { get; } = [];

    /// <summary>The context id keyed by its single core atom's concept id; the cautious strategy and the query-context setup reuse through it.</summary>
    private Dictionary<int, int> ContextByCoreAtom { get; } = [];

    /// <summary>The outgoing edges of each source context.</summary>
    private Dictionary<int, List<ContextEdge>> OutgoingEdges { get; } = [];

    /// <summary>The incoming edges of each target context.</summary>
    private Dictionary<int, List<ContextEdge>> IncomingEdges { get; } = [];

    /// <summary>The deduplicated edge triples <c>⟨u, f, v⟩</c>.</summary>
    private HashSet<ContextEdge> Edges { get; } = [];

    /// <summary>An empty edge list shared by every context with no edges in a direction.</summary>
    private static IReadOnlyList<ContextEdge> NoEdges { get; } = [];

    /// <summary>The data-demand marker atoms every created context counts live heads of; empty for a module carrying no admitted data restriction.</summary>
    private IReadOnlySet<int> DataDemandMarkers { get; }

    /// <summary>The id of the trivial (empty-core) context <c>v_⊤</c>, or <c>-1</c> before it is created.</summary>
    public int TrivialContextId { get; private set; } = -1;

    /// <summary>The id of the distinguished single root context <c>vr</c>, or <c>-1</c> when no single root was minted — the <see cref="RootContextTopology.SingleRoot"/> arm's storage; it stays <c>-1</c> under the fragmented topology, whose root tier lives in <see cref="RootContextByIndividual"/>. One of FIVE registry channels beside the trivial slot, the single-core-atom registry, the ground bucket, and the query set: <c>vr</c> and the trivial context both have empty cores, so the registry distinguishes them by slot, never by core shape, and the expansion strategy can never return a root-class context.</summary>
    public int RootContextId { get; private set; } = -1;

    /// <summary>The nominal-root context id per individual under <see cref="RootContextTopology.PerIndividualRoots"/> — the resolver's storage arm, empty under the single-root topology. Told individuals resolve at engine construction; generated nominals mint lazily at first seed.</summary>
    private Dictionary<int, int> RootContextByIndividual { get; } = [];

    /// <summary>The ids of every root-class context in creation order — the single root alone under <see cref="RootContextTopology.SingleRoot"/>, one per resolved individual under the fragmented topology. The module-inconsistency read-off scans it: the inter-nominal carrier can land a <c>⊥</c>-adjacent image in any nominal root, so the verdict must cover every member.</summary>
    private List<int> RootClassContexts { get; } = [];

    /// <summary>The deduplicated root edges as (source context, individual) pairs — nominal-labelled, kept apart from the function-labelled ordinary edges so the Pred sweeps never see them. The edge's target is DERIVED from the individual under both topologies (the single root, or the individual's nominal root), so the pair needs no target dimension.</summary>
    private HashSet<(int Source, int Individual)> RootEdgeSet { get; } = [];

    /// <summary>The source contexts of the root edges labelled by each individual — the r-Pred per-<c>oi</c> iteration reads it.</summary>
    private Dictionary<int, List<int>> RootEdgeSourcesByIndividual { get; } = [];

    /// <summary>An empty source list shared by every individual with no root edges.</summary>
    private static IReadOnlyList<int> NoSources { get; } = [];

    /// <summary>The ids of the ground contexts (core a marker atom <c>O_a</c> per individual representative) — the distinguishing bucket the module-inconsistency verdict scans for a derived empty clause and the Self-ghost pass walks for its unconditional loop-concept heads; a query or cautious-successor context is never in it.</summary>
    private HashSet<int> GroundContexts { get; } = [];

    /// <summary>The ids of the query-initialized contexts (core a queried named class <c>{A(x)}</c>) — the subsumption read-off surface, marked when the reasoner seeds a signature class before saturation. The scoped Eq paramodulation reads it (a query context is a read-off surface where the central-variable-versus-individual rewrite stays unrestricted); a cautious-successor context created for the same core during saturation reuses the marked context, so the mark rides the registry entry.</summary>
    private HashSet<int> QueryContexts { get; } = [];

    /// <summary>Initialises an empty structure whose contexts count the given data-demand marker heads.</summary>
    /// <param name="dataDemandMarkers">The data-demand marker atoms each context counts.</param>
    public ContextStructure(IReadOnlySet<int> dataDemandMarkers)
    {
        DataDemandMarkers = dataDemandMarkers;
    }

    /// <summary>The number of contexts created.</summary>
    public int Count
    {
        get
        {
            return Contexts.Count;
        }
    }

    /// <summary>The context at an id.</summary>
    /// <param name="id">The context id.</param>
    /// <returns>The context.</returns>
    public Context this[int id]
    {
        get
        {
            return Contexts[id];
        }
    }

    /// <summary>Creates a context with the given core and registers it, recording the trivial slot when the core is empty and the single-atom slot otherwise. Never creates the root context — <see cref="CreateRootContext"/> owns that distinguished slot.</summary>
    /// <param name="coreAtoms">The core atoms (empty or a single concept atom).</param>
    /// <returns>The new context.</returns>
    public Context CreateContext(IReadOnlyList<DlLiteral> coreAtoms)
    {
        int id = Contexts.Count;
        Context context = new(id, coreAtoms, isRoot: false, homeIndividual: -1, DataDemandMarkers);
        Contexts.Add(context);

        if(coreAtoms.Count == 0)
        {
            TrivialContextId = id;
        }
        else
        {
            ContextByCoreAtom[coreAtoms[0].Symbol] = id;
        }

        return context;
    }

    /// <summary>Creates the distinguished single root context <c>vr</c> (empty core) and records it in the root slot and the root-class list — outside the trivial slot and the core registry, so the reuse machinery and the expansion strategy never confuse it with the trivial context.</summary>
    /// <returns>The root context.</returns>
    public Context CreateRootContext()
    {
        int id = Contexts.Count;
        Context context = new(id, [], isRoot: true, homeIndividual: -1, DataDemandMarkers);
        Contexts.Add(context);
        RootContextId = id;
        RootClassContexts.Add(id);

        return context;
    }

    /// <summary>Creates the nominal-root context <c>v_o</c> for an individual under <see cref="RootContextTopology.PerIndividualRoots"/> (empty core; the caller seeds the context clause <c>⊤ → x ≈ o</c>) and records it in the per-individual map and the root-class list — outside every other registry channel, so the reuse machinery and the expansion strategy never return it.</summary>
    /// <param name="individual">The home individual <c>o</c>.</param>
    /// <returns>The nominal-root context.</returns>
    public Context CreateNominalRootContext(int individual)
    {
        int id = Contexts.Count;
        Context context = new(id, [], isRoot: true, individual, DataDemandMarkers);
        Contexts.Add(context);
        RootContextByIndividual[individual] = id;
        RootClassContexts.Add(id);

        return context;
    }

    /// <summary>Looks up the nominal-root context resolved for an individual under the fragmented topology.</summary>
    /// <param name="individual">The individual id.</param>
    /// <param name="contextId">The nominal-root context id when one was resolved.</param>
    /// <returns><see langword="true"/> when the individual's nominal root exists.</returns>
    public bool TryGetRootByIndividual(int individual, out int contextId)
    {
        return RootContextByIndividual.TryGetValue(individual, out contextId);
    }

    /// <summary>The root-class context ids in creation order — the module-inconsistency read-off and the root-class population statistic read them.</summary>
    public IReadOnlyList<int> RootClassContextIds
    {
        get
        {
            return RootClassContexts;
        }
    }

    /// <summary>The number of root edges added.</summary>
    public int RootEdgeCount
    {
        get
        {
            return RootEdgeSet.Count;
        }
    }

    /// <summary>Adds a root edge for a (source context, individual) pair if the pair is new (deduplicated); the target root-class context is derived from the individual under the engine's topology.</summary>
    /// <param name="source">The source context id <c>u</c>.</param>
    /// <param name="individual">The labelling individual id <c>o</c>.</param>
    /// <returns><see langword="true"/> when the edge was newly added, <see langword="false"/> when it already existed.</returns>
    public bool TryAddRootEdge(int source, int individual)
    {
        if(!RootEdgeSet.Add((source, individual)))
        {
            return false;
        }

        if(!RootEdgeSourcesByIndividual.TryGetValue(individual, out List<int>? sources))
        {
            sources = [];
            RootEdgeSourcesByIndividual[individual] = sources;
        }

        sources.Add(source);

        return true;
    }

    /// <summary>Whether the root edge for a (source context, individual) pair exists.</summary>
    /// <param name="source">The source context id.</param>
    /// <param name="individual">The labelling individual id.</param>
    /// <returns><see langword="true"/> when the edge exists.</returns>
    public bool HasRootEdge(int source, int individual)
    {
        return RootEdgeSet.Contains((source, individual));
    }

    /// <summary>The source contexts of the root edges labelled by an individual (the r-Pred per-<c>oi</c> sweep reads them).</summary>
    /// <param name="individual">The labelling individual id.</param>
    /// <returns>The source context ids.</returns>
    public IReadOnlyList<int> RootEdgeSources(int individual)
    {
        return RootEdgeSourcesByIndividual.TryGetValue(individual, out List<int>? sources) ? sources : NoSources;
    }

    /// <summary>Looks up the registered context for a single-atom core.</summary>
    /// <param name="coreAtomSymbol">The core concept atom's id.</param>
    /// <param name="contextId">The registered context id when present.</param>
    /// <returns><see langword="true"/> when a context with exactly that core exists.</returns>
    public bool TryGetByCoreAtom(int coreAtomSymbol, out int contextId)
    {
        return ContextByCoreAtom.TryGetValue(coreAtomSymbol, out contextId);
    }

    /// <summary>Records a context as a ground context (core a marker atom for an individual representative), so the module-inconsistency scan and the Self-ghost pass find it.</summary>
    /// <param name="contextId">The ground context id.</param>
    public void MarkGround(int contextId)
    {
        GroundContexts.Add(contextId);
    }

    /// <summary>The ground context ids — the distinguishing bucket the module-inconsistency verdict and the Self-ghost pass read.</summary>
    public IReadOnlyCollection<int> GroundContextIds
    {
        get
        {
            return GroundContexts;
        }
    }

    /// <summary>Whether a context is a ground context — a read-off surface for the module-inconsistency verdict and the key-join membership readout.</summary>
    /// <param name="contextId">The context id.</param>
    /// <returns><see langword="true"/> when the context is a ground context.</returns>
    public bool IsGround(int contextId)
    {
        return GroundContexts.Contains(contextId);
    }

    /// <summary>Records a context as a query-initialized context (core a queried named class), so the scoped Eq paramodulation keeps it a read-off surface.</summary>
    /// <param name="contextId">The query context id.</param>
    public void MarkQuery(int contextId)
    {
        QueryContexts.Add(contextId);
    }

    /// <summary>Whether a context is a query-initialized context — the subsumption read-off surface where the scoped central-variable-versus-individual paramodulation stays unrestricted.</summary>
    /// <param name="contextId">The context id.</param>
    /// <returns><see langword="true"/> when the context is a query-initialized context.</returns>
    public bool IsQueryContext(int contextId)
    {
        return QueryContexts.Contains(contextId);
    }

    /// <summary>Adds a function edge if the triple is new (deduplicated), maintaining the outgoing and incoming multi-maps.</summary>
    /// <param name="edge">The edge to add.</param>
    /// <returns><see langword="true"/> when the edge was newly added, <see langword="false"/> when it already existed.</returns>
    public bool TryAddEdge(ContextEdge edge)
    {
        if(!Edges.Add(edge))
        {
            return false;
        }

        Append(OutgoingEdges, edge.Source, edge);
        Append(IncomingEdges, edge.Target, edge);

        return true;
    }

    /// <summary>The incoming edges of a context (the Pred site-1 sweep reads them).</summary>
    /// <param name="targetId">The target context id.</param>
    /// <returns>The incoming edges.</returns>
    public IReadOnlyList<ContextEdge> Incoming(int targetId)
    {
        return IncomingEdges.TryGetValue(targetId, out List<ContextEdge>? edges) ? edges : NoEdges;
    }

    /// <summary>The outgoing edges of a context (the Pred site-2 sweep reads them).</summary>
    /// <param name="sourceId">The source context id.</param>
    /// <returns>The outgoing edges.</returns>
    public IReadOnlyList<ContextEdge> Outgoing(int sourceId)
    {
        return OutgoingEdges.TryGetValue(sourceId, out List<ContextEdge>? edges) ? edges : NoEdges;
    }

    /// <summary>Appends an edge to a source-or-target multi-map, creating the list on first use.</summary>
    /// <param name="map">The multi-map.</param>
    /// <param name="key">The source or target context id.</param>
    /// <param name="edge">The edge.</param>
    private static void Append(Dictionary<int, List<ContextEdge>> map, int key, ContextEdge edge)
    {
        if(!map.TryGetValue(key, out List<ContextEdge>? edges))
        {
            edges = [];
            map[key] = edges;
        }

        edges.Add(edge);
    }
}
