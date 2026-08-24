using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core.Collections;

/// <summary>
/// Correctness suite for <see cref="RadixSort"/>. The headline
/// test is a parity run against <see cref="Array.Sort{T}(T[])"/>
/// over a randomised 100 000-element sequence; the remaining
/// tests cover edge cases (empty, single, already-sorted,
/// reverse-sorted, all-equal, boundary values) and the paired
/// keys-and-values variant.
/// </summary>
[TestClass]
[SuppressMessage(
    "ApiDesign",
    "RS0030:Do not use banned APIs",
    Justification = "Seeded System.Random generates deterministic, reproducible sort fixtures — not query randomness or identities. The entropy ban's purpose (injecting engine randomness via RandomnessDelegate) does not apply to synthetic test data.")]
internal sealed class RadixSortTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SortEmptyArrayIsNoOp()
    {
        uint[] keys = [];
        RadixSort.Sort(keys);

        Assert.IsEmpty(keys);
    }

    [TestMethod]
    public void SortSingleElementIsNoOp()
    {
        uint[] keys = [42];
        RadixSort.Sort(keys);

        Assert.HasCount(1, keys);
        Assert.AreEqual(42u, keys[0]);
    }

    [TestMethod]
    public void SortTwoElementsAscending()
    {
        uint[] keys = [1, 2];
        RadixSort.Sort(keys);

        Assert.AreEqual(1u, keys[0]);
        Assert.AreEqual(2u, keys[1]);
    }

    [TestMethod]
    public void SortTwoElementsReversed()
    {
        uint[] keys = [2, 1];
        RadixSort.Sort(keys);

        Assert.AreEqual(1u, keys[0]);
        Assert.AreEqual(2u, keys[1]);
    }

    [TestMethod]
    public void SortAlreadySortedRemainsSorted()
    {
        uint[] keys = new uint[100];
        for(int i = 0; i < keys.Length; i++)
        {
            keys[i] = (uint)(i * 7);
        }

        RadixSort.Sort(keys);

        for(int i = 0; i < keys.Length; i++)
        {
            Assert.AreEqual((uint)(i * 7), keys[i]);
        }
    }

    [TestMethod]
    public void SortReverseSortedAscends()
    {
        uint[] keys = new uint[100];
        for(int i = 0; i < keys.Length; i++)
        {
            keys[i] = (uint)(keys.Length - 1 - i);
        }

        RadixSort.Sort(keys);

        for(int i = 0; i < keys.Length; i++)
        {
            Assert.AreEqual((uint)i, keys[i]);
        }
    }

    [TestMethod]
    public void SortAllEqualLeavesValuesUntouched()
    {
        uint[] keys = new uint[50];
        Array.Fill(keys, 42u);

        RadixSort.Sort(keys);

        for(int i = 0; i < keys.Length; i++)
        {
            Assert.AreEqual(42u, keys[i]);
        }
    }

    [TestMethod]
    public void SortHandlesBoundaryValues()
    {
        uint[] keys = [uint.MaxValue, 0, 1, uint.MaxValue / 2];
        RadixSort.Sort(keys);

        Assert.AreEqual(0u, keys[0]);
        Assert.AreEqual(1u, keys[1]);
        Assert.AreEqual(uint.MaxValue / 2, keys[2]);
        Assert.AreEqual(uint.MaxValue, keys[3]);
    }

    [TestMethod]
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Deterministic-seed System.Random for a reproducible parity test; no security boundary.")]
    public void ParityWithArraySortOverRandomisedSequence()
    {
        const int size = 100_000;
        uint[] radixKeys = new uint[size];
        uint[] referenceKeys = new uint[size];
        Random random = new(Seed: unchecked((int)0xC0FFEE));

        for(int i = 0; i < size; i++)
        {
            uint key = (uint)random.Next();
            radixKeys[i] = key;
            referenceKeys[i] = key;
        }

        RadixSort.Sort(radixKeys);
        Array.Sort(referenceKeys);

        for(int i = 0; i < size; i++)
        {
            Assert.AreEqual(referenceKeys[i], radixKeys[i], $"Mismatch at index {i}.");
        }
    }

    [TestMethod]
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Deterministic-seed System.Random for a reproducible parity test; no security boundary.")]
    public void SortPairKeysAndValuesStaysInSync()
    {
        //Pair (key, value) where value = key * 7 + 1. After
        //sorting by key, the relationship must hold for every
        //index.
        const int size = 1_000;
        uint[] keys = new uint[size];
        uint[] values = new uint[size];
        Random random = new(Seed: 17);
        for(int i = 0; i < size; i++)
        {
            uint key = (uint)random.Next();
            keys[i] = key;
            values[i] = key * 7u + 1u;
        }

        RadixSort.Sort(keys, values);

        for(int i = 1; i < size; i++)
        {
            //IsLessThanOrEqualTo takes (upperBound, value)
            //and asserts value <= upperBound; this verifies
            //ascending order across consecutive keys.
            Assert.IsLessThanOrEqualTo(keys[i], keys[i - 1]);
        }

        for(int i = 0; i < size; i++)
        {
            Assert.AreEqual(keys[i] * 7u + 1u, values[i], $"Pair broken at index {i}.");
        }
    }

    [TestMethod]
    public void SortPairThrowsWhenLengthsDiffer()
    {
        uint[] keys = [1, 2, 3];
        uint[] values = [10, 20];

        Assert.Throws<ArgumentException>(() => RadixSort.Sort(keys, values));
    }

    [TestMethod]
    public void SortIsStableForEqualKeys()
    {
        //Two pairs share the same key. After a stable sort the
        //relative order of equal-keyed pairs must be preserved.
        uint[] keys = [3, 1, 3, 2, 1, 3];
        uint[] values = [100, 200, 300, 400, 500, 600];

        RadixSort.Sort(keys, values);

        //Expected order: keys (1, 1, 2, 3, 3, 3); the two 1s
        //keep input order (200 before 500); the three 3s keep
        //(100, 300, 600).
        uint[] expectedKeys = [1, 1, 2, 3, 3, 3];
        uint[] expectedValues = [200, 500, 400, 100, 300, 600];

        for(int i = 0; i < keys.Length; i++)
        {
            Assert.AreEqual(expectedKeys[i], keys[i]);
            Assert.AreEqual(expectedValues[i], values[i]);
        }
    }

    [TestMethod]
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Deterministic-seed System.Random for a reproducible parity test; no security boundary.")]
    public void SortUlongParityWithArraySort()
    {
        const int size = 10_000;
        ulong[] radixKeys = new ulong[size];
        ulong[] referenceKeys = new ulong[size];
        Random random = new(Seed: unchecked((int)0xCAFEBABE));

        for(int i = 0; i < size; i++)
        {
            ulong high = (ulong)(uint)random.Next();
            ulong low = (ulong)(uint)random.Next();
            ulong key = (high << 32) | low;
            radixKeys[i] = key;
            referenceKeys[i] = key;
        }

        RadixSort.Sort(radixKeys);
        Array.Sort(referenceKeys);

        for(int i = 0; i < size; i++)
        {
            Assert.AreEqual(referenceKeys[i], radixKeys[i], $"Mismatch at index {i}.");
        }
    }
}
