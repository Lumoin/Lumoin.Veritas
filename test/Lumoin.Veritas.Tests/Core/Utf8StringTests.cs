using Lumoin.Veritas.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class Utf8StringTests
{
    [TestMethod]
    public void EqualInstancesProduceSameHashCode()
    {
        byte[] bytes = "http://example.org/test"u8.ToArray();
        Utf8String a = new(bytes);
        Utf8String b = new(bytes.AsMemory());

        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void EqualInstancesAreEqual()
    {
        byte[] bytes1 = "http://example.org/test"u8.ToArray();
        byte[] bytes2 = "http://example.org/test"u8.ToArray();
        Utf8String a = new(bytes1);
        Utf8String b = new(bytes2);

        Assert.IsTrue(a == b);
        Assert.IsTrue(a.Equals(b));
    }

    [TestMethod]
    public void DifferentInstancesAreNotEqual()
    {
        Utf8String a = new("alpha"u8.ToArray());
        Utf8String b = new("beta"u8.ToArray());

        Assert.IsTrue(a != b);
        Assert.IsFalse(a.Equals(b));
    }

    [TestMethod]
    public void CompareToReturnsCorrectOrdering()
    {
        Utf8String a = new("aaa"u8.ToArray());
        Utf8String b = new("bbb"u8.ToArray());

        Assert.IsLessThan(0, a.CompareTo(b));
        Assert.IsGreaterThan(0, b.CompareTo(a));
        Assert.AreEqual(0, a.CompareTo(a));
    }

    [TestMethod]
    public void ToStringDecodesUtf8()
    {
        Utf8String s = new("héllo"u8.ToArray());

        Assert.AreEqual("héllo", s.ToString());
    }

    [TestMethod]
    public void FromStringRoundTrips()
    {
        const string original = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
        Utf8String s = Utf8Strings.From(original);

        Assert.AreEqual(original, s.ToString());
    }

    [TestMethod]
    public void EmptyInstanceReportsIsEmpty()
    {
        Utf8String empty = new(ReadOnlyMemory<byte>.Empty);

        Assert.IsTrue(empty.IsEmpty);
        Assert.AreEqual(0, empty.Length);
    }

    [TestMethod]
    public void LengthReflectsByteCount()
    {
        //The é character is two bytes in UTF-8.
        Utf8String s = new("café"u8.ToArray());

        Assert.AreEqual(5, s.Length);
    }
}
