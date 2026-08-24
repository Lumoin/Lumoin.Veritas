using Lumoin.Veritas.Core.Sourcing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class SourceSpanExtensionsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NoneSentinelHasAllZeroEndpoints()
    {
        SourceSpan none = SourceSpan.None;

        Assert.AreEqual(0, none.StartByte);
        Assert.AreEqual(0, none.EndByte);
        Assert.AreEqual(0, none.StartLine);
        Assert.AreEqual(0, none.StartColumn);
        Assert.AreEqual(0, none.EndLine);
        Assert.AreEqual(0, none.EndColumn);
    }

    [TestMethod]
    public void ByteLengthIsEndMinusStart()
    {
        SourceSpan span = new(StartByte: 10, EndByte: 25, StartLine: 0, StartColumn: 0, EndLine: 0, EndColumn: 15);

        Assert.AreEqual(15, span.ByteLength);
    }

    [TestMethod]
    public void NoneHasZeroByteLength()
    {
        Assert.AreEqual(0, SourceSpan.None.ByteLength);
    }
}
