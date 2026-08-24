namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// How a <see cref="RankSelectBitVector"/> counts the set bits of the
/// superblock-relative word run inside <see cref="RankSelectBitVector.Rank1"/>
/// — the inner step of every rank. All modes return identical counts; they
/// differ only in instruction scheduling, so the choice is a measured
/// per-deployment knob.
/// </summary>
public enum RankScanMode
{
    /// <summary>One popcount per word accumulated serially. The default.</summary>
    Sequential,

    /// <summary>Two independent accumulators over the word run, breaking the serial add chain.</summary>
    Unrolled,

    /// <summary>A single 512-bit shuffle-table popcount over the whole superblock with lanes beyond the run masked out, where the hardware supports it; falls back to <see cref="Unrolled"/> elsewhere.</summary>
    VectorPopCount,
}
