using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class TermDictionaryTests
{
    [TestMethod]
    public void GetOrAddAssignsSequentialIds()
    {
        TermDictionary dictionary = new();
        NamedNode a = new(Utf8String.FromString("http://example.org/a"));
        NamedNode b = new(Utf8String.FromString("http://example.org/b"));

        long idA = dictionary.GetOrAdd(a);
        long idB = dictionary.GetOrAdd(b);

        Assert.AreEqual(0, idA);
        Assert.AreEqual(1, idB);
    }

    [TestMethod]
    public void GetOrAddReturnsSameIdForSameTerm()
    {
        TermDictionary dictionary = new();
        NamedNode node = new(Utf8String.FromString("http://example.org/same"));

        long first = dictionary.GetOrAdd(node);
        long second = dictionary.GetOrAdd(node);

        Assert.AreEqual(first, second);
        Assert.AreEqual(1, dictionary.Count);
    }

    [TestMethod]
    public void ResolveRoundTrips()
    {
        TermDictionary dictionary = new();
        BlankNode original = new(Utf8String.FromString("b0"));

        long id = dictionary.GetOrAdd(original);
        RdfTerm resolved = dictionary.Resolve(id);

        Assert.AreEqual(original, resolved);
    }

    [TestMethod]
    public void ResolveThrowsForInvalidId()
    {
        TermDictionary dictionary = new();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dictionary.Resolve(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dictionary.Resolve(-1));
    }

    [TestMethod]
    public void GetIdOrDefaultReturnsNegativeOneForMissingTerm()
    {
        TermDictionary dictionary = new();
        NamedNode missing = new(Utf8String.FromString("http://example.org/missing"));

        Assert.AreEqual(TermId.None, dictionary.GetIdOrDefault(missing));
    }

    [TestMethod]
    public void ContainsReturnsTrueForKnownTerm()
    {
        TermDictionary dictionary = new();
        NamedNode node = new(Utf8String.FromString("http://example.org/known"));
        dictionary.GetOrAdd(node);

        Assert.IsTrue(dictionary.Contains(node));
    }

    [TestMethod]
    public void ContainsReturnsFalseForUnknownTerm()
    {
        TermDictionary dictionary = new();
        NamedNode node = new(Utf8String.FromString("http://example.org/unknown"));

        Assert.IsFalse(dictionary.Contains(node));
    }
}