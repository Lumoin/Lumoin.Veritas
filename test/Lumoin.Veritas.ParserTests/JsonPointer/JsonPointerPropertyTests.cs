using CsCheck;
using Ptr = Lumoin.Veritas.JsonPointer.JsonPointer;
using Seg = Lumoin.Veritas.JsonPointer.JsonPointerSegment;

namespace Lumoin.Veritas.ParserTests.JsonPointer;

/// <summary>
/// CsCheck-driven algebraic properties of <see cref="Ptr"/>: parse/serialize and escape round-trips,
/// the append/parent and append/relative inverses, ancestry antisymmetry, and the consistency of
/// equality, ordering, and the comparison operators across randomly generated pointers.
/// </summary>
[TestClass]
internal sealed class JsonPointerPropertyTests
{
    /// <summary>Alphanumeric reference tokens of length 0..10.</summary>
    private static Gen<string> GenToken { get; } = Gen.String[Gen.Char.AlphaNumeric, 0, 10];

    /// <summary>A segment built from a generated token.</summary>
    private static Gen<Seg> GenSegment { get; } = GenToken.Select(Seg.Create);

    /// <summary>A pointer of depth 0..5 built from generated segments.</summary>
    private static Gen<Ptr> GenPointer { get; } =
        Gen.Int[0, 5].SelectMany(depth =>
            GenSegment.Array[depth, depth]
               .Select(static segs => Ptr.FromSegments(segs)));

    /// <summary>Tokens that exercise the escape path alongside plain alphanumerics.</summary>
    private static Gen<string> GenTokenWithSpecialChars { get; } =
        Gen.OneOf(
            Gen.Const("a/b"),
            Gen.Const("c~d"),
            Gen.Const("~/"),
            Gen.Const("a~0b"),
            GenToken);

    /// <summary>Serializing then parsing reproduces the original pointer.</summary>
    [TestMethod]
    public void ParseToStringRoundtrip()
    {
        GenPointer.Sample(pointer =>
        {
            string str = pointer.ToString();
            Ptr parsed = Ptr.Parse(str);

            Assert.AreEqual(pointer, parsed, $"Roundtrip failed for '{str}'.");
        });
    }

    /// <summary>Non-throwing parse agrees with <see cref="Ptr.Parse(string)"/> on valid input.</summary>
    [TestMethod]
    public void TryParseAgreesWithParse()
    {
        GenPointer.Sample(pointer =>
        {
            string str = pointer.ToString();
            bool success = Ptr.TryParse(str, out Ptr result);

            Assert.IsTrue(success);
            Assert.AreEqual(pointer, result);
        });
    }

    /// <summary>A pointer round-trips through its URI fragment form.</summary>
    [TestMethod]
    public void UriFragmentRoundtrip()
    {
        GenPointer.Sample(pointer =>
        {
            string fragment = pointer.ToUriFragment();
            Ptr parsed = Ptr.ParseUriFragment(fragment);

            Assert.AreEqual(pointer, parsed, $"URI fragment roundtrip failed for '{pointer}'.");
        });
    }

    /// <summary>Escaping a token and parsing it back reproduces the raw token value.</summary>
    [TestMethod]
    public void EscapeUnescapeRoundtrip()
    {
        GenTokenWithSpecialChars.Sample(token =>
        {
            string escaped = Ptr.Escape(token);
            Ptr pointer = Ptr.Parse("/" + escaped);

            Assert.AreEqual(token, pointer.Segments[0].Value,
                $"Escape roundtrip failed for '{token}' (escaped: '{escaped}').");
        });
    }

    /// <summary>The parent of an appended property pointer is the original pointer.</summary>
    [TestMethod]
    public void AppendPropertyThenParentReturnsOriginal()
    {
        GenPointer.SelectMany(pointer =>
            GenToken.Where(static t => t.Length > 0).Select(token => (pointer, token)))
        .Sample(t =>
        {
            Ptr appended = t.pointer.Append(t.token);
            Ptr parent = appended.Parent!.Value;

            Assert.AreEqual(t.pointer, parent,
                $"Append('{t.token}').Parent should equal original for '{t.pointer}'.");
        });
    }

    /// <summary>The parent of an appended index pointer is the original pointer.</summary>
    [TestMethod]
    public void AppendIndexThenParentReturnsOriginal()
    {
        GenPointer.SelectMany(pointer =>
            Gen.Int[0, 999].Select(idx => (pointer, idx)))
        .Sample(t =>
        {
            Ptr appended = t.pointer.Append(t.idx);
            Ptr parent = appended.Parent!.Value;

            Assert.AreEqual(t.pointer, parent,
                $"Append({t.idx}).Parent should equal original for '{t.pointer}'.");
        });
    }

    /// <summary>Appending a segment increases depth by exactly one.</summary>
    [TestMethod]
    public void AppendIncreasesDepthByOne()
    {
        GenPointer.SelectMany(pointer =>
            GenSegment.Select(seg => (pointer, seg)))
        .Sample(t =>
        {
            Ptr appended = t.pointer.Append(t.seg);

            Assert.AreEqual(t.pointer.Depth + 1, appended.Depth);
        });
    }

    /// <summary>Appending a pointer sums the two depths.</summary>
    [TestMethod]
    public void AppendPointerDepthIsSum()
    {
        GenPointer.SelectMany(a =>
            GenPointer.Select(b => (a, b)))
        .Sample(t =>
        {
            Ptr combined = t.a.Append(t.b);

            Assert.AreEqual(t.a.Depth + t.b.Depth, combined.Depth);
        });
    }

    /// <summary>A pointer is a strict ancestor of its one-segment extension, never the reverse.</summary>
    [TestMethod]
    public void IsAncestorOfIsAntisymmetric()
    {
        GenPointer.SelectMany(ancestor =>
            GenSegment.Select(seg => (ancestor, descendant: ancestor.Append(seg))))
        .Sample(t =>
        {
            Assert.IsTrue(t.ancestor.IsAncestorOf(t.descendant),
                $"'{t.ancestor}' should be ancestor of '{t.descendant}'.");
            Assert.IsFalse(t.descendant.IsAncestorOf(t.ancestor),
                $"'{t.descendant}' should not be ancestor of '{t.ancestor}'.");
        });
    }

    /// <summary><see cref="Ptr.IsDescendantOf"/> is the converse of <see cref="Ptr.IsAncestorOf"/>.</summary>
    [TestMethod]
    public void IsDescendantOfIsSymmetricWithIsAncestorOf()
    {
        GenPointer.SelectMany(ancestor =>
            GenSegment.Select(seg => (ancestor, descendant: ancestor.Append(seg))))
        .Sample(t =>
        {
            Assert.AreEqual(
                t.ancestor.IsAncestorOf(t.descendant),
                t.descendant.IsDescendantOf(t.ancestor));
        });
    }

    /// <summary>The root is a strict ancestor of every non-root pointer.</summary>
    [TestMethod]
    public void RootIsAncestorOfAllNonRootPointers()
    {
        GenPointer.Where(static p => !p.IsRoot)
        .Sample(pointer =>
        {
            Assert.IsTrue(Ptr.Root.IsAncestorOf(pointer));
        });
    }

    /// <summary>No pointer is a strict ancestor of the root.</summary>
    [TestMethod]
    public void NothingIsAncestorOfRoot()
    {
        GenPointer.Sample(pointer =>
        {
            Assert.IsFalse(pointer.IsAncestorOf(Ptr.Root));
        });
    }

    /// <summary>The or-equal ancestry relation is reflexive.</summary>
    [TestMethod]
    public void IsAncestorOfOrEqualToIsReflexive()
    {
        GenPointer.Sample(pointer =>
        {
            Assert.IsTrue(pointer.IsAncestorOfOrEqualTo(pointer));
        });
    }

    /// <summary><see cref="Ptr.RelativeTo(Ptr)"/> undoes an append (depth is preserved).</summary>
    [TestMethod]
    public void RelativeToUndoesAppend()
    {
        GenPointer.SelectMany(ancestor =>
            GenPointer.Where(static p => !p.IsRoot).Select(suffix => (ancestor, suffix)))
        .Sample(t =>
        {
            Ptr combined = t.ancestor.Append(t.suffix);
            Ptr relative = combined.RelativeTo(t.ancestor);

            Assert.AreEqual(t.suffix.Depth, relative.Depth,
                $"RelativeTo should undo append for '{t.ancestor}' + '{t.suffix}'.");
        });
    }

    /// <summary><see cref="Ptr.SelfAndAncestors"/> yields depth + 1 pointers.</summary>
    [TestMethod]
    public void SelfAndAncestorsCountEqualsDepthPlusOne()
    {
        GenPointer.Sample(pointer =>
        {
            int count = pointer.SelfAndAncestors().Count();

            Assert.AreEqual(pointer.Depth + 1, count);
        });
    }

    /// <summary><see cref="Ptr.Ancestors"/> yields exactly depth pointers.</summary>
    [TestMethod]
    public void AncestorsCountEqualsDepth()
    {
        GenPointer.Sample(pointer =>
        {
            int count = pointer.Ancestors().Count();

            Assert.AreEqual(pointer.Depth, count);
        });
    }

    /// <summary>Equality is reflexive and hash-stable.</summary>
    [TestMethod]
    public void EqualityIsReflexive()
    {
        GenPointer.Sample(pointer =>
        {
            Ptr same = Ptr.Parse(pointer.ToString());

            Assert.AreEqual(pointer, same);
            Assert.AreEqual(pointer.GetHashCode(), same.GetHashCode());
        });
    }

    /// <summary>Comparison is reflexive (a pointer compares equal to itself).</summary>
    [TestMethod]
    public void CompareToIsReflexive()
    {
        GenPointer.Sample(pointer =>
        {
            Assert.AreEqual(0, pointer.CompareTo(pointer));
        });
    }

    /// <summary>Comparison is antisymmetric: the sign reverses when operands swap.</summary>
    [TestMethod]
    public void CompareToIsAntisymmetric()
    {
        GenPointer.SelectMany(a =>
            GenPointer.Select(b => (a, b)))
        .Sample(t =>
        {
            int ab = t.a.CompareTo(t.b);
            int ba = t.b.CompareTo(t.a);

            if(ab > 0)
            {
                Assert.IsLessThan(0, ba);
            }
            else if(ab < 0)
            {
                Assert.IsGreaterThan(0, ba);
            }
            else
            {
                Assert.AreEqual(0, ba);
            }
        });
    }

    /// <summary>The comparison operators agree with <see cref="System.IComparable{T}.CompareTo"/>.</summary>
    [TestMethod]
    public void CompareToIsConsistentWithOperators()
    {
        GenPointer.SelectMany(a =>
            GenPointer.Select(b => (a, b)))
        .Sample(t =>
        {
            int cmp = t.a.CompareTo(t.b);

            Assert.AreEqual(cmp < 0, t.a < t.b);
            Assert.AreEqual(cmp <= 0, t.a <= t.b);
            Assert.AreEqual(cmp > 0, t.a > t.b);
            Assert.AreEqual(cmp >= 0, t.a >= t.b);
        });
    }

    /// <summary><see cref="Ptr.LastSegment"/> equals the final element of <see cref="Ptr.Segments"/>.</summary>
    [TestMethod]
    public void LastSegmentMatchesSegmentsArrayEnd()
    {
        GenPointer.Where(static p => !p.IsRoot)
        .Sample(pointer =>
        {
            Seg last = pointer.LastSegment!.Value;
            Seg fromArray = pointer.Segments[pointer.Depth - 1];

            Assert.AreEqual(fromArray, last);
        });
    }
}
