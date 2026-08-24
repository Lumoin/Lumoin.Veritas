using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The <see cref="DurableSketchStore"/> persists a node's structural sketch as a generation-versioned
/// manifest artifact and loads it back on restart: a persist-then-load round-trip recovers a verified,
/// servable sketch image and the dataset StateId it reflects; a load under a different dictionary epoch is
/// refused (the structural identifiers would denote different terms); an empty store reports nothing found;
/// an at-rest-corrupt sketch artifact is refused rather than served; and a second persist supersedes the
/// first, so a load recovers the latest committed generation. These exercise the durable persistence behind
/// the shipped manifest atomic publish, not a re-derivation from the feed.
/// </summary>
[TestClass]
internal sealed class DurableSketchStoreTests
{
    /// <summary>A line of triples: subjects <c>[0, count)</c> under a shared predicate, each linked to the next identifier.</summary>
    /// <param name="count">The number of triples.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] Line(uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(i, 10, i + 1);
        }

        return triples;
    }

    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Persisting a generation then loading it under the same epoch recovers a verified sketch image of the persisted budget and the StateId it reflects, including a StateId whose high (tag) bit is set.</summary>
    [TestMethod]
    public void PersistThenLoadRoundTripsImageAndStateId()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);

            NodeIdentifier stateId = new(0x8000_0000_0000_0001UL);
            ReplicationIndexFeed feed = new(Line(40), stateId);
            const ulong epoch = 0x1234_5678_9ABC_DEF0UL;
            const int symbolBudget = 64;

            using IncrementalSketchMaintainer maintainer = new(feed, pool, IncrementalSketchMaintainerOptions.Default, epoch);
            DurableSketchCommit commit = sketches.Persist(maintainer, symbolBudget);
            Assert.AreEqual(0L, commit.Generation, "The first persisted generation is zero.");
            Assert.AreEqual(stateId, commit.StateId, "The receipt carries the persisted StateId.");

            using DurableSketchLoad load = sketches.TryLoad(epoch);
            Assert.AreEqual(DurableSketchLoadOutcome.Loaded, load.Outcome, "A persisted sketch under a matching epoch loads.");
            Assert.AreEqual(stateId, load.StateId, "The loaded sketch reflects the persisted StateId (full 64-bit round-trip).");
            Assert.AreEqual(0L, load.Generation, "The loaded generation is the one persisted.");

            VerifiedSketch verified = SketchPersistence.LoadVerifiedSketch(load.Image.Span, SketchContract.Structural);
            Assert.AreEqual(symbolBudget, verified.SymbolCount, "The loaded image is a valid structural sketch of the persisted budget.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A sketch persisted under one dictionary epoch is refused when loaded under a different epoch, because its structural identifiers would denote different local terms.</summary>
    [TestMethod]
    public void LoadWithMismatchedDictionaryEpochIsRefused()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);

            ReplicationIndexFeed feed = new(Line(30), new NodeIdentifier(7UL));
            using IncrementalSketchMaintainer maintainer = new(feed, pool, IncrementalSketchMaintainerOptions.Default, dictionaryEpoch: 11UL);
            sketches.Persist(maintainer, symbolBudget: 48);

            using DurableSketchLoad load = sketches.TryLoad(dictionaryEpoch: 22UL);
            Assert.AreEqual(DurableSketchLoadOutcome.EpochMismatch, load.Outcome, "A foreign dictionary epoch must be refused, not served.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Loading from a store that holds no committed generation reports nothing found rather than throwing.</summary>
    [TestMethod]
    public void LoadFromEmptyStoreIsNotFound()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);

            using DurableSketchLoad load = sketches.TryLoad(dictionaryEpoch: 11UL);
            Assert.AreEqual(DurableSketchLoadOutcome.NotFound, load.Outcome, "An empty store holds no committed sketch generation.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>An at-rest-corrupt sketch artifact is refused on load rather than served (detection precedes use).</summary>
    [TestMethod]
    public void CorruptSketchArtifactIsRejected()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);

            ReplicationIndexFeed feed = new(Line(40), new NodeIdentifier(9UL));
            const ulong epoch = 11UL;
            using IncrementalSketchMaintainer maintainer = new(feed, pool, IncrementalSketchMaintainerOptions.Default, epoch);
            sketches.Persist(maintainer, symbolBudget: 64);

            string[] sketchArtifacts = Directory.GetFiles(directory, "sketch-*.skt");
            Assert.HasCount(1, sketchArtifacts, "Exactly one sketch artifact was persisted.");
            byte[] bytes = File.ReadAllBytes(sketchArtifacts[0]);
            //Flip the first symbol-payload byte. The first block starts at the 4096-byte alignment boundary (the
            //front matter for one small block is far shorter), so this corrupts a checksum-covered region — at-rest
            //rot the verifying load must catch, not merely a malformed header.
            bytes[4096] ^= 0xFF;
            File.WriteAllBytes(sketchArtifacts[0], bytes);

            using DurableSketchLoad load = sketches.TryLoad(epoch);
            Assert.AreEqual(DurableSketchLoadOutcome.Rejected, load.Outcome, "A corrupt sketch artifact must be refused, not served.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A sketch artifact whose length no longer matches the manifest's recorded length is refused, binding the file to the generation that named it (over-length tampering the sketch's own checksums need not catch).</summary>
    [TestMethod]
    public void SketchArtifactLengthMismatchIsRejected()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);

            ReplicationIndexFeed feed = new(Line(40), new NodeIdentifier(9UL));
            const ulong epoch = 11UL;
            using IncrementalSketchMaintainer maintainer = new(feed, pool, IncrementalSketchMaintainerOptions.Default, epoch);
            sketches.Persist(maintainer, symbolBudget: 64);

            string[] sketchArtifacts = Directory.GetFiles(directory, "sketch-*.skt");
            Assert.HasCount(1, sketchArtifacts, "Exactly one sketch artifact was persisted.");
            byte[] bytes = File.ReadAllBytes(sketchArtifacts[0]);
            byte[] extended = new byte[bytes.Length + 1];
            bytes.CopyTo(extended, 0);
            File.WriteAllBytes(sketchArtifacts[0], extended);

            using DurableSketchLoad load = sketches.TryLoad(epoch);
            Assert.AreEqual(DurableSketchLoadOutcome.Rejected, load.Outcome, "An artifact whose length differs from the manifest's record must be refused.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Persisting beyond the retention window reclaims superseded sketch artifacts — only the newest few remain — while the latest generation still loads.</summary>
    [TestMethod]
    public void SupersededSketchArtifactsAreReclaimed()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);
            const ulong epoch = 11UL;

            //Persist seven generations (0..6); the store retains the newest four (3..6) and reclaims 0..2.
            for(uint i = 0; i < 7; i++)
            {
                ReplicationIndexFeed feed = new(Line(30 + i), new NodeIdentifier(100UL + i));
                using IncrementalSketchMaintainer maintainer = new(feed, pool, IncrementalSketchMaintainerOptions.Default, epoch);
                sketches.Persist(maintainer, symbolBudget: 48);
            }

            string[] sketchArtifacts = Directory.GetFiles(directory, "sketch-*.skt");
            Assert.HasCount(4, sketchArtifacts, "Only the newest four sketch artifacts are retained.");

            using DurableSketchLoad load = sketches.TryLoad(epoch);
            Assert.AreEqual(DurableSketchLoadOutcome.Loaded, load.Outcome, "The latest generation still loads after collection.");
            Assert.AreEqual(6L, load.Generation, "The latest committed generation (the seventh, index 6) loads.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A second persist supersedes the first: the generation increments and a load recovers the latest committed generation and its StateId.</summary>
    [TestMethod]
    public void LatestGenerationIsLoaded()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-b8-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            DurableSketchStore sketches = new(store, pool);
            const ulong epoch = 11UL;

            ReplicationIndexFeed firstFeed = new(Line(30), new NodeIdentifier(100UL));
            using IncrementalSketchMaintainer firstMaintainer = new(firstFeed, pool, IncrementalSketchMaintainerOptions.Default, epoch);
            sketches.Persist(firstMaintainer, symbolBudget: 48);

            NodeIdentifier secondState = new(200UL);
            ReplicationIndexFeed secondFeed = new(Line(60), secondState);
            using IncrementalSketchMaintainer secondMaintainer = new(secondFeed, pool, IncrementalSketchMaintainerOptions.Default, epoch);
            DurableSketchCommit second = sketches.Persist(secondMaintainer, symbolBudget: 48);
            Assert.AreEqual(1L, second.Generation, "The second persisted generation is one.");

            using DurableSketchLoad load = sketches.TryLoad(epoch);
            Assert.AreEqual(DurableSketchLoadOutcome.Loaded, load.Outcome, "The latest generation loads.");
            Assert.AreEqual(1L, load.Generation, "TryLoad recovers the latest committed generation.");
            Assert.AreEqual(secondState, load.StateId, "TryLoad recovers the latest generation's StateId.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
