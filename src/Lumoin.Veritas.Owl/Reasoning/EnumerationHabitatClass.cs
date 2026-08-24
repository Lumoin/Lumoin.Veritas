namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The enumeration-CSP habitat class the census-first recognizer assigns one
/// module at survey time, from axiom shapes alone: the nominal-funnel counting
/// shape (Shape N), the role-free enumeration algebra (Shape E), the mixed
/// module carrying both clusters, the nominal-free partition-counting template
/// (Shape P), the nominal-free boolean-cardinality gadget (Shape G), the
/// spy-point domain-bound encoding (Shape S), the bijection-chain cardinality
/// arithmetic (Shape B), the told-ground witness encoding (Shape W), the
/// restriction-rich ground ontology (Shape R), the bounded skolem-expansion
/// modal module (Shape M), the branching modal-gadget module (Shape K), the
/// diagonal-pinned role module (Shape D), or
/// none.
/// The class is a census
/// label, never a verdict: it rides
/// the assembled statistics and the decision trace on every context-arm
/// decision and abstention, and the decider faces apply their own exact
/// jurisdiction predicates independently of it.
/// This listing is a LABEL CATALOGUE and states no order: the order the
/// recognizer offers a module to its probes in, and each probe's admission on
/// each census path, are declared in the recognizer's registry table alone.
/// </summary>
public enum EnumerationHabitatClass
{
    /// <summary>The module carries no habitat shape — the zero-cost default for every nominal-free module without both a counting mention and a partition template, and for nominal modules without a funnel-and-cap or whole-module enumeration-algebra shape.</summary>
    None = 0,

    /// <summary>The nominal-funnel counting shape (Shape N): a universal inverse-role funnel into a non-empty one-of together with a max-cardinality cap anchored on a one-of — the derived-pigeonhole habitat the clash-only face reads.</summary>
    NominalCounting = 1,

    /// <summary>The role-free enumeration algebra (Shape E): every axiom is one of the admitted class-algebra kinds over named classes, one-ofs of named individuals, and boolean connectives, with at least one one-of present — the equality-partition habitat the certifying face reads.</summary>
    EnumerationAlgebra = 2,

    /// <summary>Both clusters in one module: the funnel-and-cap shape beside an enumeration-algebra one-of axiom outside the funnel and cap shapes. The certifying face is silent over the whole module; only the clash-only face may decide it.</summary>
    Mixed = 3,

    /// <summary>The partition-counting template (Shape P): a nominal-free module whose named class is equivalent to an intersection of existential restrictions and exactly one unqualified max-cardinality restriction over one named object property — the set-partition counting habitat the closed-form partition faces read.</summary>
    PartitionCounting = 4,

    /// <summary>The boolean-cardinality-gadget module (Shape G): a nominal-free module carrying a bare 0/1 cardinality gadget equivalence beside a named-class intersection equivalence — the propositional habitat the bounded assignment-evaluation gadget faces read.</summary>
    BooleanCardinalityGadget = 5,

    /// <summary>The spy-point domain-bound encoding (Shape S): a nominal module carrying an <c>owl:Thing</c> subclass existential into a one-of beside a told unqualified max-cardinality cap — the domain-bounding habitat the closed-form spy-point clash face reads.</summary>
    SpyPointDomainBound = 6,

    /// <summary>The bijection-chain cardinality arithmetic (Shape B): a module carrying ONE plain role that simultaneously bears a told functional or inverse-functional characteristic, stands in a told inverse-role pair over plain roles, and heads a told existential restriction in subclass or equivalence position — all three ingredients bound to that one same role, the role linkage every size-variable derivation consumes — the habitat the propagating bijection-chain faces read.</summary>
    BijectionChainArithmetic = 7,

    /// <summary>The told-ground witness encoding (Shape W): a module carrying a told object-property assertion beside a told inverse-role pair over plain roles and a told plain-role existential restriction in subclass or equivalence position — the ground-membership habitat the told-ground-witness faces read.</summary>
    ToldGroundWitness = 8,

    /// <summary>The restriction-rich ground ontology (Shape R): a module carrying at least two value, universal, or cardinality restrictions in obligation position over a told individual population above the told-ground carrier ceiling — the repair habitat the repairing faces read, where an obligation's witnessing edge was never told and only edge invention exhibits a model.</summary>
    RestrictionRichGround = 9,

    /// <summary>The bounded skolem-expansion modal module (Shape M): a module carrying a told class assertion whose label reaches an existential, a universal over a told inverse of a role an existential or a told edge uses, and a numeric clash template, with no disjunctive construct anywhere — the modal habitat the bounded expansion clash face reads, where the contradiction is reachable only by creating existential witnesses and propagating a fact back up an inverse role.</summary>
    ModalRoleExpansion = 10,

    /// <summary>The branching modal-gadget module (Shape K): a module built from two syntactically DISJOINT layers — a propositional layer of unqualified cardinality gadgets composed by binary-intersection equivalences over named classes above the composition threshold, and a modal layer of existentials and universals over ONE characteristic-free role — with no disjunctive construct, no nominal and no has-value anywhere. It is the habitat the two modal-gadget faces read: the monotone composition clash face, and the certify face that verifies the whole module against a MINTED skolem tree.</summary>
    ModalGadgetTree = 11,

    /// <summary>The diagonal-pinned role module (Shape D): a module carrying a told inverse-functional characteristic over a plain role beside a told range over a plain role resolving — inline or through one told hop — to a one-of of named individuals. It is the first REFUTATION-module habitat: the clash-only nominal-pinned-role face reads it, where told self-loops at every range member pin the role's extension into the identity diagonal and a reverse-denied told edge has no model.</summary>
    NominalPinnedRole = 12,
}
