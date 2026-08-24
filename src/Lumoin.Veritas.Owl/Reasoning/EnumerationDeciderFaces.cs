using System;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The enumeration-CSP habitat decider's face selection — a flag set of
/// independent enables: production runs every face lit, and the battery
/// drives every other selection — including the explicit dark control —
/// through the internal measurement overloads. The census-first recognizer
/// is not gated here — the habitat class ships unconditionally as a
/// counting label.
/// This listing is a BIT CATALOGUE and states no order: which family owns
/// which face, and the production every-face-lit selection, are folded from
/// the recognizer's registry table alone.
/// </summary>
[Flags]
internal enum EnumerationDeciderFaces
{
    /// <summary>Both faces dark — the explicit dark control: the window measurements still ride the census, no decision moves, every module reaches the engine untouched.</summary>
    None = 0,

    /// <summary>The clash-only face (face one) is lit: a sound told clash over the repaired forced-merge kinds or the counting comparison decides the module inconsistent pre-engine.</summary>
    ClashOnly = 1,

    /// <summary>The certifying face (face two) is lit: a whole-axiom-set-admitted enumeration-algebra module within its windows is decided pre-engine with its exact subsumption set.</summary>
    Certifying = 2,

    /// <summary>The partition-clash face (face three) is lit: a partition-counting module whose distinct pairwise-disjoint anchors outnumber its told cardinality cap is decided inconsistent pre-engine by the closed-form pigeonhole refutation.</summary>
    PartitionClash = 4,

    /// <summary>The partition-certify face (face four) is lit: a partition-counting module whose distinct anchors fit inside its told cardinality cap is decided consistent pre-engine by the closed-form witness model.</summary>
    PartitionCertify = 8,

    /// <summary>The gadget-clash face (face five) is lit: a boolean-cardinality-gadget module whose compiled propositional theory rejects every assignment inside the atom window is decided inconsistent pre-engine by the exhaustion refutation.</summary>
    GadgetClash = 16,

    /// <summary>The gadget-certify face (face six) is lit: a boolean-cardinality-gadget module whose compiled propositional theory admits an assignment is decided consistent pre-engine by the explicit witness model that assignment induces.</summary>
    GadgetCertify = 32,

    /// <summary>The pair-clash face (face seven) is lit: an enumeration-algebra module past the member-universe window whose anchor-and-pair composition resolves and whose whole vector space fails is decided inconsistent pre-engine by the exhaustion refutation over the two-element quotient.</summary>
    EnumerationPairClash = 64,

    /// <summary>The pair-certify face (face eight) is lit: an enumeration-algebra module past the member-universe window whose anchor-and-pair composition resolves and whose vector space admits a passing assignment is decided consistent pre-engine by the witness model that vector induces.</summary>
    EnumerationPairCertify = 128,

    /// <summary>The spy-point clash face (face nine) is lit: a spy-point module whose told domain bound — the sum of the funnel members' inverse-linked caps — falls below its told minimum-cardinality demand is decided inconsistent pre-engine by the closed-form domain-bound pigeonhole. The face is clash-only: it has no certify counterpart, because a demand inside the bound proves nothing about the surrounding module.</summary>
    SpyPointClash = 256,

    /// <summary>The bijection-chain clash face (face ten) is lit: a bijection-chain module whose told size variables propagate to an impossible state — two forced sizes in one equality class, a forced size outside its told bounds, a negative sum residue, a product with no cardinal solution, or an asserted empty conjunct — is decided inconsistent pre-engine. The face is monotone: unrecognized axioms are ignored, because extra axioms only shrink the model class.</summary>
    BijectionChainClash = 512,

    /// <summary>The bijection-chain certify face (face eleven) is lit: a whole-module-admitted bijection-chain module matching exactly one certificate route — the all-empty vacuity model or the canonical grounded-tower fiber model — is decided consistent pre-engine by that route's explicit witness construction.</summary>
    BijectionChainCertify = 1024,

    /// <summary>The told-ground-witness clash face (face twelve) is lit: a told-ground module whose ground memberships derive a class membership beside its own denial, a told disjoint partner, an asserted empty class, or a denied edge is decided inconsistent pre-engine. The face is monotone: unrecognized axioms are ignored, because extra axioms only shrink the model class, and no rule instantiates an existential with a told term.</summary>
    ToldGroundWitnessClash = 2048,

    /// <summary>The told-ground-witness certify face (face thirteen) is lit: a whole-module-admitted told-ground module whose described model — one carrier per told term, the told edges closed under told inverse mirroring, and one least-fixpoint extension per named class — satisfies every axiom on re-check is decided consistent pre-engine by that explicit witness construction.</summary>
    ToldGroundWitnessCertify = 4096,

    /// <summary>The repairing-ground clash face (face fourteen) is lit: a restriction-rich ground module whose told ground memberships derive a class membership beside its own denial, a told disjoint partner, an asserted empty class, or a denied edge is decided inconsistent pre-engine. The face is monotone and told-only: unrecognized axioms are ignored, because extra axioms only shrink the model class, and it never reads a repaired edge, a minted element, or the told-sameness quotient.</summary>
    RepairingGroundClash = 8192,

    /// <summary>The repairing certify face (face fifteen) is lit: a whole-module-admitted restriction-rich ground module whose repaired described model — the told terms under the told-sameness quotient, the told edges under the re-applied closure operator, the deterministic and bounded-choice repairs, and the minted witnesses — satisfies every axiom on re-check is decided consistent pre-engine by that explicit witness construction. The face declares no refutation on any path: every completeness limit is a silence carrying its measurement.</summary>
    RepairingCertify = 16384,

    /// <summary>The modal-expansion clash face (face sixteen) is lit: a modal role-expansion module whose bounded skolem expansion reaches a node carrying an unqualified minimum above an unqualified maximum on one property, or carrying <c>owl:Nothing</c>, is decided inconsistent pre-engine by that expansion. The face is clash-only: it has no certify counterpart, because a clash-free bounded expansion proves nothing about a module the face never finished building.</summary>
    ModalExpansionClash = 32768,

    /// <summary>The modal-gadget clash face (face seventeen) is lit: a branching modal-gadget module whose monotone composition closure derives a class membership beside its own told complement, or a told bottom membership, is decided inconsistent pre-engine. The face is monotone: unrecognized axioms are ignored, because every derivation is a chain of set-intersection facts that holds in every model of any superset of the axioms it used, and no rule reads a cardinality bound, a role, a successor or a constructed model.</summary>
    ModalGadgetClash = 65536,

    /// <summary>The modal-gadget certify face (face eighteen) is lit: a whole-module-admitted branching modal-gadget module whose MINTED skolem tree — one node per demanded successor signature, each node's propositional state computed rather than enumerated — satisfies every admitted axiom on re-check against the tree's RAW relations is decided consistent pre-engine by that explicit witness construction. The face declares no refutation on any path: a failed construction, an exhausted sweep, an inadmissible axiom and a window trip are all silence.</summary>
    ModalGadgetCertify = 131072,

    /// <summary>The nominal-pinned-role clash face (face nineteen) is lit: a diagonal-pinned role module whose told inverse-functionality and told self-loops at EVERY member of the role's told nominal range pin the role's extension into the identity diagonal, beside a told edge whose exact reverse a told concept denial excludes, is decided inconsistent pre-engine by the five-step closed form. The face is clash-only, told-only, and MONOTONE: unrecognized axioms are ignored, because extra axioms only shrink the model class, and a pinned extension certifies nothing about the surrounding module.</summary>
    NominalPinnedRoleClash = 262144,
}
