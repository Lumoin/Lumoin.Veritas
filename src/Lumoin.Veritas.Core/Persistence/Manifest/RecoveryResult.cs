namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// The outcome of recovering the committed state: the manifest generation that was loaded, and
/// whether it was loaded on the degraded last-resort path. A non-degraded result followed a CURRENT
/// pointer — the live one or a retained one — so it names a generation that was atomically committed.
/// A degraded result came from scanning the manifests directly because no CURRENT pointer survived;
/// it is the highest-stamped manifest that still verifies, which a recovery cannot prove was the last
/// committed generation rather than an orphan of a torn publish.
/// </summary>
/// <param name="Manifest">The recovered manifest generation; never <see langword="null"/> for a result returned by <see cref="ManifestRecovery"/>.</param>
/// <param name="IsDegraded"><see langword="true"/> when the manifest was recovered by the degraded direct scan rather than by following a CURRENT pointer.</param>
public readonly record struct RecoveryResult(Manifest Manifest, bool IsDegraded);
