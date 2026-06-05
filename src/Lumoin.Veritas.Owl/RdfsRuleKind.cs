namespace Lumoin.Veritas.Owl;

/// <summary>
/// The RDFS entailment rule that produced a derivation, carried by
/// <see cref="InferenceTraceEvent"/>. Rules that the materializer
/// answers through precomputed closures are labelled by the rule
/// whose conclusion they produce — a statement inherited through a
/// chain of superproperties is still an rdfs7 conclusion.
/// </summary>
public enum RdfsRuleKind
{
    /// <summary>rdfs2 — a property's domain types its subjects.</summary>
    Rdfs2Domain = 0,

    /// <summary>rdfs3 — a property's range types its objects.</summary>
    Rdfs3Range = 1,

    /// <summary>rdfs5 — subproperty transitivity over schema statements.</summary>
    Rdfs5SubPropertyTransitivity = 2,

    /// <summary>rdfs7 — a statement holds under every superproperty.</summary>
    Rdfs7SubPropertyInheritance = 3,

    /// <summary>rdfs9 — an instance of a class is an instance of every superclass.</summary>
    Rdfs9TypeInheritance = 4,

    /// <summary>rdfs11 — subclass transitivity over schema statements.</summary>
    Rdfs11SubClassTransitivity = 5,

    /// <summary>rdf1 — the predicate of any statement is an <c>rdf:Property</c>.</summary>
    Rdf1PropertyTyping = 6,

    /// <summary>
    /// rdfs2/rdfs3 over the RDF(S) axiomatic schema — the domains and
    /// ranges of the vocabulary properties type the subjects and
    /// objects of <c>rdf:type</c>, <c>rdfs:subClassOf</c>,
    /// <c>rdfs:subPropertyOf</c>, <c>rdfs:domain</c>, and
    /// <c>rdfs:range</c> statements as <c>rdfs:Class</c> /
    /// <c>rdf:Property</c>.
    /// </summary>
    RdfsAxiomaticTyping = 7,

    /// <summary>rdfs6 — every <c>rdf:Property</c> is a subproperty of itself.</summary>
    Rdfs6SubPropertyReflexivity = 8,

    /// <summary>rdfs8 — every <c>rdfs:Class</c> is a subclass of <c>rdfs:Resource</c>.</summary>
    Rdfs8ResourceSubsumption = 9,

    /// <summary>rdfs10 — every <c>rdfs:Class</c> is a subclass of itself.</summary>
    Rdfs10SubClassReflexivity = 10,

    /// <summary>rdfs12 — every <c>rdfs:ContainerMembershipProperty</c> is a subproperty of <c>rdfs:member</c>.</summary>
    Rdfs12ContainerMembership = 11,

    /// <summary>rdfs13 — every <c>rdfs:Datatype</c> is a subclass of <c>rdfs:Literal</c>.</summary>
    Rdfs13DatatypeLiteral = 12,
}
