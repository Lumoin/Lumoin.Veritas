using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie;

/// <summary>
/// Pins a SET of hypertrie roots in one registration — the
/// dataset-level snapshot. Where a <see cref="HypertrieSnapshot"/>
/// pins one root per object, a root-set pin keeps an arbitrary
/// number of roots sweep-reachable at the cost of one registry
/// entry and one handle array, which is what lets a dataset hold
/// thousands-to-millions of logical graphs without a snapshot and
/// store object per graph.
/// </summary>
/// <remarks>
/// <para>
/// <b>Liveness.</b> Pins register weakly, like snapshots: an
/// explicitly disposed pin deregisters deterministically, and a
/// pin dropped without disposal becomes sweepable once the
/// collector reclaims it. A superseded dataset state is released
/// by simply dropping it — in-flight readers keep their state
/// (and so its pin) reachable for exactly as long as they can
/// still query it.
/// </para>
/// <para>
/// <b>Immutability.</b> The pinned root set is fixed at
/// construction. A new dataset state pins a new set; the old pin
/// dies with the old state.
/// </para>
/// </remarks>
[DebuggerDisplay("HypertrieRootSetPin Roots={Roots.Count}")]
public sealed class HypertrieRootSetPin: IDisposable
{
    //Naked field: Interlocked.Exchange requires ref semantics.
    private int disposed;

    /// <summary>The store whose sweep this pin participates in.</summary>
    public NodeStore Store { get; }

    /// <summary>The pinned root handles.</summary>
    public IReadOnlyList<NodeHandle> Roots { get; }

    /// <summary>
    /// Constructs and registers the pin. Called by
    /// <see cref="NodeStore.PinRoots"/>; consumers do not call this
    /// directly.
    /// </summary>
    /// <param name="store">The store to register with.</param>
    /// <param name="roots">The roots to pin; the array is owned by the pin.</param>
    internal HypertrieRootSetPin(NodeStore store, NodeHandle[] roots)
    {
        Store = store;
        Roots = roots;

        store.RegisterRootSetPin(this);
    }

    /// <summary>
    /// Deregisters the pin, releasing every pinned root to the next
    /// sweep's reachability analysis. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Store.UnregisterRootSetPin(this);
    }
}
