using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves a local replica's CONTENT-HASH sketch to a peer over a Verisync message channel: for each symbol-budget
/// request it projects the local triples through the content-hash projection, persists their sketch at that budget,
/// and writes the image. It is the content-hash counterpart of <see cref="SketchChannelServer"/> (which serves the
/// structural sketch); a content-hash reconcile pairs it with a <see cref="SketchChannelClient"/> for the inbound
/// sketch fetch and a <see cref="ContentTripleFetchServer"/> for the by-key triple fetch. The served snapshot is
/// supplied at construction; a replica that mutates between fetches serves a fresh server per connection.
/// </summary>
/// <remarks>
/// The content-hash sketch shares the structural sketch's geometry (a 16-byte item, an 8-byte checksum), so the
/// same response-pipe pause-writer-threshold caveat applies: a sketch larger than <c>maxFrameLength</c> deadlocks
/// the writer. A budget a faulty or hostile peer cannot be served at is declined by value (no response written),
/// which the one-shot client reads as an unavailable peer.
/// </remarks>
public sealed class ContentHashSketchChannelServer
{
    private readonly ColumnarTripleIndex local;
    private readonly ContentHashReconciliationProjection projection;
    private readonly MemoryPool<byte> pool;
    private readonly PipeReader requestReader;
    private readonly PipeWriter responseWriter;
    private readonly int maxFrameLength;

    /// <summary>Creates a server that serves <paramref name="local"/>'s content-hash sketch over a duplex connection to one peer.</summary>
    /// <param name="local">The local replica whose sketch is served.</param>
    /// <param name="projection">The content-hash projection the local triples are projected through.</param>
    /// <param name="pool">The pool the transient item and image buffers are rented from.</param>
    /// <param name="requestReader">The pipe budget requests are read from.</param>
    /// <param name="responseWriter">The pipe sketch-image responses are written to.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public ContentHashSketchChannelServer(ColumnarTripleIndex local, ContentHashReconciliationProjection projection, MemoryPool<byte> pool, PipeReader requestReader, PipeWriter responseWriter, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.local = local;
        this.projection = projection;
        this.pool = pool;
        this.requestReader = requestReader;
        this.responseWriter = responseWriter;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>The reserved dictionary epoch a content-hash endpoint stamps and requires: content-hash items are epoch-independent, so the domain fixes the epoch at <c>0</c> and any other value is out of contract.</summary>
    private const ulong ContentHashEpoch = 0;

    /// <summary>Serves this endpoint's stamped content-hash sketch for each request until the requesting side completes the channel, then completes the response side. Every request draws exactly one stamped response: a request for a domain other than content-hash, an epoch other than the reserved <c>0</c>, or a budget that cannot be served in one frame draws a stamped-empty decline the one-shot client reads as an unavailable peer; a serveable request draws the stamped image.</summary>
    /// <param name="cancellationToken">The token that cancels the serve loop.</param>
    /// <returns>A task that completes when the requesting side ends and the final reply has been flushed.</returns>
    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        MessageChannelReader<SketchChannelRequest> requestChannel = new(requestReader, SketchChannelFraming.ReadRequest, maxFrameLength);
        MessageChannelWriter<SketchChannelResponse> responseChannel = new(responseWriter, SketchChannelFraming.WriteStampedImage, maxFrameLength);

        try
        {
            await foreach(SketchChannelRequest request in requestChannel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if(request.Domain != SketchChannelDomain.ContentHash || request.DictionaryEpoch != ContentHashEpoch || !IsServeableBudget(request.SymbolBudget))
                {
                    //A request for a domain this endpoint does not serve, an epoch outside the content-hash contract,
                    //or an unserveable budget draws a stamped-empty decline rather than an image; the one-shot client
                    //reads a frame with no image and reconciles as peer-unavailable.
                    await responseChannel.WriteAsync(new SketchChannelResponse(SketchChannelDomain.ContentHash, ContentHashEpoch, ReadOnlyMemory<byte>.Empty), cancellationToken).ConfigureAwait(false);

                    continue;
                }

                using SlabBufferWriter imageWriter = new(pool);
                ContentHashSketch.WriteImage(local, projection, request.SymbolBudget, pool, imageWriter);
                int imageLength = imageWriter.BytesWritten;

                using IMemoryOwner<byte> imageOwner = imageWriter.Detach();
                await responseChannel.WriteAsync(new SketchChannelResponse(SketchChannelDomain.ContentHash, ContentHashEpoch, imageOwner.Memory[..imageLength]), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await responseChannel.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Whether a peer-supplied symbol budget can be served on this channel: not negative, and not so large that its coded symbols alone would exceed one frame.</summary>
    /// <param name="symbolBudget">The peer-supplied symbol budget.</param>
    /// <returns><see langword="true"/> when the budget is serveable.</returns>
    private bool IsServeableBudget(int symbolBudget)
    {
        if(symbolBudget < 0)
        {
            return false;
        }

        long symbolByteCount = (long)symbolBudget * SketchContract.Structural.SymbolWidth;

        return symbolByteCount <= maxFrameLength;
    }
}
