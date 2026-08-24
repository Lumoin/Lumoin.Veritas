using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves a local replica's sketch to a peer over a Verisync message channel: it reads symbol-budget requests
/// from the request pipe and, for each, serves the maintained encoder's sketch at that budget and writes the image
/// to the response pipe, ending when the requesting side completes the channel. It is the peer-facing other half
/// of <see cref="SketchChannelClient"/>; an active-active replica runs a server for inbound fetches and uses a
/// client for outbound ones. Each request is served at the maintainer's CURRENT committed generation — the
/// maintainer folds every committed default-graph delta, so a long-lived connection always reads fresh symbols,
/// byte-identical to the whole-set re-projection they replace.
/// </summary>
/// <remarks>
/// As on <see cref="SketchChannelClient"/>, the response pipe must have a pause-writer threshold of at least
/// <c>maxFrameLength</c>, or a sketch larger than the threshold pauses this writer before the peer can read it and
/// deadlocks; the default in-memory <see cref="Pipe"/> threshold is 64 KiB.
/// </remarks>
public sealed class SketchChannelServer
{
    private readonly IncrementalSketchMaintainer maintainer;
    private readonly MemoryPool<byte> pool;
    private readonly PipeReader requestReader;
    private readonly PipeWriter responseWriter;
    private readonly ulong dictionaryEpoch;
    private readonly int maxFrameLength;

    /// <summary>Creates a server that serves the maintained encoder's structural sketch over a duplex connection to one peer.</summary>
    /// <param name="maintainer">The maintained encoder whose symbol prefix is served per request.</param>
    /// <param name="pool">The pool the transient image buffers are rented from.</param>
    /// <param name="requestReader">The pipe budget requests are read from.</param>
    /// <param name="responseWriter">The pipe sketch-image responses are written to.</param>
    /// <param name="dictionaryEpoch">This structural endpoint's dictionary epoch; a request carrying a different epoch is declined with a stamped-empty response, since the served identifiers would be incomparable under the peer's dictionary.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public SketchChannelServer(IncrementalSketchMaintainer maintainer, MemoryPool<byte> pool, PipeReader requestReader, PipeWriter responseWriter, ulong dictionaryEpoch, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(maintainer);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.maintainer = maintainer;
        this.pool = pool;
        this.requestReader = requestReader;
        this.responseWriter = responseWriter;
        this.dictionaryEpoch = dictionaryEpoch;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Serves this endpoint's stamped sketch for each request until the requesting side completes the channel, then completes the response side. Every request draws exactly one stamped response: a request for a different domain or epoch, or a budget a faulty or hostile peer cannot be served at (negative, or whose image would exceed one frame), draws a stamped-EMPTY response (a decline the one-shot client reads as an unavailable peer) rather than an exception that ends the loop; a serveable request draws the stamped image. The response side is completed even if the loop throws, so the peer's read always ends rather than hanging on an abandoned pipe.</summary>
    /// <param name="cancellationToken">The token that cancels the serve loop.</param>
    /// <returns>A task that completes when the requesting side ends and the final reply has been flushed; it faults if serving a serveable request throws, after the response side has been completed.</returns>
    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        MessageChannelReader<SketchChannelRequest> requestChannel = new(requestReader, SketchChannelFraming.ReadRequest, maxFrameLength);
        MessageChannelWriter<SketchChannelResponse> responseChannel = new(responseWriter, SketchChannelFraming.WriteStampedImage, maxFrameLength);

        //The response side is completed in the finally so the peer's reader always observes the channel ending —
        //a serve that throws (an over-large local replica, say) completes the response cleanly, so the peer reads
        //no frame and declines as an unavailable peer rather than blocking forever, while this task still faults so
        //the server side observes the error. This mirrors the channel reader, which completes its reader in a
        //finally for the same reason.
        try
        {
            await foreach(SketchChannelRequest request in requestChannel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if(request.Domain != SketchChannelDomain.Structural || request.DictionaryEpoch != dictionaryEpoch || !IsServeableBudget(request.SymbolBudget))
                {
                    //A peer-supplied request is untrusted on a real transport. A domain this structural endpoint does
                    //not serve, an epoch under which the served identifiers would be incomparable, or a budget that is
                    //negative or larger than a single frame can carry, each draws a stamped-empty decline rather than
                    //an image — the symmetric counterpart of the session refusing a mismatched or corrupt peer sketch
                    //by value. The one-shot client reads a frame with no image and reconciles as peer-unavailable.
                    await responseChannel.WriteAsync(new SketchChannelResponse(SketchChannelDomain.Structural, dictionaryEpoch, ReadOnlyMemory<byte>.Empty), cancellationToken).ConfigureAwait(false);

                    continue;
                }

                using SlabBufferWriter imageWriter = new(pool);
                maintainer.WriteSketchImage(request.SymbolBudget, pool, imageWriter);
                int imageLength = imageWriter.BytesWritten;

                //The channel serializer copies the image into its own buffer before this awaits the flush, so the
                //owned image is valid through the write and released at the end of the iteration.
                using IMemoryOwner<byte> imageOwner = imageWriter.Detach();
                await responseChannel.WriteAsync(new SketchChannelResponse(SketchChannelDomain.Structural, dictionaryEpoch, imageOwner.Memory[..imageLength]), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await responseChannel.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Whether a peer-supplied symbol budget can be served on this channel: not negative, and not so large that its coded symbols alone would exceed one frame. The image is at least the coded-symbol bytes, so a budget failing this can never produce a single-frame reply.</summary>
    /// <param name="symbolBudget">The peer-supplied symbol budget.</param>
    /// <returns><see langword="true"/> when the budget is serveable; <see langword="false"/> when it must be declined.</returns>
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
