namespace Lumoin.Veritas.Replication;

/// <summary>
/// The kind of transport fault a <see cref="SketchFetchFaultPlan"/> injects into a replication sketch fetch. These
/// model the network adversities a distributed-repair certification must survive, beyond the value-based declines a
/// governance policy can express: a peer that drops, a payload that corrupts in flight, a transport that throws.
/// </summary>
public enum SketchFetchFaultKind
{
    /// <summary>No fault — the inner fetch runs and its image is returned unchanged.</summary>
    Pass,

    /// <summary>The fetch is skipped and an empty image is returned, which the session reads as an unavailable peer (the value-based decline).</summary>
    Drop,

    /// <summary>The inner fetch runs, then its bytes are mutated so the sketch fails its checksum/decode — modelling a payload corrupted in flight.</summary>
    Corrupt,

    /// <summary>The fetch throws <see cref="Lumoin.Veritas.Core.Network.InjectedNetworkFaultException"/> — modelling a transport that errors rather than declining by value.</summary>
    Fail,
}
