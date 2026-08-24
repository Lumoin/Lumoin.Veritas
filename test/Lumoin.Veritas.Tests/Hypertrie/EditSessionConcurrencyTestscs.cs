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
/// <remarks>
/// <para>
/// <b>No wall-clock budgets are injected as cancellation tokens.</b>
/// All operations are issued with <see cref="TestContext.CancellationToken"/>,
/// which fires only when the test runner cancels the run.
/// Deadlock-detection comes from explicit structural assertions
/// (e.g., <c>Assert.IsFalse(parked.IsCompleted, ...)</c>): if a
/// supposedly-parked operation has already completed, the gate is
/// broken and the test fails immediately. If a real deadlock did
/// occur, the runner's overall budget would terminate the hung
/// test; that is the runner's job, not the test body's, and a
/// per-test wall-clock heuristic injected as a CancellationToken
/// would conflate "deadlock" with "thread scheduling delay under
/// parallel test execution" — exactly the false-positive failure
/// mode the prior shape exhibited.
/// </para>
/// </remarks>
[TestClass]
internal sealed class EditSessionConcurrencyTests
{
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

        EditSession first = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        EditSession second = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            first.Add(EncodedTriple.FromEncoded(2, 20, 200));
            second.Add(EncodedTriple.FromEncoded(3, 30, 300));

            using HypertrieSnapshot firstCommitted = await first.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            EditSessionConcurrencyException thrown = await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await second.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false))
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

        EditSession first = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        EditSession second = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);

        try
        {
            first.Add(EncodedTriple.FromEncoded(2, 20, 200));
            second.Add(EncodedTriple.FromEncoded(3, 30, 300));

            using HypertrieSnapshot firstCommitted = await first.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            //Second's commit will throw; we only care that dispose
            //afterward does not surface anything.
            await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await second.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false))
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
        //
        //The structural assertion is Assert.IsFalse(sweepTask.IsCompleted, ...)
        //immediately after launching SweepAsync. If the gate were
        //broken (sweep grabbing the exclusive scope while the
        //shared holder remains), the ValueTask would be completed
        //synchronously and the assertion fires. This is the
        //race-free property the test is verifying; no wall-clock
        //budget participates in the assertion.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            ValueTask<SweepResult> sweepTask = store.SweepAsync(TestContext.CancellationToken);

            //Determinism: a freshly-launched sweep that races with
            //a held shared scope must not have completed yet. We
            //inspect the underlying task; ValueTask wrapping a
            //pending Task preserves the IsCompleted bit.
            Assert.IsFalse(sweepTask.IsCompleted, "Sweep must not complete while session holds the shared scope.");

            //Disposing the session releases the shared scope and
            //the sweep can now finish. The await without a per-call
            //budget means a real deadlock manifests as a hung test,
            //which is the runner's job to terminate via its own
            //per-run budget. This is the structurally correct
            //separation: the test verifies the gate property; the
            //runner verifies test liveness.
            await session.DisposeAsync().ConfigureAwait(false);
            await sweepTask.ConfigureAwait(false);

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
        //without deadlock; a hung test is caught by the runner's
        //per-run budget rather than a per-call cancellation.
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 10, 100)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        ValueTask<SweepResult> sweepTask = store.SweepAsync(TestContext.CancellationToken);
        ValueTask<EditSession> openTask = graph.OpenEditSessionAsync(TestContext.CancellationToken);

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

        ValueTask<EditSession> firstOpen = graph.OpenEditSessionAsync(TestContext.CancellationToken);
        ValueTask<EditSession> secondOpen = graph.OpenEditSessionAsync(TestContext.CancellationToken);

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
}
