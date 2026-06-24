using System;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of loading a node's durable structural sketch on restart: the value-based
/// <see cref="DurableSketchLoadOutcome"/>, and — when it is <see cref="DurableSketchLoadOutcome.Loaded"/> —
/// the verified sketch image to serve, the dataset <see cref="StateId"/> it reflects, and the commit
/// generation it was persisted at. A consumer compares <see cref="StateId"/> with its live feed's
/// current generation: equal means the durable sketch is current and can be served straight from disk
/// (no re-derivation); a later live generation means the durable sketch is stale and the node re-derives.
/// </summary>
/// <param name="Outcome">Whether a sketch was loaded, and if not, why.</param>
/// <param name="Image">The verified sketch image (a <c>SketchSegment</c> a peer can load), or empty when not <see cref="DurableSketchLoadOutcome.Loaded"/>.</param>
/// <param name="StateId">The dataset StateId the loaded sketch reflects, or <see cref="NodeIdentifier.Empty"/> when not loaded.</param>
/// <param name="Generation">The commit generation the loaded sketch was persisted at, or the recovered generation for a refusal, else 0.</param>
public readonly record struct DurableSketchLoad(
    DurableSketchLoadOutcome Outcome,
    ReadOnlyMemory<byte> Image,
    NodeIdentifier StateId,
    long Generation);
