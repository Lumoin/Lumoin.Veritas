using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The union-find representation of an <c>owl:sameAs</c> equivalence: each
/// clique of equal terms is held once under a canonical representative,
/// instead of materialising the quadratic <c>sameAs</c> permutations and
/// the per-member triple copies the <c>eq-*</c> rules produce.
/// </summary>
/// <remarks>
/// <para>
/// Merges relabel the smaller clique onto the larger one's root (weighted
/// union with direct parent pointers), so <see cref="Find"/> is a single
/// dictionary probe and the total relabelling work is O(n log n) over any
/// merge sequence. The member lists the relabelling maintains are the
/// same lists expansion back to the full materialization reads — nothing
/// is kept twice.
/// </para>
/// <para>
/// A term never merged is its own representative and carries no entry;
/// <see cref="Cliques"/> therefore enumerates only genuine equivalence
/// classes (two members or more).
/// </para>
/// </remarks>
[DebuggerDisplay("OwlSameAsEquivalence Cliques={MembersByRoot.Count}")]
public sealed class OwlSameAsEquivalence
{
    /// <summary>Per-term direct pointer to its clique root; absent for terms never merged.</summary>
    private Dictionary<TermId, TermId> Parent { get; } = [];

    /// <summary>Per-root member lists, the root included; only cliques of two or more have entries.</summary>
    private Dictionary<TermId, List<TermId>> MembersByRoot { get; } = [];

    /// <summary>The number of equivalence classes with two or more members.</summary>
    public int CliqueCount
    {
        get
        {
            return MembersByRoot.Count;
        }
    }

    /// <summary>
    /// The canonical representative of the term — the term itself when it
    /// was never merged.
    /// </summary>
    /// <param name="term">The term to resolve.</param>
    /// <returns>The representative.</returns>
    public TermId Find(TermId term)
    {
        return Parent.TryGetValue(term, out TermId root) ? root : term;
    }

    /// <summary>Whether the two terms are in the same equivalence class.</summary>
    /// <param name="left">The first term.</param>
    /// <param name="right">The second term.</param>
    /// <returns><see langword="true"/> when the representatives coincide.</returns>
    public bool AreEquivalent(TermId left, TermId right)
    {
        return Find(left) == Find(right);
    }

    /// <summary>
    /// Merges the two terms' equivalence classes, relabelling the smaller
    /// clique onto the larger one's root.
    /// </summary>
    /// <param name="left">The first term.</param>
    /// <param name="right">The second term.</param>
    /// <returns><see langword="true"/> when a merge happened; <see langword="false"/> when the terms were already equivalent.</returns>
    public bool Union(TermId left, TermId right)
    {
        TermId leftRoot = Find(left);
        TermId rightRoot = Find(right);
        if(leftRoot == rightRoot)
        {
            return false;
        }

        List<TermId> leftMembers = MembersOf(leftRoot);
        List<TermId> rightMembers = MembersOf(rightRoot);
        (TermId bigRoot, List<TermId> big, TermId smallRoot, List<TermId> small) = leftMembers.Count >= rightMembers.Count
            ? (leftRoot, leftMembers, rightRoot, rightMembers)
            : (rightRoot, rightMembers, leftRoot, leftMembers);

        foreach(TermId member in small)
        {
            Parent[member] = bigRoot;
            big.Add(member);
        }

        MembersByRoot.Remove(smallRoot);

        return true;
    }

    /// <summary>The members of the term's equivalence class, the term itself included.</summary>
    /// <param name="term">The term to look up.</param>
    /// <returns>The clique members, or a single-element view for a term never merged.</returns>
    public IReadOnlyList<TermId> EquivalentTo(TermId term)
    {
        TermId root = Find(term);

        return MembersByRoot.TryGetValue(root, out List<TermId>? members) ? members : [root];
    }

    /// <summary>The equivalence classes with two or more members.</summary>
    public IEnumerable<IReadOnlyList<TermId>> Cliques
    {
        get
        {
            return MembersByRoot.Values;
        }
    }

    /// <summary>The triple with every position replaced by its representative.</summary>
    /// <param name="triple">The triple to canonicalize.</param>
    /// <returns>The canonical triple.</returns>
    public EncodedTriple Canonicalize(EncodedTriple triple)
    {
        return EncodedTriple.FromEncoded(
            Find(triple.Subject).Encoded,
            Find(triple.Predicate).Encoded,
            Find(triple.Object).Encoded);
    }

    /// <summary>The root's member list, created as a singleton on first merge contact.</summary>
    /// <param name="root">The clique root.</param>
    /// <returns>The mutable member list.</returns>
    private List<TermId> MembersOf(TermId root)
    {
        if(!MembersByRoot.TryGetValue(root, out List<TermId>? members))
        {
            members = [root];
            MembersByRoot[root] = members;
            Parent[root] = root;
        }

        return members;
    }
}
