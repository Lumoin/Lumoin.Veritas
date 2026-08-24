using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// A point in the plane: the input and vertex type the geometry works over. A
/// small immutable value with no behaviour of its own — geometry stays in the
/// kernel and the algorithms, never on the coordinate carrier.
/// </summary>
/// <remarks>
/// Laid out as two sequential doubles (X then Y) so a span of points reinterprets
/// as an interleaved coordinate buffer without copying.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y})")]
public readonly record struct Point2d(double X, double Y)
{
    /// <summary>Renders the point with round-trip coordinate precision.</summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R})");
    }
}
