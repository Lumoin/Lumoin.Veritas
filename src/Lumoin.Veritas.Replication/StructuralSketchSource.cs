using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves a node's own structural sketch to peers: each fetch reads the node's current reconciliation generation
/// from a <see cref="ReplicationIndexFeed"/> and persists its structural sketch at the requested budget. Its
/// <see cref="FetchAsync"/> is an <see cref="AsyncSketchFetchDelegate"/>, so one node's serve is another node's
/// peer fetch — the symmetric, in-process other half of <see cref="VeritasEngine"/>-style reconcile that makes a
/// node a bidirectional peer without a network. Because each fetch reads the feed's current generation, the served
/// sketch always reflects the latest committed default graph.
/// </summary>
/// <remarks>
/// The served image is returned through the <see cref="AsyncSketchFetchDelegate"/> contract, which carries no
/// ownership or disposal hook, so the returned buffer is a plain managed array (the consumer's verifying load
/// copies the symbols into its own buffer and drops it); the projection and persistence buffers underneath are
/// pooled. A pool-aware return awaits the streaming wire-deserialization contract work.
/// </remarks>
public sealed class StructuralSketchSource
{
    /// <summary>The feed whose current generation each fetch serves.</summary>
    private ReplicationIndexFeed Feed { get; }

    /// <summary>The pool the projection and persistence rent their transient buffers from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a sketch source serving a feed's current generation.</summary>
    /// <param name="feed">The reconciliation feed whose current generation is served.</param>
    /// <param name="pool">The pool the transient projection and persistence buffers are rented from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="feed"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public StructuralSketchSource(ReplicationIndexFeed feed, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(pool);

        Feed = feed;
        Pool = pool;
    }

    /// <summary>Serves the node's current structural sketch at a budget — the asynchronous fetch a peer's reconcile awaits.</summary>
    /// <param name="symbolBudget">The number of coded symbols the served sketch must carry.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>The node's persisted structural sketch image for its current reconciliation generation.</returns>
    public ValueTask<ReadOnlyMemory<byte>> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ColumnarTripleIndex index = Feed.Current().Index;
        ArrayBufferWriter<byte> destination = new();
        LocalSketchImage.Write(index, symbolBudget, Pool, destination);

        return new ValueTask<ReadOnlyMemory<byte>>(destination.WrittenMemory);
    }
}
