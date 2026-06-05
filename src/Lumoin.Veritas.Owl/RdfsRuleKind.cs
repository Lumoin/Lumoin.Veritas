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
}
