using System.Numerics;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The differential oracle for exact-arithmetic tests: a dyadic
/// rational <c>Numerator × 2^Exponent</c> in arbitrary precision.
/// Every finite <see cref="double"/> is exactly representable here, so
/// sums and products of doubles can be verified bit-exactly with no
/// rounding anywhere — mathematically ground truth, no recorded
/// vectors and no native reference build required.
/// </summary>
internal readonly struct ExactRational
{
    /// <summary>The integer numerator; carries the sign.</summary>
    public BigInteger Numerator { get; }

    /// <summary>The power-of-two scale applied to <see cref="Numerator"/>.</summary>
    public int Exponent { get; }

    /// <summary>The exact sign of the represented value: -1, 0 or 1.</summary>
    public int Sign => Numerator.Sign;

    /// <summary>Builds the rational <paramref name="numerator"/> × 2^<paramref name="exponent"/>.</summary>
    public ExactRational(BigInteger numerator, int exponent)
    {
        Numerator = numerator;
        Exponent = exponent;
    }

    /// <summary>
    /// Decomposes a finite double exactly: subnormals scale from
    /// <c>2^-1074</c>, normals carry the implicit leading bit.
    /// </summary>
    public static ExactRational FromDouble(double value)
    {
        if(!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Only finite doubles are exactly representable.");
        }

        long bits = BitConverter.DoubleToInt64Bits(value);
        bool isNegative = bits < 0;
        int biasedExponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xF_FFFF_FFFF_FFFF;

        BigInteger numerator;
        int exponent;

        if(biasedExponent == 0)
        {
            numerator = mantissa;
            exponent = -1074;
        }
        else
        {
            numerator = mantissa | (1L << 52);
            exponent = biasedExponent - 1075;
        }

        if(isNegative)
        {
            numerator = -numerator;
        }

        return new ExactRational(numerator, exponent);
    }

    /// <summary>The exact sum of all components of an expansion.</summary>
    public static ExactRational SumOf(ReadOnlySpan<double> components)
    {
        ExactRational total = FromDouble(0.0);

        for(int index = 0; index < components.Length; index++)
        {
            total += FromDouble(components[index]);
        }

        return total;
    }

    /// <summary>The exact sum; numerators align to the smaller exponent.</summary>
    public static ExactRational operator +(ExactRational left, ExactRational right)
    {
        if(left.Exponent == right.Exponent)
        {
            return new ExactRational(left.Numerator + right.Numerator, left.Exponent);
        }

        //Aligns to the smaller exponent so both numerators are integers
        //at the same scale; the shift is exact.
        if(left.Exponent > right.Exponent)
        {
            BigInteger shifted = left.Numerator << (left.Exponent - right.Exponent);

            return new ExactRational(shifted + right.Numerator, right.Exponent);
        }

        BigInteger shiftedRight = right.Numerator << (right.Exponent - left.Exponent);

        return new ExactRational(left.Numerator + shiftedRight, left.Exponent);
    }

    /// <summary>The exact negation.</summary>
    public static ExactRational operator -(ExactRational value)
    {
        return new ExactRational(-value.Numerator, value.Exponent);
    }

    /// <summary>The exact difference.</summary>
    public static ExactRational operator -(ExactRational left, ExactRational right)
    {
        return left + (-right);
    }

    /// <summary>The exact product; exponents add.</summary>
    public static ExactRational operator *(ExactRational left, ExactRational right)
    {
        return new ExactRational(left.Numerator * right.Numerator, left.Exponent + right.Exponent);
    }

    /// <summary>
    /// Exact value equality regardless of representation — <c>(2, 0)</c>
    /// and <c>(1, 1)</c> are the same value.
    /// </summary>
    public bool ValueEquals(ExactRational other)
    {
        return (this - other).Numerator.IsZero;
    }
}
