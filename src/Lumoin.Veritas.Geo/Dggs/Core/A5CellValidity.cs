namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Non-throwing semantic validity of a cell id: whether the id decodes to a valid origin/segment
/// pair. Mirrors exactly the range checks <see cref="Serialization.Deserialize"/> enforces by
/// exception, so value-based callers can refuse an undecodable id without reaching a throwing path.
/// </summary>
internal static class A5CellValidity
{
    /// <summary>Answers whether <paramref name="index"/> decodes to a valid origin/segment pair.</summary>
    /// <param name="index">The candidate cell id.</param>
    /// <returns><see langword="true"/> when the id is decodable, including the world cell.</returns>
    public static bool IsDecodable(ulong index)
    {
        int resolution = Serialization.GetResolution(index);
        if(resolution == -1)
        {
            return true;
        }

        int quintantShift = Serialization.HilbertStartBit;
        int quintantOffset = 0;
        if(resolution == Serialization.MaxResolution)
        {
            int markerBits = (index & 1UL) != 0UL ? 1 : (index & 0b100UL) != 0UL ? 3 : 5;
            quintantShift = Serialization.HilbertStartBit + markerBits;
            quintantOffset = markerBits == 1 ? 0 : markerBits == 3 ? 32 : 40;
        }

        int topBits = (int)(index >> quintantShift) + quintantOffset;
        if(resolution == 0)
        {
            return topBits >= 0 && topBits < Origins.All.Length;
        }

        int originId = topBits / 5;

        return originId >= 0 && originId < Origins.All.Length;
    }
}
