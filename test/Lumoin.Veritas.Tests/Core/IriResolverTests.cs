using System.Collections.Generic;
using Lumoin.Veritas.Core.Iris;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verifies <see cref="IriResolver"/> against the RFC 3986 §5.4 worked
/// examples (Appendix C's reference resolution table) and the scheme-detection
/// and edge-behaviour contract. The resolver delegates to <see cref="System.Uri"/>;
/// these tests pin the observable behaviour the syntax layers depend on.
/// </summary>
[TestClass]
internal sealed class IriResolverTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string Base = "http://a/b/c/d;p?q";

    /// <summary>The RFC 3986 §5.4.1 "normal" reference-resolution examples.</summary>
    private static IEnumerable<object[]> NormalExamples()
    {
        yield return ["g:h", "g:h"];
        yield return ["g", "http://a/b/c/g"];
        yield return ["./g", "http://a/b/c/g"];
        yield return ["g/", "http://a/b/c/g/"];
        yield return ["/g", "http://a/g"];

        //RFC 3986 §5.4.1 gives "http://g"; System.Uri normalises an authority with an empty path
        //to a single "/", so the resolver — which delegates to System.Uri — yields "http://g/".
        //The normalisation is benign (empty path is equivalent to "/") and is the project's
        //established behaviour, shared by the JSON-LD and CBOR-LD layers.
        yield return ["//g", "http://g/"];
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
        string resolved = IriResolver.ResolveIri(Base, reference);

        Assert.AreEqual(expected, resolved);
    }

    [TestMethod]
    [DynamicData(nameof(AbnormalExamples))]
    public void ResolvesAbnormalReferenceAgainstBase(string reference, string expected)
    {
        string resolved = IriResolver.ResolveIri(Base, reference);

        Assert.AreEqual(expected, resolved);
    }

    [TestMethod]
    public void ResolvesEmptyReferenceToBase()
    {
        string resolved = IriResolver.ResolveIri(Base, "");

        Assert.AreEqual(Base, resolved);
    }

    [TestMethod]
    public void ReturnsAbsoluteReferenceUnchanged()
    {
        string resolved = IriResolver.ResolveIri(Base, "https://other.example/x");

        Assert.AreEqual("https://other.example/x", resolved);
    }

    [TestMethod]
    public void ReturnsReferenceUnchangedWhenBaseIsRelative()
    {
        string resolved = IriResolver.ResolveIri("not-absolute/base", "child");

        Assert.AreEqual("child", resolved);
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsTrueForSchemeQualifiedIri()
    {
        Assert.IsTrue(IriResolver.IsAbsoluteIri("http://example.org/a"));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsTrueForUrnScheme()
    {
        Assert.IsTrue(IriResolver.IsAbsoluteIri("urn:isbn:0451450523"));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForSchemeLessReference()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri("../g"));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForFragmentReference()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri("#frag"));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForEmpty()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri(""));
    }

    [TestMethod]
    public void IsAbsoluteIriReturnsFalseForLeadingColon()
    {
        Assert.IsFalse(IriResolver.IsAbsoluteIri(":nope"));
    }
}
