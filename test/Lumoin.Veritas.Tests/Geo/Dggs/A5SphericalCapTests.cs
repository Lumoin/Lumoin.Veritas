using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/cap.json</c> for <see cref="SphericalCapTraversal"/>: the
    /// eight <c>sphericalCap</c> cases (expanded via <see cref="Compaction.Uncompact"/>, exact hex
    /// arrays in the fixture's own order — no extra sort, since <see cref="SphericalCapTraversal.SphericalCap"/>
    /// output is already unsigned-ascending and <see cref="Compaction.Uncompact"/> preserves it), the
    /// six compacted cases (unsigned-ascending, exact), and the three helper tables
    /// (<see cref="SphericalCapTraversal.MetersToH"/>/<see cref="SphericalCapTraversal.EstimateCellRadius"/>
    /// exact-equality doubles, <see cref="SphericalCapTraversal.PickCoarseResolution"/> exact integers)
    /// with their monotonicity and never-exceeds-target side conditions.
    /// </summary>
    [TestClass]
    internal sealed class A5SphericalCapTests
    {
        /// <summary>
        /// Tolerance for the sine-routed <c>metersToH</c> rows. The fixture's recorded value and this
        /// runtime's computed value disagree by 1 ulp in <c>sin(10000000 / (2 · 6371007.2))</c> (verified
        /// bit patterns: <c>3fe69d2eed0eb2cc</c> fixture vs <c>3fe69d2eed0eb2cd</c> here), which
        /// propagates to 2 ulps in <c>h = s²</c> — a demonstrable platform-libm divergence, so the row is
        /// asserted at the same 0.5e-15 regime this project uses elsewhere for libm-routed values instead
        /// of bit-exact. All algebraic-only outputs in this class stay bit-exact.
        /// </summary>
        private const double Precision15 = 0.5e-15;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the uncompacted spherical cap cell set matches the fixture's expected hex array in fixture order.</summary>
        [TestMethod]
        public async Task SphericalCapMatchesFixtureWhenUncompacted()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("sphericalCap").EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("cellId").GetString()!);
                double radius = testCase.GetProperty("radius").GetDouble();
                int targetResolution = Serialization.GetResolution(cellId);

                ulong[] cells = Compaction.Uncompact(SphericalCapTraversal.SphericalCap(cellId, radius), targetResolution);

                string[] expected = ReadStringArray(testCase.GetProperty("cells"));
                Assert.AreSequenceEqual(expected, ToHex(cells));
            }
        }

        /// <summary>Pins that the compacted spherical cap cell set, sorted unsigned-ascending, matches the fixture's expected hex array.</summary>
        [TestMethod]
        public async Task SphericalCapMatchesFixtureCompactedCases()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("sphericalCapCompact").EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("cellId").GetString()!);
                double radius = testCase.GetProperty("radius").GetDouble();

                ulong[] compacted = SphericalCapTraversal.SphericalCap(cellId, radius);
                ulong[] sorted = (ulong[])compacted.Clone();
                Array.Sort(sorted);

                string[] expected = ReadStringArray(testCase.GetProperty("compactedCells"));
                Assert.AreSequenceEqual(expected, ToHex(sorted));
            }
        }

        /// <summary>Pins that MetersToH matches the fixture's expected values at the documented libm-divergence tolerance.</summary>
        [TestMethod]
        public async Task MetersToHMatchesFixtureAtLibmTolerance()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("helpers").GetProperty("metersToH").EnumerateArray())
            {
                double meters = testCase.GetProperty("meters").GetDouble();

                // Sine-routed: asserted at the libm-divergence regime documented on Precision15 above,
                // never looser.
                Assert.AreEqual(testCase.GetProperty("expectedH").GetDouble(), SphericalCapTraversal.MetersToH(meters), Precision15);
            }
        }

        /// <summary>Pins that EstimateCellRadius matches the fixture's expected meter values bit-exactly.</summary>
        [TestMethod]
        public async Task EstimateCellRadiusMatchesFixtureExactly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("helpers").GetProperty("estimateCellRadius").EnumerateArray())
            {
                int resolution = testCase.GetProperty("resolution").GetInt32();
                Assert.AreEqual(testCase.GetProperty("expectedMeters").GetDouble(), SphericalCapTraversal.EstimateCellRadius(resolution));
            }
        }

        /// <summary>Pins that EstimateCellRadius decreases monotonically as resolution increases across the fixture's rows.</summary>
        [TestMethod]
        public async Task EstimateCellRadiusDecreasesWithIncreasingResolution()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            double previousMeters = double.PositiveInfinity;
            foreach(JsonElement testCase in fixture.RootElement.GetProperty("helpers").GetProperty("estimateCellRadius").EnumerateArray())
            {
                double expectedMeters = testCase.GetProperty("expectedMeters").GetDouble();
                Assert.IsLessThan(previousMeters, expectedMeters);
                previousMeters = expectedMeters;
            }
        }

        /// <summary>Pins that EstimateCellRadius throws for resolutions outside its valid lookup range.</summary>
        [TestMethod]
        public void EstimateCellRadiusThrowsForOutOfRangeResolution()
        {
            // The lookup has an explicit range guard rather than the undefined out-of-bounds read the
            // arithmetic would otherwise perform.
            Assert.Throws<ArgumentOutOfRangeException>(() => SphericalCapTraversal.EstimateCellRadius(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SphericalCapTraversal.EstimateCellRadius(31));
        }

        /// <summary>Pins that PickCoarseResolution matches the fixture's expected resolution and never exceeds the target resolution.</summary>
        [TestMethod]
        public async Task PickCoarseResolutionMatchesFixtureExactly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("helpers").GetProperty("pickCoarseResolution").EnumerateArray())
            {
                double radius = testCase.GetProperty("radius").GetDouble();
                int targetResolution = testCase.GetProperty("targetRes").GetInt32();

                int coarseResolution = SphericalCapTraversal.PickCoarseResolution(radius, targetResolution);

                Assert.AreEqual(testCase.GetProperty("expectedCoarseRes").GetInt32(), coarseResolution);
                Assert.IsLessThanOrEqualTo(targetResolution, coarseResolution);
            }
        }

        /// <summary>Converts cell ids to hex, preserving order.</summary>
        private static string[] ToHex(ulong[] cellIds)
        {
            string[] hex = new string[cellIds.Length];
            for(int index = 0; index < cellIds.Length; index++)
            {
                hex[index] = Hex.U64ToHex(cellIds[index]);
            }

            return hex;
        }

        /// <summary>Reads a JSON array of strings, preserving order.</summary>
        private static string[] ReadStringArray(JsonElement arrayElement)
        {
            string[] result = new string[arrayElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement element in arrayElement.EnumerateArray())
            {
                result[index] = element.GetString()!;
                index++;
            }

            return result;
        }

        /// <summary>Loads <c>fixtures/traversal/cap.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/cap.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
