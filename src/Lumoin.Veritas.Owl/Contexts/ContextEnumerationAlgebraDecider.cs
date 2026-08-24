using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>How the certifying face ended on a module it measured.</summary>
internal enum EnumerationAlgebraVerdict
{
    /// <summary>The face stayed silent: a window bound was exceeded, or the whole-axiom-set gate rejected the module; ordinary saturation owns it.</summary>
    Silent = 0,

    /// <summary>The module is consistent — a witnessed partition-and-assignment model exists — and the exact subsumption set was read off over blocks plus the generic element.</summary>
    Consistent = 1,

    /// <summary>The module is inconsistent: every equality partition respecting the told constraints leaves some block unsatisfiable.</summary>
    Inconsistent = 2,
}

/// <summary>
/// The certifying face's outcome: the verdict, the exact subsumption set on a
/// consistent verdict, and the window measurement the census carries.
/// </summary>
/// <param name="Verdict">How the face ended.</param>
/// <param name="Subsumptions">The exact module-local subsumption set on a <see cref="EnumerationAlgebraVerdict.Consistent"/> verdict; empty otherwise.</param>
/// <param name="MemberUniverse">The deduplicated named-individual member universe measured over the whole module.</param>
/// <param name="MemberSilences">One when the member universe exceeded <see cref="ContextEnumerationAlgebraDecider.MemberUniverseBound"/> and no pair-composition verdict replaced that silence; zero otherwise.</param>
/// <param name="ClassSilences">One when the named-class count exceeded <see cref="ContextEnumerationAlgebraDecider.SignatureClassBound"/> inside the member window; zero otherwise.</param>
/// <param name="PairCount">The pair count <c>k</c> of the anchor-and-pair composition read past the member window, landed before any boundary comparison; zero when the composition did not resolve and on every module inside the window.</param>
/// <param name="PairVectorCount">The assignment vectors the pair sweep evaluated: the passing vector's index plus one on a certification stopped at its witness, the whole <c>2^k</c> space on a refutation and on an exhaustive read-off, and zero on every silent, dark, and measurement-only pass.</param>
/// <param name="PairSilences">One when the pair count exceeded <see cref="ContextEnumerationAlgebraDecider.PairAssignmentBound"/>; zero otherwise.</param>
internal readonly record struct EnumerationAlgebraOutcome(
    EnumerationAlgebraVerdict Verdict,
    IReadOnlyList<(NamedNode SubClass, NamedNode SuperClass)> Subsumptions,
    int MemberUniverse,
    int MemberSilences,
    int ClassSilences,
    int PairCount,
    int PairVectorCount,
    int PairSilences)
{
    /// <summary>The silent outcome carrying only the member-universe measurement, with the pair-composition fields unread.</summary>
    /// <param name="memberUniverse">The measured member universe.</param>
    /// <param name="memberSilences">The member-window silences.</param>
    /// <param name="classSilences">The class-window silences.</param>
    /// <returns>The silent outcome.</returns>
    public static EnumerationAlgebraOutcome SilentWith(int memberUniverse, int memberSilences, int classSilences)
    {
        return new EnumerationAlgebraOutcome(EnumerationAlgebraVerdict.Silent, [], memberUniverse, memberSilences, classSilences, PairCount: 0, PairVectorCount: 0, PairSilences: 0);
    }

    /// <summary>The silent outcome of the past-window pair branch: the member-window silence stands and the structural pair reading rides it, so a jurisdiction silence is still visible as a measured composition.</summary>
    /// <param name="memberUniverse">The measured member universe.</param>
    /// <param name="pairCount">The measured pair count; zero when the composition did not resolve.</param>
    /// <param name="pairSilences">The pair-window silences.</param>
    /// <returns>The silent outcome.</returns>
    public static EnumerationAlgebraOutcome PairSilentWith(int memberUniverse, int pairCount, int pairSilences)
    {
        return new EnumerationAlgebraOutcome(EnumerationAlgebraVerdict.Silent, [], memberUniverse, MemberSilences: 1, ClassSilences: 0, pairCount, PairVectorCount: 0, pairSilences);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's certifying face (face two) and its
/// pair-composition faces (faces seven and eight) over the
/// proven signature Σ_E: named classes, <c>owl:Thing</c>, <c>owl:Nothing</c>,
/// one-ofs of named individuals, and complement, union, and intersection,
/// under <c>SubClassOf</c>, <c>EquivalentClasses</c>, <c>DisjointClasses</c>,
/// <c>ClassAssertion</c>, told <c>SameIndividual</c>, and told
/// <c>DifferentIndividuals</c>. The jurisdiction gate is the positive
/// closed-world admission over the module's ENTIRE axiom set
/// (<see cref="ContextHabitatRecognizer.IsEnumerationAlgebraModule"/>) —
/// admit exactly the Σ_E kinds, reject otherwise, never a blacklist.
/// Consistency is a block-structure search: equality partitions of the
/// deduplicated member universe respecting the told same and different
/// constraints, with per-block class assignments — a witnessed candidate is
/// a genuine model, and blocks constrain independently once the partition is
/// fixed. The exact set is read off over blocks PLUS AT MOST ONE GENERIC
/// ELEMENT: the GenericSat sweep — every class assignment with every one-of
/// atom pinned false, checked against the class axioms — refutes any
/// candidate subsumption an anonymous witness can refute; the domain bound
/// <c>n + 1</c> is tight, and the read-off without the generic element is
/// wrong by construction. Past the member-universe window a second sweep
/// tier answers the ANCHOR-AND-PAIR COMPOSITION: one named class equated to
/// a two-member one-of whose members are told distinct, and to one further
/// two-member one-of per remaining pair, pins every model's named universe
/// onto the anchor's two elements — each pair biject onto them, so exactly
/// two local resolutions exist per pair — and the module is decided by a
/// bounded walk over the <c>2^k</c> assignment vectors of a synthetic
/// two-block quotient, each vector checked by the very same block machinery.
/// A passing vector is a witnessed model; an exhausted space refutes.
/// Every bound is a named window constant; outside any bound the face is
/// silent.
/// </summary>
internal static class ContextEnumerationAlgebraDecider
{
    /// <summary>
    /// The member-universe ceiling: the partition search is exact up to this
    /// many deduplicated named individuals and SILENT above it. Derivation
    /// (algorithmic, with the cost formula the battery pins): the sweep costs
    /// at most Bell(8) = 4,140 partitions × 8 blocks ×
    /// 2^<see cref="SignatureClassBound"/> = 256 assignments — under nine
    /// million bounded axiom evaluations at both ceilings together.
    /// </summary>
    public const int MemberUniverseBound = 8;

    /// <summary>
    /// The signature-class ceiling: the assignment sweep is exact up to this
    /// many named classes and SILENT above it. Derivation (algorithmic): the
    /// per-block sweep enumerates 2^8 = 256 assignments, and the ordered
    /// candidate-pair set at the bound is 8 × 8 = 64 pairs — exactly one
    /// 64-bit refutation mask word.
    /// </summary>
    public const int SignatureClassBound = 8;

    /// <summary>
    /// The pair-composition ceiling: the anchor-and-pair vector sweep is exact
    /// up to this many pairs and SILENT above it. Derivation (engineering, with
    /// the corpus clearance the battery pins): 2^16 = 65,536 vectors, each one
    /// pooled two-block valuation of the member universe plus a bounded
    /// evaluation of every admitted axiom at both blocks, stays
    /// microseconds-cheap and allocation-free, and the value matches the
    /// counting faces' shared sixteen ceiling so every counting-family
    /// pre-engine face carries one boundary discipline; the repairing face
    /// carries its own wider windows sized by its habitat. The corpus maximum
    /// is nine pairs — near two-fold margin in pairs, one-hundred-twenty-eight-fold
    /// in vectors.
    /// </summary>
    public const int PairAssignmentBound = 16;

    /// <summary>The synthetic quotient's block count: the anchor pins exactly two elements, so every pair-composition vector partitions the member universe into these blocks.</summary>
    private const int PairBlockCount = 2;

    /// <summary>The pair index a member of the anchor pair carries: the anchor's side is fixed, so no vector bit reads it.</summary>
    private const int AnchorPairIndex = -1;

    /// <summary>The pair index a member no anchor-or-pair slot covers carries — the stray-member sentinel the composition rejects on.</summary>
    private const int UncoveredPairIndex = -2;

    /// <summary>Measures the Shape E census window without running any search: the member universe, the two window-exceeded silences the bounds would charge, and — past the member window — the structural anchor-and-pair reading with its own window silence. Computed identically dark and lit, so the census ships unconditionally; no vector is ever evaluated on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement; all-zero when the whole-axiom-set gate rejects the module.</returns>
    public static EnumerationAlgebraOutcome Measure(ReasoningModule module)
    {
        if(!ContextHabitatRecognizer.IsEnumerationAlgebraModule(module, out _))
        {
            return EnumerationAlgebraOutcome.SilentWith(0, 0, 0);
        }

        Signature signature = CollectSignature(module);
        int members = signature.Individuals.Count;

        return members > MemberUniverseBound
            ? PairComposition(module, signature, members, sweepVectors: false, includeSubsumptions: false)
            : EnumerationAlgebraOutcome.SilentWith(members, 0, signature.Classes.Count > SignatureClassBound ? 1 : 0);
    }

    /// <summary>
    /// Runs the certifying face: the whole-axiom-set gate, the window checks,
    /// the block-structure consistency search, and — on a consistent module —
    /// the exact-set read-off over blocks plus the GenericSat generic element.
    /// Past the member-universe window the pair-composition tier answers
    /// instead, deciding the anchor-and-pair modules the block search cannot
    /// reach and staying silent — with the measurement already landed — on
    /// every other past-window module.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="includeSubsumptions">Whether to read off the exact subsumption set; consistency-only callers skip the pair collection but not the search.</param>
    /// <returns>The outcome: a certified verdict with its exact set, or silence.</returns>
    public static EnumerationAlgebraOutcome Run(ReasoningModule module, bool includeSubsumptions)
    {
        if(!ContextHabitatRecognizer.IsEnumerationAlgebraModule(module, out _))
        {
            return EnumerationAlgebraOutcome.SilentWith(0, 0, 0);
        }

        Signature signature = CollectSignature(module);
        int members = signature.Individuals.Count;
        if(members > MemberUniverseBound)
        {
            return PairComposition(module, signature, members, sweepVectors: true, includeSubsumptions);
        }

        if(signature.Classes.Count > SignatureClassBound)
        {
            return EnumerationAlgebraOutcome.SilentWith(members, 0, 1);
        }

        Constraints constraints = CollectConstraints(module, signature);
        bool consistent;
        ulong refuted;
        if(members == 0)
        {
            consistent = SweepGenericAssignments(signature, constraints, out refuted);
        }
        else
        {
            consistent = SweepPartitions(signature, constraints, out refuted);
            if(consistent)
            {
                SweepGenericAssignments(signature, constraints, out ulong genericRefuted);
                refuted |= genericRefuted;
            }
        }

        return consistent
            ? new EnumerationAlgebraOutcome(EnumerationAlgebraVerdict.Consistent, ReadOffSubsumptions(signature, refuted, includeSubsumptions), members, 0, 0, PairCount: 0, PairVectorCount: 0, PairSilences: 0)
            : new EnumerationAlgebraOutcome(EnumerationAlgebraVerdict.Inconsistent, [], members, 0, 0, PairCount: 0, PairVectorCount: 0, PairSilences: 0);
    }

    /// <summary>
    /// The pair-composition tier over a module past the member-universe
    /// window, shared by the measurement and the deciding pass: the anchor and
    /// pair structure is read first, so its count is on the record before any
    /// boundary comparison; the pair window, the jurisdictional class cap, and
    /// the definitional-pin resolution then gate the sweep. Every jurisdiction
    /// failure is a named silence carrying the reading, never a verdict over an
    /// unswept structure.
    /// </summary>
    /// <param name="module">The gate-admitted module.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="members">The member universe, already past the member-universe bound.</param>
    /// <param name="sweepVectors">Whether the vector sweep runs; the measurement pass stops at the structural reading and its window comparison.</param>
    /// <param name="includeSubsumptions">Whether to read off the exact subsumption set.</param>
    /// <returns>The outcome: a pair-composition verdict with its exact set, or silence carrying the reading.</returns>
    private static EnumerationAlgebraOutcome PairComposition(ReasoningModule module, Signature signature, int members, bool sweepVectors, bool includeSubsumptions)
    {
        Constraints constraints = CollectConstraints(module, signature);
        using VeritasMemoryPool<int> pool = new();
        using IMemoryOwner<int> pairIndexBuffer = pool.Rent(members);
        using IMemoryOwner<int> sideBuffer = pool.Rent(members);
        using IMemoryOwner<int> occurrenceBuffer = pool.Rent(members);
        Span<int> pairIndexOfMember = pairIndexBuffer.Memory.Span;
        Span<int> sideOfMember = sideBuffer.Memory.Span;
        if(!TryCollectPairComposition(module, signature, constraints, pairIndexOfMember, sideOfMember, occurrenceBuffer.Memory.Span, out int pairCount))
        {
            return EnumerationAlgebraOutcome.PairSilentWith(members, 0, 0);
        }

        if(pairCount > PairAssignmentBound)
        {
            return EnumerationAlgebraOutcome.PairSilentWith(members, pairCount, 1);
        }

        //The measurement pass stops here: the structural reading and the pair
        //window are the whole census contribution, and no vector is walked.
        if(!sweepVectors)
        {
            return EnumerationAlgebraOutcome.PairSilentWith(members, pairCount, 0);
        }

        //The class cap is jurisdictional on this tier, not merely a cost knob:
        //the refutation-mask bit and the generic sweep's assignment space both
        //wrap silently past the bound, so an over-wide signature must silence
        //before any mask word is touched.
        if(signature.Classes.Count > SignatureClassBound)
        {
            return EnumerationAlgebraOutcome.PairSilentWith(members, pairCount, 0);
        }

        using IMemoryOwner<int> resolvedBuffer = pool.Rent(signature.Classes.Count);
        if(!ClassesArePinned(module, signature, resolvedBuffer.Memory.Span))
        {
            return EnumerationAlgebraOutcome.PairSilentWith(members, pairCount, 0);
        }

        //The verdict needs one witness, but the EXACT set needs the refutations
        //every model contributes: a walk stopped at its first witness would
        //leave a candidate pair some later model refutes standing, and the set
        //would claim an entailment that does not hold. So the walk runs whole
        //exactly when a read-off can move — a caller asking for the set over a
        //signature carrying at least one ordered candidate pair.
        using IMemoryOwner<int> blockBuffer = pool.Rent(members);
        bool consistent = SweepPairVectors(
            signature,
            constraints,
            pairIndexOfMember,
            sideOfMember,
            pairCount,
            blockBuffer.Memory.Span,
            exhaustive: includeSubsumptions && signature.Classes.Count > 1,
            out ulong refuted,
            out int evaluatedVectors);
        if(!consistent)
        {
            return new EnumerationAlgebraOutcome(EnumerationAlgebraVerdict.Inconsistent, [], members, 0, 0, pairCount, evaluatedVectors, 0);
        }

        //The generic element's own satisfiability answers nothing here — a
        //quotient-consistent module may fail at every generic assignment — so
        //the sweep's boolean is discarded and only its refutations join the
        //read-off, the discipline the block tier's own consistent branch keeps.
        SweepGenericAssignments(signature, constraints, out ulong genericRefuted);
        refuted |= genericRefuted;

        return new EnumerationAlgebraOutcome(EnumerationAlgebraVerdict.Consistent, ReadOffSubsumptions(signature, refuted, includeSubsumptions), members, 0, 0, pairCount, evaluatedVectors, 0);
    }

    /// <summary>Reads the exact subsumption set off the accumulated refutation mask: every ordered candidate pair of distinct named classes no examined element refuted.</summary>
    /// <param name="signature">The signature.</param>
    /// <param name="refuted">The accumulated refutation mask.</param>
    /// <param name="includeSubsumptions">Whether the caller asked for the set; a consistency-only caller reads off nothing.</param>
    /// <returns>The subsumption set, empty when the caller asked for none.</returns>
    private static List<(NamedNode SubClass, NamedNode SuperClass)> ReadOffSubsumptions(Signature signature, ulong refuted, bool includeSubsumptions)
    {
        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions = [];
        if(!includeSubsumptions)
        {
            return subsumptions;
        }

        for(int i = 0; i < signature.Classes.Count; i++)
        {
            for(int j = 0; j < signature.Classes.Count; j++)
            {
                if(i != j && (refuted & PairBit(i, j)) == 0)
                {
                    subsumptions.Add((new NamedNode(signature.Classes[i]), new NamedNode(signature.Classes[j])));
                }
            }
        }

        return subsumptions;
    }

    /// <summary>The module's Σ_E signature: the named classes in first-seen order with their bit indices, and the deduplicated named individuals with their universe indices.</summary>
    private sealed class Signature
    {
        /// <summary>The named class IRIs, by bit index; <c>owl:Thing</c> and <c>owl:Nothing</c> are constants, never bits.</summary>
        public List<Utf8String> Classes { get; } = [];

        /// <summary>The class bit index by IRI.</summary>
        public Dictionary<Utf8String, int> ClassBits { get; } = [];

        /// <summary>The named individual IRIs, by universe index.</summary>
        public List<Utf8String> Individuals { get; } = [];

        /// <summary>The universe index by individual IRI.</summary>
        public Dictionary<Utf8String, int> IndividualIds { get; } = [];
    }

    /// <summary>The element-level constraint sets one pass over the admitted axioms collects.</summary>
    private sealed class Constraints
    {
        /// <summary>The class-axiom constraints every domain element must satisfy: subsumption implications, equivalence biconditionals as two implications, and pairwise disjointness negations — each as (kind, left expression, right expression).</summary>
        public List<(OwlClassExpression Left, OwlClassExpression Right, bool Biconditional)> Implications { get; } = [];

        /// <summary>The pairwise disjointness constraints as expression pairs — at most one of each pair true at any element.</summary>
        public List<(OwlClassExpression First, OwlClassExpression Second)> Disjointness { get; } = [];

        /// <summary>The told class assertions as (asserted expression, universe index).</summary>
        public List<(OwlClassExpression Class, int Individual)> Assertions { get; } = [];

        /// <summary>The told same-individual pairs, by universe index.</summary>
        public List<(int First, int Second)> SamePairs { get; } = [];

        /// <summary>The told different-individual pairs, by universe index.</summary>
        public List<(int First, int Second)> DifferentPairs { get; } = [];
    }

    /// <summary>The refutation-mask bit for the ordered candidate pair (<paramref name="subClass"/> ⊑ <paramref name="superClass"/>).</summary>
    /// <param name="subClass">The subclass bit index.</param>
    /// <param name="superClass">The superclass bit index.</param>
    /// <returns>The single-bit mask.</returns>
    private static ulong PairBit(int subClass, int superClass)
    {
        return 1UL << ((subClass * SignatureClassBound) + superClass);
    }

    /// <summary>Collects the signature in one pass over the admitted axioms: every named class on an expression surface and every named individual in a one-of, assertion, or told (in)equality, deduplicated in first-seen order.</summary>
    /// <param name="module">The gate-admitted module.</param>
    /// <returns>The signature.</returns>
    private static Signature CollectSignature(ReasoningModule module)
    {
        Signature signature = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlSubClassOfAxiom subClass):
                {
                    CollectExpression(signature, subClass.SubClass);
                    CollectExpression(signature, subClass.SuperClass);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    CollectExpression(signature, equivalent.First);
                    CollectExpression(signature, equivalent.Second);
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    for(int i = 0; i < disjoint.Operands.Count; i++)
                    {
                        CollectExpression(signature, disjoint.Operands[i]);
                    }

                    break;
                }
                case(OwlClassAssertionAxiom { Individual: NamedNode individual } assertion):
                {
                    CollectExpression(signature, assertion.Class);
                    InternIndividual(signature, individual.Iri);
                    break;
                }
                case(OwlSameIndividualAxiom { First: NamedNode first, Second: NamedNode second }):
                {
                    InternIndividual(signature, first.Iri);
                    InternIndividual(signature, second.Iri);
                    break;
                }
                case(OwlDifferentIndividualsAxiom different):
                {
                    for(int i = 0; i < different.Individuals.Count; i++)
                    {
                        if(different.Individuals[i] is NamedNode named)
                        {
                            InternIndividual(signature, named.Iri);
                        }
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return signature;
    }

    /// <summary>Interns one individual, assigning a universe index on first sight.</summary>
    /// <param name="signature">The signature.</param>
    /// <param name="individual">The individual's IRI.</param>
    private static void InternIndividual(Signature signature, Utf8String individual)
    {
        if(!signature.IndividualIds.ContainsKey(individual))
        {
            signature.IndividualIds.Add(individual, signature.Individuals.Count);
            signature.Individuals.Add(individual);
        }
    }

    /// <summary>Walks one expression with an explicit stack, interning named classes and one-of members — the collector descends into complement subtrees so complement-wrapped members are first-class.</summary>
    /// <param name="signature">The signature.</param>
    /// <param name="root">The expression.</param>
    private static void CollectExpression(Signature signature, OwlClassExpression root)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlClassReference reference):
                {
                    Utf8String iri = reference.Class.Iri;
                    if(!iri.Equals(OwlVocabulary.Thing) && !iri.Equals(OwlVocabulary.Nothing) && !signature.ClassBits.ContainsKey(iri))
                    {
                        signature.ClassBits.Add(iri, signature.Classes.Count);
                        signature.Classes.Add(iri);
                    }

                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    for(int i = 0; i < oneOf.Individuals.Count; i++)
                    {
                        InternIndividual(signature, ((NamedNode)oneOf.Individuals[i]).Iri);
                    }

                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Push(complement.Operand);
                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int i = 0; i < union.Operands.Count; i++)
                    {
                        work.Push(union.Operands[i]);
                    }

                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int i = 0; i < intersection.Operands.Count; i++)
                    {
                        work.Push(intersection.Operands[i]);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    /// <summary>Collects the element-level constraints in one pass over the admitted axioms.</summary>
    /// <param name="module">The gate-admitted module.</param>
    /// <param name="signature">The collected signature.</param>
    /// <returns>The constraints.</returns>
    private static Constraints CollectConstraints(ReasoningModule module, Signature signature)
    {
        Constraints constraints = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlSubClassOfAxiom subClass):
                {
                    constraints.Implications.Add((subClass.SubClass, subClass.SuperClass, Biconditional: false));
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    constraints.Implications.Add((equivalent.First, equivalent.Second, Biconditional: true));
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    for(int i = 0; i < disjoint.Operands.Count; i++)
                    {
                        for(int j = i + 1; j < disjoint.Operands.Count; j++)
                        {
                            constraints.Disjointness.Add((disjoint.Operands[i], disjoint.Operands[j]));
                        }
                    }

                    break;
                }
                case(OwlClassAssertionAxiom { Individual: NamedNode individual } assertion):
                {
                    constraints.Assertions.Add((assertion.Class, signature.IndividualIds[individual.Iri]));
                    break;
                }
                case(OwlSameIndividualAxiom { First: NamedNode first, Second: NamedNode second }):
                {
                    constraints.SamePairs.Add((signature.IndividualIds[first.Iri], signature.IndividualIds[second.Iri]));
                    break;
                }
                case(OwlDifferentIndividualsAxiom different):
                {
                    for(int i = 0; i < different.Individuals.Count; i++)
                    {
                        for(int j = i + 1; j < different.Individuals.Count; j++)
                        {
                            if(different.Individuals[i] is NamedNode firstNamed && different.Individuals[j] is NamedNode secondNamed)
                            {
                                constraints.DifferentPairs.Add((signature.IndividualIds[firstNamed.Iri], signature.IndividualIds[secondNamed.Iri]));
                            }
                        }
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return constraints;
    }

    /// <summary>
    /// The block-structure sweep: every restricted-growth partition of the
    /// member universe respecting the told same and different constraints,
    /// each block swept over every class assignment. A partition is a model
    /// candidate exactly when every block has a satisfying assignment —
    /// blocks constrain independently once the partition is fixed — and each
    /// satisfying (block, assignment) pair of a candidate contributes its
    /// refuted candidate subsumptions to the mask.
    /// </summary>
    /// <param name="signature">The signature.</param>
    /// <param name="constraints">The constraints.</param>
    /// <param name="refuted">The accumulated refutation mask over candidate pairs.</param>
    /// <returns><see langword="true"/> when some partition is a model candidate.</returns>
    private static bool SweepPartitions(Signature signature, Constraints constraints, out ulong refuted)
    {
        refuted = 0;
        bool consistent = false;
        int members = signature.Individuals.Count;
        int assignmentCount = 1 << signature.Classes.Count;
        using VeritasMemoryPool<int> pool = new();
        using PartitionGrowthEnumerator partitions = PartitionGrowthEnumerator.Create(pool, members);
        Span<int> blockOfMember = new int[members];
        while(partitions.MoveNext())
        {
            ReadOnlySpan<int> blocks = partitions.Current;
            if(!RespectsToldConstraints(blocks, constraints))
            {
                continue;
            }

            blocks.CopyTo(blockOfMember);
            bool allBlocksSatisfiable = true;
            ulong partitionRefutations = 0;
            for(int block = 0; block < partitions.BlockCount; block++)
            {
                bool satisfiable = false;
                for(int assignment = 0; assignment < assignmentCount; assignment++)
                {
                    if(!SatisfiesAtBlock(signature, constraints, blockOfMember, block, assignment))
                    {
                        continue;
                    }

                    satisfiable = true;
                    partitionRefutations |= RefutationsOfAssignment(signature, assignment);
                }

                if(!satisfiable)
                {
                    allBlocksSatisfiable = false;
                    break;
                }
            }

            if(allBlocksSatisfiable)
            {
                consistent = true;
                refuted |= partitionRefutations;
            }
        }

        return consistent;
    }

    /// <summary>
    /// Reads the anchor-and-pair composition off the module in one structural
    /// pass: the single told-distinct anchor pair, then every further
    /// two-member one-of equivalence on the SAME named class as a pair slot,
    /// with a per-member occurrence count enforcing the exact partition. A
    /// one-of on the anchor class of any other size is never selected from or
    /// truncated — its members simply raise their occurrence counts, and the
    /// closing check then rejects the module rather than dropping a told
    /// constraint. The composition resolves exactly when every member is
    /// covered by the anchor or by one pair, and covered exactly once.
    /// </summary>
    /// <param name="module">The gate-admitted module.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="constraints">The collected constraints, source of the told distinctness the anchor needs.</param>
    /// <param name="pairIndexOfMemberToFill">The per-member pair index the reading fills: the pair's index, the anchor sentinel, or the uncovered sentinel.</param>
    /// <param name="sideOfMemberToFill">The per-member side the reading fills: zero for the anchor's first member and each pair's first member, one for their partners.</param>
    /// <param name="occurrences">The per-member occurrence scratch the closing check reads.</param>
    /// <param name="pairCount">The resolved pair count; zero when the composition did not resolve.</param>
    /// <returns><see langword="true"/> when the composition resolved.</returns>
    private static bool TryCollectPairComposition(
        ReasoningModule module,
        Signature signature,
        Constraints constraints,
        Span<int> pairIndexOfMemberToFill,
        Span<int> sideOfMemberToFill,
        Span<int> occurrences,
        out int pairCount)
    {
        pairCount = 0;
        pairIndexOfMemberToFill.Fill(UncoveredPairIndex);
        sideOfMemberToFill.Clear();
        occurrences.Clear();
        if(!TrySelectAnchor(module, constraints, signature, out Utf8String anchorClass, out int anchorFirst, out int anchorSecond))
        {
            return false;
        }

        pairIndexOfMemberToFill[anchorFirst] = AnchorPairIndex;
        sideOfMemberToFill[anchorFirst] = 0;
        occurrences[anchorFirst] = 1;
        pairIndexOfMemberToFill[anchorSecond] = AnchorPairIndex;
        sideOfMemberToFill[anchorSecond] = 1;
        occurrences[anchorSecond] = 1;

        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!TryReadOneOfEquivalence(axiom, out Utf8String definedClass, out OwlObjectOneOf? oneOf)
                || !definedClass.Equals(anchorClass)
                || IsAnchorList(oneOf, signature, anchorFirst, anchorSecond))
            {
                continue;
            }

            if(oneOf.Individuals.Count == 2)
            {
                int first = signature.IndividualIds[((NamedNode)oneOf.Individuals[0]).Iri];
                int second = signature.IndividualIds[((NamedNode)oneOf.Individuals[1]).Iri];
                if(first != second)
                {
                    pairIndexOfMemberToFill[first] = pairCount;
                    sideOfMemberToFill[first] = 0;
                    pairIndexOfMemberToFill[second] = pairCount;
                    sideOfMemberToFill[second] = 1;
                    pairCount++;
                }

                occurrences[first]++;
                occurrences[second]++;
                continue;
            }

            for(int i = 0; i < oneOf.Individuals.Count; i++)
            {
                occurrences[signature.IndividualIds[((NamedNode)oneOf.Individuals[i]).Iri]]++;
            }
        }

        for(int member = 0; member < signature.Individuals.Count; member++)
        {
            if(occurrences[member] != 1 || pairIndexOfMemberToFill[member] == UncoveredPairIndex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Selects the module's single anchor: a named class equated to a two-member one-of whose two members are told distinct. Candidates are deduplicated by their unordered member set, so a syntactic duplicate of one anchor axiom is one candidate; zero candidates or two distinct ones leave the composition unresolved.</summary>
    /// <param name="module">The gate-admitted module.</param>
    /// <param name="constraints">The collected constraints.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="anchorClass">The anchor's named class; the default when no single anchor exists.</param>
    /// <param name="anchorFirst">The anchor's first member's universe index; minus one when no single anchor exists.</param>
    /// <param name="anchorSecond">The anchor's second member's universe index; minus one when no single anchor exists.</param>
    /// <returns><see langword="true"/> when exactly one anchor exists.</returns>
    private static bool TrySelectAnchor(ReasoningModule module, Constraints constraints, Signature signature, out Utf8String anchorClass, out int anchorFirst, out int anchorSecond)
    {
        anchorClass = default;
        anchorFirst = -1;
        anchorSecond = -1;
        int candidates = 0;
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(!TryReadOneOfEquivalence(axiom, out Utf8String definedClass, out OwlObjectOneOf? oneOf) || oneOf.Individuals.Count != 2)
            {
                continue;
            }

            int first = signature.IndividualIds[((NamedNode)oneOf.Individuals[0]).Iri];
            int second = signature.IndividualIds[((NamedNode)oneOf.Individuals[1]).Iri];
            if(first == second || !IsToldDistinct(constraints, first, second))
            {
                continue;
            }

            if(candidates > 0 && ((first == anchorFirst && second == anchorSecond) || (first == anchorSecond && second == anchorFirst)))
            {
                continue;
            }

            candidates++;
            if(candidates > 1)
            {
                return false;
            }

            anchorClass = definedClass;
            anchorFirst = first;
            anchorSecond = second;
        }

        return candidates == 1;
    }

    /// <summary>Whether a told <c>DifferentIndividuals</c> axiom separates the two universe members, in either told order.</summary>
    /// <param name="constraints">The collected constraints.</param>
    /// <param name="first">The first member's universe index.</param>
    /// <param name="second">The second member's universe index.</param>
    /// <returns><see langword="true"/> when the pair is told distinct.</returns>
    private static bool IsToldDistinct(Constraints constraints, int first, int second)
    {
        foreach((int told, int other) in constraints.DifferentPairs)
        {
            if((told == first && other == second) || (told == second && other == first))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a one-of list is the anchor's own list — the two anchor members in either told order, the shape a syntactic duplicate of the anchor axiom repeats.</summary>
    /// <param name="oneOf">The one-of list.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="anchorFirst">The anchor's first member's universe index.</param>
    /// <param name="anchorSecond">The anchor's second member's universe index.</param>
    /// <returns><see langword="true"/> when the list is the anchor's.</returns>
    private static bool IsAnchorList(OwlObjectOneOf oneOf, Signature signature, int anchorFirst, int anchorSecond)
    {
        if(oneOf.Individuals.Count != 2)
        {
            return false;
        }

        int first = signature.IndividualIds[((NamedNode)oneOf.Individuals[0]).Iri];
        int second = signature.IndividualIds[((NamedNode)oneOf.Individuals[1]).Iri];

        return (first == anchorFirst && second == anchorSecond) || (first == anchorSecond && second == anchorFirst);
    }

    /// <summary>Reads one told equivalence between a named non-constant class and a one-of of named individuals, in either told order; every other axiom and every other equivalence shape rejects.</summary>
    /// <param name="axiom">The axiom to read.</param>
    /// <param name="definedClass">The named class's IRI; the default on rejection.</param>
    /// <param name="oneOf">The one-of list; <see langword="null"/> on rejection.</param>
    /// <returns><see langword="true"/> on the one-of equivalence shape.</returns>
    private static bool TryReadOneOfEquivalence(OwlAxiom axiom, out Utf8String definedClass, [NotNullWhen(true)] out OwlObjectOneOf? oneOf)
    {
        definedClass = default;
        oneOf = null;
        if(axiom is OwlEquivalentClassesAxiom { First: OwlClassReference first, Second: OwlObjectOneOf firstList } && IsSignatureClass(first))
        {
            definedClass = first.Class.Iri;
            oneOf = firstList;

            return true;
        }

        if(axiom is OwlEquivalentClassesAxiom { First: OwlObjectOneOf secondList, Second: OwlClassReference second } && IsSignatureClass(second))
        {
            definedClass = second.Class.Iri;
            oneOf = secondList;

            return true;
        }

        return false;
    }

    /// <summary>Whether a class reference names a signature class rather than one of the two constants, which carry no assignment bit.</summary>
    /// <param name="reference">The class reference.</param>
    /// <returns><see langword="true"/> for a signature class.</returns>
    private static bool IsSignatureClass(OwlClassReference reference)
    {
        Utf8String iri = reference.Class.Iri;

        return !iri.Equals(OwlVocabulary.Thing) && !iri.Equals(OwlVocabulary.Nothing);
    }

    /// <summary>
    /// Whether every named class of the signature is definitionally pinned — an
    /// explicit sweep to fixpoint, no recursion. The anchor class is pinned by
    /// its own one-of equivalence; every other class must carry a told
    /// equivalence whose other side mentions only already-pinned classes,
    /// members, and the two constants. A class the sweep never places is
    /// undefined or sits on a definition cycle, and the tier stays silent on it.
    /// </summary>
    /// <param name="module">The gate-admitted module.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="resolvedClasses">The per-class placement scratch the sweep fills.</param>
    /// <returns><see langword="true"/> when every named class was placed.</returns>
    private static bool ClassesArePinned(ReasoningModule module, Signature signature, Span<int> resolvedClasses)
    {
        resolvedClasses.Clear();
        int placed = 0;
        bool progressed = true;
        while(progressed && placed < signature.Classes.Count)
        {
            progressed = false;
            foreach(OwlAxiom axiom in module.Axioms)
            {
                if(axiom is not OwlEquivalentClassesAxiom equivalence)
                {
                    continue;
                }

                if(TryPinClass(equivalence.First, equivalence.Second, signature, resolvedClasses))
                {
                    placed++;
                    progressed = true;
                }

                if(TryPinClass(equivalence.Second, equivalence.First, signature, resolvedClasses))
                {
                    placed++;
                    progressed = true;
                }
            }
        }

        return placed == signature.Classes.Count;
    }

    /// <summary>Pins one named class from one side of a told equivalence when the other side is closed over already-pinned classes; an unplaceable side, a constant, and an already-placed class all decline.</summary>
    /// <param name="definedSide">The candidate defined side.</param>
    /// <param name="body">The candidate defining side.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="resolvedClasses">The per-class placement flags.</param>
    /// <returns><see langword="true"/> when the class was pinned by this call.</returns>
    private static bool TryPinClass(OwlClassExpression definedSide, OwlClassExpression body, Signature signature, Span<int> resolvedClasses)
    {
        if(definedSide is not OwlClassReference reference
            || !signature.ClassBits.TryGetValue(reference.Class.Iri, out int bit)
            || resolvedClasses[bit] != 0
            || !MentionsOnlyPinnedClasses(body, signature, resolvedClasses))
        {
            return false;
        }

        resolvedClasses[bit] = 1;

        return true;
    }

    /// <summary>Whether an expression's every named-class reference is already pinned — an explicit-stack walk over the admitted grammar; one-of lists are closed over the member universe and need no pin.</summary>
    /// <param name="root">The expression.</param>
    /// <param name="signature">The collected signature.</param>
    /// <param name="resolvedClasses">The per-class placement flags.</param>
    /// <returns><see langword="true"/> when the expression is closed over pinned classes.</returns>
    private static bool MentionsOnlyPinnedClasses(OwlClassExpression root, Signature signature, ReadOnlySpan<int> resolvedClasses)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlClassReference reference):
                {
                    if(signature.ClassBits.TryGetValue(reference.Class.Iri, out int bit) && resolvedClasses[bit] == 0)
                    {
                        return false;
                    }

                    break;
                }
                case(OwlObjectOneOf):
                {
                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Push(complement.Operand);
                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int i = 0; i < union.Operands.Count; i++)
                    {
                        work.Push(union.Operands[i]);
                    }

                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int i = 0; i < intersection.Operands.Count; i++)
                    {
                        work.Push(intersection.Operands[i]);
                    }

                    break;
                }
                default:
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The pair-composition sweep: every assignment vector in index order, each
    /// laying the synthetic two-block quotient down over the member universe —
    /// the anchor's members to their fixed blocks, each pair's members to the
    /// blocks its vector bit selects — and each checked by the very same told
    /// constraint and per-block assignment machinery the block tier runs. A
    /// vector passes exactly when the quotient alone satisfies the module, so
    /// the generic element plays no part in the verdict. The walk stops at its
    /// first witness unless the caller's read-off needs the refutations every
    /// model contributes.
    /// </summary>
    /// <param name="signature">The signature.</param>
    /// <param name="constraints">The constraints.</param>
    /// <param name="pairIndexOfMember">The per-member pair index.</param>
    /// <param name="sideOfMember">The per-member side.</param>
    /// <param name="pairCount">The pair count, inside the pair window.</param>
    /// <param name="blockOfMember">The two-block quotient buffer one vector at a time fills.</param>
    /// <param name="exhaustive">Whether every vector is walked so the refutation mask covers every model; a consistency-only or single-class read-off stops at the first witness.</param>
    /// <param name="refuted">The accumulated refutation mask over candidate pairs.</param>
    /// <param name="evaluatedVectors">The vectors evaluated: the witness's index plus one on an early stop, the whole space otherwise.</param>
    /// <returns><see langword="true"/> when some vector passed.</returns>
    private static bool SweepPairVectors(
        Signature signature,
        Constraints constraints,
        ReadOnlySpan<int> pairIndexOfMember,
        ReadOnlySpan<int> sideOfMember,
        int pairCount,
        Span<int> blockOfMember,
        bool exhaustive,
        out ulong refuted,
        out int evaluatedVectors)
    {
        refuted = 0;
        evaluatedVectors = 0;
        bool consistent = false;
        int members = signature.Individuals.Count;
        int assignmentCount = 1 << signature.Classes.Count;
        int vectorCount = 1 << pairCount;
        for(int vector = 0; vector < vectorCount; vector++)
        {
            evaluatedVectors = vector + 1;
            for(int member = 0; member < members; member++)
            {
                int pairIndex = pairIndexOfMember[member];
                int bit = pairIndex >= 0 ? (vector >> pairIndex) & 1 : 0;
                blockOfMember[member] = bit ^ sideOfMember[member];
            }

            if(!RespectsToldConstraints(blockOfMember, constraints))
            {
                continue;
            }

            bool allBlocksSatisfiable = true;
            ulong vectorRefutations = 0;
            for(int block = 0; block < PairBlockCount; block++)
            {
                bool satisfiable = false;
                for(int assignment = 0; assignment < assignmentCount; assignment++)
                {
                    if(!SatisfiesAtBlock(signature, constraints, blockOfMember, block, assignment))
                    {
                        continue;
                    }

                    satisfiable = true;
                    vectorRefutations |= RefutationsOfAssignment(signature, assignment);
                }

                if(!satisfiable)
                {
                    allBlocksSatisfiable = false;
                    break;
                }
            }

            if(!allBlocksSatisfiable)
            {
                continue;
            }

            consistent = true;
            refuted |= vectorRefutations;
            if(!exhaustive)
            {
                return true;
            }
        }

        return consistent;
    }

    /// <summary>Whether a partition respects the told constraints: every told same pair shares a block and every told different pair separates.</summary>
    /// <param name="blocks">The partition's growth string.</param>
    /// <param name="constraints">The constraints.</param>
    /// <returns><see langword="true"/> when the partition is admissible.</returns>
    private static bool RespectsToldConstraints(ReadOnlySpan<int> blocks, Constraints constraints)
    {
        foreach((int first, int second) in constraints.SamePairs)
        {
            if(blocks[first] != blocks[second])
            {
                return false;
            }
        }

        foreach((int first, int second) in constraints.DifferentPairs)
        {
            if(blocks[first] == blocks[second])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one block under one class assignment satisfies every element-level constraint: the implications and disjointness at the block, and every told assertion whose individual lands in the block.</summary>
    /// <param name="signature">The signature.</param>
    /// <param name="constraints">The constraints.</param>
    /// <param name="blockOfMember">The partition's growth string.</param>
    /// <param name="block">The block index under test.</param>
    /// <param name="assignment">The class-bit assignment.</param>
    /// <returns><see langword="true"/> when the block satisfies everything.</returns>
    private static bool SatisfiesAtBlock(Signature signature, Constraints constraints, ReadOnlySpan<int> blockOfMember, int block, int assignment)
    {
        foreach((OwlClassExpression left, OwlClassExpression right, bool biconditional) in constraints.Implications)
        {
            bool leftHolds = Evaluate(left, signature, assignment, blockOfMember, block);
            bool rightHolds = Evaluate(right, signature, assignment, blockOfMember, block);
            if(biconditional ? leftHolds != rightHolds : leftHolds && !rightHolds)
            {
                return false;
            }
        }

        foreach((OwlClassExpression first, OwlClassExpression second) in constraints.Disjointness)
        {
            if(Evaluate(first, signature, assignment, blockOfMember, block) && Evaluate(second, signature, assignment, blockOfMember, block))
            {
                return false;
            }
        }

        foreach((OwlClassExpression assertedClass, int individual) in constraints.Assertions)
        {
            if(blockOfMember[individual] == block && !Evaluate(assertedClass, signature, assignment, blockOfMember, block))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The GenericSat sweep — the read-off repair's generic element: every
    /// class assignment with every one-of atom pinned false, checked against
    /// the class axioms only (an anonymous element carries no assertion).
    /// Each satisfying assignment contributes its refutations; a satisfiable
    /// sweep also witnesses the empty-universe module consistent.
    /// </summary>
    /// <param name="signature">The signature.</param>
    /// <param name="constraints">The constraints.</param>
    /// <param name="refuted">The accumulated refutation mask.</param>
    /// <returns><see langword="true"/> when some generic assignment satisfies the class axioms.</returns>
    private static bool SweepGenericAssignments(Signature signature, Constraints constraints, out ulong refuted)
    {
        refuted = 0;
        bool satisfiable = false;
        int assignmentCount = 1 << signature.Classes.Count;
        for(int assignment = 0; assignment < assignmentCount; assignment++)
        {
            bool holds = true;
            foreach((OwlClassExpression left, OwlClassExpression right, bool biconditional) in constraints.Implications)
            {
                bool leftHolds = Evaluate(left, signature, assignment, [], block: -1);
                bool rightHolds = Evaluate(right, signature, assignment, [], block: -1);
                if(biconditional ? leftHolds != rightHolds : leftHolds && !rightHolds)
                {
                    holds = false;
                    break;
                }
            }

            if(holds)
            {
                foreach((OwlClassExpression first, OwlClassExpression second) in constraints.Disjointness)
                {
                    if(Evaluate(first, signature, assignment, [], block: -1) && Evaluate(second, signature, assignment, [], block: -1))
                    {
                        holds = false;
                        break;
                    }
                }
            }

            if(holds)
            {
                satisfiable = true;
                refuted |= RefutationsOfAssignment(signature, assignment);
            }
        }

        return satisfiable;
    }

    /// <summary>The candidate subsumptions one satisfying assignment refutes: every ordered pair whose subclass bit is set and superclass bit is clear.</summary>
    /// <param name="signature">The signature.</param>
    /// <param name="assignment">The satisfying class-bit assignment.</param>
    /// <returns>The refutation mask contribution.</returns>
    private static ulong RefutationsOfAssignment(Signature signature, int assignment)
    {
        ulong mask = 0;
        for(int i = 0; i < signature.Classes.Count; i++)
        {
            if((assignment & (1 << i)) == 0)
            {
                continue;
            }

            for(int j = 0; j < signature.Classes.Count; j++)
            {
                if(i != j && (assignment & (1 << j)) == 0)
                {
                    mask |= PairBit(i, j);
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Evaluates a Σ_E expression at one element with an explicit two-stack
    /// walk — no recursion: named classes read their assignment bit,
    /// <c>owl:Thing</c> and <c>owl:Nothing</c> are constants, a one-of is
    /// true exactly when the element is a block containing one of its
    /// members (and pinned false at the generic element, whose block index
    /// is negative), and complement, union, and intersection fold their
    /// operands.
    /// </summary>
    /// <param name="root">The expression.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="assignment">The class-bit assignment at the element.</param>
    /// <param name="blockOfMember">The partition's growth string; empty at the generic element.</param>
    /// <param name="block">The element's block index; negative for the generic element.</param>
    /// <returns>The truth value.</returns>
    private static bool Evaluate(OwlClassExpression root, Signature signature, int assignment, ReadOnlySpan<int> blockOfMember, int block)
    {
        //Post-order over an explicit frame stack: a frame is pushed unexpanded,
        //re-visited after its children, and folds the result stack's tail.
        Stack<(OwlClassExpression Expression, bool Expanded)> frames = new();
        Stack<bool> results = new();
        frames.Push((root, false));

        while(frames.Count > 0)
        {
            (OwlClassExpression expression, bool expanded) = frames.Pop();
            switch(expression)
            {
                case(OwlClassReference reference):
                {
                    Utf8String iri = reference.Class.Iri;
                    if(iri.Equals(OwlVocabulary.Thing))
                    {
                        results.Push(true);
                    }
                    else if(iri.Equals(OwlVocabulary.Nothing))
                    {
                        results.Push(false);
                    }
                    else
                    {
                        results.Push((assignment & (1 << signature.ClassBits[iri])) != 0);
                    }

                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    bool contains = false;
                    if(block >= 0)
                    {
                        for(int i = 0; i < oneOf.Individuals.Count; i++)
                        {
                            if(blockOfMember[signature.IndividualIds[((NamedNode)oneOf.Individuals[i]).Iri]] == block)
                            {
                                contains = true;
                                break;
                            }
                        }
                    }

                    results.Push(contains);
                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    if(expanded)
                    {
                        results.Push(!results.Pop());
                    }
                    else
                    {
                        frames.Push((expression, true));
                        frames.Push((complement.Operand, false));
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    if(expanded)
                    {
                        bool any = false;
                        for(int i = 0; i < union.Operands.Count; i++)
                        {
                            any |= results.Pop();
                        }

                        results.Push(any);
                    }
                    else
                    {
                        frames.Push((expression, true));
                        for(int i = 0; i < union.Operands.Count; i++)
                        {
                            frames.Push((union.Operands[i], false));
                        }
                    }

                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    if(expanded)
                    {
                        bool all = true;
                        for(int i = 0; i < intersection.Operands.Count; i++)
                        {
                            all &= results.Pop();
                        }

                        results.Push(all);
                    }
                    else
                    {
                        frames.Push((expression, true));
                        for(int i = 0; i < intersection.Operands.Count; i++)
                        {
                            frames.Push((intersection.Operands[i], false));
                        }
                    }

                    break;
                }
                default:
                {
                    results.Push(false);
                    break;
                }
            }
        }

        return results.Pop();
    }
}
