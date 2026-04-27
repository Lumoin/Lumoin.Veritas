using Lumoin.Veritas.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class Utf8StringPoolTests
{
    [TestMethod]
    public void InternReturnsSameInstanceForDuplicateBytes()
    {
        using Utf8StringPool pool = new();

        Utf8String first = pool.Intern("http://example.org/resource"u8);
        Utf8String second = pool.Intern("http://example.org/resource"u8);

        Assert.AreEqual(first, second);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void InternDistinguishesDifferentValues()
    {
        using Utf8StringPool pool = new();

        Utf8String a = pool.Intern("alpha"u8);
        Utf8String b = pool.Intern("beta"u8);

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(2, pool.Count);
    }

    [TestMethod]
    public void InternStringEncodesAsUtf8()
    {
        using Utf8StringPool pool = new();

        Utf8String interned = pool.Intern("hello");

        Assert.AreEqual("hello", interned.ToString());
        Assert.AreEqual(5, interned.Length);
    }

    [TestMethod]
    public void InternStringDeduplicatesWithByteVersion()
    {
        using Utf8StringPool pool = new();

        Utf8String fromBytes = pool.Intern("test"u8);
        Utf8String fromString = pool.Intern("test");

        Assert.AreEqual(fromBytes, fromString);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void DisposePreventsFurtherInterning()
    {
        Utf8StringPool pool = new();
        pool.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => pool.Intern("test"u8));
    }

    [TestMethod]
    public void HandlesLargeValuesSpanningMultipleBuffers()
    {
        //Use a tiny initial buffer to force multiple allocations.
        using Utf8StringPool pool = new(initialBufferSize: 16);

        Utf8String a = pool.Intern("this-is-a-longer-string-than-the-buffer"u8);
        Utf8String b = pool.Intern("another-long-string-that-exceeds-capacity"u8);

        Assert.AreEqual("this-is-a-longer-string-than-the-buffer", a.ToString());
        Assert.AreEqual("another-long-string-that-exceeds-capacity", b.ToString());
        Assert.AreEqual(2, pool.Count);
    }

    [TestMethod]
    public void DoubleDisposeDoesNotThrow()
    {
        Utf8StringPool pool = new();
        pool.Dispose();
        pool.Dispose();
    }
}