namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Identifies which RDFS entailment rule produced an
/// <see cref="InferredTriple"/>.
/// </summary>
/// <remarks>
/// <para>
/// The six members cover the entailment rules
/// <see cref="RdfsInference.InferWithProvenanceAsync"/> implements; see
/// <see href="https://www.w3.org/TR/rdf12-schema/#ch_entailment">RDF 1.2 Schema §8</see>
/// for the rule definitions. The set is closed: any extension to the
/// rule surface adds a new member here and a corresponding emission
/// site in <see cref="RdfsInference"/>.
/// </para>
/// </remarks>
public enum InferenceRule
{
    /// <summary>
    /// rdfs2: <c>(p rdfs:domain c) ∧ (s p o) ⇒ (s rdf:type c)</c>.
    /// </summary>
    Rdfs2,

    /// <summary>
    /// rdfs3: <c>(p rdfs:range c) ∧ (s p o) ⇒ (o rdf:type c)</c>.
    /// </summary>
    Rdfs3,

    /// <summary>
    /// rdfs5: <c>(p1 rdfs:subPropertyOf p2) ∧ (p2 rdfs:subPropertyOf p3) ⇒ (p1 rdfs:subPropertyOf p3)</c>.
    /// </summary>
    Rdfs5,

    /// <summary>
    /// rdfs7: <c>(p rdfs:subPropertyOf q) ∧ (s p o) ⇒ (s q o)</c>.
    /// </summary>
    Rdfs7,

    /// <summary>
    /// rdfs9: <c>(c1 rdfs:subClassOf c2) ∧ (x rdf:type c1) ⇒ (x rdf:type c2)</c>.
    /// </summary>
    Rdfs9,

    /// <summary>
    /// rdfs11: <c>(c1 rdfs:subClassOf c2) ∧ (c2 rdfs:subClassOf c3) ⇒ (c1 rdfs:subClassOf c3)</c>.
    /// </summary>
    Rdfs11
}
