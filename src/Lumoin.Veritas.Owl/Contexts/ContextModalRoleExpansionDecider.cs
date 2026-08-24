using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>The Shape M clash reason family — the modal-expansion counterpart of the spy-point clash reason: stable leading identifiers the statistics assembly and the battery discriminate on.</summary>
internal static class ModalExpansionClashReasons
{
    /// <summary>The asserted-empty-class clash: a node label carries <c>owl:Nothing</c>, whose extension is empty in every interpretation while the label demands a member. The reason carries no argument, because the node it lands on may be a spawned skolem successor that no told term names.</summary>
    public const string AssertedNothingMembership = "ModalExpansionAssertedNothingMembership";

    /// <summary>The node-local numeric clash: one node carries an unqualified minimum and an unqualified maximum on the same property IRI and property kind, with the minimum strictly above the maximum.</summary>
    /// <param name="property">The property IRI whose two bounds contradict each other.</param>
    /// <returns>The named reason.</returns>
    public static string NodeLocalNumericBound(Utf8String property)
    {
        return $"ModalExpansionNodeLocalNumericBound({property})";
    }
}

/// <summary>Which entry point the decider takes. The production value is code zero, so an options value left at <see langword="default"/> is bit-identical to production.</summary>
internal enum ModalExpansionEntry
{
    /// <summary>The production path: the jurisdiction gates, the engagement signals, and the bounded expansion behind them.</summary>
    Decide = 0,

    /// <summary>The measurement path: the window ceilings are compared and nothing is expanded — the dark control's entry, which forms no verdict on any input.</summary>
    MeasureOnly = 1,
}

/// <summary>
/// The five modal-expansion bounds as overridable members where ZERO MEANS "USE
/// THE <c>const</c>", so a value left at <see langword="default"/> is exactly
/// production and a caller supplies only the non-zero overrides it needs. Each
/// effective bound is read through a get-only property that returns the
/// <c>const</c> on a zero backing member, so no member ever holds a duplicated
/// literal of a <c>const</c>.
/// </summary>
/// <param name="NodeOverride">The node-arena override; zero reads <see cref="ContextModalRoleExpansionDecider.ModalExpansionNodeBound"/>.</param>
/// <param name="DepthOverride">The spawn-depth override; zero reads <see cref="ContextModalRoleExpansionDecider.ModalExpansionDepthBound"/>.</param>
/// <param name="LabelOverride">The facts-per-node override; zero reads <see cref="ContextModalRoleExpansionDecider.ModalExpansionLabelBound"/>.</param>
/// <param name="EdgeOverride">The directed-edge override; zero reads <see cref="ContextModalRoleExpansionDecider.ModalExpansionEdgeBound"/>.</param>
/// <param name="StepOverride">The rule-application override; zero reads <see cref="ContextModalRoleExpansionDecider.ModalExpansionStepBound"/>.</param>
internal readonly record struct ModalExpansionBounds(
    int NodeOverride,
    int DepthOverride,
    int LabelOverride,
    int EdgeOverride,
    int StepOverride)
{
    /// <summary>The effective node-arena ceiling — told level-0 nodes plus spawned skolem nodes together.</summary>
    public int Node => NodeOverride == 0 ? ContextModalRoleExpansionDecider.ModalExpansionNodeBound : NodeOverride;

    /// <summary>The effective spawn-depth ceiling.</summary>
    public int Depth => DepthOverride == 0 ? ContextModalRoleExpansionDecider.ModalExpansionDepthBound : DepthOverride;

    /// <summary>The effective facts-per-node ceiling, measured under the counting conventions.</summary>
    public int Label => LabelOverride == 0 ? ContextModalRoleExpansionDecider.ModalExpansionLabelBound : LabelOverride;

    /// <summary>The effective directed-edge ceiling, materialised inverses included.</summary>
    public int Edge => EdgeOverride == 0 ? ContextModalRoleExpansionDecider.ModalExpansionEdgeBound : EdgeOverride;

    /// <summary>The effective rule-application ceiling.</summary>
    public int Step => StepOverride == 0 ? ContextModalRoleExpansionDecider.ModalExpansionStepBound : StepOverride;
}

/// <summary>
/// The modal expansion's TWO variation points, accepted only by the internal
/// entry points. Every member names its PRODUCTION behaviour at code zero, so a
/// value left at <see langword="default"/> is bit-identical to production and the
/// reasoner's call path passes none. The list is CLOSED at two members: this
/// family has no verification pass to absorb what a construction proposes, so a
/// RULE variation would put a wrong-clash code path into the shipped decider,
/// and every guard that would otherwise want one is pinned module-side by a
/// fixture whose correct outcome under the shipped rules is silence and whose
/// discrimination control clashes. Widening or narrowing a BOUND can only change
/// how much of the expansion runs, and the expansion is sound at every prefix, so
/// a bound override can produce a silence or a decision and never a wrong one.
/// </summary>
/// <param name="Entry">Whether the decider decides or only measures its window ceilings.</param>
/// <param name="Bounds">The five modal-expansion bounds, zero-means-production per member.</param>
internal readonly record struct ModalExpansionConstructionOptions(
    ModalExpansionEntry Entry,
    ModalExpansionBounds Bounds);

/// <summary>
/// The Shape M window measurement the census-first recognizer's
/// pre-clausification pass reads on every modal-role-expansion-jurisdiction
/// module. The five quantities are charged under the family's counting
/// conventions: a rule application is ONE rule firing and an existential spawn is
/// ONE application producing an edge fact AND a membership fact; forward and
/// materialised-inverse edges count separately and NO edge is ever derived from
/// transitivity, so no such edge is ever counted; a node's label count excludes
/// <c>owl:Thing</c> and excludes the intersection concept itself while counting
/// its conjuncts individually. Every quantity is charged at a level's propagation
/// fixpoint, whose fact set is the unique least fixpoint of a monotone deduped
/// rule set, so every number here is a property of the module rather than of a
/// scan order.
/// </summary>
/// <param name="NodesSpawned">The fresh skolem successors the expansion allocated at the stopping point; told level-0 nodes are NOT counted here, though the node bound covers them.</param>
/// <param name="MaxDepthReached">The deepest spawn level reached, measured from the told frontier at level zero.</param>
/// <param name="PeakLabelSize">The largest per-node counted fact set reached.</param>
/// <param name="EdgesMaterialised">The directed edges the structure holds — told, spawn-forward, and materialised inverse.</param>
/// <param name="RuleApplications">The rule firings charged to the stopping point.</param>
/// <param name="WindowSilences">One when the node, depth, label, edge, or step bound stopped the expansion — a named silence, never a verdict over an unfinished structure; zero otherwise.</param>
internal readonly record struct ModalExpansionWindow(
    int NodesSpawned,
    int MaxDepthReached,
    int PeakLabelSize,
    int EdgesMaterialised,
    int RuleApplications,
    int WindowSilences)
{
    /// <summary>The empty window: no modal expansion ran.</summary>
    public static ModalExpansionWindow Empty => default;
}

/// <summary>The Shape M decider's outcome: the bounded expansion's refutation when a clash was reached inside the window, and the window measurement the census carries unconditionally.</summary>
/// <param name="Consistent">The verdict — <see langword="false"/> for the reached clash — or <see langword="null"/> when the face is silent on the module. The face has no certify direction, so <see langword="true"/> never occurs and the type has no path to it.</param>
/// <param name="Window">The window measurement.</param>
internal readonly record struct ModalExpansionOutcome(bool? Consistent, ModalExpansionWindow Window)
{
    /// <summary>The named clash reason on a refutation; <see langword="null"/> on every silent outcome.</summary>
    public string? ClashReason { get; init; }

    /// <summary>The silent outcome carrying only the window measurement.</summary>
    /// <param name="window">The measured window.</param>
    /// <returns>The silent outcome.</returns>
    public static ModalExpansionOutcome SilentWith(ModalExpansionWindow window)
    {
        return new ModalExpansionOutcome(null, window);
    }
}

/// <summary>
/// The enumeration-CSP habitat decider's modal role-expansion clash face (face
/// sixteen): a tier-2 PROPAGATION WITH BOUNDED EXISTENTIAL SPAWNING over a
/// module whose contradiction is reachable only by creating existential
/// witnesses and then propagating information back UP through told inverse roles
/// into a node that already carries a numeric bound. It is the first shipped
/// face that spawns a skolem successor and propagates a fact back up an inverse
/// role.
/// The rule set is deterministic throughout: a told class assertion seeds a node
/// whose individual may be NAMED OR BLANK; named subsumption and the
/// name-to-definition half of a told equivalence unfold a label; an intersection
/// eliminates to its conjuncts; an existential allocates a FRESH successor once
/// per node and structurally identical existential; a universal delivers its
/// filler across a MATERIALISED edge for the EXACT role IRI; a told inverse pair
/// mirrors every edge at creation time; a told-transitive property pushes the
/// universal itself to the successor, which is the SINGLE mechanism for that
/// fact — the edge relation is NEVER transitively closed; and a node carrying an
/// unqualified minimum above an unqualified maximum on one (property IRI,
/// property kind) pair, or carrying <c>owl:Nothing</c>, is a CLASH.
/// The search discipline is breadth-first by level: the non-spawning rules run to
/// fixpoint over the WHOLE materialised structure — ancestors included, which is
/// what makes an upward clash reachable — the clash check runs at each level's
/// fixpoint, and the whole level's existentials are spawned at once with the
/// semantic skip check frozen at the batch boundary. Nothing recurses: the
/// propagation is an explicit worklist and the levels are an explicit loop.
/// The face is CLASH-ONLY and has no certify counterpart BY DESIGN: the outcome
/// type has no path to <see langword="true"/>. Budget exhaustion, an
/// inadmissible axiom, a missing engagement signal and a clash-free fixpoint are
/// ALL silence, and none of them is ever read as a consistency verdict.
/// Termination comes from the BUDGET, never from blocking: no blocking condition
/// of any kind is implemented, so no blocking condition can be implemented
/// wrongly, and the face walks into an infinite chain, trips its bound and
/// abstains. Completeness is NOT claimed.
/// Jurisdiction is a closed admission grammar with drop-not-approximate: an
/// axiom outside the grammar is DROPPED and the module continues, because for a
/// clash-direction face over a monotone logic ignoring an axiom can only lose
/// clashes. Three gates instead SILENCE the module whole, because each leaves the
/// face without a basis for reading the rest of it: a disjunctive construct
/// anywhere in the module, a cardinality restriction on a non-simple role, and a
/// property IRI whose object-versus-data kind is ambiguous or undetermined.
/// </summary>
internal static class ContextModalRoleExpansionDecider
{
    /// <summary>
    /// The node-arena ceiling: told level-0 nodes PLUS spawned skolem successors
    /// together are held up to this many and the face is SILENT above it. The
    /// ceiling is an ENGINEERING one with overflow-silence and a revisable value,
    /// never a compiled-in corpus fact: the packed label table holds one bit per
    /// node and interned concept and the edge relation one bit per role and
    /// ordered node pair, so the arena's cost is this constant against the label
    /// ceiling plus its square against the role count. Collecting the told shapes
    /// is one linear pass bounded by the module's own axiom count rather than by
    /// this constant. A module whose told individuals alone exceed the arena is a
    /// window silence, not a second allocation and not an exception.
    /// </summary>
    public const int ModalExpansionNodeBound = 64;

    /// <summary>The spawn-depth ceiling: successors are allocated down to this many levels below the told frontier and the face is SILENT below it. The value JOINS the counting family's shared sixteen boundary discipline, so this family adds no new boundary to the house. It is unreachable for any branching factor above one — the node arena caps a binary expansion at depth six — and is carried for the boundary discipline and for the linear-chain case.</summary>
    public const int ModalExpansionDepthBound = 16;

    /// <summary>The facts-per-node ceiling: one node carries up to this many counted label facts and the face is SILENT above it. The quantity is the one the window reports — <c>owl:Thing</c> and the intersection concept itself excluded, conjuncts counted individually — so the bound and the statistic compare the same number. No shipped face bounds a per-node label set, so the constant carries its own justification rather than joining a uniformity claim: it is the label width one packed bitset row spans at the arena's word granularity.</summary>
    public const int ModalExpansionLabelBound = 64;

    /// <summary>The directed-edge ceiling: the structure holds up to this many edges — told, spawn-forward and materialised-inverse counted separately — and the face is SILENT above it. It cannot bind on the SPAWNED structure at all, since every spawned node arrives with exactly one forward edge and its materialised inverse and so contributes at most twice the node arena; it binds only on a told-edge-dense module, which is the shape constraint it is carried for.</summary>
    public const int ModalExpansionEdgeBound = 256;

    /// <summary>The rule-application ceiling: one decision charges up to this many rule firings and the face is SILENT above it. This is the BINDING cost control of the five — the node arena against the label ceiling admits four thousand derived facts, each costing at least one application against this constant — while the other four are shape constraints. The value sits orders of magnitude below the engine's own attempt ceiling, so a module this face abstains on still reaches the engine with its budget intact.</summary>
    public const int ModalExpansionStepBound = 8192;

    /// <summary>The word width of one packed bitset word.</summary>
    private const int ModalExpansionWordBits = 64;

    /// <summary>The property-kind evidence bit of an object-side occurrence.</summary>
    private const int ModalObjectSide = 1;

    /// <summary>The property-kind evidence bit of a data-side occurrence.</summary>
    private const int ModalDataSide = 2;

    /// <summary>The family's own buffer pool: the packed label table, the edge relation and their batch snapshots are rented from here, never from a shared pool, once per decision and released on a semantic disposable that trims the pool behind it.</summary>
    private static VeritasMemoryPool<ulong> ModalExpansionBufferPool { get; } = new();

    /// <summary>Measures the Shape M census window without deciding anything: the window CEILINGS are compared against the told arena and nothing is expanded, so the census ships identically dark and lit. No verdict is ever formed on this path.</summary>
    /// <param name="module">The module to measure.</param>
    /// <returns>The silent outcome carrying the measurement.</returns>
    public static ModalExpansionOutcome Measure(ReasoningModule module)
    {
        return Measure(module, default);
    }

    /// <summary>The construction-options overload of the measurement: the options change only the bounds a decision would run under, so the measurement compares the same ceilings under every value and no verdict is formed on this path either.</summary>
    /// <param name="module">The module to measure.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The silent outcome carrying the measurement.</returns>
    public static ModalExpansionOutcome Measure(ReasoningModule module, ModalExpansionConstructionOptions options)
    {
        ModalExpansionGround ground = Harvest(module);

        return ModalExpansionOutcome.SilentWith(MeasureWindow(ground, options.Bounds));
    }

    /// <summary>
    /// Runs the modal role-expansion clash face in jurisdiction order: the told
    /// harvest and the window ceiling comparison first, so an arena silence still
    /// carries its counter; then the three silencing gates; then the engagement
    /// signals, which are a cheap necessary-condition pre-filter so the expansion
    /// never runs pointlessly; then the bounded expansion itself. The face
    /// returns <see langword="false"/> or silence only — never a consistency
    /// certificate.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <returns>The outcome: the reached clash with its measurement, or silence.</returns>
    public static ModalExpansionOutcome Run(ReasoningModule module)
    {
        return Run(module, default);
    }

    /// <summary>The construction-options overload of the decision: the two variation points reach the expansion only through this entry point, the production reasoner path passing none.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="options">The construction options; <see langword="default"/> is production.</param>
    /// <returns>The outcome: the reached clash with its measurement, or silence.</returns>
    public static ModalExpansionOutcome Run(ReasoningModule module, ModalExpansionConstructionOptions options)
    {
        if(options.Entry == ModalExpansionEntry.MeasureOnly)
        {
            return Measure(module, options);
        }

        ModalExpansionGround ground = Harvest(module);
        ModalExpansionWindow window = MeasureWindow(ground, options.Bounds);
        if(window.WindowSilences > 0 || ground.Silenced || !IsEngaged(ground))
        {
            return ModalExpansionOutcome.SilentWith(window);
        }

        return Expand(ground, options.Bounds);
    }

    /// <summary>The concept forms the expansion carries. Every other class-expression constructor is outside the admission grammar and drops the axiom carrying it.</summary>
    private enum ModalConceptKind
    {
        /// <summary>A named class other than the two semantics-fixed constants — the only form a definition is keyed on.</summary>
        Name = 0,

        /// <summary><c>owl:Thing</c>: derivable and storable, excluded from every counted label size, and consumed by no rule.</summary>
        Thing = 1,

        /// <summary><c>owl:Nothing</c>: the empty class whose membership is a clash.</summary>
        Nothing = 2,

        /// <summary>An intersection over admitted operands, eliminated to its conjuncts and excluded from every counted label size.</summary>
        Intersection = 3,

        /// <summary>An existential over a plain named role — the spawner.</summary>
        Existential = 4,

        /// <summary>A universal over a plain named role — the deliverer, and under a told-transitive role the pushed fact itself.</summary>
        Universal = 5,

        /// <summary>An unqualified minimum, maximum or exact cardinality over a kind-determined property — the only form the numeric clash reads.</summary>
        Bound = 6,
    }

    /// <summary>Why an expansion stopped short of a fixpoint.</summary>
    private enum ModalExpansionTrip
    {
        /// <summary>No bound stopped the expansion.</summary>
        None = 0,

        /// <summary>The node arena filled.</summary>
        Node = 1,

        /// <summary>The next level would sit below the depth ceiling.</summary>
        Depth = 2,

        /// <summary>One node's counted label set reached its ceiling.</summary>
        Label = 3,

        /// <summary>The directed-edge relation reached its ceiling.</summary>
        Edge = 4,

        /// <summary>The rule applications reached their ceiling.</summary>
        Step = 5,
    }

    /// <summary>Which worklist fact one queue entry carries.</summary>
    private enum ModalFactKind
    {
        /// <summary>A node-and-concept membership.</summary>
        Membership = 0,

        /// <summary>A role-and-ordered-pair edge.</summary>
        Edge = 1,
    }

    /// <summary>
    /// One interned concept. Interning is STRUCTURAL — two syntactically distinct
    /// blank nodes carrying identical content are ONE concept — so a label fact,
    /// an existential's spawn key and every counted quantity are properties of
    /// the module rather than of its serialisation.
    /// </summary>
    /// <param name="Kind">The concept form.</param>
    /// <param name="Symbol">The interned class or property IRI for a name and a bound; unused otherwise.</param>
    /// <param name="Role">The interned role for an existential and a universal; unused otherwise.</param>
    /// <param name="Filler">The interned filler concept for an existential and a universal; unused otherwise.</param>
    /// <param name="First">The first operand slot of an intersection inside the shared operand list; unused otherwise.</param>
    /// <param name="Count">The operand count of an intersection; unused otherwise.</param>
    /// <param name="BoundKind">The cardinality flavour of a bound; unused otherwise.</param>
    /// <param name="Bound">The cardinality of a bound; unused otherwise.</param>
    /// <param name="DataSide">Whether a bound is a data bound rather than an object bound — the second half of the numeric clash's key.</param>
    private readonly record struct ModalConcept(
        ModalConceptKind Kind,
        int Symbol,
        int Role,
        int Filler,
        int First,
        int Count,
        OwlCardinalityKind BoundKind,
        int Bound,
        bool DataSide);

    /// <summary>The interning key of every concept form other than an intersection, whose operand list is compared element by element instead.</summary>
    /// <param name="Kind">The concept form.</param>
    /// <param name="Symbol">The interned class or property IRI.</param>
    /// <param name="Role">The interned role.</param>
    /// <param name="Filler">The interned filler concept.</param>
    /// <param name="BoundKind">The cardinality flavour.</param>
    /// <param name="Bound">The cardinality.</param>
    /// <param name="DataSide">Whether a bound is a data bound.</param>
    private readonly record struct ModalConceptKey(
        ModalConceptKind Kind,
        int Symbol,
        int Role,
        int Filler,
        OwlCardinalityKind BoundKind,
        int Bound,
        bool DataSide);

    /// <summary>One directed edge over interned indices.</summary>
    /// <param name="Role">The interned role.</param>
    /// <param name="Source">The source node.</param>
    /// <param name="Target">The target node.</param>
    private readonly record struct ModalEdge(int Role, int Source, int Target);

    /// <summary>One told membership over interned indices.</summary>
    /// <param name="Node">The told node.</param>
    /// <param name="Concept">The interned concept.</param>
    private readonly record struct ModalMembership(int Node, int Concept);

    /// <summary>One pending existential: the dedupe key the spawn rule fires at most once on.</summary>
    /// <param name="Node">The node carrying the existential.</param>
    /// <param name="Concept">The structurally interned existential concept.</param>
    private readonly record struct ModalSpawn(int Node, int Concept);

    /// <summary>One worklist entry: a membership when <paramref name="Kind"/> is a membership, a role edge otherwise.</summary>
    /// <param name="Kind">Which fact the entry carries.</param>
    /// <param name="A">The node of a membership; the role of an edge.</param>
    /// <param name="B">The concept of a membership; the source of an edge.</param>
    /// <param name="C">Unused on a membership; the target of an edge.</param>
    private readonly record struct ModalFact(ModalFactKind Kind, int A, int B, int C);

    /// <summary>One property occurrence in a cardinality restriction, the evidence the property-kind determination reads.</summary>
    /// <param name="Property">The property IRI.</param>
    /// <param name="Side">The occurrence's side — the object-side or the data-side bit.</param>
    /// <param name="KindAgnostic">Whether the restriction carried no qualifier at all, in which case the constructor fixes no kind and a declaration must.</param>
    private readonly record struct ModalPropertyUse(Utf8String Property, int Side, bool KindAgnostic);

    /// <summary>
    /// The harvested told surface: the interned concept table and its symbol,
    /// role and told-term tables, the definitions the unfolding rules read, the
    /// told inverse and transitive relations, the told memberships and edges the
    /// seed lays down, and the three gates' evidence. Collecting it is one linear
    /// pass over the module's axioms plus one over its class expressions.
    /// </summary>
    private sealed class ModalExpansionGround
    {
        /// <summary>The interned class and property IRIs, in first-seen order.</summary>
        public List<Utf8String> Symbols { get; } = [];

        /// <summary>The interned class and property IRIs' indices.</summary>
        public Dictionary<Utf8String, int> SymbolIndices { get; } = [];

        /// <summary>The interned roles, in first-seen order — the edge relation's major index.</summary>
        public List<Utf8String> Roles { get; } = [];

        /// <summary>The interned roles' indices.</summary>
        public Dictionary<Utf8String, int> RoleIndices { get; } = [];

        /// <summary>The interned concepts, in first-seen order — the label table's bit index.</summary>
        public List<ModalConcept> Concepts { get; } = [];

        /// <summary>The interned concepts' keys, intersections excepted.</summary>
        public Dictionary<ModalConceptKey, int> ConceptIndices { get; } = [];

        /// <summary>The shared operand list every interned intersection slices.</summary>
        public List<int> IntersectionOperands { get; } = [];

        /// <summary>The definitions keyed on a name's symbol: the superclasses of a told subsumption and the definition side of a told equivalence read name-to-definition.</summary>
        public Dictionary<int, List<int>> Definitions { get; } = [];

        /// <summary>The told inverse-role relation, recorded in both argument orders over plain roles only.</summary>
        public Dictionary<int, List<int>> InverseRoles { get; } = [];

        /// <summary>The told-transitive roles, the only roles the universal push fires for.</summary>
        public HashSet<int> TransitiveRoles { get; } = [];

        /// <summary>The told individual terms, in first-seen order — the level-0 frontier, named and blank alike.</summary>
        public List<Utf8String> ToldTerms { get; } = [];

        /// <summary>The told individual terms' indices, keyed by IRI or anonymous label.</summary>
        public Dictionary<Utf8String, int> ToldTermIndices { get; } = [];

        /// <summary>The told class assertions the seed lays down.</summary>
        public List<ModalMembership> ToldMemberships { get; } = [];

        /// <summary>The told object-property assertions the seed lays down.</summary>
        public List<ModalEdge> ToldEdges { get; } = [];

        /// <summary>The property-kind evidence declarations carry, keyed by property IRI.</summary>
        public Dictionary<Utf8String, int> DeclaredSides { get; } = [];

        /// <summary>The property-kind evidence restriction positions carry, keyed by property IRI.</summary>
        public Dictionary<Utf8String, int> RestrictionSides { get; } = [];

        /// <summary>Every property occurrence in a cardinality restriction, the kind determination's input.</summary>
        public List<ModalPropertyUse> CardinalityUses { get; } = [];

        /// <summary>The linearisation scratch the structural interning walks.</summary>
        public List<OwlClassExpression> Linearised { get; } = [];

        /// <summary>The linearisation's first-child slots.</summary>
        public List<int> LinearisedFirst { get; } = [];

        /// <summary>The linearisation's child counts.</summary>
        public List<int> LinearisedCount { get; } = [];

        /// <summary>The linearisation's per-node interned concept, filled in reverse order so a child is always interned before its parent.</summary>
        public List<int> LinearisedConcept { get; } = [];

        /// <summary>The operand scratch one intersection is canonicalised in.</summary>
        public List<int> OperandScratch { get; } = [];

        /// <summary>Whether a disjunctive construct occurs anywhere in the module — the module-wide gate that keeps a union out of every label, since the face has no disjunction handler and a union reaching a label could only be misread.</summary>
        public bool Disjunctive { get; set; }

        /// <summary>Whether a cardinality restriction sits on a non-simple role — a role told transitive, or a told inverse of one. Such a module has left OWL 2 DL, where admitting number restrictions on non-simple roles is an undecidability rather than an unsoundness, so the face abstains rather than guessing.</summary>
        public bool NonSimpleBound { get; set; }

        /// <summary>Whether a property IRI's object-versus-data kind is ambiguous or undetermined — half of the numeric clash's key, which is determined and never defaulted.</summary>
        public bool KindUnresolved { get; set; }

        /// <summary>Whether the module carries an existential the spawn rule could fire.</summary>
        public bool HasSpawner { get; set; }

        /// <summary>Whether the module carries a universal whose role is a told inverse of a role an existential or a told edge uses — the upward channel that makes a non-local clash reachable.</summary>
        public bool HasUpwardChannel { get; set; }

        /// <summary>Whether the module carries a clash template — a minimum above a maximum on one property, or an <c>owl:Nothing</c> occurrence.</summary>
        public bool HasClashTemplate { get; set; }

        /// <summary>The interned <c>owl:Nothing</c> concept; <c>-1</c> when the module never mentions it.</summary>
        public int NothingConcept { get; set; } = -1;

        /// <summary>Whether one of the three gates silences the module whole.</summary>
        public bool Silenced => Disjunctive || NonSimpleBound || KindUnresolved;
    }

    /// <summary>
    /// The decision's whole packed working set, owned by ONE rental from the
    /// family's own pool and released on the disposable's own scope: the node
    /// label table, the role edge relation, and the batch snapshots of both that
    /// freeze the spawn rule's semantic skip check at a level's batch boundary.
    /// Every region is packed <see cref="ulong"/> bitsets in one flat array with
    /// no per-element object anywhere, and the whole array is zeroed on
    /// reservation because a rented buffer carries whatever the previous rental
    /// left.
    /// </summary>
    private sealed class ModalExpansionBuffers: IDisposable
    {
        /// <summary>The single rental backing every region, supplied by the reservation factory that is this type's only construction path.</summary>
        private IMemoryOwner<ulong> Owner { get; init; } = default!;

        /// <summary>Whether the rental has already been returned.</summary>
        private bool Released { get; set; }

        /// <summary>The node arena's capacity — told level-0 nodes and spawned successors together.</summary>
        public int NodeCapacity { get; init; }

        /// <summary>The words one node's label row spans.</summary>
        public int LabelWords { get; init; }

        /// <summary>The words one edge row spans.</summary>
        public int NodeWords { get; init; }

        /// <summary>The word offset of the label table.</summary>
        private int LabelOffset { get; init; }

        /// <summary>The word offset of the edge relation.</summary>
        private int EdgeOffset { get; init; }

        /// <summary>The word offset of the label snapshot.</summary>
        private int LabelSnapshotOffset { get; init; }

        /// <summary>The word offset of the edge snapshot.</summary>
        private int EdgeSnapshotOffset { get; init; }

        /// <summary>The words the label table spans.</summary>
        private int LabelRegionWords { get; init; }

        /// <summary>The words the edge relation spans.</summary>
        private int EdgeRegionWords { get; init; }

        /// <summary>The label table, indexed node-major then concept.</summary>
        public Span<ulong> Labels => Owner.Memory.Span.Slice(LabelOffset, LabelRegionWords);

        /// <summary>The edge relation, indexed role-major then source then target.</summary>
        public Span<ulong> Edges => Owner.Memory.Span.Slice(EdgeOffset, EdgeRegionWords);

        /// <summary>The label table as of the current level's batch boundary.</summary>
        public Span<ulong> LabelSnapshot => Owner.Memory.Span.Slice(LabelSnapshotOffset, LabelRegionWords);

        /// <summary>The edge relation as of the current level's batch boundary.</summary>
        public Span<ulong> EdgeSnapshot => Owner.Memory.Span.Slice(EdgeSnapshotOffset, EdgeRegionWords);

        /// <summary>Reserves the whole working set in ONE rental sized from the harvested surface and the effective bounds, zeroing it before any region is read.</summary>
        /// <param name="ground">The harvested told surface.</param>
        /// <param name="bounds">The effective bounds, whose node ceiling sizes the arena.</param>
        /// <returns>The reserved working set.</returns>
        public static ModalExpansionBuffers Reserve(ModalExpansionGround ground, ModalExpansionBounds bounds)
        {
            int nodeCapacity = bounds.Node < 1 ? 1 : bounds.Node;
            int concepts = ground.Concepts.Count == 0 ? 1 : ground.Concepts.Count;
            int roles = ground.Roles.Count == 0 ? 1 : ground.Roles.Count;
            int labelWords = (concepts + ModalExpansionWordBits - 1) / ModalExpansionWordBits;
            int nodeWords = (nodeCapacity + ModalExpansionWordBits - 1) / ModalExpansionWordBits;
            int labelRegionWords = nodeCapacity * labelWords;
            int edgeRegionWords = roles * nodeCapacity * nodeWords;

            int labelOffset = 0;
            int edgeOffset = labelOffset + labelRegionWords;
            int labelSnapshotOffset = edgeOffset + edgeRegionWords;
            int edgeSnapshotOffset = labelSnapshotOffset + labelRegionWords;
            int total = edgeSnapshotOffset + edgeRegionWords;

            IMemoryOwner<ulong> owner = ModalExpansionBufferPool.Rent(total == 0 ? 1 : total);
            owner.Memory.Span.Clear();

            return new ModalExpansionBuffers
            {
                Owner = owner,
                Released = false,
                NodeCapacity = nodeCapacity,
                LabelWords = labelWords,
                NodeWords = nodeWords,
                LabelOffset = labelOffset,
                EdgeOffset = edgeOffset,
                LabelSnapshotOffset = labelSnapshotOffset,
                EdgeSnapshotOffset = edgeSnapshotOffset,
                LabelRegionWords = labelRegionWords,
                EdgeRegionWords = edgeRegionWords,
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
            ModalExpansionBufferPool.TrimExcess();
        }
    }

    /// <summary>
    /// One expansion's mutable state: the packed working set, the effective
    /// bounds, the propagation worklist and the level's spawn batch, the per-node
    /// counted label sizes, the five charged quantities, and the bound that
    /// stopped the run. Every scratch list is owned here and reused, so the
    /// expansion allocates once per decision rather than once per firing.
    /// </summary>
    private sealed class ModalExpansionState
    {
        /// <summary>The harvested told surface the rules read.</summary>
        public ModalExpansionGround Ground { get; init; } = default!;

        /// <summary>The packed working set.</summary>
        public ModalExpansionBuffers Buffers { get; init; } = default!;

        /// <summary>The effective bounds.</summary>
        public ModalExpansionBounds Bounds { get; init; }

        /// <summary>The propagation worklist — an explicit first-in-first-out queue over derived facts, never a recursion.</summary>
        public Queue<ModalFact> Worklist { get; } = new();

        /// <summary>The existentials awaiting the next level's spawn batch.</summary>
        public List<ModalSpawn> Pending { get; } = [];

        /// <summary>The current level's frozen spawn batch.</summary>
        public List<ModalSpawn> Batch { get; } = [];

        /// <summary>The existentials already queued for a spawn — the syntactic dedupe key, checked before the semantic skip.</summary>
        public HashSet<ModalSpawn> Spawned { get; } = [];

        /// <summary>The per-node counted label sizes.</summary>
        public List<int> LabelCounts { get; } = [];

        /// <summary>The inverse-materialisation worklist one edge creation closes over.</summary>
        public Queue<ModalEdge> EdgeClosure { get; } = new();

        /// <summary>The concept scratch one node's label read fills.</summary>
        public List<int> ConceptScratch { get; } = [];

        /// <summary>The target scratch one edge row read fills.</summary>
        public List<int> TargetScratch { get; } = [];

        /// <summary>The bound scratch the numeric clash check fills.</summary>
        public List<int> ClashScratch { get; } = [];

        /// <summary>The nodes the arena holds — told level-0 nodes plus spawned successors.</summary>
        public int NodeCount { get; set; }

        /// <summary>The spawned successors allocated.</summary>
        public int NodesSpawned { get; set; }

        /// <summary>The deepest spawn level reached.</summary>
        public int MaxDepth { get; set; }

        /// <summary>The largest counted label size reached.</summary>
        public int PeakLabel { get; set; }

        /// <summary>The directed edges materialised.</summary>
        public int Edges { get; set; }

        /// <summary>The rule applications charged.</summary>
        public int Applications { get; set; }

        /// <summary>The bound that stopped the run; none when the run reached a fixpoint or a clash.</summary>
        public ModalExpansionTrip Trip { get; set; }
    }

    /// <summary>Harvests the told surface: the module-wide jurisdiction scan first, then the admission pass that interns what the rules may read, then the gate resolution and the engagement signals the two passes' evidence settles.</summary>
    /// <param name="module">The module to harvest.</param>
    /// <returns>The harvested told surface.</returns>
    private static ModalExpansionGround Harvest(ReasoningModule module)
    {
        ModalExpansionGround ground = new();
        ScanJurisdiction(module, ground);
        foreach(OwlAxiom axiom in module.Axioms)
        {
            CollectAxiom(axiom, ground);
        }

        ResolveGates(ground);

        return ground;
    }

    /// <summary>
    /// The module-wide jurisdiction scan: every axiom and every class expression
    /// nested in one is inspected for a disjunctive construct, for the property
    /// occurrences the kind determination reads, and for the cardinality
    /// restrictions the non-simple-role gate compares. The scan is module-wide
    /// rather than reachability-scoped because the set of axioms the face
    /// actually uses is known only after the expansion has run, and the gates
    /// must be decided before it. The expression walk drains the structural
    /// traversal seam with an explicit stack and never recurses.
    /// </summary>
    /// <param name="module">The module to scan.</param>
    /// <param name="ground">The told surface accumulator.</param>
    private static void ScanJurisdiction(ReasoningModule module, ModalExpansionGround ground)
    {
        List<RdfTerm> individuals = [];
        Stack<OwlClassExpression> work = new();
        Stack<OwlDataRange> ranges = new();
        foreach(OwlAxiom axiom in module.Axioms)
        {
            InspectAxiom(axiom, ground, ranges);
            individuals.Clear();
            axiom.AppendMentionedIndividuals(individuals, work);
            while(work.Count > 0)
            {
                OwlClassExpression expression = work.Pop();
                InspectExpression(expression, ground, ranges);
                expression.AppendMentionedIndividuals(individuals, work);
            }

            DrainRanges(ground, ranges);
        }
    }

    /// <summary>Inspects one axiom's own surfaces: the two disjunctive axiom kinds, the property declarations the kind determination stands on, and the data ranges an axiom-level range position carries.</summary>
    /// <param name="axiom">The axiom to inspect.</param>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="rangesToAppendTo">The data-range worklist an axiom-level range is pushed onto.</param>
    private static void InspectAxiom(OwlAxiom axiom, ModalExpansionGround ground, Stack<OwlDataRange> rangesToAppendTo)
    {
        switch(axiom)
        {
            case(OwlDisjointClassesAxiom):
            case(OwlDisjointUnionAxiom):
            {
                ground.Disjunctive = true;
                break;
            }
            case(OwlDeclarationAxiom { Kind: OwlEntityKind.ObjectProperty } objectDeclaration):
            {
                RecordSide(ground.DeclaredSides, objectDeclaration.Entity.Iri, ModalObjectSide);
                break;
            }
            case(OwlDeclarationAxiom { Kind: OwlEntityKind.DataProperty } dataDeclaration):
            {
                RecordSide(ground.DeclaredSides, dataDeclaration.Entity.Iri, ModalDataSide);
                break;
            }
            case(OwlDataPropertyRangeAxiom dataRange):
            {
                rangesToAppendTo.Push(dataRange.Range);
                break;
            }
            case(OwlDatatypeDefinitionAxiom datatypeDefinition):
            {
                rangesToAppendTo.Push(datatypeDefinition.Range);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Inspects one class expression's own surface: the three disjunctive constructors, the property side a restriction position carries, and the cardinality restrictions the kind determination and the non-simple-role gate compare.</summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="rangesToAppendTo">The data-range worklist a data restriction's range is pushed onto.</param>
    private static void InspectExpression(OwlClassExpression expression, ModalExpansionGround ground, Stack<OwlDataRange> rangesToAppendTo)
    {
        switch(expression)
        {
            case(OwlObjectUnionOf):
            case(OwlObjectComplementOf):
            case(OwlObjectOneOf):
            {
                ground.Disjunctive = true;
                break;
            }
            case(OwlObjectCardinality objectCardinality):
            {
                Utf8String objectProperty = objectCardinality.Property.Property.Iri;
                RecordSide(ground.RestrictionSides, objectProperty, ModalObjectSide);
                ground.CardinalityUses.Add(new ModalPropertyUse(objectProperty, ModalObjectSide, objectCardinality.Filler is null));
                break;
            }
            case(OwlDataCardinality dataCardinality):
            {
                Utf8String dataProperty = dataCardinality.Property.Iri;
                RecordSide(ground.RestrictionSides, dataProperty, ModalDataSide);
                ground.CardinalityUses.Add(new ModalPropertyUse(dataProperty, ModalDataSide, dataCardinality.Range is null));
                if(dataCardinality.Range is OwlDataRange cardinalityRange)
                {
                    rangesToAppendTo.Push(cardinalityRange);
                }

                break;
            }
            case(OwlObjectSomeValuesFrom existential):
            {
                RecordSide(ground.RestrictionSides, existential.Property.Property.Iri, ModalObjectSide);
                break;
            }
            case(OwlObjectAllValuesFrom universal):
            {
                RecordSide(ground.RestrictionSides, universal.Property.Property.Iri, ModalObjectSide);
                break;
            }
            case(OwlObjectHasValue valuePin):
            {
                RecordSide(ground.RestrictionSides, valuePin.Property.Property.Iri, ModalObjectSide);
                break;
            }
            case(OwlObjectHasSelf reflexive):
            {
                RecordSide(ground.RestrictionSides, reflexive.Property.Property.Iri, ModalObjectSide);
                break;
            }
            case(OwlDataSomeValuesFrom dataExistential):
            {
                RecordDataProperties(ground, dataExistential.Properties);
                rangesToAppendTo.Push(dataExistential.Range);
                break;
            }
            case(OwlDataAllValuesFrom dataUniversal):
            {
                RecordDataProperties(ground, dataUniversal.Properties);
                rangesToAppendTo.Push(dataUniversal.Range);
                break;
            }
            case(OwlDataHasValue dataValuePin):
            {
                RecordSide(ground.RestrictionSides, dataValuePin.Property.Iri, ModalDataSide);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Drains one axiom's data ranges with an explicit stack, flagging the enumerated-literal constructor the disjunction gate names and descending through the composite ranges that may nest one.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="ranges">The data-range worklist to drain.</param>
    private static void DrainRanges(ModalExpansionGround ground, Stack<OwlDataRange> ranges)
    {
        while(ranges.Count > 0)
        {
            OwlDataRange range = ranges.Pop();
            switch(range)
            {
                case(OwlDataOneOf):
                {
                    ground.Disjunctive = true;
                    break;
                }
                case(OwlDataIntersectionOf intersection):
                {
                    for(int index = 0; index < intersection.Ranges.Count; index++)
                    {
                        ranges.Push(intersection.Ranges[index]);
                    }

                    break;
                }
                case(OwlDataUnionOf union):
                {
                    for(int index = 0; index < union.Ranges.Count; index++)
                    {
                        ranges.Push(union.Ranges[index]);
                    }

                    break;
                }
                case(OwlDataComplementOf complement):
                {
                    ranges.Push(complement.Range);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    /// <summary>Records one data-restriction property list's side, the n-ary form included.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="properties">The restricted data properties.</param>
    private static void RecordDataProperties(ModalExpansionGround ground, IReadOnlyList<NamedNode> properties)
    {
        for(int index = 0; index < properties.Count; index++)
        {
            RecordSide(ground.RestrictionSides, properties[index].Iri, ModalDataSide);
        }
    }

    /// <summary>Records one property-kind evidence bit, keyed by property IRI under the content equality of <see cref="Utf8String"/> rather than by any string round-trip.</summary>
    /// <param name="sidesToAppendTo">The evidence relation.</param>
    /// <param name="property">The property IRI.</param>
    /// <param name="side">The evidence bit.</param>
    private static void RecordSide(Dictionary<Utf8String, int> sidesToAppendTo, Utf8String property, int side)
    {
        sidesToAppendTo[property] = sidesToAppendTo.TryGetValue(property, out int recorded) ? recorded | side : side;
    }

    /// <summary>
    /// The admission pass: every axiom inside the closed grammar is interned into
    /// what the rules read, and every axiom outside it is DROPPED and the module
    /// continues. Dropping is sound in the clash direction over a monotone logic
    /// — an unread axiom can only lose clashes, never create one — so the whole
    /// admission problem reduces to reading correctly what is read at all.
    /// </summary>
    /// <param name="axiom">The axiom to admit or drop.</param>
    /// <param name="ground">The told surface accumulator.</param>
    private static void CollectAxiom(OwlAxiom axiom, ModalExpansionGround ground)
    {
        switch(axiom)
        {
            case(OwlClassAssertionAxiom assertion):
            {
                CollectAssertion(assertion, ground);
                break;
            }
            case(OwlObjectPropertyAssertionAxiom edge):
            {
                CollectToldEdge(edge, ground);
                break;
            }
            case(OwlSubClassOfAxiom subClass):
            {
                if(subClass.SubClass is OwlClassReference reference && TryInternConcept(ground, subClass.SuperClass, out int definition))
                {
                    AddDefinition(ground, reference.Class.Iri, definition);
                }

                break;
            }
            case(OwlEquivalentClassesAxiom equivalent):
            {
                CollectEquivalence(equivalent, ground);
                break;
            }
            case(OwlInverseObjectPropertiesAxiom { First: OwlObjectPropertyReference first, Second: OwlObjectPropertyReference second }):
            {
                LinkInverse(ground, first.Named.Iri, second.Named.Iri);
                break;
            }
            case(OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Transitive, Property: OwlObjectPropertyReference transitive }):
            {
                ground.TransitiveRoles.Add(InternRole(ground, transitive.Named.Iri));
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Collects one told class assertion as a level-0 seed. The individual may be NAMED OR BLANK and the blank case is first-class: a blank-node individual is an ordinary domain element in every model, so the axiom seeds the same label either way.</summary>
    /// <param name="axiom">The class assertion.</param>
    /// <param name="ground">The told surface accumulator.</param>
    private static void CollectAssertion(OwlClassAssertionAxiom axiom, ModalExpansionGround ground)
    {
        if(TryTermKey(axiom.Individual, out Utf8String key) && TryInternConcept(ground, axiom.Class, out int concept))
        {
            ground.ToldMemberships.Add(new ModalMembership(InternTerm(ground, key), concept));
        }
    }

    /// <summary>Collects one told object-property assertion as an ordinary materialised edge over two level-0 nodes; the inverse mirroring the seed runs is the same rule every spawned edge takes.</summary>
    /// <param name="axiom">The property assertion.</param>
    /// <param name="ground">The told surface accumulator.</param>
    private static void CollectToldEdge(OwlObjectPropertyAssertionAxiom axiom, ModalExpansionGround ground)
    {
        if(TryTermKey(axiom.Source, out Utf8String source) && TryTermKey(axiom.Target, out Utf8String target))
        {
            ground.ToldEdges.Add(new ModalEdge(InternRole(ground, axiom.Property.Iri), InternTerm(ground, source), InternTerm(ground, target)));
        }
    }

    /// <summary>
    /// Collects one told equivalence in the NAME-TO-DEFINITION direction only.
    /// Which operand is the name is decided by CONSTRUCT and never by argument
    /// position: the name side is the operand that is a class IRI. With exactly
    /// one class-IRI operand that operand is the name; with both operands class
    /// IRIs there is no composition and no conjunct to drop, so both directions
    /// are plain subsumptions and both are derived; with neither the axiom drops
    /// whole. The definition-to-name half of a composition is deliberately not
    /// implemented, because dropping a conjunct in that direction is unsound
    /// while the half implemented is a plain subsumption with no side condition.
    /// </summary>
    /// <param name="axiom">The equivalence axiom.</param>
    /// <param name="ground">The told surface accumulator.</param>
    private static void CollectEquivalence(OwlEquivalentClassesAxiom axiom, ModalExpansionGround ground)
    {
        if(axiom.First is OwlClassReference firstName && TryInternConcept(ground, axiom.Second, out int firstDefinition))
        {
            AddDefinition(ground, firstName.Class.Iri, firstDefinition);
        }

        if(axiom.Second is OwlClassReference secondName && TryInternConcept(ground, axiom.First, out int secondDefinition))
        {
            AddDefinition(ground, secondName.Class.Iri, secondDefinition);
        }
    }

    /// <summary>Records one definition against a name's symbol, skipping a definition the name already carries.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="name">The defined class IRI.</param>
    /// <param name="definition">The interned definition concept.</param>
    private static void AddDefinition(ModalExpansionGround ground, Utf8String name, int definition)
    {
        int symbol = InternSymbol(ground, name);
        if(!ground.Definitions.TryGetValue(symbol, out List<int>? definitions))
        {
            definitions = [];
            ground.Definitions[symbol] = definitions;
        }

        for(int index = 0; index < definitions.Count; index++)
        {
            if(definitions[index] == definition)
            {
                return;
            }
        }

        definitions.Add(definition);
    }

    /// <summary>Records one told inverse pair in BOTH argument orders over plain roles, the relation the eager edge mirroring and the non-simple-role closure both read.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="first">The first role IRI.</param>
    /// <param name="second">The second role IRI.</param>
    private static void LinkInverse(ModalExpansionGround ground, Utf8String first, Utf8String second)
    {
        int left = InternRole(ground, first);
        int right = InternRole(ground, second);
        LinkInverseDirection(ground, left, right);
        LinkInverseDirection(ground, right, left);
    }

    /// <summary>Records one direction of the told inverse-role relation, skipping a partner the direction already carries.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="role">The role the direction is keyed on.</param>
    /// <param name="partner">The partner role.</param>
    private static void LinkInverseDirection(ModalExpansionGround ground, int role, int partner)
    {
        if(!ground.InverseRoles.TryGetValue(role, out List<int>? partners))
        {
            partners = [];
            ground.InverseRoles[role] = partners;
        }

        for(int index = 0; index < partners.Count; index++)
        {
            if(partners[index] == partner)
            {
                return;
            }
        }

        partners.Add(partner);
    }

    /// <summary>Reads one individual term's carrier key: an IRI for a named individual and the anonymous label for a blank one, both under the content equality of <see cref="Utf8String"/>. A term denoting neither is no individual.</summary>
    /// <param name="term">The candidate individual term.</param>
    /// <param name="key">The carrier key; empty when the term is no individual.</param>
    /// <returns><see langword="true"/> on an individual term.</returns>
    private static bool TryTermKey(RdfTerm term, out Utf8String key)
    {
        switch(term)
        {
            case(NamedNode named):
            {
                key = named.Iri;

                return true;
            }
            case(BlankNode anonymous):
            {
                key = anonymous.Label;

                return true;
            }
            default:
            {
                key = default;

                return false;
            }
        }
    }

    /// <summary>Interns one told individual term, appending it in first-seen order.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="key">The carrier key.</param>
    /// <returns>The told node index.</returns>
    private static int InternTerm(ModalExpansionGround ground, Utf8String key)
    {
        if(ground.ToldTermIndices.TryGetValue(key, out int index))
        {
            return index;
        }

        index = ground.ToldTerms.Count;
        ground.ToldTerms.Add(key);
        ground.ToldTermIndices[key] = index;

        return index;
    }

    /// <summary>Interns one class or property IRI, appending it in first-seen order.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="iri">The IRI.</param>
    /// <returns>The symbol index.</returns>
    private static int InternSymbol(ModalExpansionGround ground, Utf8String iri)
    {
        if(ground.SymbolIndices.TryGetValue(iri, out int index))
        {
            return index;
        }

        index = ground.Symbols.Count;
        ground.Symbols.Add(iri);
        ground.SymbolIndices[iri] = index;

        return index;
    }

    /// <summary>Interns one role IRI, appending it in first-seen order — the edge relation's major index, and the identity every role lookup compares by FULL IRI rather than by local name.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="iri">The role IRI.</param>
    /// <returns>The role index.</returns>
    private static int InternRole(ModalExpansionGround ground, Utf8String iri)
    {
        if(ground.RoleIndices.TryGetValue(iri, out int index))
        {
            return index;
        }

        index = ground.Roles.Count;
        ground.Roles.Add(iri);
        ground.RoleIndices[iri] = index;

        return index;
    }

    /// <summary>
    /// Interns one class expression STRUCTURALLY, or reports it outside the
    /// admission grammar so its whole axiom drops. The walk is a linearisation
    /// followed by a reverse pass: children always sit at a higher slot than
    /// their parent, so the reverse pass interns every child before the parent
    /// that reads it, and nothing recurses.
    /// </summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="root">The expression to intern.</param>
    /// <param name="concept">The interned concept; <c>-1</c> when the expression is outside the grammar.</param>
    /// <returns><see langword="true"/> when the whole expression is inside the grammar.</returns>
    private static bool TryInternConcept(ModalExpansionGround ground, OwlClassExpression root, out int concept)
    {
        concept = -1;
        List<OwlClassExpression> nodes = ground.Linearised;
        List<int> first = ground.LinearisedFirst;
        List<int> count = ground.LinearisedCount;
        List<int> interned = ground.LinearisedConcept;
        nodes.Clear();
        first.Clear();
        count.Clear();
        interned.Clear();
        nodes.Add(root);
        for(int index = 0; index < nodes.Count; index++)
        {
            while(first.Count <= index)
            {
                first.Add(0);
                count.Add(0);
            }

            int start = nodes.Count;
            AppendChildren(nodes[index], nodes);
            first[index] = start;
            count[index] = nodes.Count - start;
        }

        while(interned.Count < nodes.Count)
        {
            interned.Add(-1);
        }

        for(int index = nodes.Count - 1; index >= 0; index--)
        {
            if(!TryInternNode(ground, index, out int node))
            {
                return false;
            }

            interned[index] = node;
        }

        concept = interned[0];

        return true;
    }

    /// <summary>Appends one expression's admitted child expressions to the linearisation. A qualified cardinality's filler is deliberately NOT a child: a qualified bound is outside the grammar and its whole axiom drops rather than its qualifier being read.</summary>
    /// <param name="expression">The expression to expand.</param>
    /// <param name="nodesToAppendTo">The linearisation the children are appended to.</param>
    private static void AppendChildren(OwlClassExpression expression, List<OwlClassExpression> nodesToAppendTo)
    {
        switch(expression)
        {
            case(OwlObjectIntersectionOf intersection):
            {
                for(int index = 0; index < intersection.Operands.Count; index++)
                {
                    nodesToAppendTo.Add(intersection.Operands[index]);
                }

                break;
            }
            case(OwlObjectSomeValuesFrom existential):
            {
                nodesToAppendTo.Add(existential.Filler);
                break;
            }
            case(OwlObjectAllValuesFrom universal):
            {
                nodesToAppendTo.Add(universal.Filler);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Interns one linearised node, its children already interned. Construct dispatch is on the expression's CONSTRUCT, never on an operand list's shape, so no constructor sharing a list shape with another can be consumed as that other.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="slot">The linearisation slot.</param>
    /// <param name="concept">The interned concept; <c>-1</c> when the node is outside the grammar.</param>
    /// <returns><see langword="true"/> when the node is inside the grammar.</returns>
    private static bool TryInternNode(ModalExpansionGround ground, int slot, out int concept)
    {
        concept = -1;
        OwlClassExpression expression = ground.Linearised[slot];
        switch(expression)
        {
            case(OwlClassReference reference):
            {
                concept = InternNamed(ground, reference.Class.Iri);

                return true;
            }
            case(OwlObjectIntersectionOf):
            {
                concept = InternIntersection(ground, slot);

                return true;
            }
            case(OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference existentialRole }):
            {
                concept = InternConcept(ground, new ModalConceptKey(ModalConceptKind.Existential, 0, InternRole(ground, existentialRole.Named.Iri), ground.LinearisedConcept[ground.LinearisedFirst[slot]], OwlCardinalityKind.Min, 0, false));

                return true;
            }
            case(OwlObjectAllValuesFrom { Property: OwlObjectPropertyReference universalRole }):
            {
                concept = InternConcept(ground, new ModalConceptKey(ModalConceptKind.Universal, 0, InternRole(ground, universalRole.Named.Iri), ground.LinearisedConcept[ground.LinearisedFirst[slot]], OwlCardinalityKind.Min, 0, false));

                return true;
            }
            case(OwlObjectCardinality { Property: OwlObjectPropertyReference boundRole } objectBound):
            {
                if(!ContextHabitatRecognizer.IsUnqualifiedFiller(objectBound.Filler) || objectBound.Cardinality < 0)
                {
                    return false;
                }

                concept = InternConcept(ground, new ModalConceptKey(ModalConceptKind.Bound, InternSymbol(ground, boundRole.Named.Iri), 0, 0, objectBound.Kind, objectBound.Cardinality, false));

                return true;
            }
            case(OwlDataCardinality dataBound):
            {
                if(!IsUnqualifiedRange(dataBound.Range) || dataBound.Cardinality < 0)
                {
                    return false;
                }

                concept = InternConcept(ground, new ModalConceptKey(ModalConceptKind.Bound, InternSymbol(ground, dataBound.Property.Iri), 0, 0, dataBound.Kind, dataBound.Cardinality, true));

                return true;
            }
            default:
            {
                return false;
            }
        }
    }

    /// <summary>Whether a data cardinality restriction's range leaves the count unqualified: no range at all, or the explicit <c>rdfs:Literal</c> — the two spellings of the same unrestricted count.</summary>
    /// <param name="range">The restriction's qualifying range.</param>
    /// <returns><see langword="true"/> for an unqualified count.</returns>
    private static bool IsUnqualifiedRange(OwlDataRange? range)
    {
        return range is null || (range is OwlDatatypeReference reference && reference.Datatype.Iri.Equals(RdfVocabulary.Rdfs.LiteralClass));
    }

    /// <summary>Interns one named class, mapping the two semantics-fixed constants onto their own concept forms so no definition is ever keyed on them and the empty class is a clash rather than a name.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="iri">The class IRI.</param>
    /// <returns>The interned concept.</returns>
    private static int InternNamed(ModalExpansionGround ground, Utf8String iri)
    {
        if(iri.Equals(OwlVocabulary.Thing))
        {
            return InternConcept(ground, new ModalConceptKey(ModalConceptKind.Thing, 0, 0, 0, OwlCardinalityKind.Min, 0, false));
        }

        if(iri.Equals(OwlVocabulary.Nothing))
        {
            int nothing = InternConcept(ground, new ModalConceptKey(ModalConceptKind.Nothing, 0, 0, 0, OwlCardinalityKind.Min, 0, false));
            ground.NothingConcept = nothing;

            return nothing;
        }

        return InternConcept(ground, new ModalConceptKey(ModalConceptKind.Name, InternSymbol(ground, iri), 0, 0, OwlCardinalityKind.Min, 0, false));
    }

    /// <summary>Interns one keyed concept form, appending it in first-seen order.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="key">The concept key.</param>
    /// <returns>The interned concept.</returns>
    private static int InternConcept(ModalExpansionGround ground, ModalConceptKey key)
    {
        if(ground.ConceptIndices.TryGetValue(key, out int index))
        {
            return index;
        }

        index = ground.Concepts.Count;
        ground.Concepts.Add(new ModalConcept(key.Kind, key.Symbol, key.Role, key.Filler, 0, 0, key.BoundKind, key.Bound, key.DataSide));
        ground.ConceptIndices[key] = index;

        return index;
    }

    /// <summary>
    /// Interns one intersection by its CANONICALISED operand set — sorted and
    /// deduplicated over the operands' own interned concepts — and compares it
    /// against the intersections already interned element by element. An
    /// intersection with no operand denotes <c>owl:Thing</c> and interns as it.
    /// </summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="slot">The linearisation slot of the intersection.</param>
    /// <returns>The interned concept.</returns>
    private static int InternIntersection(ModalExpansionGround ground, int slot)
    {
        List<int> operands = ground.OperandScratch;
        operands.Clear();
        int start = ground.LinearisedFirst[slot];
        int operandCount = ground.LinearisedCount[slot];
        for(int index = 0; index < operandCount; index++)
        {
            int operand = ground.LinearisedConcept[start + index];
            if(!operands.Contains(operand))
            {
                operands.Add(operand);
            }
        }

        operands.Sort();
        if(operands.Count == 0)
        {
            return InternConcept(ground, new ModalConceptKey(ModalConceptKind.Thing, 0, 0, 0, OwlCardinalityKind.Min, 0, false));
        }

        for(int index = 0; index < ground.Concepts.Count; index++)
        {
            ModalConcept candidate = ground.Concepts[index];
            if(candidate.Kind == ModalConceptKind.Intersection && candidate.Count == operands.Count && SharesOperands(ground, candidate, operands))
            {
                return index;
            }
        }

        int first = ground.IntersectionOperands.Count;
        for(int index = 0; index < operands.Count; index++)
        {
            ground.IntersectionOperands.Add(operands[index]);
        }

        int interned = ground.Concepts.Count;
        ground.Concepts.Add(new ModalConcept(ModalConceptKind.Intersection, 0, 0, 0, first, operands.Count, OwlCardinalityKind.Min, 0, false));

        return interned;
    }

    /// <summary>Whether one interned intersection carries exactly the canonicalised operand set offered.</summary>
    /// <param name="ground">The told surface accumulator.</param>
    /// <param name="candidate">The interned intersection.</param>
    /// <param name="operands">The canonicalised operand set.</param>
    /// <returns><see langword="true"/> on an element-by-element match.</returns>
    private static bool SharesOperands(ModalExpansionGround ground, ModalConcept candidate, List<int> operands)
    {
        for(int index = 0; index < operands.Count; index++)
        {
            if(ground.IntersectionOperands[candidate.First + index] != operands[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the two evidence-driven gates and the engagement signals once the
    /// harvest is complete: the non-simple role closure the cardinality gate
    /// compares, the property-kind determination the numeric clash's key needs,
    /// and the four necessary conditions the pre-filter reads.
    /// </summary>
    /// <param name="ground">The harvested told surface.</param>
    private static void ResolveGates(ModalExpansionGround ground)
    {
        ResolveNonSimpleBounds(ground);
        ResolvePropertyKinds(ground);
        ResolveSignals(ground);
    }

    /// <summary>
    /// The non-simple-role gate: the non-simple set is the CLOSURE of the
    /// told-transitive roles under told inverse pairs, because transitivity of a
    /// role and of its inverse are the same fact, and a cardinality restriction
    /// on any member silences the module whole. The closure runs as a bounded
    /// worklist over role indices and never recurses.
    /// </summary>
    /// <param name="ground">The harvested told surface.</param>
    private static void ResolveNonSimpleBounds(ModalExpansionGround ground)
    {
        if(ground.CardinalityUses.Count == 0 || ground.TransitiveRoles.Count == 0)
        {
            return;
        }

        HashSet<int> nonSimple = [];
        Queue<int> pending = new();
        foreach(int transitive in ground.TransitiveRoles)
        {
            if(nonSimple.Add(transitive))
            {
                pending.Enqueue(transitive);
            }
        }

        while(pending.Count > 0)
        {
            int role = pending.Dequeue();
            if(!ground.InverseRoles.TryGetValue(role, out List<int>? partners))
            {
                continue;
            }

            for(int index = 0; index < partners.Count; index++)
            {
                if(nonSimple.Add(partners[index]))
                {
                    pending.Enqueue(partners[index]);
                }
            }
        }

        for(int index = 0; index < ground.CardinalityUses.Count; index++)
        {
            if(ground.RoleIndices.TryGetValue(ground.CardinalityUses[index].Property, out int role) && nonSimple.Contains(role))
            {
                ground.NonSimpleBound = true;

                return;
            }
        }
    }

    /// <summary>
    /// The property-kind determination, which the numeric clash's second key half
    /// stands on: a kind is fixed by the restriction CONSTRUCTOR where the
    /// constructor is explicitly kinded — a qualified restriction names its own
    /// class or data range — and otherwise by a DECLARATION present in the
    /// module. There is no third step and no default: a property carrying a
    /// kind-agnostic cardinality restriction with no declaration is
    /// KIND-UNDETERMINED and silences the module whole, exactly as a
    /// kind-AMBIGUOUS one does, because guessing a kind is guessing half of the
    /// clash's key. The rule also closes the imports question, since an
    /// unresolved import cannot change a kind an undeclared property never had.
    /// </summary>
    /// <param name="ground">The harvested told surface.</param>
    private static void ResolvePropertyKinds(ModalExpansionGround ground)
    {
        foreach(KeyValuePair<Utf8String, int> declared in ground.DeclaredSides)
        {
            if(declared.Value == (ModalObjectSide | ModalDataSide))
            {
                ground.KindUnresolved = true;

                return;
            }
        }

        foreach(KeyValuePair<Utf8String, int> restricted in ground.RestrictionSides)
        {
            if(restricted.Value == (ModalObjectSide | ModalDataSide))
            {
                ground.KindUnresolved = true;

                return;
            }
        }

        for(int index = 0; index < ground.CardinalityUses.Count; index++)
        {
            ModalPropertyUse use = ground.CardinalityUses[index];
            if(!use.KindAgnostic)
            {
                continue;
            }

            if(!ground.DeclaredSides.TryGetValue(use.Property, out int declaredSide) || declaredSide != use.Side)
            {
                ground.KindUnresolved = true;

                return;
            }
        }
    }

    /// <summary>
    /// The engagement signals: a root exists, a spawner exists, an upward channel
    /// exists, and a clash template exists. They are NECESSARY conditions used as
    /// a cheap pre-filter so the expansion never runs pointlessly — failing one
    /// costs reach and never correctness — and the spawner signal is read in its
    /// loose form, an admitted existential anywhere, since a looser filter only
    /// runs an expansion that then decides nothing.
    /// </summary>
    /// <param name="ground">The harvested told surface.</param>
    private static void ResolveSignals(ModalExpansionGround ground)
    {
        HashSet<int> existentialRoles = [];
        for(int index = 0; index < ground.Concepts.Count; index++)
        {
            ModalConcept concept = ground.Concepts[index];
            if(concept.Kind == ModalConceptKind.Existential)
            {
                ground.HasSpawner = true;
                existentialRoles.Add(concept.Role);
            }
        }

        for(int index = 0; index < ground.ToldEdges.Count; index++)
        {
            existentialRoles.Add(ground.ToldEdges[index].Role);
        }

        for(int index = 0; index < ground.Concepts.Count && !ground.HasUpwardChannel; index++)
        {
            ModalConcept concept = ground.Concepts[index];
            if(concept.Kind != ModalConceptKind.Universal || !ground.InverseRoles.TryGetValue(concept.Role, out List<int>? partners))
            {
                continue;
            }

            for(int partner = 0; partner < partners.Count; partner++)
            {
                if(existentialRoles.Contains(partners[partner]))
                {
                    ground.HasUpwardChannel = true;
                    break;
                }
            }
        }

        ground.HasClashTemplate = ground.NothingConcept >= 0 || HasNumericTemplate(ground);
    }

    /// <summary>Whether some property IRI carries both a minimum and a maximum with the minimum strictly above the maximum, on one property kind — the template without which the numeric clash cannot fire anywhere.</summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <returns><see langword="true"/> on a numeric clash template.</returns>
    private static bool HasNumericTemplate(ModalExpansionGround ground)
    {
        for(int left = 0; left < ground.Concepts.Count; left++)
        {
            ModalConcept minimum = ground.Concepts[left];
            if(minimum.Kind != ModalConceptKind.Bound || !CarriesMinimum(minimum))
            {
                continue;
            }

            for(int right = 0; right < ground.Concepts.Count; right++)
            {
                ModalConcept maximum = ground.Concepts[right];
                if(maximum.Kind == ModalConceptKind.Bound
                    && CarriesMaximum(maximum)
                    && maximum.Symbol == minimum.Symbol
                    && maximum.DataSide == minimum.DataSide
                    && minimum.Bound > maximum.Bound)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether one admitted bound carries a minimum: a minimum restriction directly, or an exact restriction, which is read as its minimum and maximum halves together.</summary>
    /// <param name="concept">The bound concept.</param>
    /// <returns><see langword="true"/> when the bound constrains from below.</returns>
    private static bool CarriesMinimum(ModalConcept concept)
    {
        return concept.BoundKind is OwlCardinalityKind.Min or OwlCardinalityKind.Exact;
    }

    /// <summary>Whether one admitted bound carries a maximum: a maximum restriction directly, or an exact restriction, which is read as its minimum and maximum halves together.</summary>
    /// <param name="concept">The bound concept.</param>
    /// <returns><see langword="true"/> when the bound constrains from above.</returns>
    private static bool CarriesMaximum(ModalConcept concept)
    {
        return concept.BoundKind is OwlCardinalityKind.Max or OwlCardinalityKind.Exact;
    }

    /// <summary>Whether every engagement signal holds, so the expansion has something to reach.</summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <returns><see langword="true"/> when the expansion may run.</returns>
    private static bool IsEngaged(ModalExpansionGround ground)
    {
        return ground.ToldMemberships.Count > 0 && ground.HasSpawner && ground.HasUpwardChannel && ground.HasClashTemplate;
    }

    /// <summary>Reads the window off the harvested surface by comparing CEILINGS only, expanding nothing: a module whose told individuals alone exceed the node arena is a window silence, and every other quantity is a measurement no unexpanded module has.</summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <param name="bounds">The effective bounds.</param>
    /// <returns>The window measurement.</returns>
    private static ModalExpansionWindow MeasureWindow(ModalExpansionGround ground, ModalExpansionBounds bounds)
    {
        return new ModalExpansionWindow(0, 0, 0, 0, 0, ground.ToldTerms.Count > bounds.Node ? 1 : 0);
    }

    /// <summary>
    /// The bounded expansion: the told frontier is seeded at level zero, then
    /// each round propagates the non-spawning rules to fixpoint over the WHOLE
    /// materialised structure, checks for a clash at that fixpoint, and spawns
    /// the whole level's existentials at once. The loop leaves on the first level
    /// whose fixpoint carries a clash, on a fixpoint with nothing left to spawn,
    /// or on a bound trip — and the last two are silence.
    /// </summary>
    /// <param name="ground">The harvested told surface.</param>
    /// <param name="bounds">The effective bounds.</param>
    /// <returns>The outcome: the reached clash with its measurement, or silence.</returns>
    private static ModalExpansionOutcome Expand(ModalExpansionGround ground, ModalExpansionBounds bounds)
    {
        using ModalExpansionBuffers buffers = ModalExpansionBuffers.Reserve(ground, bounds);
        ModalExpansionState state = new()
        {
            Ground = ground,
            Buffers = buffers,
            Bounds = bounds,
        };

        if(!SeedTold(state))
        {
            return ModalExpansionOutcome.SilentWith(MeasuredWindow(state));
        }

        int level = 0;
        while(true)
        {
            if(!Propagate(state))
            {
                return ModalExpansionOutcome.SilentWith(MeasuredWindow(state));
            }

            if(TryFindClash(state, out string clashReason))
            {
                return new ModalExpansionOutcome(false, MeasuredWindow(state))
                {
                    ClashReason = clashReason,
                };
            }

            if(state.Pending.Count == 0)
            {
                return ModalExpansionOutcome.SilentWith(MeasuredWindow(state));
            }

            if(level + 1 > bounds.Depth)
            {
                state.Trip = ModalExpansionTrip.Depth;

                return ModalExpansionOutcome.SilentWith(MeasuredWindow(state));
            }

            level++;
            if(!SpawnBatch(state, level))
            {
                return ModalExpansionOutcome.SilentWith(MeasuredWindow(state));
            }
        }
    }

    /// <summary>Reads the measured window off a stopped expansion. Every charged quantity is reported as it stands, and a quantity whose bound stopped the run stands AT its ceiling, which is how a window row identifies which of the five tripped; the single silence counter is charged for any of them.</summary>
    /// <param name="state">The expansion state.</param>
    /// <returns>The window measurement.</returns>
    private static ModalExpansionWindow MeasuredWindow(ModalExpansionState state)
    {
        return new ModalExpansionWindow(
            state.NodesSpawned,
            state.MaxDepth,
            state.PeakLabel,
            state.Edges,
            state.Applications,
            state.Trip == ModalExpansionTrip.None ? 0 : 1);
    }

    /// <summary>Seeds the told frontier: every told individual term is a LEVEL-0 node, every told class assertion its label, and every told object-property assertion an ordinary materialised edge. Told facts are not rule applications and are not charged as such.</summary>
    /// <param name="state">The expansion state.</param>
    /// <returns><see langword="true"/> when the seed fitted inside every bound.</returns>
    private static bool SeedTold(ModalExpansionState state)
    {
        for(int index = 0; index < state.Ground.ToldTerms.Count; index++)
        {
            state.LabelCounts.Add(0);
        }

        state.NodeCount = state.Ground.ToldTerms.Count;
        for(int index = 0; index < state.Ground.ToldMemberships.Count; index++)
        {
            ModalMembership membership = state.Ground.ToldMemberships[index];
            if(!TryAddMembership(state, membership.Node, membership.Concept, false))
            {
                return false;
            }
        }

        for(int index = 0; index < state.Ground.ToldEdges.Count; index++)
        {
            ModalEdge edge = state.Ground.ToldEdges[index];
            if(!TryMaterialiseEdge(state, edge.Role, edge.Source, edge.Target, false))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs the non-spawning rules to fixpoint over the whole materialised structure. The worklist ranges over every derived fact wherever its premises sit, ancestors an earlier level already processed included, which is what lets a fact derived at a spawned descendant reach the node where a bound lives.</summary>
    /// <param name="state">The expansion state.</param>
    /// <returns><see langword="true"/> when the fixpoint was reached inside every bound.</returns>
    private static bool Propagate(ModalExpansionState state)
    {
        while(state.Worklist.Count > 0)
        {
            ModalFact fact = state.Worklist.Dequeue();
            bool advanced = fact.Kind == ModalFactKind.Membership
                ? PropagateMembership(state, fact.A, fact.B)
                : PropagateEdge(state, fact.A, fact.B, fact.C);
            if(!advanced)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Fires every rule one membership fact triggers: a name unfolds to its
    /// definitions, an intersection eliminates to its conjuncts, a universal
    /// delivers its filler across every materialised edge for the EXACT role and,
    /// under a told-transitive role, pushes ITSELF to the successor, and an
    /// existential queues its spawn under the syntactic dedupe key. A bound,
    /// <c>owl:Thing</c> and <c>owl:Nothing</c> trigger nothing: the clash check
    /// reads them at the level fixpoint instead.
    /// </summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="node">The node the fact landed at.</param>
    /// <param name="concept">The concept the fact carries.</param>
    /// <returns><see langword="true"/> when every derived fact fitted inside its bound.</returns>
    private static bool PropagateMembership(ModalExpansionState state, int node, int concept)
    {
        ModalConcept read = state.Ground.Concepts[concept];
        switch(read.Kind)
        {
            case(ModalConceptKind.Name):
            {
                if(state.Ground.Definitions.TryGetValue(read.Symbol, out List<int>? definitions))
                {
                    for(int index = 0; index < definitions.Count; index++)
                    {
                        if(!TryAddMembership(state, node, definitions[index], true))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            case(ModalConceptKind.Intersection):
            {
                for(int index = 0; index < read.Count; index++)
                {
                    if(!TryAddMembership(state, node, state.Ground.IntersectionOperands[read.First + index], true))
                    {
                        return false;
                    }
                }

                return true;
            }
            case(ModalConceptKind.Universal):
            {
                ReadTargets(state.Buffers.Edges, state.Buffers, read.Role, node, state.TargetScratch);
                bool transitive = state.Ground.TransitiveRoles.Contains(read.Role);
                for(int index = 0; index < state.TargetScratch.Count; index++)
                {
                    int target = state.TargetScratch[index];
                    if(!TryAddMembership(state, target, read.Filler, true))
                    {
                        return false;
                    }

                    if(transitive && !TryAddMembership(state, target, concept, true))
                    {
                        return false;
                    }
                }

                return true;
            }
            case(ModalConceptKind.Existential):
            {
                ModalSpawn spawn = new(node, concept);
                if(state.Spawned.Add(spawn))
                {
                    state.Pending.Add(spawn);
                }

                return true;
            }
            default:
            {
                return true;
            }
        }
    }

    /// <summary>Fires the universal delivery one new edge triggers, in the direction the edge arrives from: every universal at the source over the edge's EXACT role delivers its filler to the target, and a told-transitive role additionally pushes the universal itself. Absence of an edge licenses nothing — there is no closed-world negation anywhere in this face.</summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="role">The edge's role.</param>
    /// <param name="source">The edge's source.</param>
    /// <param name="target">The edge's target.</param>
    /// <returns><see langword="true"/> when every derived fact fitted inside its bound.</returns>
    private static bool PropagateEdge(ModalExpansionState state, int role, int source, int target)
    {
        ReadLabel(state.Buffers.Labels, state.Buffers, source, state.ConceptScratch);
        bool transitive = state.Ground.TransitiveRoles.Contains(role);
        for(int index = 0; index < state.ConceptScratch.Count; index++)
        {
            int concept = state.ConceptScratch[index];
            ModalConcept read = state.Ground.Concepts[concept];
            if(read.Kind != ModalConceptKind.Universal || read.Role != role)
            {
                continue;
            }

            if(!TryAddMembership(state, target, read.Filler, true))
            {
                return false;
            }

            if(transitive && !TryAddMembership(state, target, concept, true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Spawns the whole level's existentials at once. The structure is SNAPSHOT
    /// at the batch boundary and the semantic skip check reads the snapshot, so
    /// the spawn count is independent of the order the batch is processed in;
    /// skipping fewer spawns is the conservative direction and costs only nodes.
    /// Each spawn allocates a FRESH node — reusing an existing successor as a
    /// witness is forbidden — and charges ONE rule application for the edge fact
    /// and the membership fact together.
    /// </summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="level">The level the batch's fresh nodes are allocated at.</param>
    /// <returns><see langword="true"/> when the whole batch fitted inside every bound.</returns>
    private static bool SpawnBatch(ModalExpansionState state, int level)
    {
        state.Buffers.Labels.CopyTo(state.Buffers.LabelSnapshot);
        state.Buffers.Edges.CopyTo(state.Buffers.EdgeSnapshot);
        state.Batch.Clear();
        for(int index = 0; index < state.Pending.Count; index++)
        {
            state.Batch.Add(state.Pending[index]);
        }

        state.Pending.Clear();
        for(int index = 0; index < state.Batch.Count; index++)
        {
            ModalSpawn spawn = state.Batch[index];
            ModalConcept read = state.Ground.Concepts[spawn.Concept];
            if(SkipsSpawn(state, spawn.Node, read))
            {
                continue;
            }

            if(state.NodeCount + 1 > state.Bounds.Node)
            {
                state.Trip = ModalExpansionTrip.Node;

                return false;
            }

            if(state.Applications + 1 > state.Bounds.Step)
            {
                state.Trip = ModalExpansionTrip.Step;

                return false;
            }

            int fresh = state.NodeCount;
            state.NodeCount++;
            state.NodesSpawned++;
            state.LabelCounts.Add(0);
            state.MaxDepth = Math.Max(state.MaxDepth, level);
            state.Applications++;
            if(!TryMaterialiseEdge(state, read.Role, spawn.Node, fresh, false) || !TryAddMembership(state, fresh, read.Filler, false))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one pending existential is already witnessed as of the batch boundary: some successor on its role, in the SNAPSHOT edge relation, whose snapshot label already carries the filler. A fresh node allocated inside the batch carries no snapshot bit, so it never witnesses a later spawn in the same batch.</summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="node">The node carrying the existential.</param>
    /// <param name="existential">The existential concept.</param>
    /// <returns><see langword="true"/> when the spawn is skipped.</returns>
    private static bool SkipsSpawn(ModalExpansionState state, int node, ModalConcept existential)
    {
        ReadTargets(state.Buffers.EdgeSnapshot, state.Buffers, existential.Role, node, state.TargetScratch);
        for(int index = 0; index < state.TargetScratch.Count; index++)
        {
            if(TestBit(state.Buffers.LabelSnapshot, state.TargetScratch[index] * state.Buffers.LabelWords, existential.Filler))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds one membership fact, deduplicated per node: a fact already at the
    /// node is no application and no trip. The counted label size excludes
    /// <c>owl:Thing</c> and the intersection concept itself, so the bound and the
    /// reported peak compare the same quantity.
    /// </summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="node">The node the fact lands at.</param>
    /// <param name="concept">The concept the fact carries.</param>
    /// <param name="charge">Whether the fact charges its own rule application; a told fact and a spawn's membership do not, the spawn charging once for both its facts.</param>
    /// <returns><see langword="true"/> when the fact landed or was already present.</returns>
    private static bool TryAddMembership(ModalExpansionState state, int node, int concept, bool charge)
    {
        int rowStart = node * state.Buffers.LabelWords;
        if(TestBit(state.Buffers.Labels, rowStart, concept))
        {
            return true;
        }

        bool counted = state.Ground.Concepts[concept].Kind is not ModalConceptKind.Thing and not ModalConceptKind.Intersection;
        if(counted && state.LabelCounts[node] + 1 > state.Bounds.Label)
        {
            state.Trip = ModalExpansionTrip.Label;

            return false;
        }

        if(charge && state.Applications + 1 > state.Bounds.Step)
        {
            state.Trip = ModalExpansionTrip.Step;

            return false;
        }

        SetBit(state.Buffers.Labels, rowStart, concept);
        if(counted)
        {
            state.LabelCounts[node]++;
            state.PeakLabel = Math.Max(state.PeakLabel, state.LabelCounts[node]);
        }

        if(charge)
        {
            state.Applications++;
        }

        state.Worklist.Enqueue(new ModalFact(ModalFactKind.Membership, node, concept, 0));

        return true;
    }

    /// <summary>
    /// Materialises one edge and closes it EAGERLY under the told inverse pairs,
    /// so no later rule ever observes an unmirrored edge. The closure is a
    /// bounded worklist over the told inverse relation and never recurses; the
    /// seed edge charges an application only where its caller does not charge one
    /// for it, and every mirrored edge charges its own.
    /// </summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="role">The seed edge's role.</param>
    /// <param name="source">The seed edge's source.</param>
    /// <param name="target">The seed edge's target.</param>
    /// <param name="chargeSeed">Whether the seed edge charges its own rule application.</param>
    /// <returns><see langword="true"/> when the whole closure fitted inside every bound.</returns>
    private static bool TryMaterialiseEdge(ModalExpansionState state, int role, int source, int target, bool chargeSeed)
    {
        state.EdgeClosure.Clear();
        state.EdgeClosure.Enqueue(new ModalEdge(role, source, target));
        bool charge = chargeSeed;
        while(state.EdgeClosure.Count > 0)
        {
            ModalEdge edge = state.EdgeClosure.Dequeue();
            int rowStart = EdgeRow(state.Buffers, edge.Role, edge.Source);
            if(TestBit(state.Buffers.Edges, rowStart, edge.Target))
            {
                charge = true;
                continue;
            }

            if(state.Edges + 1 > state.Bounds.Edge)
            {
                state.Trip = ModalExpansionTrip.Edge;

                return false;
            }

            if(charge && state.Applications + 1 > state.Bounds.Step)
            {
                state.Trip = ModalExpansionTrip.Step;

                return false;
            }

            SetBit(state.Buffers.Edges, rowStart, edge.Target);
            state.Edges++;
            if(charge)
            {
                state.Applications++;
            }

            state.Worklist.Enqueue(new ModalFact(ModalFactKind.Edge, edge.Role, edge.Source, edge.Target));
            if(state.Ground.InverseRoles.TryGetValue(edge.Role, out List<int>? partners))
            {
                for(int index = 0; index < partners.Count; index++)
                {
                    state.EdgeClosure.Enqueue(new ModalEdge(partners[index], edge.Target, edge.Source));
                }
            }

            charge = true;
        }

        return true;
    }

    /// <summary>
    /// Scans the level fixpoint for a clash, node by node in allocation order:
    /// an <c>owl:Nothing</c> membership first, then a node-local numeric
    /// contradiction — an unqualified minimum strictly above an unqualified
    /// maximum on one (property IRI, property kind) pair, an exact bound reading
    /// as its minimum and maximum halves together. No bound is ever compared
    /// against a COUNT of materialised successors: that would need a distinctness
    /// assumption the face never makes.
    /// </summary>
    /// <param name="state">The expansion state.</param>
    /// <param name="reason">The named clash reason; empty when no clash was found.</param>
    /// <returns><see langword="true"/> when the fixpoint carries a clash.</returns>
    private static bool TryFindClash(ModalExpansionState state, out string reason)
    {
        reason = string.Empty;
        for(int node = 0; node < state.NodeCount; node++)
        {
            int rowStart = node * state.Buffers.LabelWords;
            if(state.Ground.NothingConcept >= 0 && TestBit(state.Buffers.Labels, rowStart, state.Ground.NothingConcept))
            {
                reason = ModalExpansionClashReasons.AssertedNothingMembership;

                return true;
            }

            ReadLabel(state.Buffers.Labels, state.Buffers, node, state.ClashScratch);
            for(int left = 0; left < state.ClashScratch.Count; left++)
            {
                ModalConcept minimum = state.Ground.Concepts[state.ClashScratch[left]];
                if(minimum.Kind != ModalConceptKind.Bound || !CarriesMinimum(minimum))
                {
                    continue;
                }

                for(int right = 0; right < state.ClashScratch.Count; right++)
                {
                    ModalConcept maximum = state.Ground.Concepts[state.ClashScratch[right]];
                    if(maximum.Kind == ModalConceptKind.Bound
                        && CarriesMaximum(maximum)
                        && maximum.Symbol == minimum.Symbol
                        && maximum.DataSide == minimum.DataSide
                        && minimum.Bound > maximum.Bound)
                    {
                        reason = ModalExpansionClashReasons.NodeLocalNumericBound(state.Ground.Symbols[minimum.Symbol]);

                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>The first word index of one role's row for one source node inside an edge region.</summary>
    /// <param name="buffers">The packed working set.</param>
    /// <param name="role">The role.</param>
    /// <param name="source">The source node.</param>
    /// <returns>The row's first word index.</returns>
    private static int EdgeRow(ModalExpansionBuffers buffers, int role, int source)
    {
        return ((role * buffers.NodeCapacity) + source) * buffers.NodeWords;
    }

    /// <summary>Reads one node's label into a scratch list, so the caller iterates a snapshot rather than a table its own derivations extend.</summary>
    /// <param name="labels">The label region to read.</param>
    /// <param name="buffers">The packed working set.</param>
    /// <param name="node">The node.</param>
    /// <param name="conceptsToAppendTo">The scratch list the concepts are read into.</param>
    private static void ReadLabel(ReadOnlySpan<ulong> labels, ModalExpansionBuffers buffers, int node, List<int> conceptsToAppendTo)
    {
        conceptsToAppendTo.Clear();
        ReadRow(labels, node * buffers.LabelWords, buffers.LabelWords, conceptsToAppendTo);
    }

    /// <summary>Reads one node's successors on one role into a scratch list, for the same reason a label is read into one.</summary>
    /// <param name="edges">The edge region to read.</param>
    /// <param name="buffers">The packed working set.</param>
    /// <param name="role">The role.</param>
    /// <param name="source">The source node.</param>
    /// <param name="targetsToAppendTo">The scratch list the targets are read into.</param>
    private static void ReadTargets(ReadOnlySpan<ulong> edges, ModalExpansionBuffers buffers, int role, int source, List<int> targetsToAppendTo)
    {
        targetsToAppendTo.Clear();
        ReadRow(edges, EdgeRow(buffers, role, source), buffers.NodeWords, targetsToAppendTo);
    }

    /// <summary>Reads the set positions of one packed row into a scratch list, low bit first.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="rowWords">The words the row spans.</param>
    /// <param name="positionsToAppendTo">The scratch list the positions are read into.</param>
    private static void ReadRow(ReadOnlySpan<ulong> words, int rowStart, int rowWords, List<int> positionsToAppendTo)
    {
        for(int index = 0; index < rowWords; index++)
        {
            ulong bits = words[rowStart + index];
            while(bits != 0)
            {
                int offset = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                positionsToAppendTo.Add((index * ModalExpansionWordBits) + offset);
            }
        }
    }

    /// <summary>Reads one bit of a packed row.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="index">The bit position.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    private static bool TestBit(ReadOnlySpan<ulong> words, int rowStart, int index)
    {
        return (words[rowStart + (index / ModalExpansionWordBits)] & (1UL << (index % ModalExpansionWordBits))) != 0;
    }

    /// <summary>Sets one bit of a packed row.</summary>
    /// <param name="words">The region holding the row.</param>
    /// <param name="rowStart">The row's first word index inside the region.</param>
    /// <param name="index">The bit position.</param>
    private static void SetBit(Span<ulong> words, int rowStart, int index)
    {
        words[rowStart + (index / ModalExpansionWordBits)] |= 1UL << (index % ModalExpansionWordBits);
    }
}
