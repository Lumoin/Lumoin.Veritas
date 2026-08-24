using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Threading;

/// <summary>
/// A scope handle held while an exclusive acquirer owns an
/// <see cref="AsyncSharedExclusiveLock"/>. Disposal releases the
/// exclusive hold; multiple disposes on the same scope are a
/// contract violation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async-only disposal.</b> The scope is <see cref="IAsyncDisposable"/>
/// without a sync <see cref="IDisposable"/> sibling. Today's
/// release path is purely synchronous — two
/// <see cref="System.Threading.SemaphoreSlim.Release()"/> calls
/// — so <see cref="DisposeAsync"/> returns a completed
/// <see cref="ValueTask"/>. The async-only surface is deliberate:
/// every code path that takes a scope is already async (it had
/// to <c>await</c> the entry method to obtain one), and pinning
/// disposal to the async surface keeps the type future-proof
/// against a release path that grows real async work — for
/// instance, a cluster-wide lock layer that has to notify peers
/// on release. Adding async work later is then a non-breaking
/// change; adding it on top of a sync <see cref="IDisposable"/>
/// would not be.
/// </para>
/// <para>
/// A default-initialised scope holds no lock and disposes as a
/// no-op; this keeps the type robust under the <c>default</c>
/// pattern but does not endorse it as a usage shape — callers
/// always go through
/// <see cref="AsyncSharedExclusiveLock.EnterExclusiveAsync"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("ExclusiveScope")]
public readonly struct ExclusiveScope: IAsyncDisposable, IEquatable<ExclusiveScope>
{
    private AsyncSharedExclusiveLock? Owner { get; }

    internal ExclusiveScope(AsyncSharedExclusiveLock owner)
    {
        Owner = owner;
    }

    /// <summary>
    /// Releases the exclusive hold. Today the release is purely
    /// synchronous and the returned <see cref="ValueTask"/> is
    /// always already completed; the async surface is the only
    /// disposal contract so future async release paths fit
    /// without a breaking change.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Owner?.ReleaseExclusive();

        return ValueTask.CompletedTask;
    }

    /// <summary>Returns <c>true</c> when both scopes refer to the same underlying lock instance (or both are default).</summary>
    public bool Equals(ExclusiveScope other) => ReferenceEquals(Owner, other.Owner);

    /// <summary>Returns <c>true</c> when <paramref name="obj"/> is an <see cref="ExclusiveScope"/> referring to the same underlying lock.</summary>
    public override bool Equals(object? obj) => obj is ExclusiveScope other && Equals(other);

    /// <summary>Hash code consistent with <see cref="Equals(ExclusiveScope)"/> — derived from the owning lock's identity.</summary>
    public override int GetHashCode() => Owner is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Owner);

    /// <summary>Equality operator; equivalent to <see cref="Equals(ExclusiveScope)"/>.</summary>
    public static bool operator ==(ExclusiveScope left, ExclusiveScope right) => left.Equals(right);

    /// <summary>Inequality operator; the negation of <see cref="Equals(ExclusiveScope)"/>.</summary>
    public static bool operator !=(ExclusiveScope left, ExclusiveScope right) => !left.Equals(right);
}
