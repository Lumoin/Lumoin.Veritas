using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Shared helpers for class-hierarchy reasoning. The
/// <c>rdfs:subClassOf*</c> closure of a class and the
/// "is value a SHACL instance of class" decision are both common to
/// multiple evaluators (<see cref="ClassEvaluator"/>,
/// <see cref="RootClassEvaluator"/>, and any future class-aware
/// evaluator). Centralising them here ensures consistent semantics
/// and unified cache reuse.
/// </summary>
/// <remarks>
/// <para>
/// All graph traversal goes through
/// <see cref="TraversalPrimitives.TransitiveClosureAsync{TNode, TLabel}"/>
/// with <see cref="RdfAdjacencyAdapter.ForwardAsync"/> bound to the
/// data graph. No recursion; the primitive is BFS over an explicit
/// queue with a cycle-guarding visited set.
/// </para>
/// <para>
/// <b>Cache semantics.</b>
/// <see cref="GetStrictSuperclassesAsync"/> materialises the closure
/// of a class and stores the result on
/// <see cref="SubclassClosureCache"/>; subsequent calls for the same
/// class hit the cache.
/// <see cref="IsInstanceOfAsync"/> first consults
/// <see cref="ClassMembershipCache"/> for the
/// <c>(value, targetClass)</c> decision; on a miss, it walks the
/// value's <c>rdf:type</c> assertions, consulting the closure cache
/// for each asserted type's superclass set. The final decision is
/// written back to the membership cache.
/// </para>
/// <para>
/// <b>Type choice.</b> The class being walked is identified by
/// <see cref="TermId"/>, matching what
/// <see cref="TraversalPrimitives"/> and
/// <see cref="RdfAdjacencyAdapter"/> operate on natively. Predicate
/// identifiers (<c>rdf:type</c>, <c>rdfs:subClassOf</c>) remain
/// <see cref="IriId"/> because the storage layer indexes triples by
/// predicate label.
/// </para>
/// </remarks>
public static class ClassHierarchyHelpers
{
    /// <summary>
    /// Returns the set of strict SHACL superclasses of
    /// <paramref name="cls"/> — the result of walking
    /// <c>rdfs:subClassOf+</c> from <paramref name="cls"/> through
    /// the data graph, excluding <paramref name="cls"/> itself.
    /// Cached per-run.
    /// </summary>
    /// <param name="cls">The class whose closure is requested.</param>
    /// <param name="rdfsSubClassOfId">
    /// The pre-resolved <c>rdfs:subClassOf</c> predicate identifier.
    /// </param>
    /// <param name="adapter">The adjacency adapter bound to the data graph.</param>
    /// <param name="cache">The closure cache.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The strict-superclass set. The caller must not mutate it.</returns>
    public static async ValueTask<HashSet<TermId>> GetStrictSuperclassesAsync(
        TermId cls,
        IriId rdfsSubClassOfId,
        RdfAdjacencyAdapter adapter,
        SubclassClosureCache cache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(cache);

        if(cache.TryGet(cls, out HashSet<TermId>? cached) && cached is not null)
        {
            return cached;
        }

        HashSet<TermId> result = [];

        await foreach(TermId superclass in TraversalPrimitives.TransitiveClosureAsync(
            cls, rdfsSubClassOfId, adapter.ForwardAsync, cancellationToken).ConfigureAwait(false))
        {
            result.Add(superclass);
        }

        cache.Set(cls, result);

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="value"/> is a SHACL
    /// instance of <paramref name="targetClass"/>. The decision is
    /// memoised in <paramref name="membershipCache"/>; per-asserted-type
    /// closures are memoised in <paramref name="closureCache"/>.
    /// </summary>
    /// <param name="value">The value-node identifier.</param>
    /// <param name="targetClass">The target class identifier.</param>
    /// <param name="rdfTypeId">The pre-resolved <c>rdf:type</c> predicate.</param>
    /// <param name="rdfsSubClassOfId">The pre-resolved <c>rdfs:subClassOf</c> predicate.</param>
    /// <param name="adapter">The adjacency adapter.</param>
    /// <param name="membershipCache">The (value, class) membership cache.</param>
    /// <param name="closureCache">The (class) strict-superclass cache.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>
    /// <c>true</c> if some asserted <c>rdf:type</c> of
    /// <paramref name="value"/> equals <paramref name="targetClass"/>
    /// or has it as a strict superclass; otherwise <c>false</c>.
    /// </returns>
    public static async ValueTask<bool> IsInstanceOfAsync(
        TermId value,
        IriId targetClass,
        IriId rdfTypeId,
        IriId rdfsSubClassOfId,
        RdfAdjacencyAdapter adapter,
        ClassMembershipCache membershipCache,
        SubclassClosureCache closureCache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(membershipCache);
        ArgumentNullException.ThrowIfNull(closureCache);

        if(membershipCache.TryGet(value, targetClass, out bool cached))
        {
            return cached;
        }

        bool isMember = false;
        TermId target = targetClass.Value;

        //Walk the value's rdf:type assertions. For each direct type
        //we test reflexive equality with the target, otherwise we
        //consult the closure cache for the asserted type's strict
        //superclass set and check membership there.
        await foreach(TermId assertedType in adapter.ForwardAsync(
            value, rdfTypeId, cancellationToken).ConfigureAwait(false))
        {
            if(assertedType.Equals(target))
            {
                isMember = true;
                break;
            }

            HashSet<TermId> superclasses = await GetStrictSuperclassesAsync(
                assertedType, rdfsSubClassOfId, adapter, closureCache, cancellationToken).ConfigureAwait(false);

            if(superclasses.Contains(target))
            {
                isMember = true;
                break;
            }
        }

        membershipCache.Set(value, targetClass, isMember);

        return isMember;
    }
}
