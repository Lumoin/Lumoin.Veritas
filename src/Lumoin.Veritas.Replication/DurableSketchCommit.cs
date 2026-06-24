using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The receipt of persisting a node's structural sketch as a durable generation
/// (<see cref="DurableSketchStore.Persist"/>): the commit generation it was published under, the dataset
/// <see cref="StateId"/> it was keyed to, and the sketch image's byte length.
/// </summary>
/// <param name="Generation">The monotonic commit generation the sketch was published under.</param>
/// <param name="StateId">The dataset StateId the persisted sketch reflects — the key a restart matches against the live feed.</param>
/// <param name="ImageByteLength">The persisted sketch image's length in bytes.</param>
public readonly record struct DurableSketchCommit(long Generation, NodeIdentifier StateId, int ImageByteLength);
