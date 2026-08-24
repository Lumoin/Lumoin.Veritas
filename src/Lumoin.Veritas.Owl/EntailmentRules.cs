namespace Lumoin.Veritas.Owl;

/// <summary>
/// The canonical names of the entailment rules the materializers fire,
/// carried by <see cref="InferenceTraceEvent.Rule"/> and
/// <see cref="Rl.OwlRlResult.InconsistencyRule"/>. The names are the rule
/// identifiers of the defining specifications — the RDFS entailment
/// patterns and the OWL 2 RL/RDF rules tables — plus a few
/// implementation-completion labels for derivations the rule set
/// materialises without a table row of their own (symmetry seeds and
/// reflexive-property instantiation).
/// </summary>
/// <remarks>
/// A rule that the materializer answers through a precomputed closure is
/// labelled by the rule whose conclusion it produces — a statement
/// inherited through a chain of superproperties is still an
/// <see cref="Rdfs7"/> conclusion.
/// </remarks>
public static class EntailmentRules
{
    //RDFS entailment patterns.

    /// <summary>rdf1 — the predicate of any statement is an <c>rdf:Property</c>.</summary>
    public const string Rdf1 = "rdf1";

    /// <summary>rdfs2 — a property's domain types its subjects.</summary>
    public const string Rdfs2 = "rdfs2";

    /// <summary>rdfs3 — a property's range types its objects.</summary>
    public const string Rdfs3 = "rdfs3";

    /// <summary>rdfs5 — subproperty transitivity over schema statements.</summary>
    public const string Rdfs5 = "rdfs5";

    /// <summary>rdfs6 — every <c>rdf:Property</c> is a subproperty of itself.</summary>
    public const string Rdfs6 = "rdfs6";

    /// <summary>rdfs7 — a statement holds under every superproperty.</summary>
    public const string Rdfs7 = "rdfs7";

    /// <summary>rdfs8 — every <c>rdfs:Class</c> is a subclass of <c>rdfs:Resource</c>.</summary>
    public const string Rdfs8 = "rdfs8";

    /// <summary>rdfs9 — an instance of a class is an instance of every superclass.</summary>
    public const string Rdfs9 = "rdfs9";

    /// <summary>rdfs10 — every <c>rdfs:Class</c> is a subclass of itself.</summary>
    public const string Rdfs10 = "rdfs10";

    /// <summary>rdfs11 — subclass transitivity over schema statements.</summary>
    public const string Rdfs11 = "rdfs11";

    /// <summary>rdfs12 — every <c>rdfs:ContainerMembershipProperty</c> is a subproperty of <c>rdfs:member</c>.</summary>
    public const string Rdfs12 = "rdfs12";

    /// <summary>rdfs13 — every <c>rdfs:Datatype</c> is a subclass of <c>rdfs:Literal</c>.</summary>
    public const string Rdfs13 = "rdfs13";

    /// <summary>The rdfs2/rdfs3 consequences of the RDF(S) axiomatic schema — vocabulary domains and ranges typing the subjects and objects of schema statements.</summary>
    public const string AxiomaticTyping = "axiomatic-typing";

    //OWL 2 RL/RDF rules, Table 4 (equality).

    /// <summary>eq-ref — every term of a statement is <c>owl:sameAs</c> itself.</summary>
    public const string EqRef = "eq-ref";

    /// <summary>eq-sym — <c>owl:sameAs</c> is symmetric.</summary>
    public const string EqSym = "eq-sym";

    /// <summary>eq-trans — <c>owl:sameAs</c> is transitive.</summary>
    public const string EqTrans = "eq-trans";

    /// <summary>eq-rep-s — a statement holds with a same-as subject substituted.</summary>
    public const string EqRepS = "eq-rep-s";

    /// <summary>eq-rep-p — a statement holds with a same-as predicate substituted.</summary>
    public const string EqRepP = "eq-rep-p";

    /// <summary>eq-rep-o — a statement holds with a same-as object substituted.</summary>
    public const string EqRepO = "eq-rep-o";

    /// <summary>eq-diff1 — <c>owl:sameAs</c> and <c>owl:differentFrom</c> between the same pair contradict.</summary>
    public const string EqDiff1 = "eq-diff1";

    /// <summary>eq-diff2/eq-diff3 — two same-as members of an <c>owl:AllDifferent</c> list contradict.</summary>
    public const string EqDiff2 = "eq-diff2";

    /// <summary>Implementation completion: <c>owl:differentFrom</c> is materialised symmetrically for the eq-diff checks.</summary>
    public const string DifferentFromSymmetry = "different-from-symmetry";

    //OWL 2 RL/RDF rules, Table 5 (properties).

    /// <summary>prp-dom — a property's domain types its subjects.</summary>
    public const string PrpDom = "prp-dom";

    /// <summary>prp-rng — a property's range types its objects.</summary>
    public const string PrpRng = "prp-rng";

    /// <summary>prp-fp — a functional property's values for one subject equate.</summary>
    public const string PrpFp = "prp-fp";

    /// <summary>prp-ifp — an inverse-functional property's subjects for one value equate.</summary>
    public const string PrpIfp = "prp-ifp";

    /// <summary>prp-irp — an irreflexive property with a reflexive statement contradicts.</summary>
    public const string PrpIrp = "prp-irp";

    /// <summary>prp-symp — a symmetric property's statements reverse.</summary>
    public const string PrpSymp = "prp-symp";

    /// <summary>prp-asyp — an asymmetric property with statements both ways contradicts.</summary>
    public const string PrpAsyp = "prp-asyp";

    /// <summary>prp-trp — a transitive property's statements compose.</summary>
    public const string PrpTrp = "prp-trp";

    /// <summary>Implementation completion: a reflexive property instantiates over the named individuals.</summary>
    public const string ReflexiveInstantiation = "reflexive-instantiation";

    /// <summary>prp-spo1 — a statement holds under every superproperty.</summary>
    public const string PrpSpo1 = "prp-spo1";

    /// <summary>prp-spo2 — a property chain entails the superproperty between its endpoints.</summary>
    public const string PrpSpo2 = "prp-spo2";

    /// <summary>prp-eqp1 — a statement of a property holds for its equivalent.</summary>
    public const string PrpEqp1 = "prp-eqp1";

    /// <summary>prp-eqp2 — a statement of a property holds for its equivalent, the other way.</summary>
    public const string PrpEqp2 = "prp-eqp2";

    /// <summary>prp-pdw — disjoint properties sharing a statement contradict; disjointness is materialised symmetrically.</summary>
    public const string PrpPdw = "prp-pdw";

    /// <summary>prp-adp — pairwise disjointness of an <c>owl:AllDisjointProperties</c> list, statements and contradiction both.</summary>
    public const string PrpAdp = "prp-adp";

    /// <summary>prp-inv1 — an inverse property's statements reverse.</summary>
    public const string PrpInv1 = "prp-inv1";

    /// <summary>prp-inv2 — an inverse property's statements reverse, the other way.</summary>
    public const string PrpInv2 = "prp-inv2";

    /// <summary>prp-key — instances sharing a value for every key property equate.</summary>
    public const string PrpKey = "prp-key";

    /// <summary>prp-npa1/prp-npa2 — a negative property assertion with the asserted statement contradicts.</summary>
    public const string PrpNpa = "prp-npa";

    //OWL 2 RL/RDF rules, Table 6 (classes).

    /// <summary>cls-nothing2 — an instance of <c>owl:Nothing</c> contradicts.</summary>
    public const string ClsNothing2 = "cls-nothing2";

    /// <summary>cls-int1 — an instance of every intersection member is an instance of the intersection.</summary>
    public const string ClsInt1 = "cls-int1";

    /// <summary>cls-int2 — an instance of an intersection is an instance of every member.</summary>
    public const string ClsInt2 = "cls-int2";

    /// <summary>cls-uni — an instance of a union member is an instance of the union.</summary>
    public const string ClsUni = "cls-uni";

    /// <summary>cls-com — an instance of a class and its complement contradicts.</summary>
    public const string ClsCom = "cls-com";

    /// <summary>cls-svf1 — a value in the filler puts the subject in the some-values restriction.</summary>
    public const string ClsSvf1 = "cls-svf1";

    /// <summary>cls-svf2 — any value puts the subject in a some-values-from-<c>owl:Thing</c> restriction.</summary>
    public const string ClsSvf2 = "cls-svf2";

    /// <summary>cls-avf — an all-values restriction types every value of its instances.</summary>
    public const string ClsAvf = "cls-avf";

    /// <summary>cls-hv1 — an instance of a has-value restriction carries the value.</summary>
    public const string ClsHv1 = "cls-hv1";

    /// <summary>cls-hv2 — carrying the value puts the subject in the has-value restriction.</summary>
    public const string ClsHv2 = "cls-hv2";

    /// <summary>cls-maxc1 — a max-0 cardinality restriction with any edge contradicts.</summary>
    public const string ClsMaxc1 = "cls-maxc1";

    /// <summary>cls-maxc2 — a max-1 cardinality restriction equates its instances' values.</summary>
    public const string ClsMaxc2 = "cls-maxc2";

    /// <summary>cls-maxqc1/cls-maxqc2 — a qualified max-0 restriction with a qualified edge contradicts.</summary>
    public const string ClsMaxqc1 = "cls-maxqc1";

    /// <summary>cls-maxqc3/cls-maxqc4 — a qualified max-1 restriction equates its instances' qualified values.</summary>
    public const string ClsMaxqc4 = "cls-maxqc4";

    /// <summary>cls-oo — every member of an enumeration is an instance of it.</summary>
    public const string ClsOo = "cls-oo";

    //OWL 2 RL/RDF rules, Table 7 (class axioms).

    /// <summary>cax-sco — an instance of a subclass is an instance of the superclass.</summary>
    public const string CaxSco = "cax-sco";

    /// <summary>cax-eqc1 — an instance of a class is an instance of its equivalent.</summary>
    public const string CaxEqc1 = "cax-eqc1";

    /// <summary>cax-eqc2 — an instance of a class is an instance of its equivalent, the other way.</summary>
    public const string CaxEqc2 = "cax-eqc2";

    /// <summary>cax-dw — disjoint classes sharing an instance contradict; disjointness is materialised symmetrically.</summary>
    public const string CaxDw = "cax-dw";

    /// <summary>cax-adc — pairwise disjointness of an <c>owl:AllDisjointClasses</c> list, statements and contradiction both.</summary>
    public const string CaxAdc = "cax-adc";

    //OWL 2 RL/RDF rules, Table 8 (datatypes).

    /// <summary>dt-diff — <c>owl:sameAs</c> between literals denoting distinct data values contradicts.</summary>
    public const string DtDiff = "dt-diff";

    /// <summary>dt-not-type — a literal outside its asserted datatype's value space contradicts.</summary>
    public const string DtNotType = "dt-not-type";

    /// <summary>
    /// Extension beyond the §4.3 tables: two ranges confine a property's
    /// values to the intersection of their value spaces, so every
    /// datatype-map space containing that intersection is a range too —
    /// the value-space arithmetic D-entailment adds over the minimal rules.
    /// </summary>
    public const string DtRangeIntersection = "dt-range-intersection";

    /// <summary>
    /// Extension beyond the §4.3 tables: a property chain
    /// <c>p ∘ p ⊑ p</c> states exactly transitivity, so the
    /// <c>owl:TransitiveProperty</c> typing materialises from the chain.
    /// </summary>
    public const string ChainTransitivity = "chain-trans";

    /// <summary>
    /// Extension beyond the §4.3 tables: transitivity states exactly the
    /// chain <c>p ∘ p ⊑ p</c>, so the chain structure materialises from
    /// the typing — on deterministic list nodes, keeping the fixpoint
    /// idempotent.
    /// </summary>
    public const string TransitivityChain = "trans-chain";

    //OWL 2 RL/RDF rules, Table 9 (schema).

    /// <summary>scm-cls — every declared class is its own sub- and equivalent class, below <c>owl:Thing</c> and above <c>owl:Nothing</c>.</summary>
    public const string ScmCls = "scm-cls";

    /// <summary>scm-sco — subclass transitivity over schema statements.</summary>
    public const string ScmSco = "scm-sco";

    /// <summary>scm-eqc1 — equivalent classes are mutual subclasses; equivalence is materialised symmetrically.</summary>
    public const string ScmEqc1 = "scm-eqc1";

    /// <summary>scm-eqc2 — mutual subclasses are equivalent.</summary>
    public const string ScmEqc2 = "scm-eqc2";

    /// <summary>scm-op — a declared object property is its own sub- and equivalent property.</summary>
    public const string ScmOp = "scm-op";

    /// <summary>scm-dp — a declared datatype property is its own sub- and equivalent property.</summary>
    public const string ScmDp = "scm-dp";

    /// <summary>scm-spo — subproperty transitivity over schema statements.</summary>
    public const string ScmSpo = "scm-spo";

    /// <summary>scm-eqp1 — equivalent properties are mutual subproperties; equivalence is materialised symmetrically.</summary>
    public const string ScmEqp1 = "scm-eqp1";

    /// <summary>scm-eqp2 — mutual subproperties are equivalent.</summary>
    public const string ScmEqp2 = "scm-eqp2";

    /// <summary>scm-dom1 — a domain's superclasses are domains.</summary>
    public const string ScmDom1 = "scm-dom1";

    /// <summary>scm-dom2 — a superproperty's domains are domains.</summary>
    public const string ScmDom2 = "scm-dom2";

    /// <summary>scm-rng1 — a range's superclasses are ranges.</summary>
    public const string ScmRng1 = "scm-rng1";

    /// <summary>scm-rng2 — a superproperty's ranges are ranges.</summary>
    public const string ScmRng2 = "scm-rng2";

    /// <summary>scm-hv — has-value restrictions on one value order by their properties' subsumption.</summary>
    public const string ScmHv = "scm-hv";

    /// <summary>scm-svf1 — some-values restrictions on one property order by their fillers' subsumption.</summary>
    public const string ScmSvf1 = "scm-svf1";

    /// <summary>scm-svf2 — some-values restrictions on one filler order by their properties' subsumption.</summary>
    public const string ScmSvf2 = "scm-svf2";

    /// <summary>scm-avf1 — all-values restrictions on one property order by their fillers' subsumption.</summary>
    public const string ScmAvf1 = "scm-avf1";

    /// <summary>scm-avf2 — all-values restrictions on one filler order contravariantly: the superproperty's restriction subsumes under the subproperty's.</summary>
    public const string ScmAvf2 = "scm-avf2";

    /// <summary>scm-int — an intersection is a subclass of every member.</summary>
    public const string ScmInt = "scm-int";

    /// <summary>scm-uni — every member is a subclass of the union.</summary>
    public const string ScmUni = "scm-uni";

    //RDF-Based-semantics completions beyond the §4.3 tables.

    /// <summary>
    /// Extension beyond the §4.3 tables: the functional and
    /// inverse-functional characteristics transfer across
    /// <c>owl:inverseOf</c>, exchanging kinds — a functional property's
    /// inverse is inverse functional and an inverse-functional property's
    /// inverse is functional, on either orientation of the inverse
    /// statement.
    /// </summary>
    public const string InverseCharacteristicTransfer = "inverse-characteristic-transfer";

    /// <summary>
    /// Extension beyond the §4.3 tables: a property whose range is a
    /// singleton enumeration is functional, and one whose domain is a
    /// singleton enumeration is inverse functional — the enumerated
    /// extension holds one individual, so all values (or subjects)
    /// coincide.
    /// </summary>
    public const string SingletonEnumerationCharacteristic = "singleton-enumeration-characteristic";

    /// <summary>
    /// Extension beyond the §4.3 tables: <c>owl:complementOf</c> denotes a
    /// symmetric relation between class extensions, so the reversed
    /// statement materialises.
    /// </summary>
    public const string ComplementOfSymmetry = "complement-of-symmetry";

    /// <summary>
    /// Extension beyond the §4.3 tables: an enumeration whose member set
    /// is a subset of another enumeration's is its subclass —
    /// <c>owl:oneOf</c> reads its list as a set, so order and repetition
    /// carry no meaning; equal sets subsume both ways and scm-eqc2 closes
    /// the equivalence.
    /// </summary>
    public const string OneOfMemberSubset = "one-of-member-subset";

    /// <summary>
    /// Extension beyond the §4.3 tables: a union whose disjunct set is a
    /// subset of another union's is its subclass — <c>owl:unionOf</c>
    /// reads its list as a set, so order and repetition carry no meaning;
    /// equal sets subsume both ways and scm-eqc2 closes the equivalence.
    /// </summary>
    public const string UnionOfMemberSubset = "union-of-member-subset";

    /// <summary>
    /// Extension beyond the §4.3 tables: <c>rdf:nil</c> is the empty
    /// collection and carries no <c>rdf:first</c> or <c>rdf:rest</c> edge
    /// — an unconditional condition of the RDF-Based semantics,
    /// independent of any list machinery around the node — so either edge
    /// on it contradicts. A fixed-subject falsity, never a general
    /// list-well-formedness pass.
    /// </summary>
    public const string NilStructureClash = "nil-structure-clash";

    /// <summary>
    /// Extension beyond the §4.3 tables: <c>owl:oneOf</c> confines its
    /// subject's extension to the finite enumerated sequence, and the
    /// RDF-Based universe is infinite — the mandatory datatype map's
    /// value spaces alone — so an enumeration of <c>owl:Thing</c>
    /// contradicts at any arity and the list is never read. The falsity
    /// holds under the RDF-Based semantics the calculus claims; the
    /// Direct semantics admits finite domains and is answered by a
    /// different engine.
    /// </summary>
    public const string ThingEnumerationClash = "thing-enumeration-clash";

    /// <summary>
    /// Extension beyond the §4.3 tables: the restriction conditions
    /// determine the extension exactly, so one asserted value places the
    /// subject in a min-cardinality-1 restriction on the property. Bounds
    /// above one never conclude membership — two asserted values need not
    /// be distinct individuals — and the zero bound stays out as
    /// universally true.
    /// </summary>
    public const string MinCardinalityOneMembership = "min-cardinality-one-membership";

    //The comprehension completions: fired only in closures the entailment
    //path computes with the informative comprehension conditions granted,
    //over the expression structure those conditions supply.

    /// <summary>
    /// Comprehension completion: a union whose member set holds a class and
    /// a complement of that class covers everything, so <c>owl:Thing</c> is
    /// its subclass — the excluded middle over the class extensions.
    /// </summary>
    public const string UnionExcludedMiddle = "union-excluded-middle";

    /// <summary>
    /// Comprehension completion: a union whose member set holds a
    /// some-values-from-<c>owl:Thing</c> restriction and a max-0-cardinality
    /// restriction on one property covers everything — every individual
    /// either has a value for the property or has none — so
    /// <c>owl:Thing</c> is its subclass.
    /// </summary>
    public const string UnionValueDichotomy = "union-value-dichotomy";

    /// <summary>
    /// Comprehension completion: a functional property confines every
    /// individual to at most one value, so <c>owl:Thing</c> is a subclass
    /// of any max-1-cardinality restriction on it.
    /// </summary>
    public const string FunctionalMaxOneUniversal = "functional-max-one-universal";

    /// <summary>
    /// Comprehension completion: an enumeration over the empty list denotes
    /// the empty class, so it is a subclass of <c>owl:Nothing</c>; the
    /// equivalence composes through scm-cls and scm-eqc2.
    /// </summary>
    public const string EmptyEnumerationNothing = "empty-enumeration-nothing";

    /// <summary>
    /// Comprehension completion: a property ranged by every member of an
    /// intersection is ranged by the intersection — the iff reading of
    /// <c>rdfs:range</c> confines the values to the members' common
    /// extension.
    /// </summary>
    public const string IntersectionRangeCompletion = "intersection-range-completion";

    /// <summary>
    /// Comprehension completion: an intersection of complements orders
    /// against a complement of a union by De Morgan duality — the union's
    /// disjunct set contained in the complemented set concludes one
    /// subsumption direction, the reverse containment the other, and equal
    /// sets compose the equivalence through scm-eqc2.
    /// </summary>
    public const string DeMorganSubset = "de-morgan-subset";

    /// <summary>
    /// Comprehension completion: an exact cardinality is the pair of the
    /// same-bound min- and max-cardinality restrictions on the property, so
    /// a class stated as the intersection of such a pair also carries the
    /// intersection over the single exact-cardinality restriction.
    /// </summary>
    public const string CardinalityShorthand = "cardinality-shorthand";

    /// <summary>
    /// Comprehension completion: every member of a some-values-from
    /// restriction has a value for the property inside the filler, so a
    /// fresh deterministic witness carries the edge and the typing — one
    /// witness per (member, restriction, property, filler), never shared
    /// across a node's independent existentials, with minting refused when
    /// the restriction repeats on the member's witness-derivation chain.
    /// </summary>
    public const string SomeValuesFromWitness = "some-values-from-witness";

    /// <summary>
    /// Comprehension completion: a declared domain of <c>rdf:type</c>
    /// subsumes every class — ICEXT is the <c>rdf:type</c> slice, so every
    /// member of any class is an <c>rdf:type</c> subject and lands in the
    /// domain. The subject of the domain statement is the fixed
    /// <c>rdf:type</c> term; a general property's domain never concludes a
    /// subsumption.
    /// </summary>
    public const string TypeDomainUniversalSubsumption = "type-domain-universal-subsumption";

    /// <summary>
    /// Comprehension completion: one has-value node carrying two
    /// <c>owl:onProperty</c> edges states one extension equation per
    /// property, so two functional properties whose domains are that node
    /// share the extension (domain × value) and subsume each other; the
    /// equivalence composes through scm-eqp2. The rule keys on
    /// <c>owl:hasValue</c> — no other detail predicate pins the value each
    /// member maps to.
    /// </summary>
    public const string SharedHasValuePropertyCollapse = "shared-hasvalue-property-collapse";

    /// <summary>
    /// Comprehension completion: a property ranged by two datatypes with
    /// disjoint value spaces has the empty extension, which is a
    /// subproperty of every property — emitted for each term the closure
    /// types as a property, and only while the property carries no
    /// statement.
    /// </summary>
    public const string DisjointRangeVacuousSubproperty = "disjoint-range-vacuous-subproperty";

    /// <summary>
    /// Comprehension completion falsity: a property ranged by two
    /// datatypes with disjoint value spaces admits no statement — any
    /// object of such a statement would denote a value in both spaces —
    /// so an asserted statement of the property contradicts, whatever the
    /// object's term kind.
    /// </summary>
    public const string DisjointRangeClash = "disjoint-range-clash";

    /// <summary>
    /// Comprehension completion: a literal typed by an alias IRI held
    /// <c>owl:sameAs</c> a datatype-map member denotes through the member's
    /// own lexical-to-value map — the RDF-Based semantics keys a typed
    /// literal's denotation on the datatype its type IRI DENOTES — so the
    /// literal is <c>owl:sameAs</c> its retype onto the member. Minted per
    /// occurring alias-typed literal whose lexical form is valid for the
    /// member; an invalid form leaves both denotations arbitrary and
    /// refuses.
    /// </summary>
    public const string DatatypeAliasRetype = "datatype-alias-retype";

    /// <summary>
    /// Comprehension completion: a singleton enumeration proves its class's
    /// extension holds exactly one member, an equivalent cardinality
    /// restriction on an inverse property converts a proven count through
    /// the property's fibres, a functional property multiplies a proven
    /// count by its fibre bound, and the anchored read-back equates a
    /// counted equivalence's bound term <c>owl:sameAs</c> the minted digit
    /// literal of the proven count. Emitted for the read-back alone; the
    /// intermediate counts are certificates, never triples.
    /// </summary>
    public const string FibreCardinalityCertificate = "fibre-cardinality-certificate";

    /// <summary>
    /// Implementation completion beyond the Table 4/8 rules: an
    /// <c>owl:sameAs</c> identity between two datatypes with known-disjoint
    /// value spaces contradicts the datatype map, whose members denote
    /// distinct resources in every interpretation — one resource cannot
    /// carry both value spaces.
    /// </summary>
    public const string DtDisjointIdentity = "dt-disjoint-identity";
}
