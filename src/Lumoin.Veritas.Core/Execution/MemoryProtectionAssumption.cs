namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The operator's stance on whether main memory is hardware
/// error-corrected — a knob on <see cref="ExecutionPolicy"/> that
/// <see cref="ExecutionPolicy.Resolve()"/> turns into a
/// <see cref="ResolvedProtectionState"/>. The resolved state scales
/// how aggressively data is verified on load and how often the scrub
/// walk is initiated: unprotected memory, where silent bit-rot is the
/// live risk, verifies and scrubs more.
/// </summary>
/// <remarks>
/// <para>
/// The knob exists because the hardware probe is unreliable exactly
/// where large data deploys — hypervisors and containers present
/// synthetic firmware tables that defeat detection. When detection is
/// inconclusive, the resolution defaults conservatively to
/// <see cref="AssumeUnprotected"/> (verify more, not less); an
/// operator who knows the true state overrides the probe with
/// <see cref="AssumeProtected"/> or <see cref="AssumeUnprotected"/>.
/// </para>
/// <para>
/// This concerns the MEMORY channel only — RAM error correction (see
/// <see cref="CorruptionChannel.Memory"/>). It is not a statement about data
/// at rest or in flight: "protected" must not be read as "the persisted data
/// is safe". Corruption of stored bytes is the storage channel
/// (<see cref="CorruptionChannel.Storage"/>), which error correction does not
/// reach and which the checksum-and-scrub layer guards regardless of this knob.
/// </para>
/// </remarks>
public enum MemoryProtectionAssumption
{
    /// <summary>Probe the hardware and resolve from what it reports; an inconclusive probe resolves to unprotected. The default.</summary>
    AutoDetect,

    /// <summary>Treat memory as error-corrected regardless of the probe: the lighter verify-and-scrub cadence. The operator asserts the hardware state.</summary>
    AssumeProtected,

    /// <summary>Treat memory as unprotected regardless of the probe: the heavier verify-and-scrub cadence. Also the conservative resolution of an inconclusive <see cref="AutoDetect"/> probe.</summary>
    AssumeUnprotected,
}
