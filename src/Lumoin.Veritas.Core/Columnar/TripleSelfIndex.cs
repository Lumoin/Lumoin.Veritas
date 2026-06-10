using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A succinct self-index over a triple set: three Burrows-Wheeler columns —
/// one per cyclic rotation, each a <see cref="WaveletMatrix"/> — plus each
/// rotation's leader block boundaries as an <see cref="EliasFanoSequence"/>.
/// Any pattern, whatever subset of positions it binds and in whatever order
/// the bindings arrive, resolves to one contiguous row range
/// (<see cref="SelfIndexRange"/>) via backward and forward search steps, and
/// the seeks answer ordered candidate enumeration over the unbound positions
/// — every rotation's navigation from one structure instead of one
/// materialised table per order. Single-writer at build, then read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> Conceptually each rotation owns its sorted table — leader,
/// second, third position — but only the THIRD column is stored (the symbol
/// cyclically preceding the leader), as a wavelet matrix. A backward step
/// (<see cref="BindPreceding"/>) maps a range through that column's ranks into
/// the preceding rotation's table; a forward step (<see cref="BindFollowing"/>)
/// narrows a leader block to one second-position value through the following
/// rotation's column. Stability of the sorts makes both maps exact.
/// </para>
/// </remarks>
[DebuggerDisplay("TripleSelfIndex Count={Count} Bits={BitCount}")]
public sealed class TripleSelfIndex
{
    //The stored third-position column per rotation, wavelet-indexed:
    //objects for the subject-led rotation, predicates for the object-led,
    //subjects for the predicate-led.
    private readonly WaveletMatrix[] bwtColumns;

    //Per rotation: the leader's cumulative block starts — entry j is the
    //number of triples whose leader symbol is below j, the last entry the
    //triple count.
    private readonly EliasFanoSequence[] leaderBoundaries;

    /// <summary>The number of distinct triples indexed.</summary>
    public int Count { get; }

    /// <summary>The total footprint in bits across the three wavelet columns and the three boundary sequences.</summary>
    public long BitCount
    {
        get
        {
            long bits = 0;
            for(int rotation = 0; rotation < 3; rotation++)
            {
                bits += bwtColumns[rotation].BitCount + leaderBoundaries[rotation].BitCount;
            }

            return bits;
        }
    }

    private TripleSelfIndex(WaveletMatrix[] bwtColumns, EliasFanoSequence[] leaderBoundaries, int count)
    {
        this.bwtColumns = bwtColumns;
        this.leaderBoundaries = leaderBoundaries;
        Count = count;
    }

    /// <summary>Builds the self-index from a triple enumeration; duplicates are collapsed.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="selectSampleRate">The select-sample rate passed to the wavelet columns' bit-vectors.</param>
    /// <returns>The built index.</returns>
    /// <exception cref="ArgumentException">A term identifier is too large to index.</exception>
    public static TripleSelfIndex Build(IEnumerable<EncodedTriple> triples, int selectSampleRate = 512)
    {
        ArgumentNullException.ThrowIfNull(triples);

        HashSet<EncodedTriple> distinct = [.. triples];
        int count = distinct.Count;

        //The boundary sequences' lower payload is exactly the backend's lane
        //layout, so resolving the canonical kernel bundle once here and threading
        //it into every EliasFanoSequence.Build packs (and later decodes) those
        //boundaries on the hardware path while the succinct layer stays free of
        //this layer.
        ColumnarKernelBackend backend = ColumnarKernelBackend.Default;

        WaveletMatrix[] bwtColumns = new WaveletMatrix[3];
        EliasFanoSequence[] leaderBoundaries = new EliasFanoSequence[3];

        if(count == 0)
        {
            for(int rotation = 0; rotation < 3; rotation++)
            {
                bwtColumns[rotation] = WaveletMatrix.Build([]);
                leaderBoundaries[rotation] = EliasFanoSequence.Build([0u], lanePacker: backend.Pack.Invoke, laneUnpacker: backend.DecodeFrame.Invoke);
            }

            return new TripleSelfIndex(bwtColumns, leaderBoundaries, 0);
        }

        using IMemoryOwner<uint> subjectOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        using IMemoryOwner<uint> predicateOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        using IMemoryOwner<uint> objectOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        using IMemoryOwner<uint> permutationOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        using IMemoryOwner<uint> scratchOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        Span<uint> subjects = subjectOwner.Memory.Span[..count];
        Span<uint> predicates = predicateOwner.Memory.Span[..count];
        Span<uint> objects = objectOwner.Memory.Span[..count];
        Span<uint> permutation = permutationOwner.Memory.Span[..count];
        Span<uint> scratch = scratchOwner.Memory.Span[..count];

        uint maxId = 0;
        int row = 0;
        foreach(EncodedTriple triple in distinct)
        {
            subjects[row] = triple.Subject.Encoded;
            predicates[row] = triple.Predicate.Encoded;
            objects[row] = triple.Object.Encoded;
            maxId |= subjects[row] | predicates[row] | objects[row];
            row++;
        }

        if(maxId >= int.MaxValue - 1)
        {
            throw new ArgumentException("A term identifier is too large to index.", nameof(triples));
        }

        //Rotation order matches SelfIndexRotation: subject-led, object-led,
        //predicate-led. Each sorts by (leader, second, third) via three stable
        //radix passes from the third key inward, stores the third column as
        //the rotation's wavelet matrix, and the leader's cumulative block
        //starts as its boundary sequence.
        (bwtColumns[0], leaderBoundaries[0]) = BuildRotation(subjects, predicates, objects, permutation, scratch, selectSampleRate, backend);
        (bwtColumns[1], leaderBoundaries[1]) = BuildRotation(objects, subjects, predicates, permutation, scratch, selectSampleRate, backend);
        (bwtColumns[2], leaderBoundaries[2]) = BuildRotation(predicates, objects, subjects, permutation, scratch, selectSampleRate, backend);

        return new TripleSelfIndex(bwtColumns, leaderBoundaries, count);
    }

    /// <summary>Builds one rotation: sorts by (leader, second, third) via three stable radix passes from the third key inward, wavelet-indexes the sorted third column, and packs the leader's cumulative block starts.</summary>
    /// <param name="leader">The rotation's leading position column.</param>
    /// <param name="second">The second position column.</param>
    /// <param name="third">The third position column — the stored one.</param>
    /// <param name="permutation">A permutation buffer the sort orders.</param>
    /// <param name="scratch">A gather buffer at least the column length.</param>
    /// <param name="selectSampleRate">The select-sample rate for the wavelet column.</param>
    /// <param name="backend">The kernel bundle the boundary sequence packs (and later decodes) its lower payload with.</param>
    /// <returns>The rotation's wavelet column and boundary sequence.</returns>
    private static (WaveletMatrix Column, EliasFanoSequence Boundary) BuildRotation(
        Span<uint> leader,
        Span<uint> second,
        Span<uint> third,
        Span<uint> permutation,
        Span<uint> scratch,
        int selectSampleRate,
        ColumnarKernelBackend backend)
    {
        int count = permutation.Length;
        for(int i = 0; i < count; i++)
        {
            permutation[i] = (uint)i;
        }

        SortPass(third, permutation, scratch);
        SortPass(second, permutation, scratch);
        SortPass(leader, permutation, scratch);

        for(int i = 0; i < count; i++)
        {
            scratch[i] = third[(int)permutation[i]];
        }

        WaveletMatrix column = WaveletMatrix.Build(scratch, selectSampleRate: selectSampleRate);
        EliasFanoSequence boundary = BuildLeaderBoundary(leader, count, backend);

        return (column, boundary);
    }

    /// <summary>One stable radix pass: orders the permutation by the column's values, preserving the prior order among ties.</summary>
    /// <param name="column">The key column, addressed through the permutation.</param>
    /// <param name="permutation">The permutation to reorder.</param>
    /// <param name="scratch">A gather buffer at least the permutation's length.</param>
    private static void SortPass(Span<uint> column, Span<uint> permutation, Span<uint> scratch)
    {
        for(int i = 0; i < permutation.Length; i++)
        {
            scratch[i] = column[(int)permutation[i]];
        }

        RadixSort.Sort(scratch[..permutation.Length], permutation);
    }

    /// <summary>The leader's cumulative block starts: entry <c>j</c> counts the triples whose leader symbol is below <c>j</c>, through one entry past the largest leader.</summary>
    /// <param name="leader">The leader column.</param>
    /// <param name="count">The triple count.</param>
    /// <param name="backend">The kernel bundle the boundary sequence packs (and later decodes) its lower payload with.</param>
    /// <returns>The boundary sequence.</returns>
    private static EliasFanoSequence BuildLeaderBoundary(Span<uint> leader, int count, ColumnarKernelBackend backend)
    {
        uint maxLeader = 0;
        foreach(uint value in leader)
        {
            if(value > maxLeader)
            {
                maxLeader = value;
            }
        }

        int entries = (int)maxLeader + 2;
        using IMemoryOwner<uint> cumulativeOwner = VeritasMemoryPool<uint>.Shared.Rent(entries);
        Span<uint> cumulative = cumulativeOwner.Memory.Span[..entries];
        cumulative.Clear();
        foreach(uint value in leader)
        {
            cumulative[(int)value + 1]++;
        }

        uint running = 0;
        for(int i = 0; i < entries; i++)
        {
            running += cumulative[i];
            cumulative[i] = running;
        }

        Debug.Assert(running == (uint)count, "The cumulative boundary must end at the triple count.");

        return EliasFanoSequence.Build(cumulative, lanePacker: backend.Pack.Invoke, laneUnpacker: backend.DecodeFrame.Invoke);
    }

    /// <summary>The rotation a backward step lands in: the one led by the position cyclically preceding this rotation's leader.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The preceding rotation.</returns>
    private static SelfIndexRotation PrecedingRotation(SelfIndexRotation rotation) => rotation switch
    {
        SelfIndexRotation.SubjectPredicateObject => SelfIndexRotation.ObjectSubjectPredicate,
        SelfIndexRotation.ObjectSubjectPredicate => SelfIndexRotation.PredicateObjectSubject,
        _ => SelfIndexRotation.SubjectPredicateObject,
    };

    /// <summary>The rotation a forward step reads: the one led by this rotation's second position.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The following rotation.</returns>
    private static SelfIndexRotation FollowingRotation(SelfIndexRotation rotation) => rotation switch
    {
        SelfIndexRotation.SubjectPredicateObject => SelfIndexRotation.PredicateObjectSubject,
        SelfIndexRotation.PredicateObjectSubject => SelfIndexRotation.ObjectSubjectPredicate,
        _ => SelfIndexRotation.SubjectPredicateObject,
    };

    /// <summary>The largest leader symbol a rotation's boundary covers, or <c>-1</c> when the index is empty.</summary>
    /// <param name="boundary">The rotation's boundary sequence.</param>
    /// <returns>The largest covered leader symbol.</returns>
    private static long MaxLeader(EliasFanoSequence boundary)
    {
        return (long)boundary.Count - 2;
    }

    /// <summary>The leader symbol whose block holds a row position — the largest boundary index whose start is at or below the position.</summary>
    /// <param name="boundary">The rotation's boundary sequence.</param>
    /// <param name="position">The row position, below <see cref="Count"/>.</param>
    /// <returns>The leader symbol.</returns>
    private static TermId LeaderAt(EliasFanoSequence boundary, int position)
    {
        return new TermId((uint)(boundary.NextGEQ((uint)position + 1) - 1));
    }

    /// <summary>The whole table of a rotation — the range before any binding.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The full range.</returns>
    public SelfIndexRange FullRange(SelfIndexRotation rotation)
    {
        return new SelfIndexRange(rotation, 0, Count);
    }

    /// <summary>Binds a rotation's leader position to a symbol: the leader's whole block, empty when the symbol does not occur as that position.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <param name="symbol">The leader symbol.</param>
    /// <returns>The block range.</returns>
    public SelfIndexRange BindFirst(SelfIndexRotation rotation, TermId symbol)
    {
        EliasFanoSequence boundary = leaderBoundaries[(int)rotation];
        if(symbol.Encoded > MaxLeader(boundary))
        {
            return new SelfIndexRange(rotation, 0, 0);
        }

        int low = (int)boundary.Access((int)symbol.Encoded);
        int high = (int)boundary.Access((int)symbol.Encoded + 1);

        return new SelfIndexRange(rotation, low, high);
    }

    /// <summary>
    /// The backward search step: binds the position cyclically preceding the
    /// range's bound prefix to a symbol, landing in the preceding rotation's
    /// table with that symbol prepended to the prefix.
    /// </summary>
    /// <param name="range">The current range.</param>
    /// <param name="symbol">The symbol for the preceding position.</param>
    /// <returns>The narrowed range in the preceding rotation.</returns>
    public SelfIndexRange BindPreceding(SelfIndexRange range, TermId symbol)
    {
        SelfIndexRotation target = PrecedingRotation(range.Rotation);
        EliasFanoSequence boundary = leaderBoundaries[(int)target];
        if(range.IsEmpty || symbol.Encoded > MaxLeader(boundary))
        {
            return new SelfIndexRange(target, 0, 0);
        }

        WaveletMatrix column = bwtColumns[(int)range.Rotation];
        int blockStart = (int)boundary.Access((int)symbol.Encoded);
        int low = blockStart + column.Rank(symbol.Encoded, range.Low);
        int high = blockStart + column.Rank(symbol.Encoded, range.High);

        return new SelfIndexRange(target, low, high);
    }

    /// <summary>
    /// The forward search step: narrows a leader's whole block to the rows
    /// whose second position carries the given symbol — the contiguous
    /// sub-block the rotation's sort guarantees.
    /// </summary>
    /// <param name="range">The leader's block, exactly as <see cref="BindFirst"/> returned it.</param>
    /// <param name="firstSymbol">The bound leader symbol.</param>
    /// <param name="followingSymbol">The symbol for the second position.</param>
    /// <returns>The narrowed range, in the same rotation.</returns>
    /// <exception cref="ArgumentException"><paramref name="range"/> is not the leader's block.</exception>
    public SelfIndexRange BindFollowing(SelfIndexRange range, TermId firstSymbol, TermId followingSymbol)
    {
        if(range.IsEmpty)
        {
            return new SelfIndexRange(range.Rotation, range.Low, range.Low);
        }

        EliasFanoSequence ownBoundary = leaderBoundaries[(int)range.Rotation];
        if(firstSymbol.Encoded > MaxLeader(ownBoundary)
            || range.Low != (int)ownBoundary.Access((int)firstSymbol.Encoded)
            || range.High != (int)ownBoundary.Access((int)firstSymbol.Encoded + 1))
        {
            throw new ArgumentException("The range must be the bound leader's whole block.", nameof(range));
        }

        SelfIndexRotation following = FollowingRotation(range.Rotation);
        EliasFanoSequence boundary = leaderBoundaries[(int)following];
        if(followingSymbol.Encoded > MaxLeader(boundary))
        {
            return new SelfIndexRange(range.Rotation, range.Low, range.Low);
        }

        //The leader's occurrences in the following rotation's stored column
        //appear in this rotation's row order, so global ranks at the second
        //symbol's block edges are offsets into the leader's block.
        WaveletMatrix column = bwtColumns[(int)following];
        int blockLow = (int)boundary.Access((int)followingSymbol.Encoded);
        int blockHigh = (int)boundary.Access((int)followingSymbol.Encoded + 1);
        int localLow = column.Rank(firstSymbol.Encoded, blockLow);
        int localHigh = column.Rank(firstSymbol.Encoded, blockHigh);

        return new SelfIndexRange(range.Rotation, range.Low + localLow, range.Low + localHigh);
    }

    /// <summary>Seeks the smallest leader symbol at or above a target that leads a non-empty block.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <param name="target">The sought lower bound.</param>
    /// <param name="symbol">Receives the leader symbol when one exists.</param>
    /// <returns><see langword="true"/> when such a leader exists.</returns>
    public bool TrySeekFirst(SelfIndexRotation rotation, TermId target, out TermId symbol)
    {
        symbol = default;
        EliasFanoSequence boundary = leaderBoundaries[(int)rotation];
        if(target.Encoded > MaxLeader(boundary))
        {
            return false;
        }

        int position = (int)boundary.Access((int)target.Encoded);
        if(position >= Count)
        {
            return false;
        }

        symbol = LeaderAt(boundary, position);

        return true;
    }

    /// <summary>Seeks the smallest symbol at or above a target occurring at the range's preceding position — the candidates a backward step could bind.</summary>
    /// <param name="range">The current range.</param>
    /// <param name="target">The sought lower bound.</param>
    /// <param name="symbol">Receives the symbol when one exists.</param>
    /// <returns><see langword="true"/> when such a symbol occurs in the range.</returns>
    public bool TrySeekPreceding(SelfIndexRange range, TermId target, out TermId symbol)
    {
        symbol = default;
        if(!bwtColumns[(int)range.Rotation].TryRangeNextGEQ(range.Low, range.High, target.Encoded, out uint value))
        {
            return false;
        }

        symbol = new TermId(value);

        return true;
    }

    /// <summary>Seeks the smallest second-position symbol at or above a target within a bound leader's block — the candidates a forward step could bind.</summary>
    /// <param name="range">The leader's block, exactly as <see cref="BindFirst"/> returned it.</param>
    /// <param name="firstSymbol">The bound leader symbol.</param>
    /// <param name="target">The sought lower bound.</param>
    /// <param name="symbol">Receives the symbol when one exists.</param>
    /// <returns><see langword="true"/> when such a symbol exists under the leader.</returns>
    public bool TrySeekFollowing(SelfIndexRange range, TermId firstSymbol, TermId target, out TermId symbol)
    {
        symbol = default;
        if(range.IsEmpty)
        {
            return false;
        }

        SelfIndexRotation following = FollowingRotation(range.Rotation);
        EliasFanoSequence boundary = leaderBoundaries[(int)following];
        if(target.Encoded > MaxLeader(boundary))
        {
            return false;
        }

        //The next occurrence of the leader at or beyond the target's block
        //start names the smallest qualifying second symbol; its row maps back
        //to that symbol through the boundary.
        WaveletMatrix column = bwtColumns[(int)following];
        int from = (int)boundary.Access((int)target.Encoded);
        int occurrence = column.Rank(firstSymbol.Encoded, from);
        if(occurrence >= range.Length)
        {
            return false;
        }

        symbol = LeaderAt(boundary, column.Select(firstSymbol.Encoded, occurrence));

        return true;
    }
}
