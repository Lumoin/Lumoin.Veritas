using System.Collections.Generic;
using Lumoin.Veritas.Cbor.CborLd;

namespace Lumoin.Veritas.ParserTests.CborLd;

[TestClass]
internal sealed class CborLdRegistryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PassthroughEntryHasIdZeroAndEmptyMaps()
    {
        CborLdRegistryEntry passthrough = CborLdRegistryEntry.Passthrough;

        Assert.AreEqual(0, passthrough.RegistryEntryId);
        Assert.IsEmpty(passthrough.Keywords);
        Assert.IsEmpty(passthrough.Terms);
    }

    [TestMethod]
    public void EmptyRegistryAlwaysContainsPassthroughEntry()
    {
        CborLdRegistry registry = CborLdRegistry.Empty;

        Assert.IsTrue(registry.TryGet(0, out CborLdRegistryEntry? entry));
        Assert.IsNotNull(entry);
        Assert.AreEqual(0, entry.RegistryEntryId);
    }

    [TestMethod]
    public void RegistryContainsAddedEntries()
    {
        Dictionary<string, CborLdKeywordCodec> keywords = new()
        {
            ["@id"] = new CborLdKeywordCodec("@id", 0),
            ["@type"] = new CborLdKeywordCodec("@type", 2)
        };
        Dictionary<string, CborLdTermCodec> terms = new()
        {
            ["name"] = new CborLdTermCodec("name", 100)
        };
        CborLdRegistryEntry entry = new(registryEntryId: 1, keywords, terms);

        CborLdRegistry registry = new([entry]);

        Assert.IsTrue(registry.TryGet(1, out CborLdRegistryEntry? resolved));
        Assert.IsNotNull(resolved);
        Assert.AreEqual(1, resolved.RegistryEntryId);
        Assert.HasCount(2, resolved.Keywords);
        Assert.HasCount(1, resolved.Terms);
        //Passthrough is still present.
        Assert.IsTrue(registry.TryGet(0, out _));
    }

    [TestMethod]
    public void TryGetReturnsFalseForUnknownId()
    {
        CborLdRegistry registry = CborLdRegistry.Empty;

        Assert.IsFalse(registry.TryGet(42, out CborLdRegistryEntry? entry));
        Assert.IsNull(entry);
    }

    [TestMethod]
    public async Task AsDelegateResolvesPassthrough()
    {
        CborLdRegistry registry = CborLdRegistry.Empty;
        LoadCborLdRegistryEntryDelegate load = registry.AsDelegate();

        CborLdRegistryEntry? entry = await load(0, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(entry);
        Assert.AreEqual(0, entry.RegistryEntryId);
    }

    [TestMethod]
    public async Task AsDelegateReturnsNullForUnknownId()
    {
        CborLdRegistry registry = CborLdRegistry.Empty;
        LoadCborLdRegistryEntryDelegate load = registry.AsDelegate();

        CborLdRegistryEntry? entry = await load(99, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNull(entry);
    }

    [TestMethod]
    public void KeywordCodecCarriesKeywordAndId()
    {
        CborLdKeywordCodec codec = new("@type", 2);

        Assert.AreEqual("@type", codec.Keyword);
        Assert.AreEqual(2, codec.CborId);
    }

    [TestMethod]
    public void TermCodecCarriesTermAndId()
    {
        CborLdTermCodec codec = new("knows", 102);

        Assert.AreEqual("knows", codec.Term);
        Assert.AreEqual(102, codec.CborId);
    }
}
