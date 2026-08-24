using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// One observable, bounded, multi-class FIFO backing the compute lane.
/// Work is served in strict priority order — the lowest-priority
/// <see cref="ComputeWorkClass"/> first, FIFO within a class — and
/// admission is bounded so overload sheds with a verdict rather than
/// growing without limit. The reserved
/// <see cref="ComputeWorkClass.ControlPlaneTick"/> sits outside the
/// shed-able bound, so a quota re-read is admitted and served even when
/// compute work is at capacity.
/// </summary>
/// <remarks>
/// <para>
/// Not internally synchronised: the compute lane owns the lock that
/// guards concurrent access (the single-shared-FIFO mutual-exclusion
/// discipline), so this stays a plain deterministic data structure
/// exercised single-threaded in isolation. Per-class backlogs live in a
/// dictionary keyed by the class and ordered by its priority, so the work
/// classes can be an open set (built-in plus consumer-created) rather than
/// a fixed enumeration; shed-able and reserved depth are kept in running
/// counters so admission is constant-time.
/// </para>
/// </remarks>
internal sealed class BoundedComputeQueue
{
    /// <summary>Per-class FIFO backlogs, ordered by class priority (lowest priority value served first); classes appear lazily on first enqueue.</summary>
    private readonly SortedDictionary<ComputeWorkClass, Queue<ComputeWorkDelegate>> backlogs = new();

    /// <summary>The running count of queued shed-able work (every class except the reserved control-plane tick), kept in step with the backlogs so admission is constant-time.</summary>
    private int shedableCount;

    /// <summary>The running count of queued reserved control-plane ticks.</summary>
    private int reservedCount;

    /// <summary>Constructs a queue bounding the shed-able classes at <paramref name="shedableCapacity"/>.</summary>
    /// <param name="shedableCapacity">The maximum queued shed-able work items; must be at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shedableCapacity"/> is less than one.</exception>
    public BoundedComputeQueue(int shedableCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shedableCapacity, 1);

        ShedableCapacity = shedableCapacity;
    }

    /// <summary>The capacity bounding the shed-able classes.</summary>
    public int ShedableCapacity { get; }

    /// <summary>The total queued items across all classes, reserved and shed-able.</summary>
    public int Count => shedableCount + reservedCount;

    /// <summary>The queued depth of a single class — the observable per-class backlog.</summary>
    /// <param name="workClass">The class to read.</param>
    /// <returns>The number of queued items of that class.</returns>
    public int DepthOf(ComputeWorkClass workClass)
    {
        return backlogs.TryGetValue(workClass, out Queue<ComputeWorkDelegate>? backlog) ? backlog.Count : 0;
    }

    /// <summary>
    /// Offers work to the queue. The reserved
    /// <see cref="ComputeWorkClass.ControlPlaneTick"/> is always admitted;
    /// a shed-able class is admitted only while the shed-able backlog is
    /// below capacity, and otherwise sheds with
    /// <see cref="ComputeAdmission.ShedQueueFull"/>.
    /// </summary>
    /// <param name="workClass">The work's priority class.</param>
    /// <param name="work">The turn body.</param>
    /// <returns>The admission verdict.</returns>
    public ComputeAdmission TryEnqueue(ComputeWorkClass workClass, ComputeWorkDelegate work)
    {
        ArgumentNullException.ThrowIfNull(work);

        bool reserved = workClass == ComputeWorkClass.ControlPlaneTick;
        if(!reserved && shedableCount >= ShedableCapacity)
        {
            return ComputeAdmission.ShedQueueFull;
        }

        if(!backlogs.TryGetValue(workClass, out Queue<ComputeWorkDelegate>? backlog))
        {
            backlog = new Queue<ComputeWorkDelegate>();
            backlogs[workClass] = backlog;
        }

        backlog.Enqueue(work);
        if(reserved)
        {
            reservedCount++;
        }
        else
        {
            shedableCount++;
        }

        return ComputeAdmission.Admitted;
    }

    /// <summary>
    /// Removes and returns the highest-priority queued turn — the
    /// lowest-priority-value non-empty class, FIFO within it.
    /// </summary>
    /// <param name="workClass">The dequeued turn's class, when one was available.</param>
    /// <param name="work">The dequeued turn body, when one was available.</param>
    /// <returns><c>true</c> when a turn was dequeued; <c>false</c> when the queue is empty.</returns>
    public bool TryDequeue(out ComputeWorkClass workClass, [MaybeNullWhen(false)] out ComputeWorkDelegate work)
    {
        foreach(KeyValuePair<ComputeWorkClass, Queue<ComputeWorkDelegate>> entry in backlogs)
        {
            if(entry.Value.Count == 0)
            {
                continue;
            }

            work = entry.Value.Dequeue();
            workClass = entry.Key;
            if(workClass == ComputeWorkClass.ControlPlaneTick)
            {
                reservedCount--;
            }
            else
            {
                shedableCount--;
            }

            return true;
        }

        workClass = default;
        work = null;

        return false;
    }
}
