namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The heap buffer carrier for <see cref="ExactOrientation3d"/>'s two heavy paths:
/// the steering predicate's degree-four exact tail — whose buffers size exactly as
/// the incircle tail's, three component products of sixteen-component expansions and
/// their two sums — and the planarity band comparison, which adds the compressed
/// orientation's squared product and reuses the determinant buffer as that product's
/// scratch and as the final difference. Created once per parse (and once per test
/// class) and reused across every entry: the exact tail is guaranteed, not rare, on
/// exactly aligned steering inputs, because a zero determinant can never clear the
/// static filter's strict bound, and the band comparison runs it every time. Plain
/// heap arrays, never pooled: the carrier allocates one buffer set for its whole
/// life and a parse creates at most one of it, so there is no per-item rental to
/// amortize and no pooling seam to bind. The carrier is single-owner state — one
/// holder, one thread at a time, exactly as its consumers run.
/// </summary>
internal sealed class Orientation3dScratch
{
    /// <summary>
    /// The shared product scratch: <c>ProductScratchCapacity(16, 16)</c> =
    /// <c>ScaleCapacity(16) + ProductCapacity(16, 16)</c> = 32 + 512.
    /// </summary>
    internal const int TermScratchCapacity = 544;

    /// <summary>
    /// One component product of the cross-dot — or one normal component's square in
    /// the band comparison: <c>ProductCapacity(16, 16)</c> = 2 · 16 · 16, a product
    /// of two sixteen-component expansions.
    /// </summary>
    internal const int TermCapacity = 512;

    /// <summary>The first two terms' sum: <c>SumCapacity(512, 512)</c>.</summary>
    internal const int PartialSumCapacity = 1024;

    /// <summary>
    /// The full cross-dot — or, in the band comparison, first the raw norm square,
    /// then the squared product's scratch, then the final difference:
    /// <c>SumCapacity(1024, 512)</c>, which also covers the scratch's
    /// <c>ScaleCapacity(24) + ProductCapacity(24, 24)</c> = 1200 and the
    /// difference's <c>SumCapacity(1152, 128)</c> = 1280.
    /// </summary>
    internal const int DeterminantCapacity = 1536;

    /// <summary>
    /// The compressed orientation expansion's square:
    /// <c>ProductCapacity(24, 24)</c> = 2 · 24 · 24 over the wall-derived
    /// compressed capacity.
    /// </summary>
    internal const int DeterminantSquareCapacity = 1152;

    /// <summary>Binds the seven allocated buffers to the carrier; the factory is the only caller, so every buffer arrives at its derived capacity.</summary>
    private Orientation3dScratch(
        double[] termScratch,
        double[] termA,
        double[] termB,
        double[] termC,
        double[] partialSum,
        double[] determinant,
        double[] determinantSquare)
    {
        TermScratch = termScratch;
        TermA = termA;
        TermB = termB;
        TermC = termC;
        PartialSum = partialSum;
        Determinant = determinant;
        DeterminantSquare = determinantSquare;
    }

    /// <summary>The shared scratch every carrier-sized product runs through.</summary>
    internal double[] TermScratch { get; }

    /// <summary>The first component product — or the normal X component's square.</summary>
    internal double[] TermA { get; }

    /// <summary>The second component product — or the normal Y component's square.</summary>
    internal double[] TermB { get; }

    /// <summary>The third component product — or the normal Z component's square.</summary>
    internal double[] TermC { get; }

    /// <summary>The running sum of the first two terms.</summary>
    internal double[] PartialSum { get; }

    /// <summary>The exact cross-dot whose dominant component carries the steering sign — and the band comparison's multi-role large buffer.</summary>
    internal double[] Determinant { get; }

    /// <summary>The compressed orientation expansion's exact square.</summary>
    internal double[] DeterminantSquare { get; }

    /// <summary>
    /// Allocates the seven buffers at their derived capacities — 5,792 doubles,
    /// 46,336 bytes, one allocation set for the carrier's whole life.
    /// </summary>
    public static Orientation3dScratch Create()
    {
        return new Orientation3dScratch(
            new double[TermScratchCapacity],
            new double[TermCapacity],
            new double[TermCapacity],
            new double[TermCapacity],
            new double[PartialSumCapacity],
            new double[DeterminantCapacity],
            new double[DeterminantSquareCapacity]);
    }
}
