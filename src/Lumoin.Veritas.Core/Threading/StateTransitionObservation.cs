using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Threading;

/// <summary>
/// A test-armed observation seam over a background loop's progress: an
/// observer arms the seam and awaits the returned task, and the loop signals
/// once per completed transition. Unarmed — the production state — a signal
/// costs a single volatile read; the observer awaits a transition
/// deterministically, bounded by its own token rather than a wall clock.
/// </summary>
internal sealed class StateTransitionObservation
{
    /// <summary>Set when an observer arms the seam; never cleared, so signaling stays a single volatile read until the first observer arrives.</summary>
    private volatile bool observing;

    /// <summary>Completed and replaced on each signaled transition while the seam is armed, so every armed await sees exactly one transition. Accessed via interlocked exchange.</summary>
    private TaskCompletionSource advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Arms the seam and returns the task completing on the next signaled transition.</summary>
    /// <returns>The task completing on the next signaled transition.</returns>
    public Task Observe()
    {
        observing = true;

        return Volatile.Read(ref advanced).Task;
    }

    /// <summary>Completes the current observation task and installs a fresh one, waking any armed observer; a no-op until the seam is armed. Continuations run asynchronously, so signaling while holding a lock is safe.</summary>
    public void Signal()
    {
        if(!observing)
        {
            return;
        }

        Interlocked.Exchange(ref advanced, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }
}
