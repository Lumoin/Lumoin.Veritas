using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Tests for <see cref="NumericValue"/>'s arithmetic (SPARQL §17.4 promotion) and XSD canonical lexical
/// serialization — the numeric tower the SPARQL expression evaluator, SHACL value-range checks, and OWL share.
/// </summary>
[TestClass]
internal sealed class NumericValueTests
{
    /// <summary>Parses a numeric literal lexical form of the given datatype.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatype">The XSD datatype IRI.</param>
    /// <returns>The parsed value.</returns>
    private static NumericValue Parse(string lexical, Utf8String datatype)
    {
        Assert.IsTrue(NumericValue.TryParse(lexical, datatype, out NumericValue value), $"Expected '{lexical}' to parse as {datatype}.");

        return value;
    }

    /// <summary>An integer renders as bare digits.</summary>
    [TestMethod]
    public void IntegerCanonicalForm()
    {
        Assert.AreEqual("6", new NumericValue(new BigInteger(6)).ToCanonicalLexical());
        Assert.AreEqual("-42", new NumericValue(new BigInteger(-42)).ToCanonicalLexical());
        Assert.AreEqual("0", new NumericValue(BigInteger.Zero).ToCanonicalLexical());
    }

    /// <summary>A decimal always carries a decimal point and trims redundant trailing zeros to one fractional digit.</summary>
    [TestMethod]
    public void DecimalCanonicalForm()
    {
        Assert.AreEqual("11.1", new NumericValue(11.1m).ToCanonicalLexical());
        Assert.AreEqual("1.0", new NumericValue(1.0m).ToCanonicalLexical());
        Assert.AreEqual("2.2", new NumericValue(2.20m).ToCanonicalLexical());
        Assert.AreEqual("6.0", new NumericValue(6m).ToCanonicalLexical());
        Assert.AreEqual("2.0", new NumericValue(2.0m).ToCanonicalLexical());
    }

    /// <summary>A double renders in canonical mantissa-and-exponent form, matching the W3C results fixtures.</summary>
    [TestMethod]
    public void DoubleCanonicalForm()
    {
        Assert.AreEqual("3.21E4", new NumericValue(32100.0).ToCanonicalLexical());
        Assert.AreEqual("4.0E-1", new NumericValue(0.4).ToCanonicalLexical());
        Assert.AreEqual("1.0E2", new NumericValue(100.0).ToCanonicalLexical());
        Assert.AreEqual("3.0E4", new NumericValue(30000.0).ToCanonicalLexical());
        Assert.AreEqual("0.0E0", new NumericValue(0.0).ToCanonicalLexical());
    }

    /// <summary>The float/double specials render as the XSD short forms.</summary>
    [TestMethod]
    public void FloatingSpecials()
    {
        Assert.AreEqual("INF", new NumericValue(double.PositiveInfinity).ToCanonicalLexical());
        Assert.AreEqual("-INF", new NumericValue(double.NegativeInfinity).ToCanonicalLexical());
        Assert.AreEqual("NaN", new NumericValue(double.NaN).ToCanonicalLexical());
    }

    /// <summary>SUM over decimals stays decimal (1.0 + 2.2 + 3.5 + 2.2 + 2.2 = 11.1), exactly.</summary>
    [TestMethod]
    public void DecimalSumIsExactAndStaysDecimal()
    {
        NumericValue sum = new(0m);
        foreach(string lexical in new[] { "1.0", "2.2", "3.5", "2.2", "2.2" })
        {
            sum = NumericValue.Add(sum, Parse(lexical, Vocabulary.Xsd.Decimal));
        }

        Assert.AreEqual(NumericKind.Decimal, sum.Kind);
        Assert.AreEqual("11.1", sum.ToCanonicalLexical());
    }

    /// <summary>Addition promotes integer + decimal to decimal.</summary>
    [TestMethod]
    public void AddPromotesIntegerAndDecimal()
    {
        NumericValue result = NumericValue.Add(Parse("1", Vocabulary.Xsd.Integer), Parse("2.2", Vocabulary.Xsd.Decimal));

        Assert.AreEqual(NumericKind.Decimal, result.Kind);
        Assert.AreEqual("3.2", result.ToCanonicalLexical());
    }

    /// <summary>Integer + integer stays integer.</summary>
    [TestMethod]
    public void AddKeepsIntegerKind()
    {
        NumericValue result = NumericValue.Add(Parse("1", Vocabulary.Xsd.Integer), Parse("5", Vocabulary.Xsd.Integer));

        Assert.AreEqual(NumericKind.Integer, result.Kind);
        Assert.AreEqual("6", result.ToCanonicalLexical());
    }

    /// <summary>Integer ÷ integer yields an exact decimal; a zero divisor in the exact kinds fails.</summary>
    [TestMethod]
    public void DivideIntegerYieldsDecimalAndRejectsZero()
    {
        Assert.IsTrue(NumericValue.TryDivide(Parse("1", Vocabulary.Xsd.Integer), Parse("2", Vocabulary.Xsd.Integer), out NumericValue half));
        Assert.AreEqual(NumericKind.Decimal, half.Kind);
        Assert.AreEqual("0.5", half.ToCanonicalLexical());

        Assert.IsFalse(NumericValue.TryDivide(Parse("1", Vocabulary.Xsd.Integer), Parse("0", Vocabulary.Xsd.Integer), out _));
    }

    /// <summary>AVG of the decimals (11.1 / 5) is 2.22, decimal.</summary>
    [TestMethod]
    public void DecimalAverage()
    {
        NumericValue sum = new(11.1m);
        Assert.IsTrue(NumericValue.TryDivide(sum, new NumericValue(new BigInteger(5)), out NumericValue avg));
        Assert.AreEqual("2.22", avg.ToCanonicalLexical());
    }

    /// <summary>Numeric equality follows the promotion lattice across kinds.</summary>
    [TestMethod]
    public void EqualityIsCrossKind()
    {
        Assert.AreEqual(new NumericValue(new BigInteger(1)), new NumericValue(1.0m));
        Assert.AreNotEqual(new NumericValue(1.0m), new NumericValue(2.0m));
    }
}
