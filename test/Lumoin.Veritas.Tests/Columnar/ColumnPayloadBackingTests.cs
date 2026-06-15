using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The native column-payload backing selected through the build: a native-backed index is
/// structurally byte-identical to its managed twin and decodes value-for-value across every
/// order and level, the policy knob reaches the columns, and Apply/compaction preserves the
/// backing so a native index stays native across commits.
/// </summary>
[TestClass]
internal sealed class ColumnPayloadBackingTests
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 2_000;

    /// <summary>Builds a corpus where each subject carries one predicate and <paramref name="fanOut"/> objects in its own id range.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fanOut">The objects per subject.</param>
    /// <param name="subjectBase">The first subject id, so disjoint batches do not collide.</param>
    /// <returns>The triple corpus.</returns>
    private static List<EncodedTriple> Corpus(int subjects, int fanOut, uint subjectBase = 0)
    {
        List<EncodedTriple> corpus = new(subjects * fanOut);
        for(int s = 0; s < subjects; s++)
        {
            long objectBase = 5_000_000L + ((long)(subjectBase + s) * fanOut * 4);
            for(int k = 0; k < fanOut; k++)
            {
                corpus.Add(EncodedTriple.FromEncoded(subjectBase + (uint)s, Predicate, (uint)(objectBase + (k * 4))));
            }
        }

        return corpus;
    }

    /// <summary>Decodes a packed column to a flat array, block by block.</summary>
    /// <param name="column">The packed column.</param>
    /// <returns>The decoded values.</returns>
    private static uint[] DecodeAll(BlockPackedColumn column)
    {
        uint[] decoded = new uint[column.Length];
        Span<uint> scratch = new uint[BlockPackedColumn.BlockLength];
        for(int block = 0; block < column.BlockCount; block++)
        {
            int count = column.BlockLengthOf(block);
            column.DecodeBlock(block, scratch);
            scratch[..count].CopyTo(decoded.AsSpan(block << BlockPackedColumn.BlockShift, count));
        }

        return decoded;
    }

    /// <summary>Asserts two packings of the same column agree on encoding, footprint, and decoded values.</summary>
    /// <param name="managed">The managed-backed column.</param>
    /// <param name="native">The native-backed column.</param>
    private static void AssertColumnsMatch(BlockPackedColumn managed, BlockPackedColumn native)
    {
        Assert.AreEqual(managed.Mode, native.Mode);
        Assert.AreEqual(managed.PackedByteCount, native.PackedByteCount);
        CollectionAssert.AreEqual(DecodeAll(managed), DecodeAll(native));
    }

    /// <summary>A native-backed index reports its backing, matches the managed index's triple count, and is column-for-column byte-identical and decode-identical across every order and level.</summary>
    [TestMethod]
    public void NativePolicyBuildsStructurallyIdenticalIndex()
    {
        List<EncodedTriple> corpus = Corpus(1_000, 8);
        ColumnarTripleIndex managed = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.AllSixOrders, backing: ColumnPayloadBacking.Managed);
        ColumnarTripleIndex native = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.AllSixOrders, backing: ColumnPayloadBacking.NativeAligned);

        Assert.AreEqual(ColumnPayloadBacking.Managed, managed.Backing);
        Assert.AreEqual(ColumnPayloadBacking.NativeAligned, native.Backing);
        Assert.AreEqual(managed.TripleCount, native.TripleCount);

        for(int permutation = 0; permutation < 6; permutation++)
        {
            ColumnarOrder managedOrder = managed.OrderAt(permutation);
            ColumnarOrder nativeOrder = native.OrderAt(permutation);

            for(int level = 0; level < 3; level++)
            {
                AssertColumnsMatch(managedOrder.ValuesColumnAt(level), nativeOrder.ValuesColumnAt(level));

                if(level < 2)
                {
                    AssertColumnsMatch(managedOrder.OffsetsColumnAt(level), nativeOrder.OffsetsColumnAt(level));
                }
            }
        }
    }

    /// <summary>An Apply that compacts a native index folds into a fresh base that is still native — the backing survives commits.</summary>
    [TestMethod]
    public void ApplyCompactionPreservesNativeBacking()
    {
        ColumnarTripleIndex native = ColumnarTripleIndex.Build(Corpus(1_000, 1), ColumnarOrderSetMode.AllSixOrders, backing: ColumnPayloadBacking.NativeAligned);

        //Adding a full base's worth crosses the compaction threshold, so Apply rebuilds the base.
        List<EncodedTriple> additions = Corpus(1_000, 1, subjectBase: 1_000);
        ColumnarTripleIndex evolved = native.Apply(additions, []);

        Assert.AreEqual(ColumnPayloadBacking.NativeAligned, evolved.Backing, "Native backing must survive Apply/compaction.");
        Assert.AreEqual(2_000, evolved.TripleCount);
    }
}
