namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// How a <see cref="ResolvedProtectionState"/> arrived at its
/// memory-protection verdict — kept alongside the verdict so the
/// distinction between an affirmative hardware reading, an operator
/// assertion, and a conservative default is never lost.
/// </summary>
internal enum ProtectionDetectionSource
{
    /// <summary>A platform probe affirmatively determined the protection state from hardware-reported facts.</summary>
    Probed,

    /// <summary>The operator forced the state through <see cref="MemoryProtectionAssumption.AssumeProtected"/> or <see cref="MemoryProtectionAssumption.AssumeUnprotected"/>; no probe was consulted.</summary>
    AssumedByPolicy,

    /// <summary>An <see cref="MemoryProtectionAssumption.AutoDetect"/> probe was inconclusive, so the state defaulted conservatively to unprotected.</summary>
    UnknownDefaulted,
}
