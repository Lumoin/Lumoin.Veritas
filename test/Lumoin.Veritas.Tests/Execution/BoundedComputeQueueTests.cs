using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The compute lane's bounded multi-class FIFO: shed-able classes admit
/// up to capacity then shed with a verdict, the reserved control-plane
/// tick is never bound out, dequeue is strict priority order and FIFO
/// within a class, and the per-class depth is observable. Exercised
/// single-threaded — the lane, not the queue, owns synchronisation.
/// </summary>
[TestClass]
internal sealed class BoundedComputeQueueTests
{
    /// <summary>The expected service order when priority and within-class FIFO both apply: two ViewBuilds (FIFO), then Reasoning, then Scrub.</summary>
    private static readonly int[] PriorityThenFifoOrder = [3, 4, 2, 1];

    /// <summary>The expected service order when a reserved tick is enqueued behind shed-able work: the tick first.</summary>
    private static readonly int[] TickServedFirstOrder = [2, 1];

    /// <summary>Builds a turn body that records its identifier into <paramref name="order"/> when run, so dequeue order is observable.</summary>
    /// <param name="order">The list each run appends to.</param>
    /// <param name="id">The identifier this turn records.</param>
    /// <returns>The turn body.</returns>
    private static ComputeWorkDelegate Recording(List<int> order, int id)
    {
        return _ =>
        {
            order.Add(id);

            return ValueTask.CompletedTask;
        };
    }

    /// <summary>A turn body that does nothing, for tests that only assert on admission and depth.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    private static ValueTask NoOp(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    [TestMethod]
    public void ConstructorRejectsNonPositiveCapacity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BoundedComputeQueue(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BoundedComputeQueue(-1));
    }

    [TestMethod]
    public void ShedableClassesAdmitUpToCapacityThenShed()
    {
        BoundedComputeQueue queue = new(2);

        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoOp));
        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(ComputeWorkClass.Reasoning, NoOp));

        //The bound is across the shed-able classes together, not per class.
        Assert.AreEqual(ComputeAdmission.ShedQueueFull, queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoOp));
        Assert.AreEqual(2, queue.Count);
    }

    [TestMethod]
    public async Task DequeueServesStrictPriorityOrderAndFifoWithinAClass()
    {
        BoundedComputeQueue queue = new(16);
        List<int> order = [];

        //Enqueued lowest-priority first and out of FIFO order across classes.
        queue.TryEnqueue(ComputeWorkClass.Scrub, Recording(order, 1));
        queue.TryEnqueue(ComputeWorkClass.Reasoning, Recording(order, 2));
        queue.TryEnqueue(ComputeWorkClass.ViewBuild, Recording(order, 3));
        queue.TryEnqueue(ComputeWorkClass.ViewBuild, Recording(order, 4));

        await DrainAll(queue).ConfigureAwait(false);

        //ViewBuild before Reasoning before Scrub (priority); 3 before 4 (FIFO within ViewBuild).
        Assert.AreSequenceEqual(PriorityThenFifoOrder, order);
    }

    [TestMethod]
    public void ControlPlaneTickIsAdmittedEvenWhenShedableClassesAreFull()
    {
        BoundedComputeQueue queue = new(1);

        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoOp));
        Assert.AreEqual(ComputeAdmission.ShedQueueFull, queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoOp));

        //The reserved tick sits outside the shed-able bound, so a quota re-read is never starved out.
        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(ComputeWorkClass.ControlPlaneTick, NoOp));
        Assert.AreEqual(1, queue.DepthOf(ComputeWorkClass.ControlPlaneTick));
    }

    [TestMethod]
    public async Task ControlPlaneTickIsServedBeforeShedableWork()
    {
        BoundedComputeQueue queue = new(16);
        List<int> order = [];

        queue.TryEnqueue(ComputeWorkClass.ViewBuild, Recording(order, 1));
        queue.TryEnqueue(ComputeWorkClass.ControlPlaneTick, Recording(order, 2));

        await DrainAll(queue).ConfigureAwait(false);

        Assert.AreSequenceEqual(TickServedFirstOrder, order);
    }

    [TestMethod]
    public void DequeueFromAnEmptyQueueReturnsFalse()
    {
        BoundedComputeQueue queue = new(4);

        Assert.IsFalse(queue.TryDequeue(out _, out _));
    }

    [TestMethod]
    public void DepthAndCountTrackEnqueueAndDequeue()
    {
        BoundedComputeQueue queue = new(8);

        queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoOp);
        queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoOp);
        queue.TryEnqueue(ComputeWorkClass.Scrub, NoOp);

        Assert.AreEqual(2, queue.DepthOf(ComputeWorkClass.ViewBuild));
        Assert.AreEqual(1, queue.DepthOf(ComputeWorkClass.Scrub));
        Assert.AreEqual(3, queue.Count);

        Assert.IsTrue(queue.TryDequeue(out ComputeWorkClass first, out _));
        Assert.AreEqual(ComputeWorkClass.ViewBuild, first);
        Assert.AreEqual(1, queue.DepthOf(ComputeWorkClass.ViewBuild));
        Assert.AreEqual(2, queue.Count);
    }

    /// <summary>Runs every queued turn to completion in dequeue order, so a recording turn body reveals the service order.</summary>
    /// <param name="queue">The queue to drain.</param>
    /// <returns>A task that completes when the queue is drained.</returns>
    private static async Task DrainAll(BoundedComputeQueue queue)
    {
        while(queue.TryDequeue(out _, out ComputeWorkDelegate? work))
        {
            await work(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
