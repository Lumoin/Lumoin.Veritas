using System.Globalization;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// Parses numeric lexical forms into exact values for the OWL 2 datatype
/// map through two surfaces. <see cref="TryGetValue"/> answers in
/// <see cref="NumericValue"/>: the XSD numeric tower directly, plus the
/// <c>owl:rational</c> <c>numerator/denominator</c> form, which converts to
/// an exact decimal when its reduced denominator divides a power of ten —
/// so <c>"1/2"^^owl:rational</c> and <c>"0.5"^^xsd:decimal</c> compare
/// equal, while a rational outside decimal precision stays unparsed and the
/// callers treat it as unknown. <see cref="TryGetFraction"/> answers in
/// <see cref="ExactRational"/> instead, which puts every exact-real lexical
/// — the non-terminating rationals included — on one exactly comparable
/// footing.
/// </summary>
public static class OwlNumericLexicals
{
    /// <summary>
    /// Parses a numeric literal's lexical form per its datatype.
    /// </summary>
    /// <param name="lexicalForm">The lexical form.</param>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="value">The parsed exact value.</param>
    /// <returns><see langword="true"/> when the form parsed.</returns>
    public static bool TryGetValue(string lexicalForm, Utf8String datatypeIri, out NumericValue value)
    {
        System.ArgumentNullException.ThrowIfNull(lexicalForm);

        if(datatypeIri.Equals(OwlVocabulary.Rational))
        {
            return TryParseRational(lexicalForm, out value);
        }

        if(datatypeIri.Equals(OwlVocabulary.Real))
        {
            //owl:real has no lexical space of its own; the corpus carries
            //real values through its subtypes.
            value = default;

            return false;
        }

        return NumericValue.TryParse(lexicalForm, datatypeIri, out value);
    }

    /// <summary>
    /// Parses a numeric literal's lexical form into an exact fraction on the
    /// exact-real line. An <c>owl:rational</c> form yields its raw pair, so a
    /// non-terminating rational such as <c>1/3</c> is exact here where
    /// <see cref="TryGetValue"/> leaves it unparsed; a non-positive
    /// denominator yields no fraction, which is the admission
    /// <see cref="TryGetValue"/> also gives it. An <c>xsd:decimal</c> or
    /// integer-tower form validates through <see cref="NumericValue.TryParse"/>
    /// and converts exactly. The <c>xsd:float</c> and <c>xsd:double</c> spaces
    /// are disjoint copies of the real line and <c>owl:real</c> has no lexical
    /// space of its own, so none of them yields a fraction.
    /// </summary>
    /// <param name="lexicalForm">The lexical form.</param>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="fraction">The parsed exact fraction.</param>
    /// <returns><see langword="true"/> when the form yielded a fraction.</returns>
    public static bool TryGetFraction(string lexicalForm, Utf8String datatypeIri, out ExactRational fraction)
    {
        System.ArgumentNullException.ThrowIfNull(lexicalForm);

        fraction = default;

        if(datatypeIri.Equals(OwlVocabulary.Rational))
        {
            return TryParseRationalFraction(lexicalForm, out fraction);
        }

        if(!NumericValue.TryParse(lexicalForm, datatypeIri, out NumericValue value))
        {
            return false;
        }

        if(value.Kind == NumericKind.Integer)
        {
            fraction = new ExactRational(value.AsInteger(), BigInteger.One);

            return true;
        }

        if(value.Kind == NumericKind.Decimal)
        {
            fraction = DecimalFraction(value.AsDecimal());

            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the raw numerator/denominator pair of an <c>owl:rational</c> form.
    /// The denominator must be strictly positive:
    /// <see cref="BigInteger.GreatestCommonDivisor"/> is non-negative, so a
    /// negative denominator survives Construct's reduction and leaves its
    /// twos-and-fives loop unable to reach one, which makes every such form
    /// unparsed on the <see cref="NumericValue"/> surface too.
    /// </summary>
    /// <param name="lexicalForm">The lexical form.</param>
    /// <param name="fraction">The parsed exact fraction.</param>
    /// <returns><see langword="true"/> when the form yielded a fraction.</returns>
    private static bool TryParseRationalFraction(string lexicalForm, out ExactRational fraction)
    {
        fraction = default;

        int slash = lexicalForm.IndexOf('/', System.StringComparison.Ordinal);
        if(slash <= 0 || slash == lexicalForm.Length - 1)
        {
            //A rational without a denominator part is an integer form.
            if(!BigInteger.TryParse(lexicalForm.Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger plain))
            {
                return false;
            }

            fraction = new ExactRational(plain, BigInteger.One);

            return true;
        }

        if(!BigInteger.TryParse(lexicalForm[..slash].Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger numerator)
            || !BigInteger.TryParse(lexicalForm[(slash + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger denominator)
            || denominator.Sign <= 0)
        {
            return false;
        }

        fraction = new ExactRational(numerator, denominator);

        return true;
    }

    /// <summary>
    /// Converts a decimal to its exact fraction from the value's own bits: the
    /// 96-bit mantissa spans the first three words little-endian, and the fourth
    /// carries the sign bit and the base-ten scale, so the value is exactly
    /// mantissa / 10^scale.
    /// </summary>
    /// <param name="value">The decimal value to convert.</param>
    /// <returns>The exact fraction denoting the same value.</returns>
    private static ExactRational DecimalFraction(decimal value)
    {
        System.Span<int> bits = stackalloc int[4];
        _ = decimal.GetBits(value, bits);

        BigInteger mantissa = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | new BigInteger((uint)bits[0]);
        int flags = bits[3];
        if(flags < 0)
        {
            mantissa = -mantissa;
        }

        int scale = (flags >> 16) & 0xFF;

        return new ExactRational(mantissa, BigInteger.Pow(10, scale));
    }

    private static bool TryParseRational(string lexicalForm, out NumericValue value)
    {
        value = default;

        int slash = lexicalForm.IndexOf('/', System.StringComparison.Ordinal);
        if(slash <= 0 || slash == lexicalForm.Length - 1)
        {
            //A rational without a denominator part is an integer form.
            return BigInteger.TryParse(lexicalForm.Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger plain)
                && Construct(plain, BigInteger.One, out value);
        }

        if(!BigInteger.TryParse(lexicalForm[..slash].Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger numerator)
            || !BigInteger.TryParse(lexicalForm[(slash + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger denominator)
            || denominator.IsZero)
        {
            return false;
        }

        return Construct(numerator, denominator, out value);
    }

    //Reduces the fraction and converts it to an exact decimal when the
    //denominator's prime factors are only 2 and 5 within decimal's
    //precision.
    private static bool Construct(BigInteger numerator, BigInteger denominator, out NumericValue value)
    {
        value = default;

        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if(!divisor.IsOne && !divisor.IsZero)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        if(denominator.IsOne)
        {
            value = new NumericValue(numerator);

            return true;
        }

        //Strip the factors of 2 and 5; anything left makes the expansion
        //non-terminating.
        BigInteger remaining = denominator;
        int twos = 0;
        while(remaining.IsEven)
        {
            remaining /= 2;
            twos++;
        }

        int fives = 0;
        while(remaining % 5 == 0)
        {
            remaining /= 5;
            fives++;
        }

        if(remaining != BigInteger.One)
        {
            return false;
        }

        //numerator / (2^a · 5^b) = numerator · 5^a · 2^b / 10^(a+b).
        int scale = twos > fives ? twos : fives;
        BigInteger scaled = numerator * BigInteger.Pow(10, scale) / denominator;
        if(scale > 28 || BigInteger.Abs(scaled) > new BigInteger(decimal.MaxValue))
        {
            return false;
        }

        decimal result = (decimal)scaled / Pow10(scale);
        value = new NumericValue(result);

        return true;
    }

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;
        for(int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
