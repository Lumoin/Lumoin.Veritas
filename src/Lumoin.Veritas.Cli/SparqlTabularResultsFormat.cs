namespace Lumoin.Veritas.Cli;

/// <summary>
/// The result format a SELECT's bindings (or an ASK's boolean) render to. The delimited members define
/// SELECT results only — an ASK under a delimited preference answers SPARQL-results-JSON instead, since
/// no W3C delimited format defines a boolean document.
/// </summary>
internal enum SparqlTabularResultsFormat
{
    /// <summary>SPARQL 1.1 Query Results CSV (<c>text/csv</c>) — the default.</summary>
    Csv = 0,

    /// <summary>SPARQL 1.1 Query Results TSV (<c>text/tab-separated-values</c>).</summary>
    Tsv,

    /// <summary>SPARQL 1.1 Query Results JSON (<c>application/sparql-results+json</c>).</summary>
    Json,

    /// <summary>SPARQL Query Results XML (<c>application/sparql-results+xml</c>).</summary>
    Xml,
}
