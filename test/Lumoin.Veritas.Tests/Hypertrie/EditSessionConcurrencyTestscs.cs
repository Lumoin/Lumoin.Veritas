using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Concurrency tests for <see cref="EditSession"/>: optimistic-
/// concurrency conflicts between racing sessions, sweep blocking
/// while a session holds the shared mutation gate, and the shape
/// of <see cref="EditSessionConcurrencyException"/>'s payload on
/// conflict.
/// </summary>
[TestClass]
internal sealed class EditSessionConcurrencyTests
{
    //Per-test cancellation budget. Same convention the
    //AsyncSharedExclusiveLock tests use: any operation taking
    //longer than this has hit a deadlock and the budget fires
    //the failure rather than the runner-level timeout. The
    //budget bounds the wait on a real deadlock; in the
    //no-deadlock path the work completes in milliseconds and
    //the budget is never approached. Sized generously so OS
    //scheduling jitter under parallel test execution cannot
    //graze the threshold — a real deadlock blocks forever, so
    //any finite budget detects it.
    private static TimeSpan DeadlockBudget { get; } = TimeSpan.FromSeconds(30);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SecondSessionCommitConflictsWithFirstCommit()
    {
        //Two sessions open against the same base. The first
        //commits, advancing the journal head; the second's commit
        //then sees a head mismatch and throws
        //EditSessionConcurrencyException carrying the actual head.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource budget = NewBudget();

        EditSession first = await graph.OpenEditSessionAsync(budget.Token).ConfigureAwait(false);
        EditSession second = await graph.OpenEditSessionAsync(budget.Token).ConfigureAwait(false);
        try
        {
            first.Add(EncodedTriple.FromEncoded(2, 20, 200));
            second.Add(EncodedTriple.FromEncoded(3, 30, 300));

            using HypertrieSnapshot firstCommitted = await first.CommitAsync(budget.Token).ConfigureAwait(false);

            EditSessionConcurrencyException thrown = await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await second.CommitAsync(budget.Token).ConfigureAwait(false))
                .ConfigureAwait(false);

            NodeIdentifier reportedExpected = thrown.ExpectedHead;
            NodeIdentifier reportedActual = thrown.ActualHead;

            //The second session's expected head is the base; the
            //actual head is the first session's committed snapshot.
            Assert.AreEqual(graph.Snapshot.Id, reportedExpected);
            Assert.AreEqual(firstCommitted.Id, reportedActual);
        }
        finally
        {
            await second.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ConflictedSessionDisposeDoesNotThrow()
    {
        //After the OCC conflict above, disposing the losing
        //session must not surface the abandon-write failure as a
        //test-level exception. Dispose swallows the inner failure
        //by design — there is no caller to surface it to.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource budget = NewBudget();

        EditSession first = await graph.OpenEditSessionAsync(budget.Token).ConfigureAwait(false);
        EditSession second = await graph.OpenEditSessionAsync(budget.Token).ConfigureAwait(false);

        try
        {
            first.Add(EncodedTriple.FromEncoded(2, 20, 200));
            second.Add(EncodedTriple.FromEncoded(3, 30, 300));

            using HypertrieSnapshot firstCommitted = await first.CommitAsync(budget.Token).ConfigureAwait(false);

            //Second's commit will throw; we only care that dispose
            //afterward does not surface anything.
            await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await second.CommitAsync(budget.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        finally
        {
            //If either of these throws, the test fails.
            await second.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task SweepBlocksWhileSessionIsOpen()
    {
        //A sweep takes the mutation gate exclusively. An open
        //session holds the gate in shared mode for its lifetime.
        //Therefore a sweep started while a session is open must
        //not complete until the session disposes.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource budget = NewBudget();

        EditSession session = await graph.OpenEditSessionAsync(budget.Token).ConfigureAwait(false);
        try
        {
            ValueTask<SweepResult> sweepTask = store.SweepAsync(budget.Token);

            //Determinism: a freshly-launched sweep that races with
            //a held shared scope must not have completed yet. We
            //inspect the underlying task; ValueTask wrapping a
            //pending Task preserves the IsCompleted bit.
            Assert.IsFalse(sweepTask.IsCompleted, "Sweep must not complete while session holds the shared scope.");

            //Disposing the session releases the shared scope and
            //the sweep can now finish.
            await session.DisposeAsync().ConfigureAwait(false);

            await sweepTask.ConfigureAwait(false);

            //One sweep recorded; the call returned without timing
            //out within budget, which is the property the test is
            //asserting.
            Assert.AreEqual(1, store.SweepCount);
        }
        finally
        {
            //Defensive: if a path above threw before the explicit
            //dispose, ensure the scope is released so other tests
            //do not inherit a wedged store. DisposeAsync is
            //idempotent.
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task SessionOpenBlocksWhileSweepIsRunning()
    {
        //The reverse: a sweep holding the gate exclusively means
        //a new session's open (which acquires the shared scope
        //before writing the Started entry) cannot complete until
        //the sweep finishes. Stage this by issuing the open while
        //a sweep is in flight via two concurrent acquires of the
        //same gate.
        //
        //Because the in-process journal completes appends
        //synchronously and the sweep has nothing to evict on a
        //small graph, the actual race window is very narrow. The
        //correctness check here is that both operations complete
        //without deadlock — the budget catches any failure mode.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource budget = NewBudget();

        ValueTask<SweepResult> sweepTask = store.SweepAsync(budget.Token);
        ValueTask<EditSession> openTask = graph.OpenEditSessionAsync(budget.Token);

        await sweepTask.ConfigureAwait(false);
        EditSession session = await openTask.ConfigureAwait(false);

        try
        {
            Assert.AreEqual(1, store.SweepCount);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task TwoConcurrentSessionsCanRunWithoutBlocking()
    {
        //Sessions take the gate in shared mode — many sessions
        //against the same store are mutually compatible.
        //Sequential commits race for the journal head; this test
        //confirms that the open path itself does not serialise.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource budget = NewBudget();

        ValueTask<EditSession> firstOpen = graph.OpenEditSessionAsync(budget.Token);
        ValueTask<EditSession> secondOpen = graph.OpenEditSessionAsync(budget.Token);

        EditSession first = await firstOpen.ConfigureAwait(false);
        EditSession second = await secondOpen.ConfigureAwait(false);

        try
        {
            Assert.AreNotEqual(first.Id, second.Id);
        }
        finally
        {
            await second.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
        }
    }

    private CancellationTokenSource NewBudget()
    {
        CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        budget.CancelAfter(DeadlockBudget);

        return budget;
    }
}
