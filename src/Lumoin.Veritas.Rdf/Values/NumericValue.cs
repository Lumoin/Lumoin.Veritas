using System;
using System.Diagnostics;
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
/// <para>
/// Beyond parse and compare, the type carries the SPARQL §17.4 arithmetic
/// (<see cref="Add"/>/<see cref="Subtract"/>/<see cref="Multiply"/>/
/// <see cref="Divide"/>/<see cref="Negate"/>, each promoting to the higher
/// operand kind) and renders back to the XSD canonical lexical form
/// (<see cref="ToCanonicalLexical"/>) with its result datatype IRI
/// (<see cref="DatatypeIri"/>) — so the same numeric tower drives the SPARQL
/// expression evaluator, SHACL value-range checks, and OWL.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToCanonicalLexical(),nq} ({Kind})")]
public readonly struct NumericValue: IEquatable<NumericValue>
{
    /// <summary>Which slot of the union carries the value.</summary>
    public NumericKind Kind { get; }

    private readonly BigInteger integerValue;
    private readonly decimal decimalValue;
    private readonly float floatValue;
    private readonly double doubleValue;

    /// <summary>Constructs an <c>xsd:integer</c>-kind value.</summary>
    /// <param name="value">The integer value.</param>
    public NumericValue(BigInteger value)
    {
        Kind = NumericKind.Integer;
        integerValue = value;
        decimalValue = default;
        floatValue = default;
        doubleValue = default;
    }

    /// <summary>Constructs an <c>xsd:decimal</c>-kind value.</summary>
    /// <param name="value">The decimal value.</param>
    public NumericValue(decimal value)
    {
        Kind = NumericKind.Decimal;
        integerValue = default;
        decimalValue = value;
        floatValue = default;
        doubleValue = default;
    }

    /// <summary>Constructs an <c>xsd:float</c>-kind value.</summary>
    /// <param name="value">The float value.</param>
    public NumericValue(float value)
    {
        Kind = NumericKind.Float;
        integerValue = default;
        decimalValue = default;
        floatValue = value;
        doubleValue = default;
    }

    /// <summary>Constructs an <c>xsd:double</c>-kind value.</summary>
    /// <param name="value">The double value.</param>
    public NumericValue(double value)
    {
        Kind = NumericKind.Double;
        integerValue = default;
        decimalValue = default;
        floatValue = default;
        doubleValue = value;
    }

    /// <summary>Returns the value as a <see cref="BigInteger"/> (meaningful when <see cref="Kind"/> is <see cref="NumericKind.Integer"/>).</summary>
    /// <returns>The integer slot.</returns>
    public BigInteger AsInteger() => integerValue;

    /// <summary>Returns the value as a <see cref="decimal"/> (meaningful when <see cref="Kind"/> is <see cref="NumericKind.Decimal"/>).</summary>
    /// <returns>The decimal slot.</returns>
    public decimal AsDecimal() => decimalValue;

    /// <summary>Returns the value as a <see cref="float"/> (meaningful when <see cref="Kind"/> is <see cref="NumericKind.Float"/>).</summary>
    /// <returns>The float slot.</returns>
    public float AsFloat() => floatValue;

    /// <summary>Returns the value as a <see cref="double"/> (meaningful when <see cref="Kind"/> is <see cref="NumericKind.Double"/>).</summary>
    /// <returns>The double slot.</returns>
    public double AsDouble() => doubleValue;

    /// <summary>The XSD datatype IRI matching this value's <see cref="Kind"/> (<c>xsd:integer</c>/<c>decimal</c>/<c>float</c>/<c>double</c>).</summary>
    public Utf8String DatatypeIri => Kind switch
    {
        NumericKind.Integer => Vocabulary.Xsd.Integer,
        NumericKind.Decimal => Vocabulary.Xsd.Decimal,
        NumericKind.Float => Vocabulary.Xsd.Float,
        _ => Vocabulary.Xsd.Double
    };

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

    /// <summary>Returns whether this value is numerically equal to another under the SPARQL promotion lattice (so <c>1</c> integer equals <c>1.0</c> decimal); a NaN operand is never equal.</summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are numerically equal.</returns>
    public bool Equals(NumericValue other) => Compare(this, other) == ComparisonResult.Equal;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NumericValue other && Equals(other);

    /// <summary>Computes a hash consistent with numeric equality by hashing the value promoted to <see cref="double"/> (so equal values across kinds collide).</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => ToDouble(this).GetHashCode();

    /// <summary>Returns whether two numeric values are numerically equal under the promotion lattice.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when numerically equal.</returns>
    public static bool operator ==(NumericValue left, NumericValue right) => left.Equals(right);

    /// <summary>Returns whether two numeric values are not numerically equal under the promotion lattice.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when not numerically equal.</returns>
    public static bool operator !=(NumericValue left, NumericValue right) => !left.Equals(right);

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

    /// <summary>Adds two numeric values, promoting to the higher operand kind (SPARQL §17.4).</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum, in the promoted kind.</returns>
    public static NumericValue Add(NumericValue left, NumericValue right)
    {
        return Combine(left, right,
            static (a, b) => a + b,
            static (a, b) => a + b,
            static (a, b) => a + b,
            static (a, b) => a + b);
    }

    /// <summary>Subtracts the right value from the left, promoting to the higher operand kind.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference, in the promoted kind.</returns>
    public static NumericValue Subtract(NumericValue left, NumericValue right)
    {
        return Combine(left, right,
            static (a, b) => a - b,
            static (a, b) => a - b,
            static (a, b) => a - b,
            static (a, b) => a - b);
    }

    /// <summary>Multiplies two numeric values, promoting to the higher operand kind.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product, in the promoted kind.</returns>
    public static NumericValue Multiply(NumericValue left, NumericValue right)
    {
        return Combine(left, right,
            static (a, b) => a * b,
            static (a, b) => a * b,
            static (a, b) => a * b,
            static (a, b) => a * b);
    }

    /// <summary>
    /// Divides the left value by the right (SPARQL §17.4 / XPath <c>op:numeric-divide</c>): integer/integer yields an
    /// <c>xsd:decimal</c>, decimal/decimal stays decimal, float and double divide in their kind. A zero divisor in
    /// the integer or decimal kind yields <see langword="false"/> (the caller raises an error); float/double divide
    /// by zero produces the IEEE infinity/NaN, which is a valid result.
    /// </summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="result">Receives the quotient on success.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> for an exact-kind division by zero.</returns>
    public static bool TryDivide(NumericValue left, NumericValue right, out NumericValue result)
    {
        NumericKind target = left.Kind > right.Kind ? left.Kind : right.Kind;

        //integer ÷ integer is defined to be xsd:decimal (it is not integer division).
        if(target == NumericKind.Integer)
        {
            target = NumericKind.Decimal;
        }

        switch(target)
        {
            case NumericKind.Decimal:
            {
                decimal divisor = ToDecimal(right);
                if(divisor == 0m)
                {
                    result = default;

                    return false;
                }

                result = new NumericValue(ToDecimal(left) / divisor);

                return true;
            }
            case NumericKind.Float:
            {
                result = new NumericValue(ToFloat(left) / ToFloat(right));

                return true;
            }
            default:
            {
                result = new NumericValue(ToDouble(left) / ToDouble(right));

                return true;
            }
        }
    }

    /// <summary>Negates the value, preserving its kind.</summary>
    /// <returns>The negated value.</returns>
    public NumericValue Negate()
    {
        return Kind switch
        {
            NumericKind.Integer => new NumericValue(-integerValue),
            NumericKind.Decimal => new NumericValue(-decimalValue),
            NumericKind.Float => new NumericValue(-floatValue),
            _ => new NumericValue(-doubleValue)
        };
    }

    /// <summary>Applies the kind-specific binary operation after promoting both operands to the higher of the two kinds.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="onInteger">The operation in the integer kind.</param>
    /// <param name="onDecimal">The operation in the decimal kind.</param>
    /// <param name="onFloat">The operation in the float kind.</param>
    /// <param name="onDouble">The operation in the double kind.</param>
    /// <returns>The result in the promoted kind.</returns>
    /// <summary>A binary operation over two <c>xsd:integer</c> (arbitrary-precision) operands.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The result.</returns>
    private delegate BigInteger IntegerBinaryOperation(BigInteger left, BigInteger right);

    /// <summary>A binary operation over two <c>xsd:decimal</c> operands.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The result.</returns>
    private delegate decimal DecimalBinaryOperation(decimal left, decimal right);

    /// <summary>A binary operation over two <c>xsd:float</c> operands.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The result.</returns>
    private delegate float FloatBinaryOperation(float left, float right);

    /// <summary>A binary operation over two <c>xsd:double</c> operands.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The result.</returns>
    private delegate double DoubleBinaryOperation(double left, double right);

    private static NumericValue Combine(
        NumericValue left,
        NumericValue right,
        IntegerBinaryOperation onInteger,
        DecimalBinaryOperation onDecimal,
        FloatBinaryOperation onFloat,
        DoubleBinaryOperation onDouble)
    {
        NumericKind target = left.Kind > right.Kind ? left.Kind : right.Kind;

        return target switch
        {
            NumericKind.Integer => new NumericValue(onInteger(left.integerValue, right.integerValue)),
            NumericKind.Decimal => new NumericValue(onDecimal(ToDecimal(left), ToDecimal(right))),
            NumericKind.Float => new NumericValue(onFloat(ToFloat(left), ToFloat(right))),
            _ => new NumericValue(onDouble(ToDouble(left), ToDouble(right)))
        };
    }

    private static decimal ToDecimal(NumericValue v)
        => v.Kind switch
        {
            NumericKind.Integer => (decimal)v.integerValue,
            NumericKind.Decimal => v.decimalValue,
            NumericKind.Float => (decimal)v.floatValue,
            _ => (decimal)v.doubleValue
        };

    /// <summary>
    /// Renders this value in its XSD canonical lexical form: <c>xsd:integer</c> as digits; <c>xsd:decimal</c> always
    /// with a decimal point (e.g. <c>1.0</c>, <c>11.1</c>); <c>xsd:float</c>/<c>xsd:double</c> in the mantissa-and-
    /// exponent form the W3C results fixtures use (e.g. <c>3.21E4</c>, <c>4.0E-1</c>, <c>1.0E2</c>), with <c>INF</c>/
    /// <c>-INF</c>/<c>NaN</c> for the specials.
    /// </summary>
    /// <returns>The canonical lexical form.</returns>
    public string ToCanonicalLexical()
    {
        return Kind switch
        {
            NumericKind.Integer => integerValue.ToString(CultureInfo.InvariantCulture),
            NumericKind.Decimal => CanonicalDecimal(decimalValue),
            NumericKind.Float => CanonicalFloating(floatValue, float.IsNaN(floatValue), float.IsPositiveInfinity(floatValue), float.IsNegativeInfinity(floatValue), ((double)floatValue).ToString("R", CultureInfo.InvariantCulture)),
            _ => CanonicalFloating(doubleValue, double.IsNaN(doubleValue), double.IsPositiveInfinity(doubleValue), double.IsNegativeInfinity(doubleValue), doubleValue.ToString("R", CultureInfo.InvariantCulture))
        };
    }

    /// <summary>Renders an <c>xsd:decimal</c> in canonical form: a mandatory decimal point, trailing fractional zeros trimmed to one (so an integral decimal is <c>N.0</c>).</summary>
    /// <param name="value">The decimal value.</param>
    /// <returns>The canonical decimal lexical form.</returns>
    private static string CanonicalDecimal(decimal value)
    {
        //decimal preserves trailing zeros from its scale; normalize so e.g. 2.20 → "2.2" and 6 → "6.0".
        decimal normalized = value / 1.000000000000000000000000000000000m;
        string text = normalized.ToString(CultureInfo.InvariantCulture);

        return text.Contains('.', StringComparison.Ordinal) ? text : text + ".0";
    }

    /// <summary>Renders an <c>xsd:float</c>/<c>xsd:double</c> in XSD canonical scientific form (one digit before the point, an <c>E</c> exponent), or the <c>INF</c>/<c>-INF</c>/<c>NaN</c> specials.</summary>
    /// <param name="value">The numeric value (used only to test for zero).</param>
    /// <param name="isNaN">Whether the value is NaN.</param>
    /// <param name="isPositiveInfinity">Whether the value is +∞.</param>
    /// <param name="isNegativeInfinity">Whether the value is −∞.</param>
    /// <param name="roundTrip">The round-trip ("R") rendering of the value, used only for a finite value.</param>
    /// <returns>The canonical floating lexical form.</returns>
    private static string CanonicalFloating(double value, bool isNaN, bool isPositiveInfinity, bool isNegativeInfinity, string roundTrip)
    {
        if(isNaN)
        {
            return "NaN";
        }

        if(isPositiveInfinity)
        {
            return "INF";
        }

        if(isNegativeInfinity)
        {
            return "-INF";
        }

        return ToScientific(roundTrip, value == 0);
    }

    /// <summary>
    /// Normalizes a .NET round-trip numeric rendering into the XSD canonical mantissa<c>E</c>exponent form: exactly
    /// one (possibly signed) digit before the decimal point, a fractional part with at least one digit, and a
    /// base-ten exponent with a sign (e.g. <c>3.21E4</c>, <c>4.0E-1</c>, <c>1.0E2</c>, <c>0.0E0</c>).
    /// </summary>
    /// <param name="roundTrip">The .NET "R"-format rendering (e.g. <c>32100</c>, <c>0.4</c>, <c>1E-10</c>).</param>
    /// <param name="isZero">Whether the value is zero (rendered <c>0.0E0</c>).</param>
    /// <returns>The canonical scientific form.</returns>
    private static string ToScientific(string roundTrip, bool isZero)
    {
        if(isZero)
        {
            return "0.0E0";
        }

        bool negative = roundTrip[0] == '-';
        string magnitude = negative ? roundTrip[1..] : roundTrip;

        //Split any existing exponent off the .NET rendering, then re-derive a single-leading-digit mantissa.
        int exponent = 0;
        int eIndex = magnitude.IndexOfAny(['E', 'e']);
        if(eIndex >= 0)
        {
            exponent = int.Parse(magnitude[(eIndex + 1)..], CultureInfo.InvariantCulture);
            magnitude = magnitude[..eIndex];
        }

        //Collect all significant digits and the position of the decimal point within them.
        int pointIndex = magnitude.IndexOf('.', StringComparison.Ordinal);
        string digits = pointIndex >= 0 ? magnitude.Remove(pointIndex, 1) : magnitude;
        int integerDigits = pointIndex >= 0 ? pointIndex : magnitude.Length;

        //The decimal point currently sits after `integerDigits` digits; canonical form puts it after the first
        //significant digit, so the exponent shifts by (leading-zero-trimmed integerDigits - 1).
        int firstSignificant = 0;
        while(firstSignificant < digits.Length - 1 && digits[firstSignificant] == '0')
        {
            firstSignificant++;
        }

        digits = digits[firstSignificant..].TrimEnd('0');
        if(digits.Length == 0)
        {
            digits = "0";
        }

        exponent += integerDigits - firstSignificant - 1;
        string mantissa = digits.Length == 1 ? digits + ".0" : digits[..1] + "." + digits[1..];

        return (negative ? "-" : string.Empty) + mantissa + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

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
public enum NumericKind
{
    Integer = 0,
    Decimal = 1,
    Float = 2,
    Double = 3,
}
