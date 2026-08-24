namespace Lumoin.Veritas.Cli;

/// <summary>
/// How a failed <see cref="OperationResult"/> failed, for surfaces that map failure classes onto distinct
/// answers — the HTTP endpoint maps these onto the SPARQL Protocol's status codes while the command-line
/// and MCP surfaces render the message alone.
/// </summary>
internal enum OperationFailureKind
{
    /// <summary>An ordinary operation failure with no protocol-specific classification (an unreadable file, an unknown algorithm, a bad parameter).</summary>
    General = 0,

    /// <summary>The query text does not parse, or is a SPARQL Update where a query belongs — the protocol's malformed-query class (HTTP 400).</summary>
    Malformed,

    /// <summary>The query is well-formed but the engine refuses to execute it — the protocol's query-request-refused class (HTTP 500).</summary>
    Refused,

    /// <summary>The request's dataset description names a graph the engine cannot serve (HTTP 400).</summary>
    DatasetNotAcceptable,
}
