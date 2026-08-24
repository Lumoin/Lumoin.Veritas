using System;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// A dimensionally extended nine-intersection matrix: the dimension of the
/// pairwise intersections of the interior, boundary, and exterior point sets
/// of two geometries, row-major with the first operand's parts as rows. Each
/// cell carries an intersection dimension — −1 for the empty intersection
/// (serialized <c>F</c>), otherwise 0, 1, or 2 — never a boolean: exact-digit
/// pattern cells and the line/line crosses branch need the digits themselves.
/// </summary>
/// <param name="InteriorInterior">Dimension of interior ∩ interior.</param>
/// <param name="InteriorBoundary">Dimension of interior ∩ boundary.</param>
/// <param name="InteriorExterior">Dimension of interior ∩ exterior.</param>
/// <param name="BoundaryInterior">Dimension of boundary ∩ interior.</param>
/// <param name="BoundaryBoundary">Dimension of boundary ∩ boundary.</param>
/// <param name="BoundaryExterior">Dimension of boundary ∩ exterior.</param>
/// <param name="ExteriorInterior">Dimension of exterior ∩ interior.</param>
/// <param name="ExteriorBoundary">Dimension of exterior ∩ boundary.</param>
/// <param name="ExteriorExterior">Dimension of exterior ∩ exterior.</param>
public readonly record struct IntersectionMatrix(
    int InteriorInterior,
    int InteriorBoundary,
    int InteriorExterior,
    int BoundaryInterior,
    int BoundaryBoundary,
    int BoundaryExterior,
    int ExteriorInterior,
    int ExteriorBoundary,
    int ExteriorExterior)
{
    /// <summary>The cell value of an empty intersection, serialized <c>F</c>.</summary>
    public const int Empty = -1;

    /// <summary>
    /// The matrix with the operand roles swapped: <c>relate(b, a)</c> is the
    /// transpose of <c>relate(a, b)</c> — the off-diagonal cells swap, the
    /// diagonal stands.
    /// </summary>
    public IntersectionMatrix Transpose()
    {
        return new IntersectionMatrix(
            InteriorInterior,
            BoundaryInterior,
            ExteriorInterior,
            InteriorBoundary,
            BoundaryBoundary,
            ExteriorBoundary,
            InteriorExterior,
            BoundaryExterior,
            ExteriorExterior);
    }

    /// <summary>
    /// Whether this matrix satisfies a nine-character intersection pattern
    /// over the alphabet <c>T</c> (any of 0/1/2), <c>F</c> (empty), <c>*</c>
    /// (either), and the exact digits <c>0</c>/<c>1</c>/<c>2</c> —
    /// case-sensitive, row-major. A pattern outside that closed form is a
    /// caller contract violation and throws; the validated non-throwing seam
    /// is <see cref="GeometryRelate.TryRelate(in FlatGeometry, in FlatGeometry, ReadOnlySpan{char}, out bool)"/>.
    /// </summary>
    public bool Matches(ReadOnlySpan<char> pattern)
    {
        if(pattern.Length != 9)
        {
            throw new ArgumentException("An intersection pattern is exactly nine characters.", nameof(pattern));
        }

        Span<int> cells = stackalloc int[9];
        CopyCells(cells);

        for(int index = 0; index < 9; index++)
        {
            if(!CellMatches(cells[index], pattern[index], nameof(pattern)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The UTF-8 overload of <see cref="Matches(ReadOnlySpan{char})"/> over
    /// the same case-sensitive nine-symbol alphabet.
    /// </summary>
    public bool Matches(ReadOnlySpan<byte> pattern)
    {
        if(pattern.Length != 9)
        {
            throw new ArgumentException("An intersection pattern is exactly nine characters.", nameof(pattern));
        }

        Span<int> cells = stackalloc int[9];
        CopyCells(cells);

        for(int index = 0; index < 9; index++)
        {
            if(!CellMatches(cells[index], (char)pattern[index], nameof(pattern)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The canonical nine-character serialization over <c>F</c>, <c>0</c>,
    /// <c>1</c>, <c>2</c> — a computed matrix never contains pattern symbols.
    /// </summary>
    public override string ToString()
    {
        Span<char> text = stackalloc char[9];
        Span<int> cells = stackalloc int[9];
        CopyCells(cells);

        for(int index = 0; index < 9; index++)
        {
            text[index] = SymbolOf(cells[index]);
        }

        return new string(text);
    }

    /// <summary>
    /// Writes the canonical nine-byte UTF-8 serialization into
    /// <paramref name="destination"/>, which must hold at least nine bytes.
    /// Any literal built over the written buffer must own it exclusively for
    /// the literal's lifetime or copy it out.
    /// </summary>
    public void WriteUtf8(Span<byte> destination)
    {
        Span<int> cells = stackalloc int[9];
        CopyCells(cells);

        for(int index = 0; index < 9; index++)
        {
            destination[index] = (byte)SymbolOf(cells[index]);
        }
    }

    /// <summary>Copies the nine cells row-major into <paramref name="cells"/>.</summary>
    private void CopyCells(Span<int> cells)
    {
        cells[0] = InteriorInterior;
        cells[1] = InteriorBoundary;
        cells[2] = InteriorExterior;
        cells[3] = BoundaryInterior;
        cells[4] = BoundaryBoundary;
        cells[5] = BoundaryExterior;
        cells[6] = ExteriorInterior;
        cells[7] = ExteriorBoundary;
        cells[8] = ExteriorExterior;
    }

    /// <summary>
    /// The one-cell pattern test; throws for a symbol outside the closed
    /// alphabet so a malformed pattern never reads as a mismatch.
    /// </summary>
    private static bool CellMatches(int cell, char symbol, string parameterName)
    {
        switch(symbol)
        {
            case '*':
                return true;
            case 'T':
                return cell >= 0;
            case 'F':
                return cell == Empty;
            case '0':
            case '1':
            case '2':
                return cell == symbol - '0';
            default:
                throw new ArgumentException($"'{symbol}' is not an intersection-pattern symbol.", parameterName);
        }
    }

    /// <summary>The serialization symbol of one cell dimension.</summary>
    private static char SymbolOf(int cell)
    {
        return cell switch
        {
            Empty => 'F',
            0 => '0',
            1 => '1',
            _ => '2',
        };
    }
}
