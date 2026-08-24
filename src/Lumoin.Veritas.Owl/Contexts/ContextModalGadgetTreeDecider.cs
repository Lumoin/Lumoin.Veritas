using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape K clash reason family — stable leading identifiers the statistics assembly and the battery discriminate on.</summary>
internal static class ModalGadgetClashReasons
{
    /// <summary>The asserted-bottom clash: the individual carries a told complement of <c>owl:Thing</c>, or the closure derived its membership in <c>owl:Nothing</c>. The reason carries no argument, because both readings name the same empty extension.</summary>
    public const string AssertedBottomMembership = "ModalGadgetAssertedBottomMembership";

    /// <summary>The complemented-membership clash: the composition closure derived a class membership at an individual whose told class assertions carry that same class under a complement.</summary>
    /// <param name="classIri">The class IRI derived at the individual and denied at it.</param>
    /// <returns>The named reason.</returns>
    public static string ComplementedMembership(Utf8String classIri)
    {
        return $"ModalGadgetComplementedMembership({classIri})";
    }
}

/// <summary>Which entry point the decider takes and which face answers. The production value is code zero, so an options value left at <see langword="default"/> is bit-identical to production.</summary>
internal enum ModalGadgetEntry
{
    /// <summary>The production path: both faces answer on their own entry points, each behind its own jurisdiction.</summary>
    Decide = 0,

    /// <summary>The measurement path: the window ceilings are compared and nothing is derived, composed, constructed or verified — the dark control's entry, which forms no verdict on any input.</summary>
    MeasureOnly = 1,

    /// <summary>The clash face alone answers; the certify entry point returns silence carrying its measurement.</summary>
    ClashOnly = 2,

    /// <summary>The certify face alone answers; the clash entry point returns silence carrying its measurement.</summary>
    CertifyOnly = 3,
}

/// <summary>
/// The eleven modal-gadget bounds as overridable members where ZERO MEANS "USE
/// THE <c>const</c>", so a value left at <see langword="default"/> is exactly
/// production and a caller supplies only the non-zero overrides it needs. Each
/// effective bound is read through a get-only property that returns the
/// <c>const</c> on a zero backing member, so no member ever holds a duplicated
/// literal of a <c>const</c>.
/// </summary>
/// <param name="FreeAtomOverride">The surviving-free-atom override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetFreeAtomBound"/>.</param>
/// <param name="SignatureOverride">The deduped-signature override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetSignatureBound"/>.</param>
/// <param name="ModalAtomOverride">The raw-modal-atom override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetModalAtomBound"/>.</param>
/// <param name="VectorOverride">The evaluated-vector override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetVectorBound"/>.</param>
/// <param name="NodeOverride">The node-arena override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetNodeBound"/>.</param>
/// <param name="DepthOverride">The spawn-depth override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetDepthBound"/>.</param>
/// <param name="LabelOverride">The classes-per-node override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetLabelBound"/>.</param>
/// <param name="EdgeOverride">The directed-edge override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetEdgeBound"/>.</param>
/// <param name="AxiomOverride">The module-admission override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetAxiomBound"/>.</param>
/// <param name="VerifyPassOverride">The whole-module-verification override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetVerifyPassBound"/>.</param>
/// <param name="StepOverride">The rule-application override; zero reads <see cref="ContextModalGadgetTreeDecider.ModalGadgetStepBound"/>.</param>
internal readonly record struct ModalGadgetBounds(
    int FreeAtomOverride,
    int SignatureOverride,
    int ModalAtomOverride,
    int VectorOverride,
    int NodeOverride,
    int DepthOverride,
    int LabelOverride,
    int EdgeOverride,
    int AxiomOverride,
    int VerifyPassOverride,
    int StepOverride)
{
    /// <summary>The effective ceiling on the gadget atoms that survive defined-atom elimination.</summary>
    public int FreeAtom => FreeAtomOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetFreeAtomBound : FreeAtomOverride;

    /// <summary>The effective ceiling on the deduped successor demands.</summary>
    public int Signature => SignatureOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetSignatureBound : SignatureOverride;

    /// <summary>The effective ceiling on the raw existential and universal occurrences at one node.</summary>
    public int ModalAtom => ModalAtomOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetModalAtomBound : ModalAtomOverride;

    /// <summary>The effective ceiling on the vectors actually evaluated.</summary>
    public int Vector => VectorOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetVectorBound : VectorOverride;

    /// <summary>The effective ceiling on the node arena — told individuals and spawned successors together.</summary>
    public int Node => NodeOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetNodeBound : NodeOverride;

    /// <summary>The effective ceiling on the spawn depth below the told frontier.</summary>
    public int Depth => DepthOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetDepthBound : DepthOverride;

    /// <summary>The effective ceiling on the named classes one node's label may span.</summary>
    public int Label => LabelOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetLabelBound : LabelOverride;

    /// <summary>The effective ceiling on the directed edges the structure holds, all roles together.</summary>
    public int Edge => EdgeOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetEdgeBound : EdgeOverride;

    /// <summary>The effective ceiling on the module's logical axiom count at admission.</summary>
    public int Axiom => AxiomOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetAxiomBound : AxiomOverride;

    /// <summary>The effective ceiling on the whole-module verification passes one decision may spend.</summary>
    public int VerifyPass => VerifyPassOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetVerifyPassBound : VerifyPassOverride;

    /// <summary>The effective ceiling on the clash face's rule applications.</summary>
    public int Step => StepOverride == 0 ? ContextModalGadgetTreeDecider.ModalGadgetStepBound : StepOverride;
}

/// <summary>
/// The certify face's CONSTRUCTION-ONLY variation points, accepted only by the
/// internal entry points. Every member names its PRODUCTION behaviour at code
/// zero, so a value left at <see langword="default"/> is bit-identical to
/// production and the reasoner's call path passes none. A construction variation
/// is safe here where the sibling family forbade one outright: the certify face
/// HAS a verification pass, and that pass re-evaluates every admitted axiom
/// against the finished structure, so a non-default construction can produce a
/// SILENCE or a slower walk and never a wrong verdict. The licence is bounded —
/// no member varies the verification pass, no member varies the clash face's
/// rules, and no member varies an admission gate.
/// </summary>
/// <param name="SuppressDefinedAtomElimination">Whether every gadget atom is enumerated instead of computed; production computes the defined ones.</param>
/// <param name="SuppressMinimalModalFirst">Whether the walk skips the all-false modal vector's head position; production tries it first.</param>
internal readonly record struct ModalGadgetConstruction(
    bool SuppressDefinedAtomElimination,
    bool SuppressMinimalModalFirst);

/// <summary>
/// The modal-gadget family's THREE variation points, accepted only by the
/// internal entry points. The seam is CLOSED at three members and a fourth needs
/// its own claim.
/// </summary>
/// <param name="Entry">Whether the decider decides, only measures, or answers on one face alone.</param>
/// <param name="Bounds">The eleven modal-gadget bounds, zero-means-production per member.</param>
/// <param name="Construction">The certify face's two construction variations, zero-means-production per member.</param>
internal readonly record struct ModalGadgetConstructionOptions(
    ModalGadgetEntry Entry,
    ModalGadgetBounds Bounds,
    ModalGadgetConstruction Construction);

/// <summary>
/// The Shape K window measurement the census-first recognizer's
/// pre-clausification pass reads on every modal-gadget-jurisdiction module. The
/// quantities are charged under the family's counting conventions: a free atom is
/// a gadget property atom that survives defined-atom elimination and a defined
/// atom is never counted; a signature is the COMPUTED propositional signature
/// over the free literals with each non-propositional filler its own signature,
/// taken once at admission over the module's existential occurrences and
/// therefore independent of which vector is tried; a node is an element of the
/// constructed domain, told or spawned. Every quantity is a property of the
/// module or of the vector that decided, never of a scan order: the clash face's
/// closure is a unique least fixpoint and the certify face's construction is
/// deterministic given its vector.
/// The window carries counters and no memberships, and it is the ONLY type the
/// two faces share.
/// </summary>
/// <param name="FreeAtomCount">The gadget atoms that survived defined-atom elimination on the vector that decided, or on the last vector tried on a silence.</param>
/// <param name="SignatureCount">The deduped successor demands measured once at admission.</param>
/// <param name="NodesBuilt">The arena nodes the construction held — told and spawned together.</param>
/// <param name="WindowSilences">One per face that charged a bound trip; a bound trip is a named silence, never a verdict over an unfinished structure.</param>
internal readonly record struct ModalGadgetWindow(
    int FreeAtomCount,
    int SignatureCount,
    int NodesBuilt,
    int WindowSilences)
{
    /// <summary>The empty window: no modal-gadget measurement ran.</summary>
    public static ModalGadgetWindow Empty => default;
}

/// <summary>The Shape K clash face's outcome: the monotone closure's refutation when a clash was reached inside the step bound, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The verdict — <see langword="false"/> for the reached clash — or <see langword="null"/> when the face is silent on the module. The face has no certify direction, so <see langword="true"/> never occurs and the type has no path to it.</param>
/// <param name="Window">The window measurement.</param>
/// <param name="Reason">The named clash reason on a refutation; <see langword="null"/> on every silent outcome.</param>
internal readonly record struct ModalGadgetClashOutcome(bool? Consistent, ModalGadgetWindow Window, string? Reason)
{
    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static ModalGadgetClashOutcome SilentWith(ModalGadgetWindow window)
    {
        return new ModalGadgetClashOutcome(null, window, null);
    }
}

/// <summary>
/// The Shape K certify face's outcome: the minted tree's certificate when the
/// verification pass accepted the whole module, and the window measurement the
/// census carries unconditionally. The type carries NO MEMBERSHIP MEMBER OF ANY
/// KIND — not a class, not an individual, not a pair, not a model handle —
/// because the certify model witnesses CONSISTENCY and nothing else: a second
/// model of the same module can put an element in a positive class where this one
/// puts it in the complement, and every real goal holds in both. A membership
/// fact cannot be read out of this face because there is nowhere to read it from.
/// </summary>
/// <param name="Consistent">The verdict — <see langword="true"/> for the verified certificate — or <see langword="null"/> when the face is silent on the module. The face has no refutation direction, so <see langword="false"/> never occurs and the type has no path to it.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct ModalGadgetCertifyOutcome(bool? Consistent, ModalGadgetWindow Window)
{
    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static ModalGadgetCertifyOutcome SilentWith(ModalGadgetWindow window)
    {
        return new ModalGadgetCertifyOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's TWO modal-gadget faces over the
/// BRANCHING MODAL-GADGET habitat: a module built from two syntactically DISJOINT
/// layers — a propositional layer of unqualified cardinality gadgets composed by
/// binary-intersection equivalences over named classes, and a modal layer of
/// existentials and universals over ONE characteristic-free role — carrying a told
/// ABox of class assertions and no property assertions at all.
/// The CLASH face (face seventeen) is a TIER-2 monotone fixpoint: binary
/// intersection composition in both directions over told and derived membership,
/// ending in a clash where a derived membership meets its own told complement or a
/// told bottom. It never leaves the told individuals, reads no cardinality bound,
/// has no role rule at all and never observes a constructed model, and those
/// exclusions are what license its monotone jurisdiction — every derivation is a
/// chain of set-intersection facts that holds in every model of any superset of
/// the axioms it used, so an axiom it did not read can never invalidate one.
/// The CERTIFY face (face eighteen) is a TIER-3 construct-then-verify over a
/// MINTED skolem tree: whole-module allow-list admission, defined-atom
/// elimination, told unit propagation, a minimal-modal-first vector walk, one
/// successor per TRUE EXISTENTIAL atom deduped by computed filler signature — a
/// universal NEVER spawns, it only pushes its filler onto children the
/// existentials already create — and then a verification pass that re-evaluates
/// every admitted axiom against the finished structure's RAW RELATIONS. That pass
/// is the SOLE soundness carrier: no property of the construction is relied on,
/// equivalences are checked as set equality in BOTH inclusions at every element,
/// and only a clean pass emits a certificate.
/// The two faces share NO derivation structure. The certify outcome carries no
/// membership member, the clash face cannot reach a constructed interpretation
/// from its entry point, and the only type both touch is the window, which carries
/// counters and no memberships.
/// The two JURISDICTION POSTURES are opposite and are never merged: the clash face
/// IGNORES what it does not recognize, licensed by the exclusions above; the
/// certify face is whole-module ALL-OR-NOTHING and anything outside its allow-list
/// silences the module whole, because a certify face that ignores an axiom
/// certifies a structure that axiom may falsify.
/// Sound-or-silent throughout: the clash face answers <see langword="false"/> or
/// silence and the certify face <see langword="true"/> or silence. A failed
/// construction, a told unit contradiction, an exhausted vector sweep, a failed
/// verification pass and a window trip are ALL silence, and none of them is ever
/// read as a verdict. Termination comes from the BOUNDS, never from a blocking
/// condition: no blocking condition of any kind is implemented, so none can be
/// implemented wrongly. Completeness is NOT claimed.
/// Nothing recurses: the clash closure is an explicit worklist, the definition
/// graph is an explicit Kahn queue, the tree walk is an explicit level loop, and
/// the verification pass evaluates a grammar whose admitted shapes are of fixed
/// depth over named operands, so it descends nowhere at all.
/// </summary>
internal static class ContextModalGadgetTreeDecider
{
    /// <summary>
    /// The surviving-free-atom ceiling: the gadget atoms that survive
    /// defined-atom elimination are enumerated up to this many and the certify
    /// face is SILENT above it. The bound is an EXPONENT bound — it caps the
    /// SIZE of one vector, and the vector ceiling caps how many are evaluated —
    /// and it JOINS the house shared-sixteen atom-ceiling family, whose
    /// uniformity claim is that an atom ceiling a bounded assignment walk
    /// enumerates over sits at sixteen. It is charged against B1's SURVIVING
    /// atoms and NEVER against a module's raw gadget-atom count: a module whose
    /// raw atoms far exceed this ceiling is admitted whenever its residue after
    /// elimination fits.
    /// </summary>
    public const int ModalGadgetFreeAtomBound = 16;

    /// <summary>The deduped-successor ceiling: one node's TRUE existentials collapse to at most this many distinct successor demands and the certify face is SILENT above it. The bound is an EXPONENT bound over the quantity the walk actually enumerates — enumeration runs over deduped SIGNATURES, never over raw modal atoms — and it JOINS the house shared-sixteen atom-ceiling family for the same reason the free-atom ceiling does.</summary>
    public const int ModalGadgetSignatureBound = 16;

    /// <summary>
    /// The raw-modal-atom ceiling: one node carries up to this many raw modal
    /// atoms and the certify face is SILENT above it. A raw modal atom is ONE
    /// EXISTENTIAL OR UNIVERSAL occurrence at the node before dedupe — both
    /// quantifier kinds, which is what this ceiling is charged against and what
    /// separates it from the deduped-signature ceiling beside it. The constant
    /// carries a SELF-CONTAINED justification and joins no uniformity family: it
    /// sizes a node's modal state vector and its per-node evaluation, which is a
    /// different quantity from the atom ceilings a bounded assignment walk
    /// enumerates over, and a module carrying many diamonds that dedupe to few
    /// signatures would clear the signature ceiling while carrying a wide modal
    /// state. Raising it widens a node's state vector and nothing else, because
    /// the walk never enumerates raw atoms.
    /// </summary>
    public const int ModalGadgetModalAtomBound = 32;

    /// <summary>The evaluated-vector ceiling: one decision evaluates up to this many free-and-modal vectors and the certify face is SILENT above it. This is the BINDING cost control of the eleven — the two exponent ceilings cap the size of a vector and this one caps how many are tried, so worst-case work is this constant times one construction and one verification pass — and it is the bound that clears the house linear-headroom floor by four orders of magnitude.</summary>
    public const int ModalGadgetVectorBound = 65536;

    /// <summary>The node-arena ceiling: told individuals PLUS spawned successors together are held up to this many and the certify face is SILENT above it. Told individuals are nodes of the constructed structure and carry labels and edges, so a module whose TOLD individuals alone exceed the arena is a window silence, never a second allocation and never an exception. The packed label table holds one bit per node and named class, so the arena's cost is this constant against the label ceiling.</summary>
    public const int ModalGadgetNodeBound = 64;

    /// <summary>The spawn-depth ceiling: successors are minted down to this many levels below the told frontier and the certify face is SILENT below it. It cannot bind for any branching factor above one given the node arena, and is carried for the boundary discipline and for the linear-chain case.</summary>
    public const int ModalGadgetDepthBound = 8;

    /// <summary>The classes-per-node ceiling: the module's named classes are held up to this many and the certify face is SILENT above it. The quantity is the width of one packed label row, so the bound and the table's row span compare the same number.</summary>
    public const int ModalGadgetLabelBound = 512;

    /// <summary>The directed-edge ceiling: the structure holds up to this many edges — counted once per role and ordered pair, with a gadget self-loop an edge and a data-property literal NOT an edge, since it is no ordered pair of domain elements — and the certify face is SILENT above it.</summary>
    public const int ModalGadgetEdgeBound = 256;

    /// <summary>The module-admission ceiling: a module carrying more than this many logical axioms is outside the certify face's admission and the face is SILENT on it. Non-logical content — declarations, annotations — is not charged.</summary>
    public const int ModalGadgetAxiomBound = 1024;

    /// <summary>The whole-module-verification ceiling: one decision spends up to this many complete evaluations of every admitted axiom against a finished structure and the certify face is SILENT above it. A partial pass abandoned on the first failure still counts as one. The value MIRRORS the ninth family's repaired-model verification ceiling, so the house carries ONE number for how many whole-module verification passes a certify face may spend.</summary>
    public const int ModalGadgetVerifyPassBound = 64;

    /// <summary>The rule-application ceiling: the clash face charges up to this many rule firings and is SILENT above it. One application is ONE rule firing producing one membership fact; a clash check is not an application. The bound governs the clash face ALONE and is independent of every other bound here, because that face creates no nodes.</summary>
    public const int ModalGadgetStepBound = 4096;

    /// <summary>The word width of one packed bitset word.</summary>
    private const int ModalGadgetWordBits = 64;

    /// <summary>The family's own buffer pool: the packed label table, the role-indexed edge planes and the per-node data-filler bits are rented from here, never from a shared pool, once per decision and released on a semantic disposable that trims the pool behind it.</summary>
    private static VeritasMemoryPool<ulong> ModalGadgetBufferPool { get; } = new();

    /// <summary>Measures the Shape K census window without deciding anything: the window CEILINGS are compared against the admitted surface and nothing is composed, constructed or verified, so the census ships identically dark and lit. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The measurement.</returns>
    public static ModalGadgetWindow Measure(ReasoningModule module)
    {
        return Measure(module, default);
    }

    /// <summary>The construction-options overload of the measurement: the options change only the bounds a decision would run under, so the measurement compares the same ceilings under every value and no verdict is formed on this path either.</summary>
    /// <param name="module">The module to measure.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The measurement.</returns>
    public static ModalGadgetWindow Measure(ReasoningModule module, ModalGadgetConstructionOptions options)
    {
        ModalGadgetGround ground = Harvest(module, options);

        return MeasureWindow(ground, options.Bounds);
    }

    /// <summary>Runs the modal-gadget clash face: the monotone composition closure over the told axioms and its own derived membership set. The face returns <see langword="false"/> or silence only — never a consistency certificate.</summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the reached clash with its measurement, or silence.</returns>
    public static ModalGadgetClashOutcome RunClash(ReasoningModule module)
    {
        return RunClash(module, default);
    }

    /// <summary>The construction-options overload of the clash decision. No option varies this face's rules: the clash face has no verification pass, so a rule variation would put a wrong-clash code path into the shipped decider, and the entry selection can only silence the face, never change what it derives.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The outcome: the reached clash with its measurement, or silence.</returns>
    public static ModalGadgetClashOutcome RunClash(ReasoningModule module, ModalGadgetConstructionOptions options)
    {
        if(options.Entry is ModalGadgetEntry.MeasureOnly or ModalGadgetEntry.CertifyOnly)
        {
            return ModalGadgetClashOutcome.SilentWith(ModalGadgetWindow.Empty);
        }

        return Compose(HarvestClash(module), options.Bounds);
    }

    /// <summary>Runs the modal-gadget certify face in jurisdiction order: the whole-module admission and the definition-graph resolution first, then the admission-static window ceilings, then defined-atom elimination, the static signature measurement, told unit propagation, the minimal-modal-first vector walk, and the verification pass that is the face's sole soundness carrier. The face returns <see langword="true"/> or silence only — never a refutation.</summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the verified certificate with its measurement, or silence.</returns>
    public static ModalGadgetCertifyOutcome RunCertify(ReasoningModule module)
    {
        return RunCertify(module, default);
    }

    /// <summary>The construction-options overload of the certify decision: the three variation points reach the construction only through this entry point, the production reasoner path passing none.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The outcome: the verified certificate with its measurement, or silence.</returns>
    public static ModalGadgetCertifyOutcome RunCertify(ReasoningModule module, ModalGadgetConstructionOptions options)
    {
        ModalGadgetGround ground = Harvest(module, options);
        ModalGadgetWindow window = MeasureWindow(ground, options.Bounds);
        if(options.Entry is ModalGadgetEntry.MeasureOnly or ModalGadgetEntry.ClashOnly)
        {
            return ModalGadgetCertifyOutcome.SilentWith(window);
        }

        if(ground.Silenced || window.WindowSilences > 0)
        {
            return ModalGadgetCertifyOutcome.SilentWith(window);
        }

        return Construct(ground, options, window);
    }

    /// <summary>
    /// The admission-static window comparison: the quantities a module carries
    /// before any construction runs are compared against their ceilings, and a
    /// module past one of them is SILENT with the counter charged. The split is
    /// enumerated rather than left to judgement — the logical axiom count, the
    /// named class count, the told individuals against the arena, and the raw
    /// modal atoms at one node are admission-static; the surviving free atoms and
    /// the computed signature set are charged AFTER the phases that produce them
    /// and NEVER against a raw count here, and the remaining five are purely
    /// dynamic.
    /// </summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="bounds">The effective bounds.</param>
    /// <returns>The measured window.</returns>
    private static ModalGadgetWindow MeasureWindow(ModalGadgetGround ground, ModalGadgetBounds bounds)
    {
        bool exceeded = ground.LogicalAxioms > bounds.Axiom
            || ground.Classes.Count > bounds.Label
            || ground.Individuals.Count > bounds.Node
            || ground.PeakRawModalAtoms > bounds.ModalAtom;

        return new ModalGadgetWindow(0, ground.SignatureCount, 0, exceeded ? 1 : 0);
    }

    /// <summary>
    /// The Shape K certify face's harvested surface: the admitted classes,
    /// gadget properties and told individuals, the single modal role, the class
    /// definition table the construction evaluates in topological order, the told
    /// class assertions and boxes, the static signature measurement, and the
    /// admission verdict. A silenced ground carries whatever it had collected
    /// when the gate tripped, so the census still reports the module's static
    /// quantities.
    /// </summary>
    private sealed class ModalGadgetGround
    {
        /// <summary>Whether an admission gate silenced the module whole.</summary>
        public bool Silenced { get; set; }

        /// <summary>The module's logical axiom count — non-logical declarations and annotations excluded.</summary>
        public int LogicalAxioms { get; set; }

        /// <summary>The named classes the admitted surface mentions, interned in first-mention order.</summary>
        public List<Utf8String> Classes { get; } = [];

        /// <summary>The interning index over <see cref="Classes"/>.</summary>
        public Dictionary<Utf8String, int> ClassIndex { get; } = [];

        /// <summary>The properties some admitted cardinality restriction bounds — the gadget layer, interned in first-mention order.</summary>
        public List<Utf8String> Properties { get; } = [];

        /// <summary>The interning index over <see cref="Properties"/>.</summary>
        public Dictionary<Utf8String, int> PropertyIndex { get; } = [];

        /// <summary>Whether each gadget property is object-side; a property occurring on both sides is a kind ambiguity and silences the module.</summary>
        public List<bool> PropertyIsObject { get; } = [];

        /// <summary>Whether each gadget property's kind has been fixed by an occurrence.</summary>
        public List<bool> PropertyKindFixed { get; } = [];

        /// <summary>The single role standing in existential or universal position; <see langword="null"/> when the module carries no modal restriction.</summary>
        public NamedNode? ModalRole { get; set; }

        /// <summary>The class definition table, one entry per interned class.</summary>
        public List<ModalGadgetClassDefinition> Definitions { get; } = [];

        /// <summary>The told individual terms, interned in first-mention order by IRI or anonymous label.</summary>
        public List<Utf8String> Individuals { get; } = [];

        /// <summary>The interning index over <see cref="Individuals"/>.</summary>
        public Dictionary<Utf8String, int> IndividualIndex { get; } = [];

        /// <summary>The told named class assertions as (individual, class) pairs.</summary>
        public List<ModalGadgetAssertion> ToldTypes { get; } = [];

        /// <summary>The told universal class assertions as (individual, filler class) pairs — the told boxes.</summary>
        public List<ModalGadgetAssertion> ToldBoxes { get; } = [];

        /// <summary>The told individuals asserted into <c>owl:Thing</c> — carrier-only admissions: each assertion contributes its individual to the domain and NOTHING else, no class-table intern, no label-bound charge, no free bit the walk could enumerate. The verification pass re-evaluates every one of them against the built-in's semantics-fixed extension, the whole domain.</summary>
        public List<int> ThingAssertions { get; } = [];

        /// <summary>The classes whose definition is an existential — one raw modal atom each, indexed by the definition's own <see cref="ModalGadgetClassDefinition.ModalAtom"/>.</summary>
        public List<int> ExistentialClasses { get; } = [];

        /// <summary>The gadget properties that survive defined-atom elimination.</summary>
        public List<int> FreeProperties { get; } = [];

        /// <summary>The definer class of each gadget property, or <c>-1</c> where the property is free.</summary>
        public List<int> PropertyDefiner { get; } = [];

        /// <summary>Whether each gadget property's definer sits on its zero-bound polarity class, so the property's bit is the definer's negation.</summary>
        public List<bool> PropertyDefinerInverted { get; } = [];

        /// <summary>The topological evaluation order over the classes; empty where the definition graph carries a cycle.</summary>
        public List<int> EvaluationOrder { get; } = [];

        /// <summary>The signature group of each raw modal atom, indexing the deduped successor demands.</summary>
        public List<int> AtomSignature { get; } = [];

        /// <summary>The forced-true free-atom mask of each signature group.</summary>
        public List<ulong> SignatureTrue { get; } = [];

        /// <summary>The forced-false free-atom mask of each signature group.</summary>
        public List<ulong> SignatureFalse { get; } = [];

        /// <summary>Whether each signature group is non-propositional, in which case no successor free vector can be solved for it and the verification pass decides.</summary>
        public List<bool> SignatureOpaque { get; } = [];

        /// <summary>The deduped successor demands — the static signature measurement.</summary>
        public int SignatureCount { get; set; }

        /// <summary>The largest raw modal-atom count at one node: the module's existential occurrences beside the told boxes standing at that node.</summary>
        public int PeakRawModalAtoms { get; set; }

        /// <summary>The free-vector bit position of each gadget property, or <c>-1</c> where the property is defined and never enumerated.</summary>
        public List<int> FreeSlot { get; } = [];

        /// <summary>The forced-true free-atom mask of each told box's filler.</summary>
        public List<ulong> BoxTrue { get; } = [];

        /// <summary>The forced-false free-atom mask of each told box's filler.</summary>
        public List<ulong> BoxFalse { get; } = [];

        /// <summary>Whether each told box's filler is non-propositional, in which case no successor free vector can be solved for it and the verification pass decides.</summary>
        public List<bool> BoxOpaque { get; } = [];
    }

    /// <summary>One step of the signature walk: a class and the truth value the walk requires of it.</summary>
    /// <param name="ClassIndex">The class index.</param>
    /// <param name="WantTrue">Whether the walk requires the class to hold.</param>
    private readonly record struct ModalGadgetSignatureStep(int ClassIndex, bool WantTrue);

    /// <summary>One told class assertion reduced to interned indices.</summary>
    /// <param name="Individual">The told individual's index.</param>
    /// <param name="Class">The asserted class's index, or the universal's filler class for a told box.</param>
    private readonly record struct ModalGadgetAssertion(int Individual, int Class);

    /// <summary>Which polarity side of its gadget property a cardinality-defined class stands on.</summary>
    private enum ModalGadgetPolarity
    {
        /// <summary>The minimum side: the class holds exactly where the property's extension is non-empty.</summary>
        MinOne = 0,

        /// <summary>The zero side: the class holds exactly where the property's extension is empty.</summary>
        Zero = 1,
    }

    /// <summary>Which further definition a class carries beside its cardinality one.</summary>
    private enum ModalGadgetDefinitionKind
    {
        /// <summary>No further definition.</summary>
        None = 0,

        /// <summary>A binary intersection of two named classes.</summary>
        Intersection = 1,

        /// <summary>An existential over the modal role into a named class.</summary>
        Existential = 2,
    }

    /// <summary>
    /// One class's admitted definitions. A class may carry a cardinality
    /// definition, a further definition, both, or neither; a SECOND definition of
    /// either kind silences the module, because two functional dependencies for
    /// one value leave the construction choosing between them, and choosing is
    /// guessing.
    /// </summary>
    private sealed class ModalGadgetClassDefinition
    {
        /// <summary>The gadget property this class bounds, or <c>-1</c> where it carries no cardinality definition.</summary>
        public int CardinalityProperty { get; set; } = -1;

        /// <summary>Which polarity side of that property the class stands on.</summary>
        public ModalGadgetPolarity Polarity { get; set; }

        /// <summary>The told bound's flavour, kept so the verification pass checks the axiom the module wrote rather than the polarity the construction read.</summary>
        public OwlCardinalityKind BoundKind { get; set; }

        /// <summary>The told bound's value — zero or one.</summary>
        public int Bound { get; set; }

        /// <summary>Which further definition the class carries.</summary>
        public ModalGadgetDefinitionKind OtherKind { get; set; }

        /// <summary>The intersection's operand class indices, all of them named; empty where the class carries no intersection definition.</summary>
        public List<int> Operands { get; } = [];

        /// <summary>The existential's filler class index, or <c>-1</c>.</summary>
        public int ExistentialFiller { get; set; } = -1;

        /// <summary>The raw modal atom this class's existential definition owns, or <c>-1</c>.</summary>
        public int ModalAtom { get; set; } = -1;
    }

    /// <summary>
    /// The Shape K clash face's harvested surface: the interned classes and told
    /// individuals, the composition axioms the two composition rules consume, the
    /// told named-class seeds, the told complement denials the clash form pairs
    /// against, and the told bottom membership. NOTHING here reads a cardinality
    /// bound, a role, a successor or a constructed structure, and that exhaustive
    /// exclusion is exactly what licenses the face to ignore every axiom it did
    /// not recognize.
    /// </summary>
    private sealed class ModalGadgetClashGround
    {
        /// <summary>The named classes the told axioms mention, interned in first-mention order.</summary>
        public List<Utf8String> Classes { get; } = [];

        /// <summary>The interning index over <see cref="Classes"/>.</summary>
        public Dictionary<Utf8String, int> ClassIndex { get; } = [];

        /// <summary>The told individual terms, interned by IRI or anonymous label.</summary>
        public List<Utf8String> Individuals { get; } = [];

        /// <summary>The interning index over <see cref="Individuals"/>.</summary>
        public Dictionary<Utf8String, int> IndividualIndex { get; } = [];

        /// <summary>The composition axioms: a named class equated with an intersection every operand of which is a named class reference.</summary>
        public List<ModalGadgetComposition> Compositions { get; } = [];

        /// <summary>The composition indices each class occurs in as an OPERAND — the backward rule's trigger index.</summary>
        public Dictionary<int, List<int>> OperandOccurrences { get; } = [];

        /// <summary>The composition indices each class occurs in as the NAME — the forward rule's trigger index.</summary>
        public Dictionary<int, List<int>> NameOccurrences { get; } = [];

        /// <summary>The told class assertions whose asserted class is a named class IRI — the closure's seeds.</summary>
        public List<ModalGadgetAssertion> Seeds { get; } = [];

        /// <summary>The told complement class assertions, keyed by individual and class — the clash form's told half, READ and never derived.</summary>
        public HashSet<long> Denials { get; } = [];

        /// <summary>Whether some individual carries a told complement of <c>owl:Thing</c>.</summary>
        public bool ToldBottom { get; set; }
    }

    /// <summary>One composition axiom reduced to interned indices: a named class equated with an intersection of named classes.</summary>
    private sealed class ModalGadgetComposition
    {
        /// <summary>The NAME side's class index — the operand that is a class IRI, chosen by CONSTRUCT and never by argument position.</summary>
        public int Name { get; init; }

        /// <summary>The intersection's operand class indices, all of them named.</summary>
        public List<int> Operands { get; init; } = [];
    }

    /// <summary>
    /// Harvests the clash face's told surface. The axiom set the two composition
    /// rules may use is exactly the equivalences pairing a class-IRI name side
    /// with an intersection ALL of whose operands are class-IRI references: an
    /// axiom with one non-named operand is unusable in WHOLE and never in part,
    /// because the equality the rule's proof consumes is over ALL operands and a
    /// derivation from a subset of them is not an instance of that proof at all.
    /// An equivalence whose two sides are both class IRIs engages no composition,
    /// and one with no class-IRI side drops whole. Every other axiom in the module
    /// is IGNORED — not approximated, not partially read, not silenced on.
    /// </summary>
    /// <param name="module">The module to harvest.</param>
    /// <returns>The harvested told surface.</returns>
    private static ModalGadgetClashGround HarvestClash(ReasoningModule module)
    {
        ModalGadgetClashGround ground = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    HarvestComposition(ground, equivalent);
                    break;
                }
                case(OwlClassAssertionAxiom assertion):
                {
                    HarvestClashAssertion(ground, assertion);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return ground;
    }

    /// <summary>Reads one told equivalence into the composition set where its shape is the composition one, identifying the name side by construct.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="equivalent">The told equivalence.</param>
    private static void HarvestComposition(ModalGadgetClashGround ground, OwlEquivalentClassesAxiom equivalent)
    {
        if(equivalent.First is OwlClassReference firstName && IsNamedIntersection(equivalent.Second))
        {
            AddComposition(ground, firstName.Class.Iri, (OwlObjectIntersectionOf)equivalent.Second);

            return;
        }

        if(equivalent.Second is OwlClassReference secondName && IsNamedIntersection(equivalent.First))
        {
            AddComposition(ground, secondName.Class.Iri, (OwlObjectIntersectionOf)equivalent.First);
        }
    }

    /// <summary>Whether one class expression is a non-empty intersection ALL of whose operands are named-class references — the only definition side the composition rules may read.</summary>
    /// <param name="expression">The candidate definition side.</param>
    /// <returns><see langword="true"/> on the named-only intersection.</returns>
    private static bool IsNamedIntersection(OwlClassExpression expression)
    {
        if(expression is not OwlObjectIntersectionOf intersection || intersection.Operands.Count == 0)
        {
            return false;
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(intersection.Operands[i] is not OwlClassReference)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Interns one composition axiom and indexes it under its name side and under each of its operands.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="name">The name side's class IRI.</param>
    /// <param name="intersection">The definition side.</param>
    private static void AddComposition(ModalGadgetClashGround ground, Utf8String name, OwlObjectIntersectionOf intersection)
    {
        int nameIndex = InternClashClass(ground, name);
        List<int> operands = [];
        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            operands.Add(InternClashClass(ground, ((OwlClassReference)intersection.Operands[i]).Class.Iri));
        }

        ModalGadgetComposition composition = new() { Name = nameIndex, Operands = operands };
        int compositionIndex = ground.Compositions.Count;
        ground.Compositions.Add(composition);
        AppendOccurrence(ground.NameOccurrences, nameIndex, compositionIndex);
        for(int i = 0; i < operands.Count; i++)
        {
            AppendOccurrence(ground.OperandOccurrences, operands[i], compositionIndex);
        }
    }

    /// <summary>Appends one composition index to a class's trigger list, creating the list on first occurrence.</summary>
    /// <param name="occurrences">The trigger index.</param>
    /// <param name="classIndex">The class the trigger is keyed on.</param>
    /// <param name="compositionIndex">The composition index to append.</param>
    private static void AppendOccurrence(Dictionary<int, List<int>> occurrences, int classIndex, int compositionIndex)
    {
        if(!occurrences.TryGetValue(classIndex, out List<int>? bucket))
        {
            bucket = [];
            occurrences[classIndex] = bucket;
        }

        bucket.Add(compositionIndex);
    }

    /// <summary>Reads one told class assertion into the seeds, the denials, or the told bottom. The condition is on the ASSERTED CLASS and never on the individual: a named class seeds the closure, a complement of a named class is READ as a denial rather than derived, a complement of <c>owl:Thing</c> is the told bottom, and an assertion of any other anonymous expression is ignored.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="assertion">The told class assertion.</param>
    private static void HarvestClashAssertion(ModalGadgetClashGround ground, OwlClassAssertionAxiom assertion)
    {
        if(TermKey(assertion.Individual) is not Utf8String individualKey)
        {
            return;
        }

        int individual = InternClashIndividual(ground, individualKey);
        switch(assertion.Class)
        {
            case(OwlClassReference named):
            {
                ground.Seeds.Add(new ModalGadgetAssertion(individual, InternClashClass(ground, named.Class.Iri)));
                break;
            }
            case(OwlObjectComplementOf { Operand: OwlClassReference denied }):
            {
                if(denied.Class.Iri.Equals(OwlVocabulary.Thing))
                {
                    ground.ToldBottom = true;
                }
                else
                {
                    ground.Denials.Add(FactKey(individual, InternClashClass(ground, denied.Class.Iri)));
                }

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>The interning key of one told individual term: its IRI when named, its anonymous label when blank, and nothing for any other term shape.</summary>
    /// <param name="term">The told term standing in individual position.</param>
    /// <returns>The interning key, or <see langword="null"/> where the term names no individual.</returns>
    private static Utf8String? TermKey(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => named.Iri,
            BlankNode anonymous => anonymous.Label,
            _ => null,
        };
    }

    /// <summary>Interns one class IRI into the clash face's class table.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="iri">The class IRI.</param>
    /// <returns>The interned index.</returns>
    private static int InternClashClass(ModalGadgetClashGround ground, Utf8String iri)
    {
        if(ground.ClassIndex.TryGetValue(iri, out int existing))
        {
            return existing;
        }

        int index = ground.Classes.Count;
        ground.Classes.Add(iri);
        ground.ClassIndex[iri] = index;

        return index;
    }

    /// <summary>Interns one individual key into the clash face's individual table.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="key">The individual's interning key.</param>
    /// <returns>The interned index.</returns>
    private static int InternClashIndividual(ModalGadgetClashGround ground, Utf8String key)
    {
        if(ground.IndividualIndex.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int index = ground.Individuals.Count;
        ground.Individuals.Add(key);
        ground.IndividualIndex[key] = index;

        return index;
    }

    /// <summary>The packed key of one membership fact, used by the denial set and the closure's dedupe.</summary>
    /// <param name="individual">The individual index.</param>
    /// <param name="classIndex">The class index.</param>
    /// <returns>The packed key.</returns>
    private static long FactKey(int individual, int classIndex)
    {
        return ((long)individual << 32) | (uint)classIndex;
    }

    /// <summary>
    /// The clash face's monotone fixpoint: a worklist over
    /// <c>(individual, class)</c> membership facts with label-set dedupe, so a
    /// fact is derived at most once per individual and the closure is the unique
    /// least fixpoint of a monotone deduped rule set — which is what makes the
    /// decision and the step count properties of the module rather than of a
    /// traversal order. The seeds are the told named class assertions; the
    /// backward rule composes a name from ALL of its operands and the forward rule
    /// decomposes a name into every operand; the two clash forms are checked after
    /// every derived fact and the face stops at the first one. Nothing recurses:
    /// the worklist is explicit and the trigger index is precomputed.
    /// </summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <param name="bounds">The effective bounds; only the step ceiling governs this face.</param>
    /// <returns>The outcome: the reached clash with its measurement, or silence.</returns>
    private static ModalGadgetClashOutcome Compose(ModalGadgetClashGround ground, ModalGadgetBounds bounds)
    {
        if(ground.ToldBottom)
        {
            return new ModalGadgetClashOutcome(false, ModalGadgetWindow.Empty, ModalGadgetClashReasons.AssertedBottomMembership);
        }

        if(ground.Classes.Count == 0 || ground.Individuals.Count == 0)
        {
            return ModalGadgetClashOutcome.SilentWith(ModalGadgetWindow.Empty);
        }

        HashSet<long> derived = [];
        Queue<long> work = new();
        for(int i = 0; i < ground.Seeds.Count; i++)
        {
            long seed = FactKey(ground.Seeds[i].Individual, ground.Seeds[i].Class);
            if(derived.Add(seed))
            {
                if(ReadsClash(ground, ground.Seeds[i].Individual, ground.Seeds[i].Class) is string seedReason)
                {
                    return new ModalGadgetClashOutcome(false, ModalGadgetWindow.Empty, seedReason);
                }

                work.Enqueue(seed);
            }
        }

        int applications = 0;
        while(work.Count > 0)
        {
            long fact = work.Dequeue();
            int individual = (int)(fact >> 32);
            int classIndex = (int)(uint)fact;
            if(ground.NameOccurrences.TryGetValue(classIndex, out List<int>? asName))
            {
                for(int i = 0; i < asName.Count; i++)
                {
                    ModalGadgetComposition composition = ground.Compositions[asName[i]];
                    for(int operand = 0; operand < composition.Operands.Count; operand++)
                    {
                        if(EnqueueDerived(ground, derived, work, individual, composition.Operands[operand], ref applications) is string forwardReason)
                        {
                            return new ModalGadgetClashOutcome(false, ModalGadgetWindow.Empty, forwardReason);
                        }

                        if(applications > bounds.Step)
                        {
                            return ModalGadgetClashOutcome.SilentWith(new ModalGadgetWindow(0, 0, 0, 1));
                        }
                    }
                }
            }

            if(ground.OperandOccurrences.TryGetValue(classIndex, out List<int>? asOperand))
            {
                for(int i = 0; i < asOperand.Count; i++)
                {
                    ModalGadgetComposition composition = ground.Compositions[asOperand[i]];
                    if(!DerivesEveryOperand(derived, composition, individual))
                    {
                        continue;
                    }

                    if(EnqueueDerived(ground, derived, work, individual, composition.Name, ref applications) is string backwardReason)
                    {
                        return new ModalGadgetClashOutcome(false, ModalGadgetWindow.Empty, backwardReason);
                    }

                    if(applications > bounds.Step)
                    {
                        return ModalGadgetClashOutcome.SilentWith(new ModalGadgetWindow(0, 0, 0, 1));
                    }
                }
            }
        }

        return ModalGadgetClashOutcome.SilentWith(ModalGadgetWindow.Empty);
    }

    /// <summary>Whether every operand of one composition is already derived at the individual. A subset licenses nothing: the equality the rule stands on is over ALL operands.</summary>
    /// <param name="derived">The closure's fact set.</param>
    /// <param name="composition">The composition axiom.</param>
    /// <param name="individual">The individual index.</param>
    /// <returns><see langword="true"/> when every operand holds at the individual.</returns>
    private static bool DerivesEveryOperand(HashSet<long> derived, ModalGadgetComposition composition, int individual)
    {
        for(int i = 0; i < composition.Operands.Count; i++)
        {
            if(!derived.Contains(FactKey(individual, composition.Operands[i])))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Derives one membership fact where it is new, charges one rule application for it, and checks the two clash forms against it.</summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <param name="derived">The closure's fact set.</param>
    /// <param name="work">The closure's worklist.</param>
    /// <param name="individual">The individual index.</param>
    /// <param name="classIndex">The derived class index.</param>
    /// <param name="applications">The charged rule applications.</param>
    /// <returns>The named clash reason where the new fact clashes; <see langword="null"/> otherwise.</returns>
    private static string? EnqueueDerived(ModalGadgetClashGround ground, HashSet<long> derived, Queue<long> work, int individual, int classIndex, ref int applications)
    {
        long fact = FactKey(individual, classIndex);
        if(!derived.Add(fact))
        {
            return null;
        }

        applications++;
        if(ReadsClash(ground, individual, classIndex) is string reason)
        {
            return reason;
        }

        work.Enqueue(fact);

        return null;
    }

    /// <summary>The two clash forms, checked against one membership fact: a told complement of the very class derived at the very individual, and a membership in the empty class. No other clash form exists on this face, and none of them reads a cardinality bound, a successor count, a role, or any property of a constructed structure.</summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <param name="individual">The individual index.</param>
    /// <param name="classIndex">The class index.</param>
    /// <returns>The named clash reason, or <see langword="null"/> where the fact is clash-free.</returns>
    private static string? ReadsClash(ModalGadgetClashGround ground, int individual, int classIndex)
    {
        if(ground.Classes[classIndex].Equals(OwlVocabulary.Nothing))
        {
            return ModalGadgetClashReasons.AssertedBottomMembership;
        }

        return ground.Denials.Contains(FactKey(individual, classIndex))
            ? ModalGadgetClashReasons.ComplementedMembership(ground.Classes[classIndex])
            : null;
    }

    /// <summary>
    /// Harvests the certify face's admitted surface under the whole-module
    /// ALL-OR-NOTHING posture: exactly five axiom kinds are admitted and anything
    /// else silences the module WHOLE, because a certify face that ignores an
    /// axiom certifies a structure that axiom may falsify. The clash face's
    /// monotone licence transfers to nothing here — adding axioms can never break
    /// a clash but can always break a model.
    /// </summary>
    /// <param name="module">The module to admit.</param>
    /// <param name="options">The construction options; only the elimination switch reaches this phase.</param>
    /// <returns>The harvested admitted surface, silenced where a gate tripped.</returns>
    private static ModalGadgetGround Harvest(ReasoningModule module, ModalGadgetConstructionOptions options)
    {
        ModalGadgetGround ground = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            Admit(ground, axiom);
            if(ground.Silenced)
            {
                return ground;
            }
        }

        if(ground.ModalRole is NamedNode modalRole && ground.PropertyIndex.ContainsKey(modalRole.Iri))
        {
            ground.Silenced = true;

            return ground;
        }

        ResolveDefinitions(ground, options.Construction);
        if(ground.Silenced)
        {
            return ground;
        }

        ComputeSignatures(ground);
        ComputePeakRawModalAtoms(ground);

        return ground;
    }

    /// <summary>Applies the axiom-kind allow-list to one axiom. Declarations and annotations are non-logical passthrough; an <c>owl:imports</c> row is NOT, and it REJECTS: a module importing an ontology whose axioms are not in the module handed in would be constructed and verified against the axioms this face can see, and consistent would be emitted for a module whose imported closure may have no model.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="axiom">The axiom.</param>
    private static void Admit(ModalGadgetGround ground, OwlAxiom axiom)
    {
        switch(axiom)
        {
            case(OwlEquivalentClassesAxiom equivalent):
            {
                ground.LogicalAxioms++;
                AdmitEquivalence(ground, equivalent);
                break;
            }
            case(OwlClassAssertionAxiom assertion):
            {
                ground.LogicalAxioms++;
                AdmitAssertion(ground, assertion);
                break;
            }
            case(OwlDeclarationAxiom or OwlAnnotationAssertionAxiom or OwlSubAnnotationPropertyOfAxiom
                or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom):
            {
                break;
            }
            default:
            {
                ground.Silenced = true;
                break;
            }
        }
    }

    /// <summary>
    /// Admits one told equivalence into the class definition table. The NAME side
    /// is the operand that is a class IRI, chosen by CONSTRUCT and never by
    /// argument position, since the abstract syntax is unordered; two named sides
    /// and no named side both silence, since neither matches an admitted shape.
    /// The two BUILT-IN classes are neither defined nor free — their extensions are
    /// fixed by the semantics and not by the module — so a module treating either
    /// as an ordinary atomic class silences. The unqualified-count spelling
    /// <c>owl:Thing</c> inside a cardinality restriction is the absence of a
    /// qualification rather than an atomic occurrence, and does not trip that gate.
    /// </summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="equivalent">The told equivalence.</param>
    private static void AdmitEquivalence(ModalGadgetGround ground, OwlEquivalentClassesAxiom equivalent)
    {
        OwlClassExpression definition;
        Utf8String name;
        if(equivalent.First is OwlClassReference firstName && equivalent.Second is not OwlClassReference)
        {
            name = firstName.Class.Iri;
            definition = equivalent.Second;
        }
        else if(equivalent.Second is OwlClassReference secondName && equivalent.First is not OwlClassReference)
        {
            name = secondName.Class.Iri;
            definition = equivalent.First;
        }
        else
        {
            ground.Silenced = true;

            return;
        }

        if(IsBuiltInClass(name))
        {
            ground.Silenced = true;

            return;
        }

        ModalGadgetClassDefinition entry = DefinitionOf(ground, InternClass(ground, name));
        switch(definition)
        {
            case(OwlObjectIntersectionOf intersection):
            {
                AdmitIntersection(ground, entry, intersection);
                break;
            }
            case(OwlObjectCardinality { Property: OwlObjectPropertyReference boundedRole } cardinality) when IsUnqualifiedCount(cardinality.Filler):
            {
                AdmitCardinality(ground, entry, boundedRole.Named.Iri, isObject: true, cardinality.Kind, cardinality.Cardinality);
                break;
            }
            case(OwlDataCardinality { Range: null } dataCardinality):
            {
                AdmitCardinality(ground, entry, dataCardinality.Property.Iri, isObject: false, dataCardinality.Kind, dataCardinality.Cardinality);
                break;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole, Filler: OwlClassReference filler }):
            {
                AdmitExistential(ground, entry, existentialRole.Named, filler.Class.Iri);
                break;
            }
            default:
            {
                ground.Silenced = true;
                break;
            }
        }
    }

    /// <summary>Admits one intersection definition, whose every operand must be a named class other than the two built-ins. A class carrying a SECOND further definition silences: two functional dependencies for one value leave the construction choosing between them, and choosing is guessing.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="entry">The named class's definition entry.</param>
    /// <param name="intersection">The intersection definition side.</param>
    private static void AdmitIntersection(ModalGadgetGround ground, ModalGadgetClassDefinition entry, OwlObjectIntersectionOf intersection)
    {
        if(entry.OtherKind != ModalGadgetDefinitionKind.None || intersection.Operands.Count == 0)
        {
            ground.Silenced = true;

            return;
        }

        for(int i = 0; i < intersection.Operands.Count; i++)
        {
            if(intersection.Operands[i] is not OwlClassReference operand || IsBuiltInClass(operand.Class.Iri))
            {
                ground.Silenced = true;

                return;
            }

            entry.Operands.Add(InternClass(ground, operand.Class.Iri));
        }

        entry.OtherKind = ModalGadgetDefinitionKind.Intersection;
    }

    /// <summary>
    /// Admits one cardinality definition. The bound is read from the PARSED
    /// NUMERIC VALUE and never from a datatype IRI — the structural surface
    /// carries the value alone, so no lexical or datatype gating is reachable
    /// here, and a module spelling its bounds across seven different numeric
    /// datatypes reads identically. Only the four informative
    /// (flavour, value) combinations are admitted; a bound that constrains
    /// nothing silences, since it leaves the polarity pair unidentifiable.
    /// </summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="entry">The named class's definition entry.</param>
    /// <param name="property">The bounded property's IRI.</param>
    /// <param name="isObject">Whether the bound sits on an object property.</param>
    /// <param name="kind">The told bound's flavour.</param>
    /// <param name="bound">The told bound's parsed value.</param>
    private static void AdmitCardinality(ModalGadgetGround ground, ModalGadgetClassDefinition entry, Utf8String property, bool isObject, OwlCardinalityKind kind, int bound)
    {
        if(entry.CardinalityProperty >= 0)
        {
            ground.Silenced = true;

            return;
        }

        ModalGadgetPolarity polarity;
        switch((kind, bound))
        {
            case(OwlCardinalityKind.Min, 1):
            case(OwlCardinalityKind.Exact, 1):
            {
                polarity = ModalGadgetPolarity.MinOne;
                break;
            }
            case(OwlCardinalityKind.Max, 0):
            case(OwlCardinalityKind.Exact, 0):
            {
                polarity = ModalGadgetPolarity.Zero;
                break;
            }
            default:
            {
                ground.Silenced = true;

                return;
            }
        }

        int propertyIndex = InternProperty(ground, property, isObject);
        if(ground.Silenced)
        {
            return;
        }

        entry.CardinalityProperty = propertyIndex;
        entry.Polarity = polarity;
        entry.BoundKind = kind;
        entry.Bound = bound;
    }

    /// <summary>Admits one existential definition over the module's single modal role into a named filler.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="entry">The named class's definition entry.</param>
    /// <param name="role">The existential's role.</param>
    /// <param name="filler">The existential's filler class IRI.</param>
    private static void AdmitExistential(ModalGadgetGround ground, ModalGadgetClassDefinition entry, NamedNode role, Utf8String filler)
    {
        if(entry.OtherKind != ModalGadgetDefinitionKind.None || IsBuiltInClass(filler) || !BindsModalRole(ground, role))
        {
            ground.Silenced = true;

            return;
        }

        entry.OtherKind = ModalGadgetDefinitionKind.Existential;
        entry.ExistentialFiller = InternClass(ground, filler);
    }

    /// <summary>Admits one told class assertion, dispatching on the construct kind FIRST and on the built-in IRI second: a named class other than the two built-ins, the span-exact <c>owl:Thing</c> as a CARRIER-ONLY admission — the assertion contributes its individual to the domain and nothing else, and the verification pass evaluates it against the built-in's whole-domain extension rather than skipping it — or a universal over the single modal role into a named non-built-in class. An asserted <c>owl:Nothing</c> and every other asserted class shape silence the module whole — and the named arm, not the complement ban beside it, is what keeps this face structurally silent on every refutation probe the conformance harness builds.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="assertion">The told class assertion.</param>
    private static void AdmitAssertion(ModalGadgetGround ground, OwlClassAssertionAxiom assertion)
    {
        if(TermKey(assertion.Individual) is not Utf8String individualKey)
        {
            ground.Silenced = true;

            return;
        }

        int individual = InternIndividual(ground, individualKey);
        switch(assertion.Class)
        {
            case(OwlClassReference named) when !IsBuiltInClass(named.Class.Iri):
            {
                ground.ToldTypes.Add(new ModalGadgetAssertion(individual, InternClass(ground, named.Class.Iri)));
                break;
            }
            case(OwlClassReference top) when top.Class.Iri.Equals(OwlVocabulary.Thing):
            {
                ground.ThingAssertions.Add(individual);
                break;
            }
            case(OwlObjectAllValuesFrom { Property: OwlObjectPropertyReference universalRole, Filler: OwlClassReference filler }) when !IsBuiltInClass(filler.Class.Iri) && BindsModalRole(ground, universalRole.Named):
            {
                ground.ToldBoxes.Add(new ModalGadgetAssertion(individual, InternClass(ground, filler.Class.Iri)));
                break;
            }
            default:
            {
                ground.Silenced = true;
                break;
            }
        }
    }

    /// <summary>Whether one class IRI names a built-in whose extension the semantics fixes rather than the module.</summary>
    /// <param name="iri">The class IRI.</param>
    /// <returns><see langword="true"/> for <c>owl:Thing</c> and <c>owl:Nothing</c>.</returns>
    private static bool IsBuiltInClass(Utf8String iri)
    {
        return iri.Equals(OwlVocabulary.Thing) || iri.Equals(OwlVocabulary.Nothing);
    }

    /// <summary>Whether a cardinality restriction's filler leaves the count unqualified: no filler at all, or the explicit <c>owl:Thing</c> — the two spellings of the same unrestricted count.</summary>
    /// <param name="filler">The restriction's qualification filler.</param>
    /// <returns><see langword="true"/> for an unqualified count.</returns>
    private static bool IsUnqualifiedCount(OwlClassExpression? filler)
    {
        return filler is null || (filler is OwlClassReference reference && reference.Class.Iri.Equals(OwlVocabulary.Thing));
    }

    /// <summary>Binds the module's single modal role on the first modal restriction that names one and re-checks it by FULL IRI on every later one. More than one modal role is outside this face's jurisdiction, because the finite-tree result its certificate stands on is stated over a module whose modal layer is one characteristic-free role.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="role">The role the modal restriction quantifies.</param>
    /// <returns><see langword="true"/> when the restriction carries the bound role.</returns>
    private static bool BindsModalRole(ModalGadgetGround ground, NamedNode role)
    {
        if(ground.ModalRole is null)
        {
            ground.ModalRole = role;

            return true;
        }

        return ground.ModalRole.Iri.Equals(role.Iri);
    }

    /// <summary>Interns one class IRI into the certify face's class table, extending the definition table with it.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="iri">The class IRI.</param>
    /// <returns>The interned index.</returns>
    private static int InternClass(ModalGadgetGround ground, Utf8String iri)
    {
        if(ground.ClassIndex.TryGetValue(iri, out int existing))
        {
            return existing;
        }

        int index = ground.Classes.Count;
        ground.Classes.Add(iri);
        ground.ClassIndex[iri] = index;
        ground.Definitions.Add(new ModalGadgetClassDefinition());

        return index;
    }

    /// <summary>The definition entry of one interned class.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="classIndex">The interned class index.</param>
    /// <returns>The definition entry.</returns>
    private static ModalGadgetClassDefinition DefinitionOf(ModalGadgetGround ground, int classIndex)
    {
        return ground.Definitions[classIndex];
    }

    /// <summary>Interns one gadget property, fixing its object-versus-data kind on first occurrence. A property occurring on both sides is a kind ambiguity and silences the module, since its extension would be fixed twice by two mechanisms that do not know about each other.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="iri">The property IRI.</param>
    /// <param name="isObject">Whether this occurrence is object-side.</param>
    /// <returns>The interned index.</returns>
    private static int InternProperty(ModalGadgetGround ground, Utf8String iri, bool isObject)
    {
        if(ground.PropertyIndex.TryGetValue(iri, out int existing))
        {
            if(ground.PropertyKindFixed[existing] && ground.PropertyIsObject[existing] != isObject)
            {
                ground.Silenced = true;
            }

            return existing;
        }

        int index = ground.Properties.Count;
        ground.Properties.Add(iri);
        ground.PropertyIndex[iri] = index;
        ground.PropertyIsObject.Add(isObject);
        ground.PropertyKindFixed.Add(true);
        ground.PropertyDefiner.Add(-1);
        ground.PropertyDefinerInverted.Add(false);

        return index;
    }

    /// <summary>Interns one individual key into the certify face's individual table.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="key">The individual's interning key.</param>
    /// <returns>The interned index.</returns>
    private static int InternIndividual(ModalGadgetGround ground, Utf8String key)
    {
        if(ground.IndividualIndex.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int index = ground.Individuals.Count;
        ground.Individuals.Add(key);
        ground.IndividualIndex[key] = index;

        return index;
    }

    /// <summary>
    /// Resolves the within-node definition graph and runs defined-atom
    /// elimination. Each gadget property carries a POLARITY PAIR of classes, one
    /// equivalent to a minimum bound on it and one to a zero bound; those two
    /// cardinality equivalences are the property's GADGET DEFINITION and are NOT
    /// definers. A DEFINER is a FURTHER definition whose name side is a member of
    /// that pair — and a property carrying two of them, or a pair that cannot be
    /// identified at all, SILENCES rather than defaulting to free. A property with
    /// no definer is FREE and is enumerated; one with a definer is COMPUTED in
    /// topological order and never enumerated. The phase carries NO soundness
    /// weight: the verification pass re-evaluates whatever it produces.
    /// </summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="construction">The construction variations; suppressing elimination leaves every gadget atom free.</param>
    private static void ResolveDefinitions(ModalGadgetGround ground, ModalGadgetConstruction construction)
    {
        int propertyCount = ground.Properties.Count;
        List<int> minSide = [];
        List<int> zeroSide = [];
        for(int p = 0; p < propertyCount; p++)
        {
            minSide.Add(-1);
            zeroSide.Add(-1);
        }

        for(int classIndex = 0; classIndex < ground.Definitions.Count; classIndex++)
        {
            ModalGadgetClassDefinition definition = ground.Definitions[classIndex];
            if(definition.CardinalityProperty < 0)
            {
                continue;
            }

            List<int> side = definition.Polarity == ModalGadgetPolarity.MinOne ? minSide : zeroSide;
            if(side[definition.CardinalityProperty] >= 0)
            {
                ground.Silenced = true;

                return;
            }

            side[definition.CardinalityProperty] = classIndex;
        }

        for(int p = 0; p < propertyCount; p++)
        {
            if(minSide[p] < 0 || zeroSide[p] < 0)
            {
                ground.Silenced = true;

                return;
            }

            bool minDefines = ground.Definitions[minSide[p]].OtherKind != ModalGadgetDefinitionKind.None;
            bool zeroDefines = ground.Definitions[zeroSide[p]].OtherKind != ModalGadgetDefinitionKind.None;
            if(minDefines && zeroDefines)
            {
                ground.Silenced = true;

                return;
            }

            if(construction.SuppressDefinedAtomElimination)
            {
                continue;
            }

            if(minDefines)
            {
                ground.PropertyDefiner[p] = minSide[p];
                ground.PropertyDefinerInverted[p] = false;
            }
            else if(zeroDefines)
            {
                ground.PropertyDefiner[p] = zeroSide[p];
                ground.PropertyDefinerInverted[p] = true;
            }
        }

        for(int p = 0; p < propertyCount; p++)
        {
            ground.FreeSlot.Add(-1);
            if(ground.PropertyDefiner[p] < 0)
            {
                ground.FreeSlot[p] = ground.FreeProperties.Count;
                ground.FreeProperties.Add(p);
            }
        }

        for(int classIndex = 0; classIndex < ground.Definitions.Count; classIndex++)
        {
            if(ground.Definitions[classIndex].OtherKind == ModalGadgetDefinitionKind.Existential)
            {
                ground.Definitions[classIndex].ModalAtom = ground.ExistentialClasses.Count;
                ground.ExistentialClasses.Add(classIndex);
            }
        }

        OrderDefinitions(ground);
    }

    /// <summary>Computes the topological evaluation order over the classes with an explicit Kahn queue: an intersection-defined class follows its operands and a cardinality-defined class whose property is computed follows that property's definer. A cycle has no evaluation order and SILENCES the module.</summary>
    /// <param name="ground">The harvested surface.</param>
    private static void OrderDefinitions(ModalGadgetGround ground)
    {
        int classCount = ground.Classes.Count;
        List<List<int>> dependents = [];
        List<int> indegree = [];
        for(int i = 0; i < classCount; i++)
        {
            dependents.Add([]);
            indegree.Add(0);
        }

        for(int classIndex = 0; classIndex < classCount; classIndex++)
        {
            ModalGadgetClassDefinition definition = ground.Definitions[classIndex];
            if(definition.OtherKind == ModalGadgetDefinitionKind.Intersection)
            {
                for(int i = 0; i < definition.Operands.Count; i++)
                {
                    dependents[definition.Operands[i]].Add(classIndex);
                    indegree[classIndex]++;
                }
            }
            else if(definition.OtherKind == ModalGadgetDefinitionKind.None && definition.CardinalityProperty >= 0 && ground.PropertyDefiner[definition.CardinalityProperty] >= 0)
            {
                dependents[ground.PropertyDefiner[definition.CardinalityProperty]].Add(classIndex);
                indegree[classIndex]++;
            }
        }

        Queue<int> ready = new();
        for(int i = 0; i < classCount; i++)
        {
            if(indegree[i] == 0)
            {
                ready.Enqueue(i);
            }
        }

        while(ready.Count > 0)
        {
            int current = ready.Dequeue();
            ground.EvaluationOrder.Add(current);
            List<int> bucket = dependents[current];
            for(int i = 0; i < bucket.Count; i++)
            {
                indegree[bucket[i]]--;
                if(indegree[bucket[i]] == 0)
                {
                    ready.Enqueue(bucket[i]);
                }
            }
        }

        if(ground.EvaluationOrder.Count != classCount)
        {
            ground.Silenced = true;
        }
    }

    /// <summary>
    /// The STATIC signature measurement, taken once at admission over the
    /// module's existential occurrences and independent of which vector is tried:
    /// a signature is the COMPUTED propositional signature over the free literals,
    /// with each non-propositional filler its own signature. Read instead as a
    /// construction-time count of successors actually demanded it would be zero on
    /// every module whose all-false vector succeeds and it would vary with the
    /// vector, which the order-invariance convention forbids.
    /// </summary>
    /// <param name="ground">The harvested surface.</param>
    private static void ComputeSignatures(ModalGadgetGround ground)
    {
        Dictionary<long, int> groups = [];
        for(int atom = 0; atom < ground.ExistentialClasses.Count; atom++)
        {
            int filler = ground.Definitions[ground.ExistentialClasses[atom]].ExistentialFiller;
            ground.AtomSignature.Add(AssignSignature(ground, groups, filler));
        }

        ground.SignatureCount = ground.SignatureTrue.Count;
        for(int box = 0; box < ground.ToldBoxes.Count; box++)
        {
            bool propositional = TryComputeSignature(ground, ground.ToldBoxes[box].Class, out ulong boxTrue, out ulong boxFalse);
            ground.BoxTrue.Add(propositional ? boxTrue : 0UL);
            ground.BoxFalse.Add(propositional ? boxFalse : 0UL);
            ground.BoxOpaque.Add(!propositional);
        }
    }

    /// <summary>Assigns one existential filler to its signature group, deduplicating propositional signatures by their forced-literal masks and giving each non-propositional filler its own group.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="groups">The propositional signature index.</param>
    /// <param name="filler">The existential's filler class index.</param>
    /// <returns>The signature group index.</returns>
    private static int AssignSignature(ModalGadgetGround ground, Dictionary<long, int> groups, int filler)
    {
        if(!TryComputeSignature(ground, filler, out ulong signatureTrue, out ulong signatureFalse))
        {
            ground.SignatureTrue.Add(0);
            ground.SignatureFalse.Add(0);
            ground.SignatureOpaque.Add(true);

            return ground.SignatureTrue.Count - 1;
        }

        long key = unchecked((long)(signatureTrue * 1000003UL ^ signatureFalse));
        if(groups.TryGetValue(key, out int existing) && ground.SignatureTrue[existing] == signatureTrue && ground.SignatureFalse[existing] == signatureFalse)
        {
            return existing;
        }

        ground.SignatureTrue.Add(signatureTrue);
        ground.SignatureFalse.Add(signatureFalse);
        ground.SignatureOpaque.Add(false);
        groups[key] = ground.SignatureTrue.Count - 1;

        return ground.SignatureTrue.Count - 1;
    }

    /// <summary>Computes one class's propositional signature over the free gadget literals with an explicit stack: an intersection required TRUE contributes both operands, a cardinality-defined class contributes its property's literal directly where the property is free and pushes the property's definer where it is computed, and an existential, a negated compound or an atomic class makes the signature non-propositional.</summary>
    /// <param name="ground">The harvested surface.</param>
    /// <param name="classIndex">The class whose signature is computed.</param>
    /// <param name="signatureTrue">The forced-true free-atom mask.</param>
    /// <param name="signatureFalse">The forced-false free-atom mask.</param>
    /// <returns><see langword="true"/> when the signature is propositional.</returns>
    private static bool TryComputeSignature(ModalGadgetGround ground, int classIndex, out ulong signatureTrue, out ulong signatureFalse)
    {
        signatureTrue = 0;
        signatureFalse = 0;
        Stack<ModalGadgetSignatureStep> work = new();
        work.Push(new ModalGadgetSignatureStep(classIndex, true));
        int guard = (ground.Classes.Count * 4) + 4;
        while(work.Count > 0)
        {
            guard--;
            if(guard < 0)
            {
                return false;
            }

            ModalGadgetSignatureStep step = work.Pop();
            ModalGadgetClassDefinition definition = ground.Definitions[step.ClassIndex];
            if(definition.OtherKind == ModalGadgetDefinitionKind.Intersection)
            {
                if(!step.WantTrue)
                {
                    return false;
                }

                for(int i = 0; i < definition.Operands.Count; i++)
                {
                    work.Push(new ModalGadgetSignatureStep(definition.Operands[i], true));
                }

                continue;
            }

            if(definition.OtherKind == ModalGadgetDefinitionKind.Existential || definition.CardinalityProperty < 0)
            {
                return false;
            }

            bool wantBit = (definition.Polarity == ModalGadgetPolarity.MinOne) == step.WantTrue;
            int slot = ground.FreeSlot[definition.CardinalityProperty];
            if(slot >= 0)
            {
                if(wantBit)
                {
                    signatureTrue |= 1UL << slot;
                }
                else
                {
                    signatureFalse |= 1UL << slot;
                }

                continue;
            }

            int definer = ground.PropertyDefiner[definition.CardinalityProperty];
            bool definerWant = ground.PropertyDefinerInverted[definition.CardinalityProperty] ? !wantBit : wantBit;
            work.Push(new ModalGadgetSignatureStep(definer, definerWant));
        }

        return true;
    }

    /// <summary>Charges the largest RAW modal-atom count at one node: the module's existential occurrences beside the told boxes standing at that node — BOTH quantifier kinds, which is the convention this quantity is counted under.</summary>
    /// <param name="ground">The harvested surface.</param>
    private static void ComputePeakRawModalAtoms(ModalGadgetGround ground)
    {
        List<int> boxes = [];
        for(int i = 0; i < ground.Individuals.Count; i++)
        {
            boxes.Add(0);
        }

        int peak = 0;
        for(int i = 0; i < ground.ToldBoxes.Count; i++)
        {
            boxes[ground.ToldBoxes[i].Individual]++;
            peak = boxes[ground.ToldBoxes[i].Individual] > peak ? boxes[ground.ToldBoxes[i].Individual] : peak;
        }

        ground.PeakRawModalAtoms = ground.ExistentialClasses.Count + peak;
    }

    /// <summary>
    /// One construction's packed working set, rented ONCE per decision and reused
    /// across vectors. The regions are the packed atomic class extensions, the
    /// ROLE-INDEXED edge planes — one plane per role IRI, never one flat relation,
    /// so no evaluation can satisfy a modal existential across a gadget self-loop
    /// — the per-node data-property filler bits, the per-node gadget bit table,
    /// and the per-node free and modal vectors. The whole set is CLEARED at the
    /// head of every vector rather than re-rented, so no vector's structure can
    /// leak into another's verification pass and the no-per-node-allocation
    /// discipline is untouched.
    /// The gadget bit table is the construction's own state: the materialisation
    /// phase reads it to fix the raw relations, and the verification pass NEVER
    /// touches it.
    /// </summary>
    private sealed class ModalGadgetBuffers: IDisposable
    {
        /// <summary>The single rental backing every region, supplied by the reservation factory that is this type's only construction path.</summary>
        private IMemoryOwner<ulong> Owner { get; init; } = default!;

        /// <summary>Whether the rental has already been returned.</summary>
        private bool Released { get; set; }

        /// <summary>The node arena's capacity — told individuals and spawned successors together.</summary>
        public int NodeCapacity { get; init; }

        /// <summary>The words one node's class-label row spans.</summary>
        public int LabelWords { get; init; }

        /// <summary>The words one edge row spans.</summary>
        public int NodeWords { get; init; }

        /// <summary>The words one node's property row spans.</summary>
        public int PropertyWords { get; init; }

        /// <summary>The word offset of the class-label table.</summary>
        private int LabelOffset { get; init; }

        /// <summary>The word offset of the role-indexed edge planes.</summary>
        private int EdgeOffset { get; init; }

        /// <summary>The word offset of the data-filler bits.</summary>
        private int DataOffset { get; init; }

        /// <summary>The word offset of the gadget bit table.</summary>
        private int GadgetOffset { get; init; }

        /// <summary>The word offset of the per-node free vectors.</summary>
        private int FreeOffset { get; init; }

        /// <summary>The word offset of the per-node modal vectors.</summary>
        private int ModalOffset { get; init; }

        /// <summary>The words the class-label table spans.</summary>
        private int LabelRegionWords { get; init; }

        /// <summary>The words the edge planes span.</summary>
        private int EdgeRegionWords { get; init; }

        /// <summary>The words one per-node property region spans.</summary>
        private int PropertyRegionWords { get; init; }

        /// <summary>The atomic class extensions, indexed node-major then class.</summary>
        public Span<ulong> Labels => Owner.Memory.Span.Slice(LabelOffset, LabelRegionWords);

        /// <summary>The edge relation, indexed role-major then source then target.</summary>
        public Span<ulong> Edges => Owner.Memory.Span.Slice(EdgeOffset, EdgeRegionWords);

        /// <summary>The data-property filler bits, indexed node-major then property.</summary>
        public Span<ulong> Data => Owner.Memory.Span.Slice(DataOffset, PropertyRegionWords);

        /// <summary>The construction's gadget bit table, indexed node-major then property; read by the materialisation phase and by nothing else.</summary>
        public Span<ulong> Gadget => Owner.Memory.Span.Slice(GadgetOffset, PropertyRegionWords);

        /// <summary>The per-node free vectors, one word each.</summary>
        public Span<ulong> Free => Owner.Memory.Span.Slice(FreeOffset, NodeCapacity);

        /// <summary>The per-node modal vectors, one word each.</summary>
        public Span<ulong> Modal => Owner.Memory.Span.Slice(ModalOffset, NodeCapacity);

        /// <summary>Reserves the whole working set in ONE rental sized from the admitted surface and the effective bounds, zeroing it before any region is read.</summary>
        /// <param name="ground">The harvested admitted surface.</param>
        /// <param name="bounds">The effective bounds, whose node ceiling sizes the arena.</param>
        /// <returns>The reserved working set.</returns>
        public static ModalGadgetBuffers Reserve(ModalGadgetGround ground, ModalGadgetBounds bounds)
        {
            int nodeCapacity = bounds.Node < 1 ? 1 : bounds.Node;
            int classes = ground.Classes.Count == 0 ? 1 : ground.Classes.Count;
            int properties = ground.Properties.Count == 0 ? 1 : ground.Properties.Count;
            int planes = ground.Properties.Count + 1;
            int labelWords = (classes + ModalGadgetWordBits - 1) / ModalGadgetWordBits;
            int nodeWords = (nodeCapacity + ModalGadgetWordBits - 1) / ModalGadgetWordBits;
            int propertyWords = (properties + ModalGadgetWordBits - 1) / ModalGadgetWordBits;
            int labelRegionWords = nodeCapacity * labelWords;
            int edgeRegionWords = planes * nodeCapacity * nodeWords;
            int propertyRegionWords = nodeCapacity * propertyWords;

            int labelOffset = 0;
            int edgeOffset = labelOffset + labelRegionWords;
            int dataOffset = edgeOffset + edgeRegionWords;
            int gadgetOffset = dataOffset + propertyRegionWords;
            int freeOffset = gadgetOffset + propertyRegionWords;
            int modalOffset = freeOffset + nodeCapacity;
            int total = modalOffset + nodeCapacity;

            IMemoryOwner<ulong> owner = ModalGadgetBufferPool.Rent(total == 0 ? 1 : total);
            owner.Memory.Span.Clear();

            return new ModalGadgetBuffers
            {
                Owner = owner,
                Released = false,
                NodeCapacity = nodeCapacity,
                LabelWords = labelWords,
                NodeWords = nodeWords,
                PropertyWords = propertyWords,
                LabelOffset = labelOffset,
                EdgeOffset = edgeOffset,
                DataOffset = dataOffset,
                GadgetOffset = gadgetOffset,
                FreeOffset = freeOffset,
                ModalOffset = modalOffset,
                LabelRegionWords = labelRegionWords,
                EdgeRegionWords = edgeRegionWords,
                PropertyRegionWords = propertyRegionWords,
            };
        }

        /// <summary>Resets the arena to the empty structure at the head of every vector: zero nodes beyond the told frontier, the edge relation empty, and every label, gadget-bit, data-filler and vector cell cleared. The reset is a CLEAR of the rented buffers and never a re-rent.</summary>
        public void Clear()
        {
            Owner.Memory.Span.Clear();
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
            ModalGadgetBufferPool.TrimExcess();
        }
    }

    /// <summary>Whether one bit of a packed row is set.</summary>
    /// <param name="region">The packed region.</param>
    /// <param name="rowOffset">The row's word offset.</param>
    /// <param name="bit">The bit index within the row.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    private static bool ReadsBit(Span<ulong> region, int rowOffset, int bit)
    {
        return (region[rowOffset + (bit / ModalGadgetWordBits)] & (1UL << (bit % ModalGadgetWordBits))) != 0;
    }

    /// <summary>Sets one bit of a packed row.</summary>
    /// <param name="region">The packed region.</param>
    /// <param name="rowOffset">The row's word offset.</param>
    /// <param name="bit">The bit index within the row.</param>
    private static void WritesBit(Span<ulong> region, int rowOffset, int bit)
    {
        region[rowOffset + (bit / ModalGadgetWordBits)] |= 1UL << (bit % ModalGadgetWordBits);
    }

    /// <summary>Whether one node stands in one atomic class's extension.</summary>
    /// <param name="buffers">The working set.</param>
    /// <param name="node">The node index.</param>
    /// <param name="classIndex">The class index.</param>
    /// <returns><see langword="true"/> when the node is in the extension.</returns>
    private static bool ReadsLabel(ModalGadgetBuffers buffers, int node, int classIndex)
    {
        return ReadsBit(buffers.Labels, node * buffers.LabelWords, classIndex);
    }

    /// <summary>Whether one directed edge stands in one role's plane.</summary>
    /// <param name="buffers">The working set.</param>
    /// <param name="plane">The role plane index — zero for the modal role, one plus the property index for a gadget property.</param>
    /// <param name="source">The source node index.</param>
    /// <param name="target">The target node index.</param>
    /// <returns><see langword="true"/> when the edge stands.</returns>
    private static bool ReadsEdge(ModalGadgetBuffers buffers, int plane, int source, int target)
    {
        return ReadsBit(buffers.Edges, ((plane * buffers.NodeCapacity) + source) * buffers.NodeWords, target);
    }

    /// <summary>Materialises one directed edge in one role's plane.</summary>
    /// <param name="buffers">The working set.</param>
    /// <param name="plane">The role plane index.</param>
    /// <param name="source">The source node index.</param>
    /// <param name="target">The target node index.</param>
    private static void WritesEdge(ModalGadgetBuffers buffers, int plane, int source, int target)
    {
        WritesBit(buffers.Edges, ((plane * buffers.NodeCapacity) + source) * buffers.NodeWords, target);
    }

    /// <summary>
    /// The certify face's minimal-modal-first walk: told unit propagation pins
    /// what the told class assertions force, the residue is enumerated, the
    /// all-false modal vector is tried at the head of each free vector, and every
    /// candidate structure is re-verified against its RAW relations. A
    /// VERIFICATION failure advances to the next vector where one remains inside
    /// the windows; a WINDOW TRIP ends the face SILENT immediately and no further
    /// vector is tried. A told unit contradiction is silence and never a verdict:
    /// this face is certify-only and has no path to a refutation.
    /// </summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="options">The construction options.</param>
    /// <param name="staticWindow">The admission-static measurement the census carries.</param>
    /// <returns>The outcome: the verified certificate with its measurement, or silence.</returns>
    private static ModalGadgetCertifyOutcome Construct(ModalGadgetGround ground, ModalGadgetConstructionOptions options, ModalGadgetWindow staticWindow)
    {
        ModalGadgetBounds bounds = options.Bounds;
        int freeCount = ground.FreeProperties.Count;
        int signatureCount = ground.SignatureCount;
        int toldNodes = ground.Individuals.Count == 0 ? 1 : ground.Individuals.Count;
        if(freeCount > bounds.FreeAtom || freeCount >= ModalGadgetWordBits
            || signatureCount > bounds.Signature || signatureCount >= ModalGadgetWordBits
            || ground.ExistentialClasses.Count >= ModalGadgetWordBits
            || toldNodes > bounds.Node)
        {
            return ModalGadgetCertifyOutcome.SilentWith(new ModalGadgetWindow(freeCount, signatureCount, 0, 1));
        }

        List<ulong> pinTrue = [];
        List<ulong> pinFalse = [];
        List<ulong> modalPin = [];
        for(int i = 0; i < toldNodes; i++)
        {
            pinTrue.Add(0);
            pinFalse.Add(0);
            modalPin.Add(0);
        }

        HashSet<long> forcedAtomic = [];
        if(!TryPinTold(ground, pinTrue, pinFalse, modalPin, forcedAtomic))
        {
            return ModalGadgetCertifyOutcome.SilentWith(new ModalGadgetWindow(freeCount, signatureCount, 0, 0));
        }

        ulong freeMask = freeCount == 0 ? 0UL : (1UL << freeCount) - 1;
        ulong pinnedEverywhere = freeMask;
        for(int i = 0; i < toldNodes; i++)
        {
            pinnedEverywhere &= pinTrue[i] | pinFalse[i];
        }

        ulong openMask = freeMask & ~pinnedEverywhere;
        long freeSpace = 1L << BitOperations.PopCount(openMask);
        long signatureSpace = 1L << signatureCount;

        using ModalGadgetBuffers buffers = ModalGadgetBuffers.Reserve(ground, bounds);
        int vectors = 0;
        int passes = 0;
        int nodesBuilt = toldNodes;
        for(long vector = 0; vector < freeSpace; vector++)
        {
            ulong shared = ScattersVector(vector, openMask);
            for(long step = 0; step < signatureSpace; step++)
            {
                long modal = options.Construction.SuppressMinimalModalFirst ? signatureSpace - 1 - step : step;
                vectors++;
                if(vectors > bounds.Vector || passes >= bounds.VerifyPass)
                {
                    return ModalGadgetCertifyOutcome.SilentWith(new ModalGadgetWindow(freeCount, signatureCount, nodesBuilt, 1));
                }

                buffers.Clear();
                Build(ground, buffers, bounds, shared, (ulong)modal, pinTrue, pinFalse, modalPin, forcedAtomic, toldNodes, out int built, out bool tripped);
                nodesBuilt = built;
                if(tripped)
                {
                    return ModalGadgetCertifyOutcome.SilentWith(new ModalGadgetWindow(freeCount, signatureCount, built, 1));
                }

                passes++;
                if(Verifies(ground, buffers, built))
                {
                    return new ModalGadgetCertifyOutcome(true, new ModalGadgetWindow(freeCount, signatureCount, built, staticWindow.WindowSilences));
                }
            }
        }

        return ModalGadgetCertifyOutcome.SilentWith(new ModalGadgetWindow(freeCount, signatureCount, nodesBuilt, 0));
    }

    /// <summary>Scatters one enumeration counter's bits into the free-vector positions the told propagation left open.</summary>
    /// <param name="counter">The enumeration counter.</param>
    /// <param name="openMask">The free-atom positions the told propagation left open.</param>
    /// <returns>The assembled free vector.</returns>
    private static ulong ScattersVector(long counter, ulong openMask)
    {
        ulong assembled = 0;
        ulong remaining = openMask;
        int source = 0;
        while(remaining != 0)
        {
            int position = BitOperations.TrailingZeroCount(remaining);
            remaining &= remaining - 1;
            if(((counter >> source) & 1) != 0)
            {
                assembled |= 1UL << position;
            }

            source++;
        }

        return assembled;
    }

    /// <summary>
    /// Told unit propagation: pins the free bits and modal atoms the told class
    /// assertions force at each told individual. A contradiction discovered here
    /// is SILENCE and never a verdict, because this face is certify-only. The
    /// phase carries no soundness weight either: what it pins the verification
    /// pass re-checks.
    /// </summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="pinTrue">The per-node forced-true free-atom masks.</param>
    /// <param name="pinFalse">The per-node forced-false free-atom masks.</param>
    /// <param name="modalPin">The per-node forced-true modal-atom masks.</param>
    /// <param name="forcedAtomicToAppendTo">The atomic class memberships the told types force, keyed by node and class.</param>
    /// <returns><see langword="true"/> when the told types propagate without contradiction.</returns>
    private static bool TryPinTold(ModalGadgetGround ground, List<ulong> pinTrue, List<ulong> pinFalse, List<ulong> modalPin, HashSet<long> forcedAtomicToAppendTo)
    {
        Stack<ModalGadgetSignatureStep> work = new();
        for(int told = 0; told < ground.ToldTypes.Count; told++)
        {
            int node = ground.ToldTypes[told].Individual;
            work.Push(new ModalGadgetSignatureStep(ground.ToldTypes[told].Class, true));
            int guard = (ground.Classes.Count * 4) + 4;
            while(work.Count > 0)
            {
                guard--;
                if(guard < 0)
                {
                    work.Clear();
                    break;
                }

                ModalGadgetSignatureStep step = work.Pop();
                ModalGadgetClassDefinition definition = ground.Definitions[step.ClassIndex];
                if(definition.OtherKind == ModalGadgetDefinitionKind.Intersection)
                {
                    if(!step.WantTrue)
                    {
                        continue;
                    }

                    for(int i = 0; i < definition.Operands.Count; i++)
                    {
                        work.Push(new ModalGadgetSignatureStep(definition.Operands[i], true));
                    }

                    continue;
                }

                if(definition.OtherKind == ModalGadgetDefinitionKind.Existential)
                {
                    if(step.WantTrue)
                    {
                        modalPin[node] |= 1UL << definition.ModalAtom;
                    }

                    continue;
                }

                if(definition.CardinalityProperty < 0)
                {
                    if(step.WantTrue)
                    {
                        forcedAtomicToAppendTo.Add(FactKey(node, step.ClassIndex));
                    }

                    continue;
                }

                bool wantBit = (definition.Polarity == ModalGadgetPolarity.MinOne) == step.WantTrue;
                int slot = ground.FreeSlot[definition.CardinalityProperty];
                if(slot >= 0)
                {
                    if(wantBit)
                    {
                        pinTrue[node] |= 1UL << slot;
                    }
                    else
                    {
                        pinFalse[node] |= 1UL << slot;
                    }

                    continue;
                }

                int definer = ground.PropertyDefiner[definition.CardinalityProperty];
                bool definerWant = ground.PropertyDefinerInverted[definition.CardinalityProperty] ? !wantBit : wantBit;
                work.Push(new ModalGadgetSignatureStep(definer, definerWant));
            }
        }

        for(int node = 0; node < pinTrue.Count; node++)
        {
            if((pinTrue[node] & pinFalse[node]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds one candidate structure: the told frontier under the vector and its
    /// per-node pins, one successor per TRUE EXISTENTIAL atom deduped by computed
    /// filler signature — a TRUE UNIVERSAL spawns NOTHING and only pushes its
    /// filler onto children the existentials already create — and then the raw
    /// relations, which are the self-loops and literals the gadget bits demand
    /// beside the modal edges the spawn created. A bound trip stops the build with
    /// the trip flagged, which ends the whole face silent.
    /// </summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="buffers">The working set, cleared at the head of this vector.</param>
    /// <param name="bounds">The effective bounds.</param>
    /// <param name="shared">The enumerated free vector.</param>
    /// <param name="modal">The enumerated modal-signature vector.</param>
    /// <param name="pinTrue">The per-node forced-true free-atom masks.</param>
    /// <param name="pinFalse">The per-node forced-false free-atom masks.</param>
    /// <param name="modalPin">The per-node forced-true modal-atom masks.</param>
    /// <param name="forcedAtomic">The atomic class memberships the told types force.</param>
    /// <param name="toldNodes">The told frontier's node count.</param>
    /// <param name="built">The arena's node count at the stopping point.</param>
    /// <param name="tripped">Whether a bound stopped the build.</param>
    private static void Build(
        ModalGadgetGround ground,
        ModalGadgetBuffers buffers,
        ModalGadgetBounds bounds,
        ulong shared,
        ulong modal,
        List<ulong> pinTrue,
        List<ulong> pinFalse,
        List<ulong> modalPin,
        HashSet<long> forcedAtomic,
        int toldNodes,
        out int built,
        out bool tripped)
    {
        tripped = false;
        built = toldNodes;
        ulong expanded = ExpandsModal(ground, modal);
        for(int node = 0; node < toldNodes; node++)
        {
            ulong pinned = node < pinTrue.Count ? pinTrue[node] | pinFalse[node] : 0UL;
            ulong nodeFree = (shared & ~pinned) | (node < pinTrue.Count ? pinTrue[node] : 0UL);
            ulong nodeModal = expanded | (node < modalPin.Count ? modalPin[node] : 0UL);
            Evaluate(ground, buffers, node, nodeFree, nodeModal, forcedAtomic);
        }

        int edges = 0;
        for(int node = 0; node < toldNodes; node++)
        {
            ulong groups = 0;
            for(int atom = 0; atom < ground.ExistentialClasses.Count; atom++)
            {
                if((buffers.Modal[node] & (1UL << atom)) != 0)
                {
                    groups |= 1UL << ground.AtomSignature[atom];
                }
            }

            while(groups != 0)
            {
                int group = BitOperations.TrailingZeroCount(groups);
                groups &= groups - 1;
                if(built >= bounds.Node || built >= buffers.NodeCapacity || bounds.Depth < 1)
                {
                    tripped = true;

                    return;
                }

                int successor = built;
                built++;
                ulong successorFree = ground.SignatureOpaque[group] ? 0UL : ground.SignatureTrue[group];
                for(int box = 0; box < ground.ToldBoxes.Count; box++)
                {
                    if(ground.ToldBoxes[box].Individual == node && !ground.BoxOpaque[box])
                    {
                        successorFree |= ground.BoxTrue[box];
                    }
                }

                Evaluate(ground, buffers, successor, successorFree, 0, forcedAtomic);
                WritesEdge(buffers, 0, node, successor);
                edges++;
                if(edges > bounds.Edge)
                {
                    tripped = true;

                    return;
                }
            }
        }

        for(int node = 0; node < built; node++)
        {
            for(int property = 0; property < ground.Properties.Count; property++)
            {
                if(!ReadsBit(buffers.Gadget, node * buffers.PropertyWords, property))
                {
                    continue;
                }

                if(ground.PropertyIsObject[property])
                {
                    WritesEdge(buffers, property + 1, node, node);
                    edges++;
                    if(edges > bounds.Edge)
                    {
                        tripped = true;

                        return;
                    }
                }
                else
                {
                    WritesBit(buffers.Data, node * buffers.PropertyWords, property);
                }
            }
        }
    }

    /// <summary>Expands one modal-signature vector into the raw modal atoms it sets: every existential occurrence sharing a signature group takes that group's value.</summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="modal">The modal-signature vector.</param>
    /// <returns>The raw modal-atom vector.</returns>
    private static ulong ExpandsModal(ModalGadgetGround ground, ulong modal)
    {
        ulong expanded = 0;
        for(int atom = 0; atom < ground.AtomSignature.Count; atom++)
        {
            if((modal & (1UL << ground.AtomSignature[atom])) != 0)
            {
                expanded |= 1UL << atom;
            }
        }

        return expanded;
    }

    /// <summary>Computes one node's whole state in topological order: every class's atomic extension membership from its definition, then every gadget property's bit from its free slot or from its definer's membership. Nothing recurses — the order is precomputed and the loop is flat.</summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="buffers">The working set.</param>
    /// <param name="node">The node index.</param>
    /// <param name="freeVector">The node's free gadget vector.</param>
    /// <param name="modalVector">The node's raw modal-atom vector.</param>
    /// <param name="forcedAtomic">The atomic class memberships the told types force.</param>
    private static void Evaluate(ModalGadgetGround ground, ModalGadgetBuffers buffers, int node, ulong freeVector, ulong modalVector, HashSet<long> forcedAtomic)
    {
        buffers.Free[node] = freeVector;
        buffers.Modal[node] = modalVector;
        for(int position = 0; position < ground.EvaluationOrder.Count; position++)
        {
            int classIndex = ground.EvaluationOrder[position];
            ModalGadgetClassDefinition definition = ground.Definitions[classIndex];
            bool value;
            if(definition.OtherKind == ModalGadgetDefinitionKind.Intersection)
            {
                value = true;
                for(int i = 0; i < definition.Operands.Count; i++)
                {
                    value = value && ReadsLabel(buffers, node, definition.Operands[i]);
                }
            }
            else if(definition.OtherKind == ModalGadgetDefinitionKind.Existential)
            {
                value = (modalVector & (1UL << definition.ModalAtom)) != 0;
            }
            else if(definition.CardinalityProperty >= 0)
            {
                bool bit = ReadsPropertyBit(ground, buffers, node, definition.CardinalityProperty, freeVector);
                value = definition.Polarity == ModalGadgetPolarity.MinOne ? bit : !bit;
            }
            else
            {
                value = forcedAtomic.Contains(FactKey(node, classIndex));
            }

            if(value)
            {
                WritesBit(buffers.Labels, node * buffers.LabelWords, classIndex);
            }
        }

        for(int property = 0; property < ground.Properties.Count; property++)
        {
            if(ReadsPropertyBit(ground, buffers, node, property, freeVector))
            {
                WritesBit(buffers.Gadget, node * buffers.PropertyWords, property);
            }
        }
    }

    /// <summary>One gadget property's bit at one node: its free-vector slot where the property survived elimination, and its definer's membership — negated where the definer stands on the property's zero side — where it did not.</summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="buffers">The working set.</param>
    /// <param name="node">The node index.</param>
    /// <param name="property">The gadget property index.</param>
    /// <param name="freeVector">The node's free gadget vector.</param>
    /// <returns>The property's bit.</returns>
    private static bool ReadsPropertyBit(ModalGadgetGround ground, ModalGadgetBuffers buffers, int node, int property, ulong freeVector)
    {
        int slot = ground.FreeSlot[property];
        if(slot >= 0)
        {
            return (freeVector & (1UL << slot)) != 0;
        }

        bool definerValue = ReadsLabel(buffers, node, ground.PropertyDefiner[property]);

        return ground.PropertyDefinerInverted[property] ? !definerValue : definerValue;
    }

    /// <summary>
    /// The verification pass — the certify face's SOLE soundness carrier. Every
    /// admitted axiom is re-evaluated against the finished structure's RAW
    /// RELATIONS: the domain, the modal role's extension, each gadget property's
    /// extension, and the atomic class extensions the construction fixed. The
    /// construction's GADGET BIT TABLE and its MODAL-ATOM VECTOR are consulted
    /// NOWHERE here — without that the pass would be circular and would verify
    /// every axiom by construction, and the sharp case is real in this habitat: a
    /// vector may fix an existential's atom FALSE while a successor spawned for a
    /// different diamond happens to satisfy that existential's filler, so in the
    /// real structure it is TRUE and the equivalence is violated.
    /// Every equivalence is checked as SET EQUALITY over the whole domain in BOTH
    /// inclusions at EVERY element, never as a one-way inclusion: a verifier
    /// checking only that a class's members have the demanded filler accepts a
    /// structure where the filler was materialised while the class is false at
    /// that element, which is not a model and would certify a void certificate.
    /// Every told class assertion is checked at its element, and every told box
    /// over the actual edge set — so a box at a node with no successors is
    /// vacuously satisfied because there is no edge for it to range over, which is
    /// exactly what a one-element model of this habitat relies on.
    /// Wherever the pass evaluates a class reference, the two BUILT-INS read
    /// their semantics-fixed extensions — <c>owl:Thing</c> is the WHOLE DOMAIN
    /// and <c>owl:Nothing</c> is EMPTY — so a told <c>owl:Thing</c> assertion is
    /// checked as domain membership of its carrier, which holds for every arena
    /// node; the admission fences keep both built-ins out of every DEFINITION
    /// position, so no other evaluation site can reach one.
    /// </summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="buffers">The finished structure.</param>
    /// <param name="nodeCount">The arena's node count.</param>
    /// <returns><see langword="true"/> when the structure satisfies every admitted axiom.</returns>
    private static bool Verifies(ModalGadgetGround ground, ModalGadgetBuffers buffers, int nodeCount)
    {
        for(int classIndex = 0; classIndex < ground.Definitions.Count; classIndex++)
        {
            ModalGadgetClassDefinition definition = ground.Definitions[classIndex];
            for(int node = 0; node < nodeCount; node++)
            {
                bool member = ReadsLabel(buffers, node, classIndex);
                if(definition.CardinalityProperty >= 0 && member != HoldsCardinality(ground, buffers, node, nodeCount, definition))
                {
                    return false;
                }

                if(definition.OtherKind == ModalGadgetDefinitionKind.Intersection && member != HoldsIntersection(buffers, node, definition))
                {
                    return false;
                }

                if(definition.OtherKind == ModalGadgetDefinitionKind.Existential && member != HoldsExistential(buffers, node, nodeCount, definition.ExistentialFiller))
                {
                    return false;
                }
            }
        }

        for(int told = 0; told < ground.ToldTypes.Count; told++)
        {
            if(!ReadsLabel(buffers, ground.ToldTypes[told].Individual, ground.ToldTypes[told].Class))
            {
                return false;
            }
        }

        for(int told = 0; told < ground.ThingAssertions.Count; told++)
        {
            if(ground.ThingAssertions[told] >= nodeCount)
            {
                return false;
            }
        }

        for(int box = 0; box < ground.ToldBoxes.Count; box++)
        {
            int node = ground.ToldBoxes[box].Individual;
            for(int successor = 0; successor < nodeCount; successor++)
            {
                if(ReadsEdge(buffers, 0, node, successor) && !ReadsLabel(buffers, successor, ground.ToldBoxes[box].Class))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether one cardinality restriction holds at one node, counted over the property's RAW extension: the role plane's row for an object property and the data-filler bit for a data property, never the construction's gadget bit.</summary>
    /// <param name="ground">The harvested admitted surface.</param>
    /// <param name="buffers">The finished structure.</param>
    /// <param name="node">The node index.</param>
    /// <param name="nodeCount">The arena's node count.</param>
    /// <param name="definition">The cardinality definition.</param>
    /// <returns><see langword="true"/> when the restriction holds at the node.</returns>
    private static bool HoldsCardinality(ModalGadgetGround ground, ModalGadgetBuffers buffers, int node, int nodeCount, ModalGadgetClassDefinition definition)
    {
        int property = definition.CardinalityProperty;
        int fillers = 0;
        if(ground.PropertyIsObject[property])
        {
            for(int target = 0; target < nodeCount; target++)
            {
                fillers += ReadsEdge(buffers, property + 1, node, target) ? 1 : 0;
            }
        }
        else
        {
            fillers = ReadsBit(buffers.Data, node * buffers.PropertyWords, property) ? 1 : 0;
        }

        return definition.BoundKind switch
        {
            OwlCardinalityKind.Min => fillers >= definition.Bound,
            OwlCardinalityKind.Max => fillers <= definition.Bound,
            OwlCardinalityKind.Exact => fillers == definition.Bound,
            _ => false,
        };
    }

    /// <summary>Whether one intersection definition holds at one node, read over the atomic class extensions the construction fixed.</summary>
    /// <param name="buffers">The finished structure.</param>
    /// <param name="node">The node index.</param>
    /// <param name="definition">The intersection definition.</param>
    /// <returns><see langword="true"/> when every operand holds at the node.</returns>
    private static bool HoldsIntersection(ModalGadgetBuffers buffers, int node, ModalGadgetClassDefinition definition)
    {
        for(int i = 0; i < definition.Operands.Count; i++)
        {
            if(!ReadsLabel(buffers, node, definition.Operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one existential definition holds at one node, read over the modal role's ACTUAL edge relation and the filler's atomic extension — never over the construction's modal-atom vector.</summary>
    /// <param name="buffers">The finished structure.</param>
    /// <param name="node">The node index.</param>
    /// <param name="nodeCount">The arena's node count.</param>
    /// <param name="filler">The existential's filler class index.</param>
    /// <returns><see langword="true"/> when some modal successor satisfies the filler.</returns>
    private static bool HoldsExistential(ModalGadgetBuffers buffers, int node, int nodeCount, int filler)
    {
        for(int successor = 0; successor < nodeCount; successor++)
        {
            if(ReadsEdge(buffers, 0, node, successor) && ReadsLabel(buffers, successor, filler))
            {
                return true;
            }
        }

        return false;
    }
}
