using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class BuildPoolsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void CreateDefaultReturnsNonNullFields()
    {
        BuildPools pools = BuildPools.CreateDefault();
        Assert.IsNotNull(pools.KeyPool);
        Assert.IsNotNull(pools.ChildPool);
        Assert.IsNotNull(pools.NodePool);
        Assert.IsNotNull(pools.InlineLookup);
    }

    [TestMethod]
    public void CreateDefaultInlineLookupIsCallable()
    {
        BuildPools pools = BuildPools.CreateDefault();
        int result = pools.InlineLookup([10u, 20u, 30u], 20u);
        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void CreateDefaultInlineLookupReturnsMinusOneForMissingKey()
    {
        BuildPools pools = BuildPools.CreateDefault();
        int result = pools.InlineLookup([10u, 20u, 30u], 99u);
        Assert.AreEqual(-1, result);
    }
}
