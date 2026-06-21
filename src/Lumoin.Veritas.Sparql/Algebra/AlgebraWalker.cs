using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Sparql.Algebra;

/// <summary>Rewrites one algebra operator after its children have been transformed; return the operator unchanged to leave it as is.</summary>
/// <param name="operator">The operator to rewrite.</param>
/// <returns>The rewritten operator, or the same instance to leave it unchanged.</returns>
public delegate AlgebraOperator AlgebraRewrite(AlgebraOperator @operator);

/// <summary>
/// Iterative traversal and rewriting over a SPARQL algebra tree, taking the uniform
/// <see cref="AlgebraOperator.Children"/> surface as the adjacency. Both operations use an explicit work
/// stack rather than call-stack recursion — matching the project's iterative traversal discipline (see
/// <c>Lumoin.Veritas.Core.Algebra.IterativeTraversal</c>) so an adversarially deep plan cannot overflow the
/// stack. <see cref="Traverse"/> enumerates an operator and its descendants; <see cref="Transform"/>
/// rebuilds the tree bottom-up under a rewrite function.
/// </summary>
/// <remarks>
/// <para>
/// The walk has <em>tree</em> semantics: every operator position is visited and rewritten, including two
/// value-equal sibling subtrees (for example the two identical <c>Bgp</c>s of <c>{ A } UNION { A }</c>). It
/// therefore mirrors <c>IterativeTraversal</c>'s explicit-stack technique rather than routing through it,
/// because that primitive's visited set yields each distinct node once (graph/DAG semantics) and would merge
/// positions a plan walk must keep distinct. The adjacency is the fixed <see cref="AlgebraOperator.Children"/>,
/// so no per-operator walking code is needed and a new operator is covered for free.
/// </para>
/// <para>SPARQL 1.2 §18.6 [SPARQL Algebra]; the §10.5 traversal layer.</para>
/// </remarks>
public static class AlgebraWalker
{
    /// <summary>
    /// Enumerates an operator and all its descendants in pre-order — each operator before its children, and
    /// children in evaluation order — using an explicit stack (no recursion). The enumeration is lazy.
    /// </summary>
    /// <param name="root">The operator to enumerate from.</param>
    /// <returns>The operator followed by every descendant, in pre-order.</returns>
    public static IEnumerable<AlgebraOperator> Traverse(AlgebraOperator root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Iterate(root);
    }

    /// <summary>
    /// Rewrites a tree bottom-up using an explicit work stack (no recursion): every operator's children are
    /// transformed first, the operator is rebuilt with the transformed children when any changed, and then
    /// <paramref name="rewrite"/> is applied to the (possibly rebuilt) operator. A subtree that
    /// <paramref name="rewrite"/> leaves untouched is returned by reference, so unchanged branches are not
    /// reallocated.
    /// </summary>
    /// <param name="root">The operator to rewrite.</param>
    /// <param name="rewrite">The rewrite applied to each operator after its children; return the operator unchanged to leave it as is.</param>
    /// <returns>The rewritten tree, or the same <paramref name="root"/> instance when nothing changed.</returns>
    public static AlgebraOperator Transform(AlgebraOperator root, AlgebraRewrite rewrite)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rewrite);

        //Post-order rebuild over an explicit stack: each operator is visited twice — first to schedule its
        //children (the expand phase), then to combine their rewritten results into a rebuilt operator (the
        //combine phase). Rewritten children accumulate on a results stack; because children expand in order,
        //a node's children sit on top of the results stack — last child on top — when its combine phase runs.
        Stack<(AlgebraOperator Node, bool Combine)> work = new();
        Stack<AlgebraOperator> results = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (AlgebraOperator node, bool combine) = work.Pop();
            IReadOnlyList<AlgebraOperator> children = node.Children;

            if(combine)
            {
                //The rewritten children are the top children.Count entries of the results stack, last child
                //on top; pop them back into position order, rebuilding only if one changed.
                AlgebraOperator[] rewritten = new AlgebraOperator[children.Count];
                bool changed = false;
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    AlgebraOperator child = results.Pop();
                    rewritten[i] = child;
                    if(!ReferenceEquals(child, children[i]))
                    {
                        changed = true;
                    }
                }

                AlgebraOperator rebuilt = changed ? node.RebuildWithChildren(rewritten) : node;
                results.Push(rewrite(rebuilt));

                continue;
            }

            if(children.Count == 0)
            {
                //A leaf has no children to await; rewrite it straight onto the results stack.
                results.Push(rewrite(node));
            }
            else
            {
                //Schedule the combine after the children, then push the children so they expand in order
                //(reverse push, since the stack pops last-in first).
                work.Push((node, Combine: true));
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    work.Push((children[i], Combine: false));
                }
            }
        }

        return results.Pop();
    }

    /// <summary>Yields an operator and its descendants in pre-order using an explicit stack.</summary>
    /// <param name="root">The operator to enumerate from.</param>
    /// <returns>The operator and its descendants, in pre-order.</returns>
    private static IEnumerable<AlgebraOperator> Iterate(AlgebraOperator root)
    {
        Stack<AlgebraOperator> stack = new();
        stack.Push(root);

        while(stack.Count > 0)
        {
            AlgebraOperator current = stack.Pop();
            yield return current;

            //Push children in reverse so they pop — and are yielded — in evaluation order.
            IReadOnlyList<AlgebraOperator> children = current.Children;
            for(int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }
}
