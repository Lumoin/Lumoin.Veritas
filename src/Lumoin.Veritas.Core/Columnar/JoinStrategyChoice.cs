using System.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The per-query join-strategy decision <see cref="JoinStrategySelector"/>
/// hands the batched pipeline: which of the opt-in techniques the plan should
/// engage for this query. Under the manual policy it mirrors the
/// <see cref="QueryEnginePolicy"/> flags verbatim; under the adaptive policy
/// the factorising routes are engaged where the index statistics say their
/// compression pays.
/// </summary>
/// <param name="SemijoinReduction">Whether an acyclic plan of three or more patterns attaches the Yannakakis semijoin tree.</param>
/// <param name="FactorizedStar">Whether a qualifying star routes through the factorising join.</param>
/// <param name="FactorizedChain">Whether a qualifying three-pattern chain routes through the join-then-nest factorisation.</param>
[DebuggerDisplay("JoinStrategyChoice Semijoin={SemijoinReduction} Star={FactorizedStar} Chain={FactorizedChain}")]
public readonly record struct JoinStrategyChoice(
    bool SemijoinReduction,
    bool FactorizedStar,
    bool FactorizedChain);
