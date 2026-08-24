using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The inverse kernel's own floor: reference points at a nanodegree ceiling
/// (origin, both half-circumference abscissae, both limit ordinates), the
/// libm-free abscissa leg additionally pinned by bit pattern, in-place
/// aliasing, multi-pair independence, and the span-shape caller contract.
/// </summary>
[TestClass]
internal sealed class ScalarWebMercatorToWgs84Tests
{
    /// <summary>
    /// Absolute tolerance, in degrees, for the inverse kernel's certified
    /// reference points. The latitude leg's only error source is a handful
    /// of libm calls (<see cref="Math.Atan"/>, <see cref="Math.Exp"/>) at
    /// these finite magnitudes; the measured worst deviation at these
    /// points is on the order of 1e-14°, so this ceiling carries roughly
    /// five orders of magnitude of margin.
    /// </summary>
    private const double ReferencePointToleranceDegrees = 1e-9;

    /// <summary>
    /// Absolute tolerance, in degrees, used where the identical scalar
    /// formula is evaluated twice from different call sites (in-place vs.
    /// out-of-place, batched vs. single-pair) and must agree. Any genuine
    /// divergence would indicate a call-site-dependent bug rather than
    /// floating-point noise, so this carries the same order-of-magnitude
    /// margin as <see cref="ReferencePointToleranceDegrees"/>.
    /// </summary>
    private const double RepeatedComputationToleranceDegrees = 1e-9;

    /// <summary>The inverse transform kernel under test.</summary>
    private static CoordinateTransformKernel Transform { get; } = ScalarWebMercatorToWgs84.GetTransform();

    /// <summary>The origin maps to the origin.</summary>
    [TestMethod]
    public void OriginMapsToOrigin()
    {
        ReadOnlySpan<double> source = stackalloc double[] { 0.0, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(0.0, destination[0], ReferencePointToleranceDegrees);
        Assert.AreEqual(0.0, destination[1], ReferencePointToleranceDegrees);
    }

    /// <summary>A positive abscissa at half the Earth's circumference maps to positive 180 degrees longitude.</summary>
    [TestMethod]
    public void PositiveAbscissaAtHalfCircumferenceMapsToPositiveOneEightyLongitude()
    {
        double x = Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters;

        ReadOnlySpan<double> source = stackalloc double[] { x, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(180.0, destination[0], ReferencePointToleranceDegrees);
        Assert.AreEqual(0.0, destination[1], ReferencePointToleranceDegrees);
    }

    /// <summary>A negative abscissa at half the Earth's circumference maps to negative 180 degrees longitude.</summary>
    [TestMethod]
    public void NegativeAbscissaAtHalfCircumferenceMapsToNegativeOneEightyLongitude()
    {
        double x = -(Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters);

        ReadOnlySpan<double> source = stackalloc double[] { x, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(-180.0, destination[0], ReferencePointToleranceDegrees);
        Assert.AreEqual(0.0, destination[1], ReferencePointToleranceDegrees);
    }

    /// <summary>A positive ordinate at half the Earth's circumference maps to the Mercator latitude limit.</summary>
    [TestMethod]
    public void PositiveOrdinateAtHalfCircumferenceMapsToMercatorLimitLatitude()
    {
        double y = Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters;

        ReadOnlySpan<double> source = stackalloc double[] { 0.0, y };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(0.0, destination[0], ReferencePointToleranceDegrees);
        Assert.AreEqual(ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees, destination[1], ReferencePointToleranceDegrees);
    }

    /// <summary>A negative ordinate at half the Earth's circumference maps to the negative Mercator latitude limit.</summary>
    [TestMethod]
    public void NegativeOrdinateAtHalfCircumferenceMapsToNegativeMercatorLimitLatitude()
    {
        double y = -(Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters);

        ReadOnlySpan<double> source = stackalloc double[] { 0.0, y };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(0.0, destination[0], ReferencePointToleranceDegrees);
        Assert.AreEqual(-ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees, destination[1], ReferencePointToleranceDegrees);
    }

    /// <summary>The abscissa leg at pi times the Earth radius is exact and libm-free, asserted by bit pattern.</summary>
    [TestMethod]
    public void AbscissaLegAtPiTimesRadiusIsExactAndLibmFree()
    {
        double x = Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters;

        ReadOnlySpan<double> source = stackalloc double[] { x, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        //The abscissa leg is pure IEEE multiply/divide with no libm call, hence
        //bit-reproducible on any conforming runtime; an exact assertion is sound
        //here, unlike the latitude leg, which routes through platform libm and
        //is tolerance-only. Asserted by bit pattern so the row stays an
        //exactness gate even for values ordinary double equality cannot split.
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(180.00000000000003), BitConverter.DoubleToInt64Bits(destination[0]));
    }

    /// <summary>An in-place transform produces the same result as an out-of-place transform.</summary>
    [TestMethod]
    public void InPlaceTransformProducesSameResult()
    {
        Span<double> buffer = stackalloc double[] { 1_000_000.0, 2_000_000.0 };

        //Snapshot the inputs into separate stack scratch before the in-place transform mutates buffer.
        Span<double> copy = stackalloc double[2];
        buffer.CopyTo(copy);

        Span<double> outOfPlace = stackalloc double[2];

        Transform(copy, outOfPlace);
        Transform(buffer, buffer);

        Assert.AreEqual(outOfPlace[0], buffer[0], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(outOfPlace[1], buffer[1], RepeatedComputationToleranceDegrees);
    }

    /// <summary>Multiple coordinate pairs in one call transform independently of each other.</summary>
    [TestMethod]
    public void MultiplePairsTransformIndependently()
    {
        ReadOnlySpan<double> source = stackalloc double[]
        {
            0.0, 0.0,
            1_000_000.0, 2_000_000.0,
            -1_000_000.0, -2_000_000.0,
            3_000_000.0, 0.0
        };
        Span<double> multiDestination = stackalloc double[8];

        Transform(source, multiDestination);

        ReadOnlySpan<double> firstSource = stackalloc double[] { 0.0, 0.0 };
        Span<double> firstDestination = stackalloc double[2];
        Transform(firstSource, firstDestination);

        ReadOnlySpan<double> secondSource = stackalloc double[] { 1_000_000.0, 2_000_000.0 };
        Span<double> secondDestination = stackalloc double[2];
        Transform(secondSource, secondDestination);

        ReadOnlySpan<double> thirdSource = stackalloc double[] { -1_000_000.0, -2_000_000.0 };
        Span<double> thirdDestination = stackalloc double[2];
        Transform(thirdSource, thirdDestination);

        ReadOnlySpan<double> fourthSource = stackalloc double[] { 3_000_000.0, 0.0 };
        Span<double> fourthDestination = stackalloc double[2];
        Transform(fourthSource, fourthDestination);

        Assert.AreEqual(firstDestination[0], multiDestination[0], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(firstDestination[1], multiDestination[1], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(secondDestination[0], multiDestination[2], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(secondDestination[1], multiDestination[3], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(thirdDestination[0], multiDestination[4], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(thirdDestination[1], multiDestination[5], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(fourthDestination[0], multiDestination[6], RepeatedComputationToleranceDegrees);
        Assert.AreEqual(fourthDestination[1], multiDestination[7], RepeatedComputationToleranceDegrees);
    }

    /// <summary>An odd-length source throws ArgumentException.</summary>
    [TestMethod]
    public void OddLengthSourceThrows()
    {
        double[] destination = new double[4];

        Assert.Throws<ArgumentException>(() => Transform(new double[] { 0.0, 0.0, 1.0 }, destination));
    }

    /// <summary>A destination shorter than the source throws ArgumentException.</summary>
    [TestMethod]
    public void DestinationShorterThanSourceThrows()
    {
        double[] destination = new double[2];

        Assert.Throws<ArgumentException>(() => Transform(new double[] { 0.0, 0.0, 1_000_000.0, 2_000_000.0 }, destination));
    }
}
