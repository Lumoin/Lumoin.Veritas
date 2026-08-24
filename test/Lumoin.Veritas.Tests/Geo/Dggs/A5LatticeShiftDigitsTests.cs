using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/lattice/shift-digits.json</c> (1028 rows) for the digit-shift
    /// operation that rearranges a Hilbert curve level's quaternary digits so a child cell always
    /// overlaps its parent. All assertions are exact integer equality — no tolerances.
    /// </summary>
    [TestClass]
    internal sealed class A5LatticeShiftDigitsTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that shifting a digit sequence's quaternary digits matches the fixture's expected output across all 1028 rows.</summary>
        [TestMethod]
        public async Task ShiftDigitsProducesCorrectOutputForAllCases()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement cases = fixture.RootElement.GetProperty("shiftDigits");

            Assert.AreEqual(1028, cases.GetArrayLength());

            foreach(JsonElement testCase in cases.EnumerateArray())
            {
                List<int> digits = [];
                foreach(JsonElement digit in testCase.GetProperty("digitsBefore").EnumerateArray())
                {
                    digits.Add(digit.GetInt32());
                }

                int index = testCase.GetProperty("i").GetInt32();
                JsonElement flipsElement = testCase.GetProperty("flips");
                FlipPair flips = new((Flip)flipsElement[0].GetInt32(), (Flip)flipsElement[1].GetInt32());
                bool invertJ = testCase.GetProperty("invertJ").GetBoolean();
                int[] pattern = ResolvePattern(testCase.GetProperty("patternName").GetString());

                DigitShifter.ShiftDigits(digits, index, flips, invertJ, pattern);

                List<int> expected = [];
                foreach(JsonElement digit in testCase.GetProperty("digitsAfter").EnumerateArray())
                {
                    expected.Add(digit.GetInt32());
                }

                Assert.AreSequenceEqual(expected, digits);
            }
        }

        /// <summary>Maps a fixture-recorded pattern name to the corresponding permutation table.</summary>
        private static int[] ResolvePattern(string? patternName)
        {
            return patternName switch
            {
                "PATTERN" => DigitShifter.Pattern,
                "PATTERN_FLIPPED" => DigitShifter.PatternFlipped,
                "PATTERN_REVERSED" => DigitShifter.PatternReversed,
                "PATTERN_FLIPPED_REVERSED" => DigitShifter.PatternFlippedReversed,
                _ => throw new ArgumentOutOfRangeException(nameof(patternName), patternName, "Unknown fixture pattern name."),
            };
        }

        /// <summary>Loads <c>fixtures/lattice/shift-digits.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/lattice/shift-digits.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
