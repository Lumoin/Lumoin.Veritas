using Lumoin.Veritas.Core.Encoding;
using System.Collections.Generic;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Per-validation-run cache for <c>rdfs:subClassOf</c> transitive
/// closures. The cached value is the set of strict SHACL superclasses
/// of a given class — i.e., the result of walking
/// <c>rdfs:subClassOf+</c> from the class through the data graph,
/// excluding the class itself.
/// </summary>
/// <remarks>
/// <para>
/// Sibling of <see cref="ClassMembershipCache"/>. Both caches live on
/// <see cref="ValidationContext"/> and have run-scoped lifetimes;
/// neither is rebuilt mid-run.
/// </para>
/// <para>
/// <b>Sharing across evaluators.</b> The closure of a class is
/// purely a function of the class identifier and the data graph;
/// every evaluator that needs SHACL-instance-of or class-hierarchy
/// reasoning can ask the same cache. Today
/// <see cref="Evaluators.ClassEvaluator"/> and
/// <see cref="Evaluators.RootClassEvaluator"/> consume this cache
/// through the shared
/// <see cref="Evaluators.ClassHierarchyHelpers"/> entry points, but
/// the cache is general — any evaluator following the same usage
/// shape can wire in.
/// </para>
/// <para>
/// <b>Stored value type.</b> The cache returns the underlying
/// <see cref="HashSet{T}"/> rather than an
/// <see cref="System.Collections.Generic.IReadOnlySet{T}"/>
/// projection, matching the project's preference for concrete
/// hash-based set types over interface dispatch on hot paths.
/// Consumers must not mutate the returned set.
/// </para>
/// <para>
/// <b>Concurrency.</b> Single-threaded per run; the orchestrator
/// invokes evaluators sequentially. If parallel evaluation is ever
/// added, the backing dictionary needs to become a concurrent map.
/// </para>
/// </remarks>
public sealed class SubclassClosureCache
{
    private readonly Dictionary<TermId, HashSet<TermId>> store = [];

    /// <summary>
    /// Looks up the previously-computed strict-superclass set for
    /// <paramref name="cls"/>.
    /// </summary>
    /// <param name="cls">The class whose closure is requested.</param>
    /// <param name="strictSuperclasses">
    /// When the method returns <c>true</c>, set to the cached
    /// closure. Otherwise set to <c>null</c> and the caller must
    /// compute and store via <see cref="Set"/>.
    /// </param>
    /// <returns><c>true</c> if the cache holds the closure.</returns>
    public bool TryGet(TermId cls, out HashSet<TermId>? strictSuperclasses) => store.TryGetValue(cls, out strictSuperclasses);

    /// <summary>
    /// Stores the strict-superclass set for <paramref name="cls"/>,
    /// overwriting any prior entry. Callers retain no reference to
    /// the supplied set after handing it to the cache; the cache
    /// holds the instance directly to avoid copying.
    /// </summary>
    /// <param name="cls">The class.</param>
    /// <param name="strictSuperclasses">The strict-superclass set.</param>
    public void Set(TermId cls, HashSet<TermId> strictSuperclasses) => store[cls] = strictSuperclasses;

    /// <summary>
    /// The number of class closures currently cached. Useful in
    /// tests to assert that the cache is being populated as expected.
    /// </summary>
    public int Count => store.Count;
}
