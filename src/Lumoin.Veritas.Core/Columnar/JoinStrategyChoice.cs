using System.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The per-query join-strategy decision the join-route seam hands the batched
/// pipeline: which of the opt-in techniques the plan should engage for this
/// query. It is the composed decision's factorisation axis read into the
/// pipeline's own vocabulary — the <see cref="QueryEnginePolicy"/> flags
/// verbatim where that axis states nothing, and the named engagement where a
/// force, a hint, or a selector stated one.
/// </summary>
/// <param name="SemijoinReduction">Whether an acyclic plan of three or more patterns attaches the Yannakakis semijoin tree.</param>
/// <param name="FactorizedStar">Whether a qualifying star routes through the factorising join.</param>
/// <param name="FactorizedChain">Whether a qualifying three-pattern chain routes through the join-then-nest factorisation.</param>
[DebuggerDisplay("JoinStrategyChoice Semijoin={SemijoinReduction} Star={FactorizedStar} Chain={FactorizedChain}")]
public readonly record struct JoinStrategyChoice(
    bool SemijoinReduction,
    bool FactorizedStar,
    bool FactorizedChain);
