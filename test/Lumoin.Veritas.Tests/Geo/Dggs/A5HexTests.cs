using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Hex round-trip cases: minimal lowercase output, throwing behavior for empty/non-hex/whitespace
    /// input, and the deliberate deviation for input longer than 16 hex digits.
    /// </summary>
    [TestClass]
    internal sealed class A5HexTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that hex-to-U64 conversion matches the fixed set of hand-picked hex strings.</summary>
        [TestMethod]
        public void HexToU64ConvertsPinnedCases()
        {
            Assert.AreEqual(1715004UL, Hex.HexToU64("1a2b3c"));
            Assert.AreEqual(0UL, Hex.HexToU64("0"));
            Assert.AreEqual(255UL, Hex.HexToU64("ff"));
            Assert.AreEqual(4294967295UL, Hex.HexToU64("ffffffff"));
        }

        /// <summary>Pins that U64-to-hex conversion matches the fixed set of hand-picked ulong values, formatted minimally and lowercase.</summary>
        [TestMethod]
        public void U64ToHexConvertsPinnedCases()
        {
            Assert.AreEqual("1a2b3c", Hex.U64ToHex(1715004UL));
            Assert.AreEqual("0", Hex.U64ToHex(0UL));
            Assert.AreEqual("ff", Hex.U64ToHex(255UL));
            Assert.AreEqual("ffffffff", Hex.U64ToHex(4294967295UL));
        }

        /// <summary>Pins that hex-to-U64-to-hex round-trips each pinned string back to itself.</summary>
        [TestMethod]
        public void RoundTripPreservesPinnedCases()
        {
            string[] testValues = ["1a2b3c", "0", "ff", "ffffffff"];
            foreach(string hexValue in testValues)
            {
                Assert.AreEqual(hexValue, Hex.U64ToHex(Hex.HexToU64(hexValue)));
            }
        }

        /// <summary>Pins that <see cref="ulong.MaxValue"/> round-trips through sixteen hex digits in both directions.</summary>
        [TestMethod]
        public void FullWidthValueRoundTrips()
        {
            Assert.AreEqual("ffffffffffffffff", Hex.U64ToHex(ulong.MaxValue));
            Assert.AreEqual(ulong.MaxValue, Hex.HexToU64("ffffffffffffffff"));
        }

        /// <summary>Pins that uppercase hex digits parse identically to their lowercase equivalents.</summary>
        [TestMethod]
        public void UppercaseInputIsAccepted()
        {
            Assert.AreEqual(255UL, Hex.HexToU64("FF"));
        }

        /// <summary>Pins that empty, non-hex, and whitespace-padded input all throw <see cref="FormatException"/>.</summary>
        [TestMethod]
        public void MalformedInputThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => Hex.HexToU64(string.Empty));
            Assert.Throws<FormatException>(() => Hex.HexToU64("zz"));
            Assert.Throws<FormatException>(() => Hex.HexToU64(" ff"));
            Assert.Throws<FormatException>(() => Hex.HexToU64("ff "));
        }

        /// <summary>Pins that seventeen or more hex digits throw <see cref="OverflowException"/> as the documented deviation.</summary>
        [TestMethod]
        public void SeventeenHexDigitsThrowOverflowAsDocumentedDeviation()
        {
            Assert.Throws<OverflowException>(() => Hex.HexToU64("1ffffffffffffffff"));
        }
    }
}
