using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Injects transport faults into a replication sketch fetch: a closure-free decorator that, before each inner
/// <see cref="AsyncSketchFetchDelegate"/>, asks a deterministic <see cref="SketchFetchFaultPlan"/> what to do for
/// this fetch and applies it — pass through, drop to an unavailable result, corrupt the fetched bytes, or fail with
/// an <see cref="InjectedNetworkFaultException"/>, each optionally after an injected latency. It is the test/chaos
/// substrate for certifying that the anti-entropy session converges under an adverse network; its
/// <see cref="FetchAsync"/> is itself an <see cref="AsyncSketchFetchDelegate"/>, so it composes in front of any
/// fetch (and beside <see cref="GovernedSketchFetch"/>).
/// </summary>
/// <remarks>
/// The fault is chosen by 1-based call index against the injected clock, so a scenario is reproducible without
/// randomness or wall-clock waits — matching the engine's value-based, entropy-seamed design. As an explicit
/// binding frame it captures nothing, so it holds no lexical closure. Corrupt copies the fetched image into a
/// pooled buffer, inverts it, and releases the fetched original, so an owned result is never leaked and the
/// corruption stays pool-backed like the path it decorates.
/// </remarks>
public sealed class FaultInjectingSketchFetch
{
    private readonly AsyncSketchFetchDelegate inner;
    private readonly SketchFetchFaultPlan plan;
    private readonly MemoryPool<byte> pool;
    private readonly TimeProvider timeProvider;

    //A naked field: the call counter is advanced with Interlocked, which needs a by-ref target.
    private int callCount;

    /// <summary>Creates a fault-injecting fetch over an inner fetch.</summary>
    /// <param name="inner">The fetch faults are injected around — invoked unless the fault drops or fails it.</param>
    /// <param name="plan">The deterministic plan choosing the fault per 1-based call index.</param>
    /// <param name="pool">The pool the corrupted copy of a fetched image is rented from.</param>
    /// <param name="timeProvider">The clock an injected latency runs against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/>, <paramref name="plan"/>, <paramref name="pool"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public FaultInjectingSketchFetch(AsyncSketchFetchDelegate inner, SketchFetchFaultPlan plan, MemoryPool<byte> pool, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.inner = inner;
        this.plan = plan;
        this.pool = pool;
        this.timeProvider = timeProvider;
    }

    /// <summary>Applies the planned fault for this fetch, then fetches (pass/corrupt), drops to an unavailable result, or fails. An <see cref="AsyncSketchFetchDelegate"/> — pass it to <see cref="AntiEntropySession.ReconcileAsync"/>.</summary>
    /// <param name="symbolBudget">The fetch's symbol budget.</param>
    /// <param name="cancellationToken">The token that cancels the injected latency or the fetch.</param>
    /// <returns>The peer's owned image (passed through or corrupted), or <see cref="SketchFetchResult.Unavailable"/> when the fault drops the fetch.</returns>
    /// <exception cref="InjectedNetworkFaultException">The planned fault fails the fetch.</exception>
    public async ValueTask<SketchFetchResult> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        int callIndex = Interlocked.Increment(ref callCount);
        SketchFetchFault fault = plan(callIndex);
        if(fault.Delay > TimeSpan.Zero)
        {
            await Task.Delay(fault.Delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return fault.Kind switch
        {
            SketchFetchFaultKind.Pass => await inner(symbolBudget, cancellationToken).ConfigureAwait(false),
            SketchFetchFaultKind.Drop => SketchFetchResult.Unavailable,
            SketchFetchFaultKind.Corrupt => Corrupt(await inner(symbolBudget, cancellationToken).ConfigureAwait(false)),
            SketchFetchFaultKind.Fail => throw new InjectedNetworkFaultException($"An injected fault failed the replication fetch at call {callIndex}."),
            _ => await inner(symbolBudget, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Copies the fetched image into a pooled buffer with every byte inverted, so the sketch fails its checksum or decode whatever its layout, preserving the peer's domain-and-epoch stamp so the corruption is caught at the sketch load (not a contract or epoch refusal), and releases the fetched original; a result with no image passes through unchanged (nothing to corrupt, no rental to release), keeping the peer's stamp so a stamped decline still classifies as its named refusal. The length is preserved, so the corruption is distinct from a drop.</summary>
    /// <param name="image">The fetched image to corrupt; disposed here once its bytes have been copied.</param>
    /// <returns>The corrupted, pool-owned copy carrying the original stamp, or the unchanged result when it carried no image.</returns>
    private SketchFetchResult Corrupt(SketchFetchResult image)
    {
        if(!image.HasImage)
        {
            return image;
        }

        ReadOnlyMemory<byte> source = image.Image;
        int length = source.Length;
        IMemoryOwner<byte> owner = pool.Rent(length);

        //The fetched original is released whichever way the copy exits, and a failed copy returns the fresh rental
        //before it throws, so no owned image leaks.
        try
        {
            Span<byte> destination = owner.Memory.Span[..length];
            source.Span.CopyTo(destination);
            for(int i = 0; i < destination.Length; i++)
            {
                destination[i] ^= 0xFF;
            }
        }
        catch
        {
            owner.Dispose();
            throw;
        }
        finally
        {
            image.Dispose();
        }

        return new SketchFetchResult(owner, length, image.Domain, image.DictionaryEpoch);
    }
}
