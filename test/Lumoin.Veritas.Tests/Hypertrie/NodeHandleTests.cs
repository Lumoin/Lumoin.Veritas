using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class NodeHandleTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NoneEqualsDefault() => Assert.AreEqual(default(NodeHandle), NodeHandle.None);

    [TestMethod]
    public void FromEncodedZeroIsNone() => Assert.IsTrue(NodeHandle.FromEncoded(0).IsNone);

    [TestMethod]
    public void FromEncodedNonZeroIsNotNone() => Assert.IsFalse(NodeHandle.FromEncoded(1).IsNone);

    [TestMethod]
    public void EqualEncodedValuesAreEqual() => Assert.AreEqual(NodeHandle.FromEncoded(42), NodeHandle.FromEncoded(42));

    [TestMethod]
    public void DifferentEncodedValuesAreNotEqual() => Assert.AreNotEqual(NodeHandle.FromEncoded(1), NodeHandle.FromEncoded(2));
}
