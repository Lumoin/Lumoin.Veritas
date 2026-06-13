using System.Diagnostics;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The resolved memory-protection verdict — the plan-side counterpart
/// of the <see cref="MemoryProtectionAssumption"/> knob, produced by
/// <see cref="ExecutionPolicy.Resolve()"/>. It carries both the
/// verdict and how it was reached, so the verify-and-scrub cadence
/// reads a settled fact rather than re-interpreting the policy.
/// </summary>
/// <remarks>
/// <para>
/// A "protected" verdict is only ever as trustworthy as its
/// <see cref="Source"/>. A <see cref="ProtectionDetectionSource.Probed"/>
/// reading rests on firmware self-report (SMBIOS), which a hypervisor
/// can synthesise; this is why an inconclusive
/// <see cref="MemoryProtectionAssumption.AutoDetect"/> resolves to
/// unprotected (<see cref="ProtectionDetectionSource.UnknownDefaulted"/>)
/// rather than optimistically assuming correction is present. An
/// operator who knows the true hardware state asserts it, surfacing as
/// <see cref="ProtectionDetectionSource.AssumedByPolicy"/>.
/// </para>
/// </remarks>
/// <param name="MemoryIsProtected">Whether main memory is treated as hardware error-corrected. Drives the lighter (protected) or heavier (unprotected) verify-and-scrub cadence.</param>
/// <param name="Source">How <paramref name="MemoryIsProtected"/> was determined — an affirmative probe, an operator assertion, or a conservative default.</param>
[DebuggerDisplay("ResolvedProtectionState Protected={MemoryIsProtected} Source={Source}")]
internal readonly record struct ResolvedProtectionState(
    bool MemoryIsProtected,
    ProtectionDetectionSource Source);
