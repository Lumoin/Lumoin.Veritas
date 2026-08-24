using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The matrix mechanics of the relate engine: cell order, transpose identity,
/// pattern-symbol semantics, serialization parity, and the raise-only
/// discipline's publicly observable fixture.
/// </summary>
[TestClass]
internal sealed class IntersectionMatrixTests
{
    /// <summary>Cells serialize row-major with the interior row first.</summary>
    [TestMethod]
    public void CellsSerializeRowMajorWithTheInteriorRowFirst()
    {
        var matrix = new IntersectionMatrix(2, 1, 0, -1, 2, 1, 0, -1, 2);

        Assert.AreEqual("210F210F2", matrix.ToString(), "Cells serialize row-major II IB IE BI BB BE EI EB EE.");
    }

    /// <summary>Transpose swaps the off-diagonal cells and stands on the diagonal.</summary>
    [TestMethod]
    public void TransposeSwapsTheOffDiagonalCells()
    {
        var matrix = new IntersectionMatrix(0, 1, 2, -1, 0, 1, 2, -1, 0);
        IntersectionMatrix transposed = matrix.Transpose();

        Assert.AreEqual(matrix.InteriorBoundary, transposed.BoundaryInterior, "IB and BI swap under transpose.");
        Assert.AreEqual(matrix.InteriorExterior, transposed.ExteriorInterior, "IE and EI swap under transpose.");
        Assert.AreEqual(matrix.BoundaryExterior, transposed.ExteriorBoundary, "BE and EB swap under transpose.");
        Assert.AreEqual(matrix.InteriorInterior, transposed.InteriorInterior, "The diagonal stands under transpose.");
        Assert.AreEqual(matrix, transposed.Transpose(), "Transposing twice restores the matrix.");
    }

    /// <summary>Pattern symbols answer the documented semantics against the empty-to-empty matrix.</summary>
    /// <param name="pattern">The nine-symbol pattern under test.</param>
    /// <param name="expected">Whether the pattern matches.</param>
    [TestMethod]
    [DataRow("*********", true)]
    [DataRow("TFFFTFFFT", false)]
    [DataRow("FFFFFFFF2", true)]
    [DataRow("FFFFFFFFT", true)]
    [DataRow("FFFFFFFF1", false)]
    public void PatternSymbolsMatchTheEmptyToEmptyMatrix(string pattern, bool expected)
    {
        var matrix = new IntersectionMatrix(-1, -1, -1, -1, -1, -1, -1, -1, 2);

        Assert.AreEqual(expected, matrix.Matches(pattern), $"'{pattern}' against FFFFFFFF2.");
    }

    /// <summary>An exact digit matches only its own dimension; T matches any non-empty one.</summary>
    [TestMethod]
    public void ExactDigitPatternRejectsALargerDimension()
    {
        var matrix = new IntersectionMatrix(2, -1, -1, -1, -1, -1, -1, -1, 2);

        Assert.IsFalse(matrix.Matches("0********"), "An exact digit never matches a larger dimension.");
        Assert.IsTrue(matrix.Matches("T********"), "T matches any non-empty dimension.");
        Assert.IsTrue(matrix.Matches("2********"), "The exact digit matches its own dimension.");
    }

    /// <summary>A malformed pattern symbol throws instead of answering a mismatch.</summary>
    [TestMethod]
    public void MalformedPatternSymbolThrowsInsteadOfMismatching()
    {
        var matrix = new IntersectionMatrix(-1, -1, -1, -1, -1, -1, -1, -1, 2);

        Assert.Throws<ArgumentException>(() => matrix.Matches("t********"), "A lowercase symbol is a caller contract violation.");
        Assert.Throws<ArgumentException>(() => matrix.Matches("TFFFTFFF"), "A short pattern is a caller contract violation.");
    }

    /// <summary>The UTF-8 write mirrors the string serialization byte for byte.</summary>
    [TestMethod]
    public void Utf8WriteAgreesWithTheStringSerialization()
    {
        var matrix = new IntersectionMatrix(2, -1, 1, 0, -1, 2, 1, 0, 2);
        Span<byte> utf8 = stackalloc byte[9];
        matrix.WriteUtf8(utf8);
        string text = matrix.ToString();

        for(int index = 0; index < 9; index++)
        {
            Assert.AreEqual((byte)text[index], utf8[index], "UTF-8 bytes mirror the string serialization.");
        }
    }

    /// <summary>The UTF-8 pattern overload answers identically to the character overload.</summary>
    [TestMethod]
    public void Utf8PatternOverloadAgreesWithTheCharacterOverload()
    {
        var matrix = new IntersectionMatrix(2, -1, -1, -1, 1, -1, -1, -1, 2);

        Assert.AreEqual(
            matrix.Matches("TFFFTFFFT"),
            matrix.Matches("TFFFTFFFT"u8),
            "Both pattern overloads answer identically.");
    }

    /// <summary>A raised boundary cell never regresses when a lower-dimension touch follows.</summary>
    [TestMethod]
    public void SharedRunAndIsolatedTouchKeepTheRaisedBoundaryCell()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))", out FlatGeometry first, out _),
            "The square must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((2 0, 4 0, 4 4, 0 4, 0 2, 2 2, 2 0))", out FlatGeometry second, out _),
            "The L-shape must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(first, second, out IntersectionMatrix matrix), "The pair must relate.");

        Assert.AreEqual(1, matrix.BoundaryBoundary, "A shared run raises BB to 1 and the isolated vertex touch never regresses it.");
    }
}
