namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Selects between the two surface syntaxes the reader and writer
/// accept: RDF 1.2 Turtle (a single default graph) and RDF 1.2 TriG
/// (a dataset of named graphs plus an optional default graph).
/// </summary>
/// <remarks>
/// The two formats share grammar — TriG adds the <c>{ ... }</c> graph
/// block and the optional <c>GRAPH</c> keyword. A reader in
/// <see cref="Turtle"/> mode rejects graph blocks; a reader in
/// <see cref="TriG"/> mode accepts both bare triples (placed in the
/// default graph) and graph-wrapped triples (placed in the named
/// graph).
/// </remarks>
public enum TurtleSyntax
{
    /// <summary>
    /// RDF 1.2 Turtle. A single default graph; graph blocks are rejected.
    /// </summary>
    Turtle,

    /// <summary>
    /// RDF 1.2 TriG. Triples outside a graph block belong to the default
    /// graph; <c>iri { ... }</c> and <c>GRAPH iri { ... }</c> denote named graphs.
    /// </summary>
    TriG
}
