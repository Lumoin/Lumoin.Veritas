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
/// A protected term always wins the representative choice over an
/// unprotected root regardless of clique sizes, and the smaller
/// identifier wins between two protected roots — a consumer whose rules
/// read certain terms by identifier keeps those reads faithful under
/// canonicalization. The preference trades the pure weighted bound for
/// representative stability: a merge into a protected root relabels the
/// unprotected side whole, which stays linear per element because an
/// element's root changes at most once onto each protected root it ever
/// joins.
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
    private Dictionary<TermId, CanonicalTermId> Parent { get; } = [];

    /// <summary>Per-root member lists, the root included; only cliques of two or more have entries.</summary>
    private Dictionary<CanonicalTermId, List<TermId>> MembersByRoot { get; } = [];

    /// <summary>The terms that win the representative choice; empty when no protection was configured.</summary>
    private IReadOnlySet<TermId> ProtectedRepresentatives { get; }

    /// <summary>The shared empty protection set of the default constructor path.</summary>
    private static HashSet<TermId> NoProtection { get; } = [];

    /// <summary>
    /// Initialises the equivalence store.
    /// </summary>
    /// <param name="protectedRepresentatives">
    /// The terms that must stay clique representatives — a protected term
    /// wins the choice over any unprotected root, and the smaller
    /// identifier wins between two protected roots; <see langword="null"/>
    /// for the plain weighted union.
    /// </param>
    public OwlSameAsEquivalence(IReadOnlySet<TermId>? protectedRepresentatives = null)
    {
        ProtectedRepresentatives = protectedRepresentatives ?? NoProtection;
    }

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
    /// was never merged. The sole producer of
    /// <see cref="CanonicalTermId"/>: canonical space is entered here and
    /// nowhere else.
    /// </summary>
    /// <param name="term">The term to resolve.</param>
    /// <returns>The representative, typed canonical.</returns>
    public CanonicalTermId Find(TermId term)
    {
        return Parent.TryGetValue(term, out CanonicalTermId root) ? root : new CanonicalTermId(term);
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
    /// Merges the two terms' equivalence classes. The kept root is the
    /// protected one when exactly one side is protected, the smaller
    /// identifier when both are, and the larger clique's otherwise — the
    /// plain weighted union.
    /// </summary>
    /// <param name="left">The first term.</param>
    /// <param name="right">The second term.</param>
    /// <returns><see langword="true"/> when a merge happened; <see langword="false"/> when the terms were already equivalent.</returns>
    public bool Union(TermId left, TermId right)
    {
        CanonicalTermId leftRoot = Find(left);
        CanonicalTermId rightRoot = Find(right);
        if(leftRoot == rightRoot)
        {
            return false;
        }

        List<TermId> leftMembers = MembersOf(leftRoot);
        List<TermId> rightMembers = MembersOf(rightRoot);
        bool leftWins = (ProtectedRepresentatives.Contains(leftRoot.Id), ProtectedRepresentatives.Contains(rightRoot.Id)) switch
        {
            (true, false) => true,
            (false, true) => false,
            (true, true) => leftRoot.CompareTo(rightRoot) <= 0,
            (false, false) => leftMembers.Count >= rightMembers.Count,
        };

        (CanonicalTermId keptRoot, List<TermId> kept, CanonicalTermId droppedRoot, List<TermId> dropped) = leftWins
            ? (leftRoot, leftMembers, rightRoot, rightMembers)
            : (rightRoot, rightMembers, leftRoot, leftMembers);

        foreach(TermId member in dropped)
        {
            Parent[member] = keptRoot;
            kept.Add(member);
        }

        MembersByRoot.Remove(droppedRoot);

        return true;
    }

    /// <summary>The members of the term's equivalence class, the term itself included.</summary>
    /// <param name="term">The term to look up.</param>
    /// <returns>The clique members, or a single-element view for a term never merged.</returns>
    public IReadOnlyList<TermId> EquivalentTo(TermId term)
    {
        CanonicalTermId root = Find(term);

        return MembersByRoot.TryGetValue(root, out List<TermId>? members) ? members : [root.Id];
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
            Find(triple.Subject).Id.Encoded,
            Find(triple.Predicate).Id.Encoded,
            Find(triple.Object).Id.Encoded);
    }

    /// <summary>The root's member list, created as a singleton on first merge contact.</summary>
    /// <param name="root">The clique root.</param>
    /// <returns>The mutable member list.</returns>
    private List<TermId> MembersOf(CanonicalTermId root)
    {
        if(!MembersByRoot.TryGetValue(root, out List<TermId>? members))
        {
            members = [root.Id];
            MembersByRoot[root] = members;
            Parent[root.Id] = root;
        }

        return members;
    }
}
