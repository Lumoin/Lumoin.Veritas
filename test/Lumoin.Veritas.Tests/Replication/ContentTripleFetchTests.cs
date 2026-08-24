using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The content-hash triple-fetch transport round-trips over a real pipe: the server resolves a requested item
/// through its side-map and decodes it through its dictionary into terms, the client reads them back, and the
/// terms re-hash to the requested key (so the wire serialization preserves the triple's content). A key the server
/// does not hold is dropped from the response.
/// </summary>
[TestClass]
internal sealed class ContentTripleFetchTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A named node for an IRI string.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Encodes a triple of named nodes against a dictionary.</summary>
    /// <param name="dictionary">The dictionary to encode against.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <param name="@object">The object.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermDictionary dictionary, NamedNode subject, NamedNode predicate, NamedNode @object)
    {
        return EncodedTriple.FromEncoded(dictionary.GetOrAdd((RdfTerm)subject).Encoded, dictionary.GetOrAdd((RdfTerm)predicate).Encoded, dictionary.GetOrAdd((RdfTerm)@object).Encoded);
    }

    /// <summary>A held key resolves to its triple over the wire and the terms re-hash to it; an unknown key is dropped.</summary>
    [TestMethod]
    public async Task ServesTriplesForHeldKeysAndSkipsUnknownOnes()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasHash hash = VeritasHashing.Default;
        TermDictionary dictionary = new();
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        EncodedTriple held = Encode(dictionary, subject, predicate, Iri("http://example.org/o1"));
        EncodedTriple alsoHeld = Encode(dictionary, subject, predicate, Iri("http://example.org/o2"));
        ColumnarTripleIndex index = ColumnarTripleIndex.Build([held, alsoHeld]);
        ContentHashReconciliationProjection projection = new(dictionary, hash, pool);
        ContentHashSideMap sideMap = ContentHashSideMap.Build(index, projection.Projection);

        //A key the side-map does not hold (a triple the dictionary can encode but the index never indexed).
        EncodedTriple absent = Encode(dictionary, subject, predicate, Iri("http://example.org/o3"));
        ContentKey128 heldKey = projection.Project(held);
        ContentKey128 absentKey = projection.Project(absent);

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ContentTripleFetchServer server = new(sideMap, dictionary, pool, requestPipe.Reader, responsePipe.Writer);
        Task serve = server.ServeAsync(TestContext.CancellationToken);
        ContentTripleFetchClient client = new(requestPipe.Writer, responsePipe.Reader, pool);

        //Each triple is borrowed for the handler call, so its key is re-hashed inside the handler (ProjectTerms reads
        //the borrowed spans and retains nothing) rather than the triple being stashed and read after the fetch.
        List<ContentKey128> resolvedKeys = [];
        await client.FetchAsync([heldKey, absentKey], (in ContentTriple triple) =>
            resolvedKeys.Add(projection.ProjectTerms(triple.Subject, triple.Predicate, triple.Object)), TestContext.CancellationToken).ConfigureAwait(false);
        await serve.ConfigureAwait(false);

        Assert.HasCount(1, resolvedKeys, "Only the held key resolves; the unknown key is skipped.");
        Assert.AreEqual(heldKey, resolvedKeys[0], "The fetched triple's terms re-hash to the requested key — the wire preserves the content.");
    }

    /// <summary>A hostile request frame declaring far more items than its bytes can hold is refused with an <see cref="InvalidDataException"/> before any array is sized from the count — not an out-of-memory fault.</summary>
    [TestMethod]
    public async Task RejectsAHostileItemCountWithoutAllocating()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasHash hash = VeritasHashing.Default;
        TermDictionary dictionary = new();
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        EncodedTriple one = Encode(dictionary, subject, predicate, Iri("http://example.org/o"));
        ColumnarTripleIndex index = ColumnarTripleIndex.Build([one]);
        ContentHashReconciliationProjection projection = new(dictionary, hash, pool);
        ContentHashSideMap sideMap = ContentHashSideMap.Build(index, projection.Projection);

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ContentTripleFetchServer server = new(sideMap, dictionary, pool, requestPipe.Reader, responsePipe.Writer);
        Task serve = server.ServeAsync(TestContext.CancellationToken);

        //A four-byte payload declaring int.MaxValue items and no item bytes — the small-frame, huge-count attack.
        MessageChannelWriter<int> hostile = new(requestPipe.Writer, static (count, output) =>
        {
            Span<byte> span = output.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32BigEndian(span, count);
            output.Advance(sizeof(int));
        }, MessageChannel.DefaultMaxFrameLength);
        await hostile.WriteAsync(int.MaxValue, TestContext.CancellationToken).ConfigureAwait(false);
        await hostile.CompleteAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await serve.ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>A response whose declared triple fits the per-item floor but whose first term's field length overruns the frame is refused through the pooled decode with an <see cref="InvalidDataException"/> — the malformed-input type the content-hash session declines on — so a hostile field length cannot force a read past the frame.</summary>
    [TestMethod]
    public async Task RejectsATripleWhoseFieldLengthOverrunsItsFrame()
    {
        using VeritasMemoryPool<byte> pool = new();
        Pipe requestPipe = new();
        Pipe responsePipe = new();

        //One triple (count = 1) whose subject IRI field claims 100 content bytes while only 10 are present. The
        //whole triple is 15 bytes (tag + length + 10 content), so it clears the per-item floor and the up-front
        //count check, then the pooled measure pass catches the field-length overrun.
        byte[] payload = new byte[4 + 1 + 4 + 10];
        BinaryPrimitives.WriteInt32BigEndian(payload, 1);
        payload[4] = (byte)'I';
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(5), 100);
        MessageChannelWriter<byte[]> hostile = new(responsePipe.Writer, static (bytes, output) => output.Write(bytes), MessageChannel.DefaultMaxFrameLength);
        await hostile.WriteAsync(payload, TestContext.CancellationToken).ConfigureAwait(false);
        await hostile.CompleteAsync().ConfigureAwait(false);

        ContentTripleFetchClient client = new(requestPipe.Writer, responsePipe.Reader, pool);
        ContentKey128 requested = new(1, 2);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await client.FetchAsync([requested], static (in ContentTriple _) => { }, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }
}
