using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core;

/// <summary>
/// An in-memory graph store using sorted arrays with three permutation indices
/// (SPO, POS, OSP) for efficient pattern matching via binary search.
/// </summary>
/// <remarks>
/// <para>
/// This is the default storage provider for small graphs and testing. Triples are
/// stored in three sorted arrays, each ordered by a different permutation of
/// (subject, predicate, object). A query selects the index whose first position
/// matches the most selective bound variable, performs binary search to find the
/// range, then linearly scans with filters for remaining positions.
/// </para>
/// <para>
/// The store is built once from a sequence of triples and is immutable after construction.
/// For mutable graphs, use a different provider or rebuild.
/// </para>
/// </remarks>
[DebuggerDisplay("InMemoryGraphStore Count={Count}")]
public sealed class InMemoryGraphStore
{
    /// <summary>
    /// Triples sorted by subject, predicate, object.
    /// </summary>
    private EncodedTriple[] Spo { get; }

    /// <summary>
    /// Triples sorted by predicate, object, subject.
    /// </summary>
    private EncodedTriple[] Pos { get; }

    /// <summary>
    /// Triples sorted by object, subject, predicate.
    /// </summary>
    private EncodedTriple[] Osp { get; }

    private InMemoryGraphStore(EncodedTriple[] spo, EncodedTriple[] pos, EncodedTriple[] osp)
    {
        Spo = spo;
        Pos = pos;
        Osp = osp;
    }

    /// <summary>
    /// Gets the number of triples in the store.
    /// </summary>
    public int Count => Spo.Length;

    /// <summary>
    /// Builds a new <see cref="InMemoryGraphStore"/> from the given triples.
    /// </summary>
    /// <param name="triples">The triples to index. Duplicates are removed.</param>
    /// <returns>A new immutable graph store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    public static InMemoryGraphStore Build(IEnumerable<EncodedTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(triples);
        EncodedTriple[] spoArray = triples.Distinct().ToArray();
        Array.Sort(spoArray, CompareSpo);

        EncodedTriple[] posArray = new EncodedTriple[spoArray.Length];
        spoArray.CopyTo(posArray, 0);
        Array.Sort(posArray, ComparePos);

        EncodedTriple[] ospArray = new EncodedTriple[spoArray.Length];
        spoArray.CopyTo(ospArray, 0);
        Array.Sort(ospArray, CompareOsp);

        return new InMemoryGraphStore(spoArray, posArray, ospArray);
    }

    /// <summary>
    /// Returns all triples matching the given pattern.
    /// </summary>
    /// <param name="subject">The subject to match, or <c>null</c> for any.</param>
    /// <param name="predicate">The predicate to match, or <c>null</c> for any.</param>
    /// <param name="object">The object to match, or <c>null</c> for any.</param>
    /// <returns>All matching triples.</returns>
    public IEnumerable<EncodedTriple> Match(TermId? subject, TermId? predicate, TermId? @object)
    {
        //Select the best index based on which positions are bound.
        if(subject.HasValue)
        {
            return ScanSpo(subject.Value, predicate, @object);
        }

        if(predicate.HasValue)
        {
            return ScanPos(predicate.Value, @object, subject);
        }

        if(@object.HasValue)
        {
            return ScanOsp(@object.Value, subject, predicate);
        }

        //All unbound — return everything.
        return Spo;
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.MatchTriplesAsync"/> delegate backed by this store.
    /// </summary>
    /// <returns>An async match delegate.</returns>
    public StorageDelegates.MatchTriplesAsync AsMatchDelegate()
    {
        return (s, p, o, ct) => ToAsyncEnumerable(Match(s, p, o), ct);
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.CountTriplesAsync"/> delegate backed by this store.
    /// </summary>
    /// <returns>An async count delegate.</returns>
    public StorageDelegates.CountTriplesAsync AsCountDelegate()
    {
        return _ => ValueTask.FromResult((long)Count);
    }

    private IEnumerable<EncodedTriple> ScanSpo(TermId subject, TermId? predicate, TermId? @object)
    {
        int start = LowerBound(Spo, subject, static t => t.Subject);
        for(int i = start; i < Spo.Length && Spo[i].Subject == subject; i++)
        {
            if(predicate.HasValue && Spo[i].Predicate != predicate.Value)
            {
                continue;
            }

            if(@object.HasValue && Spo[i].Object != @object.Value)
            {
                continue;
            }

            yield return Spo[i];
        }
    }

    private IEnumerable<EncodedTriple> ScanPos(TermId predicate, TermId? @object, TermId? subject)
    {
        int start = LowerBound(Pos, predicate, static t => t.Predicate);
        for(int i = start; i < Pos.Length && Pos[i].Predicate == predicate; i++)
        {
            if(@object.HasValue && Pos[i].Object != @object.Value)
            {
                continue;
            }

            if(subject.HasValue && Pos[i].Subject != subject.Value)
            {
                continue;
            }

            yield return Pos[i];
        }
    }

    private IEnumerable<EncodedTriple> ScanOsp(TermId @object, TermId? subject, TermId? predicate)
    {
        int start = LowerBound(Osp, @object, static t => t.Object);
        for(int i = start; i < Osp.Length && Osp[i].Object == @object; i++)
        {
            if(subject.HasValue && Osp[i].Subject != subject.Value)
            {
                continue;
            }

            if(predicate.HasValue && Osp[i].Predicate != predicate.Value)
            {
                continue;
            }

            yield return Osp[i];
        }
    }

    private static int LowerBound(EncodedTriple[] array, TermId value, Func<EncodedTriple, TermId> selector)
    {
        int lo = 0;
        int hi = array.Length;
        while(lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if(selector(array[mid]) < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static int CompareSpo(EncodedTriple a, EncodedTriple b)
    {
        int c = a.Subject.CompareTo(b.Subject);
        if(c != 0)
        {
            return c;
        }

        c = a.Predicate.CompareTo(b.Predicate);
        if(c != 0)
        {
            return c;
        }

        return a.Object.CompareTo(b.Object);
    }

    private static int ComparePos(EncodedTriple a, EncodedTriple b)
    {
        int c = a.Predicate.CompareTo(b.Predicate);
        if(c != 0)
        {
            return c;
        }

        c = a.Object.CompareTo(b.Object);
        if(c != 0)
        {
            return c;
        }

        return a.Subject.CompareTo(b.Subject);
    }

    private static int CompareOsp(EncodedTriple a, EncodedTriple b)
    {
        int c = a.Object.CompareTo(b.Object);
        if(c != 0)
        {
            return c;
        }

        c = a.Subject.CompareTo(b.Subject);
        if(c != 0)
        {
            return c;
        }

        return a.Predicate.CompareTo(b.Predicate);
    }

    private static async IAsyncEnumerable<EncodedTriple> ToAsyncEnumerable(
        IEnumerable<EncodedTriple> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(EncodedTriple triple in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return triple;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
