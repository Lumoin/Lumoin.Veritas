using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// Iterative traversal and rewriting over a SPARQL <see cref="ExpressionNode"/> tree, the expression-side
/// analogue of <c>AlgebraWalker</c>. Both operations use an explicit work stack rather than call-stack
/// recursion — matching the project's iterative traversal discipline — so a deeply nested expression cannot
/// overflow the stack. <see cref="Traverse"/> enumerates an expression and its sub-expressions;
/// <see cref="Transform"/> rebuilds the tree bottom-up under a rewrite function.
/// </summary>
/// <remarks>
/// <para>
/// The adjacency is the per-type <see cref="Children"/> switch (the dual <see cref="Rebuild"/> reconstructs a
/// node from rewritten children), so the closed expression hierarchy is covered in one place. The walk has
/// <em>tree</em> semantics: every position is visited and rewritten, including two value-equal sibling
/// sub-expressions.
/// </para>
/// <para>
/// An <see cref="AggregateExpression"/> is a <em>leaf</em> for the walk: its <see cref="AggregateExpression.Argument"/>
/// is not descended into, because the aggregation translation replaces an aggregate wholesale with a reference
/// to its result variable rather than rewriting inside it. Likewise <see cref="ExistsExpression"/> /
/// <see cref="NotExistsExpression"/> (which carry a graph pattern, not sub-expressions) and
/// <see cref="TripleTermExpression"/> (which carries a triple pattern) are leaves.
/// </para>
/// <para>SPARQL <c>Expression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rExpression">SPARQL 1.2 §19.8 [Expression]</see>.</para>
/// </remarks>
public static class ExpressionWalker
{
    /// <summary>The empty child list shared by leaf expressions.</summary>
    private static IReadOnlyList<ExpressionNode> NoChildren { get; } = [];

    /// <summary>
    /// Enumerates an expression and all its sub-expressions in pre-order — each node before its children, and
    /// children in source order — using an explicit stack (no recursion). The enumeration is lazy.
    /// </summary>
    /// <param name="root">The expression to enumerate from.</param>
    /// <returns>The expression followed by every sub-expression, in pre-order.</returns>
    public static IEnumerable<ExpressionNode> Traverse(ExpressionNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Iterate(root);
    }

    /// <summary>
    /// Rewrites an expression bottom-up using an explicit work stack (no recursion): every node's children are
    /// transformed first, the node is rebuilt with the transformed children when any changed, and then
    /// <paramref name="rewrite"/> is applied to the (possibly rebuilt) node. A sub-expression that
    /// <paramref name="rewrite"/> leaves untouched is returned by reference, so an unchanged tree is not
    /// reallocated.
    /// </summary>
    /// <param name="root">The expression to rewrite.</param>
    /// <param name="rewrite">The rewrite applied to each node after its children; return the node unchanged to leave it as is.</param>
    /// <returns>The rewritten expression, or the same <paramref name="root"/> instance when nothing changed.</returns>
    public static ExpressionNode Transform(ExpressionNode root, Func<ExpressionNode, ExpressionNode> rewrite)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rewrite);

        //Post-order rebuild over an explicit stack: each node is visited twice — first to schedule its children
        //(the expand phase), then to combine their rewritten results into a rebuilt node (the combine phase).
        //Rewritten children accumulate on a results stack; because children expand in order, a node's children
        //sit on top of the results stack — last child on top — when its combine phase runs.
        Stack<(ExpressionNode Node, bool Combine)> work = new();
        Stack<ExpressionNode> results = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (ExpressionNode node, bool combine) = work.Pop();
            IReadOnlyList<ExpressionNode> children = Children(node);

            if(combine)
            {
                //The rewritten children are the top children.Count entries of the results stack, last child on
                //top; pop them back into position order, rebuilding only if one changed.
                ExpressionNode[] rewritten = new ExpressionNode[children.Count];
                bool changed = false;
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    ExpressionNode child = results.Pop();
                    rewritten[i] = child;
                    if(!ReferenceEquals(child, children[i]))
                    {
                        changed = true;
                    }
                }

                ExpressionNode rebuilt = changed ? Rebuild(node, rewritten) : node;
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

    /// <summary>Yields an expression and its sub-expressions in pre-order using an explicit stack.</summary>
    /// <param name="root">The expression to enumerate from.</param>
    /// <returns>The expression and its sub-expressions, in pre-order.</returns>
    private static IEnumerable<ExpressionNode> Iterate(ExpressionNode root)
    {
        Stack<ExpressionNode> stack = new();
        stack.Push(root);

        while(stack.Count > 0)
        {
            ExpressionNode current = stack.Pop();
            yield return current;

            //Push children in reverse so they pop — and are yielded — in source order.
            IReadOnlyList<ExpressionNode> children = Children(current);
            for(int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    /// <summary>Returns the immediate sub-expressions of a node, in source order; empty for a leaf.</summary>
    /// <param name="node">The expression node.</param>
    /// <returns>The node's sub-expressions, in source order.</returns>
    private static IReadOnlyList<ExpressionNode> Children(ExpressionNode node)
    {
        return node switch
        {
            AndExpression and => [and.Left, and.Right],
            OrExpression or => [or.Left, or.Right],
            NotExpression not => [not.Inner],
            ComparisonExpression comparison => [comparison.Left, comparison.Right],
            ArithmeticExpression arithmetic => arithmetic.Right is null ? [arithmetic.Left] : [arithmetic.Left, arithmetic.Right],
            InExpression test => Prepend(test.Value, test.Set),
            NotInExpression test => Prepend(test.Value, test.Set),
            FunctionCallExpression call => call.Arguments,
            BuiltInCallExpression call => call.Arguments,
            IfExpression conditional => [conditional.Condition, conditional.IfTrue, conditional.IfFalse],
            CoalesceExpression coalesce => coalesce.Alternatives,

            //Leaves: ConstantExpression, VariableExpression, BoundExpression, TripleTermExpression,
            //ExistsExpression, NotExistsExpression, and AggregateExpression (replaced wholesale, never descended).
            _ => NoChildren
        };
    }

    /// <summary>Rebuilds a node from its rewritten children, preserving every non-expression field; leaves return themselves.</summary>
    /// <param name="node">The original node (the source of the preserved fields and the list arities).</param>
    /// <param name="children">The rewritten children, in the order <see cref="Children"/> produced them.</param>
    /// <returns>The rebuilt node.</returns>
    private static ExpressionNode Rebuild(ExpressionNode node, ExpressionNode[] children)
    {
        return node switch
        {
            AndExpression and => new AndExpression(and.Span, children[0], children[1]),
            OrExpression or => new OrExpression(or.Span, children[0], children[1]),
            NotExpression not => new NotExpression(not.Span, children[0]),
            ComparisonExpression comparison => new ComparisonExpression(comparison.Span, children[0], comparison.Op, children[1]),
            ArithmeticExpression arithmetic => arithmetic.Right is null
                ? new ArithmeticExpression(arithmetic.Span, children[0], arithmetic.Op, Right: null)
                : new ArithmeticExpression(arithmetic.Span, children[0], arithmetic.Op, children[1]),
            InExpression test => new InExpression(test.Span, children[0], Tail(children)),
            NotInExpression test => new NotInExpression(test.Span, children[0], Tail(children)),
            FunctionCallExpression call => new FunctionCallExpression(call.Span, call.Function, children),
            BuiltInCallExpression call => new BuiltInCallExpression(call.Span, call.Function, children, call.IsDistinct),
            IfExpression conditional => new IfExpression(conditional.Span, children[0], children[1], children[2]),
            CoalesceExpression coalesce => new CoalesceExpression(coalesce.Span, children),

            //A leaf has no children; it is its own rebuild.
            _ => node
        };
    }

    /// <summary>Builds a child list of a leading value followed by a set of items (the <c>IN</c> / <c>NOT IN</c> shape).</summary>
    /// <param name="head">The leading value expression.</param>
    /// <param name="tail">The trailing set expressions.</param>
    /// <returns>A list holding <paramref name="head"/> then every item of <paramref name="tail"/>.</returns>
    private static List<ExpressionNode> Prepend(ExpressionNode head, IReadOnlyList<ExpressionNode> tail)
    {
        List<ExpressionNode> items = new(tail.Count + 1) { head };
        items.AddRange(tail);

        return items;
    }

    /// <summary>Returns every child after the first — the set of an <c>IN</c> / <c>NOT IN</c> node whose leading child is the tested value.</summary>
    /// <param name="items">The full child list, value first.</param>
    /// <returns>The children after the first, in order.</returns>
    private static List<ExpressionNode> Tail(ExpressionNode[] items)
    {
        List<ExpressionNode> tail = new(items.Length - 1);
        for(int i = 1; i < items.Length; i++)
        {
            tail.Add(items[i]);
        }

        return tail;
    }
}
