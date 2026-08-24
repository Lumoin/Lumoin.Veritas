using System;
using System.Buffers;
using System.IO;
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
    private readonly MemoryPool<byte> pool;
    private readonly SketchChannelDomain domain;
    private readonly ulong dictionaryEpoch;
    private readonly int maxFrameLength;

    /// <summary>Creates a client over a duplex connection to one peer.</summary>
    /// <param name="requestWriter">The pipe the budget request is written to.</param>
    /// <param name="responseReader">The pipe the sketch-image response is read from.</param>
    /// <param name="pool">The pool the owned sketch-image response is rented from.</param>
    /// <param name="domain">The reconciliation domain every request is stamped with; the peer refuses a domain it does not serve.</param>
    /// <param name="dictionaryEpoch">The dictionary epoch every request is stamped with — this endpoint's epoch in the structural domain, the reserved <c>0</c> in the content-hash domain.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public SketchChannelClient(PipeWriter requestWriter, PipeReader responseReader, MemoryPool<byte> pool, SketchChannelDomain domain, ulong dictionaryEpoch, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(requestWriter);
        ArgumentNullException.ThrowIfNull(responseReader);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.requestWriter = requestWriter;
        this.responseReader = responseReader;
        this.pool = pool;
        this.domain = domain;
        this.dictionaryEpoch = dictionaryEpoch;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Writes one stamped symbol-budget request and reads the peer's one sketch-image response — the asynchronous fetch the session awaits. Ownership of the returned image transfers to the caller, which disposes it.</summary>
    /// <param name="symbolBudget">The number of coded symbols the peer's sketch must carry.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>The peer's stamped sketch image as an owned <see cref="SketchFetchResult"/>, or <see cref="SketchFetchResult.Unavailable"/> when the peer sent no response frame at all.</returns>
    /// <exception cref="InvalidDataException">The peer's response frame violated the channel protocol or could not be deserialized — the wire boundary normalizes a transport-protocol violation to the malformed-input type the session declines on.</exception>
    public async ValueTask<SketchFetchResult> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        MessageChannelWriter<SketchChannelRequest> requestChannel = new(requestWriter, SketchChannelFraming.WriteRequest, maxFrameLength);

        //Complete the request side on EVERY exit (success or a faulted/cancelled write) so the peer's serve loop
        //always ends and never strands on an abandoned request pipe; completing a pipe writer is idempotent. A
        //write that throws propagates after the completion, faulting this fetch while still releasing the peer.
        try
        {
            await requestChannel.WriteAsync(new SketchChannelRequest(domain, dictionaryEpoch, symbolBudget), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await requestChannel.CompleteAsync().ConfigureAwait(false);
        }

        OwnedMessageChannelReader<SketchFetchResult> responseChannel = new(responseReader, SketchChannelFraming.ReadOwnedImage, pool, maxFrameLength);
        SketchFetchResult result = SketchFetchResult.Unavailable;

        //The first frame is the peer's whole sketch and its ownership transfers to the caller: capturing it ENDS
        //the read, without waiting for the peer to complete the response side. That first-frame return is the
        //fetch's liveness mechanism on a raw duplex transport — a socket propagates no writer completion, so a
        //serve loop parked on its next request would otherwise hold this read open forever. Breaking disposes the
        //enumerator, which completes the response reader; a further frame a misbehaving peer sends is never read,
        //so no rental is created to leak. A captured rental is released on EVERY abnormal exit — cancellation and
        //transport faults are the wire's ordinary failure modes and propagate as themselves, while the read
        //drive's protocol violations and deserializer failures are normalized to the malformed-input type the
        //session declines on.
        try
        {
            await foreach(SketchFetchResult image in responseChannel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                result = image;

                break;
            }
        }
        catch(Exception exception)
        {
            result.Dispose();
            if(exception is InvalidOperationException or MessageDeserializationException)
            {
                throw new InvalidDataException("A peer sketch response violated the channel protocol or could not be deserialized.", exception);
            }

            throw;
        }

        return result;
    }
}
