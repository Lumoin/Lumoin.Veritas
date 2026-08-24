namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The deterministic bit-mixing sweep source the spatial suites draw their
/// fixtures from: a fixed-increment mixer whose whole state is one caller-held
/// <see cref="ulong"/>, so a sweep is reproducible from its start value alone
/// and no entropy source enters a test.
/// </summary>
internal static class DeterministicBitMixer
{
    /// <summary>Advances the mixing state and returns the next 64-bit pattern.</summary>
    /// <param name="state">The mixing state, advanced in place.</param>
    /// <returns>The next 64-bit pattern.</returns>
    public static ulong NextBitPattern(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong mixed = state;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;

        return mixed ^ (mixed >> 31);
    }

    /// <summary>The next value in [0, 1): the pattern's top 53 bits over two to the fifty-third.</summary>
    /// <param name="state">The mixing state, advanced in place.</param>
    /// <returns>The drawn value in [0, 1).</returns>
    public static double NextUnitDouble(ref ulong state)
    {
        return (NextBitPattern(ref state) >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>The next non-negative value below <paramref name="exclusiveBound"/>.</summary>
    /// <param name="state">The mixing state, advanced in place.</param>
    /// <param name="exclusiveBound">The exclusive upper bound; must be positive.</param>
    /// <returns>The drawn value in [0, <paramref name="exclusiveBound"/>).</returns>
    public static int NextBelow(ref ulong state, int exclusiveBound)
    {
        return (int)(NextBitPattern(ref state) % (ulong)exclusiveBound);
    }
}
