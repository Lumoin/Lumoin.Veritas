using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Bit-exact encode/decode of a 64-bit cell id: the resolution is encoded by the position of the least
/// significant set bit, with the quintant (origin/segment) packed into the top bits and the Hilbert
/// curve position <c>S</c> packed between them. Every internal cell id is a raw <see cref="ulong"/>;
/// <see cref="Core.A5CellId"/> is the public wrapper built on top of this class at the facade layer only.
/// </summary>
internal static class Serialization
{
    /// <summary>
    /// The first resolution whose position within a face is encoded via the Hilbert curve rather than a
    /// plain quintant/segment index; resolutions 0 and 1 use a flat (non-Hilbert) encoding.
    /// </summary>
    public const int FirstHilbertResolution = 2;

    /// <summary>The finest resolution the encoding supports.</summary>
    public const int MaxResolution = 30;

    /// <summary>
    /// The abstract cell that contains the whole world: resolution −1, with the twelve resolution-0
    /// cells as its children.
    /// </summary>
    public const ulong WorldCell = 0UL;

    /// <summary>Bit position where the quintant/origin field starts for every resolution except 30 (64 − 6 bits).</summary>
    internal const int HilbertStartBit = 58;

    /// <summary>
    /// Resolution encoded by <paramref name="index"/>: −1 for the world cell, otherwise the position of
    /// the least significant set bit (adjusted for the three resolution-30 marker widths below).
    /// </summary>
    public static int GetResolution(ulong index)
    {
        if(index == 0UL)
        {
            return -1;
        }

        // Resolution 30 uses three encoding patterns: ...1 -> 5-bit quintant (0-31), 58-bit S; ...100
        // -> 3-bit quintant (32-39), 58-bit S; ...10000 -> 1-bit quintant (40-41), 58-bit S.
        if((index & 1UL) != 0UL || (index & 0b111UL) == 0b100UL || (index & 0b11111UL) == 0b10000UL)
        {
            return MaxResolution;
        }

        int resolution = MaxResolution - 1;
        ulong shifted = index >> 1;
        if(shifted == 0UL)
        {
            return -1;
        }

        // Fast path: split into 32-bit chunks and scan for the trailing-zero position. Every branch
        // below is gated purely on a bitwise AND against already-shifted-out low bits, and every shift
        // amount here is a fixed positive literal (1, 2, 4, 8, 16, or 32) — never a computed or negative
        // count — so an ordinary unsigned uint/ulong right shift (always logical in C#) is well-defined
        // regardless of any higher, never-inspected bits.
        uint low32 = (uint)(shifted & 0xffffffffUL);
        uint remaining;

        if(low32 == 0)
        {
            shifted >>= 32;
            resolution -= 16;
            remaining = (uint)shifted;
        }
        else
        {
            remaining = low32;
        }

        if((remaining & 0xffff) == 0)
        {
            remaining >>= 16;
            resolution -= 8;
        }

        if(resolution >= 6 && (remaining & 0xff) == 0)
        {
            remaining >>= 8;
            resolution -= 4;
        }

        if(resolution >= 4 && (remaining & 0xf) == 0)
        {
            remaining >>= 4;
            resolution -= 2;
        }

        while(resolution > -1 && (remaining & 0b1) == 0)
        {
            resolution -= 1;

            // For non-Hilbert resolutions the marker moves by 1 bit per resolution level; for Hilbert
            // resolutions it moves by 2 bits per level.
            remaining >>= resolution < FirstHilbertResolution ? 1 : 2;
        }

        return resolution;
    }

    /// <summary>Decodes <paramref name="index"/> into its origin, segment, Hilbert position and resolution.</summary>
    public static A5Cell Deserialize(ulong index)
    {
        int resolution = GetResolution(index);

        // Technically not a resolution, but useful to think of as an abstract cell containing the whole world.
        if(resolution == -1)
        {
            return new A5Cell(Origins.All[0], 0, 0UL, resolution);
        }

        // For res 30, quintant bits are fewer to make room for S:
        //   ...1     marker (1 bit)  -> 5-bit quintant (0-31)
        //   ...100   marker (3 bits) -> 3-bit quintant + 32 (32-39)
        //   ...10000 marker (5 bits) -> 1-bit quintant + 40 (40-41)
        int quintantShift = HilbertStartBit;
        int quintantOffset = 0;
        if(resolution == MaxResolution)
        {
            int markerBits = (index & 1UL) != 0UL ? 1 : (index & 0b100UL) != 0UL ? 3 : 5;
            quintantShift = HilbertStartBit + markerBits;
            quintantOffset = markerBits == 1 ? 0 : markerBits == 3 ? 32 : 40;
        }

        // Extract origin*segment from the top bits.
        Debug.Assert(quintantShift is >= 0 and <= 63, "Quintant shift must be a valid ulong shift count.");
        int topBits = (int)(index >> quintantShift) + quintantOffset;

        Origin origin;
        int segment;

        if(resolution == 0)
        {
            if(topBits < 0 || topBits >= Origins.All.Length)
            {
                throw new ArgumentException($"Could not parse origin: {topBits}", nameof(index));
            }

            origin = Origins.All[topBits];
            segment = 0;
        }
        else
        {
            int originId = topBits / 5;
            if(originId < 0 || originId >= Origins.All.Length)
            {
                throw new ArgumentException($"Could not parse origin: {topBits}", nameof(index));
            }

            origin = Origins.All[originId];
            segment = (topBits + origin.FirstQuintant) % 5;
        }

        if(resolution < FirstHilbertResolution)
        {
            return new A5Cell(origin, segment, 0UL, resolution);
        }

        // Mask away origin & segment and shift away resolution and marker bits.
        int hilbertLevels = resolution - FirstHilbertResolution + 1;
        int hilbertBits = 2 * hilbertLevels;
        ulong removalMask = (1UL << quintantShift) - 1UL;
        int sShift = quintantShift - hilbertBits;
        Debug.Assert(sShift >= 0, "S extraction shift must be non-negative.");
        ulong s = (index & removalMask) >> sShift;

        return new A5Cell(origin, segment, s, resolution);
    }

    /// <summary>Encodes a decoded cell back into its 64-bit id.</summary>
    public static ulong Serialize(A5Cell cell)
    {
        (Origin origin, int segment, ulong s, int resolution) = cell;
        if(resolution > MaxResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(cell), resolution, $"Resolution ({resolution}) is too large");
        }

        if(resolution == -1)
        {
            return WorldCell;
        }

        // A resolution-30 quintant of 42 or above has no room in any of the three res-30 marker
        // regimes and re-encodes at resolution 29 with S truncated by 2 bits; deciding the demotion
        // up front keeps the encoder free of any re-entry.
        if(resolution == MaxResolution && (5 * origin.Id) + (((segment - origin.FirstQuintant) + 5) % 5) >= 42)
        {
            s >>= 2;
            resolution = MaxResolution - 1;
        }

        // For res 30, quintant bits are fewer to make room for S:
        //   quintant 0-31:  ...1     marker -> 5-bit quintant
        //   quintant 32-39: ...100   marker -> 3-bit quintant + 32
        //   quintant 40-41: ...10000 marker -> 1-bit quintant + 40
        int quintantShift = HilbertStartBit;

        int markerPosition;
        if(resolution < FirstHilbertResolution)
        {
            markerPosition = resolution + 1;
        }
        else
        {
            int hilbertResolution = 1 + resolution - FirstHilbertResolution;
            markerPosition = (2 * hilbertResolution) + 1;
        }

        // Top bits encode the origin id and segment.
        int segmentN = ((segment - origin.FirstQuintant) + 5) % 5;

        ulong index;
        if(resolution == 0)
        {
            index = (ulong)origin.Id << quintantShift;
        }
        else
        {
            int quintant = (5 * origin.Id) + segmentN;
            if(resolution == MaxResolution)
            {
                int quintantValue;
                if(quintant <= 31)
                {
                    quintantShift = HilbertStartBit + 1;
                    quintantValue = quintant;
                }
                else if(quintant <= 39)
                {
                    quintantShift = HilbertStartBit + 3;
                    quintantValue = quintant - 32;
                }
                else if(quintant <= 41)
                {
                    quintantShift = HilbertStartBit + 5;
                    quintantValue = quintant - 40;
                }
                else
                {
                    // Structurally unreachable: quintants 42 and above were demoted to resolution 29
                    // before encoding began.
                    throw new ArgumentOutOfRangeException(nameof(cell), quintant, "Resolution-30 quintants 42+ demote before encoding.");
                }

                index = (ulong)quintantValue << quintantShift;
            }
            else
            {
                index = (ulong)quintant << quintantShift;
            }
        }

        if(resolution >= FirstHilbertResolution)
        {
            int hilbertLevels = resolution - FirstHilbertResolution + 1;
            int hilbertBits = 2 * hilbertLevels;
            Debug.Assert(hilbertBits is > 0 and < 64, "Hilbert bit width must be a valid ulong shift count.");
            if(s >= 1UL << hilbertBits)
            {
                throw new ArgumentOutOfRangeException(nameof(cell), s, $"S ({s}) is too large for resolution level {resolution}");
            }

            int sShift = quintantShift - hilbertBits;
            Debug.Assert(sShift >= 0, "S placement shift must be non-negative.");
            checked
            {
                index += s << sShift;
            }
        }

        // Resolution is encoded by the position of the least significant 1.
        int markerShift = quintantShift - markerPosition;
        Debug.Assert(markerShift >= 0, "Marker shift must be non-negative for every (resolution, quintant) combination reachable here.");
        index |= 1UL << markerShift;

        return index;
    }

    /// <summary>Expands <paramref name="index"/> to every descendant at <paramref name="childResolution"/> (default: one level down).</summary>
    public static ulong[] CellToChildren(ulong index, int? childResolution = null)
    {
        A5Cell cell = Deserialize(index);
        int currentResolution = cell.Resolution;
        int newResolution = childResolution ?? currentResolution + 1;

        if(newResolution < currentResolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childResolution),
                newResolution,
                $"Target resolution ({newResolution}) must be equal to or greater than current resolution ({currentResolution})");
        }

        if(newResolution > MaxResolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childResolution),
                newResolution,
                $"Target resolution ({newResolution}) exceeds maximum resolution ({MaxResolution})");
        }

        if(newResolution == currentResolution)
        {
            return [index];
        }

        Origin[] newOrigins = currentResolution == -1 ? Origins.All : [cell.Origin];
        int[] newSegments = (currentResolution == -1 && newResolution > 0) || currentResolution == 0
            ? [0, 1, 2, 3, 4]
            : [cell.Segment];

        int resolutionDiff = newResolution - Math.Max(currentResolution, FirstHilbertResolution - 1);

        // resolutionDiff is negative only for the world-cell-to-resolution-0 step (currentResolution ==
        // -1, newResolution == 0, giving -1). Resolution 0 never encodes S in Serialize above, and
        // Deserialize always returns S == 0 when currentResolution == -1, so "exactly one child per
        // (origin, segment) pair, with S == 0" is the exact net effect — reproduced explicitly here
        // without ever evaluating a negative shift count.
        ulong childrenCount = resolutionDiff < 0 ? 1UL : 1UL << (2 * resolutionDiff);
        ulong shiftedS = resolutionDiff < 0 ? 0UL : cell.S << (2 * resolutionDiff);

        int totalCount = checked(newOrigins.Length * newSegments.Length * (int)childrenCount);
        ulong[] children = new ulong[totalCount];
        int writeIndex = 0;
        foreach(Origin newOrigin in newOrigins)
        {
            foreach(int newSegment in newSegments)
            {
                for(ulong i = 0; i < childrenCount; i++)
                {
                    ulong newS = shiftedS + i;
                    children[writeIndex] = Serialize(new A5Cell(newOrigin, newSegment, newS, newResolution));
                    writeIndex++;
                }
            }
        }

        return children;
    }

    /// <summary>
    /// Walks <paramref name="index"/> up the hierarchy to <paramref name="parentResolution"/> (default:
    /// one level up). Implemented as pure bit operations over the encoded index — no
    /// deserialize/serialize round trip — since the three encoding regimes (non-Hilbert res 0/1, Hilbert
    /// res 2-29, variable-width res 30) all reduce to the same shape after normalizing a res-30 cell.
    /// </summary>
    public static ulong CellToParent(ulong index, int? parentResolution = null)
    {
        int targetResolution = parentResolution ?? GetResolution(index) - 1;

        if(targetResolution == -1)
        {
            return WorldCell;
        }

        if(targetResolution < -1 || targetResolution > MaxResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(parentResolution), targetResolution, $"Target resolution ({targetResolution}) is out of range");
        }

        if(index == WorldCell)
        {
            throw new ArgumentException($"Target resolution ({targetResolution}) must be equal to or less than current resolution (-1)", nameof(index));
        }

        // Normalize res-30 children to the standard res-29 layout; the fast paths below then treat the
        // cell as a Hilbert-range cell.
        ulong c = index;
        if(IsMaxResolutionMarker(index))
        {
            if(targetResolution == MaxResolution)
            {
                return index; // Identity: already at resolution 30.
            }

            c = NormalizeMaxResolutionToPrevious(index);
            if(targetResolution == MaxResolution - 1)
            {
                return c;
            }
        }

        if(targetResolution >= FirstHilbertResolution)
        {
            // At targetResolution == MaxResolution (30) reached with a cell that was NOT already at
            // resolution 30 (the IsMaxResolutionMarker branch above was skipped), the marker shift below
            // is `59 - 2*30 = -1`: the OR must contribute nothing and the cell must be returned
            // unchanged. Reproduced explicitly below rather than ever evaluating a negative ulong shift
            // count.
            int keepShift = 60 - (2 * targetResolution);
            Debug.Assert(keepShift >= 0, "keepShift is 60 - 2*targetResolution, non-negative for targetResolution <= 30.");
            int markerShift = 59 - (2 * targetResolution);
            if(markerShift < 0)
            {
                return c;
            }

            return ((c >> keepShift) << keepShift) | (1UL << markerShift);
        }

        if(targetResolution == 1)
        {
            // Top 6 bits already encode 5*originId + segmentN; only the marker moves. Identity (cell
            // already at res 1) is preserved.
            return ((c >> 58) << 58) | (1UL << 56);
        }

        // targetResolution === 0: top 6 bits change from quintant (0-59) to originId (0-11). Identity
        // (cell already at res 0) needs an explicit guard since dividing an originId by 5 would corrupt
        // it — a res-0 cell has bit 57 set with all lower bits zero.
        if((c & ((1UL << 57) - 1UL)) == 0UL)
        {
            return c;
        }

        return (((c >> 58) / 5UL) << 58) | (1UL << 57);
    }

    /// <summary>Returns the twelve resolution-0 cells, the starting point for all higher-resolution subdivisions.</summary>
    public static ulong[] GetResolutionZeroCells()
    {
        return CellToChildren(WorldCell, 0);
    }

    /// <summary>Whether <paramref name="index"/> is the first child of its parent at <paramref name="resolution"/> (default: the cell's own resolution).</summary>
    public static bool IsFirstChild(ulong index, int? resolution = null)
    {
        int effectiveResolution = resolution ?? GetResolution(index);

        if(effectiveResolution < FirstHilbertResolution)
        {
            // For resolution 0: first child is origin 0 (child count 12).
            // For resolution 1 (and the world cell, resolution -1): first children sit at multiples of 5 (child count 5).
            int top6Bits = (int)(index >> HilbertStartBit);
            int childCount = effectiveResolution == 0 ? 12 : 5;

            return top6Bits % childCount == 0;
        }

        if(effectiveResolution == MaxResolution)
        {
            // S's 2 least significant bits sit just above the marker bits.
            int markerBits = (index & 1UL) != 0UL ? 1 : (index & 0b100UL) != 0UL ? 3 : 5;

            return (index & (3UL << markerBits)) == 0UL;
        }

        int sPosition = 2 * (MaxResolution - effectiveResolution);
        Debug.Assert(sPosition is >= 0 and <= 63, "sPosition must be a valid ulong shift count.");
        ulong sMask = 3UL << sPosition; // Mask for the 2 least significant bits of S.

        return (index & sMask) == 0UL;
    }

    /// <summary>
    /// Bit-level descendant test: is <paramref name="child"/> the same cell as <paramref name="parent"/>,
    /// or one of its descendants at any deeper resolution? Restricted to the Hilbert range —
    /// <paramref name="parentResolution"/> must be in [<see cref="FirstHilbertResolution"/>,
    /// <see cref="MaxResolution"/>), and <paramref name="child"/> must not be a resolution-30 cell
    /// (whose encoding uses a variable quintant shift). Callers handling those cases fall back to
    /// <see cref="CellToParent"/> equality.
    /// </summary>
    public static bool IsChildOf(ulong child, ulong parent, int parentResolution)
    {
        Debug.Assert(
            parentResolution is >= FirstHilbertResolution and < MaxResolution,
            "IsChildOf is restricted to the Hilbert range [FirstHilbertResolution, MaxResolution).");

        // Parent's identifying bits occupy positions 63..(60-2P): 6 quintant bits + 2(P-1) Hilbert bits.
        // Bit (59-2P) is the marker, below that is zero. Shifting both right by (60-2P) keeps exactly
        // those identifying bits and discards the marker, so a descendant matches iff the high bits match.
        int shift = 60 - (2 * parentResolution);

        return (child >> shift) == (parent >> shift);
    }

    /// <summary>Difference between two neighboring sibling cells at <paramref name="resolution"/>.</summary>
    public static ulong GetStride(int resolution)
    {
        // Both level 0 & 1 just write values 0-11 or 0-59 to the first 6 bits.
        if(resolution < FirstHilbertResolution)
        {
            return 1UL << HilbertStartBit;
        }

        // For res 30, S is shifted left by 1 (marker bit at position 0).
        if(resolution == MaxResolution)
        {
            return 2UL;
        }

        // For Hilbert levels, the position shifts by 2 bits per resolution level.
        int sPosition = 2 * (MaxResolution - resolution);
        Debug.Assert(sPosition is >= 0 and <= 63, "sPosition must be a valid ulong shift count.");

        return 1UL << sPosition;
    }

    /// <summary>
    /// Cheap predicate mirroring the first three checks in <see cref="GetResolution"/>: resolution-30
    /// cells are exactly those whose low bits match one of the three variable-width quintant marker patterns.
    /// </summary>
    private static bool IsMaxResolutionMarker(ulong index)
    {
        return (index & 1UL) != 0UL || (index & 0b111UL) == 0b100UL || (index & 0b11111UL) == 0b10000UL;
    }

    /// <summary>
    /// Re-packs a resolution-30 cell into the standard resolution-29 bit layout (6-bit quintant in bits
    /// 63-58, 56-bit S in bits 57-2, marker at bit 1). The 58-bit resolution-30 S is truncated by 2 bits,
    /// exactly as <see cref="CellToParent"/> targeting resolution 29 would.
    /// </summary>
    private static ulong NormalizeMaxResolutionToPrevious(ulong index)
    {
        int quintantShift;
        int quintantOffset;
        int markerBits;

        if((index & 1UL) != 0UL)
        {
            quintantShift = 59;
            quintantOffset = 0;
            markerBits = 1;
        }
        else if((index & 0b100UL) != 0UL)
        {
            quintantShift = 61;
            quintantOffset = 32;
            markerBits = 3;
        }
        else
        {
            quintantShift = 63;
            quintantOffset = 40;
            markerBits = 5;
        }

        ulong quintant = (index >> quintantShift) + (ulong)quintantOffset;
        ulong s58 = (index >> markerBits) & ((1UL << 58) - 1UL);

        return (quintant << 58) | ((s58 >> 2) << 2) | (1UL << 1);
    }
}
