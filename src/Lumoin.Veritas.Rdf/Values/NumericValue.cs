using System;
using System.Globalization;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// In-memory representation of a numeric XSD value, parsed from its
/// lexical form. The kind tag indicates which slot of the
/// discriminated union carries the value; the comparator promotes to
/// the higher-precision of the two operand kinds before comparing.
/// </summary>
/// <remarks>
/// <para>
/// Promotion lattice per SPARQL 1.1 §17.4.1:
/// </para>
/// <list type="bullet">
///   <item><description><c>integer</c> stays <see cref="BigInteger"/> when both operands are integer.</description></item>
///   <item><description><c>integer</c> ↔ <c>decimal</c> promotes to <see cref="decimal"/>.</description></item>
///   <item><description>Anything ↔ <c>float</c> promotes to <see cref="float"/>.</description></item>
///   <item><description>Anything ↔ <c>double</c> promotes to <see cref="double"/>.</description></item>
/// </list>
/// <para>
/// <c>xsd:integer</c> is unbounded; the integer slot uses
/// <see cref="BigInteger"/> rather than <see cref="long"/> so that
/// arbitrary-precision integers parse and compare without silent
/// overflow.
/// </para>
/// <para>
/// <c>NaN</c> in <c>xsd:float</c> or <c>xsd:double</c> propagates to
/// <see cref="ComparisonResult.Incomparable"/>. <c>+INF</c> and
/// <c>-INF</c> are well-ordered relative to all finite values.
/// </para>
/// </remarks>
internal readonly struct NumericValue
{
    /// <summary>Which slot of the union carries the value.</summary>
    public NumericKind Kind { get; }

    private readonly BigInteger integerValue;
    private readonly decimal decimalValue;
    private readonly float floatValue;
    private readonly double doubleValue;

    private NumericValue(BigInteger value)
    {
        Kind = NumericKind.Integer;
        integerValue = value;
        decimalValue = default;
        floatValue = default;
        doubleValue = default;
    }

    private NumericValue(decimal value)
    {
        Kind = NumericKind.Decimal;
        integerValue = default;
        decimalValue = value;
        floatValue = default;
        doubleValue = default;
    }

    private NumericValue(float value)
    {
        Kind = NumericKind.Float;
        integerValue = default;
        decimalValue = default;
        floatValue = value;
        doubleValue = default;
    }

    private NumericValue(double value)
    {
        Kind = NumericKind.Double;
        integerValue = default;
        decimalValue = default;
        floatValue = default;
        doubleValue = value;
    }

    public BigInteger AsInteger() => integerValue;
    public decimal AsDecimal() => decimalValue;
    public float AsFloat() => floatValue;
    public double AsDouble() => doubleValue;

    /// <summary>
    /// Parses the lexical form of a numeric literal whose datatype
    /// IRI determines which kind to produce. Returns <c>false</c> on
    /// any parsing failure including overflow of a derived-integer
    /// type's range.
    /// </summary>
    public static bool TryParse(string lexicalForm, Utf8String datatypeIri, out NumericValue value)
    {
        value = default;

        //Decide the target kind from the datatype IRI. Derived-integer
        //types all parse as BigInteger; the comparator doesn't need
        //to know which derived type produced the value, because
        //ordering across derived integer types is just integer
        //ordering.
        if(IsIntegerKind(datatypeIri))
        {
            if(BigInteger.TryParse(lexicalForm, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out BigInteger parsed))
            {
                value = new NumericValue(parsed);

                return true;
            }

            return false;
        }

        if(datatypeIri == Vocabulary.Xsd.Decimal)
        {
            //decimal.TryParse with NumberStyles.AllowDecimalPoint
            //matches the SPARQL canonical form (sign, digits, optional
            //fractional part). Disallows exponent — xsd:decimal forbids
            //'e' / 'E'.
            if(decimal.TryParse(lexicalForm,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal parsed))
            {
                value = new NumericValue(parsed);

                return true;
            }

            return false;
        }

        if(datatypeIri == Vocabulary.Xsd.Float)
        {
            //xsd:float allows INF, -INF, NaN as lexical forms. Handle
            //those explicitly before falling through to standard
            //float parsing.
            if(TryParseSpecialFloat(lexicalForm, out float special))
            {
                value = new NumericValue(special);

                return true;
            }

            if(float.TryParse(lexicalForm,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                value = new NumericValue(parsed);

                return true;
            }

            return false;
        }

        if(datatypeIri == Vocabulary.Xsd.Double)
        {
            if(TryParseSpecialDouble(lexicalForm, out double special))
            {
                value = new NumericValue(special);

                return true;
            }

            if(double.TryParse(lexicalForm,
                NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                value = new NumericValue(parsed);

                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Compares two numeric values under the SPARQL promotion lattice.
    /// </summary>
    public static ComparisonResult Compare(NumericValue left, NumericValue right)
    {
        //Determine the promotion target from the higher of the two
        //kinds. NumericKind values are ordered such that promoting to
        //the larger kind matches SPARQL §17.4.1.
        NumericKind target = left.Kind > right.Kind ? left.Kind : right.Kind;

        return target switch
        {
            NumericKind.Integer => CompareInteger(left, right),
            NumericKind.Decimal => CompareDecimal(left, right),
            NumericKind.Float => CompareFloat(left, right),
            NumericKind.Double => CompareDouble(left, right),
            _ => ComparisonResult.Incomparable,
        };
    }

    //Both already integers — direct BigInteger comparison.
    private static ComparisonResult CompareInteger(NumericValue left, NumericValue right)
        => SignToResult(left.integerValue.CompareTo(right.integerValue));

    //At least one decimal; the other may be integer (promote) or
    //decimal (direct).
    private static ComparisonResult CompareDecimal(NumericValue left, NumericValue right)
    {
        decimal l = left.Kind == NumericKind.Integer ? (decimal)left.integerValue : left.decimalValue;
        decimal r = right.Kind == NumericKind.Integer ? (decimal)right.integerValue : right.decimalValue;

        return SignToResult(l.CompareTo(r));
    }

    //At least one float; promote integer/decimal to float as needed.
    //If either side is NaN the comparison is incomparable.
    private static ComparisonResult CompareFloat(NumericValue left, NumericValue right)
    {
        float l = ToFloat(left);
        float r = ToFloat(right);
        if(float.IsNaN(l) || float.IsNaN(r))
        {
            return ComparisonResult.Incomparable;
        }

        return SignToResult(l.CompareTo(r));
    }

    //At least one double; promote everything to double. NaN check.
    private static ComparisonResult CompareDouble(NumericValue left, NumericValue right)
    {
        double l = ToDouble(left);
        double r = ToDouble(right);
        if(double.IsNaN(l) || double.IsNaN(r))
        {
            return ComparisonResult.Incomparable;
        }

        return SignToResult(l.CompareTo(r));
    }

    private static float ToFloat(NumericValue v)
        => v.Kind switch
        {
            NumericKind.Integer => (float)v.integerValue,
            NumericKind.Decimal => (float)v.decimalValue,
            NumericKind.Float => v.floatValue,
            NumericKind.Double => (float)v.doubleValue,
            _ => float.NaN,
        };

    private static double ToDouble(NumericValue v)
        => v.Kind switch
        {
            NumericKind.Integer => (double)v.integerValue,
            NumericKind.Decimal => (double)v.decimalValue,
            NumericKind.Float => v.floatValue,
            NumericKind.Double => v.doubleValue,
            _ => double.NaN,
        };

    private static ComparisonResult SignToResult(int sign)
        => sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal,
        };

    //XSD §3.2.4 short forms for xsd:float. Pattern-matched as a
    //switch expression with a tuple result so the out parameter is
    //assigned in one place.
    private static bool TryParseSpecialFloat(string lexical, out float result)
    {
        (bool matched, result) = lexical switch
        {
            "INF" => (true, float.PositiveInfinity),
            "-INF" => (true, float.NegativeInfinity),
            "NaN" => (true, float.NaN),
            _ => (false, default(float)),
        };

        return matched;
    }

    //XSD §3.2.5 short forms for xsd:double.
    private static bool TryParseSpecialDouble(string lexical, out double result)
    {
        (bool matched, result) = lexical switch
        {
            "INF" => (true, double.PositiveInfinity),
            "-INF" => (true, double.NegativeInfinity),
            "NaN" => (true, double.NaN),
            _ => (false, default(double)),
        };

        return matched;
    }

    //All XSD integer types — the parent xsd:integer plus every
    //derived type — share the integer parser. Range checks for
    //derived types (xsd:byte's [-128, 127], etc.) are not enforced
    //here: validation of derived-integer ranges is a datatype-
    //validity check that belongs in DatatypeEvaluator's lexical-
    //form layer when that lands. The comparator's job is to give a
    //correct ordering for two values that are well-formed integers.
    //
    //Vocabulary.Xsd.ByteValue is named to avoid collision with
    //System.Byte; the underlying IRI is xsd:byte.
    private static bool IsIntegerKind(Utf8String datatypeIri)
        => datatypeIri == Vocabulary.Xsd.Integer
        || datatypeIri == Vocabulary.Xsd.Long
        || datatypeIri == Vocabulary.Xsd.Int
        || datatypeIri == Vocabulary.Xsd.Short
        || datatypeIri == Vocabulary.Xsd.ByteValue
        || datatypeIri == Vocabulary.Xsd.UnsignedLong
        || datatypeIri == Vocabulary.Xsd.UnsignedInt
        || datatypeIri == Vocabulary.Xsd.UnsignedShort
        || datatypeIri == Vocabulary.Xsd.UnsignedByte
        || datatypeIri == Vocabulary.Xsd.NonNegativeInteger
        || datatypeIri == Vocabulary.Xsd.NonPositiveInteger
        || datatypeIri == Vocabulary.Xsd.PositiveInteger
        || datatypeIri == Vocabulary.Xsd.NegativeInteger;
}

/// <summary>
/// Discriminator for the four slots of <see cref="NumericValue"/>.
/// Ordered so that <c>(int)</c> comparison gives the SPARQL
/// promotion target.
/// </summary>
internal enum NumericKind
{
    Integer = 0,
    Decimal = 1,
    Float = 2,
    Double = 3,
}
