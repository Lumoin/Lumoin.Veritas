using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// <see cref="ComputeWorkClass"/> is the project's first extensible "dynamic
/// enum" — a <see langword="readonly"/> <see langword="struct"/> over a
/// priority that is both identity and service order, with named built-in
/// instances and a <see cref="ComputeWorkClass.Create"/> extension point.
/// These pin the contract embedders rely on: a total priority order, equality
/// by priority, collision-rejecting registration, snapshot enumeration, and
/// a name fallback for custom classes.
/// </summary>
/// <remarks>
/// The registry is process-global and never reset, so every priority created
/// here is distinct across the whole class. The built-in span is 0..500, so
/// the custom priorities sit above it except where a test deliberately slots
/// a class between two built-ins to prove ordering.
/// </remarks>
[TestClass]
internal sealed class ComputeWorkClassTests
{
    /// <summary>A no-op turn body, since these tests exercise ordering and identity rather than execution.</summary>
    private static readonly ComputeWorkDelegate NoWork = _ => System.Threading.Tasks.ValueTask.CompletedTask;

    /// <summary>The expected dequeue order when a custom class is slotted between two built-ins.</summary>
    private static readonly int[] ExpectedSlottedOrder = [1, 2, 3];

    [TestMethod]
    public void BuiltInClassesOrderByPriorityWithTheTickFirstAndScrubLast()
    {
        //The control-plane tick is served before everything; scrub yields to everything.
        Assert.IsTrue(ComputeWorkClass.ControlPlaneTick < ComputeWorkClass.ViewBuild);
        Assert.IsTrue(ComputeWorkClass.ViewBuild < ComputeWorkClass.BulkSort);
        Assert.IsTrue(ComputeWorkClass.Reasoning < ComputeWorkClass.Scrub);
        Assert.IsTrue(ComputeWorkClass.ControlPlaneTick < ComputeWorkClass.Scrub);

        //The comparison and ordering operators agree.
        Assert.IsLessThan(0, ComputeWorkClass.ControlPlaneTick.CompareTo(ComputeWorkClass.Scrub));
        Assert.IsTrue(ComputeWorkClass.Scrub >= ComputeWorkClass.Reasoning);
        Assert.IsTrue(ComputeWorkClass.ViewBuild <= ComputeWorkClass.ViewBuild);
    }

    [TestMethod]
    public void EqualityIsByPriority()
    {
        ComputeWorkClass viewBuildFirst = ComputeWorkClass.ViewBuild;
        ComputeWorkClass viewBuildSecond = ComputeWorkClass.ViewBuild;

        Assert.AreEqual(viewBuildFirst, viewBuildSecond);
        Assert.AreNotEqual(ComputeWorkClass.ViewBuild, ComputeWorkClass.Scrub);
        Assert.IsTrue(ComputeWorkClass.ViewBuild == ComputeWorkClass.ViewBuild);
        Assert.IsTrue(ComputeWorkClass.ViewBuild != ComputeWorkClass.Scrub);
        Assert.AreEqual(ComputeWorkClass.ViewBuild.Priority, ComputeWorkClass.ViewBuild.GetHashCode());
    }

    [TestMethod]
    public void CreateRegistersACustomClassAtItsPriority()
    {
        ComputeWorkClass custom = ComputeWorkClass.Create(1501);

        Assert.AreEqual(1501, custom.Priority);
        Assert.Contains(custom, ComputeWorkClass.All);
    }

    [TestMethod]
    public void CreateRejectsADuplicatePriorityForBothCustomAndBuiltInClasses()
    {
        ComputeWorkClass.Create(1502);

        Assert.ThrowsExactly<ArgumentException>(() => ComputeWorkClass.Create(1502));
        Assert.ThrowsExactly<ArgumentException>(() => ComputeWorkClass.Create(ComputeWorkClass.ViewBuild.Priority));
    }

    [TestMethod]
    public void NamesCoverBuiltInsAndFallBackForACustomClass()
    {
        Assert.AreEqual("control_plane_tick", ComputeWorkClassNames.GetName(ComputeWorkClass.ControlPlaneTick));
        Assert.AreEqual("view_build", ComputeWorkClassNames.GetName(ComputeWorkClass.ViewBuild));
        Assert.AreEqual("scrub", ComputeWorkClassNames.GetName(ComputeWorkClass.Scrub));

        ComputeWorkClass custom = ComputeWorkClass.Create(1503);

        Assert.AreEqual("custom_1503", ComputeWorkClassNames.GetName(custom));
        Assert.AreEqual("custom_1503", custom.ToString());
    }

    [TestMethod]
    public void TheQueueServesACustomClassInItsPriorityOrder()
    {
        //A custom class slotted between ViewBuild (100) and BulkSort (200).
        ComputeWorkClass slotted = ComputeWorkClass.Create(150);
        BoundedComputeQueue queue = new(16);

        //Enqueued out of priority order; the queue must serve by priority.
        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(ComputeWorkClass.BulkSort, NoWork));
        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(slotted, NoWork));
        Assert.AreEqual(ComputeAdmission.Admitted, queue.TryEnqueue(ComputeWorkClass.ViewBuild, NoWork));

        int[] served = new int[3];
        Assert.IsTrue(queue.TryDequeue(out ComputeWorkClass first, out _));
        served[0] = first == ComputeWorkClass.ViewBuild ? 1 : 0;
        Assert.IsTrue(queue.TryDequeue(out ComputeWorkClass second, out _));
        served[1] = second == slotted ? 2 : 0;
        Assert.IsTrue(queue.TryDequeue(out ComputeWorkClass third, out _));
        served[2] = third == ComputeWorkClass.BulkSort ? 3 : 0;

        Assert.AreSequenceEqual(ExpectedSlottedOrder, served);
    }
}
