namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The heap buffer carrier for <see cref="ExactInCircle"/>'s exact tail: the
/// six expansion buffers whose derived capacities exceed any sensible stack
/// budget. Created once per triangulation build (and once per test class) and
/// reused across every tail entry — the tail is guaranteed, not rare, on
/// exactly cocircular inputs, because a zero determinant can never clear the
/// static filter's error bound, so per-call allocation would put its ~37 KB
/// of buffers on exactly the inputs that need the tail most. Plain heap
/// arrays, never pooled: the substrate's geometry folder holds no pool
/// dependency. The carrier is single-owner state —
/// one holder, one thread at a time, exactly as the per-call triangulation
/// that owns it runs.
/// </summary>
internal sealed class InCircleScratch
{
    /// <summary>
    /// The shared product scratch: <c>ProductScratchCapacity(16, 16)</c> =
    /// <c>ScaleCapacity(16) + ProductCapacity(16, 16)</c> = 32 + 512.
    /// </summary>
    internal const int TermScratchCapacity = 544;

    /// <summary>
    /// One lift-weighted term of the determinant:
    /// <c>ProductCapacity(16, 16)</c> = 2 · 16 · 16, a product of two
    /// sixteen-component accumulators.
    /// </summary>
    internal const int TermCapacity = 512;

    /// <summary>The first two terms' sum: <c>SumCapacity(512, 512)</c>.</summary>
    internal const int PartialSumCapacity = 1024;

    /// <summary>The full determinant: <c>SumCapacity(1024, 512)</c>.</summary>
    internal const int DeterminantCapacity = 1536;

    /// <summary>Binds the six pre-sized buffers as the carrier's whole state.</summary>
    /// <param name="termScratch">The shared product scratch.</param>
    /// <param name="termA">The first lift-weighted term.</param>
    /// <param name="termB">The second lift-weighted term.</param>
    /// <param name="termC">The third lift-weighted term.</param>
    /// <param name="partialSum">The running sum of the first two terms.</param>
    /// <param name="determinant">The full determinant.</param>
    private InCircleScratch(
        double[] termScratch,
        double[] termA,
        double[] termB,
        double[] termC,
        double[] partialSum,
        double[] determinant)
    {
        TermScratch = termScratch;
        TermA = termA;
        TermB = termB;
        TermC = termC;
        PartialSum = partialSum;
        Determinant = determinant;
    }

    /// <summary>The shared scratch every lift-weighted product runs through.</summary>
    internal double[] TermScratch { get; }

    /// <summary>The first lift-weighted term, <c>alift · pairA</c>.</summary>
    internal double[] TermA { get; }

    /// <summary>The second lift-weighted term, <c>blift · pairB</c>.</summary>
    internal double[] TermB { get; }

    /// <summary>The third lift-weighted term, <c>clift · pairC</c>.</summary>
    internal double[] TermC { get; }

    /// <summary>The running sum of the first two terms.</summary>
    internal double[] PartialSum { get; }

    /// <summary>The exact determinant whose dominant component carries the sign.</summary>
    internal double[] Determinant { get; }

    /// <summary>
    /// Allocates the six buffers at their derived capacities — 4,640 doubles,
    /// 37,120 bytes, one allocation set for the carrier's whole life.
    /// </summary>
    /// <returns>The carrier.</returns>
    public static InCircleScratch Create()
    {
        return new InCircleScratch(
            new double[TermScratchCapacity],
            new double[TermCapacity],
            new double[TermCapacity],
            new double[TermCapacity],
            new double[PartialSumCapacity],
            new double[DeterminantCapacity]);
    }
}
