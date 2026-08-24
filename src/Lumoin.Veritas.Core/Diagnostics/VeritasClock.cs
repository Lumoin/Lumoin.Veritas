using System;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// The library's central <see cref="TimeProvider"/> accessor.
/// Routes wall-clock reads in the library through one named
/// place so the BannedSymbols rule that <c>DateTime.UtcNow</c> /
/// <c>DateTimeOffset.UtcNow</c> are not called directly is
/// satisfied by a single substitution at every call site.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is.</b> A static accessor returning the system
/// time provider. Used by call sites that need a wall-clock read
/// where the clock is not a tested observable — for example, the
/// composition root of a host application that constructs a
/// trace-emitting class with <c>VeritasClock.System</c>.
/// </para>
/// <para>
/// <b>What this is not.</b> A dependency-injection seam. The
/// returned provider is immutable — callers cannot swap it. Code
/// that genuinely needs a substitutable clock (the journal,
/// trace emitters, validators) takes a
/// <see cref="TimeProvider"/> parameter explicitly and lets the
/// caller pass one. <see cref="VeritasClock"/> exists for the
/// "I just need a non-banned <c>UtcNow</c>" cases where the
/// clock isn't a tested observable.
/// </para>
/// </remarks>
public static class VeritasClock
{
    /// <summary>
    /// The system <see cref="TimeProvider"/> — wall-clock UTC,
    /// monotonic timestamps, OS-supplied resolution. Equivalent
    /// to <see cref="TimeProvider.System"/> but accessed through
    /// this property so call sites are uniform across the
    /// codebase.
    /// </summary>
    public static TimeProvider System => TimeProvider.System;
}
