using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One cell of a HyperCube partition: for each join variable, the
/// number of shares its key domain is split into and the coordinate
/// this cell owns. A parallel worker evaluating a join under its
/// cell accepts a key for a variable only when the key hashes to
/// the cell's coordinate, so every output tuple belongs to exactly
/// one cell — the cells' results union to the full join with no
/// duplicates and no replicated work.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hashing.</b> Keys are spread with a fixed per-variable
/// multiplicative mixer — deterministic, allocation-free, and
/// distinct per variable index so the same key partitions
/// independently across variables. Partitioning hashes are
/// load-spreading mechanics, not identity or entropy; they do not
/// route through the entropy seams.
/// </para>
/// <para>
/// <b>Unpartitioned default.</b> <c>default(HyperCubeCell)</c>
/// accepts every key for every variable — the sequential case costs
/// one null check per intersection result.
/// </para>
/// </remarks>
[DebuggerDisplay("HyperCubeCell Partitioned={IsPartitioned}")]
public readonly record struct HyperCubeCell
{
    //64-bit odd constants with high bit dispersion (golden-ratio
    //family) used as per-variable multipliers; the variable index
    //selects the stream so a key's coordinate for one variable is
    //independent of its coordinate for another.
    private const ulong MixSalt = 0x9E3779B97F4A7C15UL;

    private const ulong MixMultiplier = 0xC2B2AE3D27D4EB4FUL;

    /// <summary>Per-global-variable share counts, parallel to the query's variable list. <c>null</c> for the unpartitioned cell; a share of one leaves that variable whole.</summary>
    private int[]? Shares { get; }

    /// <summary>This cell's coordinate per variable, parallel to <see cref="Shares"/>.</summary>
    private int[]? Coordinates { get; }

    /// <summary>Whether this cell restricts any variable; <c>false</c> for the unpartitioned default.</summary>
    public bool IsPartitioned => Shares is not null;

    /// <summary>
    /// Constructs a cell owning <paramref name="coordinates"/> in a
    /// grid of <paramref name="shares"/>.
    /// </summary>
    /// <param name="shares">Per-variable share counts, parallel to the query's variable list; a share of one leaves that variable unpartitioned.</param>
    /// <param name="coordinates">This cell's coordinate per variable; each in <c>[0, shares[i])</c>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The arrays differ in length.</exception>
    public HyperCubeCell(int[] shares, int[] coordinates)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(coordinates);

        if(shares.Length != coordinates.Length)
        {
            throw new ArgumentException("Shares and coordinates must be parallel arrays.", nameof(coordinates));
        }

        Shares = shares;
        Coordinates = coordinates;
    }

    /// <summary>
    /// Whether this cell owns <paramref name="key"/> for the
    /// variable at <paramref name="variableIndex"/> in the query's
    /// variable list.
    /// </summary>
    /// <param name="variableIndex">The variable's index in the query's variable list.</param>
    /// <param name="key">The candidate key.</param>
    /// <returns><c>true</c> when the key hashes to this cell's coordinate (or the variable is unpartitioned).</returns>
    public bool Accepts(int variableIndex, uint key)
    {
        if(Shares is null)
        {
            return true;
        }

        int share = Shares[variableIndex];

        if(share <= 1)
        {
            return true;
        }

        ulong mixed = (key + MixSalt * ((ulong)variableIndex + 1)) * MixMultiplier;
        mixed ^= mixed >> 32;

        return (int)(mixed % (uint)share) == Coordinates![variableIndex];
    }
}
