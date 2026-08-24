using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The forward kernel's reference-point floor: the origin, both antimeridian
/// longitudes, and the Mercator latitude limit, plus polar clamping,
/// negation symmetry, in-place aliasing, multi-pair independence, and the
/// span-shape caller contract.
/// </summary>
[TestClass]
internal sealed class ScalarWgs84ToWebMercatorTests
{
    /// <summary>The default forward transform kernel under test.</summary>
    private static CoordinateTransformKernel Transform { get; } = CoordinateTransformKernelSelection.Default;

    /// <summary>The origin maps to the origin.</summary>
    [TestMethod]
    public void OriginMapsToOrigin()
    {
        ReadOnlySpan<double> source = stackalloc double[] { 0.0, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(0.0, destination[0], 1e-6);
        Assert.AreEqual(0.0, destination[1], 1e-6);
    }

    /// <summary>180 degrees longitude maps to positive half the Earth's circumference.</summary>
    [TestMethod]
    public void OneEightyLongitudeEqualsHalfTheEarthCircumference()
    {
        const double Expected = 20_037_508.342789244;

        ReadOnlySpan<double> source = stackalloc double[] { 180.0, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(Expected, destination[0], 1e-3);
        Assert.AreEqual(0.0, destination[1], 1e-6);
    }

    /// <summary>Negative 180 degrees longitude maps to negative half the Earth's circumference.</summary>
    [TestMethod]
    public void NegativeOneEightyLongitudeEqualsNegativeHalfCircumference()
    {
        const double Expected = -20_037_508.342789244;

        ReadOnlySpan<double> source = stackalloc double[] { -180.0, 0.0 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(Expected, destination[0], 1e-3);
        Assert.AreEqual(0.0, destination[1], 1e-6);
    }

    /// <summary>The latitude at the Mercator limit maps to positive half the Earth's circumference in Y.</summary>
    [TestMethod]
    public void LatitudeAtMercatorLimitMapsToPositiveHalfCircumferenceY()
    {
        const double Expected = 20_037_508.342789244;

        ReadOnlySpan<double> source = stackalloc double[] { 0.0, 85.05112877980659 };
        Span<double> destination = stackalloc double[2];

        Transform(source, destination);

        Assert.AreEqual(0.0, destination[0], 1e-6);
        Assert.AreEqual(Expected, destination[1], 1e-2);
    }

    /// <summary>A polar latitude beyond the Mercator limit is clamped to the limit, for both hemispheres.</summary>
    [TestMethod]
    public void PolarLatitudeIsClampedToMercatorLimit()
    {
        const double ExpectedY = 20_037_508.342789244;

        Span<double> source = stackalloc double[] { 0.0, 89.0 };
        Span<double> destinationAt89 = stackalloc double[2];
        Transform(source, destinationAt89);

        //After clamp the result should equal the limit's result, not blow up.
        Assert.AreEqual(0.0, destinationAt89[0], 1e-6);
        Assert.AreEqual(ExpectedY, destinationAt89[1], 1e-2);

        //Same for the south pole.
        source[1] = -89.0;
        Span<double> destinationAtMinus89 = stackalloc double[2];
        Transform(source, destinationAtMinus89);

        Assert.AreEqual(-ExpectedY, destinationAtMinus89[1], 1e-2);
    }

    /// <summary>A negated input produces a negated output.</summary>
    [TestMethod]
    public void SymmetryNegativeInputProducesNegativeOutput()
    {
        ReadOnlySpan<double> positive = stackalloc double[] { 45.0, 30.0 };
        Span<double> positiveResult = stackalloc double[2];
        Transform(positive, positiveResult);

        ReadOnlySpan<double> negative = stackalloc double[] { -45.0, -30.0 };
        Span<double> negativeResult = stackalloc double[2];
        Transform(negative, negativeResult);

        Assert.AreEqual(positiveResult[0], -negativeResult[0], 1e-6);
        Assert.AreEqual(positiveResult[1], -negativeResult[1], 1e-6);
    }

    /// <summary>An in-place transform produces the same result as an out-of-place transform.</summary>
    [TestMethod]
    public void InPlaceTransformProducesSameResult()
    {
        Span<double> buffer = stackalloc double[] { 6.1296, 49.8153 };

        //Snapshot the inputs into separate stack scratch before the in-place transform mutates buffer.
        Span<double> copy = stackalloc double[2];
        buffer.CopyTo(copy);

        Span<double> outOfPlace = stackalloc double[2];

        Transform(copy, outOfPlace);
        Transform(buffer, buffer);

        Assert.AreEqual(outOfPlace[0], buffer[0], 1e-9);
        Assert.AreEqual(outOfPlace[1], buffer[1], 1e-9);
    }

    /// <summary>Multiple coordinate pairs in one call transform independently of each other.</summary>
    [TestMethod]
    public void MultiplePairsTransformIndependently()
    {
        ReadOnlySpan<double> source = stackalloc double[]
        {
            0.0, 0.0,
            180.0, 0.0,
            -180.0, 0.0,
            0.0, 85.05112877980659
        };
        Span<double> destination = stackalloc double[8];

        Transform(source, destination);

        Assert.AreEqual(0.0, destination[0], 1e-6);
        Assert.AreEqual(0.0, destination[1], 1e-6);
        Assert.AreEqual(20_037_508.342789244, destination[2], 1e-3);
        Assert.AreEqual(-20_037_508.342789244, destination[4], 1e-3);
        Assert.AreEqual(20_037_508.342789244, destination[7], 1e-2);
    }

    /// <summary>An odd-length source throws ArgumentException.</summary>
    [TestMethod]
    public void OddLengthSourceThrows()
    {
        double[] destination = new double[4];

        Assert.Throws<ArgumentException>(() => Transform(new double[] { 0.0, 0.0, 1.0 }, destination));
    }
}
