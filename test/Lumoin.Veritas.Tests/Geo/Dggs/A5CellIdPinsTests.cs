using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Pinned cases for the <see cref="Serialization"/> named constants; the twelve resolution-0 cell
    /// ids as an explicit literal list; the negative-shift-count pass-through identity of
    /// <see cref="Serialization.CellToParent"/> at resolution 30; and the negative-resolution/world-cell
    /// behavior of <see cref="CellInfo.GetNumCells(int)"/>/<see cref="CellInfo.GetNumChildren"/>/<see cref="CellInfo.CellArea"/>.
    /// </summary>
    [TestClass]
    internal sealed class A5CellIdPinsTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="Serialization.MaxResolution"/> is fixed at 30.</summary>
        [TestMethod]
        [SuppressMessage("Style", "MSTEST0032:Assertion condition is always true",
            Justification = "The whole point of this pin is that MaxResolution's numeric value is fixed at 30; comparing the constant to a literal is the assertion, not an accident.")]
        public void MaxResolutionConstantIsThirty()
        {
            Assert.AreEqual(30, Serialization.MaxResolution);
        }

        /// <summary>Pins that <see cref="Serialization.WorldCell"/> is fixed at 0.</summary>
        [TestMethod]
        [SuppressMessage("Style", "MSTEST0032:Assertion condition is always true",
            Justification = "The whole point of this pin is that WorldCell's numeric value is fixed at 0; comparing the constant to a literal is the assertion, not an accident.")]
        public void WorldCellConstantIsZero()
        {
            Assert.AreEqual(0UL, Serialization.WorldCell);
        }

        /// <summary>Pins that the twelve resolution-0 cells, computed from the bit layout and as explicit hex literals, match <see cref="Serialization.GetResolutionZeroCells"/>.</summary>
        [TestMethod]
        public void ResolutionZeroCellsMatchThePinnedList()
        {
            // Derived from the bit layout: a resolution-0 cell packs its origin id (0-11) into the top
            // 6 bits (58-63) with the resolution marker at bit 57 and nothing else set — origin id N
            // serializes to (N << 58) | (1 << 57). Cross-checked in
            // ResolutionZeroCellsIncludeSeveralOfTheFixtureTestIds below against serialization.json's
            // own 237 test ids, four of which happen to already be resolution-0 cells.
            ulong[] expected = new ulong[12];
            for(int originId = 0; originId < 12; originId++)
            {
                expected[originId] = ((ulong)originId << 58) | (1UL << 57);
            }

            ulong[] actual = Serialization.GetResolutionZeroCells();
            Assert.AreSequenceEqual(expected, actual);

            string[] expectedHex =
            [
                "200000000000000", "600000000000000", "a00000000000000", "e00000000000000",
                "1200000000000000", "1600000000000000", "1a00000000000000", "1e00000000000000",
                "2200000000000000", "2600000000000000", "2a00000000000000", "2e00000000000000"
            ];

            for(int index = 0; index < actual.Length; index++)
            {
                Assert.AreEqual(expectedHex[index], Hex.U64ToHex(actual[index]));
            }
        }

        /// <summary>Pins that at least one of the serialization fixture's 237 test ids is itself a resolution-0 cell.</summary>
        [TestMethod]
        public async Task ResolutionZeroCellsIncludeSeveralOfTheFixtureTestIds()
        {
            using JsonDocument fixture = await LoadSerializationFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            HashSet<ulong> res0Cells = new(Serialization.GetResolutionZeroCells());

            int crossCheckedCount = 0;
            foreach(JsonElement idElement in fixture.RootElement.GetProperty("testIds").EnumerateArray())
            {
                ulong id = Hex.HexToU64(idElement.GetString()!);
                if(res0Cells.Contains(id))
                {
                    crossCheckedCount++;
                }
            }

            Assert.IsGreaterThan(0, crossCheckedCount);
        }

        /// <summary>Pins that <see cref="Serialization.CellToParent"/> with a negative shift count degenerates to a no-op, returning the input cell unchanged.</summary>
        [TestMethod]
        public void CellToParentAtResolutionThirtyOnASubResolutionThirtyCellReturnsInputUnchanged()
        {
            // Shifting by a negative count degenerates to a no-op: the marker OR contributes nothing,
            // and the parent computation returns the input unchanged.
            ulong cell = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 29));
            Assert.AreEqual(29, Serialization.GetResolution(cell));

            ulong result = Serialization.CellToParent(cell, 30);

            Assert.AreEqual(cell, result);
        }

        /// <summary>Pins that <see cref="CellInfo.GetNumChildren"/> for the world cell (resolution -1) returns 12 via the explicit zero-check branch rather than a computed ratio.</summary>
        [TestMethod]
        public void GetNumChildrenForNegativeParentResolutionReturnsTwelve()
        {
            // The world cell (resolution -1) has 12 resolution-0 children; GetNumCells(-1) is 0, so an
            // explicit zero-check kicks in here rather than computing through that count.
            Assert.AreEqual(12.0, CellInfo.GetNumChildren(-1, 0));
        }

        /// <summary>Pins that <see cref="CellInfo.GetNumChildren"/> from resolution 0 to resolution 1 returns 5.</summary>
        [TestMethod]
        public void GetNumChildrenForResolutionZeroParentReturnsFive()
        {
            Assert.AreEqual(5.0, CellInfo.GetNumChildren(0, 1));
        }

        /// <summary>Pins that <see cref="CellInfo.GetNumChildren"/> across a single Hilbert level returns 4, at both a mid-range and the top-of-range resolution pair.</summary>
        [TestMethod]
        public void GetNumChildrenAcrossHilbertLevelsReturnsFour()
        {
            Assert.AreEqual(4.0, CellInfo.GetNumChildren(5, 6));
            Assert.AreEqual(4.0, CellInfo.GetNumChildren(Serialization.MaxResolution - 2, Serialization.MaxResolution - 1));
        }

        /// <summary>Pins that both the <see cref="double"/>- and <see cref="BigInteger"/>-returning overloads of <see cref="CellInfo.GetNumCells(int)"/> return zero at resolution -1.</summary>
        [TestMethod]
        public void GetNumCellsForNegativeResolutionReturnsZeroForBothOverloads()
        {
            Assert.AreEqual(0.0, CellInfo.GetNumCells(-1));
            Assert.AreEqual(BigInteger.Zero, CellInfo.GetNumCells((BigInteger)(-1)));
        }

        /// <summary>Pins that <see cref="CellInfo.CellArea"/> at negative resolutions returns the whole authalic Earth area via its own early-return branch, distinct from dividing by a zero cell count.</summary>
        [TestMethod]
        public void CellAreaForNegativeResolutionReturnsAuthalicAreaEarth()
        {
            // A distinct early return, not equivalent to dividing by GetNumCells(-1) (which is zero) —
            // a load-bearing branch.
            Assert.AreEqual(Constants.AuthalicAreaEarth, CellInfo.CellArea(-1));
            Assert.AreEqual(Constants.AuthalicAreaEarth, CellInfo.CellArea(-5));
        }

        /// <summary>Pins that <see cref="A5CellId"/> comparison treats its underlying value as unsigned, so a top-bit-set id compares greater than a small id.</summary>
        [TestMethod]
        public void A5CellIdComparesUnsignedNotSigned()
        {
            A5CellId high = new(0x8000000000000000UL); // Top bit set: negative if ever compared as a signed long.
            A5CellId low = new(1UL);

            Assert.IsGreaterThan(0, high.CompareTo(low));
            Assert.IsTrue(high > low);
            Assert.IsTrue(low < high);
            Assert.IsTrue(high >= low);
            Assert.IsTrue(low <= high);
        }

        /// <summary>Loads <c>fixtures/serialization.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadSerializationFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/serialization.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
