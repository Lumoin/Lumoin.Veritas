using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Pre-resolved SHACL core vocabulary term identifiers used during
/// discovery and population. Resolved once against a
/// <see cref="TermDictionary"/> at loader startup and passed through
/// every phase.
/// </summary>
/// <remarks>
/// <para>
/// Assembling this struct is the loader's first job: the SHACL vocabulary
/// IRIs need to be <see cref="TermDictionary.GetOrAdd"/>'d before any
/// discovery query can reference them. The resulting
/// <see cref="IriId"/> values are then used in every subsequent
/// <see cref="StorageDelegates.MatchTriplesAsync"/> call.
/// </para>
/// <para>
/// <b>Target and shape-reference predicates</b> are lists rather than
/// individual fields because their sets are closed and small (five and
/// eight elements respectively); iterating them once per discovery rule
/// is cheaper than declaring eight named fields plus a concatenation
/// step.
/// </para>
/// </remarks>
/// <param name="Path">The <c>sh:path</c> predicate identifier.</param>
/// <param name="NodeShapeClass">The <c>sh:NodeShape</c> class identifier.</param>
/// <param name="PropertyShapeClass">The <c>sh:PropertyShape</c> class identifier.</param>
/// <param name="ShapeClass">The <c>sh:Shape</c> class identifier.</param>
/// <param name="ShapeClassClass">The <c>sh:ShapeClass</c> class identifier (SHACL 1.2).</param>
/// <param name="PropertyPredicate">The <c>sh:property</c> predicate identifier.</param>
/// <param name="TargetPredicates">
/// The target-declaration predicates: <c>sh:targetClass</c>,
/// <c>sh:targetNode</c>, <c>sh:targetSubjectsOf</c>,
/// <c>sh:targetObjectsOf</c>, <c>sh:targetWhere</c>.
/// </param>
/// <param name="ShapeReferencePredicates">
/// Shape-reference predicates whose object is a shape: <c>sh:node</c>,
/// <c>sh:not</c>, <c>sh:and</c>, <c>sh:or</c>, <c>sh:xone</c>,
/// <c>sh:qualifiedValueShape</c>, <c>sh:reifierShape</c>,
/// <c>sh:memberShape</c>. <c>sh:property</c> is handled separately
/// because the discovered shape is a property shape rather than a node
/// shape.
/// </param>
internal readonly record struct ShaclDiscoveryIds(
    IriId Path,
    IriId NodeShapeClass,
    IriId PropertyShapeClass,
    IriId ShapeClass,
    IriId ShapeClassClass,
    IriId PropertyPredicate,
    IReadOnlyList<IriId> TargetPredicates,
    IReadOnlyList<IriId> ShapeReferencePredicates);
