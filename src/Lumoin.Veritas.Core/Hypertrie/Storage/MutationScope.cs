using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A scope handle held while a mutation or a sweep operates on a
/// <see cref="NodeStore"/>. Holding the scope guarantees that no
/// other mutation or sweep is concurrently active on the same
/// store. Reads (snapshots, iterators, queries) take no scope and
/// run concurrently with whoever holds the scope.
/// </summary>
/// <remarks>
/// <para>
/// The scope is acquired through
/// <see cref="NodeStore.EnterMutationScopeAsync"/>. Mutation paths
/// that produce new snapshots (graph builds today, edit sessions
/// in a future batch) and sweeps both go through this method, so
/// they serialise correctly against each other.
/// </para>
/// <para>
/// <b>Disposal contract.</b> The scope must be disposed exactly
/// once. The <c>using</c> declaration handles this automatically.
/// A default-initialised scope holds no semaphore and disposes as
/// a no-op; this keeps the type robust under the <c>default</c>
/// pattern but does not endorse it as a usage shape — callers
/// always go through the acquire method.
/// </para>
/// </remarks>
[DebuggerDisplay("MutationScope")]
internal readonly struct MutationScope: IDisposable
{
    private SemaphoreSlim? Gate { get; }

    private MutationScope(SemaphoreSlim gate)
    {
        Gate = gate;
    }

    /// <summary>
    /// Acquires <paramref name="gate"/> asynchronously and returns
    /// a scope whose disposal releases it.
    /// </summary>
    /// <param name="gate">The semaphore to acquire; must not be <c>null</c>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A scope holding the semaphore.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gate"/> is <c>null</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered before the semaphore was acquired.</exception>
    internal static async ValueTask<MutationScope> AcquireAsync(SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gate);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new MutationScope(gate);
    }

    /// <summary>Releases the held semaphore. A scope that holds no semaphore (a default-initialised value) disposes as a no-op.</summary>
    public void Dispose()
    {
        Gate?.Release();
    }
}
