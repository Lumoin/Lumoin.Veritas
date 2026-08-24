using Lumoin.Veritas.JsonPointer;
using Ptr = Lumoin.Veritas.JsonPointer.JsonPointer;
using Seg = Lumoin.Veritas.JsonPointer.JsonPointerSegment;

namespace Lumoin.Veritas.ParserTests.JsonPointer;

/// <summary>
/// Validates <see cref="Ptr"/>: RFC 6901 parsing and escaping, factory and composition operations
/// (append, parent, ancestry, relative), URI-fragment round-tripping, and value
/// equality/ordering/conversions. Evaluation against a concrete document is a separate layer.
/// </summary>
[TestClass]
internal sealed class JsonPointerTests
{
    /// <summary>The empty string parses to the root pointer.</summary>
    [TestMethod]
    public void ParseEmptyStringReturnsRoot()
    {
        Ptr pointer = Ptr.Parse("");

        Assert.IsTrue(pointer.IsRoot);
        Assert.AreEqual(0, pointer.Depth);
        Assert.AreEqual("", pointer.ToString());
    }

    /// <summary>A lone <c>'/'</c> is one segment whose token is the empty string.</summary>
    [TestMethod]
    public void ParseSingleSlashReturnsEmptyTokenSegment()
    {
        Ptr pointer = Ptr.Parse("/");

        Assert.AreEqual(1, pointer.Depth);
        Assert.AreEqual("", pointer.Segments[0].Value);
    }

    /// <summary>A two-token path parses into both property segments.</summary>
    [TestMethod]
    public void ParsePropertyPath()
    {
        Ptr pointer = Ptr.Parse("/foo/bar");

        Assert.AreEqual(2, pointer.Depth);
        Assert.AreEqual("foo", pointer.Segments[0].Value);
        Assert.AreEqual("bar", pointer.Segments[1].Value);
    }

    /// <summary>Numeric tokens are stored as raw tokens and report as possible array indexes.</summary>
    [TestMethod]
    public void ParseNumericTokensAsTokens()
    {
        Ptr pointer = Ptr.Parse("/items/0/name");

        Assert.AreEqual(3, pointer.Depth);
        Assert.AreEqual("items", pointer.Segments[0].Value);
        Assert.AreEqual("0", pointer.Segments[1].Value);
        Assert.IsTrue(pointer.Segments[1].CanBeArrayIndex);
        Assert.AreEqual("name", pointer.Segments[2].Value);
    }

    /// <summary>The append marker <c>"-"</c> parses to an append-marker segment.</summary>
    [TestMethod]
    public void ParseAppendMarker()
    {
        Ptr pointer = Ptr.Parse("/items/-");

        Assert.AreEqual(2, pointer.Depth);
        Assert.IsTrue(pointer.Segments[1].IsAppendMarker);
    }

    /// <summary>Parsing resolves <c>~1</c> → <c>'/'</c> and <c>~0</c> → <c>'~'</c>.</summary>
    [TestMethod]
    public void ParseUnescapesTildeSequences()
    {
        Ptr pointer = Ptr.Parse("/a~1b/c~0d");

        Assert.AreEqual("a/b", pointer.Segments[0].Value);
        Assert.AreEqual("c~d", pointer.Segments[1].Value);
    }

    /// <summary><see cref="Ptr.Parse(string)"/> rejects <see langword="null"/>.</summary>
    [TestMethod]
    public void ParseThrowsOnNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(static () => Ptr.Parse(null!));
    }

    /// <summary>A non-empty pointer without a leading <c>'/'</c> is rejected.</summary>
    [TestMethod]
    public void ParseThrowsOnMissingLeadingSlash()
    {
        Assert.ThrowsExactly<FormatException>(static () => Ptr.Parse("foo"));
    }

    /// <summary>A leading-zero numeric token is stored raw and is not a valid array index.</summary>
    [TestMethod]
    public void ParseLeadingZeroAsToken()
    {
        Ptr pointer = Ptr.Parse("/01");

        Assert.AreEqual("01", pointer.Segments[0].Value);
        Assert.IsFalse(pointer.Segments[0].CanBeArrayIndex);
    }

    /// <summary>An unknown escape (<c>~2</c>) is a format error.</summary>
    [TestMethod]
    public void ParseInvalidEscapeThrows()
    {
        Assert.ThrowsExactly<FormatException>(static () => Ptr.Parse("/a~2b"));
    }

    /// <summary>A trailing <c>'~'</c> is a format error.</summary>
    [TestMethod]
    public void ParseTrailingTildeThrows()
    {
        Assert.ThrowsExactly<FormatException>(static () => Ptr.Parse("/a~"));
    }

    /// <summary><see cref="Ptr.TryParse(string, out Ptr)"/> succeeds for a valid pointer.</summary>
    [TestMethod]
    public void TryParseReturnsTrueForValidPointer()
    {
        bool success = Ptr.TryParse("/foo/0", out Ptr result);

        Assert.IsTrue(success);
        Assert.AreEqual(2, result.Depth);
    }

    /// <summary>The empty string parses to the root via <see cref="Ptr.TryParse(string, out Ptr)"/>.</summary>
    [TestMethod]
    public void TryParseReturnsTrueForEmptyString()
    {
        bool success = Ptr.TryParse("", out Ptr result);

        Assert.IsTrue(success);
        Assert.IsTrue(result.IsRoot);
    }

    /// <summary><see langword="null"/> fails non-throwing parse.</summary>
    [TestMethod]
    public void TryParseReturnsFalseForNull()
    {
        Assert.IsFalse(Ptr.TryParse(null, out _));
    }

    /// <summary>A missing leading slash fails non-throwing parse.</summary>
    [TestMethod]
    public void TryParseReturnsFalseForMissingSlash()
    {
        Assert.IsFalse(Ptr.TryParse("noslash", out _));
    }

    /// <summary>An invalid escape fails non-throwing parse.</summary>
    [TestMethod]
    public void TryParseReturnsFalseForInvalidEscape()
    {
        Assert.IsFalse(Ptr.TryParse("/a~2b", out _));
    }

    /// <summary>Serialization re-escapes reserved characters.</summary>
    [TestMethod]
    public void ToStringPreservesEscaping()
    {
        Ptr pointer = Ptr.Parse("/a~1b/0/c~0d");

        Assert.AreEqual("/a~1b/0/c~0d", pointer.ToString());
    }

    /// <summary>The root serializes to the empty string.</summary>
    [TestMethod]
    public void ToStringRootReturnsEmptyString()
    {
        Assert.AreEqual("", Ptr.Root.ToString());
    }

    /// <summary>An empty segment sequence yields the root.</summary>
    [TestMethod]
    public void FromSegmentsEmptyReturnsRoot()
    {
        Assert.IsTrue(Ptr.FromSegments([]).IsRoot);
    }

    /// <summary><see cref="Ptr.FromSegments(ReadOnlySpan{Seg})"/> preserves segment order and depth.</summary>
    [TestMethod]
    public void FromSegmentsCreatesPointerWithCorrectDepth()
    {
        Seg[] segments = [Seg.Create("a"), Seg.FromIndex(1)];
        Ptr pointer = Ptr.FromSegments(segments);

        Assert.AreEqual(2, pointer.Depth);
        Assert.AreEqual("a", pointer.Segments[0].Value);
        Assert.AreEqual("1", pointer.Segments[1].Value);
    }

    /// <summary><see cref="Ptr.FromProperty(string)"/> builds a single property segment.</summary>
    [TestMethod]
    public void FromPropertyCreatesSingleSegmentPointer()
    {
        Ptr pointer = Ptr.FromProperty("name");

        Assert.AreEqual(1, pointer.Depth);
        Assert.AreEqual("name", pointer.Segments[0].Value);
    }

    /// <summary><see cref="Ptr.FromProperty(string)"/> rejects <see langword="null"/>.</summary>
    [TestMethod]
    public void FromPropertyThrowsOnNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(static () => Ptr.FromProperty(null!));
    }

    /// <summary><see cref="Ptr.FromIndex(int)"/> builds a single index segment.</summary>
    [TestMethod]
    public void FromIndexCreatesSingleSegmentPointer()
    {
        Ptr pointer = Ptr.FromIndex(5);

        Assert.AreEqual(1, pointer.Depth);
        Assert.AreEqual("5", pointer.Segments[0].Value);
        Assert.IsTrue(pointer.Segments[0].CanBeArrayIndex);
    }

    /// <summary><see cref="Ptr.FromIndex(int)"/> rejects a negative index.</summary>
    [TestMethod]
    public void FromIndexThrowsOnNegative()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => Ptr.FromIndex(-1));
    }

    /// <summary>The root has no parent.</summary>
    [TestMethod]
    public void ParentReturnsNullForRoot()
    {
        Assert.IsNull(Ptr.Root.Parent);
    }

    /// <summary>A depth-one pointer's parent is the root.</summary>
    [TestMethod]
    public void ParentReturnsRootForDepthOne()
    {
        Ptr pointer = Ptr.Parse("/foo");

        Assert.AreEqual(Ptr.Root, pointer.Parent);
    }

    /// <summary>The parent drops the final segment.</summary>
    [TestMethod]
    public void ParentRemovesLastSegment()
    {
        Ptr pointer = Ptr.Parse("/foo/bar/baz");

        Ptr parent = pointer.Parent!.Value;
        Assert.AreEqual(2, parent.Depth);
        Assert.AreEqual("/foo/bar", parent.ToString());
    }

    /// <summary>The root has no last segment.</summary>
    [TestMethod]
    public void LastSegmentReturnsNullForRoot()
    {
        Assert.IsNull(Ptr.Root.LastSegment);
    }

    /// <summary><see cref="Ptr.LastSegment"/> returns the final segment.</summary>
    [TestMethod]
    public void LastSegmentReturnsFinalSegment()
    {
        Ptr pointer = Ptr.Parse("/foo/42");

        Assert.AreEqual("42", pointer.LastSegment!.Value.Value);
    }

    /// <summary>The root has no ancestors.</summary>
    [TestMethod]
    public void AncestorsOfRootIsEmpty()
    {
        Assert.HasCount(0, Ptr.Root.Ancestors().ToList());
    }

    /// <summary><see cref="Ptr.Ancestors"/> runs from root to immediate parent (exclusive of self).</summary>
    [TestMethod]
    public void AncestorsEnumeratesFromRootToParent()
    {
        Ptr pointer = Ptr.Parse("/a/b/c");
        List<Ptr> ancestors = pointer.Ancestors().ToList();

        Assert.HasCount(3, ancestors);
        Assert.IsTrue(ancestors[0].IsRoot);
        Assert.AreEqual("/a", ancestors[1].ToString());
        Assert.AreEqual("/a/b", ancestors[2].ToString());
    }

    /// <summary><see cref="Ptr.SelfAndAncestors"/> appends self after the ancestor chain.</summary>
    [TestMethod]
    public void SelfAndAncestorsIncludesSelf()
    {
        Ptr pointer = Ptr.Parse("/a/b");
        List<Ptr> all = pointer.SelfAndAncestors().ToList();

        Assert.HasCount(3, all);
        Assert.IsTrue(all[0].IsRoot);
        Assert.AreEqual("/a", all[1].ToString());
        Assert.AreEqual("/a/b", all[2].ToString());
    }

    /// <summary>Appending a property name extends the pointer by one segment.</summary>
    [TestMethod]
    public void AppendPropertyAddsSegment()
    {
        Ptr pointer = Ptr.Parse("/foo");
        Ptr result = pointer.Append("bar");

        Assert.AreEqual(2, result.Depth);
        Assert.AreEqual("/foo/bar", result.ToString());
    }

    /// <summary>Appending an index extends the pointer by one segment.</summary>
    [TestMethod]
    public void AppendIndexAddsSegment()
    {
        Ptr pointer = Ptr.Parse("/items");
        Ptr result = pointer.Append(3);

        Assert.AreEqual(2, result.Depth);
        Assert.AreEqual("3", result.Segments[1].Value);
    }

    /// <summary>Appending a negative index is rejected.</summary>
    [TestMethod]
    public void AppendIndexThrowsOnNegative()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => Ptr.Root.Append(-1));
    }

    /// <summary>Appending a segment value extends the pointer.</summary>
    [TestMethod]
    public void AppendSegmentAddsSegment()
    {
        Ptr pointer = Ptr.Root;
        Ptr result = pointer.Append(Seg.AppendMarker);

        Assert.AreEqual(1, result.Depth);
        Assert.IsTrue(result.Segments[0].IsAppendMarker);
    }

    /// <summary>Appending a pointer concatenates segment sequences.</summary>
    [TestMethod]
    public void AppendPointerConcatenates()
    {
        Ptr a = Ptr.Parse("/foo");
        Ptr b = Ptr.Parse("/bar/baz");
        Ptr result = a.Append(b);

        Assert.AreEqual(3, result.Depth);
        Assert.AreEqual("/foo/bar/baz", result.ToString());
    }

    /// <summary>Appending the root pointer is a no-op.</summary>
    [TestMethod]
    public void AppendRootPointerReturnsSelf()
    {
        Ptr pointer = Ptr.Parse("/foo");
        Ptr result = pointer.Append(Ptr.Root);

        Assert.AreEqual(pointer, result);
    }

    /// <summary>Appending to the root returns the other pointer.</summary>
    [TestMethod]
    public void AppendToRootReturnsOther()
    {
        Ptr other = Ptr.Parse("/foo/bar");
        Ptr result = Ptr.Root.Append(other);

        Assert.AreEqual(other, result);
    }

    /// <summary>A strict prefix is an ancestor.</summary>
    [TestMethod]
    public void IsAncestorOfReturnsTrueForPrefix()
    {
        Ptr ancestor = Ptr.Parse("/foo");
        Ptr descendant = Ptr.Parse("/foo/bar/baz");

        Assert.IsTrue(ancestor.IsAncestorOf(descendant));
    }

    /// <summary>A pointer is not a strict ancestor of itself.</summary>
    [TestMethod]
    public void IsAncestorOfReturnsFalseForSelf()
    {
        Ptr pointer = Ptr.Parse("/foo");

        Assert.IsFalse(pointer.IsAncestorOf(pointer));
    }

    /// <summary>Divergent paths are not ancestors of each other.</summary>
    [TestMethod]
    public void IsAncestorOfReturnsFalseForDivergent()
    {
        Ptr a = Ptr.Parse("/foo/bar");
        Ptr b = Ptr.Parse("/foo/baz");

        Assert.IsFalse(a.IsAncestorOf(b));
    }

    /// <summary>The root is an ancestor of every non-root pointer.</summary>
    [TestMethod]
    public void RootIsAncestorOfEverything()
    {
        Assert.IsTrue(Ptr.Root.IsAncestorOf(Ptr.Parse("/anything")));
    }

    /// <summary><see cref="Ptr.IsDescendantOf"/> mirrors <see cref="Ptr.IsAncestorOf"/>.</summary>
    [TestMethod]
    public void IsDescendantOfIsSymmetric()
    {
        Ptr ancestor = Ptr.Parse("/foo");
        Ptr descendant = Ptr.Parse("/foo/bar");

        Assert.IsTrue(descendant.IsDescendantOf(ancestor));
        Assert.IsFalse(ancestor.IsDescendantOf(descendant));
    }

    /// <summary>The or-equal ancestry relation includes self and strict descendants.</summary>
    [TestMethod]
    public void IsAncestorOfOrEqualToIncludesSelf()
    {
        Ptr pointer = Ptr.Parse("/foo");

        Assert.IsTrue(pointer.IsAncestorOfOrEqualTo(pointer));
        Assert.IsTrue(pointer.IsAncestorOfOrEqualTo(Ptr.Parse("/foo/bar")));
    }

    /// <summary>The or-equal descendant relation includes self and strict ancestors.</summary>
    [TestMethod]
    public void IsDescendantOfOrEqualToIncludesSelf()
    {
        Ptr pointer = Ptr.Parse("/foo");

        Assert.IsTrue(pointer.IsDescendantOfOrEqualTo(pointer));
        Assert.IsTrue(pointer.IsDescendantOfOrEqualTo(Ptr.Root));
    }

    /// <summary><see cref="Ptr.RelativeTo(Ptr)"/> returns the suffix after the ancestor.</summary>
    [TestMethod]
    public void RelativeToReturnsRemainingSegments()
    {
        Ptr ancestor = Ptr.Parse("/foo");
        Ptr descendant = Ptr.Parse("/foo/bar/baz");

        Ptr relative = descendant.RelativeTo(ancestor);

        Assert.AreEqual(2, relative.Depth);
        Assert.AreEqual("/bar/baz", relative.ToString());
    }

    /// <summary>A pointer relative to itself is the root.</summary>
    [TestMethod]
    public void RelativeToSelfReturnsRoot()
    {
        Ptr pointer = Ptr.Parse("/foo/bar");

        Assert.IsTrue(pointer.RelativeTo(pointer).IsRoot);
    }

    /// <summary><see cref="Ptr.RelativeTo(Ptr)"/> rejects a non-ancestor.</summary>
    [TestMethod]
    public void RelativeToThrowsForNonAncestor()
    {
        Ptr a = Ptr.Parse("/foo");
        Ptr b = Ptr.Parse("/bar");

        Assert.ThrowsExactly<ArgumentException>(() => a.RelativeTo(b));
    }

    /// <summary>The URI fragment form is the pointer string with a <c>'#'</c> prefix.</summary>
    [TestMethod]
    public void ToUriFragmentProducesHashPrefix()
    {
        Ptr pointer = Ptr.Parse("/foo/0");

        Assert.AreEqual("#/foo/0", pointer.ToUriFragment());
    }

    /// <summary>The root's URI fragment is a bare <c>'#'</c>.</summary>
    [TestMethod]
    public void ToUriFragmentRootIsHash()
    {
        Assert.AreEqual("#", Ptr.Root.ToUriFragment());
    }

    /// <summary>Characters outside the fragment-safe set are percent-encoded.</summary>
    [TestMethod]
    public void ToUriFragmentPercentEncodesSpecialCharacters()
    {
        Ptr pointer = Ptr.Parse("/a b");

        Assert.Contains("%20", pointer.ToUriFragment());
    }

    /// <summary>A pointer round-trips through its URI fragment.</summary>
    [TestMethod]
    public void ParseUriFragmentRoundtrips()
    {
        Ptr pointer = Ptr.Parse("/foo/0/bar");
        string fragment = pointer.ToUriFragment();
        Ptr parsed = Ptr.ParseUriFragment(fragment);

        Assert.AreEqual(pointer, parsed);
    }

    /// <summary><see cref="Ptr.ParseUriFragment(string)"/> rejects <see langword="null"/>.</summary>
    [TestMethod]
    public void ParseUriFragmentThrowsOnNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(static () => Ptr.ParseUriFragment(null!));
    }

    /// <summary>A fragment without the leading <c>'#'</c> is a format error.</summary>
    [TestMethod]
    public void ParseUriFragmentThrowsWithoutHash()
    {
        Assert.ThrowsExactly<FormatException>(static () => Ptr.ParseUriFragment("/foo"));
    }

    /// <summary><see langword="null"/> fails non-throwing fragment parse.</summary>
    [TestMethod]
    public void TryParseUriFragmentReturnsFalseForNull()
    {
        Assert.IsFalse(Ptr.TryParseUriFragment(null, out _));
    }

    /// <summary>A fragment without the leading <c>'#'</c> fails non-throwing parse.</summary>
    [TestMethod]
    public void TryParseUriFragmentReturnsFalseWithoutHash()
    {
        Assert.IsFalse(Ptr.TryParseUriFragment("/foo", out _));
    }

    /// <summary>A valid fragment succeeds non-throwing parse.</summary>
    [TestMethod]
    public void TryParseUriFragmentReturnsTrueForValid()
    {
        bool success = Ptr.TryParseUriFragment("#/foo/0", out Ptr result);

        Assert.IsTrue(success);
        Assert.AreEqual(2, result.Depth);
    }

    /// <summary><see cref="Ptr.Escape(string)"/> encodes <c>'~'</c> and <c>'/'</c>.</summary>
    [TestMethod]
    public void EscapeEncodesSpecialCharacters()
    {
        Assert.AreEqual("a~0b", Ptr.Escape("a~b"));
        Assert.AreEqual("a~1b", Ptr.Escape("a/b"));
        Assert.AreEqual("a~0~1b", Ptr.Escape("a~/b"));
    }

    /// <summary>A token with no reserved characters escapes to the same reference.</summary>
    [TestMethod]
    public void EscapeReturnsInputUnchangedWhenNoSpecialChars()
    {
        string input = "simple";
        Assert.AreSame(input, Ptr.Escape(input));
    }

    /// <summary><see cref="Ptr.Escape(string)"/> rejects <see langword="null"/>.</summary>
    [TestMethod]
    public void EscapeThrowsOnNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(static () => Ptr.Escape(null!));
    }

    /// <summary>Structurally identical pointers are equal and hash equally.</summary>
    [TestMethod]
    public void EqualPointersAreEqual()
    {
        Ptr a = Ptr.Parse("/foo/0");
        Ptr b = Ptr.Parse("/foo/0");

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Pointers with different segments are unequal.</summary>
    [TestMethod]
    public void DifferentPointersAreNotEqual()
    {
        Ptr a = Ptr.Parse("/foo");
        Ptr b = Ptr.Parse("/bar");

        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a != b);
    }

    /// <summary>Pointers of different depth are unequal.</summary>
    [TestMethod]
    public void DifferentDepthPointersAreNotEqual()
    {
        Ptr a = Ptr.Parse("/foo");
        Ptr b = Ptr.Parse("/foo/bar");

        Assert.AreNotEqual(a, b);
    }

    /// <summary>The <see cref="object"/> equality overload narrows by type.</summary>
    [TestMethod]
    public void EqualsObjectOverload()
    {
        Ptr a = Ptr.Parse("/foo");
        object b = Ptr.Parse("/foo");
        object c = "not a pointer";

        Assert.IsTrue(a.Equals(b));
        Assert.IsFalse(a.Equals(c));
        Assert.IsFalse(a.Equals((object?)null));
    }

    /// <summary>Ordering compares segment-by-segment, then by depth.</summary>
    [TestMethod]
    public void CompareToSortsLexicographically()
    {
        Ptr a = Ptr.Parse("/a");
        Ptr b = Ptr.Parse("/b");

        Assert.IsLessThan(0, a.CompareTo(b));
        Assert.IsGreaterThan(0, b.CompareTo(a));
    }

    /// <summary>A shorter pointer sorts before its extension.</summary>
    [TestMethod]
    public void CompareToShorterPointerSortsFirst()
    {
        Ptr shorter = Ptr.Parse("/a");
        Ptr longer = Ptr.Parse("/a/b");

        Assert.IsLessThan(0, shorter.CompareTo(longer));
    }

    /// <summary>The comparison operators agree with <see cref="IComparable{T}.CompareTo"/>.</summary>
    [TestMethod]
    public void ComparisonOperatorsWork()
    {
        Ptr a = Ptr.Parse("/a");
        Ptr b = Ptr.Parse("/b");

        Assert.IsTrue(a < b);
        Assert.IsTrue(a <= b);
        Assert.IsTrue(b > a);
        Assert.IsTrue(b >= a);
        Assert.IsTrue(a <= Ptr.Parse("/a"));
        Assert.IsTrue(a >= Ptr.Parse("/a"));
    }

    /// <summary>A string implicitly converts to a parsed pointer.</summary>
    [TestMethod]
    public void ImplicitConversionFromStringParses()
    {
        Ptr pointer = "/foo/bar";

        Assert.AreEqual(2, pointer.Depth);
    }

    /// <summary>An explicit string conversion serializes the pointer.</summary>
    [TestMethod]
    public void ExplicitConversionToStringCallsToString()
    {
        Ptr pointer = Ptr.Parse("/foo/bar");

        Assert.AreEqual("/foo/bar", (string)pointer);
    }

    /// <summary>A numeric token survives a parse round-trip as a raw token (not converted to an index type).</summary>
    [TestMethod]
    public void NumericTokenRoundtripsCorrectly()
    {
        Ptr pointer = Ptr.Parse("/0");
        Ptr reparsed = Ptr.Parse(pointer.ToString());

        Assert.AreEqual(pointer, reparsed);
        Assert.AreEqual("0", reparsed.Segments[0].Value);
    }

    /// <summary>A numeric property key (JSON-LD / JSON Schema usage) round-trips.</summary>
    [TestMethod]
    public void NumericPropertyKeyRoundtrips()
    {
        Ptr pointer = Ptr.FromProperty("42");
        Ptr reparsed = Ptr.Parse(pointer.ToString());

        Assert.AreEqual(pointer, reparsed);
    }
}
