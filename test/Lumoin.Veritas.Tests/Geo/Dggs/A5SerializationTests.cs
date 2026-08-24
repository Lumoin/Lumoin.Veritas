using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/serialization.json</c> for <see cref="Serialization"/>: the 31
    /// resolution bit masks, all 237 fixture test ids round-tripping through
    /// <see cref="Serialization.Serialize"/>/<see cref="Serialization.Deserialize"/> bit-for-bit, the
    /// resolution-30 variable-width marker regimes and their fallback to resolution 29, the
    /// <see cref="Serialization.CellToParent"/>/<see cref="Serialization.CellToChildren"/> hierarchy,
    /// <see cref="Serialization.IsChildOf"/>, and <see cref="Serialization.GetResolutionZeroCells"/>. Every
    /// assertion here is exact — cell ids are compared bit-for-bit, with zero tolerance.
    /// </summary>
    [TestClass]
    internal sealed class A5SerializationTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the fixture's resolution mask array has exactly MaxResolution + 1 entries.</summary>
        [TestMethod]
        public async Task ResolutionMasksFixtureHasThirtyOneEntries()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(Serialization.MaxResolution + 1, fixture.RootElement.GetProperty("resolutionMasks").GetArrayLength());
        }

        /// <summary>Pins that serializing a cell at each resolution produces the fixture's expected 64-bit binary mask.</summary>
        [TestMethod]
        public async Task SerializeEncodesEachResolutionMaskCorrectly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            int resolution = 0;
            foreach(JsonElement maskElement in fixture.RootElement.GetProperty("resolutionMasks").EnumerateArray())
            {
                // Origin 0 has first quintant 4, so segment 4 gives the start of its Hilbert curve.
                A5Cell cell = new(Origins.All[0], 4, 0UL, resolution);
                ulong serialized = Serialization.Serialize(cell);
                Assert.AreEqual(maskElement.GetString(), ToBinary64(serialized));
                resolution++;
            }
        }

        /// <summary>Pins that GetResolution extracts the expected resolution from each fixture mask.</summary>
        [TestMethod]
        public async Task GetResolutionExtractsResolutionFromEachMask()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            int expectedResolution = 0;
            foreach(JsonElement maskElement in fixture.RootElement.GetProperty("resolutionMasks").EnumerateArray())
            {
                string binary = maskElement.GetString()!;
                Assert.AreEqual(64, binary.Length);
                ulong parsed = Convert.ToUInt64(binary, 2);
                Assert.AreEqual(expectedResolution, Serialization.GetResolution(parsed));
                expectedResolution++;
            }
        }

        /// <summary>Pins that serializing a cell encodes its origin, segment, and S value into the expected bit pattern.</summary>
        [TestMethod]
        public void SerializeEncodesOriginSegmentAndSCorrectly()
        {
            A5Cell cell = new(Origins.All[0], 4, 0UL, Serialization.MaxResolution - 1);
            ulong serialized = Serialization.Serialize(cell);
            Assert.AreEqual(0b10UL, serialized);
        }

        /// <summary>Pins that Serialize throws ArgumentOutOfRangeException with the expected message when S exceeds the resolution's range.</summary>
        [TestMethod]
        public void SerializeThrowsWhenSIsTooLargeForResolution()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(static () => Serialization.Serialize(new A5Cell(Origins.All[0], 0, 16UL, 3)));
            Assert.IsTrue(exception.Message.Contains("S (16) is too large for resolution level 3", StringComparison.Ordinal));
        }

        /// <summary>Pins that Serialize throws ArgumentOutOfRangeException with the expected message when the resolution exceeds the maximum.</summary>
        [TestMethod]
        public void SerializeThrowsWhenResolutionExceedsMaximum()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(static () => Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 31)));
            Assert.IsTrue(exception.Message.Contains("Resolution (31) is too large", StringComparison.Ordinal));
        }

        /// <summary>Pins that deserializing then reserializing every resolution mask, at every non-zero origin, round-trips bit-for-bit.</summary>
        [TestMethod]
        public async Task RoundTripPreservesResolutionMasksAcrossAllOrigins()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            List<string> masks = [];
            foreach(JsonElement maskElement in fixture.RootElement.GetProperty("resolutionMasks").EnumerateArray())
            {
                masks.Add(maskElement.GetString()!);
            }

            for(int originId = 1; originId < 12; originId++)
            {
                string originSegmentId = Convert.ToString(5 * originId, 2).PadLeft(6, '0');

                // Exclude res 30 (index MaxResolution): it has a different bit layout (5-bit quintant).
                for(int resolution = Serialization.FirstHilbertResolution; resolution < Serialization.MaxResolution; resolution++)
                {
                    string binary = originSegmentId + masks[resolution][6..];
                    ulong serialized = Convert.ToUInt64(binary, 2);
                    A5Cell deserialized = Serialization.Deserialize(serialized);
                    ulong reserialized = Serialization.Serialize(deserialized);
                    Assert.AreEqual(serialized, reserialized);
                }
            }
        }

        /// <summary>Pins that deserializing then reserializing every one of the fixture's 237 test ids round-trips bit-for-bit.</summary>
        [TestMethod]
        public async Task RoundTripPreservesAllFixtureTestIds()
        {
            foreach(ulong cell in await LoadTestIdsAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                A5Cell deserialized = Serialization.Deserialize(cell);
                ulong reserialized = Serialization.Serialize(deserialized);
                Assert.AreEqual(cell, reserialized);
            }
        }

        /// <summary>Pins that every fixture test id's children resolve back to it via CellToParent.</summary>
        [TestMethod]
        public async Task HierarchyRoundTripsBetweenCellToParentAndCellToChildren()
        {
            foreach(ulong cell in await LoadTestIdsAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                int resolution = Serialization.GetResolution(cell);
                if(resolution >= Serialization.MaxResolution)
                {
                    continue;
                }

                // Skip res 29 cells whose only child would fall back to res 29 itself (out-of-bounds quintant).
                ulong firstChild = Serialization.CellToChildren(cell)[0];
                if(Serialization.GetResolution(firstChild) != resolution + 1)
                {
                    continue;
                }

                Assert.AreEqual(cell, Serialization.CellToParent(firstChild));

                foreach(ulong child in Serialization.CellToChildren(cell))
                {
                    Assert.AreEqual(cell, Serialization.CellToParent(child));
                }
            }
        }

        /// <summary>Pins that CellToChildren at the cell's own current resolution returns a single-element array containing that same cell.</summary>
        [TestMethod]
        public async Task CellToChildrenWithSameResolutionReturnsOriginalCell()
        {
            foreach(ulong cell in await LoadTestIdsAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                int currentResolution = Serialization.GetResolution(cell);
                ulong[] children = Serialization.CellToChildren(cell, currentResolution);

                Assert.HasCount(1, children);
                Assert.AreEqual(cell, children[0]);
            }
        }

        /// <summary>Pins that CellToParent at the cell's own current resolution returns the cell unchanged.</summary>
        [TestMethod]
        public async Task CellToParentWithSameResolutionReturnsOriginalCell()
        {
            foreach(ulong cell in await LoadTestIdsAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                int currentResolution = Serialization.GetResolution(cell);
                ulong parent = Serialization.CellToParent(cell, currentResolution);

                Assert.AreEqual(cell, parent);
            }
        }

        /// <summary>Pins that a non-Hilbert cell's children in the non-Hilbert range number five, each resolving back to the parent.</summary>
        [TestMethod]
        public void NonHilbertToNonHilbertHierarchyProducesFiveChildren()
        {
            ulong cell = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 0));
            ulong[] children = Serialization.CellToChildren(cell);

            Assert.HasCount(5, children);
            foreach(ulong child in children)
            {
                Assert.AreEqual(cell, Serialization.CellToParent(child));
            }
        }

        /// <summary>Pins that a non-Hilbert cell's children crossing into the Hilbert range number four, each resolving back to the parent.</summary>
        [TestMethod]
        public void NonHilbertToHilbertHierarchyProducesFourChildren()
        {
            ulong cell = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 1));
            ulong[] children = Serialization.CellToChildren(cell);

            Assert.HasCount(4, children);
            foreach(ulong child in children)
            {
                Assert.AreEqual(cell, Serialization.CellToParent(child));
            }
        }

        /// <summary>Pins that a Hilbert cell's non-Hilbert parent's children include the original cell.</summary>
        [TestMethod]
        public void HilbertToNonHilbertHierarchyRoundTrips()
        {
            ulong cell = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 2));
            ulong parent = Serialization.CellToParent(cell, 1);
            ulong[] children = Serialization.CellToChildren(parent);

            Assert.HasCount(4, children);
            Assert.Contains(cell, children);
        }

        /// <summary>Pins that a chain of cells at resolutions 0 through 4 link correctly through CellToParent and CellToChildren.</summary>
        [TestMethod]
        public void LowResolutionHierarchyChainLinksParentsAndChildren()
        {
            int[] resolutions = [0, 1, 2, 3, 4];
            ulong[] cells = new ulong[resolutions.Length];
            for(int index = 0; index < resolutions.Length; index++)
            {
                cells[index] = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, resolutions[index]));
            }

            for(int index = 1; index < cells.Length; index++)
            {
                Assert.AreEqual(cells[index - 1], Serialization.CellToParent(cells[index]));
            }

            for(int index = 0; index < cells.Length - 1; index++)
            {
                ulong[] children = Serialization.CellToChildren(cells[index]);
                Assert.Contains(cells[index + 1], children);
            }
        }

        /// <summary>Pins that expanding a base cell to children resolution by resolution produces the expected 12/60/240/960 division counts.</summary>
        [TestMethod]
        public void BaseCellDivisionCountsMatchExpectedTotals()
        {
            ulong baseCell = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, -1));
            ulong[] currentCells = [baseCell];
            int[] expectedCounts = [12, 60, 240, 960]; // 12, 12*5, 12*5*4, 12*5*4*4

            for(int resolution = 0; resolution < 4; resolution++)
            {
                List<ulong> allChildren = [];
                foreach(ulong cell in currentCells)
                {
                    allChildren.AddRange(Serialization.CellToChildren(cell));
                }

                Assert.HasCount(expectedCounts[resolution], allChildren);
                currentCells = allChildren.ToArray();
            }
        }

        /// <summary>Pins that IsChildOf is true for a cell against itself at the same resolution, across every Hilbert resolution.</summary>
        [TestMethod]
        public void IsChildOfIsTrueForSelfAtSameResolution()
        {
            for(int resolution = Serialization.FirstHilbertResolution; resolution < Serialization.MaxResolution; resolution++)
            {
                ulong cell = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, resolution));
                Assert.IsTrue(Serialization.IsChildOf(cell, cell, resolution));
            }
        }

        /// <summary>Pins that IsChildOf is true for every direct child of a parent, across every Hilbert resolution.</summary>
        [TestMethod]
        public void IsChildOfIsTrueForDirectChildren()
        {
            for(int resolution = Serialization.FirstHilbertResolution; resolution < Serialization.MaxResolution - 1; resolution++)
            {
                ulong parent = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, resolution));
                foreach(ulong child in Serialization.CellToChildren(parent))
                {
                    Assert.IsTrue(Serialization.IsChildOf(child, parent, resolution));
                }
            }
        }

        /// <summary>Pins that IsChildOf is true for every descendant several generations below a parent.</summary>
        [TestMethod]
        public void IsChildOfIsTrueForDeepDescendants()
        {
            ulong parent = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 5));
            ulong[] descendants = [parent];
            for(int resolution = 6; resolution < 10; resolution++)
            {
                List<ulong> next = [];
                foreach(ulong cell in descendants)
                {
                    next.AddRange(Serialization.CellToChildren(cell));
                }

                descendants = next.ToArray();
            }

            foreach(ulong descendant in descendants)
            {
                Assert.IsTrue(Serialization.IsChildOf(descendant, parent, 5));
            }
        }

        /// <summary>Pins that IsChildOf is false between distinct sibling cells.</summary>
        [TestMethod]
        public void IsChildOfIsFalseForSiblings()
        {
            ulong parent = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 4));
            ulong[] children = Serialization.CellToChildren(parent);

            for(int i = 0; i < children.Length; i++)
            {
                for(int j = 0; j < children.Length; j++)
                {
                    if(i == j)
                    {
                        continue;
                    }

                    Assert.IsFalse(Serialization.IsChildOf(children[i], children[j], 5));
                }
            }
        }

        /// <summary>Pins that IsChildOf is false when the candidate child is actually at a shallower resolution than the parent.</summary>
        [TestMethod]
        public void IsChildOfIsFalseWhenChildIsAtShallowerResolutionThanParent()
        {
            ulong grandparent = Serialization.Serialize(new A5Cell(Origins.All[0], 0, 0UL, 4));
            ulong parent = Serialization.CellToChildren(grandparent)[0]; // Resolution 5.

            Assert.IsFalse(Serialization.IsChildOf(grandparent, parent, 5));
        }

        /// <summary>Pins that GetResolutionZeroCells returns exactly twelve cells, all at resolution zero, matching the pinned hex values.</summary>
        [TestMethod]
        public void GetResolutionZeroCellsReturnsTwelveResolutionZeroCells()
        {
            ulong[] res0Cells = Serialization.GetResolutionZeroCells();
            Assert.HasCount(12, res0Cells);

            foreach(ulong cell in res0Cells)
            {
                Assert.AreEqual(0, Serialization.GetResolution(cell));
            }

            // Derived by right-padding each origin's marker-bit hex digit with zeros to fill the 64-bit
            // width; cross-checked against the pinned res-0 cells in A5CellIdPinsTests.
            string[] expectedHexValues =
            [
                "200000000000000", "600000000000000", "a00000000000000", "e00000000000000",
                "1200000000000000", "1600000000000000", "1a00000000000000", "1e00000000000000",
                "2200000000000000", "2600000000000000", "2a00000000000000", "2e00000000000000"
            ];

            for(int index = 0; index < res0Cells.Length; index++)
            {
                Assert.AreEqual(expectedHexValues[index], Hex.U64ToHex(res0Cells[index]));
            }
        }

        /// <summary>Pins that GetResolution detects resolution 30 from the least-significant marker bit across representative bit patterns.</summary>
        [TestMethod]
        public void GetResolutionDetectsResolutionThirtyFromLeastSignificantBit()
        {
            Assert.AreEqual(30, Serialization.GetResolution(1UL));
            Assert.AreEqual(30, Serialization.GetResolution(3UL));
            Assert.AreEqual(30, Serialization.GetResolution(ulong.MaxValue));
        }

        /// <summary>Pins that resolution-30 cells round-trip for every valid quintant, encoding the correct one-, three-, or five-bit marker.</summary>
        [TestMethod]
        public void ResolutionThirtyRoundTripsForValidQuintants()
        {
            // Quintants 0-31 use ...1, 32-39 use ...100, 40-41 use ...10000.
            for(int quintant = 0; quintant < 42; quintant++)
            {
                int originId = quintant / 5;
                Origin origin = Origins.All[originId];
                int segmentN = quintant % 5;
                int segment = (segmentN + origin.FirstQuintant) % 5;

                A5Cell cell = new(origin, segment, 0UL, 30);
                ulong serialized = Serialization.Serialize(cell);
                Assert.AreEqual(30, Serialization.GetResolution(serialized));

                if(quintant <= 31)
                {
                    Assert.AreEqual(1UL, serialized & 1UL);
                }
                else if(quintant <= 39)
                {
                    Assert.AreEqual(0b100UL, serialized & 0b111UL);
                }
                else
                {
                    Assert.AreEqual(0b10000UL, serialized & 0b11111UL);
                }

                A5Cell deserialized = Serialization.Deserialize(serialized);
                Assert.AreEqual(originId, deserialized.Origin.Id);
                Assert.AreEqual(segment, deserialized.Segment);
                Assert.AreEqual(0UL, deserialized.S);
                Assert.AreEqual(30, deserialized.Resolution);

                Assert.AreEqual(serialized, Serialization.Serialize(deserialized));
            }
        }

        /// <summary>Pins that resolution-30 cells with a one-bit marker round-trip for non-zero S values.</summary>
        [TestMethod]
        public void ResolutionThirtyRoundTripsWithNonZeroSForOneBitMarker()
        {
            Origin origin = Origins.All[0];
            int segment = (0 + origin.FirstQuintant) % 5; // segmentN = 0.

            AssertResolutionThirtyRoundTrips(origin, segment);
        }

        /// <summary>Pins the exact one-bit-marker bit layout for quintants 0 through 31 at resolution 30.</summary>
        [TestMethod]
        public void BitLayoutOneBitMarkerEncodesQuintantZeroToThirtyOne()
        {
            Origin origin = Origins.All[0];
            int segment = (0 + origin.FirstQuintant) % 5; // quintant = 0.

            ulong cell0 = Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30));
            Assert.AreEqual(1UL, cell0);

            ulong cell1 = Serialization.Serialize(new A5Cell(origin, segment, 1UL, 30));
            Assert.AreEqual(0b11UL, cell1); // S=1 at bit 1, marker at bit 0.
        }

        /// <summary>Pins the exact five-bit-marker bit layout for quintants 40 through 41 at resolution 30.</summary>
        [TestMethod]
        public void BitLayoutFiveBitMarkerEncodesQuintantFortyToFortyOne()
        {
            // Origin 8, segmentN=0 -> quintant 40.
            Origin origin = Origins.All[8];
            int segment = (0 + origin.FirstQuintant) % 5;

            ulong cell0 = Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30));
            Assert.AreEqual(0b10000UL, cell0); // Just the marker.

            ulong cell1 = Serialization.Serialize(new A5Cell(origin, segment, 1UL, 30));
            Assert.AreEqual(0b110000UL, cell1); // S=1 at bit 5, marker 10000 at bits 4-0.
        }

        /// <summary>Pins the exact three-bit-marker bit layout for quintants 32 through 39 at resolution 30.</summary>
        [TestMethod]
        public void BitLayoutThreeBitMarkerEncodesQuintantThirtyTwoToThirtyNine()
        {
            // Origin 6 has quintants 30-34; segmentN=2 gives quintant 32.
            Origin origin = Origins.All[6];
            int segmentN = 2;
            int segment = (segmentN + origin.FirstQuintant) % 5;

            ulong cell0 = Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30));
            Assert.AreEqual(0b100UL, cell0); // Just the marker.

            ulong cell1 = Serialization.Serialize(new A5Cell(origin, segment, 1UL, 30));
            Assert.AreEqual(0b1100UL, cell1); // S=1 at bit 3, marker 100 at bits 2-0.
        }

        /// <summary>Pins that resolution-30 cells with a three-bit marker round-trip for non-zero S values.</summary>
        [TestMethod]
        public void ResolutionThirtyRoundTripsWithNonZeroSForThreeBitMarker()
        {
            // Quintant 35 (origin 7, segmentN=0) uses the ...100 encoding.
            Origin origin = Origins.All[7];
            int segment = (0 + origin.FirstQuintant) % 5;

            foreach(ulong s in new ulong[] { 0UL, 1UL, 42UL, (1UL << 58) - 1UL })
            {
                A5Cell cell = new(origin, segment, s, 30);
                ulong serialized = Serialization.Serialize(cell);
                Assert.AreEqual(0b100UL, serialized & 0b111UL);

                A5Cell deserialized = Serialization.Deserialize(serialized);
                Assert.AreEqual(s, deserialized.S);
                Assert.AreEqual(30, deserialized.Resolution);
                Assert.AreEqual(serialized, Serialization.Serialize(deserialized));
            }
        }

        /// <summary>Pins that a resolution-30 cell whose quintant is above 41 falls back to encoding at resolution 29, folding S accordingly.</summary>
        [TestMethod]
        public void ResolutionThirtyFallsBackToResolutionTwentyNineForQuintantAboveFortyOne()
        {
            // Origin 9 has quintants 45-49, all > 41.
            Origin origin = Origins.All[9];
            int segment = (0 + origin.FirstQuintant) % 5;

            ulong cell = Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30));
            Assert.AreEqual(29, Serialization.GetResolution(cell));

            ulong cell2 = Serialization.Serialize(new A5Cell(origin, segment, 7UL, 30));
            Assert.AreEqual(29, Serialization.GetResolution(cell2));
            Assert.AreEqual(1UL, Serialization.Deserialize(cell2).S); // 7 >> 2 = 1.
        }

        /// <summary>Pins that a resolution-30 cell whose quintant is entirely out of the origin's segment range falls back to resolution 29, preserving origin and folded S.</summary>
        [TestMethod]
        public void ResolutionThirtyFallsBackToResolutionTwentyNineForOutOfBoundsQuintant()
        {
            // Origin 11 has quintants 55-59, all > 41.
            Origin origin = Origins.All[11];
            int segment = (0 + origin.FirstQuintant) % 5;

            ulong cell = Serialization.Serialize(new A5Cell(origin, segment, 100UL, 30));
            Assert.AreEqual(29, Serialization.GetResolution(cell));
            Assert.AreEqual(25UL, Serialization.Deserialize(cell).S); // 100 >> 2 = 25.
            Assert.AreEqual(11, Serialization.Deserialize(cell).Origin.Id);
        }

        /// <summary>Pins that Serialize throws ArgumentOutOfRangeException with the expected message when S exceeds resolution 30's range.</summary>
        [TestMethod]
        public void SerializeThrowsForResolutionThirtyWhenSIsTooLarge()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                static () => Serialization.Serialize(new A5Cell(Origins.All[0], (0 + Origins.All[0].FirstQuintant) % 5, 1UL << 58, 30)));
            Assert.IsTrue(exception.Message.Contains("too large for resolution level 30", StringComparison.Ordinal));
        }

        /// <summary>Pins that the four resolution-30 children sharing an S group all resolve to the same resolution-29 parent.</summary>
        [TestMethod]
        public void CellToParentFromResolutionThirtyToTwentyNine()
        {
            Origin origin = Origins.All[0];
            int segment = (0 + origin.FirstQuintant) % 5;

            // Four children at res 30 (S=0..3) share the same res 29 parent (S=0).
            for(int i = 0; i < 4; i++)
            {
                ulong child = Serialization.Serialize(new A5Cell(origin, segment, (ulong)i, 30));
                ulong parent = Serialization.CellToParent(child);

                Assert.AreEqual(29, Serialization.GetResolution(parent));
                Assert.AreEqual(0UL, Serialization.Deserialize(parent).S);
            }
        }

        /// <summary>Pins that CellToChildren from resolution 29 to 30 produces exactly four children with S values 0 through 3.</summary>
        [TestMethod]
        public void CellToChildrenFromResolutionTwentyNineToThirty()
        {
            Origin origin = Origins.All[0];
            int segment = (0 + origin.FirstQuintant) % 5;
            ulong parent = Serialization.Serialize(new A5Cell(origin, segment, 0UL, 29));

            ulong[] children = Serialization.CellToChildren(parent, 30);

            Assert.HasCount(4, children);
            for(int i = 0; i < children.Length; i++)
            {
                Assert.AreEqual(30, Serialization.GetResolution(children[i]));
                Assert.AreEqual((ulong)i, Serialization.Deserialize(children[i]).S);
            }
        }

        /// <summary>Pins that every resolution-30 child produced by CellToChildren resolves back to its resolution-29 parent.</summary>
        [TestMethod]
        public void ResolutionThirtyChildrenRoundTripToParent()
        {
            Origin origin = Origins.All[0];
            int segment = (0 + origin.FirstQuintant) % 5;
            ulong parent = Serialization.Serialize(new A5Cell(origin, segment, 42UL, 29));

            ulong[] children = Serialization.CellToChildren(parent, 30);

            Assert.HasCount(4, children);
            foreach(ulong child in children)
            {
                Assert.AreEqual(parent, Serialization.CellToParent(child));
            }
        }

        /// <summary>Pins that GetStride returns 2 for resolution 30.</summary>
        [TestMethod]
        public void GetStrideReturnsTwoForResolutionThirty()
        {
            Assert.AreEqual(2UL, Serialization.GetStride(30));
        }

        /// <summary>Pins that IsFirstChild identifies the S=0 and S=4 resolution-30 cells as first children under the one-bit marker, and S=1 as not.</summary>
        [TestMethod]
        public void IsFirstChildForOneBitMarker()
        {
            Origin origin = Origins.All[0];
            int segment = (0 + origin.FirstQuintant) % 5;

            Assert.IsTrue(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30))));
            Assert.IsFalse(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 1UL, 30))));
            Assert.IsTrue(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 4UL, 30))));
        }

        /// <summary>Pins that resolution-30 cells with a five-bit marker round-trip for non-zero S values.</summary>
        [TestMethod]
        public void ResolutionThirtyRoundTripsWithNonZeroSForFiveBitMarker()
        {
            // Quintant 40 (origin 8, segmentN=0) uses the ...10000 encoding.
            Origin origin = Origins.All[8];
            int segment = (0 + origin.FirstQuintant) % 5;

            foreach(ulong s in new ulong[] { 0UL, 1UL, 42UL, (1UL << 58) - 1UL })
            {
                A5Cell cell = new(origin, segment, s, 30);
                ulong serialized = Serialization.Serialize(cell);
                Assert.AreEqual(0b10000UL, serialized & 0b11111UL);

                A5Cell deserialized = Serialization.Deserialize(serialized);
                Assert.AreEqual(s, deserialized.S);
                Assert.AreEqual(30, deserialized.Resolution);
                Assert.AreEqual(serialized, Serialization.Serialize(deserialized));
            }
        }

        /// <summary>Pins that IsFirstChild identifies the S=0 and S=4 resolution-30 cells as first children under the three-bit marker, and S=1 as not.</summary>
        [TestMethod]
        public void IsFirstChildForThreeBitMarker()
        {
            Origin origin = Origins.All[7]; // Quintant 35, uses ...100.
            int segment = (0 + origin.FirstQuintant) % 5;

            Assert.IsTrue(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30))));
            Assert.IsFalse(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 1UL, 30))));
            Assert.IsTrue(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 4UL, 30))));
        }

        /// <summary>Pins that IsFirstChild identifies the S=0 and S=4 resolution-30 cells as first children under the five-bit marker, and S=1 as not.</summary>
        [TestMethod]
        public void IsFirstChildForFiveBitMarker()
        {
            Origin origin = Origins.All[8]; // Quintant 40, uses ...10000.
            int segment = (0 + origin.FirstQuintant) % 5;

            Assert.IsTrue(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 0UL, 30))));
            Assert.IsFalse(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 1UL, 30))));
            Assert.IsTrue(Serialization.IsFirstChild(Serialization.Serialize(new A5Cell(origin, segment, 4UL, 30))));
        }

        /// <summary>Pins that resolution-30 children produced from a five-bit-marker parent carry the correct marker and resolve back to that parent.</summary>
        [TestMethod]
        public void ResolutionThirtyChildrenRoundTripForFiveBitMarker()
        {
            // Origin 8 (quintant 40) uses the ...10000 encoding.
            Origin origin = Origins.All[8];
            int segment = (0 + origin.FirstQuintant) % 5;
            ulong parent = Serialization.Serialize(new A5Cell(origin, segment, 10UL, 29));

            ulong[] children = Serialization.CellToChildren(parent, 30);

            Assert.HasCount(4, children);
            foreach(ulong child in children)
            {
                Assert.AreEqual(30, Serialization.GetResolution(child));
                Assert.AreEqual(0b10000UL, child & 0b11111UL);
                Assert.AreEqual(parent, Serialization.CellToParent(child));
            }
        }

        /// <summary>Pins that resolution-30 children produced from a three-bit-marker parent carry the correct marker and resolve back to that parent.</summary>
        [TestMethod]
        public void ResolutionThirtyChildrenRoundTripForThreeBitMarker()
        {
            // Origin 7 (quintant 35) uses the ...100 encoding.
            Origin origin = Origins.All[7];
            int segment = (0 + origin.FirstQuintant) % 5;
            ulong parent = Serialization.Serialize(new A5Cell(origin, segment, 10UL, 29));

            ulong[] children = Serialization.CellToChildren(parent, 30);

            Assert.HasCount(4, children);
            foreach(ulong child in children)
            {
                Assert.AreEqual(30, Serialization.GetResolution(child));
                Assert.AreEqual(0b100UL, child & 0b111UL);
                Assert.AreEqual(parent, Serialization.CellToParent(child));
            }
        }

        /// <summary>Pins that CellToChildren of a resolution-30 cell throws ArgumentOutOfRangeException with the expected message.</summary>
        [TestMethod]
        public void CellToChildrenOfResolutionThirtyThrows()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                static () => Serialization.CellToChildren(Serialization.Serialize(new A5Cell(Origins.All[0], (0 + Origins.All[0].FirstQuintant) % 5, 0UL, 30))));
            Assert.IsTrue(exception.Message.Contains("exceeds maximum resolution", StringComparison.Ordinal));
        }

        /// <summary>Pins that every fixture <c>res30Locations</c> cell round-trips through deserialize/serialize bit-for-bit.</summary>
        [TestMethod]
        public async Task ResolutionThirtyLocationsRoundTrip()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            foreach(JsonElement location in fixture.RootElement.GetProperty("res30Locations").EnumerateArray())
            {
                ulong cell = Hex.HexToU64(location.GetProperty("hex").GetString()!);
                A5Cell deserialized = Serialization.Deserialize(cell);
                ulong reserialized = Serialization.Serialize(deserialized);
                Assert.AreEqual(cell, reserialized);
            }
        }

        /// <summary>Pins that every fixture <c>res30Locations</c> row recorded at resolution 29 does in fact deserialize to resolution 29.</summary>
        [TestMethod]
        public async Task ResolutionThirtyLocationsOutOfBoundsQuintantsFallBackToTwentyNine()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int checkedCount = 0;
            foreach(JsonElement location in fixture.RootElement.GetProperty("res30Locations").EnumerateArray())
            {
                if(location.GetProperty("resolution").GetInt32() != 29)
                {
                    continue;
                }

                ulong cell = Hex.HexToU64(location.GetProperty("hex").GetString()!);
                Assert.AreEqual(29, Serialization.GetResolution(cell));
                checkedCount++;
            }

            Assert.IsGreaterThan(0, checkedCount);
        }

        /// <summary>Pins that every fixture <c>res30Locations</c> row recorded at resolution 30 does in fact deserialize to resolution 30.</summary>
        [TestMethod]
        public async Task ResolutionThirtyLocationsInBoundsQuintantsEncodeAtThirty()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int checkedCount = 0;
            foreach(JsonElement location in fixture.RootElement.GetProperty("res30Locations").EnumerateArray())
            {
                if(location.GetProperty("resolution").GetInt32() != 30)
                {
                    continue;
                }

                ulong cell = Hex.HexToU64(location.GetProperty("hex").GetString()!);
                Assert.AreEqual(30, Serialization.GetResolution(cell));
                checkedCount++;
            }

            Assert.IsGreaterThan(0, checkedCount);
        }

        /// <summary>Shared body for the resolution-30 non-zero-S round trip cases keyed by marker width.</summary>
        private static void AssertResolutionThirtyRoundTrips(Origin origin, int segment)
        {
            foreach(ulong s in new ulong[] { 0UL, 1UL, 42UL, (1UL << 58) - 1UL })
            {
                A5Cell cell = new(origin, segment, s, 30);
                ulong serialized = Serialization.Serialize(cell);
                A5Cell deserialized = Serialization.Deserialize(serialized);

                Assert.AreEqual(s, deserialized.S);
                Assert.AreEqual(30, deserialized.Resolution);
                Assert.AreEqual(serialized, Serialization.Serialize(deserialized));
            }
        }

        /// <summary>Formats a <see cref="ulong"/> as a zero-padded 64-character binary string, matching the fixture's mask format.</summary>
        private static string ToBinary64(ulong value)
        {
            return Convert.ToString(unchecked((long)value), 2).PadLeft(64, '0');
        }

        /// <summary>Parses the fixture's 237 <c>testIds</c> hex strings into raw cell ids.</summary>
        private static async Task<ulong[]> LoadTestIdsAsync(CancellationToken cancellationToken)
        {
            using JsonDocument fixture = await LoadFixtureAsync(cancellationToken).ConfigureAwait(false);
            JsonElement testIds = fixture.RootElement.GetProperty("testIds");
            ulong[] result = new ulong[testIds.GetArrayLength()];

            int index = 0;
            foreach(JsonElement idElement in testIds.EnumerateArray())
            {
                result[index] = Hex.HexToU64(idElement.GetString()!);
                index++;
            }

            return result;
        }

        /// <summary>Loads <c>fixtures/serialization.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/serialization.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
