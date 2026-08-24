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
        NamedNode a = new(Utf8Strings.From("http://example.org/a"));
        NamedNode b = new(Utf8Strings.From("http://example.org/b"));

        IriId idA = dictionary.GetOrAdd(a);
        IriId idB = dictionary.GetOrAdd(b);

        //External identifiers are 1-based; 0 is reserved for TermId.None.
        Assert.AreEqual(1U, idA.Encoded);
        Assert.AreEqual(2U, idB.Encoded);
    }

    [TestMethod]
    public void GetOrAddReturnsSameIdForSameTerm()
    {
        TermDictionary dictionary = new();
        NamedNode node = new(Utf8Strings.From("http://example.org/same"));

        IriId first = dictionary.GetOrAdd(node);
        IriId second = dictionary.GetOrAdd(node);

        Assert.AreEqual(first.Encoded, second.Encoded);
        Assert.AreEqual(1, dictionary.Count);
    }

    [TestMethod]
    public void ResolveRoundTrips()
    {
        TermDictionary dictionary = new();
        BlankNode original = new(Utf8Strings.From("b0"));

        BlankNodeId id = dictionary.GetOrAdd(original);
        RdfTerm resolved = dictionary.Resolve(id.Value);

        Assert.AreEqual(original, resolved);
    }

    [TestMethod]
    public void ResolveThrowsForInvalidId()
    {
        TermDictionary dictionary = new();

        //Zero is the TermId.None sentinel and never resolves to a term.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dictionary.Resolve(0U));
        //Counts beyond the dictionary's range also throw.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => dictionary.Resolve(1U));
    }

    [TestMethod]
    public void GetIdOrDefaultReturnsNoneForMissingTerm()
    {
        TermDictionary dictionary = new();
        NamedNode missing = new(Utf8Strings.From("http://example.org/missing"));

        Assert.AreEqual(TermId.None, dictionary.GetIdOrDefault(missing));
    }

    [TestMethod]
    public void ContainsReturnsTrueForKnownTerm()
    {
        TermDictionary dictionary = new();
        NamedNode node = new(Utf8Strings.From("http://example.org/known"));
        dictionary.GetOrAdd(node);

        Assert.IsTrue(dictionary.Contains(node));
    }

    [TestMethod]
    public void ContainsReturnsFalseForUnknownTerm()
    {
        TermDictionary dictionary = new();
        NamedNode node = new(Utf8Strings.From("http://example.org/unknown"));

        Assert.IsFalse(dictionary.Contains(node));
    }
}
