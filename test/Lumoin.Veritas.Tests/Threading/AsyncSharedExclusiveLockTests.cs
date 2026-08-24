using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Tests.Threading;

[TestClass]
internal sealed class AsyncSharedExclusiveLockTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NewLockReportsZeroSharedHolders()
    {
        using AsyncSharedExclusiveLock gate = new();

        Assert.AreEqual(0, gate.SharedCount);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [TestMethod]
    public async Task SharedAcquireAndReleaseRestoresZeroCount()
    {
        using AsyncSharedExclusiveLock gate = new();
        SharedScope scope = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            Assert.AreEqual(1, gate.SharedCount);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }

        Assert.AreEqual(0, gate.SharedCount);
    }

    [TestMethod]
    public async Task MultipleSharedHoldersRunConcurrently()
    {
        using AsyncSharedExclusiveLock gate = new();
        SharedScope a = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);
        SharedScope b = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);
        SharedScope c = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3, gate.SharedCount);

        await a.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(2, gate.SharedCount);

        await b.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(1, gate.SharedCount);

        await c.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(0, gate.SharedCount);
    }

    [TestMethod]
    public async Task ExclusiveBlocksWhileSharedHeld()
    {
        using AsyncSharedExclusiveLock gate = new();
        SharedScope shared = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);

        //An exclusive acquirer started while a shared holder is
        //active cannot complete synchronously — the lock would
        //have to grant exclusive while shared is held to do so.
        //Observing IsCompleted=false at the moment of return is
        //a deterministic proof of contention; a Task.Delay would
        //add nothing but a wall-clock guess.
        ValueTask<ExclusiveScope> exclusivePending = gate.EnterExclusiveAsync(TestContext.CancellationToken);
        Assert.IsFalse(exclusivePending.IsCompleted, "Exclusive must not complete while a shared holder is active.");

        await shared.DisposeAsync().ConfigureAwait(false);

        ExclusiveScope exclusive = await exclusivePending.ConfigureAwait(false);
        try
        {
            Assert.AreEqual(0, gate.SharedCount);
        }
        finally
        {
            await exclusive.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task SharedBlocksWhileExclusiveHeld()
    {
        using AsyncSharedExclusiveLock gate = new();
        ExclusiveScope exclusive = await gate.EnterExclusiveAsync(TestContext.CancellationToken).ConfigureAwait(false);

        ValueTask<SharedScope> sharedPending = gate.EnterSharedAsync(TestContext.CancellationToken);
        Assert.IsFalse(sharedPending.IsCompleted, "Shared must not complete while an exclusive holder is active.");

        await exclusive.DisposeAsync().ConfigureAwait(false);

        SharedScope shared = await sharedPending.ConfigureAwait(false);
        try
        {
            Assert.AreEqual(1, gate.SharedCount);
        }
        finally
        {
            await shared.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ExclusiveHasWriterPriorityOverNewShared()
    {
        using AsyncSharedExclusiveLock gate = new();
        SharedScope first = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);

        //Exclusive arrives — must wait on first shared but should
        //also block any new shared holder from forming.
        ValueTask<ExclusiveScope> exclusivePending = gate.EnterExclusiveAsync(TestContext.CancellationToken);
        Assert.IsFalse(exclusivePending.IsCompleted, "Exclusive must not complete while a shared holder is active.");

        ValueTask<SharedScope> latecomerPending = gate.EnterSharedAsync(TestContext.CancellationToken);
        Assert.IsFalse(latecomerPending.IsCompleted, "A shared acquirer arriving after an exclusive waiter must not jump the queue.");

        //Release the first shared holder — exclusive should run next.
        await first.DisposeAsync().ConfigureAwait(false);

        ExclusiveScope exclusive = await exclusivePending.ConfigureAwait(false);

        //Latecomer is still waiting — exclusive holds the gate.
        Assert.IsFalse(latecomerPending.IsCompleted, "Latecomer must not complete while exclusive holds the gate.");

        //Release exclusive — latecomer should run.
        await exclusive.DisposeAsync().ConfigureAwait(false);

        SharedScope latecomer = await latecomerPending.ConfigureAwait(false);
        try
        {
            //Reaching here proves the latecomer was released after exclusive disposed.
        }
        finally
        {
            await latecomer.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task SharedHonoursCancellation()
    {
        using AsyncSharedExclusiveLock gate = new();
        //Hold the gate exclusively so the next shared acquire blocks.
        ExclusiveScope exclusive = await gate.EnterExclusiveAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource cts = new();
            ValueTask<SharedScope> pending = gate.EnterSharedAsync(cts.Token);
            Assert.IsFalse(pending.IsCompleted);

            await cts.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        finally
        {
            await exclusive.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ExclusiveHonoursCancellation()
    {
        using AsyncSharedExclusiveLock gate = new();
        SharedScope shared = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource cts = new();
            ValueTask<ExclusiveScope> pending = gate.EnterExclusiveAsync(cts.Token);
            Assert.IsFalse(pending.IsCompleted);

            await cts.CancelAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        finally
        {
            await shared.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CancelledSharedAcquireDoesNotLeakPermits()
    {
        //After a cancelled shared acquire, the gate must still be
        //in a state where future acquires succeed without leaking
        //a permit.
        using AsyncSharedExclusiveLock gate = new();
        ExclusiveScope blocker = await gate.EnterExclusiveAsync(TestContext.CancellationToken).ConfigureAwait(false);

        using CancellationTokenSource cts = new();
        ValueTask<SharedScope> cancelled = gate.EnterSharedAsync(cts.Token);
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelled.ConfigureAwait(false))
            .ConfigureAwait(false);

        await blocker.DisposeAsync().ConfigureAwait(false);

        //After releasing the blocker, future acquires must work.
        SharedScope follow = await gate.EnterSharedAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            Assert.AreEqual(1, gate.SharedCount);
        }
        finally
        {
            await follow.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ManyConcurrentSharedHoldersAllProceed()
    {
        //Stress the multi-shared path with a meaningful concurrency
        //level, ensuring the SharedCount tracks correctly.
        const int holders = 32;

        using AsyncSharedExclusiveLock gate = new();
        Task<SharedScope>[] tasks = new Task<SharedScope>[holders];

        for(int i = 0; i < holders; i++)
        {
            tasks[i] = gate.EnterSharedAsync(TestContext.CancellationToken).AsTask();
        }

        SharedScope[] scopes = await Task.WhenAll(tasks).ConfigureAwait(false);

        Assert.AreEqual(holders, gate.SharedCount);

        foreach(SharedScope scope in scopes)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }

        Assert.AreEqual(0, gate.SharedCount);
    }

    [TestMethod]
    public async Task DefaultSharedScopeDisposeAsyncIsNoOp()
    {
        SharedScope defaultScope = default;

        await defaultScope.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DefaultExclusiveScopeDisposeAsyncIsNoOp()
    {
        ExclusiveScope defaultScope = default;

        await defaultScope.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public async Task InterleavedSharedAndExclusiveAcquiresDoNotDeadlock()
    {
        //Guards against deadlock in the lock's release path. The
        //test interleaves rounds of N concurrent shared acquires
        //(all released) with one exclusive acquire (also released),
        //repeated rounds times. A misformed release — for example,
        //failing to signal the drain gate when the last shared
        //holder leaves — would leave a subsequent exclusive
        //acquire blocked forever.
        //
        //The work runs as a Task awaited bound only by the test
        //cancellation token. A misformed release path blocks the
        //await forever, and the runner-level hang guard — not an
        //in-test deadline — owns that case, so no pass/fail here
        //rides elapsed time.
        const int rounds = 8;
        const int sharedHoldersPerRound = 8;

        using AsyncSharedExclusiveLock gate = new();

        Task work = Task.Run(async () =>
        {
            for(int round = 0; round < rounds; round++)
            {
                Task<SharedScope>[] sharedTasks = new Task<SharedScope>[sharedHoldersPerRound];
                for(int i = 0; i < sharedTasks.Length; i++)
                {
                    sharedTasks[i] = gate.EnterSharedAsync(TestContext.CancellationToken).AsTask();
                }
                SharedScope[] sharedScopes = await Task.WhenAll(sharedTasks).ConfigureAwait(false);
                foreach(SharedScope s in sharedScopes)
                {
                    await s.DisposeAsync().ConfigureAwait(false);
                }

                ExclusiveScope ex = await gate.EnterExclusiveAsync(TestContext.CancellationToken).ConfigureAwait(false);
                await ex.DisposeAsync().ConfigureAwait(false);
            }
        }, TestContext.CancellationToken);

        await work.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
    }
}
