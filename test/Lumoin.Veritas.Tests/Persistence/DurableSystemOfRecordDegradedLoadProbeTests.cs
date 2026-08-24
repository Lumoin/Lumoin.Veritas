using System;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The recovery-fidelity contract for <see cref="DurableSystemOfRecordStore.TryLoad"/> over an orphan manifest a
/// crash between the manifest fsync and the CURRENT rename leaves behind: recovery must never serve the
/// never-committed orphan as committed truth. When a committed prior generation's commit evidence — its retained
/// CURRENT copy — survives, that generation loads, evidenced and non-degraded, over the orphan; only when no
/// evidence survives anywhere does the orphan load, and then it is surfaced degraded and evidence-less so a host
/// can refuse it. The parallel heal path (<see cref="Lumoin.Veritas.Core.Integrity.GenerationCommitCoordinator"/>)
/// treats the same degraded flag as a hard refusal; the system-of-record load path now surfaces it too.
/// </summary>
[TestClass]
internal sealed class DurableSystemOfRecordDegradedLoadProbeTests
{
    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a small dictionary and graph over a shared subject and predicate.</summary>
    /// <param name="epoch">The dictionary epoch.</param>
    /// <param name="objectCount">The number of object terms (and triples).</param>
    /// <returns>The dictionary and the encoded triples.</returns>
    private static (TermDictionary Dictionary, EncodedTriple[] Triples) BuildGraph(ulong epoch, int objectCount)
    {
        TermDictionary dictionary = new(epoch);
        TermId subject = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/s")));
        TermId predicate = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/p")));

        EncodedTriple[] triples = new EncodedTriple[objectCount];
        for(int i = 0; i < objectCount; i++)
        {
            TermId @object = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"http://example.org/o{i}")));
            triples[i] = new EncodedTriple(subject, predicate, @object);
        }

        return (dictionary, triples);
    }

    /// <summary>
    /// Commits generation 0 normally, then crashes generation 1 at the commit point before the CURRENT rename so
    /// generation 1's manifest, dictionary, and record are all fully-durable orphans while the live CURRENT still
    /// names the committed generation 0. Returns the store directory holding that exact on-disk state.
    /// </summary>
    /// <param name="pool">The byte pool the commits rent from.</param>
    /// <returns>The store directory holding the committed generation 0 and the orphan generation 1.</returns>
    private static string StageOrphanOverCommittedPrior(VeritasMemoryPool<byte> pool)
    {
        string directory = Directory.CreateTempSubdirectory("veritas-orphan-").FullName;

        FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);

        //Commit generation 0 normally: it is the only generation ever made live through an atomic CURRENT publish,
        //so its retained current-0 copy — written only after the rename — is its commit evidence.
        DurableSystemOfRecordStore committedStore = new(persistence, pool);
        (TermDictionary d0, EncodedTriple[] t0) = BuildGraph(0xAAAA, objectCount: 3);
        DurableSystemOfRecordCommit commit0 = committedStore.Persist(d0, t0);
        Assert.AreEqual(0L, commit0.Generation, "Generation 0 is the committed baseline.");

        //Crash generation 1 at the commit point: dict-1, sor-1, and manifest-1 are all written and fsynced, but the
        //CURRENT rename that would make generation 1 live never runs, so no retained current-1 is ever written.
        FailAtStepStore crashing = new(persistence, PublishFailStep.BeforeRename);
        DurableSystemOfRecordStore orphaningStore = new(crashing, pool);
        (TermDictionary d1, EncodedTriple[] t1) = BuildGraph(0xBBBB, objectCount: 5);
        Assert.ThrowsExactly<IOException>(() => orphaningStore.Persist(d1, t1), "The commit crashes at the commit point before the CURRENT rename.");

        Assert.IsTrue(File.Exists(Path.Combine(directory, ManifestNaming.ManifestName(1))), "manifest-1 is a fully-durable orphan on disk.");
        Assert.IsNull(persistence.Read(ManifestNaming.RetainedCurrentName(1)), "No retained current-1 exists — the orphan was never committed.");

        return directory;
    }

    /// <summary>
    /// When the live CURRENT is lost but the committed prior generation's retained CURRENT copy survives, recovery
    /// prefers that commit evidence: TryLoad serves the committed generation 0, evidenced and non-degraded, and
    /// never the never-committed orphan generation 1.
    /// </summary>
    [TestMethod]
    public void CommittedPriorLoadsWithEvidenceOverOrphanWhenRetainedCopySurvives()
    {
        using VeritasMemoryPool<byte> pool = new();
        string directory = StageOrphanOverCommittedPrior(pool);
        try
        {
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);

            //Lose only the live CURRENT pointer (a torn or rotted live pointer); the retained current-0 copy — the
            //commit evidence for generation 0 — survives.
            persistence.Delete(ManifestNaming.CurrentPointerName);
            Assert.IsNotNull(persistence.Read(ManifestNaming.RetainedCurrentName(0)), "The committed generation 0's retained CURRENT copy survives as commit evidence.");

            DurableSystemOfRecordStore reopened = new(persistence, pool);
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = reopened.TryLoad(termPool, triplePool);
            load.Triples?.Dispose();

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "The committed generation loads.");
            Assert.AreEqual(0L, load.Generation, "The committed generation 0 is served over the never-committed orphan 1.");
            Assert.IsFalse(load.IsDegraded, "Recovery followed the retained CURRENT copy, so the load is not degraded.");
            Assert.IsTrue(load.CommitEvidenced, "The retained CURRENT copy attests generation 0 was committed.");
            Assert.IsFalse(load.IsRollback, "Loading generation 0 (the newest generation a surviving pointer names) is not a rollback.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// When every CURRENT pointer (live and retained) has been lost, recovery falls to the degraded direct scan and
    /// the only surviving generation is the orphan. TryLoad still serves it — there is nothing else — but no longer
    /// silently: it is surfaced degraded and evidence-less so a host can refuse to treat it as committed truth.
    /// </summary>
    [TestMethod]
    public void OrphanOnlyStoreLoadsButIsSurfacedDegradedAndEvidenceLess()
    {
        using VeritasMemoryPool<byte> pool = new();
        string directory = StageOrphanOverCommittedPrior(pool);
        try
        {
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);

            //A later restart in which the live CURRENT and every retained current-* copy have been lost or rotted:
            //the "current" prefix covers the live pointer, its staging name, and every retained copy.
            foreach(string pointerName in persistence.List("current"))
            {
                persistence.Delete(pointerName);
            }

            //Recovery now has no CURRENT pointer to follow, so it falls to the degraded direct scan, which returns
            //the highest-stamped verifying manifest = the never-committed orphan generation 1, flagged degraded.
            RecoveryResult degraded = new ManifestRecovery(persistence).Recover();
            Assert.IsTrue(degraded.IsDegraded, "With no surviving CURRENT pointer, recovery is the degraded direct scan.");
            Assert.IsFalse(degraded.CommitEvidenced, "No retained CURRENT copy survives, so the degraded pick has no commit evidence.");
            Assert.AreEqual(1L, degraded.Manifest.CommitGeneration, "The degraded scan surfaces the orphan generation 1, which was never committed.");

            DurableSystemOfRecordStore reopened = new(persistence, pool);
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = reopened.TryLoad(termPool, triplePool);
            load.Triples?.Dispose();

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "There is nothing else to serve, so the orphan still loads.");
            Assert.AreEqual(1L, load.Generation, "The served generation is the orphan 1.");
            Assert.IsTrue(load.IsDegraded, "TryLoad no longer drops the degraded flag: the orphan load is surfaced degraded.");
            Assert.IsFalse(load.CommitEvidenced, "The orphan has no retained CURRENT copy, so the load is evidence-less — a host can refuse it.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
