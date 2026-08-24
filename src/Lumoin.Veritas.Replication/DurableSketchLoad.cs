using System;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Persistence;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of loading a node's durable structural sketch on restart: the value-based
/// <see cref="DurableSketchLoadOutcome"/>, and — when it is <see cref="DurableSketchLoadOutcome.Loaded"/> —
/// the verified sketch image to serve, the dataset <see cref="StateId"/> it reflects, and the commit generation
/// it was persisted at. The image lives in a pooled buffer this load OWNS, so the caller disposes the load when
/// it is done with the image (returning the buffer to its pool); a non-loaded outcome owns nothing and disposing
/// it is a no-op. A consumer compares <see cref="StateId"/> with its live feed's current generation: equal means
/// the durable sketch is current and can be served straight from disk (no re-derivation); a later live generation
/// means the durable sketch is stale and the node re-derives.
/// </summary>
public sealed class DurableSketchLoad : IDisposable
{
    /// <summary>The pooled image source backing <see cref="Image"/>; non-<see langword="null"/> only for a <see cref="DurableSketchLoadOutcome.Loaded"/> outcome, disposed with this load.</summary>
    private readonly PooledSegmentImageSource? imageSource;

    /// <summary>Constructs a load outcome, optionally owning the pooled sketch image.</summary>
    /// <param name="outcome">Whether a sketch was loaded, and if not, why.</param>
    /// <param name="imageSource">The pooled image this load owns, or <see langword="null"/> for a non-loaded outcome.</param>
    /// <param name="stateId">The dataset StateId the loaded sketch reflects.</param>
    /// <param name="generation">The commit generation the load reflects.</param>
    private DurableSketchLoad(DurableSketchLoadOutcome outcome, PooledSegmentImageSource? imageSource, NodeIdentifier stateId, long generation)
    {
        Outcome = outcome;
        this.imageSource = imageSource;
        StateId = stateId;
        Generation = generation;
    }

    /// <summary>Whether a sketch was loaded, and if not, why.</summary>
    public DurableSketchLoadOutcome Outcome { get; }

    /// <summary>The verified sketch image (a <c>SketchSegment</c> a peer can load), or empty when not <see cref="DurableSketchLoadOutcome.Loaded"/>; valid until this load is disposed.</summary>
    /// <exception cref="ObjectDisposedException">This load has been disposed.</exception>
    public ReadOnlyMemory<byte> Image => imageSource?.ImageMemory ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>The dataset StateId the loaded sketch reflects, or <see cref="NodeIdentifier.Empty"/> when not loaded.</summary>
    public NodeIdentifier StateId { get; }

    /// <summary>The commit generation the loaded sketch was persisted at, or the recovered generation for a refusal, else 0.</summary>
    public long Generation { get; }

    /// <summary>Builds a <see cref="DurableSketchLoadOutcome.Loaded"/> outcome that takes ownership of the pooled sketch image.</summary>
    /// <param name="imageSource">The verified pooled sketch image; this load owns and disposes it.</param>
    /// <param name="stateId">The dataset StateId the loaded sketch reflects.</param>
    /// <param name="generation">The commit generation the sketch was persisted at.</param>
    /// <returns>The loaded outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imageSource"/> is <see langword="null"/>.</exception>
    public static DurableSketchLoad ForLoaded(PooledSegmentImageSource imageSource, NodeIdentifier stateId, long generation)
    {
        ArgumentNullException.ThrowIfNull(imageSource);

        return new DurableSketchLoad(DurableSketchLoadOutcome.Loaded, imageSource, stateId, generation);
    }

    /// <summary>Builds a non-loaded outcome (it owns no image).</summary>
    /// <param name="outcome">The non-loaded outcome.</param>
    /// <param name="generation">The recovered generation for a refusal, or 0 when nothing is committed.</param>
    /// <returns>The non-loaded outcome.</returns>
    public static DurableSketchLoad ForOutcome(DurableSketchLoadOutcome outcome, long generation)
    {
        return new DurableSketchLoad(outcome, imageSource: null, NodeIdentifier.Empty, generation);
    }

    /// <summary>Returns the pooled image buffer to its pool; a no-op for a non-loaded outcome.</summary>
    public void Dispose()
    {
        imageSource?.Dispose();
    }
}
