namespace Lumoin.Veritas.Cli;

/// <summary>The RDF serialization a CONSTRUCT/DESCRIBE result graph renders to.</summary>
internal enum SparqlGraphResultsFormat
{
    /// <summary>N-Triples (<c>application/n-triples</c>) — the default; the graph is default-graph quads, so the N-Quads writer emits valid N-Triples.</summary>
    NTriples = 0,

    /// <summary>Turtle (<c>text/turtle</c>).</summary>
    Turtle,
}
