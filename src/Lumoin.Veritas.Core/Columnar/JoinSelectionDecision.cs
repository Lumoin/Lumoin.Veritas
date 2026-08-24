using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One join-route decision: which view-borne route runs, which selector decided it, why, and what it
/// decided on the route's own axes. Value-based — the engine reads the route and enters it, and a route it
/// cannot serve falls through to the sound default rather than raising.
/// </summary>
/// <remarks>
/// Every axis beyond the route has a value meaning "not decided — the engine's standing behaviour
/// applies", and that value is the default, so a selector that knows nothing of an axis leaves the engine
/// exactly as it was. New axes are added the same way.
/// </remarks>
/// <param name="Route">The route to run: <see cref="QueryEngineKind.FreeJoin"/>, <see cref="QueryEngineKind.ColumnarBatched"/>, or <see cref="QueryEngineKind.Columnar"/>. Any other value names a route this seam does not serve and falls through.</param>
/// <param name="SelectorKind">Who decided the ROUTE. <see cref="JoinStrategySelectorKind.None"/> on a decision no selector took.</param>
/// <param name="Reason">Why, for the built-in rules; <see cref="JoinSelectionReason.Unspecified"/> otherwise.</param>
/// <param name="Depth">The depth the Free Join route's relations build at; <see cref="FreeJoinDepthPolicy.Unspecified"/> leaves the engine's per-relation rule to decide.</param>
/// <param name="Build">How the Free Join route's tries materialise their maps; <see cref="FreeJoinTrieBuildPreference.Unspecified"/> leaves the policy's value standing.</param>
/// <param name="Factorization">Which factorising route the batched pipeline engages; <see cref="FactorizationEngagement.Unspecified"/> leaves the policy's flags standing verbatim.</param>
/// <param name="HintedAxes">Which axes a per-query hint overlaid; <see cref="JoinSelectionHintedAxes.None"/> on a query that hinted nothing or whose hints all lost.</param>
public readonly record struct JoinSelectionDecision(
    QueryEngineKind Route,
    JoinStrategySelectorKind SelectorKind,
    JoinSelectionReason Reason,
    FreeJoinDepthPolicy Depth,
    FreeJoinTrieBuildPreference Build,
    FactorizationEngagement Factorization,
    JoinSelectionHintedAxes HintedAxes)
{
    /// <summary>A decision by the library's structural rule.</summary>
    /// <param name="route">The route to run.</param>
    /// <param name="reason">Why the structural rule chose it.</param>
    /// <param name="depth">The depth axis; unstated leaves the engine's per-relation rule to decide.</param>
    /// <param name="build">The trie-build axis; unstated leaves the policy's value standing.</param>
    /// <param name="factorization">The factorisation axis; unstated leaves the policy's flags standing.</param>
    /// <returns>The decision, stamped <see cref="JoinStrategySelectorKind.Structural"/>.</returns>
    public static JoinSelectionDecision Structural(
        QueryEngineKind route,
        JoinSelectionReason reason,
        FreeJoinDepthPolicy depth = FreeJoinDepthPolicy.Unspecified,
        FreeJoinTrieBuildPreference build = FreeJoinTrieBuildPreference.Unspecified,
        FactorizationEngagement factorization = FactorizationEngagement.Unspecified)
    {
        return new JoinSelectionDecision(route, JoinStrategySelectorKind.Structural, reason, depth, build, factorization, JoinSelectionHintedAxes.None);
    }

    /// <summary>A decision by the library's flags-verbatim rule.</summary>
    /// <param name="route">The route to run.</param>
    /// <param name="reason">Why the flags-verbatim rule chose it.</param>
    /// <param name="depth">The depth axis; unstated leaves the engine's per-relation rule to decide.</param>
    /// <param name="build">The trie-build axis; unstated leaves the policy's value standing.</param>
    /// <param name="factorization">The factorisation axis; unstated leaves the policy's flags standing.</param>
    /// <returns>The decision, stamped <see cref="JoinStrategySelectorKind.Manual"/>.</returns>
    public static JoinSelectionDecision Manual(
        QueryEngineKind route,
        JoinSelectionReason reason,
        FreeJoinDepthPolicy depth = FreeJoinDepthPolicy.Unspecified,
        FreeJoinTrieBuildPreference build = FreeJoinTrieBuildPreference.Unspecified,
        FactorizationEngagement factorization = FactorizationEngagement.Unspecified)
    {
        return new JoinSelectionDecision(route, JoinStrategySelectorKind.Manual, reason, depth, build, factorization, JoinSelectionHintedAxes.None);
    }

    /// <summary>
    /// A decision by the library's calibrated rule, whose route follows the structural rule and whose
    /// remaining axes follow the measured statistics of the view it was consulted over. The rationale
    /// vocabulary is the structural one: the calibrated identity rides <see cref="SelectorKind"/>, so one
    /// rationale keeps one name.
    /// </summary>
    /// <param name="route">The route to run.</param>
    /// <param name="reason">Why the route was chosen — the structural rationale.</param>
    /// <param name="factorization">The factorisation axis; unstated leaves the policy's flags standing.</param>
    /// <returns>The decision, stamped <see cref="JoinStrategySelectorKind.Calibrated"/>.</returns>
    public static JoinSelectionDecision Calibrated(
        QueryEngineKind route,
        JoinSelectionReason reason,
        FactorizationEngagement factorization = FactorizationEngagement.Unspecified)
    {
        return new JoinSelectionDecision(route, JoinStrategySelectorKind.Calibrated, reason, FreeJoinDepthPolicy.Unspecified, FreeJoinTrieBuildPreference.Unspecified, factorization, JoinSelectionHintedAxes.None);
    }

    /// <summary>A route an explicit policy force fixed, taken without consulting any selector.</summary>
    /// <param name="route">The route the force names.</param>
    /// <returns>The decision, stamped <see cref="JoinStrategySelectorKind.Forced"/> and <see cref="JoinSelectionReason.PolicyForced"/>.</returns>
    public static JoinSelectionDecision Forced(QueryEngineKind route)
    {
        return new JoinSelectionDecision(route, JoinStrategySelectorKind.Forced, JoinSelectionReason.PolicyForced, FreeJoinDepthPolicy.Unspecified, FreeJoinTrieBuildPreference.Unspecified, FactorizationEngagement.Unspecified, JoinSelectionHintedAxes.None);
    }

    /// <summary>
    /// A route a per-query hint named, taken without consulting any selector: the caller outranks the
    /// statistics on the axis it names, and yields to every policy force.
    /// </summary>
    /// <param name="route">The route the hint names.</param>
    /// <returns>The decision, stamped <see cref="JoinStrategySelectorKind.Hinted"/> and <see cref="JoinSelectionReason.HintedRoute"/>.</returns>
    public static JoinSelectionDecision Hinted(QueryEngineKind route)
    {
        return new JoinSelectionDecision(route, JoinStrategySelectorKind.Hinted, JoinSelectionReason.HintedRoute, FreeJoinDepthPolicy.Unspecified, FreeJoinTrieBuildPreference.Unspecified, FactorizationEngagement.Unspecified, JoinSelectionHintedAxes.None);
    }

    /// <summary>A decision by a deployment-supplied selector, naming itself.</summary>
    /// <param name="route">The route to run.</param>
    /// <param name="selectorKind">The supplied selector's telemetry identity.</param>
    /// <param name="depth">The depth axis; unstated leaves the engine's per-relation rule to decide.</param>
    /// <param name="build">The trie-build axis; unstated leaves the policy's value standing.</param>
    /// <param name="factorization">The factorisation axis; unstated leaves the policy's flags standing.</param>
    /// <returns>The decision, stamped with the supplied identity and <see cref="JoinSelectionReason.Unspecified"/>.</returns>
    public static JoinSelectionDecision Supplied(
        QueryEngineKind route,
        JoinStrategySelectorKind selectorKind,
        FreeJoinDepthPolicy depth = FreeJoinDepthPolicy.Unspecified,
        FreeJoinTrieBuildPreference build = FreeJoinTrieBuildPreference.Unspecified,
        FactorizationEngagement factorization = FactorizationEngagement.Unspecified)
    {
        return new JoinSelectionDecision(route, selectorKind, JoinSelectionReason.Unspecified, depth, build, factorization, JoinSelectionHintedAxes.None);
    }
}
