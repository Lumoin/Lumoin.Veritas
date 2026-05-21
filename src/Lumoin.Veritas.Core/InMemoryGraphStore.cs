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

        HashSet<EncodedTriple> distinct = new(triples);
        EncodedTriple[] spoArray = new EncodedTriple[distinct.Count];
        distinct.CopyTo(spoArray);
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
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any.</param>
    /// <returns>All matching triples.</returns>
    /// <remarks>
    /// <para>
    /// <b>Unbound positions.</b> A position parameter of
    /// <see cref="TermId.None"/> means "match any value at this
    /// position." Since <see cref="TermId.None"/> equals
    /// <c>default(TermId)</c>, position parameters that default to
    /// <c>default</c> are unbound by construction.
    /// </para>
    /// </remarks>
    public IEnumerable<EncodedTriple> Match(TermId subject, TermId predicate, TermId @object)
    {
        //Select the best index based on which positions are bound.
        if(!subject.IsNone)
        {
            return ScanSpo(subject, predicate, @object);
        }

        if(!predicate.IsNone)
        {
            return ScanPos(predicate, @object, subject);
        }

        if(!@object.IsNone)
        {
            return ScanOsp(@object, subject, predicate);
        }

        //All unbound — return everything.
        return Spo;
    }

    /// <summary>
    /// Returns the cross-product of <paramref name="subjects"/> with a
    /// bound <paramref name="predicate"/>, optionally constrained by a
    /// bound <paramref name="object"/>. The POS index is descended once
    /// for the predicate; every triple in that predicate range is then
    /// filtered by a hashed subject-set membership test.
    /// </summary>
    /// <param name="subjects">The encoded subject identifiers. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <param name="predicate">The bound predicate to match.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any.</param>
    /// <returns>Matching triples; output ordering is unspecified.</returns>
    /// <exception cref="ArgumentException"><paramref name="predicate"/> is <see cref="TermId.None"/>, or <paramref name="subjects"/> contains <see cref="TermId.None"/>.</exception>
    public IEnumerable<EncodedTriple> MatchBySubjects(
        ReadOnlyMemory<TermId> subjects,
        TermId predicate,
        TermId @object)
    {
        if(predicate.IsNone)
        {
            throw new ArgumentException(
                "Predicate must be bound for MatchBySubjects. Use Match with an unbound predicate instead.",
                nameof(predicate));
        }

        if(subjects.IsEmpty)
        {
            return [];
        }

        //Eager validation so the call site (not the consumer's foreach)
        //sees the invariant failure.
        ReadOnlySpan<TermId> span = subjects.Span;
        for(int i = 0; i < span.Length; i++)
        {
            if(span[i].IsNone)
            {
                throw new ArgumentException(
                    "Subject set must not contain TermId.None. None means 'unbound' and has no meaning as a set member.",
                    nameof(subjects));
            }
        }

        HashSet<TermId> subjectSet = new(subjects.Length);
        for(int i = 0; i < span.Length; i++)
        {
            subjectSet.Add(span[i]);
        }

        return ScanPosBySubjectSet(predicate, @object, subjectSet);
    }

    /// <summary>
    /// Mirror of <see cref="MatchBySubjects"/> across the object position:
    /// returns the cross-product of a bound <paramref name="predicate"/>
    /// with <paramref name="objects"/>, optionally constrained by a bound
    /// <paramref name="subject"/>.
    /// </summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="predicate">The bound predicate to match.</param>
    /// <param name="objects">The encoded object identifiers. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <returns>Matching triples; output ordering is unspecified.</returns>
    /// <exception cref="ArgumentException"><paramref name="predicate"/> is <see cref="TermId.None"/>, or <paramref name="objects"/> contains <see cref="TermId.None"/>.</exception>
    public IEnumerable<EncodedTriple> MatchByObjects(
        TermId subject,
        TermId predicate,
        ReadOnlyMemory<TermId> objects)
    {
        if(predicate.IsNone)
        {
            throw new ArgumentException(
                "Predicate must be bound for MatchByObjects. Use Match with an unbound predicate instead.",
                nameof(predicate));
        }

        if(objects.IsEmpty)
        {
            return [];
        }

        ReadOnlySpan<TermId> span = objects.Span;
        for(int i = 0; i < span.Length; i++)
        {
            if(span[i].IsNone)
            {
                throw new ArgumentException(
                    "Object set must not contain TermId.None. None means 'unbound' and has no meaning as a set member.",
                    nameof(objects));
            }
        }

        HashSet<TermId> objectSet = new(objects.Length);
        for(int i = 0; i < span.Length; i++)
        {
            objectSet.Add(span[i]);
        }

        return ScanPosByObjectSet(subject, predicate, objectSet);
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
    /// Creates a <see cref="StorageDelegates.MatchTriplesBySubjectsAsync"/>
    /// delegate backed by this store.
    /// </summary>
    public StorageDelegates.MatchTriplesBySubjectsAsync AsMatchBySubjectsDelegate()
    {
        return (subjects, p, o, ct) => ToAsyncEnumerable(MatchBySubjects(subjects, p, o), ct);
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.MatchTriplesByObjectsAsync"/>
    /// delegate backed by this store.
    /// </summary>
    public StorageDelegates.MatchTriplesByObjectsAsync AsMatchByObjectsDelegate()
    {
        return (s, p, objects, ct) => ToAsyncEnumerable(MatchByObjects(s, p, objects), ct);
    }

    /// <summary>
    /// Bundles the three match delegates into a <see cref="GraphMatchOps"/>
    /// for callers — such as
    /// <see cref="Lumoin.Veritas.Rdf.PropertyPathEvaluator"/> — that need
    /// all three forms.
    /// </summary>
    public GraphMatchOps AsMatchOps()
    {
        return new GraphMatchOps(
            AsMatchDelegate(),
            AsMatchBySubjectsDelegate(),
            AsMatchByObjectsDelegate());
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.CountTriplesAsync"/> delegate backed by this store.
    /// </summary>
    /// <returns>An async count delegate.</returns>
    public StorageDelegates.CountTriplesAsync AsCountDelegate()
    {
        return _ => ValueTask.FromResult((long)Count);
    }

    //The first parameter is the bound index key (must be a
    //concrete non-TermId.None value — the scan is meaningless
    //otherwise). The remaining parameters may be TermId.None for
    //unbound positions.
    private IEnumerable<EncodedTriple> ScanSpo(TermId subject, TermId predicate, TermId @object)
    {
        int start = LowerBound(Spo, subject, static t => t.Subject);
        for(int i = start; i < Spo.Length && Spo[i].Subject == subject; i++)
        {
            if(!predicate.IsNone && Spo[i].Predicate != predicate)
            {
                continue;
            }

            if(!@object.IsNone && Spo[i].Object != @object)
            {
                continue;
            }

            yield return Spo[i];
        }
    }

    private IEnumerable<EncodedTriple> ScanPos(TermId predicate, TermId @object, TermId subject)
    {
        int start = LowerBound(Pos, predicate, static t => t.Predicate);
        for(int i = start; i < Pos.Length && Pos[i].Predicate == predicate; i++)
        {
            if(!@object.IsNone && Pos[i].Object != @object)
            {
                continue;
            }

            if(!subject.IsNone && Pos[i].Subject != subject)
            {
                continue;
            }

            yield return Pos[i];
        }
    }

    //Predicate-rooted scan filtered by a hashed subject set. The
    //caller is responsible for predicate-bound and subject-set-nonempty
    //validation; this helper assumes both. The optional bound object
    //narrows the row further.
    private IEnumerable<EncodedTriple> ScanPosBySubjectSet(TermId predicate, TermId @object, HashSet<TermId> subjectSet)
    {
        int start = LowerBound(Pos, predicate, static t => t.Predicate);
        for(int i = start; i < Pos.Length && Pos[i].Predicate == predicate; i++)
        {
            if(!@object.IsNone && Pos[i].Object != @object)
            {
                continue;
            }

            if(!subjectSet.Contains(Pos[i].Subject))
            {
                continue;
            }

            yield return Pos[i];
        }
    }

    //Mirror of ScanPosBySubjectSet across the object position.
    private IEnumerable<EncodedTriple> ScanPosByObjectSet(TermId subject, TermId predicate, HashSet<TermId> objectSet)
    {
        int start = LowerBound(Pos, predicate, static t => t.Predicate);
        for(int i = start; i < Pos.Length && Pos[i].Predicate == predicate; i++)
        {
            if(!subject.IsNone && Pos[i].Subject != subject)
            {
                continue;
            }

            if(!objectSet.Contains(Pos[i].Object))
            {
                continue;
            }

            yield return Pos[i];
        }
    }

    private IEnumerable<EncodedTriple> ScanOsp(TermId @object, TermId subject, TermId predicate)
    {
        int start = LowerBound(Osp, @object, static t => t.Object);
        for(int i = start; i < Osp.Length && Osp[i].Object == @object; i++)
        {
            if(!subject.IsNone && Osp[i].Subject != subject)
            {
                continue;
            }

            if(!predicate.IsNone && Osp[i].Predicate != predicate)
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
