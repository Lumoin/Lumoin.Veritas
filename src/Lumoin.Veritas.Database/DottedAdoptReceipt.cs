namespace Lumoin.Veritas.Database;

/// <summary>
/// The receipt of one remove-aware adopt write-back: how the journalled commit landed, and what the committed
/// plan actually adopted — the addition assignments and drop assignments the commit-time guard admitted, which
/// can be fewer than the peer offered (a peer dot the live context covered by commit time became a local
/// tombstone mid-flight and was skipped). The counts describe the COMMITTED plan, so an operator surface
/// composed from them reports real effect, never the wire's offer.
/// </summary>
/// <param name="Outcome">How the write-back landed: committed, a no-op (peer knowledge already covered), or conflict-exhausted.</param>
/// <param name="AdoptedAdditions">The addition assignments the committed plan adopted; zero unless <paramref name="Outcome"/> is <see cref="WriteBackOutcome.Committed"/>.</param>
/// <param name="AdoptedDrops">The drop assignments the committed plan applied; zero unless <paramref name="Outcome"/> is <see cref="WriteBackOutcome.Committed"/>.</param>
public readonly record struct DottedAdoptReceipt(WriteBackOutcome Outcome, int AdoptedAdditions, int AdoptedDrops);
