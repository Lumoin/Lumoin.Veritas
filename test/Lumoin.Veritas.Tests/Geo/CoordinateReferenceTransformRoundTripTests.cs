using CsCheck;
using Lumoin.Veritas.Geo.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The certified round-trip bounds as pure absolute ceilings: the geographic
/// leg (CRS84 → Web Mercator → CRS84) and the metre leg (Web Mercator → CRS84
/// → Web Mercator), each swept unseeded across its accepted domain plus a
/// top-decile metre band and deterministic near-boundary rows, and the
/// boundary cross-tab pinning — per axis and per sign, as an
/// environment-verified fact of this runtime's arithmetic — which boundary
/// round trips succeed and which the image-validity rule refuses. The direct
/// Web Mercator boundary inputs (±π·R on either axis) refuse outright and
/// are pinned in the surface test family; this file's cross-tab walks the
/// CRS84 side of the boundary, where the forward leg succeeds and only the
/// return leg's image-validity rule is at stake. The EPSG:4326 composed
/// pairs carry their own directly-swept bounds here, so every certified
/// ordered pair's ceiling is asserted by its own sweep.
/// </summary>
[TestClass]
internal sealed class CoordinateReferenceTransformRoundTripTests
{
    /// <summary>
    /// Absolute ceiling, in degrees, for the CRS84 → Web Mercator → CRS84
    /// round trip. The measured worst deviation on this runtime is
    /// approximately 5.7e-14°, so this ceiling carries roughly an order of
    /// magnitude of margin; the ordinate leg's error is a few ulps of the
    /// intermediate latitude carried through four transcendental calls.
    /// </summary>
    private const double GeographicRoundTripCeilingDegrees = 1e-12;

    /// <summary>
    /// Absolute ceiling, in metres, for the Web Mercator → CRS84 → Web
    /// Mercator round trip. The ordinate leg's conditioning,
    /// dy/dφ = R·sec(φ)·π/180, reaches approximately 1.29e6 metres per degree
    /// at the latitude limit, so one ulp of the intermediate latitude is
    /// already about 1.83e-8 metres of y; the measured worst case is
    /// approximately 5.2e-8 metres, an irreducible few latitude-ulps, and
    /// this ceiling sits an order of magnitude above it.
    /// </summary>
    private const double MetreRoundTripCeilingMeters = 5e-7;

    /// <summary>Half the Web Mercator world extent in metres, π · R — the projection square's edge on both axes.</summary>
    private const double HalfWorldMeters = Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters;

    /// <summary>The metre-side sweep bound, kept a millimetre clear of the exact edge so the generator never lands on the refusing sliver.</summary>
    private const double NearBoundaryMeters = HalfWorldMeters - 0.001;

    /// <summary>Sample count for the geographic round-trip sweeps.</summary>
    private const long GeographicRoundTripIterationCount = 500;

    /// <summary>Sample count for the metre-side round-trip sweeps.</summary>
    private const long MetreRoundTripIterationCount = 500;

    /// <summary>The CRS84 to Web Mercator to CRS84 round trip stays within the certified geographic ceiling across an unseeded sweep.</summary>
    [TestMethod]
    public void GeographicRoundTripStaysWithinTheCertifiedCeiling()
    {
        //Deliberately clear of the exact ±180° and ±limit edges, whose forward images land on
        //the projection square's refusing slivers — the boundary cross-tab below pins those.
        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-179.999, 179.999], Gen.Double[-85.051, 85.051],
            (longitude, latitude) => new[] { longitude, latitude });

        pointGenerator.Sample(point => GeographicRoundTripsWithinCeiling(point), iter: GeographicRoundTripIterationCount);
    }

    /// <summary>The Web Mercator to CRS84 to Web Mercator round trip stays within the certified metre ceiling across an unseeded sweep.</summary>
    [TestMethod]
    public void MetreRoundTripStaysWithinTheCertifiedCeiling()
    {
        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-NearBoundaryMeters, NearBoundaryMeters], Gen.Double[-NearBoundaryMeters, NearBoundaryMeters],
            (x, y) => new[] { x, y });

        pointGenerator.Sample(point => MetreRoundTripsWithinCeiling(point), iter: MetreRoundTripIterationCount);
    }

    /// <summary>The metre round trip stays within the certified ceiling across the top decile of positive y, where the error is largest.</summary>
    [TestMethod]
    public void MetreRoundTripTopDecilePositiveYStaysWithinTheCertifiedCeiling()
    {
        //The error is monotone in |y|, so the top decile below the edge is where the worst case lives.
        double lowerBound = 0.9 * HalfWorldMeters;

        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-NearBoundaryMeters, NearBoundaryMeters], Gen.Double[lowerBound, NearBoundaryMeters],
            (x, y) => new[] { x, y });

        pointGenerator.Sample(point => MetreRoundTripsWithinCeiling(point), iter: MetreRoundTripIterationCount);
    }

    /// <summary>The metre round trip stays within the certified ceiling across the top decile of negative y, where the error is largest.</summary>
    [TestMethod]
    public void MetreRoundTripTopDecileNegativeYStaysWithinTheCertifiedCeiling()
    {
        double upperBound = -0.9 * HalfWorldMeters;

        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-NearBoundaryMeters, NearBoundaryMeters], Gen.Double[-NearBoundaryMeters, upperBound],
            (x, y) => new[] { x, y });

        pointGenerator.Sample(point => MetreRoundTripsWithinCeiling(point), iter: MetreRoundTripIterationCount);
    }

    /// <summary>Deterministic near-boundary metre points, one millimetre or a fraction of a percent clear of the edge, round-trip within the certified ceiling.</summary>
    /// <param name="x">The abscissa under test.</param>
    /// <param name="y">The ordinate under test.</param>
    [TestMethod]
    [DataRow(0.0, 0.998 * HalfWorldMeters, DisplayName = "y at 0.998·π·R")]
    [DataRow(0.0, NearBoundaryMeters, DisplayName = "y at π·R minus one millimetre")]
    [DataRow(0.999 * HalfWorldMeters, 0.0, DisplayName = "x at 0.999·π·R")]
    [DataRow(0.0, -(0.998 * HalfWorldMeters), DisplayName = "y at -0.998·π·R")]
    [DataRow(0.0, -NearBoundaryMeters, DisplayName = "y at -(π·R minus one millimetre)")]
    [DataRow(-(0.999 * HalfWorldMeters), 0.0, DisplayName = "x at -0.999·π·R")]
    public void DeterministicNearBoundaryMetreRoundTripStaysWithinTheCertifiedCeiling(double x, double y)
    {
        double[] point = [x, y];

        Assert.IsTrue(MetreRoundTripsWithinCeiling(point));
    }

    /// <summary>Deterministic points just inside the geographic domain limits round-trip within the certified ceiling.</summary>
    /// <param name="longitude">The longitude under test.</param>
    /// <param name="latitude">The latitude under test.</param>
    [TestMethod]
    [DataRow(0.0, 85.0511287, DisplayName = "Latitude just inside the Mercator limit")]
    [DataRow(0.0, -85.0511287, DisplayName = "Negative latitude just inside the Mercator limit")]
    [DataRow(179.99999999, 0.0, DisplayName = "Longitude just inside the antimeridian")]
    public void DeterministicNearLimitGeographicRoundTripStaysWithinTheCertifiedCeiling(double longitude, double latitude)
    {
        double[] point = [longitude, latitude];

        Assert.IsTrue(GeographicRoundTripsWithinCeiling(point));
    }

    /// <summary>The EPSG:4326 to Web Mercator to EPSG:4326 round trip stays within the certified geographic ceiling across an unseeded sweep.</summary>
    [TestMethod]
    public void Epsg4326RoundTripStaysWithinTheCertifiedCeiling()
    {
        //The composed pair's own directly-swept bound: the extra leg is the bit-exact swap, so
        //the ceiling is the same geographic ceiling, asserted here in EPSG:4326's lat-lon order
        //rather than inherited by argument.
        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-85.051, 85.051], Gen.Double[-179.999, 179.999],
            (latitude, longitude) => new[] { latitude, longitude });

        pointGenerator.Sample(point => Epsg4326RoundTripsWithinCeiling(point), iter: GeographicRoundTripIterationCount);
    }

    /// <summary>The Web Mercator to EPSG:4326 to Web Mercator round trip stays within the certified metre ceiling across an unseeded sweep.</summary>
    [TestMethod]
    public void WebMercatorViaEpsg4326RoundTripStaysWithinTheCertifiedCeiling()
    {
        //The reverse composed pair, swept in metres: Web Mercator through EPSG:4326 and back.
        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-NearBoundaryMeters, NearBoundaryMeters], Gen.Double[-NearBoundaryMeters, NearBoundaryMeters],
            (x, y) => new[] { x, y });

        pointGenerator.Sample(point => MetreViaEpsg4326RoundTripsWithinCeiling(point), iter: MetreRoundTripIterationCount);
    }

    /// <summary>The largest accepted magnitude on either axis, either sign, still round-trips within the certified metre ceiling.</summary>
    /// <param name="axis">The axis under test: 0 for the ordinate, 1 for the abscissa.</param>
    /// <param name="sign">The sign applied to the probed magnitude.</param>
    [TestMethod]
    [DataRow(0, 1.0, DisplayName = "Largest accepted positive ordinate under the image-validity rule")]
    [DataRow(0, -1.0, DisplayName = "Largest accepted negative ordinate under the image-validity rule")]
    [DataRow(1, 1.0, DisplayName = "Largest accepted positive abscissa under the image-validity rule")]
    [DataRow(1, -1.0, DisplayName = "Largest accepted negative abscissa under the image-validity rule")]
    public void SliverAdjacentExtremeRoundTripsWithinTheCertifiedCeiling(int axis, double sign)
    {
        //Self-scoping row: steps down from the square's exact edge until the surface accepts,
        //so it always probes the largest accepted magnitude on this runtime — one ulp inside
        //the refusing sliver, wherever this platform's arithmetic puts that frontier.
        double magnitude = HalfWorldMeters;
        double[] point = new double[2];
        double[] geographic = new double[2];

        for(int step = 0; step < 64; step++)
        {
            point[0] = axis == 1 ? sign * magnitude : 0.0;
            point[1] = axis == 0 ? sign * magnitude : 0.0;

            if(CoordinateReferenceTransform.TryTransform(
                CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, point, geographic, out _))
            {
                break;
            }

            magnitude = Math.BitDecrement(magnitude);
        }

        Assert.IsTrue(MetreRoundTripsWithinCeiling(point));
    }

    /// <summary>The positive antimeridian forwards to Web Mercator exactly at the projection square's edge and refuses on return.</summary>
    [TestMethod]
    public void PositiveAntimeridianForwardsToWebMercatorAndRefusesOnReturn()
    {
        double[] geographic = [180.0, 0.0];
        double[] mercator = new double[2];

        //The abscissa leg is pure IEEE multiply/divide with no libm call, so forward(180°) lands
        //exactly on the projection square's edge at +π·R on any conforming runtime.
        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, geographic, mercator, out CoordinateTransformRefusal forwardRefusal);

        Assert.IsTrue(forwardAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, forwardRefusal);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(HalfWorldMeters), BitConverter.DoubleToInt64Bits(mercator[0]));

        double[] roundTripped = new double[2];

        //Environment-verified fact of this runtime's arithmetic: inverting the exact edge
        //overshoots +180° by a couple of ulps, so the image-validity rule refuses on return
        //rather than emit a coordinate CRS84 would itself reject.
        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, mercator, roundTripped, out CoordinateTransformRefusal inverseRefusal);

        Assert.IsFalse(inverseAccepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, inverseRefusal.Kind);
        Assert.AreEqual(0, inverseRefusal.ElementIndex);
    }

    /// <summary>The negative antimeridian forwards to Web Mercator exactly at the projection square's edge and refuses on return.</summary>
    [TestMethod]
    public void NegativeAntimeridianForwardsToWebMercatorAndRefusesOnReturn()
    {
        double[] geographic = [-180.0, 0.0];
        double[] mercator = new double[2];

        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, geographic, mercator, out CoordinateTransformRefusal forwardRefusal);

        Assert.IsTrue(forwardAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, forwardRefusal);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-HalfWorldMeters), BitConverter.DoubleToInt64Bits(mercator[0]));

        double[] roundTripped = new double[2];

        //Mirror of the positive antimeridian above: the x-side boundary arithmetic is exact and
        //sign-symmetric on this runtime, so both signs overshoot on return alike.
        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, mercator, roundTripped, out CoordinateTransformRefusal inverseRefusal);

        Assert.IsFalse(inverseAccepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, inverseRefusal.Kind);
        Assert.AreEqual(0, inverseRefusal.ElementIndex);
    }

    /// <summary>The positive Mercator latitude limit forwards exactly to the projection square's edge and refuses on return.</summary>
    [TestMethod]
    public void PositiveMercatorLimitForwardsToWebMercatorAndRefusesOnReturn()
    {
        double[] geographic = [0.0, ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees];
        double[] mercator = new double[2];

        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, geographic, mercator, out CoordinateTransformRefusal forwardRefusal);

        Assert.IsTrue(forwardAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, forwardRefusal);

        double[] roundTripped = new double[2];

        //Environment-verified fact of this runtime's arithmetic: forward(+limit) happens to land
        //exactly on the square's edge at +π·R (the y-leg's libm calls hit the boundary exactly at
        //this input), so inverting it overshoots the latitude limit and the return leg refuses.
        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, mercator, roundTripped, out CoordinateTransformRefusal inverseRefusal);

        Assert.IsFalse(inverseAccepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, inverseRefusal.Kind);
        Assert.AreEqual(1, inverseRefusal.ElementIndex);
    }

    /// <summary>The negative Mercator latitude limit forwards a couple of ulps inside the projection square's edge and round-trips successfully on return.</summary>
    [TestMethod]
    public void NegativeMercatorLimitForwardsToWebMercatorAndRoundTripsSuccessfully()
    {
        double[] geographic = [0.0, -ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees];
        double[] mercator = new double[2];

        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, geographic, mercator, out CoordinateTransformRefusal forwardRefusal);

        Assert.IsTrue(forwardAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, forwardRefusal);

        double[] roundTripped = new double[2];

        //NOT a mirror of the positive-limit row above — this is the point of the row.
        //Environment-verified fact of this runtime's arithmetic: forward(-limit) lands a couple
        //of ulps inside the square rather than exactly on its edge (the y-leg's libm calls are
        //not symmetric about zero at this input), so the return leg's image-validity check
        //accepts it and the round trip succeeds rather than refuses.
        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, mercator, roundTripped, out CoordinateTransformRefusal inverseRefusal);

        Assert.IsTrue(inverseAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, inverseRefusal);
        Assert.IsLessThanOrEqualTo(GeographicRoundTripCeilingDegrees, Math.Abs(roundTripped[1] - geographic[1]));
    }

    /// <summary>Returns whether the CRS84 → Web Mercator → CRS84 round trip succeeds with each coordinate inside the geographic ceiling.</summary>
    private static bool GeographicRoundTripsWithinCeiling(double[] point)
    {
        double[] mercator = new double[2];
        double[] roundTripped = new double[2];

        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, point, mercator, out CoordinateTransformRefusal forwardRefusal);
        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, mercator, roundTripped, out CoordinateTransformRefusal inverseRefusal);

        return forwardAccepted
            && inverseAccepted
            && forwardRefusal == CoordinateTransformRefusal.None
            && inverseRefusal == CoordinateTransformRefusal.None
            && Math.Abs(point[0] - roundTripped[0]) <= GeographicRoundTripCeilingDegrees
            && Math.Abs(point[1] - roundTripped[1]) <= GeographicRoundTripCeilingDegrees;
    }

    /// <summary>Returns whether the Web Mercator → CRS84 → Web Mercator round trip succeeds with each coordinate inside the metre ceiling.</summary>
    private static bool MetreRoundTripsWithinCeiling(double[] point)
    {
        double[] geographic = new double[2];
        double[] roundTripped = new double[2];

        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, point, geographic, out CoordinateTransformRefusal inverseRefusal);
        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, geographic, roundTripped, out CoordinateTransformRefusal forwardRefusal);

        return inverseAccepted
            && forwardAccepted
            && inverseRefusal == CoordinateTransformRefusal.None
            && forwardRefusal == CoordinateTransformRefusal.None
            && Math.Abs(point[0] - roundTripped[0]) <= MetreRoundTripCeilingMeters
            && Math.Abs(point[1] - roundTripped[1]) <= MetreRoundTripCeilingMeters;
    }

    /// <summary>Returns whether the EPSG:4326 → Web Mercator → EPSG:4326 round trip succeeds with each coordinate inside the geographic ceiling.</summary>
    private static bool Epsg4326RoundTripsWithinCeiling(double[] point)
    {
        double[] mercator = new double[2];
        double[] roundTripped = new double[2];

        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.WebMercator, point, mercator, out CoordinateTransformRefusal forwardRefusal);
        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Epsg4326, mercator, roundTripped, out CoordinateTransformRefusal inverseRefusal);

        return forwardAccepted
            && inverseAccepted
            && forwardRefusal == CoordinateTransformRefusal.None
            && inverseRefusal == CoordinateTransformRefusal.None
            && Math.Abs(point[0] - roundTripped[0]) <= GeographicRoundTripCeilingDegrees
            && Math.Abs(point[1] - roundTripped[1]) <= GeographicRoundTripCeilingDegrees;
    }

    /// <summary>Returns whether the Web Mercator → EPSG:4326 → Web Mercator round trip succeeds with each coordinate inside the metre ceiling.</summary>
    private static bool MetreViaEpsg4326RoundTripsWithinCeiling(double[] point)
    {
        double[] geographic = new double[2];
        double[] roundTripped = new double[2];

        bool inverseAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Epsg4326, point, geographic, out CoordinateTransformRefusal inverseRefusal);
        bool forwardAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.WebMercator, geographic, roundTripped, out CoordinateTransformRefusal forwardRefusal);

        return inverseAccepted
            && forwardAccepted
            && inverseRefusal == CoordinateTransformRefusal.None
            && forwardRefusal == CoordinateTransformRefusal.None
            && Math.Abs(point[0] - roundTripped[0]) <= MetreRoundTripCeilingMeters
            && Math.Abs(point[1] - roundTripped[1]) <= MetreRoundTripCeilingMeters;
    }
}
