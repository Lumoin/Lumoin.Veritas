using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Projections;

/// <summary>
/// Converts between geodetic and authalic (equal-area) latitude via an order-6 Clenshaw-summed Fourier
/// series. The coefficient tables and the recurrence's variable-reuse order are transcribed exactly —
/// reordering shifts float64 rounding at the ulp level, which matters at this projection's 1e-10
/// fixture tolerance.
/// </summary>
internal static class AuthalicProjection
{
    /// <summary>Coefficients for the geodetic-to-authalic direction, transcribed digit-for-digit.</summary>
    private static double[] GeodeticToAuthalic { get; } =
    [
        -2.2392098386786394e-3, 2.1308606513250217e-6, -2.5592576864212742e-9, 3.3701965267802837e-12,
        -4.6675453126112487e-15, 6.6749287038481596e-18
    ];

    /// <summary>Coefficients for the authalic-to-geodetic direction, transcribed digit-for-digit.</summary>
    private static double[] AuthalicToGeodetic { get; } =
    [
        2.2392089963541657e-3, 2.8831978048607556e-6, 5.0862207399726603e-9, 1.02018123778161e-11,
        2.1912872306767718e-14, 4.9284235482523806e-17
    ];

    /// <summary>
    /// The geodetic-to-authalic coefficient table, exposed for the batch point-to-cell kernel core so
    /// its lane-wise Clenshaw mirror of <see cref="Forward"/> broadcasts the exact same coefficient
    /// values the scalar recurrence reads.
    /// </summary>
    internal static ReadOnlySpan<double> GeodeticToAuthalicCoefficients => GeodeticToAuthalic;

    /// <summary>Converts geodetic latitude (radians) to authalic latitude (radians).</summary>
    public static double Forward(double phi)
    {
        return ApplyCoefficients(phi, GeodeticToAuthalic);
    }

    /// <summary>Converts authalic latitude (radians) to geodetic latitude (radians).</summary>
    public static double Inverse(double phi)
    {
        return ApplyCoefficients(phi, AuthalicToGeodetic);
    }

    /// <summary>
    /// Order-6 Clenshaw summation. The <c>u0</c>/<c>u1</c> reuse order below is transcribed
    /// line-by-line and must not be reassociated; the same applies to the factored
    /// trig identity <c>2·(cosPhi − sinPhi)·(cosPhi + sinPhi)</c>, which stays factored rather than
    /// folding to <c>2·cos(2·phi)</c>.
    /// </summary>
    private static double ApplyCoefficients(double phi, double[] coefficients)
    {
        double sinPhi = Math.Sin(phi);
        double cosPhi = Math.Cos(phi);
        double x = 2 * (cosPhi - sinPhi) * (cosPhi + sinPhi);

        double u0 = (x * coefficients[5]) + coefficients[4];
        double u1 = (x * u0) + coefficients[3];
        u0 = (x * u1) - u0 + coefficients[2];
        u1 = (x * u0) - u1 + coefficients[1];
        u0 = (x * u1) - u0 + coefficients[0];

        return phi + (2 * sinPhi * cosPhi * u0);
    }
}
