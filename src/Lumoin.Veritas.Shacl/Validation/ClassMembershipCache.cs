using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Per-validation-run cache for <c>sh:class</c> membership decisions.
/// Shared across every <see cref="Evaluators.ClassEvaluator"/>
/// invocation within a single
/// <see cref="ShaclValidator.ValidateAsync"/> call so the subclass
/// closure of any class is computed at most once and each
/// <c>(value, class)</c> membership is decided at most once.
/// </summary>
/// <remarks>
/// <para>
/// The cache is the only mutable surface exposed through
/// <see cref="ValidationContext"/>. All other context fields are
/// treated as read-only by evaluators.
/// </para>
/// <para>
/// Concurrency is not a concern: the orchestrator invokes evaluators
/// sequentially within a single run. If that ever changes, the backing
/// <see cref="Dictionary{TKey, TValue}"/> would need to be swapped for
/// a concurrent map.
/// </para>
/// </remarks>
public sealed class ClassMembershipCache
{
    private readonly Dictionary<(TermId Value, IriId Class), bool> store = [];

    /// <summary>
    /// Looks up a previously-computed membership decision for
    /// <paramref name="value"/> against <paramref name="cls"/>.
    /// </summary>
    /// <param name="value">The candidate value-node identifier.</param>
    /// <param name="cls">The class under test.</param>
    /// <param name="isMember">
    /// When the method returns <c>true</c>, set to the cached decision.
    /// Otherwise set to <c>false</c> and the caller must compute and
    /// store via <see cref="Set"/>.
    /// </param>
    /// <returns><c>true</c> if the cache already holds a decision.</returns>
    public bool TryGet(TermId value, IriId cls, out bool isMember)
        => store.TryGetValue((value, cls), out isMember);

    /// <summary>
    /// Stores a membership decision, overwriting any prior entry for
    /// the same key.
    /// </summary>
    /// <param name="value">The value-node identifier.</param>
    /// <param name="cls">The class under test.</param>
    /// <param name="isMember">The decision to cache.</param>
    public void Set(TermId value, IriId cls, bool isMember)
        => store[(value, cls)] = isMember;

    /// <summary>
    /// The number of decisions currently cached. Useful in tests to
    /// assert that the cache is being populated as expected.
    /// </summary>
    public int Count => store.Count;
}
