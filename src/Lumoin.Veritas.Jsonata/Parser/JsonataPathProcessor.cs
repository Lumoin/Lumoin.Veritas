using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Ast;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// The post-parse path-processing pass: an iterative (no-recursion) transform that mirrors the reference
/// JSONata parser's <c>processAST</c> path flattening and parent (<c>%</c>) ancestry resolution, adapted to
/// this build's node types. It runs after a successful parse, before the tree is returned, over a node whose
/// raw <c>@</c> / <c>#</c> binds (<see cref="ContextBindExpression"/> / <see cref="IndexBindExpression"/>) and
/// parent operators (<see cref="ParentExpression"/>) the parser produced.
/// </summary>
/// <remarks>
/// <para>
/// The transform flattens a nested <see cref="MapExpression"/> chain into a flat step list, folds <c>@</c> /
/// <c>#</c> into the source step's focus / index / index-stage, migrates trailing predicates into stages, and
/// resolves each <c>%</c> to an ancestor slot attached to the right earlier step (the verbatim
/// <c>seekParent</c> / <c>pushAncestry</c> / <c>resolveAncestry</c> level arithmetic, focus-skip loop, and
/// label reuse). It emits a <see cref="PathExpression"/> ONLY for a path that became a tuple stream (a step
/// bearing a focus / index / ancestor / stage); a plain path is returned as the original nested
/// <see cref="MapExpression"/> chain unchanged, so the engine's plain-path behaviour is byte-for-byte
/// preserved.
/// </para>
/// <para>
/// Every traversal is an explicit work stack whose genuine nesting depth (not its sibling breadth) is bounded
/// by <see cref="JsonataLimits.MaxParseDepth"/>, so an adversarially deep input throws a catchable
/// <see cref="JsonataParseException"/> rather than overflowing the C# stack — the same no-recursion discipline
/// the parser and evaluator follow — while a valid wide expression (a many-element array / call / object /
/// block) is never rejected.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/path-operators#navigate-to-the-parent">the JSONata parent-operator reference</see> and <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</para>
/// </remarks>
internal sealed partial class JsonataPathProcessor
{
    /// <summary>The diagnostics sink the pass records S0213 / S0214 / S0215 / S0216 / S0217-equivalent errors into.</summary>
    private readonly DiagnosticBag diagnostics;

    /// <summary>The running ancestor-slot label counter (the reference's <c>ancestorLabel</c>); each <see cref="ParentExpression"/> takes the next value.</summary>
    private int ancestorLabel;

    /// <summary>The running ancestry-registry index counter (the reference's <c>ancestorIndex</c>); each <see cref="ParentExpression"/> takes the next value.</summary>
    private int ancestorIndex;

    /// <summary>The ancestry registry (the reference's <c>ancestry</c> list): every parent slot in source order, used to rewrite a reused label so two <c>%</c> on one step share a tuple key.</summary>
    private readonly List<AncestorSlot> ancestry = [];

    /// <summary>Initialises a processor over a diagnostics sink.</summary>
    /// <param name="diagnostics">The bag the pass records its error-severity diagnostics into.</param>
    private JsonataPathProcessor(DiagnosticBag diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Runs the path-processing pass over a parsed tree and returns the processed tree. Plain (non-tuple) paths
    /// are returned unchanged; tuple paths become <see cref="PathExpression"/> nodes with resolved ancestry;
    /// any unresolvable parent or invalid step records an error-severity diagnostic.
    /// </summary>
    /// <param name="root">The parsed expression tree.</param>
    /// <param name="diagnostics">The bag the pass records diagnostics into.</param>
    /// <returns>The processed tree.</returns>
    public static JsonataExpression Process(JsonataExpression root, DiagnosticBag diagnostics)
    {
        JsonataPathProcessor processor = new(diagnostics);
        ProcessResult result = processor.ProcessNode(root);
        processor.ReportUnresolvedParents(result);

        return result.Node;
    }

    /// <summary>
    /// Processes one node and its whole subtree into a <see cref="ProcessResult"/>, iteratively (no recursion):
    /// an explicit post-order walk schedules every node's children before the node's own combine step, so a
    /// child's processed result and its bubbled-up ancestry are available when the parent combines.
    /// </summary>
    /// <param name="root">The subtree root to process.</param>
    /// <returns>The processed result for the subtree.</returns>
    /// <remarks>
    /// The genuine nesting depth — the number of pending combine markers on the stack, i.e. how many ancestor
    /// nodes are still mid-descent — is bounded by <see cref="JsonataLimits.MaxParseDepth"/>; the stack's total
    /// size (which also grows with a single node's sibling breadth, e.g. a wide array / call / object / block)
    /// is deliberately NOT bounded, so a valid wide expression that parses and evaluates is never rejected here.
    /// The walk is iterative, so it cannot overflow the C# stack regardless of breadth; the parse-time
    /// <see cref="JsonataLimits.MaxExpressionLength"/> already bounds the tree's total size.
    /// </remarks>
    private ProcessResult ProcessNode(JsonataExpression root)
    {
        Stack<WorkItem> work = new();
        Stack<ProcessResult> results = new();
        work.Push(new WorkItem(root, Combine: false));
        int descentDepth = 0;

        while(work.Count > 0)
        {
            WorkItem item = work.Pop();
            if(item.Combine)
            {
                descentDepth--;
                results.Push(Combine(item.Node, results));

                continue;
            }

            IReadOnlyList<JsonataExpression> children = ProcessChildren(item.Node);
            if(children.Count == 0)
            {
                results.Push(CombineLeaf(item.Node));

                continue;
            }

            if(++descentDepth > JsonataLimits.MaxParseDepth)
            {
                throw new JsonataParseException("The JSONata path-processing pass exceeded the maximum nesting depth.", root.Span);
            }

            work.Push(new WorkItem(item.Node, Combine: true));
            for(int i = children.Count - 1; i >= 0; i--)
            {
                work.Push(new WorkItem(children[i], Combine: false));
            }
        }

        return results.Pop();
    }

    /// <summary>
    /// Returns the children of a node the post-order walk must process first, in source order. The set mirrors
    /// the nodes <c>processAST</c> descends into; leaves and nodes whose children carry no parent slot return
    /// the empty list so the walk treats them as leaves. The order matches <see cref="Combine"/>'s consumption
    /// (children pop in reverse off the results stack, so they are popped back into source order there).
    /// </summary>
    /// <param name="node">The node whose process-children are requested.</param>
    /// <returns>The children to process, in source order; empty for a leaf.</returns>
    private static IReadOnlyList<JsonataExpression> ProcessChildren(JsonataExpression node)
    {
        return node switch
        {
            MapExpression map => [map.Source, map.Step],
            ContextBindExpression contextBind => [contextBind.Source],
            IndexBindExpression indexBind => [indexBind.Source],
            PredicateExpression predicate => [predicate.Source, predicate.Filter],
            SortExpression sort => SortChildren(sort),
            ObjectConstructorExpression { Source: { } source } group => ObjectGroupChildren(group, source),
            BlockExpression block => block.Statements,
            ParentExpression => [],

            //Every other node (binary, unary, call, range, conditional, default, bind, lambda, transform,
            //array / prefix-object constructor, keep-array, apply, the leaves) is processed in the default arm,
            //which walks its immediate children so a nested '%' bubbles its slot up via pushAncestry.
            _ => ImmediateChildren(node)
        };
    }

    /// <summary>Returns a sort node's process-children: its source followed by each order-by term's key expression, in term order.</summary>
    /// <param name="sort">The sort node.</param>
    /// <returns>The source then the term keys.</returns>
    private static List<JsonataExpression> SortChildren(SortExpression sort)
    {
        List<JsonataExpression> children = [sort.Source];
        foreach(SortTerm term in sort.Terms)
        {
            children.Add(term.Key);
        }

        return children;
    }

    /// <summary>Returns a led group-by object constructor's process-children: its grouping source followed by each member key then value, interleaved.</summary>
    /// <param name="group">The led path-step object constructor.</param>
    /// <param name="source">The constructor's grouping source.</param>
    /// <returns>The source then the interleaved member key / value expressions.</returns>
    private static List<JsonataExpression> ObjectGroupChildren(ObjectConstructorExpression group, JsonataExpression source)
    {
        List<JsonataExpression> children = [source];
        foreach((JsonataExpression Key, JsonataExpression Value) member in group.Members)
        {
            children.Add(member.Key);
            children.Add(member.Value);
        }

        return children;
    }

    /// <summary>Returns a node's immediate children, in source order, for the default processing arm; empty for a leaf.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The immediate children.</returns>
    private static IReadOnlyList<JsonataExpression> ImmediateChildren(JsonataExpression node)
    {
        return node switch
        {
            BinaryExpression binary => [binary.Left, binary.Right],
            UnaryExpression unary => [unary.Operand],
            DefaultExpression def => [def.Left, def.Right],
            ConditionalExpression { WhenFalse: null } conditional => [conditional.Condition, conditional.WhenTrue],
            ConditionalExpression conditional => [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse!],
            BindExpression bind => [bind.Value],
            RangeExpression range => [range.Low, range.High],
            ApplyExpression apply => [apply.Left, apply.Right],
            KeepArrayExpression keepArray => [keepArray.Source],
            LambdaExpression lambda => [lambda.Body],
            CallExpression call => CallImmediateChildren(call),
            ArrayConstructorExpression array => array.Elements,
            ObjectConstructorExpression obj => PrefixObjectChildren(obj),
            TransformExpression transform => TransformChildren(transform),

            //Leaves and nodes with no parent-bearing structure: nothing to descend into.
            _ => []
        };
    }

    /// <summary>Returns a call's immediate children: its procedure followed by its argument expressions.</summary>
    /// <param name="call">The call node.</param>
    /// <returns>The procedure then the arguments.</returns>
    private static List<JsonataExpression> CallImmediateChildren(CallExpression call)
    {
        List<JsonataExpression> children = [call.Procedure];
        foreach(JsonataExpression argument in call.Arguments)
        {
            children.Add(argument);
        }

        return children;
    }

    /// <summary>Returns a prefix object constructor's immediate children: each member key then value, interleaved (no grouping source).</summary>
    /// <param name="obj">The prefix object constructor.</param>
    /// <returns>The interleaved member key / value expressions.</returns>
    private static List<JsonataExpression> PrefixObjectChildren(ObjectConstructorExpression obj)
    {
        List<JsonataExpression> children = [];
        if(obj.Source is not null)
        {
            children.Add(obj.Source);
        }

        foreach((JsonataExpression Key, JsonataExpression Value) member in obj.Members)
        {
            children.Add(member.Key);
            children.Add(member.Value);
        }

        return children;
    }

    /// <summary>Returns a transform's immediate children: its pattern, its update, and its optional delete clause.</summary>
    /// <param name="transform">The transform node.</param>
    /// <returns>The pattern, update, and (when present) delete clauses.</returns>
    private static IReadOnlyList<JsonataExpression> TransformChildren(TransformExpression transform)
    {
        return transform.Delete is null
            ? [transform.Pattern, transform.Update]
            : [transform.Pattern, transform.Update, transform.Delete];
    }

    /// <summary>Combines a leaf node (no process-children) into a result: a parent operator registers its slot; everything else is carried through unchanged.</summary>
    /// <param name="node">The leaf node.</param>
    /// <returns>The processed result.</returns>
    private ProcessResult CombineLeaf(JsonataExpression node)
    {
        return node is ParentExpression parent ? ProcessParent(parent) : ProcessResult.Plain(node);
    }

    /// <summary>
    /// Combines a node whose children have been processed (their results are on top of the results stack, last
    /// child on top) into the node's own processed result. Dispatches on the node kind to the matching
    /// <c>processAST</c> case.
    /// </summary>
    /// <param name="node">The node being combined.</param>
    /// <param name="results">The results stack the children's results are popped from.</param>
    /// <returns>The node's processed result.</returns>
    private ProcessResult Combine(JsonataExpression node, Stack<ProcessResult> results)
    {
        switch(node)
        {
            case(MapExpression map):
            {
                ProcessResult step = results.Pop();
                ProcessResult source = results.Pop();

                return ProcessMap(map, source, step);
            }
            case(ContextBindExpression contextBind):
            {
                ProcessResult source = results.Pop();

                return ProcessContextBind(contextBind, source);
            }
            case(IndexBindExpression indexBind):
            {
                ProcessResult source = results.Pop();

                return ProcessIndexBind(indexBind, source);
            }
            case(PredicateExpression predicate):
            {
                ProcessResult filter = results.Pop();
                ProcessResult source = results.Pop();

                return ProcessPredicate(predicate, source, filter);
            }
            case(SortExpression sort):
            {
                ProcessResult[] termResults = PopMany(results, sort.Terms.Count);
                ProcessResult source = results.Pop();

                return ProcessSort(sort, source, termResults);
            }
            case(ObjectConstructorExpression { Source: not null } group):
            {
                ProcessResult[] memberResults = PopMany(results, group.Members.Count * 2);
                ProcessResult source = results.Pop();

                return ProcessObjectGroup(group, source, memberResults);
            }
            case(BlockExpression block):
            {
                ProcessResult[] statementResults = PopMany(results, block.Statements.Count);

                return ProcessBlock(block, statementResults);
            }
            case(KeepArrayExpression keepArray):
            {
                ProcessResult source = results.Pop();

                return ProcessKeepArray(keepArray, source);
            }
            case(ApplyExpression apply):
            {
                ProcessResult right = results.Pop();
                ProcessResult left = results.Pop();

                return ProcessApply(apply, left, right);
            }
            default:
            {
                IReadOnlyList<JsonataExpression> children = ImmediateChildren(node);
                ProcessResult[] childResults = PopMany(results, children.Count);

                return ProcessDefault(node, childResults);
            }
        }
    }

    /// <summary>Pops <paramref name="count"/> child results off the stack and returns them in source order (the stack holds them last-child-on-top).</summary>
    /// <param name="results">The results stack.</param>
    /// <param name="count">The number of child results to pop.</param>
    /// <returns>The popped results in source order.</returns>
    private static ProcessResult[] PopMany(Stack<ProcessResult> results, int count)
    {
        ProcessResult[] popped = new ProcessResult[count];
        for(int i = count - 1; i >= 0; i--)
        {
            popped[i] = results.Pop();
        }

        return popped;
    }

    /// <summary>One item on the processor's explicit post-order work stack: a node and whether this is its combine pass.</summary>
    /// <param name="Node">The node to process.</param>
    /// <param name="Combine">Whether this is the node's combine pass (its children are already processed).</param>
    private readonly record struct WorkItem(JsonataExpression Node, bool Combine);
}
