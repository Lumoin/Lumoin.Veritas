using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Counts the value nodes conforming to a qualified value shape, with
/// optional sibling-disjoint subtraction. Shared by
/// <see cref="QualifiedMinCountEvaluator"/> and
/// <see cref="QualifiedMaxCountEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both qualified-cardinality evaluators perform exactly the same
/// counting; only the comparison against the bound differs. Pulling
/// the count into a shared helper keeps the per-evaluator code thin
/// and ensures the two evaluators agree on edge cases — particularly
/// the sibling-disjoint subtraction, which is the only non-trivial
/// piece.
/// </para>
/// <para>
/// <b>Sibling-disjoint subtraction.</b> Per SHACL 1.2 Core §4.7.4,
/// when <c>sh:qualifiedValueShapesDisjoint</c> is <c>true</c>, a
/// value node only counts if it conforms to the inner shape AND does
/// not also conform to any sibling qualified value shape. A sibling
/// is another <c>sh:qualifiedValueShape</c> declared on a property
/// shape that shares a parent node shape with the constraint's
/// containing shape (where the parent links via <c>sh:property</c>).
/// </para>
/// <para>
/// <b>Walking siblings.</b> The current implementation locates
/// siblings by linear scan of all shapes in the registry, gated on
/// <c>Disjoint=true</c>. The walk is bounded — three levels:
/// (parent shapes that reference the containing shape via
/// <c>sh:property</c>) → (the parents' other property-shape
/// references) → (those property shapes' qualified-value-shape ids).
/// In typical SHACL graphs all three levels have small cardinality.
/// At very large scale, the registry could expose an inverse
/// <c>sh:property</c> index to make level one <c>O(1)</c>; the helper
/// here would consume that index without changing its semantics.
/// </para>
/// <para>
/// <b>Recursion contract.</b> Matches <see cref="NodeEvaluator"/> and
/// <see cref="AndEvaluator"/>: invokes
/// <see cref="ValidationContext.ShapeValidator"/> on each value node
/// against the candidate inner shape; an empty result array indicates
/// conformance.
/// </para>
/// </remarks>
internal static class QualifiedValueShapeCounting
{
    /// <summary>
    /// Result of a counting attempt.
    /// </summary>
    /// <param name="WasEvaluable">
    /// <c>true</c> if the count was computed; <c>false</c> if the
    /// inner shape was unresolvable or the recursion delegate was
    /// missing.
    /// </param>
    /// <param name="ConformingCount">
    /// Number of value nodes counted as conforming, after sibling
    /// subtraction when applicable. Meaningful only when
    /// <see cref="WasEvaluable"/> is <c>true</c>.
    /// </param>
    public readonly record struct CountResult(bool WasEvaluable, int ConformingCount);

    /// <summary>
    /// Counts conforming value nodes for a qualified-cardinality
    /// constraint. Applies sibling-disjoint subtraction when
    /// <paramref name="disjoint"/> is <c>true</c>.
    /// </summary>
    /// <param name="containingShape">
    /// The shape on which the qualified-cardinality constraint is
    /// declared. Used to discover sibling shapes for disjoint
    /// subtraction.
    /// </param>
    /// <param name="valueShapeId">The inner shape's term id.</param>
    /// <param name="disjoint">Whether sibling subtraction applies.</param>
    /// <param name="valueNodes">The value nodes to count over.</param>
    /// <param name="context">Validation context (provides registry and recursion).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async ValueTask<CountResult> CountAsync(
        Shape containingShape,
        TermId valueShapeId,
        bool disjoint,
        ImmutableArray<TermId> valueNodes,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(containingShape);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null)
        {
            return new CountResult(WasEvaluable: false, ConformingCount: 0);
        }

        if(!context.Shapes.TryGetShape(valueShapeId, out Shape? innerShape))
        {
            return new CountResult(WasEvaluable: false, ConformingCount: 0);
        }

        ImmutableArray<Shape> siblingShapes = disjoint
            ? CollectSiblingValueShapes(containingShape, valueShapeId, context)
            : [];

        int conformingCount = 0;
        foreach(TermId value in valueNodes)
        {
            ImmutableArray<ValidationResult> innerResults =
                await recurse(innerShape, value, cancellationToken).ConfigureAwait(false);

            if(!innerResults.IsEmpty)
            {
                continue;
            }

            //Inner conforms. Apply sibling-disjoint subtraction.
            if(siblingShapes.Length > 0 && await ConformsToAnySiblingAsync(
                value, siblingShapes, recurse, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            conformingCount++;
        }

        return new CountResult(WasEvaluable: true, ConformingCount: conformingCount);
    }

    /// <summary>
    /// Collects the sibling qualified value shapes of the constraint declared
    /// on <paramref name="containingShape"/>, for the sibling-disjoint
    /// subtraction of SHACL 1.2 Core §4.7.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sibling is another <c>sh:qualifiedValueShape</c> on a property shape
    /// that shares a parent node shape — linked via <c>sh:property</c> — with
    /// the containing shape. The search is bounded to three levels (parents
    /// referencing the containing shape, their other property-shape
    /// references, and those shapes' qualified-value-shape ids) and locates
    /// them by linear scan of the registry; <paramref name="ownValueShapeId"/>
    /// is excluded. At very large scale the registry could expose an inverse
    /// <c>sh:property</c> index to make level one <c>O(1)</c>; this helper
    /// would consume that index without changing its semantics.
    /// </para>
    /// <para>
    /// Exposed as <c>internal</c> rather than <c>private</c> so the validator's
    /// dependency-discovery pass reuses the exact sibling set this counting
    /// helper consumes — a single source of truth that keeps discovery and
    /// counting from disagreeing about which siblings exist.
    /// </para>
    /// </remarks>
    /// <param name="containingShape">The shape the qualified-cardinality constraint is declared on.</param>
    /// <param name="ownValueShapeId">The constraint's own value-shape id, excluded from the result.</param>
    /// <param name="context">Validation context providing the shape registry.</param>
    /// <returns>The sibling qualified value shapes, empty when there are none.</returns>
    internal static ImmutableArray<Shape> CollectSiblingValueShapes(
        Shape containingShape,
        TermId ownValueShapeId,
        ValidationContext context)
    {
        HashSet<TermId> siblingIds = [];

        foreach(Shape candidateParent in context.Shapes.AllShapes)
        {
            if(!HasPropertyReferenceTo(candidateParent, containingShape.Id))
            {
                continue;
            }

            //candidateParent is a parent of containingShape. Walk its
            //sibling property-shape references and harvest their
            //qualified-value-shape ids.
            foreach(ConstraintComponent siblingConstraint in candidateParent.Constraints)
            {
                if(siblingConstraint is not PropertyConstraint siblingProperty)
                {
                    continue;
                }

                if(siblingProperty.PropertyShapeId.Equals(containingShape.Id))
                {
                    //The constraint's own containing-shape reference;
                    //its qualified value shape is the one we are
                    //computing siblings for, not a sibling itself.
                    continue;
                }

                if(!context.Shapes.TryGetShape(siblingProperty.PropertyShapeId, out Shape? siblingPropertyShape))
                {
                    continue;
                }

                foreach(ConstraintComponent inner in siblingPropertyShape.Constraints)
                {
                    TermId? siblingValueShapeId = inner switch
                    {
                        QualifiedMinCountConstraint min => min.ValueShapeId,
                        QualifiedMaxCountConstraint max => max.ValueShapeId,
                        _ => null,
                    };

                    if(siblingValueShapeId is { } id && !id.Equals(ownValueShapeId))
                    {
                        siblingIds.Add(id);
                    }
                }
            }
        }

        if(siblingIds.Count == 0)
        {
            return [];
        }

        ImmutableArray<Shape>.Builder builder = ImmutableArray.CreateBuilder<Shape>(siblingIds.Count);
        foreach(TermId siblingId in siblingIds)
        {
            if(context.Shapes.TryGetShape(siblingId, out Shape? siblingShape))
            {
                builder.Add(siblingShape);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="shape"/> has any
    /// <c>sh:property</c> constraint pointing at <paramref name="targetId"/>.
    /// </summary>
    /// <remarks>
    /// Linear scan of the shape's constraints; constraint counts are small in
    /// practice.
    /// </remarks>
    /// <param name="shape">The candidate parent shape.</param>
    /// <param name="targetId">The property-shape id to look for.</param>
    /// <returns><c>true</c> when a matching <c>sh:property</c> reference exists.</returns>
    private static bool HasPropertyReferenceTo(Shape shape, TermId targetId)
    {
        foreach(ConstraintComponent constraint in shape.Constraints)
        {
            if(constraint is PropertyConstraint propertyConstraint
                && propertyConstraint.PropertyShapeId.Equals(targetId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> conforms to at least
    /// one of the sibling inner shapes, short-circuiting on the first match.
    /// </summary>
    /// <param name="value">The value node tested against the siblings.</param>
    /// <param name="siblings">The sibling shapes to test conformance against.</param>
    /// <param name="recurse">The shape-validation delegate used to test each sibling.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns><c>true</c> when the value conforms to any sibling.</returns>
    private static async ValueTask<bool> ConformsToAnySiblingAsync(
        TermId value,
        ImmutableArray<Shape> siblings,
        ShapeValidatorDelegate recurse,
        CancellationToken cancellationToken)
    {
        foreach(Shape sibling in siblings)
        {
            ImmutableArray<ValidationResult> siblingResults =
                await recurse(sibling, value, cancellationToken).ConfigureAwait(false);

            if(siblingResults.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }
}
