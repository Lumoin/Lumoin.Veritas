using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves a local replica's sketch to a peer over a Verisync message channel: it reads symbol-budget requests
/// from the request pipe and, for each, persists the local replica's sketch at that budget and writes the image
/// to the response pipe, ending when the requesting side completes the channel. It is the peer-facing other half
/// of <see cref="SketchChannelClient"/>; an active-active replica runs a server for inbound fetches and uses a
/// client for outbound ones. The served index is the snapshot supplied at construction — a replica that mutates
/// between fetches serves a fresh server per connection.
/// </summary>
public sealed class SketchChannelServer
{
    private readonly ColumnarTripleIndex local;
    private readonly MemoryPool<byte> pool;
    private readonly PipeReader requestReader;
    private readonly PipeWriter responseWriter;
    private readonly int maxFrameLength;

    /// <summary>Creates a server that serves <paramref name="local"/>'s sketch over a duplex connection to one peer.</summary>
    /// <param name="local">The local replica whose sketch is served.</param>
    /// <param name="pool">The pool the transient projection and image buffers are rented from.</param>
    /// <param name="requestReader">The pipe budget requests are read from.</param>
    /// <param name="responseWriter">The pipe sketch-image responses are written to.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public SketchChannelServer(ColumnarTripleIndex local, MemoryPool<byte> pool, PipeReader requestReader, PipeWriter responseWriter, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.local = local;
        this.pool = pool;
        this.requestReader = requestReader;
        this.responseWriter = responseWriter;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Serves a sketch for each budget request until the requesting side completes the channel, then completes the response side. A budget above what a single frame can carry fails the channel's frame-length bound rather than producing an unbounded reply.</summary>
    /// <param name="cancellationToken">The token that cancels the serve loop.</param>
    /// <returns>A task that completes when the requesting side ends and the final reply has been flushed.</returns>
    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        MessageChannelReader<int> requestChannel = new(requestReader, SketchChannelFraming.ReadBudget, maxFrameLength);
        MessageChannelWriter<ReadOnlyMemory<byte>> responseChannel = new(responseWriter, SketchChannelFraming.WriteImage, maxFrameLength);

        await foreach(int symbolBudget in requestChannel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            using SlabBufferWriter imageWriter = new(pool);
            LocalSketchImage.Write(local, symbolBudget, pool, imageWriter);
            int imageLength = imageWriter.BytesWritten;

            //The channel serializer copies the image into its own buffer before this awaits the flush, so the
            //owned image is valid through the write and released at the end of the iteration.
            using IMemoryOwner<byte> imageOwner = imageWriter.Detach();
            await responseChannel.WriteAsync(imageOwner.Memory[..imageLength], cancellationToken).ConfigureAwait(false);
        }

        await responseChannel.CompleteAsync().ConfigureAwait(false);
    }
}
