using System.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// Configuration for a <see cref="ReasoningRendezvous"/>: load-time knobs
/// parameterising a fully dynamic per-request strategy choice — the same
/// shape as the join layer's <c>QueryEnginePolicy</c>.
/// </summary>
/// <param name="PreferRdfsWhenSufficient">Whether a TBox within the RDFS vocabulary runs the cheaper streaming pass instead of the full RL closure.</param>
/// <param name="DelegateBeyondRl">Whether axioms beyond the RL grammar are handed to the description-logic delegate when one is wired; <c>false</c> always reports them instead.</param>
[DebuggerDisplay("ReasoningPolicy PreferRdfs={PreferRdfsWhenSufficient} Delegate={DelegateBeyondRl}")]
public readonly record struct ReasoningPolicy(
    bool PreferRdfsWhenSufficient,
    bool DelegateBeyondRl)
{
    /// <summary>
    /// The default policy: RDFS-shaped TBoxes take the streaming pass, and
    /// beyond-RL modules are delegated when a delegate is wired.
    /// </summary>
    public static ReasoningPolicy Default { get; } = new(PreferRdfsWhenSufficient: true, DelegateBeyondRl: true);
}
