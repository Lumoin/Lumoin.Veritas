using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
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
}
