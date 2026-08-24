using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/compact.json</c> for <see cref="Compaction"/>: the twelve
    /// <see cref="Compaction.Compact"/> cases, the eight <see cref="Compaction.Uncompact"/> cases
    /// (including the lower-resolution error case), and the three compact/uncompact round-trip cases.
    /// Cell ids are compared bit-for-bit; output ordering is unsigned-64 ascending.
    /// </summary>
    [TestClass]
    internal sealed class A5CompactionTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="Compaction.Compact"/>, sorted unsigned-ascending, matches the fixture's expected output for every case.</summary>
        [TestMethod]
        public async Task CompactMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("compact").EnumerateArray())
            {
                ulong[] input = ReadHexArray(testCase.GetProperty("input"));
                ulong[] expected = ReadHexArray(testCase.GetProperty("expectedOutput"));
                Array.Sort(expected);

                ulong[] result = Compaction.Compact(input);

                Assert.AreSequenceEqual(expected, result);
            }
        }

        /// <summary>Pins that <see cref="Compaction.Uncompact"/> matches the fixture's expected count and target resolution for every non-error case.</summary>
        [TestMethod]
        public async Task UncompactMatchesFixtureForEveryNonErrorCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("uncompact").EnumerateArray())
            {
                if(IsExpectedErrorCase(testCase))
                {
                    continue;
                }

                ulong[] input = ReadHexArray(testCase.GetProperty("input"));
                int targetResolution = testCase.GetProperty("targetResolution").GetInt32();
                int expectedCount = testCase.GetProperty("expectedCount").GetInt32();

                ulong[] result = Compaction.Uncompact(input, targetResolution);

                Assert.HasCount(expectedCount, result);
                foreach(ulong cell in result)
                {
                    Assert.AreEqual(targetResolution, Serialization.GetResolution(cell));
                }
            }
        }

        /// <summary>Pins that <see cref="Compaction.Uncompact"/> throws <see cref="ArgumentOutOfRangeException"/> for every fixture case whose target resolution is lower than the input cells' resolution.</summary>
        [TestMethod]
        public async Task UncompactThrowsWhenTargetResolutionIsLowerThanCellResolution()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int checkedCount = 0;

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("uncompact").EnumerateArray())
            {
                if(!IsExpectedErrorCase(testCase))
                {
                    continue;
                }

                ulong[] input = ReadHexArray(testCase.GetProperty("input"));
                int targetResolution = testCase.GetProperty("targetResolution").GetInt32();

                bool threw = false;
                try
                {
                    Compaction.Uncompact(input, targetResolution);
                }
                catch(ArgumentOutOfRangeException)
                {
                    threw = true;
                }

                Assert.IsTrue(threw, "Uncompacting to a lower resolution must throw.");
                checkedCount++;
            }

            Assert.IsGreaterThan(0, checkedCount);
        }

        /// <summary>Pins that compacting then uncompacting each fixture's initial cells matches its expected compacted set, resolution, and final cell counts for every round-trip case.</summary>
        [TestMethod]
        public async Task RoundTripMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("roundTrip").EnumerateArray())
            {
                ulong[] initialCells = ReadHexArray(testCase.GetProperty("initialCells"));
                ulong[] afterCompact = ReadHexArray(testCase.GetProperty("afterCompact"));
                int targetResolution = testCase.GetProperty("targetResolution").GetInt32();

                ulong[] compactResult = Compaction.Compact(initialCells);
                ulong[] sortedCompactResult = (ulong[])compactResult.Clone();
                Array.Sort(sortedCompactResult);
                ulong[] sortedAfterCompact = (ulong[])afterCompact.Clone();
                Array.Sort(sortedAfterCompact);
                Assert.AreSequenceEqual(sortedAfterCompact, sortedCompactResult);

                ulong[] uncompactResult = Compaction.Uncompact(afterCompact, targetResolution);

                if(testCase.TryGetProperty("expectedCount", out JsonElement expectedCountElement))
                {
                    Assert.HasCount(expectedCountElement.GetInt32(), uncompactResult);
                }

                if(testCase.TryGetProperty("expectedFinalCount", out JsonElement expectedFinalCountElement))
                {
                    Assert.HasCount(expectedFinalCountElement.GetInt32(), uncompactResult);
                }

                foreach(ulong cell in uncompactResult)
                {
                    Assert.AreEqual(targetResolution, Serialization.GetResolution(cell));
                }

                if(testCase.TryGetProperty("afterUncompact", out JsonElement afterUncompactElement))
                {
                    ulong[] expectedAfterUncompact = ReadHexArray(afterUncompactElement);
                    Assert.AreSequenceEqual(expectedAfterUncompact, uncompactResult);
                }
            }
        }

        /// <summary>Whether an <c>uncompact</c> fixture case is the deliberate lower-resolution error case.</summary>
        private static bool IsExpectedErrorCase(JsonElement testCase)
        {
            return testCase.TryGetProperty("expectedError", out JsonElement errorFlag) && errorFlag.GetBoolean();
        }

        /// <summary>Parses a fixture array of hex strings into raw cell ids.</summary>
        private static ulong[] ReadHexArray(JsonElement arrayElement)
        {
            ulong[] result = new ulong[arrayElement.GetArrayLength()];

            int index = 0;
            foreach(JsonElement element in arrayElement.EnumerateArray())
            {
                result[index] = Hex.HexToU64(element.GetString()!);
                index++;
            }

            return result;
        }

        /// <summary>Loads <c>fixtures/compact.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/compact.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
