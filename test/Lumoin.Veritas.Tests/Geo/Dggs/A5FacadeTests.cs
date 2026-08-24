using System.Numerics;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Regions;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Tests for the public facade (<see cref="A5"/>) and the point-to-cell kernel seam
    /// (<see cref="A5PointToCellKernel"/>/<see cref="A5PointToCellKernelSelection"/>). The public API
    /// surface itself is pinned at compile time by the public-API analyzer's PublicAPI.Shipped.txt in
    /// the library project — a silent addition or removal fails the build, no reflection involved. Here:
    /// one happy-path smoke assertion per facade method, reusing known values already pinned elsewhere
    /// in this test project; the facade's own input sanitation; and the kernel's span-length contract
    /// and its exact equivalence with the per-point facade call.
    /// </summary>
    [TestClass]
    internal sealed class A5FacadeTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>
        /// The 19 parity points backing <c>fixtures/cell-to-lonlat.json</c>'s high-resolution pins in
        /// <c>A5HighResolutionPinsTests</c>, reused here for the kernel-versus-facade equivalence check.
        /// </summary>
        private static LonLat[] ParityPoints { get; } =
        [
            new(139.7623824402441, 35.677369792795794),
            new(80.7623824402441, 35.677369792795794),
            new(-80.7623824402441, 35.677369792795794),
            new(-139.7623824402441, 35.677369792795794),
            new(87, 35),
            new(88, 35),
            new(90, 0),
            new(120, 30),
            new(150, -30),
            new(170, 35),
            new(179, 0),
            new(-170, 35),
            new(-179, 0),
            new(100, 80),
            new(130, -70),
            new(0, 0),
            new(-73.9857, 40.7484),
            new(2.3522, 48.8566),
            new(151.2093, -33.8688)
        ];

        /// <summary>Pins that <see cref="A5.LonLatToCell"/> matches the same high-resolution case pinned directly against the internal kernel.</summary>
        [TestMethod]
        public void LonLatToCellMatchesThePinnedHighResolutionCase()
        {
            // Same case A5HighResolutionPinsTests pins directly against the internal Cell.LonLatToCell.
            A5CellId cellId = A5.LonLatToCell(new LonLat(0, 0), 21);

            Assert.AreEqual("4f05dccc726e0000", FormatHex(cellId));
        }

        /// <summary>Pins that <see cref="A5.CellToLonLat"/> of the world cell returns the origin, longitude 0 and latitude 0.</summary>
        [TestMethod]
        public void CellToLonLatOfWorldCellReturnsOrigin()
        {
            LonLat lonLat = A5.CellToLonLat(A5.WorldCell);

            Assert.AreEqual(0, lonLat.Longitude);
            Assert.AreEqual(0, lonLat.Latitude);
        }

        /// <summary>Pins that <see cref="A5.CellToBoundary"/> of the world cell returns the empty array.</summary>
        [TestMethod]
        public void CellToBoundaryOfWorldCellReturnsEmpty()
        {
            LonLat[] boundary = A5.CellToBoundary(A5.WorldCell);

            Assert.HasCount(0, boundary);
        }

        /// <summary>Pins that a cell id's string and UTF-8 span hex forms round-trip through <see cref="A5CellId.Parse(ReadOnlySpan{char})"/> and formatting back to the pinned hex string.</summary>
        [TestMethod]
        public void HexRoundTripMatchesThePinnedCase()
        {
            A5CellId cellId = new(1715004UL);

            Assert.AreEqual("1a2b3c", FormatHex(cellId));
            Assert.AreEqual(cellId, A5CellId.Parse("1a2b3c"));

            Span<byte> utf8 = stackalloc byte[16];
            Assert.IsTrue(cellId.TryFormat(utf8, out int bytesWritten));
            Assert.AreEqual(cellId, A5CellId.Parse(utf8[..bytesWritten]));
        }

        /// <summary>Pins that a cell id's canonical big-endian bytes round-trip through <see cref="A5CellId.TryWriteBigEndian"/>/<see cref="A5CellId.ReadBigEndian"/> and that lexicographic byte order matches unsigned numeric order.</summary>
        [TestMethod]
        public void CanonicalBigEndianBytesRoundTripAndSortNumerically()
        {
            A5CellId smaller = new(0x0000_0001_0000_0000UL);
            A5CellId larger = new(0x8000_0000_0000_0000UL);

            Span<byte> smallerBytes = stackalloc byte[A5CellId.CanonicalByteLength];
            Span<byte> largerBytes = stackalloc byte[A5CellId.CanonicalByteLength];
            Assert.IsTrue(smaller.TryWriteBigEndian(smallerBytes));
            Assert.IsTrue(larger.TryWriteBigEndian(largerBytes));

            Assert.AreEqual(smaller, A5CellId.ReadBigEndian(smallerBytes));
            Assert.AreEqual(larger, A5CellId.ReadBigEndian(largerBytes));

            // Lexicographic byte order must equal unsigned numeric order — the property that lets a
            // sorted byte store, a columnar index, and a signed payload agree.
            Assert.IsLessThan(0, smallerBytes.SequenceCompareTo(largerBytes));

            Assert.IsFalse(smaller.TryWriteBigEndian(stackalloc byte[A5CellId.CanonicalByteLength - 1]));
            Assert.Throws<ArgumentOutOfRangeException>(static () => A5CellId.ReadBigEndian(new byte[A5CellId.CanonicalByteLength - 1]));
        }

        /// <summary>Pins that <see cref="A5.GetResolution"/> of the world cell is -1.</summary>
        [TestMethod]
        public void GetResolutionOfWorldCellIsMinusOne()
        {
            Assert.AreEqual(-1, A5.GetResolution(A5.WorldCell));
        }

        /// <summary>Pins that <see cref="A5.CellToParent"/> matches the internal <see cref="Serialization.CellToParent"/> and that <see cref="A5.CellToChildren"/> of that parent contains the original cell.</summary>
        [TestMethod]
        public void CellToParentAndCellToChildrenRoundTripTheHighResolutionPin()
        {
            A5CellId cellId = A5.LonLatToCell(new LonLat(0, 0), 21);

            A5CellId parent = A5.CellToParent(cellId);
            Assert.AreEqual(new A5CellId(Serialization.CellToParent(cellId.Value)), parent);

            A5CellId[] children = A5.CellToChildren(parent, 21);
            Assert.Contains(cellId, children);
        }

        /// <summary>Pins that <see cref="A5.GetResolutionZeroCells"/> returns twelve cell ids matching the internal <see cref="Serialization.GetResolutionZeroCells"/> values in order.</summary>
        [TestMethod]
        public void GetResolutionZeroCellsReturnsTwelveCellsMatchingTheInternalLayer()
        {
            A5CellId[] cells = A5.GetResolutionZeroCells();

            Assert.HasCount(12, cells);
            ulong[] expected = Serialization.GetResolutionZeroCells();
            for(int index = 0; index < cells.Length; index++)
            {
                Assert.AreEqual(expected[index], cells[index].Value);
            }
        }

        /// <summary>Pins that <see cref="A5.GetCellCount"/> and <see cref="A5.GetCellCountExact"/> both return 12 at resolution 0.</summary>
        [TestMethod]
        public void CellCountAndCellCountExactMatchThePinnedResolutionZeroCount()
        {
            Assert.AreEqual(12.0, A5.GetCellCount(0));
            Assert.AreEqual(new BigInteger(12), A5.GetCellCountExact(0));
        }

        /// <summary>Pins that <see cref="A5.GetChildCount"/> for the world cell (resolution -1) returns 12.</summary>
        [TestMethod]
        public void GetChildCountForNegativeParentResolutionReturnsTwelve()
        {
            Assert.AreEqual(12.0, A5.GetChildCount(-1, 0));
        }

        /// <summary>Pins that <see cref="A5.CellArea"/> at a negative resolution returns the whole authalic Earth area.</summary>
        [TestMethod]
        public void CellAreaForNegativeResolutionReturnsAuthalicAreaEarth()
        {
            Assert.AreEqual(Constants.AuthalicAreaEarth, A5.CellArea(-1));
        }

        /// <summary>Pins that compacting the twelve resolution-0 cells collapses to the world cell.</summary>
        [TestMethod]
        public void CompactCollapsesTheTwelveResolutionZeroCellsToTheWorldCell()
        {
            A5CellId[] compacted = A5.Compact(A5.GetResolutionZeroCells());

            Assert.AreSequenceEqual(new[] { A5.WorldCell }, compacted);
        }

        /// <summary>Pins that uncompacting the world cell to resolution 0 expands back to the twelve resolution-0 cells.</summary>
        [TestMethod]
        public void UncompactExpandsTheWorldCellBackToTheResolutionZeroCells()
        {
            A5CellId[] expanded = A5.Uncompact([A5.WorldCell], 0);

            Assert.AreSequenceEqual(A5.GetResolutionZeroCells(), expanded);
        }

        /// <summary>Pins that both <see cref="A5.GridDisk"/> and <see cref="A5.GridDiskVertex"/> return only the center cell when k is zero.</summary>
        [TestMethod]
        public void GridDiskAndGridDiskVertexReturnOnlyTheCenterCellForZeroHops()
        {
            A5CellId cellId = A5.LonLatToCell(new LonLat(0, 0), 5);

            Assert.AreSequenceEqual(new[] { cellId }, A5.GridDisk(cellId, 0));
            Assert.AreSequenceEqual(new[] { cellId }, A5.GridDiskVertex(cellId, 0));
        }

        /// <summary>Pins that <see cref="A5.SphericalCap"/> matches the internal <see cref="SphericalCapTraversal.SphericalCap"/> cell-for-cell.</summary>
        [TestMethod]
        public void SphericalCapMatchesTheInternalLayer()
        {
            A5CellId cellId = A5.LonLatToCell(new LonLat(0, 0), 12);
            double radius = SphericalCapTraversal.EstimateCellRadius(12) * 2;

            A5CellId[] actual = A5.SphericalCap(cellId, radius);
            ulong[] expected = SphericalCapTraversal.SphericalCap(cellId.Value, radius);

            Assert.HasCount(expected.Length, actual);
            for(int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(expected[index], actual[index].Value);
            }
        }

        /// <summary>Pins that <see cref="A5.LineStringToCells"/> returns a single cell for a single waypoint.</summary>
        [TestMethod]
        public void LineStringToCellsReturnsSingleCellForSingleWaypoint()
        {
            A5CellId[] cells = A5.LineStringToCells([new LonLat(10, 50)], 5);

            Assert.HasCount(1, cells);
        }

        /// <summary>Pins that <see cref="A5.PolygonToCells(ReadOnlySpan{LonLat}, int)"/> matches the internal <see cref="PolygonToCells.GetCells(LonLat[], int)"/> cell-for-cell for a simple ring.</summary>
        [TestMethod]
        public void PolygonToCellsMatchesTheInternalLayerForASimpleRing()
        {
            LonLat[] ring = [new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44), new LonLat(-5, 44)];

            A5CellId[] actual = A5.PolygonToCells(ring, 5);
            ulong[] expected = PolygonToCells.GetCells(ring, 5);

            Assert.HasCount(expected.Length, actual);
            for(int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(expected[index], actual[index].Value);
            }
        }

        /// <summary>Pins that <see cref="A5.PolygonToCells(ReadOnlySpan{LonLat[]}, int)"/> matches the internal <see cref="PolygonToCells.GetCells(LonLat[][], int)"/> cell-for-cell for a ring with a hole.</summary>
        [TestMethod]
        public void PolygonToCellsWithHolesMatchesTheInternalLayer()
        {
            LonLat[] ring = [new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44), new LonLat(-5, 44)];
            LonLat[] hole = [new LonLat(2, 51), new LonLat(8, 51), new LonLat(8, 47), new LonLat(2, 47)];
            LonLat[][] rings = [ring, hole];

            A5CellId[] actual = A5.PolygonToCells(rings, 5);
            ulong[] expected = PolygonToCells.GetCells(rings, 5);

            Assert.HasCount(expected.Length, actual);
            for(int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(expected[index], actual[index].Value);
            }
        }

        /// <summary>Pins that <see cref="A5.LonLatToCell"/> throws <see cref="ArgumentOutOfRangeException"/> for NaN and infinite coordinates.</summary>
        [TestMethod]
        public void LonLatToCellThrowsForNonFiniteCoordinates()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LonLatToCell(new LonLat(double.NaN, 0), 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LonLatToCell(new LonLat(0, double.PositiveInfinity), 5));
        }

        /// <summary>Pins that <see cref="A5.LonLatToCell"/> throws <see cref="ArgumentOutOfRangeException"/> for resolutions above 30 and below -1.</summary>
        [TestMethod]
        public void LonLatToCellThrowsForOutOfRangeResolution()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LonLatToCell(new LonLat(0, 0), 31));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LonLatToCell(new LonLat(0, 0), -2));
        }

        /// <summary>Pins that <see cref="A5CellId.Parse(ReadOnlySpan{char})"/> and <see cref="A5CellId.TryParse(ReadOnlySpan{char}, out A5CellId)"/> reject empty, malformed, and oversized input while accepting valid uppercase hex.</summary>
        [TestMethod]
        public void ParseRejectsEmptyAndMalformedAndOversizedInput()
        {
            // A span surface has no null case — a null string converts to the empty span, so emptiness
            // IS the null posture.
            Assert.Throws<FormatException>(() => A5CellId.Parse(ReadOnlySpan<char>.Empty));
            Assert.Throws<FormatException>(() => A5CellId.Parse("zz"));
            Assert.Throws<FormatException>(() => A5CellId.Parse(" ff"));
            Assert.Throws<OverflowException>(() => A5CellId.Parse("1ffffffffffffffff"));

            Assert.IsFalse(A5CellId.TryParse(ReadOnlySpan<char>.Empty, out _));
            Assert.IsFalse(A5CellId.TryParse("1ffffffffffffffff", out _));
            Assert.IsTrue(A5CellId.TryParse("FF", out A5CellId parsed));
            Assert.AreEqual(new A5CellId(255UL), parsed);
        }

        /// <summary>Pins that <see cref="A5.LineStringToCells"/> throws <see cref="ArgumentOutOfRangeException"/> for out-of-range resolutions and non-finite waypoint coordinates.</summary>
        [TestMethod]
        public void LineStringToCellsThrowsForOutOfRangeResolutionOrNonFiniteWaypoint()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LineStringToCells([new LonLat(0, 0), new LonLat(1, 1)], -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LineStringToCells([new LonLat(0, 0), new LonLat(1, 1)], 31));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.LineStringToCells([new LonLat(double.NaN, 0), new LonLat(1, 1)], 5));
        }

        /// <summary>Pins that both <see cref="A5.PolygonToCells(ReadOnlySpan{LonLat}, int)"/> and <see cref="A5.PolygonToCells(ReadOnlySpan{LonLat[]}, int)"/> throw <see cref="ArgumentOutOfRangeException"/> for out-of-range resolutions and non-finite vertices.</summary>
        [TestMethod]
        public void PolygonToCellsThrowsForOutOfRangeResolutionOrNonFiniteVertex()
        {
            Assert.Throws<ArgumentOutOfRangeException>(static () => A5.PolygonToCells([new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44)], -1));
            Assert.Throws<ArgumentOutOfRangeException>(static () => A5.PolygonToCells([new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44)], 31));
            Assert.Throws<ArgumentOutOfRangeException>(static () => A5.PolygonToCells([new LonLat(double.NaN, 54), new LonLat(15, 54), new LonLat(15, 44)], 5));
            Assert.Throws<ArgumentOutOfRangeException>(static () => A5.PolygonToCells(new LonLat[][] { [new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44)] }, -1));
        }

        /// <summary>Pins that <see cref="A5.GetCellCount"/>, <see cref="A5.GetCellCountExact"/>, <see cref="A5.CellArea"/>, and <see cref="A5.GetChildCount"/> all throw <see cref="ArgumentOutOfRangeException"/> above <see cref="A5.MaxResolution"/>.</summary>
        [TestMethod]
        public void CellCountFamilyThrowsForResolutionAboveMaxResolution()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.GetCellCount(A5.MaxResolution + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.GetCellCountExact(A5.MaxResolution + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.CellArea(A5.MaxResolution + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.GetChildCount(0, A5.MaxResolution + 1));
        }

        /// <summary>Pins that <see cref="A5.Uncompact"/> throws <see cref="ArgumentOutOfRangeException"/> above <see cref="A5.MaxResolution"/>.</summary>
        [TestMethod]
        public void UncompactThrowsForResolutionAboveMaxResolution()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => A5.Uncompact([A5.WorldCell], A5.MaxResolution + 1));
        }

        /// <summary>Pins that <see cref="A5PointToCellKernelSelection.Default"/> throws <see cref="ArgumentException"/> when the source span's length is odd, breaking the longitude/latitude pairing.</summary>
        [TestMethod]
        public void KernelThrowsForOddSourceSpanLength()
        {
            Span<double> source = stackalloc double[3];
            Span<A5CellId> destination = stackalloc A5CellId[1];

            try
            {
                A5PointToCellKernelSelection.Default(source, 5, destination);
                Assert.Fail("Should have thrown for odd source span length.");
            }
            catch(ArgumentException)
            {
                //Expected.
            }
        }

        /// <summary>Pins that <see cref="A5PointToCellKernelSelection.Default"/> throws <see cref="ArgumentException"/> when the destination span's length does not match the source's point count.</summary>
        [TestMethod]
        public void KernelThrowsForMismatchedDestinationLength()
        {
            Span<double> source = stackalloc double[4];
            Span<A5CellId> destination = stackalloc A5CellId[1];

            try
            {
                A5PointToCellKernelSelection.Default(source, 5, destination);
                Assert.Fail("Should have thrown for mismatched destination length.");
            }
            catch(ArgumentException)
            {
                //Expected.
            }
        }

        /// <summary>Pins that <see cref="A5PointToCellKernelSelection.Default"/> and <see cref="A5PointToCellKernelSelection.Scalar"/> both match <see cref="A5.LonLatToCell"/> point-for-point over <see cref="ParityPoints"/>.</summary>
        [TestMethod]
        public void KernelDefaultAndScalarAreEquivalentPerPointToTheFacadeOverTheParityPoints()
        {
            const int resolution = 12;

            double[] source = new double[ParityPoints.Length * 2];
            for(int index = 0; index < ParityPoints.Length; index++)
            {
                source[2 * index] = ParityPoints[index].Longitude;
                source[(2 * index) + 1] = ParityPoints[index].Latitude;
            }

            A5CellId[] defaultResults = new A5CellId[ParityPoints.Length];
            A5PointToCellKernelSelection.Default(source, resolution, defaultResults);

            A5CellId[] scalarResults = new A5CellId[ParityPoints.Length];
            A5PointToCellKernelSelection.Scalar(source, resolution, scalarResults);

            for(int index = 0; index < ParityPoints.Length; index++)
            {
                A5CellId expected = A5.LonLatToCell(ParityPoints[index], resolution);
                Assert.AreEqual(expected, defaultResults[index], $"point {index}");
                Assert.AreEqual(expected, scalarResults[index], $"point {index}");
            }
        }

        /// <summary>
        /// Renders a cell id's hexadecimal form through the span-based formatter — the tests' only
        /// string materialization, at the assertion boundary where a comparison against fixture text
        /// genuinely needs one.
        /// </summary>
        private static string FormatHex(A5CellId cellId)
        {
            Span<char> buffer = stackalloc char[16];
            Assert.IsTrue(cellId.TryFormat(buffer, out int charsWritten));

            return new string(buffer[..charsWritten]);
        }
    }
}
