using Lumoin.Veritas.JsonPointer;
using Pointer = Lumoin.Veritas.JsonPointer.JsonPointer;

namespace Lumoin.Veritas.ParserTests.JsonPointer;

/// <summary>
/// Validates <see cref="Pointer"/> against the RFC 6901 §5 (string form) and §6 (URI fragment form)
/// examples: parsing, token unescaping, round-tripping, and structural composition. Evaluation against
/// a concrete document is a separate layer, so these tests assert the pointer model itself.
/// </summary>
[TestClass]
internal sealed class JsonPointerRfc6901Tests
{
    /// <summary>The RFC 6901 §5 string pointer, the count of reference tokens, and the final token's unescaped value.</summary>
    public static IEnumerable<object[]> StringPointers()
    {
        yield return ["", 0, ""];
        yield return ["/foo", 1, "foo"];
        yield return ["/foo/0", 2, "0"];
        yield return ["/", 1, ""];
        yield return ["/a~1b", 1, "a/b"];
        yield return ["/c%d", 1, "c%d"];
        yield return ["/e^f", 1, "e^f"];
        yield return ["/g|h", 1, "g|h"];
        yield return ["/i\\j", 1, "i\\j"];
        yield return ["/k\"l", 1, "k\"l"];
        yield return ["/ ", 1, " "];
        yield return ["/m~0n", 1, "m~n"];
    }

    /// <summary>The RFC 6901 §6 URI fragment and the equivalent string pointer.</summary>
    public static IEnumerable<object[]> UriFragments()
    {
        yield return ["#", ""];
        yield return ["#/foo", "/foo"];
        yield return ["#/foo/0", "/foo/0"];
        yield return ["#/", "/"];
        yield return ["#/a~1b", "/a~1b"];
        yield return ["#/c%25d", "/c%d"];
        yield return ["#/e%5Ef", "/e^f"];
        yield return ["#/g%7Ch", "/g|h"];
        yield return ["#/i%5Cj", "/i\\j"];
        yield return ["#/k%22l", "/k\"l"];
        yield return ["#/%20", "/ "];
        yield return ["#/m~0n", "/m~0n"];
    }

    /// <summary>Each §5 pointer parses to the expected token count and final unescaped token.</summary>
    /// <param name="pointer">The pointer string.</param>
    /// <param name="depth">The expected segment count.</param>
    /// <param name="lastToken">The expected final token's unescaped value.</param>
    [TestMethod]
    [DynamicData(nameof(StringPointers))]
    public void ParsesStringPointerToTokens(string pointer, int depth, string lastToken)
    {
        Pointer parsed = Pointer.Parse(pointer);

        Assert.AreEqual(depth, parsed.Depth);
        if(depth > 0)
        {
            Assert.AreEqual(lastToken, parsed.Segments[^1].Value);
        }
    }

    /// <summary>Parsing then re-serializing a §5 pointer yields the original string.</summary>
    /// <param name="pointer">The pointer string.</param>
    /// <param name="depth">Unused (shared table).</param>
    /// <param name="lastToken">Unused (shared table).</param>
    [TestMethod]
    [DynamicData(nameof(StringPointers))]
    public void RoundTripsStringPointer(string pointer, int depth, string lastToken)
    {
        _ = depth;
        _ = lastToken;

        Assert.AreEqual(pointer, Pointer.Parse(pointer).ToString());
    }

    /// <summary>A §5 pointer's URI fragment matches §6 and re-parses to the same pointer.</summary>
    /// <param name="fragment">The URI fragment.</param>
    /// <param name="pointer">The equivalent string pointer.</param>
    [TestMethod]
    [DynamicData(nameof(UriFragments))]
    public void RoundTripsUriFragment(string fragment, string pointer)
    {
        Pointer fromString = Pointer.Parse(pointer);

        Assert.AreEqual(fragment, fromString.ToUriFragment());
        Assert.AreEqual(fromString, Pointer.ParseUriFragment(fragment));
    }

    /// <summary>A pointer that does not start with <c>'/'</c> (and is not empty) is rejected.</summary>
    [TestMethod]
    public void RejectsPointerWithoutLeadingSlash()
    {
        Assert.IsFalse(Pointer.TryParse("foo", out _));
        Assert.ThrowsExactly<FormatException>(static () => Pointer.Parse("foo"));
    }

    /// <summary>Escaping is the inverse of token unescaping (<c>~</c> → <c>~0</c>, <c>/</c> → <c>~1</c>).</summary>
    [TestMethod]
    public void EscapesReservedCharacters()
    {
        Assert.AreEqual("a~1b", Pointer.Escape("a/b"));
        Assert.AreEqual("m~0n", Pointer.Escape("m~n"));
        Assert.AreEqual("~01", Pointer.Escape("~1"));
    }

    /// <summary>Composition, ancestry, and relative-pointer operations agree with the token sequence.</summary>
    [TestMethod]
    public void ComposesAndRelatesPointers()
    {
        Pointer root = Pointer.Root;
        Pointer foo = root.Append("foo");
        Pointer fooZero = foo.Append(0);

        Assert.AreEqual("/foo/0", fooZero.ToString());
        Assert.IsTrue(foo.IsAncestorOf(fooZero));
        Assert.IsTrue(fooZero.IsDescendantOf(foo));
        Assert.AreEqual(foo, fooZero.Parent);
        Assert.AreEqual("/0", fooZero.RelativeTo(foo).ToString());

        string[] ancestors = fooZero.Ancestors().Select(static p => p.ToString()).ToArray();
        string[] expectedAncestors = ["", "/foo"];
        Assert.AreSequenceEqual(expectedAncestors, ancestors);
    }

    /// <summary>An index-shaped token reports as a possible array index; a non-numeric or leading-zero one does not.</summary>
    [TestMethod]
    public void ClassifiesArrayIndexTokens()
    {
        Assert.IsTrue(JsonPointerSegment.Create("0").TryGetArrayIndex(out int zero));
        Assert.AreEqual(0, zero);
        Assert.IsTrue(JsonPointerSegment.Create("42").CanBeArrayIndex);
        Assert.IsFalse(JsonPointerSegment.Create("01").CanBeArrayIndex);
        Assert.IsFalse(JsonPointerSegment.Create("foo").CanBeArrayIndex);
        Assert.IsTrue(JsonPointerSegment.AppendMarker.IsAppendMarker);
    }
}
