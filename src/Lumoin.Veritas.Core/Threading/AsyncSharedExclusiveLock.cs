using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Threading;

/// <summary>
/// An asynchronous shared/exclusive lock — multiple shared
/// holders may hold the lock concurrently, while exclusive holders
/// hold it alone. Reads (snapshots, iterators, queries) take no
/// lock; only operations that mutate shared state (edit-session
/// commits, sweeps) coordinate through this primitive.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The BCL ships <see cref="ReaderWriterLockSlim"/>
/// for the synchronous case but no async equivalent. Async waits
/// must not bridge to blocking calls (the project does not allow
/// that pattern), so an async-native primitive is required. The
/// implementation is the standard
/// "shared-counter + writer-semaphore + drain-semaphore" shape
/// every async reader/writer lock in the wild uses.
/// </para>
/// <para>
/// <b>Algorithm.</b> The lock holds two semaphores and a counter:
/// <list type="bullet">
///   <item><description><c>WriterGate</c> — a one-permit semaphore that exclusive holders must own. Shared holders take and immediately release this on entry to ensure no exclusive holder is currently active or waiting; this is the writer-priority interlock.</description></item>
///   <item><description><c>DrainGate</c> — a one-permit semaphore an exclusive holder waits on after taking <c>WriterGate</c>, until the shared count drops to zero.</description></item>
///   <item><description><c>SharedCount</c> — an interlocked counter tracking the current number of shared holders.</description></item>
/// </list>
/// <para>
/// <b>Shared acquisition.</b> Wait on <c>WriterGate</c>, increment
/// <c>SharedCount</c>, release <c>WriterGate</c>. The brief
/// occupation of <c>WriterGate</c> means a pending exclusive
/// waiter blocks new shared holders from forming, which is the
/// writer-priority property. If <c>SharedCount</c> is zero before
/// the increment, the shared holder also takes <c>DrainGate</c>
/// (so a future exclusive holder waits for at least this shared
/// holder to leave).
/// </para>
/// <para>
/// <b>Shared release.</b> Decrement <c>SharedCount</c>; if it
/// reaches zero, release <c>DrainGate</c>.
/// </para>
/// <para>
/// <b>Exclusive acquisition.</b> Wait on <c>WriterGate</c>; then
/// wait on <c>DrainGate</c>.
/// </para>
/// <para>
/// <b>Exclusive release.</b> Release <c>DrainGate</c> and then
/// <c>WriterGate</c>.
/// </para>
/// </para>
/// <para>
/// <b>Re-entry.</b> Not supported. A holder that calls a method
/// that itself tries to acquire the lock will deadlock. Mutation
/// paths in the codebase are flat — no re-entry today — so this
/// limitation is documented rather than worked around.
/// </para>
/// <para>
/// <b>Cancellation.</b> Both acquisition methods honour the
/// supplied <see cref="CancellationToken"/>. A cancellation that
/// fires after one inner semaphore has been taken but before the
/// other is taken correctly releases the first before throwing,
/// so no permits are leaked.
/// </para>
/// <para>
/// <b>Disposal.</b> The lock is <see cref="IDisposable"/> because
/// it owns two <see cref="SemaphoreSlim"/> instances. Dispose is
/// safe to call only when no scope is held; concurrent disposal
/// is a contract violation. Disposing more than once is a no-op.
/// </para>
/// </remarks>
[DebuggerDisplay("AsyncSharedExclusiveLock SharedCount={SharedCount}")]
public sealed class AsyncSharedExclusiveLock: IDisposable
{
    //The writer-priority interlock. Held briefly by shared
    //acquirers and for the full duration by exclusive holders.
    private SemaphoreSlim WriterGate { get; } = new(initialCount: 1, maxCount: 1);

    //Held while at least one shared holder is active. Exclusive
    //acquirers wait on this after taking WriterGate.
    private SemaphoreSlim DrainGate { get; } = new(initialCount: 1, maxCount: 1);

    private int sharedCount;

    private int disposed;

    /// <summary>
    /// The current number of shared holders. Diagnostic only;
    /// callers must not synchronise on this value.
    /// </summary>
    public int SharedCount => Volatile.Read(ref sharedCount);

    /// <summary>
    /// Acquires the lock in shared mode. Multiple shared scopes
    /// may be active at the same time; an exclusive scope blocks
    /// every shared scope until released.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A scope handle whose disposal releases the shared lock.</returns>
    public async ValueTask<SharedScope> EnterSharedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        //Pass through WriterGate to interlock with any pending or
        //active exclusive holder. If we acquire it, an exclusive
        //holder is not currently active and will not start until
        //we release.
        await WriterGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            int observed = Interlocked.Increment(ref sharedCount);

            if(observed == 1)
            {
                //We are the first shared holder — take DrainGate so
                //a future exclusive holder will wait until we leave.
                //If the DrainGate take throws (cancellation), unwind
                //the count and rethrow without holding either gate.
                try
                {
                    await DrainGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    Interlocked.Decrement(ref sharedCount);
                    throw;
                }
            }
        }
        finally
        {
            //Release WriterGate regardless: the writer-priority
            //interlock is only held momentarily by shared acquirers.
            WriterGate.Release();
        }

        return new SharedScope(this);
    }

    /// <summary>
    /// Acquires the lock in exclusive mode. Blocks until every
    /// active shared holder has released and no other exclusive
    /// holder is active. New shared holders cannot start while an
    /// exclusive acquirer is waiting.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A scope handle whose disposal releases the exclusive lock.</returns>
    public async ValueTask<ExclusiveScope> EnterExclusiveAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        //Take WriterGate first — this blocks future shared holders
        //(writer priority) and serialises with any other exclusive
        //holder.
        await WriterGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        //Then wait for the drain — every active shared holder must
        //leave before we proceed. If the drain wait throws
        //(cancellation), release WriterGate before rethrowing.
        try
        {
            await DrainGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            WriterGate.Release();
            throw;
        }

        return new ExclusiveScope(this);
    }

    internal void ReleaseShared()
    {
        int observed = Interlocked.Decrement(ref sharedCount);
        Debug.Assert(observed >= 0, "Shared count went negative — release imbalance.");

        if(observed == 0)
        {
            DrainGate.Release();
        }
    }

    internal void ReleaseExclusive()
    {
        DrainGate.Release();
        WriterGate.Release();
    }

    /// <summary>Releases the resources owned by this lock. Disposing more than once is a no-op.</summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        WriterGate.Dispose();
        DrainGate.Dispose();
    }
}
