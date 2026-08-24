using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.El;

/// <summary>
/// Consequence-based OWL 2 EL classification: normalizes the TBox to the
/// EL normal forms and saturates the completion rules to fixpoint,
/// producing the subclass closure over named classes in polynomial time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Normal forms.</b> Every interpreted axiom reduces to
/// <c>A ⊑ B</c>, <c>A₁ ⊓ A₂ ⊑ B</c>, <c>A ⊑ ∃r.B</c>, <c>∃r.A ⊑ B</c>
/// over atoms (named classes, <c>⊤</c>, <c>⊥</c>, and fresh normalization
/// names), plus the role axioms <c>r ⊑ s</c> and <c>r₁∘r₂ ⊑ s</c>
/// (transitivity is <c>r∘r ⊑ r</c>; longer chains decompose with fresh
/// roles). Complex sides decompose monotonically with fresh atoms.
/// </para>
/// <para>
/// <b>Completion.</b> The saturation maintains the subsumer sets
/// <c>S(C)</c> and role edges <c>R(r)</c> with an explicit work queue —
/// the no-recursion discipline holds — firing the standard rules:
/// told subsumption, conjunction, existential introduction and
/// elimination, bottom propagation through role edges, role hierarchy,
/// and role composition.
/// </para>
/// <para>
/// <b>Coverage.</b> Single-property data existentials
/// (<c>DataSomeValuesFrom</c>/<c>DataHasValue</c>) are interpreted in BOTH positions. In
/// superclass or assertion position the occurrence is a value demand: the range's value-space
/// emptiness is decided once with the same checker the tableau uses, and an empty range tells its
/// carrier <c>⊑ ⊥</c>. On the subclass side, in an equivalence, or in a disjointness the
/// occurrence names a concept <c>∃d.R ⊑ F</c>, and a carrier whose own demands force a
/// <c>d</c>-value inside <c>R</c> — the entailment decided as the joint unsatisfiability of those
/// demands with <c>∀d.¬R</c>, against the same data-property box — is told <c>F</c>, so the
/// classes an interval definition subsumes are derived rather than delegated. A functional data
/// property whose pooled cone is reached by demands on two or more distinct classes is named
/// unsupported and delegated: a common subsumee inherits both demands and functionality forces
/// them onto one value, which the per-carrier decision does not test. Local and global reflexivity
/// (<c>ObjectHasSelf</c> and the <c>Reflexive</c> characteristic) are interpreted as a
/// reflexive role edge <c>(r, x, x)</c>. A symmetric role, an <c>InverseObjectProperties</c> pairing,
/// and a one-directional inverse sub-property (<c>r⁻ ⊑ s</c> / <c>s ⊑ r⁻</c>) mirror each asserted
/// ground edge <c>(a, b)</c> over a paired role with its reverse under the inverse role — a saturation
/// rule that fires on every edge however derived, gated to roles whose edges are confined to the
/// asserted ground graph (no existential, self-demand, or chain in their sub-role closure). An
/// inverse-role range or domain axiom reduces to a forward range or domain on the paired role
/// (<c>range(r⁻) = domain(r)</c> types the edge source, <c>domain(r⁻) = range(r)</c> types the edge
/// target), and inverse transitivity to forward transitivity — owner-independent writes the forward
/// rules fire unchanged. A functional role unions the two asserted successors of one individual,
/// and an inverse-functional role the two asserted predecessors, into the <c>SameIndividual</c>
/// union-find — a told identity that lets a <c>DifferentIndividuals</c> collision or a pooled
/// disjoint-type clash decide, gated to the same asserted ground graph. An asymmetric or irreflexive
/// role (<c>AsymmetricObjectProperty</c> / <c>IrreflexiveObjectProperty</c>) is decided over that same
/// asserted post-merge graph: a self-edge, or — for an asymmetric role — an edge and its reverse, over
/// the constrained role's sub-role closure decides the module inconsistent, and a told global reflexivity
/// under a constrained role (<c>Reflexive(s)</c> or <c>⊤ ⊑ ∃s.Self</c> with <c>s ⊑* r</c>) decides it
/// inconsistent outright; the same ground-only gate delegates a constrained role whose edges are not
/// confined to that graph. A role that is both symmetric-in-effect and asymmetric-constrained — itself, or
/// under an asymmetric super-role — is decided EMPTY in every model, its characteristics reduced to
/// <c>∃r.⊤ ⊑ ⊥</c>, so the module no longer delegates that combination. Both spellings of the six ground
/// characteristics are admitted: each inverse spelling is exactly a forward characteristic —
/// <c>Asymmetric(r⁻)</c>, <c>Irreflexive(r⁻)</c>, <c>Symmetric(r⁻)</c>, <c>Reflexive(r⁻)</c> on <c>r</c>, and
/// the functional pair swapping, <c>Functional(r⁻) ≡ InverseFunctional(r)</c>. A single-individual nominal
/// <c>{a}</c> is interpreted
/// as the individual node for <c>a</c> wherever the nominal stays an edge endpoint or a
/// negative-position concept: as the filler of an existential in a class assertion
/// (<c>ObjectHasValue(r, a)</c> or <c>∃r.{a}</c> via a one-individual <c>ObjectOneOf</c>, and the
/// inverse spellings <c>ObjectHasValue(r⁻, a)</c> and <c>∃r⁻.{a}</c>, which are that same edge with its
/// endpoints exchanged — the ground fact <c>(a, x) ∈ r</c>), as a
/// bare class assertion <c>x : {a}</c> (the told identity <c>x = a</c>, folded into the
/// <c>SameIndividual</c> union-find — so a <c>DifferentIndividuals</c> over individuals thereby
/// collapsed forces <c>⊑ ⊥</c>), on the subclass side (<c>∃r.{a} ⊑ B</c> as a left existential
/// keyed on the individual node, <c>∃r⁻.{a} ⊑ B</c> as the same left existential over the role's
/// synthetic mirror, or <c>{a} ⊑ B</c> as told typing of <c>a</c>), and as a
/// singleton operand of a disjointness. Constructs outside this calculus — value restrictions, a
/// multi-individual enumeration, data universals, a data existential over several properties or over a
/// reserved data property, and a data range the checker cannot decide — are not interpreted; each
/// occurrence is recorded on
/// <see cref="ElClassification.UnsupportedConstructs"/> (the undecided marker for an
/// abstaining range) so consumers see exactly what the closure does not account for,
/// and the coupled reasoner delegates. The classification is sound for what it interprets.
/// </para>
/// </remarks>
public static class ElClassifier
{
    /// <summary>
    /// Classifies the document's TBox.
    /// </summary>
    /// <param name="document">The structural ontology document.</param>
    /// <param name="cancellationToken">A token that aborts saturation between work items.</param>
    /// <returns>The classification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ElClassification Classify(OwlOntologyDocument document, CancellationToken cancellationToken = default)
    {
        return Classify(document, DatatypeRegistry.Empty, cancellationToken);
    }

    /// <summary>
    /// Classifies the document's TBox consulting a registered-datatype set at the concrete-domain leaves —
    /// the registry-carrying counterpart of <see cref="Classify(OwlOntologyDocument, CancellationToken)"/>.
    /// </summary>
    /// <param name="document">The structural ontology document.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts saturation between work items.</param>
    /// <returns>The classification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ElClassification Classify(OwlOntologyDocument document, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);

        ClassifierContext context = new()
        {
            Registry = registry,
        };

        context.Normalize(document);
        context.Saturate(cancellationToken);

        return context.BuildResult();
    }

    /// <summary>
    /// Decides a module's consistency by EL saturation and classifies its named
    /// classes in one pass — the EL fast-path of the pay-as-you-go reasoner.
    /// Seeds the ABox (class/role assertions, <c>SameIndividual</c>) so the
    /// verdict accounts for forced-empty individuals and the non-empty-domain
    /// requirement, not the TBox alone.
    /// </summary>
    /// <remarks>
    /// Sound and complete only for modules wholly within the EL⊥ fragment
    /// (conjunction, existential, bottom, role hierarchy, role chains, transitive
    /// roles) — the caller gates on that fragment with <c>ElModuleSurvey</c> and
    /// falls back to the tableau oracle for anything outside it.
    /// </remarks>
    /// <param name="axioms">The module's axioms.</param>
    /// <param name="cancellationToken">A token that aborts saturation between work items.</param>
    /// <returns>The module classification: the consistency verdict, the named-class subsumers, and the saturation telemetry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="axioms"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ElModuleClassification ClassifyModule(IReadOnlyList<OwlAxiom> axioms, CancellationToken cancellationToken = default)
    {
        return ClassifyModule(axioms, DatatypeRegistry.Empty, cancellationToken);
    }

    /// <summary>
    /// Decides a module's consistency by EL saturation and classifies its named classes in one pass,
    /// consulting a registered-datatype set at the concrete-domain leaves — the registry-carrying counterpart
    /// of <see cref="ClassifyModule(IReadOnlyList{OwlAxiom}, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// Runs the ground-identity completion loop around normalization and saturation. The saturation can
    /// derive an identity between two individuals — a live node told to be both — after the pre-intern
    /// consumers (the distinctness collision scan, the functional and inverse-functional endpoint union, and
    /// the asymmetric and irreflexive edge scans) have resolved their keys. A module carrying both that
    /// discovery surface and one of those consumers
    /// (<see cref="ClassifierContext.RequiresGroundIdentityCompletion"/>) therefore folds every discovered
    /// pair into the told-identity set exactly as a <c>SameIndividual</c> axiom states it and rebuilds the
    /// whole context from the raw axioms, so the consumers read an identity-complete union-find. A pass that
    /// has already derived <c>⊥</c> stands as it is: every subsumption it derived is entailed without the
    /// extra identities. Each rebuild merges at least two individual keys, which bounds the loop by the
    /// module's individual count less one; exceeding that structural bound records the saturation-restart
    /// marker on a fresh delegation, and no partial rebuild's result is read. A module whose gate is false,
    /// or whose saturation discovers no new identity, takes exactly one pass.
    /// </remarks>
    /// <param name="axioms">The module's axioms.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts saturation between work items.</param>
    /// <returns>The module classification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="axioms"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ElModuleClassification ClassifyModule(IReadOnlyList<OwlAxiom> axioms, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(axioms);
        ArgumentNullException.ThrowIfNull(registry);

        List<(Utf8String First, Utf8String Second)> discoveredIdentities = [];
        int rebuilds = 0;
        int rebuildCap = 0;

        while(true)
        {
            ClassifierContext context = new()
            {
                Registry = registry,
            };

            context.NormalizeModule(axioms, discoveredIdentities);
            //One EL-saturation bracket spans each rebuild round: it opens before
            //Saturate and closes in the finally, so the ElSaturation phase counts once
            //per round at every exit — the completed saturation and a cancellation or
            //any other exception thrown out of the saturation, whose seeding preamble
            //and worklist loop both run inside the bracket.
            long saturateStart = ReasoningInstrumentation.Begin();
            try
            {
                context.Saturate(cancellationToken);
            }
            finally
            {
                ReasoningInstrumentation.End(ReasoningPhase.ElSaturation, saturateStart);
            }

            if(rebuilds == 0)
            {
                rebuildCap = context.GroundIdentityRebuildCap;
            }

            //Three ways this pass is the whole decision: the module pairs no told nominal identity with a
            //pre-intern consumer, so nothing can read a stale union-find; the pass already derived ⊥, whose
            //derivation stands without the identities a rebuild would add; or the saturation discovered no
            //identity the union-find does not already state, which is the fixpoint.
            if(!context.RequiresGroundIdentityCompletion()
                || context.HasDerivedInconsistency()
                || !context.TryDiscoverGroundIdentities(discoveredIdentities))
            {
                return context.BuildModuleResult();
            }

            rebuilds++;
            if(rebuilds > rebuildCap)
            {
                ClassifierContext delegated = new()
                {
                    Registry = registry,
                };

                delegated.DelegateGroundIdentityRestart();

                return delegated.BuildModuleResult();
            }
        }
    }

    //The per-run state: atom and role interning, the normal-form indexes,
    //the saturation sets, and the work queue.
    private sealed class ClassifierContext
    {
        private const int Top = 0;

        private const int Bottom = 1;

        /// <summary>Interned atom names; fresh normalization atoms carry <c>null</c>.</summary>
        private List<Utf8String?> AtomNames { get; } = [new("http://www.w3.org/2002/07/owl#Thing"u8.ToArray()), new("http://www.w3.org/2002/07/owl#Nothing"u8.ToArray())];

        private Dictionary<Utf8String, int> AtomIds { get; } = [];

        /// <summary>Interned role names; fresh chain-decomposition roles carry <c>null</c>.</summary>
        private List<Utf8String?> RoleNames { get; } = [];

        private Dictionary<Utf8String, int> RoleIds { get; } = [];

        //Normal-form indexes.
        private Dictionary<int, List<int>> ToldSubsumptions { get; } = [];

        private Dictionary<(int First, int Second), List<int>> Conjunctions { get; } = [];

        /// <summary>Conjunction partners per atom: for A, the (other, conclusion) pairs of every A ⊓ other ⊑ conclusion.</summary>
        private Dictionary<int, List<(int Other, int Conclusion)>> ConjunctionsByAtom { get; } = [];

        private Dictionary<int, List<(int Role, int Filler)>> RightExistentials { get; } = [];

        private Dictionary<(int Role, int Filler), List<int>> LeftExistentials { get; } = [];

        private Dictionary<int, List<int>> RoleSubsumptions { get; } = [];

        /// <summary>Chains indexed by their FIRST role: (second, conclusion).</summary>
        private Dictionary<int, List<(int Second, int Conclusion)>> ChainsByFirst { get; } = [];

        /// <summary>Chains indexed by their SECOND role: (first, conclusion).</summary>
        private Dictionary<int, List<(int First, int Conclusion)>> ChainsBySecond { get; } = [];

        /// <summary>Range atoms per role: (C,D) ∈ R(r) puts each range atom of r into S(D).</summary>
        private Dictionary<int, List<int>> RangesByRole { get; } = [];

        /// <summary>Data demands per atom: each is a data property paired with a range a member of the atom must carry a value of. The property is kept — not dropped — so the §1.3 sidecar can pool functional demands, inherit super-property ranges, and check disjointness against the module <see cref="Box"/>; a jointly-empty demand set makes the atom unsatisfiable. Populated only for superclass/assertion-position data existentials, which the module survey gates. The collection is PER ATOM: demands that meet only through a subsumption between two distinct carriers are not pooled here, which is what <see cref="FenceMultiCarrierFunctionalPooling"/> fences.</summary>
        private Dictionary<int, List<DataDemand>> DataDemands { get; } = [];

        /// <summary>Left-position data existentials: each a data property, the range a value must lie in, and the fresh atom <see cref="NameLeft"/> named the occurrence with — the normal form <c>∃d.R ⊑ F</c>, whose subsuming rules carry the atom onward like any other left-named concept. <see cref="SeedDataRecognitions"/> reads the list against every demand carrier, telling a carrier whose own demands entail a <c>d</c>-value in <c>R</c> that it is <c>F</c>. Empty for every module carrying no subclass-, equivalence-, or disjointness-position data existential, where the classifier is byte-identical to the demand-only calculus.</summary>
        private List<LeftDataExistential> LeftDataExistentials { get; } = [];

        /// <summary>The module's data-property RBox — the sub-property closure, the functional properties, the disjoint pairs, and the asserted ranges — built once in <see cref="Normalize"/>/<see cref="NormalizeModule"/> and consumed by <see cref="SeedDataDemands"/>. Empty when the module carries no data-property axiom, so a demand reduces to its own range's emptiness exactly as before.</summary>
        private DataPropertyBox Box { get; set; } = DataPropertyBox.Empty;

        /// <summary>The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> when the host registered none. Set by the enclosing classifier entry before normalization.</summary>
        public DatatypeRegistry Registry { get; set; } = DatatypeRegistry.Empty;

        /// <summary>The told domain conclusions of the module's <c>DataPropertyDomain</c> axioms: each a data property paired with the atom of its named domain class. In <see cref="SeedDataDemands"/> an atom carrying a demand on that property — or on a sub-property of it, through the <see cref="Box"/> closure — is told it is the domain class.</summary>
        private List<(Utf8String Property, int Conclusion)> DataDomainConclusions { get; } = [];

        /// <summary>Self demands per atom: the roles on which a member of the atom must have a reflexive edge to itself (<c>ObjectHasSelf</c> on the superclass/assertion side, or every atom under a reflexive role via <see cref="Top"/>). Fired as a self-edge <c>(role, x, x)</c> when the atom enters a node's subsumer set.</summary>
        private Dictionary<int, List<int>> SelfDemands { get; } = [];

        /// <summary>Self eliminations per role: the conclusion atoms a genuine self-edge on the role licenses (<c>ObjectHasSelf(r) ⊑ B</c> on the subclass side). Fired only for an edge whose source equals its target, never an ordinary successor.</summary>
        private Dictionary<int, List<int>> SelfEliminations { get; } = [];

        /// <summary>Inverse role pairings: each role mapped to the roles whose extension is its reverse. <c>SymmetricObjectProperty(r)</c> pairs <c>r</c> with itself; <c>InverseObjectProperties(r, s)</c> pairs <c>r</c> with <c>s</c> and <c>s</c> with <c>r</c>. While an edge over a paired role is processed, its reverse is enqueued under each inverse role — the saturation mirror — so the rule fires on every edge however derived (asserted, hierarchy-promoted, composed, or itself a mirror), which is what makes inverse pairings that chain (<c>r⁻ = s</c>, <c>s⁻ = t</c>) and mixed symmetric/inverse roles complete. The ground-only gate confines every paired role to asserted ground edges, so the mirror never sees a shared-filler existential edge.</summary>
        private Dictionary<int, List<int>> InversePairs { get; } = [];

        /// <summary>The roles that receive a mirror edge — the target of some inverse pairing (<see cref="InversePairs"/> value). A functional or inverse-functional role here gains a non-asserted successor the pre-merge scan cannot see, so it is delegated; for the mutual <c>InverseObjectProperties</c> pairing every paired role is both a source and a target, and this coincides with the pairing keys, but a one-directional <c>r⁻ ⊑ s</c> makes only <c>s</c> a target.</summary>
        private HashSet<int> MirrorTargets { get; } = [];

        /// <summary>Each forward role whose inverse appears in a subclass-side existential mapped to its synthetic mirror role (<see cref="GetOrMintMirrorRole"/>), so repeated <c>∃r⁻</c> occurrences over one role share one mirror.</summary>
        private Dictionary<int, int> MirrorRoleForInverted { get; } = [];

        /// <summary>Each forward role whose inverse appears in a superclass-side existential mapped to its synthetic generator role (<see cref="GetOrMintGeneratorRole"/>), so repeated <c>∃r⁻</c> occurrences over one role share one generator. Kept separate from <see cref="MirrorRoleForInverted"/>: the mirror pairing is <c>r ⊑ mirror⁻</c> (the forward role's edges force mirror edges), the generator pairing is <c>g ⊑ r⁻</c> (the generator's edges force the forward role's edges), so the forward role — not a fresh internal role — becomes the mirror target here. This is also the key set the generator self/chain fence reads in the gate.</summary>
        private Dictionary<int, int> GeneratorRoleForInverted { get; } = [];

        /// <summary>
        /// The roles whose right existentials mint a per-owner witness instead of targeting the shared
        /// filler node: an existential over such a role gives each owner a distinct interned successor
        /// whose demand set is the owner's own demands plus its <c>∃r⁻.owner-core</c> decoration, so the
        /// inverse mirror over that successor stays owner-local and one owner's <c>⊥</c> cannot empty
        /// another's — inheriting the owner's demands keeps two owners that share a filler core from folding
        /// their onward successors, and the unique-ownership check (<see cref="MintOwnerByNode"/>) abstains
        /// the module when a fold would still occur. Populated by
        /// <see cref="TryAdmitInverseMinting"/> for a module whose mirrored roles bear only asserted ground
        /// edges and right existentials; empty for every inverse-free module, where each existential targets
        /// its plain filler and the classifier is byte-identical to the shared-filler calculus. The
        /// mint↔mirror pairing is the sole gate: a role mints iff its inverse mirror is admitted, so no
        /// shared-filler edge is ever mirrored. Minting alone (with no backward demand-consumer) is sound and
        /// complete for this admitted fragment because the survey delegates every inverse existential in
        /// class position, so no axiom demands a witness's <c>∃r⁻</c> provenance, the gate delegates any
        /// mirrored role bearing a self-demand or chain, and a range over a mirrored role reduces
        /// owner-independently (<see cref="ReduceMirrorRangesToDomains"/>) — so no witness is unsatisfiable
        /// through its provenance and the forward mirror already writes the concrete backward edge.
        /// Broadening the fragment to a chain or inverse existential over a mirrored role is the backward
        /// rule's (R-BACK's) work, not a precondition for the current admission.
        /// </summary>
        private HashSet<int> CoupledRoles { get; } = [];

        /// <summary>
        /// Whether the module is fold-safe: it carries no machinery that can write a position-dependent
        /// fact onto a folded witness, so every witness position sharing a raw intern key is bisimilar and
        /// accepting a cross-owner witness fold (the <see cref="MintOwnerByNode"/> abstention) is sound.
        /// Computed once by <see cref="ComputeFoldSafety"/> from normalization-time state ONLY — the told,
        /// existential, range, nominal, chain, self-elimination, and role-hierarchy indexes, never a
        /// saturation index — so the fence is a deterministic function of the normal form. False by default
        /// and for every module the fence does not clear (a fresh context per classification resets it, in
        /// lockstep with the other gate state); when true, the R-EXIST mint accepts a witness another owner
        /// already minted instead of abstaining. Clause F4 of the fence — not any admission precedence — is
        /// what excludes a chain or self-elimination interacting over a promoted super-role of a coupled or
        /// mirrored role.
        /// </summary>
        private bool FoldSafe { get; set; }

        /// <summary>
        /// Whether the module mints its witnesses on SHARED CONTENT KEYS: an existential over a coupled
        /// role interns its successor on the filler core and the role mark alone — no owner term, no
        /// inherited demand set — so every owner of one <c>(role, filler)</c> pair reaches ONE node, the
        /// canonical element of that content class serving them all at once. True exactly when the module
        /// carries a coupled role and no chain link or conclusion, self-demand, or self-elimination lies in
        /// the upward closure of <see cref="CoupledRoles"/> and <see cref="MirrorTargets"/> — the features
        /// that write a position-actual fact no content key records. Computed once by
        /// <see cref="ComputeWitnessRegime"/> from normalization-time state ONLY, false by default and for
        /// every module with no coupled role, and reset per classification with the other gate state. False
        /// is the per-owner regime: owner-inherited keys, the <see cref="MintOwnerByNode"/> ownership
        /// ledger, the <see cref="FoldSafe"/> fence, and both mint abstentions.
        /// </summary>
        private bool SharedWitnessKeys { get; set; }

        /// <summary>
        /// The backward-consumer roles: every role in the upward closure of <see cref="MirrorTargets"/> that
        /// is also a <see cref="LeftExistentials"/> key role. Every edge running from a minted witness to one
        /// of its owners is mirror-produced — the mint edge runs owner to witness, the inverse-pairing
        /// cascade is the only rule that reverses an edge's endpoints and enqueues under an
        /// <see cref="InversePairs"/> value (hence a <see cref="MirrorTargets"/> member), and upward
        /// promotion preserves endpoints inside the closure — so a left-existential deposit that travels
        /// witness-to-owner always carries a role from this set. That makes the set a lossless module-level
        /// pre-filter for <see cref="TryConsumeBackwardDemands"/> and its per-firing early-out: an empty set
        /// means no witness-to-owner deposit exists, the module never
        /// refines a witness, and it classifies exactly as a module with no backward consumer. Computed once
        /// with the fold-safety fence from normalization-time state only, and empty by default (a fresh
        /// context per classification resets it with the other gate state).
        /// </summary>
        private HashSet<int> BackwardConsumerRoles { get; } = [];

        /// <summary>
        /// Whether a self-elimination reaches a witness-carrying role: some <see cref="SelfEliminations"/>
        /// key lies in the upward closure of <see cref="CoupledRoles"/> and <see cref="MirrorTargets"/> —
        /// the same witness-reachable role set clause F4 of <see cref="ComputeFoldSafety"/> reads.
        /// Precomputed once with the fold-safety fence. Guards the R-EXIST mint: a cyclic self-reproducing
        /// existential that folds a witness onto its own owner commits that owner to a self-loop model, and a
        /// self-elimination can then fire on the fold's artifact self-edge and condemn a module whose true
        /// models need no self-loop, so a self-fold under a reachable self-elimination abstains the module to
        /// the general decider. A told self-demand on a real individual is a genuine self-edge and stays
        /// decided, and a module with no self-elimination in the closure is unaffected. False by default and
        /// reset per classification with the other gate state.
        /// </summary>
        private bool WitnessClosureBearsSelfElimination { get; set; }

        /// <summary>The roles asserted functional (<c>FunctionalObjectProperty</c>): each individual has at most one successor over the role, so two asserted successors of one individual are the same. Held for the ground-only gate; the union of the successors runs in the pre-merge pass.</summary>
        private HashSet<int> FunctionalRoles { get; } = [];

        /// <summary>The roles asserted inverse-functional (<c>InverseFunctionalObjectProperty</c>): each individual has at most one predecessor over the role, so two asserted predecessors of one individual are the same. Held for the ground-only gate; the union of the predecessors runs in the pre-merge pass.</summary>
        private HashSet<int> InverseFunctionalRoles { get; } = [];

        /// <summary>The roles asserted asymmetric (<c>AsymmetricObjectProperty</c>): an edge and its reverse over the role or its sub-roles cannot coexist, and a self-edge is its own reverse. Held for the ground-only gate; the clash scan runs over the asserted post-merge edges.</summary>
        private HashSet<int> AsymmetricRoles { get; } = [];

        /// <summary>The roles asserted irreflexive (<c>IrreflexiveObjectProperty</c>): no element bears a self-edge over the role or its sub-roles. Held for the ground-only gate; the clash scan runs over the asserted post-merge edges.</summary>
        private HashSet<int> IrreflexiveRoles { get; } = [];

        /// <summary>
        /// Whether the module asserts a <c>DifferentIndividuals</c> axiom. The distinctness collision scan
        /// (<see cref="SeedDistinctnessClash"/>) compares union-find representatives at normalize time, so
        /// the axiom is an identity consumer resolved strictly before interning — one of the inputs
        /// <see cref="RequiresGroundIdentityCompletion"/> weighs against a told nominal identity the merge
        /// can only discover at saturation. False by default and reset per classification with the other
        /// gate state.
        /// </summary>
        private bool HasDistinctnessAssertions { get; set; }

        //Saturation state.
        private Dictionary<int, HashSet<int>> Subsumers { get; } = [];

        /// <summary>Role edges by role, with per-source and per-target adjacency for the join rules.</summary>
        private Dictionary<int, HashSet<(int Source, int Target)>> Edges { get; } = [];

        private Dictionary<(int Role, int Source), List<int>> EdgesBySource { get; } = [];

        private Dictionary<(int Role, int Target), List<int>> EdgesByTarget { get; } = [];

        /// <summary>
        /// Per target node, the roles that carry at least one edge INTO it — the enumeration the
        /// left-existential join over incoming edges runs on, so the join costs the roles incident to the
        /// subject rather than every role the module ever used. The content of
        /// <c>IncomingRoles[target]</c> equals, as a set, exactly the roles whose
        /// <see cref="EdgesByTarget"/> key <c>(role, target)</c> exists: <see cref="EnqueueEdge"/> is the
        /// sole writer of both and appends the role here in the same branch that creates the
        /// <see cref="EdgesByTarget"/> list, so the append fires exactly once per <c>(role, target)</c> key
        /// and the two key sets stay in lockstep at every point of the saturation. Strictly append-only,
        /// like the edge indexes it mirrors, which makes the join's <see cref="EdgesByTarget"/> read an
        /// indexer read rather than a probe.
        /// </summary>
        private Dictionary<int, List<int>> IncomingRoles { get; } = [];

        /// <summary>
        /// The V-node registry: interns a <see cref="VNodeDescriptor"/> — a (core atom, backward-demand
        /// set) description — to a node id. A successor decorated with per-owner provenance by the
        /// inverse-existential rules interns to a fresh atom distinct from the shared filler class, while
        /// an undecorated successor (empty demand set) is the filler atom itself and never enters the
        /// registry. Empty for every module with no inverse-coupled existential, so those classify over
        /// the identical atom ids they always did and pay nothing.
        /// </summary>
        private Dictionary<VNodeDescriptor, int> NodeByDescr { get; } = [];

        /// <summary>The inverse of <see cref="NodeByDescr"/>: each minted node id mapped to its interned description, so a backward rule can grow a node's demand set and re-intern the strict superset.</summary>
        private Dictionary<int, VNodeDescriptor> DescrByNode { get; } = [];

        /// <summary>
        /// Each minted witness mapped to the owner that first minted it — the unique-ownership invariant's
        /// witness ledger. A decided module's witnesses each have exactly one creating owner (or are their
        /// own, the cyclic self-model), so a mirror edge points only at the true predecessor; a mint that
        /// returns another owner's witness (two distinct owners' demand sets coinciding under order-blind
        /// set union, from mutually recursive cross-core existentials) records an unsupported marker and the
        /// module abstains to the general decider — unless the module is <see cref="FoldSafe"/>, where no
        /// machinery can distinguish the folded positions and the fold is accepted.
        /// </summary>
        private Dictionary<int, int> MintOwnerByNode { get; } = [];

        /// <summary>
        /// The shared-regime mint-edge ledger: each shared witness mapped to the <c>(coupled role, owner)</c>
        /// pairs whose existential minted it, appended at both shared mint sites — the R-EXIST introduction
        /// and the backward refinement's re-point — before the mirror cascade sees the edge, and
        /// deduplicated per pair so a re-derivation adds nothing. It is the direction test of the shared
        /// regime, where the key carries no ownership decoration to read: an edge runs from a witness to one
        /// of its owners exactly when the ledger records that owner for the witness, and every ledger entry
        /// is a mint that genuinely happened. Empty in the per-owner regime and for every module with no
        /// coupled role, so every test over it is false there.
        /// </summary>
        private Dictionary<int, List<(int Role, int Owner)>> MintedFrom { get; } = [];

        private Queue<WorkItem> Work { get; } = new();

        /// <summary>The trigger atoms of one left-existential join, parallel to <see cref="BackwardTriggerConclusions"/>: the atom whose left-existential key licenses the conclusion at the same index. Filled by a join site and offered to <see cref="TryConsumeBackwardDemands"/> as one batch, then reused by the next join — the saturation runs one join at a time off the work queue, so one buffer per role in the batch suffices and no join allocates.</summary>
        private List<int> BackwardTriggerAtoms { get; } = [];

        /// <summary>The conclusions of one left-existential join, parallel to <see cref="BackwardTriggerAtoms"/>.</summary>
        private List<int> BackwardTriggerConclusions { get; } = [];

        /// <summary>The decorations of one refinement that the witness's key does not already record — the batch <see cref="CanonicalizeDemands(ImmutableArray{long}, IReadOnlyList{long})"/> folds into the refined key in one union.</summary>
        private List<long> BackwardNewDecorations { get; } = [];

        /// <summary>The conclusions of one refinement whose licensing decoration is new, parallel to <see cref="BackwardNewDecorations"/>: they are deposited on the refined node, while a conclusion whose decoration the key already records is deposited on the unrefined witness directly.</summary>
        private List<int> BackwardNewConclusions { get; } = [];

        /// <summary>The coupled roles carrying the minting edge from an owner to the witness a refinement refines — the edges the refinement re-points at the refined node.</summary>
        private List<int> BackwardMintingRoles { get; } = [];

        private List<string> Unsupported { get; } = [];

        /// <summary>The atoms that are user-named classes (classification output covers exactly these).</summary>
        private HashSet<int> NamedAtoms { get; } = [];

        /// <summary>Interned individual keys → their atom id, a space disjoint from <see cref="AtomIds"/> so OWL punning (one IRI as both class and individual) never conflates the two; used only on the module-consistency path.</summary>
        private Dictionary<Utf8String, int> IndividualIds { get; } = [];

        /// <summary>The atoms that stand for named individuals — the nodes whose forced <c>⊥</c> condemns module consistency. Populated on BOTH paths: the module path interns every ABox subject and edge endpoint, and every class-space nominal (<c>ObjectOneOf</c> in class position, on either side of an inclusion) interns its individual here on the TBox-classification path too, so a nominal-bearing document carries individual atoms with no ABox at all.</summary>
        private HashSet<int> IndividualAtoms { get; } = [];

        /// <summary>The <c>SameIndividual</c> union-find parent map over individual keys, mirroring the tableau's pre-merge so co-referent individuals collapse to one atom. Empty on the TBox-classification path.</summary>
        private Dictionary<Utf8String, Utf8String> Merges { get; } = [];

        /// <summary>The asserted ABox role edges to seed before saturation: <c>(role, source individual, target individual)</c>. Empty on the TBox-classification path.</summary>
        private List<(int Role, int Source, int Target)> AssertedEdges { get; } = [];

        /// <summary>
        /// The inhabitation (liveness) set: the nodes a model is forced to populate — <see cref="Top"/>
        /// (the non-empty-domain witness), every <see cref="IndividualAtoms"/> member, and every
        /// forward-edge successor of a live node. The reachability gate of the nominal merge: a live node's
        /// constraints may be pooled onto the real individual it is told to be, whereas a clash on a
        /// non-live class node means only that the class is empty. Seeded identically on both paths —
        /// <see cref="Saturate"/>'s seeding loop reads <see cref="IndividualAtoms"/> with no lane test — so
        /// a nominal-bearing document seeds its class-space nominals live before the first work item, and
        /// only a module carrying no individual atom at all starts with <see cref="Top"/> alone.
        /// </summary>
        private HashSet<int> Live { get; } = [];

        /// <summary>Per node, the individual atoms in its subsumer set — the nominals the node is told to be (<c>N ⊑ {a}</c>). Drives the gated merge onto the real individual once the node is live.</summary>
        private Dictionary<int, List<int>> NominalsAt { get; } = [];

        /// <summary>Per individual atom, the nodes whose subsumer set contains it — the inverse of <see cref="NominalsAt"/>, so a subsumer newly derived for an individual propagates to every node told to be it.</summary>
        private Dictionary<int, List<int>> NominalCarriers { get; } = [];

        /// <summary>Per node, its forward-edge successors across all roles. When liveness reaches a node, it must propagate to the successors of edges that already existed — a class node fires its existential edge from its own self-subsumer during the init loop, before the carrier is inhabited by a later-processed individual, so that edge predates the source becoming live.</summary>
        private Dictionary<int, List<int>> OutgoingTargets { get; } = [];

        /// <summary>The work items the saturation dequeued — the completion-rule applications, surfaced as decision telemetry.</summary>
        private long Processed { get; set; }

        public ClassifierContext()
        {
            //The pre-seeded ⊤ and ⊥ atoms must intern to their fixed
            //indices — every owl:Thing / owl:Nothing reference resolves to
            //them, never to a fresh atom.
            AtomIds[AtomNames[Top]!.Value] = Top;
            AtomIds[AtomNames[Bottom]!.Value] = Bottom;
        }

        private enum WorkKind
        {
            Subsumer,
            Edge,
            Liveness,
        }

        private readonly record struct WorkItem(WorkKind Kind, int Subject, int Atom, int Role, int Target);

        /// <summary>
        /// A V-node description: a core atom paired with the canonical (sorted, distinct) set of
        /// backward-demand decorations that separate one per-owner witness of an existential from
        /// another. Equality is structural over the core and the demand elements in order, so equal
        /// descriptions intern to the same node and the demand array — held canonical by its producer —
        /// is compared by value rather than by the reference identity an immutable-collection key would use.
        /// </summary>
        private readonly struct VNodeDescriptor : IEquatable<VNodeDescriptor>
        {
            /// <summary>Initialises a description from its core atom and its canonical demand set.</summary>
            /// <param name="core">The seed atom the node specialises.</param>
            /// <param name="demands">The canonical (sorted, distinct) demand decorations; never default or empty.</param>
            public VNodeDescriptor(int core, ImmutableArray<long> demands)
            {
                Debug.Assert(IsCanonical(demands), "A V-node description must carry a canonical (non-empty, strictly ascending) demand set so equal descriptions intern to one node.");
                Core = core;
                Demands = demands;
            }

            /// <summary>The seed atom the node is a specialisation of (a named filler, or <see cref="Top"/>).</summary>
            public int Core { get; }

            /// <summary>The canonical (sorted, distinct) backward-demand decorations.</summary>
            public ImmutableArray<long> Demands { get; }

            /// <summary>Whether another description has the same core and the same demand elements in order.</summary>
            /// <param name="other">The description to compare.</param>
            /// <returns><see langword="true"/> when the cores and demand sequences are equal.</returns>
            public bool Equals(VNodeDescriptor other)
            {
                if(Core != other.Core || Demands.Length != other.Demands.Length)
                {
                    return false;
                }

                for(int index = 0; index < Demands.Length; index++)
                {
                    if(Demands[index] != other.Demands[index])
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>Whether an object is an equal description.</summary>
            /// <param name="obj">The object to compare.</param>
            /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal <see cref="VNodeDescriptor"/>.</returns>
            public override bool Equals(object? obj) => obj is VNodeDescriptor other && Equals(other);

            /// <summary>Whether two descriptions have the same core and the same demand elements in order — the same relation <see cref="Equals(VNodeDescriptor)"/> decides, so the operator and the method never disagree.</summary>
            /// <param name="left">The left description.</param>
            /// <param name="right">The right description.</param>
            /// <returns><see langword="true"/> when the descriptions are equal.</returns>
            public static bool operator ==(VNodeDescriptor left, VNodeDescriptor right) => left.Equals(right);

            /// <summary>Whether two descriptions differ in their core or in any demand element — the negation of <see cref="Equals(VNodeDescriptor)"/>.</summary>
            /// <param name="left">The left description.</param>
            /// <param name="right">The right description.</param>
            /// <returns><see langword="true"/> when the descriptions are unequal.</returns>
            public static bool operator !=(VNodeDescriptor left, VNodeDescriptor right) => !left.Equals(right);

            /// <summary>A hash combining the core and every demand element, consistent with <see cref="Equals(VNodeDescriptor)"/>.</summary>
            /// <returns>The structural hash of the description.</returns>
            public override int GetHashCode()
            {
                HashCode hash = new();
                hash.Add(Core);
                foreach(long demand in Demands)
                {
                    hash.Add(demand);
                }

                return hash.ToHashCode();
            }

            /// <summary>Whether a demand array is canonical: non-empty and strictly ascending, hence sorted and duplicate-free — the form the interner's structural equality requires so equal demand sets fold to one node.</summary>
            /// <param name="demands">The demand array.</param>
            /// <returns><see langword="true"/> when the array is non-empty and strictly increasing.</returns>
            private static bool IsCanonical(ImmutableArray<long> demands)
            {
                if(demands.IsDefaultOrEmpty)
                {
                    return false;
                }

                for(int index = 1; index < demands.Length; index++)
                {
                    if(demands[index - 1] >= demands[index])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        //Normalization.

        /// <summary>Reduces the document's interpreted axioms to the EL normal forms — TBox classification, where the ABox is not seeded. Ends with the same ground-role-feature gate the module path runs: an inverse pairing (user-asserted or the synthetic mirror of a subclass-side inverse existential) either activates per-owner witness minting or records the unsupported marker and tears the mirror down — without the gate, a mirror over a shared filler node would leak one owner's backward clash to every owner of the same existential.</summary>
        public void Normalize(OwlOntologyDocument document)
        {
            Box = DataPropertyBox.Build(document.Axioms);
            foreach(OwlAxiom axiom in document.Axioms)
            {
                NormalizeAxiom(axiom);
            }

            GateGroundRoleFeatures();
        }

        /// <summary>
        /// Reduces one interpreted TBox/RBox axiom to the EL normal forms. ABox
        /// (class/role assertion, same/different individual), annotation, and
        /// data-property axioms are no-ops here — the module-consistency path
        /// (<see cref="NormalizeModule"/>) seeds the ABox separately.
        /// </summary>
        /// <param name="axiom">The axiom to reduce.</param>
        private void NormalizeAxiom(OwlAxiom axiom)
        {
            switch(axiom)
            {
                case OwlSubClassOfAxiom subClass:
                {
                    NormalizeInclusion(subClass.SubClass, subClass.SuperClass);
                    break;
                }
                case OwlEquivalentClassesAxiom equivalent:
                {
                    NormalizeInclusion(equivalent.First, equivalent.Second);
                    NormalizeInclusion(equivalent.Second, equivalent.First);
                    break;
                }
                case OwlDisjointClassesAxiom disjoint:
                {
                    for(int i = 0; i < disjoint.Operands.Count; i++)
                    {
                        for(int j = i + 1; j < disjoint.Operands.Count; j++)
                        {
                            NormalizeInclusion(new OwlObjectIntersectionOf([disjoint.Operands[i], disjoint.Operands[j]]), BottomReference);
                        }
                    }

                    break;
                }
                case OwlObjectPropertyDomainAxiom domain when !domain.Property.IsInverse:
                {
                    //∃r.⊤ ⊑ D, through the inclusion machinery so a
                    //complex domain decomposes normally.
                    NormalizeInclusion(new OwlObjectSomeValuesFrom(domain.Property, TopReference), domain.Domain);
                    break;
                }
                case OwlObjectPropertyRangeAxiom range when !range.Property.IsInverse:
                {
                    AddRoleRange(RoleOf(range.Property.Property), range.Range);
                    break;
                }
                case OwlObjectPropertyRangeAxiom { Property.IsInverse: true } inverseRange:
                {
                    //range(r⁻) = domain(r): an r⁻-target is an r-source, so the inverse role's range is
                    //a domain on the forward role — ∃r.⊤ ⊑ D, typing the edge source. A range concept
                    //is owner-independent, so it types the genuinely-role-bearing end and never a
                    //shared existential filler. Through the inclusion machinery so a complex range
                    //decomposes normally.
                    NormalizeInclusion(new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(inverseRange.Property.Property), TopReference), inverseRange.Range);
                    break;
                }
                case OwlObjectPropertyDomainAxiom { Property.IsInverse: true } inverseDomain:
                {
                    //domain(r⁻) = range(r): an r⁻-source is an r-target, so the inverse role's domain is
                    //a range on the forward role, typing the edge target through the per-edge range rule.
                    AddRoleRange(RoleOf(inverseDomain.Property.Property), inverseDomain.Domain);
                    break;
                }
                case OwlSubObjectPropertyOfAxiom subProperty when !subProperty.SubProperty.IsInverse && !subProperty.SuperProperty.IsInverse:
                {
                    AddRoleSubsumption(RoleOf(subProperty.SubProperty.Property), RoleOf(subProperty.SuperProperty.Property));
                    break;
                }
                case (OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: true, SuperProperty.IsInverse: false } inverseSubProperty):
                {
                    //r⁻ ⊑ s: every r-edge (a, b) forces the reverse s-edge (b, a), a one-directional
                    //case of the inverse mirror (InverseObjectProperties adds both directions).
                    AddInversePair(RoleOf(inverseSubProperty.SubProperty.Property), RoleOf(inverseSubProperty.SuperProperty.Property));
                    break;
                }
                case (OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: false, SuperProperty.IsInverse: true } subPropertyInverse):
                {
                    //s ⊑ r⁻: every s-edge (a, b) forces the reverse r-edge (b, a).
                    AddInversePair(RoleOf(subPropertyInverse.SubProperty.Property), RoleOf(subPropertyInverse.SuperProperty.Property));
                    break;
                }
                case OwlPropertyChainAxiom chain:
                {
                    NormalizeChain(chain);
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Transitive } transitive:
                {
                    //A role is transitive exactly when its inverse is, so both spellings compose the
                    //same underlying role with itself.
                    int role = RoleOf(transitive.Property.Property);
                    AddChain(role, role, role);
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Reflexive } reflexive:
                {
                    //Global reflexivity is ⊤ ⊑ ∃r.Self: every node has a self-edge. Demanding
                    //it on Top seeds the self-edge on every atom the saturation reaches, since
                    //⊤ enters every atom's subsumer set in the init loop. A role is reflexive exactly
                    //when its inverse is, so both spellings demand the same self-edge on the same
                    //underlying role.
                    AddSelfDemand(Top, RoleOf(reflexive.Property.Property));
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Symmetric } symmetric:
                {
                    //A symmetric role is its own inverse. The saturation mirror and the ground-only
                    //safety gate run during/after normalization, once every edge-generating index is
                    //populated. A role is symmetric exactly when its inverse is, so both spellings
                    //self-pair the same underlying role.
                    int symmetricRole = RoleOf(symmetric.Property.Property);
                    AddInversePair(symmetricRole, symmetricRole);
                    break;
                }
                case (OwlInverseObjectPropertiesAxiom { First.IsInverse: false, Second.IsInverse: false } inverse):
                {
                    //InverseObjectProperties(r, s) makes each role's extension the reverse of the
                    //other's; the saturation mirror seeds the reverse of every edge over either under
                    //the other, gated to the asserted ground graph.
                    int first = RoleOf(inverse.First.Property);
                    int second = RoleOf(inverse.Second.Property);
                    AddInversePair(first, second);
                    AddInversePair(second, first);
                    break;
                }
                case (OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Functional } functional) when !functional.Property.IsInverse:
                {
                    //A functional role's successor union runs in the pre-merge pass; the role id is held
                    //for the ground-only gate, which delegates if its successors are not all asserted.
                    FunctionalRoles.Add(RoleOf(functional.Property.Property));
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Functional, Property.IsInverse: true } functionalInverse:
                {
                    //Functional(r⁻) bounds each element to one r⁻-successor — one r-PREDECESSOR — so it IS
                    //inverse-functionality on r and registers the swapped set.
                    InverseFunctionalRoles.Add(RoleOf(functionalInverse.Property.Property));
                    break;
                }
                case (OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional } inverseFunctional) when !inverseFunctional.Property.IsInverse:
                {
                    InverseFunctionalRoles.Add(RoleOf(inverseFunctional.Property.Property));
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional, Property.IsInverse: true } inverseFunctionalInverse:
                {
                    //InverseFunctional(r⁻) bounds each element to one r⁻-predecessor — one r-SUCCESSOR — so
                    //it IS functionality on r and registers the swapped set.
                    FunctionalRoles.Add(RoleOf(inverseFunctionalInverse.Property.Property));
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Asymmetric } asymmetric:
                {
                    //An asymmetric role forbids an edge's reverse over its sub-role closure and every
                    //self-edge; the role id is held for the ground-only gate, which decides the asserted
                    //post-merge edges and delegates if a non-asserted or mirrored edge could arise. A role
                    //is asymmetric exactly when its inverse is, so both spellings constrain the same
                    //underlying role.
                    AsymmetricRoles.Add(RoleOf(asymmetric.Property.Property));
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Irreflexive } irreflexive:
                {
                    //An irreflexive role forbids a self-edge over its sub-role closure; the role id is held
                    //for the ground-only gate, which decides the asserted post-merge edges and delegates if
                    //a non-asserted or mirrored edge could arise. A role is irreflexive exactly when its
                    //inverse is, so both spellings constrain the same underlying role.
                    IrreflexiveRoles.Add(RoleOf(irreflexive.Property.Property));
                    break;
                }
                case OwlEquivalentObjectPropertiesAxiom equivalentProperties when !equivalentProperties.First.IsInverse && !equivalentProperties.Second.IsInverse:
                {
                    int first = RoleOf(equivalentProperties.First.Property);
                    int second = RoleOf(equivalentProperties.Second.Property);
                    AddRoleSubsumption(first, second);
                    AddRoleSubsumption(second, first);
                    break;
                }
                case OwlDeclarationAxiom { Kind: OwlEntityKind.Class } declaration:
                {
                    NamedAtoms.Add(AtomOf(declaration.Entity.Iri));
                    break;
                }
                case OwlDataPropertyDomainAxiom dataDomain when !IsReservedDataProperty(dataDomain.Property.Iri) && dataDomain.Domain is OwlClassReference dataDomainClass:
                {
                    //DataPropertyDomain(dp, C) is the told edge ∃dp.⊤ ⊑ C over the
                    //admitted data existentials: a class carrying a dp-demand — or a
                    //sub-property demand, through the box closure — is told it is C.
                    //Fired in SeedDataDemands, where the demand atoms are known.
                    DataDomainConclusions.Add((dataDomain.Property.Iri, AtomOf(dataDomainClass.Class.Iri)));
                    break;
                }
                case OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                    or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom or OwlClassAssertionAxiom
                    or OwlObjectPropertyAssertionAxiom or OwlDataPropertyAssertionAxiom or OwlSameIndividualAxiom
                    or OwlDifferentIndividualsAxiom or OwlNegativeObjectPropertyAssertionAxiom or OwlNegativeDataPropertyAssertionAxiom
                    or OwlSubDataPropertyOfAxiom or OwlEquivalentDataPropertiesAxiom or OwlDisjointDataPropertiesAxiom
                    or OwlDataPropertyRangeAxiom or OwlFunctionalDataPropertyAxiom:
                {
                    //ABox, annotation, and verdict-neutral data-property axioms do
                    //not participate in class subsumption directly. The five
                    //data-property RBox types shape the concrete-domain leaves
                    //through the module Box, consulted in SeedDataDemands.
                    break;
                }
                default:
                {
                    Unsupported.Add($"{axiom.GetType().Name} is outside the EL classification calculus.");
                    break;
                }
            }
        }

        /// <summary>
        /// Reduces a module's axioms to the EL normal forms AND seeds its ABox —
        /// the module-consistency path. <c>SameIndividual</c> pre-merges its
        /// individuals union-find style (mirroring the tableau's pre-pass) so
        /// co-referent individuals collapse to one atom; <c>ClassAssertion</c>
        /// adds told types to the individual; <c>ObjectPropertyAssertion</c>
        /// records an asserted edge; <c>DifferentIndividuals</c> is vacuous in
        /// EL⊥. Every other axiom flows through <see cref="NormalizeAxiom"/>.
        /// </summary>
        /// <param name="axioms">The module's axioms.</param>
        /// <param name="discoveredIdentities">The identities a previous saturation derived over this module's individuals, folded exactly as a <c>SameIndividual</c> axiom states them so the pre-intern consumers read an identity-complete union-find; empty on the first pass.</param>
        public void NormalizeModule(IReadOnlyList<OwlAxiom> axioms, IReadOnlyList<(Utf8String First, Utf8String Second)> discoveredIdentities)
        {
            Box = DataPropertyBox.Build(axioms);

            //SameIndividual pre-merge: a tiny union-find over individual keys,
            //the same pre-pass the tableau runs so all assertions and edges on
            //co-referent individuals accumulate on one node. A singleton nominal
            //the ground-spine walk reports is the told identity of its anchor —
            //the asserted subject for a nominal on the asserted class's own
            //conjunct spine (x : {a}, x : D ⊓ {a}), the first sibling for two
            //nominals sharing a grounded filler spine — so it joins the same
            //union-find. Both ends of every fold resolve through the union-find as
            //the fold is written, so an assertion carrying several nominals chains
            //them onto one representative instead of overwriting the earlier fold
            //with the later one.
            List<(RdfTerm? Anchor, RdfTerm Nominal)> identities = [];
            List<(NamedNode Role, RdfTerm? Source, RdfTerm? Target)> groundEdges = [];
            List<(RdfTerm? Anchor, OwlClassExpression Element)> typings = [];
            foreach((Utf8String discoveredFirst, Utf8String discoveredSecond) in discoveredIdentities)
            {
                Merges[FindKey(discoveredFirst)] = FindKey(discoveredSecond);
            }

            foreach(OwlAxiom axiom in axioms)
            {
                if(axiom is OwlSameIndividualAxiom same
                    && TryIndividualKey(same.First, out Utf8String first)
                    && TryIndividualKey(same.Second, out Utf8String second))
                {
                    Merges[FindKey(first)] = FindKey(second);
                }
                else if(axiom is OwlClassAssertionAxiom assertion && TryIndividualKey(assertion.Individual, out Utf8String subject))
                {
                    identities.Clear();
                    groundEdges.Clear();
                    typings.Clear();
                    GroundSpineWalk(assertion.Class, identities, groundEdges, typings);
                    foreach((RdfTerm? anchor, RdfTerm nominal) in identities)
                    {
                        if(TryAnchorKey(anchor, subject, out Utf8String anchorKey) && TryIndividualKey(nominal, out Utf8String nominalKey))
                        {
                            Merges[FindKey(anchorKey)] = FindKey(nominalKey);
                        }
                    }
                }
            }

            //Functional collapse: a Functional/InverseFunctional role makes two asserted successors
            //(resp. predecessors) of one individual the same, a told identity the union-find above did
            //not state. Run it here, after the stated identities and before the interning loop, so
            //every IndividualAtomOf(FindKey(...)) below lands on the collapsed representative and the
            //distinctness/disjointness clashes fire with no extra machinery. The ground-only gate
            //(after normalization) delegates the module if the collapse could have missed a successor.
            SeedFunctionalMerges(axioms);

            foreach(OwlAxiom axiom in axioms)
            {
                switch(axiom)
                {
                    case OwlClassAssertionAxiom { Class: OwlObjectOneOf { Individuals.Count: 1 } singletonNominal } bareNominal
                        when TryIndividualKey(bareNominal.Individual, out _) && TryIndividualKey(singletonNominal.Individuals[0], out _):
                    {
                        //A bare singleton nominal assertion x : {a} was folded into the
                        //SameIndividual union-find in the pre-merge pass; nothing more to do.
                        break;
                    }
                    case OwlClassAssertionAxiom assertion when TryIndividualKey(assertion.Individual, out Utf8String individual):
                    {
                        AssertType(IndividualAtomOf(FindKey(individual)), assertion.Class);
                        break;
                    }
                    case OwlObjectPropertyAssertionAxiom roleAssertion
                        when TryIndividualKey(roleAssertion.Source, out Utf8String source) && TryIndividualKey(roleAssertion.Target, out Utf8String target):
                    {
                        AssertedEdges.Add((RoleOf(roleAssertion.Property), IndividualAtomOf(FindKey(source)), IndividualAtomOf(FindKey(target))));
                        break;
                    }
                    case OwlSameIndividualAxiom:
                    {
                        //Handled in the pre-merge union-find above.
                        break;
                    }
                    case OwlDifferentIndividualsAxiom different:
                    {
                        //EL⊥ otherwise drops DifferentIndividuals — sound only while no
                        //construct can force two individuals equal. A told identity
                        //(SameIndividual, or a bare nominal x : {a}) reactivates it as a
                        //clash source: two distinct-asserted individuals that the pre-merge
                        //collapsed to one node are an unsatisfiable distinctness. The scan
                        //reads the pre-intern union-find, so the axiom is also recorded as
                        //an identity consumer for the ground-identity completion gate.
                        HasDistinctnessAssertions = true;
                        SeedDistinctnessClash(different);
                        break;
                    }
                    default:
                    {
                        NormalizeAxiom(axiom);
                        break;
                    }
                }
            }

            GateGroundRoleFeatures();
        }

        /// <summary>Resolves a ground-spine anchor to its individual key: the walk's root subject for the anchor the walk reports as absent, and the nominal's own key for an anchor the walk descended onto.</summary>
        /// <param name="anchor">The reported anchor, or <see langword="null"/> for the walk's root subject.</param>
        /// <param name="subject">The walk's root subject key.</param>
        /// <param name="key">The resolved key.</param>
        /// <returns><see langword="false"/> for an anchor term that is not an individual.</returns>
        private static bool TryAnchorKey(RdfTerm? anchor, Utf8String subject, out Utf8String key)
        {
            if(anchor is null)
            {
                key = subject;

                return true;
            }

            return TryIndividualKey(anchor, out key);
        }

        /// <summary>
        /// Gates the inverse pairings and functional collapses on the ground-only safety condition once
        /// normalization has populated every edge-generating index. The saturation mirror and the
        /// functional successor union are sound only while every paired or functional role's edges are
        /// confined to asserted ground edges between concrete individuals; a role the saturation could
        /// give a non-asserted edge — a right existential, a self-demand (local or global reflexivity),
        /// or a chain link or conclusion, anywhere in its sub-role closure (edges promote to
        /// super-roles) — would feed a shared-filler or composed edge into the mirror or hide a
        /// successor from the union. A functional role that is also symmetric or inverse-paired
        /// (anywhere in its sub-role closure) is delegated too: the mirror seeds successors the
        /// pre-merge successor scan cannot see. An unsafe feature records an unsupported marker, so the
        /// coupled reasoner delegates rather than trust a contaminating mirror or an incomplete union.
        /// </summary>
        private void GateGroundRoleFeatures()
        {
            if(InversePairs.Count == 0 && FunctionalRoles.Count == 0 && InverseFunctionalRoles.Count == 0 && AsymmetricRoles.Count == 0 && IrreflexiveRoles.Count == 0)
            {
                return;
            }

            ReduceForcedEmptyRoles();

            HashSet<int> edgeGenerating = ComputeEdgeGeneratingRoles();
            Dictionary<int, List<int>> subRolesBySuper = BuildSubRolesBySuper();

            if(InversePairs.Count > 0)
            {
                if(!TryAdmitInverseMinting(edgeGenerating, subRolesBySuper))
                {
                    DelegateGroundRoleFeatures("An inverse or symmetric role bearing an edge the witness mint cannot reproduce is outside the EL classification calculus.");

                    return;
                }

                ReduceMirrorRangesToDomains();

                if(CoupledRoles.Count > 0)
                {
                    ComputeWitnessRegime();
                }
            }

            if(FunctionalRoles.Count > 0 || InverseFunctionalRoles.Count > 0)
            {
                foreach(int role in FunctionalRoles)
                {
                    if(IsUnsafeGroundRole(role, edgeGenerating, subRolesBySuper))
                    {
                        DelegateGroundRoleFeatures("A functional role with a non-asserted successor is outside the EL classification calculus.");

                        return;
                    }
                }

                foreach(int role in InverseFunctionalRoles)
                {
                    if(IsUnsafeGroundRole(role, edgeGenerating, subRolesBySuper))
                    {
                        DelegateGroundRoleFeatures("An inverse-functional role with a non-asserted predecessor is outside the EL classification calculus.");

                        return;
                    }
                }
            }

            //The asymmetric/irreflexive ground-graph tier, in the order told-check -> scan -> gate: a clash
            //found on told axioms or asserted edges is entailed regardless of the unsafe features the gate
            //abstains on, so deciding INCONSISTENT before the gate decides strictly more. A module the
            //existing inverse/functional gates already delegated returned above and never reaches here — a
            //recorded residual, not a restructuring of those gates.
            if(AsymmetricRoles.Count == 0 && IrreflexiveRoles.Count == 0)
            {
                return;
            }

            if(TryDecideToldReflexivityClash())
            {
                return;
            }

            if(TryDecideAssertedEdgeClash(subRolesBySuper))
            {
                return;
            }

            GateConstrainedGroundRoles(edgeGenerating, subRolesBySuper);
        }

        /// <summary>
        /// Reduces every role that is both symmetric-in-effect and asymmetric-constrained to the empty role.
        /// A role <c>s</c> self-paired in <see cref="InversePairs"/> (the index every symmetric spelling
        /// funnels into) whose <see cref="UpwardRoleClosure"/> meets <see cref="AsymmetricRoles"/> — itself,
        /// or an asymmetric super-role an s-edge promotes into — has an empty extension in every model: any
        /// <c>s(x, y)</c> forces <c>s(y, x)</c> by symmetry, both promote to the asymmetric role, and the
        /// reverse pair violates asymmetry. So <c>M</c> is per-model equivalent to
        /// <c>M \ {Symmetric(s), Asymmetric(s), Irreflexive(s)} ∪ {∃s.⊤ ⊑ ⊥}</c>: every model of <c>M</c>
        /// has <c>s = ∅</c> and models the seeded left existential, and every model of the rewrite has no
        /// s-edge, so <c>Symmetric(s)</c>, <c>Asymmetric(s)</c>, <c>Irreflexive(s)</c> hold vacuously. The
        /// seeded axiom is therefore ENTAILED by <c>M</c>, so the rewrite only ever derives entailed
        /// conclusions even on a module that later delegates. <c>∃s.⊤ ⊑ ⊥</c> is a complete encoding
        /// because every processed s-edge — asserted, hierarchy-promoted, minted, mirrored, chain-composed,
        /// or self-demanded — fires the left existential on its source (⊤ is in every subsumer set), and
        /// sub-role edges promote into <c>s</c>, so the whole downward closure is caught: there is no legal
        /// edge configuration on an empty role to distinguish, hence no gate is needed and the vacuous
        /// asymmetric/irreflexive registrations on <c>s</c> can leave the constrained sets. Only <c>s</c>
        /// itself is unconstrained: an asymmetric super-role <c>r ≠ s</c> keeps its constraint, since its
        /// other sub-roles still need the scan and gate. The direction is load-bearing — symmetry on a
        /// super-role with asymmetry on a sub-role forces nothing (super-edges do not descend), so the
        /// closure runs UPWARD from the self-paired role. Roles are collected before any index is mutated,
        /// and <see cref="MirrorTargets"/> is rebuilt from the surviving pairings only when a role was
        /// rewritten, so a module carrying no self-pairing or no asymmetric role takes the byte-identical
        /// path with no mutation. Iteration throughout, no recursion.
        /// </summary>
        private void ReduceForcedEmptyRoles()
        {
            if(AsymmetricRoles.Count == 0 || InversePairs.Count == 0)
            {
                return;
            }

            List<int> forcedEmpty = [];
            foreach(KeyValuePair<int, List<int>> entry in InversePairs)
            {
                int role = entry.Key;
                if(!entry.Value.Contains(role))
                {
                    continue;
                }

                foreach(int super in UpwardRoleClosure(role))
                {
                    if(AsymmetricRoles.Contains(super))
                    {
                        forcedEmpty.Add(role);

                        break;
                    }
                }
            }

            if(forcedEmpty.Count == 0)
            {
                return;
            }

            foreach(int role in forcedEmpty)
            {
                AddLeftExistential(role, Top, Bottom);

                List<int> pairing = InversePairs[role];
                pairing.Remove(role);
                if(pairing.Count == 0)
                {
                    InversePairs.Remove(role);
                }

                AsymmetricRoles.Remove(role);
                IrreflexiveRoles.Remove(role);
            }

            MirrorTargets.Clear();
            foreach(List<int> remaining in InversePairs.Values)
            {
                MirrorTargets.UnionWith(remaining);
            }
        }

        /// <summary>
        /// Decides the module inconsistent when a told global reflexivity forces a self-edge a constrained
        /// role forbids: a role in <see cref="SelfDemands"/> under <see cref="Top"/> — the register of
        /// <c>ReflexiveObjectProperty</c> and a superclass <c>⊤ ⊑ ∃s.Self</c> — whose upward
        /// role closure meets <see cref="AsymmetricRoles"/> or <see cref="IrreflexiveRoles"/>. Reflexivity on
        /// <c>s</c> puts a self-edge on every element of the non-empty domain, and a self-edge over <c>s</c>
        /// is a self-edge over its super-roles (an edge promotes to its super-roles), which irreflexivity —
        /// and asymmetry, which implies it — forbid. Only the upward closure participates: reflexivity forces
        /// self-edges on <c>s</c> and thus on every SUPER-role of <c>s</c>, nothing on its sub-roles or
        /// siblings. Seeds <c>⊤ ⊑ ⊥</c> so the verdict machinery condemns the module on both
        /// the module and classification paths. A class-level self-demand (<c>B ⊑ ∃s.Self</c>)
        /// needs an inhabited carrier and is edge-generating, so the gate delegates it instead of this
        /// told-decision.
        /// </summary>
        /// <returns><see langword="true"/> when a told reflexivity clash was seeded.</returns>
        private bool TryDecideToldReflexivityClash()
        {
            if(!SelfDemands.TryGetValue(Top, out List<int>? reflexiveRoles))
            {
                return false;
            }

            foreach(int reflexiveRole in reflexiveRoles)
            {
                foreach(int super in UpwardRoleClosure(reflexiveRole))
                {
                    if(AsymmetricRoles.Contains(super) || IrreflexiveRoles.Contains(super))
                    {
                        AddToldSubsumption(Top, Bottom);

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Decides the module inconsistent when the asserted post-merge ground graph violates a constrained
        /// role: a self-edge over a role in the downward sub-role closure of an asymmetric or irreflexive
        /// role, or — for an asymmetric role — an edge and its reverse over that closure. The endpoints are
        /// post-merge individual atoms (the <c>SameIndividual</c> pre-merge, the bare-nominal folds, and the
        /// functional collapse have already run), so a merge-created self-edge or reverse pair is seen. An
        /// edge over a sub-role is an edge over the constrained super-role, so the constraint bites on the
        /// whole downward closure; a super-role edge does not violate a sub-role constraint, which the
        /// closure direction encodes. Seeds <c>source ⊑ ⊥</c> on the first clash — the source is a
        /// live individual atom, the <see cref="SeedDistinctnessClash"/> pattern — and the verdict machinery
        /// condemns the module. A duplicate edge <c>(a, b)</c>, <c>(a, b)</c> is ordered-pair-set-absorbed and
        /// never a clash; only a genuine reverse <c>(b, a)</c> is.
        /// </summary>
        /// <param name="subRolesBySuper">The super-role to direct-sub-roles map from <see cref="BuildSubRolesBySuper"/>.</param>
        /// <returns><see langword="true"/> when an asserted-edge clash was seeded.</returns>
        private bool TryDecideAssertedEdgeClash(Dictionary<int, List<int>> subRolesBySuper)
        {
            if(AssertedEdges.Count == 0)
            {
                return false;
            }

            //The roles whose edges feed any constrained role (either characteristic) — the self-edge scan,
            //since asymmetry implies irreflexivity — and the per-edge-role map to the asymmetric roles it
            //feeds, for the reverse-pair scan.
            HashSet<int> feedsConstrained = [];
            Dictionary<int, List<int>> asymmetricFedBy = [];
            foreach(int constrained in AsymmetricRoles)
            {
                foreach(int subRole in SubRoleClosure(constrained, subRolesBySuper))
                {
                    feedsConstrained.Add(subRole);
                    AddToList(asymmetricFedBy, subRole, constrained);
                }
            }

            foreach(int constrained in IrreflexiveRoles)
            {
                feedsConstrained.UnionWith(SubRoleClosure(constrained, subRolesBySuper));
            }

            Dictionary<int, HashSet<(int Source, int Target)>> pairsByAsymmetric = [];
            foreach((int role, int source, int target) in AssertedEdges)
            {
                if(source == target && feedsConstrained.Contains(role))
                {
                    AddToldSubsumption(source, Bottom);

                    return true;
                }

                if(!asymmetricFedBy.TryGetValue(role, out List<int>? constrainedRoles))
                {
                    continue;
                }

                foreach(int constrained in constrainedRoles)
                {
                    if(!pairsByAsymmetric.TryGetValue(constrained, out HashSet<(int Source, int Target)>? pairs))
                    {
                        pairs = [];
                        pairsByAsymmetric[constrained] = pairs;
                    }

                    if(pairs.Contains((target, source)))
                    {
                        AddToldSubsumption(source, Bottom);

                        return true;
                    }

                    pairs.Add((source, target));
                }
            }

            return false;
        }

        /// <summary>
        /// Delegates the module when a constrained role's edges are not confined to the asserted ground
        /// graph — it is edge-generating (a right existential, a self-demand, a chain), or a role in its
        /// reflexive-transitive sub-role closure is a mirror target (a symmetric, inverse, or generator
        /// pairing). There the saturation can add a self-edge or a reverse edge the asserted-edge scan never
        /// saw, outside the EL classification calculus, so the coupled reasoner falls back to the tableau.
        /// Runs last, after the told check and scan found no entailed clash.
        /// </summary>
        /// <param name="edgeGenerating">The roles that can receive a non-asserted edge (<see cref="ComputeEdgeGeneratingRoles"/>).</param>
        /// <param name="subRolesBySuper">The super-role to direct-sub-roles map from <see cref="BuildSubRolesBySuper"/>.</param>
        private void GateConstrainedGroundRoles(HashSet<int> edgeGenerating, Dictionary<int, List<int>> subRolesBySuper)
        {
            foreach(int role in AsymmetricRoles)
            {
                if(IsUnsafeGroundRole(role, edgeGenerating, subRolesBySuper))
                {
                    DelegateGroundRoleFeatures("An asymmetric role with a non-asserted or mirrored edge is outside the EL classification calculus.");

                    return;
                }
            }

            foreach(int role in IrreflexiveRoles)
            {
                if(IsUnsafeGroundRole(role, edgeGenerating, subRolesBySuper))
                {
                    DelegateGroundRoleFeatures("An irreflexive role with a non-asserted or mirrored edge is outside the EL classification calculus.");

                    return;
                }
            }
        }

        /// <summary>Records the ground-role-feature delegation marker and tears down the inverse mirror and its paired per-owner minting together, so the coupled reasoner delegates the whole module to the tableau and the classifier falls back to the byte-identical shared-filler path. Clearing <see cref="CoupledRoles"/> in lockstep with <see cref="InversePairs"/> keeps the invariant that a role mints iff its mirror is admitted: a delegated module never mints a witness whose owner-local mirror is gone. <see cref="GeneratorRoleForInverted"/> is left uncleared: its pairing dies with <see cref="InversePairs"/>, so a generator existential <c>A ⊑ ∃g.C</c> becomes an inert-but-sound edge onto the plain filler <c>C</c> — no left existential, range, or chain ever targets a generator, and bottom back-propagation through the g-edge derives only the correct <c>C ⊑ ⊥ ⇒ A ⊑ ⊥</c>.</summary>
        /// <param name="reason">The unsupported-construct note.</param>
        private void DelegateGroundRoleFeatures(string reason)
        {
            Unsupported.Add(reason);
            InversePairs.Clear();
            CoupledRoles.Clear();
        }

        /// <summary>
        /// Tries to admit the module's inverse-paired roles for per-owner witness minting, populating
        /// <see cref="CoupledRoles"/> on success. Every mirrored role — an inverse-paired role and every
        /// sub-role whose edge promotes up to one — must bear only edges the per-owner mint reproduces:
        /// asserted ground edges (both endpoints concrete individuals) and right existentials (each owner
        /// gets a distinct interned witness). A self-demand puts a self-edge and a chain a composed edge
        /// under the mirror that the mint does not decorate per owner, so a mirrored role bearing either
        /// delegates the whole module. A range on a mirrored role does NOT delegate: a range is an
        /// owner-independent constraint — every mirror edge's target is the mirrored edge's genuine
        /// source, so the range reduces to a domain on the source role
        /// (<see cref="ReduceMirrorRangesToDomains"/>), and a range on a minting role itself types each
        /// per-owner witness through the per-edge range rule over the rewritten fresh successors.
        /// Subclass-position inverse existentials ride the synthetic-mirror reduction and
        /// superclass-position ones are reduced at normalization to forward existentials over a generator
        /// role (<see cref="GetOrMintGeneratorRole"/>), so every existential the gate sees is forward and
        /// R-EXIST's per-owner mint covers it; no backward provenance is ever demanded. Admission rests on
        /// the genuine-edge invariant: in an admitted module every forward-role edge — a mint-mirror to the
        /// Skolem predecessor, a sub-role promotion (a shared filler an uncoupled existential promotes up),
        /// an asserted ground edge, or an r ∘ r composition of those — realizes a relationship that holds in
        /// every model, by induction over derivation order, and the forward role is a mirror TARGET, never a
        /// pairing key, so no forward-role edge is ever re-mirrored into a backward edge and a shared filler
        /// receives only universal facts, never an owner-specific deposit. The generator reduction's fence
        /// therefore admits over the forward role's upward closure — the role and its transitive super-roles,
        /// since a witness edge promotes upward — exactly the forward role's own self-transitivity
        /// r ∘ r ⊑ r and a self-demand on the forward role itself, and delegates a chain that touches the
        /// closure through a strict super-role or any other role and a self-demand on a strict super-role,
        /// where the witness edges become chain links or self carriers the mint does not decorate per owner.
        /// Composition on a mirror-target role, which receives the forward role's mirrored edges through a
        /// one-directional pairing, stays decided by the second check, because every mirrored edge reflects a
        /// real per-owner edge. Cross-owner witness folds stay abstained wherever a chain or self-elimination
        /// touches the witness closure (<see cref="ComputeFoldSafety"/>), and a cyclic self-fold under a
        /// reachable self-elimination abstains at the mint (<see cref="WitnessClosureBearsSelfElimination"/>).
        /// </summary>
        /// <param name="edgeGenerating">The roles that can receive an edge other than an asserted ground edge (<see cref="ComputeEdgeGeneratingRoles"/>).</param>
        /// <param name="subRolesBySuper">The super-role to direct-sub-roles map from <see cref="BuildSubRolesBySuper"/>.</param>
        /// <returns><see langword="true"/> when minting is admitted (and <see cref="CoupledRoles"/> populated); <see langword="false"/> to delegate.</returns>
        private bool TryAdmitInverseMinting(HashSet<int> edgeGenerating, Dictionary<int, List<int>> subRolesBySuper)
        {
            HashSet<int> mirrored = [];
            foreach(int paired in InversePairs.Keys)
            {
                mirrored.UnionWith(SubRoleClosure(paired, subRolesBySuper));
            }

            HashSet<int> selfAndChain = ComputeSelfAndChainRoles();

            //The generator fence admits the forward role's own upward closure — the role itself plus its
            //transitive super-roles, since a witness edge promotes upward — to carry exactly the self and
            //chain features the per-owner mint reproduces on the forward role alone, and delegates the whole
            //module on any other. The admissible slice is the forward role's own self-transitivity
            //r ∘ r ⊑ r, whose composed edge is again a forward-role edge over the same per-owner witnesses,
            //and a self-demand on the forward role itself, whose self-edge is inert under the fold (F4 or a
            //position-dependent consumer forces delegation wherever it could matter). A chain whose entry
            //touches the closure through a strict super-role or any other role, or a self-demand on a strict
            //super-role, puts a composed or self edge on a role the witness edge promotes into but the mint
            //does not decorate per owner, so the module delegates. Chain ENTRIES are enumerated — never
            //looked up per role — because the chain index carries no conclusion key: a chain whose
            //conclusion alone lies in the closure is invisible to a first/second-role lookup and must
            //delegate. Composition on a mirror-target role — a role receiving the forward role's mirrored
            //edges through a one-directional pairing — is decided by the untouched second check below:
            //every mirrored edge reflects a real per-owner edge, so a composed conclusion edge realizes a
            //real path in every model, the same canonical-model edge invariant the chain rules preserve,
            //and unique ownership keeps owner subtrees disjoint.
            foreach(int forwardRole in GeneratorRoleForInverted.Keys)
            {
                HashSet<int> forwardClosure = UpwardRoleClosure(forwardRole);

                foreach((int first, List<(int Second, int Conclusion)> entries) in ChainsByFirst)
                {
                    foreach((int second, int conclusion) in entries)
                    {
                        bool touchesClosure = forwardClosure.Contains(first) || forwardClosure.Contains(second) || forwardClosure.Contains(conclusion);
                        bool forwardSelfTransitivity = first == forwardRole && second == forwardRole && conclusion == forwardRole;
                        if(touchesClosure && !forwardSelfTransitivity)
                        {
                            return false;
                        }
                    }
                }

                foreach(List<int> selfRoles in SelfDemands.Values)
                {
                    foreach(int selfRole in selfRoles)
                    {
                        if(forwardClosure.Contains(selfRole) && selfRole != forwardRole)
                        {
                            return false;
                        }
                    }
                }
            }

            HashSet<int> toMint = [];
            foreach(int role in mirrored)
            {
                if(!edgeGenerating.Contains(role))
                {
                    //A mirrored role with no edge of its own mints nothing; whether the module is safe to
                    //admit is decided below, once it is known whether any mirrored role does mint.
                    continue;
                }

                if(selfAndChain.Contains(role))
                {
                    //A self-edge or a composed edge under the mirror the per-owner mint does not reproduce —
                    //delegate the whole module.
                    return false;
                }

                toMint.Add(role);
            }

            CoupledRoles.UnionWith(toMint);

            return true;
        }

        /// <summary>
        /// Registers the owner-independent reduction <c>range(mirror) ⇒ domain(base)</c> for every inverse
        /// pairing: a base-role edge <c>(x, y)</c> forces the mirror edge <c>(y, x)</c>, whose target is
        /// the base edge's source, so a range on the mirror role holds of every base-role source in every
        /// model. Registered as the left existential <c>∃base.⊤ ⊑ range</c> — the same reduction an
        /// inverse-spelled range axiom (<c>range(r⁻) = domain(r)</c>) receives at normalization — so a
        /// range on a mirrored role types the genuinely-role-bearing end and is never attributed to a
        /// single existential owner or witness. The mirror's own per-edge range firing derives the same
        /// conclusions on live paths; this registration makes the entailment independent of mirror-edge
        /// liveness.
        /// </summary>
        private void ReduceMirrorRangesToDomains()
        {
            foreach(KeyValuePair<int, List<int>> pairing in InversePairs)
            {
                foreach(int mirrorRole in pairing.Value)
                {
                    if(RangesByRole.TryGetValue(mirrorRole, out List<int>? ranges))
                    {
                        foreach(int range in ranges)
                        {
                            AddLeftExistential(pairing.Key, Top, range);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The roles that bear a self-demand or take part in a chain — the edge-generating features other
        /// than a right existential, whose self-edge or composed edge the per-owner mint does not
        /// reproduce. No hierarchy closure is applied: a sub-role bearing one of these is itself in the
        /// mirrored closure the caller tests, so a direct membership test over that closure catches it.
        /// </summary>
        /// <returns>The self-demand and chain-involved role ids.</returns>
        private HashSet<int> ComputeSelfAndChainRoles()
        {
            HashSet<int> roles = [];
            foreach(List<int> selfRoles in SelfDemands.Values)
            {
                foreach(int role in selfRoles)
                {
                    roles.Add(role);
                }
            }

            foreach(KeyValuePair<int, List<(int Second, int Conclusion)>> entry in ChainsByFirst)
            {
                roles.Add(entry.Key);
                foreach((int second, int conclusion) in entry.Value)
                {
                    roles.Add(second);
                    roles.Add(conclusion);
                }
            }

            return roles;
        }

        /// <summary>
        /// The upward role closure of a role: the role itself plus every transitive super-role via
        /// <see cref="RoleSubsumptions"/>. An edge over a role promotes to its super-roles at saturation,
        /// so a generator's witness edge on the forward role reaches every role in this closure — the
        /// generator fence tests it against the self-demand and chain roles. Built with an explicit
        /// worklist.
        /// </summary>
        /// <param name="role">The role to close upward over the role hierarchy.</param>
        /// <returns>The role and its transitive super-roles.</returns>
        private HashSet<int> UpwardRoleClosure(int role)
        {
            HashSet<int> closure = [role];
            Stack<int> work = new();
            work.Push(role);
            while(work.Count > 0)
            {
                int current = work.Pop();
                if(RoleSubsumptions.TryGetValue(current, out List<int>? supers))
                {
                    foreach(int super in supers)
                    {
                        if(closure.Add(super))
                        {
                            work.Push(super);
                        }
                    }
                }
            }

            return closure;
        }

        /// <summary>
        /// The reflexive-transitive upward closure of a SET of roles under <see cref="RoleSubsumptions"/> —
        /// every seed role plus its transitive super-roles, since an edge over a role promotes to its
        /// super-roles at saturation. Built with an explicit worklist, no recursion.
        /// </summary>
        /// <param name="roles">The seed roles to close upward over the role hierarchy.</param>
        /// <returns>The seed roles and their transitive super-roles.</returns>
        private HashSet<int> Up(IEnumerable<int> roles)
        {
            HashSet<int> closure = [];
            Stack<int> work = new();
            foreach(int role in roles)
            {
                if(closure.Add(role))
                {
                    work.Push(role);
                }
            }

            while(work.Count > 0)
            {
                int current = work.Pop();
                if(RoleSubsumptions.TryGetValue(current, out List<int>? supers))
                {
                    foreach(int super in supers)
                    {
                        if(closure.Add(super))
                        {
                            work.Push(super);
                        }
                    }
                }
            }

            return closure;
        }

        /// <summary>
        /// Chooses the module's witness regime once, immediately after
        /// <see cref="ReduceMirrorRangesToDomains"/> and while <see cref="CoupledRoles"/> is non-empty, and
        /// computes the state both regimes share: the mint-site self-fold condition
        /// (<see cref="WitnessClosureBearsSelfElimination"/>) and the backward-consumer role set
        /// (<see cref="BackwardConsumerRoles"/>), each a normalization-time index the refinement rule reads
        /// whichever regime the module takes. The regime is <see cref="SharedWitnessKeys"/>: a module whose
        /// witness-reachable closure bears no chain link or conclusion, self-demand, or self-elimination
        /// mints on shared content keys, where a witness denotes the canonical element of its content class
        /// and serves every co-owner at once, so the population is bounded by the distinct
        /// <c>(role, filler)</c> pairs rather than by the owner count and no cross-owner fold can arise to
        /// abstain on. A module carrying one of those features writes position-actual facts a content key
        /// does not record, so it keeps the per-owner regime and runs <see cref="ComputeFoldSafety"/>.
        /// </summary>
        private void ComputeWitnessRegime()
        {
            HashSet<int> mirrorClosure = Up(MirrorTargets);
            HashSet<int> witnessRoles = Up([.. MirrorTargets, .. CoupledRoles]);

            WitnessClosureBearsSelfElimination = WitnessRolesBearSelfElimination(witnessRoles);

            foreach((int role, int _) in LeftExistentials.Keys)
            {
                if(mirrorClosure.Contains(role))
                {
                    BackwardConsumerRoles.Add(role);
                }
            }

            SharedWitnessKeys = !WitnessRolesBearChainOrSelfFeature(witnessRoles);
            if(SharedWitnessKeys)
            {
                return;
            }

            ComputeFoldSafety(witnessRoles);
        }

        /// <summary>
        /// Whether the witness-reachable closure bears a chain or self feature — the regime selector's
        /// exclusion. A chain link or conclusion composes edges the mint does not decorate, and a
        /// self-demand or self-elimination turns the literal <c>source == target</c> coincidence into a
        /// consumer, so each of them can write a fact that holds of one position and not of another
        /// position sharing its content. Chain ENTRIES are enumerated rather than looked up per role
        /// because the chain index carries no conclusion key, so a chain whose conclusion alone lies in the
        /// closure is invisible to a first/second-role lookup. Iterated over the closure, which is already
        /// upward-closed, so no hierarchy re-closure runs here.
        /// </summary>
        /// <param name="witnessRoles">The upward closure of the coupled and mirror-target roles.</param>
        /// <returns><see langword="true"/> when a chain or self feature reaches the closure.</returns>
        private bool WitnessRolesBearChainOrSelfFeature(HashSet<int> witnessRoles)
        {
            foreach(int role in witnessRoles)
            {
                if(ChainsByFirst.ContainsKey(role) || ChainsBySecond.ContainsKey(role) || SelfEliminations.ContainsKey(role))
                {
                    return true;
                }
            }

            foreach(List<(int Second, int Conclusion)> chains in ChainsByFirst.Values)
            {
                foreach((int _, int conclusion) in chains)
                {
                    if(witnessRoles.Contains(conclusion))
                    {
                        return true;
                    }
                }
            }

            foreach(List<int> selfRoles in SelfDemands.Values)
            {
                foreach(int selfRole in selfRoles)
                {
                    if(witnessRoles.Contains(selfRole))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Computes the module-level fold-safety fence <see cref="FoldSafe"/> once, for a module the
        /// regime selector left in the per-owner regime.
        /// The fence reads NORMALIZATION-TIME state only — never a saturation index — and holds exactly
        /// when the module carries no machinery that can write a position-dependent fact onto a folded
        /// witness, so every witness position sharing a raw intern key is bisimilar and accepting a
        /// cross-owner fold is sound. A backward distinguisher needs no clause of its own: a left
        /// existential over the upward closure of <see cref="MirrorTargets"/> is recorded in
        /// <see cref="BackwardConsumerRoles"/>, and a conclusion it would deposit on a witness through an
        /// owner-directed edge is consumed into that witness's intern key by
        /// <see cref="TryConsumeBackwardDemands"/> instead — the witness refines and the owner's minting edge
        /// re-points — so the distinguishing deposit is key-recorded and equal-key
        /// positions remain bisimilar without fencing the axiom out. Four clauses, all conjoined, over the
        /// upward role closures (<see cref="Up(IEnumerable{int})"/>) an edge promotes into:
        /// <list type="bullet">
        /// <item><description>F5 — no DOUBLE-MIRRORED backward consumer
        /// (<see cref="BearsDoubleMirroredBackwardConsumer"/>): no role that is both an
        /// <see cref="InversePairs"/> key and a <see cref="MirrorTargets"/> member has a
        /// <see cref="BackwardConsumerRoles"/> member in its UPWARD closure — a consumer on a strict
        /// super-role reads the doubly mirrored edges just as one on the role itself does, since an edge
        /// promotes upward. The consumption records a witness-to-owner deposit in the witness's key, but the
        /// second mirror turns the same axiom into an owner-directed consumer whose conclusions no key
        /// records, and a witness inherits its owner's whole demand set, so a deeper position interns onto an
        /// earlier one and the owner-directed consumer reads the earlier position's facts across the
        /// fold.</description></item>
        /// <item><description>F2 — no position-actual range write: no <see cref="RangesByRole"/> key whose
        /// role lies in the upward closure of <see cref="CoupledRoles"/> and <see cref="MirrorTargets"/>. A
        /// range fires per actual edge, and folded positions can have different actual in-roles.</description></item>
        /// <item><description>F3 — no class-space nominal: no individual atom appears in a class-space
        /// position — no <see cref="ToldSubsumptions"/> value, no <see cref="LeftExistentials"/> key filler,
        /// no <see cref="RightExistentials"/> filler, no <see cref="RangesByRole"/> range atom is in
        /// <see cref="IndividualAtoms"/>. Assertion-root individuals occur as keys and edge endpoints, not
        /// as class-space values, so they never trip this clause.</description></item>
        /// <item><description>F4 — no chain or self-elimination over a witness-reachable role: no role in
        /// the upward closure of <see cref="CoupledRoles"/> and <see cref="MirrorTargets"/> is a chain link
        /// or conclusion (<see cref="ChainsByFirst"/> / <see cref="ChainsBySecond"/> keys and conclusions)
        /// or a <see cref="SelfEliminations"/> key. This — not admission precedence — excludes a chain or
        /// self-elimination over a super-role of a coupled role, which the downward-closure admission fence
        /// leaves unseen.</description></item>
        /// </list>
        /// </summary>
        /// <param name="witnessRoles">The upward closure of the coupled and mirror-target roles, computed once by <see cref="ComputeWitnessRegime"/>.</param>
        private void ComputeFoldSafety(HashSet<int> witnessRoles)
        {
            if(BackwardConsumerRoles.Count > 0 && BearsDoubleMirroredBackwardConsumer())
            {
                FoldSafe = false;

                return;
            }

            foreach(int role in RangesByRole.Keys)
            {
                if(witnessRoles.Contains(role))
                {
                    FoldSafe = false;

                    return;
                }
            }

            if(HasClassSpaceNominal())
            {
                FoldSafe = false;

                return;
            }

            foreach(int role in witnessRoles)
            {
                if(ChainsByFirst.ContainsKey(role) || ChainsBySecond.ContainsKey(role) || SelfEliminations.ContainsKey(role))
                {
                    FoldSafe = false;

                    return;
                }
            }

            foreach(List<(int Second, int Conclusion)> chains in ChainsByFirst.Values)
            {
                foreach((int _, int conclusion) in chains)
                {
                    if(witnessRoles.Contains(conclusion))
                    {
                        FoldSafe = false;

                        return;
                    }
                }
            }

            FoldSafe = true;
        }

        /// <summary>
        /// Whether the module carries a DOUBLE-MIRRORED backward consumer: a role that is both an
        /// <see cref="InversePairs"/> key and a <see cref="MirrorTargets"/> member — so its edges are
        /// mirrored in both directions and a witness-to-owner edge is mirrored again into an
        /// owner-to-witness edge — some role of whose UPWARD CLOSURE is a
        /// <see cref="BackwardConsumerRoles"/> member. The closure is load-bearing, not decorative: an edge
        /// promotes to its super-roles, so a consumer registered on a strict super-role of the doubly
        /// mirrored role reads both mirrored directions exactly as one registered on the role itself, and
        /// testing the role alone leaves that module unfenced.
        /// A left existential consuming over such a role fires in BOTH directions: the witness-to-owner
        /// firing is consumed into the witness's key by <see cref="TryConsumeBackwardDemands"/>, but the
        /// owner-to-witness firing deposits on the owner and is recorded in no key, and a witness inherits
        /// its owner's whole demand set, so once the decorations saturate a deeper position interns onto an
        /// earlier one whose ladder facts it does not carry and the owner-directed consumer reads those
        /// facts across the fold. Such a module keeps the cross-owner abstention.
        /// The condition is deliberately BROADER than that hazard: a symmetric role is its own inverse
        /// pairing, so ANY module whose symmetric coupled role carries a left existential trips the clause,
        /// whether or not its witnesses ever fold. That over-delegates in the fail-safe direction only — the
        /// clause never opens a fold, it only refuses one, and refusing costs nothing on a module where no
        /// cross-owner witness is ever returned.
        /// </summary>
        /// <returns><see langword="true"/> when a double-mirrored role's upward closure bears a backward consumer.</returns>
        private bool BearsDoubleMirroredBackwardConsumer()
        {
            foreach(int role in MirrorTargets)
            {
                if(!InversePairs.ContainsKey(role))
                {
                    continue;
                }

                foreach(int promoted in UpwardRoleClosure(role))
                {
                    if(BackwardConsumerRoles.Contains(promoted))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any role in the witness-reachable closure is a <see cref="SelfEliminations"/> key — the
        /// mint-site guard's module-constant condition, read over the same closure clause F4 of the
        /// fold-safety fence uses. Iterated, no hierarchy re-closure: the closure is already upward-closed.
        /// </summary>
        /// <param name="witnessRoles">The upward closure of the coupled and mirror-target roles.</param>
        /// <returns><see langword="true"/> when a self-elimination reaches a witness-carrying role.</returns>
        private bool WitnessRolesBearSelfElimination(HashSet<int> witnessRoles)
        {
            foreach(int role in witnessRoles)
            {
                if(SelfEliminations.ContainsKey(role))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether some <see cref="ToldSubsumptions"/> value is an <see cref="IndividualAtoms"/> member — a
        /// node told to BE an individual (<c>N ⊑ {a}</c>), which is the whole surface on which the
        /// liveness-gated merge can discover an identity the ground-key regime never stated. That told
        /// value is the single write through which an individual atom enters subsumer space: conjunction
        /// conclusions are fresh or named-class atoms, and left-existential keys and asserted-edge
        /// endpoints consume an individual node rather than deriving anything about it. Shared as the first
        /// clause of <see cref="HasClassSpaceNominal"/> and as the trigger of
        /// <see cref="RequiresGroundIdentityCompletion"/>, so the fold-safety fence and the ground-identity
        /// completion gate read one definition of an identity-bearing nominal.
        /// </summary>
        /// <returns><see langword="true"/> when an individual atom is a told subsumer.</returns>
        private bool HasIdentityBearingNominal()
        {
            if(IndividualAtoms.Count == 0)
            {
                return false;
            }

            foreach(List<int> supers in ToldSubsumptions.Values)
            {
                foreach(int super in supers)
                {
                    if(IndividualAtoms.Contains(super))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the module's told nominal identity can meet a consumer that resolved identity before
        /// interning — the entry condition of the ground-identity completion loop. The merge behind the
        /// liveness gate pools a live carrier's constraints onto the individual it is told to be, which is
        /// an identity derived at saturation; the distinctness collision scan, the functional and
        /// inverse-functional endpoint union, and the asymmetric and irreflexive edge scans all read the
        /// ground-key union-find that closes before interning, so a module carrying both would otherwise
        /// answer from an identity-incomplete pre-merge. The condition is exactly the pairing — an
        /// identity-bearing nominal AND one of those consumers — so the set of modules that ever rebuild is
        /// exactly the set that can read a stale union-find, and every other module takes the single-pass
        /// path. A discovered identity in a module with no such consumer needs no replay: the saturation's
        /// own pooling is identity-complete for it.
        /// </summary>
        /// <returns><see langword="true"/> when the module pairs an identity-bearing nominal with a pre-intern identity consumer.</returns>
        public bool RequiresGroundIdentityCompletion()
        {
            return HasIdentityBearingNominal()
                && (HasDistinctnessAssertions || FunctionalRoles.Count > 0 || InverseFunctionalRoles.Count > 0 || AsymmetricRoles.Count > 0 || IrreflexiveRoles.Count > 0);
        }

        /// <summary>
        /// Whether the saturation derived <c>⊥</c> on <c>⊤</c> or on a named individual — the module's
        /// inconsistency condition, read by <see cref="BuildModuleResult"/> as the verdict and by the
        /// ground-identity completion loop as the pass that settles the decision on its own: every
        /// subsumption a pass derives is entailed without the identities a later pass would add, so an
        /// inconsistency it reaches is entailed too and no rebuild can retract it.
        /// </summary>
        /// <returns><see langword="true"/> when the module is decided inconsistent by this pass.</returns>
        public bool HasDerivedInconsistency()
        {
            if(SubsumersOf(Top).Contains(Bottom))
            {
                return true;
            }

            foreach(int individual in IndividualAtoms)
            {
                if(SubsumersOf(individual).Contains(Bottom))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The number of rebuilds the ground-identity completion loop may run for this module: one fewer
        /// than its individual count. Each rebuild folds at least one pair of individual keys the previous
        /// pass had apart, so the partition strictly coarsens and a module of <c>n</c> individuals reaches
        /// one class after at most <c>n − 1</c> rebuilds. The bound is structural — it cannot fire on a
        /// module the loop genuinely decides — and it is the loop's termination backstop beside the
        /// cancellation token each pass threads through.
        /// </summary>
        public int GroundIdentityRebuildCap => IndividualAtoms.Count - 1;

        /// <summary>
        /// Appends every identity this saturation derived that the pre-intern union-find does not already
        /// state. EVERY live node is scanned, not only the individuals: a live node is inhabited, and an
        /// individual atom in its subsumer set says its element IS that individual, so two such atoms on one
        /// live node make the two individuals equal in every model. Scanning the anonymous carriers too is
        /// what makes the sweep independent of which shapes the ground-spine walk grounds — an identity
        /// mediated by a witness below a nominal-free layer is caught here exactly as a direct one is. The
        /// individual case is subsumed: an individual atom is seeded live and self-subsumes, so a nominal
        /// pooled onto it joins its own atom in its subsumer set.
        /// </summary>
        /// <param name="discoveredToAppendTo">The discovered identity pairs, as individual keys.</param>
        /// <returns><see langword="true"/> when at least one pair was appended.</returns>
        public bool TryDiscoverGroundIdentities(List<(Utf8String First, Utf8String Second)> discoveredToAppendTo)
        {
            bool discovered = false;
            List<int> nominals = [];
            foreach(int node in Live)
            {
                if(!Subsumers.TryGetValue(node, out HashSet<int>? subsumers))
                {
                    continue;
                }

                nominals.Clear();
                foreach(int subsumer in subsumers)
                {
                    if(IndividualAtoms.Contains(subsumer))
                    {
                        nominals.Add(subsumer);
                    }
                }

                if(nominals.Count < 2)
                {
                    continue;
                }

                for(int first = 0; first < nominals.Count; first++)
                {
                    if(AtomNames[nominals[first]] is not Utf8String firstKey)
                    {
                        continue;
                    }

                    for(int second = first + 1; second < nominals.Count; second++)
                    {
                        if(AtomNames[nominals[second]] is not Utf8String secondKey || FindKey(firstKey).Equals(FindKey(secondKey)))
                        {
                            continue;
                        }

                        discoveredToAppendTo.Add((firstKey, secondKey));
                        discovered = true;
                    }
                }
            }

            return discovered;
        }

        /// <summary>Records the saturation-restart marker on a context carrying nothing else, so the coupled reasoner delegates a module whose rebuild sequence passed the structural bound and no partial rebuild's classification is read.</summary>
        public void DelegateGroundIdentityRestart()
        {
            Unsupported.Add("A saturation-restart sequence that does not settle within the module's individual bound is outside the EL classification calculus.");
        }

        /// <summary>
        /// Whether an individual atom appears in a class-space position at normalization — clause F3 of the
        /// fold-safety fence. Scans for an <see cref="IndividualAtoms"/> member as a told subsumer (the shared
        /// <see cref="HasIdentityBearingNominal"/> clause), a <see cref="LeftExistentials"/> key filler, a
        /// <see cref="RightExistentials"/> filler, or a
        /// <see cref="RangesByRole"/> range atom — the told class-to-nominal and nominal-filler spellings the
        /// liveness cascade could otherwise flow across a fold edge. The predicate stays the broader one: it
        /// shares the told-value clause with the ground-identity completion gate without narrowing to it.
        /// Assertion-root individuals never enter any
        /// of these, so they leave the fence clear.
        /// </summary>
        /// <returns><see langword="true"/> when a class-space nominal is present.</returns>
        private bool HasClassSpaceNominal()
        {
            if(IndividualAtoms.Count == 0)
            {
                return false;
            }

            if(HasIdentityBearingNominal())
            {
                return true;
            }

            foreach((int _, int filler) in LeftExistentials.Keys)
            {
                if(IndividualAtoms.Contains(filler))
                {
                    return true;
                }
            }

            foreach(List<(int Role, int Filler)> existentials in RightExistentials.Values)
            {
                foreach((int _, int filler) in existentials)
                {
                    if(IndividualAtoms.Contains(filler))
                    {
                        return true;
                    }
                }
            }

            foreach(List<int> ranges in RangesByRole.Values)
            {
                foreach(int range in ranges)
                {
                    if(IndividualAtoms.Contains(range))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a ground-constrained role's edges are not confined to the asserted ground edges the
        /// pre-merge and normalize passes scanned: it bears a non-asserted edge (it is edge-generating), or
        /// some role in its reflexive-transitive sub-role closure is a mirror target — a role the saturation
        /// mirror seeds reverse edges onto, from a symmetric self-pairing, an <c>InverseObjectProperties</c>
        /// pairing, or a one-directional <c>r⁻ ⊑ s</c>. The predicate is characteristic-neutral: a
        /// functional or inverse-functional role whose successors (resp. predecessors) it thereby cannot
        /// account for, and an asymmetric or irreflexive role whose forbidden self-edge or reverse edge the
        /// asserted-edge scan cannot see, are all delegated by it.
        /// </summary>
        /// <param name="role">The ground-constrained role.</param>
        /// <param name="edgeGenerating">The roles that can receive a non-asserted edge.</param>
        /// <param name="subRolesBySuper">The super-role to direct-sub-roles map.</param>
        /// <returns><see langword="true"/> when the role must be delegated.</returns>
        private bool IsUnsafeGroundRole(int role, HashSet<int> edgeGenerating, Dictionary<int, List<int>> subRolesBySuper)
        {
            if(edgeGenerating.Contains(role))
            {
                return true;
            }

            foreach(int subRole in SubRoleClosure(role, subRolesBySuper))
            {
                if(MirrorTargets.Contains(subRole))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The roles the saturation can give an edge other than an asserted ground edge: every role
        /// bearing a right existential (<c>A ⊑ ∃r.B</c>), a self-demand (<c>ObjectHasSelf</c> or the
        /// reflexive characteristic), or a chain link or conclusion (a property chain, or transitivity
        /// <c>r∘r⊑r</c>), closed upward over the role hierarchy because a sub-role's edge is promoted
        /// to its super-roles. A symmetric, inverse, or functional role outside this set carries only
        /// asserted ground edges, so the asserted-edge mirror or merge captures its whole extension. A
        /// subclass-side <c>∃r.Self ⊑ B</c> (a self-elimination) is intentionally not listed: it
        /// consumes a self-edge rather than producing one, and the <c>source == target</c> fence keeps
        /// it sound on a mirrored ground edge.
        /// </summary>
        /// <returns>The edge-generating role ids.</returns>
        private HashSet<int> ComputeEdgeGeneratingRoles()
        {
            HashSet<int> generating = [];

            foreach(List<(int Role, int Filler)> existentials in RightExistentials.Values)
            {
                foreach((int role, int _) in existentials)
                {
                    generating.Add(role);
                }
            }

            foreach(List<int> roles in SelfDemands.Values)
            {
                foreach(int role in roles)
                {
                    generating.Add(role);
                }
            }

            foreach(KeyValuePair<int, List<(int Second, int Conclusion)>> entry in ChainsByFirst)
            {
                generating.Add(entry.Key);
                foreach((int second, int conclusion) in entry.Value)
                {
                    generating.Add(second);
                    generating.Add(conclusion);
                }
            }

            //Edges promote from a sub-role to its super-roles in ProcessEdge, so a generating
            //sub-role makes its super-roles generating too.
            Stack<int> work = new(generating);
            while(work.Count > 0)
            {
                int role = work.Pop();
                if(RoleSubsumptions.TryGetValue(role, out List<int>? supers))
                {
                    foreach(int super in supers)
                    {
                        if(generating.Add(super))
                        {
                            work.Push(super);
                        }
                    }
                }
            }

            return generating;
        }

        /// <summary>Reverses the sub-role to super-role hierarchy into a super-role to sub-roles map, so a range on a super-role can push down to all of its sub-roles.</summary>
        /// <returns>The super-role to direct-sub-roles map.</returns>
        private Dictionary<int, List<int>> BuildSubRolesBySuper()
        {
            Dictionary<int, List<int>> subRolesBySuper = [];
            foreach(KeyValuePair<int, List<int>> entry in RoleSubsumptions)
            {
                foreach(int super in entry.Value)
                {
                    if(!subRolesBySuper.TryGetValue(super, out List<int>? subs))
                    {
                        subs = [];
                        subRolesBySuper[super] = subs;
                    }

                    subs.Add(entry.Key);
                }
            }

            return subRolesBySuper;
        }

        /// <summary>The reflexive-transitive set of sub-roles of a role — itself and every role whose edges promote up to it through the hierarchy.</summary>
        /// <param name="role">The super-role.</param>
        /// <param name="subRolesBySuper">The super-role to direct-sub-roles map from <see cref="BuildSubRolesBySuper"/>.</param>
        /// <returns>The role together with all its transitive sub-roles.</returns>
        private static HashSet<int> SubRoleClosure(int role, Dictionary<int, List<int>> subRolesBySuper)
        {
            HashSet<int> closure = [role];
            Stack<int> work = new();
            work.Push(role);
            while(work.Count > 0)
            {
                int current = work.Pop();
                if(subRolesBySuper.TryGetValue(current, out List<int>? subs))
                {
                    foreach(int sub in subs)
                    {
                        if(closure.Add(sub))
                        {
                            work.Push(sub);
                        }
                    }
                }
            }

            return closure;
        }

        /// <summary>
        /// Seeds a told <c>⊥</c> on any two individuals a <c>DifferentIndividuals</c>
        /// axiom asserts distinct that the <c>SameIndividual</c> / bare-nominal pre-merge
        /// has nonetheless collapsed to one representative: a told identity forcing the
        /// two equal contradicts their asserted distinctness, so the module is
        /// inconsistent. The collision is visible only after the union-find closure, so it
        /// is a representative comparison, not a syntactic operand check.
        /// </summary>
        /// <param name="axiom">The <c>DifferentIndividuals</c> axiom.</param>
        private void SeedDistinctnessClash(OwlDifferentIndividualsAxiom axiom)
        {
            IReadOnlyList<RdfTerm> individuals = axiom.Individuals;
            for(int i = 0; i < individuals.Count; i++)
            {
                if(!TryIndividualKey(individuals[i], out Utf8String keyI))
                {
                    continue;
                }

                Utf8String representative = FindKey(keyI);
                for(int j = i + 1; j < individuals.Count; j++)
                {
                    if(TryIndividualKey(individuals[j], out Utf8String keyJ) && representative.Equals(FindKey(keyJ)))
                    {
                        AddToldSubsumption(IndividualAtomOf(representative), Bottom);
                    }
                }
            }
        }

        /// <summary>
        /// Unions the asserted successors a functional role — or predecessors an inverse-functional
        /// role — forces equal into the <c>SameIndividual</c> union-find. <c>FunctionalObjectProperty(r)</c>
        /// bounds each individual to one r-successor, so two asserted successors of one individual denote
        /// the same element; <c>InverseFunctionalObjectProperty(r)</c> bounds each to one r-predecessor
        /// symmetrically. Both read the asserted ground edges over the role and its sub-roles — an
        /// <c>ObjectPropertyAssertion</c>, or a ground edge <see cref="GroundSpineWalk"/> reports for a
        /// class assertion (<c>ObjectHasValue(r, a)</c> / <c>∃r.{a}</c> in either direction, at any depth on
        /// a grounded spine) — resolved
        /// through the union-find, and iterate to a
        /// fixpoint because a union can expose a further shared source or target. Runs before the
        /// interning loop so the collapse reaches every asserted type and edge; the post-normalization
        /// gate delegates the module if a functional role's successors are not confined to these asserted
        /// edges (an existential, self, chain, or mirror successor the scan cannot see). The scan reads both
        /// spellings: <c>Functional(r⁻)</c> bounds r-PREDECESSORS (it is inverse-functionality on <c>r</c>)
        /// and <c>InverseFunctional(r⁻)</c> bounds r-SUCCESSORS (it is functionality on <c>r</c>), each
        /// collapsing the same endpoints as its forward equivalent, so the pre-merge agrees with the
        /// <see cref="NormalizeAxiom"/> registration per spelling.
        /// </summary>
        /// <param name="axioms">The module's axioms.</param>
        private void SeedFunctionalMerges(IReadOnlyList<OwlAxiom> axioms)
        {
            HashSet<Utf8String> functional = [];
            HashSet<Utf8String> inverseFunctional = [];
            foreach(OwlAxiom axiom in axioms)
            {
                //Functional(r) and InverseFunctional(r⁻) both bound r-SUCCESSORS (the successor union);
                //InverseFunctional(r) and Functional(r⁻) both bound r-PREDECESSORS (the predecessor union).
                //The inverse spelling swaps which endpoint the fixpoint collapses, so the scan branches on
                //the characteristic AND the spelling, mirroring the NormalizeAxiom registration exactly.
                HashSet<Utf8String>? collapseByEndpoint = axiom switch
                {
                    OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Functional, Property.IsInverse: false } => functional,
                    OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Functional, Property.IsInverse: true } => inverseFunctional,
                    OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional, Property.IsInverse: false } => inverseFunctional,
                    OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.InverseFunctional, Property.IsInverse: true } => functional,
                    _ => null,
                };

                if(collapseByEndpoint is not null && axiom is OwlObjectPropertyCharacteristicAxiom characteristic)
                {
                    collapseByEndpoint.Add(characteristic.Property.Property.Iri);
                }
            }

            if(functional.Count == 0 && inverseFunctional.Count == 0)
            {
                return;
            }

            Dictionary<Utf8String, List<Utf8String>> subRolesBySuper = BuildAssertedSubRoles(axioms);
            List<(Utf8String Role, Utf8String Source, Utf8String Target)> edges = CollectAssertedRoleEdges(axioms);

            bool merged = true;
            while(merged)
            {
                merged = false;
                foreach(Utf8String role in functional)
                {
                    merged |= UnionFunctionalEndpoints(role, subRolesBySuper, edges, byTarget: false);
                }

                foreach(Utf8String role in inverseFunctional)
                {
                    merged |= UnionFunctionalEndpoints(role, subRolesBySuper, edges, byTarget: true);
                }
            }
        }

        /// <summary>
        /// Unions, for one functional or inverse-functional role, the endpoints its functionality forces
        /// equal across the asserted edges over the role's reflexive-transitive sub-roles: for a
        /// functional role the targets sharing a source representative, for an inverse-functional role
        /// the sources sharing a target representative. Endpoints are resolved through the union-find on
        /// each visit, so a union earlier in the pass is seen later in it.
        /// </summary>
        /// <param name="role">The functional or inverse-functional role.</param>
        /// <param name="subRolesBySuper">The asserted super-role to direct-sub-roles map.</param>
        /// <param name="edges">The asserted ground edges, as role/source/target keys.</param>
        /// <param name="byTarget">Whether to group by target (inverse-functional) rather than source (functional).</param>
        /// <returns><see langword="true"/> when at least one new union was made.</returns>
        private bool UnionFunctionalEndpoints(Utf8String role, Dictionary<Utf8String, List<Utf8String>> subRolesBySuper, List<(Utf8String Role, Utf8String Source, Utf8String Target)> edges, bool byTarget)
        {
            HashSet<Utf8String> closure = AssertedSubRoleClosure(role, subRolesBySuper);
            Dictionary<Utf8String, Utf8String> firstByKey = [];
            bool merged = false;
            foreach((Utf8String edgeRole, Utf8String source, Utf8String target) in edges)
            {
                if(!closure.Contains(edgeRole))
                {
                    continue;
                }

                Utf8String key = FindKey(byTarget ? target : source);
                Utf8String other = FindKey(byTarget ? source : target);
                if(firstByKey.TryGetValue(key, out Utf8String existing))
                {
                    Utf8String existingRepresentative = FindKey(existing);
                    if(!existingRepresentative.Equals(other))
                    {
                        Merges[existingRepresentative] = other;
                        merged = true;
                    }
                }
                else
                {
                    firstByKey[key] = other;
                }
            }

            return merged;
        }

        /// <summary>Builds the asserted super-role to direct-sub-roles map at the IRI level from the module's forward sub-property and equivalent-property axioms, so a functional role's successors can be gathered over its sub-roles before the role ids are interned.</summary>
        /// <param name="axioms">The module's axioms.</param>
        /// <returns>The super-role IRI to direct-sub-role IRIs map.</returns>
        private static Dictionary<Utf8String, List<Utf8String>> BuildAssertedSubRoles(IReadOnlyList<OwlAxiom> axioms)
        {
            Dictionary<Utf8String, List<Utf8String>> subRolesBySuper = [];
            foreach(OwlAxiom axiom in axioms)
            {
                switch(axiom)
                {
                    case (OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: false, SuperProperty.IsInverse: false } subProperty):
                    {
                        AddAssertedSubRole(subRolesBySuper, subProperty.SuperProperty.Property.Iri, subProperty.SubProperty.Property.Iri);
                        break;
                    }
                    case (OwlEquivalentObjectPropertiesAxiom { First.IsInverse: false, Second.IsInverse: false } equivalent):
                    {
                        AddAssertedSubRole(subRolesBySuper, equivalent.First.Property.Iri, equivalent.Second.Property.Iri);
                        AddAssertedSubRole(subRolesBySuper, equivalent.Second.Property.Iri, equivalent.First.Property.Iri);
                        break;
                    }
                    case (OwlPropertyChainAxiom { Chain: [{ IsInverse: false } link], SuperProperty.IsInverse: false } chain):
                    {
                        //A single-link property chain is a plain sub-role inclusion (the normalizer
                        //reduces it to one, populating the same role hierarchy the gate reads), so its
                        //asserted edges promote up to the super-role and must be counted for that role's
                        //functionality. A multi-link chain composes edges instead, which makes its
                        //conclusion edge-generating and delegates any functional role over it.
                        AddAssertedSubRole(subRolesBySuper, chain.SuperProperty.Property.Iri, link.Property.Iri);
                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }

            return subRolesBySuper;
        }

        /// <summary>Records a direct sub-role IRI under its super-role IRI.</summary>
        /// <param name="subRolesBySuper">The map to extend.</param>
        /// <param name="super">The super-role IRI.</param>
        /// <param name="sub">The sub-role IRI.</param>
        private static void AddAssertedSubRole(Dictionary<Utf8String, List<Utf8String>> subRolesBySuper, Utf8String super, Utf8String sub)
        {
            if(!subRolesBySuper.TryGetValue(super, out List<Utf8String>? subs))
            {
                subs = [];
                subRolesBySuper[super] = subs;
            }

            subs.Add(sub);
        }

        /// <summary>The reflexive-transitive set of sub-role IRIs of a role IRI — itself and every role whose asserted edges promote up to it through the hierarchy.</summary>
        /// <param name="role">The super-role IRI.</param>
        /// <param name="subRolesBySuper">The asserted super-role to direct-sub-roles map.</param>
        /// <returns>The role together with all its transitive sub-role IRIs.</returns>
        private static HashSet<Utf8String> AssertedSubRoleClosure(Utf8String role, Dictionary<Utf8String, List<Utf8String>> subRolesBySuper)
        {
            HashSet<Utf8String> closure = [role];
            Stack<Utf8String> work = new();
            work.Push(role);
            while(work.Count > 0)
            {
                Utf8String current = work.Pop();
                if(subRolesBySuper.TryGetValue(current, out List<Utf8String>? subs))
                {
                    foreach(Utf8String sub in subs)
                    {
                        if(closure.Add(sub))
                        {
                            work.Push(sub);
                        }
                    }
                }
            }

            return closure;
        }

        /// <summary>Collects the module's asserted ground role edges as role/source/target IRI keys: an <c>ObjectPropertyAssertion</c>, and every ground edge <see cref="GroundSpineWalk"/> reports for a class assertion, the inverse spellings contributing the same edge with its endpoints exchanged and the recursive ones running between two nominals below the subject — each an edge from a concrete individual to a concrete individual. Both this scan and <see cref="AssertType"/> read the same walk, so the raw edge set and the interned one cannot disagree. This is the raw re-scan the functional pre-merge reads, and it runs before interning, which is why it re-reads the axioms rather than the interned index.</summary>
        /// <param name="axioms">The module's axioms.</param>
        /// <returns>The asserted edges as role/source/target keys.</returns>
        private static List<(Utf8String Role, Utf8String Source, Utf8String Target)> CollectAssertedRoleEdges(IReadOnlyList<OwlAxiom> axioms)
        {
            List<(Utf8String Role, Utf8String Source, Utf8String Target)> edges = [];
            List<(RdfTerm? Anchor, RdfTerm Nominal)> identities = [];
            List<(NamedNode Role, RdfTerm? Source, RdfTerm? Target)> groundEdges = [];
            List<(RdfTerm? Anchor, OwlClassExpression Element)> typings = [];
            foreach(OwlAxiom axiom in axioms)
            {
                switch(axiom)
                {
                    case (OwlObjectPropertyAssertionAxiom roleAssertion)
                        when TryIndividualKey(roleAssertion.Source, out Utf8String source) && TryIndividualKey(roleAssertion.Target, out Utf8String target):
                    {
                        edges.Add((roleAssertion.Property.Iri, source, target));
                        break;
                    }
                    case (OwlClassAssertionAxiom assertion) when TryIndividualKey(assertion.Individual, out Utf8String subject):
                    {
                        identities.Clear();
                        groundEdges.Clear();
                        typings.Clear();
                        GroundSpineWalk(assertion.Class, identities, groundEdges, typings);
                        foreach((NamedNode role, RdfTerm? source, RdfTerm? target) in groundEdges)
                        {
                            if(TryAnchorKey(source, subject, out Utf8String sourceKey) && TryAnchorKey(target, subject, out Utf8String targetKey))
                            {
                                edges.Add((role.Iri, sourceKey, targetKey));
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

            return edges;
        }

        /// <summary>
        /// Adds an individual's asserted type as told subsumptions and right
        /// existentials on its atom. The type is an EL class expression (the
        /// module survey guarantees it): a named class, a conjunction, or an
        /// existential over a forward role with an EL filler; a complex filler is
        /// named through the existing inclusion machinery. An existential whose
        /// filler conjunct spine bears a singleton nominal is not an existential
        /// at all — the witness IS the named individual — so
        /// <see cref="GroundSpineWalk"/> decides that whole subtree: its ground
        /// edges are interned as asserted edges and its remaining elements
        /// re-enter this walk anchored on the individual they are asserted of.
        /// </summary>
        /// <param name="individualAtom">The individual's atom.</param>
        /// <param name="type">The asserted EL class expression.</param>
        private void AssertType(int individualAtom, OwlClassExpression type)
        {
            Stack<(int Subject, OwlClassExpression Type)> work = new();
            List<(RdfTerm? Anchor, RdfTerm Nominal)> identities = [];
            List<(NamedNode Role, RdfTerm? Source, RdfTerm? Target)> groundEdges = [];
            List<(RdfTerm? Anchor, OwlClassExpression Element)> typings = [];
            work.Push((individualAtom, type));

            while(work.Count > 0)
            {
                (int subject, OwlClassExpression current) = work.Pop();

                switch(current)
                {
                    case OwlClassReference reference:
                    {
                        AddToldSubsumption(subject, AtomOf(reference.Class.Iri));
                        break;
                    }
                    case OwlObjectIntersectionOf intersection:
                    {
                        foreach(OwlClassExpression operand in intersection.Operands)
                        {
                            work.Push((subject, operand));
                        }

                        break;
                    }
                    case OwlObjectSomeValuesFrom { Filler: OwlClassReference namedFiller } existential when !existential.Property.IsInverse:
                    {
                        AddRightExistential(subject, RoleOf(existential.Property.Property), AtomOf(namedFiller.Class.Iri));
                        break;
                    }
                    case (OwlObjectHasValue or OwlObjectSomeValuesFrom) when BearsGroundNominalSpine(current):
                    {
                        //An asserted existential whose filler conjunct spine carries a singleton nominal
                        //is the ground edge (r, subject, a) onto the shared individual node a, seeded
                        //like an ordinary asserted role edge since the subject is a concrete interned
                        //individual. The inverse spelling says the subject has an r-PREDECESSOR which is
                        //a, so it is that same edge with its endpoints exchanged — the ground fact
                        //(a, subject) ∈ r. Both endpoints are concrete individuals, so no witness is
                        //minted and no backward demand is decorated. The walk descends through the
                        //nominal: the filler's remaining conjuncts are asserted of a itself and re-enter
                        //this walk on a's own atom, and an existential among them grounds again. The arm
                        //precedes the complex-filler arms below, which would otherwise name the filler as
                        //a proxy and route a ground edge through the existential machinery; the
                        //reserved-role guard lives inside the walk, so a reserved spelling at any depth
                        //stays out of the ground family and takes the arms below.
                        identities.Clear();
                        groundEdges.Clear();
                        typings.Clear();
                        GroundSpineWalk(current, identities, groundEdges, typings);
                        foreach((NamedNode role, RdfTerm? source, RdfTerm? target) in groundEdges)
                        {
                            AssertedEdges.Add((RoleOf(role), AnchorAtom(source, subject), AnchorAtom(target, subject)));
                        }

                        foreach((RdfTerm? anchor, OwlClassExpression element) in typings)
                        {
                            work.Push((AnchorAtom(anchor, subject), element));
                        }

                        break;
                    }
                    case (OwlObjectSomeValuesFrom { Filler: OwlClassReference namedInverseFiller } inverseExistential) when inverseExistential.Property.IsInverse && !IsReservedRole(inverseExistential.Property.Property.Iri):
                    {
                        //x : ∃r⁻.C reduces to the forward existential ∃g.C over the synthetic generator role
                        //of r (g ⊑ r⁻): the asserted individual's r-predecessor is minted per-owner as a
                        //forward g-successor and the mirror writes the real r-edge back onto the individual.
                        //The owner is a genuinely inhabited node, so the witness is forced, not hypothetical.
                        AddRightExistential(subject, GetOrMintGeneratorRole(RoleOf(inverseExistential.Property.Property)), AtomOf(namedInverseFiller.Class.Iri));
                        break;
                    }
                    case OwlObjectSomeValuesFrom existential when !existential.Property.IsInverse:
                    {
                        int filler = FreshAtom();
                        AddRightExistential(subject, RoleOf(existential.Property.Property), filler);
                        NormalizeInclusion(AtomReference(filler), existential.Filler);
                        break;
                    }
                    case (OwlObjectSomeValuesFrom { Filler: not OwlObjectOneOf } complexInverseExistential) when complexInverseExistential.Property.IsInverse && !IsReservedRole(complexInverseExistential.Property.Property.Iri):
                    {
                        //A complex filler is named as a fresh proxy the generator existential hangs off, so
                        //the same per-owner mint carries it. The filler exclusion keeps an ObjectOneOf out:
                        //a SINGLE-individual enumeration is the asserted ground edge the shape arm above
                        //writes, and a MULTI-individual one is a disjunction the fragment does not express,
                        //which this arm would otherwise decide through the superclass singleton-nominal
                        //branch with no marker. Without the exclusion it falls to the default instead.
                        int complexInverseFiller = FreshAtom();
                        AddRightExistential(subject, GetOrMintGeneratorRole(RoleOf(complexInverseExistential.Property.Property)), complexInverseFiller);
                        NormalizeInclusion(AtomReference(complexInverseFiller), complexInverseExistential.Filler);
                        break;
                    }
                    case (OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome) when !IsReservedDataProperty(dataSome.Properties[0].Iri):
                    {
                        //An asserted data existential demands a dp-value in the range on
                        //the individual; an empty range forces the individual empty.
                        AddDataDemand(subject, dataSome.Properties[0].Iri, dataSome.Range);
                        break;
                    }
                    case (OwlDataHasValue dataHas) when !IsReservedDataProperty(dataHas.Property.Iri):
                    {
                        AddDataDemand(subject, dataHas.Property.Iri, new OwlDataOneOf([dataHas.Value]));
                        break;
                    }
                    case (OwlObjectHasSelf hasSelf):
                    {
                        //An asserted self-restriction demands a reflexive edge on the individual. A
                        //self-edge is its own reverse, so the inverse spelling demands the same edge on
                        //the same forward role.
                        AddSelfDemand(subject, RoleOf(hasSelf.Property.Property));
                        break;
                    }
                    case (OwlObjectOneOf { Individuals: [NamedNode or BlankNode] } assertedNominal):
                    {
                        //A bare singleton nominal on the asserted class's conjunct spine is the told
                        //identity x = a, folded into the SameIndividual union-find by the pre-merge pass
                        //that runs before any interning — the regime the distinctness and functional-collapse
                        //scans read. NominalAtom therefore resolves the individual to the subject's own atom,
                        //and this arm asserts exactly that: an under-folded identity would leave the ground
                        //regime disagreeing with the interned one where no later net can see it, so it fails
                        //here rather than answering from the disagreement. The arm also keeps the spine walk
                        //total: without it the operand falls to the default arm and records an unsupported
                        //marker for a shape the module decides. A multi-individual enumeration is a
                        //disjunction the fragment does not express and keeps falling to that marker.
                        int assertedNominalAtom = NominalAtom(assertedNominal.Individuals[0]);
                        if(assertedNominalAtom != subject)
                        {
                            throw new InvalidOperationException("A bare singleton nominal in a class assertion must resolve to its subject's own atom through the pre-intern identity fold.");
                        }

                        break;
                    }
                    default:
                    {
                        Unsupported.Add($"{current.GetType().Name} in a class assertion is outside the EL classification calculus.");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Walks an asserted class for the ground facts its nominals state, and reports them as three
        /// finding kinds each applier consumes on its own terms. The walk is the single point of truth for
        /// "does this spine ground, and where do the recursion edges go": it reads raw class expressions,
        /// raw role IRIs, and raw individual terms only — no merge resolution, no interning — so the
        /// pre-intern identity fold, the raw asserted-edge re-scan, and <see cref="AssertType"/>'s interning
        /// walk cannot disagree about the ground edge set, whichever regime each of them resolves keys in.
        /// Iterative over an explicit stack.
        /// <list type="bullet">
        /// <item><description>An identity — a singleton nominal asserted of an anchor. A bare nominal on the
        /// walked class's own conjunct spine is the told identity of the walk's subject; two or more nominals
        /// sharing one grounded filler spine are told identities of each other, folding onto the
        /// first.</description></item>
        /// <item><description>A ground edge — an existential over a non-reserved role, in either direction
        /// and in either the enumeration or the <c>ObjectHasValue</c> spelling, whose filler conjunct spine
        /// carries a singleton nominal. The witness IS the named individual, so the edge runs between two
        /// concrete individuals; an inverse spelling exchanges that edge's endpoints and nothing else. Each
        /// nominal on the spine contributes its own edge.</description></item>
        /// <item><description>A typing — every remaining element, reported against the anchor it is asserted
        /// of. Groundness DESCENDS through a nominal (the filler's other conjuncts are asserted of the
        /// individual itself, and an existential among them grounds again) and does NOT descend through a
        /// nominal-free existential layer, whose filler is a genuine anonymous witness the caller keeps on
        /// its own path.</description></item>
        /// </list>
        /// An anchor reported as <see langword="null"/> is the walk's own subject, which the walk never
        /// names: the caller resolves it in whichever regime it works in.
        /// </summary>
        /// <param name="assertedClass">The asserted class expression to walk.</param>
        /// <param name="identitiesToAppendTo">The identity findings, each an anchor paired with the nominal term it is told to be.</param>
        /// <param name="groundEdgesToAppendTo">The ground-edge findings, each a role paired with its source and target terms in edge order.</param>
        /// <param name="typingsToAppendTo">The typing findings, each an anchor paired with the element asserted of it.</param>
        private static void GroundSpineWalk(
            OwlClassExpression assertedClass,
            List<(RdfTerm? Anchor, RdfTerm Nominal)> identitiesToAppendTo,
            List<(NamedNode Role, RdfTerm? Source, RdfTerm? Target)> groundEdgesToAppendTo,
            List<(RdfTerm? Anchor, OwlClassExpression Element)> typingsToAppendTo)
        {
            Stack<(RdfTerm? Anchor, OwlClassExpression Expression)> work = new();
            List<OwlClassExpression> spine = [];
            List<OwlClassExpression> fillerSpine = [];
            work.Push((null, assertedClass));

            while(work.Count > 0)
            {
                (RdfTerm? anchor, OwlClassExpression expression) = work.Pop();
                spine.Clear();
                CollectConjunctSpine(expression, spine);

                foreach(OwlClassExpression element in spine)
                {
                    if(element is OwlObjectOneOf { Individuals: [RdfTerm single] })
                    {
                        identitiesToAppendTo.Add((anchor, single));

                        continue;
                    }

                    if(element is OwlObjectHasValue hasValue && !IsReservedRole(hasValue.Property.Property.Iri))
                    {
                        groundEdgesToAppendTo.Add(hasValue.Property.IsInverse
                            ? (hasValue.Property.Property, hasValue.Individual, anchor)
                            : (hasValue.Property.Property, anchor, hasValue.Individual));

                        continue;
                    }

                    if(element is not OwlObjectSomeValuesFrom existential || IsReservedRole(existential.Property.Property.Iri))
                    {
                        typingsToAppendTo.Add((anchor, element));

                        continue;
                    }

                    fillerSpine.Clear();
                    CollectConjunctSpine(existential.Filler, fillerSpine);
                    if(!TryFirstSingletonNominal(fillerSpine, out RdfTerm descentAnchor, out OwlClassExpression anchorElement))
                    {
                        typingsToAppendTo.Add((anchor, element));

                        continue;
                    }

                    foreach(OwlClassExpression fillerElement in fillerSpine)
                    {
                        if(fillerElement is not OwlObjectOneOf { Individuals: [RdfTerm nominal] })
                        {
                            continue;
                        }

                        groundEdgesToAppendTo.Add(existential.Property.IsInverse
                            ? (existential.Property.Property, nominal, anchor)
                            : (existential.Property.Property, anchor, nominal));

                        if(!ReferenceEquals(fillerElement, anchorElement))
                        {
                            identitiesToAppendTo.Add((descentAnchor, nominal));
                        }
                    }

                    foreach(OwlClassExpression fillerElement in fillerSpine)
                    {
                        if(fillerElement is not OwlObjectOneOf { Individuals: [RdfTerm] })
                        {
                            work.Push(((RdfTerm?)descentAnchor, fillerElement));
                        }
                    }
                }
            }
        }

        /// <summary>Whether a conjunct spine carries a singleton nominal — the condition that makes an existential over it a ground edge onto a named individual rather than a demand for an anonymous witness.</summary>
        /// <param name="spine">The conjunct spine to test.</param>
        /// <returns><see langword="true"/> when some element is a one-individual <c>ObjectOneOf</c>.</returns>
        private static bool BearsSingletonNominal(List<OwlClassExpression> spine)
        {
            return TryFirstSingletonNominal(spine, out RdfTerm _, out OwlClassExpression _);
        }

        /// <summary>Finds the first singleton nominal on a conjunct spine — the individual a grounded filler's remaining conjuncts are asserted of, and the one every sibling nominal on the same spine folds with.</summary>
        /// <param name="spine">The conjunct spine to search.</param>
        /// <param name="nominal">The first nominal's individual term.</param>
        /// <param name="nominalElement">The spine element carrying it, so a caller can tell the anchor's own slot from a sibling's.</param>
        /// <returns><see langword="true"/> when the spine carries a singleton nominal.</returns>
        private static bool TryFirstSingletonNominal(List<OwlClassExpression> spine, out RdfTerm nominal, out OwlClassExpression nominalElement)
        {
            foreach(OwlClassExpression element in spine)
            {
                if(element is OwlObjectOneOf { Individuals: [RdfTerm single] })
                {
                    nominal = single;
                    nominalElement = element;

                    return true;
                }
            }

            nominal = default!;
            nominalElement = default!;

            return false;
        }

        /// <summary>Whether one asserted element is an existential <see cref="GroundSpineWalk"/> grounds — over a non-reserved role, in either direction, whose filler conjunct spine bears a singleton nominal, the <c>ObjectHasValue</c> spelling being the one-nominal case of it. The guard of <see cref="AssertType"/>'s ground arm, so the arm claims exactly the elements the walk decides and every other element takes the arms below.</summary>
        /// <param name="element">The asserted element.</param>
        /// <returns><see langword="true"/> when the element grounds onto named individuals.</returns>
        private static bool BearsGroundNominalSpine(OwlClassExpression element)
        {
            if(element is OwlObjectHasValue hasValue)
            {
                return !IsReservedRole(hasValue.Property.Property.Iri);
            }

            if(element is not OwlObjectSomeValuesFrom existential || IsReservedRole(existential.Property.Property.Iri))
            {
                return false;
            }

            List<OwlClassExpression> fillerSpine = [];
            CollectConjunctSpine(existential.Filler, fillerSpine);

            return BearsSingletonNominal(fillerSpine);
        }

        /// <summary>Resolves a ground-spine anchor to its atom: the walk's own subject for the anchor the walk reports as absent, and the individual's interned node for an anchor the walk descended onto.</summary>
        /// <param name="anchor">The reported anchor, or <see langword="null"/> for the walk's subject.</param>
        /// <param name="subject">The walk's subject atom.</param>
        /// <returns>The anchor's atom.</returns>
        private int AnchorAtom(RdfTerm? anchor, int subject)
        {
            return anchor is null ? subject : NominalAtom(anchor);
        }

        /// <summary>
        /// Appends the conjunct-spine elements of a class expression: the expression itself when it is not
        /// an intersection, and otherwise every non-intersection operand reachable through nested
        /// intersections, flattened over an explicit stack. One definition of "the conjunct spine", read by
        /// <see cref="GroundSpineWalk"/> for both the asserted class and every filler it descends into, so
        /// a spine shape one applier recognizes the others recognize too. A conjunction is
        /// polarity-preserving, so every element of the spine is asserted of the subject.
        /// </summary>
        /// <param name="root">The class expression to walk.</param>
        /// <param name="spineToAppendTo">The list the spine elements are appended to.</param>
        private static void CollectConjunctSpine(OwlClassExpression root, List<OwlClassExpression> spineToAppendTo)
        {
            Stack<OwlClassExpression> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlClassExpression current = work.Pop();
                if(current is OwlObjectIntersectionOf intersection)
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push(operand);
                    }

                    continue;
                }

                spineToAppendTo.Add(current);
            }
        }

        /// <summary>The individual key: a named individual by IRI, an anonymous one by label, an engine-minted one by its deterministic Skolem IRI.</summary>
        /// <param name="individual">The individual term.</param>
        /// <param name="key">The key.</param>
        /// <returns><see langword="false"/> for a term that is not an individual.</returns>
        private static bool TryIndividualKey(RdfTerm individual, out Utf8String key)
        {
            switch(individual)
            {
                case NamedNode named:
                    key = named.Iri;

                    return true;
                case BlankNode blank:
                    key = blank.Label;

                    return true;
                case EngineNode engine:
                    key = engine.SkolemIri();

                    return true;
                default:
                    key = default;

                    return false;
            }
        }

        /// <summary>Resolves an individual key through the <c>SameIndividual</c> merge map to its representative, compressing the path so repeated resolutions after a long chain of merges (a functional collapse of many successors onto one node) stay near-constant.</summary>
        /// <param name="key">The key to resolve.</param>
        /// <returns>The representative key.</returns>
        private Utf8String FindKey(Utf8String key)
        {
            Utf8String root = key;
            while(Merges.TryGetValue(root, out Utf8String parent) && !parent.Equals(root))
            {
                root = parent;
            }

            Utf8String current = key;
            while(Merges.TryGetValue(current, out Utf8String parent) && !parent.Equals(current))
            {
                Merges[current] = root;
                current = parent;
            }

            return root;
        }

        private static OwlClassReference BottomReference { get; } = new(new NamedNode(new Utf8String("http://www.w3.org/2002/07/owl#Nothing"u8.ToArray())));

        private static OwlClassReference TopReference { get; } = new(new NamedNode(new Utf8String("http://www.w3.org/2002/07/owl#Thing"u8.ToArray())));

        //Reduces one inclusion to normal forms with an explicit pending
        //queue; complex sides decompose monotonically through fresh atoms.
        private void NormalizeInclusion(OwlClassExpression left, OwlClassExpression right)
        {
            Queue<(OwlClassExpression Left, OwlClassExpression Right)> pending = new();
            pending.Enqueue((left, right));

            while(pending.Count > 0)
            {
                (OwlClassExpression currentLeft, OwlClassExpression currentRight) = pending.Dequeue();

                //Right-side intersections split; the conjuncts inherit the left side.
                if(currentRight is OwlObjectIntersectionOf rightIntersection)
                {
                    foreach(OwlClassExpression operand in rightIntersection.Operands)
                    {
                        pending.Enqueue((currentLeft, operand));
                    }

                    continue;
                }

                //A superclass ObjectHasValue(r, a) is the existential ∃r.{a}; rewrite it to the
                //enumeration form so the nominal flows through the same complex-filler machinery. The
                //property expression is carried unchanged, so an inverse spelling re-enqueues as ∃r⁻.{a}
                //and rides the same complex-filler naming, generator reduction and told-nominal path the
                //enumeration spelling does — the two spellings are one claim.
                if(currentRight is OwlObjectHasValue rightHasValue)
                {
                    pending.Enqueue((currentLeft, new OwlObjectSomeValuesFrom(rightHasValue.Property, new OwlObjectOneOf([rightHasValue.Individual]))));

                    continue;
                }

                //Right-side existential with a complex filler: name the filler. The property expression
                //is carried unchanged, so an inverse existential re-enqueues as ∃r⁻.F over the named
                //filler F and lands in the named-filler inverse arm below.
                if(currentRight is OwlObjectSomeValuesFrom { Filler: not OwlClassReference } rightExistential)
                {
                    int filler = FreshAtom();
                    pending.Enqueue((AtomReference(filler), rightExistential.Filler));
                    pending.Enqueue((currentLeft, new OwlObjectSomeValuesFrom(rightExistential.Property, AtomReference(filler))));

                    continue;
                }

                int subject = NameLeft(currentLeft);

                if(currentRight is OwlClassReference rightReference)
                {
                    AddToldSubsumption(subject, AtomOf(rightReference.Class.Iri));
                }
                else if(currentRight is OwlObjectSomeValuesFrom { Filler: OwlClassReference namedFiller } existential && !existential.Property.IsInverse)
                {
                    AddRightExistential(subject, RoleOf(existential.Property.Property), AtomOf(namedFiller.Class.Iri));
                }
                else if(currentRight is OwlObjectSomeValuesFrom { Filler: OwlClassReference inverseFiller, Property.IsInverse: true } inverseExistential && !IsReservedRole(inverseExistential.Property.Property.Iri))
                {
                    //∃r⁻.C on the superclass side reduces eagerly to the forward existential ∃g.C over the
                    //synthetic generator role of r (g ⊑ r⁻): the witness is the subject's r-PREDECESSOR,
                    //minted per-owner as a forward g-successor, and the mirror writes the real r-edge back
                    //onto the subject.
                    AddRightExistential(subject, GetOrMintGeneratorRole(RoleOf(inverseExistential.Property.Property)), AtomOf(inverseFiller.Class.Iri));
                }
                else if(currentRight is OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome && !IsReservedDataProperty(dataSome.Properties[0].Iri))
                {
                    //A superclass-side data existential is a value demand on the subject:
                    //a member carries a dp-value in the range, so an empty range empties it.
                    AddDataDemand(subject, dataSome.Properties[0].Iri, dataSome.Range);
                }
                else if(currentRight is OwlDataHasValue dataHas && !IsReservedDataProperty(dataHas.Property.Iri))
                {
                    //DataHasValue is the singleton-enumeration data existential.
                    AddDataDemand(subject, dataHas.Property.Iri, new OwlDataOneOf([dataHas.Value]));
                }
                else if(currentRight is OwlObjectHasSelf hasSelf)
                {
                    //A superclass-side self-restriction demands a reflexive edge on the subject. A
                    //self-edge is its own reverse, so ∃r⁻.Self holds of exactly the elements ∃r.Self does
                    //and the inverse spelling registers the same demand on the forward role.
                    AddSelfDemand(subject, RoleOf(hasSelf.Property.Property));
                }
                else if(currentRight is OwlObjectOneOf { Individuals.Count: 1 } singletonNominal)
                {
                    //A superclass-side singleton nominal {a} tells the subject it is the individual a.
                    //The subject is either a named class (A ⊑ {a}) or a fresh existential-filler proxy
                    //(the F of A ⊑ ∃r.{a}); both carry the individual atom as a told subsumer, and the
                    //liveness-gated merge pools the subject's constraints onto the real individual only
                    //once the subject is inhabited — so an uninhabited carrier never condemns the module.
                    AddToldSubsumption(subject, NominalAtom(singletonNominal.Individuals[0]));
                }
                else
                {
                    Unsupported.Add($"{currentRight.GetType().Name} on the superclass side is outside the EL classification calculus.");
                }
            }
        }

        //Names a subclass-side expression with an atom, emitting the
        //defining normal forms. Expressions nest arbitrarily, so the walk
        //runs post-order over an explicit stack: a node constructs once
        //every child has its atom.
        private int NameLeft(OwlClassExpression root)
        {
            Dictionary<OwlClassExpression, int> named = new(ReferenceEqualityComparer.Instance);
            Stack<OwlClassExpression> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlClassExpression node = work.Peek();
                if(named.ContainsKey(node))
                {
                    work.Pop();

                    continue;
                }

                switch(node)
                {
                    case OwlClassReference reference:
                    {
                        named[node] = AtomOf(reference.Class.Iri);
                        work.Pop();
                        break;
                    }
                    case OwlObjectIntersectionOf intersection:
                    {
                        bool ready = true;
                        foreach(OwlClassExpression operand in intersection.Operands)
                        {
                            if(!named.ContainsKey(operand))
                            {
                                ready = false;
                                work.Push(operand);
                            }
                        }

                        if(!ready)
                        {
                            break;
                        }

                        //Fold the conjunction pairwise through fresh atoms:
                        //A₁ ⊓ A₂ ⊑ F, F ⊓ A₃ ⊑ F′, …
                        int current = intersection.Operands.Count == 0 ? Top : named[intersection.Operands[0]];
                        for(int i = 1; i < intersection.Operands.Count; i++)
                        {
                            int folded = FreshAtom();
                            AddConjunction(current, named[intersection.Operands[i]], folded);
                            current = folded;
                        }

                        named[node] = current;
                        work.Pop();
                        break;
                    }
                    case OwlObjectSomeValuesFrom existential when !existential.Property.IsInverse:
                    {
                        if(!named.TryGetValue(existential.Filler, out int filler))
                        {
                            work.Push(existential.Filler);

                            break;
                        }

                        //∃r.C on the left: F with ∃r.C ⊑ F.
                        int fresh = FreshAtom();
                        AddLeftExistential(RoleOf(existential.Property.Property), filler, fresh);
                        named[node] = fresh;
                        work.Pop();
                        break;
                    }
                    case (OwlObjectSomeValuesFrom inverseExistential) when inverseExistential.Property.IsInverse:
                    {
                        if(!named.TryGetValue(inverseExistential.Filler, out int inverseFiller))
                        {
                            work.Push(inverseExistential.Filler);

                            break;
                        }

                        //∃r⁻.C on the left reduces to an ordinary left existential over the
                        //synthetic mirror role of r: every r-edge (x, y) forces the mirror edge
                        //(y, x), so a node has an r-predecessor in C exactly when it has a
                        //mirror-successor in C — F with ∃mirror(r).C ⊑ F.
                        int inverseFresh = FreshAtom();
                        AddLeftExistential(GetOrMintMirrorRole(RoleOf(inverseExistential.Property.Property)), inverseFiller, inverseFresh);
                        named[node] = inverseFresh;
                        work.Pop();
                        break;
                    }
                    case (OwlObjectHasSelf hasSelf):
                    {
                        //∃r.Self on the left: a fresh atom F with ∃self(r) ⊑ F. A genuine
                        //self-edge on r licenses F; an ordinary r-successor never does. A self-edge is
                        //its own reverse, so ∃r⁻.Self consumes the same edge on the same forward role.
                        int fresh = FreshAtom();
                        AddSelfElimination(RoleOf(hasSelf.Property.Property), fresh);
                        named[node] = fresh;
                        work.Pop();
                        break;
                    }
                    case (OwlObjectOneOf { Individuals.Count: 1 } singletonNominal):
                    {
                        //A singleton nominal {a} on the left names the individual node a
                        //itself — a subclass concept that holds of exactly the one individual.
                        named[node] = NominalAtom(singletonNominal.Individuals[0]);
                        work.Pop();
                        break;
                    }
                    case (OwlObjectHasValue hasValue):
                    {
                        //∃r.{a} on the left: a fresh atom F with ∃r.{a} ⊑ F, so any node with
                        //a genuine r-edge to the individual a gains F — a left existential
                        //keyed on the individual node, which self-seeds and so fires on every
                        //asserted or composed edge into it. ∃r⁻.{a} is that same left existential over
                        //the synthetic mirror role of r — every r-edge forces the reverse mirror edge, so
                        //a node has a as an r-predecessor exactly when it has a as a mirror-successor.
                        int fresh = FreshAtom();
                        int hasValueRole = RoleOf(hasValue.Property.Property);
                        AddLeftExistential(hasValue.Property.IsInverse ? GetOrMintMirrorRole(hasValueRole) : hasValueRole, NominalAtom(hasValue.Individual), fresh);
                        named[node] = fresh;
                        work.Pop();
                        break;
                    }
                    case OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome when !IsReservedDataProperty(dataSome.Properties[0].Iri):
                    {
                        //∃d.R on the left: a fresh atom F with ∃d.R ⊑ F. The concrete leaf has no node of
                        //its own, so the occurrence is registered for recognition instead of an edge rule —
                        //an atom whose own value demands force a d-value inside R is told F.
                        int fresh = FreshAtom();
                        LeftDataExistentials.Add(new LeftDataExistential(dataSome.Properties[0].Iri, dataSome.Range, fresh));
                        named[node] = fresh;
                        work.Pop();
                        break;
                    }
                    case OwlDataHasValue dataHas when !IsReservedDataProperty(dataHas.Property.Iri):
                    {
                        //DataHasValue is the singleton-enumeration data existential, so ∃d.{v} ⊑ F is the
                        //same left occurrence over the one-literal range the value denotes.
                        int fresh = FreshAtom();
                        LeftDataExistentials.Add(new LeftDataExistential(dataHas.Property.Iri, new OwlDataOneOf([dataHas.Value]), fresh));
                        named[node] = fresh;
                        work.Pop();
                        break;
                    }
                    default:
                    {
                        Unsupported.Add($"{node.GetType().Name} is outside the EL classification calculus.");
                        named[node] = FreshAtom();
                        work.Pop();
                        break;
                    }
                }
            }

            return named[root];
        }

        private void NormalizeChain(OwlPropertyChainAxiom chain)
        {
            List<int> links = [];
            foreach(OwlObjectPropertyExpression link in chain.Chain)
            {
                if(link.IsInverse || chain.SuperProperty.IsInverse)
                {
                    Unsupported.Add("ObjectInverseOf in a property chain is outside EL.");

                    return;
                }

                links.Add(RoleOf(link.Property));
            }

            int conclusion = RoleOf(chain.SuperProperty.Property);

            if(links.Count == 1)
            {
                AddRoleSubsumption(links[0], conclusion);

                return;
            }

            //Longer chains decompose left-associatively with fresh roles:
            //r₁∘r₂ ⊑ f₁, f₁∘r₃ ⊑ f₂, …, fₙ₋₁∘rₙ ⊑ s.
            int current = links[0];
            for(int i = 1; i < links.Count; i++)
            {
                int target = i == links.Count - 1 ? conclusion : FreshRole();
                AddChain(current, links[i], target);
                current = target;
            }
        }

        //Saturation.

        /// <summary>
        /// Routes every right-existential over a range-bearing role to a fresh
        /// successor atom told to be the original filler, so a property range types
        /// that anonymous successor at saturation and never the named filler class.
        /// Without it, an existential's edge targets the shared named filler node, so
        /// the range rule would write the range concept onto the filler class
        /// everywhere it is used — a contamination that can flip a sound consistency
        /// verdict. A role is range-bearing when it, or any role it is a subrole of,
        /// carries a range, because the role hierarchy promotes a subrole edge to its
        /// superrole and the superrole's range then fires on the shared target.
        /// Range-free modules are untouched: nothing else writes to an edge target's
        /// subsumer set, so this is a no-op on their classification and costs nothing.
        /// </summary>
        private void RewriteRangeBearingExistentials()
        {
            if(RangesByRole.Count == 0)
            {
                return;
            }

            HashSet<int> rangeBearing = ComputeRangeBearingRoles();

            //One shared successor per (role, filler) — the canonical "filler under
            //this role's range" — the same sharing the named filler node already had.
            Dictionary<(int Role, int Filler), int> successors = [];
            foreach(KeyValuePair<int, List<(int Role, int Filler)>> entry in RightExistentials)
            {
                List<(int Role, int Filler)> existentials = entry.Value;
                for(int index = 0; index < existentials.Count; index++)
                {
                    (int role, int filler) = existentials[index];
                    if(!rangeBearing.Contains(role))
                    {
                        continue;
                    }

                    if(!successors.TryGetValue((role, filler), out int successor))
                    {
                        successor = FreshAtom();
                        AddToldSubsumption(successor, filler);
                        successors[(role, filler)] = successor;
                    }

                    existentials[index] = (role, successor);
                }
            }
        }

        /// <summary>
        /// The roles whose existential successors must carry a range: every role that
        /// carries a range directly, and every subrole — reflexive-transitively — of
        /// such a role, since the role hierarchy promotes a subrole edge to its
        /// superrole and the superrole's range then fires on the shared target.
        /// </summary>
        /// <returns>The range-bearing role ids.</returns>
        private HashSet<int> ComputeRangeBearingRoles()
        {
            //Reverse the subrole→superrole edges so a range on a superrole pushes down
            //to all of its subroles.
            Dictionary<int, List<int>> subRolesBySuper = BuildSubRolesBySuper();

            HashSet<int> bearing = [];
            Stack<int> work = new();
            foreach(int role in RangesByRole.Keys)
            {
                if(bearing.Add(role))
                {
                    work.Push(role);
                }
            }

            while(work.Count > 0)
            {
                int role = work.Pop();
                if(subRolesBySuper.TryGetValue(role, out List<int>? subs))
                {
                    foreach(int sub in subs)
                    {
                        if(bearing.Add(sub))
                        {
                            work.Push(sub);
                        }
                    }
                }
            }

            return bearing;
        }

        /// <summary>
        /// Decides each collected data demand by its range's value-space emptiness and
        /// seeds the result: an empty range tells its atom <c>⊑ ⊥</c> (the saturation's
        /// bottom-propagation then carries the unsatisfiability through the closure),
        /// while a range the checker cannot decide records the undecided marker so the
        /// coupled reasoner delegates the module rather than trust a fragment-relative
        /// verdict. A satisfiable range imposes nothing. This mirrors the tableau, which
        /// decides the same single-property data leaf with the same value-space checker,
        /// so the verdicts agree on the admitted fragment.
        /// </summary>
        /// <remarks>
        /// The decision is PER ATOM: one call carries exactly the demands
        /// <see cref="DataDemands"/> records against that atom, so the sidecar's functional
        /// pooling sees one carrier's demands at a time. Demands that meet only through a
        /// subsumption between two distinct carriers — where the subsumee inherits both and a
        /// functional property forces them onto one value — are outside that call's scope, and
        /// <see cref="FenceMultiCarrierFunctionalPooling"/> delegates any module where such a
        /// meeting is possible.
        /// </remarks>
        private void SeedDataDemands()
        {
            foreach(KeyValuePair<int, List<DataDemand>> entry in DataDemands)
            {
                int atom = entry.Key;
                List<DataDemand> demands = entry.Value;

                //§1.3 joint satisfiability of the atom's data demands against the
                //RBox: a jointly-empty value space — from functional pooling,
                //disjointness, super-property range inheritance, or a single empty
                //range — tells the atom ⊑ ⊥, so the bottom propagation carries the
                //unsatisfiability to its subsumees, individuals, and consistency; an
                //undecided obligation scopes the classification to the modelled
                //subset. With the empty box this is the per-range emptiness check
                //byte-for-byte, since a demand carries no node universal in EL.
                List<AlcConcept> concepts = new(demands.Count);
                foreach(DataDemand demand in demands)
                {
                    concepts.Add(new AlcDataSome(demand.Property, demand.Range));
                }

                DataConsistencyStatus status = DataRestrictionConsistency.Decide(concepts, Box, gate: null, Registry, out _, out bool selfCertified);
                if(selfCertified && !Unsupported.Contains(DataRestrictionConsistency.SelfCertifiedMarker))
                {
                    Unsupported.Add(DataRestrictionConsistency.SelfCertifiedMarker);
                }

                switch(status)
                {
                    case DataConsistencyStatus.Clash:
                        AddToldSubsumption(atom, Bottom);
                        break;
                    case DataConsistencyStatus.Undecided:
                        Unsupported.Add(DataRestrictionConsistency.UndecidedMarker);
                        break;
                    default:
                        break;
                }

                //DataPropertyDomain firing: a class carrying a demand on a domain
                //property — or on a sub-property of one, through the box closure — is
                //told it is the domain class.
                if(DataDomainConclusions.Count == 0)
                {
                    continue;
                }

                foreach(DataDemand demand in demands)
                {
                    foreach((Utf8String domainProperty, int conclusion) in DataDomainConclusions)
                    {
                        if(Box.IsSuperOrSelf(demand.Property, domainProperty))
                        {
                            AddToldSubsumption(atom, conclusion);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recognizes each left-position data existential on the atoms whose own value demands
        /// entail it: for a carrier <c>X</c> with demands <c>D(X)</c> and an occurrence
        /// <c>∃d.R ⊑ F</c>, the entailment <c>D(X) ⊨ ∃d.R</c> is decided as the joint
        /// unsatisfiability of <c>D(X)</c> with <c>∀d.¬R</c> — a clash means every model of
        /// <c>X</c> carries a <c>d</c>-value inside <c>R</c>, so <c>X ⊑ F</c> is told and the
        /// saturation carries it onward exactly as any other told subsumption.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The test runs against the module <see cref="Box"/>, so a demand on a sub-property or on
        /// an equivalent property of the occurrence's property is constrained by the occurrence's
        /// universal through the same super-property closure the demand decision uses, and a
        /// functional property pools the carrier's own demands. A range the checker cannot decide
        /// records the undecided marker once, so the coupled reasoner delegates the module rather
        /// than trust a recognition it could not test; a satisfiable joint space tells nothing,
        /// which is the sound direction — a missing recognition only weakens the closure.
        /// </para>
        /// <para>
        /// Carriers with no demand are not enumerated: <see cref="DataDemands"/> holds a bucket
        /// only for an atom that carries at least one demand, and an atom with no demand entails no
        /// value at all. A carrier whose demands are already jointly empty is told <c>⊥</c> by
        /// <see cref="SeedDataDemands"/> and clashes here too, so it gains the occurrence's atom as
        /// well — sound, since <c>⊥</c> is below every atom.
        /// </para>
        /// </remarks>
        private void SeedDataRecognitions()
        {
            if(LeftDataExistentials.Count == 0)
            {
                return;
            }

            foreach(KeyValuePair<int, List<DataDemand>> entry in DataDemands)
            {
                int atom = entry.Key;
                List<DataDemand> demands = entry.Value;

                List<AlcConcept> concepts = new(demands.Count + 1);
                foreach(DataDemand demand in demands)
                {
                    concepts.Add(new AlcDataSome(demand.Property, demand.Range));
                }

                foreach(LeftDataExistential recognition in LeftDataExistentials)
                {
                    //The occurrence's negation is the one concept that varies across the sweep, so it
                    //is appended and dropped around each call and the carrier's demand concepts are
                    //built once.
                    concepts.Add(new AlcDataAll(recognition.Property, new OwlDataComplementOf(recognition.Range)));
                    DataConsistencyStatus status = DataRestrictionConsistency.Decide(concepts, Box, gate: null, Registry, out _, out bool selfCertified);
                    concepts.RemoveAt(concepts.Count - 1);

                    if(selfCertified && !Unsupported.Contains(DataRestrictionConsistency.SelfCertifiedMarker))
                    {
                        Unsupported.Add(DataRestrictionConsistency.SelfCertifiedMarker);
                    }

                    switch(status)
                    {
                        case DataConsistencyStatus.Clash:
                            AddToldSubsumption(atom, recognition.Conclusion);
                            break;
                        case DataConsistencyStatus.Undecided:
                            if(!Unsupported.Contains(DataRestrictionConsistency.UndecidedMarker))
                            {
                                Unsupported.Add(DataRestrictionConsistency.UndecidedMarker);
                            }

                            break;
                        default:
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Delegates a module whose functional data property can pool value demands carried by two
        /// or more distinct classes: a subsumee of both carriers inherits both demands, and
        /// functionality forces them onto ONE value, which the per-carrier demand decision never
        /// tests. For each functional property the carriers are the atoms bearing at least one
        /// demand whose property lies in that property's pooled cone — the same
        /// <see cref="DataPropertyBox.IsSuperOrSelf"/> sweep the sidecar's functional pool runs, so
        /// a demand reaching the functional property through a sub-property or an equivalence
        /// counts — and two or more of them record one marker, on which the coupled reasoner
        /// delegates the whole module.
        /// </summary>
        /// <remarks>
        /// The fence is unconditional on <see cref="LeftDataExistentials"/>: the hazard needs only
        /// two carriers whose demands meet through a subsumption, which a module states with no
        /// left-position occurrence anywhere. A SINGLE carrier is not fenced — one carrier's demands
        /// are pooled soundly and completely by the sidecar's own functional pool inside the single
        /// <see cref="SeedDataDemands"/> call — and neither is a functional property whose cone no
        /// demand reaches. Abstention is the only direction the fence moves the verdict in: the
        /// module goes to the general decider, which reads the demands jointly.
        /// </remarks>
        private void FenceMultiCarrierFunctionalPooling()
        {
            foreach(Utf8String functional in Box.FunctionalProperties)
            {
                int carriers = 0;
                foreach(KeyValuePair<int, List<DataDemand>> entry in DataDemands)
                {
                    foreach(DataDemand demand in entry.Value)
                    {
                        if(Box.IsSuperOrSelf(demand.Property, functional))
                        {
                            carriers++;

                            break;
                        }
                    }
                }

                if(carriers >= 2)
                {
                    Unsupported.Add("A functional data property pooling value demands across distinct classes is outside the EL classification calculus.");

                    return;
                }
            }
        }

        /// <summary>Saturates the completion rules to fixpoint over the work queue.</summary>
        public void Saturate(CancellationToken cancellationToken)
        {
            //Sound ranges: route every existential over a range-bearing role to a
            //fresh successor before seeding, so a range types the successor and never
            //the named filler class. Must precede the seeding loop so the fresh
            //successor atoms are seeded with self and ⊤.
            RewriteRangeBearingExistentials();

            //Fence the one demand configuration the per-carrier decision cannot see — a functional
            //data property pooling demands carried by two or more distinct classes — before any
            //demand is decided, so the module is delegated rather than answered from a partial pool.
            FenceMultiCarrierFunctionalPooling();

            //Seed empty data demands as told ⊥ before the init loop, so the very first
            //ProcessSubsumer(a, a) fires the told subsumption and the existing
            //bottom-propagation carries the unsatisfiability to subsumers, individuals,
            //and overall consistency to fixpoint — no second pass.
            SeedDataDemands();

            //Recognize the left-position data existentials on the demand carriers that entail them,
            //as told subsumptions seeded before the init loop for the same reason.
            SeedDataRecognitions();

            //Seed the inhabitation roots: every ABox individual is forced to exist, and ⊤ is the
            //non-empty-domain witness. Liveness then flows forward along edges as they are added,
            //so a class carrier becomes live exactly when an inhabited node forces an edge into it.
            Live.Add(Top);
            foreach(int individual in IndividualAtoms)
            {
                Live.Add(individual);
            }

            //Initialise: every atom subsumes itself and ⊤.
            for(int atom = 0; atom < AtomNames.Count; atom++)
            {
                EnqueueSubsumer(atom, atom);
                EnqueueSubsumer(atom, Top);
            }

            //Seed the asserted ABox edges (empty on the TBox-classification path).
            foreach((int role, int source, int target) in AssertedEdges)
            {
                EnqueueEdge(role, source, target);
            }

            while(Work.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Processed++;

                WorkItem item = Work.Dequeue();

                switch(item.Kind)
                {
                    case WorkKind.Edge:
                        ProcessEdge(item.Role, item.Subject, item.Target);
                        break;
                    case WorkKind.Liveness:
                        ProcessLiveness(item.Subject);
                        break;
                    default:
                        ProcessSubsumer(item.Subject, item.Atom);
                        break;
                }
            }
        }

        //S(C) gains A: told subsumptions, conjunctions, right existentials,
        //and the left-existential join over incoming edges fire.
        private void ProcessSubsumer(int subject, int atom)
        {
            HashSet<int> subjectSubsumers = SubsumersOf(subject);

            //Told subsumption: A ⊑ B.
            if(ToldSubsumptions.TryGetValue(atom, out List<int>? told))
            {
                foreach(int b in told)
                {
                    EnqueueSubsumer(subject, b);
                }
            }

            //Conjunction: A ⊓ A′ ⊑ B with A′ already present.
            if(ConjunctionsByAtom.TryGetValue(atom, out List<(int Other, int Conclusion)>? conjunctions))
            {
                foreach((int other, int conclusion) in conjunctions)
                {
                    if(subjectSubsumers.Contains(other))
                    {
                        EnqueueSubsumer(subject, conclusion);
                    }
                }
            }

            //Right existential: A ⊑ ∃r.B introduces an edge from the owner `subject` to the filler's
            //V-node. A role admitted for inverse minting mints that V-node under the module's witness
            //regime.
            //
            //Under SHARED keys the witness is interned on the filler core and the role mark alone — no
            //owner term, no inheritance — so every owner of one (role, filler) pair reaches ONE node
            //denoting the canonical element of that content class, and the witness population is bounded by
            //the distinct pairs rather than by the owner count. Every saturation write onto that node is a
            //consequence of the axioms about that one element, so serving several owners from it draws no
            //conclusion an owner's own position does not warrant; a mirrored ⊥ travelling back from an
            //owner is the one write that is position-specific, and the mint-edge ledger suppresses exactly
            //that. The mark is never the empty demand set, which is what keeps the mint off the shared
            //filler node the pre-mint calculus used. An owner may be a named class or an individual, since
            //R-EXIST fires on any subject whose subsumer set reaches the existential.
            //
            //Under PER-OWNER keys each owner gets a witness decorated with
            //its provenance (∃r⁻ of the owner's core) plus the OWNER's own inherited demand set, so the
            //inverse mirror over the witness stays owner-local and one owner's ⊥ cannot empty another's. The
            //inheritance is load-bearing: without it two distinct-owner witnesses that share a filler core
            //mint the SAME onward successor (its demand keyed by the core alone), and a backward inverse
            //clash reaching one owner's branch then empties every co-folded owner through that shared
            //successor — a false inconsistency. A cyclic re-derivation by the same owner adds no new
            //decoration, so its demand set saturates over the fixed signature and the witness still folds
            //and terminates. The soundness invariant is unique ownership: every witness in a decided module
            //has exactly one creating owner (or is its own, the cyclic self-model), so a mirror edge points
            //only at the witness's true predecessor and a backward consumer reads only that owner. The
            //demand set enforces it for any two owners whose sets differ; mutually recursive cross-core
            //existentials can make two distinct owners' sets coincide (set union is order-blind), so a mint
            //that returns another owner's witness abstains the module to the general decider instead of
            //accepting the fold. An ordinary (uncoupled) role has no backward demand, so the edge targets
            //the plain filler.
            if(RightExistentials.TryGetValue(atom, out List<(int Role, int Filler)>? existentials))
            {
                foreach((int role, int filler) in existentials)
                {
                    if(!CoupledRoles.Contains(role))
                    {
                        EnqueueEdge(role, subject, filler);

                        continue;
                    }

                    if(SharedWitnessKeys)
                    {
                        int shared = MintOwnedWitness(filler, [MarkDemand(role)], subject);
                        RecordMintEdge(shared, role, subject);
                        EnqueueEdge(role, subject, shared);

                        continue;
                    }

                    ImmutableArray<long> ownerDemands = DescrByNode.TryGetValue(subject, out VNodeDescriptor ownerDescriptor)
                        ? ownerDescriptor.Demands
                        : ImmutableArray<long>.Empty;
                    int witness = MintOwnedWitness(filler, CanonicalizeDemands(ownerDemands, PackDemand(role, CoreLabelOf(subject))), subject);

                    EnqueueEdge(role, subject, witness);
                }
            }

            //Self demand: A ⊑ ∃r.Self introduces a reflexive edge (r, subject, subject).
            if(SelfDemands.TryGetValue(atom, out List<int>? selfRoles))
            {
                foreach(int role in selfRoles)
                {
                    EnqueueEdge(role, subject, subject);
                }
            }

            //Left-existential join over INCOMING edges: (C,subject) ∈ R(r)
            //and ∃r.atom ⊑ B puts B into S(C); ⊥ propagates backwards the
            //same way. The join runs over the roles the incoming-role index records for the subject —
            //exactly the roles with an edge into it, which is exactly the set that can match — so a subject
            //with no incoming edge does no work here and the per-event cost is independent of how many
            //roles the module uses. The index mirrors EdgesByTarget's key set, so the source list is an
            //indexer read.
            if(IncomingRoles.TryGetValue(subject, out List<int>? incomingRoles))
            {
                foreach(int incomingRole in incomingRoles)
                {
                    List<int> sources = EdgesByTarget[(incomingRole, subject)];

                    if(LeftExistentials.TryGetValue((incomingRole, atom), out List<int>? conclusions))
                    {
                        //The arriving atom is the whole trigger set of this join, so the batch offered to the
                        //backward-demand consumption is one pair per conclusion; a deposit the consumption does
                        //not claim is an ordinary forward, ground, or shared-filler conclusion and lands plainly.
                        BackwardTriggerAtoms.Clear();
                        BackwardTriggerConclusions.Clear();
                        foreach(int conclusion in conclusions)
                        {
                            BackwardTriggerAtoms.Add(atom);
                            BackwardTriggerConclusions.Add(conclusion);
                        }

                        foreach(int source in sources)
                        {
                            if(TryConsumeBackwardDemands(incomingRole, source, subject, BackwardTriggerAtoms, BackwardTriggerConclusions, out int _))
                            {
                                continue;
                            }

                            foreach(int conclusion in conclusions)
                            {
                                EnqueueSubsumer(source, conclusion);
                            }
                        }
                    }

                    //Bottom propagation backwards over the incoming edges, one source at a time so the
                    //shared regime's witness-to-owner suppression applies per source: a witness of THIS
                    //subject keeps its ⊥ (its position under an empty owner is vacuous), while every other
                    //source — an ordinary predecessor, or a witness of some other node — receives it.
                    if(atom == Bottom)
                    {
                        foreach(int source in sources)
                        {
                            if(IsOwnerDirectedEdge(source, subject))
                            {
                                continue;
                            }

                            EnqueueSubsumer(source, Bottom);
                        }
                    }
                }
            }

            //Nominal merge. An individual atom in a node's subsumer set means the node is told to be
            //that individual (N ⊑ {a}). Direction 1 (always): the node absorbs everything the
            //individual necessarily is — pure subsumption, written into a class or proxy node, where a
            //resulting ⊥ only empties that node. Direction 2 (only when the node is live, i.e.
            //reachable from a genuine individual or the non-empty domain): the real individual absorbs
            //the node's constraints — the single path by which a class carrier may bear on an
            //individual's consistency, gated precisely so an uninhabited carrier cannot.
            if(IndividualAtoms.Contains(atom))
            {
                foreach(int x in (int[])[.. SubsumersOf(atom)])
                {
                    EnqueueSubsumer(subject, x);
                }

                if(Live.Contains(subject))
                {
                    foreach(int x in (int[])[.. subjectSubsumers])
                    {
                        EnqueueSubsumer(atom, x);
                    }
                }
            }

            //The individual `subject` gained a subsumer: every node told to be it inherits the
            //subsumer (Direction 1, as the individual's own constraints grow).
            if(IndividualAtoms.Contains(subject) && NominalCarriers.TryGetValue(subject, out List<int>? carriers))
            {
                foreach(int carrier in carriers)
                {
                    EnqueueSubsumer(carrier, atom);
                }
            }

            //A live node gained a subsumer: it flows to every individual the node is told to be
            //(Direction 2, as the live carrier's constraints grow).
            if(Live.Contains(subject) && NominalsAt.TryGetValue(subject, out List<int>? nominals))
            {
                foreach(int nominal in nominals)
                {
                    EnqueueSubsumer(nominal, atom);
                }
            }
        }

        //R(r) gains (source, target): hierarchy, ranges, the
        //left-existential join, bottom propagation, and compositions fire.
        private void ProcessEdge(int role, int source, int target)
        {
            //Role hierarchy: r ⊑ s.
            if(RoleSubsumptions.TryGetValue(role, out List<int>? superRoles))
            {
                foreach(int super in superRoles)
                {
                    EnqueueEdge(super, source, target);
                }
            }

            //Inverse pairing: a paired role's edge (a, b) forces its reverse (b, a) under each inverse
            //role. SymmetricObjectProperty registers a self-pairing and InverseObjectProperties a
            //mutual pairing; the mirror fires on every edge however derived — asserted, promoted,
            //composed, or itself a reverse — so chained pairings (r⁻ = s, s⁻ = t) and mixed
            //symmetric/inverse roles close completely, and the Edges-set dedup terminates the mutual
            //reverse. The gate confines every paired role to asserted ground edges and per-owner minted
            //witness edges, so the reverse target is a genuine role atom or an owner-local witness —
            //never a shared filler another owner's constraints could leak through.
            if(InversePairs.TryGetValue(role, out List<int>? inverseRoles))
            {
                foreach(int inverseRole in inverseRoles)
                {
                    EnqueueEdge(inverseRole, target, source);
                }
            }

            //Self elimination: a GENUINE self-edge (source == target) on r licenses
            //∃self(r) ⊑ B. The source == target fence is the soundness guard — an
            //ordinary r-successor, which only satisfies ∃r.⊤, must never fire it.
            if(source == target && SelfEliminations.TryGetValue(role, out List<int>? selfConclusions))
            {
                foreach(int conclusion in selfConclusions)
                {
                    EnqueueSubsumer(source, conclusion);
                }
            }

            //Ranges: the target gains r's range atoms. A range write onto a real individual node is
            //gated on the source being live — a hypothetical edge from an uninhabited class carrier
            //must not type a forced-existing individual (the documented superclass-nominal leak). A
            //class or proxy target is always typed; its constraints reach the individual only through
            //the liveness-gated nominal merge.
            if(RangesByRole.TryGetValue(role, out List<int>? ranges) && (!IndividualAtoms.Contains(target) || Live.Contains(source)))
            {
                foreach(int range in ranges)
                {
                    EnqueueSubsumer(target, range);
                }
            }

            //Left-existential elimination over the target's current subsumers — snapshotted, because a
            //self-edge makes the source and target sets the same instance. The whole match set of this edge
            //is collected first and offered to the backward-demand consumption as ONE batch, so a witness
            //carrying several simultaneous triggers refines once instead of exploring the subset lattice of
            //its triggers; a deposit the consumption does not claim lands plainly.
            BackwardTriggerAtoms.Clear();
            BackwardTriggerConclusions.Clear();
            foreach(int atom in (int[])[.. SubsumersOf(target)])
            {
                if(LeftExistentials.TryGetValue((role, atom), out List<int>? conclusions))
                {
                    foreach(int conclusion in conclusions)
                    {
                        BackwardTriggerAtoms.Add(atom);
                        BackwardTriggerConclusions.Add(conclusion);
                    }
                }
            }

            if(BackwardTriggerConclusions.Count > 0 && !TryConsumeBackwardDemands(role, source, target, BackwardTriggerAtoms, BackwardTriggerConclusions, out int _))
            {
                foreach(int conclusion in BackwardTriggerConclusions)
                {
                    EnqueueSubsumer(source, conclusion);
                }
            }

            //Bottom propagation: an edge into an unsatisfiable target, unless the edge runs from a shared
            //witness to one of the owners that minted it.
            if(SubsumersOf(target).Contains(Bottom) && !IsOwnerDirectedEdge(source, target))
            {
                EnqueueSubsumer(source, Bottom);
            }

            //Composition r₁∘r₂ ⊑ s, this edge as the FIRST link.
            if(ChainsByFirst.TryGetValue(role, out List<(int Second, int Conclusion)>? asFirst))
            {
                foreach((int second, int conclusion) in asFirst)
                {
                    if(EdgesBySource.TryGetValue((second, target), out List<int>? continuations))
                    {
                        foreach(int end in continuations)
                        {
                            EnqueueEdge(conclusion, source, end);
                        }
                    }
                }
            }

            //Composition with this edge as the SECOND link.
            if(ChainsBySecond.TryGetValue(role, out List<(int First, int Conclusion)>? asSecond))
            {
                foreach((int first, int conclusion) in asSecond)
                {
                    if(EdgesByTarget.TryGetValue((first, source), out List<int>? starts))
                    {
                        foreach(int start in starts)
                        {
                            EnqueueEdge(conclusion, start, target);
                        }
                    }
                }
            }
        }

        //A node became live. Two consequences, both monotone and order-independent with the
        //<see cref="ProcessSubsumer"/> triggers (the liveness set and the nominal sets only grow):
        //pool the node's constraints onto every individual it is told to be (the gated half of the
        //nominal merge), and propagate liveness forward to the node's existing successors.
        private void ProcessLiveness(int node)
        {
            //The gated merge: whichever of {node live, nominal present} is established second fires it.
            if(NominalsAt.TryGetValue(node, out List<int>? nominals))
            {
                int[] subsumers = [.. SubsumersOf(node)];
                foreach(int nominal in nominals)
                {
                    foreach(int x in subsumers)
                    {
                        EnqueueSubsumer(nominal, x);
                    }
                }
            }

            //Forward liveness over edges that already existed: a successor of a now-inhabited node is
            //itself inhabited. <see cref="EnqueueEdge"/> covers edges created once the source is
            //already live; this covers the edges a class node fired from its own self-subsumer in the
            //init loop, before the carrier was inhabited by a later-processed individual. An
            //owner-directed edge is skipped: it is the mirror of a mint edge, so it says the TARGET has
            //this node as a successor, not the other way round, and the target's own inhabitation must
            //come from its own predecessors.
            if(OutgoingTargets.TryGetValue(node, out List<int>? targets))
            {
                foreach(int target in (int[])[.. targets])
                {
                    if(IsOwnerDirectedEdge(node, target))
                    {
                        continue;
                    }

                    MarkLive(target);
                }
            }
        }

        //Result.

        /// <summary>Projects the saturation onto the user-named classes.</summary>
        public ElClassification BuildResult()
        {
            Dictionary<Utf8String, IReadOnlySet<Utf8String>> result = [];
            HashSet<Utf8String> unsatisfiable = [];

            foreach(int atom in NamedAtoms)
            {
                if(AtomNames[atom] is not Utf8String name)
                {
                    continue;
                }

                HashSet<Utf8String> named = [];
                bool isUnsatisfiable = false;
                foreach(int subsumer in SubsumersOf(atom))
                {
                    if(subsumer == Bottom)
                    {
                        isUnsatisfiable = true;
                    }

                    if(AtomNames[subsumer] is Utf8String subsumerName && (NamedAtoms.Contains(subsumer) || subsumer == Top))
                    {
                        named.Add(subsumerName);
                    }
                }

                result[name] = named;
                if(isUnsatisfiable)
                {
                    unsatisfiable.Add(name);
                }
            }

            return new ElClassification(result, unsatisfiable, Unsupported);
        }

        /// <summary>
        /// Builds the module-consistency result: the named-class classification
        /// together with the consistency verdict. The module is inconsistent
        /// exactly when <c>owl:Thing</c> is unsatisfiable (<c>⊥ ∈ S(⊤)</c>, so no
        /// non-empty model exists — OWL 2 DL requires a non-empty domain) or some
        /// named individual is forced empty (<c>⊥ ∈ S(individual)</c>). The ABox
        /// does not contaminate the named-class projection: a class assertion adds
        /// to the individual's subsumers, never the class's, and asserted edges and
        /// bottom-propagation stay within the individual and anonymous-successor
        /// graph — so the named-class classification matches the TBox-only one.
        /// </summary>
        /// <returns>The module classification.</returns>
        public ElModuleClassification BuildModuleResult()
        {
            ElClassification classification = BuildResult();

            bool isConsistent = !HasDerivedInconsistency();

            int edges = 0;
            foreach(KeyValuePair<int, HashSet<(int Source, int Target)>> roleEdges in Edges)
            {
                edges += roleEdges.Value.Count;
            }

            return new ElModuleClassification(isConsistent, classification, Processed, edges);
        }

        //Interning and index helpers.

        private OwlClassReference AtomReference(int atom)
        {
            //Fresh atoms get a synthetic IRI node only for re-entry into the
            //normalization queue; it never reaches the result.
            Utf8String name = AtomNames[atom] ?? Utf8Strings.From($"urn:veritas:el:{atom}");
            AtomNames[atom] ??= name;
            AtomIds[name] = atom;

            return new OwlClassReference(new NamedNode(name));
        }

        private int AtomOf(Utf8String iri)
        {
            if(AtomIds.TryGetValue(iri, out int existing))
            {
                return existing;
            }

            int atom = AtomNames.Count;
            AtomNames.Add(iri);
            AtomIds[iri] = atom;
            NamedAtoms.Add(atom);

            return atom;
        }

        private int FreshAtom()
        {
            int atom = AtomNames.Count;
            AtomNames.Add(null);

            return atom;
        }

        /// <summary>
        /// Interns a V-node — a (core atom, backward-demand set) description — to a node id. An empty
        /// demand set is the description of the core class itself, so it returns the core atom unchanged
        /// and interns nothing: an existential over an inverse-free role targets its plain filler exactly
        /// as before. A non-empty description mints a fresh atom the first time it is seen and seeds it
        /// mid-loop as the initialisation loop (which has already run) seeds every pre-existing atom — it
        /// subsumes itself and <see cref="Top"/>, and is told to be its core so the core's consequences
        /// flow onto it. Interning is keyed on the whole grown description so distinct owners get distinct
        /// witnesses and a re-pointed successor with a strictly larger demand set is a distinct node.
        /// </summary>
        /// <param name="core">The seed atom the node specialises (a named filler, or <see cref="Top"/>).</param>
        /// <param name="demands">The canonical (sorted, distinct) backward-demand decorations; empty for a plain successor.</param>
        /// <param name="minted">Whether the call minted a fresh node rather than returning an interned or core atom — the caller records first ownership on a fresh mint and detects a cross-owner fold on an interned return.</param>
        /// <returns>The interned node id: the core atom for an empty description, else a stable minted atom.</returns>
        private int GetOrMintNode(int core, ImmutableArray<long> demands, out bool minted)
        {
            minted = false;
            if(demands.IsDefaultOrEmpty)
            {
                return core;
            }

            VNodeDescriptor descriptor = new(core, demands);
            if(NodeByDescr.TryGetValue(descriptor, out int existing))
            {
                return existing;
            }

            minted = true;
            int node = FreshAtom();
            NodeByDescr[descriptor] = node;
            DescrByNode[node] = descriptor;

            EnqueueSubsumer(node, node);
            EnqueueSubsumer(node, Top);
            EnqueueSubsumer(node, core);

            return node;
        }

        /// <summary>
        /// Mints or interns an owner's witness and applies the bookkeeping every witness mint shares, so no
        /// mint site can reach a node without it. A cyclic self-fold — the interned node IS the owner —
        /// under a self-elimination reaching the witness roles records an unsupported marker, and that
        /// guard is REGIME-INDEPENDENT: the artifact self-edge it exists for arises from any key shape that
        /// folds a cyclic existential onto one node, so it is a live check under shared keys too, where the
        /// regime selector makes its second conjunct false. The two per-owner clauses are gated on
        /// <see cref="SharedWitnessKeys"/> being false: a fresh mint records the owner in
        /// <see cref="MintOwnerByNode"/>, and, outside the fold-safety fence, a non-fresh return whose first
        /// owner is another node records the cross-owner marker that abstains the module to the general
        /// decider. Under shared keys a non-fresh return IS the design — the <see cref="MintedFrom"/>
        /// ledger records the mint edge instead and no unique-ownership invariant exists to violate. Both
        /// the existential introduction and the backward refinement of
        /// <see cref="TryConsumeBackwardDemands"/> mint here.
        /// The self-fold clause is inert at the refinement site: a refinement's demand set strictly contains
        /// its origin's, which in turn contains the origin's own mint decoration, so a refined node is
        /// neither its origin nor a node its origin was minted from.
        /// </summary>
        /// <param name="core">The seed atom the witness specialises.</param>
        /// <param name="demands">The canonical demand set of the witness.</param>
        /// <param name="owner">The node minting the witness — the existential's subject, or the owner whose minting edge a refinement re-points.</param>
        /// <returns>The interned witness node.</returns>
        private int MintOwnedWitness(int core, ImmutableArray<long> demands, int owner)
        {
            int witness = GetOrMintNode(core, demands, out bool minted);
            if(minted && !SharedWitnessKeys)
            {
                MintOwnerByNode[witness] = owner;
            }
            else if(witness == owner && WitnessClosureBearsSelfElimination)
            {
                //A cyclic self-reproducing existential folds the witness onto its own owner, committing
                //that owner to a self-loop model; a self-elimination reaching the witness role then
                //fires on the fold's artifact self-edge and forces a conclusion the module's true
                //self-loop-free models never carry, so the module abstains to the general decider.
                //Self-elimination is the only consumer keyed on the literal source == target
                //coincidence, which makes this guard exactly sufficient: for every other consumer the
                //fold makes the owner and its genuine successor the same node, so ranges, left
                //existentials, and bottom propagation draw only type-identical conclusions, and
                //composing a self-loop edge yields no edge that is not already present.
                Unsupported.Add("A cyclic self-reproducing witness folded onto its owner where a self-elimination reaches the witness role is outside the EL classification calculus.");
            }
            else if(!SharedWitnessKeys && !FoldSafe && witness != owner && MintOwnerByNode.TryGetValue(witness, out int firstOwner) && firstOwner != owner)
            {
                Unsupported.Add("A per-owner witness shared across distinct owners (mutually recursive inverse-coupled existentials) is outside the EL classification calculus.");
            }

            return witness;
        }

        /// <summary>
        /// Records one shared-regime mint edge: the coupled role and the owner whose existential minted the
        /// witness. Written at both shared mint sites before the edge is enqueued, so the direction test
        /// sees the entry before the mirror cascade can deliver a deposit over the reversed edge, and
        /// deduplicated per pair — a cyclic re-derivation by the same owner over the same role adds
        /// nothing, so the list stays as small as the owner's genuinely distinct mint edges.
        /// </summary>
        /// <param name="witness">The minted or interned witness node.</param>
        /// <param name="role">The coupled role carrying the mint edge.</param>
        /// <param name="owner">The node whose existential minted the witness.</param>
        private void RecordMintEdge(int witness, int role, int owner)
        {
            if(!MintedFrom.TryGetValue(witness, out List<(int Role, int Owner)>? mintEdges))
            {
                mintEdges = [];
                MintedFrom[witness] = mintEdges;
            }

            if(!mintEdges.Contains((role, owner)))
            {
                mintEdges.Add((role, owner));
            }
        }

        /// <summary>
        /// Whether the edge from <paramref name="source"/> to <paramref name="target"/> is OWNER-DIRECTED:
        /// in the shared regime it runs from a witness to one of the owners that minted it AND NOT the
        /// other way round, so it is the mirror of a mint edge and carries a fact about the OWNER's
        /// position, never about the witness's. Two rules must not read it in the witness's direction.
        /// Bottom propagation: the <c>⊥</c> arrives precisely because THAT owner is unsatisfiable, and the
        /// witness position under an empty owner is vacuous rather than empty — the position simply does
        /// not exist, while the same node still serves every other owner. Liveness: the witness may be
        /// inhabited through a completely different owner, so its inhabitation says nothing about THIS
        /// owner, whose own inhabitation reaches it through its own predecessors; without the gate a live
        /// witness would mark every co-owner inhabited and an empty owner's clash would then be pooled onto
        /// a real individual.
        /// The test is GEOMETRIC and reads no role: the question is only which way the mint edge ran, which
        /// is independent of whether any left existential consumes over the edge's role, so it must not
        /// inherit the refinement rule's backward-consumer pre-filter.
        /// The converse clause is what keeps a MUTUAL mint pair — two nodes each minted from the other,
        /// which a cyclic core reaching its own content key produces — reading forwards: there the same
        /// edge is at once a witness-to-owner mirror and a genuine forward mint edge, and the forward
        /// reading is the one that must win, since <paramref name="target"/> being empty leaves
        /// <paramref name="source"/>'s own existential demand unsatisfiable and a live
        /// <paramref name="source"/> genuinely forces its successor. Completeness elsewhere is untouched
        /// for the same reason: a witness unsatisfiable in itself condemns its owners over the FORWARD mint
        /// edges, which never satisfy this test, and an owner's own <c>⊥</c> already subsumes everything at
        /// the owner. The ledger is empty in the per-owner regime, so the test is false there and both
        /// rules run unchanged.
        /// </summary>
        /// <param name="source">The edge source.</param>
        /// <param name="target">The edge target.</param>
        /// <returns><see langword="true"/> when the edge is the mirror of a mint edge and must not be read in the source's direction.</returns>
        private bool IsOwnerDirectedEdge(int source, int target)
        {
            return SharedWitnessKeys && IsMintedFromOwner(source, target) && !IsMintedFromOwner(target, source);
        }

        /// <summary>
        /// Whether the mint-edge ledger records <paramref name="owner"/> as an owner that minted
        /// <paramref name="witness"/> — the geometric fact that a mint edge runs from
        /// <paramref name="owner"/> to <paramref name="witness"/>. A ledger key is always a minted node, so
        /// the test is false for every named class, individual, and shared filler.
        /// </summary>
        /// <param name="witness">The candidate witness node.</param>
        /// <param name="owner">The candidate owner.</param>
        /// <returns><see langword="true"/> when a mint edge runs from the owner to the witness.</returns>
        private bool IsMintedFromOwner(int witness, int owner)
        {
            if(!MintedFrom.TryGetValue(witness, out List<(int Role, int Owner)>? mintEdges))
            {
                return false;
            }

            foreach((int _, int mintedOwner) in mintEdges)
            {
                if(mintedOwner == owner)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Consumes the backward demands of one left-existential join: a conclusion the join would deposit
        /// on a minted witness through an edge running from that witness to one of its owners is recorded in
        /// the witness's intern key instead of being written onto a position the fold may merge. The witness
        /// is refined — re-interned with the licensing decorations added to its demand set — the conclusions
        /// are deposited on the refined node, the unrefined node's subsumers are inherited, and every coupled
        /// minting edge is re-pointed at the refined node, so the owner reaches the position that carries the
        /// fact. The direction is read off the KEY LATTICE, never off role syntax — a coupled role may be a
        /// strict sub-role of the pairing key, so the mint edge is mirrored only after promotion and no role
        /// relation connects the deposit role to the minting role. The deposit is backward exactly when
        /// <paramref name="role"/> is a <see cref="BackwardConsumerRoles"/> member, <paramref name="source"/>
        /// is minted, and some <see cref="CoupledRoles"/> role carries the edge from <paramref name="target"/>
        /// to <paramref name="source"/> whose OWNERSHIP DECORATION <paramref name="source"/>'s key records —
        /// that is, the candidate <see cref="PackDemand(int, int)"/> of that role and
        /// <paramref name="target"/>'s core is present in the key. The decoration is what makes the
        /// classification exact: it holds precisely of a witness reached from its owner, so an individual, a
        /// named class, or a shared filler on the far side is rejected (nothing minted
        /// <paramref name="source"/> from it) and so is a deposit travelling from an owner to its own
        /// successor (the successor's core is recorded in the SUCCESSOR's key, not in the owner's), which is
        /// the conclusion O2's key-determinacy leaves sound to deposit plainly. A demand-set comparison
        /// cannot decide this: a cyclic re-derivation whose mint decoration the owner's key already records
        /// mints a witness with the SAME demand set as its owner, so the two keys are equal at exactly the
        /// fold depth where the separation matters. Under shared witness keys the key records no ownership
        /// decoration and the mint-edge ledger is the direction test instead: the deposit is backward
        /// exactly when a recorded mint edge runs from <paramref name="target"/> to
        /// <paramref name="source"/>. In both regimes the matched roles are the minting edges the
        /// refinement re-points.
        /// A decoration the key already records is LICENSED: its conclusion is deposited on
        /// <paramref name="source"/> directly with no refinement, which is what makes a ladder whose
        /// conclusion is its own trigger saturate instead of refining forever. Every key read is candidate
        /// construction with a binary search over an immutable interned key, and the edge read is an O(1)
        /// hash-set membership; no packed demand is decoded. Iterative, no recursion.
        /// </summary>
        /// <param name="role">The join role — the role of the edge the conclusions travel over.</param>
        /// <param name="source">The edge source, the node the join would deposit the conclusions on.</param>
        /// <param name="target">The edge target, whose subsumers licensed the conclusions.</param>
        /// <param name="atoms">The trigger atoms, one per conclusion: <paramref name="atoms"/>[i] is the left-existential key atom licensing <paramref name="conclusions"/>[i].</param>
        /// <param name="conclusions">The conclusions the join would deposit, parallel to <paramref name="atoms"/>.</param>
        /// <param name="refined">The node the conclusions were deposited on: the refined witness, or <paramref name="source"/> itself when every decoration was already licensed.</param>
        /// <returns><see langword="true"/> when the deposit was consumed here; <see langword="false"/> when the caller must deposit every conclusion plainly.</returns>
        private bool TryConsumeBackwardDemands(int role, int source, int target, List<int> atoms, List<int> conclusions, out int refined)
        {
            refined = source;
            if(!BackwardConsumerRoles.Contains(role) || !DescrByNode.TryGetValue(source, out VNodeDescriptor sourceDescriptor))
            {
                return false;
            }

            BackwardMintingRoles.Clear();
            if(SharedWitnessKeys)
            {
                //The shared key records no ownership decoration, so the mint-edge ledger IS the direction
                //test: the deposit is backward exactly when some mint edge of the source runs from it to
                //this target, and the roles of those entries are the minting edges the refinement
                //re-points. A ledger entry is a mint that genuinely happened, so the test never accepts an
                //edge no mint created; and every witness-to-owner edge is mirror-produced from a mint edge
                //the ledger recorded, so it never rejects one either.
                if(MintedFrom.TryGetValue(source, out List<(int Role, int Owner)>? mintEdges))
                {
                    foreach((int mintedRole, int mintedOwner) in mintEdges)
                    {
                        if(mintedOwner == target)
                        {
                            BackwardMintingRoles.Add(mintedRole);
                        }
                    }
                }
            }
            else
            {
                int targetCore = CoreLabelOf(target);
                foreach(int coupled in CoupledRoles)
                {
                    if(Edges.TryGetValue(coupled, out HashSet<(int Source, int Target)>? coupledEdges)
                        && coupledEdges.Contains((target, source))
                        && sourceDescriptor.Demands.AsSpan().BinarySearch(PackDemand(coupled, targetCore)) >= 0)
                    {
                        BackwardMintingRoles.Add(coupled);
                    }
                }
            }

            if(BackwardMintingRoles.Count == 0)
            {
                return false;
            }

            BackwardNewDecorations.Clear();
            BackwardNewConclusions.Clear();
            for(int index = 0; index < atoms.Count; index++)
            {
                long decoration = PackBackwardDemand(role, atoms[index]);
                if(sourceDescriptor.Demands.AsSpan().BinarySearch(decoration) >= 0)
                {
                    EnqueueSubsumer(source, conclusions[index]);

                    continue;
                }

                BackwardNewDecorations.Add(decoration);
                BackwardNewConclusions.Add(conclusions[index]);
            }

            if(BackwardNewDecorations.Count == 0)
            {
                return true;
            }

            //The refined node denotes the same canonical element its origin does, so in the per-owner
            //regime it inherits the origin's FIRST owner rather than taking the owner this deposit arrived
            //through: a witness two owners already share is refined once per owner, and recording the
            //arriving owner would read those refinements as a cross-owner collision. A collision between
            //two DISTINCT origins whose owners differ still reaches the ownership ledger, which is the case
            //the abstention exists for. The shared regime reads no ownership ledger at all — convergent
            //refinements of different owners onto one node are the population collapse it exists to
            //deliver — so the arriving owner is the one whose minting edge this call re-points, and the
            //mint-edge ledger below records that edge.
            int refinementOwner = !SharedWitnessKeys && MintOwnerByNode.TryGetValue(source, out int originOwner) ? originOwner : target;
            refined = MintOwnedWitness(sourceDescriptor.Core, CanonicalizeDemands(sourceDescriptor.Demands, BackwardNewDecorations), refinementOwner);

            //The refined node dominates its origin from the moment it exists rather than only at the
            //fixpoint: it carries the origin's facts, its own key records the consumed decorations, and the
            //owner's minting edges point at it, so a consumer firing on a retained pre-refinement edge draws
            //a subset of what the re-pointed edge draws.
            foreach(int subsumer in (int[])[.. SubsumersOf(source)])
            {
                EnqueueSubsumer(refined, subsumer);
            }

            foreach(int conclusion in BackwardNewConclusions)
            {
                EnqueueSubsumer(refined, conclusion);
            }

            foreach(int mintingRole in BackwardMintingRoles)
            {
                if(SharedWitnessKeys)
                {
                    //The re-pointed edge is a mint edge of the refined node, so the ledger records it
                    //before the edge exists — the refined node's own later deposits read the same direction
                    //test the origin's did.
                    RecordMintEdge(refined, mintingRole, target);
                }

                EnqueueEdge(mintingRole, target, refined);
            }

            return true;
        }

        /// <summary>
        /// The core label of an owner node — a minted witness's seed atom (via <see cref="DescrByNode"/>),
        /// or the node itself for an ordinary atom. A backward-demand decoration records a witness's owner
        /// by this label, not by node id, so distinct owners separate their witnesses while a cyclic
        /// re-derivation of the same owner-core (<c>A ⊑ ∃r.A</c>) folds to one interned witness and the
        /// saturation terminates.
        /// </summary>
        /// <param name="node">The owner node.</param>
        /// <returns>The node's core atom.</returns>
        private int CoreLabelOf(int node) => DescrByNode.TryGetValue(node, out VNodeDescriptor descriptor) ? descriptor.Core : node;

        /// <summary>
        /// Packs a backward-demand decoration — an <c>∃r⁻.owner-core</c> provenance — into one long: the
        /// role in the high word, the owner's core atom in the low word, untagged. Distinct
        /// <c>(role, owner-core)</c> pairs pack to distinct longs, so a witness's demand set separates its
        /// owners and a backward rule reads the role back to relate it to the axioms whose sub-role covers
        /// it. Requires <c>0 ≤ role &lt; 2^29</c>: role ids are sequential interned or
        /// <see cref="FreshRole"/> indices, so the bound holds of every module, and it is what keeps the
        /// role field clear of bits 61 and 62 — the tags of <see cref="MarkDemand(int)"/> and
        /// <see cref="PackBackwardDemand(int, int)"/> — so the three decoration kinds occupy genuinely
        /// disjoint numeric namespaces at the bit level rather than by any argument about which of them a
        /// module constructs.
        /// </summary>
        /// <param name="role">The forward role of the existential the witness satisfies.</param>
        /// <param name="ownerCore">The core atom of the witness's owner.</param>
        /// <returns>The packed demand.</returns>
        private static long PackDemand(int role, int ownerCore) => ((long)role << 32) | (uint)ownerCore;

        /// <summary>
        /// Packs the shared-witness mark of a coupled role — the WHOLE decoration a shared witness's key
        /// carries — into one long, tagged in bit 61 so the mark namespace is disjoint from
        /// <see cref="PackDemand(int, int)"/>'s untagged minting namespace and
        /// <see cref="PackBackwardDemand(int, int)"/>'s bit-62 backward namespace. The mark records the
        /// role and nothing else: a shared witness denotes the canonical element of its
        /// <c>(core, mint role)</c> content class and serves every owner of that class at once, so no owner
        /// term belongs in the key, while the role stays because a filler reached over two different
        /// coupled roles bears two different sets of in-role facts. The decoration is never empty, which is
        /// what keeps the mint off <see cref="GetOrMintNode"/>'s shared-filler return. Requires
        /// <c>0 ≤ role &lt; 2^29</c>, the same bound the other two packers state, which is what keeps the
        /// role field clear of both tag bits.
        /// </summary>
        /// <param name="role">The coupled role whose existential mints the witness.</param>
        /// <returns>The packed mark.</returns>
        private static long MarkDemand(int role) => (1L << 61) | ((long)role << 32);

        /// <summary>
        /// Packs a backward-demand decoration — the <c>∃role.atom</c> left-existential trigger whose
        /// conclusion a witness's key has consumed — into one long, tagged in bit 62 so the backward
        /// namespace is disjoint from <see cref="PackDemand(int, int)"/>'s minting namespace. Untagged the
        /// two coincide whenever the minting role is also a backward-consumer role (a symmetric role is its
        /// own mirror target, and a mutual pairing makes both roles keys and targets) and the trigger atom is
        /// an owner core — an ordinary axiom shape, since a node's core is always in its own subsumer set —
        /// which would make a refinement's union fail to grow where it must and let two owners' refined
        /// witnesses coincide through the packing alone. Requires <c>0 ≤ role &lt; 2^29</c>: role ids are
        /// sequential interned or <see cref="FreshRole"/> indices, so the bound holds of every module, and it
        /// is what keeps the tag clear of the role field — bit 61 stays
        /// <see cref="MarkDemand(int)"/>'s alone — and the packed values ascending in (role, atom)
        /// order. The atom is read out of a <see cref="LeftExistentials"/> key, never from a subsumer set, so
        /// it is an axiom atom fixed at normalization and the decoration universe stays finite.
        /// </summary>
        /// <param name="role">The role of the left-existential key whose conclusion is consumed.</param>
        /// <param name="atom">The atom of that left-existential key.</param>
        /// <returns>The packed backward demand.</returns>
        private static long PackBackwardDemand(int role, int atom) => (1L << 62) | ((long)role << 32) | (uint)atom;

        /// <summary>
        /// Merges a minting owner's demand set with the witness's own backward decoration into a canonical
        /// (sorted-ascending, duplicate-free) demand array — the form <see cref="VNodeDescriptor"/> interns
        /// on, so equal demand sets fold to one node. Inheriting the owner's demands is what keeps two
        /// distinct-owner witnesses that share a filler core from folding their onward successors: without
        /// it a backward inverse clash on one owner's branch would empty every co-folded owner through the
        /// shared successor. The array is always non-empty (the decoration is always added) and strictly
        /// ascending — the canonical form the descriptor's structural equality requires, guaranteed by this
        /// producer.
        /// </summary>
        /// <param name="ownerDemands">The minting owner's demand set (empty for a named class, individual, or <see cref="Top"/> owner).</param>
        /// <param name="decoration">The witness's own <c>∃r⁻.owner-core</c> decoration.</param>
        /// <returns>The canonical merged demand array.</returns>
        private static ImmutableArray<long> CanonicalizeDemands(ImmutableArray<long> ownerDemands, long decoration)
        {
            if(ownerDemands.IsDefaultOrEmpty)
            {
                return [decoration];
            }

            SortedSet<long> merged = [.. ownerDemands, decoration];

            return [.. merged];
        }

        /// <summary>
        /// Merges a witness's demand set with a BATCH of new decorations into a canonical (sorted-ascending,
        /// duplicate-free) demand array — the form <see cref="VNodeDescriptor"/> interns on. The backward
        /// refinement consumes every trigger of one join firing in a single union, so a witness carrying
        /// several simultaneous triggers mints one refined node rather than the sibling lattice a
        /// one-decoration-at-a-time refinement would explore before converging on the same top node. The
        /// caller has already split off the decorations the key records, so the result is a strict superset
        /// of <paramref name="demands"/> and the refinement chain is strictly ascending in the finite
        /// decoration lattice.
        /// </summary>
        /// <param name="demands">The witness's own demand set.</param>
        /// <param name="decorations">The new backward decorations to fold in.</param>
        /// <returns>The canonical merged demand array.</returns>
        private static ImmutableArray<long> CanonicalizeDemands(ImmutableArray<long> demands, IReadOnlyList<long> decorations)
        {
            SortedSet<long> merged = demands.IsDefaultOrEmpty ? [] : [.. demands];
            foreach(long decoration in decorations)
            {
                merged.Add(decoration);
            }

            return [.. merged];
        }

        //Interns an individual to its own atom in a space disjoint from the
        //class atoms: the key is recorded in IndividualIds and the atom in
        //IndividualAtoms, never in AtomIds/NamedAtoms, so a punned IRI (class
        //and individual at once) gets two distinct atoms and the individual
        //never appears in the named-class subsumption projection.
        private int IndividualAtomOf(Utf8String key)
        {
            if(IndividualIds.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int atom = AtomNames.Count;
            AtomNames.Add(key);
            IndividualIds[key] = atom;
            IndividualAtoms.Add(atom);

            return atom;
        }

        /// <summary>
        /// The atom for a nominal's individual: the shared <see cref="IndividualAtomOf(Utf8String)"/>
        /// node, resolved through the <c>SameIndividual</c> merge so a nominal filler, an asserted
        /// edge, and a co-reference all collapse onto one node. A nominal term that is not an
        /// individual (a literal or triple term inside an <c>ObjectOneOf</c>) is recorded unsupported
        /// and given a fresh throwaway atom, so the coupled reasoner delegates the module rather than
        /// misroute it.
        /// </summary>
        /// <param name="term">The enumerated nominal term.</param>
        /// <returns>The individual atom, or a fresh atom for a non-individual term.</returns>
        private int NominalAtom(RdfTerm term)
        {
            if(TryIndividualKey(term, out Utf8String key))
            {
                return IndividualAtomOf(FindKey(key));
            }

            Unsupported.Add($"{term.GetType().Name} in an ObjectOneOf is not an individual.");

            return FreshAtom();
        }

        private int RoleOf(NamedNode property)
        {
            if(RoleIds.TryGetValue(property.Iri, out int existing))
            {
                return existing;
            }

            int role = RoleNames.Count;
            RoleNames.Add(property.Iri);
            RoleIds[property.Iri] = role;

            return role;
        }

        private int FreshRole()
        {
            int role = RoleNames.Count;
            RoleNames.Add(null);

            return role;
        }

        private void AddToldSubsumption(int sub, int super)
        {
            if(!ToldSubsumptions.TryGetValue(sub, out List<int>? list))
            {
                list = [];
                ToldSubsumptions[sub] = list;
            }

            list.Add(super);
        }

        private void AddConjunction(int first, int second, int conclusion)
        {
            if(!Conjunctions.TryGetValue((first, second), out List<int>? list))
            {
                list = [];
                Conjunctions[(first, second)] = list;
            }

            list.Add(conclusion);

            AddConjunctionByAtom(first, second, conclusion);
            AddConjunctionByAtom(second, first, conclusion);
        }

        private void AddConjunctionByAtom(int atom, int other, int conclusion)
        {
            if(!ConjunctionsByAtom.TryGetValue(atom, out List<(int Other, int Conclusion)>? list))
            {
                list = [];
                ConjunctionsByAtom[atom] = list;
            }

            list.Add((other, conclusion));
        }

        private void AddRightExistential(int subject, int role, int filler)
        {
            if(!RightExistentials.TryGetValue(subject, out List<(int Role, int Filler)>? list))
            {
                list = [];
                RightExistentials[subject] = list;
            }

            list.Add((role, filler));
        }

        /// <summary>Registers a range atom on a role — the atom every edge target over the role gains through the per-edge range rule. A complex range is named through the inclusion machinery first. Shared by a forward range axiom and an inverse-role domain axiom (<c>domain(r⁻) = range(r)</c>).</summary>
        /// <param name="role">The role whose targets carry the range.</param>
        /// <param name="rangeExpression">The range class expression.</param>
        private void AddRoleRange(int role, OwlClassExpression rangeExpression)
        {
            int atom;
            if(rangeExpression is OwlClassReference namedRange)
            {
                atom = AtomOf(namedRange.Class.Iri);
            }
            else
            {
                atom = FreshAtom();
                NormalizeInclusion(AtomReference(atom), rangeExpression);
            }

            if(!RangesByRole.TryGetValue(role, out List<int>? ranges))
            {
                ranges = [];
                RangesByRole[role] = ranges;
            }

            ranges.Add(atom);
        }

        private void AddDataDemand(int subject, Utf8String property, OwlDataRange range)
        {
            if(!DataDemands.TryGetValue(subject, out List<DataDemand>? list))
            {
                list = [];
                DataDemands[subject] = list;
            }

            list.Add(new DataDemand(property, range));
        }

        /// <summary>One data value demand a member of an atom must meet: a data property and a range a value of the property must lie in — the property-keyed form the §1.3 sidecar pools and inherits over.</summary>
        /// <param name="Property">The demanding data property IRI.</param>
        /// <param name="Range">The range the demanded value lies in.</param>
        private readonly record struct DataDemand(Utf8String Property, OwlDataRange Range);

        /// <summary>One left-position data existential the normalization named: the data property, the range a value must lie in, and the atom the occurrence was named with — the subclass-side counterpart of <see cref="DataDemand"/>, which <see cref="SeedDataRecognitions"/> decides against each demand carrier.</summary>
        /// <param name="Property">The data property the occurrence restricts.</param>
        /// <param name="Range">The range a value of the property must lie in for the occurrence to hold.</param>
        /// <param name="Conclusion">The atom naming the occurrence, whose own normal forms carry it onward.</param>
        private readonly record struct LeftDataExistential(Utf8String Property, OwlDataRange Range, int Conclusion);

        private void AddSelfDemand(int subject, int role)
        {
            if(!SelfDemands.TryGetValue(subject, out List<int>? list))
            {
                list = [];
                SelfDemands[subject] = list;
            }

            list.Add(role);
        }

        private void AddSelfElimination(int role, int conclusion)
        {
            if(!SelfEliminations.TryGetValue(role, out List<int>? list))
            {
                list = [];
                SelfEliminations[role] = list;
            }

            list.Add(conclusion);
        }

        /// <summary>
        /// The synthetic mirror role of a forward role whose inverse appears in a subclass-side
        /// existential: a fresh role paired one-directionally (every forward edge forces the reverse
        /// mirror edge), so <c>∃r⁻.C ⊑ Y</c> reduces to the ordinary left existential
        /// <c>∃mirror.C ⊑ Y</c> over the shipped mirror + left-existential rules — a node has an
        /// <c>r</c>-predecessor in <c>C</c> exactly when it has a mirror-successor in <c>C</c>.
        /// Memoized so every inverse occurrence of one role shares one mirror. Only the mirror role
        /// becomes a mirror target: the forward role gains no edge from the pairing (a mirror of a
        /// mirror is the original edge), so a functional forward role is not spuriously fenced. The
        /// mirror role is internal — it bears no existential, self-demand, chain, or range, so it never
        /// generates edges and never trips the admission gate.
        /// </summary>
        /// <param name="forwardRole">The forward role the inverse existential inverts.</param>
        /// <returns>The memoized synthetic mirror role.</returns>
        private int GetOrMintMirrorRole(int forwardRole)
        {
            if(MirrorRoleForInverted.TryGetValue(forwardRole, out int existing))
            {
                return existing;
            }

            int mirror = FreshRole();
            MirrorRoleForInverted[forwardRole] = mirror;
            AddInversePair(forwardRole, mirror);

            return mirror;
        }

        /// <summary>
        /// The synthetic generator role of a forward role whose inverse appears in a superclass-side
        /// existential: a fresh role paired one-directionally as <c>g ⊑ r⁻</c> (every generator edge
        /// <c>(x, w)</c> forces the reverse forward edge <c>(w, x)</c>), so <c>A ⊑ ∃r⁻.C</c> reduces to
        /// the ordinary right existential <c>A ⊑ ∃g.C</c> — the owner's <c>r</c>-predecessor is minted
        /// per-owner as a forward <c>g</c>-successor and the mirror writes the real <c>r</c>-edge back
        /// onto the owner. Memoized so every superclass inverse occurrence of one role shares one
        /// generator. The forward role <c>r</c> becomes the mirror target of the pairing (it receives the
        /// witness edges), so its non-asserted successors fence functional admission exactly where they
        /// must. The generator role is internal: it bears only right existentials, never a range,
        /// self-demand, or chain, so it generates only the witness edges the reduction intends. Generator
        /// roles are minted at normalization only, so the demand-packing signature is frozen before the
        /// saturation loop and the termination argument holds; this must never be called from a
        /// saturation-loop path.
        /// </summary>
        /// <param name="forwardRole">The forward role the superclass inverse existential inverts.</param>
        /// <returns>The memoized synthetic generator role.</returns>
        private int GetOrMintGeneratorRole(int forwardRole)
        {
            if(GeneratorRoleForInverted.TryGetValue(forwardRole, out int existing))
            {
                return existing;
            }

            int generator = FreshRole();
            GeneratorRoleForInverted[forwardRole] = generator;
            AddInversePair(generator, forwardRole);

            return generator;
        }

        /// <summary>Records that <paramref name="inverse"/>'s extension is the reverse of <paramref name="role"/>'s, so the saturation mirror seeds the reverse of each <paramref name="role"/> edge under <paramref name="inverse"/>. A symmetric role pairs with itself; an <c>InverseObjectProperties</c> axiom records both directions.</summary>
        /// <param name="role">The role whose edges are mirrored.</param>
        /// <param name="inverse">The role the reverse edges are seeded under.</param>
        private void AddInversePair(int role, int inverse)
        {
            if(!InversePairs.TryGetValue(role, out List<int>? list))
            {
                list = [];
                InversePairs[role] = list;
            }

            if(!list.Contains(inverse))
            {
                list.Add(inverse);
            }

            MirrorTargets.Add(inverse);
        }

        private static bool IsReservedDataProperty(Utf8String property)
        {
            return property.Equals(OwlVocabulary.TopDataProperty) || property.Equals(OwlVocabulary.BottomDataProperty);
        }

        /// <summary>Whether an object property is a reserved built-in (<c>owl:topObjectProperty</c> / <c>owl:bottomObjectProperty</c>) whose fixed extension the saturation does not interpret, so the generator reduction over its inverse must delegate rather than pair a synthetic role with it.</summary>
        /// <param name="role">The object property IRI.</param>
        /// <returns><see langword="true"/> when the property is a reserved built-in.</returns>
        private static bool IsReservedRole(Utf8String role)
        {
            return role.Equals(OwlVocabulary.TopObjectProperty) || role.Equals(OwlVocabulary.BottomObjectProperty);
        }

        private void AddLeftExistential(int role, int filler, int conclusion)
        {
            if(!LeftExistentials.TryGetValue((role, filler), out List<int>? list))
            {
                list = [];
                LeftExistentials[(role, filler)] = list;
            }

            list.Add(conclusion);
        }

        private void AddRoleSubsumption(int sub, int super)
        {
            if(!RoleSubsumptions.TryGetValue(sub, out List<int>? list))
            {
                list = [];
                RoleSubsumptions[sub] = list;
            }

            list.Add(super);
        }

        private void AddChain(int first, int second, int conclusion)
        {
            if(!ChainsByFirst.TryGetValue(first, out List<(int Second, int Conclusion)>? byFirst))
            {
                byFirst = [];
                ChainsByFirst[first] = byFirst;
            }

            byFirst.Add((second, conclusion));

            if(!ChainsBySecond.TryGetValue(second, out List<(int First, int Conclusion)>? bySecond))
            {
                bySecond = [];
                ChainsBySecond[second] = bySecond;
            }

            bySecond.Add((first, conclusion));
        }

        private HashSet<int> SubsumersOf(int atom)
        {
            if(!Subsumers.TryGetValue(atom, out HashSet<int>? set))
            {
                set = [];
                Subsumers[atom] = set;
            }

            return set;
        }

        private void EnqueueSubsumer(int subject, int atom)
        {
            if(SubsumersOf(subject).Add(atom))
            {
                if(IndividualAtoms.Contains(atom))
                {
                    //Maintain the nominal indexes both ways: the node is told to be the individual,
                    //and the individual gains a carrier — so the gated merge can fire whether the node
                    //later becomes live or the individual later gains a subsumer.
                    AddToList(NominalsAt, subject, atom);
                    AddToList(NominalCarriers, atom, subject);
                }

                Work.Enqueue(new WorkItem(WorkKind.Subsumer, subject, atom, Role: 0, Target: 0));
            }
        }

        //Marks a node inhabited and schedules the gated nominal merge for it. The set add makes it
        //idempotent, so liveness propagates through the work queue without recursion.
        private void MarkLive(int node)
        {
            if(Live.Add(node))
            {
                Work.Enqueue(new WorkItem(WorkKind.Liveness, node, Atom: 0, Role: 0, Target: 0));
            }
        }

        private static void AddToList(Dictionary<int, List<int>> map, int key, int value)
        {
            if(!map.TryGetValue(key, out List<int>? list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(value);
        }

        private void EnqueueEdge(int role, int source, int target)
        {
            if(!Edges.TryGetValue(role, out HashSet<(int Source, int Target)>? set))
            {
                set = [];
                Edges[role] = set;
            }

            if(!set.Add((source, target)))
            {
                return;
            }

            if(!EdgesBySource.TryGetValue((role, source), out List<int>? bySource))
            {
                bySource = [];
                EdgesBySource[(role, source)] = bySource;
            }

            bySource.Add(target);

            if(!EdgesByTarget.TryGetValue((role, target), out List<int>? byTarget))
            {
                byTarget = [];
                EdgesByTarget[(role, target)] = byTarget;

                //The first edge of this (role, target) pair is what makes the role incident to the target,
                //so the incoming-role index gains the role here and only here — once per key, mirroring the
                //list this branch creates.
                AddToList(IncomingRoles, target, role);
            }

            byTarget.Add(source);

            AddToList(OutgoingTargets, source, target);

            //Liveness flows forward: a successor of an inhabited node is itself inhabited. An edge whose
            //source is not yet live is revisited by ProcessLiveness when the source later becomes live. An
            //owner-directed edge does not carry it: that edge is the mirror of a mint edge, so an
            //inhabited witness says nothing about an owner that may not be inhabited at all.
            if(Live.Contains(source) && !IsOwnerDirectedEdge(source, target))
            {
                MarkLive(target);
            }

            Work.Enqueue(new WorkItem(WorkKind.Edge, Subject: source, Atom: 0, role, target));
        }
    }
}
