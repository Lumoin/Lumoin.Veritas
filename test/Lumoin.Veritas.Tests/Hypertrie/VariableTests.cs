using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Hypertrie;


[TestClass]
internal sealed class VariableTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EqualIdsCompareEqual()
    {
        Variable left = new(7);
        Variable right = new(7);

        Assert.AreEqual(left, right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
    }

    [TestMethod]
    public void DifferentIdsCompareUnequal()
    {
        Variable left = new(1);
        Variable right = new(2);

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void DefaultVariableHasZeroId()
    {
        Variable variable = default;

        Assert.AreEqual(0, variable.Id);
    }
}