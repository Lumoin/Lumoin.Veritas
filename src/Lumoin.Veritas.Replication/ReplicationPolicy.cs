using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The tunable budget an <see cref="AntiEntropySession"/> reconciles under: how many coded symbols each side's
/// sketch carries, as a function of the LOCAL replica's item count. A rateless peel is guaranteed complete only
/// when the combinable symbol prefix covers the symmetric difference, so the budget must over-provision the
/// difference both replicas might hold. The default sizes for two equal-sized replicas — the proven cap
/// <c>BaseSymbols + PerItemSymbols * (|A| + |B|)</c> with <c>|A| = |B|</c> — which deliberately over-provisions a
/// smaller peer (a safe, complete peel) and under-provisions a much larger one (a partial peel the session
/// declines rather than applying an incomplete difference).
/// </summary>
/// <remarks>
/// The budget rests on the local count alone because the local side computes it before it has seen the peer:
/// the same number is what the local sketch is persisted at and what the peer is asked to produce, so the two
/// streams are index-wise combinable. Pinning the budget to the peer's count would need a round trip the seam
/// does not have here.
/// </remarks>
public readonly record struct ReplicationPolicy
{
    /// <summary>Creates a replication policy with an explicit symbol budget shape and the default false-decode ceiling. It chains the parameterless constructor so the <see cref="MaxFalseDecodeProbability"/> initializer runs.</summary>
    /// <param name="baseSymbols">The constant symbol headroom every budget carries, independent of item count; not negative.</param>
    /// <param name="perItemSymbols">The symbols budgeted per reconciliation item across both replicas; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="baseSymbols"/> or <paramref name="perItemSymbols"/> is negative.</exception>
    public ReplicationPolicy(int baseSymbols, int perItemSymbols): this()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseSymbols);
        ArgumentOutOfRangeException.ThrowIfNegative(perItemSymbols);

        BaseSymbols = baseSymbols;
        PerItemSymbols = perItemSymbols;
    }

    /// <summary>The constant symbol headroom every budget carries, independent of item count.</summary>
    public int BaseSymbols { get; }

    /// <summary>The symbols budgeted per reconciliation item across both replicas.</summary>
    public int PerItemSymbols { get; }

    /// <summary>
    /// The ceiling on the decoder's false-decode probability bound a complete peel may carry and still be acted on:
    /// the per-decode masquerade union bound (<c>PurityCheckCount * 2^(-8 * checksumWidth)</c>) a peel exceeds is
    /// declined as <see cref="AntiEntropyOutcome.FalseDecodeBoundExceeded"/> rather than laundered into a convergence
    /// claim. The default never trips for the width-8 structural checksum short of about <c>10^10</c> purity checks
    /// (the bound is then below <c>10^-9</c>); it exists so a session NAMES evidence-quality refusal instead of
    /// silently trusting an under-checked peel.
    /// </summary>
    public double MaxFalseDecodeProbability { get; init; } = 1e-9;

    /// <summary>The well-known default: 100 base symbols plus 20 per item across both replicas — the cap the proven two-replica reconciliation converges under.</summary>
    public static ReplicationPolicy Default { get; } = new(100, 20);

    /// <summary>The symbol budget for a replica holding <paramref name="localItemCount"/> items: the base headroom plus the per-item allowance over an assumed equal-sized peer (<c>2 * localItemCount</c> items in total).</summary>
    /// <param name="localItemCount">The local replica's reconciliation item count; not negative.</param>
    /// <returns>The number of coded symbols both sides' sketches carry for this session.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="localItemCount"/> is negative.</exception>
    /// <exception cref="OverflowException">The budget exceeds <see cref="int"/> range.</exception>
    public int SymbolBudget(int localItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(localItemCount);

        return checked(BaseSymbols + (PerItemSymbols * (2 * localItemCount)));
    }
}
