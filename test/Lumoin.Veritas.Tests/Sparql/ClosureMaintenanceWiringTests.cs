using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Tests.Database;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// The Sparql-layer closure-maintenance seam in its production wiring: a stub
/// <see cref="ClosureMaintenanceDelegate"/> — the tests know nothing of the reasoner — drives a reasoned
/// <see cref="MutableSparqlDataset"/> through <see cref="DatasetEditSession.CommitAsync"/> and observes that the
/// served store and the opaque reasoning payload swap atomically with the asserted store, that the outcome seam
/// fires exactly once per delegate invocation, that the maintenance mutex serializes commits, that an unwired
/// dataset is byte-identical, and that a bare fork serves asserted-only.
/// </summary>
[TestClass]
internal sealed class ClosureMaintenanceWiringTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A stub maintenance seam: records every delegate invocation and outcome notification, and returns a caller-programmed served delta.</summary>
    private sealed class StubMaintenance
    {
        /// <summary>The programmed response for each invocation.</summary>
        private Func<StubInvocation, ValueTask<MaintainedCommitDelta>> Responder { get; }

        /// <summary>Every recorded delegate invocation, in order.</summary>
        public List<StubInvocation> Invocations { get; } = [];

        /// <summary>Every recorded outcome notification's landed value, in order.</summary>
        public List<bool> Outcomes { get; } = [];

        /// <summary>Constructs the stub over a response function.</summary>
        /// <param name="responder">Maps an invocation to its served delta (or throws, or blocks).</param>
        public StubMaintenance(Func<StubInvocation, ValueTask<MaintainedCommitDelta>> responder)
        {
            Responder = responder;
        }

        /// <summary>The maintenance delegate to register.</summary>
        public ClosureMaintenanceDelegate Delegate => MaintainAsync;

        /// <summary>The outcome delegate to register.</summary>
        public ClosureMaintenanceOutcomeDelegate Outcome => OnOutcome;

        /// <summary>Records the invocation and returns the programmed served delta.</summary>
        /// <param name="baseAdded">The commit's net asserted additions.</param>
        /// <param name="baseRemoved">The commit's net asserted removals.</param>
        /// <param name="tentativeAssertedStore">The session's tentative post-op asserted default-graph store.</param>
        /// <param name="wholesaleReplace">Whether the caller flagged a wholesale default-graph replacement.</param>
        /// <param name="cancellationToken">The commit's cancellation token.</param>
        /// <returns>The programmed served delta.</returns>
        private ValueTask<MaintainedCommitDelta> MaintainAsync(
            IReadOnlyCollection<EncodedTriple> baseAdded,
            IReadOnlyCollection<EncodedTriple> baseRemoved,
            HypertrieGraphStore tentativeAssertedStore,
            bool wholesaleReplace,
            CancellationToken cancellationToken)
        {
            StubInvocation invocation = new([.. baseAdded], [.. baseRemoved], wholesaleReplace);
            Invocations.Add(invocation);

            return Responder(invocation);
        }

        /// <summary>Records an outcome notification.</summary>
        /// <param name="landed">Whether the commit landed.</param>
        private void OnOutcome(bool landed)
        {
            Outcomes.Add(landed);
        }
    }

    /// <summary>One recorded delegate invocation: the net asserted delta the commit passed and the wholesale-replace flag.</summary>
    /// <param name="BaseAdded">The commit's net asserted additions.</param>
    /// <param name="BaseRemoved">The commit's net asserted removals.</param>
    /// <param name="WholesaleReplace">Whether the caller flagged a wholesale default-graph replacement.</param>
    private sealed record StubInvocation(EncodedTriple[] BaseAdded, EncodedTriple[] BaseRemoved, bool WholesaleReplace);

    /// <summary>Encodes a triple over three raw term identifiers.</summary>
    /// <param name="s">The subject identifier.</param>
    /// <param name="p">The predicate identifier.</param>
    /// <param name="o">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple T(uint s, uint p, uint o)
    {
        return EncodedTriple.FromEncoded(s, p, o);
    }

    /// <summary>Reads all of a store's encoded triples into a set.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The store's triples.</returns>
    private static HashSet<EncodedTriple> Triples(HypertrieGraphStore store)
    {
        return [.. store.Match(TermId.None, TermId.None, TermId.None)];
    }

    /// <summary>Builds a served delta whose overlay is on.</summary>
    /// <param name="servedAdd">The served additions.</param>
    /// <param name="servedRemove">The served removals.</param>
    /// <param name="reasoningState">The opaque reasoning-state payload.</param>
    /// <returns>The served delta.</returns>
    private static MaintainedCommitDelta Delta(IEnumerable<EncodedTriple> servedAdd, IEnumerable<EncodedTriple> servedRemove, object? reasoningState)
    {
        return new MaintainedCommitDelta
        {
            ServedAdditions = [.. servedAdd],
            ServedRemovals = [.. servedRemove],
            OverlayOn = true,
            ReasoningState = reasoningState,
        };
    }

    /// <summary>Asserts the outcome seam fired exactly once with the expected landed value.</summary>
    /// <param name="outcomes">The recorded outcome notifications.</param>
    /// <param name="expected">The expected single landed value.</param>
    /// <param name="message">The assertion message.</param>
    private static void AssertSingleOutcome(List<bool> outcomes, bool expected, string message)
    {
        Assert.HasCount(1, outcomes, message);
        Assert.AreEqual(expected, outcomes[0], message);
    }

    /// <summary>Opens a reasoned dataset and registers a stub maintenance seam.</summary>
    /// <param name="asserted">The initial asserted default-graph triples.</param>
    /// <param name="served">The initial served default-graph triples (asserted ∪ derived).</param>
    /// <param name="reasoningState">The initial opaque reasoning-state payload.</param>
    /// <param name="responder">The stub's per-invocation response function.</param>
    /// <param name="journalAppend">An optional journal append seam; <see langword="null"/> wires a fresh in-memory journal.</param>
    /// <param name="journalRead">An optional journal read seam; <see langword="null"/> wires a fresh in-memory journal.</param>
    /// <returns>The reasoned dataset and its stub.</returns>
    private async Task<(MutableSparqlDataset Dataset, StubMaintenance Stub)> OpenReasonedAsync(
        IReadOnlyList<EncodedTriple> asserted,
        IReadOnlyList<EncodedTriple> served,
        object? reasoningState,
        Func<StubInvocation, ValueTask<MaintainedCommitDelta>> responder,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync? journalAppend = null,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead = null)
    {
        StubMaintenance stub = new(responder);
        MutableSparqlDataset dataset = await MutableSparqlDataset
            .CreateAsync(
                new TermDictionary(),
                asserted,
                initialServedTriples: served,
                initialReasoningState: reasoningState,
                namedGraphs: null,
                journalAppend: journalAppend,
                journalRead: journalRead,
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        dataset.RegisterMaintenance(stub.Delegate, stub.Outcome);

        return (dataset, stub);
    }

    /// <summary>Commits a default-graph delta through a fresh session, returning the committed snapshot.</summary>
    /// <param name="dataset">The dataset.</param>
    /// <param name="additions">The triples to add.</param>
    /// <param name="removals">The triples to remove.</param>
    /// <returns>The committed snapshot.</returns>
    private async Task<SparqlDataset> CommitDefaultAsync(MutableSparqlDataset dataset, IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals)
    {
        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(session.ConfigureAwait(false))
        {
            await session.ApplyDeltaAsync(TermId.None, additions, removals, TestContext.CancellationToken).ConfigureAwait(false);

            return await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A wired commit applies the served delta and swaps the reasoning payload atomically: the served store gains the derived triple the asserted store never carries, and the payload advances in the same publish.</summary>
    [TestMethod]
    public async Task WiredCommitAppliesServedDeltaAndSwapsPayloadAtomically()
    {
        EncodedTriple asserted = T(1, 2, 3);
        EncodedTriple derived = T(1, 2, 99);

        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta([.. invocation.BaseAdded, derived], invocation.BaseRemoved, "gen1"))).ConfigureAwait(false);

        //Before the commit the served store is empty and the payload is the open generation.
        Assert.IsEmpty(Triples(dataset.Snapshot().DefaultGraph!), "The served store starts empty.");
        Assert.AreEqual("gen0", dataset.CurrentState().ReasoningState);

        await CommitDefaultAsync(dataset, [asserted], []).ConfigureAwait(false);

        //After the commit the asserted store holds only the asserted triple; the served store holds it plus the
        //derived one; the payload advanced in the same publish.
        Assert.IsTrue(Triples(dataset.DefaultGraph).SetEquals([asserted]), "The asserted store carries the asserted triple only.");
        Assert.IsTrue(Triples(dataset.Snapshot().DefaultGraph!).SetEquals([asserted, derived]), "The served store carries the asserted and derived triples.");
        Assert.AreEqual("gen1", dataset.CurrentState().ReasoningState, "The reasoning payload swapped atomically with the served store.");
        Assert.HasCount(1, stub.Invocations);
        Assert.IsTrue(new HashSet<EncodedTriple>(stub.Invocations[0].BaseAdded).SetEquals([asserted]), "The delegate received the net asserted addition.");
        AssertSingleOutcome(stub.Outcomes, true, "The outcome fired once, landed.");
    }

    /// <summary>An unwired dataset is byte-identical: the served store is the asserted store by reference, the reasoning payload is null, and there is no maintenance mutex.</summary>
    [TestMethod]
    public async Task UnwiredCommitIsByteIdenticalServingAssertedOnly()
    {
        MutableSparqlDataset dataset = await MutableSparqlDataset
            .CreateAsync(new TermDictionary(), [], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsNull(dataset.MaintenanceMutex, "An unwired dataset has no maintenance mutex.");
        Assert.IsNull(dataset.CurrentState().ReasoningState, "An unwired dataset carries no reasoning payload.");
        Assert.AreSame(dataset.CurrentState().DefaultGraph, dataset.CurrentState().ServedDefaultGraph, "The served store is the asserted store by reference when unwired.");

        EncodedTriple triple = T(1, 2, 3);
        await CommitDefaultAsync(dataset, [triple], []).ConfigureAwait(false);

        Assert.IsTrue(Triples(dataset.DefaultGraph).SetEquals([triple]), "The asserted store holds the committed triple.");
        Assert.AreSame(dataset.CurrentState().DefaultGraph, dataset.CurrentState().ServedDefaultGraph, "The committed served store stays the asserted store by reference.");
        Assert.IsNull(dataset.CurrentState().ReasoningState);
    }

    /// <summary>A named-graph-only commit invokes no delegate and carries the served store and the reasoning payload forward from the base state BY REFERENCE, so the entailments still serve.</summary>
    [TestMethod]
    public async Task NamedGraphOnlyCommitCarriesServedStoreAndPayloadForwardByReference()
    {
        EncodedTriple asserted = T(1, 2, 3);
        EncodedTriple derived = T(1, 2, 99);
        object payloadOne = "gen1";

        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta([.. invocation.BaseAdded, derived], invocation.BaseRemoved, payloadOne))).ConfigureAwait(false);

        await CommitDefaultAsync(dataset, [asserted], []).ConfigureAwait(false);
        HypertrieGraphStore servedAfterFirst = dataset.CurrentState().ServedDefaultGraph;
        object? payloadAfterFirst = dataset.CurrentState().ReasoningState;

        //A named-graph-only commit: create and populate a named graph, touching the default graph not at all.
        TermId graph = dataset.Dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/g")));
        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(session.ConfigureAwait(false))
        {
            await session.ApplyDeltaAsync(graph, [T(4, 5, 6)], [], TestContext.CancellationToken).ConfigureAwait(false);
            await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }

        Assert.HasCount(1, stub.Invocations, "The named-graph-only commit invoked no maintenance delegate.");
        Assert.AreSame(servedAfterFirst, dataset.CurrentState().ServedDefaultGraph, "The served store carried forward by reference.");
        Assert.AreSame(payloadAfterFirst, dataset.CurrentState().ReasoningState, "The reasoning payload carried forward by reference.");
        Assert.IsTrue(Triples(dataset.Snapshot().DefaultGraph!).SetEquals([asserted, derived]), "The entailments still serve after a named-graph-only commit.");
        AssertSingleOutcome(stub.Outcomes, true, "Only the default-graph commit fired an outcome.");
    }

    /// <summary>A stale session's commit skips the delegate (the pre-check sees the advanced head) and its append fails naturally; no outcome fires for the skipped invocation.</summary>
    [TestMethod]
    public async Task StaleSessionSkipsTheDelegateAndItsAppendFails()
    {
        EncodedTriple derived = T(1, 2, 99);

        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta([.. invocation.BaseAdded, derived], invocation.BaseRemoved, "gen1"))).ConfigureAwait(false);

        //Two sessions open on the same base state.
        DatasetEditSession first = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        DatasetEditSession second = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(first.ConfigureAwait(false))
        await using(second.ConfigureAwait(false))
        {
            await first.ApplyDeltaAsync(TermId.None, [T(1, 2, 3)], [], TestContext.CancellationToken).ConfigureAwait(false);
            await second.ApplyDeltaAsync(TermId.None, [T(1, 2, 4)], [], TestContext.CancellationToken).ConfigureAwait(false);

            //The first commit wins and publishes.
            await first.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            //The second is now stale: its pre-check sees the advanced head, skips the delegate, and its append fails.
            await Assert.ThrowsExactlyAsync<EditSessionConcurrencyException>(
                async () => await second.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        Assert.HasCount(1, stub.Invocations, "The stale session skipped the delegate — only the winner invoked it.");
        AssertSingleOutcome(stub.Outcomes, true, "The skipped invocation fired no outcome; only the winner did.");
    }

    /// <summary>The outcome seam fires exactly once with landed=true when a wired commit succeeds.</summary>
    [TestMethod]
    public async Task OutcomeFiresOnceLandedTrueOnSuccess()
    {
        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta(invocation.BaseAdded, invocation.BaseRemoved, "gen1"))).ConfigureAwait(false);

        await CommitDefaultAsync(dataset, [T(1, 2, 3)], []).ConfigureAwait(false);

        AssertSingleOutcome(stub.Outcomes, true, "The outcome fired exactly once, landed.");
    }

    /// <summary>The outcome seam fires exactly once with landed=false when the linearising append fails after the delegate ran, and the commit leaves the current state unchanged.</summary>
    [TestMethod]
    public async Task OutcomeFiresOnceLandedFalseOnForcedAppendFailure()
    {
        InMemoryDatasetJournal inner = new();
        FaultInjectingDatasetJournal journal = new(inner.AppendDelegate, inner.ReadDelegate);

        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta([.. invocation.BaseAdded, T(1, 2, 99)], invocation.BaseRemoved, "gen1")),
            journal.AppendDelegate,
            journal.ReadDelegate).ConfigureAwait(false);

        NodeIdentifier headBefore = dataset.StateId;

        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(session.ConfigureAwait(false))
        {
            await session.ApplyDeltaAsync(TermId.None, [T(1, 2, 3)], [], TestContext.CancellationToken).ConfigureAwait(false);

            //Fail every append from here: the session-open Started entry is already written, so the next append is
            //the linearising Committed one — it fails after the delegate has run.
            journal.Arm(static _ => true);

            await Assert.ThrowsExactlyAsync<EditSessionConcurrencyException>(
                async () => await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        Assert.HasCount(1, stub.Invocations, "The delegate ran before the append failed.");
        AssertSingleOutcome(stub.Outcomes, false, "The outcome fired once, not landed.");
        Assert.AreEqual(headBefore, dataset.StateId, "The failed commit left the current state unchanged.");
        Assert.IsEmpty(Triples(dataset.Snapshot().DefaultGraph!), "Nothing was published to the served store.");
    }

    /// <summary>A throwing delegate fails the commit, fires the outcome landed=false, and publishes nothing — the current state is unchanged.</summary>
    [TestMethod]
    public async Task DelegateThrowFailsTheCommitFiresLandedFalseAndDoesNotPublish()
    {
        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            static _ => throw new InvalidOperationException("stub maintenance failure")).ConfigureAwait(false);

        NodeIdentifier headBefore = dataset.StateId;

        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(session.ConfigureAwait(false))
        {
            await session.ApplyDeltaAsync(TermId.None, [T(1, 2, 3)], [], TestContext.CancellationToken).ConfigureAwait(false);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        Assert.HasCount(1, stub.Invocations, "The delegate was invoked before it threw.");
        AssertSingleOutcome(stub.Outcomes, false, "A delegate throw is an invoked, not-landed outcome.");
        Assert.AreEqual(headBefore, dataset.StateId, "A delegate throw publishes nothing.");
        Assert.IsEmpty(Triples(dataset.Snapshot().DefaultGraph!), "The served store is unchanged after a delegate throw.");
    }

    /// <summary>An incremental default-graph commit passes wholesaleReplace=false; a full default-graph replacement passes wholesaleReplace=true.</summary>
    [TestMethod]
    public async Task WholesaleReplaceFlagPassedOnDefaultGraphReplace()
    {
        EncodedTriple t1 = T(1, 2, 3);

        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [t1],
            [t1],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta(invocation.BaseAdded, invocation.BaseRemoved, "gen"))).ConfigureAwait(false);

        //An incremental insert over a non-empty base is not a wholesale replace.
        await CommitDefaultAsync(dataset, [T(1, 2, 4)], []).ConfigureAwait(false);
        Assert.IsFalse(stub.Invocations[0].WholesaleReplace, "An incremental insert is not a wholesale replace.");

        //A full default-graph replacement retracts the entire asserted default graph.
        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(session.ConfigureAwait(false))
        {
            await session.ReplaceGraphAsync(TermId.None, [T(1, 2, 5)], TestContext.CancellationToken).ConfigureAwait(false);
            await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }

        Assert.HasCount(2, stub.Invocations);
        Assert.IsTrue(stub.Invocations[1].WholesaleReplace, "A full default-graph replacement is a wholesale replace.");
    }

    /// <summary>Two concurrent wired commits serialize on the maintenance mutex: the second blocks until the first publishes, then finds the head advanced, skips the delegate, and its append fails — the delegate ran exactly once.</summary>
    [TestMethod]
    public async Task ConcurrentWiredCommitsSerializeOnTheMaintenanceMutex()
    {
        EncodedTriple derivedForWinner = T(1, 2, 99);
        TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        (MutableSparqlDataset dataset, StubMaintenance stub) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            async invocation =>
            {
                //The first delegate call — the winner's — signals that it holds the mutex, then blocks until released,
                //so the competing commit is provably stalled on the mutex meanwhile.
                if(Interlocked.Increment(ref calls) == 1)
                {
                    firstEntered.SetResult();
                    await release.Task.ConfigureAwait(false);
                }

                return Delta([.. invocation.BaseAdded, derivedForWinner], invocation.BaseRemoved, "gen1");
            }).ConfigureAwait(false);

        DatasetEditSession winner = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        DatasetEditSession loser = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(winner.ConfigureAwait(false))
        await using(loser.ConfigureAwait(false))
        {
            await winner.ApplyDeltaAsync(TermId.None, [T(1, 2, 3)], [], TestContext.CancellationToken).ConfigureAwait(false);
            await loser.ApplyDeltaAsync(TermId.None, [T(1, 2, 4)], [], TestContext.CancellationToken).ConfigureAwait(false);

            //Start the winner; wait until it is inside the delegate holding the mutex.
            Task<SparqlDataset> winnerCommit = winner.CommitAsync(TestContext.CancellationToken).AsTask();
            await firstEntered.Task.ConfigureAwait(false);

            //Start the loser; it must block on the mutex — it cannot reach its pre-check or the delegate yet.
            Task<SparqlDataset> loserCommit = loser.CommitAsync(TestContext.CancellationToken).AsTask();
            Assert.IsFalse(loserCommit.IsCompleted, "The competing commit blocks on the maintenance mutex.");
            Assert.AreEqual(1, Volatile.Read(ref calls), "The competing commit has not entered the delegate while the winner holds the mutex.");

            //Release the winner; it publishes and frees the mutex.
            release.SetResult();
            await winnerCommit.ConfigureAwait(false);

            //The loser now acquires the mutex, sees the advanced head, skips the delegate, and its append fails.
            await Assert.ThrowsExactlyAsync<EditSessionConcurrencyException>(async () => await loserCommit.ConfigureAwait(false)).ConfigureAwait(false);
        }

        Assert.AreEqual(1, Volatile.Read(ref calls), "The delegate ran exactly once — the loser skipped it after the mutex serialized the commits.");
        AssertSingleOutcome(stub.Outcomes, true, "Only the winner fired an outcome.");
        Assert.IsTrue(Triples(dataset.Snapshot().DefaultGraph!).SetEquals([T(1, 2, 3), derivedForWinner]), "The served store equals the closure of the final committed base.");
    }

    /// <summary>A bare fork of a reasoned dataset serves asserted-only: the fork's state carries its served store equal to its asserted store, honouring the clean-unwired-engine fork rule.</summary>
    [TestMethod]
    public async Task ForkServesAssertedOnly()
    {
        EncodedTriple asserted = T(1, 2, 3);
        EncodedTriple derived = T(1, 2, 99);

        (MutableSparqlDataset dataset, _) = await OpenReasonedAsync(
            [],
            [],
            "gen0",
            invocation => new ValueTask<MaintainedCommitDelta>(Delta([.. invocation.BaseAdded, derived], invocation.BaseRemoved, "gen1"))).ConfigureAwait(false);

        //Commit so the parent's served store diverges from its asserted store.
        await CommitDefaultAsync(dataset, [asserted], []).ConfigureAwait(false);
        Assert.AreNotSame(dataset.CurrentState().DefaultGraph, dataset.CurrentState().ServedDefaultGraph, "The parent's served store diverged from its asserted store.");

        MutableSparqlDataset fork = await dataset.ForkAsync(cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNull(fork.MaintenanceMutex, "A bare fork carries no maintenance seam.");
        Assert.AreSame(fork.CurrentState().DefaultGraph, fork.CurrentState().ServedDefaultGraph, "The fork serves asserted-only: its served store equals its asserted store by reference.");
        Assert.IsNull(fork.CurrentState().ReasoningState, "The fork carries no reasoning payload.");
        Assert.IsTrue(Triples(fork.Snapshot().DefaultGraph!).SetEquals([asserted]), "The fork serves the asserted graph only, never the parent's overlay.");
    }
}
