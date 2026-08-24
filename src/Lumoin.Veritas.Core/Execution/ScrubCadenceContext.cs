namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The inputs a <see cref="ScrubCadenceEstimatorDelegate"/> derives a
/// scrub-initiation interval from: this node's memory-protection verdict,
/// the size of the data under protection, and the replica constellation
/// that cross-checks it. The estimator weighs these against a target
/// probability of undetected corruption per period.
/// </summary>
/// <remarks>
/// <para>
/// Only the protection verdict is known when an execution policy resolves;
/// the data size and cluster shape are runtime and persistence facts, so
/// the scrub scheduler re-evaluates the estimator with a fuller context as
/// they become known. The defaults make a resolve-time, protection-only
/// context constructible directly.
/// </para>
/// </remarks>
/// <param name="MemoryIsProtected">Whether this node's memory is treated as hardware error-corrected; this lowers ONLY the memory channel's per-item intensity (see <see cref="CorruptionChannel"/>), not the storage channel or any other.</param>
/// <param name="ProtectedItemCount">The number of content items (blocks) under integrity protection — the aggregate error opportunity. A non-positive value means "unknown", and the estimator substitutes a nominal size.</param>
/// <param name="ReplicaCount">The number of replicas holding this data. Cross-checking across replicas lets each node scrub less often; one replica is a single node with no cross-check.</param>
/// <param name="ProtectedReplicaCount">How many of the replicas have error-corrected memory — the heterogeneous-protection constellation; protected replicas are the more reliable detectors.</param>
/// <param name="TargetUndetectedCorruptionProbability">The acceptable probability of undetected corruption per scrub period. A non-positive value means "use the estimator's default target".</param>
public readonly record struct ScrubCadenceContext(
    bool MemoryIsProtected,
    long ProtectedItemCount = 0,
    int ReplicaCount = 1,
    int ProtectedReplicaCount = 0,
    double TargetUndetectedCorruptionProbability = 0.0);
