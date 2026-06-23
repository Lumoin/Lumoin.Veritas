using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Fetches a peer replica's sketch image over a Verisync message channel: it writes one symbol-budget request
/// frame to the request pipe and reads the peer's one sketch-image response frame from the response pipe. Its
/// <see cref="FetchAsync"/> is an <see cref="AsyncSketchFetchDelegate"/> — pass it to
/// <see cref="AntiEntropySession.ReconcileAsync"/> to reconcile over a real wire. The pipe pair is the caller's
/// duplex connection to one peer (a socket, an in-memory <see cref="Pipe"/>, or any duplex stream); one client
/// drives one fetch over it, completing the request side so a cooperating <see cref="SketchChannelServer"/> ends
/// its serve loop and replies.
/// </summary>
/// <remarks>
/// A length-prefixed sketch frame is buffered whole before it is read, so the response pipe's pause-writer
/// threshold must be at least <c>maxFrameLength</c>: a frame larger than the threshold pauses the writer before
/// the reader can consume it and deadlocks. The default in-memory <see cref="Pipe"/> threshold is 64 KiB, so a
/// duplex carrying sketches larger than that — a socket connection, or an in-memory pipe sized for the budget —
/// must be constructed with a pause-writer threshold matching the chosen frame length.
/// </remarks>
public sealed class SketchChannelClient
{
    private readonly PipeWriter requestWriter;
    private readonly PipeReader responseReader;
    private readonly int maxFrameLength;

    /// <summary>Creates a client over a duplex connection to one peer.</summary>
    /// <param name="requestWriter">The pipe the budget request is written to.</param>
    /// <param name="responseReader">The pipe the sketch-image response is read from.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public SketchChannelClient(PipeWriter requestWriter, PipeReader responseReader, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(requestWriter);
        ArgumentNullException.ThrowIfNull(responseReader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.requestWriter = requestWriter;
        this.responseReader = responseReader;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Writes one symbol-budget request and reads the peer's one sketch-image response — the asynchronous fetch the session awaits.</summary>
    /// <param name="symbolBudget">The number of coded symbols the peer's sketch must carry.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>The peer's sketch image, or an empty image when the peer sent no response.</returns>
    public async ValueTask<ReadOnlyMemory<byte>> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        MessageChannelWriter<int> requestChannel = new(requestWriter, SketchChannelFraming.WriteBudget, maxFrameLength);

        //Complete the request side on EVERY exit (success or a faulted/cancelled write) so the peer's serve loop
        //always ends and never strands on an abandoned request pipe; completing a pipe writer is idempotent. A
        //write that throws propagates after the completion, faulting this fetch while still releasing the peer.
        try
        {
            await requestChannel.WriteAsync(symbolBudget, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await requestChannel.CompleteAsync().ConfigureAwait(false);
        }

        MessageChannelReader<ReadOnlyMemory<byte>> responseChannel = new(responseReader, SketchChannelFraming.ReadImage, maxFrameLength);
        await foreach(ReadOnlyMemory<byte> image in responseChannel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            //The first frame is the peer's whole sketch; returning here disposes the enumerator, which completes
            //the response reader. A peer that replied with nothing falls through to the empty image below.
            return image;
        }

        return ReadOnlyMemory<byte>.Empty;
    }
}
