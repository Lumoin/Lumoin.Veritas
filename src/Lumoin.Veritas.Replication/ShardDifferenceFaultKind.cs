namespace Lumoin.Veritas.Replication;

/// <summary>The class of fault a shard-difference fetch converted into a value decline, so the diagnosis on the trace distinguishes a torn transport from a peer violating the protocol.</summary>
public enum ShardDifferenceFaultKind
{
    /// <summary>The transport itself faulted: a refused or torn connection, an I/O fault, a disposed stream.</summary>
    Transport = 0,

    /// <summary>The peer violated the channel or session protocol: a malformed or truncated frame, an out-of-order envelope, an unknown kind.</summary>
    Protocol = 1,
}
