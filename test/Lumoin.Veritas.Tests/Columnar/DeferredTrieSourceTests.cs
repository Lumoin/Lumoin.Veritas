using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// Tests for <see cref="DeferredTrieSource"/>: the recovered-triple count is available before the build, the build
/// materialises a hypertrie holding exactly the recovered triples, and the owned buffer is released idempotently —
/// the deferred system-of-record build the warm serve-from-disk start defers until a query needs the trie.
/// </summary>
[TestClass]
internal sealed class DeferredTrieSourceTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The recovered count is exposed before any build, so a pre-materialisation trace event can carry it, and the build materialises exactly the recovered triples.</summary>
    [TestMethod]
    public async Task BuildMaterialisesTheRecoveredTriplesAndCountPrecedesTheBuild()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(2, 100, 3),
            EncodedTriple.FromEncoded(3, 100, 1),
        ];

        using VeritasMemoryPool<EncodedTriple> pool = new();
        DeferredTrieSource source = CreateSource(pool, triples);

        Assert.AreEqual(triples.Length, source.Count, "The recovered count is available before the build.");

        HypertrieGraphStore store = await source.BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(triples.Length, store.Count, "The materialised trie holds every recovered triple.");
        HashSet<EncodedTriple> materialised = [.. store.Match(TermId.None, TermId.None, TermId.None)];
        foreach(EncodedTriple triple in triples)
        {
            Assert.Contains(triple, materialised, "Every recovered triple is present in the materialised trie.");
        }

        source.Dispose();
    }

    /// <summary>Disposing the source returns its buffer; a build after disposal is refused rather than reading a returned buffer.</summary>
    [TestMethod]
    public async Task BuildAfterDisposeIsRefused()
    {
        EncodedTriple[] triples = [EncodedTriple.FromEncoded(1, 100, 2)];

        using VeritasMemoryPool<EncodedTriple> pool = new();
        DeferredTrieSource source = CreateSource(pool, triples);

        source.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await source.BuildAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>Dispose is idempotent — a second disposal does not double-return the buffer.</summary>
    [TestMethod]
    public void DisposeIsIdempotent()
    {
        EncodedTriple[] triples = [EncodedTriple.FromEncoded(1, 100, 2)];

        using VeritasMemoryPool<EncodedTriple> pool = new();
        DeferredTrieSource source = CreateSource(pool, triples);

        source.Dispose();
        source.Dispose();
    }

    /// <summary>Wraps the triples in a pooled <see cref="DecodedItemSegment"/> the source takes ownership of.</summary>
    /// <param name="pool">The pool the buffer is rented from; outlives the source so the post-build return lands in a live pool.</param>
    /// <param name="triples">The triples the recovered segment carries.</param>
    /// <returns>A deferred source over the recovered triples.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The segment's ownership transfers to the returned DeferredTrieSource, which each test disposes (or which returns the buffer on build).")]
    private static DeferredTrieSource CreateSource(VeritasMemoryPool<EncodedTriple> pool, EncodedTriple[] triples)
    {
        IMemoryOwner<EncodedTriple> owner = pool.Rent(triples.Length);
        triples.CopyTo(owner.Memory.Span);

        return new DeferredTrieSource(new DecodedItemSegment(owner, triples.Length), VeritasHashing.Default);
    }
}
