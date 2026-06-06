using System.Globalization;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// Parses numeric lexical forms into exact values for the OWL 2 datatype
/// map: the XSD numeric tower through <see cref="NumericValue"/>, plus the
/// <c>owl:rational</c> <c>numerator/denominator</c> form, which converts to
/// an exact decimal when its reduced denominator divides a power of ten —
/// so <c>"1/2"^^owl:rational</c> and <c>"0.5"^^xsd:decimal</c> compare
/// equal. A rational outside decimal precision stays unparsed, which the
/// callers treat as unknown.
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
