using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Iris;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verifies the byte-native <see cref="IriResolver"/> against the RFC 3986 §5.4 worked
/// examples (Appendix C's reference resolution table) and the scheme-detection and
/// edge-behaviour contract, including the per-component absent-vs-empty distinctions
/// (an explicit empty query does not inherit the base's; an empty-but-present
/// authority still recomposes). The resolver performs the §5.2 transform directly;
/// these tests pin the observable behaviour the syntax layers depend on.
/// </summary>
[TestClass]
internal sealed class IriResolverTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string Base = "http://a/b/c/d;p?q";

    /// <summary>Resolves a reference against a base through the byte-native shape, returning the result's text for row comparison.</summary>
    /// <param name="baseIri">The base IRI text.</param>
    /// <param name="reference">The reference text.</param>
    /// <returns>The resolved IRI's text.</returns>
    private static string Resolve(string baseIri, string reference)
    {
        IriBase parsedBase = IriResolver.ParseBase(Utf8Strings.From(baseIri));

        return IriResolver.ResolveIri(in parsedBase, Utf8Strings.From(reference)).ToString();
    }

    /// <summary>The RFC 3986 §5.4.1 "normal" reference-resolution examples.</summary>
    private static IEnumerable<object[]> NormalExamples()
    {
        yield return ["g:h", "g:h"];
        yield return ["g", "http://a/b/c/g"];
        yield return ["./g", "http://a/b/c/g"];
        yield return ["g/", "http://a/b/c/g/"];
        yield return ["/g", "http://a/g"];

        //RFC 3986 §5.4.1: an authority with an empty path recomposes with no
        //trailing slash (the resolver implements §5.2 directly, not via
        //System.Uri, which would normalise this to "http://g/").
        yield return ["//g", "http://g"];
        yield return ["?y", "http://a/b/c/d;p?y"];
        yield return ["g?y", "http://a/b/c/g?y"];
        yield return ["#s", "http://a/b/c/d;p?q#s"];
        yield return ["g#s", "http://a/b/c/g#s"];
        yield return ["g?y#s", "http://a/b/c/g?y#s"];
        yield return [";x", "http://a/b/c/;x"];
        yield return ["g;x", "http://a/b/c/g;x"];
        yield return ["g;x?y#s", "http://a/b/c/g;x?y#s"];
        yield return ["", "http://a/b/c/d;p?q"];
        yield return [".", "http://a/b/c/"];
        yield return ["./", "http://a/b/c/"];
        yield return ["..", "http://a/b/"];
        yield return ["../", "http://a/b/"];
        yield return ["../g", "http://a/b/g"];
        yield return ["../..", "http://a/"];
        yield return ["../../", "http://a/"];
        yield return ["../../g", "http://a/g"];
    }

    /// <summary>The RFC 3986 §5.4.2 "abnormal" reference-resolution examples.</summary>
    private static IEnumerable<object[]> AbnormalExamples()
    {
        yield return ["../../../g", "http://a/g"];
        yield return ["../../../../g", "http://a/g"];
        yield return ["/./g", "http://a/g"];
        yield return ["/../g", "http://a/g"];
        yield return ["g.", "http://a/b/c/g."];
        yield return [".g", "http://a/b/c/.g"];
        yield return ["g..", "http://a/b/c/g.."];
        yield return ["..g", "http://a/b/c/..g"];
        yield return ["./../g", "http://a/b/g"];
        yield return ["./g/.", "http://a/b/c/g/"];
        yield return ["g/./h", "http://a/b/c/g/h"];
        yield return ["g/../h", "http://a/b/c/h"];
        yield return ["g;x=1/./y", "http://a/b/c/g;x=1/y"];
        yield return ["g;x=1/../y", "http://a/b/c/y"];
    }

    [TestMethod]
    [DynamicData(nameof(NormalExamples))]
    public void ResolvesNormalReferenceAgainstBase(string reference, string expected)
    {
        Assert.AreEqual(expected, Resolve(Base, reference));
    }

    [TestMethod]
    [DynamicData(nameof(AbnormalExamples))]
    public void ResolvesAbnormalReferenceAgainstBase(string reference, string expected)
    {
        Assert.AreEqual(expected, Resolve(Base, reference));
    }

    [TestMethod]
    public void ResolvesEmptyReferenceToBase()
    {
        Assert.AreEqual(Base, Resolve(Base, ""));
    }

    [TestMethod]
    public void ReturnsAbsoluteReferenceUnchanged()
    {
        Assert.AreEqual("https://other.example/x", Resolve(Base, "https://other.example/x"));
    }

    [TestMethod]
    public void ReturnsReferenceUnchangedWhenBaseIsRelative()
    {
        Assert.AreEqual("child", Resolve("not-absolute/base", "child"));
    }

    [TestMethod]
    public void ReturnsReferenceUnchangedWithoutBase()
    {
        IriBase none = IriBase.None;
        Utf8String reference = Utf8Strings.From("child");

        Assert.AreEqual(reference, IriResolver.ResolveIri(in none, reference));
    }

    [TestMethod]
    public void ResolvesAgainstEmptyAuthorityBase()
    {
        //A present-but-empty authority (file:///a/b) must survive resolution and
        //recompose its "//" prefix; collapsing it to an absent authority would
        //produce "file:/a/g".
        Assert.AreEqual("file:///a/g", Resolve("file:///a/b", "g"));
    }

    [TestMethod]
    public void ExplicitEmptyQueryDoesNotInheritBaseQuery()
    {
        //RFC 3986 §5.2.2: a reference with an empty path and a PRESENT (empty) query
        //takes its own query; only an ABSENT reference query inherits the base's.
        Assert.AreEqual("http://a/b/c/d;p?", Resolve(Base, "?"));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsTrueForSchemeQualifiedIri()
    {
        Assert.IsTrue(IriResolver.IsAbsoluteIri("http://example.org/a"u8));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsTrueForUrnScheme()
    {
        Assert.IsTrue(IriResolver.IsAbsoluteIri("urn:isbn:0451450523"u8));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForSchemeLessReference()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri("../g"u8));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForFragmentReference()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri("#frag"u8));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForEmpty()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri(""u8));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForLeadingColon()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri(":nope"u8));
    }
}
