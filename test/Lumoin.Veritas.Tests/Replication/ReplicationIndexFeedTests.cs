using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The reconciliation-index feed seeds from a replica's committed triples and advances by each commit's effective
/// delta, tracking the dataset StateId; a generation read before an advance is unaffected by it (the immutability
/// that makes per-connection sketch serving safe).
/// </summary>
[TestClass]
internal sealed class ReplicationIndexFeedTests
{
    /// <summary>Seeding then advancing applies the delta and records the new generation, and a prior generation snapshot is unaffected.</summary>
    [TestMethod]
    public void SeedsThenAdvancesByDeltaLeavingPriorGenerationsIntact()
    {
        EncodedTriple t1 = EncodedTriple.FromEncoded(1, 10, 2);
        EncodedTriple t2 = EncodedTriple.FromEncoded(2, 10, 3);
        EncodedTriple t3 = EncodedTriple.FromEncoded(3, 10, 4);
        ReplicationIndexFeed feed = new([t1, t2], new NodeIdentifier(1));

        ReplicationGeneration seeded = feed.Current();
        Assert.AreEqual(2, seeded.Index.TripleCount, "The seed holds the two seeded triples.");
        Assert.AreEqual(new NodeIdentifier(1), seeded.StateId, "The seed reflects the seeding StateId.");

        feed.Advance([t3], [t1], new NodeIdentifier(2));

        ReplicationGeneration advanced = feed.Current();
        Assert.AreEqual(new NodeIdentifier(2), advanced.StateId, "Advance records the new StateId.");
        HashSet<EncodedTriple> advancedTriples = [.. advanced.Index.EnumerateTriples()];
        Assert.IsTrue(advancedTriples.SetEquals([t2, t3]), "Advance applies the delta: t1 removed, t3 added.");

        HashSet<EncodedTriple> seededTriples = [.. seeded.Index.EnumerateTriples()];
        Assert.IsTrue(seededTriples.SetEquals([t1, t2]), "The prior generation snapshot is unaffected by the advance.");
    }
}
