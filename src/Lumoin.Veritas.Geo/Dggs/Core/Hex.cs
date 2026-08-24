using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Hex-string encoding of 64-bit cell ids: the only sanctioned string form of a cell id (JSON has no 64-bit
/// integers).
/// </summary>
internal static class Hex
{
    /// <summary>
    /// Minimal lowercase hex with no padding — zero renders as <c>"0"</c>, never an empty string or a
    /// zero-padded/uppercase form.
    /// </summary>
    public static string U64ToHex(ulong index)
    {
        return index.ToString("x", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses with <see cref="NumberStyles.AllowHexSpecifier"/> only — empty or non-hex input throws
    /// <see cref="FormatException"/>, and no whitespace is tolerated. Input longer than 16 hex digits
    /// throws <see cref="OverflowException"/> — a <see cref="ulong"/> cannot represent it.
    /// </summary>
    public static ulong HexToU64(string hex)
    {
        return ulong.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }
}
