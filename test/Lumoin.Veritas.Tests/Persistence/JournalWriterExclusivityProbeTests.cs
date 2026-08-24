using System;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Journal;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// Adversarial probe for the "single-writer exclusivity is platform-dependent, with a handle-free
/// construction window" finding on <see cref="FileBackedJournal"/>. Two questions:
/// (A) on the platform under test (Windows), does a second <see cref="FileBackedJournal"/> over an
/// already-open log fail at construction — i.e. is the FileShare.Read guard a HARD block that closes
/// the construction window here? and (B) if two writers ever did clobber a log into a
/// duplicate/holey sequence, does reopen FAIL CLOSED (a named refusal) or serve corrupt data?
/// These pin the mechanism the finding names; the Unix advisory-lock weakness itself is not
/// reproducible on win32 and is verified by inspection (FileShare.Read at FileBackedJournal.cs:128).
/// </summary>
[TestClass]
internal sealed class JournalWriterExclusivityProbeTests
{
    /// <summary>The MSTest context, used for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static string CreateTempDirectory()
    {
        return Directory.CreateTempSubdirectory("veritas-journal-exclusivity-").FullName;
    }

    private static JournalEntry MakeEntry(NodeIdentifier parent, NodeIdentifier child)
    {
        return new JournalEntry(
            ParentId: parent,
            ChildId: child,
            EntryKind: EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: null,
            Additions: ImmutableArray<EncodedTriple>.Empty,
            Removals: ImmutableArray<EncodedTriple>.Empty,
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>
    /// Probe A: while one journal holds its write handle (opened FileShare.Read at
    /// FileBackedJournal.cs:128), a second journal over the SAME path fails at construction on Windows.
    /// This is the hard enforcement the finding concedes for Windows: the construction window
    /// (File.ReadAllBytes at :216 precedes the exclusive OpenHandle at :128) is benign here because the
    /// second opener's OpenHandle loses with a sharing violation before it can write anything.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void SecondWriterOverAnOpenLogFailsAtConstructionOnWindows()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using FileBackedJournal first = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            //A second FileBackedJournal over the same open path must fail: its OpenHandle(FileAccess.Write,
            //FileShare.Read) collides with the first writer's still-open write handle. On Windows this is a
            //hard sharing violation surfaced as IOException, so two writers can never coexist.
            IOException error = Assert.ThrowsExactly<IOException>(
                () => { using FileBackedJournal second = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool); });

            Assert.IsNotNull(error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Probe B: a log carrying a duplicated sequence number — the shape two clobbering writers would
    /// eventually leave — makes reopen throw <see cref="InvalidDataException"/> at the density check
    /// (FileBackedJournal.cs:221-224). The "bricking open" the finding names is therefore a FAIL-CLOSED
    /// named refusal, not silent service of interleaved/corrupt records.
    /// </summary>
    [TestMethod]
    public async Task DuplicateSequenceLogFailsClosedOnReopen()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            //Produce one genuine sequence-0 record through the real durable path.
            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            //Stage a log with the seq-0 record written twice: a duplicate sequence number, exactly what an
            //un-serialised second writer clobbering the same offsets could leave on disk.
            byte[] oneRecord = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            byte[] duplicated = [.. oneRecord, .. oneRecord];
            await File.WriteAllBytesAsync(path, duplicated, TestContext.CancellationToken).ConfigureAwait(false);

            //Reopen refuses rather than serving the duplicate: the density check turns a holey/colliding
            //sequence into a hard InvalidDataException (fail-closed), never a silent mis-read.
            InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(
                () => { using FileBackedJournal reopened = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool); });

            Assert.Contains("sequence", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
