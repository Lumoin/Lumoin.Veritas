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
    public static RandomSourceDelegate Constant(ulong value) => new ConstantRandomSource(value).Next;

    /// <summary>
    /// A source backed by a seeded <see cref="Random"/>. The same
    /// seed produces the same sequence across runs and machines, so
    /// graphs constructed with this source are reproducible from the
    /// seed alone.
    /// </summary>
    public static RandomSourceDelegate FromSeed(int seed)
    {
        return new SeededRandomSource(seed).Next;
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
    public static RandomSourceDelegate Shared() => static () => unchecked((ulong)Random.Shared.NextInt64());

    /// <summary>Carries the constant returned by <see cref="Constant"/> as explicit state.</summary>
    /// <param name="value">The constant value handed out on every call.</param>
    private sealed class ConstantRandomSource(ulong value)
    {
        /// <summary>The constant value.</summary>
        private ulong Value { get; } = value;

        /// <summary>Returns the constant value.</summary>
        /// <returns>The constant.</returns>
        public ulong Next() => Value;
    }

    /// <summary>Carries the seeded <see cref="Random"/> behind <see cref="FromSeed"/> as explicit state.</summary>
    /// <param name="seed">The seed; the same seed reproduces the same sequence across runs and machines.</param>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Graph generation is not security-sensitive. Using System.Random is the correct choice — graphs are synthetic test data and benchmark fixtures, not security tokens. A cryptographic RNG would be wasteful and would prevent reproducibility from a seed, which is the entire point of this method.")]
    [SuppressMessage(
        "ApiDesign",
        "RS0030:Do not use banned APIs",
        Justification = "RandomSources is synthetic-graph generation, not query randomness, and exposes its own RandomSourceDelegate seam. System.Random here produces graph salt reproducible from the seed; routing it through the entropy ban would defeat the seed-reproducibility this method exists for.")]
    private sealed class SeededRandomSource(int seed)
    {
        /// <summary>The seeded pseudo-random generator.</summary>
        private Random Rng { get; } = new(seed);

        /// <summary>Draws the next value from the seeded generator.</summary>
        /// <returns>The next pseudo-random <see cref="ulong"/>.</returns>
        public ulong Next() => unchecked((ulong)Rng.NextInt64());
    }
}
