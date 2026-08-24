using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The reason strings a ground clash carries — an information string, not a
/// verdict: the reasoner reads the clash flag and answers decisively, the reason
/// records which of the pre-merge or closure checks fired for traces and the
/// battery. Each formatter renders a stable leading identifier so a row can pin
/// the clash kind without depending on the interned role or representative
/// spelling.
/// </summary>
internal static class GroundClashReasons
{
    /// <summary>The reason for a pre-merge representative collision: a <c>DifferentIndividuals</c> pair whose members share a representative after the <c>SameIndividual</c> unions.</summary>
    /// <param name="representative">The shared representative key.</param>
    /// <returns>The reason string.</returns>
    public static string PreMergeCollision(Utf8String representative)
    {
        return string.Format(CultureInfo.InvariantCulture, "GroundMergeCollision({0})", representative);
    }

    /// <summary>The reason for an entailed negative object-property assertion: the closure contains the denied directioned edge.</summary>
    /// <param name="role">The rendered denied role.</param>
    /// <param name="source">The source representative key.</param>
    /// <param name="target">The target representative key.</param>
    /// <returns>The reason string.</returns>
    public static string NegativeEdgeEntailed(string role, Utf8String source, Utf8String target)
    {
        return string.Format(CultureInfo.InvariantCulture, "NegativeEdgeEntailed({0}: {1} -> {2})", role, source, target);
    }

    /// <summary>The reason for a disjoint-role violation: a representative pair carrying both disjoint roles in the closure (the asymmetric self-inverse pair renders as this too, the <c>Asy(R) ⟺ Dis(R, R⁻)</c> reduction).</summary>
    /// <param name="first">The rendered first disjoint role.</param>
    /// <param name="second">The rendered second disjoint role.</param>
    /// <param name="source">The source representative key.</param>
    /// <param name="target">The target representative key.</param>
    /// <returns>The reason string.</returns>
    public static string DisjointRolesViolated(string first, string second, Utf8String source, Utf8String target)
    {
        return string.Format(CultureInfo.InvariantCulture, "DisjointRolesViolated({0}, {1}: {2} -> {3})", first, second, source, target);
    }

    /// <summary>The reason for an irreflexivity violation: the closure contains a self-loop of the irreflexive role at a representative.</summary>
    /// <param name="role">The rendered irreflexive role.</param>
    /// <param name="node">The representative carrying the loop.</param>
    /// <returns>The reason string.</returns>
    public static string IrreflexivityViolated(string role, Utf8String node)
    {
        return string.Format(CultureInfo.InvariantCulture, "IrreflexivityViolated({0}: {1})", role, node);
    }

    /// <summary>The reason for a key-forced merge collision: a <c>DifferentIndividuals</c> pair whose members share a representative after a key-value join fired a union (a told-pass collision renders as <see cref="PreMergeCollision"/> instead).</summary>
    /// <param name="representative">The shared representative key.</param>
    /// <returns>The reason string.</returns>
    public static string KeyMergeCollision(Utf8String representative)
    {
        return string.Format(CultureInfo.InvariantCulture, "KeyMergeCollision({0})", representative);
    }

    /// <summary>The reason for a told ground pigeonhole: a told <c>≤n</c> counting constraint on a ground representative with <c>n + 1</c> pairwise told-distinct closed successors (told filler membership when qualified).</summary>
    /// <param name="representative">The constrained representative key.</param>
    /// <returns>The reason string.</returns>
    public static string GroundCountingPigeonhole(Utf8String representative)
    {
        return string.Format(CultureInfo.InvariantCulture, "GroundCountingPigeonhole({0})", representative);
    }

    /// <summary>Whether a clash reason names the told ground pigeonhole — the statistics assembly reads the counting rider's contribution off this stable leading identifier.</summary>
    /// <param name="reason">The clash reason string.</param>
    /// <returns><see langword="true"/> for a pigeonhole reason.</returns>
    public static bool IsGroundCountingPigeonhole(string? reason)
    {
        return reason is not null && reason.StartsWith("GroundCountingPigeonhole(", StringComparison.Ordinal);
    }
}

/// <summary>
/// The representative-level asserted-edge graph the ground slice's pure-edge clash
/// consumers are decided over: a directed edge set over
/// individual representatives keyed uniformly by <see cref="RawRoleId"/> — every
/// input and query key is raw (intake) space by type — closed to a fixpoint
/// under the told RBox (role hierarchy and equivalences, the directioned inverse
/// mirror, symmetric roles, transitive and general property chains, and the
/// reflexive characteristic lifting a loop per representative up the hierarchy).
/// The closure exists because edge-shape clashes — a denied entailed edge, an
/// asymmetry or irreflexivity or role-disjointness violation — are invisible to
/// the context clause machinery, which only ever carries an outgoing successor
/// atom <c>r(x, f(x))</c> and never a cycle between two named nodes.
/// </summary>
/// <remarks>
/// The closure is re-runnable over an augmented base: the post-saturation
/// Self-ghost pass adds one loop per unconditionally derived self atom through
/// <see cref="AddSelfLoops"/>, then re-runs <see cref="Close"/> and re-checks
/// <see cref="DetectClash"/> over the full rule set — a ghost loop can be a chain
/// component, so a membership-only re-check would be unsound. Bounded by
/// <c>|representatives|² × |role symbols|</c>, statically finite: every closure
/// step is monotone over that finite space, and the fixpoint loop and chain
/// traversal are explicit worklists with no recursion.
/// </remarks>
internal sealed class GroundAssertionGraph
{
    /// <summary>The symbol table naming roles for the clash reasons.</summary>
    private ContextSymbolTable Symbols { get; }

    /// <summary>The representative keys — the graph's nodes — for the reflexive-loop seeding and the irreflexivity scan.</summary>
    private IReadOnlyList<Utf8String> Representatives { get; }

    /// <summary>The reflexive-transitive directioned super-role closure (super-roles per raw directioned role), inverse-coupled — the told hierarchy plus symmetric and inverse coupling; a mutual-inclusion class carries arcs between all its members, so a closed edge is present under every spelling of its class.</summary>
    private IReadOnlyDictionary<RawRoleId, HashSet<RawRoleId>> SuperRoles { get; }

    /// <summary>The told property chains (a raw directioned word and its raw directioned super role), transitivity being the length-two <c>R∘R ⊑ R</c> word — composed on the finite graph.</summary>
    private IReadOnlyList<(IReadOnlyList<RawRoleId> Word, RawRoleId Super)> Chains { get; }

    /// <summary>The negative object-property obligations: a denied raw directioned edge over representatives; a clash when the closure contains it.</summary>
    private IReadOnlyList<(Utf8String Source, RawRoleId Role, Utf8String Target)> NegativeObligations { get; }

    /// <summary>The disjoint raw directioned-role pairs (a <c>DisjointObjectProperties</c> operand pair, or the <c>(R, R⁻)</c> pair of an asymmetric role); a clash when the closure carries some representative pair over both.</summary>
    private IReadOnlyList<(RawRoleId First, RawRoleId Second)> DisjointRolePairs { get; }

    /// <summary>The irreflexive raw directioned roles; a clash when the closure carries a self-loop of one at a representative.</summary>
    private IReadOnlyCollection<RawRoleId> IrreflexiveRoles { get; }

    /// <summary>The directed closure edges as (source, raw directioned role, target) triples — the membership set the clash checks read.</summary>
    private HashSet<(Utf8String From, RawRoleId Role, Utf8String To)> Edges { get; } = [];

    /// <summary>The per-(node, role) successor lists the chain composition traverses, kept in step with <see cref="Edges"/>.</summary>
    private Dictionary<(Utf8String Node, RawRoleId Role), List<Utf8String>> Outgoing { get; } = [];

    /// <summary>Initialises the graph from the asserted base edges, the reflexive loops per representative, the told RBox facts, and the clash obligations; the base edges and reflexive loops seed <see cref="Edges"/>, and <see cref="Close"/> then computes the fixpoint.</summary>
    /// <param name="symbols">The symbol table naming roles for the clash reasons.</param>
    /// <param name="representatives">The representative keys — the graph's nodes.</param>
    /// <param name="baseEdges">The asserted object-property edges over representatives.</param>
    /// <param name="superRoles">The reflexive-transitive directioned super-role closure.</param>
    /// <param name="chains">The told property chains.</param>
    /// <param name="reflexiveRoles">The reflexive directioned roles, each seeding a loop per representative.</param>
    /// <param name="negativeObligations">The negative object-property obligations.</param>
    /// <param name="disjointRolePairs">The disjoint directioned-role pairs.</param>
    /// <param name="irreflexiveRoles">The irreflexive directioned roles.</param>
    public GroundAssertionGraph(
        ContextSymbolTable symbols,
        IReadOnlyList<Utf8String> representatives,
        IReadOnlyList<(Utf8String Source, RawRoleId Role, Utf8String Target)> baseEdges,
        IReadOnlyDictionary<RawRoleId, HashSet<RawRoleId>> superRoles,
        IReadOnlyList<(IReadOnlyList<RawRoleId> Word, RawRoleId Super)> chains,
        IReadOnlyCollection<RawRoleId> reflexiveRoles,
        IReadOnlyList<(Utf8String Source, RawRoleId Role, Utf8String Target)> negativeObligations,
        IReadOnlyList<(RawRoleId First, RawRoleId Second)> disjointRolePairs,
        IReadOnlyCollection<RawRoleId> irreflexiveRoles)
    {
        Symbols = symbols;
        Representatives = representatives;
        SuperRoles = superRoles;
        Chains = chains;
        NegativeObligations = negativeObligations;
        DisjointRolePairs = disjointRolePairs;
        IrreflexiveRoles = irreflexiveRoles;

        foreach((Utf8String source, RawRoleId role, Utf8String target) in baseEdges)
        {
            AddWithMirror(source, role, target);
        }

        foreach(RawRoleId role in reflexiveRoles)
        {
            foreach(Utf8String node in representatives)
            {
                AddWithMirror(node, role, node);
            }
        }
    }

    /// <summary>An empty graph over a symbol table — the ground handle of a whole-module or reserved-role rejection, which delegates and never consults the closure.</summary>
    /// <param name="symbols">The symbol table.</param>
    /// <returns>The empty graph.</returns>
    public static GroundAssertionGraph Empty(ContextSymbolTable symbols)
    {
        return new GroundAssertionGraph(symbols, [], [], new Dictionary<RawRoleId, HashSet<RawRoleId>>(), [], [], [], [], []);
    }

    /// <summary>Adds one loop <c>role(node, node)</c> per pair to the base and returns whether any edge was new — the Self-ghost pass augmentation the caller follows with a fresh <see cref="Close"/> and <see cref="DetectClash"/>.</summary>
    /// <param name="loops">The (node, directioned role) loops the completed saturation's unconditional self atoms contribute.</param>
    /// <returns><see langword="true"/> when at least one loop edge was newly added.</returns>
    public bool AddSelfLoops(IReadOnlyList<(Utf8String Node, RawRoleId Role)> loops)
    {
        bool changed = false;
        foreach((Utf8String node, RawRoleId role) in loops)
        {
            changed |= AddWithMirror(node, role, node);
        }

        return changed;
    }

    /// <summary>Enumerates the graph's self-edges — every (node, directioned role) pair whose edge loops the node onto itself. Read after <see cref="Close"/>, where the hierarchy, chain-composition, inverse-mirror, and pre-merge lifts are all present, so one pass over the closed set sees every entailed ground loop.</summary>
    /// <returns>The (node, raw directioned role) self-loop pairs.</returns>
    public List<(Utf8String Node, RawRoleId Role)> SelfEdges()
    {
        List<(Utf8String Node, RawRoleId Role)> loops = [];
        foreach((Utf8String from, RawRoleId role, Utf8String to) in Edges)
        {
            if(from.Equals(to))
            {
                loops.Add((from, role));
            }
        }

        return loops;
    }

    /// <summary>The closed successors of a node over a raw directioned role — the key-value join's object-value readout and the counting rider's successor enumeration. Read after <see cref="Close"/>, where hierarchy lifting, chain composition, and the inverse mirror are all present.</summary>
    /// <param name="node">The source node.</param>
    /// <param name="role">The raw directioned role.</param>
    /// <returns>The closed successor nodes; empty when the node has none.</returns>
    public IReadOnlyList<Utf8String> TargetsOf(Utf8String node, RawRoleId role)
    {
        return Successors(node, role);
    }

    /// <summary>Computes the RBox closure to a fixpoint by an explicit fixpoint loop: hierarchy lifting over the directioned super-role closure and chain composition over the finite graph, re-iterated until no edge is added.</summary>
    public void Close()
    {
        bool grew = true;
        while(grew)
        {
            grew = false;
            List<(Utf8String From, RawRoleId Role, Utf8String To)> snapshot = [.. Edges];
            foreach((Utf8String from, RawRoleId role, Utf8String to) in snapshot)
            {
                if(SuperRoles.TryGetValue(role, out HashSet<RawRoleId>? supers))
                {
                    foreach(RawRoleId super in supers)
                    {
                        grew |= AddWithMirror(from, super, to);
                    }
                }
            }

            foreach((IReadOnlyList<RawRoleId> word, RawRoleId super) in Chains)
            {
                grew |= ComposeChain(word, super);
            }
        }
    }

    /// <summary>Runs the clash checks over the closed graph in a fixed order — negative obligations, disjoint-role pairs, then irreflexivity — returning the first violation's reason, or <see langword="null"/> when the closed graph carries no clash.</summary>
    /// <returns>The clash reason, or <see langword="null"/> when consistent.</returns>
    public string? DetectClash()
    {
        foreach((Utf8String source, RawRoleId role, Utf8String target) in NegativeObligations)
        {
            if(Edges.Contains((source, role, target)))
            {
                return GroundClashReasons.NegativeEdgeEntailed(Symbols.RenderRole(role), source, target);
            }
        }

        foreach((RawRoleId first, RawRoleId second) in DisjointRolePairs)
        {
            foreach((Utf8String from, RawRoleId role, Utf8String to) in Edges)
            {
                if(role == first && Edges.Contains((from, second, to)))
                {
                    return GroundClashReasons.DisjointRolesViolated(Symbols.RenderRole(first), Symbols.RenderRole(second), from, to);
                }
            }
        }

        foreach(RawRoleId role in IrreflexiveRoles)
        {
            foreach(Utf8String node in Representatives)
            {
                if(Edges.Contains((node, role, node)))
                {
                    return GroundClashReasons.IrreflexivityViolated(Symbols.RenderRole(role), node);
                }
            }
        }

        return null;
    }

    /// <summary>Composes a chain word over the graph, adding one super-role edge per (start, end) pair a path spelling the word connects; returns whether any edge was new. The frontier advances one letter at a time by explicit iteration, so no recursion arises.</summary>
    /// <param name="word">The directioned chain word.</param>
    /// <param name="super">The directioned super role the path entails.</param>
    /// <returns><see langword="true"/> when at least one composed edge was newly added.</returns>
    private bool ComposeChain(IReadOnlyList<RawRoleId> word, RawRoleId super)
    {
        List<(Utf8String Start, Utf8String Current)> frontier = [];
        foreach(Utf8String start in Representatives)
        {
            foreach(Utf8String next in Successors(start, word[0]))
            {
                frontier.Add((start, next));
            }
        }

        for(int letter = 1; letter < word.Count; letter++)
        {
            List<(Utf8String Start, Utf8String Current)> advanced = [];
            foreach((Utf8String start, Utf8String current) in frontier)
            {
                foreach(Utf8String next in Successors(current, word[letter]))
                {
                    advanced.Add((start, next));
                }
            }

            frontier = advanced;
        }

        bool changed = false;
        foreach((Utf8String start, Utf8String end) in frontier)
        {
            changed |= AddWithMirror(start, super, end);
        }

        return changed;
    }

    /// <summary>The shared empty successor list returned for a node with no edge over a role.</summary>
    private static List<Utf8String> NoSuccessors { get; } = [];

    /// <summary>The successors of a node over a raw directioned role, or an empty list when the node has none.</summary>
    /// <param name="node">The source node.</param>
    /// <param name="role">The raw directioned role.</param>
    /// <returns>The successor nodes.</returns>
    private List<Utf8String> Successors(Utf8String node, RawRoleId role)
    {
        return Outgoing.TryGetValue((node, role), out List<Utf8String>? list) ? list : NoSuccessors;
    }

    /// <summary>Adds a directioned edge and its inverse mirror <c>role⁻(target, source)</c>, returning whether either was new.</summary>
    /// <param name="source">The source node.</param>
    /// <param name="role">The raw directioned role.</param>
    /// <param name="target">The target node.</param>
    /// <returns><see langword="true"/> when at least one of the edge or its mirror was newly added.</returns>
    private bool AddWithMirror(Utf8String source, RawRoleId role, Utf8String target)
    {
        bool changed = AddDirected(source, role, target);
        changed |= AddDirected(target, ContextSymbolTable.Inverse(role), source);

        return changed;
    }

    /// <summary>Adds one directed edge to the membership set and the successor index, returning whether it was new.</summary>
    /// <param name="source">The source node.</param>
    /// <param name="role">The raw directioned role.</param>
    /// <param name="target">The target node.</param>
    /// <returns><see langword="true"/> when the edge was newly added.</returns>
    private bool AddDirected(Utf8String source, RawRoleId role, Utf8String target)
    {
        if(!Edges.Add((source, role, target)))
        {
            return false;
        }

        if(!Outgoing.TryGetValue((source, role), out List<Utf8String>? list))
        {
            list = [];
            Outgoing[(source, role)] = list;
        }

        list.Add(target);

        return true;
    }
}
