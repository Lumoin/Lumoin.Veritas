using System;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// A pluggable source of pseudo-random <see cref="ulong"/> values.
/// </summary>
/// <remarks>
/// <para>
/// Random graph generators take a
/// <see cref="RandomSourceDelegate"/> at construction and call it
/// once to obtain a salt. The salt is then used as a deterministic
/// hash input for every per-edge decision, so the
/// <see cref="GraphSource{TNode}.Adjacency"/> and
/// <see cref="GraphSource{TNode}.Edges"/> views always agree
/// regardless of access order — both compute the same function of
/// <c>(source, target, salt)</c>.
/// </para>
/// <para>
/// Returning a constant produces a deterministic, reproducible graph
/// keyed on that constant; wrapping a seeded <see cref="Random"/>
/// produces a different graph per seed but reproducible across runs;
/// wrapping <see cref="Random.Shared"/> produces a different graph
/// each run.
/// </para>
/// </remarks>
/// <returns>A <see cref="ulong"/> drawn from this source.</returns>
public delegate ulong RandomSourceDelegate();

/// <summary>
/// Common <see cref="RandomSourceDelegate"/> constructors.
/// </summary>
public static class RandomSources
{
    /// <summary>
    /// A source that always returns <paramref name="value"/>. Useful
    /// for deterministic tests, reproducibility checks, and the
    /// degenerate-graph cases (constant-zero salt makes most random
    /// generators emit empty graphs; constant-MaxValue typically emits
    /// dense ones).
    /// </summary>
    public static RandomSourceDelegate Constant(ulong value) => () => value;

    /// <summary>
    /// A source backed by a seeded <see cref="Random"/>. The same
    /// seed produces the same sequence across runs and machines, so
    /// graphs constructed with this source are reproducible from the
    /// seed alone.
    /// </summary>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Graph generation is not security-sensitive. Using System.Random is the correct choice — graphs are synthetic test data and benchmark fixtures, not security tokens. A cryptographic RNG would be wasteful and would prevent reproducibility from a seed, which is the entire point of this method.")]
    [SuppressMessage(
        "ApiDesign",
        "RS0030:Do not use banned APIs",
        Justification = "RandomSources is synthetic-graph generation, not query randomness, and exposes its own RandomSourceDelegate seam. System.Random here produces graph salt reproducible from the seed; routing it through the entropy ban would defeat the seed-reproducibility this method exists for.")]
    public static RandomSourceDelegate FromSeed(int seed)
    {
        Random rng = new(seed);
        return () => unchecked((ulong)rng.NextInt64());
    }

    /// <summary>
    /// A source backed by <see cref="Random.Shared"/>. Thread-safe
    /// but non-reproducible across runs. Suitable for one-off
    /// generation; not suitable for tests that must replay the same
    /// graph.
    /// </summary>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Graph generation is not security-sensitive. Random.Shared is the standard non-cryptographic RNG and is the correct choice for synthetic graph data.")]
    [SuppressMessage(
        "ApiDesign",
        "RS0030:Do not use banned APIs",
        Justification = "RandomSources is synthetic-graph generation, not query randomness, and exposes its own RandomSourceDelegate seam. Random.Shared here produces graph salt, not identities or security tokens.")]
    public static RandomSourceDelegate Shared() => () => unchecked((ulong)Random.Shared.NextInt64());
}
