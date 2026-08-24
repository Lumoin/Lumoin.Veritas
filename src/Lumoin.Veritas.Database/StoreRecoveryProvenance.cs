namespace Lumoin.Veritas.Database;

/// <summary>
/// The provenance of a database opened by serving from a durable persistence store: which committed
/// generation is being served and how faithfully recovery could prove it. A host inspects this after
/// <see cref="VeritasEngine.OpenAsync(Lumoin.Veritas.Core.Persistence.PersistenceStore, VeritasEngineOptions?, System.Threading.CancellationToken)"/>
/// to tell an exact recovery apart from a degraded or rolled-back one — a degraded or rolled-back open is
/// never silently indistinguishable from serving committed truth, within what surviving evidence can attest:
/// a rollback past a generation that no surviving pointer, retained copy, or manifest names is locally
/// undetectable, the epistemic floor of any local signal.
/// </summary>
/// <param name="Generation">The committed generation being served.</param>
/// <param name="IsDegraded"><see langword="true"/> when the served generation was recovered by the degraded direct manifest scan (no surviving CURRENT pointer) rather than by following a CURRENT pointer.</param>
/// <param name="CommitEvidenced"><see langword="true"/> when a retained CURRENT copy attests the served generation was committed; always true on a pointer-followed open, false only on an evidence-less degraded pick (a manifest with no proof it was ever committed).</param>
/// <param name="IsRollback"><see langword="true"/> when the served generation is older than the newest generation the surviving evidence names — the live pointer's or the newest verifying retained copy's — because everything newer failed verification and recovery fell back to an intact older generation.</param>
public readonly record struct StoreRecoveryProvenance(long Generation, bool IsDegraded, bool CommitEvidenced, bool IsRollback);
