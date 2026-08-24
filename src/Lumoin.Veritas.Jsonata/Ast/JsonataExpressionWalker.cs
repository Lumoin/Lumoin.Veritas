using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Ast;

/// <summary>
/// Rewrites one <see cref="JsonataExpression"/> into another, applied to each node after its children
/// have been rewritten. Return the node unchanged to leave it as is.
/// </summary>
/// <param name="node">The node to rewrite.</param>
/// <returns>The rewritten node, or the same instance when nothing changed.</returns>
public delegate JsonataExpression JsonataExpressionRewriter(JsonataExpression node);

/// <summary>
/// Iterative traversal and rewriting over a JSONata <see cref="JsonataExpression"/> tree. Both
/// operations use an explicit work stack rather than call-stack recursion — matching the project's
/// iterative traversal discipline — so a deeply nested expression cannot overflow the stack.
/// <see cref="Traverse"/> enumerates an expression and its sub-expressions; <see cref="Transform"/>
/// rebuilds the tree bottom-up under a rewrite delegate.
/// </summary>
/// <remarks>
/// <para>
/// The adjacency is the per-type <see cref="Children"/> switch (the dual <see cref="Rebuild"/>
/// reconstructs a node from rewritten children), so the closed expression hierarchy is covered in one
/// place. The walk has <em>tree</em> semantics: every position is visited and rewritten, including two
/// value-equal sibling sub-expressions.
/// </para>
/// <para>
/// A <see cref="ConditionalExpression"/> in the no-else form carries a <see langword="null"/>
/// <see cref="ConditionalExpression.WhenFalse"/>, so it exposes two children rather than three; the
/// <see cref="Children"/> and <see cref="Rebuild"/> switches special-case the null arm.
/// </para>
/// <para>
/// An <see cref="ArrayConstructorExpression"/> is the first variadic node: it exposes its N elements as
/// its children, and the explicit-stack traversal and rewrite loops are arity-agnostic, so they already
/// handle an arbitrary child count with no per-node special case. An
/// <see cref="ObjectConstructorExpression"/> is variadic too: its optional grouping source followed by its
/// N key/value member pairs are exposed as a flat child list <c>[source?, k0, v0, k1, v1, ...]</c> in
/// <see cref="Children"/> and re-paired into a source and members in <see cref="Rebuild"/>, so the source
/// and the interleaved key and value positions participate in the structural walk, comparison, and hash for
/// free. A <see cref="BlockExpression"/> is variadic in the
/// same way: its statements are exposed as its children, so the arity-agnostic loops walk them with no
/// per-node special case. A <see cref="CallExpression"/> is variadic too: its children are the procedure
/// followed by its N arguments, walked by the same arity-agnostic loops. A
/// <see cref="LambdaExpression"/> exposes only its body as a child — its parameter names and its type
/// signature are preserved scalar data compared in <see cref="ShallowEqual"/> and folded in
/// <see cref="AddShallowHash"/>.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</para>
/// </remarks>
public static class JsonataExpressionWalker
{
    /// <summary>The empty child list shared by leaf expressions.</summary>
    private static IReadOnlyList<JsonataExpression> NoChildren { get; } = [];

    /// <summary>
    /// Enumerates an expression and all its sub-expressions in pre-order — each node before its
    /// children, and children in source order — using an explicit stack (no recursion). The enumeration
    /// is lazy.
    /// </summary>
    /// <param name="root">The expression to enumerate from.</param>
    /// <returns>The expression followed by every sub-expression, in pre-order.</returns>
    public static IEnumerable<JsonataExpression> Traverse(JsonataExpression root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Iterate(root);
    }

    /// <summary>
    /// Rewrites an expression bottom-up using an explicit work stack (no recursion): every node's
    /// children are transformed first, the node is rebuilt with the transformed children when any
    /// changed, and then <paramref name="rewrite"/> is applied to the (possibly rebuilt) node. A
    /// sub-expression that <paramref name="rewrite"/> leaves untouched is returned by reference, so an
    /// unchanged tree is not reallocated.
    /// </summary>
    /// <param name="root">The expression to rewrite.</param>
    /// <param name="rewrite">The rewrite applied to each node after its children; return the node unchanged to leave it as is.</param>
    /// <returns>The rewritten expression, or the same <paramref name="root"/> instance when nothing changed.</returns>
    public static JsonataExpression Transform(JsonataExpression root, JsonataExpressionRewriter rewrite)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rewrite);

        //Post-order rebuild over an explicit stack: each node is visited twice — first to schedule its
        //children (the expand phase), then to combine their rewritten results into a rebuilt node (the
        //combine phase). Rewritten children accumulate on a results stack; because children expand in
        //order, a node's children sit on top of the results stack — last child on top — when its combine
        //phase runs.
        Stack<(JsonataExpression Node, bool Combine)> work = new();
        Stack<JsonataExpression> results = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (JsonataExpression node, bool combine) = work.Pop();
            IReadOnlyList<JsonataExpression> children = Children(node);

            if(combine)
            {
                //The rewritten children are the top children.Count entries of the results stack, last
                //child on top; pop them back into position order, rebuilding only if one changed.
                JsonataExpression[] rewritten = new JsonataExpression[children.Count];
                bool changed = false;
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    JsonataExpression child = results.Pop();
                    rewritten[i] = child;
                    if(!ReferenceEquals(child, children[i]))
                    {
                        changed = true;
                    }
                }

                JsonataExpression rebuilt = changed ? Rebuild(node, rewritten) : node;
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

    /// <summary>
    /// Gets a structural, source-position-insensitive equality comparer over expression trees: two
    /// expressions are equal when their node types, scalar fields, and child shapes match (spans
    /// ignored).
    /// </summary>
    /// <remarks>
    /// It is the right key for deduplicating textually-identical expressions, where record value
    /// equality is insufficient: the span is the first positional field and participates in record
    /// <c>==</c>, so two structurally-equal subtrees at different offsets compare unequal.
    /// </remarks>
    public static IEqualityComparer<JsonataExpression> StructuralComparer { get; } = new StructuralEqualityComparer();

    /// <summary>
    /// Determines whether two expression trees are structurally equal ignoring source spans: same node
    /// type, same scalar fields, and structurally-equal children at every position.
    /// </summary>
    /// <param name="left">The first expression, or <see langword="null"/>.</param>
    /// <param name="right">The second expression, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the trees are structurally equal (both <see langword="null"/> counts as equal).</returns>
    public static bool StructurallyEqual(JsonataExpression? left, JsonataExpression? right)
    {
        if(ReferenceEquals(left, right))
        {
            return true;
        }

        if(left is null || right is null)
        {
            return false;
        }

        Stack<(JsonataExpression Left, JsonataExpression Right)> pairs = new();
        pairs.Push((left, right));

        while(pairs.Count > 0)
        {
            (JsonataExpression a, JsonataExpression b) = pairs.Pop();
            if(!ShallowEqual(a, b))
            {
                return false;
            }

            IReadOnlyList<JsonataExpression> childrenA = Children(a);
            IReadOnlyList<JsonataExpression> childrenB = Children(b);
            if(childrenA.Count != childrenB.Count)
            {
                return false;
            }

            for(int i = 0; i < childrenA.Count; i++)
            {
                pairs.Push((childrenA[i], childrenB[i]));
            }
        }

        return true;
    }

    /// <summary>Computes a structural, span-insensitive hash code consistent with <see cref="StructurallyEqual"/>.</summary>
    /// <param name="node">The expression to hash.</param>
    /// <returns>The structural hash code.</returns>
    public static int StructuralHashCode(JsonataExpression node)
    {
        ArgumentNullException.ThrowIfNull(node);

        HashCode hash = new();
        foreach(JsonataExpression current in Iterate(node))
        {
            AddShallowHash(ref hash, current);
        }

        return hash.ToHashCode();
    }

    /// <summary>Compares the scalar (non-child, non-span) fields of two same-position nodes.</summary>
    /// <param name="a">The first node.</param>
    /// <param name="b">The second node.</param>
    /// <returns><see langword="true"/> when the nodes have the same type and equal scalar fields.</returns>
    private static bool ShallowEqual(JsonataExpression a, JsonataExpression b)
    {
        if(a.GetType() != b.GetType())
        {
            return false;
        }

        return (a, b) switch
        {
            (LiteralExpression x, LiteralExpression y) => x.Kind == y.Kind && x.Value.Equals(y.Value),
            (RegexExpression x, RegexExpression y) => x.Pattern.Equals(y.Pattern) && x.Flags.Equals(y.Flags),
            (NameExpression x, NameExpression y) => x.Name.Equals(y.Name),
            (VariableExpression x, VariableExpression y) => x.Form == y.Form && x.Name.Equals(y.Name),
            (BindExpression x, BindExpression y) => x.VariableName.Equals(y.VariableName),
            (LambdaExpression x, LambdaExpression y) => ParameterListsEqual(x.Parameters, y.Parameters) && x.Signature.Equals(y.Signature),
            (BinaryExpression x, BinaryExpression y) => x.Operator == y.Operator,
            (DefaultExpression x, DefaultExpression y) => x.Operator == y.Operator,
            (UnaryExpression x, UnaryExpression y) => x.Operator == y.Operator,
            (ConditionalExpression x, ConditionalExpression y) => x.WhenFalse is null == (y.WhenFalse is null),
            (ErrorExpression x, ErrorExpression y) => x.ExpectedProduction.Equals(y.ExpectedProduction),

            //The array constructor's only scalar field is whether it is a path step (the cons marker); a
            //cons-marked constructor and a plain one are distinct nodes even with identical elements (the
            //elements are child-count-checked and walked by the caller).
            (ArrayConstructorExpression x, ArrayConstructorExpression y) => x.ConsArray == y.ConsArray,

            //The object constructor's only scalar field is whether it carries a grouping source: the led
            //path-step form (a non-null source) and the prefix form (a null source) are distinct nodes even
            //with identical members. The leading source child and the interleaved [k, v, ...] tail are
            //child-count-checked and walked by the caller.
            (ObjectConstructorExpression x, ObjectConstructorExpression y) => x.Source is null == (y.Source is null),

            //The raw context / positional bind nodes compare on their bound variable name (their scalar data);
            //their source step is the one child, child-count-checked and walked by the caller.
            (ContextBindExpression x, ContextBindExpression y) => x.Variable.Equals(y.Variable),
            (IndexBindExpression x, IndexBindExpression y) => x.Variable.Equals(y.Variable),

            //The parent operator's scalar datum is its resolved slot label (the only slot field read at eval);
            //two parent nodes are structurally equal once type-matched and same-labelled. It is a leaf.
            (ParentExpression x, ParentExpression y) => x.Slot.Label == y.Slot.Label,

            //The flattened tuple-stream path's scalar fields are its keep-singleton / carries-ancestry flags
            //and its per-step tuple markers (whether it has a group is captured by the group child count). Its
            //step expressions, filter expressions, and group are child-count-checked and walked by the caller.
            (PathExpression x, PathExpression y) => PathScalarsEqual(x, y),

            //Map/Predicate/Block/Apply, the keep-array marker, and the variadic call (procedure plus N
            //arguments) carry only children (already type-matched and child-count-checked by the caller), so
            //no scalar fields remain. The partial-application placeholder is a scalar-free leaf — two
            //placeholders are structurally equal once type-matched.
            _ => true
        };
    }

    /// <summary>Folds a node's type and scalar fields into a hash, mirroring <see cref="ShallowEqual"/>.</summary>
    /// <param name="hash">The accumulating hash.</param>
    /// <param name="node">The node to fold in.</param>
    private static void AddShallowHash(ref HashCode hash, JsonataExpression node)
    {
        hash.Add(node.GetType());
        switch(node)
        {
            case(LiteralExpression literal):
            {
                hash.Add(literal.Kind);
                hash.Add(literal.Value);

                break;
            }
            case(RegexExpression regex):
            {
                hash.Add(regex.Pattern);
                hash.Add(regex.Flags);

                break;
            }
            case(NameExpression name):
            {
                hash.Add(name.Name);

                break;
            }
            case(VariableExpression variable):
            {
                hash.Add(variable.Form);
                hash.Add(variable.Name);

                break;
            }
            case(BindExpression bind):
            {
                hash.Add(bind.VariableName);

                break;
            }
            case(LambdaExpression lambda):
            {
                //The parameter names and the type signature are the lambda's scalar data; fold each so two
                //lambdas differing only in their parameter names or their signature hash apart, consistent
                //with ShallowEqual.
                foreach(Utf8String parameter in lambda.Parameters)
                {
                    hash.Add(parameter);
                }

                hash.Add(lambda.Signature);

                break;
            }
            case(DefaultExpression def):
            {
                hash.Add(def.Operator);

                break;
            }
            case(BinaryExpression binary):
            {
                hash.Add(binary.Operator);

                break;
            }
            case(UnaryExpression unary):
            {
                hash.Add(unary.Operator);

                break;
            }
            case(ConditionalExpression conditional):
            {
                hash.Add(conditional.WhenFalse is null);

                break;
            }
            case(ErrorExpression error):
            {
                hash.Add(error.ExpectedProduction);

                break;
            }
            case(ObjectConstructorExpression obj):
            {
                //Whether the constructor carries a grouping source is its only scalar field; fold it so the
                //led path-step form and the prefix form hash apart, consistent with ShallowEqual.
                hash.Add(obj.Source is null);

                break;
            }
            case(ArrayConstructorExpression array):
            {
                //Whether the constructor is a path step (the cons marker) is its only scalar field; fold it so
                //a cons-marked constructor and a plain one hash apart, consistent with ShallowEqual.
                hash.Add(array.ConsArray);

                break;
            }
            case(ContextBindExpression contextBind):
            {
                hash.Add(contextBind.Variable);

                break;
            }
            case(IndexBindExpression indexBind):
            {
                hash.Add(indexBind.Variable);

                break;
            }
            case(ParentExpression parent):
            {
                //The resolved slot label is the parent node's only eval-relevant scalar datum, consistent with
                //ShallowEqual.
                hash.Add(parent.Slot.Label);

                break;
            }
            case(PathExpression path):
            {
                //Fold the path-level flags and each step's tuple markers (the scalar data ShallowEqual
                //compares); the step / filter / group expressions are folded by the pre-order walk over the
                //children.
                hash.Add(path.KeepSingletonArray);
                hash.Add(path.CarriesAncestry);
                hash.Add(path.Group is null);
                foreach(PathStep step in path.Steps)
                {
                    hash.Add(step.Focus);
                    hash.Add(step.Index);
                    hash.Add(step.Ancestor is null ? -1 : step.Ancestor.Label);
                    hash.Add(step.Tuple);
                    hash.Add(step.ConsArray);
                    hash.Add(step.KeepArray);
                    hash.Add(step.Stages.Count);
                }

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Yields an expression and its sub-expressions in pre-order using an explicit stack.</summary>
    /// <param name="root">The expression to enumerate from.</param>
    /// <returns>The expression and its sub-expressions, in pre-order.</returns>
    private static IEnumerable<JsonataExpression> Iterate(JsonataExpression root)
    {
        Stack<JsonataExpression> stack = new();
        stack.Push(root);

        while(stack.Count > 0)
        {
            JsonataExpression current = stack.Pop();
            yield return current;

            //Push children in reverse so they pop — and are yielded — in source order.
            IReadOnlyList<JsonataExpression> children = Children(current);
            for(int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    /// <summary>Returns the immediate sub-expressions of a node, in source order; empty for a leaf.</summary>
    /// <param name="node">The expression node.</param>
    /// <returns>The node's sub-expressions, in source order.</returns>
    private static IReadOnlyList<JsonataExpression> Children(JsonataExpression node)
    {
        return node switch
        {
            MapExpression map => [map.Source, map.Step],
            PredicateExpression predicate => [predicate.Source, predicate.Filter],

            //The keep-array marker's only child is its marked source step.
            KeepArrayExpression keepArray => [keepArray.Source],
            BinaryExpression binary => [binary.Left, binary.Right],
            DefaultExpression def => [def.Left, def.Right],
            UnaryExpression unary => [unary.Operand],
            ConditionalExpression conditional => conditional.WhenFalse is null
                ? [conditional.Condition, conditional.WhenTrue]
                : [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            BindExpression bind => [bind.Value],
            RangeExpression range => [range.Low, range.High],

            //The function-application / chain operator's children are its two operands, in source order.
            ApplyExpression apply => [apply.Left, apply.Right],

            //A lambda's only child EXPRESSION is its body; the parameter names are preserved scalar data
            //(like BindExpression.VariableName), so the structural walk descends only into the body.
            LambdaExpression lambda => [lambda.Body],

            //A call is variadic: its children are the procedure followed by its N arguments, in source
            //order, walked by the arity-agnostic driver exactly like the array constructor's elements.
            CallExpression call => CallChildren(call),

            //The first variadic node: its children are its elements, already in source order — the
            //arity-agnostic driver loops walk an N-element list the same way they walk a fixed pair.
            ArrayConstructorExpression array => array.Elements,

            //A block's children are its statements, already in source order, walked by the arity-agnostic
            //driver exactly like the array constructor's elements.
            BlockExpression block => block.Statements,

            //The object constructor's children are its optional grouping source followed by its member
            //key/value expressions, flattened to the order [source?, k0, v0, k1, v1, ...] so the
            //arity-agnostic driver loops visit each one. The led path-step form prepends the source; the
            //prefix form (null source) yields the bare interleaved member list.
            ObjectConstructorExpression obj => ObjectConstructorChildren(obj),

            //The raw context / positional bind nodes expose their source step as their one child; the bound
            //variable name is preserved scalar data (like BindExpression.VariableName). They are consumed by
            //the ancestry pass before the tree is returned, so the evaluator never sees them, but the walk
            //still descends into the source so a transform over a pre-processing tree reaches it.
            ContextBindExpression contextBind => [contextBind.Source],
            IndexBindExpression indexBind => [indexBind.Source],

            //The flattened tuple-stream path's children are its step expressions, then its stage filters, then
            //its optional group, flattened by PathExpressionChildren so the arity-agnostic walk visits each.
            PathExpression path => PathExpressionChildren(path),

            //Leaves: LiteralExpression, RegexExpression, NameExpression, VariableExpression, ErrorExpression,
            //WildcardExpression, DescendantExpression, PlaceholderExpression, ParentExpression (its ancestor
            //slot is bookkeeping data, not a child expression).
            _ => NoChildren
        };
    }

    /// <summary>
    /// Builds a flattened tuple-stream path's child list: every step's expression first, in step order, then
    /// every <see cref="PathStageKind.Filter"/> stage's filter expression across all steps in step / stage
    /// order, then the optional group-by constructor. Index stages carry no expression, so they contribute no
    /// child. The order is fixed so <see cref="Rebuild"/> can put the rewritten children back.
    /// </summary>
    /// <param name="path">The flattened tuple-stream path.</param>
    /// <returns>The flat child list <c>[step0, step1, ..., filter0, filter1, ..., group?]</c>.</returns>
    private static JsonataExpression[] PathExpressionChildren(PathExpression path)
    {
        List<JsonataExpression> children = [];
        foreach(PathStep step in path.Steps)
        {
            children.Add(step.Step);
        }

        foreach(PathStep step in path.Steps)
        {
            foreach(PathStage stage in step.Stages)
            {
                if(stage.Kind == PathStageKind.Filter && stage.Filter is not null)
                {
                    children.Add(stage.Filter);
                }
            }
        }

        if(path.Group is not null)
        {
            children.Add(path.Group);
        }

        return [.. children];
    }

    /// <summary>
    /// Rebuilds a flattened tuple-stream path from its rewritten flat child list, in the same order
    /// <see cref="PathExpressionChildren"/> produced: the leading <c>Steps.Count</c> children are the rewritten
    /// step expressions, the next children are the rewritten filter expressions across all steps in step /
    /// stage order, and the final child (when the original carried a group) is the rewritten group constructor.
    /// Each step and each filter stage is reconstructed into a fresh object carrying the rewritten expression
    /// and the original slot / flag bookkeeping, so the rebuilt path is a deep copy that shares no mutable step
    /// state with the original.
    /// </summary>
    /// <param name="path">The original flattened tuple-stream path.</param>
    /// <param name="children">The rewritten flat child list.</param>
    /// <returns>The rebuilt path.</returns>
    private static PathExpression PathExpressionRebuild(PathExpression path, JsonataExpression[] children)
    {
        int filterCursor = path.Steps.Count;
        List<PathStep> steps = [];
        for(int i = 0; i < path.Steps.Count; i++)
        {
            PathStep original = path.Steps[i];
            PathStep rebuilt = new()
            {
                Step = children[i],
                Focus = original.Focus,
                Index = original.Index,
                Ancestor = original.Ancestor,
                Tuple = original.Tuple,
                ConsArray = original.ConsArray,
                KeepArray = original.KeepArray
            };

            foreach(PathStage stage in original.Stages)
            {
                if(stage.Kind == PathStageKind.Filter && stage.Filter is not null)
                {
                    rebuilt.Stages.Add(new PathStage { Kind = PathStageKind.Filter, Filter = children[filterCursor], Index = default });
                    filterCursor++;
                }
                else
                {
                    rebuilt.Stages.Add(new PathStage { Kind = stage.Kind, Filter = null, Index = stage.Index });
                }
            }

            steps.Add(rebuilt);
        }

        ObjectConstructorExpression? group = path.Group is null ? null : (ObjectConstructorExpression)children[^1];

        return new PathExpression(path.Span, steps, path.KeepSingletonArray, group, path.CarriesAncestry);
    }

    /// <summary>Builds a call's flat child list <c>[procedure, arg0, arg1, ...]</c> so the arity-agnostic walk visits the procedure and every argument in source order.</summary>
    /// <param name="call">The call expression.</param>
    /// <returns>The procedure followed by its arguments, in source order.</returns>
    private static JsonataExpression[] CallChildren(CallExpression call)
    {
        JsonataExpression[] children = new JsonataExpression[call.Arguments.Count + 1];
        children[0] = call.Procedure;
        for(int i = 0; i < call.Arguments.Count; i++)
        {
            children[i + 1] = call.Arguments[i];
        }

        return children;
    }

    /// <summary>
    /// Compares two flattened tuple-stream paths on their scalar fields: the path-level keep-singleton and
    /// carries-ancestry flags, whether each carries a group, the step count, and each step's tuple markers
    /// (focus / index variable names, the presence and label of an ancestor slot, the tuple / cons / keep-array
    /// flags, and the stage count and per-stage kind / index). The step expressions, filter expressions, and
    /// group constructor are children, walked by the caller; only the non-child scalar data is compared here.
    /// </summary>
    /// <param name="x">The first path.</param>
    /// <param name="y">The second path.</param>
    /// <returns><see langword="true"/> when the two paths have equal scalar data at every position.</returns>
    private static bool PathScalarsEqual(PathExpression x, PathExpression y)
    {
        if(x.KeepSingletonArray != y.KeepSingletonArray || x.CarriesAncestry != y.CarriesAncestry || (x.Group is null) != (y.Group is null) || x.Steps.Count != y.Steps.Count)
        {
            return false;
        }

        for(int i = 0; i < x.Steps.Count; i++)
        {
            if(!StepScalarsEqual(x.Steps[i], y.Steps[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares two tuple-stream path steps on their non-child scalar markers (focus / index / ancestor label / flags / stage kinds), ignoring the step and filter expressions the caller walks.</summary>
    /// <param name="x">The first step.</param>
    /// <param name="y">The second step.</param>
    /// <returns><see langword="true"/> when the two steps have equal scalar markers.</returns>
    private static bool StepScalarsEqual(PathStep x, PathStep y)
    {
        bool sameAncestor = (x.Ancestor is null) == (y.Ancestor is null) && (x.Ancestor is null || x.Ancestor.Label == y.Ancestor!.Label);
        if(!x.Focus.Equals(y.Focus) || !x.Index.Equals(y.Index) || !sameAncestor || x.Tuple != y.Tuple || x.ConsArray != y.ConsArray || x.KeepArray != y.KeepArray || x.Stages.Count != y.Stages.Count)
        {
            return false;
        }

        for(int i = 0; i < x.Stages.Count; i++)
        {
            if(x.Stages[i].Kind != y.Stages[i].Kind || !x.Stages[i].Index.Equals(y.Stages[i].Index))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares two lambda parameter-name lists by count and element-wise <see cref="Utf8String"/> equality.</summary>
    /// <param name="left">The first parameter-name list.</param>
    /// <param name="right">The second parameter-name list.</param>
    /// <returns><see langword="true"/> when the lists have the same length and equal names at every position.</returns>
    private static bool ParameterListsEqual(IReadOnlyList<Utf8String> left, IReadOnlyList<Utf8String> right)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        for(int i = 0; i < left.Count; i++)
        {
            if(!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds an object constructor's flat child list: the optional grouping source first (when the led
    /// path-step form carries one), then the member key/value expressions interleaved as
    /// <c>[k0, v0, k1, v1, ...]</c>, so the arity-agnostic walk visits the source and every key and value
    /// in position order. The prefix form (a <see langword="null"/> source) yields the bare member list.
    /// </summary>
    /// <param name="obj">The object constructor.</param>
    /// <returns>The flat child list <c>[source?, k0, v0, k1, v1, ...]</c>.</returns>
    private static JsonataExpression[] ObjectConstructorChildren(ObjectConstructorExpression obj)
    {
        int sourceCount = obj.Source is null ? 0 : 1;
        JsonataExpression[] flat = new JsonataExpression[sourceCount + (obj.Members.Count * 2)];
        if(obj.Source is not null)
        {
            flat[0] = obj.Source;
        }

        for(int i = 0; i < obj.Members.Count; i++)
        {
            flat[sourceCount + (2 * i) + 0] = obj.Members[i].Key;
            flat[sourceCount + (2 * i) + 1] = obj.Members[i].Value;
        }

        return flat;
    }

    /// <summary>Re-pairs the interleaved key/value tail of a rewritten object-constructor child list back into member tuples, skipping the leading source child when one is present.</summary>
    /// <param name="children">The rewritten flat child list (<c>[source?, k0, v0, k1, v1, ...]</c>).</param>
    /// <param name="hadSource"><see langword="true"/> when the original carried a grouping source, so the first child is the source rather than a key.</param>
    /// <returns>The re-paired member key/value tuples, in source order.</returns>
    private static (JsonataExpression Key, JsonataExpression Value)[] PairMembers(JsonataExpression[] children, bool hadSource)
    {
        int sourceCount = hadSource ? 1 : 0;
        (JsonataExpression Key, JsonataExpression Value)[] members = new (JsonataExpression Key, JsonataExpression Value)[(children.Length - sourceCount) / 2];
        for(int i = 0; i < members.Length; i++)
        {
            members[i] = (children[sourceCount + (2 * i) + 0], children[sourceCount + (2 * i) + 1]);
        }

        return members;
    }

    /// <summary>Rebuilds a node from its rewritten children, preserving every non-expression field; leaves return themselves.</summary>
    /// <param name="node">The original node (the source of the preserved fields and the child arities).</param>
    /// <param name="children">The rewritten children, in the order <see cref="Children"/> produced them.</param>
    /// <returns>The rebuilt node.</returns>
    private static JsonataExpression Rebuild(JsonataExpression node, JsonataExpression[] children)
    {
        return node switch
        {
            MapExpression map => new MapExpression(map.Span, children[0], children[1]),
            PredicateExpression predicate => new PredicateExpression(predicate.Span, children[0], children[1]),

            //The keep-array marker rebuilds from its rewritten source step; it carries no scalar field.
            KeepArrayExpression keepArray => new KeepArrayExpression(keepArray.Span, children[0]),
            BinaryExpression binary => new BinaryExpression(binary.Span, children[0], binary.Operator, children[1]),
            DefaultExpression def => new DefaultExpression(def.Span, children[0], def.Operator, children[1]),
            UnaryExpression unary => new UnaryExpression(unary.Span, unary.Operator, children[0]),
            ConditionalExpression conditional => conditional.WhenFalse is null
                ? new ConditionalExpression(conditional.Span, children[0], children[1], WhenFalse: null)
                : new ConditionalExpression(conditional.Span, children[0], children[1], children[2]),
            BindExpression bind => new BindExpression(bind.Span, bind.VariableName, children[0]),
            RangeExpression range => new RangeExpression(range.Span, children[0], children[1]),

            //The function-application / chain operator rebuilds from its two rewritten operands; it carries no
            //scalar field.
            ApplyExpression apply => new ApplyExpression(apply.Span, children[0], children[1]),

            //A lambda rebuilds from its rewritten body, preserving its parameter-name list and its type
            //signature (its scalar data).
            LambdaExpression lambda => new LambdaExpression(lambda.Span, lambda.Parameters, children[0], lambda.Signature),

            //A call rebuilds from its rewritten flat [procedure, arg0, ...] child list: the first child is
            //the procedure, the remainder the arguments in source order.
            CallExpression call => new CallExpression(call.Span, children[0], children[1..]),

            //The variadic node rebuilds from the rewritten flat child list (its element list), preserving the
            //cons marker (its only scalar field).
            ArrayConstructorExpression array => new ArrayConstructorExpression(array.Span, children, array.ConsArray),

            //A block rebuilds from its rewritten statement list, which is its variadic child list.
            BlockExpression block => new BlockExpression(block.Span, children),

            //The object constructor rebuilds from its rewritten flat [source?, k0, v0, ...] child list: the
            //leading source child (when the original carried one) is the rewritten grouping source, and the
            //interleaved tail re-pairs back into members.
            ObjectConstructorExpression obj => new ObjectConstructorExpression(obj.Span, PairMembers(children, obj.Source is not null), obj.Source is null ? null : children[0]),

            //The raw context / positional bind nodes rebuild from their one rewritten source child, preserving
            //the bound variable name (their scalar data).
            ContextBindExpression contextBind => new ContextBindExpression(contextBind.Span, children[0], contextBind.Variable),
            IndexBindExpression indexBind => new IndexBindExpression(indexBind.Span, children[0], indexBind.Variable),

            //The flattened tuple-stream path rebuilds from its rewritten flat [steps, filters, group?] child
            //list, re-threading the rewritten step expressions and filter expressions back into freshly-cloned
            //step / stage objects (the slot / flag bookkeeping is preserved), in the same order
            //PathExpressionChildren produced them.
            PathExpression path => PathExpressionRebuild(path, children),

            //A leaf has no children; it is its own rebuild (RegexExpression, WildcardExpression,
            //DescendantExpression, PlaceholderExpression, and the other leaves listed in Children).
            _ => node
        };
    }

    /// <summary>The structural, span-insensitive equality comparer exposed by <see cref="StructuralComparer"/>.</summary>
    private sealed class StructuralEqualityComparer : IEqualityComparer<JsonataExpression>
    {
        /// <inheritdoc/>
        public bool Equals(JsonataExpression? x, JsonataExpression? y)
        {
            return StructurallyEqual(x, y);
        }

        /// <inheritdoc/>
        public int GetHashCode(JsonataExpression obj)
        {
            return StructuralHashCode(obj);
        }
    }
}
