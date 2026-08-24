using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The datatype-consistency verdict of the data restrictions a single node
/// carries.
/// </summary>
internal enum DataConsistencyStatus
{
    /// <summary>Every forced data obligation provably has a value.</summary>
    Consistent,

    /// <summary>A forced data obligation is provably unsatisfiable — a datatype clash that closes the branch.</summary>
    Clash,

    /// <summary>A forced data obligation could not be decided; the obligation neither clashes nor counts as discharged, so the enclosing verdict is fragment-relative.</summary>
    Undecided,
}

/// <summary>
/// The budget gate a caller supplies to bound the sidecar's oracle work: consulted
/// before each satisfiability-checker invocation, and a <see langword="false"/>
/// return stops the decision immediately with a sound
/// <see cref="DataConsistencyStatus.Undecided"/> — the caller's budget latch owns
/// the outcome from there.
/// </summary>
/// <returns><see langword="true"/> when the next oracle invocation may proceed.</returns>
internal delegate bool DataOracleGateDelegate();

/// <summary>
/// Decides whether the concrete-domain demands a node carries — the
/// <see cref="AlcDataSome"/> existentials, the <see cref="AlcDataMinCard"/>
/// counting demands, the <see cref="AlcDataMaxCard"/> counting bounds, and the
/// <see cref="AlcDataAll"/> universals that constrain them — can be jointly
/// satisfied against the module's data-property RBox, by handing each forced
/// range conjunction to the <see cref="DatatypeSatisfiabilityChecker"/>.
/// </summary>
/// <remarks>
/// <para>
/// Data successors are leaves: a universal <c>∀dp.R</c> constrains the value
/// every same-property existential <c>∃dp.R'</c> must take, but neither
/// generates a tableau node of its own. So consistency is a finite battery of
/// satisfiability checks over per-property range conjunctions, needing no
/// blocking. A check that comes back unsatisfiable is a clash; one that comes
/// back unknown leaves the node undecided (sound abstention); only when every
/// check is satisfiable and no RBox constraint clashes is the node
/// datatype-consistent.
/// </para>
/// <para>
/// The <see cref="DataPropertyBox"/> lifts the check from single-property
/// isolation to the property hierarchy: a demand's constraining universals are
/// those of every super-property (plus the asserted ranges the box supplies);
/// a functional property pools every demand on it and its sub-properties into
/// one value; a maximum-cardinality bound pools the same sub-or-self sweep into
/// its max slot; and a disjoint property pair forbids a shared value across the
/// two. The empty box carries none of the hierarchy, functionality, or
/// disjointness, so a call against it reduces to the same-property universal
/// check plus the node's own max slots, with byte-identical verdicts and
/// conflict cores.
/// </para>
/// <para>
/// Both reasoner arms call this over the same translated leaves against the same
/// box: the snapshot tableau on each node of a completed forest, the SAT-backed
/// sibling on the data atoms true in a world's model (learning the reported
/// conflict as a clause). The verdicts agree because the checker and the box are
/// the single oracle.
/// </para>
/// </remarks>
internal static class DataRestrictionConsistency
{
    /// <summary>
    /// The name a module verdict carries on its beyond-fragment remainder when
    /// a completion's concrete-domain obligation came back undecided — the
    /// value-level counterpart of a construct the translation drops, scoping a
    /// "consistent" verdict to the modelled datatype subset.
    /// </summary>
    public const string UndecidedMarker = "DataRange(undecided-satisfiability)";

    /// <summary>
    /// The name a module verdict carries on its beyond-fragment remainder when a
    /// delegate-backed (self-certified) registered datatype decided one of the
    /// completion's concrete-domain obligations — the provenance tag that scopes a
    /// verdict to the operator's own trusted differential-battery obligation, since
    /// a delegate-backed definition is not registration-self-tested against the
    /// naive oracle the way a declarative definition is.
    /// </summary>
    public const string SelfCertifiedMarker = "DataRange(self-certified-definition)";

    /// <summary>
    /// Decides the datatype consistency of the data restrictions present at one
    /// node with no data-property RBox in scope — the same-property universal
    /// check in property-in-isolation form.
    /// </summary>
    /// <param name="concepts">The node's concepts; non-data concepts are ignored.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <param name="conflict">The conflicting atoms when the result is <see cref="DataConsistencyStatus.Clash"/>, otherwise empty.</param>
    /// <returns>The datatype-consistency verdict.</returns>
    public static DataConsistencyStatus Decide(IEnumerable<AlcConcept> concepts, DatatypeRegistry registry, out IReadOnlyList<AlcConcept> conflict)
    {
        return Decide(concepts, DataPropertyBox.Empty, gate: null, registry, out conflict, out _);
    }

    /// <summary>
    /// Decides the datatype consistency of the data restrictions present at one
    /// node against a data-property RBox, reporting on a clash the atoms whose
    /// joint presence forces it so a clause-learning caller can forbid exactly
    /// that combination.
    /// </summary>
    /// <param name="concepts">The node's concepts; non-data concepts are ignored.</param>
    /// <param name="box">The module's data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <param name="conflict">The conflicting atoms when the result is <see cref="DataConsistencyStatus.Clash"/>, otherwise empty.</param>
    /// <returns>The datatype-consistency verdict.</returns>
    public static DataConsistencyStatus Decide(IEnumerable<AlcConcept> concepts, DataPropertyBox box, DatatypeRegistry registry, out IReadOnlyList<AlcConcept> conflict)
    {
        return Decide(concepts, box, gate: null, registry, out conflict, out _);
    }

    /// <summary>
    /// Decides the datatype consistency of the data restrictions present at one
    /// node against a data-property RBox under a caller-supplied oracle budget
    /// gate: every satisfiability-checker invocation first consults the gate, and
    /// a stopped decision returns <see cref="DataConsistencyStatus.Undecided"/> —
    /// never a partial decisive verdict.
    /// </summary>
    /// <param name="concepts">The node's concepts; non-data concepts are ignored.</param>
    /// <param name="box">The module's data-property RBox.</param>
    /// <param name="gate">The per-invocation oracle budget gate, or <see langword="null"/> for unbounded.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <param name="conflict">The conflicting atoms when the result is <see cref="DataConsistencyStatus.Clash"/>, otherwise empty.</param>
    /// <param name="selfCertifiedDecided">Whether a delegate-backed (self-certified) registered datatype decided one of the node's obligations, so the caller names <see cref="SelfCertifiedMarker"/> on the module remainder.</param>
    /// <returns>The datatype-consistency verdict.</returns>
    public static DataConsistencyStatus Decide(IEnumerable<AlcConcept> concepts, DataPropertyBox box, DataOracleGateDelegate? gate, DatatypeRegistry registry, out IReadOnlyList<AlcConcept> conflict, out bool selfCertifiedDecided)
    {
        ArgumentNullException.ThrowIfNull(registry);
        //Rebuild each data demand concept over its canonical range before any
        //decision, so PointValue observes a degenerate interval as the point
        //enumeration it denotes and every checker call sees the semantic normal
        //form. The rebuild preserves the caller's instance when the range is
        //already canonical; where it does not, the caller's original concept is
        //recovered for the reported conflict core, so a clause-learning caller
        //keys its conflict back onto the concept it handed in.
        Dictionary<AlcConcept, AlcConcept> originalByCanonical = [];
        List<AlcConcept> canonicalConcepts = [];
        foreach(AlcConcept concept in concepts)
        {
            AlcConcept canonical = CanonicalizeConcept(concept);
            if(!ReferenceEquals(canonical, concept))
            {
                originalByCanonical[canonical] = concept;
            }

            canonicalConcepts.Add(canonical);
        }

        DataConsistencyStatus status = DecideCore(canonicalConcepts, box, gate, registry, out IReadOnlyList<AlcConcept> rawConflict, out selfCertifiedDecided);
        conflict = MapToOriginals(rawConflict, originalByCanonical);

        return status;
    }

    /// <summary>
    /// Rebuilds a data demand concept over its canonical range, preserving the
    /// concept's instance when its range is already canonical.
    /// </summary>
    /// <param name="concept">The concept.</param>
    /// <returns>The canonical concept — the same instance when already canonical.</returns>
    private static AlcConcept CanonicalizeConcept(AlcConcept concept)
    {
        switch(concept)
        {
            case AlcDataSome some:
            {
                OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(some.Range);

                return ReferenceEquals(canonical, some.Range) ? some : new AlcDataSome(some.Property, canonical);
            }

            case AlcDataAll all:
            {
                OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(all.Range);

                return ReferenceEquals(canonical, all.Range) ? all : new AlcDataAll(all.Property, canonical);
            }

            case AlcDataMinCard min:
            {
                OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(min.Range);

                return ReferenceEquals(canonical, min.Range) ? min : new AlcDataMinCard(min.Count, min.Property, canonical);
            }

            case AlcDataMaxCard max:
            {
                OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(max.Range);

                return ReferenceEquals(canonical, max.Range) ? max : new AlcDataMaxCard(max.Count, max.Property, canonical);
            }

            default:
            {
                return concept;
            }
        }
    }

    /// <summary>Maps a conflict core's rebuilt concepts back to the caller's original instances, leaving unchanged concepts as they are.</summary>
    /// <param name="conflict">The conflict core over rebuilt concepts.</param>
    /// <param name="originalByCanonical">The rebuilt-to-original concept map.</param>
    /// <returns>The conflict core over the caller's original concepts.</returns>
    private static IReadOnlyList<AlcConcept> MapToOriginals(IReadOnlyList<AlcConcept> conflict, Dictionary<AlcConcept, AlcConcept> originalByCanonical)
    {
        if(conflict.Count == 0 || originalByCanonical.Count == 0)
        {
            return conflict;
        }

        List<AlcConcept> mapped = new(conflict.Count);
        foreach(AlcConcept concept in conflict)
        {
            mapped.Add(originalByCanonical.TryGetValue(concept, out AlcConcept? original) ? original : concept);
        }

        return mapped;
    }

    /// <summary>
    /// Decides the datatype consistency of already-canonical data demand concepts
    /// against a data-property RBox under a caller-supplied oracle budget gate.
    /// </summary>
    /// <param name="concepts">The canonicalized node concepts; non-data concepts are ignored.</param>
    /// <param name="box">The module's data-property RBox.</param>
    /// <param name="gate">The per-invocation oracle budget gate, or <see langword="null"/> for unbounded.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="conflict">The conflicting atoms when the result is <see cref="DataConsistencyStatus.Clash"/>, otherwise empty.</param>
    /// <param name="selfCertifiedDecided">Whether a delegate-backed registered datatype decided one of the node's obligations.</param>
    /// <returns>The datatype-consistency verdict.</returns>
    private static DataConsistencyStatus DecideCore(IReadOnlyList<AlcConcept> concepts, DataPropertyBox box, DataOracleGateDelegate? gate, DatatypeRegistry registry, out IReadOnlyList<AlcConcept> conflict, out bool selfCertifiedDecided)
    {
        conflict = [];
        selfCertifiedDecided = false;

        Dictionary<Utf8String, PropertyConstraints> byProperty = [];
        foreach(AlcConcept concept in concepts)
        {
            switch(concept)
            {
                case AlcDataSome some:
                {
                    ConstraintsOf(byProperty, some.Property).Existentials.Add(some);

                    break;
                }

                case AlcDataAll all:
                {
                    ConstraintsOf(byProperty, all.Property).Universals.Add(all);

                    break;
                }

                case AlcDataMinCard min:
                {
                    ConstraintsOf(byProperty, min.Property).MinCardinalities.Add(min);

                    break;
                }

                case AlcDataMaxCard max:
                {
                    ConstraintsOf(byProperty, max.Property).MaxCardinalities.Add(max);

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        bool undecided = false;
        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            List<OwlDataRange> effectiveRanges = [];
            List<AlcDataAll> effectiveConcepts = [];
            CollectEffectiveUniversals(entry.Key, byProperty, box, effectiveRanges, effectiveConcepts);

            PropertyConstraints constraints = entry.Value;
            foreach(AlcDataSome some in constraints.Existentials)
            {
                if(gate is not null && !gate())
                {
                    return DataConsistencyStatus.Undecided;
                }

                List<OwlDataRange> existentialConjunction = Conjunction(some.Range, effectiveRanges);
                DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction(existentialConjunction, registry);
                selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(existentialConjunction, registry);
                if(verdict == DatatypeSatisfiability.Unsatisfiable)
                {
                    conflict = Conflict(some, effectiveConcepts);

                    return DataConsistencyStatus.Clash;
                }

                undecided |= verdict == DatatypeSatisfiability.Unknown;
            }

            foreach(AlcDataMinCard min in constraints.MinCardinalities)
            {
                if(gate is not null && !gate())
                {
                    return DataConsistencyStatus.Undecided;
                }

                List<OwlDataRange> minConjunction = Conjunction(min.Range, effectiveRanges);
                DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideMinCardinality(minConjunction, min.Count, registry);
                selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(minConjunction, registry);
                if(verdict == DatatypeSatisfiability.Unsatisfiable)
                {
                    conflict = Conflict(min, effectiveConcepts);

                    return DataConsistencyStatus.Clash;
                }

                undecided |= verdict == DatatypeSatisfiability.Unknown;
            }
        }

        foreach(Utf8String functional in box.FunctionalProperties)
        {
            if(gate is not null && !gate())
            {
                return DataConsistencyStatus.Undecided;
            }

            SidecarOutcome outcome = DecideFunctionalPool(functional, byProperty, box, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> poolConflict);
            switch(outcome)
            {
                case SidecarOutcome.Clash:
                {
                    conflict = poolConflict;

                    return DataConsistencyStatus.Clash;
                }

                case SidecarOutcome.Undecided:
                {
                    undecided = true;

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            if(entry.Value.MaxCardinalities.Count == 0)
            {
                continue;
            }

            if(gate is not null && !gate())
            {
                return DataConsistencyStatus.Undecided;
            }

            SidecarOutcome outcome = DecideMaxSlot(entry.Key, entry.Value.MaxCardinalities, byProperty, box, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> slotConflict);
            switch(outcome)
            {
                case SidecarOutcome.Clash:
                {
                    conflict = slotConflict;

                    return DataConsistencyStatus.Clash;
                }

                case SidecarOutcome.Undecided:
                {
                    undecided = true;

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        foreach(DisjointDataPropertyPair pair in box.DisjointPairs)
        {
            if(gate is not null && !gate())
            {
                return DataConsistencyStatus.Undecided;
            }

            SidecarOutcome outcome = DecideDisjointPair(pair, byProperty, box, gate, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> pairConflict);
            if(outcome == SidecarOutcome.Stopped)
            {
                return DataConsistencyStatus.Undecided;
            }
            switch(outcome)
            {
                case SidecarOutcome.Clash:
                {
                    conflict = pairConflict;

                    return DataConsistencyStatus.Clash;
                }

                case SidecarOutcome.Undecided:
                {
                    undecided = true;

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        return undecided ? DataConsistencyStatus.Undecided : DataConsistencyStatus.Consistent;
    }

    /// <summary>
    /// Decides the datatype consistency over an entire node forest with no
    /// data-property RBox in scope.
    /// </summary>
    /// <param name="nodeLabels">The per-node concept labels.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <returns>The combined verdict.</returns>
    public static DataConsistencyStatus DecideForest(IReadOnlyList<List<AlcConcept>> nodeLabels, DatatypeRegistry registry)
    {
        return DecideForest(nodeLabels, DataPropertyBox.Empty, registry, out _);
    }

    /// <summary>
    /// Decides the datatype consistency over an entire node forest against a
    /// data-property RBox, for callers that need only the verdict and not the
    /// conflicting atoms — a clash on any node closes the search, an undecided
    /// obligation on any node makes the verdict fragment-relative.
    /// </summary>
    /// <param name="nodeLabels">The per-node concept labels.</param>
    /// <param name="box">The module's data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> for no registration.</param>
    /// <returns>The combined verdict.</returns>
    public static DataConsistencyStatus DecideForest(IReadOnlyList<List<AlcConcept>> nodeLabels, DataPropertyBox box, DatatypeRegistry registry)
    {
        return DecideForest(nodeLabels, box, registry, out _);
    }

    /// <summary>
    /// Decides the datatype consistency over an entire node forest against a
    /// data-property RBox, reporting whether a delegate-backed registered datatype
    /// decided any node's obligation so the caller names <see cref="SelfCertifiedMarker"/>
    /// on the module remainder.
    /// </summary>
    /// <param name="nodeLabels">The per-node concept labels.</param>
    /// <param name="box">The module's data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">Whether a delegate-backed registered datatype decided any node's obligation.</param>
    /// <returns>The combined verdict.</returns>
    public static DataConsistencyStatus DecideForest(IReadOnlyList<List<AlcConcept>> nodeLabels, DataPropertyBox box, DatatypeRegistry registry, out bool selfCertifiedDecided)
    {
        bool undecided = false;
        selfCertifiedDecided = false;
        foreach(List<AlcConcept> label in nodeLabels)
        {
            DataConsistencyStatus status = Decide(label, box, gate: null, registry, out _, out bool nodeSelfCertified);
            selfCertifiedDecided |= nodeSelfCertified;
            if(status == DataConsistencyStatus.Clash)
            {
                return DataConsistencyStatus.Clash;
            }

            undecided |= status == DataConsistencyStatus.Undecided;
        }

        return undecided ? DataConsistencyStatus.Undecided : DataConsistencyStatus.Consistent;
    }

    /// <summary>
    /// Decides a functional property's pooled demand: every value-forcing demand
    /// on the property or a sub-property of it must be met by one shared value,
    /// so a counting demand of two or more clashes outright, and otherwise the
    /// single value must lie in every pooled range and every effective universal
    /// of every pooled property. A vacuous <c>MinCard(0)</c> forces no value and
    /// is excluded from the pool.
    /// </summary>
    /// <param name="functional">The functional property IRI.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when the pooled conjunction was decided (non-Unknown) and mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <param name="conflict">On a clash, the pooled demands TOGETHER WITH the node-level universal atoms conjoined into the pool — the exact unsatisfiable core, so a clause learned from it never forbids a combination that is satisfiable without those universals. Module-level box ranges stay out of the core: they hold at every node, so their omission never over-strengthens a learned clause.</param>
    /// <returns>The pool's outcome.</returns>
    private static SidecarOutcome DecideFunctionalPool(Utf8String functional, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided, out IReadOnlyList<AlcConcept> conflict)
    {
        conflict = [];

        List<AlcConcept> pooledDemands = [];
        List<OwlDataRange> pooledRanges = [];
        HashSet<Utf8String> pooledProperties = [];
        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            if(!box.IsSuperOrSelf(entry.Key, functional))
            {
                continue;
            }

            PropertyConstraints constraints = entry.Value;
            foreach(AlcDataSome some in constraints.Existentials)
            {
                pooledDemands.Add(some);
                pooledRanges.Add(some.Range);
                pooledProperties.Add(entry.Key);
            }

            foreach(AlcDataMinCard min in constraints.MinCardinalities)
            {
                if(min.Count <= 0)
                {
                    //A MinCard(0) demand forces no value, so it neither pools nor contributes its range.
                    continue;
                }

                if(min.Count >= 2)
                {
                    conflict = [min];

                    return SidecarOutcome.Clash;
                }

                pooledDemands.Add(min);
                pooledRanges.Add(min.Range);
                pooledProperties.Add(entry.Key);
            }
        }

        if(pooledDemands.Count == 0)
        {
            return SidecarOutcome.None;
        }

        List<AlcDataAll> pooledUniversalConcepts = [];
        foreach(Utf8String property in pooledProperties)
        {
            CollectEffectiveUniversals(property, byProperty, box, pooledRanges, pooledUniversalConcepts);
        }

        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction(pooledRanges, registry);
        selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(pooledRanges, registry);
        switch(verdict)
        {
            case DatatypeSatisfiability.Unsatisfiable:
            {
                List<AlcConcept> core = [.. pooledDemands];
                foreach(AlcDataAll universal in pooledUniversalConcepts)
                {
                    if(!core.Contains(universal))
                    {
                        core.Add(universal);
                    }
                }

                conflict = core;

                return SidecarOutcome.Clash;
            }

            case DatatypeSatisfiability.Unknown:
            {
                return SidecarOutcome.Undecided;
            }

            default:
            {
                return SidecarOutcome.None;
            }
        }
    }

    /// <summary>
    /// Decides one data property's MAX SLOT: the property's own
    /// maximum-cardinality bounds against every value-forcing demand on it or on a
    /// sub-property of it, pooled with the same <see cref="DataPropertyBox.IsSuperOrSelf"/>
    /// sweep the functional pool uses — a missed sub-property demand could certify
    /// a slot satisfiable while a hidden second value forces it. The slot's bound
    /// is the least of the property's own maxima (a witness model within the least
    /// bound is within every one of them); the slot is RANGE-LESS when every one of
    /// those maxima ranges over the literal top, and QUALIFIED otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A RANGE-LESS bound of one is the single-slot case: at range top every filler
    /// counts toward the bound, so the pooled demands must be met by one shared
    /// value, and <see cref="DecideFunctionalPool"/> — the same merge procedure a
    /// functional property takes, called directly rather than mirrored, since its
    /// functional gate lives in its caller and the routine itself sweeps purely by
    /// the sub-or-self relation — decides it in both directions.
    /// </para>
    /// <para>
    /// Every other slot (a qualified bound of one, or any bound of two or more)
    /// is offered first to the POINTS-ONLY overflow rule
    /// (<see cref="TryPointsOnlyOverflow"/>): a pool of nothing but point demands
    /// whose provably-distinct values are, per maximum, provably in that maximum's
    /// range outnumbers the bound in every model, which is a clash. Nothing else
    /// raises a clash here — the slot's remaining verdicts come from a
    /// witness-construction certificate, because a qualified bound counts only its
    /// range-typed fillers: merging arbitrary pooled demands into one conjunction
    /// would falsely close a node whose forced values simply lie outside the
    /// range. More forced values than the bound admits, without that per-maximum
    /// membership proof, is therefore an abstention and not an inconsistency.
    /// </para>
    /// <para>
    /// The certificate (<see cref="CertifyMaxSlot"/>) exhibits a model from the
    /// pooled demands themselves: a pool forcing no value, a lone counting demand
    /// fitting the bound, a pool of provably-distinct points fitting it, or such a
    /// pool of points beside exactly one counting demand that enough of them
    /// provably witness. Any other pool abstains.
    /// </para>
    /// </remarks>
    /// <param name="property">The anchoring data-property IRI.</param>
    /// <param name="maxima">The property's own maximum-cardinality bounds; never empty.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a decided check on the slot mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <param name="conflict">On a clash, the pooled demands and the node-level universal atoms conjoined into the pool TOGETHER WITH the slot's own maximum atoms — a bound is a node-level concept, not a module-level box axiom, so a core omitting it would teach a clause forbidding a combination that is satisfiable without the bound; empty otherwise.</param>
    /// <returns>The slot's outcome.</returns>
    private static SidecarOutcome DecideMaxSlot(Utf8String property, List<AlcDataMaxCard> maxima, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided, out IReadOnlyList<AlcConcept> conflict)
    {
        conflict = [];

        int maxBound = maxima[0].Count;
        bool rangeLess = true;
        foreach(AlcDataMaxCard maximum in maxima)
        {
            maxBound = Math.Min(maxBound, maximum.Count);
            rangeLess &= IsLiteralTop(maximum.Range);
        }

        if(rangeLess && maxBound == 1)
        {
            SidecarOutcome pooled = DecideFunctionalPool(property, byProperty, box, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> pooledConflict);
            conflict = pooled == SidecarOutcome.Clash ? WithMaxima(pooledConflict, maxima) : [];

            return pooled;
        }

        if(TryPointsOnlyOverflow(property, maxima, byProperty, box, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> overflowConflict))
        {
            conflict = overflowConflict;

            return SidecarOutcome.Clash;
        }

        return CertifyMaxSlot(property, maxBound, byProperty, box, registry, ref selfCertifiedDecided);
    }

    /// <summary>
    /// The POINTS-ONLY overflow rule on a max slot: a pool of nothing but point
    /// demands forces exactly its own values in every model, so a maximum whose
    /// range provably contains more of those pairwise-distinct values than the
    /// bound admits is violated in every model — a clash, not an abstention.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule engages only on a pool of point demands: a counting demand or a
    /// non-point existential leaves the pool's realised values open, so the pool
    /// is handed on untouched. Distinctness rides
    /// <see cref="DatatypeSatisfiabilityChecker.CompareValues"/> and any
    /// undecidable pair hands the pool on as well, since no clash may rest on an
    /// undecided identity.
    /// </para>
    /// <para>
    /// Counting is PER MAXIMUM rather than against the slot's folded bound,
    /// because the qualifying ranges may differ between the property's maxima and
    /// the sound statement is per range: a point counts against a maximum only
    /// when its own singleton range, that maximum's range, and the point's
    /// effective universals are together decisively satisfiable — for a singleton
    /// enumeration exactly the proof that the value inhabits the range. A point
    /// that cannot be placed in the range counts nowhere, so a bound qualified
    /// away from the forced values still abstains.
    /// </para>
    /// </remarks>
    /// <param name="property">The anchoring data-property IRI.</param>
    /// <param name="maxima">The property's own maximum-cardinality bounds; never empty.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a decided comparison or membership check mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <param name="conflict">On a clash, the counted point demands and the effective universal atoms consulted TOGETHER WITH the exceeded maximum atom — the bound is a node-level concept, so a core omitting it would teach a clause forbidding a combination that is satisfiable without the bound; empty otherwise.</param>
    /// <returns><see langword="true"/> when the pooled points provably overflow one of the maxima.</returns>
    private static bool TryPointsOnlyOverflow(Utf8String property, List<AlcDataMaxCard> maxima, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided, out IReadOnlyList<AlcConcept> conflict)
    {
        conflict = [];

        List<PointDemand> pooled = [];
        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            if(!box.IsSuperOrSelf(entry.Key, property))
            {
                continue;
            }

            PropertyConstraints constraints = entry.Value;
            foreach(AlcDataMinCard min in constraints.MinCardinalities)
            {
                if(min.Count >= 1)
                {
                    //A counting demand's witnesses are not the pooled points, so the
                    //pool is not points-only and the rule does not engage.
                    return false;
                }
            }

            foreach(AlcDataSome some in constraints.Existentials)
            {
                ForcedDemand demand = new(entry.Key, some, some.Range);
                if(PointValue(demand) is not Literal value)
                {
                    //A non-point existential's witness may coincide with any other
                    //value, so the pool's realised count is not the point count.
                    return false;
                }

                pooled.Add(new PointDemand(demand, value));
            }
        }

        if(pooled.Count == 0)
        {
            return false;
        }

        List<PointDemand> distinct = [];
        foreach(PointDemand point in pooled)
        {
            bool known = false;
            foreach(PointDemand kept in distinct)
            {
                DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(point.Value, kept.Value, registry);
                selfCertifiedDecided |= !registry.IsEmpty && identity != DatatypeValueIdentity.Indeterminate && EitherMentionsSelfCertified(point.Value, kept.Value, registry);
                if(identity == DatatypeValueIdentity.Indeterminate)
                {
                    return false;
                }

                known |= identity == DatatypeValueIdentity.Same;
            }

            if(!known)
            {
                distinct.Add(point);
            }
        }

        foreach(AlcDataMaxCard maximum in maxima)
        {
            List<AlcConcept> counted = [];
            List<AlcDataAll> consulted = [];
            foreach(PointDemand point in distinct)
            {
                List<OwlDataRange> effectiveRanges = [];
                List<AlcDataAll> effectiveConcepts = [];
                CollectEffectiveUniversals(point.Demand.Property, byProperty, box, effectiveRanges, effectiveConcepts);

                List<OwlDataRange> membership = new(effectiveRanges.Count + 2) { point.Demand.Range, maximum.Range };
                membership.AddRange(effectiveRanges);

                DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction(membership, registry);
                selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(membership, registry);
                if(verdict != DatatypeSatisfiability.Satisfiable)
                {
                    continue;
                }

                counted.Add(point.Demand.Concept);
                foreach(AlcDataAll universal in effectiveConcepts)
                {
                    if(!consulted.Contains(universal))
                    {
                        consulted.Add(universal);
                    }
                }
            }

            if(counted.Count > maximum.Count)
            {
                foreach(AlcDataAll universal in consulted)
                {
                    if(!counted.Contains(universal))
                    {
                        counted.Add(universal);
                    }
                }

                conflict = WithMaxima(counted, [maximum]);

                return true;
            }
        }

        return false;
    }

    /// <summary>The pooled conflict core extended with the slot's maximum atoms — the bound whose presence turns the pooled demands into a clash.</summary>
    /// <param name="pooledConflict">The pool's own conflict core.</param>
    /// <param name="maxima">The slot's maximum-cardinality bounds.</param>
    /// <returns>The extended core.</returns>
    private static List<AlcConcept> WithMaxima(IReadOnlyList<AlcConcept> pooledConflict, List<AlcDataMaxCard> maxima)
    {
        List<AlcConcept> core = new(pooledConflict.Count + maxima.Count);
        core.AddRange(pooledConflict);
        foreach(AlcDataMaxCard maximum in maxima)
        {
            if(!core.Contains(maximum))
            {
                core.Add(maximum);
            }
        }

        return core;
    }

    /// <summary>
    /// Certifies a max slot by exhibiting one model that meets the slot's bound
    /// together with every demand pooled under it, or abstains: a pool forcing no
    /// value is satisfied outright; a lone counting demand whose count fits the
    /// bound rides that demand's own counting verdict, because a model holding
    /// exactly its witnesses has that many fillers in total and so meets every
    /// bound at or above the count; and a pool of provably-pairwise-distinct point
    /// demands whose distinct count fits the bound is satisfied by those points
    /// themselves. A pool of points beside EXACTLY ONE counting demand is satisfied
    /// by the points too when enough of them provably witness that demand
    /// (<see cref="CertifyMixedPool"/>). Several counting demands, or a non-point
    /// existential beside another demand, certify no model and abstain, since the
    /// values the demands force need not coincide. The procedure raises no
    /// clash of its own: the one clash a non-single slot admits is the
    /// points-only overflow its caller has already ruled out
    /// (<see cref="TryPointsOnlyOverflow"/>), so a distinct count above the bound
    /// reaching here is an abstention.
    /// </summary>
    /// <param name="property">The anchoring data-property IRI.</param>
    /// <param name="maxBound">The slot's least maximum bound.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a decided check on the slot mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <returns><see cref="SidecarOutcome.None"/> when a model is certified, otherwise <see cref="SidecarOutcome.Undecided"/>.</returns>
    private static SidecarOutcome CertifyMaxSlot(Utf8String property, int maxBound, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided)
    {
        List<ForcedDemand> points = [];
        List<AlcDataMinCard> countingDemands = [];
        int nonPointExistentials = 0;
        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            if(!box.IsSuperOrSelf(entry.Key, property))
            {
                continue;
            }

            PropertyConstraints constraints = entry.Value;
            foreach(AlcDataSome some in constraints.Existentials)
            {
                ForcedDemand demand = new(entry.Key, some, some.Range);
                if(PointValue(demand) is null)
                {
                    nonPointExistentials++;

                    continue;
                }

                points.Add(demand);
            }

            foreach(AlcDataMinCard min in constraints.MinCardinalities)
            {
                if(min.Count >= 1)
                {
                    //A MinCard(0) demand forces no value, so it neither pools nor bears on the bound.
                    countingDemands.Add(min);
                }
            }
        }

        if(nonPointExistentials > 0)
        {
            //A non-point existential's witness need not coincide with any other
            //demand's, so no filler count is certified.
            return SidecarOutcome.Undecided;
        }

        if(points.Count == 0 && countingDemands.Count == 0)
        {
            //Nothing forces a value on the slot, so no bound can be exceeded.
            return SidecarOutcome.None;
        }

        if(points.Count == 0 && countingDemands.Count == 1)
        {
            return CertifyCountingDemand(countingDemands[0], maxBound, byProperty, box, registry, ref selfCertifiedDecided);
        }

        if(countingDemands.Count == 0)
        {
            return CertifyDistinctPoints(points, maxBound, byProperty, box, registry, ref selfCertifiedDecided);
        }

        if(countingDemands.Count == 1)
        {
            return CertifyMixedPool(points, countingDemands[0], maxBound, byProperty, box, registry, ref selfCertifiedDecided);
        }

        //Two counting demands may ride disjoint witness sets, so the pool's filler count is not certified.
        return SidecarOutcome.Undecided;
    }

    /// <summary>
    /// Certifies a max slot whose only pooled demand is one counting demand: a
    /// count above the bound abstains (whether the demand MUST exceed the bound is
    /// the general maximum direction this procedure does not claim), and otherwise
    /// the demand's own counting verdict over its range and effective universals —
    /// the same conjunction and count the per-property counting loop already
    /// decided, which owns the decisive unsatisfiable direction and its conflict
    /// core — carries the slot.
    /// </summary>
    /// <param name="counting">The pooled counting demand.</param>
    /// <param name="maxBound">The slot's least maximum bound.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when the counting conjunction was decided and mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <returns><see cref="SidecarOutcome.None"/> when the demand's witnesses model the slot, otherwise <see cref="SidecarOutcome.Undecided"/>.</returns>
    private static SidecarOutcome CertifyCountingDemand(AlcDataMinCard counting, int maxBound, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided)
    {
        if(counting.Count > maxBound)
        {
            return SidecarOutcome.Undecided;
        }

        List<OwlDataRange> effectiveRanges = [];
        List<AlcDataAll> effectiveConcepts = [];
        CollectEffectiveUniversals(counting.Property, byProperty, box, effectiveRanges, effectiveConcepts);

        List<OwlDataRange> conjunction = Conjunction(counting.Range, effectiveRanges);
        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideMinCardinality(conjunction, counting.Count, registry);
        selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(conjunction, registry);

        return verdict == DatatypeSatisfiability.Satisfiable ? SidecarOutcome.None : SidecarOutcome.Undecided;
    }

    /// <summary>
    /// Certifies a max slot whose pooled demands are all point demands: the points
    /// are folded to their provably-distinct values, and the fold itself is the
    /// model when the distinct count fits the bound and every point provably
    /// inhabits its own effective universals. An undecidable value pair, a distinct
    /// count above the bound, or a point the checker cannot place abstains — the
    /// decisive unsatisfiable direction over a single point is the per-property
    /// existential loop's, which owns its conflict core.
    /// </summary>
    /// <param name="points">The pooled point demands; never empty.</param>
    /// <param name="maxBound">The slot's least maximum bound.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a decided comparison or placement mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <returns><see cref="SidecarOutcome.None"/> when the points model the slot, otherwise <see cref="SidecarOutcome.Undecided"/>.</returns>
    private static SidecarOutcome CertifyDistinctPoints(List<ForcedDemand> points, int maxBound, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided)
    {
        List<Literal> distinct = [];
        foreach(ForcedDemand point in points)
        {
            if(PointValue(point) is not Literal value)
            {
                return SidecarOutcome.Undecided;
            }

            bool known = false;
            foreach(Literal kept in distinct)
            {
                DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(value, kept, registry);
                selfCertifiedDecided |= !registry.IsEmpty && identity != DatatypeValueIdentity.Indeterminate && EitherMentionsSelfCertified(value, kept, registry);
                if(identity == DatatypeValueIdentity.Indeterminate)
                {
                    return SidecarOutcome.Undecided;
                }

                known |= identity == DatatypeValueIdentity.Same;
            }

            if(!known)
            {
                distinct.Add(value);
            }
        }

        if(distinct.Count > maxBound)
        {
            return SidecarOutcome.Undecided;
        }

        foreach(ForcedDemand point in points)
        {
            List<OwlDataRange> effectiveRanges = [];
            List<AlcDataAll> effectiveConcepts = [];
            CollectEffectiveUniversals(point.Property, byProperty, box, effectiveRanges, effectiveConcepts);

            List<OwlDataRange> conjunction = Conjunction(point.Range, effectiveRanges);
            DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction(conjunction, registry);
            selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(conjunction, registry);
            if(verdict != DatatypeSatisfiability.Satisfiable)
            {
                return SidecarOutcome.Undecided;
            }
        }

        return SidecarOutcome.None;
    }

    /// <summary>
    /// Certifies a max slot whose pooled demands are point demands beside EXACTLY
    /// ONE counting demand: the points are the proposed model, and the counting
    /// demand rides them. The points fold to their provably-distinct values, each
    /// value keeping the point demands that force it — its CARRIERS, since a value
    /// forced under two properties is a filler of both; the distinct count must fit
    /// the bound; every pooled point must provably inhabit its own effective
    /// universals; and at least the demanded count of distinct values must WITNESS
    /// the counting demand. A value witnesses it when some carrier property the
    /// counting demand's property subsumes places the value in the demand's
    /// qualifying range, so a point outside that range is a filler that counts
    /// toward the bound but not toward the minimum. An undecidable value pair, a
    /// distinct count above the bound, a point the checker cannot place, or too few
    /// witnesses abstains: the procedure raises no clash and emits no conflict core,
    /// since the one clash a non-single slot admits is the points-only overflow its
    /// caller has already ruled out (<see cref="TryPointsOnlyOverflow"/>), which
    /// declines every pool carrying a counting demand.
    /// </summary>
    /// <param name="points">The pooled point demands; never empty.</param>
    /// <param name="counting">The single pooled counting demand.</param>
    /// <param name="maxBound">The slot's least maximum bound.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a decided comparison, placement, or membership check mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <returns><see cref="SidecarOutcome.None"/> when the points model the slot and carry the counting demand, otherwise <see cref="SidecarOutcome.Undecided"/>.</returns>
    private static SidecarOutcome CertifyMixedPool(List<ForcedDemand> points, AlcDataMinCard counting, int maxBound, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided)
    {
        List<PooledValue> distinct = [];
        foreach(ForcedDemand point in points)
        {
            if(PointValue(point) is not Literal value)
            {
                return SidecarOutcome.Undecided;
            }

            List<PointDemand>? carriers = null;
            foreach(PooledValue kept in distinct)
            {
                DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(value, kept.Value, registry);
                selfCertifiedDecided |= !registry.IsEmpty && identity != DatatypeValueIdentity.Indeterminate && EitherMentionsSelfCertified(value, kept.Value, registry);
                if(identity == DatatypeValueIdentity.Indeterminate)
                {
                    return SidecarOutcome.Undecided;
                }

                if(identity == DatatypeValueIdentity.Same && carriers is null)
                {
                    carriers = kept.Carriers;
                }
            }

            if(carriers is null)
            {
                distinct.Add(new PooledValue(value, [new PointDemand(point, value)]));
            }
            else
            {
                carriers.Add(new PointDemand(point, value));
            }
        }

        if(distinct.Count > maxBound)
        {
            return SidecarOutcome.Undecided;
        }

        foreach(ForcedDemand point in points)
        {
            List<OwlDataRange> effectiveRanges = [];
            List<AlcDataAll> effectiveConcepts = [];
            CollectEffectiveUniversals(point.Property, byProperty, box, effectiveRanges, effectiveConcepts);

            List<OwlDataRange> conjunction = Conjunction(point.Range, effectiveRanges);
            DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction(conjunction, registry);
            selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(conjunction, registry);
            if(verdict != DatatypeSatisfiability.Satisfiable)
            {
                return SidecarOutcome.Undecided;
            }
        }

        int witnesses = 0;
        foreach(PooledValue pooled in distinct)
        {
            if(WitnessesCountingDemand(pooled, counting, byProperty, box, registry, ref selfCertifiedDecided))
            {
                witnesses++;
            }
        }

        return witnesses >= counting.Count ? SidecarOutcome.None : SidecarOutcome.Undecided;
    }

    /// <summary>
    /// Whether one pooled distinct value witnesses the slot's counting demand: the
    /// question is existential over the value's carrier set, and the membership
    /// proof runs against a QUALIFYING carrier — one whose property the counting
    /// demand's property subsumes, so a filler of it is a filler of the demand's
    /// property. That carrier's own point range, the counting demand's range, and
    /// that carrier's effective ranges must be together decisively satisfiable; for
    /// a singleton enumeration that is exactly the proof that the value inhabits the
    /// demand's qualifying range under every constraint the carrier inherits.
    /// </summary>
    /// <param name="pooled">The distinct value together with the point demands forcing it.</param>
    /// <param name="counting">The pooled counting demand.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a decided membership check mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <returns><see langword="true"/> when some qualifying carrier places the value in the demand's range.</returns>
    private static bool WitnessesCountingDemand(PooledValue pooled, AlcDataMinCard counting, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided)
    {
        foreach(PointDemand carrier in pooled.Carriers)
        {
            if(!box.IsSuperOrSelf(carrier.Demand.Property, counting.Property))
            {
                //A value forced only under a property the counting demand does not
                //subsume is no filler of the demand's property, so it witnesses nothing.
                continue;
            }

            List<OwlDataRange> effectiveRanges = [];
            List<AlcDataAll> effectiveConcepts = [];
            CollectEffectiveUniversals(carrier.Demand.Property, byProperty, box, effectiveRanges, effectiveConcepts);

            List<OwlDataRange> membership = new(effectiveRanges.Count + 2) { carrier.Demand.Range, counting.Range };
            membership.AddRange(effectiveRanges);

            DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction(membership, registry);
            selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(membership, registry);
            if(verdict == DatatypeSatisfiability.Satisfiable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a data range is the literal top <c>rdfs:Literal</c> — the whole data domain, where every value of a property counts toward a maximum bound.</summary>
    /// <param name="range">The canonicalized data range.</param>
    /// <returns><see langword="true"/> when the range is the literal top.</returns>
    private static bool IsLiteralTop(OwlDataRange range)
    {
        return range is OwlDatatypeReference reference && reference.Datatype.Iri.Equals(Lumoin.Veritas.Rdf.RdfVocabulary.Rdfs.LiteralClass);
    }

    /// <summary>
    /// Decides a disjoint property pair: a shared value between the two sides is
    /// forbidden. A single property below both sides carrying a value-forcing
    /// demand clashes outright; a functional property pooling both sides forces
    /// the forbidden shared value; two point demands forcing the same value
    /// clash; and otherwise the two sides co-exist only when value-choice freedom
    /// is proven, else the pair is a sound abstention.
    /// </summary>
    /// <param name="pair">The disjoint property pair.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="gate">The per-invocation oracle budget gate, or <see langword="null"/> for unbounded — consulted before each cross-pair evaluation, the pair's per-invocation cost center.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a cross-pair comparison or point subtraction was decided by a delegate-backed (self-certified) registered datatype.</param>
    /// <param name="conflict">The demands forcing a clash, otherwise empty.</param>
    /// <returns>The pair's outcome.</returns>
    private static SidecarOutcome DecideDisjointPair(DisjointDataPropertyPair pair, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DataOracleGateDelegate? gate, DatatypeRegistry registry, ref bool selfCertifiedDecided, out IReadOnlyList<AlcConcept> conflict)
    {
        conflict = [];

        List<ForcedDemand> firstSide = ValueForcingDemandsUnder(pair.First, byProperty, box);
        List<ForcedDemand> secondSide = ValueForcingDemandsUnder(pair.Second, byProperty, box);
        if(firstSide.Count == 0 || secondSide.Count == 0)
        {
            //Value-forcing demands on only one side (or neither) impose no constraint.
            return SidecarOutcome.None;
        }

        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            if(box.IsSuperOrSelf(entry.Key, pair.First) && box.IsSuperOrSelf(entry.Key, pair.Second) && HasValueForcingDemand(entry.Value))
            {
                conflict = ValueForcingConcepts(entry.Value);

                return SidecarOutcome.Clash;
            }
        }

        foreach(Utf8String functional in box.FunctionalProperties)
        {
            if(SidePoolsFunctional(firstSide, functional, box) && SidePoolsFunctional(secondSide, functional, box))
            {
                conflict = CombineDemands(firstSide, secondSide);

                return SidecarOutcome.Clash;
            }
        }

        List<Literal> firstPoints = PointsOf(firstSide);
        List<Literal> secondPoints = PointsOf(secondSide);
        SidecarOutcome firstSubtraction = SubtractOppositePoints(firstSide, secondPoints, secondSide, byProperty, box, gate, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> firstSubtractionConflict);
        if(firstSubtraction == SidecarOutcome.Stopped)
        {
            return SidecarOutcome.Stopped;
        }

        if(firstSubtraction == SidecarOutcome.Clash)
        {
            conflict = firstSubtractionConflict;

            return SidecarOutcome.Clash;
        }

        SidecarOutcome secondSubtraction = SubtractOppositePoints(secondSide, firstPoints, firstSide, byProperty, box, gate, registry, ref selfCertifiedDecided, out IReadOnlyList<AlcConcept> secondSubtractionConflict);
        if(secondSubtraction == SidecarOutcome.Stopped)
        {
            return SidecarOutcome.Stopped;
        }

        if(secondSubtraction == SidecarOutcome.Clash)
        {
            conflict = secondSubtractionConflict;

            return SidecarOutcome.Clash;
        }

        bool pairUndecided = firstSubtraction == SidecarOutcome.Undecided || secondSubtraction == SidecarOutcome.Undecided;
        foreach(ForcedDemand demandFirst in firstSide)
        {
            foreach(ForcedDemand demandSecond in secondSide)
            {
                if(gate is not null && !gate())
                {
                    return SidecarOutcome.Stopped;
                }

                switch(EvaluateCrossPair(demandFirst, demandSecond, byProperty, box, registry, ref selfCertifiedDecided))
                {
                    case SidecarOutcome.Clash:
                    {
                        conflict = [demandFirst.Concept, demandSecond.Concept];

                        return SidecarOutcome.Clash;
                    }

                    case SidecarOutcome.Undecided:
                    {
                        pairUndecided = true;

                        break;
                    }

                    default:
                    {
                        break;
                    }
                }
            }
        }

        return pairUndecided ? SidecarOutcome.Undecided : SidecarOutcome.None;
    }

    /// <summary>
    /// Evaluates one cross-pair of value-forcing demands across a disjoint pair:
    /// two point demands are decided by value identity, and a non-point cross-pair
    /// co-exists consistently only when one side provably admits at least two
    /// distinct values (so the two disjoint properties can take different ones),
    /// else it is a sound abstention.
    /// </summary>
    /// <param name="first">The demand under the first side.</param>
    /// <param name="second">The demand under the second side.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a point-vs-point comparison was decided (Same or Distinct) and either compared literal's datatype is a delegate-backed (self-certified) registered datatype.</param>
    /// <returns>The cross-pair outcome; <see cref="SidecarOutcome.None"/> when the pair imposes no constraint.</returns>
    private static SidecarOutcome EvaluateCrossPair(ForcedDemand first, ForcedDemand second, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry, ref bool selfCertifiedDecided)
    {
        if(PointValue(first) is Literal firstValue && PointValue(second) is Literal secondValue)
        {
            DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(firstValue, secondValue, registry);
            selfCertifiedDecided |= !registry.IsEmpty && identity != DatatypeValueIdentity.Indeterminate && EitherMentionsSelfCertified(firstValue, secondValue, registry);

            return identity switch
            {
                DatatypeValueIdentity.Same => SidecarOutcome.Clash,
                DatatypeValueIdentity.Indeterminate => SidecarOutcome.Undecided,
                _ => SidecarOutcome.None
            };
        }

        if(AdmitsTwoDistinct(first, byProperty, box, registry) || AdmitsTwoDistinct(second, byProperty, box, registry))
        {
            return SidecarOutcome.None;
        }

        return SidecarOutcome.Undecided;
    }

    /// <summary>Whether a demand's range, conjoined with its effective universals, provably admits at least two distinct values.</summary>
    /// <param name="demand">The value-forcing demand.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns><see langword="true"/> when at least two distinct values provably exist.</returns>
    private static bool AdmitsTwoDistinct(ForcedDemand demand, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DatatypeRegistry registry)
    {
        List<OwlDataRange> effectiveRanges = [];
        List<AlcDataAll> effectiveConcepts = [];
        CollectEffectiveUniversals(demand.Property, byProperty, box, effectiveRanges, effectiveConcepts);

        return DatatypeSatisfiabilityChecker.DecideMinCardinality(Conjunction(demand.Range, effectiveRanges), 2, registry) == DatatypeSatisfiability.Satisfiable;
    }

    /// <summary>The point value a demand forces, when it is an existential over a singleton enumeration (a lowered <c>DataHasValue</c>, or a degenerate interval canonicalized to one).</summary>
    /// <param name="demand">The demand.</param>
    /// <returns>The forced literal, or <see langword="null"/> when the demand is not a point.</returns>
    private static Literal? PointValue(ForcedDemand demand)
    {
        return demand.Concept is AlcDataSome some && some.Range is OwlDataOneOf oneOf && oneOf.Literals.Count == 1
            ? oneOf.Literals[0]
            : null;
    }

    /// <summary>The forced point values on a side of a disjoint pair — the literals its singleton-enumeration existentials force.</summary>
    /// <param name="side">The side's value-forcing demands.</param>
    /// <returns>The forced point literals.</returns>
    private static List<Literal> PointsOf(List<ForcedDemand> side)
    {
        List<Literal> points = [];
        foreach(ForcedDemand demand in side)
        {
            if(PointValue(demand) is Literal value)
            {
                points.Add(value);
            }
        }

        return points;
    }

    /// <summary>
    /// Subtracts the opposite side's forced points from each non-point demand on a
    /// side: a witness value the demand forces under one property must, by the
    /// disjointness, avoid every value the opposite property is forced to at the
    /// same node, so it lies in the demand's range minus those points. Emptiness of
    /// that difference — or, for a counting demand, too few distinct survivors — is
    /// a clash; an undecidable difference leaves the pair fragment-relative; a
    /// non-empty difference imposes no constraint here. Same-side points are never
    /// subtracted: a non-functional property may repeat a same-side value.
    /// </summary>
    /// <param name="side">The side whose non-point demands are tested.</param>
    /// <param name="oppositePoints">The opposite side's forced point values.</param>
    /// <param name="oppositeSide">The opposite side's demands, for the conflict core.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="gate">The per-invocation oracle budget gate, or <see langword="null"/> for unbounded.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="selfCertifiedDecided">OR'd true when a subtracted demand's conjunction was decided (non-Unknown) and mentions a delegate-backed (self-certified) registered datatype.</param>
    /// <param name="conflict">On a clash, the tested demand together with the contributing opposite-side point demands.</param>
    /// <returns>The subtraction's outcome.</returns>
    private static SidecarOutcome SubtractOppositePoints(List<ForcedDemand> side, List<Literal> oppositePoints, List<ForcedDemand> oppositeSide, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, DataOracleGateDelegate? gate, DatatypeRegistry registry, ref bool selfCertifiedDecided, out IReadOnlyList<AlcConcept> conflict)
    {
        conflict = [];
        if(oppositePoints.Count == 0)
        {
            //With no opposite-side point forcing a shared value, the non-point demands impose no cross-side point constraint.
            return SidecarOutcome.None;
        }

        bool undecided = false;
        foreach(ForcedDemand demand in side)
        {
            if(PointValue(demand) is not null)
            {
                //A point demand is decided by the point-vs-point fast branch; a same-side point is never subtracted.
                continue;
            }

            if(gate is not null && !gate())
            {
                return SidecarOutcome.Stopped;
            }

            List<OwlDataRange> conjunction = SubtractionConjunction(demand, oppositePoints, byProperty, box);
            DatatypeSatisfiability verdict = demand.Concept is AlcDataMinCard min
                ? DatatypeSatisfiabilityChecker.DecideMinCardinality(conjunction, min.Count, registry)
                : DatatypeSatisfiabilityChecker.DecideConjunction(conjunction, registry);
            selfCertifiedDecided |= !registry.IsEmpty && verdict != DatatypeSatisfiability.Unknown && AnyMentionsSelfCertified(conjunction, registry);
            switch(verdict)
            {
                case DatatypeSatisfiability.Unsatisfiable:
                {
                    conflict = SubtractionConflict(demand, oppositeSide);

                    return SidecarOutcome.Clash;
                }

                case DatatypeSatisfiability.Unknown:
                {
                    undecided = true;

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        return undecided ? SidecarOutcome.Undecided : SidecarOutcome.None;
    }

    /// <summary>The conjunction a subtracted non-point demand must satisfy: its own range, its effective universals, and the negated singleton enumeration of each opposite-side point.</summary>
    /// <param name="demand">The non-point demand.</param>
    /// <param name="oppositePoints">The opposite side's forced point values.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <returns>The conjoined ranges.</returns>
    private static List<OwlDataRange> SubtractionConjunction(ForcedDemand demand, List<Literal> oppositePoints, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box)
    {
        List<OwlDataRange> effectiveRanges = [];
        List<AlcDataAll> effectiveConcepts = [];
        CollectEffectiveUniversals(demand.Property, byProperty, box, effectiveRanges, effectiveConcepts);

        List<OwlDataRange> conjunction = new(effectiveRanges.Count + oppositePoints.Count + 1) { demand.Range };
        conjunction.AddRange(effectiveRanges);
        foreach(Literal point in oppositePoints)
        {
            conjunction.Add(new OwlDataComplementOf(new OwlDataOneOf([point])));
        }

        return conjunction;
    }

    /// <summary>The conflict core a subtraction clash teaches: the emptied demand together with the opposite-side point demands that force the removed values.</summary>
    /// <param name="demand">The emptied non-point demand.</param>
    /// <param name="oppositeSide">The opposite side's demands.</param>
    /// <returns>The conflicting atoms.</returns>
    private static List<AlcConcept> SubtractionConflict(ForcedDemand demand, List<ForcedDemand> oppositeSide)
    {
        List<AlcConcept> conflict = [demand.Concept];
        foreach(ForcedDemand opposite in oppositeSide)
        {
            if(PointValue(opposite) is not null && !conflict.Contains(opposite.Concept))
            {
                conflict.Add(opposite.Concept);
            }
        }

        return conflict;
    }

    /// <summary>The value-forcing demands on a side of a disjoint pair — every existential and positive counting demand on the side property or a sub-property of it.</summary>
    /// <param name="side">The side property IRI.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <returns>The value-forcing demands under the side.</returns>
    private static List<ForcedDemand> ValueForcingDemandsUnder(Utf8String side, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box)
    {
        List<ForcedDemand> demands = [];
        foreach(KeyValuePair<Utf8String, PropertyConstraints> entry in byProperty)
        {
            if(!box.IsSuperOrSelf(entry.Key, side))
            {
                continue;
            }

            PropertyConstraints constraints = entry.Value;
            foreach(AlcDataSome some in constraints.Existentials)
            {
                demands.Add(new ForcedDemand(entry.Key, some, some.Range));
            }

            foreach(AlcDataMinCard min in constraints.MinCardinalities)
            {
                if(min.Count >= 1)
                {
                    demands.Add(new ForcedDemand(entry.Key, min, min.Range));
                }
            }
        }

        return demands;
    }

    /// <summary>Whether a side of a disjoint pair carries a value-forcing demand on a property that a functional property is a super of.</summary>
    /// <param name="side">The side's value-forcing demands.</param>
    /// <param name="functional">The functional property IRI.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <returns><see langword="true"/> when the functional property pools a demand of the side.</returns>
    private static bool SidePoolsFunctional(List<ForcedDemand> side, Utf8String functional, DataPropertyBox box)
    {
        foreach(ForcedDemand demand in side)
        {
            if(box.IsSuperOrSelf(demand.Property, functional))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a property's constraints carry a value-forcing demand — an existential or a positive counting demand.</summary>
    /// <param name="constraints">The per-property constraints.</param>
    /// <returns><see langword="true"/> when a value-forcing demand is present.</returns>
    private static bool HasValueForcingDemand(PropertyConstraints constraints)
    {
        if(constraints.Existentials.Count > 0)
        {
            return true;
        }

        foreach(AlcDataMinCard min in constraints.MinCardinalities)
        {
            if(min.Count >= 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The value-forcing demand concepts of a property — the conflict core of a common-subproperty clash.</summary>
    /// <param name="constraints">The per-property constraints.</param>
    /// <returns>The value-forcing demand concepts.</returns>
    private static List<AlcConcept> ValueForcingConcepts(PropertyConstraints constraints)
    {
        List<AlcConcept> concepts = [];
        foreach(AlcDataSome some in constraints.Existentials)
        {
            concepts.Add(some);
        }

        foreach(AlcDataMinCard min in constraints.MinCardinalities)
        {
            if(min.Count >= 1)
            {
                concepts.Add(min);
            }
        }

        return concepts;
    }

    /// <summary>The combined demand concepts of both sides of a disjoint pair — the conflict core of a functional-forced clash.</summary>
    /// <param name="firstSide">The first side's demands.</param>
    /// <param name="secondSide">The second side's demands.</param>
    /// <returns>The combined demand concepts.</returns>
    private static List<AlcConcept> CombineDemands(List<ForcedDemand> firstSide, List<ForcedDemand> secondSide)
    {
        List<AlcConcept> concepts = new(firstSide.Count + secondSide.Count);
        foreach(ForcedDemand demand in firstSide)
        {
            concepts.Add(demand.Concept);
        }

        foreach(ForcedDemand demand in secondSide)
        {
            concepts.Add(demand.Concept);
        }

        return concepts;
    }

    /// <summary>
    /// Collects the effective universal ranges constraining a property — its own
    /// node-level universals and asserted ranges together with those of every
    /// super-property — appending the contributing ranges and the node-level
    /// universal concepts to the working lists.
    /// </summary>
    /// <param name="property">The demanding property IRI.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="box">The data-property RBox.</param>
    /// <param name="rangesToAppendTo">The constraining ranges, appended to.</param>
    /// <param name="conceptsToAppendTo">The node-level universal concepts, appended to.</param>
    private static void CollectEffectiveUniversals(Utf8String property, Dictionary<Utf8String, PropertyConstraints> byProperty, DataPropertyBox box, List<OwlDataRange> rangesToAppendTo, List<AlcDataAll> conceptsToAppendTo)
    {
        AppendNodeUniversals(property, byProperty, rangesToAppendTo, conceptsToAppendTo);
        AppendRanges(box.Ranges(property), rangesToAppendTo);
        foreach(Utf8String super in box.StrictSupers(property))
        {
            AppendNodeUniversals(super, byProperty, rangesToAppendTo, conceptsToAppendTo);
            AppendRanges(box.Ranges(super), rangesToAppendTo);
        }
    }

    /// <summary>Appends a property's node-level universal ranges and concepts, when it carries any.</summary>
    /// <param name="property">The property IRI.</param>
    /// <param name="byProperty">The per-property demand index.</param>
    /// <param name="rangesToAppendTo">The ranges, appended to.</param>
    /// <param name="conceptsToAppendTo">The universal concepts, appended to.</param>
    private static void AppendNodeUniversals(Utf8String property, Dictionary<Utf8String, PropertyConstraints> byProperty, List<OwlDataRange> rangesToAppendTo, List<AlcDataAll> conceptsToAppendTo)
    {
        if(!byProperty.TryGetValue(property, out PropertyConstraints? constraints))
        {
            return;
        }

        foreach(AlcDataAll all in constraints.Universals)
        {
            rangesToAppendTo.Add(all.Range);
            conceptsToAppendTo.Add(all);
        }
    }

    /// <summary>
    /// Appends a property's asserted box ranges to a working list, canonicalized —
    /// the single choke point through which box ranges reach a decision, so the
    /// checker sees the same semantic normal form for a box range as for a demand
    /// range.
    /// </summary>
    /// <param name="ranges">The asserted ranges.</param>
    /// <param name="rangesToAppendTo">The ranges, appended to.</param>
    private static void AppendRanges(IReadOnlyList<OwlDataRange> ranges, List<OwlDataRange> rangesToAppendTo)
    {
        foreach(OwlDataRange range in ranges)
        {
            rangesToAppendTo.Add(DataRangeCanonicalizer.Canonicalize(range));
        }
    }

    /// <summary>The conjunction a per-property demand must satisfy: its own range together with its effective universal ranges.</summary>
    /// <param name="demand">The demand's range.</param>
    /// <param name="universals">The effective universal ranges.</param>
    /// <returns>The conjoined ranges.</returns>
    private static List<OwlDataRange> Conjunction(OwlDataRange demand, List<OwlDataRange> universals)
    {
        List<OwlDataRange> conjunction = new(universals.Count + 1) { demand };
        conjunction.AddRange(universals);

        return conjunction;
    }

    /// <summary>The conflict core a clash teaches: the unsatisfiable demand and the universals that empty it.</summary>
    /// <param name="demand">The unsatisfiable demand atom.</param>
    /// <param name="universals">The universal atoms.</param>
    /// <returns>The conflicting atoms.</returns>
    private static List<AlcConcept> Conflict(AlcConcept demand, List<AlcDataAll> universals)
    {
        List<AlcConcept> conflict = new(universals.Count + 1) { demand };
        conflict.AddRange(universals);

        return conflict;
    }

    /// <summary>The per-property constraint bucket, created on first contact with a property.</summary>
    /// <param name="byProperty">The per-property index.</param>
    /// <param name="property">The data-property IRI.</param>
    /// <returns>The mutable bucket.</returns>
    private static PropertyConstraints ConstraintsOf(Dictionary<Utf8String, PropertyConstraints> byProperty, Utf8String property)
    {
        if(!byProperty.TryGetValue(property, out PropertyConstraints? constraints))
        {
            constraints = new PropertyConstraints();
            byProperty[property] = constraints;
        }

        return constraints;
    }

    /// <summary>Whether either compared literal's datatype is a delegate-backed (self-certified) registered datatype — the literal-level counterpart of <see cref="AnyMentionsSelfCertified"/> for a point-vs-point comparison.</summary>
    /// <param name="first">The first compared literal.</param>
    /// <param name="second">The second compared literal.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <returns><see langword="true"/> when either literal's datatype is a self-certified registered datatype.</returns>
    private static bool EitherMentionsSelfCertified(Literal first, Literal second, DatatypeRegistry registry)
    {
        return IsSelfCertified(first.Datatype.Iri, registry) || IsSelfCertified(second.Datatype.Iri, registry);
    }

    /// <summary>Whether any range in a conjunction mentions a delegate-backed (self-certified) registered datatype.</summary>
    /// <param name="ranges">The conjoined ranges.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <returns><see langword="true"/> when a self-certified registered datatype is mentioned.</returns>
    private static bool AnyMentionsSelfCertified(IReadOnlyList<OwlDataRange> ranges, DatatypeRegistry registry)
    {
        foreach(OwlDataRange range in ranges)
        {
            if(RangeMentionsSelfCertified(range, registry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a data range mentions a delegate-backed (self-certified) registered datatype anywhere in its
    /// tree — the base of a datatype reference or restriction, or the datatype of an enumerated literal. Walked
    /// iteratively with an explicit worklist, bounded by the parser's uniform nesting cap.
    /// </summary>
    /// <param name="range">The data range.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <returns><see langword="true"/> when a self-certified registered datatype is mentioned.</returns>
    private static bool RangeMentionsSelfCertified(OwlDataRange range, DatatypeRegistry registry)
    {
        Stack<OwlDataRange> work = new();
        work.Push(range);
        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case OwlDatatypeReference reference:
                {
                    if(IsSelfCertified(reference.Datatype.Iri, registry))
                    {
                        return true;
                    }

                    break;
                }

                case OwlDatatypeRestriction restriction:
                {
                    if(IsSelfCertified(restriction.Datatype.Iri, registry))
                    {
                        return true;
                    }

                    break;
                }

                case OwlDataOneOf oneOf:
                {
                    foreach(Literal literal in oneOf.Literals)
                    {
                        if(IsSelfCertified(literal.Datatype.Iri, registry))
                        {
                            return true;
                        }
                    }

                    break;
                }

                case OwlDataComplementOf complement:
                {
                    work.Push(complement.Range);

                    break;
                }

                case OwlDataIntersectionOf intersection:
                {
                    foreach(OwlDataRange child in intersection.Ranges)
                    {
                        work.Push(child);
                    }

                    break;
                }

                case OwlDataUnionOf union:
                {
                    foreach(OwlDataRange child in union.Ranges)
                    {
                        work.Push(child);
                    }

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>Whether an IRI is registered as a delegate-backed (self-certified) datatype.</summary>
    /// <param name="iri">The datatype IRI.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <returns><see langword="true"/> when the IRI is a self-certified registered datatype.</returns>
    private static bool IsSelfCertified(Utf8String iri, DatatypeRegistry registry)
    {
        return registry.TryGet(iri, out RegisteredDatatype? registered) && registered.SelfCertified;
    }

    /// <summary>The three-valued outcome of an RBox constraint check.</summary>
    private enum SidecarOutcome
    {
        /// <summary>The constraint imposes nothing — no clash and no abstention.</summary>
        None,

        /// <summary>The constraint is provably violated.</summary>
        Clash,

        /// <summary>The constraint could not be decided within the modelled subset.</summary>
        Undecided,

        /// <summary>The caller's oracle budget gate stopped the evaluation; the enclosing decision returns <see cref="DataConsistencyStatus.Undecided"/> and the caller's budget latch owns the outcome.</summary>
        Stopped,
    }

    /// <summary>One value-forcing demand carried at a node: the property it constrains, the demand concept, and the range each forced value lies in.</summary>
    /// <param name="Property">The demanding property IRI.</param>
    /// <param name="Concept">The demand concept, for the conflict core.</param>
    /// <param name="Range">The range the forced value lies in.</param>
    private readonly record struct ForcedDemand(Utf8String Property, AlcConcept Concept, OwlDataRange Range);

    /// <summary>One pooled POINT demand: the demand itself together with the single literal its singleton enumeration forces, paired so the value is read once and the demand stays available for the conflict core.</summary>
    /// <param name="Demand">The point demand.</param>
    /// <param name="Value">The literal the demand forces.</param>
    private readonly record struct PointDemand(ForcedDemand Demand, Literal Value);

    /// <summary>One provably-distinct value of a pooled point fold together with every point demand that forces it — the CARRIER set, since a value forced under two properties is a filler of both and may witness a counting demand through either.</summary>
    /// <param name="Value">The forced value, the fold's representative for this equivalence class.</param>
    /// <param name="Carriers">The point demands forcing this value; never empty.</param>
    private readonly record struct PooledValue(Literal Value, List<PointDemand> Carriers);

    /// <summary>The existential, counting, and universal demands on one data property at a node.</summary>
    private sealed class PropertyConstraints
    {
        /// <summary>The existential value demands.</summary>
        public List<AlcDataSome> Existentials { get; } = [];

        /// <summary>The universal value constraints every value must meet.</summary>
        public List<AlcDataAll> Universals { get; } = [];

        /// <summary>The minimum-cardinality counting demands.</summary>
        public List<AlcDataMinCard> MinCardinalities { get; } = [];

        /// <summary>The maximum-cardinality counting bounds — the anchors of the property's max slot.</summary>
        public List<AlcDataMaxCard> MaxCardinalities { get; } = [];
    }
}
