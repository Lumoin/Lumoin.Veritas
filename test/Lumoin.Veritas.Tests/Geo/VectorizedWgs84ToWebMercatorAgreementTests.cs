using CsCheck;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Agreement sweep for the vectorized WGS 84 → Web Mercator kernel against
/// the exact scalar reference. The scalar kernel is the oracle; the
/// vectorized kernel must reproduce it within the documented accuracy
/// envelope across random batches, every tail size, and the canonical
/// reference points — including the latitude clamp, where Mercator's
/// vertical stretch amplifies the residual transcendental error.
/// </summary>
[TestClass]
internal sealed class VectorizedWgs84ToWebMercatorAgreementTests
{
    /// <summary>Worst-case agreement is sub-millimetre at the ±85.05° clamp and ~micrometre elsewhere; half a millimetre is a safe ceiling that still pins the envelope.</summary>
    private const double ToleranceMeters = 5e-4;

    /// <summary>A larger absolute world coordinate divides out below; this floor keeps the relative check meaningful for coordinates near the projection origin.</summary>
    private const double RelativeTolerance = 1e-9;

    /// <summary>Sample count for the random-batch agreement sweep.</summary>
    private const long IterationCount = 200;

    /// <summary>The exact scalar kernel, the oracle every vectorized result is checked against.</summary>
    private static CoordinateTransformKernel Scalar { get; } = CoordinateTransformKernelSelection.Scalar;

    /// <summary>The vectorized kernel under test.</summary>
    private static CoordinateTransformKernel Vectorized { get; } = CoordinateTransformKernelSelection.Vectorized;

    /// <summary>The ambient test execution context supplied by the test host.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The origin agrees with the scalar kernel.</summary>
    [TestMethod]
    public void OriginAgreesWithScalar()
    {
        AssertAgrees([0.0, 0.0]);
    }

    /// <summary>The antimeridian, the Mercator latitude limit, and a populated mid-latitude point all agree with the scalar kernel.</summary>
    [TestMethod]
    public void CanonicalReferencePointsAgreeWithScalar()
    {
        //Antimeridian, the Mercator latitude limit, and a populated mid-latitude.
        AssertAgrees(
        [
            180.0, 0.0,
            -180.0, 0.0,
            0.0, 85.05112877980659,
            0.0, -85.05112877980659,
            24.9384, 60.1699,
            9.5215, 47.1410
        ]);
    }

    /// <summary>Latitudes beyond the Mercator limit clamp identically on both kernels, so they agree exactly.</summary>
    [TestMethod]
    public void PolarLatitudesBeyondTheLimitClampIdenticallyToScalar()
    {
        //Both kernels clamp at exactly the same limit, so beyond it they must agree exactly.
        AssertAgrees(
        [
            10.0, 89.9,
            -10.0, -89.9,
            120.0, 90.0,
            -120.0, -90.0
        ]);
    }

    /// <summary>Every tail size from one pair through past two vector widths agrees with the scalar kernel.</summary>
    [TestMethod]
    public void EveryTailSizeAgreesWithScalar()
    {
        //Lengths from one pair up past two vector widths exercise the full-vector body,
        //the sub-width scalar fallback, and every remainder size of the tail.
        int width = Vector<double>.Count;
        int maxPairs = width + 4;

        for(int pairCount = 1; pairCount <= maxPairs; pairCount++)
        {
            double[] interleaved = new double[pairCount * 2];

            for(int pair = 0; pair < pairCount; pair++)
            {
                //Spread longitudes and latitudes deterministically across the valid ranges.
                interleaved[pair * 2] = -170.0 + (pair * 340.0 / Math.Max(1, pairCount));
                interleaved[(pair * 2) + 1] = -80.0 + (pair * 160.0 / Math.Max(1, pairCount));
            }

            AssertAgrees(interleaved);
        }
    }

    /// <summary>Random batches of random length agree with the scalar kernel within tolerance across an unseeded sweep.</summary>
    [TestMethod]
    public void RandomBatchesAgreeWithScalar()
    {
        Gen<double[]> batchGenerator =
            from pairCount in Gen.Int[1, 64]
            from longitudes in Gen.Double[-180.0, 180.0].Array[pairCount]
            from latitudes in Gen.Double[-90.0, 90.0].Array[pairCount]
            select Interleave(longitudes, latitudes);

        batchGenerator.Sample(interleaved =>
        {
            double[] scalarResult = new double[interleaved.Length];
            double[] vectorResult = new double[interleaved.Length];

            Scalar(interleaved, scalarResult);
            Vectorized(interleaved, vectorResult);

            return Agrees(scalarResult, vectorResult);
        }, iter: IterationCount);
    }

    /// <summary>An in-place vectorized transform matches the out-of-place result.</summary>
    [TestMethod]
    public void InPlaceTransformMatchesOutOfPlace()
    {
        double[] source =
        [
            12.34, 56.78,
            -98.76, -43.21,
            0.0, 0.0,
            179.999, 84.0,
            -179.999, -84.0
        ];

        double[] outOfPlace = new double[source.Length];
        Vectorized(source, outOfPlace);

        double[] inPlace = (double[])source.Clone();
        Vectorized(inPlace, inPlace);

        Assert.AreSequenceEqual(outOfPlace, inPlace);
    }

    /// <summary>An odd length source throws.</summary>
    [TestMethod]
    public void OddLengthSourceThrows()
    {
        double[] destination = new double[4];

        Assert.Throws<ArgumentException>(() => Vectorized(new double[] { 0.0, 0.0, 1.0 }, destination));
    }

    /// <summary>A destination shorter than the source throws.</summary>
    [TestMethod]
    public void ShortDestinationThrows()
    {
        Assert.Throws<ArgumentException>(() => Vectorized(new double[] { 0.0, 0.0 }, new double[1]));
    }

    /// <summary>The reported hardware-acceleration flag matches the runtime's actual vector capability.</summary>
    [TestMethod]
    public void HardwareAccelerationFlagMatchesVectorCapability()
    {
        Assert.AreEqual(Vector.IsHardwareAccelerated, CoordinateTransformKernelSelection.IsVectorizationHardwareAccelerated);
    }

    /// <summary>Interleaves parallel longitude and latitude arrays into a single lon-lat ordinate pair sequence.</summary>
    private static double[] Interleave(double[] longitudes, double[] latitudes)
    {
        double[] interleaved = new double[longitudes.Length * 2];

        for(int index = 0; index < longitudes.Length; index++)
        {
            interleaved[index * 2] = longitudes[index];
            interleaved[(index * 2) + 1] = latitudes[index];
        }

        return interleaved;
    }

    /// <summary>Asserts the vectorized kernel's result agrees with the scalar kernel's result within the tolerance envelope on every lane.</summary>
    private static void AssertAgrees(double[] interleaved)
    {
        double[] scalarResult = new double[interleaved.Length];
        double[] vectorResult = new double[interleaved.Length];

        Scalar(interleaved, scalarResult);
        Vectorized(interleaved, vectorResult);

        for(int index = 0; index < interleaved.Length; index++)
        {
            double allowed = ToleranceMeters + (RelativeTolerance * Math.Abs(scalarResult[index]));
            double difference = Math.Abs(scalarResult[index] - vectorResult[index]);

            Assert.IsLessThanOrEqualTo(allowed, difference,
                $"Lane {index}: scalar {scalarResult[index]} vs vector {vectorResult[index]} differ by {difference} (allowed {allowed}).");
        }
    }

    /// <summary>Returns whether every lane of the scalar and vector results agrees within the tolerance envelope.</summary>
    private static bool Agrees(double[] scalarResult, double[] vectorResult)
    {
        for(int index = 0; index < scalarResult.Length; index++)
        {
            double allowed = ToleranceMeters + (RelativeTolerance * Math.Abs(scalarResult[index]));

            if(Math.Abs(scalarResult[index] - vectorResult[index]) > allowed)
            {
                return false;
            }
        }

        return true;
    }
}
