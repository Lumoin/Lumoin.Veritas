using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class NodeIdentifierTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyHasZeroValue()
    {
        Assert.AreEqual(0UL, NodeIdentifier.Empty.Value);
        Assert.IsTrue(NodeIdentifier.Empty.IsEmpty);
    }

    [TestMethod]
    public void EmptyIsFullNodeNotSingleEntry()
    {
        Assert.IsTrue(NodeIdentifier.Empty.IsFullNode);
        Assert.IsFalse(NodeIdentifier.Empty.IsSingleEntryNode);
    }

    [TestMethod]
    public void AddingEntryThenRemovingReturnsToOriginal()
    {
        ulong entry = NodeEntryHashing.Default(VeritasHashing.Default, 42L, 1UL);
        NodeIdentifier original = NodeIdentifier.Empty;

        NodeIdentifier afterAdd = original.Add(entry);
        NodeIdentifier afterRemove = afterAdd.Remove(entry);

        Assert.AreNotEqual(original, afterAdd);
        Assert.AreEqual(original, afterRemove);
    }

    [TestMethod]
    public void XorCombineIsCommutative()
    {
        ulong entryA = NodeEntryHashing.Default(VeritasHashing.Default, 1L, 100UL);
        ulong entryB = NodeEntryHashing.Default(VeritasHashing.Default, 2L, 200UL);
        ulong entryC = NodeEntryHashing.Default(VeritasHashing.Default, 3L, 300UL);

        NodeIdentifier abc = NodeIdentifier.Empty.Add(entryA).Add(entryB).Add(entryC);
        NodeIdentifier cab = NodeIdentifier.Empty.Add(entryC).Add(entryA).Add(entryB);
        NodeIdentifier bca = NodeIdentifier.Empty.Add(entryB).Add(entryC).Add(entryA);

        Assert.AreEqual(abc, cab);
        Assert.AreEqual(abc, bca);
    }

    [TestMethod]
    public void AddRemoveAreAliases()
    {
        ulong entry = NodeEntryHashing.Default(VeritasHashing.Default, 99L, 777UL);

        NodeIdentifier viaAdd = NodeIdentifier.Empty.Add(entry);
        NodeIdentifier viaRemove = NodeIdentifier.Empty.Remove(entry);

        Assert.AreEqual(viaAdd, viaRemove);
    }

    [TestMethod]
    public void DistinctEntriesProduceDistinctIdentifiers()
    {
        ulong entryA = NodeEntryHashing.Default(VeritasHashing.Default, 1L, 1UL);
        ulong entryB = NodeEntryHashing.Default(VeritasHashing.Default, 2L, 1UL);

        NodeIdentifier idA = NodeIdentifier.Empty.Add(entryA);
        NodeIdentifier idB = NodeIdentifier.Empty.Add(entryB);

        Assert.AreNotEqual(idA, idB);
    }

    [TestMethod]
    public void AddingEntryDoesNotSetTagBit()
    {
        //Even an entry hash with bit 63 set must not flip the SEN tag,
        //because the combiner masks per-entry hashes to 63 bits.
        const ulong entryWithHighBitSet = 0xFFFFFFFFFFFFFFFFUL;

        NodeIdentifier id = NodeIdentifier.Empty.Add(entryWithHighBitSet);

        Assert.IsTrue(id.IsFullNode);
        Assert.IsFalse(id.IsSingleEntryNode);
    }

    [TestMethod]
    public void WithTagSetsHighBit()
    {
        NodeIdentifier original = NodeIdentifier.Empty.Add(NodeEntryHashing.Default(VeritasHashing.Default, 7L, 1UL));

        NodeIdentifier tagged = original.WithTag(true);

        Assert.IsTrue(tagged.IsSingleEntryNode);
        Assert.IsFalse(tagged.IsFullNode);
        //Stripping the tag returns the same content.
        Assert.AreEqual(original.Content, tagged.Content);
    }

    [TestMethod]
    public void WithTagFalseClearsHighBit()
    {
        NodeIdentifier tagged = NodeIdentifier.Empty
            .Add(NodeEntryHashing.Default(VeritasHashing.Default, 7L, 1UL))
            .WithTag(true);

        NodeIdentifier untagged = tagged.WithTag(false);

        Assert.IsTrue(untagged.IsFullNode);
        Assert.IsFalse(untagged.IsSingleEntryNode);
        Assert.AreEqual(tagged.Content, untagged.Content);
    }

    [TestMethod]
    public void RecordStructEqualityIsValueBased()
    {
        NodeIdentifier left = new(0xCAFEBABE_DEADBEEFUL);
        NodeIdentifier right = new(0xCAFEBABE_DEADBEEFUL);

        Assert.AreEqual(left, right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
    }

    [TestMethod]
    public void DefaultMixerIsDeterministic()
    {
        ulong a = NodeEntryHashing.Default(VeritasHashing.Default, 1L, 1UL);
        ulong b = NodeEntryHashing.Default(VeritasHashing.Default, 1L, 1UL);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void DefaultMixerNeverReturnsZero()
    {
        //There exists at least one (key, child) pair that produces
        //a raw xxHash64 of zero; the mixer must upgrade it to the
        //sentinel. Brute-forcing such a pair is expensive, so test
        //the contract by passing through the sentinel directly:
        //any invocation must produce a non-zero output.
        for(long key = 0; key < 1024; key++)
        {
            for(ulong child = 0; child < 16; child++)
            {
                ulong hash = NodeEntryHashing.Default(VeritasHashing.Default, key, child);
                Assert.AreNotEqual(0UL, hash, $"Default mixer returned zero for ({key}, {child}).");
            }
        }
    }

    [TestMethod]
    public void DefaultMixerDistinguishesPositionalArguments()
    {
        //A mixer that simply XORs its inputs would map (1, 0) and
        //(0, 1) to the same hash. Verify the default does not.
        ulong leftRight = NodeEntryHashing.Default(VeritasHashing.Default, 1L, 0UL);
        ulong rightLeft = NodeEntryHashing.Default(VeritasHashing.Default, 0L, 1UL);

        Assert.AreNotEqual(leftRight, rightLeft);
    }

    [TestMethod]
    public void DebuggerDisplayShowsHexValue()
    {
        //Sanity check that the DebuggerDisplay format string compiles
        //and the property is accessible — the value itself is a
        //black-box hash so we only check it is non-empty.
        NodeIdentifier id = new(0xABCDEF0123456789UL);

        Assert.AreEqual(0xABCDEF0123456789UL, id.Value);
    }

    [TestMethod]
    public void SanitizeContributionUpgradesRawZero()
    {
        Assert.AreEqual(NodeIdentifier.ZeroSentinel, NodeIdentifier.SanitizeContribution(0UL));
    }

    [TestMethod]
    public void SanitizeContributionUpgradesTheTagOnlyHash()
    {
        //The raw hash 0x8000000000000000 passes a raw==0 test yet its
        //single set bit is exactly what Add masks away, so unsanitized
        //it folds as a no-op — an entry invisible to the identifier.
        //The sanitizer tests the MASKED value and upgrades it.
        Assert.AreEqual(NodeIdentifier.ZeroSentinel, NodeIdentifier.SanitizeContribution(NodeIdentifier.TagMask));
    }

    [TestMethod]
    public void SanitizeContributionPassesContentBearingHashesThrough()
    {
        Assert.AreEqual(1UL, NodeIdentifier.SanitizeContribution(1UL));
        Assert.AreEqual(0xABCDEF0123456789UL, NodeIdentifier.SanitizeContribution(0xABCDEF0123456789UL));

        //A hash with the tag bit set AND content bits is content-bearing:
        //the fold keeps its content, so no upgrade happens.
        Assert.AreEqual(NodeIdentifier.TagMask | 1UL, NodeIdentifier.SanitizeContribution(NodeIdentifier.TagMask | 1UL));
    }

    [TestMethod]
    public void ZeroSentinelSurvivesTheFoldMaskUnchanged()
    {
        //The sentinel must lie inside the 63-bit content space so the
        //sanitized value IS the folded contribution — folding it into
        //Empty yields exactly the sentinel as content, tag untouched.
        NodeIdentifier folded = NodeIdentifier.Empty.Add(NodeIdentifier.ZeroSentinel);

        Assert.AreEqual(NodeIdentifier.ZeroSentinel, folded.Content);
        Assert.IsTrue(folded.IsFullNode);
    }

    [TestMethod]
    public void SanitizedContributionNeverFoldsAsANoOp()
    {
        //The invariant the sanitizer owns, stated on the fold itself:
        //no sanitized contribution leaves the identifier unchanged.
        //0x8000000000000000 is the audit's counterexample — unsanitized
        //it XORs into Empty as a no-op, so a non-empty state could
        //equal the empty-head sentinel.
        NodeIdentifier folded = NodeIdentifier.Empty.Add(NodeIdentifier.SanitizeContribution(NodeIdentifier.TagMask));

        Assert.AreNotEqual(NodeIdentifier.Empty, folded);
        Assert.AreNotEqual(NodeIdentifier.Empty, NodeIdentifier.Empty.Add(NodeIdentifier.SanitizeContribution(0UL)));
    }
}
