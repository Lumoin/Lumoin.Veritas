namespace Lumoin.Veritas.Replication;

/// <summary>
/// Reads the largest counter the LIVE causal context covers anywhere on the local host identity's own axis —
/// the identity-collision tripwire's comparison seam, bound to
/// <see cref="DottedCommitLedger.ReadOwnAxisMaximum"/>. The tripwire reads the LIVE maximum, not a session's
/// pinned snapshot, because a concurrent session may legitimately have taught the peer newer local dots; the
/// live maximum is monotone over an open, so coverage beyond it proves a second minter under this identity.
/// </summary>
/// <returns>The overall maximum covered counter on the local identity's own axis; 0 before the first mint.</returns>
public delegate ulong ReadOwnAxisMaximumDelegate();
