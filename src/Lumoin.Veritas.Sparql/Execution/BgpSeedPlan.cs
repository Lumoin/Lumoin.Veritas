using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;
using EncodedTriplePattern = Lumoin.Veritas.Core.Hypertrie.Query.TriplePattern;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The per-site <c>EXISTS</c> seeding plan (the indexed per-binding probe): the inner core's encoding
/// skeleton computed once — the pattern walk, registry, and projection map never recompute — plus the
/// seedable pattern positions per distinguished variable. Per binding, the bound seed variables are
/// PATCHED onto the skeleton as bound positions so the backend does an indexed lookup instead of an
/// unconstrained scan; the mechanical soundness rule then compares the patched encoding's rewrite set
/// against the skeleton's and DECLINES seeding for that binding on ANY difference (the compatibility path
/// answers instead) — never seed into a position that would newly trigger a per-solution rewrite.
/// Variables participating in a within-pattern self-join equality are excluded from seeding at plan level
/// (a bound position produces no binding, which would starve the equality filter), and a plan is built at
/// all only for a triple-term-free bare-BGP core. Seeding is an optimisation UNDER the caller's
/// always-applied compatibility check: the seeded source yields exactly the
/// compatible-on-seeded-variables subset of the unconstrained stream.
/// </summary>
internal sealed class BgpSeedPlan
{
    private readonly BgpMachinery.EncodedBgp skeleton;

    private readonly List<(int PatternIndex, List<TermId> Alternatives)> skeletonExpansions;

    private readonly Dictionary<SparqlVariable, List<(int Pattern, int Position)>> seedTargets;

    /// <summary>Constructs the plan; called by <see cref="TryBuild"/> only.</summary>
    /// <param name="skeleton">The core BGP's encoding skeleton.</param>
    /// <param name="skeletonExpansions">The skeleton's type-expansion plan, cached for the per-binding diff.</param>
    /// <param name="seedTargets">The seedable positions per distinguished variable.</param>
    private BgpSeedPlan(
        BgpMachinery.EncodedBgp skeleton,
        List<(int PatternIndex, List<TermId> Alternatives)> skeletonExpansions,
        Dictionary<SparqlVariable, List<(int Pattern, int Position)>> seedTargets)
    {
        this.skeleton = skeleton;
        this.skeletonExpansions = skeletonExpansions;
        this.seedTargets = seedTargets;
    }

    /// <summary>The core BGP's encoding skeleton (shared with the unseeded cursor configuration).</summary>
    public BgpMachinery.EncodedBgp Skeleton => skeleton;

    /// <summary>The skeleton's type-expansion plan (the unseeded cursor iterates these variants).</summary>
    public List<(int PatternIndex, List<TermId> Alternatives)> SkeletonExpansions => skeletonExpansions;

    /// <summary>
    /// Builds the plan for an <c>EXISTS</c> core, or <see langword="null"/> when the shape declines seeding:
    /// the core is not a bare <see cref="Bgp"/>, its encoding failed or carries triple-term destructurings,
    /// or no seedable position remains after excluding self-join-equality participants.
    /// </summary>
    /// <param name="core">The site's emptiness-preserving core algebra.</param>
    /// <param name="machinery">The shared BGP machinery.</param>
    /// <returns>The plan, or <see langword="null"/>.</returns>
    public static BgpSeedPlan? TryBuild(AlgebraOperator core, BgpMachinery machinery)
    {
        if(core is not Bgp bgp)
        {
            return null;
        }

        BgpMachinery.EncodedBgp encoded = machinery.EncodeBgp(bgp);
        if(!encoded.Encodable || encoded.TripleTermMatches.Count > 0)
        {
            return null;
        }

        //Variables participating in a self-join equality never seed: the equality filter reads the
        //ORIGINAL variable's binding, which a bound position no longer produces.
        HashSet<Variable>? equalityParticipants = null;
        foreach((Variable original, Variable fresh) in encoded.SelfJoinEqualities)
        {
            equalityParticipants ??= [];
            equalityParticipants.Add(original);
            equalityParticipants.Add(fresh);
        }

        Dictionary<SparqlVariable, List<(int Pattern, int Position)>> targets = [];
        for(int patternIndex = 0; patternIndex < encoded.Patterns.Count; patternIndex++)
        {
            EncodedTriplePattern pattern = encoded.Patterns[patternIndex];
            AddTarget(targets, encoded, equalityParticipants, pattern.Subject, patternIndex, 0);
            AddTarget(targets, encoded, equalityParticipants, pattern.Predicate, patternIndex, 1);
            AddTarget(targets, encoded, equalityParticipants, pattern.Object, patternIndex, 2);
        }

        if(targets.Count == 0)
        {
            return null;
        }

        return new BgpSeedPlan(encoded, machinery.ComputeTypeExpansions(encoded.Patterns), targets);
    }

    /// <summary>Records one seedable position when it holds a distinguished, equality-free variable.</summary>
    /// <param name="targetsToAppendTo">The accumulating per-variable position lists.</param>
    /// <param name="encoded">The encoding skeleton (its projection map decides distinguishedness).</param>
    /// <param name="equalityParticipants">The self-join-equality variables excluded from seeding, or <see langword="null"/> when none exist.</param>
    /// <param name="position">The pattern position to inspect.</param>
    /// <param name="patternIndex">The pattern's index.</param>
    /// <param name="positionIndex">The position within the pattern: 0 subject, 1 predicate, 2 object.</param>
    private static void AddTarget(
        Dictionary<SparqlVariable, List<(int Pattern, int Position)>> targetsToAppendTo,
        BgpMachinery.EncodedBgp encoded,
        HashSet<Variable>? equalityParticipants,
        PatternPosition position,
        int patternIndex,
        int positionIndex)
    {
        if(!position.IsVariable)
        {
            return;
        }

        Variable backend = position.Variable;
        if(equalityParticipants is not null && equalityParticipants.Contains(backend))
        {
            return;
        }

        if(!encoded.ToSparql.TryGetValue(backend, out SparqlVariable variable))
        {
            return;
        }

        if(!targetsToAppendTo.TryGetValue(variable, out List<(int Pattern, int Position)>? positions))
        {
            positions = [];
            targetsToAppendTo[variable] = positions;
        }

        positions.Add((patternIndex, positionIndex));
    }

    /// <summary>
    /// Patches the pre-binding's bound seed variables onto the skeleton for one binding. Outcomes, in
    /// precedence order: <paramref name="impossible"/> when a seed term is absent from the dictionary (no
    /// row can match it — the binding's answer is false without pulling); a <see langword="false"/> return
    /// when the mechanical rewrite-set diff declines (the patched encoding's type-expansion plan differs
    /// from the skeleton's — the SEM-1 carve-out; the caller probes unseeded); otherwise
    /// <paramref name="patchedPatterns"/> receives the seeded pattern list, or <see langword="null"/> when
    /// the binding binds no seed variable (unseeded is already exact).
    /// </summary>
    /// <param name="preBinding">The outer row to seed from.</param>
    /// <param name="machinery">The shared BGP machinery (dictionary lookups and the expansion plan).</param>
    /// <param name="patchedPatterns">Receives the seeded patterns, or <see langword="null"/> when nothing seeded.</param>
    /// <param name="impossible">Receives whether a seed term is absent from the dictionary, deciding the binding false outright.</param>
    /// <returns><see langword="true"/> when seeding (or the no-op) is sound for this binding; <see langword="false"/> when the diff declines it.</returns>
    public bool TryPatch(SparqlSolution preBinding, BgpMachinery machinery, out List<EncodedTriplePattern>? patchedPatterns, out bool impossible)
    {
        patchedPatterns = null;
        impossible = false;

        List<EncodedTriplePattern>? patched = null;
        foreach(KeyValuePair<SparqlVariable, List<(int Pattern, int Position)>> target in seedTargets)
        {
            if(!preBinding.TryGetValue(target.Key, out RdfTerm term))
            {
                continue;
            }

            TermId id = machinery.Dictionary.GetIdOrDefault(term);
            if(id.IsNone)
            {
                //A term absent from the data graph appears in no row; the compatibility filter could never
                //accept one either, so the binding's EXISTS is false without opening a source.
                impossible = true;

                return true;
            }

            patched ??= [.. skeleton.Patterns];
            foreach((int patternIndex, int positionIndex) in target.Value)
            {
                EncodedTriplePattern original = patched[patternIndex];
                PatternPosition bound = PatternPosition.Bound(id);
                patched[patternIndex] = positionIndex switch
                {
                    0 => new EncodedTriplePattern(bound, original.Predicate, original.Object),
                    1 => new EncodedTriplePattern(original.Subject, bound, original.Object),
                    _ => new EncodedTriplePattern(original.Subject, original.Predicate, bound),
                };
            }
        }

        if(patched is null)
        {
            return true;
        }

        //The mechanical SEM-1 rule: the seeded encoding must trigger exactly the rewrites the unbound
        //skeleton triggers. The self-join and triple-term sets are fixed by the skeleton (patching never
        //re-encodes), so the type-expansion plan is the one movable set — any difference (a seeded rdf:type
        //object under an active ladder; a predicate seeded to rdf:type over a bound object) declines.
        if(!ExpansionsEqual(machinery.ComputeTypeExpansions(patched), skeletonExpansions))
        {
            return false;
        }

        patchedPatterns = patched;

        return true;
    }

    /// <summary>Structural equality of two type-expansion plans (pattern indices and alternative lists, in order).</summary>
    /// <param name="left">The first plan.</param>
    /// <param name="right">The second plan.</param>
    /// <returns><see langword="true"/> when the plans are identical.</returns>
    private static bool ExpansionsEqual(List<(int PatternIndex, List<TermId> Alternatives)> left, List<(int PatternIndex, List<TermId> Alternatives)> right)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        for(int i = 0; i < left.Count; i++)
        {
            (int leftIndex, List<TermId> leftAlternatives) = left[i];
            (int rightIndex, List<TermId> rightAlternatives) = right[i];
            if(leftIndex != rightIndex || leftAlternatives.Count != rightAlternatives.Count)
            {
                return false;
            }

            for(int j = 0; j < leftAlternatives.Count; j++)
            {
                if(leftAlternatives[j] != rightAlternatives[j])
                {
                    return false;
                }
            }
        }

        return true;
    }
}
