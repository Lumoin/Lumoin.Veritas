using System;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Memory pools and algorithm choices threaded through a hypertrie
/// build. Bundled at the <c>BuildAsync</c> surface so the build
/// path's resource and dispatch decisions flow top-down without
/// further plumbing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a struct.</b> A single parameter on <c>BuildAsync</c>
/// carries every resource the build path needs. Adding a new pool
/// type or algorithm choice means adding a field here, not adding
/// another method parameter to <c>BuildAsync</c> and the chain of
/// internal helpers below it.
/// </para>
/// <para>
/// <b>Defaults.</b> <see cref="CreateDefault"/> returns a
/// <see cref="BuildPools"/> wired to <see cref="VeritasMemoryPool{T}"/>
/// instances with the default slab-capacity strategy and the
/// <see cref="InlineKeyLookups.SelectBestAvailable"/> lookup.
/// Callers that have already constructed pools (e.g. for a hosted
/// service that shares pools across many builds) construct
/// <see cref="BuildPools"/> directly.
/// </para>
/// <para>
/// <b>Pool lifetime.</b> The <see cref="NodePool"/> reference is
/// retained by <see cref="NodeStore"/> for the lifetime of the
/// store — the segmented arena rents segments from it during
/// build and disposes them on <see cref="NodeStore.Dispose"/>.
/// Consumers that share a single pool across many stores must keep
/// the pool alive at least as long as any store referencing it.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "BuildPools is a composition-root bundle of injected dependencies, not a value-compared type. Two BuildPools instances are equal only if their fields are reference-equal, which the default Equals implementation provides; defining an explicit equality contract on the bundle would suggest a semantic comparison the type doesn't have.")]
public readonly struct BuildPools
{
    /// <summary>Pool used for SortedArray-tier key buffers inside <see cref="EdgeMap"/>.</summary>
    public VeritasMemoryPool<uint> KeyPool { get; }

    /// <summary>Pool used for SortedArray-tier child-handle buffers inside <see cref="EdgeMap"/>.</summary>
    public VeritasMemoryPool<NodeHandle> ChildPool { get; }

    /// <summary>Pool used by <see cref="NodeStore"/> for the segmented <see cref="HypertrieNode"/> arena.</summary>
    public VeritasMemoryPool<HypertrieNode> NodePool { get; }

    /// <summary>
    /// Pool used by the bottom-up build path to materialise PSO and
    /// OSP orderings as index permutations over the canonical SPO
    /// triple array, in place of full <see cref="EncodedTriple"/>[]
    /// copies. One rental per non-canonical ordering, sized to the
    /// distinct triple count.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="KeyPool"/> because the size profile
    /// differs by orders of magnitude: <see cref="KeyPool"/> rents
    /// small (≤ low-hundreds-of-elements) buffers for individual
    /// SortedArray-tier <see cref="EdgeMap"/>s, while permutation
    /// rents scale with the input triple count (1M+ uints possible).
    /// Sharing the pool would force <see cref="VeritasMemoryPool{T}"/>'s
    /// exact-size slab policy to hold one very large slab alongside
    /// many small ones, wasting capacity.
    /// </remarks>
    public VeritasMemoryPool<uint> PermutationPool { get; }

    /// <summary>Implementation used by Inline-tier lookups in <see cref="EdgeMap.TryGetChild"/>.</summary>
    public InlineKeyLookup InlineLookup { get; }

    /// <summary>
    /// Constructs a build-pools bundle. All fields are required.
    /// </summary>
    /// <param name="keyPool">Pool for SortedArray-tier key buffers.</param>
    /// <param name="childPool">Pool for SortedArray-tier child-handle buffers.</param>
    /// <param name="nodePool">Pool used by <see cref="NodeStore"/> for its segmented arena.</param>
    /// <param name="permutationPool">Pool used by the build path to materialise PSO and OSP orderings as index permutations.</param>
    /// <param name="inlineLookup">Inline-tier lookup implementation.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public BuildPools(
        VeritasMemoryPool<uint> keyPool,
        VeritasMemoryPool<NodeHandle> childPool,
        VeritasMemoryPool<HypertrieNode> nodePool,
        VeritasMemoryPool<uint> permutationPool,
        InlineKeyLookup inlineLookup)
    {
        ArgumentNullException.ThrowIfNull(keyPool);
        ArgumentNullException.ThrowIfNull(childPool);
        ArgumentNullException.ThrowIfNull(nodePool);
        ArgumentNullException.ThrowIfNull(permutationPool);
        ArgumentNullException.ThrowIfNull(inlineLookup);

        KeyPool = keyPool;
        ChildPool = childPool;
        NodePool = nodePool;
        PermutationPool = permutationPool;
        InlineLookup = inlineLookup;
    }

    /// <summary>
    /// Constructs a <see cref="BuildPools"/> wired to default-shaped
    /// pools and the best-available inline lookup. Intended for
    /// build paths that do not share pools across multiple builds.
    /// </summary>
    /// <returns>A fresh <see cref="BuildPools"/> with newly-allocated pools.</returns>
    public static BuildPools CreateDefault()
    {
        return new BuildPools(
            new VeritasMemoryPool<uint>(),
            new VeritasMemoryPool<NodeHandle>(),
            new VeritasMemoryPool<HypertrieNode>(),
            new VeritasMemoryPool<uint>(),
            InlineKeyLookups.SelectBestAvailable());
    }
}
