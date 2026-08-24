namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Semantic options for SPARQL Update execution — the update-side counterpart of
/// <see cref="SparqlEnginePolicy"/>, kept as its own type because these options
/// CHANGE an update's effect by design, where the engine policy only ever selects
/// between evaluation routes.
/// </summary>
/// <param name="ContextualAssertionLoad">Whether a plain <c>LOAD</c> (no <c>INTO GRAPH</c>) imports the
/// source document as a CONTEXTUAL ASSERTION instead of merging it into the default graph: the document's
/// triples land in a freshly minted blank-node graph, and one provenance triple — the fresh graph name
/// <c>prov:wasDerivedFrom</c> the source document IRI — is asserted in the default graph, so imported
/// context is discoverable without being asserted globally. <c>LOAD … INTO GRAPH</c> is unaffected (an
/// explicit destination wins). Off — the default — keeps the SPARQL-specification destination: the
/// document merges into the default graph.</param>
public readonly record struct SparqlUpdateOptions(bool ContextualAssertionLoad = false)
{
    /// <summary>The default options: every update behaves exactly as the SPARQL Update specification prescribes — the record struct's default value, named for call-site clarity.</summary>
    public static SparqlUpdateOptions Default { get; }
}
