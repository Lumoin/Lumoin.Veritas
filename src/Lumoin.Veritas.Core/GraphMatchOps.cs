using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Bundle of the three match delegates that travel together for callers
/// that consult a graph through more than one access pattern: single-pattern
/// lookup, subject-set lookup, and object-set lookup. Constructed once per
/// store via <c>AsMatchOps()</c> and threaded through callers — notably
/// <see cref="Lumoin.Veritas.Rdf.PropertyPathEvaluator"/> — instead of three
/// independent delegate parameters.
/// </summary>
/// <param name="MatchTriples">Single-pattern match. The predicate may be bound or unbound.</param>
/// <param name="MatchTriplesBySubjects">Subject-set lookup under a bound predicate. Performs a single predicate-rooted descent and probes per subject.</param>
/// <param name="MatchTriplesByObjects">Object-set lookup under a bound predicate. Mirror of <paramref name="MatchTriplesBySubjects"/>.</param>
/// <remarks>
/// <para>
/// The three fields are independent delegate references — no construction
/// validation crosses them. A caller that knows it only ever needs
/// <see cref="MatchTriples"/> may still receive a fully-populated bundle;
/// the storage-supplied factory always wires all three.
/// </para>
/// <para>
/// <c>readonly record struct</c> gives value equality over the three
/// delegate references — two bundles produced from the same underlying
/// store compare equal — and zero allocation when passed by value.
/// </para>
/// </remarks>
[DebuggerDisplay("GraphMatchOps")]
public readonly record struct GraphMatchOps(
    StorageDelegates.MatchTriplesAsync MatchTriples,
    StorageDelegates.MatchTriplesBySubjectsAsync MatchTriplesBySubjects,
    StorageDelegates.MatchTriplesByObjectsAsync MatchTriplesByObjects);
