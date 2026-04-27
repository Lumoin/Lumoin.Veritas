using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class SolutionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptySolutionHasZeroBindings()
    {
        Solution solution = new([]);

        Assert.IsEmpty(solution.Bindings);
    }

    [TestMethod]
    public void TryGetValueReturnsTrueForBoundVariable()
    {
        Variable v = new(7);
        Solution solution = new([new(v, 42)]);

        bool found = solution.TryGetValue(v, out long value);

        Assert.IsTrue(found);
        Assert.AreEqual(42L, value);
    }

    [TestMethod]
    public void TryGetValueReturnsFalseForUnboundVariable()
    {
        Variable bound = new(0);
        Variable unbound = new(99);
        Solution solution = new([new(bound, 1)]);

        bool found = solution.TryGetValue(unbound, out long value);

        Assert.IsFalse(found);
        Assert.AreEqual(0L, value);
    }

    [TestMethod]
    public void GetReturnsValueForBoundVariable()
    {
        Variable v = new(3);
        Solution solution = new([new(v, 100)]);

        Assert.AreEqual(100L, solution.Get(v));
    }

    [TestMethod]
    public void GetThrowsForUnboundVariable()
    {
        Variable bound = new(0);
        Variable unbound = new(99);
        Solution solution = new([new(bound, 1)]);

        Assert.Throws<ArgumentException>(() => solution.Get(unbound));
    }

    [TestMethod]
    public void MultipleBindingsAccessibleByVariable()
    {
        Variable a = new(0);
        Variable b = new(1);
        Variable c = new(2);

        Solution solution = new([new(a, 10), new(b, 20), new(c, 30)]);

        Assert.AreEqual(10L, solution.Get(a));
        Assert.AreEqual(20L, solution.Get(b));
        Assert.AreEqual(30L, solution.Get(c));
    }

    [TestMethod]
    public void ConstructorRejectsNullBindings()
    {
        Assert.Throws<ArgumentNullException>(() => new Solution(null!));
    }
}
