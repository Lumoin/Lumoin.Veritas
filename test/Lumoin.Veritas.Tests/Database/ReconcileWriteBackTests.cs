using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The reconcile write-back applies a converged peer delta back into a mutable dataset as an ordinary journalled
/// commit — repair as ingest — so the dataset then holds the recovered triples; an empty delta is a value-based
/// no-op.
/// </summary>
[TestClass]
internal sealed class ReconcileWriteBackTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A non-empty recovered delta commits through the journal and the dataset holds it; an empty delta is a no-op.</summary>
    [TestMethod]
    public async Task WritesBackARecoveredDeltaThenReportsNoOpForEmpty()
    {
        MutableSparqlDataset dataset = await MutableSparqlDataset.CreateAsync(new TermDictionary(), [], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        EncodedTriple t1 = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple t2 = EncodedTriple.FromEncoded(1, 2, 4);

        WriteBackOutcome committed = await ReconcileWriteBack
            .ApplyAsync(dataset, new EncodedTriple[] { t1, t2 }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(WriteBackOutcome.Committed, committed, "A non-empty delta commits through the journal.");

        HashSet<EncodedTriple> triples = [.. dataset.DefaultGraph.Match(TermId.None, TermId.None, TermId.None)];
        Assert.IsTrue(triples.SetEquals(new EncodedTriple[] { t1, t2 }), "The dataset holds the written-back triples.");

        WriteBackOutcome empty = await ReconcileWriteBack
            .ApplyAsync(dataset, ReadOnlyMemory<EncodedTriple>.Empty, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(WriteBackOutcome.NoOp, empty, "An empty delta is a no-op.");
    }

    /// <summary>Builds a mutable dataset over a fault-injecting journal, disarmed at creation so only the operation under test sees injected conflicts.</summary>
    /// <param name="journal">The fault-injecting journal to wire in.</param>
    /// <returns>The dataset wired to the fault-injecting journal.</returns>
    private async Task<MutableSparqlDataset> FaultableDatasetAsync(FaultInjectingDatasetJournal journal)
    {
        return await MutableSparqlDataset
            .CreateAsync(new TermDictionary(), [], journalAppend: journal.AppendDelegate, journalRead: journal.ReadDelegate, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>A persistent head-CAS conflict on every attempt exhausts the bounded retry and reports ConflictExhausted, applying nothing — a later reconcile round re-detects.</summary>
    [TestMethod]
    public async Task PersistentConflictExhaustsTheRetryWithoutApplying()
    {
        InMemoryDatasetJournal inner = new();
        FaultInjectingDatasetJournal journal = new(inner.AppendDelegate, inner.ReadDelegate);
        MutableSparqlDataset dataset = await FaultableDatasetAsync(journal).ConfigureAwait(false);

        //Every append (including the session-open Started entry) loses the head-CAS race.
        journal.Arm(static _ => true);

        WriteBackOutcome outcome = await ReconcileWriteBack
            .ApplyAsync(dataset, new EncodedTriple[] { EncodedTriple.FromEncoded(1, 2, 3) }, maxAttempts: 3, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(WriteBackOutcome.ConflictExhausted, outcome, "A persistent conflict exhausts the bounded retry.");
        Assert.IsEmpty(dataset.DefaultGraph.Match(TermId.None, TermId.None, TermId.None), "Nothing was applied.");
    }

    /// <summary>A transient conflict at session-open time is retried — not thrown — and the next attempt commits, proving the open-time conflict is inside the retry.</summary>
    [TestMethod]
    public async Task TransientOpenTimeConflictIsRetriedThenCommits()
    {
        InMemoryDatasetJournal inner = new();
        FaultInjectingDatasetJournal journal = new(inner.AppendDelegate, inner.ReadDelegate);
        MutableSparqlDataset dataset = await FaultableDatasetAsync(journal).ConfigureAwait(false);
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        //Only the first armed append — the first attempt's session-open Started entry — conflicts; the retry's
        //open and commit succeed.
        journal.Arm(static armedAppendIndex => armedAppendIndex == 1);

        WriteBackOutcome outcome = await ReconcileWriteBack
            .ApplyAsync(dataset, new EncodedTriple[] { triple }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(WriteBackOutcome.Committed, outcome, "An open-time conflict is retried, not thrown, and the retry commits.");
        HashSet<EncodedTriple> triples = [.. dataset.DefaultGraph.Match(TermId.None, TermId.None, TermId.None)];
        Assert.IsTrue(triples.SetEquals(new EncodedTriple[] { triple }), "The retried write-back applied the delta.");
    }
}
