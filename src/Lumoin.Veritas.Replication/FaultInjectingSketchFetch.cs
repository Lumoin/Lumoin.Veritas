using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Injects transport faults into a replication sketch fetch: a closure-free decorator that, before each inner
/// <see cref="AsyncSketchFetchDelegate"/>, asks a deterministic <see cref="SketchFetchFaultPlan"/> what to do for
/// this fetch and applies it — pass through, drop to an empty image, corrupt the fetched bytes, or fail with an
/// <see cref="InjectedNetworkFaultException"/>, each optionally after an injected latency. It is the test/chaos
/// substrate for certifying that the anti-entropy session converges under an adverse network; its
/// <see cref="FetchAsync"/> is itself an <see cref="AsyncSketchFetchDelegate"/>, so it composes in front of any
/// fetch (and beside <see cref="GovernedSketchFetch"/>).
/// </summary>
/// <remarks>
/// The fault is chosen by 1-based call index against the injected clock, so a scenario is reproducible without
/// randomness or wall-clock waits — matching the engine's value-based, entropy-seamed design. As an explicit
/// binding frame it captures nothing, so it holds no lexical closure.
/// </remarks>
public sealed class FaultInjectingSketchFetch
{
    private readonly AsyncSketchFetchDelegate inner;
    private readonly SketchFetchFaultPlan plan;
    private readonly TimeProvider timeProvider;

    //A naked field: the call counter is advanced with Interlocked, which needs a by-ref target.
    private int callCount;

    /// <summary>Creates a fault-injecting fetch over an inner fetch.</summary>
    /// <param name="inner">The fetch faults are injected around — invoked unless the fault drops or fails it.</param>
    /// <param name="plan">The deterministic plan choosing the fault per 1-based call index.</param>
    /// <param name="timeProvider">The clock an injected latency runs against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/>, <paramref name="plan"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public FaultInjectingSketchFetch(AsyncSketchFetchDelegate inner, SketchFetchFaultPlan plan, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.inner = inner;
        this.plan = plan;
        this.timeProvider = timeProvider;
    }

    /// <summary>Applies the planned fault for this fetch, then fetches (pass/corrupt), drops to empty, or fails. An <see cref="AsyncSketchFetchDelegate"/> — pass it to <see cref="AntiEntropySession.ReconcileAsync"/>.</summary>
    /// <param name="symbolBudget">The fetch's symbol budget.</param>
    /// <param name="cancellationToken">The token that cancels the injected latency or the fetch.</param>
    /// <returns>The peer's image (passed through or corrupted), or an empty image when the fault drops the fetch.</returns>
    /// <exception cref="InjectedNetworkFaultException">The planned fault fails the fetch.</exception>
    public async ValueTask<ReadOnlyMemory<byte>> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
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
            SketchFetchFaultKind.Drop => ReadOnlyMemory<byte>.Empty,
            SketchFetchFaultKind.Corrupt => Corrupt(await inner(symbolBudget, cancellationToken).ConfigureAwait(false)),
            SketchFetchFaultKind.Fail => throw new InjectedNetworkFaultException($"An injected fault failed the replication fetch at call {callIndex}."),
            _ => await inner(symbolBudget, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Returns a copy of the image with every byte inverted, so the sketch fails its checksum or decode whatever its layout; an empty image is returned unchanged (nothing to corrupt). The length is preserved, so the corruption is distinct from a drop.</summary>
    /// <param name="image">The fetched image to corrupt.</param>
    /// <returns>The corrupted copy, or the empty image unchanged.</returns>
    private static ReadOnlyMemory<byte> Corrupt(ReadOnlyMemory<byte> image)
    {
        if(image.IsEmpty)
        {
            return image;
        }

        byte[] mutated = image.ToArray();
        for(int i = 0; i < mutated.Length; i++)
        {
            mutated[i] ^= 0xFF;
        }

        return mutated;
    }
}
