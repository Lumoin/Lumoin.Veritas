using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Collections.Internal.RoaringContainers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core.Collections;

/// <summary>
/// Correctness suite for <see cref="RoaringBitmap{TKey}"/>
/// instantiated at <see cref="uint"/>. Covers basic operations,
/// container-transition behaviour, set algebra, disposal,
/// allocation discipline on already-existing chunks, and a parity
/// run against <see cref="HashSet{T}"/> over a 100 000-operation
/// deterministic random sequence.
/// </summary>
[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "ApiDesign",
    "RS0030:Do not use banned APIs",
    Justification = "Seeded System.Random generates the deterministic 100 000-operation fixture sequence — not query randomness or identities. The entropy ban's purpose (injecting engine randomness via RandomnessDelegate) does not apply to synthetic test data.")]
internal sealed class RoaringBitmapTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void AddReturnsTrueForNewKey()
    {
        using RoaringBitmap<uint> bitmap = new();

        Assert.IsTrue(bitmap.Add(42));
    }

    [TestMethod]
    public void AddReturnsFalseForExistingKey()
    {
        using RoaringBitmap<uint> bitmap = new();
        bitmap.Add(42);

        Assert.IsFalse(bitmap.Add(42));
    }

    [TestMethod]
    public void ContainsReturnsFalseForMissingKey()
    {
        using RoaringBitmap<uint> bitmap = new();

        Assert.IsFalse(bitmap.Contains(42));
    }

    [TestMethod]
    public void ContainsReturnsTrueAfterAdd()
    {
        using RoaringBitmap<uint> bitmap = new();
        bitmap.Add(42);

        Assert.IsTrue(bitmap.Contains(42));
    }

    [TestMethod]
    public void RemoveReturnsFalseForMissingKey()
    {
        using RoaringBitmap<uint> bitmap = new();

        Assert.IsFalse(bitmap.Remove(42));
    }

    [TestMethod]
    public void RemoveReturnsTrueForPresentKey()
    {
        using RoaringBitmap<uint> bitmap = new();
        bitmap.Add(42);

        Assert.IsTrue(bitmap.Remove(42));
        Assert.IsFalse(bitmap.Contains(42));
    }

    [TestMethod]
    public void CountReflectsAddsAndRemoves()
    {
        using RoaringBitmap<uint> bitmap = new();

        Assert.AreEqual(0L, bitmap.Count);

        bitmap.Add(1);
        bitmap.Add(2);
        bitmap.Add(3);

        Assert.AreEqual(3L, bitmap.Count);

        bitmap.Add(2);

        Assert.AreEqual(3L, bitmap.Count);

        bitmap.Remove(2);

        Assert.AreEqual(2L, bitmap.Count);

        bitmap.Remove(99);

        Assert.AreEqual(2L, bitmap.Count);
    }

    [TestMethod]
    public void ClearEmptiesTheBitmap()
    {
        using RoaringBitmap<uint> bitmap = new();
        bitmap.Add(1);
        bitmap.Add(100_000);
        bitmap.Add(uint.MaxValue);

        bitmap.Clear();

        Assert.AreEqual(0L, bitmap.Count);
        Assert.IsFalse(bitmap.Contains(1));
        Assert.IsFalse(bitmap.Contains(100_000));
        Assert.IsFalse(bitmap.Contains(uint.MaxValue));
    }

    [TestMethod]
    public void ArrayContainerTransitionsToBitmapAtThreshold()
    {
        //For uint keys with half-width 16, array threshold is 4096.
        //Adding 4097 distinct low-half keys into one chunk must
        //trigger promotion to a BitmapContainer.
        using RoaringBitmap<uint> bitmap = new();
        for(uint i = 0; i <= 4096; i++)
        {
            bitmap.Add(i);
        }

        Assert.AreEqual(4097L, bitmap.Count);
        Container container = FirstChunkContainer(bitmap);
        Assert.IsInstanceOfType<BitmapContainer>(container);
    }

    [TestMethod]
    public void BitmapContainerTransitionsBackToArrayBelowThreshold()
    {
        using RoaringBitmap<uint> bitmap = new();
        for(uint i = 0; i <= 4096; i++)
        {
            bitmap.Add(i);
        }

        Assert.IsInstanceOfType<BitmapContainer>(FirstChunkContainer(bitmap));

        //Remove down to 4095 — strictly below the 4096 threshold —
        //and verify demotion to an ArrayContainer.
        for(uint i = 0; i <= 1; i++)
        {
            bitmap.Remove(i);
        }

        Assert.AreEqual(4095L, bitmap.Count);
        Assert.IsInstanceOfType<ArrayContainer>(FirstChunkContainer(bitmap));
    }

    [TestMethod]
    public void NoHysteresisOscillationOnThresholdCrossings()
    {
        //Add 4097 keys (promote to bitmap), remove one (demote
        //back to array), add it back (promote to bitmap again),
        //and so on for many cycles. The bitmap must stay
        //consistent through each crossing.
        using RoaringBitmap<uint> bitmap = new();
        for(uint i = 0; i <= 4096; i++)
        {
            bitmap.Add(i);
        }

        for(int cycle = 0; cycle < 100; cycle++)
        {
            bitmap.Remove(4096);
            Assert.AreEqual(4096L, bitmap.Count);
            Assert.IsFalse(bitmap.Contains(4096));

            bitmap.Add(4096);
            Assert.AreEqual(4097L, bitmap.Count);
            Assert.IsTrue(bitmap.Contains(4096));
        }
    }

    [TestMethod]
    public void KeysAcrossMultipleChunksWork()
    {
        //One key per high-half value at the chunk boundaries.
        using RoaringBitmap<uint> bitmap = new();
        uint[] keys =
        [
            0x0000_FFFF,
            0x0001_FFFF,
            0x0002_FFFF,
        ];
        foreach(uint key in keys)
        {
            bitmap.Add(key);
        }

        Assert.AreEqual(3L, bitmap.Count);
        Assert.AreEqual(3, bitmap.ChunkCount);
        foreach(uint key in keys)
        {
            Assert.IsTrue(bitmap.Contains(key));
        }

        bitmap.Remove(keys[1]);

        Assert.AreEqual(2L, bitmap.Count);
        Assert.AreEqual(2, bitmap.ChunkCount);
        Assert.IsFalse(bitmap.Contains(keys[1]));
    }

    [TestMethod]
    public void EmptyChunksAreNotRetained()
    {
        using RoaringBitmap<uint> bitmap = new();
        bitmap.Add(0x0001_0000);

        Assert.AreEqual(1, bitmap.ChunkCount);

        bitmap.Remove(0x0001_0000);

        Assert.AreEqual(0, bitmap.ChunkCount);
    }

    [TestMethod]
    public void UnionWithCombinesBothBitmaps()
    {
        using RoaringBitmap<uint> a = new();
        using RoaringBitmap<uint> b = new();
        foreach(uint x in (uint[])[1, 2, 3])
        {
            a.Add(x);
        }

        foreach(uint x in (uint[])[3, 4, 5])
        {
            b.Add(x);
        }

        a.UnionWith(b);

        Assert.AreEqual(5L, a.Count);
        foreach(uint x in (uint[])[1, 2, 3, 4, 5])
        {
            Assert.IsTrue(a.Contains(x));
        }
    }

    [TestMethod]
    public void UnionWithDisjointChunksMerges()
    {
        using RoaringBitmap<uint> a = new();
        using RoaringBitmap<uint> b = new();
        a.Add(1);
        b.Add(0x0001_0000);

        a.UnionWith(b);

        Assert.AreEqual(2L, a.Count);
        Assert.AreEqual(2, a.ChunkCount);
        Assert.IsTrue(a.Contains(1));
        Assert.IsTrue(a.Contains(0x0001_0000));
    }

    [TestMethod]
    public void IntersectWithReducesToCommon()
    {
        using RoaringBitmap<uint> a = new();
        using RoaringBitmap<uint> b = new();
        foreach(uint x in (uint[])[1, 2, 3])
        {
            a.Add(x);
        }

        foreach(uint x in (uint[])[2, 3, 4])
        {
            b.Add(x);
        }

        a.IntersectWith(b);

        Assert.AreEqual(2L, a.Count);
        Assert.IsTrue(a.Contains(2));
        Assert.IsTrue(a.Contains(3));
        Assert.IsFalse(a.Contains(1));
        Assert.IsFalse(a.Contains(4));
    }

    [TestMethod]
    public void IntersectWithDisjointChunksEmpties()
    {
        using RoaringBitmap<uint> a = new();
        using RoaringBitmap<uint> b = new();
        a.Add(1);
        b.Add(0x0001_0000);

        a.IntersectWith(b);

        Assert.AreEqual(0L, a.Count);
        Assert.AreEqual(0, a.ChunkCount);
    }

    [TestMethod]
    public void ExceptWithRemovesCommon()
    {
        using RoaringBitmap<uint> a = new();
        using RoaringBitmap<uint> b = new();
        foreach(uint x in (uint[])[1, 2, 3])
        {
            a.Add(x);
        }

        foreach(uint x in (uint[])[2, 4])
        {
            b.Add(x);
        }

        a.ExceptWith(b);

        Assert.AreEqual(2L, a.Count);
        Assert.IsTrue(a.Contains(1));
        Assert.IsTrue(a.Contains(3));
        Assert.IsFalse(a.Contains(2));
    }

    [TestMethod]
    public void SupportsKeyZero()
    {
        using RoaringBitmap<uint> bitmap = new();
        Assert.IsTrue(bitmap.Add(0));
        Assert.IsTrue(bitmap.Contains(0));
    }

    [TestMethod]
    public void SupportsMaxValue()
    {
        using RoaringBitmap<uint> bitmap = new();
        Assert.IsTrue(bitmap.Add(uint.MaxValue));
        Assert.IsTrue(bitmap.Contains(uint.MaxValue));
    }

    [TestMethod]
    public void HandlesFullKeyRange()
    {
        using RoaringBitmap<uint> bitmap = new();
        bitmap.Add(0);
        bitmap.Add(uint.MaxValue / 2);
        bitmap.Add(uint.MaxValue);

        Assert.IsTrue(bitmap.Contains(0));
        Assert.IsTrue(bitmap.Contains(uint.MaxValue / 2));
        Assert.IsTrue(bitmap.Contains(uint.MaxValue));
    }

    [TestMethod]
    public void EnumeratesKeysInAscendingOrder()
    {
        using RoaringBitmap<uint> bitmap = new();
        uint[] randomOrder = [100, 5, 0xFFFF_FFFF, 17, 0x0001_0000, 0];
        foreach(uint key in randomOrder)
        {
            bitmap.Add(key);
        }

        List<uint> observed = [];
        foreach(uint key in bitmap)
        {
            observed.Add(key);
        }

        uint[] expectedSorted = [0, 5, 17, 100, 0x0001_0000, 0xFFFF_FFFF];
        Assert.HasCount(expectedSorted.Length, observed);
        for(int i = 0; i < expectedSorted.Length; i++)
        {
            Assert.AreEqual(expectedSorted[i], observed[i]);
        }
    }

    [TestMethod]
    public void EnumeratesAcrossMultipleChunks()
    {
        using RoaringBitmap<uint> bitmap = new();
        for(uint chunk = 0; chunk < 4; chunk++)
        {
            uint baseValue = chunk << 16;
            bitmap.Add(baseValue + 0);
            bitmap.Add(baseValue + 100);
            bitmap.Add(baseValue + 0xFFFF);
        }

        long observed = 0;
        uint previous = 0;
        bool first = true;
        foreach(uint key in bitmap)
        {
            if(!first)
            {
                Assert.IsGreaterThan(previous, key, $"Keys not ascending: {previous} then {key}.");
            }

            previous = key;
            first = false;
            observed++;
        }

        Assert.AreEqual(12, observed);
    }

    [TestMethod]
    public void EnumeratorDoesNotAllocateOnEmptyBitmap()
    {
        using RoaringBitmap<uint> bitmap = new();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        foreach(uint _ in bitmap)
        {
            //Unreachable on an empty bitmap.
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        long delta = afterAlloc - beforeAlloc;

        Assert.IsLessThanOrEqualTo(1024L, delta);
    }

    [TestMethod]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Deterministic-seed System.Random is the right tool for a reproducible property-test sequence; no security boundary is involved.")]
    public void ParityWithHashSetOverRandomisedOperationSequence()
    {
        //Deterministic-seed randomised operations against both
        //RoaringBitmap<uint> and HashSet<uint>. Each operation's
        //return value (where applicable) and the post-operation
        //membership must match.
        const int operationCount = 100_000;
        const uint keyRange = 1_000_000;

        using RoaringBitmap<uint> bitmap = new();
        HashSet<uint> reference = [];

        //Deterministic seed; this is a property test, not a
        //security boundary.
        Random random = new(Seed: unchecked((int)0xDEADBEEF));

        for(int op = 0; op < operationCount; op++)
        {
            uint key = (uint)random.Next((int)keyRange);
            int kind = random.Next(3);

            switch(kind)
            {
                case 0:
                {
                    bool bitmapAdded = bitmap.Add(key);
                    bool referenceAdded = reference.Add(key);
                    Assert.AreEqual(referenceAdded, bitmapAdded, $"Add({key}) at op {op}: bitmap={bitmapAdded}, reference={referenceAdded}.");

                    break;
                }

                case 1:
                {
                    bool bitmapContains = bitmap.Contains(key);
                    bool referenceContains = reference.Contains(key);
                    Assert.AreEqual(referenceContains, bitmapContains, $"Contains({key}) at op {op}: bitmap={bitmapContains}, reference={referenceContains}.");

                    break;
                }

                case 2:
                {
                    bool bitmapRemoved = bitmap.Remove(key);
                    bool referenceRemoved = reference.Remove(key);
                    Assert.AreEqual(referenceRemoved, bitmapRemoved, $"Remove({key}) at op {op}: bitmap={bitmapRemoved}, reference={referenceRemoved}.");

                    break;
                }

                default:
                {
                    throw new InvalidOperationException();
                }
            }
        }

        Assert.AreEqual((long)reference.Count, bitmap.Count);

        //Final whole-set membership check across the union of
        //ever-touched keys.
        foreach(uint key in reference)
        {
            Assert.IsTrue(bitmap.Contains(key), $"Bitmap missing key {key} after the random run.");
        }
    }

    [TestMethod]
    public void DisposingBitmapDoesNotThrow()
    {
        RoaringBitmap<uint> bitmap = new();
        for(uint i = 0; i <= 5000; i++)
        {
            bitmap.Add(i);
        }

        bitmap.Dispose();
    }

    [TestMethod]
    public void DoubleDisposeIsSafe()
    {
        RoaringBitmap<uint> bitmap = new();
        bitmap.Add(42);

        bitmap.Dispose();
        bitmap.Dispose();
    }

    [TestMethod]
    public void AddDoesNotAllocateOnExistingChunks()
    {
        //Pre-warm a single chunk by promoting it to a
        //BitmapContainer (whose word array is a constant-size
        //pool rental). Subsequent adds into the same chunk
        //mutate the bitmap in place and allocate nothing.
        using RoaringBitmap<uint> bitmap = new();
        for(uint i = 0; i <= 4096; i++)
        {
            bitmap.Add(i);
        }

        Assert.IsInstanceOfType<BitmapContainer>(FirstChunkContainer(bitmap));

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        for(uint i = 4097; i < 5097; i++)
        {
            bitmap.Add(i);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        long delta = afterAlloc - beforeAlloc;

        Assert.IsLessThanOrEqualTo(1024L, delta);
    }

    private static Container FirstChunkContainer(RoaringBitmap<uint> bitmap)
    {
        foreach(uint key in bitmap)
        {
            return ContainerForKey(bitmap, key);
        }

        throw new InvalidOperationException("Bitmap is empty; cannot inspect first chunk.");
    }

    private static Container ContainerForKey(RoaringBitmap<uint> bitmap, uint key)
    {
        //Reflection access to the private SortedDictionary, used
        //by container-transition tests. Internal-visibility access
        //gives us the type; the dictionary itself is private.
        System.Reflection.PropertyInfo chunksProperty =
            typeof(RoaringBitmap<uint>).GetProperty(
                "Chunks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Chunks property not found on RoaringBitmap<uint>.");

        object value = chunksProperty.GetValue(bitmap)
            ?? throw new InvalidOperationException("Chunks property returned null.");
        System.Collections.IDictionary chunks = (System.Collections.IDictionary)value;

        //Split the key the same way RoaringBitmap does. For uint
        //keys the half-width is 16.
        ulong high = key >> 16;

        return (Container)(chunks[high]
            ?? throw new InvalidOperationException($"No container for high-key {high}."));
    }
}
