using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Receives the outcome of one scrub round — the verify report and the repair report — so the caller decides
/// what to do with it: stage and publish the re-derived artifacts as a new generation, record the named
/// losses, or simply observe. A scrub turn is a generation-agnostic producer; it hands the round's result here
/// rather than committing, so commit ownership stays the caller's (a host today, the generation-commit
/// coordinator later).
/// </summary>
/// <param name="verifyReport">The round's verify verdict.</param>
/// <param name="repairReport">The round's repair outcome — the re-derived artifacts and named losses, or a refusal.</param>
/// <param name="cancellationToken">Signals that the handler should abandon its work cooperatively.</param>
/// <returns>A task that completes when the result has been handled.</returns>
public delegate ValueTask ScrubRoundResultDelegate(ScrubRoundReport verifyReport, RepairPassReport repairReport, CancellationToken cancellationToken);
