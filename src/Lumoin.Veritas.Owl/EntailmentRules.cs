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

    /// <summary>scm-int — an intersection is a subclass of every member.</summary>
    public const string ScmInt = "scm-int";

    /// <summary>scm-uni — every member is a subclass of the union.</summary>
    public const string ScmUni = "scm-uni";
}
