using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Fetches the triples a peer holds for a set of content-hash items over a Verisync message channel: it writes one
/// request frame carrying the items and reads the peer's one response frame of triples (as terms). Its
/// <see cref="FetchAsync"/> is an <see cref="AsyncContentTripleFetchDelegate"/> — pass it to
/// <see cref="ContentHashAntiEntropySession.ReconcileAsync"/> as the triple-fetch seam to reconcile across
/// independently-built dictionaries over a real wire. The pipe pair is the caller's duplex connection to one peer;
/// one client drives one fetch, completing the request side so a cooperating <see cref="ContentTripleFetchServer"/>
/// ends its serve loop and replies.
/// </summary>
/// <remarks>
/// As with the sketch transport, the response is buffered whole before it is read, so the response pipe's
/// pause-writer threshold must be at least <c>maxFrameLength</c>: a triples frame larger than the threshold pauses
/// the writer before the reader can consume it and deadlocks. A connection carrying more triples than the default
/// in-memory <see cref="Pipe"/> threshold (64 KiB) must be constructed with a matching threshold and frame length.
/// </remarks>
public sealed class ContentTripleFetchClient
{
    private readonly PipeWriter requestWriter;
    private readonly PipeReader responseReader;
    private readonly MemoryPool<byte> pool;
    private readonly int maxFrameLength;

    /// <summary>Creates a client over a duplex connection to one peer.</summary>
    /// <param name="requestWriter">The pipe the item request is written to.</param>
    /// <param name="responseReader">The pipe the triples response is read from.</param>
    /// <param name="pool">The pool the item-stream response read rents each triple's transient backing from.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public ContentTripleFetchClient(PipeWriter requestWriter, PipeReader responseReader, MemoryPool<byte> pool, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(requestWriter);
        ArgumentNullException.ThrowIfNull(responseReader);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.requestWriter = requestWriter;
        this.responseReader = responseReader;
        this.pool = pool;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Writes the requested items and drives the peer's triples through <paramref name="onTriple"/> — the asynchronous fetch the content-hash session awaits. Each triple is BORROWED for its handler call: its terms view a pooled buffer released as the handler returns, so a handler that retains a term copies it first.</summary>
    /// <param name="items">The peer-only content-hash items to fetch the triples for.</param>
    /// <param name="onTriple">The synchronous handler each returned triple is driven through.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>A task that completes when every returned triple has been handled and the channel has ended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="onTriple"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The peer's response frame violated the channel protocol or could not be deserialized — the wire boundary normalizes a transport-protocol violation to the malformed-input type the session declines on.</exception>
    public async ValueTask FetchAsync(IReadOnlyList<ContentKey128> items, ContentTripleHandlerDelegate onTriple, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onTriple);

        MessageChannelWriter<IReadOnlyList<ContentKey128>> requestChannel = new(requestWriter, ContentTripleFraming.WriteKeys, maxFrameLength);

        //Complete the request side on every exit so the peer's serve loop always ends and never strands on an
        //abandoned request pipe; a write that throws propagates after the completion, releasing the peer.
        try
        {
            await requestChannel.WriteAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await requestChannel.CompleteAsync().ConfigureAwait(false);
        }

        ItemStreamChannelReader<ContentTriple> responseChannel = new(responseReader, ContentTripleFraming.DecodeTriple, pool, ContentTripleFraming.MinTripleWireBytes, maxFrameLength);
        try
        {
            await responseChannel.ReadAllAsync(onTriple.Invoke, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception exception) when(exception is InvalidOperationException or MessageDeserializationException)
        {
            throw new InvalidDataException("A peer triple response violated the channel protocol or could not be deserialized.", exception);
        }
    }
}
