namespace Lumoin.Veritas.Database;

/// <summary>
/// The value-based standing of a mutable open's metadata-plane coordination
/// (<see cref="VeritasEngineOptions.MetadataCoordination"/>), surfaced on
/// <see cref="VeritasEngine.MetadataCoordination"/> the way the explicit baseline step is surfaced on
/// <see cref="VeritasEngine.ReplicationBaseline"/>. A pending or contested standing is an expected operational
/// condition reported as a value — the open serves; only a definite adverse consultation refuses an open, and
/// that refusal is loud at the open itself, never here.
/// </summary>
public enum MetadataCoordinationStanding
{
    /// <summary>No coordination seams were configured at open: the host runs planeless, and nothing was consulted.</summary>
    NotConfigured,

    /// <summary>Every consultation the open made answered definitively in this host's favor: the identity claim stands and, where a baseline was minted, its confirm landed.</summary>
    Confirmed,

    /// <summary>At least one consultation answered undecided — an unreachable or quorumless plane — and the open proceeded, per the plane's never-a-liveness-dependency constraint. The host's coordination loop retries off the open path; the next open re-issues idempotently.</summary>
    Pending,

    /// <summary>The post-commit baseline confirm met a record already carrying a DIFFERENT lineage: the local store is committed but the deployment's agreed baseline disagrees. The store serves; the resolution is operator-level, against the coordinated record.</summary>
    Contested
}
