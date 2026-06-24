using System;
using System.Collections.Generic;
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
    private readonly int maxFrameLength;

    /// <summary>Creates a client over a duplex connection to one peer.</summary>
    /// <param name="requestWriter">The pipe the item request is written to.</param>
    /// <param name="responseReader">The pipe the triples response is read from.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public ContentTripleFetchClient(PipeWriter requestWriter, PipeReader responseReader, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(requestWriter);
        ArgumentNullException.ThrowIfNull(responseReader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.requestWriter = requestWriter;
        this.responseReader = responseReader;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Writes the requested items and reads the peer's triples — the asynchronous fetch the content-hash session awaits.</summary>
    /// <param name="items">The peer-only content-hash items to fetch the triples for.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>The triples the peer holds for the items (as terms); fewer than requested when the peer could not resolve some, or empty when it sent no response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public async ValueTask<IReadOnlyList<ContentTriple>> FetchAsync(IReadOnlyList<ContentKey128> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

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

        MessageChannelReader<IReadOnlyList<ContentTriple>> responseChannel = new(responseReader, ContentTripleFraming.ReadTriples, maxFrameLength);
        await foreach(IReadOnlyList<ContentTriple> triples in responseChannel.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            //The first frame is the peer's whole response; returning disposes the enumerator, completing the reader.
            return triples;
        }

        return [];
    }
}
