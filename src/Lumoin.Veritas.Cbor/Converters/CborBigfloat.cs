using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// A bigfloat as defined by CBOR Tag 5: the value is
/// <c>Mantissa * 2^Exponent</c>, with an integer exponent and a (possibly
/// big-integer) mantissa.
/// </summary>
/// <param name="Exponent">The base-2 exponent.</param>
/// <param name="Mantissa">The integer mantissa.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborBigfloat(int Exponent, BigInteger Mantissa)
{
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{Mantissa} * 2^{Exponent}");
}
