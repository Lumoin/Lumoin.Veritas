using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The linearizability leg of the Jepsen-in-process consistency harness. Many actors concurrently flip and read a
/// single shared triple (modelled as a boolean register: INSERT = write true, DELETE = write false, ASK = read),
/// recording a real-time history; afterwards the history must be linearizable — there must exist a total order of
/// the operations consistent with their real-time precedence under which every read returns the value of the most
/// recent preceding write. This is the runnable companion to <c>spec/CommitPublishFeed.tla</c>: the TLA+ model
/// proves the protocol; this falsifies non-linearizable behaviour over actual recorded executions. The checker is
/// proven non-vacuous by the hand-crafted positive and negative cases below.
/// </summary>
[TestClass]
internal sealed class LinearizabilityHarnessTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>A logical clock: each invoke and return increments it, giving every operation a call/return tick whose order is a sound coarsening of real time (if one operation's return tick precedes another's call tick, it truly completed before the other began).</summary>
    //A naked field: Interlocked.Increment requires a ref to the storage location.
    private long historyClock;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The kind of a recorded operation.</summary>
    private enum OperationKind
    {
        /// <summary>A write: INSERT (value true) or DELETE (value false) of the shared triple.</summary>
        Write = 0,

        /// <summary>A read: ASK of the shared triple, whose observed presence is the recorded value.</summary>
        Read = 1,
    }

    /// <summary>One recorded operation in a concurrent history.</summary>
    /// <param name="Process">The actor that issued the operation.</param>
    /// <param name="Kind">Whether the operation is a write or a read.</param>
    /// <param name="Value">For a write, the value written; for a read, the value observed.</param>
    /// <param name="Call">The logical tick at which the operation was invoked.</param>
    /// <param name="Return">The logical tick at which the operation returned.</param>
    private readonly record struct HistoryEvent(int Process, OperationKind Kind, bool Value, long Call, long Return);

    /// <summary>The next logical clock tick.</summary>
    /// <returns>A strictly increasing tick.</returns>
    private long NextTick()
    {
        return Interlocked.Increment(ref historyClock);
    }

    /// <summary>Drives one actor: a deterministic mix of writes (INSERT/DELETE) and reads (ASK) on the one shared triple, recording each as a real-time-stamped history event.</summary>
    /// <param name="database">The shared mutable database.</param>
    /// <param name="process">The actor's index, which seeds its deterministic op sequence.</param>
    /// <param name="opsPerActor">The number of operations the actor issues.</param>
    /// <param name="history">The shared history sink.</param>
    /// <returns>The actor's completion.</returns>
    private async Task RunActorAsync(VeritasEngine database, int process, int opsPerActor, ConcurrentQueue<HistoryEvent> history)
    {
        for(int op = 0; op < opsPerActor; op++)
        {
            //Two of every three operations are writes (alternating value); the third is a read. The mix and the
            //shared target create contention: concurrent writers flip the triple while readers observe it.
            bool isRead = op % 3 == 2;

            if(isRead)
            {
                long call = NextTick();
                bool observed = await database
                    .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}s> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                long ret = NextTick();
                history.Enqueue(new HistoryEvent(process, OperationKind.Read, observed, call, ret));
            }
            else
            {
                bool value = ((process + op) & 1) == 0;
                string verb = value ? "INSERT" : "DELETE";
                long call = NextTick();
                await database
                    .UpdateAsync(Utf8Strings.From($"{verb} DATA {{ <{Ex}s> <{Ex}p> <{Ex}o> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                long ret = NextTick();
                history.Enqueue(new HistoryEvent(process, OperationKind.Write, value, call, ret));
            }
        }
    }

    /// <summary>A concurrent register history recorded over a real mutable engine is linearizable for every run.</summary>
    [TestMethod]
    public async Task ConcurrentRegisterHistoryIsLinearizable()
    {
        const int Actors = 5;
        const int OpsPerActor = 6;

        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ConcurrentQueue<HistoryEvent> history = new();
        Task[] actors = new Task[Actors];
        for(int actor = 0; actor < Actors; actor++)
        {
            actors[actor] = RunActorAsync(database, actor, OpsPerActor, history);
        }

        await Task.WhenAll(actors).ConfigureAwait(false);

        HistoryEvent[] recorded = [.. history];
        Assert.IsTrue(
            RegisterLinearizability.IsLinearizable(recorded, initialState: false),
            $"The recorded concurrent history of {recorded.Length} operations must be linearizable.");
    }

    /// <summary>The checker accepts a history that has a valid linearization, including one where a read overlaps a write and is ordered before it.</summary>
    [TestMethod]
    public void CheckerAcceptsALinearizableHistory()
    {
        //W(true) spans ticks 1..4; R()=false spans 2..3, fully inside the write — it can be linearized before the
        //write (register still false), then the write. A valid order exists.
        HistoryEvent[] history =
        [
            new HistoryEvent(0, OperationKind.Write, true, 1, 4),
            new HistoryEvent(1, OperationKind.Read, false, 2, 3),
        ];

        Assert.IsTrue(RegisterLinearizability.IsLinearizable(history, initialState: false));
    }

    /// <summary>The checker rejects a history with no valid linearization — a read that returns false strictly after a write of true has completed, with no intervening write.</summary>
    [TestMethod]
    public void CheckerRejectsANonLinearizableHistory()
    {
        //W(true) returns at tick 2; R()=false is invoked at tick 3 — strictly after the write completed — so the
        //write must precede the read, which then cannot observe false. No valid order exists.
        HistoryEvent[] history =
        [
            new HistoryEvent(0, OperationKind.Write, true, 1, 2),
            new HistoryEvent(1, OperationKind.Read, false, 3, 4),
        ];

        Assert.IsFalse(RegisterLinearizability.IsLinearizable(history, initialState: false));
    }

    /// <summary>
    /// An iterative (non-recursive) Wing-Gong linearizability checker for a boolean register. It searches for a
    /// total order of the operations that extends the real-time precedence (operation A before B when A returned
    /// before B was invoked) and satisfies the sequential register spec (a read returns the last written value).
    /// Backtracking uses an explicit stack; failed (remaining-set, state) pairs are memoised, and only operations
    /// minimal in the precedence order are tried next, which keeps the search tractable for modest histories.
    /// </summary>
    private static class RegisterLinearizability
    {
        /// <summary>One node of the backtracking search.</summary>
        private sealed class SearchFrame
        {
            /// <summary>Initializes a frame.</summary>
            /// <param name="remaining">The bitmask of operations not yet linearized.</param>
            /// <param name="state">The register state reached by the operations linearized so far.</param>
            public SearchFrame(long remaining, bool state)
            {
                Remaining = remaining;
                State = state;
                Cursor = 0;
            }

            /// <summary>The bitmask of operations not yet linearized at this node.</summary>
            public long Remaining { get; }

            /// <summary>The register state reached by the operations linearized before this node.</summary>
            public bool State { get; }

            /// <summary>The next operation index to consider as the candidate to linearize next.</summary>
            public int Cursor { get; set; }
        }

        /// <summary>Determines whether a recorded history is linearizable against the boolean-register spec.</summary>
        /// <param name="history">The recorded operations, in any order (their call/return ticks carry the real-time precedence).</param>
        /// <param name="initialState">The register's value before any operation.</param>
        /// <returns><see langword="true"/> when a valid linearization exists.</returns>
        public static bool IsLinearizable(IReadOnlyList<HistoryEvent> history, bool initialState)
        {
            int count = history.Count;
            if(count == 0)
            {
                return true;
            }

            if(count > 62)
            {
                throw new System.ArgumentException("The history is too large for the bitmask search; keep it under 63 operations.", nameof(history));
            }

            long full = (1L << count) - 1;
            HashSet<(long Remaining, bool State)> failed = [];
            Stack<SearchFrame> stack = new();
            stack.Push(new SearchFrame(full, initialState));

            while(stack.Count > 0)
            {
                SearchFrame frame = stack.Peek();
                if(frame.Remaining == 0)
                {
                    return true;
                }

                bool descended = false;
                while(frame.Cursor < count)
                {
                    int index = frame.Cursor;
                    frame.Cursor++;

                    long bit = 1L << index;
                    if((frame.Remaining & bit) == 0 || !IsMinimal(history, frame.Remaining, index))
                    {
                        continue;
                    }

                    HistoryEvent candidate = history[index];
                    bool childState;
                    if(candidate.Kind == OperationKind.Read)
                    {
                        //A read can be linearized here only if it observed the current register state.
                        if(candidate.Value != frame.State)
                        {
                            continue;
                        }

                        childState = frame.State;
                    }
                    else
                    {
                        childState = candidate.Value;
                    }

                    long childRemaining = frame.Remaining & ~bit;
                    if(failed.Contains((childRemaining, childState)))
                    {
                        continue;
                    }

                    stack.Push(new SearchFrame(childRemaining, childState));
                    descended = true;
                    break;
                }

                if(!descended)
                {
                    //This (remaining, state) node is a dead end: record it so no other path re-explores it.
                    failed.Add((frame.Remaining, frame.State));
                    stack.Pop();
                }
            }

            return false;
        }

        /// <summary>Determines whether an operation is minimal in the real-time precedence among the remaining operations — no remaining operation is required to precede it (returned before it was invoked).</summary>
        /// <param name="history">The recorded operations.</param>
        /// <param name="remaining">The bitmask of operations not yet linearized.</param>
        /// <param name="index">The candidate operation's index.</param>
        /// <returns><see langword="true"/> when the candidate may be linearized next.</returns>
        private static bool IsMinimal(IReadOnlyList<HistoryEvent> history, long remaining, int index)
        {
            long candidateCall = history[index].Call;
            for(int other = 0; other < history.Count; other++)
            {
                if(other == index || (remaining & (1L << other)) == 0)
                {
                    continue;
                }

                //An operation that returned before the candidate was invoked must precede it, so the candidate is
                //not minimal while that operation is still unlinearized.
                if(history[other].Return < candidateCall)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
