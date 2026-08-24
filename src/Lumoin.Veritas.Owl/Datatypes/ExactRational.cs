using System;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// An exact value of the OWL 2 exact-real line held as a normalized
/// numerator/denominator pair of arbitrary-precision integers: the fraction is
/// reduced by its greatest common divisor, the denominator is strictly
/// positive, and the sign rides on the numerator, so one value has one
/// representation. Comparison is <see cref="BigInteger"/>
/// cross-multiplication — exact, total, and free of rounding, of a precision
/// ceiling, and of the signed zero and <c>NaN</c> the floating spaces carry.
/// The <c>default</c> instance carries a zero denominator and denotes no
/// value; every constructed instance is normalized.
/// </summary>
[DebuggerDisplay("{Numerator}/{Denominator}")]
public readonly struct ExactRational: IEquatable<ExactRational>
{
    /// <summary>Constructs the normalized fraction of a numerator and a non-zero denominator.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator; zero denotes no value and is an invariant violation.</param>
    public ExactRational(BigInteger numerator, BigInteger denominator)
    {
        ArgumentOutOfRangeException.ThrowIfZero(denominator);

        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if(!divisor.IsOne)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        if(denominator.Sign < 0)
        {
            //The greatest common divisor is non-negative, so a negative
            //denominator survives the reduction and moves its sign to the
            //numerator here.
            numerator = -numerator;
            denominator = -denominator;
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>The reduced numerator, carrying the value's sign.</summary>
    public BigInteger Numerator { get; }

    /// <summary>The reduced denominator, strictly positive in every constructed fraction.</summary>
    public BigInteger Denominator { get; }

    /// <summary>
    /// Compares two fractions exactly by cross-multiplication.
    /// </summary>
    /// <param name="left">The left fraction.</param>
    /// <param name="right">The right fraction.</param>
    /// <returns>The comparison verdict; never <see cref="ComparisonResult.Incomparable"/>, because the exact-real line is totally ordered.</returns>
    public static ComparisonResult Compare(ExactRational left, ExactRational right)
    {
        //Both denominators are positive, so cross-multiplication preserves the
        //ordering with no sign correction.
        int sign = (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);

        return sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal
        };
    }

    /// <summary>
    /// Whether the fraction's decimal expansion terminates — the
    /// <c>xsd:decimal</c> membership question, since that value space holds
    /// exactly the fractions whose reduced denominator has no prime factor
    /// besides two and five.
    /// </summary>
    /// <returns><see langword="true"/> when the expansion terminates.</returns>
    public bool HasTerminatingDecimalExpansion()
    {
        if(Denominator.IsZero)
        {
            //The default instance denotes no value, so it has no expansion.
            return false;
        }

        BigInteger remaining = Denominator;
        while(remaining.IsEven)
        {
            remaining /= 2;
        }

        while(remaining % 5 == 0)
        {
            remaining /= 5;
        }

        return remaining.IsOne;
    }

    /// <summary>Whether this fraction denotes the same value as another; normalization makes representation equality value equality.</summary>
    /// <param name="other">The fraction to compare against.</param>
    /// <returns><see langword="true"/> when the two denote one value.</returns>
    public bool Equals(ExactRational other)
    {
        return Numerator == other.Numerator && Denominator == other.Denominator;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ExactRational other && Equals(other);
    }

    /// <summary>Computes a hash consistent with value equality over the normalized pair.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Numerator, Denominator);
    }

    /// <summary>Whether two fractions denote one value.</summary>
    /// <param name="left">The left fraction.</param>
    /// <param name="right">The right fraction.</param>
    /// <returns><see langword="true"/> when the two denote one value.</returns>
    public static bool operator ==(ExactRational left, ExactRational right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two fractions denote different values.</summary>
    /// <param name="left">The left fraction.</param>
    /// <param name="right">The right fraction.</param>
    /// <returns><see langword="true"/> when the two denote different values.</returns>
    public static bool operator !=(ExactRational left, ExactRational right)
    {
        return !left.Equals(right);
    }
}
