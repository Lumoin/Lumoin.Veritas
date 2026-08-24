using CsCheck;
using Lumoin.Veritas.Geo.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The pair-closed roster surface, row by row: identity and the CRS84 ↔
/// EPSG:4326 swap are exact and asserted bitwise (signed zeros included, and
/// an in-place swap over a multi-point span, which a naive sequential swap
/// would corrupt); the projection legs delegate to the shipped kernels by
/// construction, pinned by a sweep and a fixed-batch agreement row; the
/// axis-order duality is discriminated by a shared literal pair that CRS84
/// accepts and EPSG:4326 refuses; every refusal kind is live-fired with its
/// element index; the validation total order, empty-span semantics, and the
/// refusal-leaves-the-destination-untouched contract are pinned; span-shape
/// violations still throw; and every projection pair round-trips its
/// in-place result bitwise against the out-of-place one.
/// </summary>
[TestClass]
internal sealed class CoordinateReferenceTransformTests
{
    /// <summary>Half the Web Mercator world extent in metres, π · R — the projection square's edge on both axes.</summary>
    private const double HalfWorldMeters = Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters;

    /// <summary>Sample count for the delegation agreement sweep — the house scale for kernel-agreement property tests.</summary>
    private const long DelegationSweepIterationCount = 200;

    /// <summary>Every roster identity transform reproduces the source bitwise, signed zero included.</summary>
    /// <param name="kind">The roster member under test.</param>
    [TestMethod]
    [DataRow(CoordinateReferenceSystemKind.Crs84, DisplayName = "CRS84 identity is bitwise identical, signed zero included")]
    [DataRow(CoordinateReferenceSystemKind.Epsg4326, DisplayName = "EPSG:4326 identity is bitwise identical, signed zero included")]
    [DataRow(CoordinateReferenceSystemKind.WebMercator, DisplayName = "Web Mercator identity is bitwise identical, signed zero included")]
    public void IdentityTransformIsBitwiseIdenticalIncludingNegativeZero(CoordinateReferenceSystemKind kind)
    {
        CoordinateReferenceSystem system = ToRosterMember(kind);
        double[] source = [-0.0, 0.0, 24.9384, 60.1699];
        double[] destination = new double[4];

        bool accepted = CoordinateReferenceTransform.TryTransform(system, system, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, refusal);

        for(int index = 0; index < source.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(source[index]), BitConverter.DoubleToInt64Bits(destination[index]));
        }
    }

    /// <summary>CRS84 to EPSG:4326 swaps each pair's ordinates bitwise, signed zero included.</summary>
    [TestMethod]
    public void Crs84ToEpsg4326SwapsOrdinatesBitwiseWithSignedZero()
    {
        double[] source = [-0.0, 60.1699, 24.9384, 0.0];
        double[] destination = new double[4];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.Epsg4326, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, refusal);
        AssertSwappedBitwise(source, destination);
    }

    /// <summary>EPSG:4326 to CRS84 swaps each pair's ordinates bitwise, signed zero included.</summary>
    [TestMethod]
    public void Epsg4326ToCrs84SwapsOrdinatesBitwiseWithSignedZero()
    {
        double[] source = [-0.0, 60.1699, 24.9384, 0.0];
        double[] destination = new double[4];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.Crs84, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, refusal);
        AssertSwappedBitwise(source, destination);
    }

    /// <summary>CRS84 to EPSG:4326 and back to CRS84 is a bitwise identity round trip.</summary>
    [TestMethod]
    public void Crs84ToEpsg4326ToCrs84RoundTripIsBitwiseIdentity()
    {
        double[] original = [-0.0, 60.1699, 24.9384, 0.0];
        double[] intermediate = new double[4];
        double[] roundTripped = new double[4];

        bool firstAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.Epsg4326, original, intermediate, out CoordinateTransformRefusal firstRefusal);
        bool secondAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.Crs84, intermediate, roundTripped, out CoordinateTransformRefusal secondRefusal);

        Assert.IsTrue(firstAccepted);
        Assert.IsTrue(secondAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, firstRefusal);
        Assert.AreEqual(CoordinateTransformRefusal.None, secondRefusal);

        for(int index = 0; index < original.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(original[index]), BitConverter.DoubleToInt64Bits(roundTripped[index]));
        }
    }

    /// <summary>An in-place swap over a multi-point span matches the out-of-place swap bitwise.</summary>
    [TestMethod]
    public void InPlaceSwapOverMultiPointSpanMatchesOutOfPlaceSwap()
    {
        double[] source = [-0.0, 10.0, 20.0, 30.0, 40.0, 50.0];
        double[] outOfPlace = new double[source.Length];

        bool outOfPlaceAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.Epsg4326, source, outOfPlace, out CoordinateTransformRefusal outOfPlaceRefusal);

        double[] inPlace = (double[])source.Clone();

        //A naive sequential per-point swap that writes the first ordinate before reading the
        //second would corrupt this multi-point in-place call — the destination and source are
        //the identical span, so both ordinates of every point must be read into locals first.
        bool inPlaceAccepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.Epsg4326, inPlace, inPlace, out CoordinateTransformRefusal inPlaceRefusal);

        Assert.IsTrue(outOfPlaceAccepted);
        Assert.IsTrue(inPlaceAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, outOfPlaceRefusal);
        Assert.AreEqual(CoordinateTransformRefusal.None, inPlaceRefusal);

        for(int index = 0; index < source.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(outOfPlace[index]), BitConverter.DoubleToInt64Bits(inPlace[index]));
        }
    }

    /// <summary>CRS84 to Web Mercator through the surface agrees bitwise with the scalar kernel across a random sweep.</summary>
    [TestMethod]
    public void Crs84ToWebMercatorDelegatesToScalarKernelBitwiseAcrossSweep()
    {
        Gen<double[]> pointGenerator = Gen.Select(
            Gen.Double[-180.0, 180.0], Gen.Double[-85.05, 85.05],
            (longitude, latitude) => new[] { longitude, latitude });

        pointGenerator.Sample(point =>
        {
            double[] viaSurface = new double[2];
            double[] viaScalarKernel = new double[2];

            bool accepted = CoordinateReferenceTransform.TryTransform(
                CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, point, viaSurface, out CoordinateTransformRefusal refusal);

            CoordinateTransformKernelSelection.Scalar(point, viaScalarKernel);

            return accepted
                && refusal == CoordinateTransformRefusal.None
                && BitConverter.DoubleToInt64Bits(viaSurface[0]) == BitConverter.DoubleToInt64Bits(viaScalarKernel[0])
                && BitConverter.DoubleToInt64Bits(viaSurface[1]) == BitConverter.DoubleToInt64Bits(viaScalarKernel[1]);
        }, iter: DelegationSweepIterationCount);
    }

    /// <summary>The default and scalar kernels agree bitwise on a fixed batch of canonical points.</summary>
    [TestMethod]
    public void DefaultAndScalarKernelsAgreeBitwiseOnAFixedBatch()
    {
        double[] source =
        [
            0.0, 0.0,
            180.0, 0.0,
            -180.0, 0.0,
            24.9384, 60.1699,
            -122.4194, 37.7749,
            0.0, 85.05112877980659
        ];

        double[] viaDefault = new double[source.Length];
        double[] viaScalar = new double[source.Length];

        CoordinateTransformKernelSelection.Default(source, viaDefault);
        CoordinateTransformKernelSelection.Scalar(source, viaScalar);

        for(int index = 0; index < source.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(viaScalar[index]), BitConverter.DoubleToInt64Bits(viaDefault[index]));
        }
    }

    /// <summary>EPSG:4326 to Web Mercator equals a bit-exact swap into lon-lat order followed by the scalar kernel.</summary>
    [TestMethod]
    public void Epsg4326ToWebMercatorEqualsSwapThenScalarKernelBitwise()
    {
        //EPSG:4326 axis order is latitude-first; the reference composition swaps into a scratch
        //buffer to reach lon-lat order, then runs the shipped scalar kernel on the scratch.
        double[] source = [60.1699, 24.9384, -33.8688, 151.2093, 0.0, 0.0];
        double[] scratch = new double[source.Length];
        SwapPairs(source, scratch);

        double[] reference = new double[source.Length];
        CoordinateTransformKernelSelection.Scalar(scratch, reference);

        double[] actual = new double[source.Length];
        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.WebMercator, source, actual, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, refusal);

        for(int index = 0; index < source.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(reference[index]), BitConverter.DoubleToInt64Bits(actual[index]));
        }
    }

    /// <summary>Web Mercator to EPSG:4326 equals the inverse kernel followed by a bit-exact swap into lat-lon order.</summary>
    [TestMethod]
    public void WebMercatorToEpsg4326EqualsInverseKernelThenSwapBitwise()
    {
        //The reference composition runs the inverse kernel first (Web Mercator to lon-lat), then
        //swaps the result into lat-lon order for EPSG:4326.
        double[] source = [2_776_130.0, 8_437_662.0, -13_627_665.0, 4_547_675.0];
        double[] scratch = new double[source.Length];
        ScalarWebMercatorToWgs84.GetTransform()(source, scratch);

        double[] reference = new double[source.Length];
        SwapPairs(scratch, reference);

        double[] actual = new double[source.Length];
        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Epsg4326, source, actual, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, refusal);

        for(int index = 0; index < source.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(reference[index]), BitConverter.DoubleToInt64Bits(actual[index]));
        }
    }

    /// <summary>An EPSG:4326 source with an invalid latitude at position zero refuses at element index zero.</summary>
    [TestMethod]
    public void Epsg4326SourceWithInvalidLatitudeAtPositionZeroRefusesAtIndexZero()
    {
        double[] source = [95.0, 0.0];
        double[] destination = new double[2];

        //EPSG:4326 is latitude-first, so 95 lands in the latitude position and exceeds ±90.
        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, refusal.Kind);
        Assert.AreEqual(0, refusal.ElementIndex);
    }

    /// <summary>An EPSG:4326 source with an invalid longitude at position one refuses at element index one.</summary>
    [TestMethod]
    public void Epsg4326SourceWithInvalidLongitudeAtPositionOneRefusesAtIndexOne()
    {
        double[] source = [0.0, -181.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, refusal.Kind);
        Assert.AreEqual(1, refusal.ElementIndex);
    }

    /// <summary>The same literal doubles that EPSG:4326 refuses are accepted under CRS84's lon-first axis order.</summary>
    [TestMethod]
    public void Crs84SourceWithTheSameDoublesIsAcceptedByAxisOrder()
    {
        double[] source = [95.0, 0.0];
        double[] destination = new double[2];

        //The identical literal doubles that EPSG:4326 refuses above are valid CRS84 input — 95 is
        //a valid longitude in the lon-first order, which is exactly the axis-order duality this
        //surface exists to carry rather than leave to convention.
        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, refusal);
    }

    /// <summary>An unrecognized source CRS refuses with element index minus one.</summary>
    [TestMethod]
    public void SourceCrsUnrecognizedRefusesWithElementIndexMinusOne()
    {
        var source = default(CoordinateReferenceSystem);
        double[] coordinates = [0.0, 0.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            source, CoordinateReferenceSystem.Crs84, coordinates, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.SourceCrsUnrecognized, refusal.Kind);
        Assert.AreEqual(-1, refusal.ElementIndex);
    }

    /// <summary>An unrecognized target CRS refuses with element index minus one.</summary>
    [TestMethod]
    public void TargetCrsUnrecognizedRefusesWithElementIndexMinusOne()
    {
        var target = default(CoordinateReferenceSystem);
        double[] coordinates = [0.0, 0.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, target, coordinates, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.TargetCrsUnrecognized, refusal.Kind);
        Assert.AreEqual(-1, refusal.ElementIndex);
    }

    /// <summary>A non-finite coordinate refuses at its own element index, regardless of which non-finite value it is.</summary>
    /// <param name="nonFiniteValue">The non-finite value placed at index one of the source.</param>
    [TestMethod]
    [DataRow(double.NaN, DisplayName = "NaN refuses as NonFiniteCoordinate")]
    [DataRow(double.PositiveInfinity, DisplayName = "Positive infinity refuses as NonFiniteCoordinate")]
    public void NonFiniteCoordinateRefusesAtTheOffendingIndex(double nonFiniteValue)
    {
        double[] source = [10.0, nonFiniteValue, 20.0, 30.0];
        double[] destination = new double[4];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.NonFiniteCoordinate, refusal.Kind);
        Assert.AreEqual(1, refusal.ElementIndex);
    }

    /// <summary>A CRS84 longitude outside the domain refuses at element index zero.</summary>
    [TestMethod]
    public void CoordinateOutsideSourceDomainCrs84LongitudeRefusesAtIndexZero()
    {
        double[] source = [181.0, 0.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, refusal.Kind);
        Assert.AreEqual(0, refusal.ElementIndex);
    }

    /// <summary>A Web Mercator abscissa beyond π·R refuses at element index zero.</summary>
    [TestMethod]
    public void CoordinateOutsideSourceDomainWebMercatorAbscissaBeyondPiRRefusesAtIndexZero()
    {
        double[] source = [1.01 * HalfWorldMeters, 0.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, refusal.Kind);
        Assert.AreEqual(0, refusal.ElementIndex);
    }

    /// <summary>A CRS84 latitude beyond the Mercator limit refuses at element index one on the target domain check.</summary>
    [TestMethod]
    public void CoordinateOutsideTargetDomainCrs84LatitudeBeyondMercatorLimitRefusesAtIndexOne()
    {
        double[] source = [0.0, 86.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, refusal.Kind);
        Assert.AreEqual(1, refusal.ElementIndex);
    }

    /// <summary>Each Web Mercator projection-square edge, on both axes and both signs, refuses as outside the target domain on return.</summary>
    /// <param name="x">The abscissa under test.</param>
    /// <param name="y">The ordinate under test.</param>
    /// <param name="expectedIndex">The element index the refusal is expected to carry.</param>
    [TestMethod]
    [DataRow(HalfWorldMeters, 0.0, 0, DisplayName = "The positive abscissa's image overshoots +180° on this runtime")]
    [DataRow(-HalfWorldMeters, 0.0, 0, DisplayName = "The negative abscissa's image overshoots -180° on this runtime")]
    [DataRow(0.0, HalfWorldMeters, 1, DisplayName = "The positive ordinate's image overshoots the Mercator latitude limit on this runtime")]
    [DataRow(0.0, -HalfWorldMeters, 1, DisplayName = "The negative ordinate's image overshoots the negative Mercator latitude limit on this runtime")]
    public void WebMercatorSquareBoundaryRefusesCoordinateOutsideTargetDomain(double x, double y, int expectedIndex)
    {
        //Environment-verified fact of this runtime's arithmetic (not a design choice): inverting
        //the exact projection-square edge lands a couple of ulps beyond the geographic domain the
        //reverse operation would itself accept, on both axes and both signs, so the image-validity
        //rule refuses rather than emit a coordinate CRS84 would reject right back.
        double[] source = [x, y];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, refusal.Kind);
        Assert.AreEqual(expectedIndex, refusal.ElementIndex);
    }

    /// <summary>An out-of-domain defect before a non-finite one reports the earlier element index.</summary>
    [TestMethod]
    public void MixedDefectOutOfDomainBeforeNonFiniteReportsTheEarlierIndex()
    {
        double[] source = [200.0, 30.0, double.NaN, 0.0];
        double[] destination = new double[4];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, refusal.Kind);
        Assert.AreEqual(0, refusal.ElementIndex);
    }

    /// <summary>A non-finite defect before an out-of-domain one reports the earlier element index.</summary>
    [TestMethod]
    public void MixedDefectNonFiniteBeforeOutOfDomainReportsTheEarlierIndex()
    {
        double[] source = [0.0, 30.0, double.NaN, 0.0, 200.0, 0.0];
        double[] destination = new double[6];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.NonFiniteCoordinate, refusal.Kind);
        Assert.AreEqual(2, refusal.ElementIndex);
    }

    /// <summary>When both identifiers are unrecognized, the refusal reports the source CRS first.</summary>
    [TestMethod]
    public void BothIdentifiersUnrecognizedRefusesSourceCrsUnrecognizedFirst()
    {
        var source = default(CoordinateReferenceSystem);
        var target = default(CoordinateReferenceSystem);
        double[] coordinates = [0.0, 0.0];
        double[] destination = new double[2];

        bool accepted = CoordinateReferenceTransform.TryTransform(source, target, coordinates, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.SourceCrsUnrecognized, refusal.Kind);
        Assert.AreEqual(-1, refusal.ElementIndex);
    }

    /// <summary>The zero-initialized default refusal value is never mistaken for the success value.</summary>
    [TestMethod]
    public void DefaultRefusalValueIsNotTheSuccessValue()
    {
        //Zero-initialization yields an element index of zero — a real index — so the default
        //value must never be mistaken for the success value; success is the boolean return or
        //equality with CoordinateTransformRefusal.None, and the type's doc says so.
        Assert.AreNotEqual(CoordinateTransformRefusal.None, default(CoordinateTransformRefusal));
        Assert.AreEqual(-1, CoordinateTransformRefusal.None.ElementIndex);
        Assert.AreEqual(CoordinateTransformRefusalKind.None, CoordinateTransformRefusal.None.Kind);
    }

    /// <summary>Empty spans with a recognized CRS pair succeed with the none refusal.</summary>
    [TestMethod]
    public void EmptySpansWithRecognizedPairSucceedWithNoneRefusal()
    {
        double[] source = [];
        double[] destination = [];

        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsTrue(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.None, refusal.Kind);
        Assert.AreEqual(-1, refusal.ElementIndex);
    }

    /// <summary>Empty spans with an unrecognized target CRS still refuse target CRS unrecognized.</summary>
    [TestMethod]
    public void EmptySpansWithUnrecognizedTargetRefuseTargetCrsUnrecognized()
    {
        var target = default(CoordinateReferenceSystem);
        double[] source = [];
        double[] destination = [];

        //Identifier checks fire before any coordinate is inspected, so they fire on empty spans too.
        bool accepted = CoordinateReferenceTransform.TryTransform(CoordinateReferenceSystem.Crs84, target, source, destination, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.TargetCrsUnrecognized, refusal.Kind);
        Assert.AreEqual(-1, refusal.ElementIndex);
    }

    /// <summary>A refusal on an in-place call leaves the buffer bitwise unchanged, even after the whole span has been walked.</summary>
    [TestMethod]
    public void RefusalOnAnInPlaceCallLeavesTheBufferBitwiseUnchanged()
    {
        double[] buffer = [10.0, 20.0, 30.0, 86.0];
        double[] original = (double[])buffer.Clone();

        //The second point's latitude, 86, is beyond the Mercator limit — the refusal fires at the
        //very last element, after the whole span has already been walked for validation.
        bool accepted = CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, buffer, buffer, out CoordinateTransformRefusal refusal);

        Assert.IsFalse(accepted);
        Assert.AreEqual(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, refusal.Kind);
        Assert.AreEqual(3, refusal.ElementIndex);

        for(int index = 0; index < buffer.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(original[index]), BitConverter.DoubleToInt64Bits(buffer[index]));
        }
    }

    /// <summary>An odd source length throws.</summary>
    [TestMethod]
    public void OddSourceLengthThrows()
    {
        double[] destination = new double[4];

        Assert.Throws<ArgumentException>(() => CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, new double[] { 0.0, 0.0, 1.0 }, destination, out _));
    }

    /// <summary>A destination shorter than the source throws.</summary>
    [TestMethod]
    public void DestinationShorterThanSourceThrows()
    {
        double[] destination = new double[2];

        Assert.Throws<ArgumentException>(() => CoordinateReferenceTransform.TryTransform(
            CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, new double[] { 0.0, 0.0, 24.9384, 60.1699 }, destination, out _));
    }

    /// <summary>CRS84 to Web Mercator in place matches the out-of-place result bitwise.</summary>
    [TestMethod]
    public void Crs84ToWebMercatorInPlaceMatchesOutOfPlaceBitwise()
    {
        double[] source = [24.9384, 60.1699, -122.4194, 37.7749, 0.0, 0.0];

        AssertInPlaceMatchesOutOfPlaceBitwise(CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.WebMercator, source);
    }

    /// <summary>Web Mercator to CRS84 in place matches the out-of-place result bitwise.</summary>
    [TestMethod]
    public void WebMercatorToCrs84InPlaceMatchesOutOfPlaceBitwise()
    {
        double[] source = [2_776_130.0, 8_437_662.0, -13_627_665.0, 4_547_675.0, 0.0, 0.0];

        AssertInPlaceMatchesOutOfPlaceBitwise(CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Crs84, source);
    }

    /// <summary>EPSG:4326 to Web Mercator in place matches the out-of-place result bitwise.</summary>
    [TestMethod]
    public void Epsg4326ToWebMercatorInPlaceMatchesOutOfPlaceBitwise()
    {
        double[] source = [60.1699, 24.9384, 37.7749, -122.4194, 0.0, 0.0];

        AssertInPlaceMatchesOutOfPlaceBitwise(CoordinateReferenceSystem.Epsg4326, CoordinateReferenceSystem.WebMercator, source);
    }

    /// <summary>Web Mercator to EPSG:4326 in place matches the out-of-place result bitwise.</summary>
    [TestMethod]
    public void WebMercatorToEpsg4326InPlaceMatchesOutOfPlaceBitwise()
    {
        double[] source = [2_776_130.0, 8_437_662.0, -13_627_665.0, 4_547_675.0, 0.0, 0.0];

        AssertInPlaceMatchesOutOfPlaceBitwise(CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.Epsg4326, source);
    }

    /// <summary>Maps a roster kind to its static roster member, mirroring the closed switch the production descriptor uses.</summary>
    private static CoordinateReferenceSystem ToRosterMember(CoordinateReferenceSystemKind kind)
    {
        return kind switch
        {
            CoordinateReferenceSystemKind.Crs84 => CoordinateReferenceSystem.Crs84,
            CoordinateReferenceSystemKind.Epsg4326 => CoordinateReferenceSystem.Epsg4326,
            CoordinateReferenceSystemKind.WebMercator => CoordinateReferenceSystem.WebMercator,
            _ => default,
        };
    }

    /// <summary>
    /// The local swap reference: both ordinates of every point are read into locals before either
    /// destination element is written, exactly like the production swap this test suite is not
    /// allowed to call directly.
    /// </summary>
    private static void SwapPairs(ReadOnlySpan<double> source, Span<double> destination)
    {
        for(int index = 0; index < source.Length; index += 2)
        {
            double first = source[index];
            double second = source[index + 1];

            destination[index] = second;
            destination[index + 1] = first;
        }
    }

    /// <summary>Asserts every pair's ordinates appear position-swapped in the destination, by bit pattern.</summary>
    private static void AssertSwappedBitwise(double[] source, double[] destination)
    {
        for(int index = 0; index < source.Length; index += 2)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(source[index]), BitConverter.DoubleToInt64Bits(destination[index + 1]));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(source[index + 1]), BitConverter.DoubleToInt64Bits(destination[index]));
        }
    }

    /// <summary>Runs the pair out-of-place and in-place over the same input and asserts the two results agree by bit pattern.</summary>
    private static void AssertInPlaceMatchesOutOfPlaceBitwise(CoordinateReferenceSystem source, CoordinateReferenceSystem target, double[] input)
    {
        double[] outOfPlace = new double[input.Length];

        bool outOfPlaceAccepted = CoordinateReferenceTransform.TryTransform(source, target, input, outOfPlace, out CoordinateTransformRefusal outOfPlaceRefusal);

        double[] inPlace = (double[])input.Clone();
        bool inPlaceAccepted = CoordinateReferenceTransform.TryTransform(source, target, inPlace, inPlace, out CoordinateTransformRefusal inPlaceRefusal);

        Assert.IsTrue(outOfPlaceAccepted);
        Assert.IsTrue(inPlaceAccepted);
        Assert.AreEqual(CoordinateTransformRefusal.None, outOfPlaceRefusal);
        Assert.AreEqual(CoordinateTransformRefusal.None, inPlaceRefusal);

        for(int index = 0; index < input.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(outOfPlace[index]), BitConverter.DoubleToInt64Bits(inPlace[index]));
        }
    }
}
