using Lumoin.Veritas.JsonPointer;
using Seg = Lumoin.Veritas.JsonPointer.JsonPointerSegment;

namespace Lumoin.Veritas.ParserTests.JsonPointer;

/// <summary>
/// Validates <see cref="Seg"/>: construction (raw token, index, append marker), array-index
/// classification with RFC 6901 leading-zero rules, escaping, and value equality/ordering over the
/// raw token string.
/// </summary>
[TestClass]
internal sealed class JsonPointerSegmentTests
{
    /// <summary><see cref="Seg.Create(string)"/> stores the token verbatim.</summary>
    [TestMethod]
    public void CreateStoresToken()
    {
        Seg segment = Seg.Create("name");

        Assert.AreEqual("name", segment.Value);
    }

    /// <summary>The empty string is a valid (object property) token.</summary>
    [TestMethod]
    public void CreateAcceptsEmptyString()
    {
        Seg segment = Seg.Create("");

        Assert.AreEqual("", segment.Value);
    }

    /// <summary><see cref="Seg.Create(string)"/> rejects <see langword="null"/>.</summary>
    [TestMethod]
    public void CreateThrowsOnNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(static () => Seg.Create(null!));
    }

    /// <summary><see cref="Seg.FromIndex(int)"/> stores the index as its decimal string.</summary>
    [TestMethod]
    public void FromIndexStoresTokenAsDecimalString()
    {
        Seg segment = Seg.FromIndex(42);

        Assert.AreEqual("42", segment.Value);
    }

    /// <summary>Index zero is accepted.</summary>
    [TestMethod]
    public void FromIndexAcceptsZero()
    {
        Seg segment = Seg.FromIndex(0);

        Assert.AreEqual("0", segment.Value);
    }

    /// <summary>A negative index is rejected.</summary>
    [TestMethod]
    public void FromIndexThrowsOnNegative()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => Seg.FromIndex(-1));
    }

    /// <summary>The append marker is <c>"-"</c> and reports as such.</summary>
    [TestMethod]
    public void AppendMarkerValueIsDash()
    {
        Assert.AreEqual("-", Seg.AppendMarker.Value);
        Assert.IsTrue(Seg.AppendMarker.IsAppendMarker);
    }

    /// <summary>Only the exact token <c>"-"</c> is the append marker.</summary>
    [TestMethod]
    public void IsAppendMarkerTrueOnlyForDash()
    {
        Assert.IsTrue(Seg.Create("-").IsAppendMarker);
        Assert.IsFalse(Seg.Create("name").IsAppendMarker);
        Assert.IsFalse(Seg.Create("0").IsAppendMarker);
        Assert.IsFalse(Seg.Create("").IsAppendMarker);
    }

    /// <summary>Digit-only tokens parse to their numeric index.</summary>
    [TestMethod]
    public void TryGetArrayIndexSucceedsForValidIndexes()
    {
        Assert.IsTrue(Seg.Create("0").TryGetArrayIndex(out int zero));
        Assert.AreEqual(0, zero);

        Assert.IsTrue(Seg.Create("5").TryGetArrayIndex(out int five));
        Assert.AreEqual(5, five);

        Assert.IsTrue(Seg.Create("123").TryGetArrayIndex(out int onetwothree));
        Assert.AreEqual(123, onetwothree);
    }

    /// <summary>Non-numeric, empty, dash, and mixed tokens are not array indexes.</summary>
    [TestMethod]
    public void TryGetArrayIndexFailsForNonNumericTokens()
    {
        Assert.IsFalse(Seg.Create("name").TryGetArrayIndex(out _));
        Assert.IsFalse(Seg.Create("").TryGetArrayIndex(out _));
        Assert.IsFalse(Seg.Create("-").TryGetArrayIndex(out _));
        Assert.IsFalse(Seg.Create("abc123").TryGetArrayIndex(out _));
    }

    /// <summary>RFC 6901 forbids leading zeros in array indexes.</summary>
    [TestMethod]
    public void TryGetArrayIndexRejectsLeadingZeros()
    {
        Assert.IsFalse(Seg.Create("01").TryGetArrayIndex(out _));
        Assert.IsFalse(Seg.Create("007").TryGetArrayIndex(out _));
    }

    /// <summary><see cref="Seg.CanBeArrayIndex"/> agrees with <see cref="Seg.TryGetArrayIndex(out int)"/>.</summary>
    [TestMethod]
    public void CanBeArrayIndexMatchesTryGetArrayIndex()
    {
        Assert.IsTrue(Seg.Create("0").CanBeArrayIndex);
        Assert.IsTrue(Seg.Create("42").CanBeArrayIndex);
        Assert.IsFalse(Seg.Create("name").CanBeArrayIndex);
        Assert.IsFalse(Seg.Create("01").CanBeArrayIndex);
        Assert.IsFalse(Seg.Create("-").CanBeArrayIndex);
    }

    /// <summary>Escaping a token encodes both <c>'~'</c> and <c>'/'</c>.</summary>
    [TestMethod]
    public void ToEscapedStringEscapesTildeAndSlash()
    {
        Seg segment = Seg.Create("a/b~c");

        Assert.Contains("~1", segment.ToEscapedString());
        Assert.Contains("~0", segment.ToEscapedString());
    }

    /// <summary>A token with no reserved characters escapes to itself.</summary>
    [TestMethod]
    public void ToEscapedStringReturnsTokenUnchangedWhenNoSpecialChars()
    {
        Seg segment = Seg.Create("simple");

        Assert.AreEqual("simple", segment.ToEscapedString());
    }

    /// <summary><see cref="object.ToString"/> returns the raw (unescaped) token.</summary>
    [TestMethod]
    public void ToStringReturnsRawToken()
    {
        Assert.AreEqual("name", Seg.Create("name").ToString());
        Assert.AreEqual("42", Seg.FromIndex(42).ToString());
        Assert.AreEqual("-", Seg.AppendMarker.ToString());
        Assert.AreEqual("a/b", Seg.Create("a/b").ToString());
    }

    /// <summary>Equal tokens compare equal and hash equally.</summary>
    [TestMethod]
    public void EqualSegmentsAreEqual()
    {
        Seg a = Seg.Create("name");
        Seg b = Seg.Create("name");

        Assert.IsTrue(a.Equals(b));
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Distinct tokens are unequal.</summary>
    [TestMethod]
    public void DifferentSegmentsAreNotEqual()
    {
        Seg a = Seg.Create("name");
        Seg b = Seg.Create("other");

        Assert.IsFalse(a.Equals(b));
        Assert.IsTrue(a != b);
    }

    /// <summary>A token created via <see cref="Seg.Create(string)"/> equals the same token via <see cref="Seg.FromIndex(int)"/>.</summary>
    [TestMethod]
    public void NumericTokenEqualsByStringValue()
    {
        Seg fromString = Seg.Create("0");
        Seg fromIndex = Seg.FromIndex(0);

        Assert.AreEqual(fromString, fromIndex);
        Assert.AreEqual(fromString.GetHashCode(), fromIndex.GetHashCode());
    }

    /// <summary>The <see cref="object"/> equality overload narrows by type.</summary>
    [TestMethod]
    public void EqualsObjectOverload()
    {
        Seg a = Seg.Create("name");
        object b = Seg.Create("name");
        object c = "not a segment";

        Assert.IsTrue(a.Equals(b));
        Assert.IsFalse(a.Equals(c));
        Assert.IsFalse(a.Equals((object?)null));
    }

    /// <summary>Ordering is ordinal over the raw token.</summary>
    [TestMethod]
    public void CompareToUsesOrdinalStringComparison()
    {
        Seg apple = Seg.Create("apple");
        Seg banana = Seg.Create("banana");

        Assert.IsLessThan(0, apple.CompareTo(banana));
        Assert.IsGreaterThan(0, banana.CompareTo(apple));
        Assert.AreEqual(0, apple.CompareTo(Seg.Create("apple")));
    }

    /// <summary>The comparison operators agree with <see cref="IComparable{T}.CompareTo"/>.</summary>
    [TestMethod]
    public void ComparisonOperatorsWork()
    {
        Seg a = Seg.Create("a");
        Seg b = Seg.Create("b");

        Assert.IsTrue(a < b);
        Assert.IsTrue(a <= b);
        Assert.IsTrue(b > a);
        Assert.IsTrue(b >= a);
        Assert.IsTrue(a <= Seg.Create("a"));
        Assert.IsTrue(a >= Seg.Create("a"));
    }

    /// <summary>A string implicitly converts to a token segment.</summary>
    [TestMethod]
    public void ImplicitConversionFromStringCreatesSegment()
    {
        Seg segment = "name";

        Assert.AreEqual("name", segment.Value);
    }

    /// <summary>An integer implicitly converts to an index segment.</summary>
    [TestMethod]
    public void ImplicitConversionFromIntCreatesSegment()
    {
        Seg segment = 3;

        Assert.AreEqual("3", segment.Value);
    }
}
