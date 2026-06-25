using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The configuration of a <see cref="VeritasEngine"/>: what is wired and how it
/// behaves. By default everything is wired and fully usable; setting a knob to
/// <c>null</c> is the deliberate optimisation that drops a feature for a
/// lighter, faster deployment that links less machinery.
/// </summary>
public sealed record VeritasEngineOptions
{
    /// <summary>The reasoning configuration; the default reasons fully (bounded), and <c>null</c> links no reasoning machinery so the engine serves simple-entailment results.</summary>
    public ReasoningConfiguration? Reasoning { get; init; } = ReasoningConfiguration.Default;

    /// <summary>The execution policy sizing the compute lane and the host's resource use.</summary>
    public ExecutionPolicy Execution { get; init; } = ExecutionPolicy.Default;

    /// <summary>The SPARQL <c>SERVICE</c> transport a federated query or update step uses — the outbound federation seam (in-process dispatch, an HTTP client, a cache); <see langword="null"/> means a non-silent <c>SERVICE</c> is unsupported. The caller's opaque <see cref="AccessContext"/> is forwarded to it per call.</summary>
    public SparqlServiceTransport? ServiceTransport { get; init; }

    /// <summary>The resolver for <c>FROM</c> / <c>FROM NAMED</c> dataset graphs and <c>LOAD</c> documents — the outbound graph-fetch seam; <see langword="null"/> means those operations reach no source. The caller's opaque <see cref="AccessContext"/> is forwarded to it per call.</summary>
    public GraphSourceResolver? GraphSource { get; init; }

    /// <summary>The access-control policy consulted per candidate triple of every local graph read; <see langword="null"/> allows every triple at zero cost. The protocol (OAuth, DID/VC, ZKP, …) lives in this policy and the opaque <see cref="AccessContext"/> the caller supplies per query or update, not in the engine.</summary>
    public AccessControlDelegate? AccessControl { get; init; }

    /// <summary>The fully-wired default options.</summary>
    public static VeritasEngineOptions Default { get; } = new();
}
