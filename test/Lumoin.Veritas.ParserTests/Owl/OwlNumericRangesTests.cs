using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The numeric value-space algebra and the standard datatype oracle built
/// on it: interval intersection and coverage over the OWL 2 map, exact
/// numeric value identity across lexical forms and datatypes, and the
/// signed-zero distinction of the floating value spaces.
/// </summary>
[TestClass]
internal sealed class OwlNumericRangesTests
{
    [TestMethod]
    public void NonNegativeAndNonPositiveIntersectToZeroWhichIsAShort()
    {
        Assert.IsTrue(OwlNumericRanges.TryGetRange(Vocabulary.Xsd.NonNegativeInteger, out OwlNumericRange nonNegative));
        Assert.IsTrue(OwlNumericRanges.TryGetRange(Vocabulary.Xsd.NonPositiveInteger, out OwlNumericRange nonPositive));

        OwlNumericRange? intersection = OwlNumericRanges.Intersect(nonNegative, nonPositive);
        Assert.IsNotNull(intersection);
        Assert.AreEqual(new System.Numerics.BigInteger(0), intersection.Value.Min);
        Assert.AreEqual(new System.Numerics.BigInteger(0), intersection.Value.Max);

        List<Utf8String> supersets = OwlNumericRanges.SupersetsOf(intersection.Value);
        Assert.Contains(Vocabulary.Xsd.Short, supersets);
        Assert.Contains(Vocabulary.Xsd.UnsignedByte, supersets);
        Assert.DoesNotContain(Vocabulary.Xsd.PositiveInteger, supersets);
        Assert.DoesNotContain(Vocabulary.Xsd.NegativeInteger, supersets);
    }

    [TestMethod]
    public void ShortAndUnsignedIntIntersectInsideUnsignedShort()
    {
        Assert.IsTrue(OwlNumericRanges.TryGetRange(Vocabulary.Xsd.Short, out OwlNumericRange shortRange));
        Assert.IsTrue(OwlNumericRanges.TryGetRange(Vocabulary.Xsd.UnsignedInt, out OwlNumericRange unsignedInt));

        OwlNumericRange? intersection = OwlNumericRanges.Intersect(shortRange, unsignedInt);
        Assert.IsNotNull(intersection);

        List<Utf8String> supersets = OwlNumericRanges.SupersetsOf(intersection.Value);
        Assert.Contains(Vocabulary.Xsd.UnsignedShort, supersets);
    }

    [TestMethod]
    public void DisjointIntervalsIntersectToEmpty()
    {
        Assert.IsTrue(OwlNumericRanges.TryGetRange(Vocabulary.Xsd.PositiveInteger, out OwlNumericRange positive));
        Assert.IsTrue(OwlNumericRanges.TryGetRange(Vocabulary.Xsd.NegativeInteger, out OwlNumericRange negative));

        Assert.IsNull(OwlNumericRanges.Intersect(positive, negative));
    }

    [TestMethod]
    public void RationalLexicalsConvertToExactDecimals()
    {
        Assert.IsTrue(OwlNumericLexicals.TryGetValue("1/2", OwlVocabulary.Rational, out NumericValue half));
        Assert.IsTrue(NumericValue.TryParse("0.5", Vocabulary.Xsd.Decimal, out NumericValue pointFive));
        Assert.IsTrue(half.Equals(pointFive));

        //A non-terminating expansion stays unparsed — unknown, not wrong.
        Assert.IsFalse(OwlNumericLexicals.TryGetValue("1/3", OwlVocabulary.Rational, out _));
    }

    [TestMethod]
    public void OracleSeesThroughNonCanonicalLexicalForms()
    {
        TermDictionary dictionary = new();
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);

        TermId canonical = IntegerLiteral(dictionary, "1");
        TermId padded = IntegerLiteral(dictionary, "01");
        TermId two = IntegerLiteral(dictionary, "2");

        Assert.IsFalse(oracle.LiteralsKnownDistinct(canonical, padded));
        Assert.IsTrue(oracle.LiteralsKnownDistinct(canonical, two));
    }

    [TestMethod]
    public void OracleDistinguishesSignedFloatingZeros()
    {
        TermDictionary dictionary = new();
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);

        TermId positiveZero = FloatLiteral(dictionary, "0.0");
        TermId negativeZero = FloatLiteral(dictionary, "-0.0");

        Assert.IsTrue(oracle.LiteralsKnownDistinct(positiveZero, negativeZero));
    }

    [TestMethod]
    public void OracleRefinesNumericMembershipByInterval()
    {
        TermDictionary dictionary = new();
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);

        TermId minusOne = IntegerLiteral(dictionary, "-1");
        TermId fraction = dictionary.GetOrAdd(new Literal(Utf8Strings.From("2.5"), new NamedNode(Vocabulary.Xsd.Decimal)));
        TermId nonNegative = dictionary.GetOrAdd(new NamedNode(Vocabulary.Xsd.NonNegativeInteger));
        TermId integer = dictionary.GetOrAdd(new NamedNode(Vocabulary.Xsd.Integer));

        Assert.IsTrue(oracle.LiteralOutsideDatatype(minusOne, nonNegative));
        Assert.IsTrue(oracle.LiteralOutsideDatatype(fraction, integer));
        Assert.IsFalse(oracle.LiteralOutsideDatatype(minusOne, integer));
    }

    private static TermId IntegerLiteral(TermDictionary dictionary, string lexical)
    {
        return dictionary.GetOrAdd(new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.Integer)));
    }

    private static TermId FloatLiteral(TermDictionary dictionary, string lexical)
    {
        return dictionary.GetOrAdd(new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.Float)));
    }
}
