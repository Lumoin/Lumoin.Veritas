using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The named remainder constants and format helpers the clausifier reports,
/// under the shared remainder-naming convention: whole-axiom rejections use the
/// axiom's <see cref="object.GetType"/> name directly; expression-level and
/// RBox-guard rejections carry a position-qualified sentence and the shapes
/// centralised here.
/// </summary>
internal static class ContextRemainderNames
{
    /// <summary>The whole-module rejection for an irregular RBox (a cycle in the regularity order).</summary>
    public const string RboxIrregular = "RboxIrregular(role-cycle)";

    /// <summary>The whole-module rejection for a role automaton that exceeded the per-module state budget.</summary>
    public const string RboxAutomatonBudget = "RboxAutomaton(state-budget-exceeded)";

    /// <summary>The rejection for a number restriction over a non-simple role.</summary>
    /// <param name="roleIri">The non-simple role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string NonSimpleRoleInNumberRestriction(Utf8String roleIri)
    {
        return $"NonSimpleRoleInNumberRestriction({roleIri})";
    }

    /// <summary>The rejection for a number restriction whose counted role can carry a loop (a self, reflexive, or irreflexive base, closed upward over the role hierarchy): the context-literal equality grammar cannot express the owner-successor diagonal such a merge forces, so the whole module delegates.</summary>
    /// <param name="roleIri">The counted role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string LoopCapableRoleInNumberRestriction(Utf8String roleIri)
    {
        return $"LoopCapableRoleInNumberRestriction({roleIri})";
    }

    /// <summary>The rejection for a self restriction over a non-simple role (KR 2006 Definition 5 restricts self restrictions to simple roles).</summary>
    /// <param name="roleIri">The non-simple role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string NonSimpleRoleInSelfRestriction(Utf8String roleIri)
    {
        return $"NonSimpleRoleInSelfRestriction({roleIri})";
    }

    /// <summary>The rejection for an irreflexivity assertion over a non-simple role (KR 2006 admits irreflexivity only on simple roles).</summary>
    /// <param name="roleIri">The non-simple role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string NonSimpleRoleInIrreflexivity(Utf8String roleIri)
    {
        return $"NonSimpleRoleInIrreflexivity({roleIri})";
    }

    /// <summary>The rejection for an asymmetry assertion over a non-simple role (KR 2006 admits asymmetry only on simple roles).</summary>
    /// <param name="roleIri">The non-simple role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string NonSimpleRoleInAsymmetry(Utf8String roleIri)
    {
        return $"NonSimpleRoleInAsymmetry({roleIri})";
    }

    /// <summary>The rejection for a role-disjointness assertion over a non-simple operand (KR 2006 admits role disjointness only on simple roles).</summary>
    /// <param name="roleIri">The non-simple operand's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string NonSimpleRoleInRoleDisjointness(Utf8String roleIri)
    {
        return $"NonSimpleRoleInRoleDisjointness({roleIri})";
    }

    /// <summary>The rejection for an asymmetry assertion whose property is <c>owl:topObjectProperty</c>, whose universal extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInAsymmetry(Utf8String roleIri)
    {
        return $"ReservedRoleInAsymmetry({roleIri})";
    }

    /// <summary>The rejection for a role-disjointness assertion whose operand is <c>owl:topObjectProperty</c>, whose universal extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved operand's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInRoleDisjointness(Utf8String roleIri)
    {
        return $"ReservedRoleInRoleDisjointness({roleIri})";
    }

    /// <summary>The rejection for a role-hierarchy axiom (a sub-, equivalent-, or inverse-object-property spelling) mentioning a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>), whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInRoleHierarchy(Utf8String roleIri)
    {
        return $"ReservedRoleInRoleHierarchy({roleIri})";
    }

    /// <summary>The rejection for a property-chain axiom mentioning a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>) as a chain link or the super role, whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInPropertyChain(Utf8String roleIri)
    {
        return $"ReservedRoleInPropertyChain({roleIri})";
    }

    /// <summary>The rejection for an object-property domain axiom whose property is a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>), whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInDomain(Utf8String roleIri)
    {
        return $"ReservedRoleInDomain({roleIri})";
    }

    /// <summary>The rejection for an object-property range axiom whose property is a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>), whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInRange(Utf8String roleIri)
    {
        return $"ReservedRoleInRange({roleIri})";
    }

    /// <summary>The rejection for an object-property characteristic (other than asymmetry) whose property is a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>), whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInCharacteristic(Utf8String roleIri)
    {
        return $"ReservedRoleInCharacteristic({roleIri})";
    }

    /// <summary>The rejection for a class-expression role position (an existential, universal, cardinality, self, or has-value restriction) over a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>), whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="roleIri">The reserved role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInClassExpression(Utf8String roleIri)
    {
        return $"ReservedRoleInClassExpression({roleIri})";
    }

    /// <summary>A position-qualified rejection for a class-expression construct outside the clausifier's fragment.</summary>
    /// <param name="construct">The rejected construct's type name.</param>
    /// <param name="position">The polarity position (<c>subclass</c> or <c>superclass</c>).</param>
    /// <param name="increment">The increment that will admit the construct.</param>
    /// <returns>The named rejection sentence.</returns>
    public static string ExpressionRejection(string construct, string position, string increment)
    {
        return $"{construct} in a {position} position is outside the context clausifier ({increment}).";
    }

    /// <summary>The rejection for a data restriction outside the context datatype fragment: a subclass-position data universal (its NNF dual is a value-forcing disjunct), or a ranged or higher-bound data cardinality (a range-less min-of-one / max-of-zero / exact-of-zero lowers through the <c>HasValueOf</c> value-existence atom instead), or an n-ary data shape at either polarity. The subclass-position existential and has-value lower to their NNF-dual universal markers and do not reject.</summary>
    /// <param name="construct">The rejected data restriction's type name.</param>
    /// <param name="position">The polarity position (<c>subclass</c> or <c>superclass</c>).</param>
    /// <returns>The named rejection sentence.</returns>
    public static string DataExpressionRejection(string construct, string position)
    {
        return $"{construct} in a {position} position is outside the context datatype fragment.";
    }

    /// <summary>The rejection for a data restriction over a reserved built-in data property (<c>owl:topDataProperty</c> or <c>owl:bottomDataProperty</c>), whose fixed universal/empty extension the context path does not interpret.</summary>
    /// <param name="property">The reserved data property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedDataProperty(Utf8String property)
    {
        return $"ReservedDataProperty({property})";
    }

    /// <summary>The rejection for an object-property assertion whose property is a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>): the top edge is a tautology and the bottom edge an immediate inconsistency, neither of whose fixed extension the context path interprets, so the whole module delegates.</summary>
    /// <param name="roleIri">The reserved property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInObjectPropertyAssertion(Utf8String roleIri)
    {
        return $"ReservedRoleInObjectPropertyAssertion({roleIri})";
    }

    /// <summary>The rejection for a negative object-property assertion whose property (an inverse unwrapped to its named property) is a reserved built-in (<c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>), whose fixed universal/empty extension the context path does not interpret, so the whole module delegates.</summary>
    /// <param name="roleIri">The reserved property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string ReservedRoleInNegativeObjectPropertyAssertion(Utf8String roleIri)
    {
        return $"ReservedRoleInNegativeObjectPropertyAssertion({roleIri})";
    }

    /// <summary>The rejection for an object-property assertion whose role is in the DL4 counting-capable family (a max-cardinality, decomposed exact, functional, or inverse-functional target, closed down over told sub-roles and inverses): a merge-forcing counting construct over a role with an asserted instance can force a derived ground merge outside the slice, so the whole module delegates.</summary>
    /// <param name="roleIri">The counting-capable role's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string GroundEdgeOnCountingRole(Utf8String roleIri)
    {
        return $"GroundEdgeOnCountingRole({roleIri})";
    }

    /// <summary>The rejection for an <c>RdfTerm</c> literal occupying an individual position of an admitted ABox axiom — outside the named/anonymous-individual slice, so the whole module delegates.</summary>
    public const string GroundIndividualIsLiteral = "GroundIndividualIsLiteral";

    /// <summary>The whole-module rejection for an asserted data property in a KEPT co-occurrence position of the key-data router: a sub-property, equivalence, disjointness, or negative-assertion axiom naming it, or a <c>DataSomeValuesFrom</c>/<c>DataAllValuesFrom</c>/<c>DataCardinality</c> restriction over it — the positions whose value propagation, cross-property comparison, or counting the slice does not carry. The lifted positions (domain, range, functional, <c>DataHasValue</c>) lower to engine demands instead of rejecting.</summary>
    /// <param name="property">The asserted data property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string AssertedDataPropertyBeyondKeys(Utf8String property)
    {
        return $"AssertedDataPropertyBeyondKeys({property})";
    }

    /// <summary>The rejection for a <c>HasKey</c> axiom whose keyed class is neither a named class nor <c>owl:Thing</c>: the atom-only certain-membership readout cannot compute complex-class membership without a defining clause, so the whole module delegates.</summary>
    /// <param name="construct">The keyed class expression's type name.</param>
    /// <returns>The named rejection.</returns>
    public static string HasKeyClassNotAtomic(string construct)
    {
        return $"HasKeyClassNotAtomic({construct})";
    }

    /// <summary>The rejection for a <c>HasKey</c> axiom with an empty key-property list, whose degenerate semantics forces all named instances of the keyed class pairwise equal — a shape the key-value join cannot express, so the whole module delegates.</summary>
    public const string HasKeyEmptyKeyList = "HasKeyEmptyKeyList";

    /// <summary>The rejection for a data-key value comparison the datatype checker answers <c>Indeterminate</c>: the join can neither merge (unsound) nor treat the values as distinct (incomplete), so the whole module delegates.</summary>
    /// <param name="property">The data key property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string KeyValueComparisonIndeterminate(Utf8String property)
    {
        return $"KeyValueComparisonIndeterminate({property})";
    }

    /// <summary>The whole-module rejection for a <c>HasKey</c> axiom co-occurring with a nominal construct: the key tier's membership readback runs over ground contexts, which a nominal-jurisdiction module bypasses in favour of the root context, so the whole module delegates until the key join reads root ground facts.</summary>
    public const string KeyOnNominalModule = "KeyOnNominalModule";

    /// <summary>The whole-module rejection for an object key property whose post-quotient representative is inverse-direction on a nominal-jurisdiction module: the root key join reads the per-constant index, which projects forward-representative edges only, so an inverse-direction key demand has no sound readout there and the whole module delegates.</summary>
    /// <param name="roleIri">The key property's IRI.</param>
    /// <returns>The named rejection.</returns>
    public static string InverseKeyRoleOnNominalModule(Utf8String roleIri)
    {
        return $"InverseKeyRoleOnNominalModule({roleIri})";
    }

    /// <summary>The whole-module rejection for an anonymous individual (a blank node) in a NOMINAL position — an <c>ObjectOneOf</c> member or an <c>ObjectHasValue</c> filler: a blank node is existential, and treating one as a constant is a skolemization whose entailment-direction soundness this fragment does not argue, so the whole module delegates.</summary>
    public const string AnonymousIndividualInNominal = "AnonymousIndividualInNominal";

    /// <summary>The whole-module rejection for a nominal-jurisdiction module whose frozen signature exceeds the packed <c>f(o)</c> term's field widths (function symbols or individuals): the root-context terms cannot represent every symbol combination, so the whole module delegates instead of overflowing mid-saturation.</summary>
    public const string PackedTermWidthExceeded = "PackedTermWidthExceeded";

    /// <summary>The delegation taxonomy entry for a data demand landing ON a root context that cannot be decided in place (a <c>D(o)</c> marker head): the datatype sidecar is per-context and constant-blind. The per-constant root arm decides such demands per ≈-class and delegates only the residual it cannot size under the <c>DataObligationUndecidedOnRoot</c> name; the <see cref="ContextSaturationEngine.RootDataDemandObserved"/> statistic keeps the whether-any-demand-reaches-the-root question measurable.</summary>
    public const string DataDemandOnRootContext = "DataDemandOnRootContext";

    /// <summary>The named delegation for a key-class membership riding a MULTI-literal live head at a root-class constant at the completed fixpoint (P-GC1): the disjunctive membership can force a clashing merge on one branch, so the root key join can neither fire nor certify completeness. Detected by the engine's <c>HasUndecidedRootKeyObligation</c> latch — the root-tier sibling of the ground tier's undecided-key discipline.</summary>
    public const string KeyMembershipUndecidedOnRoot = "KeyMembershipUndecidedOnRoot";

    /// <summary>The named delegation for a live root-class ground equality between two key candidates (or demand-bearing constants) whose sides the ≈-class surface did not merge at the completed fixpoint: an off-fold equality relay (an <c>A → A</c> tautology, a carried equality disjunct) leaves an identity the read-time union cannot see, so the join is incomplete. The backstop checks the invariant directly, regardless of derivation channel, and the module delegates — conservative until root-tier equality-relay completeness is proved.</summary>
    public const string RootEqualityOutsideFold = "RootEqualityOutsideFold";

    /// <summary>The named delegation for a per-constant root data obligation the datatype sidecar left UNDECIDED: a root ≈-class's pooled demand set over a data property could be neither realized nor refuted, so the reasoner delegates the module named — the root-tier per-constant sibling of the ordinary-context undecided-data-obligation discipline. Detected by the engine's <c>HasDataObligationUndecidedOnRoot</c> latch, set only under the root data-obligation arm.</summary>
    /// <param name="property">The data property's IRI whose demand stayed undecided.</param>
    /// <returns>The named delegation.</returns>
    public static string DataObligationUndecidedOnRoot(Utf8String property)
    {
        return $"DataObligationUndecidedOnRoot({property})";
    }
}

/// <summary>
/// The outcome of clausifying a module: the emitted DL-clauses, the named
/// remainder (information, not a verdict), the interning table,
/// the term order built over the clauses, and the counters the battery and the
/// future statistics slot read.
/// </summary>
/// <param name="Clauses">The emitted DL-clauses.</param>
/// <param name="Remainder">The named beyond-fragment / rejected constructs; a whole-module RBox rejection is the sole entry when the module is refused.</param>
/// <param name="Symbols">The per-module interning table.</param>
/// <param name="Order">The context term order computed over <paramref name="Clauses"/>.</param>
/// <param name="AutomatonStates">The number of automaton states allocated across all role automata.</param>
/// <param name="AutomatonBudgetExceeded">Whether a role automaton exceeded the per-module state budget (a whole-module rejection).</param>
/// <param name="FreshAtoms">The number of fresh structural / automaton-state concept atoms minted.</param>
/// <param name="FreshRoles">The number of fresh automaton-state / counting roles minted.</param>
/// <param name="CountingRoles">The number of DL4 counting roles minted — a subset of <paramref name="FreshRoles"/>; the second gate admits a clausification whose fresh-role count equals its counting-role count.</param>
/// <param name="NegativePolarityDataMarkers">The number of negative-polarity data dual disjuncts emitted — one per subclass-position data existential or has-value lowered to its universal-marker NNF dual; zero for a module carrying no negative-position data restriction.</param>
/// <param name="DataDemandDescriptors">The context data-demand marker atoms, keyed by concept-atom id, each recording the concrete-domain obligation the marker stands for — the side table the saturation engine reconstructs the datatype-sidecar obligations from; empty for a module carrying no admitted data restriction.</param>
/// <param name="DataBox">The module's data-property RBox — the sub-property closure, functional set, disjoint pairs, and asserted ranges the saturation engine hands the shared datatype sidecar; <see cref="DataPropertyBox.Empty"/> for a module carrying no data-property axiom.</param>
/// <param name="GroundClash">Whether a ground clash was decided at clausification — a pre-merge representative collision or a closure clash over the asserted-edge graph; the reasoner answers <c>Decided(inconsistent)</c> without saturating.</param>
/// <param name="GroundClashReason">The information string naming which ground check fired, or <see langword="null"/> when <paramref name="GroundClash"/> is <see langword="false"/>.</param>
/// <param name="PreMergeUnions">The number of <c>SameIndividual</c> unions the pre-merge pass performed (distinct representatives merged).</param>
/// <param name="GroundRepresentatives">The individual representatives mentioned in the admitted ABox axioms, in first-seen order — the ground contexts the saturation setup mints.</param>
/// <param name="GroundMarkers">The fresh marker concept atom per representative (<c>O_a</c>), the ground context core the setup seeds; never a signature class.</param>
/// <param name="GroundTargetByFunction">The representative each ground-edge function symbol denotes — the designated-successor routing the saturation setup resolves to a ground context id.</param>
/// <param name="GroundGraph">The representative-level asserted-edge closure the reasoner re-runs for the post-saturation Self-ghost pass; empty for a module carrying no admitted ABox axiom.</param>
/// <param name="GroundSelfLoopConcepts">The loop concept atom (<c>Self_p</c>) mapped to its forward-base representative role — the inverse of the clausifier's loop-concept mint. The Self-ghost pass reads an unconditionally derived <c>Self_p(x)</c> head of a ground context off this map to contribute the loop <c>p(a, a)</c> to the re-closure through the representative's raw-member widening; empty for a module minting no loop concept.</param>
/// <param name="KeyForcedUnions">The number of unions the round-0 told key-value join performed — a non-zero count hands the module to the reasoner's derived-merge fixpoint for a seeded re-clausification round.</param>
/// <param name="KeyDescriptors">The ground key descriptors, ONE per <c>HasKey</c> axiom (each fires independently) — the reasoner's post-saturation join re-reads these against derived-certain memberships.</param>
/// <param name="KeyValueStore">The asserted data-key values per ground representative and data property — the value side of the key join, compared in the datatype value space.</param>
/// <param name="ToldMemberships">The told named-class memberships per ground representative as interned class atoms — the round-0 join's membership predicate and the counting rider's qualified-filler check.</param>
/// <param name="NamedRoots">The representatives whose merged equivalence class contains a named (IRI-denoted) individual — the key join's named guard tests the CLASS, not the representative token.</param>
/// <param name="KeyUnionPairs">The root pairs the round-0 join merged — the seeds of the reasoner's next fixpoint round, whose re-clausification rebuilds every ground structure under the merged representatives.</param>
/// <param name="RootFacts">The ground root-context clauses of a nominal-jurisdiction module's ABox — class assertions as <c>⊤ → B(o)</c>, property assertions as <c>⊤ → S(o, o′)</c>, individual (in)equalities as <c>⊤ → o ≈ o′</c> / <c>⊤ → o ≉ o′</c>, negative property assertions as <c>S(o, o′) → ⊥</c> — seeded directly into the root context at engine setup, never emitted as ontology DL-clauses (the published DL-clause grammar bars constants in bodies); empty without nominal jurisdiction, whose ABox takes the ground-context slice instead.</param>
/// <param name="NominalJurisdiction">The jurisdiction bit: whether the module carries a nominal construct, so its ontology clauses may carry the constant-bearing head literals the second gate admits under nominal jurisdiction only, its ABox rides <paramref name="RootFacts"/>, and the engine mints the root context. <see langword="false"/> on a whole-module rejection made before the nominal scan ran (the rejection delegates regardless).</param>
/// <param name="NominalClash">Whether the enumeration-CSP decider's clash-only face decided a told clash on the nominal arm — a forced-merge collapse of a told-distinct pair or a counted told-distinct clique exceeding its cap; the reasoner answers <c>Decided(inconsistent)</c> without saturating, ahead of the second gate (an inconsistency condemns the module regardless of any remainder). Always <see langword="false"/> with the face dark.</param>
/// <param name="NominalClashReason">The information string naming which nominal check fired, or <see langword="null"/> when <paramref name="NominalClash"/> is <see langword="false"/>.</param>
/// <param name="NominalWindow">The clash-only face's window measurement — counted population, told-distinct clique, cap bound, and the per-bound silences — computed on every nominal-jurisdiction module regardless of the face's enable flag (the census ships unconditionally); <see cref="NominalCountingWindow.Empty"/> without nominal jurisdiction.</param>
internal sealed record ClausificationResult(
    IReadOnlyList<DlClause> Clauses,
    IReadOnlyList<string> Remainder,
    ContextSymbolTable Symbols,
    ContextTermOrder Order,
    int AutomatonStates,
    bool AutomatonBudgetExceeded,
    int FreshAtoms,
    int FreshRoles,
    int CountingRoles,
    int NegativePolarityDataMarkers,
    IReadOnlyDictionary<int, DataDemandDescriptor> DataDemandDescriptors,
    DataPropertyBox DataBox,
    bool GroundClash,
    string? GroundClashReason,
    int PreMergeUnions,
    IReadOnlyList<Utf8String> GroundRepresentatives,
    IReadOnlyDictionary<Utf8String, int> GroundMarkers,
    IReadOnlyDictionary<int, Utf8String> GroundTargetByFunction,
    GroundAssertionGraph GroundGraph,
    IReadOnlyDictionary<int, RoleRepresentative> GroundSelfLoopConcepts,
    int KeyForcedUnions,
    IReadOnlyList<GroundKeyDescriptor> KeyDescriptors,
    IReadOnlyDictionary<Utf8String, Dictionary<Utf8String, List<Literal>>> KeyValueStore,
    IReadOnlyDictionary<Utf8String, HashSet<int>> ToldMemberships,
    IReadOnlySet<Utf8String> NamedRoots,
    IReadOnlyList<(Utf8String First, Utf8String Second)> KeyUnionPairs,
    IReadOnlyList<DlClause> RootFacts,
    bool NominalJurisdiction,
    bool NominalClash,
    string? NominalClashReason,
    NominalCountingWindow NominalWindow);

/// <summary>
/// One <c>HasKey</c> axiom's ground join descriptor: the keyed class as an
/// interned atom (or the <c>owl:Thing</c> flag making every named representative
/// a candidate), the object key properties as directioned role ids, and the data
/// key properties by IRI. Descriptors fire INDEPENDENTLY — agreement on all of
/// one axiom's properties suffices for its merge; a per-class concatenation
/// would demand the union and under-fire a forced merge.
/// </summary>
/// <param name="ClassAtom">The keyed named class's interned concept atom; unused when <paramref name="ClassIsThing"/> is set.</param>
/// <param name="ClassIsThing">Whether the keyed class is <c>owl:Thing</c>, making every named ground representative a candidate.</param>
/// <param name="ObjectRoles">The object key properties as raw directioned roles — the closed graph's query keys; a shared value must be a NAMED ground representative.</param>
/// <param name="RootObjectRoles">The object key properties resolved to their post-quotient representatives — the root key join's query keys against the per-constant index's forward-representative symbols; empty until the post-RBox resolution runs, and forward-direction throughout consumption because an inverse-direction representative delegates the module at that resolution.</param>
/// <param name="DataProperties">The data key properties by IRI; shared values compare in the datatype value space.</param>
internal sealed record GroundKeyDescriptor(
    int ClassAtom,
    bool ClassIsThing,
    IReadOnlyList<RawRoleId> ObjectRoles,
    IReadOnlyList<RoleRepresentative> RootObjectRoles,
    IReadOnlyList<Utf8String> DataProperties);

/// <summary>
/// The OwlAxiom to DL-clause front end for the consequence-based SRIQ calculus
/// (KR 2016; <see href="https://arxiv.org/abs/1602.04498"/>): GCI normalization
/// with polarity-correct fresh names, RBox processing (reflexive-transitive closure,
/// quotienting by mutual-inclusion equivalence, the regularity check,
/// simple-role computation, role automaton construction, chain elimination), and
/// KR 2016 Table 1 clause emission over an interned, directioned signature. The
/// role automaton construction follows Decidability of SHIQ with Complex Role
/// Inclusion Axioms, Artificial Intelligence 160, 2004, Definition 10
/// (<see href="https://doi.org/10.1016/j.artint.2004.06.002"/>); chain elimination
/// follows RIQ and SROIQ are Harder than SHOIQ, KR 2008, Lemma 10
/// (<see href="https://cdn.aaai.org/KR/2008/KR08-027.pdf"/>). The
/// decidability guards live here: an irregular RBox or a number restriction over
/// a non-simple role is refused, named, never wedged. No stage throws for
/// recoverable input; every refusal is a named remainder entry. A module-level
/// reserved-role scan runs first, before intake: any <c>owl:topObjectProperty</c>
/// or <c>owl:bottomObjectProperty</c> mention in a role position rejects the whole
/// module with a per-position named remainder, with two carve-outs — a
/// <c>owl:bottomObjectProperty</c> operand of <c>DisjointObjectProperties</c> or
/// of an <c>Asymmetric</c> characteristic stays admitted, its empty extension
/// making the emitted emptiness clause a sound tautology.
/// </summary>
/// <remarks>
/// Roles are quotiented by <c>⊑*</c>-equivalence before any clause or automaton
/// is built: every role occurrence — clause atoms, RIA words, guards, automaton
/// letters — is rewritten to its class's canonical minimal representative, so
/// mutual and equivalent inclusions cannot make the automaton construction chase
/// itself; the automata are then built iteratively in letter-dependency order, so
/// the letter-dependency relation stays a strict DAG. House rules throughout: no
/// recursion (explicit worklists), value-based control flow (guard rejections are
/// return values). The result is consumed in production by
/// <see cref="Lumoin.Veritas.Owl.Reasoning.ContextSaturationModuleReasoner"/>:
/// the second gate reads the clause grammar, the saturation engine seeds from
/// the clauses, and the enumeration-CSP decider's clash face decides a
/// <see cref="ClausificationResult.NominalClash"/> pre-engine.
/// </remarks>
internal static class ContextClausifier
{
    /// <summary>The per-module ceiling on automaton states across all role automata; exceeding it refuses the module rather than wedging on the exponential automaton blow-up HS2004 Lemma 11 proves unavoidable.</summary>
    public const int AutomatonStateBudget = 4096;

    /// <summary>The counting rider's successor ceiling: the pigeonhole clique search is exact up to this many distinct closed successors of one constrained representative and SILENT above it — the module keeps its counting-edge remainder and delegates, never a verdict on an unsearched space.</summary>
    public const int GroundCountingCliqueBound = 16;

    /// <summary>Clausifies a module into DL-clauses, guards, automata, and a named remainder under the default (general-clause) equality lowering.</summary>
    /// <param name="module">The module to clausify.</param>
    /// <returns>The clausification result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static ClausificationResult Clausify(ReasoningModule module)
    {
        return Clausify(module, EqualityLowering.GeneralClause);
    }

    /// <summary>Clausifies a module into DL-clauses, guards, automata, and a named remainder under a selected equality lowering.</summary>
    /// <param name="module">The module to clausify.</param>
    /// <param name="lowering">The functionality lowering: the published general clause, or the successor-sharing V-node reuse of one function symbol per functional directioned role.</param>
    /// <returns>The clausification result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static ClausificationResult Clausify(ReasoningModule module, EqualityLowering lowering)
    {
        return Clausify(module, lowering, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false);
    }

    /// <summary>Clausifies a module under a selected equality lowering with the ground-key machinery and the pre-engine decider faces threaded, taking the vr key-join lift's switch at the production default (on): the datatype registry the data-key value comparisons consult, the seeded unions of the reasoner's derived-merge fixpoint (applied through the union-find before the pre-merge pass), the ground-counting rider's enable flag, and the enumeration-CSP decider's clash-only-face enable flag. A caller that wants the dark key-on-nominal face calls the <c>rootKeyJoinEnabled</c>-threaded overload with <c>false</c> explicitly.</summary>
    /// <param name="module">The module to clausify.</param>
    /// <param name="lowering">The functionality lowering: the published general clause, or the successor-sharing V-node reuse of one function symbol per functional directioned role.</param>
    /// <param name="registry">The registered-datatype set the data-key value comparisons consult.</param>
    /// <param name="seedUnions">The individual-key pairs earlier fixpoint rounds merged, re-applied before the pre-merge pass so the contains-named bit reconstructs as an equivalence-class property.</param>
    /// <param name="riderEnabled">Whether the told ground-counting pigeonhole rider decides clashes; off, the counting-edge remainder delegates exactly as before the rider existed.</param>
    /// <param name="nominalDeciderEnabled">Whether the enumeration-CSP decider's clash-only face decides told clashes on the nominal arm; off, the window measurement still rides the census and every decision stays byte-identical.</param>
    /// <returns>The clausification result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static ClausificationResult Clausify(ReasoningModule module, EqualityLowering lowering, DatatypeRegistry registry, IReadOnlyList<(Utf8String First, Utf8String Second)> seedUnions, bool riderEnabled, bool nominalDeciderEnabled)
    {
        return Clausify(module, lowering, registry, seedUnions, riderEnabled, nominalDeciderEnabled, rootKeyJoinEnabled: true);
    }

    /// <summary>Clausifies a module under a selected equality lowering with the ground-key machinery, the pre-engine decider faces, AND the vr key-join lift's switch threaded: on (production threads it on through the reasoner), a <c>HasKey</c>+nominal module routes past the <c>KeyOnNominalModule</c> guard into intake so the root key join can decide it; off, the guard whole-rejects the whole module.</summary>
    /// <param name="module">The module to clausify.</param>
    /// <param name="lowering">The functionality lowering: the published general clause, or the successor-sharing V-node reuse of one function symbol per functional directioned role.</param>
    /// <param name="registry">The registered-datatype set the data-key value comparisons consult.</param>
    /// <param name="seedUnions">The individual-key pairs earlier fixpoint rounds merged, re-applied before the pre-merge pass so the contains-named bit reconstructs as an equivalence-class property.</param>
    /// <param name="riderEnabled">Whether the told ground-counting pigeonhole rider decides clashes; off, the counting-edge remainder delegates exactly as before the rider existed.</param>
    /// <param name="nominalDeciderEnabled">Whether the enumeration-CSP decider's clash-only face decides told clashes on the nominal arm; off, the window measurement still rides the census and every decision stays byte-identical.</param>
    /// <param name="rootKeyJoinEnabled">Whether the vr key-join lift routes a <c>HasKey</c>+nominal module past the <c>KeyOnNominalModule</c> guard into intake (production threads it on through the reasoner); off leaves the guard whole-rejecting the module.</param>
    /// <returns>The clausification result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/>, <paramref name="registry"/>, or <paramref name="seedUnions"/> is <see langword="null"/>.</exception>
    public static ClausificationResult Clausify(ReasoningModule module, EqualityLowering lowering, DatatypeRegistry registry, IReadOnlyList<(Utf8String First, Utf8String Second)> seedUnions, bool riderEnabled, bool nominalDeciderEnabled, bool rootKeyJoinEnabled)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(seedUnions);

        return new ClausifierState(module, lowering, registry, seedUnions, riderEnabled, nominalDeciderEnabled, rootKeyJoinEnabled).Run();
    }

    /// <summary>Whether the rider's clique sweep runs on the shared struct-enumerator surface (<see cref="CombinationIndexEnumerator"/>) instead of the original in-place odometer. The two implementations are bit-for-bit identical over every input — the A/B harness pins the equivalence across the PIG boundary battery — and the flip is gated on the surface's local benchmark program, not on battery green alone.</summary>
    internal const bool RiderCliqueOnEnumeratorSurface = false;

    /// <summary>Whether some <paramref name="size"/>-subset of the nodes is pairwise told-distinct — an exact iterative combination sweep (an index odometer, no recursion) over the bounded successor list, dispatched to the original in-place odometer or its shared-surface twin by <paramref name="onEnumeratorSurface"/>.</summary>
    /// <param name="nodes">The candidate successor roots.</param>
    /// <param name="size">The clique size sought.</param>
    /// <param name="distinct">The told pairwise-distinct pairs.</param>
    /// <param name="onEnumeratorSurface">Whether the sweep runs on the shared struct-enumerator surface.</param>
    /// <returns><see langword="true"/> when a distinct clique of the size exists.</returns>
    internal static bool HasDistinctClique(List<Utf8String> nodes, int size, HashSet<(Utf8String First, Utf8String Second)> distinct, bool onEnumeratorSurface)
    {
        return onEnumeratorSurface
            ? HasDistinctCliqueOnSurface(nodes, size, distinct)
            : HasDistinctCliqueInPlace(nodes, size, distinct);
    }

    /// <summary>The original in-place odometer arm of <see cref="HasDistinctClique"/>.</summary>
    /// <param name="nodes">The candidate successor roots.</param>
    /// <param name="size">The clique size sought.</param>
    /// <param name="distinct">The told pairwise-distinct pairs.</param>
    /// <returns><see langword="true"/> when a distinct clique of the size exists.</returns>
    private static bool HasDistinctCliqueInPlace(List<Utf8String> nodes, int size, HashSet<(Utf8String First, Utf8String Second)> distinct)
    {
        if(size > nodes.Count)
        {
            return false;
        }

        int[] indices = new int[size];
        for(int i = 0; i < size; i++)
        {
            indices[i] = i;
        }

        while(true)
        {
            if(IsDistinctClique(nodes, indices, distinct))
            {
                return true;
            }

            int position = size - 1;
            while(position >= 0 && indices[position] == nodes.Count - size + position)
            {
                position--;
            }

            if(position < 0)
            {
                return false;
            }

            indices[position]++;
            for(int i = position + 1; i < size; i++)
            {
                indices[i] = indices[i - 1] + 1;
            }
        }
    }

    /// <summary>The shared-surface arm of <see cref="HasDistinctClique"/>: the same lexicographic combination sweep on <see cref="CombinationIndexEnumerator"/> over a pooled index buffer.</summary>
    /// <param name="nodes">The candidate successor roots.</param>
    /// <param name="size">The clique size sought.</param>
    /// <param name="distinct">The told pairwise-distinct pairs.</param>
    /// <returns><see langword="true"/> when a distinct clique of the size exists.</returns>
    private static bool HasDistinctCliqueOnSurface(List<Utf8String> nodes, int size, HashSet<(Utf8String First, Utf8String Second)> distinct)
    {
        if(size > nodes.Count)
        {
            return false;
        }

        using VeritasMemoryPool<int> pool = new();
        using CombinationIndexEnumerator combinations = CombinationIndexEnumerator.Create(pool, nodes.Count, size);
        while(combinations.MoveNext())
        {
            if(IsDistinctClique(nodes, combinations.Current, distinct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the indexed subset is pairwise told-distinct.</summary>
    /// <param name="nodes">The candidate successor roots.</param>
    /// <param name="indices">The subset's indices.</param>
    /// <param name="distinct">The told pairwise-distinct pairs.</param>
    /// <returns><see langword="true"/> when every pair is told-distinct.</returns>
    private static bool IsDistinctClique(List<Utf8String> nodes, ReadOnlySpan<int> indices, HashSet<(Utf8String First, Utf8String Second)> distinct)
    {
        for(int i = 0; i < indices.Length; i++)
        {
            for(int j = i + 1; j < indices.Length; j++)
            {
                if(!distinct.Contains((nodes[indices[i]], nodes[indices[j]])))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The per-module mutable clausifier state: interning, the RBox tables, the automata memo, the emitted clauses, and the named remainder.</summary>
    private sealed class ClausifierState
    {
        /// <summary>The context variable neighbour <c>z1</c>.</summary>
        private static DlTerm Z1 { get; } = DlTerm.Neighbour(1);

        /// <summary>The module being clausified.</summary>
        private ReasoningModule Module { get; }

        /// <summary>The functionality lowering: the published general clause, or the successor-sharing V-node reuse of one function symbol per functional directioned role.</summary>
        private EqualityLowering Lowering { get; }

        /// <summary>The interning table.</summary>
        private ContextSymbolTable Symbols { get; } = new();

        /// <summary>The emitted DL-clauses.</summary>
        private List<DlClause> Clauses { get; } = [];

        /// <summary>The named remainder.</summary>
        private List<string> Remainder { get; } = [];

        /// <summary>The pending GCIs from intake, in source order, each tagged with its origin axiom index.</summary>
        private List<PendingGci> Pending { get; } = [];

        /// <summary>The length-1 role inclusions (raw directioned sub, super) — kept as DL5/DL6 and fed to the automata.</summary>
        private List<(RawRoleId Sub, RawRoleId Super)> RoleInclusions { get; } = [];

        /// <summary>The length-at-least-2 role inclusions as stated (a raw directioned chain and its raw directioned super) — never emitted as clauses; their consequences live in the automata built from their representative rewriting.</summary>
        private List<(List<RawRoleId> Chain, RawRoleId Super)> ChainInclusions { get; } = [];

        /// <summary>The raw directioned roles asserted reflexive; the loop-concept emission resolves them to representatives at the Self lowering, while the ground graph seeds their loops raw.</summary>
        private HashSet<RawRoleId> ReflexiveRoles { get; } = [];

        /// <summary>The raw directioned roles asserted irreflexive; the loop-concept emission and simple-role guard resolve them to representatives at the Self lowering, while the ground graph checks their self-loops raw.</summary>
        private HashSet<RawRoleId> IrreflexiveRoles { get; } = [];

        /// <summary>The recorded disjoint-role pairs (raw directioned operands, resolved to representatives at the disjointness emission): a DisjointObjectProperties operand pair, or an asymmetric property paired with its inverse (the KR 2006 <c>Asy(R) ⟺ Dis(R, R⁻)</c> equivalence). The emission applies the simplicity guard and emits one clash clause per pair.</summary>
        private List<DisjointRolePair> DisjointRolePairs { get; } = [];

        /// <summary>The loop set L: the forward-base representative roles whose self-loops the module reasons about — seeded by every Self / Reflexive / Irreflexive role and closed upward over the representative arcs (a loop on a sub-role is a loop on every super-role).</summary>
        private HashSet<RoleRepresentative> LoopRoles { get; } = [];

        /// <summary>The loop concept <c>Self_p</c> minted per forward-base representative in <see cref="LoopRoles"/>, keyed on the base so inverse spellings fold to one atom (<c>p(a, a) ⟺ p⁻(a, a)</c>).</summary>
        private Dictionary<RoleRepresentative, int> LoopConcepts { get; } = [];

        /// <summary>The forward-base representative of every counted role a DL4 emission constrains — the loops×counting guard tests these against the closed loop set L after the upward closure, delegating the module for any counted role that can carry a loop.</summary>
        private HashSet<RoleRepresentative> CountingTargets { get; } = [];

        /// <summary>The raw directioned roles a module-level unqualified <c>≤1</c> makes functional (a <c>Functional(r)</c> forward role or an <c>InverseFunctional(r)</c> inverse role); collected only under <see cref="EqualityLowering.SuccessorSharing"/> and resolved to representatives in <see cref="FunctionalDirectionedRoles"/> once the role quotient exists.</summary>
        private HashSet<RawRoleId> FunctionalRoleIntakeIds { get; } = [];

        /// <summary>The representative directioned roles a module-level unqualified <c>≤1</c> makes functional — the successor-sharing key. A superclass-position existential / min-1 over one of these reuses the class's shared successor symbol; forward and inverse representatives are distinct keys, so directioned functionality never over-shares across directions.</summary>
        private HashSet<RoleRepresentative> FunctionalDirectionedRoles { get; } = [];

        /// <summary>The shared successor function symbol minted once per functional directioned representative role under <see cref="EqualityLowering.SuccessorSharing"/> — every existential / min-1 over that role witnesses through this one symbol, merging same-owner functional successors by construction.</summary>
        private Dictionary<RoleRepresentative, int> SharedSuccessorSymbols { get; } = [];

        /// <summary>The reflexive-transitive role-inclusion closure over raw directioned roles (super-roles per role); a mutual-inclusion class carries arcs between all its members.</summary>
        private Dictionary<RawRoleId, HashSet<RawRoleId>> SuperRoles { get; } = [];

        /// <summary>The non-simple classes as representatives (reachable from a length-at-least-2 inclusion right-hand side through the closure and inverse coupling). Non-simplicity is a class property — marking any raw member marks every member through the mutual arcs the propagation walks — so the set stores each marked member's representative once.</summary>
        private HashSet<RoleRepresentative> NonSimpleRoles { get; } = [];

        /// <summary>The completed epsilon-free role automata, memoised per representative role: primaries filled by the dependency-ordered pass, inverse-direction mirrors on demand.</summary>
        private Dictionary<RoleRepresentative, RoleAutomaton> Automata { get; } = [];

        /// <summary>The representative of each directioned role id under mutual-<c>⊑*</c>-inclusion equivalence, indexed by raw id — the canonical minimal member of the role's class. Roles interned after RBox processing (DL4 counting roles) fall past the list and represent themselves.</summary>
        private List<int> RoleRepresentatives { get; } = [];

        /// <summary>The representative-rewritten length-1 role inclusions feeding automaton arcs: deduplicated, tautologies dropped, closed under inversion except onto a self-inverse (symmetric) super class, whose mirrored arcs STEP 2 supplies instead.</summary>
        private List<(RoleRepresentative Sub, RoleRepresentative Super)> RepArcs { get; } = [];

        /// <summary>The representative-rewritten chain inclusions feeding the automata: deduplicated, closed under inversion except onto a self-inverse (symmetric) super class, whose mirrored words STEP 2 supplies instead.</summary>
        private List<(List<RoleRepresentative> Word, RoleRepresentative Super)> RepChains { get; } = [];

        /// <summary>The chain eliminations normalization deferred — each a universal over a non-simple role — emitted once the automata are built.</summary>
        private List<PendingElimination> PendingEliminations { get; } = [];

        /// <summary>The data-demand marker mint: one marker concept atom per canonical descriptor, owning the descriptor side table riding the result.</summary>
        private DataDemandMint Mint { get; }

        /// <summary>The per-property <c>HasValueOf_d</c> marker atom minted on first demand — emitted beside every value-forcing demand marker and carried up the sub-property hierarchy so a data-property domain fires through the hierarchy join-free.</summary>
        private Dictionary<Utf8String, int> HasValueOfConcepts { get; } = [];

        /// <summary>The direct sub-property edges of the data-property hierarchy (from <c>SubDataPropertyOf</c> and both directions of <c>EquivalentDataProperties</c>), deduplicated, feeding one <c>HasValueOf_d(x) → HasValueOf_e(x)</c> closure clause each.</summary>
        private HashSet<(Utf8String Sub, Utf8String Super)> DataSubEdges { get; } = [];

        /// <summary>The <c>SameIndividual</c> union-find parent map over individual keys (a named individual by IRI, an anonymous one by label); path-compressed at resolution.</summary>
        private Dictionary<Utf8String, Utf8String> IndividualMerges { get; } = [];

        /// <summary>The individual representatives mentioned in the admitted ABox axioms, in first-seen order — the ground contexts the saturation setup mints, deduplicated through <see cref="GroundRepresentativeSet"/>.</summary>
        private List<Utf8String> GroundRepresentatives { get; } = [];

        /// <summary>The set of representatives already registered, guarding <see cref="GroundRepresentatives"/> against duplicates.</summary>
        private HashSet<Utf8String> GroundRepresentativeSet { get; } = [];

        /// <summary>The fresh marker concept atom <c>O_a</c> minted once per representative — the ground context core; the <c>ClassAssertion</c> lowering and the ground-edge body guard read it, and it never enters the signature classes.</summary>
        private Dictionary<Utf8String, int> GroundMarkers { get; } = [];

        /// <summary>The ground-edge function symbol shared by every asserted role on one ordered representative pair — one Skolem symbol per pair, so same-target joins bind one neighbour.</summary>
        private Dictionary<(Utf8String Source, Utf8String Target), int> GroundEdgeSymbols { get; } = [];

        /// <summary>The representative each ground-edge function symbol denotes — the designated-successor routing rider.</summary>
        private Dictionary<int, Utf8String> GroundTargetByFunction { get; } = [];

        /// <summary>The admitted object-property assertions as representative-resolved pending edges — deferred to the emission pass so the role rewrites to its post-quotient representative and the counting scan reads the filled DL4 targets.</summary>
        private List<PendingGroundEdge> PendingGroundEdges { get; } = [];

        /// <summary>The negative object-property obligations the closure decides — a denied raw directioned edge over representatives (the property inverse-normalized through its directioned id).</summary>
        private List<(Utf8String Source, RawRoleId Role, Utf8String Target)> NegativeObligations { get; } = [];

        /// <summary>Whether a literal in an ABox individual position has already named the <see cref="ContextRemainderNames.GroundIndividualIsLiteral"/> remainder, keeping it single.</summary>
        private bool GroundLiteralRejected { get; set; }

        /// <summary>The number of <c>SameIndividual</c> unions that merged two distinct representatives.</summary>
        private int PreMergeUnions { get; set; }

        /// <summary>The registered-datatype set the data-key value comparisons consult; <see cref="DatatypeRegistry.Empty"/> outside the reasoner's threading.</summary>
        private DatatypeRegistry Registry { get; }

        /// <summary>The individual-key pairs earlier derived-merge fixpoint rounds merged, re-applied through <see cref="UnionIndividuals"/> before the pre-merge pass so the contains-named bit reconstructs as an equivalence-class property independent of seed order.</summary>
        private IReadOnlyList<(Utf8String First, Utf8String Second)> SeedUnions { get; }

        /// <summary>Whether the told ground-counting pigeonhole rider decides clashes; off, the counting-edge remainder delegates exactly as before the rider existed.</summary>
        private bool RiderEnabled { get; }

        /// <summary>Whether the enumeration-CSP decider's clash-only face decides told clashes on the nominal arm; off, the window measurement still rides the census and every decision stays byte-identical.</summary>
        private bool NominalDeciderEnabled { get; }

        /// <summary>Whether the vr key-join lift is armed: on (the production default, threaded on through the reasoner), a <c>HasKey</c> axiom co-occurring with a nominal construct routes PAST the <c>KeyOnNominalModule</c> whole-rejection into intake, so the root key join can decide it; off, the guard whole-rejects as it always has and every decision stays byte-identical.</summary>
        private bool RootKeyJoinEnabled { get; }

        /// <summary>The union-find roots whose merged equivalence class contains a named (IRI-denoted) individual — the key join's named guard tests the CLASS, not the representative token, because the union representative is first-seen with no named preference.</summary>
        private HashSet<Utf8String> NamedRoots { get; } = [];

        /// <summary>The asserted data-key values per ground representative and data property IRI — the value side of the key join, compared in the datatype value space.</summary>
        private Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>> KeyValueStore { get; } = [];

        /// <summary>The asserted data-property IRIs the key-data router lowers to engine demands: each co-occurs in a lifted position (a domain, range, or functional axiom, or a <c>DataHasValue</c> restriction), so intake emits the <c>DataHasValue</c> marker GCI for its told assertions; a property outside the set stays inert in the value store.</summary>
        private HashSet<Utf8String> DemandLoweredProperties { get; } = [];

        /// <summary>The told named-class memberships per ground representative as interned class atoms — the round-0 join's membership predicate and the counting rider's qualified-filler check; derived memberships belong to the reasoner's post-saturation join.</summary>
        private Dictionary<Utf8String, HashSet<int>> ToldMemberships { get; } = [];

        /// <summary>The ground key descriptors, one per admitted <c>HasKey</c> axiom, each firing independently.</summary>
        private List<GroundKeyDescriptor> KeyDescriptors { get; } = [];

        /// <summary>The reverse marker map (marker concept atom to its representative) the DL4 emission consults to record a told ground counting constraint.</summary>
        private Dictionary<int, Utf8String> MarkerRepresentatives { get; } = [];

        /// <summary>The told ground counting constraints — a DL4 emission whose subclass atom is a ground marker — the rider's pigeonhole search reads over the closed graph.</summary>
        private List<GroundCountingConstraint> CountingConstraints { get; } = [];

        /// <summary>The number of unions the round-0 told key-value join performed.</summary>
        private int KeyForcedUnions { get; set; }

        /// <summary>The root pairs the round-0 join merged — the seeds handed to the reasoner's next fixpoint round.</summary>
        private List<(Utf8String First, Utf8String Second)> KeyUnionPairs { get; } = [];

        /// <summary>The number of automaton states allocated so far, against <see cref="AutomatonStateBudget"/>.</summary>
        private int AutomatonStates { get; set; }

        /// <summary>Whether an automaton exceeded the state budget — a whole-module rejection.</summary>
        private bool BudgetExceeded { get; set; }

        /// <summary>Initialises the state for a module under a selected equality lowering, the datatype registry, the seeded fixpoint unions, the counting rider's flag, and the enumeration-CSP clash-only face's flag.</summary>
        /// <param name="module">The module to clausify.</param>
        /// <param name="lowering">The functionality lowering: the published general clause, or the successor-sharing V-node reuse of one function symbol per functional directioned role.</param>
        /// <param name="registry">The registered-datatype set the data-key value comparisons consult.</param>
        /// <param name="seedUnions">The individual-key pairs earlier fixpoint rounds merged.</param>
        /// <param name="riderEnabled">Whether the told ground-counting pigeonhole rider decides clashes.</param>
        /// <param name="nominalDeciderEnabled">Whether the enumeration-CSP decider's clash-only face decides told clashes on the nominal arm.</param>
        /// <param name="rootKeyJoinEnabled">Whether the vr key-join lift routes a <c>HasKey</c>+nominal module past the <c>KeyOnNominalModule</c> guard into intake.</param>
        public ClausifierState(ReasoningModule module, EqualityLowering lowering, DatatypeRegistry registry, IReadOnlyList<(Utf8String First, Utf8String Second)> seedUnions, bool riderEnabled, bool nominalDeciderEnabled, bool rootKeyJoinEnabled)
        {
            Module = module;
            Lowering = lowering;
            Registry = registry;
            SeedUnions = seedUnions;
            RiderEnabled = riderEnabled;
            NominalDeciderEnabled = nominalDeciderEnabled;
            RootKeyJoinEnabled = rootKeyJoinEnabled;
            Mint = new DataDemandMint(Symbols.FreshAtom);
        }

        /// <summary>Runs the pipeline: intake, RBox processing (closure, quotient, regularity, simplicity), normalization, dependency-ordered automaton construction, chain-elimination emission, role-hierarchy emission, and the term order.</summary>
        /// <returns>The clausification result.</returns>
        public ClausificationResult Run()
        {
            List<string> reserved = ScanReservedRoles();
            if(reserved.Count > 0)
            {
                return ReservedRoleRejection(reserved);
            }

            string? belt = ScanKeyDataBelt();
            if(belt is not null)
            {
                return WholeModuleRejection(belt);
            }

            string? nominalGuard = ScanNominalJurisdiction();
            if(nominalGuard is not null)
            {
                return WholeModuleRejection(nominalGuard);
            }

            //The seeded unions of earlier fixpoint rounds re-apply through the same
            //union-find path told SameIndividual takes, then the told-union counter
            //resets so PreMergeUnions reports only THIS round's told merges. A
            //nominal-jurisdiction module bypasses the whole ground slice: its ABox
            //routes through the root context as constants (the jurisdiction fork),
            //so the union-find, markers, edges, graph, rider, and key join never see it.
            string? preMergeClash = null;
            if(!NominalJurisdiction)
            {
                foreach((Utf8String first, Utf8String second) in SeedUnions)
                {
                    UnionIndividuals(first, second);
                }

                PreMergeUnions = 0;

                preMergeClash = PreMergeGroundIndividuals();
            }

            Intake();

            if(!ProcessRbox())
            {
                return WholeModuleRejection(ContextRemainderNames.RboxIrregular);
            }

            string? keyRoleGuard = ResolveRootKeyRoles();
            if(keyRoleGuard is not null)
            {
                return WholeModuleRejection(keyRoleGuard);
            }

            SeedLoopSet();
            CollectFunctionalDirectionedRoles();
            NormalizeAndEmit();
            BuildRequiredAutomata();

            if(BudgetExceeded)
            {
                return WholeModuleRejection(ContextRemainderNames.RboxAutomatonBudget);
            }

            EmitEliminations();

            if(BudgetExceeded)
            {
                return WholeModuleRejection(ContextRemainderNames.RboxAutomatonBudget);
            }

            EmitRoleInclusions();
            EmitReflexivity();
            EmitRoleDisjointness();
            EmitHasValueOfClosure();
            CloseLoopSet();
            CheckCountingLoopCapability();
            EmitSelfVariants();

            string? groundClashReason;
            string? nominalClashReason = null;
            NominalCountingWindow nominalWindow = NominalCountingWindow.Empty;
            GroundAssertionGraph graph;
            if(NominalJurisdiction)
            {
                EmitRootFacts();
                if(!DlTerm.FitsFunctionOfIndividual(Symbols.FunctionSymbolCount, Symbols.IndividualCount))
                {
                    return WholeModuleRejection(ContextRemainderNames.PackedTermWidthExceeded);
                }

                //The enumeration-CSP decider's clash-only face and its window
                //measurement run on the told axiom surfaces of every
                //nominal-jurisdiction module: the measurement rides the census
                //unconditionally, and the clash propagates only when the face is
                //lit — dark, every decision stays byte-identical.
                NominalCountingOutcome nominalOutcome = ContextNominalCountingDecider.Run(Module);
                nominalClashReason = NominalDeciderEnabled ? nominalOutcome.ClashReason : null;
                nominalWindow = nominalOutcome.Window;

                groundClashReason = null;
                graph = GroundAssertionGraph.Empty(Symbols);
            }
            else
            {
                EmitGroundEdges();

                graph = BuildGroundGraph();
                graph.Close();

                //The counting scan moved below the closure so the rider's pigeonhole
                //search reads closed successors; with the rider off the scan emits the
                //same remainders it always has — nothing between the sites reads them.
                HashSet<Utf8String> pigeonholeSubjects = [];
                string? pigeonholeClash = RiderEnabled ? RunGroundPigeonholeSearch(graph, pigeonholeSubjects) : null;
                ScanGroundCountingEdges(pigeonholeSubjects);

                string? closureClash = graph.DetectClash();
                string? keyClash = RunGroundKeyJoin(graph);
                groundClashReason = preMergeClash ?? closureClash ?? pigeonholeClash ?? keyClash;

                EmitGroundSelfConcepts(graph);
            }

            ContextTermOrder order = ContextTermOrder.ForModule(Clauses);

            return new ClausificationResult(Clauses, Remainder, Symbols, order, AutomatonStates, BudgetExceeded, Symbols.FreshAtoms, Symbols.FreshRoles, Symbols.CountingRoles, NegativePolarityDataMarkers, Mint.Descriptors, DataPropertyBox.Build(Module.Axioms), groundClashReason is not null, groundClashReason, PreMergeUnions, GroundRepresentatives, GroundMarkers, GroundTargetByFunction, graph, BuildSelfLoopConcepts(), KeyForcedUnions, KeyDescriptors, KeyValueStore, ToldMemberships, NamedRoots, KeyUnionPairs, RootFacts, NominalJurisdiction, nominalClashReason is not null, nominalClashReason, nominalWindow);
        }

        /// <summary>Whether the module carries a nominal construct (<c>ObjectOneOf</c> or <c>ObjectHasValue</c>) anywhere in a class-expression surface — the jurisdiction bit: a nominal-bearing module routes its ENTIRE ABox through the root context as constants and bypasses the ground-context slice whole; a nominal-free module is untouched by this increment.</summary>
        private bool NominalJurisdiction { get; set; }

        /// <summary>The ground root-context clauses of a nominal-jurisdiction module's ABox, seeded into the root context at engine setup.</summary>
        private List<DlClause> RootFacts { get; } = [];

        /// <summary>The pending root object-property assertions of a nominal-jurisdiction module — (source individual, raw directioned role, target individual, origin) — emitted as root facts AFTER the RBox quotient so the role rewrites to its representative like every other role position.</summary>
        private List<(int Source, RawRoleId Role, int Target, int Origin)> PendingRootEdges { get; } = [];

        /// <summary>The pending root negative object-property assertions — the clash forms <c>S(o, o′) → ⊥</c> — emitted after the RBox quotient.</summary>
        private List<(int Source, RawRoleId Role, int Target, int Origin)> PendingRootNegativeEdges { get; } = [];

        /// <summary>The fresh singleton atom <c>N_o</c> per interned individual — the <c>ObjectHasValue</c> normal form's memo: the first request emits the defining DL7 fact <c>⊤ → N_o(o)</c> and DL8 clause <c>N_o(x) → x ≈ o</c> into the global clause list.</summary>
        private Dictionary<int, int> NominalSingletonAtoms { get; } = [];

        /// <summary>
        /// The nominal-jurisdiction pre-scan (before intake): walks every
        /// class-expression surface for <c>ObjectOneOf</c> / <c>ObjectHasValue</c>,
        /// setting the jurisdiction bit, and enforces the two whole-module guards
        /// this increment banks — a <c>HasKey</c> axiom co-occurring with a nominal
        /// construct (<c>KeyOnNominalModule</c>: the key readback runs over ground
        /// contexts the nominal jurisdiction bypasses) and an anonymous individual
        /// in a nominal position (<c>AnonymousIndividualInNominal</c>: a blank node
        /// is existential, not a constant).
        /// </summary>
        /// <returns>A whole-module rejection name, or <see langword="null"/>.</returns>
        private string? ScanNominalJurisdiction()
        {
            bool hasNominal = false;
            bool hasKey = false;
            bool anonymousInNominal = false;
            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                OwlAxiom axiom = Module.Axioms[origin];
                if(axiom is OwlHasKeyAxiom)
                {
                    hasKey = true;
                }

                ScanAxiomForNominals(axiom, ref hasNominal, ref anonymousInNominal);
            }

            NominalJurisdiction = hasNominal;
            if(hasNominal && anonymousInNominal)
            {
                return ContextRemainderNames.AnonymousIndividualInNominal;
            }

            if(hasNominal && hasKey && !RootKeyJoinEnabled)
            {
                return ContextRemainderNames.KeyOnNominalModule;
            }

            return null;
        }

        /// <summary>Scans one axiom's class-expression surfaces for nominal constructs — the same surfaces the reserved-role scan walks.</summary>
        /// <param name="axiom">The axiom.</param>
        /// <param name="hasNominal">Set when a nominal construct occurs.</param>
        /// <param name="anonymousInNominal">Set when a blank node occupies a nominal position.</param>
        private static void ScanAxiomForNominals(OwlAxiom axiom, ref bool hasNominal, ref bool anonymousInNominal)
        {
            switch(axiom)
            {
                case(OwlObjectPropertyDomainAxiom domain):
                {
                    ScanExpressionForNominals(domain.Domain, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                case(OwlObjectPropertyRangeAxiom range):
                {
                    ScanExpressionForNominals(range.Range, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    ScanExpressionForNominals(subClass.SubClass, ref hasNominal, ref anonymousInNominal);
                    ScanExpressionForNominals(subClass.SuperClass, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    ScanExpressionForNominals(equivalent.First, ref hasNominal, ref anonymousInNominal);
                    ScanExpressionForNominals(equivalent.Second, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    for(int i = 0; i < disjoint.Operands.Count; i++)
                    {
                        ScanExpressionForNominals(disjoint.Operands[i], ref hasNominal, ref anonymousInNominal);
                    }

                    break;
                }
                case(OwlDisjointUnionAxiom disjointUnion):
                {
                    for(int i = 0; i < disjointUnion.Operands.Count; i++)
                    {
                        ScanExpressionForNominals(disjointUnion.Operands[i], ref hasNominal, ref anonymousInNominal);
                    }

                    break;
                }
                case(OwlClassAssertionAxiom classAssertion):
                {
                    ScanExpressionForNominals(classAssertion.Class, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                case(OwlDataPropertyDomainAxiom dataDomain):
                {
                    ScanExpressionForNominals(dataDomain.Domain, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                case(OwlHasKeyAxiom key):
                {
                    ScanExpressionForNominals(key.Class, ref hasNominal, ref anonymousInNominal);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        /// <summary>Walks one class expression by explicit stack for nominal constructs and anonymous individuals in nominal positions.</summary>
        /// <param name="root">The class expression.</param>
        /// <param name="hasNominal">Set when a nominal construct occurs.</param>
        /// <param name="anonymousInNominal">Set when a blank node occupies a nominal position.</param>
        private static void ScanExpressionForNominals(OwlClassExpression root, ref bool hasNominal, ref bool anonymousInNominal)
        {
            Stack<OwlClassExpression> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlClassExpression expression = work.Pop();
                switch(expression)
                {
                    case(OwlObjectIntersectionOf intersection):
                    {
                        for(int i = 0; i < intersection.Operands.Count; i++)
                        {
                            work.Push(intersection.Operands[i]);
                        }

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
                    case(OwlObjectComplementOf complement):
                    {
                        work.Push(complement.Operand);
                        break;
                    }
                    case(OwlObjectSomeValuesFrom existential):
                    {
                        work.Push(existential.Filler);
                        break;
                    }
                    case(OwlObjectAllValuesFrom universal):
                    {
                        work.Push(universal.Filler);
                        break;
                    }
                    case(OwlObjectCardinality cardinality):
                    {
                        if(cardinality.Filler is OwlClassExpression filler)
                        {
                            work.Push(filler);
                        }

                        break;
                    }
                    case(OwlObjectOneOf oneOf):
                    {
                        hasNominal = true;
                        for(int i = 0; i < oneOf.Individuals.Count; i++)
                        {
                            if(oneOf.Individuals[i] is not NamedNode)
                            {
                                anonymousInNominal = true;
                            }
                        }

                        break;
                    }
                    case(OwlObjectHasValue hasValue):
                    {
                        hasNominal = true;
                        if(hasValue.Individual is not NamedNode)
                        {
                            anonymousInNominal = true;
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

        /// <summary>Emits the pending root object-property facts after the RBox quotient: positive assertions as <c>⊤ → S(o, o′)</c>, negative assertions as the clash form <c>S(o, o′) → ⊥</c>, each role rewritten to its post-quotient representative.</summary>
        private void EmitRootFacts()
        {
            foreach((int source, RawRoleId role, int target, int origin) in PendingRootEdges)
            {
                RootFacts.Add(DlClause.Create([], [RoleAtom(Rep(role), DlTerm.Individual(source), DlTerm.Individual(target))], origin));
            }

            foreach((int source, RawRoleId role, int target, int origin) in PendingRootNegativeEdges)
            {
                RootFacts.Add(DlClause.Create([RoleAtom(Rep(role), DlTerm.Individual(source), DlTerm.Individual(target))], [], origin));
            }
        }

        /// <summary>Interns a named or skolem-constant individual of a nominal-jurisdiction module's ABox: a named node by IRI, a blank-node SUBJECT by its label (the inc-5 skolem-constant treatment — blank nodes are barred from NOMINAL positions only).</summary>
        /// <param name="individual">The individual term.</param>
        /// <param name="individualId">The interned individual id.</param>
        /// <returns><see langword="true"/> when the term is an individual (not a literal).</returns>
        private bool TryInternRootIndividual(RdfTerm individual, out int individualId)
        {
            if(!TryIndividualKey(individual, out Utf8String key))
            {
                individualId = -1;

                return false;
            }

            individualId = Symbols.InternIndividual(key, IndividualOriginOf(individual));

            return true;
        }

        /// <summary>Lowers a nominal-jurisdiction class assertion to the root fact <c>⊤ → B(o)</c>: a named class directly, a complex class through a fresh definition atom whose GCI runs the ordinary normalization pipeline.</summary>
        /// <param name="axiom">The class-assertion axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeRootClassAssertion(OwlClassAssertionAxiom axiom, int origin)
        {
            if(!TryInternRootIndividual(axiom.Individual, out int individual))
            {
                RejectGroundLiteral();

                return;
            }

            int atom;
            if(axiom.Class is OwlClassReference named)
            {
                atom = Symbols.AtomOf(named.Class.Iri);
            }
            else
            {
                atom = Symbols.FreshAtom();
                AddGci(Symbols.AtomReference(atom), axiom.Class, origin);
            }

            RootFacts.Add(DlClause.Create([], [DlLiteral.Concept(atom, DlTerm.Individual(individual))], origin));
        }

        /// <summary>Lowers a nominal-jurisdiction (all-)different assertion to the root facts <c>⊤ → oᵢ ≉ oⱼ</c> per unordered pair — inequality carries no transitivity, so every pair is stated.</summary>
        /// <param name="axiom">The different-individuals axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeRootDifferentIndividuals(OwlDifferentIndividualsAxiom axiom, int origin)
        {
            List<int> members = new(axiom.Individuals.Count);
            foreach(RdfTerm member in axiom.Individuals)
            {
                if(!TryInternRootIndividual(member, out int individual))
                {
                    RejectGroundLiteral();

                    return;
                }

                members.Add(individual);
            }

            for(int i = 0; i < members.Count; i++)
            {
                for(int j = i + 1; j < members.Count; j++)
                {
                    RootFacts.Add(DlClause.Create([], [DlLiteral.Inequality(DlTerm.Individual(members[i]), DlTerm.Individual(members[j]))], origin));
                }
            }
        }

        /// <summary>
        /// The fresh singleton class <c>N_o</c> of a nominal filler (the
        /// <c>ObjectHasValue</c> normal form): memoized per interned
        /// individual; the first request mints the atom and emits its defining
        /// clauses into the global list — the DL7 fact <c>⊤ → N_o(o)</c> and the
        /// DL8 clause <c>N_o(x) → x ≈ o</c> — so <c>∃r.{o}</c> lowers through the
        /// existing restriction machinery over <c>N_o</c> and <c>S(x, o)</c> never
        /// appears as an ontology-clause body atom.
        /// </summary>
        /// <param name="individualId">The interned filler individual.</param>
        /// <param name="origin">The first-requesting axiom's index, the defining clauses' provenance.</param>
        /// <returns>The singleton class reference.</returns>
        private OwlClassReference NominalSingletonReference(int individualId, int origin)
        {
            if(!NominalSingletonAtoms.TryGetValue(individualId, out int atom))
            {
                atom = Symbols.FreshAtom();
                NominalSingletonAtoms[individualId] = atom;
                Clauses.Add(DlClause.Create([], [DlLiteral.Concept(atom, DlTerm.Individual(individualId))], origin));
                Clauses.Add(DlClause.Create(
                    [DlLiteral.Concept(atom, DlTerm.Central)],
                    [DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individualId))],
                    origin));
            }

            return Symbols.AtomReference(atom);
        }

        /// <summary>Lowers a superclass enumeration <c>B ⊑ {o1, …, on}</c> to the disjunctive a-equality head <c>B(x) → x ≈ o1 ∨ … ∨ x ≈ on</c> (DL1-shaped over a-equalities; n = 1 is the Horn face, n = 0 degenerates to <c>B ⊑ ⊥</c>).</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="oneOf">The enumeration.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepOneOfSuper(OwlClassExpression sub, OwlObjectOneOf oneOf, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            List<DlLiteral> body = [];
            foreach(OwlClassExpression conjunct in FlattenIntersection(sub))
            {
                if(IsTop(conjunct))
                {
                    continue;
                }

                body.Add(DlLiteral.Concept(AtomicOrAbstract(conjunct, negative: true, work), DlTerm.Central));
            }

            List<DlLiteral> head = [];
            foreach(RdfTerm member in oneOf.Individuals)
            {
                if(member is not NamedNode named)
                {
                    return ContextRemainderNames.AnonymousIndividualInNominal;
                }

                head.Add(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(Symbols.InternIndividual(named.Iri, IndividualOrigin.IriDenoted))));
            }

            local.Add(DlClause.Create(body, head, origin));

            return null;
        }

        /// <summary>Lowers a subclass enumeration <c>{o1, …, on} ⊑ B</c> to the DL7 facts <c>⊤ → B(oᵢ)</c> per member.</summary>
        /// <param name="oneOf">The enumeration.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepOneOfSub(OwlObjectOneOf oneOf, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            int atom = AtomicOrAbstract(super, negative: false, work);
            foreach(RdfTerm member in oneOf.Individuals)
            {
                if(member is not NamedNode named)
                {
                    return ContextRemainderNames.AnonymousIndividualInNominal;
                }

                local.Add(DlClause.Create([], [DlLiteral.Concept(atom, DlTerm.Individual(Symbols.InternIndividual(named.Iri, IndividualOrigin.IriDenoted)))], origin));
            }

            return null;
        }

        /// <summary>
        /// Emits one loop-concept clause <c>O_a(x) → Self_p(x)</c> per closed
        /// ground self-edge whose role's forward representative base carries a
        /// minted loop concept: a ground loop <c>p(a, a)</c> entails the self
        /// restriction at <c>a</c>, so the seeded atom joins the individual's
        /// ground context and participates in ordinary saturation. Reads the
        /// CLOSED graph, where the hierarchy, chain-composition, inverse-mirror,
        /// and pre-merge lifts are already present, so one representative-base
        /// lookup per loop suffices and the edge role is always subsumed under
        /// the concept's base. Pairs deduplicate across the two directioned
        /// spellings a mirrored loop carries.
        /// </summary>
        /// <param name="graph">The closed ground assertion graph.</param>
        private void EmitGroundSelfConcepts(GroundAssertionGraph graph)
        {
            HashSet<(Utf8String Node, int Atom)> emitted = [];
            foreach((Utf8String node, RawRoleId role) in graph.SelfEdges())
            {
                if(LoopConcepts.TryGetValue(PrimaryOf(Rep(role)), out int atom) && emitted.Add((node, atom)))
                {
                    Clauses.Add(DlClause.Create(
                        [DlLiteral.Concept(GroundMarkers[node], DlTerm.Central)],
                        [DlLiteral.Concept(atom, DlTerm.Central)],
                        -1));
                }
            }
        }

        /// <summary>The inverse of the loop-concept mint — each loop concept atom <c>Self_p</c> mapped to its forward-base representative role — the map the Self-ghost pass reads to turn an unconditional loop-concept head of a ground context into a graph loop.</summary>
        /// <returns>The loop-concept-atom to forward-base-representative map, empty when no loop concept was minted.</returns>
        private Dictionary<int, RoleRepresentative> BuildSelfLoopConcepts()
        {
            Dictionary<int, RoleRepresentative> selfLoops = [];
            foreach((RoleRepresentative loopBase, int atom) in LoopConcepts)
            {
                selfLoops[atom] = loopBase;
            }

            return selfLoops;
        }

        /// <summary>Builds the whole-module rejection result: the single named remainder, no clauses.</summary>
        /// <param name="name">The rejection name.</param>
        /// <returns>The rejection result.</returns>
        private ClausificationResult WholeModuleRejection(string name)
        {
            Clauses.Clear();
            List<string> remainder = [name];
            ContextTermOrder order = ContextTermOrder.ForModule(Clauses);

            return new ClausificationResult(Clauses, remainder, Symbols, order, AutomatonStates, BudgetExceeded, Symbols.FreshAtoms, Symbols.FreshRoles, Symbols.CountingRoles, NegativePolarityDataMarkers, Mint.Descriptors, DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(Symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [], NominalJurisdiction, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
        }

        /// <summary>Builds the reserved-role rejection result: the accumulated reserved-role remainder names, no clauses — the multi-name analogue of <see cref="WholeModuleRejection"/>.</summary>
        /// <param name="names">The accumulated reserved-role remainder names, in axiom order and deduplicated.</param>
        /// <returns>The rejection result.</returns>
        private ClausificationResult ReservedRoleRejection(IReadOnlyList<string> names)
        {
            Clauses.Clear();
            ContextTermOrder order = ContextTermOrder.ForModule(Clauses);

            return new ClausificationResult(Clauses, names, Symbols, order, AutomatonStates, BudgetExceeded, Symbols.FreshAtoms, Symbols.FreshRoles, Symbols.CountingRoles, NegativePolarityDataMarkers, Mint.Descriptors, DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(Symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [], NominalJurisdiction, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
        }

        /// <summary>The reserved-role construct a scan hit is attributed to, selecting its remainder name.</summary>
        private enum ReservedRoleConstruct
        {
            /// <summary>A role-hierarchy axiom (a sub-, equivalent-, or inverse-object-property spelling).</summary>
            RoleHierarchy,

            /// <summary>A property-chain axiom link or its super role.</summary>
            PropertyChain,

            /// <summary>An object-property domain axiom's property.</summary>
            Domain,

            /// <summary>An object-property range axiom's property.</summary>
            Range,

            /// <summary>An object-property characteristic other than asymmetry.</summary>
            Characteristic,

            /// <summary>A role position inside a class expression.</summary>
            ClassExpression,

            /// <summary>An asymmetric characteristic's property (a top-only carve-out position).</summary>
            Asymmetry,

            /// <summary>A disjoint-object-properties operand (a top-only carve-out position).</summary>
            RoleDisjointness,

            /// <summary>An object-property assertion's property.</summary>
            ObjectPropertyAssertion,

            /// <summary>A negative object-property assertion's property.</summary>
            NegativeObjectPropertyAssertion,
        }

        /// <summary>
        /// The module-level reserved-role scan (the first pipeline step, before
        /// intake): walks the raw axioms — no interning, no symbol table — and
        /// records a named remainder for every <c>owl:topObjectProperty</c> /
        /// <c>owl:bottomObjectProperty</c> mention in a role position, an inverse
        /// unwrapped to its named property before the comparison. Two carve-outs: a
        /// <c>owl:bottomObjectProperty</c> operand of <c>DisjointObjectProperties</c>
        /// or of an <c>Asymmetric</c> characteristic is a sound emptiness tautology
        /// and is not recorded. Hits accumulate in axiom order, deduplicated by name.
        /// Class-expression surfaces are scanned wherever the survey admits one — the
        /// TBox positions, a class assertion's class, and a data-property domain —
        /// while declarations, annotations, imports, individual (in)equalities, and
        /// the remaining data and key axioms carry no object-role position and are
        /// never scanned.
        /// </summary>
        /// <returns>The accumulated reserved-role remainder names, empty when no scanned position mentions a reserved role.</returns>
        private List<string> ScanReservedRoles()
        {
            List<string> names = [];
            HashSet<string> seen = [];
            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                ScanAxiom(Module.Axioms[origin], names, seen);
            }

            return names;
        }

        /// <summary>Scans one axiom's role positions and class-expression surfaces, recording every reserved-role mention outside the carve-outs.</summary>
        /// <param name="axiom">The axiom.</param>
        /// <param name="namesToAppendTo">The ordered reserved-role remainder names.</param>
        /// <param name="seen">The deduplication set of recorded names.</param>
        private static void ScanAxiom(OwlAxiom axiom, List<string> namesToAppendTo, HashSet<string> seen)
        {
            switch(axiom)
            {
                case(OwlSubObjectPropertyOfAxiom subRole):
                {
                    ScanRole(subRole.SubProperty, ReservedRoleConstruct.RoleHierarchy, namesToAppendTo, seen);
                    ScanRole(subRole.SuperProperty, ReservedRoleConstruct.RoleHierarchy, namesToAppendTo, seen);
                    break;
                }
                case(OwlEquivalentObjectPropertiesAxiom equivalentRoles):
                {
                    ScanRole(equivalentRoles.First, ReservedRoleConstruct.RoleHierarchy, namesToAppendTo, seen);
                    ScanRole(equivalentRoles.Second, ReservedRoleConstruct.RoleHierarchy, namesToAppendTo, seen);
                    break;
                }
                case(OwlInverseObjectPropertiesAxiom inverse):
                {
                    ScanRole(inverse.First, ReservedRoleConstruct.RoleHierarchy, namesToAppendTo, seen);
                    ScanRole(inverse.Second, ReservedRoleConstruct.RoleHierarchy, namesToAppendTo, seen);
                    break;
                }
                case(OwlPropertyChainAxiom chain):
                {
                    for(int i = 0; i < chain.Chain.Count; i++)
                    {
                        ScanRole(chain.Chain[i], ReservedRoleConstruct.PropertyChain, namesToAppendTo, seen);
                    }

                    ScanRole(chain.SuperProperty, ReservedRoleConstruct.PropertyChain, namesToAppendTo, seen);
                    break;
                }
                case(OwlObjectPropertyDomainAxiom domain):
                {
                    ScanRole(domain.Property, ReservedRoleConstruct.Domain, namesToAppendTo, seen);
                    ScanClassExpression(domain.Domain, namesToAppendTo, seen);
                    break;
                }
                case(OwlObjectPropertyRangeAxiom range):
                {
                    ScanRole(range.Property, ReservedRoleConstruct.Range, namesToAppendTo, seen);
                    ScanClassExpression(range.Range, namesToAppendTo, seen);
                    break;
                }
                case(OwlObjectPropertyCharacteristicAxiom characteristic):
                {
                    ScanCharacteristic(characteristic, namesToAppendTo, seen);
                    break;
                }
                case(OwlDisjointObjectPropertiesAxiom disjointRoles):
                {
                    for(int i = 0; i < disjointRoles.Operands.Count; i++)
                    {
                        ScanTopOnlyRole(disjointRoles.Operands[i], ReservedRoleConstruct.RoleDisjointness, namesToAppendTo, seen);
                    }

                    break;
                }
                case(OwlSubClassOfAxiom subClass):
                {
                    ScanClassExpression(subClass.SubClass, namesToAppendTo, seen);
                    ScanClassExpression(subClass.SuperClass, namesToAppendTo, seen);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    ScanClassExpression(equivalent.First, namesToAppendTo, seen);
                    ScanClassExpression(equivalent.Second, namesToAppendTo, seen);
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    for(int i = 0; i < disjoint.Operands.Count; i++)
                    {
                        ScanClassExpression(disjoint.Operands[i], namesToAppendTo, seen);
                    }

                    break;
                }
                case(OwlDisjointUnionAxiom disjointUnion):
                {
                    for(int i = 0; i < disjointUnion.Operands.Count; i++)
                    {
                        ScanClassExpression(disjointUnion.Operands[i], namesToAppendTo, seen);
                    }

                    break;
                }
                case(OwlObjectPropertyAssertionAxiom edge):
                {
                    ScanRole(new OwlObjectPropertyReference(edge.Property), ReservedRoleConstruct.ObjectPropertyAssertion, namesToAppendTo, seen);
                    break;
                }
                case(OwlNegativeObjectPropertyAssertionAxiom negativeEdge):
                {
                    ScanRole(negativeEdge.Property, ReservedRoleConstruct.NegativeObjectPropertyAssertion, namesToAppendTo, seen);
                    break;
                }
                case(OwlClassAssertionAxiom classAssertion):
                {
                    ScanClassExpression(classAssertion.Class, namesToAppendTo, seen);
                    break;
                }
                case(OwlDataPropertyDomainAxiom dataDomain):
                {
                    ScanClassExpression(dataDomain.Domain, namesToAppendTo, seen);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        /// <summary>Scans an object-property characteristic's property: an <c>Asymmetric</c> characteristic under the top-only carve-out (the KAZ 2008 <c>Asy(R) ⟺ Dis(R, Inv(R))</c> reduction makes bottom the empty tautology), every other characteristic under the both-reserved rule.</summary>
        /// <param name="characteristic">The characteristic axiom.</param>
        /// <param name="namesToAppendTo">The ordered reserved-role remainder names.</param>
        /// <param name="seen">The deduplication set of recorded names.</param>
        private static void ScanCharacteristic(OwlObjectPropertyCharacteristicAxiom characteristic, List<string> namesToAppendTo, HashSet<string> seen)
        {
            if(characteristic.Characteristic == OwlPropertyCharacteristic.Asymmetric)
            {
                ScanTopOnlyRole(characteristic.Property, ReservedRoleConstruct.Asymmetry, namesToAppendTo, seen);

                return;
            }

            ScanRole(characteristic.Property, ReservedRoleConstruct.Characteristic, namesToAppendTo, seen);
        }

        /// <summary>
        /// Scans a class expression for reserved object-property mentions to
        /// arbitrary depth by an explicit stack (no recursion): descends through
        /// intersection, union, and complement operands and every restriction
        /// filler, recording a <see cref="ReservedRoleConstruct.ClassExpression"/>
        /// hit for the role position of every existential, universal, cardinality,
        /// self, and has-value restriction whose property (an inverse unwrapped to
        /// its named property) is a reserved built-in. Named classes, enumerations,
        /// and every data restriction carry no object-property role position.
        /// </summary>
        /// <param name="root">The class expression to walk.</param>
        /// <param name="namesToAppendTo">The ordered reserved-role remainder names.</param>
        /// <param name="seen">The deduplication set of recorded names.</param>
        private static void ScanClassExpression(OwlClassExpression root, List<string> namesToAppendTo, HashSet<string> seen)
        {
            Stack<OwlClassExpression> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlClassExpression expression = work.Pop();
                switch(expression)
                {
                    case(OwlObjectIntersectionOf intersection):
                    {
                        for(int i = 0; i < intersection.Operands.Count; i++)
                        {
                            work.Push(intersection.Operands[i]);
                        }

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
                    case(OwlObjectComplementOf complement):
                    {
                        work.Push(complement.Operand);
                        break;
                    }
                    case(OwlObjectSomeValuesFrom existential):
                    {
                        ScanRole(existential.Property, ReservedRoleConstruct.ClassExpression, namesToAppendTo, seen);
                        work.Push(existential.Filler);
                        break;
                    }
                    case(OwlObjectAllValuesFrom universal):
                    {
                        ScanRole(universal.Property, ReservedRoleConstruct.ClassExpression, namesToAppendTo, seen);
                        work.Push(universal.Filler);
                        break;
                    }
                    case(OwlObjectCardinality cardinality):
                    {
                        ScanRole(cardinality.Property, ReservedRoleConstruct.ClassExpression, namesToAppendTo, seen);
                        if(cardinality.Filler is OwlClassExpression filler)
                        {
                            work.Push(filler);
                        }

                        break;
                    }
                    case(OwlObjectHasSelf self):
                    {
                        ScanRole(self.Property, ReservedRoleConstruct.ClassExpression, namesToAppendTo, seen);
                        break;
                    }
                    case(OwlObjectHasValue hasValue):
                    {
                        ScanRole(hasValue.Property, ReservedRoleConstruct.ClassExpression, namesToAppendTo, seen);
                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>Records a reserved-role hit for a both-reserved role position: the reference's IRI (an inverse unwrapped to its named property) equal to <c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>.</summary>
        /// <param name="property">The object-property expression at the role position.</param>
        /// <param name="construct">The construct the position belongs to.</param>
        /// <param name="namesToAppendTo">The ordered reserved-role remainder names.</param>
        /// <param name="seen">The deduplication set of recorded names.</param>
        private static void ScanRole(OwlObjectPropertyExpression property, ReservedRoleConstruct construct, List<string> namesToAppendTo, HashSet<string> seen)
        {
            Utf8String iri = property.Property.Iri;
            if(iri.Equals(OwlVocabulary.TopObjectProperty) || iri.Equals(OwlVocabulary.BottomObjectProperty))
            {
                RecordReserved(construct, iri, namesToAppendTo, seen);
            }
        }

        /// <summary>Records a reserved-role hit only for <c>owl:topObjectProperty</c> in a carve-out position (a <c>DisjointObjectProperties</c> operand or an <c>Asymmetric</c> property): <c>owl:bottomObjectProperty</c> (and its inverse) leaves the emitted emptiness clause a sound tautology and is not a hit.</summary>
        /// <param name="property">The object-property expression at the carve-out position.</param>
        /// <param name="construct">The carve-out construct the position belongs to.</param>
        /// <param name="namesToAppendTo">The ordered reserved-role remainder names.</param>
        /// <param name="seen">The deduplication set of recorded names.</param>
        private static void ScanTopOnlyRole(OwlObjectPropertyExpression property, ReservedRoleConstruct construct, List<string> namesToAppendTo, HashSet<string> seen)
        {
            Utf8String iri = property.Property.Iri;
            if(iri.Equals(OwlVocabulary.TopObjectProperty))
            {
                RecordReserved(construct, iri, namesToAppendTo, seen);
            }
        }

        /// <summary>Formats the reserved-role remainder name for a construct and appends it, deduplicated by name.</summary>
        /// <param name="construct">The construct the reserved role was mentioned in.</param>
        /// <param name="iri">The reserved role's IRI.</param>
        /// <param name="namesToAppendTo">The ordered reserved-role remainder names.</param>
        /// <param name="seen">The deduplication set of recorded names.</param>
        private static void RecordReserved(ReservedRoleConstruct construct, Utf8String iri, List<string> namesToAppendTo, HashSet<string> seen)
        {
            string name;
            switch(construct)
            {
                case(ReservedRoleConstruct.RoleHierarchy):
                {
                    name = ContextRemainderNames.ReservedRoleInRoleHierarchy(iri);
                    break;
                }
                case(ReservedRoleConstruct.PropertyChain):
                {
                    name = ContextRemainderNames.ReservedRoleInPropertyChain(iri);
                    break;
                }
                case(ReservedRoleConstruct.Domain):
                {
                    name = ContextRemainderNames.ReservedRoleInDomain(iri);
                    break;
                }
                case(ReservedRoleConstruct.Range):
                {
                    name = ContextRemainderNames.ReservedRoleInRange(iri);
                    break;
                }
                case(ReservedRoleConstruct.Characteristic):
                {
                    name = ContextRemainderNames.ReservedRoleInCharacteristic(iri);
                    break;
                }
                case(ReservedRoleConstruct.ClassExpression):
                {
                    name = ContextRemainderNames.ReservedRoleInClassExpression(iri);
                    break;
                }
                case(ReservedRoleConstruct.Asymmetry):
                {
                    name = ContextRemainderNames.ReservedRoleInAsymmetry(iri);
                    break;
                }
                case(ReservedRoleConstruct.RoleDisjointness):
                {
                    name = ContextRemainderNames.ReservedRoleInRoleDisjointness(iri);
                    break;
                }
                case(ReservedRoleConstruct.ObjectPropertyAssertion):
                {
                    name = ContextRemainderNames.ReservedRoleInObjectPropertyAssertion(iri);
                    break;
                }
                case(ReservedRoleConstruct.NegativeObjectPropertyAssertion):
                {
                    name = ContextRemainderNames.ReservedRoleInNegativeObjectPropertyAssertion(iri);
                    break;
                }
                default:
                {
                    throw new UnreachableException();
                }
            }

            if(seen.Add(name))
            {
                namesToAppendTo.Add(name);
            }
        }

        /// <summary>Intake: maps each axiom to GCIs, role inclusions, or a named remainder.</summary>
        private void Intake()
        {
            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                IntakeAxiom(Module.Axioms[origin], origin);
            }
        }

        /// <summary>Maps one axiom to GCIs, role inclusions, or a named remainder.</summary>
        /// <param name="axiom">The axiom.</param>
        /// <param name="origin">The axiom's index in the module.</param>
        private void IntakeAxiom(OwlAxiom axiom, int origin)
        {
            switch(axiom)
            {
                case(OwlSubClassOfAxiom subClass):
                {
                    AddGci(subClass.SubClass, subClass.SuperClass, origin);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    AddGci(equivalent.First, equivalent.Second, origin);
                    AddGci(equivalent.Second, equivalent.First, origin);
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    IntakeDisjointClasses(disjoint.Operands, origin);
                    break;
                }
                case(OwlDisjointUnionAxiom disjointUnion):
                {
                    IntakeDisjointUnion(disjointUnion, origin);
                    break;
                }
                case(OwlObjectPropertyDomainAxiom domain):
                {
                    AddGci(new OwlObjectSomeValuesFrom(domain.Property, Thing()), domain.Domain, origin);
                    break;
                }
                case(OwlObjectPropertyRangeAxiom range):
                {
                    AddGci(Thing(), new OwlObjectAllValuesFrom(range.Property, range.Range), origin);
                    break;
                }
                case(OwlSubObjectPropertyOfAxiom subRole):
                {
                    RoleInclusions.Add((Symbols.RoleOf(subRole.SubProperty), Symbols.RoleOf(subRole.SuperProperty)));
                    break;
                }
                case(OwlEquivalentObjectPropertiesAxiom equivalentRoles):
                {
                    RawRoleId first = Symbols.RoleOf(equivalentRoles.First);
                    RawRoleId second = Symbols.RoleOf(equivalentRoles.Second);
                    RoleInclusions.Add((first, second));
                    RoleInclusions.Add((second, first));
                    break;
                }
                case(OwlInverseObjectPropertiesAxiom inverse):
                {
                    RawRoleId first = Symbols.RoleOf(inverse.First);
                    RawRoleId second = Symbols.RoleOf(inverse.Second);
                    RoleInclusions.Add((first, ContextSymbolTable.Inverse(second)));
                    RoleInclusions.Add((ContextSymbolTable.Inverse(second), first));
                    break;
                }
                case(OwlPropertyChainAxiom chain):
                {
                    IntakeChain(chain);
                    break;
                }
                case(OwlDisjointObjectPropertiesAxiom disjointRoles):
                {
                    IntakeDisjointRoles(disjointRoles.Operands, origin);
                    break;
                }
                case(OwlObjectPropertyCharacteristicAxiom characteristic):
                {
                    IntakeCharacteristic(characteristic, origin);
                    break;
                }
                case(OwlDataPropertyDomainAxiom dataDomain):
                {
                    IntakeDataDomain(dataDomain, origin);
                    break;
                }
                case(OwlSubDataPropertyOfAxiom subData):
                {
                    RecordDataEdge(subData.SubProperty.Iri, subData.SuperProperty.Iri);
                    break;
                }
                case(OwlEquivalentDataPropertiesAxiom equivalentData):
                {
                    RecordDataEdge(equivalentData.First.Iri, equivalentData.Second.Iri);
                    RecordDataEdge(equivalentData.Second.Iri, equivalentData.First.Iri);
                    break;
                }
                case(OwlFunctionalDataPropertyAxiom or OwlDisjointDataPropertiesAxiom or OwlDataPropertyRangeAxiom):
                {
                    //Range, functional, and disjoint act through the module DataPropertyBox
                    //the sidecar reads, not through emitted clauses.
                    break;
                }
                case(OwlClassAssertionAxiom classAssertion):
                {
                    IntakeClassAssertion(classAssertion, origin);
                    break;
                }
                case(OwlObjectPropertyAssertionAxiom edge):
                {
                    IntakeObjectPropertyAssertion(edge, origin);
                    break;
                }
                case(OwlNegativeObjectPropertyAssertionAxiom negativeEdge):
                {
                    IntakeNegativeObjectPropertyAssertion(negativeEdge);
                    break;
                }
                case(OwlSameIndividualAxiom same) when NominalJurisdiction:
                {
                    if(TryInternRootIndividual(same.First, out int first) && TryInternRootIndividual(same.Second, out int second))
                    {
                        RootFacts.Add(DlClause.Create([], [DlLiteral.Equality(DlTerm.Individual(first), DlTerm.Individual(second))], origin));
                    }
                    else
                    {
                        RejectGroundLiteral();
                    }

                    break;
                }
                case(OwlDifferentIndividualsAxiom different) when NominalJurisdiction:
                {
                    IntakeRootDifferentIndividuals(different, origin);
                    break;
                }
                case(OwlSameIndividualAxiom or OwlDifferentIndividualsAxiom):
                {
                    //Consumed by the pre-merge pass: the unions and the
                    //pairwise representative-collision check run before intake, so these
                    //carry no clause here.
                    break;
                }
                case(OwlDataPropertyAssertionAxiom dataAssertion):
                {
                    IntakeDataPropertyAssertion(dataAssertion, origin);
                    break;
                }
                case(OwlHasKeyAxiom hasKey):
                {
                    IntakeHasKey(hasKey);
                    break;
                }
                case(OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom
                    or OwlSubAnnotationPropertyOfAxiom or OwlAnnotationPropertyDomainAxiom or OwlAnnotationPropertyRangeAxiom):
                {
                    break;
                }
                default:
                {
                    Remainder.Add(axiom.GetType().Name);
                    break;
                }
            }
        }

        /// <summary>Intakes a disjoint-classes axiom as pairwise <c>Ci ⊓ Cj ⊑ ⊥</c> GCIs.</summary>
        /// <param name="operands">The mutually disjoint operands.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeDisjointClasses(IReadOnlyList<OwlClassExpression> operands, int origin)
        {
            for(int i = 0; i < operands.Count; i++)
            {
                for(int j = i + 1; j < operands.Count; j++)
                {
                    AddGci(new OwlObjectIntersectionOf([operands[i], operands[j]]), Nothing(), origin);
                }
            }
        }

        /// <summary>Intakes a disjoint-union axiom as the covering inclusion, the member inclusions, and pairwise disjointness.</summary>
        /// <param name="axiom">The disjoint-union axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeDisjointUnion(OwlDisjointUnionAxiom axiom, int origin)
        {
            OwlClassReference definedClass = new(axiom.Class);
            AddGci(definedClass, new OwlObjectUnionOf(axiom.Operands), origin);

            foreach(OwlClassExpression operand in axiom.Operands)
            {
                AddGci(operand, definedClass, origin);
            }

            IntakeDisjointClasses(axiom.Operands, origin);
        }

        /// <summary>Intakes a property-chain axiom as a directioned role inclusion — length-1 kept, length-at-least-2 deleted after chain elimination.</summary>
        /// <param name="chain">The property-chain axiom.</param>
        private void IntakeChain(OwlPropertyChainAxiom chain)
        {
            RawRoleId super = Symbols.RoleOf(chain.SuperProperty);
            List<RawRoleId> links = [];
            foreach(OwlObjectPropertyExpression link in chain.Chain)
            {
                links.Add(Symbols.RoleOf(link));
            }

            if(links.Count <= 1)
            {
                RoleInclusions.Add((links.Count == 1 ? links[0] : super, super));
            }
            else
            {
                ChainInclusions.Add((links, super));
            }
        }

        /// <summary>Intakes a disjoint-object-properties axiom as the unordered directioned operand pairs (raw intake ids), recorded for the disjointness emission's per-pair clash clause under the simplicity guard.</summary>
        /// <param name="operands">The mutually disjoint operands.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeDisjointRoles(IReadOnlyList<OwlObjectPropertyExpression> operands, int origin)
        {
            for(int i = 0; i < operands.Count; i++)
            {
                RawRoleId first = Symbols.RoleOf(operands[i]);
                for(int j = i + 1; j < operands.Count; j++)
                {
                    DisjointRolePairs.Add(new DisjointRolePair(first, Symbols.RoleOf(operands[j]), FromAsymmetric: false, origin));
                }
            }
        }

        /// <summary>Intakes an object-property characteristic as its SROIQ2006 reduction, or names it in the remainder.</summary>
        /// <param name="characteristic">The characteristic axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeCharacteristic(OwlObjectPropertyCharacteristicAxiom characteristic, int origin)
        {
            RawRoleId role = Symbols.RoleOf(characteristic.Property);
            switch(characteristic.Characteristic)
            {
                case(OwlPropertyCharacteristic.Transitive):
                {
                    ChainInclusions.Add(([role, role], role));
                    break;
                }
                case(OwlPropertyCharacteristic.Symmetric):
                {
                    RoleInclusions.Add((ContextSymbolTable.Inverse(role), role));
                    break;
                }
                case(OwlPropertyCharacteristic.Functional):
                {
                    AddGci(Thing(), new OwlObjectCardinality(OwlCardinalityKind.Max, 1, characteristic.Property, null), origin);
                    if(Lowering == EqualityLowering.SuccessorSharing)
                    {
                        FunctionalRoleIntakeIds.Add(role);
                    }

                    break;
                }
                case(OwlPropertyCharacteristic.InverseFunctional):
                {
                    AddGci(Thing(), new OwlObjectCardinality(OwlCardinalityKind.Max, 1, InverseExpression(characteristic.Property), null), origin);
                    if(Lowering == EqualityLowering.SuccessorSharing)
                    {
                        FunctionalRoleIntakeIds.Add(ContextSymbolTable.Inverse(role));
                    }

                    break;
                }
                case(OwlPropertyCharacteristic.Reflexive):
                {
                    ReflexiveRoles.Add(role);
                    break;
                }
                case(OwlPropertyCharacteristic.Irreflexive):
                {
                    IrreflexiveRoles.Add(role);
                    break;
                }
                case(OwlPropertyCharacteristic.Asymmetric):
                {
                    DisjointRolePairs.Add(new DisjointRolePair(role, ContextSymbolTable.Inverse(role), FromAsymmetric: true, origin));
                    break;
                }
                default:
                {
                    Remainder.Add(characteristic.GetType().Name);
                    break;
                }
            }
        }

        /// <summary>Adds a pending GCI tagged with its origin axiom.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="origin">The origin axiom's index.</param>
        private void AddGci(OwlClassExpression sub, OwlClassExpression super, int origin)
        {
            Pending.Add(new PendingGci(origin, sub, super));
        }

        /// <summary>
        /// The pre-merge ground-individual pass, run after the
        /// reserved-role scan and before intake so <c>ClassAssertion</c>,
        /// <c>ObjectPropertyAssertion</c>, and the negative assertion resolve their
        /// individuals to representatives: builds the <c>SameIndividual</c> union-find,
        /// registers one marker per representative of every admitted ABox axiom, and
        /// checks each <c>DifferentIndividuals</c> axiom for a post-union
        /// representative collision. A literal in an individual position names the
        /// literal remainder. Returns the first collision's reason, or
        /// <see langword="null"/> when the merges are collision-free.
        /// </summary>
        /// <returns>The pre-merge collision reason, or <see langword="null"/> when consistent.</returns>
        private string? PreMergeGroundIndividuals()
        {
            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                if(Module.Axioms[origin] is OwlSameIndividualAxiom same)
                {
                    if(TryIndividualKey(same.First, out Utf8String first) && TryIndividualKey(same.Second, out Utf8String second))
                    {
                        UnionIndividuals(first, second);
                    }
                    else
                    {
                        RejectGroundLiteral();
                    }
                }
            }

            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                switch(Module.Axioms[origin])
                {
                    case(OwlSameIndividualAxiom same):
                    {
                        RegisterGroundIndividual(same.First);
                        RegisterGroundIndividual(same.Second);
                        break;
                    }
                    case(OwlDifferentIndividualsAxiom different):
                    {
                        foreach(RdfTerm member in different.Individuals)
                        {
                            RegisterGroundIndividual(member);
                        }

                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }

            //A collision in a SEEDED round is key provenance: told SameIndividual
            //unions are round-invariant, so a told collision would have clashed in
            //the un-seeded first round before any seeding existed — a new collision
            //here necessarily involves a key-forced union.
            string? reason = null;
            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                if(Module.Axioms[origin] is OwlDifferentIndividualsAxiom different)
                {
                    reason ??= DetectDifferentCollision(different, keyForced: SeedUnions.Count > 0);
                }
            }

            return reason;
        }

        /// <summary>Unions two individual keys, attaching the second's root under the first's so the first-seen key is the deterministic representative; counts a union that merged two distinct roots, and ORs the contains-named bit onto the surviving root so named-ness stays an equivalence-class property.</summary>
        /// <param name="first">The first individual key.</param>
        /// <param name="second">The second individual key.</param>
        private void UnionIndividuals(Utf8String first, Utf8String second)
        {
            Utf8String rootFirst = FindIndividual(first);
            Utf8String rootSecond = FindIndividual(second);
            if(rootFirst.Equals(rootSecond))
            {
                return;
            }

            IndividualMerges[rootSecond] = rootFirst;
            PreMergeUnions++;
            if(NamedRoots.Remove(rootSecond))
            {
                NamedRoots.Add(rootFirst);
            }
        }

        /// <summary>Marks a registered individual's equivalence class as containing a named (IRI-denoted) member when the term is a named node — the key join's guard bit, OR-propagated across unions.</summary>
        /// <param name="term">The individual term.</param>
        /// <param name="key">The term's individual key.</param>
        private void MarkNamedRoot(RdfTerm term, Utf8String key)
        {
            if(term is NamedNode)
            {
                NamedRoots.Add(FindIndividual(key));
            }
        }

        /// <summary>Resolves an individual key to its representative through the merge map, compressing the path so a long merge chain stays near-constant on re-resolution.</summary>
        /// <param name="key">The individual key.</param>
        /// <returns>The representative key.</returns>
        private Utf8String FindIndividual(Utf8String key)
        {
            Utf8String root = key;
            while(IndividualMerges.TryGetValue(root, out Utf8String parent) && !parent.Equals(root))
            {
                root = parent;
            }

            Utf8String current = key;
            while(IndividualMerges.TryGetValue(current, out Utf8String parent) && !parent.Equals(current))
            {
                IndividualMerges[current] = root;
                current = parent;
            }

            return root;
        }

        /// <summary>Registers an individual's representative and marker (marking the contains-named bit for a named node), or names the literal remainder when the term is a literal.</summary>
        /// <param name="term">The individual term.</param>
        private void RegisterGroundIndividual(RdfTerm term)
        {
            if(TryIndividualKey(term, out Utf8String key))
            {
                GroundMarker(key);
                MarkNamedRoot(term, key);

                return;
            }

            RejectGroundLiteral();
        }

        /// <summary>The fresh marker concept atom of a key's representative, minted once and registering the representative; the ground context core the saturation setup seeds, never a signature class.</summary>
        /// <param name="key">The individual key.</param>
        /// <returns>The representative's marker concept atom id.</returns>
        private int GroundMarker(Utf8String key)
        {
            Utf8String representative = FindIndividual(key);
            if(!GroundMarkers.TryGetValue(representative, out int marker))
            {
                marker = Symbols.FreshAtom();
                GroundMarkers[representative] = marker;
                if(RiderEnabled)
                {
                    MarkerRepresentatives[marker] = representative;
                }

                if(GroundRepresentativeSet.Add(representative))
                {
                    GroundRepresentatives.Add(representative);
                }
            }

            return marker;
        }

        /// <summary>Names the literal-individual remainder once, keeping repeated literal positions from duplicating it.</summary>
        private void RejectGroundLiteral()
        {
            if(!GroundLiteralRejected)
            {
                GroundLiteralRejected = true;
                Remainder.Add(ContextRemainderNames.GroundIndividualIsLiteral);
            }
        }

        /// <summary>The first post-union representative collision of a <c>DifferentIndividuals</c> axiom (including the degenerate repeated-term pair), or <see langword="null"/> when its members stay pairwise distinct. The provenance flag selects the reason: a told-pass collision renders as the pre-merge reason, one found after key-forced unions as the key-merge reason.</summary>
        /// <param name="axiom">The different-individuals axiom.</param>
        /// <param name="keyForced">Whether the sweep runs after key-forced unions.</param>
        /// <returns>The collision reason, or <see langword="null"/>.</returns>
        private string? DetectDifferentCollision(OwlDifferentIndividualsAxiom axiom, bool keyForced)
        {
            IReadOnlyList<RdfTerm> individuals = axiom.Individuals;
            for(int i = 0; i < individuals.Count; i++)
            {
                if(!TryIndividualKey(individuals[i], out Utf8String keyI))
                {
                    continue;
                }

                Utf8String representative = FindIndividual(keyI);
                for(int j = i + 1; j < individuals.Count; j++)
                {
                    if(TryIndividualKey(individuals[j], out Utf8String keyJ) && representative.Equals(FindIndividual(keyJ)))
                    {
                        return keyForced ? GroundClashReasons.KeyMergeCollision(representative) : GroundClashReasons.PreMergeCollision(representative);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The module-wide key-scoped data belt, restructured as a per-property
        /// ROUTER: with at least one data-property
        /// assertion in the module, an asserted data property co-occurring in a
        /// KEPT position — a sub-property, equivalence, disjointness, or negative
        /// assertion axiom, or a <c>DataSomeValuesFrom</c>/<c>DataAllValuesFrom</c>
        /// restriction — rejects the whole module (the per-property value store
        /// performs no hierarchy closure, and the cross-property value reasoning
        /// those positions need is not built), the first
        /// KEPT hit in axiom order naming the rejection; an asserted property
        /// co-occurring only in LIFTED positions — a domain, range, or functional
        /// axiom, or a <c>DataHasValue</c> or <c>DataCardinality</c> restriction — joins
        /// <see cref="DemandLoweredProperties"/> so intake lowers its assertions
        /// to engine demands the domain GCI, the <see cref="DataPropertyBox"/>,
        /// and the shared datatype sidecar consume. <c>HasKey</c> key lists stay
        /// exempt. Returns the rejection name, or <see langword="null"/> when the
        /// module is admitted (in particular for every module without a data
        /// assertion, keeping the belt invisible off-path).
        /// </summary>
        /// <returns>The whole-module rejection name, or <see langword="null"/>.</returns>
        private string? ScanKeyDataBelt()
        {
            HashSet<Utf8String> asserted = [];
            foreach(OwlAxiom axiom in Module.Axioms)
            {
                if(axiom is OwlDataPropertyAssertionAxiom assertion)
                {
                    asserted.Add(assertion.Property.Iri);
                }
            }

            if(asserted.Count == 0)
            {
                return null;
            }

            foreach(OwlAxiom axiom in Module.Axioms)
            {
                switch(axiom)
                {
                    case(OwlSubDataPropertyOfAxiom sub):
                    {
                        if(asserted.Contains(sub.SubProperty.Iri))
                        {
                            return ContextRemainderNames.AssertedDataPropertyBeyondKeys(sub.SubProperty.Iri);
                        }

                        if(asserted.Contains(sub.SuperProperty.Iri))
                        {
                            return ContextRemainderNames.AssertedDataPropertyBeyondKeys(sub.SuperProperty.Iri);
                        }

                        break;
                    }
                    case(OwlEquivalentDataPropertiesAxiom equivalent):
                    {
                        if(asserted.Contains(equivalent.First.Iri))
                        {
                            return ContextRemainderNames.AssertedDataPropertyBeyondKeys(equivalent.First.Iri);
                        }

                        if(asserted.Contains(equivalent.Second.Iri))
                        {
                            return ContextRemainderNames.AssertedDataPropertyBeyondKeys(equivalent.Second.Iri);
                        }

                        break;
                    }
                    case(OwlDisjointDataPropertiesAxiom disjoint):
                    {
                        foreach(NamedNode operand in disjoint.Operands)
                        {
                            if(asserted.Contains(operand.Iri))
                            {
                                return ContextRemainderNames.AssertedDataPropertyBeyondKeys(operand.Iri);
                            }
                        }

                        break;
                    }
                    case(OwlDataPropertyDomainAxiom domain):
                    {
                        if(asserted.Contains(domain.Property.Iri))
                        {
                            DemandLoweredProperties.Add(domain.Property.Iri);
                        }

                        break;
                    }
                    case(OwlDataPropertyRangeAxiom range):
                    {
                        if(asserted.Contains(range.Property.Iri))
                        {
                            DemandLoweredProperties.Add(range.Property.Iri);
                        }

                        break;
                    }
                    case(OwlFunctionalDataPropertyAxiom functional):
                    {
                        if(asserted.Contains(functional.Property.Iri))
                        {
                            DemandLoweredProperties.Add(functional.Property.Iri);
                        }

                        break;
                    }
                    case(OwlNegativeDataPropertyAssertionAxiom negative):
                    {
                        if(asserted.Contains(negative.Property.Iri))
                        {
                            return ContextRemainderNames.AssertedDataPropertyBeyondKeys(negative.Property.Iri);
                        }

                        break;
                    }
                    default:
                    {
                        break;
                    }
                }

                string? expressionHit = ScanExpressionsForAssertedDataProperties(axiom, asserted, DemandLoweredProperties);
                if(expressionHit is not null)
                {
                    return expressionHit;
                }
            }

            return null;
        }

        /// <summary>Walks an axiom's class expressions with an explicit stack, returning the belt rejection for the first KEPT-shape data restriction (<c>DataSomeValuesFrom</c>, <c>DataAllValuesFrom</c>) naming an asserted data property, and flagging the property for demand lowering on a <c>DataHasValue</c> or <c>DataCardinality</c> occurrence (the lifted shapes, whose counting the shared datatype sidecar decides against the lowered point demands); <c>HasKey</c> key-property lists are the belt's one exemption, though the KEYED class expression itself is walked.</summary>
        /// <param name="axiom">The axiom whose class expressions are walked.</param>
        /// <param name="asserted">The asserted data property IRIs.</param>
        /// <param name="demandLoweredToAppendTo">The lowering set an asserted property's lifted <c>DataHasValue</c> or <c>DataCardinality</c> occurrence is appended to.</param>
        /// <returns>The rejection name, or <see langword="null"/>.</returns>
        private static string? ScanExpressionsForAssertedDataProperties(OwlAxiom axiom, HashSet<Utf8String> asserted, HashSet<Utf8String> demandLoweredToAppendTo)
        {
            Stack<OwlClassExpression> work = new();
            switch(axiom)
            {
                case(OwlSubClassOfAxiom sub):
                {
                    work.Push(sub.SubClass);
                    work.Push(sub.SuperClass);
                    break;
                }
                case(OwlEquivalentClassesAxiom equivalent):
                {
                    work.Push(equivalent.First);
                    work.Push(equivalent.Second);
                    break;
                }
                case(OwlDisjointClassesAxiom disjoint):
                {
                    foreach(OwlClassExpression operand in disjoint.Operands)
                    {
                        work.Push(operand);
                    }

                    break;
                }
                case(OwlDisjointUnionAxiom disjointUnion):
                {
                    foreach(OwlClassExpression operand in disjointUnion.Operands)
                    {
                        work.Push(operand);
                    }

                    break;
                }
                case(OwlObjectPropertyDomainAxiom domain):
                {
                    work.Push(domain.Domain);
                    break;
                }
                case(OwlObjectPropertyRangeAxiom range):
                {
                    work.Push(range.Range);
                    break;
                }
                case(OwlClassAssertionAxiom classAssertion):
                {
                    work.Push(classAssertion.Class);
                    break;
                }
                case(OwlDataPropertyDomainAxiom dataDomain):
                {
                    work.Push(dataDomain.Domain);
                    break;
                }
                case(OwlHasKeyAxiom hasKey):
                {
                    work.Push(hasKey.Class);
                    break;
                }
                default:
                {
                    break;
                }
            }

            while(work.Count > 0)
            {
                switch(work.Pop())
                {
                    case(OwlDataSomeValuesFrom dataSome):
                    {
                        foreach(NamedNode property in dataSome.Properties)
                        {
                            if(asserted.Contains(property.Iri))
                            {
                                return ContextRemainderNames.AssertedDataPropertyBeyondKeys(property.Iri);
                            }
                        }

                        break;
                    }
                    case(OwlDataAllValuesFrom dataAll):
                    {
                        foreach(NamedNode property in dataAll.Properties)
                        {
                            if(asserted.Contains(property.Iri))
                            {
                                return ContextRemainderNames.AssertedDataPropertyBeyondKeys(property.Iri);
                            }
                        }

                        break;
                    }
                    case(OwlDataHasValue dataHas):
                    {
                        if(asserted.Contains(dataHas.Property.Iri))
                        {
                            demandLoweredToAppendTo.Add(dataHas.Property.Iri);
                        }

                        break;
                    }
                    case(OwlDataCardinality dataCardinality):
                    {
                        if(asserted.Contains(dataCardinality.Property.Iri))
                        {
                            demandLoweredToAppendTo.Add(dataCardinality.Property.Iri);
                        }

                        break;
                    }
                    case(OwlObjectIntersectionOf intersection):
                    {
                        foreach(OwlClassExpression operand in intersection.Operands)
                        {
                            work.Push(operand);
                        }

                        break;
                    }
                    case(OwlObjectUnionOf union):
                    {
                        foreach(OwlClassExpression operand in union.Operands)
                        {
                            work.Push(operand);
                        }

                        break;
                    }
                    case(OwlObjectComplementOf complement):
                    {
                        work.Push(complement.Operand);
                        break;
                    }
                    case(OwlObjectSomeValuesFrom existential):
                    {
                        work.Push(existential.Filler);
                        break;
                    }
                    case(OwlObjectAllValuesFrom universal):
                    {
                        work.Push(universal.Filler);
                        break;
                    }
                    case(OwlObjectCardinality { Filler: not null } cardinality):
                    {
                        work.Push(cardinality.Filler);
                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }

            return null;
        }

        /// <summary>Intakes a data-property assertion as a ground key-value fact: the subject registers as a ground representative (marking the contains-named bit for a named node) and the literal joins the representative's per-property value list; a literal subject names the literal remainder. A property in <see cref="DemandLoweredProperties"/> (the F3.1 router's lifted set) ADDITIONALLY lowers the assertion through the class-expression equivalence <c>ClassAssertion(a, DataHasValue(p, v))</c>: the ground path emits the marker GCI <c>O_a ⊑ DataHasValue(p, v)</c> the ordinary pipeline lowers to the value-forcing point demand plus the <c>HasValueOf</c> atom; the store and the demand feed disjoint consumers (the key join and the datatype sidecar), so the dual representation double-counts nothing. Under nominal jurisdiction (the P-GC9 suppress default) the told value still joins the jurisdiction-independent key-value store and the subject takes no ground-representative, named-root, or marker side effect; a demand-lowered property instead rides the root tier through the complex-class assertion shape — a fresh atom GCI plus the root fact — so the demand lands on the root individual and is decided per ≈-class.</summary>
        /// <param name="axiom">The data-property-assertion axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeDataPropertyAssertion(OwlDataPropertyAssertionAxiom axiom, int origin)
        {
            if(NominalJurisdiction)
            {
                if(!TryIndividualKey(axiom.Source, out Utf8String rootKey))
                {
                    RejectGroundLiteral();

                    return;
                }

                StoreKeyValue(FindIndividual(rootKey), axiom.Property.Iri, axiom.Target);
                if(DemandLoweredProperties.Contains(axiom.Property.Iri) && TryInternRootIndividual(axiom.Source, out int individual))
                {
                    int atom = Symbols.FreshAtom();
                    AddGci(Symbols.AtomReference(atom), new OwlDataHasValue(axiom.Property, axiom.Target), origin);
                    RootFacts.Add(DlClause.Create([], [DlLiteral.Concept(atom, DlTerm.Individual(individual))], origin));
                }

                return;
            }

            if(!TryIndividualKey(axiom.Source, out Utf8String key))
            {
                RejectGroundLiteral();

                return;
            }

            int marker = GroundMarker(key);
            MarkNamedRoot(axiom.Source, key);
            StoreKeyValue(FindIndividual(key), axiom.Property.Iri, axiom.Target);
            if(DemandLoweredProperties.Contains(axiom.Property.Iri))
            {
                AddGci(Symbols.AtomReference(marker), new OwlDataHasValue(axiom.Property, axiom.Target), origin);
            }
        }

        /// <summary>Appends a told data value to a representative's per-property value list in the key-value store, minting the representative bucket and the property list on first sight — the jurisdiction-independent store maintenance the ground intake path and the nominal-root suppress fork share, so the value side lands identically under either jurisdiction.</summary>
        /// <param name="representative">The subject's representative key.</param>
        /// <param name="property">The data property's IRI.</param>
        /// <param name="value">The asserted literal value.</param>
        private void StoreKeyValue(Utf8String representative, Utf8String property, Literal value)
        {
            if(!KeyValueStore.TryGetValue(representative, out Dictionary<Utf8String, List<Literal>>? properties))
            {
                properties = [];
                KeyValueStore[representative] = properties;
            }

            if(!properties.TryGetValue(property, out List<Literal>? values))
            {
                values = [];
                properties[property] = values;
            }

            values.Add(value);
        }

        /// <summary>Intakes a <c>HasKey</c> axiom as one independent ground key descriptor: an empty key list or a non-atomic keyed class names its defensive remainder — the belt-and-suspenders twin of the survey's admission grammar — and emits no descriptor. The axiom carries no clause; its whole effect is the ground join.</summary>
        /// <param name="axiom">The has-key axiom.</param>
        private void IntakeHasKey(OwlHasKeyAxiom axiom)
        {
            if(axiom.ObjectProperties.Count == 0 && axiom.DataProperties.Count == 0)
            {
                Remainder.Add(ContextRemainderNames.HasKeyEmptyKeyList);

                return;
            }

            if(axiom.Class is not OwlClassReference named)
            {
                Remainder.Add(ContextRemainderNames.HasKeyClassNotAtomic(axiom.Class.GetType().Name));

                return;
            }

            List<RawRoleId> objectRoles = [];
            foreach(OwlObjectPropertyExpression property in axiom.ObjectProperties)
            {
                objectRoles.Add(Symbols.RoleOf(property));
            }

            List<Utf8String> dataProperties = [];
            foreach(NamedNode property in axiom.DataProperties)
            {
                dataProperties.Add(property.Iri);
            }

            bool isThing = named.Class.Iri.Equals(OwlVocabulary.Thing);
            KeyDescriptors.Add(new GroundKeyDescriptor(isThing ? 0 : Symbols.AtomOf(named.Class.Iri), isThing, objectRoles, RootObjectRoles: [], dataProperties));
        }

        /// <summary>
        /// Resolves every key descriptor's object roles to their post-quotient
        /// representatives — the root key join's query keys against the
        /// per-constant index's forward-representative symbols. On a
        /// nominal-jurisdiction module an inverse-direction representative names
        /// the whole-module rejection instead: the index projects
        /// forward-representative edges only, so an inverse-direction key demand
        /// has no sound readout on the root tier. Runs after the RBox quotient;
        /// the ground tier keeps querying the raw roles against the closed graph.
        /// </summary>
        /// <returns>The whole-module rejection name, or <see langword="null"/>.</returns>
        private string? ResolveRootKeyRoles()
        {
            for(int i = 0; i < KeyDescriptors.Count; i++)
            {
                GroundKeyDescriptor descriptor = KeyDescriptors[i];
                List<RoleRepresentative> rootRoles = new(descriptor.ObjectRoles.Count);
                foreach(RawRoleId role in descriptor.ObjectRoles)
                {
                    RoleRepresentative representative = Rep(role);
                    if(NominalJurisdiction && ContextSymbolTable.IsInverse(representative.Value) && Symbols.RoleIri(role) is Utf8String iri)
                    {
                        return ContextRemainderNames.InverseKeyRoleOnNominalModule(iri);
                    }

                    rootRoles.Add(representative);
                }

                KeyDescriptors[i] = descriptor with { RootObjectRoles = rootRoles };
            }

            return null;
        }

        /// <summary>
        /// The round-0 told key-value join (one pass over the
        /// pre-join stores): per descriptor, every pair of named-class candidates
        /// (told membership; contains-named bit) sharing a named object value on
        /// every object key property (over the CLOSED graph, so told sub-property
        /// edges count) and a value-space-equal literal on every data key property
        /// merges through the union-find. An <c>Indeterminate</c> value comparison
        /// names the delegation remainder instead — the join neither merges nor
        /// assumes distinctness. Cascades a fired union enables belong to the
        /// reasoner's next fixpoint round, never to this pass. After any union the
        /// distinctness sweep re-runs with key provenance, returning the first
        /// collision.
        /// </summary>
        /// <param name="graph">The closed ground assertion graph.</param>
        /// <returns>The key-merge collision reason, or <see langword="null"/>.</returns>
        private string? RunGroundKeyJoin(GroundAssertionGraph graph)
        {
            if(KeyDescriptors.Count == 0)
            {
                return null;
            }

            bool unionFired = false;
            HashSet<Utf8String> indeterminateNamed = [];
            foreach(GroundKeyDescriptor descriptor in KeyDescriptors)
            {
                List<Utf8String> candidates = KeyCandidateRoots(descriptor);
                for(int i = 0; i < candidates.Count; i++)
                {
                    for(int j = i + 1; j < candidates.Count; j++)
                    {
                        KeyPairAgreement agreement = JudgeKeyPair(graph, candidates[i], candidates[j], descriptor, indeterminateNamed);
                        if(agreement != KeyPairAgreement.Shared)
                        {
                            continue;
                        }

                        Utf8String rootFirst = FindIndividual(candidates[i]);
                        Utf8String rootSecond = FindIndividual(candidates[j]);
                        if(!rootFirst.Equals(rootSecond))
                        {
                            UnionIndividuals(rootFirst, rootSecond);
                            KeyForcedUnions++;
                            KeyUnionPairs.Add((rootFirst, rootSecond));
                            unionFired = true;
                        }
                    }
                }
            }

            if(!unionFired)
            {
                return null;
            }

            string? reason = null;
            for(int origin = 0; origin < Module.Axioms.Count; origin++)
            {
                if(Module.Axioms[origin] is OwlDifferentIndividualsAxiom different)
                {
                    reason ??= DetectDifferentCollision(different, keyForced: true);
                }
            }

            return reason;
        }

        /// <summary>The key join's per-pair outcome.</summary>
        private enum KeyPairAgreement
        {
            /// <summary>Some key property has no shared value — the pair does not fire.</summary>
            NotShared,

            /// <summary>Every key property has a shared value — the pair merges.</summary>
            Shared,

            /// <summary>A data comparison answered <c>Indeterminate</c> — the module delegates.</summary>
            Indeterminate,
        }

        /// <summary>Judges one candidate pair against one descriptor per the prp-key shape: every object key property must share a NAMED closed target, and every data key property a value-space-equal literal; the first missing share ends the pair, and an <c>Indeterminate</c> comparison names the delegation remainder once per property.</summary>
        /// <param name="graph">The closed ground assertion graph.</param>
        /// <param name="first">The first candidate root.</param>
        /// <param name="second">The second candidate root.</param>
        /// <param name="descriptor">The key descriptor.</param>
        /// <param name="indeterminateNamed">The properties whose indeterminate remainder is already named.</param>
        /// <returns>The pair's agreement.</returns>
        private KeyPairAgreement JudgeKeyPair(GroundAssertionGraph graph, Utf8String first, Utf8String second, GroundKeyDescriptor descriptor, HashSet<Utf8String> indeterminateNamed)
        {
            foreach(RawRoleId role in descriptor.ObjectRoles)
            {
                if(!SharesObjectKeyValue(graph, first, second, role))
                {
                    return KeyPairAgreement.NotShared;
                }
            }

            foreach(Utf8String property in descriptor.DataProperties)
            {
                DatatypeValueIdentity identity = ShareDataKeyValue(first, second, property);
                if(identity == DatatypeValueIdentity.Indeterminate)
                {
                    if(indeterminateNamed.Add(property))
                    {
                        Remainder.Add(ContextRemainderNames.KeyValueComparisonIndeterminate(property));
                    }

                    return KeyPairAgreement.Indeterminate;
                }

                if(identity == DatatypeValueIdentity.Distinct)
                {
                    return KeyPairAgreement.NotShared;
                }
            }

            return KeyPairAgreement.Shared;
        }

        /// <summary>The named-class candidate roots of one descriptor: distinct union-find roots among the ground representatives whose class contains a named member and whose told memberships include the keyed class (every named root for <c>owl:Thing</c>).</summary>
        /// <param name="descriptor">The key descriptor.</param>
        /// <returns>The candidate roots.</returns>
        private List<Utf8String> KeyCandidateRoots(GroundKeyDescriptor descriptor)
        {
            List<Utf8String> roots = [];
            HashSet<Utf8String> seen = [];
            foreach(Utf8String representative in GroundRepresentatives)
            {
                Utf8String root = FindIndividual(representative);
                if(!seen.Add(root) || !NamedRoots.Contains(root))
                {
                    continue;
                }

                if(!descriptor.ClassIsThing && !(ToldMemberships.TryGetValue(root, out HashSet<int>? memberships) && memberships.Contains(descriptor.ClassAtom)))
                {
                    continue;
                }

                roots.Add(root);
            }

            return roots;
        }

        /// <summary>Whether two roots share a NAMED closed target over an object key property — the Table 9 requirement that an object key value be a named individual, tested as the contains-named bit of the target's class.</summary>
        /// <param name="graph">The closed ground assertion graph.</param>
        /// <param name="first">The first root.</param>
        /// <param name="second">The second root.</param>
        /// <param name="role">The object key property's raw directioned role.</param>
        /// <returns><see langword="true"/> when a shared named target exists.</returns>
        private bool SharesObjectKeyValue(GroundAssertionGraph graph, Utf8String first, Utf8String second, RawRoleId role)
        {
            //The closed graph is keyed by RAW directioned intake ids (its base edges
            //and super-role closure both are), so the descriptor's raw role id is the
            //query key: a told sub-property edge lifts onto it inside Close().
            IReadOnlyList<Utf8String> firstTargets = graph.TargetsOf(first, role);
            if(firstTargets.Count == 0)
            {
                return false;
            }

            IReadOnlyList<Utf8String> secondTargets = graph.TargetsOf(second, role);
            if(secondTargets.Count == 0)
            {
                return false;
            }

            foreach(Utf8String candidate in firstTargets)
            {
                Utf8String root = FindIndividual(candidate);
                if(!NamedRoots.Contains(root))
                {
                    continue;
                }

                foreach(Utf8String other in secondTargets)
                {
                    if(root.Equals(FindIndividual(other)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The three-valued shared-value judgement of two roots over a data key property: <c>Same</c> when some value pair is value-space equal (per-property existential agreement), <c>Indeterminate</c> when no pair is equal but some comparison abstains, and <c>Distinct</c> otherwise — including when either root carries no value, which simply never fires the key.</summary>
        /// <param name="first">The first root.</param>
        /// <param name="second">The second root.</param>
        /// <param name="property">The data key property's IRI.</param>
        /// <returns>The shared-value judgement.</returns>
        private DatatypeValueIdentity ShareDataKeyValue(Utf8String first, Utf8String second, Utf8String property)
        {
            if(!KeyValueStore.TryGetValue(first, out Dictionary<Utf8String, List<Literal>>? firstProperties) || !firstProperties.TryGetValue(property, out List<Literal>? firstValues))
            {
                return DatatypeValueIdentity.Distinct;
            }

            if(!KeyValueStore.TryGetValue(second, out Dictionary<Utf8String, List<Literal>>? secondProperties) || !secondProperties.TryGetValue(property, out List<Literal>? secondValues))
            {
                return DatatypeValueIdentity.Distinct;
            }

            bool indeterminate = false;
            foreach(Literal firstValue in firstValues)
            {
                foreach(Literal secondValue in secondValues)
                {
                    DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(firstValue, secondValue, Registry);
                    if(identity == DatatypeValueIdentity.Same)
                    {
                        return DatatypeValueIdentity.Same;
                    }

                    indeterminate |= identity == DatatypeValueIdentity.Indeterminate;
                }
            }

            return indeterminate ? DatatypeValueIdentity.Indeterminate : DatatypeValueIdentity.Distinct;
        }

        /// <summary>
        /// The told ground-counting pigeonhole search (behind the
        /// rider flag): per told counting constraint, the closed successors of the
        /// constrained representative over the counted role — filtered to told
        /// filler members when qualified — clash when some <c>bound + 1</c> of them
        /// are pairwise told-distinct. The clique search is exact within
        /// <see cref="GroundCountingCliqueBound"/> successors and silent above it;
        /// a clashing subject's counting edges skip the delegation remainder so the
        /// decided clash surfaces instead of delegating.
        /// </summary>
        /// <param name="graph">The closed ground assertion graph.</param>
        /// <param name="pigeonholeSubjects">The subjects whose counting-edge remainders the scan suppresses; appended per clash.</param>
        /// <returns>The first pigeonhole clash reason, or <see langword="null"/>.</returns>
        private string? RunGroundPigeonholeSearch(GroundAssertionGraph graph, HashSet<Utf8String> pigeonholeSubjects)
        {
            if(CountingConstraints.Count == 0)
            {
                return null;
            }

            HashSet<(Utf8String First, Utf8String Second)> distinct = BuildToldDistinctPairs();
            if(distinct.Count == 0)
            {
                return null;
            }

            string? reason = null;
            foreach(GroundCountingConstraint constraint in CountingConstraints)
            {
                Utf8String subject = FindIndividual(constraint.Subject);
                IReadOnlyList<Utf8String> targets = graph.TargetsOf(subject, constraint.Role);
                if(targets.Count <= constraint.Bound)
                {
                    continue;
                }

                List<Utf8String> successors = [];
                HashSet<Utf8String> seen = [];
                foreach(Utf8String target in targets)
                {
                    Utf8String root = FindIndividual(target);
                    if(!seen.Add(root))
                    {
                        continue;
                    }

                    if(!constraint.FillerIsThing && !(ToldMemberships.TryGetValue(root, out HashSet<int>? memberships) && memberships.Contains(constraint.FillerAtom)))
                    {
                        continue;
                    }

                    successors.Add(root);
                }

                if(successors.Count <= constraint.Bound || successors.Count > GroundCountingCliqueBound)
                {
                    continue;
                }

                if(HasDistinctClique(successors, constraint.Bound + 1, distinct, RiderCliqueOnEnumeratorSurface))
                {
                    pigeonholeSubjects.Add(subject);
                    reason ??= GroundClashReasons.GroundCountingPigeonhole(subject);
                }
            }

            return reason;
        }

        /// <summary>The told pairwise-distinct pairs, both orders, from every <c>DifferentIndividuals</c> axiom's members resolved through the union-find.</summary>
        /// <returns>The distinct pairs.</returns>
        private HashSet<(Utf8String First, Utf8String Second)> BuildToldDistinctPairs()
        {
            HashSet<(Utf8String First, Utf8String Second)> pairs = [];
            foreach(OwlAxiom axiom in Module.Axioms)
            {
                if(axiom is not OwlDifferentIndividualsAxiom different)
                {
                    continue;
                }

                IReadOnlyList<RdfTerm> individuals = different.Individuals;
                for(int i = 0; i < individuals.Count; i++)
                {
                    if(!TryIndividualKey(individuals[i], out Utf8String keyI))
                    {
                        continue;
                    }

                    Utf8String rootI = FindIndividual(keyI);
                    for(int j = i + 1; j < individuals.Count; j++)
                    {
                        if(!TryIndividualKey(individuals[j], out Utf8String keyJ))
                        {
                            continue;
                        }

                        Utf8String rootJ = FindIndividual(keyJ);
                        pairs.Add((rootI, rootJ));
                        pairs.Add((rootJ, rootI));
                    }
                }
            }

            return pairs;
        }

        /// <summary>The individual key: a named individual by IRI, an anonymous one by label, an engine-minted one by its deterministic Skolem IRI; a literal has none (mirrors the tableau and EL arms).</summary>
        /// <param name="individual">The individual term.</param>
        /// <param name="key">The key.</param>
        /// <returns><see langword="true"/> for a named, anonymous, or engine-minted individual.</returns>
        private static bool TryIndividualKey(RdfTerm individual, out Utf8String key)
        {
            switch(individual)
            {
                case(NamedNode named):
                {
                    key = named.Iri;

                    return true;
                }
                case(BlankNode blank):
                {
                    key = blank.Label;

                    return true;
                }
                case(EngineNode engine):
                {
                    key = engine.SkolemIri();

                    return true;
                }
                default:
                {
                    key = default;

                    return false;
                }
            }
        }

        /// <summary>The interning origin of a nominal-jurisdiction ABox individual term: IRI-denoted for a named node, blank-node for an anonymous one, engine-minted for an engine node — the bit the root-intake mint site threads so the key-join candidacy filter reads a distinction the interning key cannot recover.</summary>
        /// <param name="individual">The individual term, already known to be a named, blank, or engine-minted node (a literal is rejected before interning by <see cref="TryIndividualKey"/>).</param>
        /// <returns>The individual's origin.</returns>
        private static IndividualOrigin IndividualOriginOf(RdfTerm individual)
        {
            return individual switch
            {
                NamedNode => IndividualOrigin.IriDenoted,
                BlankNode => IndividualOrigin.BlankNode,
                EngineNode => IndividualOrigin.EngineMinted,
                _ => throw new InvalidOperationException("A nominal-jurisdiction ABox individual is a named, blank, or engine-minted node; a literal is rejected before interning."),
            };
        }

        /// <summary>Lowers a class assertion to the marker GCI <c>O_a ⊑ C</c> through the ordinary GCI pipeline, recording a named class as a told membership for the key join and the counting rider; a literal individual names the literal remainder and emits no marker.</summary>
        /// <param name="axiom">The class-assertion axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeClassAssertion(OwlClassAssertionAxiom axiom, int origin)
        {
            if(NominalJurisdiction)
            {
                IntakeRootClassAssertion(axiom, origin);

                return;
            }

            if(!TryIndividualKey(axiom.Individual, out Utf8String key))
            {
                RejectGroundLiteral();

                return;
            }

            int marker = GroundMarker(key);
            MarkNamedRoot(axiom.Individual, key);
            if(axiom.Class is OwlClassReference named)
            {
                Utf8String representative = FindIndividual(key);
                if(!ToldMemberships.TryGetValue(representative, out HashSet<int>? memberships))
                {
                    memberships = [];
                    ToldMemberships[representative] = memberships;
                }

                memberships.Add(Symbols.AtomOf(named.Class.Iri));
            }

            AddGci(Symbols.AtomReference(marker), NormalizeGroundCountingComplement(axiom.Class), origin);
        }

        /// <summary>
        /// The counting rider's engine-side NNF of a complement-wrapped ground
        /// counting assertion: a told
        /// <c>¬(≥n S.C)</c> with a bound of two or higher rewrites to the
        /// equivalent told <c>≤(n−1) S.C</c>, so the direct max lowering carries
        /// the ground marker into the DL4 emission and the told constraint is
        /// recorded for the pigeonhole search — the un-rewritten form loses the
        /// marker behind the complement's fresh abstraction atoms and keeps the
        /// module on the delegating counting remainder. Any other shape passes
        /// through untouched, and with the rider disabled the assertion lowers
        /// as written.
        /// </summary>
        /// <param name="assertedClass">The asserted class expression.</param>
        /// <returns>The expression the marker GCI lowers.</returns>
        private OwlClassExpression NormalizeGroundCountingComplement(OwlClassExpression assertedClass)
        {
            if(RiderEnabled && assertedClass is OwlObjectComplementOf { Operand: OwlObjectCardinality { Kind: OwlCardinalityKind.Min, Cardinality: >= 2 } min })
            {
                return new OwlObjectCardinality(OwlCardinalityKind.Max, min.Cardinality - 1, min.Property, min.Filler);
            }

            return assertedClass;
        }

        /// <summary>Records an object-property assertion as a representative-resolved pending edge; a literal source or target names the literal remainder and records no edge.</summary>
        /// <param name="axiom">The object-property-assertion axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeObjectPropertyAssertion(OwlObjectPropertyAssertionAxiom axiom, int origin)
        {
            if(NominalJurisdiction)
            {
                if(TryInternRootIndividual(axiom.Source, out int rootSource) && TryInternRootIndividual(axiom.Target, out int rootTarget))
                {
                    PendingRootEdges.Add((rootSource, Symbols.RoleOf(axiom.Property.Iri), rootTarget, origin));
                }
                else
                {
                    RejectGroundLiteral();
                }

                return;
            }

            if(!TryIndividualKey(axiom.Source, out Utf8String source) || !TryIndividualKey(axiom.Target, out Utf8String target))
            {
                RejectGroundLiteral();

                return;
            }

            GroundMarker(source);
            GroundMarker(target);
            MarkNamedRoot(axiom.Source, source);
            MarkNamedRoot(axiom.Target, target);
            PendingGroundEdges.Add(new PendingGroundEdge(FindIndividual(source), FindIndividual(target), Symbols.RoleOf(axiom.Property.Iri), origin));
        }

        /// <summary>Records a negative object-property assertion as a closure obligation over representatives, the property inverse-normalized through its directioned id; a literal source or target names the literal remainder.</summary>
        /// <param name="axiom">The negative-object-property-assertion axiom.</param>
        private void IntakeNegativeObjectPropertyAssertion(OwlNegativeObjectPropertyAssertionAxiom axiom)
        {
            if(NominalJurisdiction)
            {
                if(TryInternRootIndividual(axiom.Source, out int rootSource) && TryInternRootIndividual(axiom.Target, out int rootTarget))
                {
                    PendingRootNegativeEdges.Add((rootSource, Symbols.RoleOf(axiom.Property), rootTarget, Origin: -1));
                }
                else
                {
                    RejectGroundLiteral();
                }

                return;
            }

            if(!TryIndividualKey(axiom.Source, out Utf8String source) || !TryIndividualKey(axiom.Target, out Utf8String target))
            {
                RejectGroundLiteral();

                return;
            }

            GroundMarker(source);
            GroundMarker(target);
            MarkNamedRoot(axiom.Source, source);
            MarkNamedRoot(axiom.Target, target);
            NegativeObligations.Add((FindIndividual(source), Symbols.RoleOf(axiom.Property), FindIndividual(target)));
        }

        /// <summary>Emits one ground-edge clause <c>O_a(x) → r(x, f_ab(x))</c> per admitted object-property assertion, the role rewritten to its post-quotient representative and the successor the ordered pair's shared function symbol, and records the function's designated target.</summary>
        private void EmitGroundEdges()
        {
            foreach(PendingGroundEdge edge in PendingGroundEdges)
            {
                int symbol = GroundEdgeSymbol(edge.Source, edge.Target);
                Clauses.Add(DlClause.Create(
                    [DlLiteral.Concept(GroundMarkers[edge.Source], DlTerm.Central)],
                    [RoleAtom(Rep(edge.Role), DlTerm.Central, DlTerm.Function(symbol))],
                    edge.Origin));
            }
        }

        /// <summary>The ground-edge function symbol of an ordered representative pair — minted once and shared across every role asserted on the pair, recording the target it denotes.</summary>
        /// <param name="source">The source representative.</param>
        /// <param name="target">The target representative.</param>
        /// <returns>The pair's shared ground-edge function symbol id.</returns>
        private int GroundEdgeSymbol(Utf8String source, Utf8String target)
        {
            if(GroundEdgeSymbols.TryGetValue((source, target), out int existing))
            {
                return existing;
            }

            int symbol = Symbols.MintFunctionSymbol(GroundMarkers[target]);
            GroundEdgeSymbols[(source, target)] = symbol;
            GroundTargetByFunction[symbol] = target;

            return symbol;
        }

        /// <summary>Names the counting-capable remainder for every asserted edge whose role is in the DL4 counting family: the <see cref="CountingTargets"/> set closed down over told sub-roles and inverses. Runs after normalization fills the targets and after the graph closure, deduplicating by name. A pigeonhole-clashing subject's edges skip the remainder (either endpoint — the mirrored spelling counts too) so the decided clash surfaces instead of delegating.</summary>
        /// <param name="pigeonholeSubjects">The subjects the rider's search clashed; empty with the rider off, reproducing the pre-rider scan exactly.</param>
        private void ScanGroundCountingEdges(HashSet<Utf8String> pigeonholeSubjects)
        {
            HashSet<string> named = [];
            foreach(PendingGroundEdge edge in PendingGroundEdges)
            {
                if(pigeonholeSubjects.Contains(edge.Source) || pigeonholeSubjects.Contains(edge.Target))
                {
                    continue;
                }

                if(IsInCountingFamily(edge.Role) && Symbols.RoleIri(edge.Role) is Utf8String iri)
                {
                    string name = ContextRemainderNames.GroundEdgeOnCountingRole(iri);
                    if(named.Add(name))
                    {
                        Remainder.Add(name);
                    }
                }
            }
        }

        /// <summary>Whether a role is in the counting-capable family: itself or its inverse a told sub-role of a DL4 counting target.</summary>
        /// <param name="role">The directioned role.</param>
        /// <returns><see langword="true"/> when the role is counting-capable.</returns>
        private bool IsInCountingFamily(RawRoleId role)
        {
            return IsSubRoleOfCountingTarget(role) || IsSubRoleOfCountingTarget(ContextSymbolTable.Inverse(role));
        }

        /// <summary>Whether a directioned role is a told sub-role of (or is) a DL4 counting target.</summary>
        /// <param name="role">The directioned role.</param>
        /// <returns><see langword="true"/> when a counting target is the role or one of its super-roles.</returns>
        private bool IsSubRoleOfCountingTarget(RawRoleId role)
        {
            if(CountingTargetContains(role))
            {
                return true;
            }

            if(SuperRoles.TryGetValue(role, out HashSet<RawRoleId>? supers))
            {
                foreach(RawRoleId super in supers)
                {
                    if(CountingTargetContains(super))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether a raw directioned role's forward representative base is a DL4 counting target.</summary>
        /// <param name="role">The directioned role.</param>
        /// <returns><see langword="true"/> when the role's forward representative base was counted.</returns>
        private bool CountingTargetContains(RawRoleId role)
        {
            return CountingTargets.Contains(PrimaryOf(Rep(role)));
        }

        /// <summary>Builds the ground assertion graph from the pending edges, the told RBox facts, the reflexive characteristic, and the closure clash obligations — the re-runnable closure handle the reasoner drives and the Self-ghost pass augments.</summary>
        /// <returns>The ground assertion graph, before closure.</returns>
        private GroundAssertionGraph BuildGroundGraph()
        {
            List<(Utf8String Source, RawRoleId Role, Utf8String Target)> baseEdges = [];
            foreach(PendingGroundEdge edge in PendingGroundEdges)
            {
                baseEdges.Add((edge.Source, edge.Role, edge.Target));
            }

            List<(IReadOnlyList<RawRoleId> Word, RawRoleId Super)> chains = [];
            foreach((List<RawRoleId> chain, RawRoleId super) in ChainInclusions)
            {
                chains.Add((chain, super));
            }

            List<(RawRoleId First, RawRoleId Second)> disjointPairs = [];
            foreach(DisjointRolePair pair in DisjointRolePairs)
            {
                disjointPairs.Add((pair.First, pair.Second));
            }

            return new GroundAssertionGraph(Symbols, GroundRepresentatives, baseEdges, SuperRoles, chains, ReflexiveRoles, NegativeObligations, disjointPairs, IrreflexiveRoles);
        }

        /// <summary>
        /// Resolves the raw functional directioned roles collected at intake to
        /// their post-quotient representatives — the successor-sharing keys the DL2
        /// emission tests. Runs after the role quotient exists and only under
        /// <see cref="EqualityLowering.SuccessorSharing"/>; the general clause never
        /// populates the intake set, so this is a no-op there. Keying by the
        /// representative (not the forward base) keeps forward and inverse
        /// functionality distinct except where the quotient itself collapses a
        /// symmetric role with its own inverse — exactly where the two directions
        /// denote one relation and sharing across them is sound.
        /// </summary>
        private void CollectFunctionalDirectionedRoles()
        {
            foreach(RawRoleId intakeRole in FunctionalRoleIntakeIds)
            {
                FunctionalDirectionedRoles.Add(Rep(intakeRole));
            }
        }

        /// <summary>RBox processing: builds the closure, quotients roles by mutual-inclusion equivalence, checks regularity over the representatives, computes simplicity, and materialises the representative-level RIAs the automata read.</summary>
        /// <returns><see langword="true"/> when the RBox is regular; <see langword="false"/> when a cycle or inadmissible shape refuses the whole module.</returns>
        private bool ProcessRbox()
        {
            BuildClosure();
            ComputeQuotient();
            if(!IsRegular())
            {
                return false;
            }

            ComputeSimplicity();
            BuildRepresentativeRias();

            return true;
        }

        /// <summary>
        /// Computes the role quotient: each directioned role's representative is the
        /// minimal id among the roles it is mutually <c>⊑*</c>-included with. The
        /// closure is inverse-coupled, so a symmetric role (whose <c>r⁻ ⊑ r</c>
        /// couples to <c>r ⊑ r⁻</c>) collapses with its own inverse into one
        /// self-inverse class, and the representative of a class's inverse class is
        /// the representative's inverse.
        /// </summary>
        private void ComputeQuotient()
        {
            for(int role = 0; role < Symbols.RoleCount; role++)
            {
                int representative = role;
                if(SuperRoles.TryGetValue(new RawRoleId(role), out HashSet<RawRoleId>? supers))
                {
                    foreach(RawRoleId candidate in supers)
                    {
                        if(candidate.Value < representative && SuperRoles.TryGetValue(candidate, out HashSet<RawRoleId>? back) && back.Contains(new RawRoleId(role)))
                        {
                            representative = candidate.Value;
                        }
                    }
                }

                RoleRepresentatives.Add(representative);
            }
        }

        /// <summary>The representative of a raw directioned role; a role interned after RBox processing (a DL4 counting role) represents itself.</summary>
        /// <param name="role">The directioned role id.</param>
        /// <returns>The representative id.</returns>
        private RoleRepresentative Rep(RawRoleId role)
        {
            return role.Value < RoleRepresentatives.Count ? new RoleRepresentative(RoleRepresentatives[role.Value]) : new RoleRepresentative(role.Value);
        }

        /// <summary>Whether a representative's class contains its own inverse — the post-quotient marker of a symmetric role, which is exactly what a <c>R⁻ ⊑ R</c> RIA collapses to.</summary>
        /// <param name="representative">The representative role id.</param>
        /// <returns><see langword="true"/> for a self-inverse (symmetric) class.</returns>
        private bool IsSymmetricClass(RoleRepresentative representative)
        {
            return Rep(ContextSymbolTable.Inverse(representative.RawMemberId)) == representative;
        }

        /// <summary>
        /// The primary (forward-facing) representative a directioned representative's
        /// automaton is built under: the even member of the representative pair. A
        /// self-inverse class's representative is already even (the class contains
        /// both directions and the minimum of an adjacent pair is even); for disjoint
        /// direction classes the inverse class's representative is the
        /// representative's inverse, so clearing the direction bit lands on it.
        /// The forward member of a representative is itself a representative: an
        /// even representative is its own forward, and an odd representative's
        /// forward is the minimum of the coupled inverse class.
        /// </summary>
        /// <param name="representative">The representative role id.</param>
        /// <returns>The primary representative id.</returns>
        private static RoleRepresentative PrimaryOf(RoleRepresentative representative)
        {
            return new RoleRepresentative(ContextSymbolTable.Forward(representative.Value));
        }

        /// <summary>
        /// Materialises the representative-level RIAs the automata read: every arc
        /// and chain rewritten to representatives and closed under inversion (the
        /// RBox of The Even More Irresistible SROIQ, KR 2006, is
        /// inversion-closed), EXCEPT onto a self-inverse
        /// (symmetric) super class — there the mirrored words are supplied by the
        /// STEP-2 fold, so adding them here would encode the mirror twice.
        /// Tautological arcs are dropped and duplicates removed.
        /// </summary>
        private void BuildRepresentativeRias()
        {
            HashSet<(RoleRepresentative Sub, RoleRepresentative Super)> arcKeys = [];
            foreach((RawRoleId sub, RawRoleId super) in RoleInclusions)
            {
                RoleRepresentative repSub = Rep(sub);
                RoleRepresentative repSuper = Rep(super);
                AddRepArc(arcKeys, repSub, repSuper);

                RoleRepresentative mirrorSuper = Rep(ContextSymbolTable.Inverse(repSuper.RawMemberId));
                if(mirrorSuper != repSuper)
                {
                    AddRepArc(arcKeys, Rep(ContextSymbolTable.Inverse(repSub.RawMemberId)), mirrorSuper);
                }
            }

            HashSet<string> chainKeys = [];
            foreach((List<RawRoleId> chain, RawRoleId super) in ChainInclusions)
            {
                List<RoleRepresentative> word = new(chain.Count);
                foreach(RawRoleId link in chain)
                {
                    word.Add(Rep(link));
                }

                RoleRepresentative repSuper = Rep(super);
                AddRepChain(chainKeys, word, repSuper);

                RoleRepresentative mirrorSuper = Rep(ContextSymbolTable.Inverse(repSuper.RawMemberId));
                if(mirrorSuper != repSuper)
                {
                    List<RoleRepresentative> mirrored = new(word.Count);
                    for(int i = word.Count - 1; i >= 0; i--)
                    {
                        mirrored.Add(Rep(ContextSymbolTable.Inverse(word[i].RawMemberId)));
                    }

                    AddRepChain(chainKeys, mirrored, mirrorSuper);
                }
            }
        }

        /// <summary>Adds a representative-level arc unless it is a within-class tautology or a duplicate.</summary>
        /// <param name="keys">The deduplication set.</param>
        /// <param name="sub">The representative sub-role.</param>
        /// <param name="super">The representative super-role.</param>
        private void AddRepArc(HashSet<(RoleRepresentative Sub, RoleRepresentative Super)> keys, RoleRepresentative sub, RoleRepresentative super)
        {
            if(sub != super && keys.Add((sub, super)))
            {
                RepArcs.Add((sub, super));
            }
        }

        /// <summary>Adds a representative-level chain word unless it is a duplicate.</summary>
        /// <param name="keys">The deduplication set, keyed on the rendered word.</param>
        /// <param name="word">The representative letters, in composition order.</param>
        /// <param name="super">The representative super-role.</param>
        private void AddRepChain(HashSet<string> keys, List<RoleRepresentative> word, RoleRepresentative super)
        {
            string key = $"{super.Value}|{string.Join(',', word.ConvertAll(static letter => letter.Value))}";
            if(keys.Add(key))
            {
                RepChains.Add((word, super));
            }
        }

        /// <summary>Builds the reflexive-transitive role-inclusion closure over directioned roles, coupling each inclusion with its inverse.</summary>
        private void BuildClosure()
        {
            foreach((RawRoleId sub, RawRoleId super) in RoleInclusions)
            {
                AddSuperRole(sub, super);
                AddSuperRole(ContextSymbolTable.Inverse(sub), ContextSymbolTable.Inverse(super));
            }

            bool grew = true;
            while(grew)
            {
                grew = false;
                foreach(KeyValuePair<RawRoleId, HashSet<RawRoleId>> entry in SuperRoles)
                {
                    List<RawRoleId> reachable = [];
                    foreach(RawRoleId super in entry.Value)
                    {
                        if(SuperRoles.TryGetValue(super, out HashSet<RawRoleId>? next))
                        {
                            reachable.AddRange(next);
                        }
                    }

                    foreach(RawRoleId super in reachable)
                    {
                        grew |= entry.Value.Add(super);
                    }
                }
            }
        }

        /// <summary>Records a directioned super-role of a directioned role.</summary>
        /// <param name="sub">The sub-role.</param>
        /// <param name="super">The super-role.</param>
        private void AddSuperRole(RawRoleId sub, RawRoleId super)
        {
            if(!SuperRoles.TryGetValue(sub, out HashSet<RawRoleId>? set))
            {
                set = [];
                SuperRoles[sub] = set;
            }

            set.Add(super);
        }

        /// <summary>
        /// Checks RBox regularity over the quotiented roles: every chain inclusion
        /// must match one of the five admissible SROIQ2006 shapes — with the
        /// super-role occurring only as the EXACT representative
        /// at an endpoint (transitivity <c>R∘R ⊑ R</c>, the R-prefix and R-suffix
        /// forms); the super-role or its inverse anywhere else in a chain, or at
        /// both ends of a chain longer than two, is inadmissible. The induced
        /// strict order — each interior chain letter's class below the super's
        /// class, plus each strict (cross-class) hierarchy inclusion's sub-class
        /// below its super-class, roles and inverses identified — must be acyclic.
        /// Mutual and equivalent inclusions are one class after the quotient and so
        /// impose no edge; the hierarchy edges make the order extend the told
        /// hierarchy, which also keeps the automaton letter-dependency graph a
        /// strict DAG.
        /// </summary>
        /// <returns><see langword="true"/> when the RBox is regular.</returns>
        private bool IsRegular()
        {
            Dictionary<int, HashSet<int>> precedes = [];
            HashSet<int> vertices = [];

            foreach((RawRoleId sub, RawRoleId super) in RoleInclusions)
            {
                int subBase = BaseOf(Rep(sub));
                int superBase = BaseOf(Rep(super));
                vertices.Add(subBase);
                vertices.Add(superBase);
                if(subBase != superBase)
                {
                    AddPrecedence(precedes, subBase, superBase);
                }
            }

            foreach((List<RawRoleId> chain, RawRoleId super) in ChainInclusions)
            {
                RoleRepresentative representative = Rep(super);
                int superBase = BaseOf(representative);
                vertices.Add(superBase);

                bool firstExact = Rep(chain[0]) == representative;
                bool lastExact = Rep(chain[^1]) == representative;
                if(chain.Count == 2 && firstExact && lastExact)
                {
                    continue;
                }

                if(firstExact && lastExact)
                {
                    return false;
                }

                for(int i = 0; i < chain.Count; i++)
                {
                    if((i == 0 && firstExact) || (i == chain.Count - 1 && lastExact))
                    {
                        continue;
                    }

                    RoleRepresentative letter = Rep(chain[i]);
                    vertices.Add(BaseOf(letter));
                    if(BaseOf(letter) == superBase)
                    {
                        return false;
                    }

                    AddPrecedence(precedes, BaseOf(letter), superBase);
                }
            }

            return IsAcyclic(precedes, vertices);
        }

        /// <summary>Records that one base role strictly precedes another in the regularity order.</summary>
        /// <param name="precedes">The strict-order adjacency.</param>
        /// <param name="lower">The lower base role.</param>
        /// <param name="higher">The higher base role.</param>
        private static void AddPrecedence(Dictionary<int, HashSet<int>> precedes, int lower, int higher)
        {
            if(!precedes.TryGetValue(lower, out HashSet<int>? set))
            {
                set = [];
                precedes[lower] = set;
            }

            set.Add(higher);
        }

        /// <summary>Whether the strict order is acyclic, by an iterative Kahn topological sort over the base-role vertices.</summary>
        /// <param name="precedes">The strict-order adjacency.</param>
        /// <param name="bases">The base-role vertices.</param>
        /// <returns><see langword="true"/> when acyclic.</returns>
        private static bool IsAcyclic(Dictionary<int, HashSet<int>> precedes, HashSet<int> bases)
        {
            Dictionary<int, int> indegree = [];
            foreach(int vertex in bases)
            {
                indegree.TryAdd(vertex, 0);
            }

            foreach(KeyValuePair<int, HashSet<int>> entry in precedes)
            {
                foreach(int higher in entry.Value)
                {
                    indegree[higher] = indegree.GetValueOrDefault(higher) + 1;
                }
            }

            Queue<int> ready = [];
            foreach(KeyValuePair<int, int> entry in indegree)
            {
                if(entry.Value == 0)
                {
                    ready.Enqueue(entry.Key);
                }
            }

            int settled = 0;
            while(ready.Count > 0)
            {
                int vertex = ready.Dequeue();
                settled++;
                if(precedes.TryGetValue(vertex, out HashSet<int>? higher))
                {
                    foreach(int next in higher)
                    {
                        indegree[next]--;
                        if(indegree[next] == 0)
                        {
                            ready.Enqueue(next);
                        }
                    }
                }
            }

            return settled == indegree.Count;
        }

        /// <summary>
        /// Computes non-simplicity (the OWL 2 DL reading of simple roles): a role
        /// is non-simple iff a length-at-least-2 inclusion right-hand side reaches
        /// it through the closure, propagated to inverses. A symmetric-only role
        /// stays simple; a transitive role does not. The traversal walks raw ids;
        /// the exposed set records representatives, sound because non-simplicity
        /// is a class property — the raw walk crosses every mutual arc, so
        /// whenever one member of a class is reached its whole class is reached.
        /// </summary>
        private void ComputeSimplicity()
        {
            HashSet<RawRoleId> visited = [];
            Queue<RawRoleId> worklist = [];
            foreach((List<RawRoleId> chain, RawRoleId super) in ChainInclusions)
            {
                MarkNonSimple(super, visited, worklist);
            }

            while(worklist.Count > 0)
            {
                RawRoleId role = worklist.Dequeue();
                if(SuperRoles.TryGetValue(role, out HashSet<RawRoleId>? supers))
                {
                    foreach(RawRoleId super in supers)
                    {
                        MarkNonSimple(super, visited, worklist);
                    }
                }
            }
        }

        /// <summary>Marks a raw directioned role and its inverse non-simple — recording their representatives — enqueuing a newly visited role for upward propagation; the inverse is marked but never enqueued, its supers being covered by the closure's inverse coupling.</summary>
        /// <param name="role">The raw directioned role.</param>
        /// <param name="visited">The raw roles already marked, guarding the worklist.</param>
        /// <param name="worklist">The propagation worklist.</param>
        private void MarkNonSimple(RawRoleId role, HashSet<RawRoleId> visited, Queue<RawRoleId> worklist)
        {
            if(visited.Add(role))
            {
                worklist.Enqueue(role);
                NonSimpleRoles.Add(Rep(role));
            }

            RawRoleId inverse = ContextSymbolTable.Inverse(role);
            if(visited.Add(inverse))
            {
                NonSimpleRoles.Add(Rep(inverse));
            }
        }

        /// <summary>The forward base index of a representative role, the vertex identity for regularity and simplicity coupling.</summary>
        /// <param name="role">The representative role.</param>
        /// <returns>The base id.</returns>
        private static int BaseOf(RoleRepresentative role)
        {
            return ContextSymbolTable.Forward(role.Value) / 2;
        }

        /// <summary>Normalization and emission: transforms each origin's GCIs to KR 2016 Table 1 shapes and emits, transactionally — a rejection discards the origin's clauses whole.</summary>
        private void NormalizeAndEmit()
        {
            Dictionary<int, List<PendingGci>> byOrigin = [];
            foreach(PendingGci gci in Pending)
            {
                if(!byOrigin.TryGetValue(gci.Origin, out List<PendingGci>? list))
                {
                    list = [];
                    byOrigin[gci.Origin] = list;
                }

                list.Add(gci);
            }

            foreach(KeyValuePair<int, List<PendingGci>> entry in byOrigin)
            {
                NormalizeOrigin(entry.Key, entry.Value);
            }
        }

        /// <summary>Normalizes one origin's GCIs to a local clause buffer, committing on success or recording the first rejection whole — including rolling back the origin's deferred chain eliminations.</summary>
        /// <param name="origin">The origin axiom's index.</param>
        /// <param name="gcis">The origin's GCIs.</param>
        private void NormalizeOrigin(int origin, List<PendingGci> gcis)
        {
            Queue<(OwlClassExpression Sub, OwlClassExpression Super)> work = [];
            foreach(PendingGci gci in gcis)
            {
                work.Enqueue((gci.Sub, gci.Super));
            }

            List<DlClause> local = [];
            int pendingMark = PendingEliminations.Count;
            string? rejection = null;
            while(rejection is null && work.Count > 0)
            {
                (OwlClassExpression sub, OwlClassExpression super) = work.Dequeue();
                rejection = Step(sub, super, work, local, origin);
            }

            if(rejection is not null)
            {
                Remainder.Add(rejection);
                PendingEliminations.RemoveRange(pendingMark, PendingEliminations.Count - pendingMark);

                return;
            }

            Clauses.AddRange(local);
        }

        /// <summary>
        /// One normalization step over a GCI: folds top/bottom, splits conjunctive
        /// superclasses and disjunctive subclasses, lowers complements, routes
        /// existentials/universals/cardinalities to their KR 2016 Table 1 shapes
        /// with polarity-correct fresh names, or emits DL1 when the sides are
        /// atomic. Returns a named rejection for an out-of-fragment construct,
        /// discarding the origin whole.
        /// </summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist, extended with rewrites.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? Step(OwlClassExpression sub, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            if(IsBottom(sub) || IsTop(super))
            {
                return null;
            }

            switch(super)
            {
                case(OwlObjectIntersectionOf intersection):
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Enqueue((sub, operand));
                    }

                    return null;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Enqueue((new OwlObjectIntersectionOf([sub, complement.Operand]), Nothing()));

                    return null;
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Exact } exact):
                {
                    work.Enqueue((sub, new OwlObjectCardinality(OwlCardinalityKind.Min, exact.Cardinality, exact.Property, exact.Filler)));
                    work.Enqueue((sub, new OwlObjectCardinality(OwlCardinalityKind.Max, exact.Cardinality, exact.Property, exact.Filler)));

                    return null;
                }
            }

            switch(sub)
            {
                case(OwlObjectUnionOf union):
                {
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        work.Enqueue((operand, super));
                    }

                    return null;
                }
                case(OwlObjectComplementOf complement):
                {
                    work.Enqueue((Thing(), new OwlObjectUnionOf([complement.Operand, super])));

                    return null;
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Exact } exact):
                {
                    work.Enqueue((new OwlObjectIntersectionOf([
                        new OwlObjectCardinality(OwlCardinalityKind.Min, exact.Cardinality, exact.Property, exact.Filler),
                        new OwlObjectCardinality(OwlCardinalityKind.Max, exact.Cardinality, exact.Property, exact.Filler)]), super));

                    return null;
                }
            }

            return StepRestrictions(sub, super, work, local, origin);
        }

        /// <summary>Handles the restriction-shaped GCIs and the final DL1 emission, after the structural folds of <see cref="Step"/>.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepRestrictions(OwlClassExpression sub, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            switch(super)
            {
                case(OwlObjectAllValuesFrom universal):
                {
                    return StepUniversalSuper(sub, universal, work, local, origin);
                }
                case(OwlObjectSomeValuesFrom existential):
                {
                    return StepMinSuper(sub, existential.Property, 1, existential.Filler, work, local, origin);
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Min } min):
                {
                    return StepMinSuper(sub, min.Property, min.Cardinality, min.Filler ?? Thing(), work, local, origin);
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Max } max):
                {
                    return StepMaxSuper(sub, max, work, local, origin);
                }
                case(OwlObjectOneOf oneOfSuper):
                {
                    return StepOneOfSuper(sub, oneOfSuper, work, local, origin);
                }
                case(OwlObjectHasValue hasValueSuper):
                {
                    //The fresh-singleton normal form: ∃r.{o} lowers as ∃r.N_o through the
                    //existing restriction machinery (the published DL-clause
                    //grammar bars constants in ontology-clause bodies, so the direct
                    //S(x, o) route stays out of the intake).
                    if(hasValueSuper.Individual is not NamedNode namedSuperFiller)
                    {
                        return ContextRemainderNames.AnonymousIndividualInNominal;
                    }

                    return StepMinSuper(sub, hasValueSuper.Property, 1, NominalSingletonReference(Symbols.InternIndividual(namedSuperFiller.Iri, IndividualOrigin.IriDenoted), origin), work, local, origin);
                }
                case(OwlObjectHasSelf self):
                {
                    return StepSelfSuper(sub, self, work, local, origin);
                }
                case(OwlDataSomeValuesFrom or OwlDataAllValuesFrom or OwlDataHasValue or OwlDataCardinality):
                {
                    return StepDataSuper(sub, super, work, local, origin);
                }
            }

            switch(sub)
            {
                case(OwlObjectSomeValuesFrom existential):
                {
                    return StepExistentialSub(existential, super, work, local, origin);
                }
                case(OwlObjectAllValuesFrom universal):
                {
                    return StepUniversalSub(universal, super, work);
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Min } min):
                {
                    work.Enqueue((Thing(), new OwlObjectUnionOf([super, new OwlObjectCardinality(OwlCardinalityKind.Max, min.Cardinality - 1, min.Property, min.Filler)])));

                    return null;
                }
                case(OwlObjectCardinality { Kind: OwlCardinalityKind.Max } max):
                {
                    work.Enqueue((Thing(), new OwlObjectUnionOf([super, new OwlObjectCardinality(OwlCardinalityKind.Min, max.Cardinality + 1, max.Property, max.Filler)])));

                    return null;
                }
                case(OwlObjectOneOf oneOfSub):
                {
                    return StepOneOfSub(oneOfSub, super, work, local, origin);
                }
                case(OwlObjectHasValue hasValueSub):
                {
                    //∃r.{o} ⊑ B rewrites over the fresh singleton and takes the ordinary
                    //existential-subclass route (the DL3 shape S(z, x) ∧ N_o(x) → B(z);
                    //a non-simple role falls to the automaton mirror as usual).
                    if(hasValueSub.Individual is not NamedNode namedSubFiller)
                    {
                        return ContextRemainderNames.AnonymousIndividualInNominal;
                    }

                    return StepExistentialSub(new OwlObjectSomeValuesFrom(hasValueSub.Property, NominalSingletonReference(Symbols.InternIndividual(namedSubFiller.Iri, IndividualOrigin.IriDenoted), origin)), super, work, local, origin);
                }
                case(OwlObjectHasSelf self):
                {
                    return StepSelfSub(self, super, work, local, origin);
                }
                case(OwlDataSomeValuesFrom or OwlDataAllValuesFrom or OwlDataHasValue or OwlDataCardinality):
                {
                    return StepDataSub(sub, super, work, local, origin);
                }
            }

            return EmitDl1(sub, super, work, local, origin);
        }

        /// <summary>Handles a universal superclass <c>A ⊑ ∀S.C</c>: the DL3-shape clause for a simple role, or a deferred chain elimination for a non-simple role, emitted once the automata are built.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="universal">The universal restriction.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepUniversalSuper(OwlClassExpression sub, OwlObjectAllValuesFrom universal, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            RoleRepresentative role = Rep(Symbols.RoleOf(universal.Property));
            int a = AtomicOrAbstract(sub, negative: true, work);
            int c = AtomicOrAbstract(universal.Filler, negative: false, work);

            if(NonSimpleRoles.Contains(role))
            {
                PendingEliminations.Add(new PendingElimination(a, role, c, origin));

                return null;
            }

            local.Add(DlClause.Create([DlLiteral.Concept(a, DlTerm.Central), RoleAtom(role, DlTerm.Central, Z1)], [DlLiteral.Concept(c, Z1)], origin));

            return null;
        }

        /// <summary>Handles a min-cardinality / existential superclass <c>B1 ⊑ ≥n S.B2</c> as DL2, guarding the KR 2006 simple-role restriction for a genuine number restriction (<c>n ≥ 2</c>); the <c>n = 1</c> existential carries no such restriction.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="property">The restricted property.</param>
        /// <param name="count">The cardinality bound.</param>
        /// <param name="filler">The filler.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepMinSuper(OwlClassExpression sub, OwlObjectPropertyExpression property, int count, OwlClassExpression filler, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            if(count <= 0)
            {
                return null;
            }

            RoleRepresentative role = Rep(Symbols.RoleOf(property));
            if(count >= 2 && NonSimpleRoles.Contains(role))
            {
                return ContextRemainderNames.NonSimpleRoleInNumberRestriction(property.Property.Iri);
            }

            int b1 = AtomicOrAbstract(sub, negative: true, work);
            int b2 = AtomicOrAbstract(filler, negative: false, work);
            EmitDl2(b1, count, role, b2, origin, local);

            return null;
        }

        /// <summary>Handles a max-cardinality superclass <c>B1 ⊑ ≤n S.B2</c> as DL4, guarding the simple-role restriction.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="max">The max-cardinality restriction.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepMaxSuper(OwlClassExpression sub, OwlObjectCardinality max, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            RawRoleId countedRole = Symbols.RoleOf(max.Property);
            RoleRepresentative role = Rep(countedRole);
            if(NonSimpleRoles.Contains(role))
            {
                return ContextRemainderNames.NonSimpleRoleInNumberRestriction(max.Property.Property.Iri);
            }

            int b1 = AtomicOrAbstract(sub, negative: true, work);
            int b2 = AtomicOrAbstract(max.Filler ?? Thing(), negative: true, work);
            EmitDl4(b1, max.Cardinality, role, countedRole, b2, origin, local);

            return null;
        }

        /// <summary>Handles an existential subclass <c>∃S.B1 ⊑ B2</c>: DL3 for a simple role, or the mirror conversion <c>B1 ⊑ ∀S⁻.B2</c> for a non-simple role.</summary>
        /// <param name="existential">The existential restriction.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepExistentialSub(OwlObjectSomeValuesFrom existential, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            RoleRepresentative role = Rep(Symbols.RoleOf(existential.Property));
            if(NonSimpleRoles.Contains(role))
            {
                work.Enqueue((existential.Filler, new OwlObjectAllValuesFrom(InverseExpression(existential.Property), super)));

                return null;
            }

            int b1 = AtomicOrAbstract(existential.Filler, negative: true, work);
            int b2 = AtomicOrAbstract(super, negative: false, work);
            local.Add(DlClause.Create([RoleAtom(role, Z1, DlTerm.Central), DlLiteral.Concept(b1, DlTerm.Central)], [DlLiteral.Concept(b2, Z1)], origin));

            return null;
        }

        /// <summary>Handles a universal subclass <c>∀r.C ⊑ D</c> by the faithful non-Horn rewrite <c>⊤ ⊑ ∃r.¬C ⊔ D</c> with fresh names.</summary>
        /// <param name="universal">The universal restriction.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepUniversalSub(OwlObjectAllValuesFrom universal, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work)
        {
            OwlClassReference x = FreshReference();
            OwlClassReference y = FreshReference();
            work.Enqueue((Thing(), new OwlObjectUnionOf([x, super])));
            work.Enqueue((x, new OwlObjectSomeValuesFrom(universal.Property, y)));
            work.Enqueue((new OwlObjectIntersectionOf([y, universal.Filler]), Nothing()));

            return null;
        }

        /// <summary>Handles a self superclass <c>A ⊑ ∃p.Self</c> (KR 2006 Definition 5 restricts self restrictions to simple roles): the loop producer <c>A(x) → Self_p(x)</c> over the base role's loop concept, guarding the simple-role restriction first.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="self">The self restriction.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepSelfSuper(OwlClassExpression sub, OwlObjectHasSelf self, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            RoleRepresentative role = Rep(Symbols.RoleOf(self.Property));
            if(NonSimpleRoles.Contains(role))
            {
                return ContextRemainderNames.NonSimpleRoleInSelfRestriction(self.Property.Property.Iri);
            }

            int carrier = AtomicOrAbstract(sub, negative: true, work);
            int loop = SelfAtom(RegisterLoopBase(role));
            local.Add(DlClause.Create([DlLiteral.Concept(carrier, DlTerm.Central)], [DlLiteral.Concept(loop, DlTerm.Central)], origin));

            return null;
        }

        /// <summary>Handles a self subclass <c>∃p.Self ⊑ B</c>: the loop consumer <c>Self_p(x) → B(x)</c> over the base role's loop concept, guarding the simple-role restriction first.</summary>
        /// <param name="self">The self restriction.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepSelfSub(OwlObjectHasSelf self, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            RoleRepresentative role = Rep(Symbols.RoleOf(self.Property));
            if(NonSimpleRoles.Contains(role))
            {
                return ContextRemainderNames.NonSimpleRoleInSelfRestriction(self.Property.Property.Iri);
            }

            int loop = SelfAtom(RegisterLoopBase(role));
            int consumer = AtomicOrAbstract(super, negative: false, work);
            local.Add(DlClause.Create([DlLiteral.Concept(loop, DlTerm.Central)], [DlLiteral.Concept(consumer, DlTerm.Central)], origin));

            return null;
        }

        /// <summary>
        /// Lowers a superclass-position data restriction (outside the base KR 2016
        /// grammar; admitted here as a datatype sidecar) to its context demand marker:
        /// a single-property existential, has-value, or positive min-cardinality
        /// emits a value-forcing demand marker beside the per-property
        /// <c>HasValueOf</c> marker (so a data-property domain fires through the
        /// hierarchy), a universal emits a universal marker only. The marker atom is
        /// memoized per descriptor and the descriptor rides the result for the
        /// saturation engine to reconstruct the obligation the shared datatype sidecar
        /// decides. A range-less max- or exact-cardinality of bound zero lowers
        /// through the per-property <c>HasValueOf</c> value-existence atom (the {0,1}
        /// negation shape). A max-cardinality of bound one or above emits a
        /// non-value-forcing maximum marker — a bound alone is satisfied vacuously by
        /// a node with no filler — and an exact cardinality of bound one or above
        /// emits both halves on one carrier, the value-forcing minimum marker and the
        /// maximum marker over the same count and range; an n-ary, reserved-property,
        /// or ranged bound-zero cardinality is a standing datatype-fragment
        /// rejection.
        /// </summary>
        /// <param name="sub">The subclass expression carrying the demand.</param>
        /// <param name="super">The superclass-position data restriction.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepDataSuper(OwlClassExpression sub, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            switch(super)
            {
                case(OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome):
                {
                    return EmitDataDemand(sub, dataSome.Properties[0].Iri, DataDemandKind.Existential, 0, dataSome.Range, valueForcing: true, local, origin, work);
                }
                case(OwlDataHasValue dataHas):
                {
                    return EmitDataDemand(sub, dataHas.Property.Iri, DataDemandKind.Existential, 0, new OwlDataOneOf([dataHas.Value]), valueForcing: true, local, origin, work);
                }
                case(OwlDataAllValuesFrom { Properties.Count: 1 } dataAll):
                {
                    return EmitDataDemand(sub, dataAll.Properties[0].Iri, DataDemandKind.Universal, 0, dataAll.Range, valueForcing: false, local, origin, work);
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Min } dataMin):
                {
                    return EmitDataDemand(sub, dataMin.Property.Iri, DataDemandKind.MinCardinality, dataMin.Cardinality, dataMin.Range ?? RdfsLiteralRange, valueForcing: dataMin.Cardinality >= 1, local, origin, work);
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Max, Cardinality: 0, Range: null } dataMax) when !IsReservedDataProperty(dataMax.Property.Iri):
                {
                    work.Enqueue((sub, new OwlObjectComplementOf(Symbols.AtomReference(HasValueOfAtom(dataMax.Property.Iri)))));

                    return null;
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Exact, Cardinality: 0, Range: null } dataExact) when !IsReservedDataProperty(dataExact.Property.Iri):
                {
                    work.Enqueue((sub, new OwlObjectComplementOf(Symbols.AtomReference(HasValueOfAtom(dataExact.Property.Iri)))));

                    return null;
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Max, Cardinality: >= 1 } dataMaxBound) when !IsReservedDataProperty(dataMaxBound.Property.Iri):
                {
                    return EmitDataDemand(sub, dataMaxBound.Property.Iri, DataDemandKind.MaxCardinality, dataMaxBound.Cardinality, dataMaxBound.Range ?? RdfsLiteralRange, valueForcing: false, local, origin, work);
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Exact, Cardinality: >= 1 } dataExactBound) when !IsReservedDataProperty(dataExactBound.Property.Iri):
                {
                    return EmitDataExactDemand(sub, dataExactBound.Property.Iri, dataExactBound.Cardinality, dataExactBound.Range ?? RdfsLiteralRange, local, origin, work);
                }
                default:
                {
                    return ContextRemainderNames.DataExpressionRejection(super.GetType().Name, "superclass");
                }
            }
        }

        /// <summary>Emits one data demand: the carrier-to-demand-marker clause and, for a value-forcing demand, the carrier-to-<c>HasValueOf</c> clause, registering the descriptor. A reserved data property is a named rejection.</summary>
        /// <param name="sub">The subclass expression carrying the demand.</param>
        /// <param name="property">The demanding data-property IRI.</param>
        /// <param name="kind">The demand kind.</param>
        /// <param name="count">The counting bound for a min- or max-cardinality demand, zero otherwise.</param>
        /// <param name="range">The demanded data range.</param>
        /// <param name="valueForcing">Whether the demand forces a value (an existential, has-value, or positive min-cardinality) and so emits <c>HasValueOf</c>; a maximum bound does not.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <param name="work">The origin's GCI worklist, extended when the subclass carrier abstracts.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? EmitDataDemand(OwlClassExpression sub, Utf8String property, DataDemandKind kind, int count, OwlDataRange range, bool valueForcing, List<DlClause> local, int origin, Queue<(OwlClassExpression, OwlClassExpression)> work)
        {
            if(IsReservedDataProperty(property))
            {
                return ContextRemainderNames.ReservedDataProperty(property);
            }

            int carrier = AtomicOrAbstract(sub, negative: true, work);
            int marker = Mint.MarkerFor(property, kind, count, range);
            local.Add(DlClause.Create([DlLiteral.Concept(carrier, DlTerm.Central)], [DlLiteral.Concept(marker, DlTerm.Central)], origin));

            if(valueForcing)
            {
                local.Add(DlClause.Create([DlLiteral.Concept(carrier, DlTerm.Central)], [DlLiteral.Concept(HasValueOfAtom(property), DlTerm.Central)], origin));
            }

            return null;
        }

        /// <summary>Emits a positive exact data cardinality as its two halves on ONE carrier: the value-forcing minimum-cardinality marker together with the carrier-to-<c>HasValueOf</c> clause, and the non-forcing maximum-cardinality marker, both over the same count and range so the sidecar's max slot meets the counting demand it pairs with. A reserved data property is a named rejection.</summary>
        /// <param name="sub">The subclass expression carrying the demand.</param>
        /// <param name="property">The demanding data-property IRI.</param>
        /// <param name="count">The exact bound both halves carry.</param>
        /// <param name="range">The demanded data range.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <param name="work">The origin's GCI worklist, extended when the subclass carrier abstracts.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? EmitDataExactDemand(OwlClassExpression sub, Utf8String property, int count, OwlDataRange range, List<DlClause> local, int origin, Queue<(OwlClassExpression, OwlClassExpression)> work)
        {
            if(IsReservedDataProperty(property))
            {
                return ContextRemainderNames.ReservedDataProperty(property);
            }

            int carrier = AtomicOrAbstract(sub, negative: true, work);
            int minimum = Mint.MarkerFor(property, DataDemandKind.MinCardinality, count, range);
            int maximum = Mint.MarkerFor(property, DataDemandKind.MaxCardinality, count, range);
            local.Add(DlClause.Create([DlLiteral.Concept(carrier, DlTerm.Central)], [DlLiteral.Concept(minimum, DlTerm.Central)], origin));
            local.Add(DlClause.Create([DlLiteral.Concept(carrier, DlTerm.Central)], [DlLiteral.Concept(maximum, DlTerm.Central)], origin));
            local.Add(DlClause.Create([DlLiteral.Concept(carrier, DlTerm.Central)], [DlLiteral.Concept(HasValueOfAtom(property), DlTerm.Central)], origin));

            return null;
        }

        /// <summary>
        /// Lowers a subclass-position data restriction to its NNF dual: a
        /// single-property data existential <c>∃d.DR ⊑ C</c> and a data has-value
        /// <c>∃d.{v} ⊑ C</c> emit the empty-body disjunctive clause
        /// <c>⊤ → Universal(¬DR)(x) ∨ C(x)</c> — a non-value-forcing universal demand
        /// marker over the complemented range beside the superclass disjuncts — whose
        /// marker the saturation engine's data rules refute or certify through the
        /// shared datatype sidecar. A subclass-position data universal (its dual is a
        /// value-forcing existential disjunct, and the <c>HasValueOf</c> companion
        /// cannot ride an uncommitted head disjunct) is a standing datatype-fragment
        /// rejection. A subclass-position range-less min-cardinality of bound one, and
        /// a subclass-position range-less max- or exact-cardinality of bound zero,
        /// lower through the per-property <c>HasValueOf</c> value-existence atom (the
        /// {0,1} value-existence shapes: a min of one is <c>HasValueOf</c>, a max or
        /// exact of zero is its negation, both landing as plain concept atoms, never a
        /// demand marker in an uncommitted head disjunct); a ranged or higher-bound
        /// data cardinality, and the n-ary shapes, remain standing datatype-fragment
        /// rejections.
        /// </summary>
        /// <param name="sub">The subclass-position data restriction.</param>
        /// <param name="super">The superclass expression, the dual's sibling disjuncts.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? StepDataSub(OwlClassExpression sub, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            switch(sub)
            {
                case(OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome):
                {
                    return EmitDataDualDisjunct(super, dataSome.Properties[0].Iri, new OwlDataComplementOf(dataSome.Range), local, origin, work);
                }
                case(OwlDataHasValue dataHas):
                {
                    return EmitDataDualDisjunct(super, dataHas.Property.Iri, new OwlDataComplementOf(new OwlDataOneOf([dataHas.Value])), local, origin, work);
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Min, Cardinality: 1, Range: null } dataMin) when !IsReservedDataProperty(dataMin.Property.Iri):
                {
                    work.Enqueue((Symbols.AtomReference(HasValueOfAtom(dataMin.Property.Iri)), super));

                    return null;
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Max, Cardinality: 0, Range: null } dataMax) when !IsReservedDataProperty(dataMax.Property.Iri):
                {
                    work.Enqueue((new OwlObjectComplementOf(Symbols.AtomReference(HasValueOfAtom(dataMax.Property.Iri))), super));

                    return null;
                }
                case(OwlDataCardinality { Kind: OwlCardinalityKind.Exact, Cardinality: 0, Range: null } dataExact) when !IsReservedDataProperty(dataExact.Property.Iri):
                {
                    work.Enqueue((new OwlObjectComplementOf(Symbols.AtomReference(HasValueOfAtom(dataExact.Property.Iri))), super));

                    return null;
                }
                default:
                {
                    return ContextRemainderNames.DataExpressionRejection(sub.GetType().Name, "subclass");
                }
            }
        }

        /// <summary>Emits one data dual disjunct: the empty-body clause whose head carries the non-value-forcing universal demand marker over the complemented range beside the superclass disjuncts, registering the descriptor. The marker literal is built directly into the head — the definitorial abstraction links a fresh name to its definition in one direction only, so an abstracted dual could never carry a marker refutation back to the fresh name — and the superclass disjuncts take the DL1 head treatment (union flattened, <c>owl:Nothing</c> dropped). A reserved data property is a named rejection.</summary>
        /// <param name="super">The superclass expression, the dual's sibling disjuncts.</param>
        /// <param name="property">The demanding data-property IRI.</param>
        /// <param name="complementedRange">The complemented data range the universal marker demands.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <param name="work">The origin's GCI worklist, extended when a superclass disjunct abstracts.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? EmitDataDualDisjunct(OwlClassExpression super, Utf8String property, OwlDataRange complementedRange, List<DlClause> local, int origin, Queue<(OwlClassExpression, OwlClassExpression)> work)
        {
            if(IsReservedDataProperty(property))
            {
                return ContextRemainderNames.ReservedDataProperty(property);
            }

            int marker = Mint.MarkerFor(property, DataDemandKind.Universal, 0, complementedRange);
            List<DlLiteral> head = [DlLiteral.Concept(marker, DlTerm.Central)];
            foreach(OwlClassExpression disjunct in FlattenUnion(super))
            {
                if(IsBottom(disjunct))
                {
                    continue;
                }

                head.Add(DlLiteral.Concept(AtomicOrAbstract(disjunct, negative: false, work), DlTerm.Central));
            }

            NegativePolarityDataMarkers++;
            local.Add(DlClause.Create([], head, origin));

            return null;
        }

        /// <summary>The number of negative-polarity data dual disjuncts emitted — one per subclass-position data existential or has-value lowered to its universal-marker NNF dual; the clausification result carries it so a widened admission is attributable.</summary>
        private int NegativePolarityDataMarkers { get; set; }

        /// <summary>The per-property <c>HasValueOf_d</c> marker atom, minted fresh on first demand and shared across every spelling of the property — the domain firing carrier.</summary>
        /// <param name="property">The data-property IRI.</param>
        /// <returns>The <c>HasValueOf</c> concept atom id.</returns>
        private int HasValueOfAtom(Utf8String property)
        {
            if(!HasValueOfConcepts.TryGetValue(property, out int atom))
            {
                atom = Symbols.FreshAtom();
                HasValueOfConcepts[property] = atom;
            }

            return atom;
        }

        /// <summary>Intakes a data-property domain axiom as the GCI <c>HasValueOf_d ⊑ C</c> — lowered by normalization to <c>HasValueOf_d(x) → C(x)</c> (join-free, §1.4) — so a node carrying any value-forcing d-demand is typed the domain class. A reserved data property is a named rejection.</summary>
        /// <param name="axiom">The data-property domain axiom.</param>
        /// <param name="origin">The axiom's index.</param>
        private void IntakeDataDomain(OwlDataPropertyDomainAxiom axiom, int origin)
        {
            if(IsReservedDataProperty(axiom.Property.Iri))
            {
                Remainder.Add(ContextRemainderNames.ReservedDataProperty(axiom.Property.Iri));

                return;
            }

            AddGci(Symbols.AtomReference(HasValueOfAtom(axiom.Property.Iri)), axiom.Domain, origin);
        }

        /// <summary>Records a direct data sub-property edge <c>sub ⊑ super</c> for the <c>HasValueOf</c> closure, dropping a reserved-property or reflexive edge.</summary>
        /// <param name="sub">The sub-property IRI.</param>
        /// <param name="super">The super-property IRI.</param>
        private void RecordDataEdge(Utf8String sub, Utf8String super)
        {
            if(!IsReservedDataProperty(sub) && !IsReservedDataProperty(super) && !sub.Equals(super))
            {
                DataSubEdges.Add((sub, super));
            }
        }

        /// <summary>Emits one <c>HasValueOf_d(x) → HasValueOf_e(x)</c> clause per direct data sub-property edge, so a value-forcing demand on a sub-property carries the domain up the hierarchy the saturation chains transitively.</summary>
        private void EmitHasValueOfClosure()
        {
            foreach((Utf8String sub, Utf8String super) in DataSubEdges)
            {
                Clauses.Add(DlClause.Create([DlLiteral.Concept(HasValueOfAtom(sub), DlTerm.Central)], [DlLiteral.Concept(HasValueOfAtom(super), DlTerm.Central)], -1));
            }
        }

        /// <summary>Whether a data-property IRI is the reserved <c>owl:topDataProperty</c> or <c>owl:bottomDataProperty</c>, whose fixed universal/empty extension the context path does not interpret.</summary>
        /// <param name="property">The data-property IRI.</param>
        /// <returns><see langword="true"/> for a reserved data property.</returns>
        private static bool IsReservedDataProperty(Utf8String property)
        {
            return property.Equals(OwlVocabulary.TopDataProperty) || property.Equals(OwlVocabulary.BottomDataProperty);
        }

        /// <summary>The <c>rdfs:Literal</c> data range — the whole data domain — used as the default range of an unqualified data min-cardinality, matching the tableau arms' <c>Translate</c> for byte-identical sidecar verdicts.</summary>
        private static OwlDataRange RdfsLiteralRange { get; } = new OwlDatatypeReference(new NamedNode(Lumoin.Veritas.Rdf.RdfVocabulary.Rdfs.LiteralClass));

        /// <summary>Emits a DL1 clause from an atomic-conjunction subclass and an atomic-disjunction superclass, abstracting any non-atomic operand with polarity-correct fresh names.</summary>
        /// <param name="sub">The subclass expression.</param>
        /// <param name="super">The superclass expression.</param>
        /// <param name="work">The origin's GCI worklist.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <returns>A named rejection, or <see langword="null"/> on progress.</returns>
        private string? EmitDl1(OwlClassExpression sub, OwlClassExpression super, Queue<(OwlClassExpression, OwlClassExpression)> work, List<DlClause> local, int origin)
        {
            List<DlLiteral> body = [];
            foreach(OwlClassExpression conjunct in FlattenIntersection(sub))
            {
                if(IsTop(conjunct))
                {
                    continue;
                }

                body.Add(DlLiteral.Concept(AtomicOrAbstract(conjunct, negative: true, work), DlTerm.Central));
            }

            List<DlLiteral> head = [];
            bool sawBottom = false;
            foreach(OwlClassExpression disjunct in FlattenUnion(super))
            {
                if(IsBottom(disjunct))
                {
                    sawBottom = true;

                    continue;
                }

                head.Add(DlLiteral.Concept(AtomicOrAbstract(disjunct, negative: false, work), DlTerm.Central));
            }

            if(sawBottom && head.Count == 0)
            {
                head.Clear();
            }

            local.Add(DlClause.Create(body, head, origin));

            return null;
        }

        /// <summary>Emits DL2 for <c>B1 ⊑ ≥n S.B2</c>: the successor edge and filler per witness, and the pairwise inequalities. Under <see cref="EqualityLowering.SuccessorSharing"/> a single (existential / min-1) witness over a functional directioned role reuses the role's shared successor symbol instead of minting a per-occurrence one, so same-owner functional successors merge by construction; a min-n≥2 witness set and a witness over a non-functional or qualified role always mint distinct symbols.</summary>
        /// <param name="b1">The subclass atom.</param>
        /// <param name="count">The witness count.</param>
        /// <param name="role">The representative directioned role.</param>
        /// <param name="b2">The filler atom.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        private void EmitDl2(int b1, int count, RoleRepresentative role, int b2, int origin, List<DlClause> local)
        {
            bool share = Lowering == EqualityLowering.SuccessorSharing && count == 1 && FunctionalDirectionedRoles.Contains(role);
            List<DlTerm> witnesses = [];
            for(int i = 0; i < count; i++)
            {
                DlTerm witness = DlTerm.Function(share ? SharedSuccessorSymbol(role, b2) : Symbols.MintFunctionSymbol(b2));
                witnesses.Add(witness);
                local.Add(DlClause.Create([DlLiteral.Concept(b1, DlTerm.Central)], [RoleAtom(role, DlTerm.Central, witness)], origin));
                local.Add(DlClause.Create([DlLiteral.Concept(b1, DlTerm.Central)], [DlLiteral.Concept(b2, witness)], origin));
            }

            for(int i = 0; i < witnesses.Count; i++)
            {
                for(int j = i + 1; j < witnesses.Count; j++)
                {
                    local.Add(DlClause.Create([DlLiteral.Concept(b1, DlTerm.Central)], [DlLiteral.Inequality(witnesses[i], witnesses[j])], origin));
                }
            }
        }

        /// <summary>The successor-sharing symbol for a functional directioned representative role: minted once (recording its first filler for rendering) and reused across every existential / min-1 over that role, so the successors the module-level <c>≤1</c> forces to coincide are one term by construction.</summary>
        /// <param name="role">The functional directioned representative role.</param>
        /// <param name="fillerAtom">The filler atom of the occurrence minting the symbol, recorded for rendering only.</param>
        /// <returns>The role's shared successor function symbol id.</returns>
        private int SharedSuccessorSymbol(RoleRepresentative role, int fillerAtom)
        {
            if(SharedSuccessorSymbols.TryGetValue(role, out int existing))
            {
                return existing;
            }

            int minted = Symbols.MintFunctionSymbol(fillerAtom);
            SharedSuccessorSymbols[role] = minted;

            return minted;
        }

        /// <summary>Emits DL4 for <c>B1 ⊑ ≤n S.B2</c>: a counting-role auxiliary minted fresh per call — identical <c>(role, filler)</c> pairs across axioms get distinct auxiliaries — and the (n+1)-way equality clause. Records the counted role's forward base so the loops×counting guard can test it against the closed loop set.</summary>
        /// <param name="b1">The subclass atom.</param>
        /// <param name="count">The upper bound.</param>
        /// <param name="role">The directioned role.</param>
        /// <param name="countedRole">The counted role's raw directioned id — the closed graph's query key the told constraint records.</param>
        /// <param name="b2">The filler atom.</param>
        /// <param name="origin">The origin axiom's index.</param>
        /// <param name="local">The origin's local clause buffer.</param>
        private void EmitDl4(int b1, int count, RoleRepresentative role, RawRoleId countedRole, int b2, int origin, List<DlClause> local)
        {
            //A DL4 whose subclass atom is a ground marker is a TOLD counting
            //constraint on that representative — the rider's pigeonhole search reads
            //these over the closed graph. Recording is rider-gated so the off path
            //stays allocation-identical.
            if(RiderEnabled && MarkerRepresentatives.TryGetValue(b1, out Utf8String subject))
            {
                CountingConstraints.Add(new GroundCountingConstraint(subject, countedRole, count, b2, b2 == Symbols.AtomOf(OwlVocabulary.Thing)));
            }

            CountingTargets.Add(PrimaryOf(role));
            RoleRepresentative counting = Symbols.FreshCountingRole();
            local.Add(DlClause.Create([RoleAtom(role, Z1, DlTerm.Central), DlLiteral.Concept(b2, DlTerm.Central)], [DlLiteral.Role(counting.Value, Z1, DlTerm.Central)], origin));

            List<DlLiteral> body = [DlLiteral.Concept(b1, DlTerm.Central)];
            List<DlTerm> vars = [];
            for(int i = 0; i <= count; i++)
            {
                DlTerm var = DlTerm.Neighbour(i + 1);
                vars.Add(var);
                body.Add(DlLiteral.Role(counting.Value, DlTerm.Central, var));
            }

            List<DlLiteral> head = [];
            for(int i = 0; i < vars.Count; i++)
            {
                for(int j = i + 1; j < vars.Count; j++)
                {
                    head.Add(DlLiteral.Equality(vars[i], vars[j]));
                }
            }

            local.Add(DlClause.Create(body, head, origin));
        }

        /// <summary>Emits the role-inclusion clauses (KR 2016 Table 1 DL5/DL6): each kept length-1 inclusion as <c>S1(z1, x) → S2(z1, x)</c> over the representative roles, with an inverse-direction representative flipping the stored argument order. A within-class inclusion between same-direction members is a tautology over one symbol and is dropped; a SELF-INVERSE class (a role coupled with its own inverse — the <c>r⁻ ⊑ r</c> symmetry lowering) additionally emits one symmetry clause <c>r(z1, x) → r(x, z1)</c> per representative, because the single-predicate encoding otherwise loses the inverse direction entirely for SIMPLE symmetric roles, where no automaton fold compensates; duplicates collapse.</summary>
        private void EmitRoleInclusions()
        {
            HashSet<(RoleRepresentative Sub, RoleRepresentative Super)> emitted = [];
            HashSet<RoleRepresentative> symmetryEmitted = [];
            foreach((RawRoleId sub, RawRoleId super) in RoleInclusions)
            {
                RoleRepresentative repSub = Rep(sub);
                RoleRepresentative repSuper = Rep(super);
                if(repSub == repSuper)
                {
                    if(IsSymmetricClass(repSuper) && symmetryEmitted.Add(repSuper))
                    {
                        Clauses.Add(DlClause.Create([RoleAtom(repSuper, Z1, DlTerm.Central)], [RoleAtom(repSuper, DlTerm.Central, Z1)], -1));
                    }

                    continue;
                }

                if(emitted.Add((repSub, repSuper)))
                {
                    Clauses.Add(DlClause.Create([RoleAtom(repSub, Z1, DlTerm.Central)], [RoleAtom(repSuper, Z1, DlTerm.Central)], -1));
                }
            }
        }

        /// <summary>Seeds the loop set L with the base role of every explicitly reflexive and irreflexive role; the self-restriction roles are registered on demand as they lower. A loop is direction-blind (<c>p(a, a) ⟺ p⁻(a, a)</c>), so the forward base key folds both spellings onto one member. Runs after RBox processing, before the upward closure.</summary>
        private void SeedLoopSet()
        {
            foreach(RawRoleId role in ReflexiveRoles)
            {
                LoopRoles.Add(PrimaryOf(Rep(role)));
            }

            foreach(RawRoleId role in IrreflexiveRoles)
            {
                LoopRoles.Add(PrimaryOf(Rep(role)));
            }
        }

        /// <summary>Registers a role's forward base into the loop set L — folding its inverse spelling onto the same key — and returns the key, for a self restriction being lowered.</summary>
        /// <param name="role">The directioned role of the self restriction (a representative id).</param>
        /// <returns>The forward base loop key.</returns>
        private RoleRepresentative RegisterLoopBase(RoleRepresentative role)
        {
            RoleRepresentative loopBase = PrimaryOf(role);
            LoopRoles.Add(loopBase);

            return loopBase;
        }

        /// <summary>The loop concept <c>Self_p</c> of a base role, minted fresh on first demand and shared across every spelling of the role; minting at clausification preserves the frozen signature saturation runs against.</summary>
        /// <param name="loopBase">The forward base loop key.</param>
        /// <returns>The loop concept atom id.</returns>
        private int SelfAtom(RoleRepresentative loopBase)
        {
            if(!LoopConcepts.TryGetValue(loopBase, out int atom))
            {
                atom = Symbols.FreshAtom();
                LoopConcepts[loopBase] = atom;
            }

            return atom;
        }

        /// <summary>Closes the loop set L upward over the representative role arcs (<c>q ∈ L and q ⊑ q' ⇒ q' ∈ L</c>), forward-base-keyed so the closure is direction-blind. Runs after the reflexivity emission and before the self-variant pass, which reads the closed set.</summary>
        private void CloseLoopSet()
        {
            bool grew = true;
            while(grew)
            {
                grew = false;
                foreach((RoleRepresentative sub, RoleRepresentative super) in RepArcs)
                {
                    if(LoopRoles.Contains(PrimaryOf(sub)) && LoopRoles.Add(PrimaryOf(super)))
                    {
                        grew = true;
                    }
                }
            }
        }

        /// <summary>Names the loops×counting remainder for every counted role whose forward base is in the closed loop set L: a functional merge over a loop-capable role forces the owner-successor diagonal the context-literal equality grammar cannot express, so the whole module delegates. Runs after the upward loop-set closure and before the self-variant pass; the guard over-approximates, delegating roles that merely test or forbid a loop as well as those that produce one.</summary>
        private void CheckCountingLoopCapability()
        {
            foreach(RoleRepresentative target in CountingTargets)
            {
                if(LoopRoles.Contains(target) && Symbols.RoleIri(target) is Utf8String iri)
                {
                    Remainder.Add(ContextRemainderNames.LoopCapableRoleInNumberRestriction(iri));
                }
            }
        }

        /// <summary>Emits the reflexivity and irreflexivity loop clauses: <c>⊤ → Self_p(x)</c> per reflexive role (KR 2006 admits reflexivity on arbitrary roles — no simplicity guard), and <c>Self_p(x) → ⊥</c> per irreflexive role guarding the KR 2006 simple-role restriction (a non-simple irreflexive role is a named remainder, never emitted).</summary>
        private void EmitReflexivity()
        {
            foreach(RawRoleId role in ReflexiveRoles)
            {
                int loop = SelfAtom(PrimaryOf(Rep(role)));
                Clauses.Add(DlClause.Create([], [DlLiteral.Concept(loop, DlTerm.Central)], -1));
            }

            foreach(RawRoleId role in IrreflexiveRoles)
            {
                RoleRepresentative representative = Rep(role);
                if(NonSimpleRoles.Contains(representative))
                {
                    if(Symbols.RoleIri(role) is Utf8String iri)
                    {
                        Remainder.Add(ContextRemainderNames.NonSimpleRoleInIrreflexivity(iri));
                    }

                    continue;
                }

                int loop = SelfAtom(PrimaryOf(representative));
                Clauses.Add(DlClause.Create([DlLiteral.Concept(loop, DlTerm.Central)], [], -1));
            }
        }

        /// <summary>
        /// Emits the pairwise disjoint-role clash clauses (KR 2006 Definition 3
        /// <c>Dis(R,S)</c> semantics; the KAZ 2008 <c>Asy(S) ⟺ Dis(S, Inv(S))</c>
        /// reduction shares the mechanism): per recorded pair, one clash clause
        /// <c>ra(z1, x) ∧ rb(z1, x) → ⊥</c> over the representative roles, so a pair
        /// shared by both roles is empty. Reserved <c>owl:topObjectProperty</c>
        /// operands (whose universal extension the context path does not interpret)
        /// are the module-level reserved-role scan's responsibility, so every pair
        /// reaching this emission has non-reserved operands or a carved-out
        /// <c>owl:bottomObjectProperty</c> operand whose empty extension makes the
        /// clash clause a sound tautology. The simplicity guard tests BOTH
        /// representatives: a non-simple operand is a named remainder (KR 2006 admits
        /// <c>Asy</c>/<c>Dis</c> only on simple roles) and delegates at the second
        /// gate. A same-representative pair (mutually included roles, or an asymmetric
        /// self-inverse role) collapses under <see cref="DlClause.Create"/>'s
        /// de-duplication to the single-literal emptiness clause <c>r(z1, x) → ⊥</c>.
        /// </summary>
        private void EmitRoleDisjointness()
        {
            foreach(DisjointRolePair pair in DisjointRolePairs)
            {
                RoleRepresentative ra = Rep(pair.First);
                RoleRepresentative rb = Rep(pair.Second);
                if(NonSimpleRoles.Contains(ra) || NonSimpleRoles.Contains(rb))
                {
                    AddNonSimpleDisjointRemainder(pair, ra, rb);
                    continue;
                }

                Clauses.Add(DlClause.Create([RoleAtom(ra, Z1, DlTerm.Central), RoleAtom(rb, Z1, DlTerm.Central)], [], pair.Origin));
            }
        }

        /// <summary>Adds the non-simple-role remainder for a pair whose representative operand is non-simple, provenance-split by <see cref="DisjointRolePair.FromAsymmetric"/>: the asymmetric property's IRI, or the non-simple operand's IRI.</summary>
        /// <param name="pair">The recorded pair.</param>
        /// <param name="ra">The first operand's representative.</param>
        /// <param name="rb">The second operand's representative.</param>
        private void AddNonSimpleDisjointRemainder(DisjointRolePair pair, RoleRepresentative ra, RoleRepresentative rb)
        {
            if(pair.FromAsymmetric)
            {
                if(Symbols.RoleIri(pair.First) is Utf8String asymmetric)
                {
                    Remainder.Add(ContextRemainderNames.NonSimpleRoleInAsymmetry(asymmetric));
                }

                return;
            }

            RawRoleId operand = NonSimpleRoles.Contains(ra) ? pair.First : pair.Second;
            if(Symbols.RoleIri(operand) is Utf8String iri)
            {
                Remainder.Add(ContextRemainderNames.NonSimpleRoleInRoleDisjointness(iri));
            }
        }

        /// <summary>
        /// The self-variant pass (KR 2016 completeness half; the last emission step,
        /// so automaton transitions are covered): over every emitted clause and every
        /// non-empty subset of its neighbour variables whose role literals all range
        /// over a loop-set base role, emits the <c>z ↦ x</c> instance in which each
        /// self-collapsed role atom <c>p(x, x)</c> becomes <c>Self_p(x)</c> — a loop
        /// satisfies a universal at its own element, is its own existential witness,
        /// propagates up the role hierarchy, and walks a chain letter in place.
        /// Tautologies (a head literal already in the body, e.g. the symmetry clause's
        /// <c>Self_p(x) → Self_p(x)</c>) are dropped and duplicate variants collapse.
        /// The subset enumeration covers all combinations in one pass, so variants of
        /// variants are unnecessary; in the admitted slice each clause carries at most
        /// one role literal, so the subsets are singletons in practice.
        /// </summary>
        private void EmitSelfVariants()
        {
            if(LoopRoles.Count == 0)
            {
                return;
            }

            List<DlClause> originals = new(Clauses);
            HashSet<DlClause> seen = [.. Clauses];
            foreach(DlClause clause in originals)
            {
                EmitClauseVariants(clause, seen);
            }
        }

        /// <summary>Emits the self-variants of one clause over every admissible non-empty subset of its neighbour variables, deduplicating against the emitted set.</summary>
        /// <param name="clause">The clause to vary.</param>
        /// <param name="seen">The emitted-clause set variants are deduplicated against.</param>
        private void EmitClauseVariants(DlClause clause, HashSet<DlClause> seen)
        {
            List<DlTerm> neighbours = DistinctNeighbours(clause);
            if(neighbours.Count == 0)
            {
                return;
            }

            int subsets = 1 << neighbours.Count;
            for(int mask = 1; mask < subsets; mask++)
            {
                HashSet<DlTerm> selected = [];
                for(int i = 0; i < neighbours.Count; i++)
                {
                    if((mask & (1 << i)) != 0)
                    {
                        selected.Add(neighbours[i]);
                    }
                }

                if(!SubsetRangesOverLoopSet(clause, selected))
                {
                    continue;
                }

                DlClause? variant = BuildSelfVariant(clause, selected);
                if(variant is not null && seen.Add(variant))
                {
                    Clauses.Add(variant);
                }
            }
        }

        /// <summary>The distinct neighbour variables occurring across a clause's body and head.</summary>
        /// <param name="clause">The clause.</param>
        /// <returns>The distinct neighbour terms.</returns>
        private static List<DlTerm> DistinctNeighbours(DlClause clause)
        {
            List<DlTerm> neighbours = [];
            CollectNeighbours(clause.Body, neighbours);
            CollectNeighbours(clause.Head, neighbours);

            return neighbours;
        }

        /// <summary>Collects the distinct neighbour variables of a literal span.</summary>
        /// <param name="literals">The literals to scan.</param>
        /// <param name="neighboursToAppendTo">The distinct-neighbour accumulator appended to.</param>
        private static void CollectNeighbours(ReadOnlySpan<DlLiteral> literals, List<DlTerm> neighboursToAppendTo)
        {
            foreach(DlLiteral literal in literals)
            {
                AddNeighbour(literal.First, neighboursToAppendTo);
                if(literal.Kind != DlLiteralKind.Concept)
                {
                    AddNeighbour(literal.Second, neighboursToAppendTo);
                }
            }
        }

        /// <summary>Appends a term to the accumulator when it is a neighbour variable not already present.</summary>
        /// <param name="term">The term to test.</param>
        /// <param name="neighboursToAppendTo">The distinct-neighbour accumulator appended to.</param>
        private static void AddNeighbour(DlTerm term, List<DlTerm> neighboursToAppendTo)
        {
            if(term.Kind == DlTermKind.Neighbour && !neighboursToAppendTo.Contains(term))
            {
                neighboursToAppendTo.Add(term);
            }
        }

        /// <summary>Whether every role literal mentioning a selected neighbour ranges over a loop-set base role — the admissibility condition for a self-variant over the subset.</summary>
        /// <param name="clause">The clause.</param>
        /// <param name="selected">The selected neighbour variables.</param>
        /// <returns><see langword="true"/> when the subset admits a self-variant.</returns>
        private bool SubsetRangesOverLoopSet(DlClause clause, HashSet<DlTerm> selected)
        {
            return RoleLiteralsRangeOverLoopSet(clause.Body, selected) && RoleLiteralsRangeOverLoopSet(clause.Head, selected);
        }

        /// <summary>Whether every role literal of a span mentioning a selected neighbour ranges over a loop-set base role.</summary>
        /// <param name="literals">The literals to test.</param>
        /// <param name="selected">The selected neighbour variables.</param>
        /// <returns><see langword="true"/> when the span imposes no loop-set violation.</returns>
        private bool RoleLiteralsRangeOverLoopSet(ReadOnlySpan<DlLiteral> literals, HashSet<DlTerm> selected)
        {
            foreach(DlLiteral literal in literals)
            {
                if(literal.Kind != DlLiteralKind.Role)
                {
                    continue;
                }

                if((selected.Contains(literal.First) || selected.Contains(literal.Second)) && !LoopRoles.Contains(RoleRepresentative.FromClauseSymbol(ContextSymbolTable.Forward(literal.Symbol))))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Builds the <c>z ↦ x</c> self-variant of a clause over a selected subset, rewriting each self-collapsed role atom to its loop concept; returns <see langword="null"/> when the variant is a tautology (a head literal is already a body literal).</summary>
        /// <param name="clause">The clause to vary.</param>
        /// <param name="selected">The selected neighbour variables mapped to the central variable.</param>
        /// <returns>The self-variant clause, or <see langword="null"/> when it is a tautology.</returns>
        private DlClause? BuildSelfVariant(DlClause clause, HashSet<DlTerm> selected)
        {
            List<DlLiteral> body = [];
            foreach(DlLiteral literal in clause.Body)
            {
                body.Add(SubstituteLiteral(literal, selected));
            }

            List<DlLiteral> head = [];
            foreach(DlLiteral literal in clause.Head)
            {
                head.Add(SubstituteLiteral(literal, selected));
            }

            HashSet<DlLiteral> bodyLiterals = [.. body];
            foreach(DlLiteral literal in head)
            {
                if(bodyLiterals.Contains(literal))
                {
                    return null;
                }
            }

            return DlClause.Create(body, head, clause.Origin);
        }

        /// <summary>Applies the <c>z ↦ x</c> substitution to a literal, collapsing a role atom whose both arguments become the central variable to the role's loop concept.</summary>
        /// <param name="literal">The literal to rewrite.</param>
        /// <param name="selected">The selected neighbour variables mapped to the central variable.</param>
        /// <returns>The rewritten literal.</returns>
        private DlLiteral SubstituteLiteral(DlLiteral literal, HashSet<DlTerm> selected)
        {
            return literal.Kind switch
            {
                DlLiteralKind.Concept => DlLiteral.Concept(literal.Symbol, Substitute(literal.First, selected)),
                DlLiteralKind.Role => SubstituteRole(literal, selected),
                DlLiteralKind.Equality => DlLiteral.Equality(Substitute(literal.First, selected), Substitute(literal.Second, selected)),
                _ => DlLiteral.Inequality(Substitute(literal.First, selected), Substitute(literal.Second, selected)),
            };
        }

        /// <summary>Applies the substitution to a role atom, collapsing <c>p(x, x)</c> to <c>Self_p(x)</c> when both arguments become the central variable.</summary>
        /// <param name="literal">The role literal.</param>
        /// <param name="selected">The selected neighbour variables mapped to the central variable.</param>
        /// <returns>The rewritten literal — a loop concept atom or the substituted role atom.</returns>
        private DlLiteral SubstituteRole(DlLiteral literal, HashSet<DlTerm> selected)
        {
            DlTerm first = Substitute(literal.First, selected);
            DlTerm second = Substitute(literal.Second, selected);
            if(first.IsCentral && second.IsCentral)
            {
                return DlLiteral.Concept(SelfAtom(RoleRepresentative.FromClauseSymbol(ContextSymbolTable.Forward(literal.Symbol))), DlTerm.Central);
            }

            return DlLiteral.Role(literal.Symbol, first, second);
        }

        /// <summary>Substitutes a neighbour variable in the selected subset with the central variable, leaving every other term unchanged.</summary>
        /// <param name="term">The term to substitute.</param>
        /// <param name="selected">The selected neighbour variables mapped to the central variable.</param>
        /// <returns>The substituted term.</returns>
        private static DlTerm Substitute(DlTerm term, HashSet<DlTerm> selected)
        {
            return term.Kind == DlTermKind.Neighbour && selected.Contains(term) ? DlTerm.Central : term;
        }

        /// <summary>
        /// Emits the chain-elimination clauses (KAZ2008 axioms (72) to (74)) for
        /// every deferred non-simple universal <c>A ⊑ ∀v.B</c>:
        /// a fresh concept per automaton state, the initial-state seeding, one
        /// propagation clause per transition, and the final-state discharge. The
        /// length-at-least-2 inclusions are never emitted as clauses — their
        /// consequences live entirely in the automata.
        /// </summary>
        private void EmitEliminations()
        {
            foreach(PendingElimination pending in PendingEliminations)
            {
                RoleAutomaton automaton = LookupAutomaton(pending.Role);
                if(BudgetExceeded)
                {
                    return;
                }

                Dictionary<int, int> stateAtom = [];
                foreach(int state in automaton.States)
                {
                    stateAtom[state] = Symbols.FreshAtom();
                }

                Clauses.Add(DlClause.Create([DlLiteral.Concept(pending.Carrier, DlTerm.Central)], [DlLiteral.Concept(stateAtom[automaton.Initial], DlTerm.Central)], pending.Origin));

                foreach((int from, RoleRepresentative letter, int to) in automaton.Transitions)
                {
                    Clauses.Add(DlClause.Create(
                        [DlLiteral.Concept(stateAtom[from], DlTerm.Central), RoleAtom(letter, DlTerm.Central, Z1)],
                        [DlLiteral.Concept(stateAtom[to], Z1)],
                        pending.Origin));
                }

                foreach(int state in automaton.Finals)
                {
                    Clauses.Add(DlClause.Create([DlLiteral.Concept(stateAtom[state], DlTerm.Central)], [DlLiteral.Concept(pending.Filler, DlTerm.Central)], pending.Origin));
                }
            }
        }

        /// <summary>
        /// Builds the role automata the deferred eliminations need, iteratively:
        /// an explicit worklist collects the primary representative of every needed
        /// role and of every non-simple letter reachable through the
        /// representative-level RIA words; a Kahn topological sort orders them by
        /// the letter-dependency graph; and the primaries are built bottom-up into
        /// the memo — no self- or mutual recursion is possible. The dependency
        /// graph is a strict DAG because its edges are a base-level subset of the
        /// regularity order's edges, which the regularity check verified acyclic.
        /// </summary>
        /// <exception cref="UnreachableException">The dependency graph has a cycle — impossible past the regularity check.</exception>
        private void BuildRequiredAutomata()
        {
            if(PendingEliminations.Count == 0)
            {
                return;
            }

            HashSet<RoleRepresentative> needed = [];
            Stack<RoleRepresentative> discovery = new();
            foreach(PendingElimination pending in PendingEliminations)
            {
                discovery.Push(PrimaryOf(pending.Role));
            }

            while(discovery.Count > 0)
            {
                RoleRepresentative primary = discovery.Pop();
                if(!needed.Add(primary))
                {
                    continue;
                }

                foreach(RoleRepresentative letter in AutomatonLetters(primary))
                {
                    if(NonSimpleRoles.Contains(letter))
                    {
                        RoleRepresentative letterPrimary = PrimaryOf(letter);
                        if(letterPrimary != primary)
                        {
                            discovery.Push(letterPrimary);
                        }
                    }
                }
            }

            Dictionary<RoleRepresentative, int> indegree = [];
            Dictionary<RoleRepresentative, List<RoleRepresentative>> dependents = [];
            foreach(RoleRepresentative primary in needed)
            {
                indegree[primary] = 0;
            }

            foreach(RoleRepresentative primary in needed)
            {
                foreach(RoleRepresentative letter in AutomatonLetters(primary))
                {
                    if(!NonSimpleRoles.Contains(letter))
                    {
                        continue;
                    }

                    RoleRepresentative letterPrimary = PrimaryOf(letter);
                    if(letterPrimary == primary || !needed.Contains(letterPrimary))
                    {
                        continue;
                    }

                    if(!dependents.TryGetValue(letterPrimary, out List<RoleRepresentative>? list))
                    {
                        list = [];
                        dependents[letterPrimary] = list;
                    }

                    list.Add(primary);
                    indegree[primary]++;
                }
            }

            Queue<RoleRepresentative> ready = [];
            foreach(KeyValuePair<RoleRepresentative, int> entry in indegree)
            {
                if(entry.Value == 0)
                {
                    ready.Enqueue(entry.Key);
                }
            }

            int built = 0;
            while(ready.Count > 0 && !BudgetExceeded)
            {
                RoleRepresentative primary = ready.Dequeue();
                Automata[primary] = BuildPrimaryAutomaton(primary);
                built++;
                if(dependents.TryGetValue(primary, out List<RoleRepresentative>? next))
                {
                    foreach(RoleRepresentative dependent in next)
                    {
                        indegree[dependent]--;
                        if(indegree[dependent] == 0)
                        {
                            ready.Enqueue(dependent);
                        }
                    }
                }
            }

            if(!BudgetExceeded && built != needed.Count)
            {
                throw new UnreachableException("The automaton letter-dependency graph has a cycle the regularity check should have rejected.");
            }
        }

        /// <summary>
        /// The letters a primary representative's automaton construction consumes
        /// before inlining: the single-arc sub-roles and the non-endpoint chain
        /// letters of its representative-level RIAs. The symmetric fold's mirrored
        /// letters share these letters' primaries, so they add no dependencies.
        /// </summary>
        /// <param name="primary">The primary representative role.</param>
        /// <returns>The letters, possibly with duplicates.</returns>
        private List<RoleRepresentative> AutomatonLetters(RoleRepresentative primary)
        {
            List<RoleRepresentative> letters = [];
            foreach((RoleRepresentative sub, RoleRepresentative super) in RepArcs)
            {
                if(super == primary && sub != primary && sub.Value != ContextSymbolTable.Inverse(primary.Value))
                {
                    letters.Add(sub);
                }
            }

            foreach((List<RoleRepresentative> word, RoleRepresentative super) in RepChains)
            {
                if(super != primary)
                {
                    continue;
                }

                bool firstExact = word[0] == primary;
                bool lastExact = word[^1] == primary;
                for(int i = 0; i < word.Count; i++)
                {
                    if((i == 0 && firstExact) || (i == word.Count - 1 && lastExact))
                    {
                        continue;
                    }

                    letters.Add(word[i]);
                }
            }

            return letters;
        }

        /// <summary>
        /// Builds the epsilon-free automaton of a primary representative (HS2004
        /// Definition 10): STEP 1 arcs from the representative-level RIAs, STEP 2's
        /// symmetric mirror fold when the representative's class contains its own
        /// inverse, STEP 3 sub-letter inlining from the completed memo, then
        /// epsilon elimination.
        /// </summary>
        /// <param name="primary">The primary representative role.</param>
        /// <returns>The completed epsilon-free automaton.</returns>
        private RoleAutomaton BuildPrimaryAutomaton(RoleRepresentative primary)
        {
            RoleAutomaton automaton = new(AllocateState(), AllocateState());
            automaton.AddTransition(automaton.Initial, primary, automaton.SingleFinal);

            foreach((RoleRepresentative sub, RoleRepresentative super) in RepArcs)
            {
                if(super == primary && sub != primary && sub.Value != ContextSymbolTable.Inverse(primary.Value))
                {
                    automaton.AddTransition(automaton.Initial, sub, automaton.SingleFinal);
                }
            }

            foreach((List<RoleRepresentative> word, RoleRepresentative super) in RepChains)
            {
                if(super == primary)
                {
                    AddChainToAutomaton(automaton, word, primary);
                }
            }

            if(IsSymmetricClass(primary))
            {
                FoldSymmetricMirror(automaton);
            }

            InlineSubLetters(automaton, primary);

            return automaton.EpsilonEliminated();
        }

        /// <summary>
        /// HS2004 Definition 10 STEP 2 for a symmetric representative: folds in a
        /// mirrored copy of the STEP-1 automaton — every arc reversed under the
        /// representative-rewritten inverse letter, epsilon arcs reversed — and
        /// links the halves with four epsilon transitions pairing each original
        /// endpoint with the mirror image of the OTHER endpoint (initial with
        /// mirrored-final, final with mirrored-initial, both directions). That
        /// pairing closes the language under inversion while keeping the initial
        /// and final states epsilon-disconnected, so the automaton never accepts
        /// the empty word.
        /// </summary>
        /// <param name="automaton">The STEP-1 automaton to fold.</param>
        private void FoldSymmetricMirror(RoleAutomaton automaton)
        {
            int[] states = [.. automaton.States];
            (int From, RoleRepresentative Letter, int To)[] transitions = [.. automaton.Transitions];

            Dictionary<int, int> copy = [];
            foreach(int state in states)
            {
                copy[state] = AllocateState();
                automaton.States.Add(copy[state]);
            }

            foreach((int from, RoleRepresentative letter, int to) in transitions)
            {
                RoleRepresentative mirroredLetter = letter == RoleAutomaton.Epsilon ? RoleAutomaton.Epsilon : MirrorLetter(letter);
                automaton.AddTransition(copy[to], mirroredLetter, copy[from]);
            }

            automaton.AddEpsilon(automaton.Initial, copy[automaton.SingleFinal]);
            automaton.AddEpsilon(copy[automaton.SingleFinal], automaton.Initial);
            automaton.AddEpsilon(copy[automaton.Initial], automaton.SingleFinal);
            automaton.AddEpsilon(automaton.SingleFinal, copy[automaton.Initial]);
        }

        /// <summary>
        /// The completed automaton of a representative role: the memoised primary,
        /// or — for an inverse-direction representative — the memoised mirror built
        /// on demand from the completed primary, a single non-recursive step.
        /// </summary>
        /// <param name="role">The representative role.</param>
        /// <returns>The completed epsilon-free automaton.</returns>
        /// <exception cref="UnreachableException">The primary automaton was not built — impossible past the dependency-ordered pass.</exception>
        private RoleAutomaton LookupAutomaton(RoleRepresentative role)
        {
            if(Automata.TryGetValue(role, out RoleAutomaton? memoised))
            {
                return memoised;
            }

            if(!Automata.TryGetValue(PrimaryOf(role), out RoleAutomaton? primary))
            {
                throw new UnreachableException("A required primary automaton was not built by the dependency-ordered pass.");
            }

            RoleAutomaton mirrored = primary.Mirror(AllocateState, MirrorLetter);
            Automata[role] = mirrored;

            return mirrored;
        }

        /// <summary>The representative of a letter's inverse — the letter alphabet of a mirrored automaton.</summary>
        /// <param name="letter">The representative letter.</param>
        /// <returns>The mirrored letter.</returns>
        private RoleRepresentative MirrorLetter(RoleRepresentative letter)
        {
            return Rep(ContextSymbolTable.Inverse(letter.RawMemberId));
        }

        /// <summary>Adds a representative-level chain word's arcs to the automaton per its admissible shape: the transitivity epsilon loop, the R-prefix / R-suffix forms anchored at the final / initial state, a fresh interior arc path otherwise. Interior states are freshly allocated per word, so no cross-word path arises; the published construction's entry and exit epsilon arcs are collapsed into direct arcs from and to the endpoints, which epsilon elimination would produce anyway.</summary>
        /// <param name="automaton">The automaton under construction.</param>
        /// <param name="chain">The representative chain word.</param>
        /// <param name="role">The primary representative super-role.</param>
        private void AddChainToAutomaton(RoleAutomaton automaton, List<RoleRepresentative> chain, RoleRepresentative role)
        {
            if(chain.Count == 2 && chain[0] == role && chain[1] == role)
            {
                automaton.AddEpsilon(automaton.SingleFinal, automaton.Initial);

                return;
            }

            bool firstIsSuper = chain[0] == role;
            bool lastIsSuper = chain[^1] == role;
            int start = firstIsSuper ? automaton.SingleFinal : automaton.Initial;
            int end = lastIsSuper ? automaton.Initial : automaton.SingleFinal;

            int previous = start;
            for(int i = 0; i < chain.Count; i++)
            {
                if((i == 0 && firstIsSuper) || (i == chain.Count - 1 && lastIsSuper))
                {
                    continue;
                }

                int next = (i == chain.Count - 1 || (i == chain.Count - 2 && lastIsSuper)) ? end : AllocateState();
                automaton.AddTransition(previous, chain[i], next);
                previous = next;
            }

            if(previous != end)
            {
                automaton.AddEpsilon(previous, end);
            }
        }

        /// <summary>
        /// Inlines a fresh copy of every non-simple sub-letter's completed automaton
        /// (STEP 3, the strict-order descent), by an explicit worklist over the
        /// pre-inline transitions. The memo already holds every needed sub-automaton
        /// because the dependency-ordered pass builds letters before their
        /// dependents; the transitions an inlined copy contributes are themselves
        /// completed automata and are not reprocessed.
        /// </summary>
        /// <param name="automaton">The automaton under construction.</param>
        /// <param name="role">The primary representative super-role.</param>
        private void InlineSubLetters(RoleAutomaton automaton, RoleRepresentative role)
        {
            Queue<(int From, RoleRepresentative Letter, int To)> pending = new(automaton.Transitions);
            while(pending.Count > 0 && !BudgetExceeded)
            {
                (int from, RoleRepresentative letter, int to) = pending.Dequeue();
                if(letter == role || letter == RoleAutomaton.Epsilon || !NonSimpleRoles.Contains(letter))
                {
                    continue;
                }

                if(!automaton.RemoveTransition(from, letter, to))
                {
                    continue;
                }

                automaton.InlineBetween(from, to, LookupAutomaton(letter), AllocateState);
            }
        }

        /// <summary>Allocates a fresh automaton state against the per-module budget, marking the budget exceeded when it is reached.</summary>
        /// <returns>The fresh state id.</returns>
        private int AllocateState()
        {
            if(AutomatonStates >= AutomatonStateBudget)
            {
                BudgetExceeded = true;

                return AutomatonStates;
            }

            return AutomatonStates++;
        }

        /// <summary>Returns the interned atom of an atomic class, or abstracts a complex one with a polarity-correct fresh name — the standard definitorial structural transformation, a positive-position subexpression abstracted as <c>X ⊑ E</c> and a negative-position one as <c>E ⊑ X</c>.</summary>
        /// <param name="expression">The class expression.</param>
        /// <param name="negative">Whether the expression sits in a negative position (an abstraction is <c>E ⊑ X</c>); otherwise positive (<c>X ⊑ E</c>).</param>
        /// <param name="work">The origin's GCI worklist, extended with the fresh definition.</param>
        /// <returns>The atom id.</returns>
        private int AtomicOrAbstract(OwlClassExpression expression, bool negative, Queue<(OwlClassExpression, OwlClassExpression)> work)
        {
            if(expression is OwlClassReference reference)
            {
                return Symbols.AtomOf(reference.Class.Iri);
            }

            OwlClassReference fresh = FreshReference();
            if(negative)
            {
                work.Enqueue((expression, fresh));
            }
            else
            {
                work.Enqueue((fresh, expression));
            }

            return Symbols.AtomOf(fresh.Class.Iri);
        }

        /// <summary>Mints a fresh structural concept and returns a class reference for re-entry into the normalization worklist.</summary>
        /// <returns>The fresh class reference.</returns>
        private OwlClassReference FreshReference()
        {
            return Symbols.AtomReference(Symbols.FreshAtom());
        }

        /// <summary>Builds a role atom over a representative directioned role — the sole packer of role symbols into DL literals, so a symbol read back off a literal is representative space — storing an inverse occurrence with flipped argument order.</summary>
        /// <param name="role">The representative directioned role.</param>
        /// <param name="first">The first argument.</param>
        /// <param name="second">The second argument.</param>
        /// <returns>The role atom, over the forward role symbol.</returns>
        private static DlLiteral RoleAtom(RoleRepresentative role, DlTerm first, DlTerm second)
        {
            int forward = ContextSymbolTable.Forward(role.Value);

            return ContextSymbolTable.IsInverse(role.Value)
                ? DlLiteral.Role(forward, second, first)
                : DlLiteral.Role(forward, first, second);
        }

        /// <summary>The inverse of an object-property expression (a named property becomes its inverse and back).</summary>
        /// <param name="expression">The object-property expression.</param>
        /// <returns>The inverse expression.</returns>
        private static OwlObjectPropertyExpression InverseExpression(OwlObjectPropertyExpression expression)
        {
            return expression.IsInverse
                ? new OwlObjectPropertyReference(expression.Property)
                : new OwlInverseObjectProperty(expression.Property);
        }

        /// <summary>The flattened conjuncts of a (possibly nested) intersection, or the single expression when it is not an intersection.</summary>
        /// <param name="expression">The class expression.</param>
        /// <returns>The conjuncts.</returns>
        private static List<OwlClassExpression> FlattenIntersection(OwlClassExpression expression)
        {
            if(expression is not OwlObjectIntersectionOf)
            {
                return [expression];
            }

            List<OwlClassExpression> conjuncts = [];
            Stack<OwlClassExpression> work = new();
            work.Push(expression);
            while(work.Count > 0)
            {
                OwlClassExpression current = work.Pop();
                if(current is OwlObjectIntersectionOf intersection)
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        work.Push(operand);
                    }
                }
                else
                {
                    conjuncts.Add(current);
                }
            }

            return conjuncts;
        }

        /// <summary>The flattened disjuncts of a (possibly nested) union, or the single expression when it is not a union.</summary>
        /// <param name="expression">The class expression.</param>
        /// <returns>The disjuncts.</returns>
        private static List<OwlClassExpression> FlattenUnion(OwlClassExpression expression)
        {
            if(expression is not OwlObjectUnionOf)
            {
                return [expression];
            }

            List<OwlClassExpression> disjuncts = [];
            Stack<OwlClassExpression> work = new();
            work.Push(expression);
            while(work.Count > 0)
            {
                OwlClassExpression current = work.Pop();
                if(current is OwlObjectUnionOf union)
                {
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        work.Push(operand);
                    }
                }
                else
                {
                    disjuncts.Add(current);
                }
            }

            return disjuncts;
        }

        /// <summary>Whether a class expression is the top concept <c>owl:Thing</c>.</summary>
        /// <param name="expression">The class expression.</param>
        /// <returns><see langword="true"/> for <c>owl:Thing</c>.</returns>
        private static bool IsTop(OwlClassExpression expression)
        {
            return expression is OwlClassReference reference && reference.Class.Iri.Equals(OwlVocabulary.Thing);
        }

        /// <summary>Whether a class expression is the bottom concept <c>owl:Nothing</c>.</summary>
        /// <param name="expression">The class expression.</param>
        /// <returns><see langword="true"/> for <c>owl:Nothing</c>.</returns>
        private static bool IsBottom(OwlClassExpression expression)
        {
            return expression is OwlClassReference reference && reference.Class.Iri.Equals(OwlVocabulary.Nothing);
        }

        /// <summary>A class reference to the top concept <c>owl:Thing</c>.</summary>
        /// <returns>The top reference.</returns>
        private static OwlClassReference Thing()
        {
            return new OwlClassReference(new NamedNode(OwlVocabulary.Thing));
        }

        /// <summary>A class reference to the bottom concept <c>owl:Nothing</c>.</summary>
        /// <returns>The bottom reference.</returns>
        private static OwlClassReference Nothing()
        {
            return new OwlClassReference(new NamedNode(OwlVocabulary.Nothing));
        }
    }

    /// <summary>A pending GCI from intake, tagged with its origin axiom index.</summary>
    /// <param name="Origin">The origin axiom's index in the module.</param>
    /// <param name="Sub">The subclass expression.</param>
    /// <param name="Super">The superclass expression.</param>
    private readonly record struct PendingGci(int Origin, OwlClassExpression Sub, OwlClassExpression Super);

    /// <summary>A pending admitted object-property assertion, individuals already resolved to representatives, deferred to the emission pass so the role rewrites to its post-quotient representative.</summary>
    /// <param name="Source">The source representative key.</param>
    /// <param name="Target">The target representative key.</param>
    /// <param name="Role">The raw directioned role id of the asserted property.</param>
    /// <param name="Origin">The origin axiom's index in the module.</param>
    private readonly record struct PendingGroundEdge(Utf8String Source, Utf8String Target, RawRoleId Role, int Origin);

    /// <summary>A told ground counting constraint: a DL4 emission whose subclass atom is a ground marker — the representative it constrains, the RAW directioned counted role (the closed graph's query key), the upper bound, the filler atom, and whether the filler is <c>owl:Thing</c> (unqualified).</summary>
    private readonly record struct GroundCountingConstraint(Utf8String Subject, RawRoleId Role, int Bound, int FillerAtom, bool FillerIsThing);

    /// <summary>A deferred chain elimination — a universal over a non-simple role, emitted once its automaton is built.</summary>
    /// <param name="Carrier">The subclass atom carrying the universal.</param>
    /// <param name="Role">The representative directioned role of the universal.</param>
    /// <param name="Filler">The filler atom.</param>
    /// <param name="Origin">The origin axiom's index in the module.</param>
    private readonly record struct PendingElimination(int Carrier, RoleRepresentative Role, int Filler, int Origin);

    /// <summary>A recorded disjoint-role pair: two raw directioned operand ids, an asymmetric-provenance flag selecting the guard remainder name, and the origin axiom index the emitted clash clause carries.</summary>
    /// <param name="First">The first raw directioned operand id.</param>
    /// <param name="Second">The second raw directioned operand id (an asymmetric pair's inverse of <paramref name="First"/>).</param>
    /// <param name="FromAsymmetric">Whether the pair comes from an <c>Asymmetric</c> characteristic (a guard reports its property's IRI) rather than a <c>DisjointObjectProperties</c> axiom (a guard reports the offending operand's IRI).</param>
    /// <param name="Origin">The origin axiom's index in the module.</param>
    private readonly record struct DisjointRolePair(RawRoleId First, RawRoleId Second, bool FromAsymmetric, int Origin);

    /// <summary>
    /// A role automaton under construction (HS2004 Definition 10): a state set, an
    /// initial state, a set of final states, and directioned-role transitions with
    /// an epsilon letter. The construction (arcs, mirror, sub-letter inlining,
    /// epsilon elimination) mutates it through explicit worklists.
    /// </summary>
    private sealed class RoleAutomaton
    {
        /// <summary>The letter marking an epsilon transition; the negative id lies outside every real role space.</summary>
        public static readonly RoleRepresentative Epsilon = new(-1);

        /// <summary>The automaton states.</summary>
        public HashSet<int> States { get; } = [];

        /// <summary>The transitions as (from, letter, to) triples.</summary>
        public List<(int From, RoleRepresentative Letter, int To)> Transitions { get; } = [];

        /// <summary>The final states.</summary>
        public HashSet<int> Finals { get; } = [];

        /// <summary>The initial state.</summary>
        public int Initial { get; }

        /// <summary>The single seeded final state of a freshly built automaton (before mirroring or elimination adds more).</summary>
        public int SingleFinal { get; }

        /// <summary>Initialises an automaton with an initial and a single final state.</summary>
        /// <param name="initial">The initial state.</param>
        /// <param name="singleFinal">The single final state.</param>
        public RoleAutomaton(int initial, int singleFinal)
        {
            Initial = initial;
            SingleFinal = singleFinal;
            States.Add(initial);
            States.Add(singleFinal);
            Finals.Add(singleFinal);
        }

        /// <summary>Adds a directioned-role transition, registering its endpoints as states.</summary>
        /// <param name="from">The source state.</param>
        /// <param name="letter">The directioned role letter.</param>
        /// <param name="to">The target state.</param>
        public void AddTransition(int from, RoleRepresentative letter, int to)
        {
            States.Add(from);
            States.Add(to);
            Transitions.Add((from, letter, to));
        }

        /// <summary>Adds an epsilon transition.</summary>
        /// <param name="from">The source state.</param>
        /// <param name="to">The target state.</param>
        public void AddEpsilon(int from, int to)
        {
            AddTransition(from, Epsilon, to);
        }

        /// <summary>Removes a specific transition, if present.</summary>
        /// <param name="from">The source state.</param>
        /// <param name="letter">The directioned role letter.</param>
        /// <param name="to">The target state.</param>
        /// <returns><see langword="true"/> when a matching transition was removed.</returns>
        public bool RemoveTransition(int from, RoleRepresentative letter, int to)
        {
            return Transitions.Remove((from, letter, to));
        }

        /// <summary>Inlines a fresh copy of a sub-letter automaton between two states via epsilon links (STEP 3).</summary>
        /// <param name="from">The source state the copy's initial is linked from.</param>
        /// <param name="to">The target state the copy's finals link to.</param>
        /// <param name="inner">The sub-letter automaton to copy in.</param>
        /// <param name="allocate">The fresh-state allocator.</param>
        public void InlineBetween(int from, int to, RoleAutomaton inner, Func<int> allocate)
        {
            Dictionary<int, int> copy = [];
            foreach(int state in inner.States)
            {
                copy[state] = allocate();
                States.Add(copy[state]);
            }

            foreach((int innerFrom, RoleRepresentative letter, int innerTo) in inner.Transitions)
            {
                AddTransition(copy[innerFrom], letter, copy[innerTo]);
            }

            AddEpsilon(from, copy[inner.Initial]);
            foreach(int final in inner.Finals)
            {
                AddEpsilon(copy[final], to);
            }
        }

        /// <summary>Builds the mirrored automaton for the inverse-direction representative's language: initials and finals swap, each arc reverses under the mapped letter, and epsilon arcs reverse.</summary>
        /// <param name="allocate">The fresh-state allocator.</param>
        /// <param name="mirrorLetter">Maps a letter to its mirrored (representative-rewritten inverse) letter.</param>
        /// <returns>The mirrored automaton.</returns>
        public RoleAutomaton Mirror(Func<int> allocate, Func<RoleRepresentative, RoleRepresentative> mirrorLetter)
        {
            Dictionary<int, int> copy = [];
            foreach(int state in States)
            {
                copy[state] = allocate();
            }

            RoleAutomaton mirror = new(copy[SingleFinal], copy[Initial]);
            foreach(int state in States)
            {
                mirror.States.Add(copy[state]);
            }

            mirror.Finals.Clear();
            mirror.Finals.Add(copy[Initial]);

            foreach((int from, RoleRepresentative letter, int to) in Transitions)
            {
                RoleRepresentative reversedLetter = letter == Epsilon ? Epsilon : mirrorLetter(letter);
                mirror.AddTransition(copy[to], reversedLetter, copy[from]);
            }

            return mirror;
        }

        /// <summary>Returns an epsilon-free automaton (standard epsilon-closure over the existing states), so chain elimination never mints a vacuous state subsumption.</summary>
        /// <returns>The epsilon-free automaton.</returns>
        public RoleAutomaton EpsilonEliminated()
        {
            Dictionary<int, HashSet<int>> closure = EpsilonClosures();

            RoleAutomaton result = new(Initial, SingleFinal);
            foreach(int state in States)
            {
                result.States.Add(state);
            }

            result.Finals.Clear();
            foreach(int state in States)
            {
                foreach(int reached in closure[state])
                {
                    if(Finals.Contains(reached))
                    {
                        result.Finals.Add(state);
                    }
                }
            }

            foreach((int from, RoleRepresentative letter, int to) in Transitions)
            {
                if(letter == Epsilon)
                {
                    continue;
                }

                foreach(int source in ReachingBy(closure, from))
                {
                    foreach(int target in closure[to])
                    {
                        result.AddTransition(source, letter, target);
                    }
                }
            }

            return result;
        }

        /// <summary>Computes the epsilon-closure (reflexive-transitive epsilon reachability) of every state, by an explicit worklist.</summary>
        /// <returns>The epsilon-reachable set per state.</returns>
        private Dictionary<int, HashSet<int>> EpsilonClosures()
        {
            Dictionary<int, List<int>> epsilonAdjacency = [];
            foreach((int from, RoleRepresentative letter, int to) in Transitions)
            {
                if(letter != Epsilon)
                {
                    continue;
                }

                if(!epsilonAdjacency.TryGetValue(from, out List<int>? list))
                {
                    list = [];
                    epsilonAdjacency[from] = list;
                }

                list.Add(to);
            }

            Dictionary<int, HashSet<int>> closure = [];
            foreach(int state in States)
            {
                HashSet<int> reachable = [state];
                Stack<int> work = new();
                work.Push(state);
                while(work.Count > 0)
                {
                    int current = work.Pop();
                    if(epsilonAdjacency.TryGetValue(current, out List<int>? next))
                    {
                        foreach(int target in next)
                        {
                            if(reachable.Add(target))
                            {
                                work.Push(target);
                            }
                        }
                    }
                }

                closure[state] = reachable;
            }

            return closure;
        }

        /// <summary>The states whose epsilon-closure contains a given state — the states that can reach it by epsilon alone.</summary>
        /// <param name="closure">The epsilon-closure map.</param>
        /// <param name="target">The target state.</param>
        /// <returns>The states reaching the target by epsilon.</returns>
        private static IEnumerable<int> ReachingBy(Dictionary<int, HashSet<int>> closure, int target)
        {
            foreach(KeyValuePair<int, HashSet<int>> entry in closure)
            {
                if(entry.Value.Contains(target))
                {
                    yield return entry.Key;
                }
            }
        }
    }
}
