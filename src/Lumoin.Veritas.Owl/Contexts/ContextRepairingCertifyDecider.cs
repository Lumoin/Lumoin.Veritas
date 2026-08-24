using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape R clash reason family — the repairing counterpart of the told-ground-witness clash reasons: four stable leading identifiers the statistics assembly and the battery discriminate on.</summary>
internal static class RepairingGroundClashReasons
{
    /// <summary>The complemented-membership clash: one told term is derived into a named class and, through a told complement conjunct, out of the same class.</summary>
    /// <param name="className">The class holding both the derived membership and its denial.</param>
    /// <returns>The named reason.</returns>
    public static string ComplementedMembership(Utf8String className)
    {
        return $"RepairingComplementedMembership({className})";
    }

    /// <summary>The empty-class assertion clash: a told class assertion types a term with <c>owl:Nothing</c>, or with a top-level complement of <c>owl:Thing</c>, whose extension is empty in every interpretation while the assertion demands a member.</summary>
    /// <param name="subject">The individual term the empty class was asserted on.</param>
    /// <returns>The named reason.</returns>
    public static string AssertedNothingMembership(Utf8String subject)
    {
        return $"RepairingAssertedNothingMembership({subject})";
    }

    /// <summary>The disjointness clash: one told term is derived into two named classes a told disjointness axiom separates.</summary>
    /// <param name="className">One named class of the clashing disjoint pair.</param>
    /// <returns>The named reason.</returns>
    public static string DisjointMembership(Utf8String className)
    {
        return $"RepairingDisjointMembership({className})";
    }

    /// <summary>The contradictory-edge clash: one ordered term pair is derived into a role's extension and, through a told negative property assertion, out of the same role.</summary>
    /// <param name="role">The role holding both the derived edge and its denial.</param>
    /// <returns>The named reason.</returns>
    public static string ContradictoryEdge(Utf8String role)
    {
        return $"RepairingContradictoryEdge({role})";
    }
}

/// <summary>The told-closure discipline the repairing construction runs under. The production value is code zero, so an options value left at <see langword="default"/> is bit-identical to production.</summary>
internal enum RepairClosureMode
{
    /// <summary>The production discipline: the told closure is an OPERATOR re-applied at every commit step, so no downstream step observes an unclosed edge relation.</summary>
    PerCommit = 0,

    /// <summary>The prologue variant: the closure runs once over the told edges and never again, so an invented edge on an inverse-paired role stays unmirrored and its both-directions re-check fails.</summary>
    SinglePrologue = 1,
}

/// <summary>Whether the phase-2 demand-set rule runs under the vacuity guard. The production value is code zero.</summary>
internal enum RepairVacuityGuardMode
{
    /// <summary>The production rule: a universal filler is admitted into the demand set only where its activating membership is re-derivable over the restricted class table, so a universal holding solely because the role under repair is empty at the carrier does not narrow that role's demand set.</summary>
    Guarded = 0,

    /// <summary>The control: every active universal filler narrows the demand set, so a vacuously activated universal closes the very demand the repair is trying to open.</summary>
    Unguarded = 1,
}

/// <summary>Where the frozen complement post-pass runs relative to the mints. The production value is code zero.</summary>
internal enum RepairComplementPlacement
{
    /// <summary>The production placement: the pass runs per candidate model, after the last commit or mint and immediately before verification, so every complement is evaluated against the domain the verifier reads.</summary>
    PerCandidateAfterLastMint = 0,

    /// <summary>The control: the pass runs at the end of the deterministic stage, before any mint, so a complement is frozen over a smaller domain and its defining equivalence fails the both-directions re-check.</summary>
    BeforeMints = 1,
}

/// <summary>Whether the universal-as-generator rule fires vacuously in the class-table fixpoint. The production value is code zero.</summary>
internal enum RepairGeneratorMode
{
    /// <summary>The production rule: a universal fires at a carrier with no successor on its role, the sound superset-direction firing that carries a vacuously satisfied equivalence to verified.</summary>
    UniversalFiresVacuously = 0,

    /// <summary>The control: a universal fires only at a carrier holding at least one successor on its role, the exclusion the standing widening-lock obligation forbids.</summary>
    UniversalRequiresSuccessor = 1,
}

/// <summary>Whether the decider takes the clash-only entry point. The production value is code zero.</summary>
internal enum RepairClashOnlyEntry
{
    /// <summary>The production path: both faces run in jurisdiction order, the monotone told-only clash first and the whole-module repairing certificate behind it.</summary>
    Disabled = 0,

    /// <summary>The clash-only path: the monotone told-only pass runs and returns without constructing anything, so face fourteen's jurisdiction is exercisable with no phase 0-4 execution behind it.</summary>
    Enabled = 1,
}

/// <summary>
/// The eight repairing bounds as overridable members where ZERO MEANS "USE THE
/// <c>const</c>", so a value left at <see langword="default"/> is exactly
/// production and a caller supplies only the non-zero overrides it needs. Each
/// effective bound is read through a get-only property that returns the
/// <c>const</c> on a zero backing member, so no member ever holds a duplicated
/// literal of a <c>const</c>.
/// </summary>
/// <param name="DemandOverride">The open-demand override; zero reads <see cref="ContextRepairingCertifyDecider.RepairDemandBound"/>.</param>
/// <param name="ComponentOverride">The demands-per-component override; zero reads <see cref="ContextRepairingCertifyDecider.RepairComponentBound"/>.</param>
/// <param name="BranchOverride">The candidates-per-demand override; zero reads <see cref="ContextRepairingCertifyDecider.RepairBranchBound"/>.</param>
/// <param name="ComponentNodeOverride">The local-evaluations-per-component override; zero reads <see cref="ContextRepairingCertifyDecider.RepairComponentNodeBound"/>.</param>
/// <param name="ComponentCountOverride">The independent-component override; zero reads <see cref="ContextRepairingCertifyDecider.RepairComponentCountBound"/>.</param>
/// <param name="ModelVerifyOverride">The whole-module verification-pass override; zero reads <see cref="ContextRepairingCertifyDecider.RepairModelVerifyBound"/>.</param>
/// <param name="MintOverride">The fresh-element override; zero reads <see cref="ContextRepairingCertifyDecider.RepairMintBound"/>.</param>
/// <param name="CascadeDepthOverride">The mint-cascade-hop override; zero reads <see cref="ContextRepairingCertifyDecider.RepairCascadeDepthBound"/>.</param>
internal readonly record struct RepairingBounds(
    int DemandOverride,
    int ComponentOverride,
    int BranchOverride,
    int ComponentNodeOverride,
    int ComponentCountOverride,
    int ModelVerifyOverride,
    int MintOverride,
    int CascadeDepthOverride)
{
    /// <summary>The effective open-demand ceiling.</summary>
    public int Demand => DemandOverride == 0 ? ContextRepairingCertifyDecider.RepairDemandBound : DemandOverride;

    /// <summary>The effective demands-per-component ceiling.</summary>
    public int Component => ComponentOverride == 0 ? ContextRepairingCertifyDecider.RepairComponentBound : ComponentOverride;

    /// <summary>The effective candidates-per-demand ceiling.</summary>
    public int Branch => BranchOverride == 0 ? ContextRepairingCertifyDecider.RepairBranchBound : BranchOverride;

    /// <summary>The effective local-evaluations-per-component ceiling.</summary>
    public int ComponentNode => ComponentNodeOverride == 0 ? ContextRepairingCertifyDecider.RepairComponentNodeBound : ComponentNodeOverride;

    /// <summary>The effective independent-component ceiling.</summary>
    public int ComponentCount => ComponentCountOverride == 0 ? ContextRepairingCertifyDecider.RepairComponentCountBound : ComponentCountOverride;

    /// <summary>The effective whole-module verification-pass ceiling.</summary>
    public int ModelVerify => ModelVerifyOverride == 0 ? ContextRepairingCertifyDecider.RepairModelVerifyBound : ModelVerifyOverride;

    /// <summary>The effective fresh-element ceiling.</summary>
    public int Mint => MintOverride == 0 ? ContextRepairingCertifyDecider.RepairMintBound : MintOverride;

    /// <summary>The effective mint-cascade-hop ceiling.</summary>
    public int CascadeDepth => CascadeDepthOverride == 0 ? ContextRepairingCertifyDecider.RepairCascadeDepthBound : CascadeDepthOverride;
}

/// <summary>
/// The repairing construction's six variation points, accepted only by the
/// internal entry points. Every member names its PRODUCTION behaviour at code
/// zero, so a value left at <see langword="default"/> is bit-identical to
/// production and the reasoner's call path passes none. Every option changes
/// only WHICH structures are proposed: the verification pass re-checks whatever
/// is proposed, so a non-default option can produce a silence or a slower walk,
/// never a wrong verdict.
/// </summary>
/// <param name="ClosureMode">Whether the told closure is an operator re-applied at every commit or a single prologue.</param>
/// <param name="VacuityGuardMode">Whether the phase-2 demand-set rule runs under the vacuity guard.</param>
/// <param name="ComplementPlacement">Where the frozen complement post-pass runs relative to the mints.</param>
/// <param name="GeneratorMode">Whether the universal-as-generator rule fires vacuously in the class-table fixpoint.</param>
/// <param name="ClashOnlyEntry">Whether the decider takes the clash-only entry point with no phase 0-4 execution behind it.</param>
/// <param name="Bounds">The eight repairing bounds, zero-means-production per member.</param>
internal readonly record struct RepairingConstructionOptions(
    RepairClosureMode ClosureMode,
    RepairVacuityGuardMode VacuityGuardMode,
    RepairComplementPlacement ComplementPlacement,
    RepairGeneratorMode GeneratorMode,
    RepairClashOnlyEntry ClashOnlyEntry,
    RepairingBounds Bounds);

/// <summary>
/// The Shape R window measurement the census-first recognizer's
/// pre-clausification pass reads on every restriction-rich-ground-jurisdiction
/// module — computed with the carrier deduplication applied BEFORE any boundary
/// comparison, so the battery's near-miss rows can pin the measured quantity
/// independently of the comparison's outcome.
/// </summary>
/// <param name="CarrierCount">The domain size: one carrier per distinct told individual term after the told-sameness quotient plus one per minted witness, one fresh carrier where the module told no term at all — Direct Semantics admits no empty domain — and the told term count where no construction ran.</param>
/// <param name="ClassCount">The distinct named classes other than the two semantics-fixed constants, one least-fixpoint extension each.</param>
/// <param name="RoleCount">The distinct roles MENTIONED in an admitted axiom, one edge relation each — mentioned rather than told-edge-bearing, so a mint on a role no told edge names is representable.</param>
/// <param name="CommittedEdges">The edges the committed relation holds at the last leaf — told, repaired and minted alike under the re-applied closure operator; the told edge count where the construction did not run.</param>
/// <param name="MintedElements">The fresh elements the construction minted for the candidate model.</param>
/// <param name="ChoicePointsOpened">The choice frames the walk opened; zero is the deterministic regime's marker.</param>
/// <param name="EvaluatedNodes">The local candidate evaluations the walk spent across its components.</param>
/// <param name="ModelVerifyPasses">The whole-module verification passes the decision spent.</param>
/// <param name="WindowSilences">One when the carriers, the named classes, or the roles exceeded their bound — a named silence, never a verdict over an unbuilt structure; zero otherwise.</param>
internal readonly record struct RepairingWindow(
    int CarrierCount,
    int ClassCount,
    int RoleCount,
    int CommittedEdges,
    int MintedElements,
    int ChoicePointsOpened,
    int EvaluatedNodes,
    int ModelVerifyPasses,
    int WindowSilences)
{
    /// <summary>The empty window: no repairing ground surface was collected.</summary>
    public static RepairingWindow Empty => default;
}

/// <summary>The Shape R decider's outcome: the monotone told-only ground refutation or the whole-module repaired-described-model certificate, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The verdict — <see langword="false"/> for the monotone told clash, <see langword="true"/> for the repaired certificate — or <see langword="null"/> when both faces are silent on the module. The certify face never answers <see langword="false"/> on any path.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct RepairingOutcome(bool? Consistent, RepairingWindow Window)
{
    /// <summary>The named clash reason on a refutation; <see langword="null"/> on every other outcome.</summary>
    public string? ClashReason { get; init; }

    /// <summary>The named certificate route on a certification — <see cref="ContextRepairingCertifyDecider.RepairedDescribedModelCertificate"/>; <see langword="null"/> on every other outcome.</summary>
    public string? Route { get; init; }

    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static RepairingOutcome SilentWith(RepairingWindow window)
    {
        return new RepairingOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's repairing faces (faces fourteen and
/// fifteen): a tier-3 BOUNDED ENUMERATION over a restriction-rich ground
/// ontology — a large told ABox whose TBox obligations are carried by value,
/// universal and cardinality restrictions the told edges do not satisfy — and
/// the first family that PROPOSES rather than only derives.
/// The CLASH face is MONOTONE and TOLD-ONLY: told object-property assertions
/// closed under told inverse mirroring, symmetry, transitivity and
/// sub-property inclusion give the ground edges, told class assertions,
/// domains, ranges, named subclass steps and existential definitions derive
/// ground memberships to a fixpoint, and a membership meeting its own denial, a
/// told disjoint partner, an asserted empty class, or a denied edge refutes the
/// module. Unrecognized axioms are IGNORED, because a refuted told subset
/// condemns every superset, and the face NEVER reads a repaired edge, a minted
/// element, or the told-sameness quotient: the quotient changes carrier
/// identity, which the clash rules read directly.
/// The CERTIFY face is the opposite discipline, a whole-module admission
/// followed by a repair that INVENTS. Its phases are ordered and the order is
/// load-bearing: the told closure OPERATOR interns the carriers, quotients the
/// told sameness and closes the edge relation, and is re-applied at every
/// commit; deterministic repair inserts each round's whole forced-value set
/// simultaneously over a class table recomputed FROM SCRATCH, because a
/// universal is anti-monotone in the edge relation; bounded witness supply
/// mints a fresh element into an OPEN demand set and opens a choice frame over
/// a CLOSED one; a bounded choice walk over an explicit frame stack enumerates
/// the residue in canonical order; a frozen complement post-pass runs per
/// candidate model after the last mint; and every admitted axiom is finally
/// re-checked against the finished structure. Construction is never trusted —
/// no phase, no closure, no mint and no choice commit is itself a soundness
/// step, and the verification pass is the sole soundness carrier.
/// The certify face declares NO refutation on any path: unrepairable
/// obligation, exhausted walk, bound overflow, failed verification check,
/// admission reject and component-spanning failure all route to SILENCE with
/// the window measurement on the record. The carrier, class and role ceilings
/// are named window constants and the eight search bounds are named constants
/// beside them; every overflow is a silence carrying its measurement, never a
/// verdict.
/// </summary>
internal static class ContextRepairingCertifyDecider
{
    /// <summary>
    /// The carrier ceiling: the repaired model is constructed over exactly up to
    /// this many distinct told individual terms and BOTH faces are SILENT above
    /// it. The ceiling is an ENGINEERING one with overflow-silence and a
    /// revisable value, never a compiled-in corpus fact: the packed edge
    /// relation holds one bit per role and ordered carrier pair, so the relation
    /// costs the square of this constant per role, and this family declares its
    /// own ceiling rather than the sixteen the told-ground and counting faces
    /// carry, because a repair habitat is defeated by ABox breadth the smaller
    /// window excludes outright. Collecting the told shapes is one linear pass
    /// bounded by the module's own axiom count rather than by this constant.
    /// </summary>
    public const int RepairCarrierBound = 256;

    /// <summary>The named-class ceiling: one least-fixpoint extension is carried per distinct named class other than the two semantics-fixed constants, and both faces are SILENT above this many. Shares the carrier bound's engineering derivation and value.</summary>
    public const int RepairClassBound = 256;

    /// <summary>The role ceiling: one edge relation is carried per distinct role mentioned in an admitted axiom, and both faces are SILENT above this many. The relation cost is linear in this constant against the carrier bound's square, so it sits an order below the other two.</summary>
    public const int RepairRoleBound = 32;

    /// <summary>The open-demand ceiling: the phase-2 extraction carries at most this many unmet bounds and existentials, and overflows to SILENCE with the measurement.</summary>
    public const int RepairDemandBound = 256;

    /// <summary>The per-component demand ceiling: one computed component carries at most this many coupled demands, and overflows to SILENCE with the measurement.</summary>
    public const int RepairComponentBound = 8;

    /// <summary>The per-demand candidate ceiling: one demand's canonical candidate list carries at most this many entries, and overflows to SILENCE with the measurement.</summary>
    public const int RepairBranchBound = 8;

    /// <summary>The per-component node ceiling: the walk spends at most this many local candidate evaluations inside ONE component, and overflows to SILENCE with the measurement. The bound is PER COMPONENT, so the whole decomposition's budget is this constant against the component count rather than their product.</summary>
    public const int RepairComponentNodeBound = 4096;

    /// <summary>The component ceiling: the computed decomposition carries at most this many independent components, and overflows to SILENCE with the measurement.</summary>
    public const int RepairComponentCountBound = 64;

    /// <summary>The verification-pass ceiling: one decision spends at most this many whole-module verification passes. The attribution rule bounds the pass count by the SUM of the component candidate counts rather than their product, and this ceiling carries that sum with margin.</summary>
    public const int RepairModelVerifyBound = 64;

    /// <summary>The mint ceiling: one decision mints at most this many fresh elements, and overflows to SILENCE with the measurement.</summary>
    public const int RepairMintBound = 32;

    /// <summary>The cascade ceiling: a mint's typing may re-open the deterministic stage at most this many times before the decision SILENCES with the measurement.</summary>
    public const int RepairCascadeDepthBound = 4;

    /// <summary>The certificate route name of the repaired described model: the module's told terms after the told-sameness quotient, its told edges closed under the re-applied closure operator, its repaired edges, its minted elements and its least-fixpoint class extensions, verified axiom by axiom.</summary>
    public const string RepairedDescribedModelCertificate = "RepairedDescribedModel";

    /// <summary>The word width of one packed bitset word.</summary>
    private const int RepairWordBits = 64;

    /// <summary>
    /// The restriction scope naming EVERY role rather than one: the
    /// universal-as-generator rule fires only where the carrier holds at least
    /// one successor on the role the universal quantifies. It is the scope the
    /// phase-1 ACTIVATION table is computed at, so a membership that holds only
    /// because a role is empty at the carrier cannot FORCE an edge the very next
    /// recompute would strand. The phase-1 table itself is never computed at this
    /// scope: the vacuous firing is sound and load-bearing there, and the
    /// equivalence it carries to verified is re-checked against it.
    /// </summary>
    private const int RepairEveryRole = -2;

    /// <summary>The family's own buffer pool: the packed edge relation, class tables and evaluation scratch are rented from here, never from a shared pool, once per decision and released on a semantic disposable that trims the pool behind it.</summary>
    private static VeritasMemoryPool<ulong> RepairBufferPool { get; } = new();

    /// <summary>Measures the Shape R census window without deciding anything: the carriers the told ground surface holds after the told-sameness quotient, the named classes, the roles, the told edge count, and the window silence the bounds would charge — computed identically dark and lit, so the census ships unconditionally. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement.</returns>
    public static RepairingOutcome Measure(ReasoningModule module)
    {
        return Measure(module, default);
    }

    /// <summary>The construction-options overload of the measurement: the options change only which structures a decision would propose, so the measurement is identical under every value and no verdict is formed on this path either.</summary>
    /// <param name="module">The module to measure.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The silent outcome carrying the measurement.</returns>
    public static RepairingOutcome Measure(ReasoningModule module, RepairingConstructionOptions options)
    {
        _ = options;
        RepairingGround ground = Harvest(module);

        return RepairingOutcome.SilentWith(MeasureWindow(ground));
    }

    /// <summary>
    /// Runs the repairing faces in jurisdiction order: the told ground harvest
    /// and the window measurement first, so a window silence still carries the
    /// numbers; then the monotone told-only clash core, which condemns the whole
    /// module and needs no admission; then the whole-module repairing
    /// certificate only where the clash core stayed silent, so no repair output
    /// can ever reach a clash rule.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the monotone ground refutation, the repaired-described-model certificate, or silence — each with its measurement.</returns>
    public static RepairingOutcome Run(ReasoningModule module)
    {
        return Run(module, default);
    }

    /// <summary>The construction-options overload of the decision: the six variation points reach the construction only through this entry point, the production reasoner path passing none. Every option changes only which structures are proposed, so a non-default value can silence the face or slow its walk and can never move a verdict.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The outcome: the monotone ground refutation, the repaired-described-model certificate, or silence — each with its measurement.</returns>
    public static RepairingOutcome Run(ReasoningModule module, RepairingConstructionOptions options)
    {
        RepairingGround ground = Harvest(module);
        RepairingWindow window = MeasureWindow(ground);
        if(window.WindowSilences > 0)
        {
            return RepairingOutcome.SilentWith(window);
        }

        bool clashOnly = options.ClashOnlyEntry == RepairClashOnlyEntry.Enabled;
        using RepairingBuffers buffers = RepairingBuffers.Reserve(ground, options, clashOnly);
        if(TryRefute(module, ground, buffers, out string? clashReason))
        {
            return new RepairingOutcome(false, window)
            {
                ClashReason = clashReason,
            };
        }

        return clashOnly ? RepairingOutcome.SilentWith(window) : Certify(module, ground, buffers, options, window);
    }

    /// <summary>The internal clash-only entry point: face fourteen's monotone told-only pass with NO phase 0-4 execution behind it. The shipped reasoner block gates RECORDING on the face bits but not EXECUTION, so this is what makes the two faces' jurisdictions separately exercisable.</summary>
    /// <param name="module">The module to refute.</param>
    /// <returns>The monotone ground refutation, or silence — each with its measurement.</returns>
    public static RepairingOutcome RunClashOnly(ReasoningModule module)
    {
        return Run(module, new RepairingConstructionOptions
        {
            ClashOnlyEntry = RepairClashOnlyEntry.Enabled,
        });
    }

    /// <summary>Reads the window off the harvested ground surface: the post-quotient domain size, the named classes, the roles, the told edge count the construction starts from, and the silence any of the three ceilings charges. The carrier comparison reads the TOLD term count, which bounds both the monotone clash face's unquotiented arrays and the certify face's quotiented ones.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    /// <returns>The window measurement.</returns>
    private static RepairingWindow MeasureWindow(RepairingGround ground)
    {
        bool exceeded = ground.Carriers.Count > RepairCarrierBound
            || ground.Classes.Count > RepairClassBound
            || ground.Roles.Count > RepairRoleBound;

        return new RepairingWindow(
            ground.ElementCount,
            ground.Classes.Count,
            ground.Roles.Count,
            ground.ToldEdges.Count,
            MintedElements: 0,
            ChoicePointsOpened: 0,
            EvaluatedNodes: 0,
            ModelVerifyPasses: 0,
            exceeded ? 1 : 0);
    }

    /// <summary>One ground role edge over interned role and element indices.</summary>
    /// <param name="Role">The role index.</param>
    /// <param name="Source">The source index.</param>
    /// <param name="Target">The target index.</param>
    private readonly record struct RepairingEdge(int Role, int Source, int Target);

    /// <summary>One told role pair over interned role indices — a told inverse pair in told argument order, or a told sub-property inclusion from the first role into the second.</summary>
    /// <param name="First">The first role index.</param>
    /// <param name="Second">The second role index.</param>
    private readonly record struct RepairingRolePair(int First, int Second);

    /// <summary>One told data pair the told-pairs reading interprets a data property as: the subject's carrier index, the property IRI, and the told literal.</summary>
    /// <param name="Carrier">The subject's carrier index.</param>
    /// <param name="Property">The data property's IRI.</param>
    /// <param name="Value">The told literal.</param>
    private readonly record struct RepairingDataPair(int Carrier, Utf8String Property, Literal Value);

    /// <summary>The obligation kinds the repair reads out of an obligation position.</summary>
    private enum RepairingObligationKind
    {
        /// <summary>A value pin <c>exists r.{a}</c>: a deterministic forced edge to a told individual.</summary>
        ValuePin = 0,

        /// <summary>A universal <c>forall r.F</c>: a demand-set narrowing, never a demand of its own.</summary>
        Universal = 1,

        /// <summary>An existential <c>exists r.F</c>: a demand for one successor inside the filler.</summary>
        Existential = 2,

        /// <summary>A minimum cardinality: a demand for the bound's many successors inside the filler.</summary>
        MinCardinality = 3,

        /// <summary>A maximum cardinality: a ceiling the mint pre-check reads, never a demand.</summary>
        MaxCardinality = 4,

        /// <summary>An exact cardinality: a demand on its lower half and a ceiling on its upper.</summary>
        ExactCardinality = 5,
    }

    /// <summary>One obligation the repair reads out of an obligation position: the class expression whose members carry it, the role it quantifies, its kind, its bound, its filler, and the pinned value's element index.</summary>
    /// <param name="Activator">The class expression whose members carry the obligation.</param>
    /// <param name="Role">The quantified role's index.</param>
    /// <param name="Kind">The obligation kind.</param>
    /// <param name="Bound">The cardinality bound; one for an existential and a value pin.</param>
    /// <param name="Filler">The qualifying filler, or <see langword="null"/> where the obligation is unqualified.</param>
    /// <param name="Value">The pinned value's element index for a value pin; <c>-1</c> otherwise.</param>
    private sealed record RepairingObligation(OwlClassExpression Activator, int Role, RepairingObligationKind Kind, int Bound, OwlClassExpression? Filler, int Value);

    /// <summary>One seeding rule of the class least fixpoint: a whole admitted class expression whose extension flows into a named class.</summary>
    /// <param name="Source">The class expression the rule evaluates.</param>
    /// <param name="Target">The named class index the evaluated extension flows into.</param>
    private sealed record RepairingSeedRule(OwlClassExpression Source, int Target);

    /// <summary>One told domain or range constraint over an interned role and a told class expression.</summary>
    /// <param name="Role">The constrained role index.</param>
    /// <param name="Constraint">The class expression the role's sources or targets are confined to.</param>
    private sealed record RepairingRoleClass(int Role, OwlClassExpression Constraint);

    /// <summary>One extracted demand: the carrier the frozen class table places in the restricting class, and the obligation it is unmet against.</summary>
    /// <param name="Carrier">The owning element index.</param>
    /// <param name="Obligation">The obligation index.</param>
    private readonly record struct RepairingDemand(int Carrier, int Obligation);

    /// <summary>One frame of the phase-3 walk's explicit stack: the demand the frame stands on and the candidate index it currently proposes.</summary>
    /// <param name="Demand">The residue demand index.</param>
    /// <param name="Candidate">The candidate index inside that demand's canonical list.</param>
    private readonly record struct RepairingFrame(int Demand, int Candidate);

    /// <summary>The axiom shapes the certify face admits, and the three NAMED rejection buckets beside them. The four buckets are CLOSED against the engine's axiom-kind roster: every kind the roster carries answers exactly one of them at the classifier's switch, so no kind silences a module without a named home, and <see cref="Unadmitted"/> is the default arm a roster addition would land in until it is given one.</summary>
    private enum RepairingShape
    {
        /// <summary>The default arm: a kind with no named home. Reaching it silences the certify face and is a defect of the closure duty, not a design state.</summary>
        Unadmitted = 0,

        /// <summary>Rejected for a repairing face by this decider with a named pre-commit-collision precondition: a key axiom, a property chain, or an inverse-functional characteristic, each of which lets an invented edge manufacture a merge the injective carrier map forbids.</summary>
        RejectedForRepair = 1,

        /// <summary>Rejected for the certify face: a negative property assertion, which the monotone clash face consumes instead.</summary>
        RejectedForCertify = 2,

        /// <summary>Rejected into the named widening backlog: a kind this decider is deliberately silent on, each needing its own soundness argument and battery rows before it may ship.</summary>
        RejectedBacklog = 3,

        /// <summary>A declaration, an annotation-family axiom, or an import — no logical content, satisfied by every structure.</summary>
        NonLogical = 4,

        /// <summary>A class assertion over an admitted class expression and an individual term.</summary>
        ClassAssertion = 5,

        /// <summary>An object-property assertion between two individual terms over a plain role.</summary>
        ObjectPropertyAssertion = 6,

        /// <summary>A subclass axiom between two admitted class expressions.</summary>
        SubClassOf = 7,

        /// <summary>An equivalence between two admitted class expressions.</summary>
        EquivalentClasses = 8,

        /// <summary>A disjointness over admitted class expressions.</summary>
        DisjointClasses = 9,

        /// <summary>A domain axiom over a plain role and an admitted class expression.</summary>
        ObjectPropertyDomain = 10,

        /// <summary>A range axiom over a plain role and an admitted class expression.</summary>
        ObjectPropertyRange = 11,

        /// <summary>A told inverse-role pair over plain roles.</summary>
        InverseObjectProperties = 12,

        /// <summary>A plain sub-object-property inclusion.</summary>
        SubObjectPropertyOf = 13,

        /// <summary>A symmetric characteristic over a plain role.</summary>
        SymmetricObjectProperty = 14,

        /// <summary>A transitive characteristic over a plain role.</summary>
        TransitiveObjectProperty = 15,

        /// <summary>A functional characteristic over a plain role.</summary>
        FunctionalObjectProperty = 16,

        /// <summary>A sameness axiom over individual terms.</summary>
        SameIndividual = 17,

        /// <summary>A distinctness axiom over individual terms.</summary>
        DifferentIndividuals = 18,

        /// <summary>A data-property assertion, read under the told-pairs interpretation.</summary>
        DataPropertyAssertion = 19,

        /// <summary>A data-property domain over an admitted class expression.</summary>
        DataPropertyDomain = 20,

        /// <summary>A data-property range over a PLAIN DATATYPE IRI.</summary>
        DataPropertyRange = 21,
    }

    /// <summary>
    /// The told ground surface one pass over the module's axioms collects: the
    /// interned carriers with their told-sameness quotient, the named classes,
    /// the roles MENTIONED anywhere, and the told edges, denials, role algebra,
    /// characteristics, sameness pairs and data pairs read over them. Interning
    /// runs over the WHOLE module rather than over an admitted subset, because
    /// the monotone clash face has no admission and the window bounds both faces
    /// alike.
    /// </summary>
    private sealed class RepairingGround
    {
        /// <summary>The distinct told individual terms in first-seen order, keyed by IRI or anonymous label under the content equality of <see cref="Utf8String"/> — the keying that keeps one term one carrier across every axiom that mentions it.</summary>
        public List<Utf8String> Carriers { get; } = [];

        /// <summary>The identity index over the carriers.</summary>
        public Dictionary<Utf8String, int> CarrierIndices { get; } = [];

        /// <summary>The told-sameness union-find parent of each carrier; the class representative is the least intern index.</summary>
        public List<int> Parents { get; } = [];

        /// <summary>The quotient element index of each carrier — the domain position its representative occupies.</summary>
        public List<int> CarrierElements { get; } = [];

        /// <summary>The domain size after the told-sameness quotient, never below one.</summary>
        public int ElementCount { get; set; }

        /// <summary>The distinct named classes other than <c>owl:Thing</c> and <c>owl:Nothing</c>, in first-seen order.</summary>
        public List<Utf8String> Classes { get; } = [];

        /// <summary>The identity index over the named classes.</summary>
        public Dictionary<Utf8String, int> ClassIndices { get; } = [];

        /// <summary>The distinct roles mentioned anywhere in the module, in first-seen order.</summary>
        public List<Utf8String> Roles { get; } = [];

        /// <summary>The identity index over the roles.</summary>
        public Dictionary<Utf8String, int> RoleIndices { get; } = [];

        /// <summary>The told object-property assertion edges over carrier indices.</summary>
        public List<RepairingEdge> ToldEdges { get; } = [];

        /// <summary>The told negative object-property assertion edges over plain roles and carrier indices.</summary>
        public List<RepairingEdge> DeniedEdges { get; } = [];

        /// <summary>The told inverse-role pairs over plain roles, in told argument order.</summary>
        public List<RepairingRolePair> InversePairs { get; } = [];

        /// <summary>The told plain sub-property inclusions, from the subproperty into the superproperty.</summary>
        public List<RepairingRolePair> SubPropertyPairs { get; } = [];

        /// <summary>The told symmetric roles.</summary>
        public List<int> SymmetricRoles { get; } = [];

        /// <summary>The told transitive roles.</summary>
        public List<int> TransitiveRoles { get; } = [];

        /// <summary>The told functional roles.</summary>
        public List<int> FunctionalRoles { get; } = [];

        /// <summary>The told sameness pairs over carrier indices.</summary>
        public List<RepairingRolePair> SamePairs { get; } = [];

        /// <summary>The told data pairs, the whole extension the told-pairs reading gives every data property.</summary>
        public List<RepairingDataPair> DataPairs { get; } = [];

        /// <summary>The distinct complement expressions the frozen post-pass evaluates, in first-seen order and compared by reference so no structural walk runs over a nested expression.</summary>
        public List<OwlObjectComplementOf> Complements { get; } = [];

        /// <summary>The largest class-expression node count any single axiom's expressions linearize to — the evaluation scratch's row budget.</summary>
        public int NodeCapacity { get; set; }
    }

    /// <summary>Collects the told ground surface in ONE pass over the module's axioms and then applies the told-sameness quotient over it. Nothing is rejected here: the window alone bounds the surface, and the two faces apply their own jurisdiction afterwards.</summary>
    /// <param name="module">The module to collect from.</param>
    /// <returns>The harvested ground surface.</returns>
    private static RepairingGround Harvest(ReasoningModule module)
    {
        RepairingGround ground = new();
        Stack<OwlClassExpression> work = new();
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            CollectAxiom(module.Axioms[index], ground, work);
            DrainExpressions(ground, work);
        }

        Quotient(ground);

        return ground;
    }

    /// <summary>Collects one axiom's direct terms, roles, edges, role algebra and data pairs, and pushes its direct class expressions onto the traversal worklist.</summary>
    /// <param name="axiom">The axiom to collect.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="workToAppendTo">The class-expression traversal worklist.</param>
    private static void CollectAxiom(OwlAxiom axiom, RepairingGround ground, Stack<OwlClassExpression> workToAppendTo)
    {
        switch(axiom)
        {
            case(OwlSubClassOfAxiom subClass):
            {
                workToAppendTo.Push(subClass.SubClass);
                workToAppendTo.Push(subClass.SuperClass);
                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                workToAppendTo.Push(equivalent.First);
                workToAppendTo.Push(equivalent.Second);
                break;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                for(int index = 0; index < disjoint.Operands.Count; index++)
                {
                    workToAppendTo.Push(disjoint.Operands[index]);
                }

                break;
            }
            case(OwlDisjointUnionAxiom union):
            {
                for(int index = 0; index < union.Operands.Count; index++)
                {
                    workToAppendTo.Push(union.Operands[index]);
                }

                break;
            }
            case(OwlHasKeyAxiom key):
            {
                workToAppendTo.Push(key.Class);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                CarrierIndex(ground, assertion.Individual);
                workToAppendTo.Push(assertion.Class);
                break;
            }
            case(OwlObjectPropertyAssertionAxiom assertion):
            {
                CollectToldEdge(assertion, ground);
                break;
            }
            case(OwlNegativeObjectPropertyAssertionAxiom denial):
            {
                CollectDeniedEdge(denial, ground);
                break;
            }
            case(OwlObjectPropertyDomainAxiom domain):
            {
                RoleIndex(ground, domain.Property.Property.Iri);
                workToAppendTo.Push(domain.Domain);
                break;
            }
            case(OwlObjectPropertyRangeAxiom range):
            {
                RoleIndex(ground, range.Property.Property.Iri);
                workToAppendTo.Push(range.Range);
                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom characteristic):
            {
                CollectCharacteristic(characteristic, ground);
                break;
            }
            case(OwlInverseObjectPropertiesAxiom inverse):
            {
                CollectInversePair(inverse, ground);
                break;
            }
            case(OwlSubObjectPropertyOfAxiom inclusion):
            {
                CollectSubProperty(inclusion, ground);
                break;
            }
            case(OwlPropertyChainAxiom chain):
            {
                RoleIndex(ground, chain.SuperProperty.Property.Iri);
                break;
            }
            case(OwlSameIndividualAxiom same):
            {
                CollectSamePair(same, ground);
                break;
            }
            case(OwlDifferentIndividualsAxiom different):
            {
                for(int index = 0; index < different.Individuals.Count; index++)
                {
                    CarrierIndex(ground, different.Individuals[index]);
                }

                break;
            }
            case(OwlDataPropertyAssertionAxiom assertion):
            {
                CollectDataPair(assertion, ground);
                break;
            }
            case(OwlNegativeDataPropertyAssertionAxiom denial):
            {
                CarrierIndex(ground, denial.Source);
                break;
            }
            case(OwlDataPropertyDomainAxiom domain):
            {
                workToAppendTo.Push(domain.Domain);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Drains the class-expression worklist, interning every named class, role and individual position the expressions carry, recording the complement occurrences the frozen post-pass evaluates, and raising the scratch row budget to this axiom's linearized node count — an explicit stack walk that descends through every combinator and filler.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="work">The traversal worklist.</param>
    private static void DrainExpressions(RepairingGround ground, Stack<OwlClassExpression> work)
    {
        int nodes = 0;
        while(work.Count > 0)
        {
            nodes++;
            switch(work.Pop())
            {
                case(OwlClassReference reference):
                {
                    ClassIndex(ground, reference);
                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    for(int index = 0; index < oneOf.Individuals.Count; index++)
                    {
                        CarrierIndex(ground, oneOf.Individuals[index]);
                    }

                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    ComplementIndex(ground, complement);
                    work.Push(complement.Operand);
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        work.Push(intersection.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int index = 0; index < union.Operands.Count; index++)
                    {
                        work.Push(union.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectSomeValuesFrom existential):
                {
                    RoleIndex(ground, existential.Property.Property.Iri);
                    work.Push(existential.Filler);
                    break;
                }
                case(OwlObjectAllValuesFrom universal):
                {
                    RoleIndex(ground, universal.Property.Property.Iri);
                    work.Push(universal.Filler);
                    break;
                }
                case(OwlObjectHasValue hasValue):
                {
                    RoleIndex(ground, hasValue.Property.Property.Iri);
                    CarrierIndex(ground, hasValue.Individual);
                    break;
                }
                case(OwlObjectHasSelf hasSelf):
                {
                    RoleIndex(ground, hasSelf.Property.Property.Iri);
                    break;
                }
                case(OwlObjectCardinality cardinality):
                {
                    RoleIndex(ground, cardinality.Property.Property.Iri);
                    if(cardinality.Filler is OwlClassExpression filler)
                    {
                        work.Push(filler);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        if(nodes > ground.NodeCapacity)
        {
            ground.NodeCapacity = nodes;
        }
    }

    /// <summary>Records one told object-property assertion as a ground edge over interned indices; a source or target that denotes neither a named nor an anonymous individual carries no edge.</summary>
    /// <param name="axiom">The told assertion.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectToldEdge(OwlObjectPropertyAssertionAxiom axiom, RepairingGround ground)
    {
        if(!TryCarrierIndex(ground, axiom.Source, out int source) || !TryCarrierIndex(ground, axiom.Target, out int target))
        {
            return;
        }

        ground.ToldEdges.Add(new RepairingEdge(RoleIndex(ground, axiom.Property.Iri), source, target));
    }

    /// <summary>Records one told negative object-property assertion as a denial over interned indices; an inline inverse role would need a role normalization neither face performs, so it carries no denial.</summary>
    /// <param name="axiom">The told denial.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectDeniedEdge(OwlNegativeObjectPropertyAssertionAxiom axiom, RepairingGround ground)
    {
        if(axiom.Property is not OwlObjectPropertyReference role
            || !TryCarrierIndex(ground, axiom.Source, out int source)
            || !TryCarrierIndex(ground, axiom.Target, out int target))
        {
            return;
        }

        ground.DeniedEdges.Add(new RepairingEdge(RoleIndex(ground, role.Named.Iri), source, target));
    }

    /// <summary>Records one told inverse-role pair over plain roles; an inline inverse argument would need a role normalization neither face performs, so it carries no pair.</summary>
    /// <param name="axiom">The told inverse-properties axiom.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectInversePair(OwlInverseObjectPropertiesAxiom axiom, RepairingGround ground)
    {
        if(axiom.First is not OwlObjectPropertyReference first || axiom.Second is not OwlObjectPropertyReference second)
        {
            return;
        }

        ground.InversePairs.Add(new RepairingRolePair(RoleIndex(ground, first.Named.Iri), RoleIndex(ground, second.Named.Iri)));
    }

    /// <summary>Records one told plain sub-property inclusion; an inline inverse on either side carries no inclusion.</summary>
    /// <param name="axiom">The told inclusion axiom.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectSubProperty(OwlSubObjectPropertyOfAxiom axiom, RepairingGround ground)
    {
        if(axiom.SubProperty is not OwlObjectPropertyReference sub || axiom.SuperProperty is not OwlObjectPropertyReference super)
        {
            return;
        }

        ground.SubPropertyPairs.Add(new RepairingRolePair(RoleIndex(ground, sub.Named.Iri), RoleIndex(ground, super.Named.Iri)));
    }

    /// <summary>Records one told role characteristic the closure operator or the mint pre-check reads: symmetry and transitivity close the edge relation, functionality caps a mint. Every other characteristic interns its role and carries no ground fact.</summary>
    /// <param name="axiom">The told characteristic axiom.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectCharacteristic(OwlObjectPropertyCharacteristicAxiom axiom, RepairingGround ground)
    {
        int role = RoleIndex(ground, axiom.Property.Property.Iri);
        if(axiom.Property is not OwlObjectPropertyReference)
        {
            return;
        }

        switch(axiom.Characteristic)
        {
            case(OwlPropertyCharacteristic.Symmetric):
            {
                ground.SymmetricRoles.Add(role);
                break;
            }
            case(OwlPropertyCharacteristic.Transitive):
            {
                ground.TransitiveRoles.Add(role);
                break;
            }
            case(OwlPropertyCharacteristic.Functional):
            {
                ground.FunctionalRoles.Add(role);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Records one told sameness pair over interned carriers — the union-find input the quotient consumes.</summary>
    /// <param name="axiom">The told sameness axiom.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectSamePair(OwlSameIndividualAxiom axiom, RepairingGround ground)
    {
        if(!TryCarrierIndex(ground, axiom.First, out int first) || !TryCarrierIndex(ground, axiom.Second, out int second))
        {
            return;
        }

        ground.SamePairs.Add(new RepairingRolePair(first, second));
    }

    /// <summary>Records one told data pair — the whole extension the told-pairs reading gives that data property.</summary>
    /// <param name="axiom">The told data-property assertion.</param>
    /// <param name="ground">The ground surface accumulator.</param>
    private static void CollectDataPair(OwlDataPropertyAssertionAxiom axiom, RepairingGround ground)
    {
        if(!TryCarrierIndex(ground, axiom.Source, out int source))
        {
            return;
        }

        ground.DataPairs.Add(new RepairingDataPair(source, axiom.Property.Iri, axiom.Target));
    }

    /// <summary>Interns one individual term as a carrier, keyed by IRI or anonymous label under the content equality of <see cref="Utf8String"/>.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="term">The individual term.</param>
    private static void CarrierIndex(RepairingGround ground, RdfTerm term)
    {
        TryCarrierIndex(ground, term, out _);
    }

    /// <summary>Interns one individual term as a carrier and reads its index; a term that denotes neither a named nor an anonymous individual is no carrier. A blank label is a carrier key WITHIN the module, so two axioms mentioning the same label resolve to one carrier.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="term">The individual term.</param>
    /// <param name="index">The carrier index; <c>-1</c> when the term is no individual.</param>
    /// <returns><see langword="true"/> on an individual term.</returns>
    private static bool TryCarrierIndex(RepairingGround ground, RdfTerm term, out int index)
    {
        switch(term)
        {
            case(NamedNode named):
            {
                index = InternCarrier(ground, named.Iri);

                return true;
            }
            case(BlankNode anonymous):
            {
                index = InternCarrier(ground, anonymous.Label);

                return true;
            }
            default:
            {
                index = -1;

                return false;
            }
        }
    }

    /// <summary>Interns one carrier key, appending it in first-seen order and opening its own union-find class.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="key">The carrier key.</param>
    /// <returns>The carrier index.</returns>
    private static int InternCarrier(RepairingGround ground, Utf8String key)
    {
        if(ground.CarrierIndices.TryGetValue(key, out int index))
        {
            return index;
        }

        index = ground.Carriers.Count;
        ground.Carriers.Add(key);
        ground.CarrierIndices[key] = index;
        ground.Parents.Add(index);
        ground.CarrierElements.Add(index);

        return index;
    }

    /// <summary>Interns one class reference as a fixpoint variable, skipping the two semantics-fixed constants whose extensions are pinned rather than propagated.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="reference">The class reference.</param>
    /// <returns>The class index, or <c>-1</c> for <c>owl:Thing</c> and <c>owl:Nothing</c>.</returns>
    private static int ClassIndex(RepairingGround ground, OwlClassReference reference)
    {
        if(reference.Class.Iri.Equals(OwlVocabulary.Thing) || reference.Class.Iri.Equals(OwlVocabulary.Nothing))
        {
            return -1;
        }

        if(ground.ClassIndices.TryGetValue(reference.Class.Iri, out int index))
        {
            return index;
        }

        index = ground.Classes.Count;
        ground.Classes.Add(reference.Class.Iri);
        ground.ClassIndices[reference.Class.Iri] = index;

        return index;
    }

    /// <summary>Interns one role IRI, so the role index set covers every role MENTIONED in the module rather than only every role carrying a told edge — a mint on an edge-free role is otherwise unrepresentable.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="role">The role IRI.</param>
    /// <returns>The role index.</returns>
    private static int RoleIndex(RepairingGround ground, Utf8String role)
    {
        if(ground.RoleIndices.TryGetValue(role, out int index))
        {
            return index;
        }

        index = ground.Roles.Count;
        ground.Roles.Add(role);
        ground.RoleIndices[role] = index;

        return index;
    }

    /// <summary>Interns one complement occurrence for the frozen post-pass, comparing by reference over the first-seen list so no structural walk runs over a nested expression.</summary>
    /// <param name="ground">The ground surface accumulator.</param>
    /// <param name="complement">The complement expression.</param>
    /// <returns>The complement row index.</returns>
    private static int ComplementIndex(RepairingGround ground, OwlObjectComplementOf complement)
    {
        for(int index = 0; index < ground.Complements.Count; index++)
        {
            if(ReferenceEquals(ground.Complements[index], complement))
            {
                return index;
            }
        }

        ground.Complements.Add(complement);

        return ground.Complements.Count - 1;
    }

    /// <summary>Applies the told-sameness quotient: the union-find merges exactly the told-equality classes, the representative of each class is its LEAST intern index, and the surviving representatives take the domain positions in intern order. The find loop is iterative with path compression, so no helper recurses.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    private static void Quotient(RepairingGround ground)
    {
        for(int index = 0; index < ground.SamePairs.Count; index++)
        {
            RepairingRolePair pair = ground.SamePairs[index];
            int first = FindRepresentative(ground, pair.First);
            int second = FindRepresentative(ground, pair.Second);
            if(first == second)
            {
                continue;
            }

            ground.Parents[first < second ? second : first] = first < second ? first : second;
        }

        int element = 0;
        for(int index = 0; index < ground.Carriers.Count; index++)
        {
            if(FindRepresentative(ground, index) == index)
            {
                ground.CarrierElements[index] = element;
                element++;
            }
        }

        for(int index = 0; index < ground.Carriers.Count; index++)
        {
            ground.CarrierElements[index] = ground.CarrierElements[FindRepresentative(ground, index)];
        }

        ground.ElementCount = element == 0 ? 1 : element;
    }

    /// <summary>Reads one carrier's told-sameness representative — the least intern index of its class — over an iterative find with path compression.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="carrier">The carrier index.</param>
    /// <returns>The representative carrier index.</returns>
    private static int FindRepresentative(RepairingGround ground, int carrier)
    {
        int root = carrier;
        while(ground.Parents[root] != root)
        {
            root = ground.Parents[root];
        }

        int walk = carrier;
        while(ground.Parents[walk] != root)
        {
            int next = ground.Parents[walk];
            ground.Parents[walk] = root;
            walk = next;
        }

        return root;
    }

    /// <summary>Reads one individual term's DOMAIN ELEMENT — its carrier index after the told-sameness quotient. Distinctness is inequality of this index, never syntactic distinctness of terms.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="term">The individual term.</param>
    /// <param name="element">The domain element index; <c>-1</c> when the term is no carrier.</param>
    /// <returns><see langword="true"/> on a carrier term.</returns>
    private static bool TryElementIndex(RepairingGround ground, RdfTerm term, out int element)
    {
        Utf8String key;
        switch(term)
        {
            case(NamedNode named):
            {
                key = named.Iri;
                break;
            }
            case(BlankNode anonymous):
            {
                key = anonymous.Label;
                break;
            }
            default:
            {
                element = -1;

                return false;
            }
        }

        if(!ground.CarrierIndices.TryGetValue(key, out int carrier))
        {
            element = -1;

            return false;
        }

        element = ground.CarrierElements[carrier];

        return true;
    }

    /// <summary>
    /// The decision's whole packed working set, owned by ONE rental from the
    /// family's own pool and released on the disposable's own scope: the
    /// monotone clash face's edge, denial and membership relations over the
    /// UNQUOTIENTED carriers, and the certify construction's edge relation, its
    /// baseline snapshot, its class table, the per-demand restricted table the
    /// vacuity guard forms, the frozen complement rows and the two evaluation
    /// scratch regions over the quotiented domain. Every region is packed
    /// <see cref="ulong"/> bitsets in one flat array with no per-element object
    /// anywhere, and the whole array is zeroed on reservation because a rented
    /// buffer carries whatever the previous rental left.
    /// </summary>
    private sealed class RepairingBuffers: IDisposable
    {
        /// <summary>The single rental backing every region, supplied by the reservation factory that is this type's only construction path.</summary>
        private IMemoryOwner<ulong> Owner { get; init; } = default!;

        /// <summary>Whether the rental has already been returned.</summary>
        private bool Released { get; set; }

        /// <summary>The clash face's domain size — the told carrier count without the quotient, never below one.</summary>
        public int ClashDelta { get; init; }

        /// <summary>The words one clash-face row spans.</summary>
        public int ClashWords { get; init; }

        /// <summary>The certify construction's domain capacity — the post-quotient element count plus the mint ceiling, never below one.</summary>
        public int Delta { get; init; }

        /// <summary>The words one certify-side row spans.</summary>
        public int Words { get; init; }

        /// <summary>The evaluation scratch's row budget, one row per linearized class-expression node.</summary>
        public int NodeCapacity { get; init; }

        /// <summary>The word offset of the clash edge relation.</summary>
        private int ClashEdgeOffset { get; init; }

        /// <summary>The word offset of the clash denial relation.</summary>
        private int ClashDenialOffset { get; init; }

        /// <summary>The word offset of the clash positive membership table.</summary>
        private int ClashPositiveOffset { get; init; }

        /// <summary>The word offset of the clash negative membership table.</summary>
        private int ClashNegativeOffset { get; init; }

        /// <summary>The word offset of the committed edge relation.</summary>
        private int EdgeOffset { get; init; }

        /// <summary>The word offset of the baseline edge snapshot the phase-3 frames restore.</summary>
        private int BaselineOffset { get; init; }

        /// <summary>The word offset of the class table.</summary>
        private int ClassOffset { get; init; }

        /// <summary>The word offset of the per-demand restricted class table.</summary>
        private int RestrictedOffset { get; init; }

        /// <summary>The word offset of the frozen complement rows.</summary>
        private int ComplementOffset { get; init; }

        /// <summary>The word offset of the first evaluation scratch region.</summary>
        private int ScratchAOffset { get; init; }

        /// <summary>The word offset of the second evaluation scratch region.</summary>
        private int ScratchBOffset { get; init; }

        /// <summary>The words the clash relations span per role.</summary>
        private int ClashRoleWords { get; init; }

        /// <summary>The words the clash membership tables span.</summary>
        private int ClashClassWords { get; init; }

        /// <summary>The words the certify edge relation spans.</summary>
        private int EdgeWords { get; init; }

        /// <summary>The words the certify class tables span.</summary>
        private int ClassWords { get; init; }

        /// <summary>The words the frozen complement rows span.</summary>
        private int ComplementWords { get; init; }

        /// <summary>The words one evaluation scratch region spans.</summary>
        private int ScratchWords { get; init; }

        /// <summary>The clash face's edge relation, indexed role-major then source.</summary>
        public Span<ulong> ClashEdges => Owner.Memory.Span.Slice(ClashEdgeOffset, ClashRoleWords);

        /// <summary>The clash face's told denial relation, indexed identically to <see cref="ClashEdges"/>.</summary>
        public Span<ulong> ClashDenials => Owner.Memory.Span.Slice(ClashDenialOffset, ClashRoleWords);

        /// <summary>The clash face's positive ground membership table, indexed class-major.</summary>
        public Span<ulong> ClashPositive => Owner.Memory.Span.Slice(ClashPositiveOffset, ClashClassWords);

        /// <summary>The clash face's told negative membership table, indexed class-major.</summary>
        public Span<ulong> ClashNegative => Owner.Memory.Span.Slice(ClashNegativeOffset, ClashClassWords);

        /// <summary>The committed edge relation, indexed role-major then source.</summary>
        public Span<ulong> Edges => Owner.Memory.Span.Slice(EdgeOffset, EdgeWords);

        /// <summary>The baseline edge snapshot the phase-3 frames restore on every backtrack.</summary>
        public Span<ulong> Baseline => Owner.Memory.Span.Slice(BaselineOffset, EdgeWords);

        /// <summary>The class table, indexed class-major, recomputed from scratch at every commit.</summary>
        public Span<ulong> Classes => Owner.Memory.Span.Slice(ClassOffset, ClassWords);

        /// <summary>The per-demand restricted class table the vacuity guard forms — a side computation, never the phase-1 table.</summary>
        public Span<ulong> Restricted => Owner.Memory.Span.Slice(RestrictedOffset, ClassWords);

        /// <summary>The frozen complement rows the post-pass writes and the verification pass reads.</summary>
        public Span<ulong> Complements => Owner.Memory.Span.Slice(ComplementOffset, ComplementWords);

        /// <summary>The first evaluation scratch region.</summary>
        public Span<ulong> ScratchA => Owner.Memory.Span.Slice(ScratchAOffset, ScratchWords);

        /// <summary>The second evaluation scratch region.</summary>
        public Span<ulong> ScratchB => Owner.Memory.Span.Slice(ScratchBOffset, ScratchWords);

        /// <summary>Reserves the whole working set in ONE rental sized from the harvested surface, zeroing it before any region is read. A clash-only decision reserves the certify regions at zero length, so nothing behind the clash face is paid for.</summary>
        /// <param name="ground">The harvested ground surface.</param>
        /// <param name="options">The construction options, whose bounds size the mint capacity.</param>
        /// <param name="clashOnly">Whether only the monotone clash face runs.</param>
        /// <returns>The reserved working set.</returns>
        public static RepairingBuffers Reserve(RepairingGround ground, RepairingConstructionOptions options, bool clashOnly)
        {
            int clashDelta = ground.Carriers.Count == 0 ? 1 : ground.Carriers.Count;
            int clashWords = (clashDelta + RepairWordBits - 1) / RepairWordBits;
            int roles = ground.Roles.Count;
            int classes = ground.Classes.Count;
            int delta = clashOnly ? 1 : ground.ElementCount + options.Bounds.Mint;
            int words = (delta + RepairWordBits - 1) / RepairWordBits;
            int nodes = ground.NodeCapacity == 0 ? 1 : ground.NodeCapacity;
            int clashRoleWords = roles * clashDelta * clashWords;
            int clashClassWords = classes * clashWords;
            int edgeWords = clashOnly ? 0 : roles * delta * words;
            int classWords = clashOnly ? 0 : classes * words;
            int complementWords = clashOnly ? 0 : ground.Complements.Count * words;
            int scratchWords = clashOnly ? 0 : nodes * words;

            int clashEdgeOffset = 0;
            int clashDenialOffset = clashEdgeOffset + clashRoleWords;
            int clashPositiveOffset = clashDenialOffset + clashRoleWords;
            int clashNegativeOffset = clashPositiveOffset + clashClassWords;
            int edgeOffset = clashNegativeOffset + clashClassWords;
            int baselineOffset = edgeOffset + edgeWords;
            int classOffset = baselineOffset + edgeWords;
            int restrictedOffset = classOffset + classWords;
            int complementOffset = restrictedOffset + classWords;
            int scratchAOffset = complementOffset + complementWords;
            int scratchBOffset = scratchAOffset + scratchWords;
            int total = scratchBOffset + scratchWords;

            IMemoryOwner<ulong> owner = RepairBufferPool.Rent(total == 0 ? 1 : total);
            owner.Memory.Span.Clear();

            return new RepairingBuffers
            {
                Owner = owner,
                Released = false,
                ClashDelta = clashDelta,
                ClashWords = clashWords,
                Delta = delta,
                Words = words,
                NodeCapacity = nodes,
                ClashEdgeOffset = clashEdgeOffset,
                ClashDenialOffset = clashDenialOffset,
                ClashPositiveOffset = clashPositiveOffset,
                ClashNegativeOffset = clashNegativeOffset,
                EdgeOffset = edgeOffset,
                BaselineOffset = baselineOffset,
                ClassOffset = classOffset,
                RestrictedOffset = restrictedOffset,
                ComplementOffset = complementOffset,
                ScratchAOffset = scratchAOffset,
                ScratchBOffset = scratchBOffset,
                ClashRoleWords = clashRoleWords,
                ClashClassWords = clashClassWords,
                EdgeWords = edgeWords,
                ClassWords = classWords,
                ComplementWords = complementWords,
                ScratchWords = scratchWords,
            };
        }

        /// <summary>Returns the rental to the family's own pool and trims the pool behind it, so a decision leaves no slab held.</summary>
        public void Dispose()
        {
            if(Released)
            {
                return;
            }

            Released = true;
            Owner.Dispose();
            RepairBufferPool.TrimExcess();
        }
    }

    /// <summary>Reads one bit of a packed row.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="index">The bit position.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    private static bool TestBit(ReadOnlySpan<ulong> words, int rowStart, int index)
    {
        return (words[rowStart + (index / RepairWordBits)] & (1UL << (index % RepairWordBits))) != 0;
    }

    /// <summary>Sets one bit of a packed row and reports whether it was new.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="index">The bit position.</param>
    /// <returns><see langword="true"/> when the bit was not already set.</returns>
    private static bool SetBit(Span<ulong> words, int rowStart, int index)
    {
        int slot = rowStart + (index / RepairWordBits);
        ulong mask = 1UL << (index % RepairWordBits);
        if((words[slot] & mask) != 0)
        {
            return false;
        }

        words[slot] |= mask;

        return true;
    }

    /// <summary>Fills one packed row with the whole domain, leaving every position above it clear so no later intersection reads a phantom element.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="rowWords">The words the row spans.</param>
    /// <param name="deltaSize">The domain size.</param>
    private static void FillRow(Span<ulong> words, int rowStart, int rowWords, int deltaSize)
    {
        for(int index = 0; index < rowWords; index++)
        {
            int low = index * RepairWordBits;
            int span = deltaSize - low;
            words[rowStart + index] = span >= RepairWordBits
                ? ulong.MaxValue
                : span <= 0 ? 0UL : (1UL << span) - 1UL;
        }
    }

    /// <summary>Clears one packed row.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="rowWords">The words the row spans.</param>
    private static void ClearRow(Span<ulong> words, int rowStart, int rowWords)
    {
        for(int index = 0; index < rowWords; index++)
        {
            words[rowStart + index] = 0UL;
        }
    }

    /// <summary>One told named-class inclusion step the monotone clash core propagates along.</summary>
    /// <param name="From">The subsumed named class index.</param>
    /// <param name="To">The subsuming named class index.</param>
    private readonly record struct RepairingInclusion(int From, int To);

    /// <summary>One told existential membership source: a named class the module tells a top-level existential over a plain role is INCLUDED IN.</summary>
    /// <param name="Target">The named class index a source of the edge is derived into.</param>
    /// <param name="Role">The existential's role index.</param>
    /// <param name="Filler">The existential's named filler class index, or <c>-1</c> where the filler is <c>owl:Thing</c> and every edge target qualifies.</param>
    private readonly record struct RepairingExistentialSource(int Target, int Role, int Filler);

    /// <summary>One told disjointness edge between two named classes.</summary>
    /// <param name="First">The first named class index.</param>
    /// <param name="Second">The second named class index.</param>
    private readonly record struct RepairingClassPair(int First, int Second);

    /// <summary>One told domain or range constraint over an interned plain role and an interned named class.</summary>
    /// <param name="Role">The constrained role index.</param>
    /// <param name="Class">The named class index the role's sources or targets are confined to.</param>
    private readonly record struct RepairingNamedRoleClass(int Role, int Class);

    /// <summary>One told named membership a class assertion seeds directly into the class table.</summary>
    /// <param name="Class">The named class index.</param>
    /// <param name="Element">The domain element index.</param>
    private readonly record struct RepairingMembership(int Class, int Element);

    /// <summary>
    /// The monotone clash core: closes the told edges under the told inverse,
    /// symmetry, transitivity and sub-property rules — every one of which is
    /// entailed in every model of the told axiom set and adds no carrier — then
    /// derives the ground memberships to a fixpoint and answers whether the
    /// recognized told subset is unsatisfiable. Unrecognized axioms are IGNORED
    /// rather than rejecting the module, because a refuted subset condemns every
    /// superset. The core reads the UNQUOTIENTED carriers: the told-sameness
    /// quotient changes carrier identity and the clash rules read carrier
    /// identity directly, so the quotient is never shared with this face.
    /// </summary>
    /// <param name="module">The module to refute.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="clashReason">The named clash reason; <see langword="null"/> when no clash was reached.</param>
    /// <returns><see langword="true"/> when the recognized subset — and therefore the whole module — is inconsistent.</returns>
    private static bool TryRefute(ReasoningModule module, RepairingGround ground, RepairingBuffers buffers, [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        CloseToldRelation(ground, buffers);
        Span<ulong> edges = buffers.ClashEdges;
        Span<ulong> denials = buffers.ClashDenials;
        for(int index = 0; index < ground.DeniedEdges.Count; index++)
        {
            RepairingEdge denied = ground.DeniedEdges[index];
            SetBit(denials, ClashEdgeRow(buffers, denied.Role, denied.Source), denied.Target);
        }

        for(int role = 0; role < ground.Roles.Count; role++)
        {
            for(int source = 0; source < buffers.ClashDelta; source++)
            {
                int row = ClashEdgeRow(buffers, role, source);
                for(int word = 0; word < buffers.ClashWords; word++)
                {
                    if((edges[row + word] & denials[row + word]) != 0)
                    {
                        clashReason = RepairingGroundClashReasons.ContradictoryEdge(ground.Roles[role]);

                        return true;
                    }
                }
            }
        }

        List<RepairingNamedRoleClass> domains = [];
        List<RepairingNamedRoleClass> ranges = [];
        List<RepairingInclusion> inclusions = [];
        List<RepairingExistentialSource> existentials = [];
        List<RepairingClassPair> disjointness = [];
        Span<ulong> positive = buffers.ClashPositive;
        Span<ulong> negative = buffers.ClashNegative;
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            if(CollectClashPremise(module.Axioms[index], ground, buffers, positive, negative, domains, ranges, inclusions, existentials, disjointness, out clashReason))
            {
                return true;
            }
        }

        SeedClashRoleClasses(buffers, domains, ranges, positive);
        PropagateClashMemberships(buffers, inclusions, existentials, positive);

        for(int index = 0; index < ground.Classes.Count; index++)
        {
            int row = index * buffers.ClashWords;
            for(int word = 0; word < buffers.ClashWords; word++)
            {
                if((positive[row + word] & negative[row + word]) != 0)
                {
                    clashReason = RepairingGroundClashReasons.ComplementedMembership(ground.Classes[index]);

                    return true;
                }
            }
        }

        for(int index = 0; index < disjointness.Count; index++)
        {
            RepairingClassPair pair = disjointness[index];
            int first = pair.First * buffers.ClashWords;
            int second = pair.Second * buffers.ClashWords;
            for(int word = 0; word < buffers.ClashWords; word++)
            {
                if((positive[first + word] & positive[second + word]) != 0)
                {
                    clashReason = RepairingGroundClashReasons.DisjointMembership(ground.Classes[pair.First]);

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Reads one clash-face edge row's first word index — role-major then source.</summary>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="role">The role index.</param>
    /// <param name="source">The source carrier index.</param>
    /// <returns>The row's first word index.</returns>
    private static int ClashEdgeRow(RepairingBuffers buffers, int role, int source)
    {
        return ((role * buffers.ClashDelta) + source) * buffers.ClashWords;
    }

    /// <summary>Closes the told edges to the LEAST FIXPOINT of the told inverse, symmetry, transitivity and sub-property rules over an explicit worklist. Each rule transcribes one told axiom's satisfaction condition as a production, so every added pair holds in every model of the told axiom set and no carrier is added; the lattice is finite, so the loop terminates.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="buffers">The decision's reserved working set.</param>
    private static void CloseToldRelation(RepairingGround ground, RepairingBuffers buffers)
    {
        Queue<RepairingEdge> work = new();
        Span<ulong> edges = buffers.ClashEdges;
        for(int index = 0; index < ground.ToldEdges.Count; index++)
        {
            RepairingEdge edge = ground.ToldEdges[index];
            if(SetBit(edges, ClashEdgeRow(buffers, edge.Role, edge.Source), edge.Target))
            {
                work.Enqueue(edge);
            }
        }

        while(work.Count > 0)
        {
            RepairingEdge edge = work.Dequeue();
            for(int index = 0; index < ground.InversePairs.Count; index++)
            {
                RepairingRolePair pair = ground.InversePairs[index];
                if(pair.First == edge.Role)
                {
                    AddClashEdge(buffers, work, pair.Second, edge.Target, edge.Source);
                }

                if(pair.Second == edge.Role)
                {
                    AddClashEdge(buffers, work, pair.First, edge.Target, edge.Source);
                }
            }

            for(int index = 0; index < ground.SubPropertyPairs.Count; index++)
            {
                RepairingRolePair pair = ground.SubPropertyPairs[index];
                if(pair.First == edge.Role)
                {
                    AddClashEdge(buffers, work, pair.Second, edge.Source, edge.Target);
                }
            }

            if(ground.SymmetricRoles.Contains(edge.Role))
            {
                AddClashEdge(buffers, work, edge.Role, edge.Target, edge.Source);
            }

            if(!ground.TransitiveRoles.Contains(edge.Role))
            {
                continue;
            }

            for(int other = 0; other < buffers.ClashDelta; other++)
            {
                if(TestBit(buffers.ClashEdges, ClashEdgeRow(buffers, edge.Role, edge.Target), other))
                {
                    AddClashEdge(buffers, work, edge.Role, edge.Source, other);
                }

                if(TestBit(buffers.ClashEdges, ClashEdgeRow(buffers, edge.Role, other), edge.Source))
                {
                    AddClashEdge(buffers, work, edge.Role, other, edge.Target);
                }
            }
        }
    }

    /// <summary>Adds one closed edge to the clash relation, enqueueing it for further closure only where it is new.</summary>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="workToAppendTo">The closure worklist.</param>
    /// <param name="role">The role the edge lands in.</param>
    /// <param name="source">The edge's source carrier.</param>
    /// <param name="target">The edge's target carrier.</param>
    private static void AddClashEdge(RepairingBuffers buffers, Queue<RepairingEdge> workToAppendTo, int role, int source, int target)
    {
        if(SetBit(buffers.ClashEdges, ClashEdgeRow(buffers, role, source), target))
        {
            workToAppendTo.Enqueue(new RepairingEdge(role, source, target));
        }
    }

    /// <summary>Collects one axiom's told ground premises: the class assertion's positive and complemented memberships together with the two outright empty-class clashes, the domain and range constraints over named targets, the named-to-named subclass steps, the existential membership sources in equivalence and subclass-superset position, and the told disjointness edges. Every unrecognized shape is ignored.</summary>
    /// <param name="axiom">The axiom to collect.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="positiveToAppendTo">The positive membership table.</param>
    /// <param name="negativeToAppendTo">The negative membership table.</param>
    /// <param name="domainsToAppendTo">The domain constraints.</param>
    /// <param name="rangesToAppendTo">The range constraints.</param>
    /// <param name="inclusionsToAppendTo">The named subclass steps.</param>
    /// <param name="existentialsToAppendTo">The existential membership sources.</param>
    /// <param name="disjointnessToAppendTo">The told disjointness edges.</param>
    /// <param name="clashReason">The outright clash reason; <see langword="null"/> when the axiom carried none.</param>
    /// <returns><see langword="true"/> when the axiom is an outright empty-class assertion.</returns>
    private static bool CollectClashPremise(
        OwlAxiom axiom,
        RepairingGround ground,
        RepairingBuffers buffers,
        Span<ulong> positiveToAppendTo,
        Span<ulong> negativeToAppendTo,
        List<RepairingNamedRoleClass> domainsToAppendTo,
        List<RepairingNamedRoleClass> rangesToAppendTo,
        List<RepairingInclusion> inclusionsToAppendTo,
        List<RepairingExistentialSource> existentialsToAppendTo,
        List<RepairingClassPair> disjointnessToAppendTo,
        [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        switch(axiom)
        {
            case(OwlClassAssertionAxiom assertion):
            {
                return CollectAssertedMembership(assertion, ground, buffers, positiveToAppendTo, negativeToAppendTo, out clashReason);
            }
            case(OwlObjectPropertyDomainAxiom { Property: OwlObjectPropertyReference domainRole } domain):
            {
                CollectNamedRoleClass(domain.Domain, domainRole.Named.Iri, ground, domainsToAppendTo);

                return false;
            }
            case(OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference rangeRole } range):
            {
                CollectNamedRoleClass(range.Range, rangeRole.Named.Iri, ground, rangesToAppendTo);

                return false;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                CollectInclusion(subClass, ground, inclusionsToAppendTo);
                CollectExistentialSource(subClass.SuperClass, subClass.SubClass, ground, existentialsToAppendTo);

                return false;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                CollectExistentialSource(equivalent.First, equivalent.Second, ground, existentialsToAppendTo);
                CollectExistentialSource(equivalent.Second, equivalent.First, ground, existentialsToAppendTo);

                return false;
            }
            case(OwlDisjointClassesAxiom disjoint):
            {
                CollectDisjointness(disjoint, ground, disjointnessToAppendTo);

                return false;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Collects one told class assertion's ground memberships: a named class types the term, a top-level complement of a named class denies it, and an intersection wrapper is read conjunct by conjunct. Asserting <c>owl:Nothing</c>, or the complement of <c>owl:Thing</c>, demands a member of an extension empty in every interpretation and clashes outright.</summary>
    /// <param name="axiom">The told class assertion.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="positiveToAppendTo">The positive membership table.</param>
    /// <param name="negativeToAppendTo">The negative membership table.</param>
    /// <param name="clashReason">The outright clash reason; <see langword="null"/> when the assertion carried none.</param>
    /// <returns><see langword="true"/> on the outright empty-class assertion.</returns>
    private static bool CollectAssertedMembership(
        OwlClassAssertionAxiom axiom,
        RepairingGround ground,
        RepairingBuffers buffers,
        Span<ulong> positiveToAppendTo,
        Span<ulong> negativeToAppendTo,
        [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        if(!TryCarrierIndex(ground, axiom.Individual, out int term))
        {
            return false;
        }

        if(axiom.Class is not OwlObjectIntersectionOf intersection)
        {
            return CollectAssertedConjunct(axiom.Class, term, ground, buffers, positiveToAppendTo, negativeToAppendTo, out clashReason);
        }

        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            if(CollectAssertedConjunct(intersection.Operands[index], term, ground, buffers, positiveToAppendTo, negativeToAppendTo, out clashReason))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Collects one told conjunct of an asserted class expression into the ground membership tables.</summary>
    /// <param name="conjunct">The conjunct expression.</param>
    /// <param name="term">The carrier index the assertion types.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="positiveToAppendTo">The positive membership table.</param>
    /// <param name="negativeToAppendTo">The negative membership table.</param>
    /// <param name="clashReason">The outright clash reason; <see langword="null"/> when the conjunct carried none.</param>
    /// <returns><see langword="true"/> on the outright empty-class conjunct.</returns>
    private static bool CollectAssertedConjunct(
        OwlClassExpression conjunct,
        int term,
        RepairingGround ground,
        RepairingBuffers buffers,
        Span<ulong> positiveToAppendTo,
        Span<ulong> negativeToAppendTo,
        [NotNullWhen(true)] out string? clashReason)
    {
        clashReason = null;
        if(conjunct is OwlClassReference asserted)
        {
            if(asserted.Class.Iri.Equals(OwlVocabulary.Nothing))
            {
                clashReason = RepairingGroundClashReasons.AssertedNothingMembership(ground.Carriers[term]);

                return true;
            }

            if(ground.ClassIndices.TryGetValue(asserted.Class.Iri, out int assertedClass))
            {
                SetBit(positiveToAppendTo, assertedClass * buffers.ClashWords, term);
            }

            return false;
        }

        if(conjunct is not OwlObjectComplementOf complement || complement.Operand is not OwlClassReference denied)
        {
            return false;
        }

        if(denied.Class.Iri.Equals(OwlVocabulary.Thing))
        {
            clashReason = RepairingGroundClashReasons.AssertedNothingMembership(ground.Carriers[term]);

            return true;
        }

        if(ground.ClassIndices.TryGetValue(denied.Class.Iri, out int deniedClass))
        {
            SetBit(negativeToAppendTo, deniedClass * buffers.ClashWords, term);
        }

        return false;
    }

    /// <summary>Collects one told domain or range constraint whose target is a NAMED class; a complex target confines the ends to a disjunction rather than to one ground class and carries no ground fact.</summary>
    /// <param name="target">The constraint's class expression.</param>
    /// <param name="role">The constrained role's IRI.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="constraintsToAppendTo">The constraint accumulator.</param>
    private static void CollectNamedRoleClass(OwlClassExpression target, Utf8String role, RepairingGround ground, List<RepairingNamedRoleClass> constraintsToAppendTo)
    {
        if(target is not OwlClassReference reference
            || !ground.ClassIndices.TryGetValue(reference.Class.Iri, out int constrained)
            || !ground.RoleIndices.TryGetValue(role, out int constrainedRole))
        {
            return;
        }

        constraintsToAppendTo.Add(new RepairingNamedRoleClass(constrainedRole, constrained));
    }

    /// <summary>Collects one told subclass step between two NAMED classes; a complex side carries no ground step.</summary>
    /// <param name="axiom">The candidate subclass axiom.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="inclusionsToAppendTo">The inclusion accumulator.</param>
    private static void CollectInclusion(OwlSubClassOfAxiom axiom, RepairingGround ground, List<RepairingInclusion> inclusionsToAppendTo)
    {
        if(axiom.SubClass is not OwlClassReference sub
            || axiom.SuperClass is not OwlClassReference super
            || !ground.ClassIndices.TryGetValue(sub.Class.Iri, out int from)
            || !ground.ClassIndices.TryGetValue(super.Class.Iri, out int to))
        {
            return;
        }

        inclusionsToAppendTo.Add(new RepairingInclusion(from, to));
    }

    /// <summary>Collects one existential membership source, read in the given side order: a top-level existential over a plain role whose filler is <c>owl:Thing</c> or a named class, paired with a named class the module tells the existential is INCLUDED IN. Only that direction is read — the converse would owe a successor the told axioms never named.</summary>
    /// <param name="existentialSide">The candidate existential side.</param>
    /// <param name="namedSide">The candidate named-class side.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="existentialsToAppendTo">The existential-source accumulator.</param>
    private static void CollectExistentialSource(OwlClassExpression existentialSide, OwlClassExpression namedSide, RepairingGround ground, List<RepairingExistentialSource> existentialsToAppendTo)
    {
        if(existentialSide is not OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference role } existential
            || namedSide is not OwlClassReference named
            || !ground.ClassIndices.TryGetValue(named.Class.Iri, out int target)
            || !ground.RoleIndices.TryGetValue(role.Named.Iri, out int existentialRole)
            || existential.Filler is not OwlClassReference filler)
        {
            return;
        }

        if(filler.Class.Iri.Equals(OwlVocabulary.Thing))
        {
            existentialsToAppendTo.Add(new RepairingExistentialSource(target, existentialRole, -1));

            return;
        }

        if(ground.ClassIndices.TryGetValue(filler.Class.Iri, out int fillerClass))
        {
            existentialsToAppendTo.Add(new RepairingExistentialSource(target, existentialRole, fillerClass));
        }
    }

    /// <summary>Collects one told disjointness axiom of any arity as unordered pairs over its NAMED class operands — only TOLD edges, never a derived disjointness.</summary>
    /// <param name="axiom">The told disjointness axiom.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="disjointnessToAppendTo">The disjointness accumulator.</param>
    private static void CollectDisjointness(OwlDisjointClassesAxiom axiom, RepairingGround ground, List<RepairingClassPair> disjointnessToAppendTo)
    {
        for(int first = 0; first < axiom.Operands.Count; first++)
        {
            if(axiom.Operands[first] is not OwlClassReference left || !ground.ClassIndices.TryGetValue(left.Class.Iri, out int leftClass))
            {
                continue;
            }

            for(int second = first + 1; second < axiom.Operands.Count; second++)
            {
                if(axiom.Operands[second] is OwlClassReference right
                    && ground.ClassIndices.TryGetValue(right.Class.Iri, out int rightClass)
                    && rightClass != leftClass)
                {
                    disjointnessToAppendTo.Add(new RepairingClassPair(leftClass, rightClass));
                }
            }
        }
    }

    /// <summary>Seeds the ground memberships the told domain and range constraints force over the CLOSED edges — the derived edges included, since a constraint holds of a role's whole extension and not merely of its told part.</summary>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="domains">The told domain constraints.</param>
    /// <param name="ranges">The told range constraints.</param>
    /// <param name="membershipsToAppendTo">The positive membership table.</param>
    private static void SeedClashRoleClasses(RepairingBuffers buffers, List<RepairingNamedRoleClass> domains, List<RepairingNamedRoleClass> ranges, Span<ulong> membershipsToAppendTo)
    {
        for(int index = 0; index < domains.Count; index++)
        {
            RepairingNamedRoleClass domain = domains[index];
            for(int source = 0; source < buffers.ClashDelta; source++)
            {
                if(!IsRowEmpty(buffers.ClashEdges, ClashEdgeRow(buffers, domain.Role, source), buffers.ClashWords))
                {
                    SetBit(membershipsToAppendTo, domain.Class * buffers.ClashWords, source);
                }
            }
        }

        for(int index = 0; index < ranges.Count; index++)
        {
            RepairingNamedRoleClass range = ranges[index];
            for(int source = 0; source < buffers.ClashDelta; source++)
            {
                int row = ClashEdgeRow(buffers, range.Role, source);
                for(int target = 0; target < buffers.ClashDelta; target++)
                {
                    if(TestBit(buffers.ClashEdges, row, target))
                    {
                        SetBit(membershipsToAppendTo, range.Class * buffers.ClashWords, target);
                    }
                }
            }
        }
    }

    /// <summary>Whether one packed row holds no element.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="rowWords">The words the row spans.</param>
    /// <returns><see langword="true"/> when every word is clear.</returns>
    private static bool IsRowEmpty(ReadOnlySpan<ulong> words, int rowStart, int rowWords)
    {
        for(int index = 0; index < rowWords; index++)
        {
            if(words[rowStart + index] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs the bounded worklist over the named subclass steps and the existential membership sources to a fixpoint: each derivation re-offers every rule, and the loop ends when no rule derives anything further. Every derivation adds a membership no rule retracts and the table is finite, so the loop terminates.</summary>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="inclusions">The named subclass steps.</param>
    /// <param name="existentials">The existential membership sources.</param>
    /// <param name="membershipsToAppendTo">The positive membership table.</param>
    private static void PropagateClashMemberships(RepairingBuffers buffers, List<RepairingInclusion> inclusions, List<RepairingExistentialSource> existentials, Span<ulong> membershipsToAppendTo)
    {
        bool derived = true;
        while(derived)
        {
            derived = false;
            for(int index = 0; index < inclusions.Count; index++)
            {
                RepairingInclusion inclusion = inclusions[index];
                int from = inclusion.From * buffers.ClashWords;
                int to = inclusion.To * buffers.ClashWords;
                for(int word = 0; word < buffers.ClashWords; word++)
                {
                    ulong merged = membershipsToAppendTo[to + word] | membershipsToAppendTo[from + word];
                    if(merged != membershipsToAppendTo[to + word])
                    {
                        membershipsToAppendTo[to + word] = merged;
                        derived = true;
                    }
                }
            }

            for(int index = 0; index < existentials.Count; index++)
            {
                RepairingExistentialSource source = existentials[index];
                for(int subject = 0; subject < buffers.ClashDelta; subject++)
                {
                    if(TestBit(membershipsToAppendTo, source.Target * buffers.ClashWords, subject))
                    {
                        continue;
                    }

                    int row = ClashEdgeRow(buffers, source.Role, subject);
                    for(int target = 0; target < buffers.ClashDelta; target++)
                    {
                        if(!TestBit(buffers.ClashEdges, row, target))
                        {
                            continue;
                        }

                        if(source.Filler >= 0 && !TestBit(membershipsToAppendTo, source.Filler * buffers.ClashWords, target))
                        {
                            continue;
                        }

                        SetBit(membershipsToAppendTo, source.Target * buffers.ClashWords, subject);
                        derived = true;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>One reusable evaluation slot: the linearized node list of the expression under evaluation and its per-node child ranges. Two slots exist so a subset or disjointness check can hold two extensions at once, and both are reused across evaluations so no node allocation rides the walk.</summary>
    private sealed class RepairingEvaluationSlot
    {
        /// <summary>The linearized nodes, every child following its parent.</summary>
        public List<OwlClassExpression> Nodes { get; } = [];

        /// <summary>The per-node child-range starts.</summary>
        public List<int> FirstChild { get; } = [];

        /// <summary>The per-node child-range lengths.</summary>
        public List<int> ChildCount { get; } = [];

        /// <summary>Whether this slot reads the second scratch region.</summary>
        public bool Second { get; init; }
    }

    /// <summary>
    /// The repaired structure under construction: the harvested ground surface,
    /// the reserved working set, the construction options, the growing domain,
    /// and the obligation, seed-rule and constraint tables one admission pass
    /// reads out of the module. The class table is recomputed from scratch from
    /// this state at every commit, never patched.
    /// </summary>
    private sealed class RepairingModel
    {
        /// <summary>The harvested ground surface.</summary>
        public RepairingGround Ground { get; init; } = new();

        /// <summary>The decision's reserved working set.</summary>
        public RepairingBuffers Buffers { get; init; } = default!;

        /// <summary>The construction options.</summary>
        public RepairingConstructionOptions Options { get; init; }

        /// <summary>The current domain size — the post-quotient carriers plus the mints committed so far.</summary>
        public int DeltaSize { get; set; }

        /// <summary>The committed relation's edge count.</summary>
        public int EdgeCount { get; set; }

        /// <summary>The fresh elements minted for the candidate model under construction.</summary>
        public int MintCount { get; set; }

        /// <summary>Whether the frozen complement rows have been written, so a complement reads them rather than the empty set.</summary>
        public bool ComplementsFrozen { get; set; }

        /// <summary>The domain size the frozen complement rows were taken over; every position above it reads empty, which is exactly how a pass placed before the mints fails its both-directions re-check.</summary>
        public int ComplementDomain { get; set; }

        /// <summary>The first evaluation slot.</summary>
        public RepairingEvaluationSlot SlotA { get; } = new();

        /// <summary>The second evaluation slot.</summary>
        public RepairingEvaluationSlot SlotB { get; } = new() { Second = true };

        /// <summary>The obligations one admission pass reads out of the module's obligation positions.</summary>
        public List<RepairingObligation> Obligations { get; } = [];

        /// <summary>The class-table seeding rules the subclass and equivalence axioms carry.</summary>
        public List<RepairingSeedRule> SeedRules { get; } = [];

        /// <summary>The told domain constraints, one per admitted domain axiom.</summary>
        public List<RepairingRoleClass> Domains { get; } = [];

        /// <summary>The told range constraints, one per admitted range axiom — the declared range the demand set intersects.</summary>
        public List<RepairingRoleClass> Ranges { get; } = [];

        /// <summary>The told named memberships a class assertion seeds directly.</summary>
        public List<RepairingMembership> DirectMemberships { get; } = [];

        /// <summary>The told domain constraints whose confining side is a NAMED class — the only ones that seed a fixpoint variable.</summary>
        public List<RepairingNamedRoleClass> NamedDomains { get; } = [];

        /// <summary>The told range constraints whose confining side is a NAMED class — the only ones that seed a fixpoint variable.</summary>
        public List<RepairingNamedRoleClass> NamedRanges { get; } = [];

        /// <summary>The named classes an enumeration closes — equated with an enumeration, or subsumed into one along told named subclass steps. A mint is never typed into a demand set holding one.</summary>
        public HashSet<int> EnumerationClosed { get; } = [];

        /// <summary>The enumeration each closed named class draws its members from, in told document order — the candidate source a closed demand set enumerates.</summary>
        public Dictionary<int, OwlObjectOneOf> EnumerationMembers { get; } = [];

        /// <summary>The told disjointness pairs over named classes the proposal-side pruning filter reads.</summary>
        public List<RepairingClassPair> Disjointness { get; } = [];
    }

    /// <summary>Reads one committed edge row's first word index — role-major then source.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="role">The role index.</param>
    /// <param name="source">The source element index.</param>
    /// <returns>The row's first word index.</returns>
    private static int EdgeRow(RepairingModel model, int role, int source)
    {
        return ((role * model.Buffers.Delta) + source) * model.Buffers.Words;
    }

    /// <summary>
    /// The whole-module admission classifier: a positive whitelist answering the
    /// shape the verification pass dispatches on, and one of the three NAMED
    /// rejection buckets for every other kind the engine's axiom-kind roster
    /// carries. The four buckets are CLOSED against that roster — every record of
    /// the axiom hierarchy answers exactly one arm below — so no kind silences a
    /// module through the default arm without a named home, and a kind the roster
    /// gains later lands in a bucket in the same change.
    /// </summary>
    /// <param name="axiom">The axiom to classify.</param>
    /// <returns>The admitted shape, or the named rejection bucket.</returns>
    private static RepairingShape Classify(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlDeclarationAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom or OwlImportAxiom => RepairingShape.NonLogical,
            OwlClassAssertionAxiom assertion => assertion.Individual is NamedNode or BlankNode && IsAdmissible(assertion.Class)
                ? RepairingShape.ClassAssertion
                : RepairingShape.Unadmitted,
            OwlObjectPropertyAssertionAxiom assertion => assertion.Source is NamedNode or BlankNode && assertion.Target is NamedNode or BlankNode
                ? RepairingShape.ObjectPropertyAssertion
                : RepairingShape.Unadmitted,
            OwlSubClassOfAxiom subClass => IsAdmissible(subClass.SubClass) && IsAdmissible(subClass.SuperClass)
                ? RepairingShape.SubClassOf
                : RepairingShape.Unadmitted,
            OwlEquivalentClassesAxiom equivalent => IsAdmissible(equivalent.First) && IsAdmissible(equivalent.Second)
                ? RepairingShape.EquivalentClasses
                : RepairingShape.Unadmitted,
            OwlDisjointClassesAxiom disjoint => AreAdmissible(disjoint.Operands)
                ? RepairingShape.DisjointClasses
                : RepairingShape.Unadmitted,
            OwlObjectPropertyDomainAxiom { Property: OwlObjectPropertyReference } domain => IsAdmissible(domain.Domain)
                ? RepairingShape.ObjectPropertyDomain
                : RepairingShape.Unadmitted,
            OwlObjectPropertyRangeAxiom { Property: OwlObjectPropertyReference } range => IsAdmissible(range.Range)
                ? RepairingShape.ObjectPropertyRange
                : RepairingShape.Unadmitted,
            OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference, Second: OwlObjectPropertyReference } => RepairingShape.InverseObjectProperties,
            OwlSubObjectPropertyOfAxiom { SubProperty: OwlObjectPropertyReference, SuperProperty: OwlObjectPropertyReference } => RepairingShape.SubObjectPropertyOf,
            OwlSubObjectPropertyOfAxiom => RepairingShape.RejectedBacklog,
            OwlObjectPropertyCharacteristicAxiom characteristic => ClassifyCharacteristic(characteristic),
            OwlSameIndividualAxiom same => same.First is NamedNode or BlankNode && same.Second is NamedNode or BlankNode
                ? RepairingShape.SameIndividual
                : RepairingShape.Unadmitted,
            OwlDifferentIndividualsAxiom different => AreIndividuals(different.Individuals)
                ? RepairingShape.DifferentIndividuals
                : RepairingShape.Unadmitted,
            OwlDataPropertyAssertionAxiom assertion => assertion.Source is NamedNode or BlankNode
                ? RepairingShape.DataPropertyAssertion
                : RepairingShape.Unadmitted,
            OwlDataPropertyDomainAxiom domain => IsAdmissible(domain.Domain)
                ? RepairingShape.DataPropertyDomain
                : RepairingShape.Unadmitted,
            OwlDataPropertyRangeAxiom { Range: OwlDatatypeReference } => RepairingShape.DataPropertyRange,
            OwlDataPropertyRangeAxiom => RepairingShape.RejectedBacklog,
            OwlHasKeyAxiom or OwlPropertyChainAxiom => RepairingShape.RejectedForRepair,
            OwlNegativeObjectPropertyAssertionAxiom or OwlNegativeDataPropertyAssertionAxiom => RepairingShape.RejectedForCertify,
            OwlDisjointUnionAxiom or OwlEquivalentObjectPropertiesAxiom or OwlDisjointObjectPropertiesAxiom
                or OwlSubDataPropertyOfAxiom or OwlEquivalentDataPropertiesAxiom or OwlDisjointDataPropertiesAxiom
                or OwlFunctionalDataPropertyAxiom or OwlDatatypeDefinitionAxiom => RepairingShape.RejectedBacklog,
            _ => RepairingShape.Unadmitted,
        };
    }

    /// <summary>Classifies one told object-property characteristic: symmetry, transitivity and functionality are admitted and re-checked, an inverse-functional characteristic is rejected for a repairing face because an invented edge can manufacture the merge it forces, and the reflexive family is a named backlog.</summary>
    /// <param name="axiom">The characteristic axiom.</param>
    /// <returns>The admitted shape, or the named rejection bucket.</returns>
    private static RepairingShape ClassifyCharacteristic(OwlObjectPropertyCharacteristicAxiom axiom)
    {
        if(axiom.Property is not OwlObjectPropertyReference)
        {
            return RepairingShape.Unadmitted;
        }

        return axiom.Characteristic switch
        {
            OwlPropertyCharacteristic.Symmetric => RepairingShape.SymmetricObjectProperty,
            OwlPropertyCharacteristic.Transitive => RepairingShape.TransitiveObjectProperty,
            OwlPropertyCharacteristic.Functional => RepairingShape.FunctionalObjectProperty,
            OwlPropertyCharacteristic.InverseFunctional => RepairingShape.RejectedForRepair,
            _ => RepairingShape.RejectedBacklog,
        };
    }

    /// <summary>Whether one classified shape lies inside the certify face's admission — every arm above the three named rejection buckets and the default one.</summary>
    /// <param name="shape">The classified shape.</param>
    /// <returns><see langword="true"/> on an admitted shape.</returns>
    private static bool IsAdmittedShape(RepairingShape shape)
    {
        return shape >= RepairingShape.NonLogical;
    }

    /// <summary>
    /// Whether a class expression lies inside the evaluable grammar: named
    /// classes including the two semantics-fixed constants, enumerations of
    /// individual terms, intersections and unions over admitted operands,
    /// complements, existentials, universals, value pins and cardinalities over
    /// PLAIN roles. The walk is an explicit stack; local reflexivity, an inline
    /// inverse in any property position, and every data-side class expression is
    /// outside the grammar, each a named backlog rather than a silent drop.
    /// </summary>
    /// <param name="root">The class expression.</param>
    /// <returns><see langword="true"/> when every position is admitted.</returns>
    private static bool IsAdmissible(OwlClassExpression root)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlClassReference):
                {
                    break;
                }
                case(OwlObjectOneOf oneOf):
                {
                    if(!AreIndividuals(oneOf.Individuals))
                    {
                        return false;
                    }

                    break;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Push(complement.Operand);
                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    if(intersection.Operands.Count == 0)
                    {
                        return false;
                    }

                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        work.Push(intersection.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    if(union.Operands.Count == 0)
                    {
                        return false;
                    }

                    for(int index = 0; index < union.Operands.Count; index++)
                    {
                        work.Push(union.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference } existential):
                {
                    work.Push(existential.Filler);
                    break;
                }
                case(OwlObjectAllValuesFrom { Property: OwlObjectPropertyReference } universal):
                {
                    work.Push(universal.Filler);
                    break;
                }
                case(OwlObjectHasValue { Property: OwlObjectPropertyReference } hasValue):
                {
                    if(hasValue.Individual is not NamedNode and not BlankNode)
                    {
                        return false;
                    }

                    break;
                }
                case(OwlObjectCardinality { Property: OwlObjectPropertyReference } cardinality):
                {
                    if(cardinality.Filler is OwlClassExpression filler)
                    {
                        work.Push(filler);
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

    /// <summary>Whether every operand of a list lies inside the evaluable grammar.</summary>
    /// <param name="operands">The operands to admit.</param>
    /// <returns><see langword="true"/> when every operand is admitted.</returns>
    private static bool AreAdmissible(IReadOnlyList<OwlClassExpression> operands)
    {
        for(int index = 0; index < operands.Count; index++)
        {
            if(!IsAdmissible(operands[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every term of a list denotes an individual — a named or an anonymous one; a literal denotes a data value the constructed domain does not hold.</summary>
    /// <param name="terms">The terms to admit.</param>
    /// <returns><see langword="true"/> when every term is an individual.</returns>
    private static bool AreIndividuals(IReadOnlyList<RdfTerm> terms)
    {
        for(int index = 0; index < terms.Count; index++)
        {
            if(terms[index] is not NamedNode and not BlankNode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates one admitted class expression against the constructed structure,
    /// writing every node's extension into the slot's own scratch region. The
    /// expression is first linearized breadth-first, so every child follows its
    /// parent, and the list is then folded from the end, so each node reads its
    /// already-evaluated children — a post-order evaluation with no recursion
    /// anywhere. <c>owl:Thing</c> reads the whole CURRENT domain, so it is fresh
    /// after every mint, and <c>owl:Nothing</c> the empty set.
    /// </summary>
    /// <param name="root">The expression to evaluate.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="table">The class table the named classes read.</param>
    /// <param name="slot">The evaluation slot, carrying the node list and naming the scratch region.</param>
    /// <param name="restrictedRole">The role the universal-as-generator rule is restricted at, or <c>-1</c> for the unrestricted rule.</param>
    /// <returns><see langword="true"/> when the expression evaluated inside the node budget and the grammar.</returns>
    private static bool TryEvaluate(OwlClassExpression root, RepairingModel model, ReadOnlySpan<ulong> table, RepairingEvaluationSlot slot, int restrictedRole)
    {
        slot.Nodes.Clear();
        slot.FirstChild.Clear();
        slot.ChildCount.Clear();
        slot.Nodes.Add(root);
        slot.FirstChild.Add(0);
        slot.ChildCount.Add(0);
        int scan = 0;
        while(scan < slot.Nodes.Count)
        {
            int start = slot.Nodes.Count;
            switch(slot.Nodes[scan])
            {
                case(OwlObjectComplementOf complement):
                {
                    AppendChild(slot, complement.Operand);
                    break;
                }
                case(OwlObjectSomeValuesFrom existential):
                {
                    AppendChild(slot, existential.Filler);
                    break;
                }
                case(OwlObjectAllValuesFrom universal):
                {
                    AppendChild(slot, universal.Filler);
                    break;
                }
                case(OwlObjectCardinality cardinality):
                {
                    if(cardinality.Filler is OwlClassExpression filler)
                    {
                        AppendChild(slot, filler);
                    }

                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        AppendChild(slot, intersection.Operands[index]);
                    }

                    break;
                }
                case(OwlObjectUnionOf union):
                {
                    for(int index = 0; index < union.Operands.Count; index++)
                    {
                        AppendChild(slot, union.Operands[index]);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }

            slot.FirstChild[scan] = start;
            slot.ChildCount[scan] = slot.Nodes.Count - start;
            scan++;
        }

        if(slot.Nodes.Count > model.Buffers.NodeCapacity)
        {
            return false;
        }

        for(int index = slot.Nodes.Count - 1; index >= 0; index--)
        {
            if(!TryEvaluateNode(index, model, table, slot, restrictedRole))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Appends one child node to the linearization, reserving its own child range for the scan to fill.</summary>
    /// <param name="slot">The evaluation slot.</param>
    /// <param name="child">The child expression.</param>
    private static void AppendChild(RepairingEvaluationSlot slot, OwlClassExpression child)
    {
        slot.Nodes.Add(child);
        slot.FirstChild.Add(0);
        slot.ChildCount.Add(0);
    }

    /// <summary>
    /// Evaluates one linearized node against its already-evaluated children.
    /// A complement reads the frozen post-pass rows once they exist and the
    /// empty set before that, so it feeds nothing into the positive fixpoint;
    /// every other form is evaluated exactly, and the class table only ever
    /// grows, so the fixpoint loop terminates whatever the anti-monotone forms
    /// contribute. The default arm answers false: a shape the admission accepted
    /// and this evaluation does not know must never read as an extension.
    /// </summary>
    /// <param name="index">The node index inside the slot.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="table">The class table the named classes read.</param>
    /// <param name="slot">The evaluation slot.</param>
    /// <param name="restrictedRole">The role the universal-as-generator rule is restricted at, or <c>-1</c> for the unrestricted rule.</param>
    /// <returns><see langword="true"/> when the node evaluated.</returns>
    private static bool TryEvaluateNode(int index, RepairingModel model, ReadOnlySpan<ulong> table, RepairingEvaluationSlot slot, int restrictedRole)
    {
        int words = model.Buffers.Words;
        int delta = model.DeltaSize;
        Span<ulong> scratch = slot.Second ? model.Buffers.ScratchB : model.Buffers.ScratchA;
        int row = index * words;
        int firstChild = slot.FirstChild[index] * words;
        int childCount = slot.ChildCount[index];
        switch(slot.Nodes[index])
        {
            case(OwlClassReference reference):
            {
                ReadClassExtension(reference, model, table, scratch, row);

                return true;
            }
            case(OwlObjectOneOf oneOf):
            {
                ClearRow(scratch, row, words);
                for(int member = 0; member < oneOf.Individuals.Count; member++)
                {
                    if(!TryElementIndex(model.Ground, oneOf.Individuals[member], out int element))
                    {
                        return false;
                    }

                    SetBit(scratch, row, element);
                }

                return true;
            }
            case(OwlObjectComplementOf complement):
            {
                ReadComplementExtension(complement, model, scratch, row);

                return true;
            }
            case(OwlObjectIntersectionOf):
            {
                FillRow(scratch, row, words, delta);
                for(int child = 0; child < childCount; child++)
                {
                    for(int word = 0; word < words; word++)
                    {
                        scratch[row + word] &= scratch[firstChild + (child * words) + word];
                    }
                }

                return true;
            }
            case(OwlObjectUnionOf):
            {
                ClearRow(scratch, row, words);
                for(int child = 0; child < childCount; child++)
                {
                    for(int word = 0; word < words; word++)
                    {
                        scratch[row + word] |= scratch[firstChild + (child * words) + word];
                    }
                }

                return true;
            }
            case(OwlObjectSomeValuesFrom existential):
            {
                return TryReadExistentialExtension(existential.Property.Property.Iri, model, scratch, row, firstChild);
            }
            case(OwlObjectAllValuesFrom universal):
            {
                return TryReadUniversalExtension(universal.Property.Property.Iri, model, scratch, row, firstChild, restrictedRole);
            }
            case(OwlObjectHasValue hasValue):
            {
                return TryReadValuePinExtension(hasValue, model, scratch, row);
            }
            case(OwlObjectCardinality cardinality):
            {
                return TryReadCardinalityExtension(cardinality, model, scratch, row, firstChild, childCount);
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Reads one class reference's extension into a scratch row: the whole CURRENT domain for <c>owl:Thing</c> — re-read after every mint rather than snapshotted — the empty set for <c>owl:Nothing</c> and for a class the harvest never saw, and the fixpoint variable otherwise.</summary>
    /// <param name="reference">The class reference.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="table">The class table.</param>
    /// <param name="scratchToAppendTo">The scratch region.</param>
    /// <param name="row">The scratch row's first word index.</param>
    private static void ReadClassExtension(OwlClassReference reference, RepairingModel model, ReadOnlySpan<ulong> table, Span<ulong> scratchToAppendTo, int row)
    {
        int words = model.Buffers.Words;
        if(reference.Class.Iri.Equals(OwlVocabulary.Thing))
        {
            FillRow(scratchToAppendTo, row, words, model.DeltaSize);

            return;
        }

        if(reference.Class.Iri.Equals(OwlVocabulary.Nothing) || !model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int index))
        {
            ClearRow(scratchToAppendTo, row, words);

            return;
        }

        for(int word = 0; word < words; word++)
        {
            scratchToAppendTo[row + word] = table[(index * words) + word];
        }
    }

    /// <summary>Reads one complement's extension into a scratch row: the frozen post-pass row once the pass has run, the empty set before it. A row frozen over a smaller domain leaves every later position clear, which is exactly how a pass placed before the mints fails its defining equivalence's both-directions re-check.</summary>
    /// <param name="complement">The complement expression.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="scratchToAppendTo">The scratch region.</param>
    /// <param name="row">The scratch row's first word index.</param>
    private static void ReadComplementExtension(OwlObjectComplementOf complement, RepairingModel model, Span<ulong> scratchToAppendTo, int row)
    {
        int words = model.Buffers.Words;
        ClearRow(scratchToAppendTo, row, words);
        if(!model.ComplementsFrozen)
        {
            return;
        }

        int index = FindComplement(model.Ground, complement);
        if(index < 0)
        {
            return;
        }

        ReadOnlySpan<ulong> frozen = model.Buffers.Complements;
        for(int word = 0; word < words; word++)
        {
            scratchToAppendTo[row + word] = frozen[(index * words) + word];
        }
    }

    /// <summary>Reads one complement occurrence's frozen row index, comparing by reference over the first-seen list.</summary>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="complement">The complement expression.</param>
    /// <returns>The row index, or <c>-1</c> when the harvest never saw the occurrence.</returns>
    private static int FindComplement(RepairingGround ground, OwlObjectComplementOf complement)
    {
        for(int index = 0; index < ground.Complements.Count; index++)
        {
            if(ReferenceEquals(ground.Complements[index], complement))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Reads one existential's extension into a scratch row: the sources holding a committed edge of the role into the filler's extension.</summary>
    /// <param name="role">The existential's role IRI.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="scratchToAppendTo">The scratch region.</param>
    /// <param name="row">The scratch row's first word index.</param>
    /// <param name="fillerRow">The filler row's first word index.</param>
    /// <returns><see langword="true"/> when the role is interned.</returns>
    private static bool TryReadExistentialExtension(Utf8String role, RepairingModel model, Span<ulong> scratchToAppendTo, int row, int fillerRow)
    {
        int words = model.Buffers.Words;
        ClearRow(scratchToAppendTo, row, words);
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return false;
        }

        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            int edgeRow = EdgeRow(model, index, source);
            for(int word = 0; word < words; word++)
            {
                if((edges[edgeRow + word] & scratchToAppendTo[fillerRow + word]) != 0)
                {
                    SetBit(scratchToAppendTo, row, source);
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>Reads one universal's extension into a scratch row: the sources every committed successor of which lies in the filler's extension. Under the restricted rule — the vacuity guard's per-demand side computation at the role under repair, or the control generator mode — a source additionally needs at least one successor, so a universal holding solely because the role is empty there does not fire.</summary>
    /// <param name="role">The universal's role IRI.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="scratchToAppendTo">The scratch region.</param>
    /// <param name="row">The scratch row's first word index.</param>
    /// <param name="fillerRow">The filler row's first word index.</param>
    /// <param name="restrictedRole">The role the rule is restricted at, or <c>-1</c> for the unrestricted rule.</param>
    /// <returns><see langword="true"/> when the role is interned.</returns>
    private static bool TryReadUniversalExtension(Utf8String role, RepairingModel model, Span<ulong> scratchToAppendTo, int row, int fillerRow, int restrictedRole)
    {
        int words = model.Buffers.Words;
        ClearRow(scratchToAppendTo, row, words);
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return false;
        }

        bool needsSuccessor = index == restrictedRole || restrictedRole == RepairEveryRole || model.Options.GeneratorMode == RepairGeneratorMode.UniversalRequiresSuccessor;
        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            int edgeRow = EdgeRow(model, index, source);
            bool holds = true;
            bool hasSuccessor = false;
            for(int word = 0; word < words; word++)
            {
                ulong successors = edges[edgeRow + word];
                hasSuccessor = hasSuccessor || successors != 0;
                if((successors & ~scratchToAppendTo[fillerRow + word]) != 0)
                {
                    holds = false;
                    break;
                }
            }

            if(holds && (hasSuccessor || !needsSuccessor))
            {
                SetBit(scratchToAppendTo, row, source);
            }
        }

        return true;
    }

    /// <summary>Reads one value pin's extension into a scratch row: the sources holding a committed edge of the role to the pinned element.</summary>
    /// <param name="hasValue">The value-pin expression.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="scratchToAppendTo">The scratch region.</param>
    /// <param name="row">The scratch row's first word index.</param>
    /// <returns><see langword="true"/> when the role is interned and the pinned term is a carrier.</returns>
    private static bool TryReadValuePinExtension(OwlObjectHasValue hasValue, RepairingModel model, Span<ulong> scratchToAppendTo, int row)
    {
        ClearRow(scratchToAppendTo, row, model.Buffers.Words);
        if(!model.Ground.RoleIndices.TryGetValue(hasValue.Property.Property.Iri, out int index) || !TryElementIndex(model.Ground, hasValue.Individual, out int value))
        {
            return false;
        }

        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            if(TestBit(edges, EdgeRow(model, index, source), value))
            {
                SetBit(scratchToAppendTo, row, source);
            }
        }

        return true;
    }

    /// <summary>Reads one cardinality restriction's extension into a scratch row by counting each source's committed successors inside the qualifying filler — the whole domain where the restriction is unqualified — and comparing that count against the told bound. The count is over DISTINCT CARRIERS, which under the injective carrier map are distinct domain elements, so it is exact for the constructed model.</summary>
    /// <param name="cardinality">The cardinality restriction.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="scratchToAppendTo">The scratch region.</param>
    /// <param name="row">The scratch row's first word index.</param>
    /// <param name="fillerRow">The filler row's first word index; read only where the restriction is qualified.</param>
    /// <param name="childCount">The node's child count — one for a qualified restriction, zero otherwise.</param>
    /// <returns><see langword="true"/> when the role is interned.</returns>
    private static bool TryReadCardinalityExtension(OwlObjectCardinality cardinality, RepairingModel model, Span<ulong> scratchToAppendTo, int row, int fillerRow, int childCount)
    {
        int words = model.Buffers.Words;
        ClearRow(scratchToAppendTo, row, words);
        if(!model.Ground.RoleIndices.TryGetValue(cardinality.Property.Property.Iri, out int index))
        {
            return false;
        }

        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            int edgeRow = EdgeRow(model, index, source);
            int count = 0;
            for(int word = 0; word < words; word++)
            {
                ulong successors = edges[edgeRow + word];
                if(childCount > 0)
                {
                    successors &= scratchToAppendTo[fillerRow + word];
                }

                count += BitOperations.PopCount(successors);
            }

            bool holds = cardinality.Kind switch
            {
                OwlCardinalityKind.Min => count >= cardinality.Cardinality,
                OwlCardinalityKind.Max => count <= cardinality.Cardinality,
                _ => count == cardinality.Cardinality,
            };

            if(holds)
            {
                SetBit(scratchToAppendTo, row, source);
            }
        }

        return true;
    }

    /// <summary>Collects the construction tables one admission pass reads out of the module: the class-table seeding rules, the told named memberships, the told domain and range constraints, the obligations every obligation position carries, and the enumeration-closed named classes a mint may never be typed into.</summary>
    /// <param name="module">The admitted module.</param>
    /// <param name="shapes">The classifier's shape per axiom.</param>
    /// <param name="model">The structure under construction.</param>
    private static void CollectConstruction(ReasoningModule module, RepairingShape[] shapes, RepairingModel model)
    {
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            OwlAxiom axiom = module.Axioms[index];
            switch(shapes[index])
            {
                case(RepairingShape.ClassAssertion):
                {
                    CollectAssertedConstruction((OwlClassAssertionAxiom)axiom, model);
                    break;
                }
                case(RepairingShape.SubClassOf):
                {
                    OwlSubClassOfAxiom subClass = (OwlSubClassOfAxiom)axiom;
                    if(subClass.SuperClass is OwlClassReference super && model.Ground.ClassIndices.TryGetValue(super.Class.Iri, out int superClass))
                    {
                        model.SeedRules.Add(new RepairingSeedRule(subClass.SubClass, superClass));
                    }

                    CollectNamedInclusions(subClass.SubClass, subClass.SuperClass, model);
                    CollectObligations(subClass.SubClass, subClass.SuperClass, model);
                    break;
                }
                case(RepairingShape.EquivalentClasses):
                {
                    OwlEquivalentClassesAxiom equivalent = (OwlEquivalentClassesAxiom)axiom;
                    if(equivalent.First is OwlClassReference first && model.Ground.ClassIndices.TryGetValue(first.Class.Iri, out int firstClass))
                    {
                        model.SeedRules.Add(new RepairingSeedRule(equivalent.Second, firstClass));
                    }

                    if(equivalent.Second is OwlClassReference second && model.Ground.ClassIndices.TryGetValue(second.Class.Iri, out int secondClass))
                    {
                        model.SeedRules.Add(new RepairingSeedRule(equivalent.First, secondClass));
                    }

                    CollectNamedInclusions(equivalent.First, equivalent.Second, model);
                    CollectNamedInclusions(equivalent.Second, equivalent.First, model);
                    CollectObligations(equivalent.First, equivalent.Second, model);
                    CollectObligations(equivalent.Second, equivalent.First, model);
                    break;
                }
                case(RepairingShape.ObjectPropertyDomain):
                {
                    OwlObjectPropertyDomainAxiom domain = (OwlObjectPropertyDomainAxiom)axiom;
                    CollectRoleConstraint(domain.Property.Property.Iri, domain.Domain, model, model.Domains, model.NamedDomains);
                    break;
                }
                case(RepairingShape.ObjectPropertyRange):
                {
                    OwlObjectPropertyRangeAxiom range = (OwlObjectPropertyRangeAxiom)axiom;
                    CollectRoleConstraint(range.Property.Property.Iri, range.Range, model, model.Ranges, model.NamedRanges);
                    break;
                }
                case(RepairingShape.DisjointClasses):
                {
                    CollectDisjointness((OwlDisjointClassesAxiom)axiom, model.Ground, model.Disjointness);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        CollectEnumerationClosed(module, shapes, model);
    }

    /// <summary>Collects one told class assertion's construction contribution: a named class or a named intersection conjunct seeds the class table directly, and every restriction conjunct becomes an obligation whose activator is the asserted term's own singleton enumeration.</summary>
    /// <param name="axiom">The told class assertion.</param>
    /// <param name="model">The structure under construction.</param>
    private static void CollectAssertedConstruction(OwlClassAssertionAxiom axiom, RepairingModel model)
    {
        if(!TryElementIndex(model.Ground, axiom.Individual, out int element))
        {
            return;
        }

        if(axiom.Class is OwlClassReference named && model.Ground.ClassIndices.TryGetValue(named.Class.Iri, out int namedClass))
        {
            model.DirectMemberships.Add(new RepairingMembership(namedClass, element));

            return;
        }

        if(axiom.Class is OwlObjectIntersectionOf intersection)
        {
            for(int index = 0; index < intersection.Operands.Count; index++)
            {
                if(intersection.Operands[index] is OwlClassReference conjunct && model.Ground.ClassIndices.TryGetValue(conjunct.Class.Iri, out int conjunctClass))
                {
                    model.DirectMemberships.Add(new RepairingMembership(conjunctClass, element));
                }
            }
        }

        CollectObligations(new OwlObjectOneOf([axiom.Individual]), axiom.Class, model);
    }

    /// <summary>Collects one told domain or range constraint: the whole expression for the demand-set formation and the verification pass, and the named-class case additionally for the class-table seeding.</summary>
    /// <param name="role">The constrained role's IRI.</param>
    /// <param name="constraint">The confining class expression.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="constraintsToAppendTo">The expression-carrying constraint accumulator.</param>
    /// <param name="namedToAppendTo">The named-class constraint accumulator.</param>
    private static void CollectRoleConstraint(Utf8String role, OwlClassExpression constraint, RepairingModel model, List<RepairingRoleClass> constraintsToAppendTo, List<RepairingNamedRoleClass> namedToAppendTo)
    {
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return;
        }

        constraintsToAppendTo.Add(new RepairingRoleClass(index, constraint));
        if(constraint is OwlClassReference reference && model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int named))
        {
            namedToAppendTo.Add(new RepairingNamedRoleClass(index, named));
        }
    }

    /// <summary>
    /// Collects the class-table seeding rules one obligation-position expression
    /// carries beyond its own restrictions: the NAMED conjuncts of a top-level
    /// intersection, each of which the axiom demands every member of the
    /// activating side lies in. The rule is the membership counterpart of the
    /// obligations the same position carries — it derives no edge and invents
    /// nothing, and the axiom that justifies it is re-checked at verification.
    /// Without it a told membership in a class an intersection DEFINES never
    /// reaches that intersection's named parts, so the defining axiom's own
    /// re-check fails on a module the repair could otherwise finish.
    /// </summary>
    /// <param name="activator">The class expression whose members the inclusion flows from.</param>
    /// <param name="position">The expression standing in obligation position.</param>
    /// <param name="model">The structure under construction.</param>
    private static void CollectNamedInclusions(OwlClassExpression activator, OwlClassExpression position, RepairingModel model)
    {
        if(position is not OwlObjectIntersectionOf intersection)
        {
            return;
        }

        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            if(intersection.Operands[index] is OwlClassReference named && model.Ground.ClassIndices.TryGetValue(named.Class.Iri, out int target))
            {
                model.SeedRules.Add(new RepairingSeedRule(activator, target));
            }
        }
    }

    /// <summary>Collects the obligations one obligation-position expression carries: the expression itself where it is a restriction, and each top-level conjunct where it is an intersection. Every collected obligation names the class expression whose members carry it, so the repair reads a frozen membership rather than a syntactic position.</summary>
    /// <param name="activator">The class expression whose members carry the obligation.</param>
    /// <param name="position">The expression standing in obligation position.</param>
    /// <param name="model">The structure under construction.</param>
    private static void CollectObligations(OwlClassExpression activator, OwlClassExpression position, RepairingModel model)
    {
        if(position is not OwlObjectIntersectionOf intersection)
        {
            CollectObligation(activator, position, model);

            return;
        }

        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            CollectObligation(activator, intersection.Operands[index], model);
        }
    }

    /// <summary>Collects one obligation-position restriction: a value pin, a universal, an existential, or a cardinality over a plain role. Every other form carries no repairable obligation and is left to the verification pass.</summary>
    /// <param name="activator">The class expression whose members carry the obligation.</param>
    /// <param name="restriction">The candidate restriction.</param>
    /// <param name="model">The structure under construction.</param>
    private static void CollectObligation(OwlClassExpression activator, OwlClassExpression restriction, RepairingModel model)
    {
        switch(restriction)
        {
            case(OwlObjectHasValue { Property: OwlObjectPropertyReference pinRole } hasValue):
            {
                if(model.Ground.RoleIndices.TryGetValue(pinRole.Named.Iri, out int role) && TryElementIndex(model.Ground, hasValue.Individual, out int value))
                {
                    model.Obligations.Add(new RepairingObligation(activator, role, RepairingObligationKind.ValuePin, 1, null, value));
                }

                break;
            }
            case(OwlObjectAllValuesFrom { Property: OwlObjectPropertyReference universalRole } universal):
            {
                if(model.Ground.RoleIndices.TryGetValue(universalRole.Named.Iri, out int role))
                {
                    model.Obligations.Add(new RepairingObligation(activator, role, RepairingObligationKind.Universal, 0, universal.Filler, -1));
                }

                break;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole } existential):
            {
                if(model.Ground.RoleIndices.TryGetValue(existentialRole.Named.Iri, out int role))
                {
                    model.Obligations.Add(new RepairingObligation(activator, role, RepairingObligationKind.Existential, 1, existential.Filler, -1));
                }

                break;
            }
            case(OwlObjectCardinality { Property: OwlObjectPropertyReference cardinalityRole } cardinality):
            {
                if(model.Ground.RoleIndices.TryGetValue(cardinalityRole.Named.Iri, out int role))
                {
                    RepairingObligationKind kind = cardinality.Kind switch
                    {
                        OwlCardinalityKind.Min => RepairingObligationKind.MinCardinality,
                        OwlCardinalityKind.Max => RepairingObligationKind.MaxCardinality,
                        _ => RepairingObligationKind.ExactCardinality,
                    };

                    model.Obligations.Add(new RepairingObligation(activator, role, kind, cardinality.Cardinality, cardinality.Filler, -1));
                }

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Computes the named classes an enumeration CLOSES: a class the module equates with an enumeration, and every class the module subsumes INTO such a class along told named subclass steps — the direction that closes the subsumed class. The converse direction never closes anything. A bounded worklist runs the propagation, so no helper recurses.</summary>
    /// <param name="module">The admitted module.</param>
    /// <param name="shapes">The classifier's shape per axiom.</param>
    /// <param name="model">The structure under construction.</param>
    private static void CollectEnumerationClosed(ReasoningModule module, RepairingShape[] shapes, RepairingModel model)
    {
        List<RepairingInclusion> steps = [];
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            if(shapes[index] == RepairingShape.EquivalentClasses)
            {
                OwlEquivalentClassesAxiom equivalent = (OwlEquivalentClassesAxiom)module.Axioms[index];
                MarkEnumerationClosed(equivalent.First, equivalent.Second, model);
                MarkEnumerationClosed(equivalent.Second, equivalent.First, model);
            }

            if(shapes[index] == RepairingShape.SubClassOf)
            {
                OwlSubClassOfAxiom subClass = (OwlSubClassOfAxiom)module.Axioms[index];
                if(subClass.SubClass is OwlClassReference sub
                    && subClass.SuperClass is OwlClassReference super
                    && model.Ground.ClassIndices.TryGetValue(sub.Class.Iri, out int from)
                    && model.Ground.ClassIndices.TryGetValue(super.Class.Iri, out int to))
                {
                    steps.Add(new RepairingInclusion(from, to));
                }
            }
        }

        bool derived = true;
        while(derived)
        {
            derived = false;
            for(int index = 0; index < steps.Count; index++)
            {
                RepairingInclusion step = steps[index];
                if(model.EnumerationClosed.Contains(step.To))
                {
                    derived = model.EnumerationClosed.Add(step.From) || derived;
                    if(model.EnumerationMembers.TryGetValue(step.To, out OwlObjectOneOf? members) && !model.EnumerationMembers.ContainsKey(step.From))
                    {
                        model.EnumerationMembers[step.From] = members;
                    }
                }
            }
        }
    }

    /// <summary>Marks one named side of a told equivalence as enumeration-closed where the other side is an enumeration, and records that enumeration as the closed class's candidate source.</summary>
    /// <param name="namedSide">The candidate named side.</param>
    /// <param name="enumerationSide">The candidate enumeration side.</param>
    /// <param name="model">The structure under construction.</param>
    private static void MarkEnumerationClosed(OwlClassExpression namedSide, OwlClassExpression enumerationSide, RepairingModel model)
    {
        if(enumerationSide is OwlObjectOneOf oneOf && namedSide is OwlClassReference named && model.Ground.ClassIndices.TryGetValue(named.Class.Iri, out int index))
        {
            model.EnumerationClosed.Add(index);
            model.EnumerationMembers[index] = oneOf;
        }
    }

    /// <summary>Seeds the told edges into the committed relation over the QUOTIENTED elements and closes them with the closure operator.</summary>
    /// <param name="model">The structure under construction.</param>
    private static void SeedToldEdges(RepairingModel model)
    {
        Queue<RepairingEdge> work = new();
        for(int index = 0; index < model.Ground.ToldEdges.Count; index++)
        {
            RepairingEdge edge = model.Ground.ToldEdges[index];
            AddEdge(model, edge.Role, model.Ground.CarrierElements[edge.Source], model.Ground.CarrierElements[edge.Target], work);
        }

        ApplyClosure(model, work);
    }

    /// <summary>Adds one edge to the committed relation, enqueueing it for closure only where it is new.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="role">The role the edge lands in.</param>
    /// <param name="source">The edge's source element.</param>
    /// <param name="target">The edge's target element.</param>
    /// <param name="workToAppendTo">The closure worklist.</param>
    /// <returns><see langword="true"/> when the edge was not already committed.</returns>
    private static bool AddEdge(RepairingModel model, int role, int source, int target, Queue<RepairingEdge> workToAppendTo)
    {
        if(!SetBit(model.Buffers.Edges, EdgeRow(model, role, source), target))
        {
            return false;
        }

        model.EdgeCount++;
        workToAppendTo.Enqueue(new RepairingEdge(role, source, target));

        return true;
    }

    /// <summary>
    /// Applies the told closure OPERATOR to the committed relation: told inverse
    /// mirroring, told symmetry, told transitivity and told sub-property
    /// inclusion, each a production transcribing one told axiom's satisfaction
    /// condition. The operator is re-applied at every commit under the
    /// production discipline, so no downstream step observes an unclosed edge
    /// relation and an invented edge on an inverse-paired role is mirrored
    /// before its both-directions re-check reads it. Under the prologue variant
    /// the operator runs on the told seeding alone and every later commit is
    /// left unclosed.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="work">The closure worklist, seeded with the edges just committed.</param>
    private static void ApplyClosure(RepairingModel model, Queue<RepairingEdge> work)
    {
        RepairingGround ground = model.Ground;
        while(work.Count > 0)
        {
            RepairingEdge edge = work.Dequeue();
            for(int index = 0; index < ground.InversePairs.Count; index++)
            {
                RepairingRolePair pair = ground.InversePairs[index];
                if(pair.First == edge.Role)
                {
                    AddEdge(model, pair.Second, edge.Target, edge.Source, work);
                }

                if(pair.Second == edge.Role)
                {
                    AddEdge(model, pair.First, edge.Target, edge.Source, work);
                }
            }

            for(int index = 0; index < ground.SubPropertyPairs.Count; index++)
            {
                RepairingRolePair pair = ground.SubPropertyPairs[index];
                if(pair.First == edge.Role)
                {
                    AddEdge(model, pair.Second, edge.Source, edge.Target, work);
                }
            }

            if(ground.SymmetricRoles.Contains(edge.Role))
            {
                AddEdge(model, edge.Role, edge.Target, edge.Source, work);
            }

            if(!ground.TransitiveRoles.Contains(edge.Role))
            {
                continue;
            }

            for(int other = 0; other < model.DeltaSize; other++)
            {
                if(TestBit(model.Buffers.Edges, EdgeRow(model, edge.Role, edge.Target), other))
                {
                    AddEdge(model, edge.Role, edge.Source, other, work);
                }

                if(TestBit(model.Buffers.Edges, EdgeRow(model, edge.Role, other), edge.Source))
                {
                    AddEdge(model, edge.Role, other, edge.Target, work);
                }
            }
        }
    }

    /// <summary>
    /// Recomputes one class table FROM SCRATCH over the FROZEN committed edge
    /// relation, never patching an earlier value: a universal is anti-monotone
    /// in the edge relation, so a patched table carries a value no axiom
    /// justifies. The table is seeded with the told named memberships and the
    /// told domain and range constraints, then every seeding rule is evaluated
    /// against the table under construction and unioned into its named target
    /// until a whole sweep adds nothing. The table only ever grows and the
    /// lattice is finite, so the loop terminates.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="table">The table to recompute — the phase-1 table, or the vacuity guard's per-demand side table.</param>
    /// <param name="restrictedRole">The role the universal-as-generator rule is restricted at, or <c>-1</c> for the phase-1 rule set.</param>
    /// <returns><see langword="true"/> when every rule evaluated inside the node budget and the grammar.</returns>
    private static bool TryRecomputeClassTable(RepairingModel model, Span<ulong> table, int restrictedRole)
    {
        int words = model.Buffers.Words;
        table.Clear();
        for(int index = 0; index < model.DirectMemberships.Count; index++)
        {
            RepairingMembership membership = model.DirectMemberships[index];
            SetBit(table, membership.Class * words, membership.Element);
        }

        SeedRoleConstraints(model, table);
        bool derived = true;
        while(derived)
        {
            derived = false;
            for(int index = 0; index < model.SeedRules.Count; index++)
            {
                RepairingSeedRule rule = model.SeedRules[index];
                if(!TryEvaluate(rule.Source, model, table, model.SlotA, restrictedRole))
                {
                    return false;
                }

                Span<ulong> scratch = model.Buffers.ScratchA;
                int target = rule.Target * words;
                for(int word = 0; word < words; word++)
                {
                    ulong merged = table[target + word] | scratch[word];
                    if(merged != table[target + word])
                    {
                        table[target + word] = merged;
                        derived = true;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Seeds the memberships the told domain and range constraints force over the COMMITTED edges — the repaired and minted edges included, since a constraint holds of a role's whole extension.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="tableToAppendTo">The class table under recomputation.</param>
    private static void SeedRoleConstraints(RepairingModel model, Span<ulong> tableToAppendTo)
    {
        int words = model.Buffers.Words;
        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int index = 0; index < model.NamedDomains.Count; index++)
        {
            RepairingNamedRoleClass domain = model.NamedDomains[index];
            for(int source = 0; source < model.DeltaSize; source++)
            {
                if(!IsRowEmpty(edges, EdgeRow(model, domain.Role, source), words))
                {
                    SetBit(tableToAppendTo, domain.Class * words, source);
                }
            }
        }

        for(int index = 0; index < model.NamedRanges.Count; index++)
        {
            RepairingNamedRoleClass range = model.NamedRanges[index];
            int target = range.Class * words;
            for(int source = 0; source < model.DeltaSize; source++)
            {
                int row = EdgeRow(model, range.Role, source);
                for(int word = 0; word < words; word++)
                {
                    tableToAppendTo[target + word] |= edges[row + word];
                }
            }
        }
    }

    /// <summary>
    /// The whole-module repairing certificate pass: every axiom must classify
    /// into the admitted shape set, every disjunctive obligation position must be
    /// absent, the described model is then repaired through the phase order, and
    /// EVERY admitted axiom is finally re-checked against the finished structure.
    /// Whole-module admission is mandatory rather than monotone, because
    /// satisfying a subset says nothing about the module. The construction only
    /// proposes: every limit — an unadmitted axiom, an unrepairable obligation,
    /// an exhausted walk, a tripped bound, a failed check, a component-spanning
    /// failure — routes to SILENCE, and the face declares no refutation on any
    /// path.
    /// </summary>
    /// <param name="module">The module to certify.</param>
    /// <param name="ground">The harvested ground surface.</param>
    /// <param name="buffers">The decision's reserved working set.</param>
    /// <param name="options">The construction options.</param>
    /// <param name="window">The window measurement the outcome carries.</param>
    /// <returns>The certificate, or silence — each with its measurement.</returns>
    private static RepairingOutcome Certify(ReasoningModule module, RepairingGround ground, RepairingBuffers buffers, RepairingConstructionOptions options, RepairingWindow window)
    {
        RepairingShape[] shapes = new RepairingShape[module.Axioms.Count];
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            shapes[index] = Classify(module.Axioms[index]);
            if(!IsAdmittedShape(shapes[index]))
            {
                return RepairingOutcome.SilentWith(window);
            }
        }

        RepairingModel model = new()
        {
            Ground = ground,
            Buffers = buffers,
            Options = options,
            DeltaSize = ground.ElementCount,
        };

        CollectConstruction(module, shapes, model);
        if(SilencesOnObligationPosition(module, shapes))
        {
            return RepairingOutcome.SilentWith(window);
        }

        SeedToldEdges(model);
        List<RepairingDemand> residue = [];
        List<List<OwlClassExpression>> demandSets = [];
        if(!TryRunRepairStages(model, residue, demandSets))
        {
            return RepairingOutcome.SilentWith(window with { CarrierCount = model.DeltaSize, CommittedEdges = model.EdgeCount, MintedElements = model.MintCount });
        }

        return Walk(module, shapes, model, residue, demandSets, window);
    }

    /// <summary>
    /// Runs the deterministic repair and the bounded witness supply to a
    /// cascade fixpoint: each round recomputes the class table from scratch over
    /// the frozen edge relation, inserts the whole forced-value set
    /// simultaneously and re-applies the closure operator, then extracts the
    /// open demands and mints a fresh witness into every OPEN demand set. A
    /// CLOSED demand set is left for the bounded choice walk. The loop ends when
    /// a round mints nothing.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="residueToAppendTo">The closed demands the walk enumerates.</param>
    /// <param name="demandSetsToAppendTo">The demand set of each closed demand, parallel to the residue.</param>
    /// <returns><see langword="false"/> when a bound tripped, an evaluation failed, or a mint was refused — every one of them a silence.</returns>
    private static bool TryRunRepairStages(RepairingModel model, List<RepairingDemand> residueToAppendTo, List<List<OwlClassExpression>> demandSetsToAppendTo)
    {
        HashSet<RepairingDemand> minted = [];
        int cascade = 0;
        while(true)
        {
            if(!TryRunDeterministicRepair(model))
            {
                return false;
            }

            if(model.Options.ComplementPlacement == RepairComplementPlacement.BeforeMints && !model.ComplementsFrozen && !TryFreezeComplements(model))
            {
                return false;
            }

            List<RepairingDemand> demands = [];
            if(!TryExtractDemands(model, demands))
            {
                return false;
            }

            residueToAppendTo.Clear();
            demandSetsToAppendTo.Clear();
            bool mintedThisRound = false;
            for(int index = 0; index < demands.Count; index++)
            {
                List<OwlClassExpression> demandSet = [];
                if(!TryFormDemandSet(model, demands[index], demandSet, out bool closed))
                {
                    return false;
                }

                if(closed)
                {
                    residueToAppendTo.Add(demands[index]);
                    demandSetsToAppendTo.Add(demandSet);

                    continue;
                }

                if(!minted.Add(demands[index]) || !TryMint(model, demands[index], demandSet))
                {
                    return false;
                }

                mintedThisRound = true;
            }

            if(!mintedThisRound)
            {
                return residueToAppendTo.Count <= model.Options.Bounds.Demand;
            }

            cascade++;
            if(cascade > model.Options.Bounds.CascadeDepth)
            {
                return false;
            }
        }
    }

    /// <summary>Runs the deterministic repair to its fixpoint: each round freezes the edge relation, recomputes the class table from scratch over it, collects the whole forced-value set over that frozen table, inserts it SIMULTANEOUSLY and re-applies the closure operator. Simultaneous insertion makes the round's result independent of collection order, so two pins on one functional role yield a deterministic outcome whose failure surfaces as a count against a bound rather than as a canonicity-dependent verdict.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <returns><see langword="false"/> when an evaluation failed — a silence.</returns>
    private static bool TryRunDeterministicRepair(RepairingModel model)
    {
        while(true)
        {
            if(!TryRecomputeClassTable(model, model.Buffers.Classes, -1) || !TryRecomputeClassTable(model, model.Buffers.Restricted, RepairEveryRole))
            {
                return false;
            }

            List<RepairingEdge> forced = [];
            for(int index = 0; index < model.Obligations.Count; index++)
            {
                RepairingObligation obligation = model.Obligations[index];
                if(obligation.Kind != RepairingObligationKind.ValuePin)
                {
                    continue;
                }

                if(!TryEvaluate(obligation.Activator, model, model.Buffers.Restricted, model.SlotB, RepairEveryRole))
                {
                    return false;
                }

                Span<ulong> activator = model.Buffers.ScratchB;
                for(int element = 0; element < model.DeltaSize; element++)
                {
                    if(TestBit(activator, 0, element))
                    {
                        forced.Add(new RepairingEdge(obligation.Role, element, obligation.Value));
                    }
                }
            }

            Queue<RepairingEdge> work = new();
            bool inserted = false;
            for(int index = 0; index < forced.Count; index++)
            {
                RepairingEdge edge = forced[index];
                inserted = AddEdge(model, edge.Role, edge.Source, edge.Target, work) || inserted;
            }

            if(!inserted)
            {
                return true;
            }

            CommitClosure(model, work);
        }
    }

    /// <summary>Re-applies the told closure operator to the edges just committed, or drops them unclosed under the single-prologue variant, where the closure ran once over the told seeding and never again.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="work">The closure worklist, seeded with the edges just committed.</param>
    private static void CommitClosure(RepairingModel model, Queue<RepairingEdge> work)
    {
        if(model.Options.ClosureMode == RepairClosureMode.SinglePrologue)
        {
            work.Clear();

            return;
        }

        ApplyClosure(model, work);
    }

    /// <summary>
    /// Extracts the open demands over the FROZEN class table: an unmet
    /// existential or lower cardinality bound on a carrier the table already
    /// places in the restricting class. Reading the frozen table is what makes
    /// the demand set a function of the committed state, which the canonical
    /// order and the component decomposition both consume. The scan runs
    /// obligation-major, so each activating class and each filler is evaluated
    /// once, and the collected demands are then emitted OWNER-MAJOR with the
    /// obligation order — the role and the restriction position — minor, which
    /// is the order the mint indices are assigned in.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="demandsToAppendTo">The demand accumulator, in canonical owner-major order.</param>
    /// <returns><see langword="false"/> when an evaluation failed or the demand bound tripped — either a silence.</returns>
    private static bool TryExtractDemands(RepairingModel model, List<RepairingDemand> demandsToAppendTo)
    {
        List<RepairingDemand> collected = [];
        for(int index = 0; index < model.Obligations.Count; index++)
        {
            RepairingObligation obligation = model.Obligations[index];
            if(obligation.Kind is not RepairingObligationKind.Existential and not RepairingObligationKind.MinCardinality and not RepairingObligationKind.ExactCardinality)
            {
                continue;
            }

            if(!TryEvaluate(obligation.Activator, model, model.Buffers.Classes, model.SlotB, -1))
            {
                return false;
            }

            OwlClassExpression? filler = obligation.Filler;
            if(filler is not null && !TryEvaluate(filler, model, model.Buffers.Classes, model.SlotA, -1))
            {
                return false;
            }

            int fillerRow = filler is null ? -1 : 0;
            for(int element = 0; element < model.DeltaSize; element++)
            {
                if(TestBit(model.Buffers.ScratchB, 0, element) && CountSuccessors(model, obligation.Role, element, model.Buffers.ScratchA, fillerRow) < obligation.Bound)
                {
                    collected.Add(new RepairingDemand(element, index));
                }
            }
        }

        for(int element = 0; element < model.DeltaSize; element++)
        {
            for(int index = 0; index < collected.Count; index++)
            {
                if(collected[index].Carrier != element)
                {
                    continue;
                }

                demandsToAppendTo.Add(collected[index]);
                if(demandsToAppendTo.Count > model.Options.Bounds.Demand)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Counts one carrier's committed successors on a role, inside a filler extension where one is supplied and over the whole domain otherwise.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="role">The role index.</param>
    /// <param name="source">The owning element.</param>
    /// <param name="filler">The scratch region holding the filler extension.</param>
    /// <param name="fillerRow">The filler row's first word index, or <c>-1</c> for an unqualified count.</param>
    /// <returns>The successor count.</returns>
    private static int CountSuccessors(RepairingModel model, int role, int source, ReadOnlySpan<ulong> filler, int fillerRow)
    {
        int words = model.Buffers.Words;
        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        int row = EdgeRow(model, role, source);
        int count = 0;
        for(int word = 0; word < words; word++)
        {
            ulong successors = edges[row + word];
            if(fillerRow >= 0)
            {
                successors &= filler[fillerRow + word];
            }

            count += BitOperations.PopCount(successors);
        }

        return count;
    }

    /// <summary>
    /// Forms one demand's demand set: the declared range of the role under
    /// repair, every universal filler ACTIVE on the carrier, and the
    /// existential's own filler. A universal filler is admitted only where its
    /// activating membership is RE-DERIVABLE over the restricted class table —
    /// the phase-1 rule set with the universal-as-generator rule restricted AT
    /// THE ROLE UNDER REPAIR ONLY — so a universal holding solely because that
    /// role is empty at the carrier does not narrow the very demand the repair is
    /// opening. The restricted table is a per-demand SIDE computation and never
    /// replaces the phase-1 table.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="demand">The demand to form a set for.</param>
    /// <param name="setToAppendTo">The demand-set accumulator.</param>
    /// <param name="closed">Whether the set holds a class an enumeration closes, so no fresh element may be minted into it.</param>
    /// <returns><see langword="false"/> when an evaluation failed — a silence.</returns>
    private static bool TryFormDemandSet(RepairingModel model, RepairingDemand demand, List<OwlClassExpression> setToAppendTo, out bool closed)
    {
        closed = false;
        RepairingObligation obligation = model.Obligations[demand.Obligation];
        if(obligation.Filler is OwlClassExpression filler)
        {
            setToAppendTo.Add(filler);
        }

        for(int index = 0; index < model.Ranges.Count; index++)
        {
            if(model.Ranges[index].Role == obligation.Role)
            {
                setToAppendTo.Add(model.Ranges[index].Constraint);
            }
        }

        bool guarded = model.Options.VacuityGuardMode == RepairVacuityGuardMode.Guarded;
        bool restrictedReady = false;
        for(int index = 0; index < model.Obligations.Count; index++)
        {
            RepairingObligation universal = model.Obligations[index];
            if(universal.Kind != RepairingObligationKind.Universal || universal.Role != obligation.Role || universal.Filler is not OwlClassExpression universalFiller)
            {
                continue;
            }

            if(!TryEvaluate(universal.Activator, model, model.Buffers.Classes, model.SlotB, -1))
            {
                return false;
            }

            if(!TestBit(model.Buffers.ScratchB, 0, demand.Carrier))
            {
                continue;
            }

            if(guarded)
            {
                if(!restrictedReady)
                {
                    if(!TryRecomputeClassTable(model, model.Buffers.Restricted, obligation.Role))
                    {
                        return false;
                    }

                    restrictedReady = true;
                }

                if(!TryEvaluate(universal.Activator, model, model.Buffers.Restricted, model.SlotB, obligation.Role))
                {
                    return false;
                }

                if(!TestBit(model.Buffers.ScratchB, 0, demand.Carrier))
                {
                    continue;
                }
            }

            setToAppendTo.Add(universalFiller);
        }

        for(int index = 0; index < setToAppendTo.Count; index++)
        {
            closed = closed || IsEnumerationClosedExpression(setToAppendTo[index], model);
        }

        return true;
    }

    /// <summary>Whether one demand-set expression is closed by an enumeration: an enumeration outright, a named class the module equates with or subsumes into one, or an intersection carrying such a conjunct. The intersection descent is an explicit stack. A predicate that misses a longer chain mints into a genuinely closed filler and is caught by the covering equivalence's both-directions re-check at verification, so the reach here is a cost saving rather than a soundness question.</summary>
    /// <param name="root">The demand-set expression.</param>
    /// <param name="model">The structure under construction.</param>
    /// <returns><see langword="true"/> when the expression is enumeration-closed.</returns>
    private static bool IsEnumerationClosedExpression(OwlClassExpression root, RepairingModel model)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            switch(work.Pop())
            {
                case(OwlObjectOneOf):
                {
                    return true;
                }
                case(OwlClassReference reference):
                {
                    if(model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int index) && model.EnumerationClosed.Contains(index))
                    {
                        return true;
                    }

                    break;
                }
                case(OwlObjectIntersectionOf intersection):
                {
                    for(int index = 0; index < intersection.Operands.Count; index++)
                    {
                        work.Push(intersection.Operands[index]);
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

    /// <summary>
    /// Mints ONE fresh element into an open demand set: the bound pre-check reads
    /// the committed, closed edge relation and refuses a mint that would break a
    /// maximum or a functional bound on the carrier and role, the mint and
    /// carrier ceilings are checked before the domain grows, and the fresh
    /// element is typed into the demand set's named classes so its own
    /// obligations are subject to the same re-checked axioms. A refused mint is a
    /// silence, never a proposal.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="demand">The open demand.</param>
    /// <param name="demandSet">The demand's formed set.</param>
    /// <returns><see langword="false"/> when the pre-check or a ceiling refused the mint — a silence.</returns>
    private static bool TryMint(RepairingModel model, RepairingDemand demand, List<OwlClassExpression> demandSet)
    {
        RepairingObligation obligation = model.Obligations[demand.Obligation];
        int current = CountSuccessors(model, obligation.Role, demand.Carrier, model.Buffers.ScratchA, -1);
        if(model.Ground.FunctionalRoles.Contains(obligation.Role) && current + 1 > 1)
        {
            return false;
        }

        for(int index = 0; index < model.Obligations.Count; index++)
        {
            RepairingObligation ceiling = model.Obligations[index];
            if(ceiling.Kind is not RepairingObligationKind.MaxCardinality and not RepairingObligationKind.ExactCardinality || ceiling.Role != obligation.Role)
            {
                continue;
            }

            if(!TryEvaluate(ceiling.Activator, model, model.Buffers.Classes, model.SlotB, -1))
            {
                return false;
            }

            if(TestBit(model.Buffers.ScratchB, 0, demand.Carrier) && current + 1 > ceiling.Bound)
            {
                return false;
            }
        }

        if(model.MintCount + 1 > model.Options.Bounds.Mint || model.DeltaSize + 1 > model.Buffers.Delta || model.DeltaSize + 1 > RepairCarrierBound)
        {
            return false;
        }

        int mint = model.DeltaSize;
        model.DeltaSize++;
        model.MintCount++;
        for(int index = 0; index < demandSet.Count; index++)
        {
            TypeMint(demandSet[index], mint, model);
        }

        Queue<RepairingEdge> work = new();
        AddEdge(model, obligation.Role, demand.Carrier, mint, work);
        CommitClosure(model, work);

        return TryRecomputeClassTable(model, model.Buffers.Classes, -1);
    }

    /// <summary>Types one minted element into a demand-set expression's named classes, so every class it enters carries its own re-checked obligations. A named class enters directly and an intersection enters through each named conjunct; every other form is left to the verification pass.</summary>
    /// <param name="expression">The demand-set expression.</param>
    /// <param name="mint">The minted element index.</param>
    /// <param name="model">The structure under construction.</param>
    private static void TypeMint(OwlClassExpression expression, int mint, RepairingModel model)
    {
        if(expression is OwlClassReference reference && model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int named))
        {
            model.DirectMemberships.Add(new RepairingMembership(named, mint));

            return;
        }

        if(expression is not OwlObjectIntersectionOf intersection)
        {
            return;
        }

        for(int index = 0; index < intersection.Operands.Count; index++)
        {
            if(intersection.Operands[index] is OwlClassReference conjunct && model.Ground.ClassIndices.TryGetValue(conjunct.Class.Iri, out int conjunctClass))
            {
                model.DirectMemberships.Add(new RepairingMembership(conjunctClass, mint));
            }
        }
    }

    /// <summary>
    /// Evaluates every complement ONCE against the frozen positive class table,
    /// writing the resulting rows for the verification pass to read. The rows
    /// feed nothing back into the fixpoint, which is what keeps that fixpoint
    /// monotone, and a row frozen over a smaller domain leaves every later
    /// position clear rather than growing with the domain.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <returns><see langword="false"/> when an evaluation failed — a silence.</returns>
    private static bool TryFreezeComplements(RepairingModel model)
    {
        int words = model.Buffers.Words;
        model.ComplementsFrozen = false;
        model.Buffers.Complements.Clear();
        for(int index = 0; index < model.Ground.Complements.Count; index++)
        {
            if(!TryEvaluate(model.Ground.Complements[index].Operand, model, model.Buffers.Classes, model.SlotA, -1))
            {
                return false;
            }

            Span<ulong> rows = model.Buffers.Complements;
            Span<ulong> scratch = model.Buffers.ScratchA;
            int row = index * words;
            FillRow(rows, row, words, model.DeltaSize);
            for(int word = 0; word < words; word++)
            {
                rows[row + word] &= ~scratch[word];
            }
        }

        model.ComplementsFrozen = true;
        model.ComplementDomain = model.DeltaSize;

        return true;
    }

    /// <summary>
    /// Confirms that every frozen complement row STILL reads the exact
    /// complement of its operand over the finished domain. The rows are frozen
    /// from the positive fixpoint and then read back into the class table ONCE,
    /// so a class the module DEFINES by a complement carries its members and its
    /// defining equivalence is checkable in both directions; that read-back can
    /// only grow the positive extensions, and a growth that moved an operand
    /// would leave the frozen row stale. A stale row would make the
    /// verification pass read something other than the Direct-Semantics
    /// satisfaction condition, so an unstable row SILENCES instead.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <returns><see langword="false"/> when a row is stale or an evaluation failed — either a silence.</returns>
    private static bool TryConfirmComplements(RepairingModel model)
    {
        int words = model.Buffers.Words;
        for(int index = 0; index < model.Ground.Complements.Count; index++)
        {
            if(!TryEvaluate(model.Ground.Complements[index].Operand, model, model.Buffers.Classes, model.SlotA, -1))
            {
                return false;
            }

            ReadOnlySpan<ulong> rows = model.Buffers.Complements;
            ReadOnlySpan<ulong> operand = model.Buffers.ScratchA;
            for(int word = 0; word < words; word++)
            {
                int span = model.DeltaSize - (word * RepairWordBits);
                ulong mask = span >= RepairWordBits ? ulong.MaxValue : span <= 0 ? 0UL : (1UL << span) - 1UL;
                if((rows[(index * words) + word] & mask) != (~operand[word] & mask))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a disjunctive shape stands in an OBLIGATION-ACTIVATING POSITION,
    /// which this decider silences on. The position is the SUBCLASS side of an
    /// admitted subclass axiom whose superclass side carries a value pin, a
    /// cardinality or an existential, or either side of an admitted equivalence
    /// whose other side carries one. A complement-defined class, a union or an
    /// enumeration standing there is a DISJUNCTIVE MEMBERSHIP obligation, and
    /// this decider carries no membership-choice repair move at all: it invents
    /// edges and mints, never a membership choice, so such an obligation is
    /// unrepairable by construction. A shape in any other position is inert. The
    /// reading is the stricter one, so an unforeseen position costs completeness
    /// rather than escaping the rule.
    /// </summary>
    /// <param name="module">The admitted module.</param>
    /// <param name="shapes">The classifier's shape per axiom.</param>
    /// <returns><see langword="true"/> when the module must be silenced.</returns>
    private static bool SilencesOnObligationPosition(ReasoningModule module, RepairingShape[] shapes)
    {
        HashSet<Utf8String> complementDefined = [];
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            if(shapes[index] != RepairingShape.EquivalentClasses)
            {
                continue;
            }

            OwlEquivalentClassesAxiom equivalent = (OwlEquivalentClassesAxiom)module.Axioms[index];
            MarkComplementDefined(equivalent.First, equivalent.Second, complementDefined);
            MarkComplementDefined(equivalent.Second, equivalent.First, complementDefined);
        }

        for(int index = 0; index < module.Axioms.Count; index++)
        {
            switch(shapes[index])
            {
                case(RepairingShape.SubClassOf):
                {
                    OwlSubClassOfAxiom subClass = (OwlSubClassOfAxiom)module.Axioms[index];
                    if(CarriesObligationContent(subClass.SuperClass) && CarriesDisjunctiveShape(subClass.SubClass, complementDefined))
                    {
                        return true;
                    }

                    break;
                }
                case(RepairingShape.EquivalentClasses):
                {
                    OwlEquivalentClassesAxiom equivalent = (OwlEquivalentClassesAxiom)module.Axioms[index];
                    if(CarriesObligationContent(equivalent.Second) && CarriesDisjunctiveShape(equivalent.First, complementDefined))
                    {
                        return true;
                    }

                    if(CarriesObligationContent(equivalent.First) && CarriesDisjunctiveShape(equivalent.Second, complementDefined))
                    {
                        return true;
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

    /// <summary>
    /// Marks one named side of a told equivalence as COMPLEMENT-DEFINED where the
    /// other side carries a complement anywhere. A union- or enumeration-defined
    /// class is deliberately NOT marked: its extension is computed exactly by the
    /// class-table fixpoint, which reads a union as a generator in sub-position,
    /// so an obligation such a class activates is discharged by the ordinary
    /// repair with no membership choice anywhere. Only the complement is
    /// unreachable that way — its frozen post-pass feeds nothing back into the
    /// table — so only the complement makes a named reference disjunctive.
    /// </summary>
    /// <param name="namedSide">The candidate named side.</param>
    /// <param name="definingSide">The defining side.</param>
    /// <param name="definedToAppendTo">The complement-defined class accumulator.</param>
    private static void MarkComplementDefined(OwlClassExpression namedSide, OwlClassExpression definingSide, HashSet<Utf8String> definedToAppendTo)
    {
        if(namedSide is OwlClassReference named && CarriesComplement(definingSide))
        {
            definedToAppendTo.Add(named.Class.Iri);
        }
    }

    /// <summary>Whether one class expression carries a complement anywhere. The walk is an explicit stack.</summary>
    /// <param name="root">The expression to scan.</param>
    /// <returns><see langword="true"/> when a complement occurs.</returns>
    private static bool CarriesComplement(OwlClassExpression root)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            OwlClassExpression node = work.Pop();
            if(node is OwlObjectComplementOf)
            {
                return true;
            }

            PushOperands(node, work);
        }

        return false;
    }

    /// <summary>Whether one class expression carries a value pin, a cardinality or an existential anywhere — the content that makes the OTHER side of its axiom an obligation-activating position. The walk is an explicit stack.</summary>
    /// <param name="root">The expression to scan.</param>
    /// <returns><see langword="true"/> when obligation content occurs.</returns>
    private static bool CarriesObligationContent(OwlClassExpression root)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            OwlClassExpression node = work.Pop();
            if(node is OwlObjectHasValue or OwlObjectCardinality or OwlObjectSomeValuesFrom)
            {
                return true;
            }

            PushOperands(node, work);
        }

        return false;
    }

    /// <summary>Whether one class expression carries a complement, a union, an enumeration, or a reference to a complement-defined class anywhere — the disjunctive shapes this decider has no membership-choice repair move for. The walk is an explicit stack.</summary>
    /// <param name="root">The expression to scan.</param>
    /// <param name="complementDefined">The complement-defined named classes.</param>
    /// <returns><see langword="true"/> when a disjunctive shape occurs.</returns>
    private static bool CarriesDisjunctiveShape(OwlClassExpression root, HashSet<Utf8String> complementDefined)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            OwlClassExpression node = work.Pop();
            if(node is OwlObjectComplementOf or OwlObjectUnionOf or OwlObjectOneOf)
            {
                return true;
            }

            if(node is OwlClassReference reference && complementDefined.Contains(reference.Class.Iri))
            {
                return true;
            }

            PushOperands(node, work);
        }

        return false;
    }

    /// <summary>Pushes one class expression's direct child expressions onto a scan worklist.</summary>
    /// <param name="node">The expression to descend.</param>
    /// <param name="workToAppendTo">The scan worklist.</param>
    private static void PushOperands(OwlClassExpression node, Stack<OwlClassExpression> workToAppendTo)
    {
        switch(node)
        {
            case(OwlObjectIntersectionOf intersection):
            {
                for(int index = 0; index < intersection.Operands.Count; index++)
                {
                    workToAppendTo.Push(intersection.Operands[index]);
                }

                break;
            }
            case(OwlObjectUnionOf union):
            {
                for(int index = 0; index < union.Operands.Count; index++)
                {
                    workToAppendTo.Push(union.Operands[index]);
                }

                break;
            }
            case(OwlObjectComplementOf complement):
            {
                workToAppendTo.Push(complement.Operand);
                break;
            }
            case(OwlObjectSomeValuesFrom existential):
            {
                workToAppendTo.Push(existential.Filler);
                break;
            }
            case(OwlObjectAllValuesFrom universal):
            {
                workToAppendTo.Push(universal.Filler);
                break;
            }
            case(OwlObjectCardinality cardinality):
            {
                if(cardinality.Filler is OwlClassExpression filler)
                {
                    workToAppendTo.Push(filler);
                }

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>
    /// The bounded choice walk over the closed residue: an EXPLICIT FRAME STACK,
    /// one frame per residue demand, advanced as an odometer inside the component
    /// the attribution rule names. Verification runs at LEAVES ONLY — a complete
    /// assignment — and intermediate proposals are cut by the proposal-side
    /// pruning filter, which declares nothing. A failed pass discards THAT
    /// candidate model and control returns to the frame stack; the face goes
    /// silent only when a component exhausts, a failure spans components, or a
    /// bound trips.
    /// </summary>
    /// <param name="module">The admitted module.</param>
    /// <param name="shapes">The classifier's shape per axiom.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="residue">The closed demands.</param>
    /// <param name="demandSets">The demand set of each closed demand.</param>
    /// <param name="window">The window measurement the outcome carries.</param>
    /// <returns>The certificate, or silence — each with its measurement.</returns>
    private static RepairingOutcome Walk(ReasoningModule module, RepairingShape[] shapes, RepairingModel model, List<RepairingDemand> residue, List<List<OwlClassExpression>> demandSets, RepairingWindow window)
    {
        List<List<int>> candidates = [];
        for(int index = 0; index < residue.Count; index++)
        {
            List<int> list = [];
            if(!TryBuildCandidates(model, residue[index], demandSets[index], list) || list.Count > model.Options.Bounds.Branch)
            {
                return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, 0, 0));
            }

            candidates.Add(list);
        }

        int[] componentOf = new int[residue.Count];
        List<List<int>> components = BuildComponents(model, residue, candidates, componentOf);
        if(components.Count > model.Options.Bounds.ComponentCount)
        {
            return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, 0, 0));
        }

        for(int index = 0; index < components.Count; index++)
        {
            if(components[index].Count > model.Options.Bounds.Component)
            {
                return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, 0, 0));
            }
        }

        model.Buffers.Edges.CopyTo(model.Buffers.Baseline);
        int baselineEdges = model.EdgeCount;
        int baselineDelta = model.DeltaSize;
        int baselineMints = model.MintCount;
        int baselineMemberships = model.DirectMemberships.Count;
        List<RepairingFrame> frames = [];
        for(int index = 0; index < residue.Count; index++)
        {
            frames.Add(new RepairingFrame(index, 0));
        }

        int[] componentNodes = new int[components.Count];
        int evaluated = 0;
        int passes = 0;
        while(true)
        {
            model.Buffers.Baseline.CopyTo(model.Buffers.Edges);
            model.EdgeCount = baselineEdges;
            model.DeltaSize = baselineDelta;
            model.MintCount = baselineMints;
            model.DirectMemberships.RemoveRange(baselineMemberships, model.DirectMemberships.Count - baselineMemberships);

            Queue<RepairingEdge> work = new();
            int prunedComponent = -1;
            int prunedDemand = -1;
            for(int index = 0; index < frames.Count; index++)
            {
                RepairingFrame frame = frames[index];
                int component = componentOf[frame.Demand];
                componentNodes[component]++;
                evaluated++;
                if(componentNodes[component] > model.Options.Bounds.ComponentNode)
                {
                    return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, evaluated, passes));
                }

                int candidate = candidates[frame.Demand][frame.Candidate];
                if(PrunesProposal(model, residue[frame.Demand], demandSets[frame.Demand], candidate))
                {
                    prunedComponent = component;
                    prunedDemand = frame.Demand;
                    break;
                }

                AddEdge(model, model.Obligations[residue[frame.Demand].Obligation].Role, residue[frame.Demand].Carrier, candidate, work);
            }

            CommitClosure(model, work);
            if(prunedComponent >= 0)
            {
                if(!TryAdvance(components[prunedComponent], frames, candidates, prunedDemand))
                {
                    return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, evaluated, passes));
                }

                continue;
            }

            if(!TryRecomputeClassTable(model, model.Buffers.Classes, -1))
            {
                return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, evaluated, passes));
            }

            if(model.Options.ComplementPlacement == RepairComplementPlacement.PerCandidateAfterLastMint
                && (!TryFreezeComplements(model) || !TryRecomputeClassTable(model, model.Buffers.Classes, -1) || !TryConfirmComplements(model)))
            {
                return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, evaluated, passes));
            }

            passes++;
            if(passes > model.Options.Bounds.ModelVerify)
            {
                return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, evaluated, passes));
            }

            if(TryVerify(module, shapes, model, out int failedElement))
            {
                return new RepairingOutcome(true, Measured(window, model, residue.Count, evaluated, passes))
                {
                    Route = RepairedDescribedModelCertificate,
                };
            }

            int failing = AttributeFailure(residue, componentOf, failedElement);
            if(failing < 0 || !TryAdvance(components[failing], frames, candidates, stoppedAtDemand: -1))
            {
                return RepairingOutcome.SilentWith(Measured(window, model, residue.Count, evaluated, passes));
            }
        }
    }

    /// <summary>Overlays the construction's measured quantities onto the window the harvest read.</summary>
    /// <param name="window">The harvested window.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="choicePoints">The choice frames the walk opened.</param>
    /// <param name="evaluated">The local candidate evaluations the walk spent.</param>
    /// <param name="passes">The whole-module verification passes the decision spent.</param>
    /// <returns>The measured window.</returns>
    private static RepairingWindow Measured(RepairingWindow window, RepairingModel model, int choicePoints, int evaluated, int passes)
    {
        return window with
        {
            CarrierCount = model.DeltaSize,
            CommittedEdges = model.EdgeCount,
            MintedElements = model.MintCount,
            ChoicePointsOpened = choicePoints,
            EvaluatedNodes = evaluated,
            ModelVerifyPasses = passes,
        };
    }

    /// <summary>
    /// Builds one closed demand's canonical candidate list, first-match-wins: the
    /// value a co-occurring value-pin conjunct pins, at most one; otherwise the
    /// enumerated members of the closed filler in TOLD DOCUMENT ORDER. An OPEN
    /// demand set reaches neither source, so it has no candidate list at all and
    /// no existing individual is proposable there — the fresh mint is the only
    /// proposal this decider can make for an open demand.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="demand">The closed demand.</param>
    /// <param name="demandSet">The demand's formed set.</param>
    /// <param name="candidatesToAppendTo">The candidate accumulator.</param>
    /// <returns><see langword="false"/> when a candidate term is no carrier — a silence.</returns>
    private static bool TryBuildCandidates(RepairingModel model, RepairingDemand demand, List<OwlClassExpression> demandSet, List<int> candidatesToAppendTo)
    {
        RepairingObligation obligation = model.Obligations[demand.Obligation];
        for(int index = 0; index < model.Obligations.Count; index++)
        {
            RepairingObligation pin = model.Obligations[index];
            if(pin.Kind == RepairingObligationKind.ValuePin && pin.Role == obligation.Role && ReferenceEquals(pin.Activator, obligation.Activator))
            {
                candidatesToAppendTo.Add(pin.Value);

                return true;
            }
        }

        for(int index = 0; index < demandSet.Count; index++)
        {
            if(!TryAppendEnumeratedMembers(demandSet[index], model, candidatesToAppendTo))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Appends one demand-set expression's enumerated members in TOLD DOCUMENT ORDER: an enumeration directly, a named class through the enumeration it draws its closure from, and an intersection through each conjunct. The intersection descent is an explicit stack whose operands are pushed in reverse, so they pop in told order and the candidate list stays content-determined.</summary>
    /// <param name="root">The demand-set expression.</param>
    /// <param name="model">The structure under construction.</param>
    /// <param name="candidatesToAppendTo">The candidate accumulator.</param>
    /// <returns><see langword="false"/> when an enumerated term is no carrier.</returns>
    private static bool TryAppendEnumeratedMembers(OwlClassExpression root, RepairingModel model, List<int> candidatesToAppendTo)
    {
        Stack<OwlClassExpression> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            OwlClassExpression node = work.Pop();
            OwlObjectOneOf? enumeration = node switch
            {
                OwlObjectOneOf oneOf => oneOf,
                OwlClassReference reference when model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int index) && model.EnumerationMembers.TryGetValue(index, out OwlObjectOneOf? members) => members,
                _ => null,
            };

            if(enumeration is null)
            {
                if(node is OwlObjectIntersectionOf intersection)
                {
                    for(int index = intersection.Operands.Count - 1; index >= 0; index--)
                    {
                        work.Push(intersection.Operands[index]);
                    }
                }

                continue;
            }

            for(int index = 0; index < enumeration.Individuals.Count; index++)
            {
                if(!TryElementIndex(model.Ground, enumeration.Individuals[index], out int element))
                {
                    return false;
                }

                if(!candidatesToAppendTo.Contains(element))
                {
                    candidatesToAppendTo.Add(element);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// COMPUTES the component decomposition rather than assuming one: two demands
    /// are coupled where committing one CAN change the other's candidate set or
    /// obligation set — the same carrier, which is where every live coupling
    /// source sits (the one-hop cascade through a defined-class definition, and
    /// the bounded transitive closure that adds obligations at that same carrier),
    /// or one demand's candidate standing as the other's carrier, which is the
    /// only cross-carrier route a commit travels. Two demands of the SAME
    /// obligation on DIFFERENT carriers are NOT coupled: committing one carrier's
    /// successor changes neither the other's candidate list nor its obligations.
    /// An under-coupled decomposition cannot mint a wrong verdict — a failure it
    /// cannot attribute to one component declares itself invalid and silences.
    /// The relation is closed by a bounded union-find, so no helper recurses.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="residue">The closed demands.</param>
    /// <param name="candidates">The candidate list of each closed demand.</param>
    /// <param name="componentOf">The component index of each closed demand, filled by this pass.</param>
    /// <returns>The components, each a demand-index list in canonical order.</returns>
    private static List<List<int>> BuildComponents(RepairingModel model, List<RepairingDemand> residue, List<List<int>> candidates, int[] componentOf)
    {
        int[] parents = new int[residue.Count];
        for(int index = 0; index < residue.Count; index++)
        {
            parents[index] = index;
        }

        for(int first = 0; first < residue.Count; first++)
        {
            for(int second = first + 1; second < residue.Count; second++)
            {
                bool coupled = residue[first].Carrier == residue[second].Carrier
                    || candidates[first].Contains(residue[second].Carrier)
                    || candidates[second].Contains(residue[first].Carrier);
                if(coupled)
                {
                    UniteComponents(parents, first, second);
                }
            }
        }

        List<List<int>> components = [];
        Dictionary<int, int> roots = [];
        for(int index = 0; index < residue.Count; index++)
        {
            int root = FindComponent(parents, index);
            if(!roots.TryGetValue(root, out int component))
            {
                component = components.Count;
                roots[root] = component;
                components.Add([]);
            }

            componentOf[index] = component;
            components[component].Add(index);
        }

        return components;
    }

    /// <summary>Unites two demands' components under the union-find.</summary>
    /// <param name="parents">The union-find parents.</param>
    /// <param name="first">The first demand index.</param>
    /// <param name="second">The second demand index.</param>
    private static void UniteComponents(int[] parents, int first, int second)
    {
        int firstRoot = FindComponent(parents, first);
        int secondRoot = FindComponent(parents, second);
        if(firstRoot != secondRoot)
        {
            parents[firstRoot < secondRoot ? secondRoot : firstRoot] = firstRoot < secondRoot ? firstRoot : secondRoot;
        }
    }

    /// <summary>Reads one demand's component root over an iterative find with path compression.</summary>
    /// <param name="parents">The union-find parents.</param>
    /// <param name="demand">The demand index.</param>
    /// <returns>The component root.</returns>
    private static int FindComponent(int[] parents, int demand)
    {
        int root = demand;
        while(parents[root] != root)
        {
            root = parents[root];
        }

        int walk = demand;
        while(parents[walk] != root)
        {
            int next = parents[walk];
            parents[walk] = root;
            walk = next;
        }

        return root;
    }

    /// <summary>
    /// The phase-3 PRUNING FILTER: a proposal-side test internal to the walk that
    /// reads the COMMITTED edge set — told, repaired and minted alike — and stops
    /// a branch early. It forms no verdict on any path, writes no clash reason
    /// and moves no statistics field the clash face owns, which is exactly why it
    /// is permitted to read the repaired state the monotone clash face may never
    /// see: that face DECLARES and this filter does not. Over-pruning costs
    /// completeness only and routes to the same exhaustion silence as an
    /// exhausted branch; under-pruning costs only time, since every surviving
    /// leaf is fully re-verified.
    /// </summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="demand">The demand the candidate is proposed for.</param>
    /// <param name="demandSet">The demand's formed set.</param>
    /// <param name="candidate">The proposed element.</param>
    /// <returns><see langword="true"/> when the branch is cut.</returns>
    private static bool PrunesProposal(RepairingModel model, RepairingDemand demand, List<OwlClassExpression> demandSet, int candidate)
    {
        RepairingObligation obligation = model.Obligations[demand.Obligation];
        if(model.Ground.FunctionalRoles.Contains(obligation.Role))
        {
            int row = EdgeRow(model, obligation.Role, demand.Carrier);
            for(int element = 0; element < model.DeltaSize; element++)
            {
                if(element != candidate && TestBit(model.Buffers.Edges, row, element))
                {
                    return true;
                }
            }
        }

        for(int index = 0; index < demandSet.Count; index++)
        {
            if(!TryEvaluate(demandSet[index], model, model.Buffers.Classes, model.SlotA, -1) || !TestBit(model.Buffers.ScratchA, 0, candidate))
            {
                return true;
            }
        }

        for(int index = 0; index < model.Disjointness.Count; index++)
        {
            RepairingClassPair pair = model.Disjointness[index];
            if(CutsOnDisjointness(model, demandSet, candidate, pair.First, pair.Second) || CutsOnDisjointness(model, demandSet, candidate, pair.Second, pair.First))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a told disjointness pair cuts one proposal: the candidate already sits in one named class of the pair while the demand set requires the other.</summary>
    /// <param name="model">The structure under construction.</param>
    /// <param name="demandSet">The demand's formed set.</param>
    /// <param name="candidate">The proposed element.</param>
    /// <param name="held">The named class the candidate may already hold.</param>
    /// <param name="required">The named class the demand set may require.</param>
    /// <returns><see langword="true"/> when the pair cuts the proposal.</returns>
    private static bool CutsOnDisjointness(RepairingModel model, List<OwlClassExpression> demandSet, int candidate, int held, int required)
    {
        if(!TestBit(model.Buffers.Classes, held * model.Buffers.Words, candidate))
        {
            return false;
        }

        for(int index = 0; index < demandSet.Count; index++)
        {
            if(demandSet[index] is OwlClassReference reference
                && model.Ground.ClassIndices.TryGetValue(reference.Class.Iri, out int demanded)
                && demanded == required)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Advances ONE component's odometer over the frame stack, from the frame the
    /// walk stopped at: that frame takes its next candidate, carrying into the
    /// earlier frames of the component when it wraps, and every LATER frame of the
    /// component resets to its first candidate because the walk never proposed
    /// them. A cut at an early frame therefore backtracks THERE rather than
    /// cycling the whole component's product first, which is what keeps a
    /// component's node count bounded by its own candidate arithmetic. A complete
    /// assignment names no stopping frame and advances from the component's last
    /// one. A wrap across the whole component exhausts it.
    /// </summary>
    /// <param name="component">The component's demand indices in canonical order.</param>
    /// <param name="framesToAdvance">The explicit frame stack.</param>
    /// <param name="candidates">The candidate list of each demand.</param>
    /// <param name="stoppedAtDemand">The demand the walk stopped at, or <c>-1</c> for a complete assignment.</param>
    /// <returns><see langword="false"/> when the component exhausted.</returns>
    private static bool TryAdvance(List<int> component, List<RepairingFrame> framesToAdvance, List<List<int>> candidates, int stoppedAtDemand)
    {
        int start = component.Count - 1;
        if(stoppedAtDemand >= 0)
        {
            for(int index = 0; index < component.Count; index++)
            {
                if(component[index] == stoppedAtDemand)
                {
                    start = index;
                    break;
                }
            }

            for(int index = start + 1; index < component.Count; index++)
            {
                framesToAdvance[component[index]] = new RepairingFrame(component[index], 0);
            }
        }

        for(int index = start; index >= 0; index--)
        {
            int demand = component[index];
            int next = framesToAdvance[demand].Candidate + 1;
            if(next < candidates[demand].Count)
            {
                framesToAdvance[demand] = new RepairingFrame(demand, next);

                return true;
            }

            framesToAdvance[demand] = new RepairingFrame(demand, 0);
        }

        return false;
    }

    /// <summary>Attributes one failed verification pass to the component holding the demand of the FIRST FAILED CHECK. A failure whose witness belongs to no demand is the SPANNING case: the decomposition is declared invalid and the face silences rather than falling back to the cross-component product.</summary>
    /// <param name="residue">The closed demands.</param>
    /// <param name="componentOf">The component index of each closed demand.</param>
    /// <param name="failedElement">The element the first failed check witnessed, or <c>-1</c> where the check named none.</param>
    /// <returns>The failing component index, or <c>-1</c> for the spanning case.</returns>
    private static int AttributeFailure(List<RepairingDemand> residue, int[] componentOf, int failedElement)
    {
        if(failedElement < 0)
        {
            return -1;
        }

        for(int index = 0; index < residue.Count; index++)
        {
            if(residue[index].Carrier == failedElement)
            {
                return componentOf[index];
            }
        }

        return -1;
    }

    /// <summary>The whole-module verification pass over ONE candidate model: every admitted axiom's Direct-Semantics satisfaction condition, transcribed onto the finished structure. The pass ABANDONS on the FIRST failed check and never finishes scoring the axiom set, so no partial credit exists; a passing pass IS the structure satisfying the axiom set, which is the family's sole soundness carrier.</summary>
    /// <param name="module">The admitted module.</param>
    /// <param name="shapes">The classifier's shape per axiom.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The element the first failed check witnessed, or <c>-1</c> where the check named none or none failed.</param>
    /// <returns><see langword="true"/> when the structure satisfies every admitted axiom.</returns>
    private static bool TryVerify(ReasoningModule module, RepairingShape[] shapes, RepairingModel model, out int failedElement)
    {
        for(int index = 0; index < module.Axioms.Count; index++)
        {
            if(!IsSatisfied(module.Axioms[index], shapes[index], model, out failedElement))
            {
                return false;
            }
        }

        failedElement = -1;

        return true;
    }

    /// <summary>
    /// The verification pass over one axiom. The told inverse, symmetry,
    /// transitivity and sub-property axioms are re-checked despite the closure
    /// operator having built them, so the verifier never trusts the generator;
    /// every equivalence is checked independently as an equality of extensions;
    /// distinctness compares CARRIER INDICES AFTER QUOTIENT, never syntactic
    /// terms, so a module telling both sameness and difference of one pair fails
    /// here rather than passing a syntactic reading. The default arm is a
    /// SILENCE: a shape the classifier admitted and this pass does not know must
    /// never read as satisfied.
    /// </summary>
    /// <param name="axiom">The axiom to verify.</param>
    /// <param name="shape">The classifier's shape for the axiom.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The element the failed check witnessed, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when the structure satisfies the axiom.</returns>
    private static bool IsSatisfied(OwlAxiom axiom, RepairingShape shape, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        switch(shape)
        {
            case(RepairingShape.NonLogical):
            {
                return true;
            }
            case(RepairingShape.ClassAssertion):
            {
                OwlClassAssertionAxiom assertion = (OwlClassAssertionAxiom)axiom;
                if(!TryElementIndex(model.Ground, assertion.Individual, out int term))
                {
                    return false;
                }

                failedElement = term;

                return TryEvaluate(assertion.Class, model, model.Buffers.Classes, model.SlotA, -1) && TestBit(model.Buffers.ScratchA, 0, term);
            }
            case(RepairingShape.ObjectPropertyAssertion):
            {
                OwlObjectPropertyAssertionAxiom assertion = (OwlObjectPropertyAssertionAxiom)axiom;
                if(!TryElementIndex(model.Ground, assertion.Source, out int source) || !TryElementIndex(model.Ground, assertion.Target, out int target))
                {
                    return false;
                }

                failedElement = source;

                return HasEdge(model, assertion.Property.Iri, source, target);
            }
            case(RepairingShape.SubClassOf):
            {
                OwlSubClassOfAxiom subClass = (OwlSubClassOfAxiom)axiom;

                return IsExtensionSubset(subClass.SubClass, subClass.SuperClass, model, out failedElement);
            }
            case(RepairingShape.EquivalentClasses):
            {
                OwlEquivalentClassesAxiom equivalent = (OwlEquivalentClassesAxiom)axiom;

                return IsExtensionSubset(equivalent.First, equivalent.Second, model, out failedElement)
                    && IsExtensionSubset(equivalent.Second, equivalent.First, model, out failedElement);
            }
            case(RepairingShape.DisjointClasses):
            {
                return AreExtensionsDisjoint((OwlDisjointClassesAxiom)axiom, model, out failedElement);
            }
            case(RepairingShape.ObjectPropertyDomain):
            {
                OwlObjectPropertyDomainAxiom domain = (OwlObjectPropertyDomainAxiom)axiom;

                return AreRoleEndsConfined(domain.Property.Property.Iri, domain.Domain, model, sources: true, out failedElement);
            }
            case(RepairingShape.ObjectPropertyRange):
            {
                OwlObjectPropertyRangeAxiom range = (OwlObjectPropertyRangeAxiom)axiom;

                return AreRoleEndsConfined(range.Property.Property.Iri, range.Range, model, sources: false, out failedElement);
            }
            case(RepairingShape.InverseObjectProperties):
            {
                OwlInverseObjectPropertiesAxiom inverse = (OwlInverseObjectPropertiesAxiom)axiom;

                return AreConverse(inverse.First.Property.Iri, inverse.Second.Property.Iri, model, out failedElement);
            }
            case(RepairingShape.SubObjectPropertyOf):
            {
                OwlSubObjectPropertyOfAxiom inclusion = (OwlSubObjectPropertyOfAxiom)axiom;

                return IsRoleSubset(inclusion.SubProperty.Property.Iri, inclusion.SuperProperty.Property.Iri, model, out failedElement);
            }
            case(RepairingShape.SymmetricObjectProperty):
            {
                OwlObjectPropertyCharacteristicAxiom characteristic = (OwlObjectPropertyCharacteristicAxiom)axiom;

                return IsSymmetric(characteristic.Property.Property.Iri, model, out failedElement);
            }
            case(RepairingShape.TransitiveObjectProperty):
            {
                OwlObjectPropertyCharacteristicAxiom characteristic = (OwlObjectPropertyCharacteristicAxiom)axiom;

                return IsTransitive(characteristic.Property.Property.Iri, model, out failedElement);
            }
            case(RepairingShape.FunctionalObjectProperty):
            {
                OwlObjectPropertyCharacteristicAxiom characteristic = (OwlObjectPropertyCharacteristicAxiom)axiom;

                return IsFunctional(characteristic.Property.Property.Iri, model, out failedElement);
            }
            case(RepairingShape.SameIndividual):
            {
                OwlSameIndividualAxiom same = (OwlSameIndividualAxiom)axiom;

                return TryElementIndex(model.Ground, same.First, out int first)
                    && TryElementIndex(model.Ground, same.Second, out int second)
                    && first == second;
            }
            case(RepairingShape.DifferentIndividuals):
            {
                return AreDistinct((OwlDifferentIndividualsAxiom)axiom, model, out failedElement);
            }
            case(RepairingShape.DataPropertyAssertion):
            {
                OwlDataPropertyAssertionAxiom assertion = (OwlDataPropertyAssertionAxiom)axiom;

                return TryElementIndex(model.Ground, assertion.Source, out failedElement) && HasDataPair(model, failedElement, assertion.Property.Iri, assertion.Target);
            }
            case(RepairingShape.DataPropertyDomain):
            {
                OwlDataPropertyDomainAxiom domain = (OwlDataPropertyDomainAxiom)axiom;

                return AreDataSubjectsConfined(domain.Property.Iri, domain.Domain, model, out failedElement);
            }
            case(RepairingShape.DataPropertyRange):
            {
                OwlDataPropertyRangeAxiom range = (OwlDataPropertyRangeAxiom)axiom;

                return range.Range is OwlDatatypeReference reference && AreDataLiteralsTyped(range.Property.Iri, reference.Datatype.Iri, model, out failedElement);
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Whether one extension is contained in another over the finished structure, naming the first element outside the containment.</summary>
    /// <param name="sub">The contained expression.</param>
    /// <param name="super">The containing expression.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first element inside <paramref name="sub"/> and outside <paramref name="super"/>, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> on containment.</returns>
    private static bool IsExtensionSubset(OwlClassExpression sub, OwlClassExpression super, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!TryEvaluate(sub, model, model.Buffers.Classes, model.SlotA, -1) || !TryEvaluate(super, model, model.Buffers.Classes, model.SlotB, -1))
        {
            return false;
        }

        ReadOnlySpan<ulong> first = model.Buffers.ScratchA;
        ReadOnlySpan<ulong> second = model.Buffers.ScratchB;
        for(int element = 0; element < model.DeltaSize; element++)
        {
            if(TestBit(first, 0, element) && !TestBit(second, 0, element))
            {
                failedElement = element;

                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every unordered pair of a told disjointness axiom's operands has an empty intersection in the finished structure, naming the first shared element.</summary>
    /// <param name="axiom">The told disjointness axiom.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first shared element, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every pair is disjoint.</returns>
    private static bool AreExtensionsDisjoint(OwlDisjointClassesAxiom axiom, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        for(int first = 0; first < axiom.Operands.Count; first++)
        {
            for(int second = first + 1; second < axiom.Operands.Count; second++)
            {
                if(!TryEvaluate(axiom.Operands[first], model, model.Buffers.Classes, model.SlotA, -1)
                    || !TryEvaluate(axiom.Operands[second], model, model.Buffers.Classes, model.SlotB, -1))
                {
                    return false;
                }

                ReadOnlySpan<ulong> left = model.Buffers.ScratchA;
                ReadOnlySpan<ulong> right = model.Buffers.ScratchB;
                for(int element = 0; element < model.DeltaSize; element++)
                {
                    if(TestBit(left, 0, element) && TestBit(right, 0, element))
                    {
                        failedElement = element;

                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Whether the finished structure holds one ordered edge of a role.</summary>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="role">The role IRI.</param>
    /// <param name="source">The source element.</param>
    /// <param name="target">The target element.</param>
    /// <returns><see langword="true"/> when the edge is held.</returns>
    private static bool HasEdge(RepairingModel model, Utf8String role, int source, int target)
    {
        return model.Ground.RoleIndices.TryGetValue(role, out int index) && TestBit(model.Buffers.Edges, EdgeRow(model, index, source), target);
    }

    /// <summary>Whether every source or every target of a role's committed extension lies inside a confining extension — the domain and range satisfaction conditions.</summary>
    /// <param name="role">The constrained role's IRI.</param>
    /// <param name="confining">The confining class expression.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="sources">Whether the sources are checked; the targets otherwise.</param>
    /// <param name="failedElement">The first unconfined end, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every checked end is confined.</returns>
    private static bool AreRoleEndsConfined(Utf8String role, OwlClassExpression confining, RepairingModel model, bool sources, out int failedElement)
    {
        failedElement = -1;
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return true;
        }

        if(!TryEvaluate(confining, model, model.Buffers.Classes, model.SlotA, -1))
        {
            return false;
        }

        ReadOnlySpan<ulong> confined = model.Buffers.ScratchA;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            int row = EdgeRow(model, index, source);
            for(int target = 0; target < model.DeltaSize; target++)
            {
                if(!TestBit(model.Buffers.Edges, row, target))
                {
                    continue;
                }

                int end = sources ? source : target;
                if(!TestBit(confined, 0, end))
                {
                    failedElement = end;

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether two roles' committed extensions are exact converses of one another — the inverse-properties satisfaction condition, re-checked rather than assumed from the closure operator that built it.</summary>
    /// <param name="first">The first role's IRI.</param>
    /// <param name="second">The second role's IRI.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first source whose converse fails, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when each role holds exactly the other's reversed pairs.</returns>
    private static bool AreConverse(Utf8String first, Utf8String second, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!model.Ground.RoleIndices.TryGetValue(first, out int firstRole) || !model.Ground.RoleIndices.TryGetValue(second, out int secondRole))
        {
            return false;
        }

        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int target = 0; target < model.DeltaSize; target++)
            {
                bool forward = TestBit(model.Buffers.Edges, EdgeRow(model, firstRole, source), target);
                bool backward = TestBit(model.Buffers.Edges, EdgeRow(model, secondRole, target), source);
                if(forward != backward)
                {
                    failedElement = source;

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether one role's committed extension is contained in another's — the sub-property satisfaction condition.</summary>
    /// <param name="sub">The subproperty's IRI.</param>
    /// <param name="super">The superproperty's IRI.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first source whose inclusion fails, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> on containment.</returns>
    private static bool IsRoleSubset(Utf8String sub, Utf8String super, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!model.Ground.RoleIndices.TryGetValue(sub, out int subRole) || !model.Ground.RoleIndices.TryGetValue(super, out int superRole))
        {
            return false;
        }

        int words = model.Buffers.Words;
        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            int subRow = EdgeRow(model, subRole, source);
            int superRow = EdgeRow(model, superRole, source);
            for(int word = 0; word < words; word++)
            {
                if((edges[subRow + word] & ~edges[superRow + word]) != 0)
                {
                    failedElement = source;

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether one role's committed extension is symmetric — the symmetry satisfaction condition.</summary>
    /// <param name="role">The role's IRI.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first source whose mirror is missing, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every edge has its mirror.</returns>
    private static bool IsSymmetric(Utf8String role, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return false;
        }

        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int target = 0; target < model.DeltaSize; target++)
            {
                if(TestBit(model.Buffers.Edges, EdgeRow(model, index, source), target) && !TestBit(model.Buffers.Edges, EdgeRow(model, index, target), source))
                {
                    failedElement = source;

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether one role's committed extension is transitive — the transitivity satisfaction condition.</summary>
    /// <param name="role">The role's IRI.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first source whose composition is missing, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every composition is held.</returns>
    private static bool IsTransitive(Utf8String role, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return false;
        }

        for(int source = 0; source < model.DeltaSize; source++)
        {
            for(int middle = 0; middle < model.DeltaSize; middle++)
            {
                if(!TestBit(model.Buffers.Edges, EdgeRow(model, index, source), middle))
                {
                    continue;
                }

                for(int target = 0; target < model.DeltaSize; target++)
                {
                    if(TestBit(model.Buffers.Edges, EdgeRow(model, index, middle), target) && !TestBit(model.Buffers.Edges, EdgeRow(model, index, source), target))
                    {
                        failedElement = source;

                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Whether one role's committed extension is functional — at most one successor per source, counted over DISTINCT CARRIERS, which under the injective carrier map are distinct domain elements.</summary>
    /// <param name="role">The role's IRI.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first source holding two successors, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every source holds at most one successor.</returns>
    private static bool IsFunctional(Utf8String role, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!model.Ground.RoleIndices.TryGetValue(role, out int index))
        {
            return false;
        }

        int words = model.Buffers.Words;
        ReadOnlySpan<ulong> edges = model.Buffers.Edges;
        for(int source = 0; source < model.DeltaSize; source++)
        {
            int row = EdgeRow(model, index, source);
            int count = 0;
            for(int word = 0; word < words; word++)
            {
                count += BitOperations.PopCount(edges[row + word]);
            }

            if(count > 1)
            {
                failedElement = source;

                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a told distinctness axiom's terms denote pairwise distinct CARRIER INDICES AFTER QUOTIENT. A module telling both sameness and difference of one pair holds two syntactically distinct and quotient-identical terms, so a syntactic reading would pass while the constructed model violates the axiom; the index comparison fails it instead.</summary>
    /// <param name="axiom">The told distinctness axiom.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The colliding element, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every pair denotes distinct elements.</returns>
    private static bool AreDistinct(OwlDifferentIndividualsAxiom axiom, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        for(int first = 0; first < axiom.Individuals.Count; first++)
        {
            if(!TryElementIndex(model.Ground, axiom.Individuals[first], out int left))
            {
                return false;
            }

            for(int second = first + 1; second < axiom.Individuals.Count; second++)
            {
                if(!TryElementIndex(model.Ground, axiom.Individuals[second], out int right) || left == right)
                {
                    failedElement = left;

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether the told-pairs extension of a data property holds one told pair — true by construction, since that extension IS the told pairs.</summary>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="subject">The subject's element index.</param>
    /// <param name="property">The data property's IRI.</param>
    /// <param name="value">The told literal.</param>
    /// <returns><see langword="true"/> when the pair is held.</returns>
    private static bool HasDataPair(RepairingModel model, int subject, Utf8String property, Literal value)
    {
        for(int index = 0; index < model.Ground.DataPairs.Count; index++)
        {
            RepairingDataPair pair = model.Ground.DataPairs[index];
            if(model.Ground.CarrierElements[pair.Carrier] == subject && pair.Property.Equals(property) && pair.Value.Equals(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether every subject of a data property's told pairs lies in the domain class's fixpoint extension — the data-property domain satisfaction condition under the told-pairs reading.</summary>
    /// <param name="property">The data property's IRI.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first unconfined subject, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every told subject is confined.</returns>
    private static bool AreDataSubjectsConfined(Utf8String property, OwlClassExpression domain, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        if(!TryEvaluate(domain, model, model.Buffers.Classes, model.SlotA, -1))
        {
            return false;
        }

        ReadOnlySpan<ulong> confined = model.Buffers.ScratchA;
        for(int index = 0; index < model.Ground.DataPairs.Count; index++)
        {
            RepairingDataPair pair = model.Ground.DataPairs[index];
            if(!pair.Property.Equals(property))
            {
                continue;
            }

            int element = model.Ground.CarrierElements[pair.Carrier];
            if(!TestBit(confined, 0, element))
            {
                failedElement = element;

                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every literal of a data property's told pairs carries the range's datatype IRI — the data-property range satisfaction condition over a PLAIN DATATYPE IRI, with no data-value domain built and no numeric fallback performed.</summary>
    /// <param name="property">The data property's IRI.</param>
    /// <param name="datatype">The range's datatype IRI.</param>
    /// <param name="model">The finished candidate model.</param>
    /// <param name="failedElement">The first offending subject, or <c>-1</c>.</param>
    /// <returns><see langword="true"/> when every told literal carries the datatype.</returns>
    private static bool AreDataLiteralsTyped(Utf8String property, Utf8String datatype, RepairingModel model, out int failedElement)
    {
        failedElement = -1;
        for(int index = 0; index < model.Ground.DataPairs.Count; index++)
        {
            RepairingDataPair pair = model.Ground.DataPairs[index];
            if(pair.Property.Equals(property) && !pair.Value.Datatype.Iri.Equals(datatype))
            {
                failedElement = model.Ground.CarrierElements[pair.Carrier];

                return false;
            }
        }

        return true;
    }
}
