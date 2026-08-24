using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The result of a canonicalized RL closure: the closure over canonical
/// representatives, the equivalence store holding the <c>owl:sameAs</c>
/// cliques, and the canonical base it ran from.
/// </summary>
/// <param name="Result">The closure over the canonical base — derivations name representatives only.</param>
/// <param name="Equivalence">The clique store; with <see cref="Result"/> it is the complete answer the <c>eq-*</c> rules would have materialised.</param>
/// <param name="CanonicalBase">The canonical base triples the closure ran from (the input with every position canonicalized and the <c>sameAs</c> triples folded into <paramref name="Equivalence"/>).</param>
public sealed record OwlRlCanonicalResult(
    OwlRlResult Result,
    OwlSameAsEquivalence Equivalence,
    IReadOnlyCollection<EncodedTriple> CanonicalBase);

/// <summary>
/// The union-find variant of the RL closure: <c>owl:sameAs</c> is held as
/// an equivalence store and every other triple is canonicalized onto clique
/// representatives, so the closure never materialises the quadratic
/// <c>sameAs</c> permutations or the per-member triple copies the
/// <c>eq-rep-*</c> rules produce.
/// </summary>
/// <remarks>
/// <para>
/// <b>The loop.</b> Input <c>sameAs</c> triples seed the equivalence
/// store; everything else canonicalizes and runs through the standard
/// <see cref="OwlRlClosure"/>. Rules other than <c>eq-*</c> can still
/// derive <c>sameAs</c> facts (functional properties, keys, max
/// cardinality); each such derivation merges its cliques and the closure
/// re-runs over the re-canonicalized base, to the merge fixpoint. The
/// result names representatives only; <see cref="ExpandToMaterialization"/>
/// reproduces exactly what the rule-based closure materialises, which is
/// the differential oracle the variant is measured against.
/// </para>
/// <para>
/// <b>Falsities under collapse.</b> The verdicts match the rule-based
/// closure: a <c>differentFrom</c> whose sides collapse onto one
/// representative is the <c>eq-diff1</c> contradiction (detected at
/// canonicalization — the standard closure needs the materialized
/// <c>sameAs</c> premise this variant never writes), an
/// <c>owl:AllDifferent</c> list whose members collapse fires the standard
/// closure's own duplicate-member <c>eq-diff2</c>, and a merge joining two
/// literals the datatype oracle knows distinct is the <c>dt-diff</c>
/// contradiction, checked pairwise across the joining cliques. The
/// pairwise literal check matches the rule-based work; a value-keyed
/// canonicalization (one value representative per clique, O(1) per merge)
/// is the production refinement.
/// </para>
/// <para>
/// <b>Identity reads under collapse.</b> The rule set reads the
/// vocabulary, the seed terms, and the cardinality bounds by identifier
/// (<see cref="OwlRlTerms.IdentityReadTerms"/>), so the equivalence store
/// keeps those terms as their cliques' representatives; when two of them
/// share a clique the non-representative one re-enters the canonical base
/// as an explicit equality, and the delegate's own equality rules restore
/// its reads.
/// </para>
/// </remarks>
public static class OwlRlCanonicalClosure
{
    /// <summary>
    /// Computes the canonicalized RL closure of <paramref name="triples"/>.
    /// </summary>
    /// <param name="triples">The base triples, schema statements included.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the <c>dt-*</c> falsities; <see cref="OwlRlDatatypeOracle.None"/> disables them.</param>
    /// <param name="cancellationToken">A token that aborts derivation between rounds.</param>
    /// <returns>The canonical result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static OwlRlCanonicalResult Compute(
        IEnumerable<EncodedTriple> triples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(terms);

        OwlRlDatatypeOracle oracle = datatypeOracle.LiteralsKnownDistinct is null ? OwlRlDatatypeOracle.None : datatypeOracle;

        //The identity-read vocabulary must survive canonicalization as its
        //cliques' representatives: the delegate closure reads those terms
        //by identifier (rdf:nil's structure, owl:Thing's enumeration,
        //owl:Nothing's instances, rdf:type's index, the cardinality
        //bounds), and a representative choice rewriting one away would
        //silently disable every such read.
        OwlSameAsEquivalence equivalence = new(terms.IdentityReadTerms);
        List<EncodedTriple> input = [];
        List<EncodedTriple> asserted = [];
        foreach(EncodedTriple triple in triples)
        {
            asserted.Add(triple);
            if(triple.Predicate == terms.SameAs)
            {
                if(!TryMerge(equivalence, triple.Subject, triple.Object, oracle, triple, out string refusalRule, out ImmutableArray<EncodedTriple> premises))
                {
                    return Inconsistent(refusalRule, premises, equivalence, canonicalBase: [], asserted);
                }
            }
            else
            {
                input.Add(triple);
            }
        }

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //Re-canonicalize the base onto the current representatives. A
            //differentFrom collapsing onto one representative is the
            //eq-diff1 contradiction the rule-based closure reaches through
            //the materialized sameAs premise.
            HashSet<EncodedTriple> canonical = [];
            foreach(EncodedTriple triple in input)
            {
                EncodedTriple canonicalized = equivalence.Canonicalize(triple);
                if(canonicalized.Predicate == terms.DifferentFrom && canonicalized.Subject == canonicalized.Object)
                {
                    //The premise is the asserted triple, never the
                    //collapsed rewrite — the rewrite exists in no graph.
                    return Inconsistent(EntailmentRules.EqDiff1, [triple], equivalence, canonical, asserted);
                }

                canonical.Add(canonicalized);
            }

            //An identity-read term that lost the representative choice —
            //possible only when two protected terms share a clique —
            //re-enters as an explicit equality, so the delegate's eq-*
            //rules restore its edges and its fixed-identifier reads stay
            //faithful to the materialized closure.
            foreach(IReadOnlyList<TermId> clique in equivalence.Cliques)
            {
                CanonicalTermId representative = equivalence.Find(clique[0]);
                foreach(TermId member in clique)
                {
                    if(member != representative.Id && terms.IdentityReadTerms.Contains(member))
                    {
                        canonical.Add(EncodedTriple.FromEncoded(member.Encoded, terms.SameAs.Encoded, representative.Id.Encoded));
                        canonical.Add(EncodedTriple.FromEncoded(representative.Id.Encoded, terms.SameAs.Encoded, member.Encoded));
                    }
                }
            }

            OwlRlResult result = OwlRlClosure.Compute(canonical, terms, oracle, cancellationToken: cancellationToken);
            if(!result.IsConsistent)
            {
                return new OwlRlCanonicalResult(result, equivalence, canonical);
            }

            //Rules beyond eq-* derive sameAs facts; each merges its
            //cliques and the closure re-runs over the collapsed base.
            bool merged = false;
            foreach(EncodedTriple derived in result.Derived)
            {
                if(derived.Predicate != terms.SameAs || equivalence.AreEquivalent(derived.Subject, derived.Object))
                {
                    continue;
                }

                if(!TryMerge(equivalence, derived.Subject, derived.Object, oracle, derived, out string refusalRule, out ImmutableArray<EncodedTriple> premises))
                {
                    return Inconsistent(refusalRule, premises, equivalence, canonical, Concatenate(asserted, result.Derived));
                }

                merged = true;
            }

            if(!merged)
            {
                return new OwlRlCanonicalResult(result, equivalence, canonical);
            }
        }
    }

    /// <summary>
    /// Expands the canonical closure back to the full materialization the
    /// rule-based closure produces: every canonical triple over every
    /// member combination of its positions' cliques, plus every ordered
    /// <c>sameAs</c> pair (reflexive included) of every clique — the
    /// differential oracle, not a production path.
    /// </summary>
    /// <param name="canonicalResult">The canonical result to expand.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns>The full triple set: base, derivations, and the <c>sameAs</c> permutations.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IReadOnlyCollection<EncodedTriple> ExpandToMaterialization(OwlRlCanonicalResult canonicalResult, OwlRlTerms terms)
    {
        ArgumentNullException.ThrowIfNull(canonicalResult);
        ArgumentNullException.ThrowIfNull(terms);

        OwlSameAsEquivalence equivalence = canonicalResult.Equivalence;
        HashSet<EncodedTriple> full = [];

        foreach(EncodedTriple triple in Concatenate(canonicalResult.CanonicalBase, canonicalResult.Result.Derived))
        {
            foreach(TermId subject in equivalence.EquivalentTo(triple.Subject))
            {
                foreach(TermId predicate in equivalence.EquivalentTo(triple.Predicate))
                {
                    foreach(TermId @object in equivalence.EquivalentTo(triple.Object))
                    {
                        full.Add(EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded));
                    }
                }
            }
        }

        foreach(IReadOnlyList<TermId> clique in equivalence.Cliques)
        {
            foreach(TermId left in clique)
            {
                foreach(TermId right in clique)
                {
                    full.Add(EncodedTriple.FromEncoded(left.Encoded, terms.SameAs.Encoded, right.Encoded));
                }
            }
        }

        //The trans-chain extension mints its list nodes per property
        //identifier, so the rule-based closure carries one chain structure
        //per clique MEMBER of a transitive property (the eq-rep-* rules
        //replicate the typing onto every member, each minting its own
        //nodes) while the canonical run carries only the representative's.
        //The oracle synthesizes the member structures, every clique term
        //substituted through the property positions exactly as eq-rep-*
        //would have written them, and eq-ref's reflexive equality of each
        //minted node beside them.
        foreach(EncodedTriple triple in Concatenate(canonicalResult.CanonicalBase, canonicalResult.Result.Derived))
        {
            if(triple.Predicate != terms.Type || triple.Object != terms.TransitiveProperty)
            {
                continue;
            }

            IReadOnlyList<TermId> members = equivalence.EquivalentTo(triple.Subject);
            if(members.Count < 2)
            {
                continue;
            }

            foreach(TermId member in members)
            {
                TermId head = terms.TransitivityChainNode(member, 0);
                TermId tail = terms.TransitivityChainNode(member, 1);
                full.Add(EncodedTriple.FromEncoded(head.Encoded, terms.Rest.Encoded, tail.Encoded));
                full.Add(EncodedTriple.FromEncoded(tail.Encoded, terms.Rest.Encoded, terms.Nil.Encoded));
                full.Add(EncodedTriple.FromEncoded(head.Encoded, terms.SameAs.Encoded, head.Encoded));
                full.Add(EncodedTriple.FromEncoded(tail.Encoded, terms.SameAs.Encoded, tail.Encoded));
                foreach(TermId substituted in members)
                {
                    full.Add(EncodedTriple.FromEncoded(substituted.Encoded, terms.PropertyChainAxiom.Encoded, head.Encoded));
                    full.Add(EncodedTriple.FromEncoded(head.Encoded, terms.First.Encoded, substituted.Encoded));
                    full.Add(EncodedTriple.FromEncoded(tail.Encoded, terms.First.Encoded, substituted.Encoded));
                }
            }
        }

        return full;
    }

    /// <summary>
    /// Merges two terms' cliques after the datatype checks the rule-based
    /// <c>eq-*</c> path performs on every materialized pair: a merge
    /// joining literals the oracle knows distinct, or datatypes the oracle
    /// knows value-space-disjoint, contradicts. Canonicalization consumes
    /// the <c>sameAs</c> edge before the inner closure runs, so this walk
    /// is the canonical engine's only detection point for both falsities.
    /// </summary>
    /// <param name="equivalence">The equivalence store.</param>
    /// <param name="left">The first term.</param>
    /// <param name="right">The second term.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="trigger">The asserted or derived <c>sameAs</c> triple whose merge is attempted — the contradiction premise when the merge fails.</param>
    /// <param name="refusalRule">The falsity rule that refused the merge; empty when the merge succeeded.</param>
    /// <param name="premises">The matched triple when the merge fails.</param>
    /// <returns><see langword="false"/> when the merge contradicts the oracle.</returns>
    private static bool TryMerge(
        OwlSameAsEquivalence equivalence,
        TermId left,
        TermId right,
        OwlRlDatatypeOracle oracle,
        EncodedTriple trigger,
        out string refusalRule,
        out ImmutableArray<EncodedTriple> premises)
    {
        if(oracle.LiteralsKnownDistinct != OwlRlDatatypeOracle.None.LiteralsKnownDistinct
            || oracle.DatatypesKnownDisjoint != OwlRlDatatypeOracle.None.DatatypesKnownDisjoint)
        {
            foreach(TermId leftMember in equivalence.EquivalentTo(left))
            {
                foreach(TermId rightMember in equivalence.EquivalentTo(right))
                {
                    if(leftMember == rightMember)
                    {
                        continue;
                    }

                    //The premise is the sameAs edge that triggered the
                    //merge — the one matched triple; the chain reaching
                    //the contradicting members awaits merge provenance.
                    if(oracle.LiteralsKnownDistinct(leftMember, rightMember))
                    {
                        refusalRule = EntailmentRules.DtDiff;
                        premises = [trigger];

                        return false;
                    }

                    if(oracle.DatatypesKnownDisjoint(leftMember, rightMember))
                    {
                        refusalRule = EntailmentRules.DtDisjointIdentity;
                        premises = [trigger];

                        return false;
                    }
                }
            }
        }

        equivalence.Union(left, right);
        refusalRule = string.Empty;
        premises = [];

        return true;
    }

    /// <summary>An inconsistent canonical result with the falsity rule and its premises.</summary>
    /// <param name="rule">The falsity rule name.</param>
    /// <param name="premises">The contradicting triples.</param>
    /// <param name="equivalence">The equivalence store at the point of contradiction.</param>
    /// <param name="canonicalBase">The canonical base at the point of contradiction.</param>
    /// <param name="reasonedOver">The asserted and derived triples the falsity may cite — premise fidelity is structural.</param>
    /// <returns>The inconsistent result.</returns>
    /// <exception cref="InvalidOperationException">A reported premise is absent from the reasoned-over graph — a fabricated premise is an invariant violation, never a report.</exception>
    private static OwlRlCanonicalResult Inconsistent(
        string rule,
        ImmutableArray<EncodedTriple> premises,
        OwlSameAsEquivalence equivalence,
        IReadOnlyCollection<EncodedTriple> canonicalBase,
        IEnumerable<EncodedTriple> reasonedOver)
    {
        foreach(EncodedTriple premise in premises)
        {
            bool held = false;
            foreach(EncodedTriple candidate in reasonedOver)
            {
                if(candidate == premise)
                {
                    held = true;
                    break;
                }
            }

            if(!held)
            {
                throw new InvalidOperationException($"Rule {rule} reported a premise absent from the reasoned-over graph.");
            }
        }

        return new OwlRlCanonicalResult(
            new OwlRlResult([], isConsistent: false, rule, premises, malformedShapes: []),
            equivalence,
            canonicalBase);
    }

    /// <summary>The two collections in sequence, without copying.</summary>
    /// <param name="first">The first collection.</param>
    /// <param name="second">The second collection.</param>
    /// <returns>The concatenated sequence.</returns>
    private static IEnumerable<EncodedTriple> Concatenate(IReadOnlyCollection<EncodedTriple> first, IReadOnlyCollection<EncodedTriple> second)
    {
        foreach(EncodedTriple triple in first)
        {
            yield return triple;
        }

        foreach(EncodedTriple triple in second)
        {
            yield return triple;
        }
    }
}
