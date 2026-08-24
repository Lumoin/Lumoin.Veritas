using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves a peer's triples for content-hash items over a Verisync message channel: it reads one request frame of
/// items, resolves each through the local side-map to the triple this node holds, decodes it through this node's
/// dictionary into dictionary-independent terms, and writes one response frame of those triples. It is the
/// peer-facing other half of <see cref="ContentTripleFetchClient"/>. An item this node does not hold is dropped
/// from the response — the requester treats a short response as an unsatisfied difference and declines — so a stale
/// or wrong request cannot make the server fail.
/// </summary>
/// <remarks>
/// The side-map and dictionary are the snapshot supplied at construction; a node that mutates between fetches
/// serves a fresh server per connection. As with the client, the response pipe must have a pause-writer threshold
/// of at least <c>maxFrameLength</c>, or a triples frame larger than the threshold deadlocks the writer.
/// </remarks>
public sealed class ContentTripleFetchServer
{
    private readonly ContentHashSideMap sideMap;
    private readonly TermDictionary dictionary;
    private readonly MemoryPool<byte> pool;
    private readonly PipeReader requestReader;
    private readonly PipeWriter responseWriter;
    private readonly int maxFrameLength;

    /// <summary>Creates a server that resolves content-hash items to this node's triples over a duplex connection to one peer.</summary>
    /// <param name="sideMap">The local side-map resolving an item to the triple this node holds.</param>
    /// <param name="dictionary">The local dictionary the resolved triples are decoded through into terms.</param>
    /// <param name="pool">The pool the item-stream request read rents each key's transient backing from.</param>
    /// <param name="requestReader">The pipe item requests are read from.</param>
    /// <param name="responseWriter">The pipe triples responses are written to.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's. Defaults to <see cref="MessageChannel.DefaultMaxFrameLength"/>.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public ContentTripleFetchServer(ContentHashSideMap sideMap, TermDictionary dictionary, MemoryPool<byte> pool, PipeReader requestReader, PipeWriter responseWriter, int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(sideMap);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        this.sideMap = sideMap;
        this.dictionary = dictionary;
        this.pool = pool;
        this.requestReader = requestReader;
        this.responseWriter = responseWriter;
        this.maxFrameLength = maxFrameLength;
    }

    /// <summary>Reads the requested content-hash keys as one item flow, resolves each to this node's triple, and writes one triples response, then completes the response side. The item stream flattens every request frame of the connection into one flow, so this serves ONE response per connection; the paired client sends exactly one keys frame, so a stale or wrong request cannot make the server fail.</summary>
    /// <param name="cancellationToken">The token that cancels the serve loop.</param>
    /// <returns>A task that completes when the requesting side ends and the reply has been flushed.</returns>
    /// <exception cref="InvalidDataException">The request wire violated the channel protocol (a hostile item count, an oversize frame, a truncated key, or trailing bytes) — the wire boundary normalizes it to the malformed-input type.</exception>
    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        ItemStreamChannelReader<ContentKey128> requestChannel = new(requestReader, ContentTripleFraming.DecodeKey, pool, ContentKey128.ByteWidth, maxFrameLength);
        MessageChannelWriter<IReadOnlyList<ContentTriple>> responseChannel = new(responseWriter, ContentTripleFraming.WriteTriples, maxFrameLength);
        List<ContentTriple> resolved = [];

        //The response side is completed in the finally so the peer's reader always observes the channel ending,
        //mirroring the sketch server: a serve that throws still releases the peer rather than stranding it.
        try
        {
            //Each key is resolved immediately into the response list; after the whole request flow ends, the one
            //response frame is written. The read drive's protocol violations are normalized to the malformed-input
            //type the requesting client (and its session) declines on.
            await requestChannel.ReadAllAsync((in ContentKey128 item) =>
            {
                if(sideMap.TryResolve(item, out EncodedTriple triple))
                {
                    resolved.Add(new ContentTriple(
                        dictionary.Resolve(triple.Subject.Encoded),
                        dictionary.Resolve(triple.Predicate.Encoded),
                        dictionary.Resolve(triple.Object.Encoded)));
                }
            }, cancellationToken).ConfigureAwait(false);

            await responseChannel.WriteAsync(resolved, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception exception) when(exception is InvalidOperationException or MessageDeserializationException)
        {
            throw new InvalidDataException("A content-hash triple-fetch request violated the channel protocol or could not be deserialized.", exception);
        }
        finally
        {
            await responseChannel.CompleteAsync().ConfigureAwait(false);
        }
    }
}
