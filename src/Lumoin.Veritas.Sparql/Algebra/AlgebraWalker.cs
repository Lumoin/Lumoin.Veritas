using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Sparql.Algebra;

/// <summary>
/// Generic traversal and rewriting over a SPARQL algebra tree. Both operations drive the uniform
/// <see cref="AlgebraOperator.Children"/> surface, so no operator needs bespoke walking code and a new
/// operator is covered automatically. <see cref="Traverse"/> enumerates an operator and its descendants;
/// <see cref="Transform"/> rebuilds the tree bottom-up under a rewrite function.
/// </summary>
/// <remarks>
/// The algebra is a tree of immutable operator records, so both members are pure: they hold no state and
/// recursion depth follows the operator nesting (queries are not deeply recursive in practice). SPARQL 1.2
/// §18.6 [SPARQL Algebra]; this is the §10.5 traversal layer.
/// </remarks>
public static class AlgebraWalker
{
    /// <summary>
    /// Enumerates an operator and all its descendants in pre-order — each operator before its children, and
    /// children in evaluation order. The enumeration is lazy.
    /// </summary>
    /// <param name="root">The operator to enumerate from.</param>
    /// <returns>The operator followed by every descendant, in pre-order.</returns>
    public static IEnumerable<AlgebraOperator> Traverse(AlgebraOperator root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Enumerate(root);
    }

    /// <summary>
    /// Rewrites a tree bottom-up: each operator's children are transformed first, the operator is rebuilt
    /// with the transformed children when any changed, and then <paramref name="rewrite"/> is applied to the
    /// (possibly rebuilt) operator. A subtree that <paramref name="rewrite"/> leaves untouched is returned by
    /// reference, so unchanged branches are not reallocated.
    /// </summary>
    /// <param name="root">The operator to rewrite.</param>
    /// <param name="rewrite">The rewrite applied to each operator after its children; return the operator unchanged to leave it as is.</param>
    /// <returns>The rewritten tree, or the same <paramref name="root"/> instance when nothing changed.</returns>
    public static AlgebraOperator Transform(AlgebraOperator root, Func<AlgebraOperator, AlgebraOperator> rewrite)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rewrite);

        return TransformCore(root, rewrite);
    }

    /// <summary>Yields an operator then, recursively, each of its children in evaluation order.</summary>
    /// <param name="operatorNode">The operator to enumerate from.</param>
    /// <returns>The operator and its descendants, in pre-order.</returns>
    private static IEnumerable<AlgebraOperator> Enumerate(AlgebraOperator operatorNode)
    {
        yield return operatorNode;

        foreach(AlgebraOperator child in operatorNode.Children)
        {
            foreach(AlgebraOperator descendant in Enumerate(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Transforms an operator's children bottom-up, rebuilds it if any child changed, then applies the rewrite.</summary>
    /// <param name="operatorNode">The operator to transform.</param>
    /// <param name="rewrite">The rewrite applied after the children are transformed.</param>
    /// <returns>The transformed operator (the same instance when nothing below it and the rewrite both leave it unchanged).</returns>
    private static AlgebraOperator TransformCore(AlgebraOperator operatorNode, Func<AlgebraOperator, AlgebraOperator> rewrite)
    {
        IReadOnlyList<AlgebraOperator> children = operatorNode.Children;
        AlgebraOperator current = operatorNode;

        if(children.Count > 0)
        {
            //Lazily materialise a replacement array only once a child actually changes, copying the
            //unchanged prefix; an all-unchanged child list leaves the operator rebuilt-free.
            AlgebraOperator[]? transformed = null;
            for(int i = 0; i < children.Count; i++)
            {
                AlgebraOperator original = children[i];
                AlgebraOperator rewritten = TransformCore(original, rewrite);

                if(transformed is null && !ReferenceEquals(rewritten, original))
                {
                    transformed = new AlgebraOperator[children.Count];
                    for(int j = 0; j < i; j++)
                    {
                        transformed[j] = children[j];
                    }
                }

                if(transformed is not null)
                {
                    transformed[i] = rewritten;
                }
            }

            if(transformed is not null)
            {
                current = operatorNode.RebuildWithChildren(transformed);
            }
        }

        return rewrite(current);
    }
}
