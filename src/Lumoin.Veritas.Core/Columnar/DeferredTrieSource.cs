using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The deferred system-of-record build source: the recovered triples (an owned <see cref="DecodedItemSegment"/>)
/// plus the hash they index under, held so a <see cref="QueryEngineRendezvous"/> under
/// <see cref="HypertrieResidency.Deferred"/> can materialise the hypertrie ON DEMAND. The first query that genuinely
/// needs the trie — an access-controlled query, a per-pattern self-join, a cyclic shape without a self-index — pays
/// the build; a warm-loaded read generation that only ever serves columnar-capable shapes from its view never builds
/// the trie at all.
/// </summary>
/// <remarks>
/// The recovered triple count is available before the build (<see cref="Count"/>), so a selection trace event can
/// report it without the trie existing. This source OWNS the triple buffer: <see cref="Dispose"/> returns it to its
/// pool and is called once the build has consumed the triples.
/// </remarks>
public sealed class DeferredTrieSource: IDisposable
{
    /// <summary>The recovered system-of-record triples, owned by this source until <see cref="Dispose"/> returns them to their pool.</summary>
    private DecodedItemSegment Triples { get; }

    /// <summary>The hash the recovered triples index under — the SAME hash the eager trie would use, so a deferred build is byte-identical in structure to the eager one.</summary>
    private VeritasHash Hash { get; }

    /// <summary>The number of recovered triples — the trie's <see cref="HypertrieGraphStore.Count"/> once built, available before the build so a pre-materialisation trace event can carry it.</summary>
    public int Count { get; }

    //One once the buffer has been returned; a naked field because Interlocked.Exchange requires a ref parameter.
    private int disposed;

    /// <summary>Constructs a deferred source over the recovered triples and the hash they index under.</summary>
    /// <param name="triples">The recovered system-of-record triples; this source takes ownership and returns the buffer on <see cref="Dispose"/>.</param>
    /// <param name="hash">The hash to index the triples under; pass the same hash the eager build would use so the materialised trie is identical.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public DeferredTrieSource(DecodedItemSegment triples, VeritasHash hash)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(hash);

        Triples = triples;
        Hash = hash;
        Count = triples.Length;
    }

    /// <summary>
    /// Materialises the hypertrie system of record from the recovered triples — the deferred build. Built from ALL
    /// recovered triples under the source's hash, so the structure, term ids, and
    /// <see cref="HypertrieGraphStore.Match"/> membership and order are identical to the eager trie, and the
    /// per-candidate access-control consultation over it is therefore byte-identical to the eager path's.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the build at any per-step check.</param>
    /// <returns>The materialised system-of-record store.</returns>
    /// <exception cref="ObjectDisposedException">The source's buffer has already been returned to its pool.</exception>
    public ValueTask<HypertrieGraphStore> BuildAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        return HypertrieGraphStore.BuildAsync(MemoryMarshal.ToEnumerable<EncodedTriple>(Triples.Memory), Hash, cancellationToken);
    }

    /// <summary>Returns the recovered-triple buffer to its pool; idempotent. Called once the build has consumed the triples.</summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) == 0)
        {
            Triples.Dispose();
        }
    }
}
