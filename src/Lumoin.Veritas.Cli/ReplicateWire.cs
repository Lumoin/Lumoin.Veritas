using System;
using System.IO.Pipelines;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// The replicate command's wire vocabulary: the one-byte service selector every inbound connection opens with,
/// the fixed budgets its outbound repair, reconcile, and metadata-plane surfaces drive, and the stream-pipe
/// options every channel end reads and writes a network stream under. Both replicas of a deployment run the same
/// executable, so these values agree on both ends by construction; the selector byte routes an accepted
/// connection to its channel, and an unknown selector closes the connection.
/// </summary>
internal static class ReplicateWire
{
    /// <summary>The service selector opening a sketch-fetch connection: the peer serves its maintained structural sketch, one fetch per connection.</summary>
    internal const byte SketchService = 1;

    /// <summary>The service selector opening a shard-difference connection: the peer serves one shard's add-only difference exchange.</summary>
    internal const byte ShardDifferenceService = 2;

    /// <summary>The service selector opening a dotted-difference connection: the peer serves one remove-aware exchange over its dotted commit ledger.</summary>
    internal const byte DottedDifferenceService = 3;

    /// <summary>The service selector opening a consensus metadata connection: the peer serves its metadata plane's correlated call frames, MANY calls over one long-lived connection rather than one exchange per connection.</summary>
    internal const byte MetadataService = 4;

    /// <summary>The one-byte service verdict a host answers every selector with before any channel frame: accepted — the routed serve's frames follow.</summary>
    internal const byte ServiceAccepted = 1;

    /// <summary>The one-byte service verdict for an UNKNOWN selector: the named refusal reply, written before the close, so a dialing peer distinguishes service-unknown from network death — an absent verdict byte is death, never inferred as unsupported.</summary>
    internal const byte ServiceRefusedUnknown = 0;

    /// <summary>
    /// The one-byte service verdict for a KNOWN selector whose engine is not open yet: the listener serves the
    /// metadata plane from before the engine open, so a peer may dial an engine-backed service during the open
    /// window. The verdict keeps the three states apart — not-ready is neither service-unknown nor network death
    /// — so a dialing peer retries rather than concluding the service is unsupported.
    /// </summary>
    internal const byte ServiceUnavailableNotReady = 2;

    /// <summary>
    /// How many consensus attempts ONE metadata-plane obligation may spend before it answers that it is
    /// undecided. Four: the identity claim runs INSIDE the engine's open, so this bounds how long an
    /// identity-bearing open spends against a quorum it cannot reach before it fails open, and four attempts
    /// leaves a contended chain room to resolve a first-round priority race while a partitioned host still
    /// proceeds promptly. <c>--metadata-attempts</c> overrides it per host.
    /// </summary>
    internal const int MetadataAttemptBudget = 4;

    /// <summary>How many times one protocol step may send to ONE recorder before abandoning it for that step. Three, so a connection that died between calls costs a redial inside the step rather than the whole step.</summary>
    internal const int MetadataAttemptsPerRecorder = 3;

    /// <summary>
    /// The deadline ONE member is given to answer a catch-up read or a readiness probe before this host gives up
    /// on it and counts it unreachable. Five seconds stands far above a loopback or a same-region round trip and
    /// far below the patience an operator has for a status line. It is spent on the two READS only and never on
    /// a write, so an identity-bearing open against an unreachable quorum is bounded by the attempt budget's own
    /// round trips rather than by this span.
    /// </summary>
    internal static TimeSpan MetadataMemberQueryDeadline { get; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The hedging delay increment per position in the membership order, which is ZERO: every recorder is
    /// activated at once. Staggering trades a round trip's latency for fewer messages and pays only when the
    /// links to the members differ in cost, which this command's endpoint map declares nothing about — so the
    /// value that costs latency is not inherited by default.
    /// </summary>
    internal static TimeSpan MetadataHedgingBaseDelay { get; } = TimeSpan.Zero;

    /// <summary>The symbol budget one dotted-difference exchange is bounded by; sized far above the loopback deployment's expected dotted differences so a whole diverged set exchanges completely.</summary>
    internal const int DottedSymbolCap = 8192;

    /// <summary>The shard-count bits of the prefix shard policy every replica of this command drives and declares: four shards, plenty for the loopback deployment this lane serves while keeping per-shard sessions small.</summary>
    internal const int ShardBits = 2;

    /// <summary>The per-shard symbol ceiling the sharded repair rung bounds a non-terminating decode into an abort with; sized far above one system-of-record block's items so a whole lost block per shard peels completely.</summary>
    internal const int ShardSymbolCap = 8192;

    /// <summary>The symbol budget the single-block repair provider fetches the peer's sketch at and caps its recovery to; sized far above one system-of-record block's items so a single lost block peels completely.</summary>
    internal const int SingleBlockSymbolCap = 8192;

    /// <summary>The round bound one reconcile pull runs under; declining rounds retry back-to-back within it, and a pull that cannot converge inside the bound reports its last outcome by name.</summary>
    internal const int ReconcileMaxRounds = 8;

    /// <summary>The stream-pipe reader options every replicate channel end reads a network stream under: the stream stays open across the channel's completion, so the connection's owner controls the socket lifetime.</summary>
    internal static StreamPipeReaderOptions LeaveOpenReaderOptions { get; } = new(leaveOpen: true);

    /// <summary>The stream-pipe writer options every replicate channel end writes a network stream under: the stream stays open across the channel's completion, so the connection's owner controls the socket lifetime.</summary>
    internal static StreamPipeWriterOptions LeaveOpenWriterOptions { get; } = new(leaveOpen: true);
}
