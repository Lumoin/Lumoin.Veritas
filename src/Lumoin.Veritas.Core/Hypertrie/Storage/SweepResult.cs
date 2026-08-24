using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Diagnostic statistics describing one completed sweep pass over
/// a <see cref="NodeStore"/>.
/// </summary>
/// <param name="NodesEvicted">The number of canonical node entries removed from the intern table during the sweep.</param>
/// <param name="NodesRetained">The number of canonical node entries that remain interned after the sweep — equal to <see cref="NodeStore.Count"/> at the moment the sweep returned.</param>
/// <param name="ChainsTouched">The number of identifier buckets whose collision chain was modified by the sweep. Most workloads expect zero collisions, so a non-zero value here is most useful as a hash-quality signal rather than a sweep-cost signal.</param>
/// <remarks>
/// <para>
/// The two counts <see cref="NodesEvicted"/> and
/// <see cref="NodesRetained"/> sum to the pre-sweep node count of
/// the store. Operators reading the statistics typically watch
/// <see cref="NodesEvicted"/> over time to confirm sweeps are
/// doing useful work; a long run of zero-eviction sweeps is a
/// hint to retune trigger thresholds.
/// </para>
/// </remarks>
[DebuggerDisplay("SweepResult NodesEvicted={NodesEvicted} NodesRetained={NodesRetained} ChainsTouched={ChainsTouched}")]
public readonly record struct SweepResult(int NodesEvicted, int NodesRetained, int ChainsTouched);
