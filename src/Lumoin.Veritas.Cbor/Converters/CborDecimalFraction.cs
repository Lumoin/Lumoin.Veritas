using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// A decimal fraction as defined by CBOR Tag 4: the value is
/// <c>Mantissa * 10^Exponent</c>, with an integer exponent and a (possibly
/// big-integer) mantissa.
/// </summary>
/// <param name="Exponent">The base-10 exponent.</param>
/// <param name="Mantissa">The integer mantissa.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborDecimalFraction(int Exponent, BigInteger Mantissa)
{
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{Mantissa} * 10^{Exponent}");
}
