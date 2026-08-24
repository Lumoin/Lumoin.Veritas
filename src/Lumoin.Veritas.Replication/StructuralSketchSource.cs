using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves a node's own structural sketch to peers: each fetch copies the maintained encoder's symbol prefix at the
/// requested budget through the shipped sketch framing, so the serve pays the incremental produce instead of a
/// whole-set re-projection per fetch. Its <see cref="FetchAsync"/> is an <see cref="AsyncSketchFetchDelegate"/>, so
/// one node's serve is another node's peer fetch — the symmetric, in-process other half of
/// <see cref="VeritasEngine"/>-style reconcile that makes a node a bidirectional peer without a network. The
/// maintainer folds every committed default-graph delta, so the served sketch always reflects the latest committed
/// default graph, byte-identical to the whole-set re-projection it replaces.
/// </summary>
/// <remarks>
/// The served image is returned as an owning <see cref="SketchFetchResult"/>: the persisted sketch is written into
/// a pooled buffer the consumer disposes once its verifying load has copied the symbols out, so the whole in-process
/// serve stays pool-backed with no managed image array — the same ownership contract the wire fetch carries.
/// </remarks>
public sealed class StructuralSketchSource
{
    /// <summary>The maintained encoder whose symbol prefix each fetch serves; it also carries the dictionary epoch every served image is stamped with.</summary>
    private IncrementalSketchMaintainer Maintainer { get; }

    /// <summary>The pool the persistence rents its transient buffers from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a sketch source serving the maintained encoder's current generation, stamped with the maintainer's dictionary epoch.</summary>
    /// <param name="maintainer">The maintained encoder whose symbol prefix is served.</param>
    /// <param name="pool">The pool the transient persistence buffers are rented from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="maintainer"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public StructuralSketchSource(IncrementalSketchMaintainer maintainer, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(maintainer);
        ArgumentNullException.ThrowIfNull(pool);

        Maintainer = maintainer;
        Pool = pool;
    }

    /// <summary>Serves the node's current structural sketch at a budget — the asynchronous fetch a peer's reconcile awaits. The returned result OWNS its pooled image; the consumer disposes it.</summary>
    /// <param name="symbolBudget">The number of coded symbols the served sketch must carry.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>The node's persisted structural sketch image for its current reconciliation generation, as an owning <see cref="SketchFetchResult"/>.</returns>
    public ValueTask<SketchFetchResult> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using SlabBufferWriter writer = new(Pool);
        Maintainer.WriteSketchImage(symbolBudget, Pool, writer);
        int length = writer.BytesWritten;

        //Detach hands ownership of a fresh pooled buffer of exactly the written bytes to the result; disposing the
        //writer afterwards releases only its own slabs, not the detached buffer, which the consumer disposes.
        IMemoryOwner<byte> owner = writer.Detach();

        return new ValueTask<SketchFetchResult>(new SketchFetchResult(owner, length, SketchChannelDomain.Structural, Maintainer.DictionaryEpoch));
    }
}
