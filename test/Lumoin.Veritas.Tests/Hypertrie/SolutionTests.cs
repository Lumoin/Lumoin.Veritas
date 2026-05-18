using Lumoin.Veritas.Core.Encoding;
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
        Solution solution = new([new(v, TermId.FromEncoded(42))]);

        bool found = solution.TryGetValue(v, out TermId value);

        Assert.IsTrue(found);
        Assert.AreEqual(TermId.FromEncoded(42), value);
    }

    [TestMethod]
    public void TryGetValueReturnsFalseForUnboundVariable()
    {
        Variable bound = new(0);
        Variable unbound = new(99);
        Solution solution = new([new(bound, TermId.FromEncoded(1))]);

        bool found = solution.TryGetValue(unbound, out TermId value);

        Assert.IsFalse(found);
        Assert.AreEqual(TermId.None, value);
    }

    [TestMethod]
    public void GetReturnsValueForBoundVariable()
    {
        Variable v = new(3);
        Solution solution = new([new(v, TermId.FromEncoded(100))]);

        Assert.AreEqual(TermId.FromEncoded(100), solution.Get(v));
    }

    [TestMethod]
    public void GetThrowsForUnboundVariable()
    {
        Variable bound = new(0);
        Variable unbound = new(99);
        Solution solution = new([new(bound, TermId.FromEncoded(1))]);

        Assert.Throws<ArgumentException>(() => solution.Get(unbound));
    }

    [TestMethod]
    public void MultipleBindingsAccessibleByVariable()
    {
        Variable a = new(0);
        Variable b = new(1);
        Variable c = new(2);

        Solution solution = new([new(a, TermId.FromEncoded(10)), new(b, TermId.FromEncoded(20)), new(c, TermId.FromEncoded(30))]);

        Assert.AreEqual(TermId.FromEncoded(10), solution.Get(a));
        Assert.AreEqual(TermId.FromEncoded(20), solution.Get(b));
        Assert.AreEqual(TermId.FromEncoded(30), solution.Get(c));
    }

    [TestMethod]
    public void ConstructorRejectsNullBindings()
    {
        Assert.Throws<ArgumentNullException>(() => new Solution(null!));
    }
}
