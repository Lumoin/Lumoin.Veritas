using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EditCommitmentHashingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyDeltaReturnsBaseUnchanged()
    {
        //Folding zero edits over a base must leave the base alone:
        //the XOR start value is the base, no contributions to
        //combine, so the result equals the start. This degenerates
        //consistently with a non-empty commit whose adds and
        //removes cancel each other content-wise — both reduce to
        //"no observable transition," both produce the base id.
        NodeIdentifier baseId = new(0xCAFEBABE);

        NodeIdentifier commitment = EditCommitmentHashing.Compute(
            VeritasHashing.Default,
            baseId,
            additions: [],
            removals: []);

        Assert.AreEqual(baseId, commitment);
    }

    [TestMethod]
    public void EmptyDeltaOverEmptyBaseReturnsEmpty()
    {
        //The pure-degenerate case: empty initial build. The
        //resulting snapshot's id is NodeIdentifier.Empty, and the
        //commitment must agree.
        NodeIdentifier commitment = EditCommitmentHashing.Compute(
            VeritasHashing.Default,
            NodeIdentifier.Empty,
            additions: [],
            removals: []);

        Assert.AreEqual(NodeIdentifier.Empty, commitment);
    }

    [TestMethod]
    public void OrderIndependenceForAdditions()
    {
        //XOR is commutative; the same multiset of additions in two
        //different orders must produce the same commitment.
        NodeIdentifier baseId = new(0x1111);

        EncodedTriple t1 = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple t2 = EncodedTriple.FromEncoded(4, 5, 6);
        EncodedTriple t3 = EncodedTriple.FromEncoded(7, 8, 9);

        NodeIdentifier abc = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t1, t2, t3], removals: []);
        NodeIdentifier cba = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t3, t2, t1], removals: []);
        NodeIdentifier bac = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t2, t1, t3], removals: []);

        Assert.AreEqual(abc, cba);
        Assert.AreEqual(abc, bac);
    }

    [TestMethod]
    public void OrderIndependenceAcrossAdditionsAndRemovals()
    {
        //Adds and removes mix into the same XOR fold; their order
        //relative to each other must also not matter.
        NodeIdentifier baseId = new(0x2222);

        EncodedTriple add1 = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple add2 = EncodedTriple.FromEncoded(4, 5, 6);
        EncodedTriple rem1 = EncodedTriple.FromEncoded(7, 8, 9);

        NodeIdentifier addsFirst = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [add1, add2], [rem1]);
        NodeIdentifier addsSecond = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [add2, add1], [rem1]);

        Assert.AreEqual(addsFirst, addsSecond);
    }

    [TestMethod]
    public void AdditionAndRemovalOfSameTripleProduceDistinctCommitments()
    {
        //The kind byte (0x00 for adds, 0x01 for removes) is mixed
        //into the per-edit hash so an Add(t) and a Remove(t) with
        //the same triple produce different per-edit hashes and do
        //not cancel in the XOR fold. Without the kind byte they
        //would collide.
        NodeIdentifier baseId = new(0x3333);
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        NodeIdentifier asAddition = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [triple], removals: []);
        NodeIdentifier asRemoval = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, additions: [], removals: [triple]);

        Assert.AreNotEqual(asAddition, asRemoval);
    }

    [TestMethod]
    public void DifferentBasesProduceDifferentCommitments()
    {
        //The base id seeds the XOR fold; same edits over different
        //bases must produce different commitments. This is what
        //makes the commitment a "what was applied to which base"
        //fingerprint rather than just "what was applied."
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        NodeIdentifier overEmpty = EditCommitmentHashing.Compute(VeritasHashing.Default, NodeIdentifier.Empty, [triple], removals: []);
        NodeIdentifier overNonEmpty = EditCommitmentHashing.Compute(VeritasHashing.Default, new NodeIdentifier(0xAAAA), [triple], removals: []);

        Assert.AreNotEqual(overEmpty, overNonEmpty);
    }

    [TestMethod]
    public void DifferentTripleContentsProduceDifferentCommitments()
    {
        //Per-edit hashing must distinguish triples that differ in
        //any one position. A trivial contrapositive: changing the
        //subject changes the commitment.
        NodeIdentifier baseId = new(0x4444);
        EncodedTriple t1 = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple t2 = EncodedTriple.FromEncoded(99, 2, 3);

        NodeIdentifier first = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t1], removals: []);
        NodeIdentifier second = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t2], removals: []);

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void IdempotentCommitmentForRetryAgainstSameBase()
    {
        //The retry-idempotency property: a session that crashed
        //mid-commit and is replayed against the same base with the
        //same effective edits produces the same commitment. The
        //journal can use this to detect duplicates.
        NodeIdentifier baseId = new(0x5555);
        EncodedTriple t = EncodedTriple.FromEncoded(10, 20, 30);

        NodeIdentifier first = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t], removals: []);
        NodeIdentifier replay = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t], removals: []);

        Assert.AreEqual(first, replay);
    }

    [TestMethod]
    public void DifferentHashesProduceDifferentCommitments()
    {
        //The hash function is the single point of variability —
        //two different VeritasHash implementations must produce
        //different commitments over the same edits and base, or
        //else hash substitution would be a no-op and the audit
        //story would silently break.
        NodeIdentifier baseId = new(0x6666);
        EncodedTriple t = EncodedTriple.FromEncoded(11, 22, 33);

        NodeIdentifier underDefault = EditCommitmentHashing.Compute(VeritasHashing.Default, baseId, [t], removals: []);
        NodeIdentifier underIdentity = EditCommitmentHashing.Compute(IdentityHash, baseId, [t], removals: []);

        Assert.AreNotEqual(underDefault, underIdentity);
    }

    //A deterministic, no-op-style hash: returns the bytewise sum
    //of the input span. Provides a minimal counter-hash to confirm
    //hash substitution actually changes the commitment.
    private static ulong IdentityHash(ReadOnlySpan<byte> bytes)
    {
        ulong sum = 0;
        for(int i = 0; i < bytes.Length; i++)
        {
            sum += bytes[i];
        }
        return sum;
    }
}
