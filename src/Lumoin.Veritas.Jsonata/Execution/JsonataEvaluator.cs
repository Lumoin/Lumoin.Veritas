using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Functions;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// Evaluates a parsed JSONata expression to a <see cref="JsonataValue"/> over an explicit work stack —
/// no recursion. The driver mirrors the project's two-phase expand/combine fold, extended with a
/// per-item cursor for the focus-rebinding operators (the dot/map and the predicate), a two-phase
/// per-item / per-group cursor for the object constructor's group-by, and a per-statement cursor for the
/// block.
/// </summary>
/// <remarks>
/// <para>
/// This build evaluates literals, field references, the variables <c>$</c>/<c>$$</c>/<c>$name</c>, the
/// dot/map <c>.</c>, the predicate/index <c>[...]</c>, the binary operators (arithmetic, concat,
/// comparison, equality, membership, boolean), unary negation, the conditional, the default operators
/// <c>?:</c> / <c>??</c>, the variable bind <c>:=</c>, the block <c>( ... )</c>, the wildcard <c>*</c>, the
/// descendant <c>**</c>, the range <c>..</c>, the array constructor <c>[ ... ]</c>, the object
/// constructor <c>{ ... }</c>, the user-defined function (lambda) <c>function(...){ ... }</c>, function
/// application <c>f(...)</c> (including partial application <c>f(?, x)</c>, which builds a partial value
/// rather than invoking), and the function-application / chain operator <c>~&gt;</c> (apply,
/// call-prepend, and compose), the transform <c>| ... | ... |</c>, and the order-by <c>^( ... )</c>. An error placeholder node evaluates to undefined.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</para>
/// </remarks>
public static partial class JsonataEvaluator
{
    /// <summary>The size, in bytes, of the on-stack scratch for encoding an object key during a field-name comparison.</summary>
    private const int MaxKeyScratchBytes = 256;

    /// <summary>
    /// The reserved parameter name a composed function <c>$f ~&gt; $g</c> binds to its single argument; its
    /// synthetic body looks this name up only in the composed lambda's OWN captured frame, so it cannot
    /// collide with a user binding of the same spelling.
    /// </summary>
    private static Utf8String ComposeParameterName { get; } = Utf8Strings.From("$compose$x");

    /// <summary>The reserved name a composed function binds to its inner-first function (the left operand of <c>~&gt;</c>), applied before the second.</summary>
    private static Utf8String ComposeFirstName { get; } = Utf8Strings.From("$compose$f");

    /// <summary>The reserved name a composed function binds to its inner-second function (the right operand of <c>~&gt;</c>), applied to the first's result.</summary>
    private static Utf8String ComposeSecondName { get; } = Utf8Strings.From("$compose$g");

    /// <summary>The bare name of the <c>$replace</c> built-in, compared against to route its regex function-replacement form to the resident replace cursor.</summary>
    private static Utf8String ReplaceFunctionName { get; } = Utf8Strings.From("replace");

    /// <summary>
    /// The internal marker value a partial-application placeholder <c>?</c> evaluates to: a unique reference
    /// carried in the function slot of a <see cref="JsonataValue"/>, distinguishable by reference identity
    /// from any genuine function value, so a call's combine can detect a placeholder argument among the
    /// evaluated argument values and build a <see cref="JsonataPartial"/> instead of invoking the procedure.
    /// It never escapes a call or chain argument list (the parser admits a placeholder only in argument
    /// position).
    /// </summary>
    private static JsonataValue PlaceholderMarker { get; } = JsonataValue.Function(new PlaceholderSlot());

    /// <summary>
    /// Evaluates an expression against an input value and returns the normalized result value, pinning the
    /// evaluation's instant to a fixed default. The date built-ins <c>$now</c> and <c>$millis</c> read that
    /// instant, so this overload is deterministic; the caller-facing facade threads a real
    /// <see cref="System.TimeProvider"/> instant through the
    /// <see cref="Evaluate(JsonataExpression, JsonataValue, long)"/> overload instead.
    /// </summary>
    /// <param name="expression">The parsed expression to evaluate.</param>
    /// <param name="input">The input document the expression is evaluated against.</param>
    /// <returns>The normalized result value (undefined when the expression matched nothing).</returns>
    public static JsonataValue Evaluate(JsonataExpression expression, JsonataValue input)
    {
        return Evaluate(expression, input, evaluationMillis: 0);
    }

    /// <summary>
    /// Evaluates an expression against an input value under a captured instant and returns the normalized
    /// result value. The instant is captured once by the caller (from a <see cref="System.TimeProvider"/>) and
    /// is constant for the whole evaluation, so the date built-ins <c>$now</c> and <c>$millis</c> read the same
    /// value however many times they are evaluated.
    /// </summary>
    /// <param name="expression">The parsed expression to evaluate.</param>
    /// <param name="input">The input document the expression is evaluated against.</param>
    /// <param name="evaluationMillis">The evaluation's instant, as integer epoch-milliseconds (UTC).</param>
    /// <returns>The normalized result value (undefined when the expression matched nothing).</returns>
    public static JsonataValue Evaluate(JsonataExpression expression, JsonataValue input, long evaluationMillis)
    {
        return Evaluate(expression, input, evaluationMillis, VeritasRandomness.System);
    }

    /// <summary>
    /// Evaluates an expression against an input value under a captured instant and a randomness source and
    /// returns the normalized result value. Both are captured once by the caller and are constant for the
    /// whole evaluation: the date built-ins <c>$now</c> / <c>$millis</c> read the instant, and the entropy
    /// built-in <c>$shuffle</c> draws its swap indices from the randomness source. A fixed
    /// <see cref="RandomnessDelegate"/> makes <c>$shuffle</c> deterministic.
    /// </summary>
    /// <param name="expression">The parsed expression to evaluate.</param>
    /// <param name="input">The input document the expression is evaluated against.</param>
    /// <param name="evaluationMillis">The evaluation's instant, as integer epoch-milliseconds (UTC).</param>
    /// <param name="randomness">The randomness source <c>$shuffle</c> draws its swap indices from.</param>
    /// <param name="maxEvaluationSteps">The step bound for this evaluation; defaults to <see cref="JsonataLimits.MaxEvaluationSteps"/> (the production bound). A batch or test host may raise it for a legitimately large but finite computation. A non-terminating recursion is still bounded by the work-stack depth limit regardless of this value.</param>
    /// <returns>The normalized result value (undefined when the expression matched nothing).</returns>
    public static JsonataValue Evaluate(JsonataExpression expression, JsonataValue input, long evaluationMillis, RandomnessDelegate randomness, int maxEvaluationSteps = JsonataLimits.MaxEvaluationSteps)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(randomness);

        JsonataContext context = JsonataContext.ForInput(input, evaluationMillis, randomness);
        EvaluationBudget budget = new(maxEvaluationSteps);

        return EvaluateCore(expression, context, budget);
    }

    /// <summary>
    /// Drives the work stack to completion: each frame is processed by its kind, with completed
    /// sub-evaluations accumulating on a results stack (last on top), exactly as the project's iterative
    /// fold rewrites a tree.
    /// </summary>
    /// <param name="root">The root expression.</param>
    /// <param name="rootContext">The root evaluation context.</param>
    /// <param name="budget">The step budget.</param>
    /// <returns>The result value.</returns>
    private static JsonataValue EvaluateCore(JsonataExpression root, JsonataContext rootContext, EvaluationBudget budget)
    {
        Stack<EvalFrame> work = new();
        Stack<JsonataValue> results = new();
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = root, Context = rootContext });

        while(work.Count > 0)
        {
            budget.Charge();
            if(work.Count > JsonataLimits.MaxEvaluationDepth)
            {
                throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, WellKnownJsonataErrors.NonTerminatingRecursion, "JSONata evaluation work-stack depth was exceeded.");
            }

            //Peek, not pop: a Map/Predicate frame stays resident across its per-item turns.
            EvalFrame frame = work.Peek();
            switch(frame.Kind)
            {
                case EvalFrameKind.Expand:
                {
                    work.Pop();
                    ExpandNode(frame, work, results);

                    break;
                }
                case EvalFrameKind.Combine:
                {
                    work.Pop();
                    CombineNode(frame, work, results);

                    break;
                }
                case EvalFrameKind.Map:
                {
                    StepMapFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.Predicate:
                {
                    StepPredicateFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.GroupBy:
                {
                    StepGroupByFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.Block:
                {
                    StepBlockFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.HigherOrder:
                {
                    StepHigherOrderFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.Transform:
                {
                    StepTransformFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.OrderBy:
                {
                    StepOrderByFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.RegexReplace:
                {
                    StepRegexReplaceFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.PathStream:
                {
                    StepPathStreamFrame(frame, work, results);

                    break;
                }
                case EvalFrameKind.Boolize:
                {
                    work.Pop();
                    results.Push(JsonataValue.Boolean(JsonataTruthiness.IsTruthy(results.Pop())));

                    break;
                }
                default:
                {
                    throw new InvalidOperationException("The JSONata evaluator reached an undefined frame kind.");
                }
            }
        }

        return results.Pop();
    }

    /// <summary>
    /// Expand phase: a leaf resolves straight onto the results stack; the dot/map and predicate become a
    /// cursor frame after their source is scheduled; the conditional schedules only its condition
    /// (short-circuit); every other operator schedules its children then its combine.
    /// </summary>
    /// <param name="frame">The frame being expanded.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void ExpandNode(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        switch(frame.Node)
        {
            case(LiteralExpression or NameExpression or VariableExpression or ErrorExpression):
            {
                results.Push(EvaluateLeaf(frame.Node, frame.Context));

                break;
            }
            case(PlaceholderExpression):
            {
                //A partial-application placeholder '?' does not evaluate to a JSONata value: it pushes the
                //internal placeholder marker so the enclosing call/chain combine detects the unbound slot and
                //builds a partial instead of invoking the procedure. The parser admits a placeholder only in an
                //argument position, so the marker never escapes a call or chain argument list.
                results.Push(PlaceholderMarker);

                break;
            }
            case(RegexExpression regex):
            {
                //Leaf-like: compile the pattern once into a first-class regex function value (an invalid
                //pattern surfaces as a JSONata error, not a leaked regex-compilation exception).
                results.Push(EvaluateRegex(regex));

                break;
            }
            case(MapExpression map):
            {
                BeginCursor(frame, EvalFrameKind.Map, map.Source, work);

                break;
            }
            case(PredicateExpression predicate):
            {
                BeginCursor(frame, EvalFrameKind.Predicate, predicate.Source, work);

                break;
            }
            case(KeepArrayExpression keepArray):
            {
                //The keep-array marker '[]' evaluates its source step, then the combine keeps the result an
                //array (the JSONata keepSingleton marker); a singleton therefore stays an array.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = keepArray.Source, Context = frame.Context });

                break;
            }
            case(SortExpression sort):
            {
                BeginCursor(frame, EvalFrameKind.OrderBy, sort.Source, work);

                break;
            }
            case(ConditionalExpression conditional):
            {
                //Short-circuit: schedule the combine, then only the condition. The combine selects and
                //pushes the chosen branch, whose result becomes the conditional's value.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = conditional.Condition, Context = frame.Context });

                break;
            }
            case(DefaultExpression def):
            {
                //Short-circuit: schedule the combine, then only the left operand. The combine keeps the left
                //value when it qualifies, or schedules the right operand (the fallback) otherwise.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = def.Left, Context = frame.Context });

                break;
            }
            case(BinaryExpression binary):
            {
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });

                //Short-circuit: 'and'/'or' schedule only the left operand; the combine decides from its
                //truthiness whether the right operand is evaluated. Every other operator needs both ready.
                if(binary.Operator is not (BinaryOperator.And or BinaryOperator.Or))
                {
                    work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = binary.Right, Context = frame.Context });
                }

                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = binary.Left, Context = frame.Context });

                break;
            }
            case(UnaryExpression unary):
            {
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = unary.Operand, Context = frame.Context });

                break;
            }
            case(BindExpression bind):
            {
                //The bind evaluates its value in the current (pre-bind) frame, then the combine installs the
                //binding and yields the value — so a self-reference ($x := $x + 1) reads the old value.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = bind.Value, Context = frame.Context });

                break;
            }
            case(BlockExpression):
            {
                //A block opens its own variable scope and runs its statements in order; turn the frame into a
                //resident Block cursor whose seed phase pushes the child frame before the first statement.
                frame.Kind = EvalFrameKind.Block;
                frame.NextIndex = -1;
                work.Push(frame);

                break;
            }
            case(ParentExpression parent):
            {
                //The parent operator '%' reads the ancestor value the tuple-stream capture step bound under the
                //slot's reserved frame key; when the slot was never bound (a non-tuple context, or an
                //unresolved parent), the lookup misses and the value is undefined. A bare '%()' therefore calls
                //the undefined value -> T1006 via the existing call path.
                results.Push(frame.Context.Frame.TryLookup(AncestorSlot.ReservedKey(parent.Slot.Label), out JsonataValue ancestor) ? ancestor : JsonataValue.Undefined);

                break;
            }
            case(PathExpression):
            {
                //The flattened tuple-stream path turns into a resident PathStream cursor (the port of the
                //reference's evaluatePath / evaluateTupleStep / tuple-aware evaluateFilter). The path processor
                //only ever emits this node for a path containing @ / # / %, so a plain path (the prior passing
                //cases) never reaches the cursor. The cursor's first turn is its default PathStreamPhase.Seed
                //phase, which normalises the input before the first step.
                frame.Kind = EvalFrameKind.PathStream;
                work.Push(frame);

                break;
            }
            case(WildcardExpression):
            {
                results.Push(EvaluateWildcard(frame.Context.Focus));

                break;
            }
            case(DescendantExpression):
            {
                results.Push(EvaluateDescendant(frame.Context.Focus));

                break;
            }
            case(LambdaExpression lambda):
            {
                //Leaf-like: the body is NOT evaluated at definition. The lambda value snapshots the CURRENT
                //binding frame and the CURRENT focus, so a later call evaluates the body against exactly the
                //environment the lambda was defined in (and resolves its own name for recursion). The declared
                //type signature is parsed once here into the value an application validates its arguments
                //against; a lambda with no signature carries a null one and binds its arguments positionally.
                JsonataSignature? signature = lambda.Signature.IsEmpty ? null : JsonataSignature.Parse(lambda.Signature.ToString());
                results.Push(JsonataValue.Function(new JsonataLambda(lambda.Parameters, lambda.Body, frame.Context.Frame, frame.Context.Focus, signature)));

                break;
            }
            case(RangeExpression range):
            {
                //Two-operand node: schedule both bounds then a combine, exactly like BinaryExpression.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = range.High, Context = frame.Context });
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = range.Low, Context = frame.Context });

                break;
            }
            case(ArrayConstructorExpression array):
            {
                //N-ary node: schedule the combine, then each element in reverse so the results pop
                //left-to-right. The combine folds the element values into the constructed array.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                for(int i = array.Elements.Count - 1; i >= 0; i--)
                {
                    work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = array.Elements[i], Context = frame.Context });
                }

                break;
            }
            case(CallExpression call):
            {
                //N-ary node: schedule the combine, then the argument values in reverse and the procedure
                //last, so the procedure expands first and the arguments pop left-to-right after it. The
                //combine pops the arguments back-to-front into position order, then the procedure, and
                //schedules the body. This mirrors the array constructor's expand/combine scheduling.
                work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = frame.Node, Context = frame.Context });
                for(int i = call.Arguments.Count - 1; i >= 0; i--)
                {
                    work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = call.Arguments[i], Context = frame.Context });
                }

                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = call.Procedure, Context = frame.Context });

                break;
            }
            case(ApplyExpression apply):
            {
                ExpandApply(apply, frame.Context, work);

                break;
            }
            case(TransformExpression transform):
            {
                //Leaf-like: the clauses are NOT evaluated at definition. The transformer value snapshots the
                //CURRENT binding frame, so a later application evaluates the pattern/update/delete clauses
                //against the environment the transform was defined in.
                results.Push(JsonataValue.Function(new JsonataTransformer(transform.Pattern, transform.Update, transform.Delete, frame.Context.Frame)));

                break;
            }
            case(ObjectConstructorExpression { Source: { } source }):
            {
                //The led path-step form 'path{ ... }' groups the source's RESULT, not the current focus: the
                //source is evaluated first (like the dot/map source), then the resident GroupBy cursor seeds
                //from that result. The cursor stays resident; its SeedFromSource phase pops the source value
                //off the results stack and reuses the SAME bucketing/valuing passes the prefix form runs.
                frame.Kind = EvalFrameKind.GroupBy;
                frame.GroupByPhase = GroupByPhase.SeedFromSource;
                work.Push(frame);
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = source, Context = frame.Context });

                break;
            }
            case(ObjectConstructorExpression):
            {
                //The prefix object constructor groups the CURRENT focus — there is no source child to evaluate
                //first (unlike the dot/map). Turn the frame into a resident GroupBy cursor seeded straight
                //from the focus; its seed phase normalizes the focus to items with no preliminary expand.
                frame.Kind = EvalFrameKind.GroupBy;
                frame.GroupByPhase = GroupByPhase.Seed;
                work.Push(frame);

                break;
            }
            default:
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"JSONata node '{frame.Node.GetType().Name}' is outside the evaluable set (literals, names, variables, the dot/map, the predicate, the binary and unary operators, the conditional, the default operators, the bind, the block, the wildcard, the descendant, the range, the array constructor, the object constructor, the lambda, the call, the function-application / chain operator '~>', and the transform '| ... | ... |')."));
            }
        }
    }

    /// <summary>Turns a frame into a Map/Predicate cursor and schedules its source for evaluation.</summary>
    /// <param name="frame">The frame to convert.</param>
    /// <param name="cursorKind">The cursor kind (<see cref="EvalFrameKind.Map"/> or <see cref="EvalFrameKind.Predicate"/>).</param>
    /// <param name="source">The source expression to evaluate first.</param>
    /// <param name="work">The work stack.</param>
    private static void BeginCursor(EvalFrame frame, EvalFrameKind cursorKind, JsonataExpression source, Stack<EvalFrame> work)
    {
        frame.Kind = cursorKind;
        frame.NextIndex = -1;
        work.Push(frame);
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = source, Context = frame.Context });
    }

    /// <summary>
    /// Schedules the children of a function-application / chain node <c>left ~&gt; right</c> ahead of its
    /// combine, split on the shape of the right operand so the combine pops a known number of values in a
    /// fixed order. When the right operand is a <see cref="CallExpression"/> with no partial-application
    /// placeholder (the call-prepend case), the left value, the call's procedure, and the call's N argument
    /// values are all scheduled, so the combine prepends the left value as the call's leading argument.
    /// Otherwise (the apply and compose cases, and a right call that carries a <c>?</c> placeholder — which
    /// evaluates as a whole to a partial value rather than prepending) only the left and right operands are
    /// scheduled, and the combine decides apply-versus-compose at runtime from whether the left value is a
    /// function. The children are pushed in reverse so they pop left-to-right after the combine is scheduled
    /// beneath them.
    /// </summary>
    /// <param name="apply">The function-application / chain node.</param>
    /// <param name="context">The evaluation context.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandApply(ApplyExpression apply, JsonataContext context, Stack<EvalFrame> work)
    {
        work.Push(new EvalFrame { Kind = EvalFrameKind.Combine, Node = apply, Context = context });

        if(apply.Right is CallExpression call && !CallHasPlaceholder(call))
        {
            //Call-prepend: schedule the argument values in reverse, then the call's procedure, then the left
            //value last so it pops first; the combine pops the left value, the procedure, then the arguments
            //back-to-front into position order and prepends the left value as the leading argument.
            for(int i = call.Arguments.Count - 1; i >= 0; i--)
            {
                work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = call.Arguments[i], Context = context });
            }

            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = call.Procedure, Context = context });
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = apply.Left, Context = context });

            return;
        }

        //Apply or compose: schedule the right operand then the left so they pop left-then-right; the combine
        //inspects the left value to choose apply (a non-function left) or compose (a function left).
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = apply.Right, Context = context });
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = apply.Left, Context = context });
    }

    /// <summary>
    /// Leaf evaluation: a JSON literal, a field lookup against the focus, a variable read
    /// (<c>$</c>/<c>$$</c>/<c>$name</c>), or the recovery placeholder (which is undefined).
    /// </summary>
    /// <param name="node">The leaf node.</param>
    /// <param name="context">The evaluation context.</param>
    /// <returns>The leaf's value.</returns>
    private static JsonataValue EvaluateLeaf(JsonataExpression node, JsonataContext context)
    {
        return node switch
        {
            LiteralExpression literal => EvaluateLiteral(literal),
            NameExpression name => LookupField(context.Focus, name.Name),
            VariableExpression { Form: VariableForm.ContextFocus } => context.Focus,
            VariableExpression { Form: VariableForm.Root } => context.Root,
            VariableExpression { Form: VariableForm.Named } variable => ResolveNamed(variable.Name, context),
            _ => JsonataValue.Undefined
        };
    }

    /// <summary>
    /// Compiles a regular-expression literal into a first-class regex function value, translating the JS
    /// flags <c>i</c> and <c>m</c> to the matching <see cref="System.Text.RegularExpressions.RegexOptions"/>
    /// (the only flags the lexer admits, as in the reference; the JS global flag is implicit in the consumers'
    /// match-all iteration and carries no option). A pattern the .NET engine cannot compile surfaces as a
    /// JSONata error rather than a leaked compilation exception.
    /// </summary>
    /// <param name="regex">The regular-expression literal node carrying the pattern and the flags.</param>
    /// <returns>The compiled regex function value.</returns>
    /// <exception cref="JsonataErrorException">The pattern could not be compiled by the .NET regular-expression engine.</exception>
    private static JsonataValue EvaluateRegex(RegexExpression regex)
    {
        string pattern = regex.Pattern.ToString();
        string flags = regex.Flags.ToString();
        System.Text.RegularExpressions.RegexOptions options = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        foreach(char flag in flags)
        {
            options |= flag switch
            {
                'i' => System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                'm' => System.Text.RegularExpressions.RegexOptions.Multiline,
                _ => System.Text.RegularExpressions.RegexOptions.None
            };
        }

        try
        {
            System.Text.RegularExpressions.Regex compiled = new(pattern, options);

            return JsonataValue.Function(new JsonataRegex(pattern, flags, compiled));
        }
        catch(System.Text.RegularExpressions.RegexParseException exception)
        {
            throw new JsonataErrorException(default, null, string.Concat("The regular expression '/", pattern, "/", flags, "' could not be compiled: ", exception.Message));
        }
    }

    /// <summary>
    /// Resolves a named variable <c>$name</c> nearest-first: a binding in the scope chain wins, then a
    /// built-in function of the same bare name, then the undefined value. A user binding therefore shadows a
    /// built-in of the same spelling, matching the reference resolver where built-ins live in the static
    /// root frame the scope chain reaches last. An unresolved name stays undefined (no throw); T1006 fires
    /// only when a non-function value is later called.
    /// </summary>
    /// <param name="name">The variable's bare name (without the leading <c>$</c>).</param>
    /// <param name="context">The evaluation context whose binding frame is checked first.</param>
    /// <returns>The bound value, the built-in function value, or the undefined value.</returns>
    private static JsonataValue ResolveNamed(Utf8String name, JsonataContext context)
    {
        if(context.Frame.TryLookup(name, out JsonataValue bound))
        {
            return bound;
        }

        if(JsonataBuiltins.TryResolve(name, out JsonataValue function))
        {
            return function;
        }

        return JsonataValue.Undefined;
    }

    /// <summary>Converts a literal node to its value, parsing a number lexeme to a double.</summary>
    /// <param name="literal">The literal node.</param>
    /// <returns>The literal value.</returns>
    private static JsonataValue EvaluateLiteral(LiteralExpression literal)
    {
        return literal.Kind switch
        {
            JsonataLiteralKind.Number => JsonataValue.Number(double.Parse(literal.Value.Span, NumberStyles.Float, CultureInfo.InvariantCulture)),
            JsonataLiteralKind.String => JsonataValue.String(literal.Value.ToString()),
            JsonataLiteralKind.Boolean => JsonataValue.Boolean(literal.Value.Span.SequenceEqual("true"u8)),
            _ => JsonataValue.Null
        };
    }

    /// <summary>
    /// Looks up a field by name on the focus over an explicit work stack (no recursion): an object focus
    /// yields the value at the key or undefined; an array focus maps the lookup over each element in
    /// document order, descending into nested arrays and flattening each per-element array result one
    /// level; a scalar focus contributes nothing.
    /// </summary>
    /// <param name="focus">The focus value.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The field value, a flattened sequence over an array focus, or undefined.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static JsonataValue LookupField(JsonataValue focus, Utf8String name)
    {
        if(focus.Kind == JsonataValueKind.Object)
        {
            return LookupObjectField(focus, name);
        }

        if(focus.Kind != JsonataValueKind.Array)
        {
            return JsonataValue.Undefined;
        }

        //The recursive map over arrays is an explicit depth-first walk in document order: each array
        //level descends into its elements, and each object element's value is flattened one level.
        List<JsonataValue> mapped = [];
        Stack<ArrayLookupCursor> stack = new();
        stack.Push(new ArrayLookupCursor(focus.AsArray, depth: 1));

        while(stack.Count > 0)
        {
            ArrayLookupCursor cursor = stack.Peek();
            if(cursor.NextIndex >= cursor.Items.Count)
            {
                stack.Pop();

                continue;
            }

            JsonataValue item = cursor.Items[cursor.NextIndex];
            cursor.NextIndex++;
            switch(item.Kind)
            {
                case JsonataValueKind.Array:
                {
                    int childDepth = cursor.Depth + 1;
                    if(childDepth > JsonataLimits.MaxEvaluationDepth)
                    {
                        throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "JSONata field lookup over nested arrays exceeded the maximum depth.");
                    }

                    stack.Push(new ArrayLookupCursor(item.AsArray, childDepth));

                    break;
                }
                case JsonataValueKind.Object:
                {
                    AppendFlattened(mapped, LookupObjectField(item, name));

                    break;
                }
                default:
                {
                    //A scalar element contributes nothing to the mapped lookup.
                    break;
                }
            }
        }

        return new JsonataSequence(mapped, KeepArray: false).Normalize();
    }

    /// <summary>Returns the value of a named field on an object focus, comparing keys over the UTF-8 span; undefined when absent.</summary>
    /// <param name="focus">The object focus.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The field value, or undefined when the object has no such key.</returns>
    private static JsonataValue LookupObjectField(JsonataValue focus, Utf8String name)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in focus.AsObject)
        {
            if(KeyEqualsName(entry.Key, name))
            {
                return entry.Value;
            }
        }

        return JsonataValue.Undefined;
    }

    /// <summary>Compares an object key against a field name by their UTF-8 bytes, encoding the key into scratch (no per-element string allocation of the name).</summary>
    /// <param name="key">The object entry key (a UTF-16 string).</param>
    /// <param name="name">The field name as UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when the key's UTF-8 encoding equals the name's bytes.</returns>
    private static bool KeyEqualsName(string key, Utf8String name)
    {
        int maxBytes = Encoding.UTF8.GetMaxByteCount(key.Length);
        if(maxBytes <= MaxKeyScratchBytes)
        {
            Span<byte> scratch = stackalloc byte[MaxKeyScratchBytes];
            int written = Encoding.UTF8.GetBytes(key, scratch);

            return name.Span.SequenceEqual(scratch[..written]);
        }

        byte[] encoded = Encoding.UTF8.GetBytes(key);

        return name.Span.SequenceEqual(encoded);
    }

    /// <summary>
    /// Combine phase: the conditional selects and schedules its branch (short-circuit); the binary and
    /// unary operators fold their already-computed children popped off the results stack.
    /// </summary>
    /// <param name="frame">The combine frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void CombineNode(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        switch(frame.Node)
        {
            case(ConditionalExpression conditional):
            {
                CombineConditional(conditional, frame.Context, work, results);

                break;
            }
            case(DefaultExpression def):
            {
                CombineDefault(def, frame.Context, work, results);

                break;
            }
            case(BinaryExpression binary):
            {
                //'and'/'or' short-circuit, so only the left operand is on the stack; the dedicated combine
                //decides whether the right is evaluated. Every other operator has both operands ready.
                if(binary.Operator is BinaryOperator.And or BinaryOperator.Or)
                {
                    CombineAndOr(binary, frame.Context, work, results);

                    break;
                }

                JsonataValue right = results.Pop();
                JsonataValue left = results.Pop();
                results.Push(ApplyBinary(left, binary.Operator, right));

                break;
            }
            case(UnaryExpression unary):
            {
                JsonataValue operand = results.Pop();
                results.Push(ApplyUnary(unary.Operator, operand));

                break;
            }
            case(KeepArrayExpression):
            {
                //Keep the source step's result an array (the JSONata keepSingleton marker): a singleton stays
                //an array and the marker rides on the value to the enclosing dot/map steps; undefined stays
                //undefined (the marker does not synthesize an array from nothing).
                results.Push(KeepAsArray(results.Pop()));

                break;
            }
            case(BindExpression bind):
            {
                //Install the value into the current frame and yield it: a bind is its bound value, and a
                //re-bind in the same frame overwrites the previous binding.
                JsonataValue value = results.Pop();
                frame.Context.Frame.Bind(bind.VariableName, value);
                results.Push(value);

                break;
            }
            case(RangeExpression):
            {
                JsonataValue high = results.Pop();
                JsonataValue low = results.Pop();
                results.Push(BuildRange(low, high));

                break;
            }
            case(ArrayConstructorExpression array):
            {
                results.Push(BuildArrayConstructor(array, results));

                break;
            }
            case(CallExpression call):
            {
                ApplyCall(call, frame.Context, work, results);

                break;
            }
            case(ApplyExpression apply):
            {
                CombineApply(apply, frame.Context, work, results);

                break;
            }
            default:
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"JSONata node '{frame.Node.GetType().Name}' has no combine rule."));
            }
        }
    }

    /// <summary>Selects a conditional's branch from its computed condition and schedules only that branch.</summary>
    /// <param name="conditional">The conditional node.</param>
    /// <param name="context">The evaluation context.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the condition value is on top).</param>
    private static void CombineConditional(ConditionalExpression conditional, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue condition = results.Pop();
        if(JsonataTruthiness.IsTruthy(condition))
        {
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = conditional.WhenTrue, Context = context });

            return;
        }

        if(conditional.WhenFalse is not null)
        {
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = conditional.WhenFalse, Context = context });

            return;
        }

        //A falsy condition with no else branch yields the undefined value.
        results.Push(JsonataValue.Undefined);
    }

    /// <summary>
    /// Resolves a default-value operator from its computed left operand: the left value is kept when it
    /// qualifies (truthy for Elvis <c>?:</c>, defined for coalesce <c>??</c>); otherwise the right operand
    /// (the fallback) is scheduled and its result becomes the value.
    /// </summary>
    /// <param name="def">The default-value node.</param>
    /// <param name="context">The evaluation context.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the left value is on top).</param>
    private static void CombineDefault(DefaultExpression def, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue left = results.Pop();
        bool keepLeft = def.Operator switch
        {
            DefaultOperator.Elvis => JsonataTruthiness.IsTruthy(left),
            _ => !left.IsUndefined
        };

        if(keepLeft)
        {
            results.Push(left);

            return;
        }

        //The left operand did not qualify, so the right operand (the fallback) is evaluated and its result
        //becomes the value.
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = def.Right, Context = context });
    }

    /// <summary>
    /// Resolves a short-circuiting boolean operator from its computed left operand: an <c>and</c> with a falsy
    /// left, or an <c>or</c> with a truthy left, determines the result (that left's own truthiness) without the
    /// right operand; otherwise the right operand is evaluated and the result is its truthiness.
    /// </summary>
    /// <param name="binary">The <c>and</c>/<c>or</c> node.</param>
    /// <param name="context">The evaluation context.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the left value is on top).</param>
    private static void CombineAndOr(BinaryExpression binary, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        bool leftBool = JsonataTruthiness.IsTruthy(results.Pop());

        //'and' is determined by a falsy left, 'or' by a truthy left; the determining left's truthiness is the
        //result, so the right operand is never evaluated.
        bool shortCircuits = binary.Operator == BinaryOperator.And ? !leftBool : leftBool;
        if(shortCircuits)
        {
            results.Push(JsonataValue.Boolean(leftBool));

            return;
        }

        //The left did not determine the result, so the value is the right operand's truthiness: evaluate the
        //right, then boolize it.
        work.Push(new EvalFrame { Kind = EvalFrameKind.Boolize, Node = binary, Context = context });
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = binary.Right, Context = context });
    }

    /// <summary>
    /// Selects the values of an object focus (every field value, in key order) or an array focus (every
    /// element, in order, the array being treated as an object whose keys are its indices), deep-flattening an
    /// array-valued field or element (arbitrarily nested arrays are spread, in order) into the result.
    /// Descends exactly one container level; a scalar, null, undefined, or function focus contributes nothing.
    /// </summary>
    /// <param name="focus">The focus value.</param>
    /// <returns>The selected values as a normalized sequence, or undefined over a scalar, null, undefined, or function focus.</returns>
    /// <remarks>
    /// An array focus is selected as its index-keyed values, matching the reference <c>evaluateWildcard</c>'s
    /// treatment of an array as an object. This build does not model the upstream length-1 outer-wrapper
    /// unwrap, an internal-sequence concern not observable for a user-supplied array.
    /// </remarks>
    /// <exception cref="JsonataEvaluationLimitException">The array-flatten depth bound was exceeded.</exception>
    private static JsonataValue EvaluateWildcard(JsonataValue focus)
    {
        if(focus.Kind != JsonataValueKind.Object && focus.Kind != JsonataValueKind.Array)
        {
            return JsonataValue.Undefined;
        }

        List<JsonataValue> selected = [];
        if(focus.Kind == JsonataValueKind.Object)
        {
            foreach(KeyValuePair<string, JsonataValue> entry in focus.AsObject)
            {
                //An array-valued field is deep-flattened into the result; every other value is pushed as-is,
                //matching the upstream wildcard's recursive flatten over an object's keys.
                AppendDeepFlattened(selected, entry.Value);
            }
        }
        else
        {
            foreach(JsonataValue element in focus.AsArray)
            {
                //An array focus is scanned as its index-keyed values: an array element is deep-flattened, every
                //other element is pushed as-is, matching the reference's treatment of an array as an object.
                AppendDeepFlattened(selected, element);
            }
        }

        return new JsonataSequence(selected, KeepArray: false).Normalize();
    }

    /// <summary>
    /// Deep-flattens a value into an accumulator over an explicit stack (no recursion): an array spreads
    /// its elements, recursively, at any depth, in order; undefined contributes nothing; every other value
    /// is pushed as a leaf. Bounded by the evaluation-depth limit.
    /// </summary>
    /// <param name="accumulator">The accumulator to append the flattened leaves to.</param>
    /// <param name="value">The value to deep-flatten in.</param>
    /// <exception cref="JsonataEvaluationLimitException">The array-flatten depth bound was exceeded.</exception>
    private static void AppendDeepFlattened(List<JsonataValue> accumulator, JsonataValue value)
    {
        if(value.IsUndefined)
        {
            return;
        }

        if(value.Kind != JsonataValueKind.Array)
        {
            accumulator.Add(value);

            return;
        }

        Stack<DeepFlattenCursor> stack = new();
        stack.Push(new DeepFlattenCursor(value.AsArray, depth: 1));

        while(stack.Count > 0)
        {
            DeepFlattenCursor cursor = stack.Peek();
            if(cursor.NextIndex >= cursor.Items.Count)
            {
                stack.Pop();

                continue;
            }

            JsonataValue item = cursor.Items[cursor.NextIndex];
            cursor.NextIndex++;
            if(item.Kind != JsonataValueKind.Array)
            {
                //A non-array leaf (including undefined, which spreads as nothing) is pushed as-is.
                if(!item.IsUndefined)
                {
                    accumulator.Add(item);
                }

                continue;
            }

            int childDepth = cursor.Depth + 1;
            if(childDepth > JsonataLimits.MaxEvaluationDepth)
            {
                throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "JSONata wildcard array flattening exceeded the maximum depth.");
            }

            stack.Push(new DeepFlattenCursor(item.AsArray, childDepth));
        }
    }

    /// <summary>
    /// Collects the focus and every value nested below it, at any depth, in pre-order, over an explicit
    /// stack bounded by the evaluation-depth limit (no recursion): a non-array value is pushed into the
    /// result before its children are visited; an array is a transparent container (never pushed, only its
    /// members visited); an object's field values are visited in key order.
    /// </summary>
    /// <param name="focus">The focus value.</param>
    /// <returns>The pre-order descendant values as a normalized sequence, or undefined when the focus is undefined.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The descendant-traversal depth bound was exceeded.</exception>
    private static JsonataValue EvaluateDescendant(JsonataValue focus)
    {
        if(focus.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        List<JsonataValue> collected = [];
        Stack<DescendantCursor> stack = new();
        stack.Push(new DescendantCursor(focus, depth: 1));

        while(stack.Count > 0)
        {
            DescendantCursor cursor = stack.Pop();
            if(cursor.Depth > JsonataLimits.MaxEvaluationDepth)
            {
                throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "JSONata descendant traversal exceeded the maximum depth.");
            }

            JsonataValue value = cursor.Value;
            if(value.Kind != JsonataValueKind.Array)
            {
                //A non-array value (object OR scalar) is pushed itself, in pre-order, before its children.
                collected.Add(value);
            }

            //Children are pushed in reverse so they pop — and are collected — in document/key order; an
            //array contributes its members, an object its field values, and a scalar nothing.
            ScheduleDescendantChildren(value, cursor.Depth + 1, stack);
        }

        return new JsonataSequence(collected, KeepArray: false).Normalize();
    }

    /// <summary>Pushes a value's children onto the descendant stack in reverse, so they pop in document/key order.</summary>
    /// <param name="value">The value whose children are scheduled.</param>
    /// <param name="childDepth">The traversal depth of the children.</param>
    /// <param name="stack">The descendant work stack.</param>
    private static void ScheduleDescendantChildren(JsonataValue value, int childDepth, Stack<DescendantCursor> stack)
    {
        switch(value.Kind)
        {
            case JsonataValueKind.Array:
            {
                IReadOnlyList<JsonataValue> items = value.AsArray;
                for(int i = items.Count - 1; i >= 0; i--)
                {
                    stack.Push(new DescendantCursor(items[i], childDepth));
                }

                break;
            }
            case JsonataValueKind.Object:
            {
                IReadOnlyList<KeyValuePair<string, JsonataValue>> entries = value.AsObject;
                for(int i = entries.Count - 1; i >= 0; i--)
                {
                    stack.Push(new DescendantCursor(entries[i].Value, childDepth));
                }

                break;
            }
            default:
            {
                //A scalar has no children to visit.
                break;
            }
        }
    }

    /// <summary>
    /// Builds the inclusive ascending integer range <c>low..high</c> as a normalized sequence of numbers:
    /// an undefined bound yields undefined (the empty sequence); a defined non-integer low bound throws
    /// T2003 and a defined non-integer high bound throws T2004; a low bound above the high bound yields
    /// undefined (not reversed, not an error); an element count above the cap throws D2014. The result is a
    /// sequence, so a one-element range (e.g. <c>3..3</c>) auto-unwraps to the bare number while a
    /// multi-element range is an array.
    /// </summary>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <returns>The normalized range sequence, or undefined for an undefined bound or an empty (low &gt; high) range.</returns>
    /// <exception cref="JsonataErrorException">A bound is a defined non-integer (T2003/T2004) or the range is too large (D2014).</exception>
    private static JsonataValue BuildRange(JsonataValue low, JsonataValue high)
    {
        //A defined-but-non-integer bound is the error case and is checked before the undefined-returns-empty
        //rule, matching the upstream guard order.
        if(!low.IsUndefined && !IsInteger(low))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.RangeLeftNotInteger, null, "The left side of the range operator must evaluate to an integer.");
        }

        if(!high.IsUndefined && !IsInteger(high))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.RangeRightNotInteger, null, "The right side of the range operator must evaluate to an integer.");
        }

        if(low.IsUndefined || high.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        double lo = low.AsNumber;
        double hi = high.AsNumber;
        if(lo > hi)
        {
            return JsonataValue.Undefined;
        }

        //The element count is computed in double space and capped before narrowing to int, so a span wider
        //than the long range cannot wrap a (long) cast negative and slip past the D2014 guard into a
        //non-terminating fill loop.
        double size = hi - lo + 1;
        if(size > JsonataLimits.MaxRangeSize)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.RangeTooLarge, null, "The size of the sequence produced by the range operator exceeded the maximum.");
        }

        int count = (int)size;
        List<JsonataValue> items = new(count);
        for(double value = lo; value <= hi; value++)
        {
            items.Add(JsonataValue.Number(value));
        }

        return new JsonataSequence(items, KeepArray: false).Normalize();
    }

    /// <summary>
    /// Builds an array constructor's value from its already-evaluated element results: the N results are on
    /// top of the results stack last-on-top, so they are popped into an indexed buffer back-to-front to
    /// recover source order. Each element is then folded left-to-right: an undefined value is skipped; a
    /// value whose element AST node is itself an <see cref="ArrayConstructorExpression"/> is kept whole (a
    /// nested array constructor stays one element); every other value is appended with one-level flatten
    /// (an array spreads its items, a scalar is added as one element). The constructed array is kept as is —
    /// it is not normalized, so a singleton stays a one-element array and is never auto-unwrapped.
    /// </summary>
    /// <param name="array">The array constructor node, consulted to tell a nested constructor element from a flattenable one, and for its cons marker.</param>
    /// <param name="results">The results stack carrying the N element values (last element on top).</param>
    /// <returns>The constructed array value (empty for the empty constructor); cons-marked when the constructor is a path step.</returns>
    /// <remarks>
    /// A constructor used as a path step carries the <see cref="ArrayConstructorExpression.ConsArray"/> marker
    /// (the JSONata <c>consarray</c> flag), so its value is built with <see cref="JsonataValue.ConsArray"/>:
    /// the enclosing dot/map step then keeps it whole rather than flattening one level. The marker rides on
    /// the value, so nested constructor steps (<c>a.[b.[c]]</c>) compose — each level produces a cons array
    /// the next-outer step keeps whole.
    /// </remarks>
    private static JsonataValue BuildArrayConstructor(ArrayConstructorExpression array, Stack<JsonataValue> results)
    {
        int count = array.Elements.Count;
        JsonataValue[] elementValues = new JsonataValue[count];
        for(int i = count - 1; i >= 0; i--)
        {
            elementValues[i] = results.Pop();
        }

        List<JsonataValue> items = [];
        for(int i = 0; i < count; i++)
        {
            JsonataValue value = elementValues[i];
            if(value.IsUndefined)
            {
                //An element that evaluates to nothing is omitted (not appended as a null/undefined slot).
                continue;
            }

            if(array.Elements[i] is ArrayConstructorExpression)
            {
                //A nested constructor stays one element: its array value is kept whole, never spread.
                items.Add(value);

                continue;
            }

            //Every other defined value is appended with one-level flatten (an array spreads its items).
            AppendFlattened(items, value);
        }

        //A constructor used as a path step is marked cons so the enclosing dot/map step keeps it whole.
        return array.ConsArray ? JsonataValue.ConsArray(items) : JsonataValue.Array(items);
    }

    /// <summary>
    /// Applies a function call from its already-evaluated procedure and argument results: the N argument
    /// values sit on top of the results stack (last argument on top, the procedure beneath them), so they
    /// are popped back-to-front into position order and the procedure is popped last; the shared
    /// <see cref="ApplyProcedure"/> path then validates the procedure (T1006 when it is not a function) and
    /// schedules the body. The body is scheduled on the existing work stack (exactly like the conditional
    /// schedules its branch), never evaluated by a recursive call, so deep recursion grows the work stack and
    /// is caught by the driver's depth bound.
    /// </summary>
    /// <param name="call">The call node, consulted for its argument count.</param>
    /// <param name="context">The call site's evaluation context.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack carrying the N argument values (last argument on top) and the procedure beneath them.</param>
    /// <exception cref="JsonataErrorException">The procedure value is not a function (code T1006).</exception>
    /// <exception cref="JsonataEvaluationLimitException">The call-nesting depth bound was exceeded.</exception>
    private static void ApplyCall(CallExpression call, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        int count = call.Arguments.Count;
        JsonataValue[] argumentValues = new JsonataValue[count];
        for(int i = count - 1; i >= 0; i--)
        {
            argumentValues[i] = results.Pop();
        }

        JsonataValue procedure = results.Pop();

        //A call whose argument list carried a '?' placeholder partially applies the procedure rather than
        //invoking it: the partial value is pushed straight onto the results stack (it is not applied now). A
        //placeholder argument evaluated to the internal marker, so it is detected among the argument values.
        if(ContainsPlaceholder(argumentValues))
        {
            results.Push(BuildPartial(call.Procedure, procedure, argumentValues, context));

            return;
        }

        ApplyProcedure(procedure, argumentValues, context, work, results, call.Procedure);
    }

    /// <summary>
    /// Applies a procedure value to an ordered list of already-evaluated argument values: a procedure value
    /// that is not a <see cref="JsonataLambda"/> function throws T1006. Otherwise a fresh child of the
    /// lambda's captured frame is opened, each parameter is bound to its argument by position (a missing
    /// trailing argument binds to undefined; a surplus argument is ignored), and the lambda body is SCHEDULED
    /// for evaluation under the lambda's captured focus and the new child frame — its result becomes the
    /// application's value. The body is scheduled on the existing work stack (exactly like the conditional
    /// schedules its branch), never evaluated by a recursive call, so deep application grows the work stack
    /// and is caught by the driver's depth bound. This is the shared apply path used by both the call operator
    /// <c>f(...)</c> and the function-application / chain operator <c>~&gt;</c>.
    /// </summary>
    /// <param name="procedure">The procedure value to apply.</param>
    /// <param name="arguments">The argument values in parameter order.</param>
    /// <param name="context">The application site's evaluation context.</param>
    /// <param name="work">The work stack the lambda body is scheduled onto.</param>
    /// <param name="results">The results stack a built-in function pushes its synchronous value onto.</param>
    /// <param name="procedureExpression">The procedure expression, consulted only to form the call error when the value is not a function: a bare name that is unbound but matches a built-in raises T1005 with a "did you mean $name?" suggestion. <see langword="null"/> (the chain, higher-order, and resident-cursor apply paths) raises the plain T1006.</param>
    /// <exception cref="JsonataErrorException">The procedure value is not a function (code T1006, or T1005 when <paramref name="procedureExpression"/> is a bare name matching a built-in), or a built-in's arguments fail signature validation (codes T0410/T0411/T0412).</exception>
    /// <exception cref="JsonataEvaluationLimitException">The call-nesting depth bound was exceeded.</exception>
    private static void ApplyProcedure(JsonataValue procedure, JsonataValue[] arguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results, JsonataExpression? procedureExpression = null)
    {
        //A partial fills its placeholder slots in order from the supplied arguments, completing the inner
        //procedure's argument list. When the inner procedure is itself a partial (a partial over a partial),
        //the completed list becomes the next level's supplied arguments and the unwrapping iterates in place
        //(no method recursion) until the procedure is a concrete function value, which the branches below
        //validate, bind, and dispatch exactly as a direct call would.
        while(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataPartial partial)
        {
            arguments = FillPlaceholders(partial.Slots, arguments);
            procedure = partial.Procedure;
        }

        if(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataLambda lambda)
        {
            //A lambda that declared a type signature validates its supplied arguments through the SAME path the
            //built-ins use: the validator resolves the effective argument list (context substitution from the
            //call-site focus for a '-' parameter, array singleton-wrapping, and the T0410/T0411/T0412 type
            //checks). A lambda with no signature binds the supplied arguments as-is.
            JsonataValue[] effective = lambda.Signature is JsonataSignature signature
                ? JsonataSignatureValidator.Validate(signature, arguments, context.Focus)
                : arguments;

            //A fresh child of the CAPTURED frame is the lambda's argument scope, so the parameters shadow the
            //captured bindings and the body still resolves the free variables (the lambda's own name included).
            JsonataBindingFrame childFrame = lambda.CapturedFrame.CreateChild();
            for(int i = 0; i < lambda.Parameters.Count; i++)
            {
                //A parameter past the effective arguments binds to undefined (too-few-arguments is not an error);
                //an argument past the parameter list is ignored (too-many-arguments is not an error).
                JsonataValue bound = i < effective.Length ? effective[i] : JsonataValue.Undefined;
                childFrame.Bind(lambda.Parameters[i], bound);
            }

            JsonataContext bodyContext = context.EnterLambda(lambda.CapturedFocus, childFrame);
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = lambda.Body, Context = bodyContext });

            return;
        }

        if(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataBuiltinFunction builtin)
        {
            //A built-in runs synchronously: the signature validator resolves the effective argument list
            //(context substitution, array singleton-wrapping, and the T0410/T0411/T0412 type checks), then the
            //named delegate runs and its value is pushed directly. No work is scheduled, so the driver never
            //recurses through a built-in and the results-stack invariant (one value left per apply path) holds.
            JsonataValue[] effective = JsonataSignatureValidator.Validate(builtin.Signature, arguments, context.Focus);

            //$replace with a regex pattern and a function replacement applies the user function once per match;
            //applying a function schedules its body (the result lands a turn later), so it cannot run inside
            //this synchronous delegate. A resident cursor drives the per-match application instead.
            if(IsRegexReplaceWithFunction(builtin, effective))
            {
                BeginRegexReplace(effective, context, work, results);

                return;
            }

            results.Push(builtin.Invoke(effective));

            return;
        }

        if(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataContextualBuiltinFunction contextual)
        {
            //A context-aware built-in runs synchronously like a pure built-in — the same signature validation
            //path — but its named delegate also receives the evaluation context, so the date built-ins
            //$now/$millis can read the evaluation's captured instant. No work is scheduled, so the
            //results-stack invariant (one value left per apply path) holds.
            JsonataValue[] effective = JsonataSignatureValidator.Validate(contextual.Signature, arguments, context.Focus);
            results.Push(contextual.Invoke(effective, context));

            return;
        }

        if(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataHigherOrderFunction higherOrder)
        {
            //A higher-order function applies a supplied function per element. It cannot run synchronously
            //because applying a lambda schedules its body (the result lands a turn later), so it opens a
            //resident per-element cursor instead of pushing a value here.
            BeginHigherOrder(higherOrder, arguments, context, work, results);

            return;
        }

        if(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataTransformer transformer)
        {
            //A transformer evaluates its clauses against the cloned input over several turns (each clause
            //schedules work whose result lands a turn later), so it opens a resident transform cursor instead
            //of pushing a value here.
            BeginTransform(transformer, arguments, context, work, results);

            return;
        }

        if(procedure.Kind == JsonataValueKind.Function && procedure.AsFunction is JsonataRegex regex)
        {
            //Applying a regex value to a string returns the first match object { match, index, groups } or the
            //undefined value when it does not match — matching the reference's regex-as-matcher application.
            results.Push(ApplyRegex(regex, arguments));

            return;
        }

        throw BuildNonFunctionCallError(procedureExpression, context);
    }

    /// <summary>
    /// Applies a regular-expression value directly to a string argument: it returns the first match object
    /// <c>{ match, index, groups }</c> at or after the start, or the undefined value when the regex does not
    /// match. A non-string (or absent) argument yields the undefined value, matching the reference's regex-as-
    /// matcher application returning nothing for a non-string input.
    /// </summary>
    /// <param name="regex">The regular-expression value being applied.</param>
    /// <param name="arguments">The application arguments; the string to match is the first.</param>
    /// <returns>The first match object, or the undefined value.</returns>
    private static JsonataValue ApplyRegex(JsonataRegex regex, JsonataValue[] arguments)
    {
        if(arguments.Length < 1 || arguments[0].Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        JsonataRegexMatch? match = regex.MatchAt(arguments[0].AsString, 0);

        return match is JsonataRegexMatch found ? JsonataRegexEngine.BuildMatchObject(found) : JsonataValue.Undefined;
    }

    /// <summary>
    /// Determines whether a built-in application is the <c>$replace</c> regular-expression function-replacement
    /// form — the <c>$replace</c> built-in whose pattern (the second argument) is a regular expression and
    /// whose replacement (the third argument) is a function value — which the resident replace cursor drives
    /// because the function is applied once per match across turns.
    /// </summary>
    /// <param name="builtin">The built-in being applied.</param>
    /// <param name="effective">The validated effective argument list.</param>
    /// <returns><see langword="true"/> when this is the <c>$replace</c> regex function-replacement form.</returns>
    private static bool IsRegexReplaceWithFunction(JsonataBuiltinFunction builtin, JsonataValue[] effective)
    {
        return builtin.Name.Equals(ReplaceFunctionName)
            && effective.Length > 2
            && effective[1].Kind == JsonataValueKind.Function
            && effective[1].AsFunction is JsonataRegex
            && effective[2].Kind == JsonataValueKind.Function
            && effective[2].AsFunction is not JsonataRegex;
    }

    /// <summary>
    /// Opens a resident <see cref="EvalFrameKind.RegexReplace"/> cursor for <c>$replace(str, /re/, fn[, limit])</c>:
    /// the matches are pre-computed synchronously (the zero-length D1004 guard fires here), an empty input or a
    /// zero limit short-circuits to the original string, and the cursor is pushed to apply the replacement
    /// function to each match's match object over the following turns. A non-string input yields the undefined
    /// value with no cursor.
    /// </summary>
    /// <param name="effective">The validated effective argument list: the string, the regex pattern, the function replacement, and an optional limit.</param>
    /// <param name="context">The application-site context the replacement function is applied under.</param>
    /// <param name="work">The work stack the cursor and the per-match application bodies are scheduled onto.</param>
    /// <param name="results">The results stack the cursor's final string is pushed onto.</param>
    /// <exception cref="JsonataErrorException">A continuation match is zero-length (code D1004).</exception>
    private static void BeginRegexReplace(JsonataValue[] effective, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(effective[0].Kind != JsonataValueKind.String)
        {
            results.Push(JsonataValue.Undefined);

            return;
        }

        string input = effective[0].AsString;
        JsonataRegex regex = (JsonataRegex)effective[1].AsFunction;
        int limit = ReadOptionalLimit(effective, 3);
        if(limit == 0)
        {
            results.Push(JsonataValue.String(input));

            return;
        }

        //Pre-compute the matches (matching is synchronous, so the D1004 zero-length guard fires now); the
        //cursor then only schedules the per-match function application across turns.
        List<RegexReplaceMatch> matches = [];
        foreach(JsonataRegexMatch match in JsonataRegexEngine.IterateMatches(regex, input))
        {
            if(matches.Count >= limit)
            {
                break;
            }

            matches.Add(new RegexReplaceMatch(match.Start, match.End, JsonataRegexEngine.BuildMatchObject(match)));
        }

        EvalFrame cursor = new()
        {
            Kind = EvalFrameKind.RegexReplace,
            Context = context,
            NextIndex = -1,
            RegexReplaceInput = input,
            RegexReplaceMatches = matches,
            RegexReplaceFunction = effective[2],
            RegexReplaceBuilder = new StringBuilder(),
            RegexReplacePosition = 0
        };
        work.Push(cursor);
    }

    /// <summary>
    /// Drives one turn of a regular-expression replace cursor: the seed turn (<see cref="EvalFrame.NextIndex"/>
    /// is <c>-1</c>) positions the cursor; each later turn folds the previous match's function result — which
    /// must be a string (else D3012) — into the output, copying the text between matches verbatim. While
    /// matches remain it schedules the next per-match application of the replacement function through the
    /// shared <see cref="ApplyProcedure"/> path; when the matches are exhausted it appends the unreplaced tail,
    /// pops the cursor, and pushes the replaced string.
    /// </summary>
    /// <param name="frame">The regular-expression replace cursor frame.</param>
    /// <param name="work">The work stack the per-match application bodies are scheduled onto.</param>
    /// <param name="results">The results stack the application results are folded from and the final string is pushed onto.</param>
    /// <exception cref="JsonataErrorException">The replacement function returned a non-string value (code D3012).</exception>
    private static void StepRegexReplaceFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        IReadOnlyList<RegexReplaceMatch> matches = frame.RegexReplaceMatches!;
        StringBuilder builder = frame.RegexReplaceBuilder!;
        string input = frame.RegexReplaceInput!;

        if(frame.NextIndex == -1)
        {
            frame.NextIndex = 0;
        }
        else
        {
            //Fold the previous match's replacement result: copy the text before the match verbatim, then the
            //replacement string (a non-string result is the D3012 error).
            JsonataValue replacement = results.Pop();
            if(replacement.Kind != JsonataValueKind.String)
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.ReplaceFunctionNotString, null, "The replacement function of the replace function must return a string.");
            }

            RegexReplaceMatch previous = matches[frame.NextIndex - 1];
            builder.Append(input, frame.RegexReplacePosition, previous.Start - frame.RegexReplacePosition);
            builder.Append(replacement.AsString);
            frame.RegexReplacePosition = previous.End;
        }

        if(frame.NextIndex < matches.Count)
        {
            ApplyProcedure(frame.RegexReplaceFunction, [matches[frame.NextIndex].MatchObject], frame.Context, work, results);
            frame.NextIndex++;

            return;
        }

        work.Pop();
        builder.Append(input, frame.RegexReplacePosition, input.Length - frame.RegexReplacePosition);
        results.Push(JsonataValue.String(builder.ToString()));
    }

    /// <summary>Reads an optional truncated-integer limit at a position in an argument list, treating an absent or non-numeric value as unbounded; a negative limit is treated as zero here (the function delegate's negative-limit error has already run on its own path).</summary>
    /// <param name="arguments">The argument list.</param>
    /// <param name="index">The position of the limit argument.</param>
    /// <returns>The limit, or <see cref="int.MaxValue"/> when absent.</returns>
    private static int ReadOptionalLimit(JsonataValue[] arguments, int index)
    {
        if(arguments.Length <= index || arguments[index].Kind != JsonataValueKind.Number)
        {
            return int.MaxValue;
        }

        double raw = arguments[index].AsNumber;
        if(raw < 0)
        {
            return 0;
        }

        return (int)Math.Truncate(raw);
    }

    /// <summary>
    /// Applies a higher-order cursor's supplied function to the synthesized per-element argument tuple. When
    /// the supplied function is a built-in, the tuple is trimmed to the built-in's higher-order arity before
    /// the shared apply path runs, matching how a higher-order caller delivers only as many of the
    /// <c>(value, index, array)</c> arguments as the applied built-in's required parameters take; a lambda
    /// receives the whole tuple and binds it by position.
    /// </summary>
    /// <param name="function">The higher-order cursor's supplied function value.</param>
    /// <param name="hofArguments">The synthesized per-element argument tuple.</param>
    /// <param name="context">The application-site context.</param>
    /// <param name="work">The work stack a lambda body is scheduled onto.</param>
    /// <param name="results">The results stack a built-in application pushes its value onto.</param>
    /// <exception cref="JsonataErrorException">The supplied function is not a function (T1006) or the trimmed built-in arguments fail validation (T0410/T0411/T0412).</exception>
    /// <exception cref="JsonataEvaluationLimitException">The call-nesting depth bound was exceeded.</exception>
    private static void ApplyHigherOrderFunction(JsonataValue function, JsonataValue[] hofArguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(function.Kind == JsonataValueKind.Function && function.AsFunction is JsonataBuiltinFunction builtin && builtin.Signature.HigherOrderArity < hofArguments.Length)
        {
            JsonataValue[] trimmed = new JsonataValue[builtin.Signature.HigherOrderArity];
            for(int i = 0; i < trimmed.Length; i++)
            {
                trimmed[i] = hofArguments[i];
            }

            ApplyProcedure(function, trimmed, context, work, results);

            return;
        }

        ApplyProcedure(function, hofArguments, context, work, results);
    }

    /// <summary>
    /// Combines a function-application / chain node <c>left ~&gt; right</c> from its already-evaluated
    /// operands on the results stack, in one of three forms decided by the right operand's AST shape and the
    /// left value's type:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Call-prepend (the right operand is a <see cref="CallExpression"/> with no <c>?</c> placeholder): pops
    /// the N argument values back-to-front, then the call's procedure, then the left value, builds the
    /// prepended argument list <c>[left, ...arguments]</c>, and applies the procedure to it through the shared
    /// apply path. A right call that carries a placeholder is not a call-prepend — it evaluates as a whole to
    /// a partial value handled by the non-call form below.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Non-call right: pops the right value and the left value. The right value must be a function (else
    /// T2006, checked before the apply-vs-compose decision regardless of the left value's type). Then, when
    /// the LEFT value is also a function it COMPOSES — pushing a NEW composed function value (a value, not
    /// applied now) equivalent to <c>function($x){ right(left($x)) }</c>; otherwise it APPLIES the right
    /// function to the single argument <c>[left]</c> through the shared apply path.
    /// </description>
    /// </item>
    /// </list>
    /// Exactly one value is left on the results stack on every path: the apply and call-prepend paths schedule
    /// a body whose result is the value, and the compose path pushes the composed function value directly.
    /// </summary>
    /// <param name="apply">The function-application / chain node.</param>
    /// <param name="context">The evaluation context.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack carrying the evaluated operands.</param>
    /// <exception cref="JsonataErrorException">The procedure / right value is not a function (code T1006).</exception>
    /// <exception cref="JsonataEvaluationLimitException">The call-nesting depth bound was exceeded.</exception>
    private static void CombineApply(ApplyExpression apply, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(apply.Right is CallExpression call && !CallHasPlaceholder(call))
        {
            //Call-prepend: the argument values sit on top (last argument on top), the procedure beneath them,
            //and the left value beneath that. Pop the arguments back-to-front into position order, then the
            //procedure, then the left value, and prepend the left value as the leading argument.
            int count = call.Arguments.Count;
            JsonataValue[] prepended = new JsonataValue[count + 1];
            for(int i = count - 1; i >= 0; i--)
            {
                prepended[i + 1] = results.Pop();
            }

            JsonataValue procedure = results.Pop();
            prepended[0] = results.Pop();
            ApplyProcedure(procedure, prepended, context, work, results);

            return;
        }

        JsonataValue right = results.Pop();
        JsonataValue left = results.Pop();
        if(right.Kind != JsonataValueKind.Function)
        {
            //The right side of '~>' must be a function — for both the apply and the compose case. This is
            //checked before the apply-vs-compose decision and regardless of the left value's type, so a
            //function left with a non-function right is the T2006 error, not a silently-built composition.
            throw new JsonataErrorException(WellKnownJsonataErrors.ChainRightNotFunction, null, "The right side of the function application operator '~>' must be a function.");
        }

        if(left.Kind == JsonataValueKind.Function)
        {
            //Compose: both operands are functions, so build (do not apply now) a new function value
            //equivalent to function($x){ right(left($x)) } and hand it up as the chain's value.
            results.Push(BuildComposedFunction(left, right, context, apply.Span));

            return;
        }

        //Apply: the right value is the function and the left value is its single argument.
        ApplyProcedure(right, [left], context, work, results);
    }

    /// <summary>
    /// Builds the composed function value for <c>first ~&gt; second</c>: a <see cref="JsonataLambda"/>
    /// equivalent to <c>function($x){ second(first($x)) }</c>, applied first-then-second. The two operand
    /// functions are bound under reserved names into the composed lambda's OWN captured frame (a fresh child
    /// of the current frame), and the synthetic body looks them up only there, so they cannot collide with
    /// user bindings. Both operands are functions (the caller validates the right operand before composing),
    /// and the body is an ordinary <see cref="CallExpression"/> tree, so the composed value is later invoked
    /// through the normal scheduled-on-the-work-stack apply path with no C# recursion.
    /// </summary>
    /// <param name="first">The inner-first function (the left operand), applied to the argument.</param>
    /// <param name="second">The inner-second function (the right operand), applied to the first's result.</param>
    /// <param name="context">The current context, whose focus and binding frame the composed lambda captures.</param>
    /// <param name="span">The composition site's source span, stamped on every synthetic body node.</param>
    /// <returns>The composed function value.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The binding-chain depth bound was exceeded.</exception>
    private static JsonataValue BuildComposedFunction(JsonataValue first, JsonataValue second, JsonataContext context, SourceSpan span)
    {
        //The operand functions live in the composed lambda's OWN captured frame under reserved names, so a
        //later call resolves them there while the parameter name shadows nothing the user can reach.
        JsonataBindingFrame capturedFrame = context.Frame.CreateChild();
        capturedFrame.Bind(ComposeFirstName, first);
        capturedFrame.Bind(ComposeSecondName, second);

        CallExpression body = BuildComposedBody(span);

        //A composed function carries no declared type signature; it binds its single synthetic argument
        //positionally.
        return JsonataValue.Function(new JsonataLambda([ComposeParameterName], body, capturedFrame, context.Focus, Signature: null));
    }

    /// <summary>
    /// Builds the synthetic body of a composed function: the call tree <c>second( first( parameter ) )</c>
    /// over the reserved names, every synthetic node stamped with the composition site's span. The inner call
    /// applies the inner-first function to the composed parameter; the outer call applies the inner-second
    /// function to that result.
    /// </summary>
    /// <param name="span">The composition site's source span, stamped on every synthetic node.</param>
    /// <returns>The synthetic body expression: the outer <c>second( first( parameter ) )</c> call.</returns>
    private static CallExpression BuildComposedBody(SourceSpan span)
    {
        VariableExpression parameter = new(span, VariableForm.Named, ComposeParameterName);
        VariableExpression firstFunction = new(span, VariableForm.Named, ComposeFirstName);
        VariableExpression secondFunction = new(span, VariableForm.Named, ComposeSecondName);

        CallExpression innerCall = new(span, firstFunction, [parameter]);

        return new CallExpression(span, secondFunction, [innerCall]);
    }

    /// <summary>Determines whether a call's argument expressions contain a partial-application placeholder <c>?</c>.</summary>
    /// <param name="call">The call expression to inspect.</param>
    /// <returns><see langword="true"/> when at least one argument is a <see cref="PlaceholderExpression"/>.</returns>
    private static bool CallHasPlaceholder(CallExpression call)
    {
        foreach(JsonataExpression argument in call.Arguments)
        {
            if(argument is PlaceholderExpression)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether any of a call's already-evaluated argument values is the placeholder marker.</summary>
    /// <param name="argumentValues">The evaluated argument values.</param>
    /// <returns><see langword="true"/> when at least one value is the placeholder marker.</returns>
    private static bool ContainsPlaceholder(JsonataValue[] argumentValues)
    {
        foreach(JsonataValue value in argumentValues)
        {
            if(IsPlaceholder(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether an evaluated value is the internal partial-application placeholder marker, by reference identity.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is the placeholder marker.</returns>
    private static bool IsPlaceholder(JsonataValue value)
    {
        return value.Kind == JsonataValueKind.Function && ReferenceEquals(value.AsFunction, PlaceholderMarker.AsFunction);
    }

    /// <summary>
    /// Builds a partial-application value from an evaluated procedure and its argument values, exactly one of
    /// which (or more) is the placeholder marker. The procedure must be a function value (a lambda, a built-in,
    /// or another partial); when it is not, a bare-name procedure whose name matches a built-in raises T1007
    /// (with a "did you mean $name?" suggestion) and any other non-function raises T1008. Each placeholder
    /// marker becomes an unbound slot (a <see langword="null"/> entry); each supplied argument keeps its value.
    /// </summary>
    /// <param name="procedureExpression">The procedure expression, consulted only to form the T1007 suggestion when the procedure value is not a function.</param>
    /// <param name="procedure">The evaluated procedure value.</param>
    /// <param name="argumentValues">The evaluated argument values, with placeholder markers in the unbound positions.</param>
    /// <param name="context">The application site's context, whose binding frame decides whether a bare procedure name shadows a built-in.</param>
    /// <returns>The partial-application function value.</returns>
    /// <exception cref="JsonataErrorException">The procedure value is not a function (code T1008, or T1007 when a same-named built-in exists).</exception>
    private static JsonataValue BuildPartial(JsonataExpression procedureExpression, JsonataValue procedure, JsonataValue[] argumentValues, JsonataContext context)
    {
        if(procedure.Kind != JsonataValueKind.Function)
        {
            throw BuildPartialNonFunctionError(procedureExpression, context);
        }

        JsonataValue?[] slots = new JsonataValue?[argumentValues.Length];
        for(int i = 0; i < argumentValues.Length; i++)
        {
            //A placeholder marker is an unbound slot (null); a supplied argument keeps its evaluated value.
            slots[i] = IsPlaceholder(argumentValues[i]) ? null : argumentValues[i];
        }

        return JsonataValue.Function(new JsonataPartial(procedure, slots));
    }

    /// <summary>
    /// Builds the error for partially applying a non-function procedure: T1007 when the procedure expression is
    /// a bare field name that is not bound in scope but matches a built-in of the same spelling (the
    /// reference's "did you mean $name?" hint), otherwise the plain T1008.
    /// </summary>
    /// <param name="procedureExpression">The procedure expression that produced the non-function value.</param>
    /// <param name="context">The application site's context, whose binding frame is checked so a bound name does not trigger the built-in suggestion.</param>
    /// <returns>The T1007 or T1008 error to throw.</returns>
    private static JsonataErrorException BuildPartialNonFunctionError(JsonataExpression procedureExpression, JsonataContext context)
    {
        if(procedureExpression is NameExpression name
            && !context.Frame.TryLookup(name.Name, out _)
            && JsonataBuiltins.TryResolve(name.Name, out _))
        {
            string suggestion = name.Name.ToString();

            return new JsonataErrorException(WellKnownJsonataErrors.PartialNonFunctionSuggestion, suggestion, string.Concat("Attempted to partially apply a non-function. Did you mean $", suggestion, "?"));
        }

        return new JsonataErrorException(WellKnownJsonataErrors.PartialNonFunction, null, "Attempted to partially apply a non-function.");
    }

    /// <summary>
    /// Builds the error for invoking a non-function procedure: T1005 when the procedure expression is a bare
    /// field name that is not bound in scope but matches a built-in of the same spelling (the reference's "did
    /// you mean $name?" hint), otherwise the plain T1006 — including every apply path with no bare-name callee
    /// expression (the chain operator, higher-order application, and the resident cursors), which pass
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="procedureExpression">The procedure expression that produced the non-function value, or <see langword="null"/> when the apply path has no callee expression.</param>
    /// <param name="context">The application site's context, whose binding frame is checked so a bound name does not trigger the built-in suggestion.</param>
    /// <returns>The T1005 or T1006 error to throw.</returns>
    private static JsonataErrorException BuildNonFunctionCallError(JsonataExpression? procedureExpression, JsonataContext context)
    {
        if(procedureExpression is NameExpression name
            && !context.Frame.TryLookup(name.Name, out _)
            && JsonataBuiltins.TryResolve(name.Name, out _))
        {
            string suggestion = name.Name.ToString();

            return new JsonataErrorException(WellKnownJsonataErrors.NonFunctionCallSuggestion, suggestion, string.Concat("Attempted to invoke a non-function. Did you mean $", suggestion, "?"));
        }

        return new JsonataErrorException(WellKnownJsonataErrors.NonFunctionCall, null, "Attempted to invoke a non-function.");
    }

    /// <summary>
    /// Fills a partial's placeholder slots in order from the supplied arguments, producing the inner
    /// procedure's complete argument list: a supplied slot keeps its value and each unbound slot
    /// (<see langword="null"/>) consumes the next supplied argument in order. A placeholder past the supplied
    /// arguments fills with undefined (too-few-arguments is not an error). The completed list has exactly one
    /// entry per slot — a partial behaves as a function of its placeholder count, so arguments beyond the
    /// placeholders (for instance the index and array a higher-order function passes) are ignored rather than
    /// appended, matching the reference's partial-as-lambda.
    /// </summary>
    /// <param name="slots">The partial's slots: a value for a supplied argument, <see langword="null"/> for an unbound placeholder.</param>
    /// <param name="arguments">The arguments supplied when the partial is applied, consumed in order by the unbound slots.</param>
    /// <returns>The completed argument list for the inner procedure.</returns>
    private static JsonataValue[] FillPlaceholders(IReadOnlyList<JsonataValue?> slots, JsonataValue[] arguments)
    {
        JsonataValue[] completed = new JsonataValue[slots.Count];
        int nextArgument = 0;
        for(int i = 0; i < slots.Count; i++)
        {
            if(slots[i] is JsonataValue supplied)
            {
                completed[i] = supplied;

                continue;
            }

            //An unbound slot consumes the next supplied argument, or binds to undefined when none remains;
            //arguments beyond the placeholders are ignored (the partial is a function of its placeholder count).
            completed[i] = nextArgument < arguments.Length ? arguments[nextArgument] : JsonataValue.Undefined;
            nextArgument++;
        }

        return completed;
    }

    /// <summary>Determines whether a value is an integral, finite number.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is a number that is finite and has no fractional part.</returns>
    private static bool IsInteger(JsonataValue value)
    {
        if(value.Kind != JsonataValueKind.Number)
        {
            return false;
        }

        double number = value.AsNumber;

        return double.IsFinite(number) && Math.Truncate(number) == number;
    }

    /// <summary>
    /// Drives one turn of a dot/map frame: the first turn consumes the source sequence (recording the
    /// keep-array marker it may carry); each later turn collects the previous item's step result, keeping a
    /// cons array (an array-constructor step) whole and flattening a normal navigated array one level; while
    /// items remain it schedules the step under the next item's rebound focus; when exhausted it normalizes
    /// the accumulator, propagating the keep-array marker so a <c>[]</c>-marked source keeps a singleton an
    /// array through this step.
    /// </summary>
    /// <param name="frame">The map cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepMapFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        MapExpression map = (MapExpression)frame.Node;

        if(frame.NextIndex == -1)
        {
            JsonataValue source = results.Pop();
            frame.KeepArrayResult = source.IsKeepSingletonArray;
            frame.Sequence = ToSequenceItems(source);
            frame.NextIndex = 0;
        }
        else
        {
            //The step result for the previous item is on top of the results stack; a cons array (an
            //array-constructor step) is kept whole, a normal array flattens one level. A step result carrying
            //the keep-array marker (a '[]'-marked inner step) propagates the marker to this map's result, so
            //the whole path keeps a singleton an array.
            JsonataValue stepResult = results.Pop();
            frame.KeepArrayResult |= stepResult.IsKeepSingletonArray;

            //Collect each defined per-item result WHOLE; the reference's single-array-kept-whole rule needs the
            //unflattened per-item results, so the fold runs once at the end (FlattenMapResults), not per item.
            if(!stepResult.IsUndefined)
            {
                frame.Accumulator.Add(stepResult);
            }
        }

        if(frame.NextIndex < frame.Sequence!.Count)
        {
            JsonataValue item = frame.Sequence[frame.NextIndex];
            frame.NextIndex++;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = map.Step, Context = frame.Context.WithFocus(item) });

            return;
        }

        work.Pop();
        results.Push(FlattenMapResults(frame.Accumulator, frame.KeepArrayResult));
    }

    /// <summary>
    /// Drives a predicate/index frame, split on the filter shape exactly as upstream <c>evaluateFilter</c>
    /// does: a literal numeric index selects one position once (with inner-array promotion); every other
    /// filter is evaluated per item, keeping the item when its result selects the current position (a
    /// number or an array of numbers) or is otherwise truthy.
    /// </summary>
    /// <param name="frame">The predicate cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepPredicateFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        PredicateExpression predicate = (PredicateExpression)frame.Node;

        if(frame.NextIndex == -1)
        {
            JsonataValue source = results.Pop();
            frame.KeepArrayResult = source.IsKeepSingletonArray;
            frame.Sequence = ToSequenceItems(source);
            frame.NextIndex = 0;

            //A literal numeric index is a single positional select with no per-item iteration; a
            //keep-array-marked source still keeps a single selected value an array.
            if(predicate.Filter is LiteralExpression { Kind: JsonataLiteralKind.Number } literal)
            {
                work.Pop();
                JsonataValue selected = SelectLiteralIndex(frame.Sequence, literal);
                results.Push(frame.KeepArrayResult ? KeepAsArray(selected) : selected);

                return;
            }
        }
        else
        {
            JsonataValue filterResult = results.Pop();
            int matchedIndex = frame.NextIndex - 1;
            if(KeepsItem(filterResult, matchedIndex, frame.Sequence!.Count))
            {
                frame.Accumulator.Add(frame.Sequence[matchedIndex]);
            }
        }

        if(frame.NextIndex < frame.Sequence!.Count)
        {
            JsonataValue item = frame.Sequence[frame.NextIndex];
            frame.NextIndex++;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = predicate.Filter, Context = frame.Context.WithFocus(item) });

            return;
        }

        work.Pop();
        results.Push(NormalizeStepResult(frame.Accumulator, frame.KeepArrayResult));
    }

    /// <summary>
    /// Drives one turn of the object constructor's group-by cursor, which stays resident across its turns
    /// (the frame is peeked, not popped, until the object is built). The led path-step form runs an extra
    /// leading seed-from-source turn that takes the grouping source's result as the input. The cursor then
    /// runs in two ordered phases: the bucketing phase evaluates each member pair's key under each input
    /// item's rebound focus and buckets the item (so a value can later aggregate over its whole group); the
    /// valuing phase evaluates each group pair's value under the grouped sub-sequence's rebound focus and
    /// collects the member when it is defined. Every loop is explicit and bounded by the work-stack depth and
    /// step budget the driver charges each turn.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepGroupByFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        switch(frame.GroupByPhase)
        {
            case GroupByPhase.SeedFromSource:
            {
                SeedGroupByFromSource(frame, work, results);

                break;
            }
            case GroupByPhase.Seed:
            {
                SeedGroupBy(frame, work, results);

                break;
            }
            case GroupByPhase.Bucketing:
            {
                BucketGroupByKey(frame, work, results);

                break;
            }
            case GroupByPhase.Valuing:
            {
                ValueGroupByGroup(frame, work, results);

                break;
            }
            default:
            {
                throw new InvalidOperationException("The JSONata group-by cursor reached an undefined phase.");
            }
        }
    }

    /// <summary>
    /// Seeds the prefix object constructor's group-by cursor from the current focus, then initialises the
    /// bucketing pass. The prefix form <c>{ ... }</c> groups the focus as a SINGLE value — its keys and values
    /// evaluate against the whole focus, not against each element of an array focus — so a field-reference key
    /// over an array focus evaluates to the array of that field's values (a non-string, hence T1003), matching
    /// the reference's standalone object construction. Per-element grouping is the led path-step form
    /// <c>path{ ... }</c>. An undefined focus still seeds one undefined item so a literal object builds over
    /// nothing.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SeedGroupBy(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue focus = frame.Context.Focus;
        IReadOnlyList<JsonataValue> items = focus.IsUndefined ? SingleUndefinedItem : [focus];
        SeedGroupByItems(frame, items, work, results);
    }

    /// <summary>
    /// Seeds the led path-step form's group-by cursor from the grouping source's already-evaluated result,
    /// which sits on top of the results stack, then initialises the bucketing pass. The led form
    /// <c>path{ ... }</c> groups that result PER ELEMENT (an array source buckets each of its elements); an
    /// empty source still seeds one undefined item so a literal object builds over nothing.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SeedGroupByFromSource(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        IReadOnlyList<JsonataValue> items = ToSequenceItems(results.Pop());
        SeedGroupByItems(frame, items.Count == 0 ? SingleUndefinedItem : items, work, results);
    }

    /// <summary>
    /// Initialises the group-by cursor over the items to bucket: sets the sequence, the ordered bucket
    /// structure and the result-entry accumulator, positions the bucketing cursor at the first
    /// <c>(item, pair)</c>, and schedules its key — or transitions straight to the valuing phase when there is
    /// no <c>(item, pair)</c> to evaluate. The prefix form seeds the focus as one whole-value item and the led
    /// path-step form seeds the source result's elements, so the bucketing and valuing passes are shared.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="items">The items to bucket: a single whole-focus item for the prefix form, the source result's elements for the led form.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void SeedGroupByItems(EvalFrame frame, IReadOnlyList<JsonataValue> items, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        frame.Sequence = items;
        frame.Groups = [];
        frame.GroupIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        frame.Entries = [];
        frame.NextIndex = 0;
        frame.PairIndex = 0;
        frame.GroupByPhase = GroupByPhase.Bucketing;

        ScheduleBucketCursor(frame, work, results);
    }

    /// <summary>
    /// Collects the just-evaluated key for the current bucketing cursor position, buckets the item under it
    /// (skipping an undefined key, throwing T1003 for a non-string key, throwing D1009 for a same-key
    /// collision from a different member pair, and appending to the first-seen bucket otherwise), advances
    /// the cursor, then schedules the next key or transitions to the valuing phase.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    /// <exception cref="JsonataErrorException">A key is a defined non-string (T1003) or collides across member pairs (D1009).</exception>
    private static void BucketGroupByKey(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue key = results.Pop();
        BucketItem(frame, frame.Sequence![frame.NextIndex], frame.PairIndex, key);
        AdvanceBucketCursor(frame);
        ScheduleBucketCursor(frame, work, results);
    }

    /// <summary>Records one focus item under its evaluated key per the group-by collision rules.</summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="item">The focus item being bucketed.</param>
    /// <param name="pairIndex">The member pair whose key produced this key value.</param>
    /// <param name="key">The evaluated key value.</param>
    /// <exception cref="JsonataErrorException">The key is a defined non-string (T1003) or collides across member pairs (D1009).</exception>
    private static void BucketItem(EvalFrame frame, JsonataValue item, int pairIndex, JsonataValue key)
    {
        if(key.IsUndefined)
        {
            //An undefined key skips this pair for this item; no member is produced.
            return;
        }

        if(key.Kind != JsonataValueKind.String)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ObjectKeyNotString, null, "A key in an object constructor must evaluate to a string.");
        }

        string keyText = key.AsString;
        if(frame.GroupIndexByKey!.TryGetValue(keyText, out int existing))
        {
            GroupByBucket bucket = frame.Groups![existing];
            if(bucket.PairIndex != pairIndex)
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.DuplicateGroupKey, null, "Multiple key definitions in an object constructor evaluate to the same key.");
            }

            //A same-pair collision appends the item to the existing group (the aggregation input).
            bucket.Items.Add(item);

            return;
        }

        //A first-seen key opens a new bucket, preserving insertion order.
        GroupByBucket created = new(keyText, pairIndex);
        created.Items.Add(item);
        frame.GroupIndexByKey[keyText] = frame.Groups!.Count;
        frame.Groups.Add(created);
    }

    /// <summary>Advances the bucketing cursor to the next <c>(item, pair)</c> position: the next pair of the current item, else the first pair of the next item.</summary>
    /// <param name="frame">The group-by cursor frame.</param>
    private static void AdvanceBucketCursor(EvalFrame frame)
    {
        ObjectConstructorExpression obj = (ObjectConstructorExpression)frame.Node;
        frame.PairIndex++;
        if(frame.PairIndex >= obj.Members.Count)
        {
            frame.PairIndex = 0;
            frame.NextIndex++;
        }
    }

    /// <summary>
    /// Schedules the key expression for the current bucketing cursor position under the item's rebound focus;
    /// when the cursor is exhausted (all items and pairs bucketed) it transitions to the valuing phase and
    /// schedules the first group's value instead.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void ScheduleBucketCursor(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        ObjectConstructorExpression obj = (ObjectConstructorExpression)frame.Node;
        if(obj.Members.Count > 0 && frame.NextIndex < frame.Sequence!.Count)
        {
            JsonataValue item = frame.Sequence[frame.NextIndex];
            JsonataExpression keyExpression = obj.Members[frame.PairIndex].Key;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = keyExpression, Context = frame.Context.WithFocus(item) });

            return;
        }

        //Bucketing is complete: begin the valuing phase from the first group.
        frame.GroupByPhase = GroupByPhase.Valuing;
        frame.GroupIndex = 0;
        ScheduleValueCursor(frame, work, results);
    }

    /// <summary>
    /// Collects the just-evaluated value for the current group, adds the member when the value is defined
    /// (omitting it otherwise), advances to the next group, then schedules the next group's value or builds
    /// and pushes the constructed object when the groups are exhausted.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void ValueGroupByGroup(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue value = results.Pop();
        GroupByBucket bucket = frame.Groups![frame.GroupIndex];
        if(!value.IsUndefined)
        {
            //A defined value sets the member; an undefined value omits it.
            frame.Entries!.Add(new KeyValuePair<string, JsonataValue>(bucket.Key, value));
        }

        frame.GroupIndex++;
        ScheduleValueCursor(frame, work, results);
    }

    /// <summary>
    /// Schedules the value expression for the current group under the grouped sub-sequence's rebound focus
    /// (a single-item group rebinds to that item, a multi-item group to the sequence of items); when the
    /// groups are exhausted it builds the object from the collected entries, pushes it, and pops the cursor.
    /// </summary>
    /// <param name="frame">The group-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void ScheduleValueCursor(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.GroupIndex < frame.Groups!.Count)
        {
            GroupByBucket bucket = frame.Groups[frame.GroupIndex];
            ObjectConstructorExpression obj = (ObjectConstructorExpression)frame.Node;
            JsonataExpression valueExpression = obj.Members[bucket.PairIndex].Value;
            JsonataValue groupFocus = GroupFocus(bucket.Items);
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = valueExpression, Context = frame.Context.WithFocus(groupFocus) });

            return;
        }

        //Every group has been valued: build the object preserving first-seen key order, pop this resident
        //cursor off the work stack, and hand the object up on the results stack.
        work.Pop();
        results.Push(JsonataValue.Object(frame.Entries!));
    }

    /// <summary>
    /// Drives one turn of a block frame, which stays resident across its turns (the frame is peeked, not
    /// popped, until the block is done). The seed turn opens a child binding frame so a bind in any statement
    /// is local to the block and visible to later statements; each subsequent turn discards the previous
    /// statement's value (only the last statement is the block's value) before scheduling the next statement
    /// under the same child frame and the same focus; the final turn leaves exactly the last statement's value
    /// on the results stack, or pushes undefined for the empty block <c>()</c>.
    /// </summary>
    /// <param name="frame">The block cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepBlockFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        BlockExpression block = (BlockExpression)frame.Node;

        if(frame.NextIndex == -1)
        {
            //Seed: open the block's own variable scope (a child binding frame). All statements then run
            //under this same child context, sharing it so a bind in statement i is visible to statement j>i.
            frame.Context = frame.Context.WithFrame(frame.Context.Frame.CreateChild());
            frame.NextIndex = 0;
        }
        else
        {
            //The just-finished statement's value is on top of the results stack. It is the block's value only
            //if it was the last statement; otherwise discard it before running the next statement.
            if(frame.NextIndex < block.Statements.Count)
            {
                results.Pop();
            }
        }

        if(frame.NextIndex < block.Statements.Count)
        {
            JsonataExpression statement = block.Statements[frame.NextIndex];
            frame.NextIndex++;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = statement, Context = frame.Context });

            return;
        }

        //Exhausted. The empty block yields undefined; otherwise the last statement's value is already on top
        //of the results stack and is left as the block's value.
        work.Pop();
        if(block.Statements.Count == 0)
        {
            results.Push(JsonataValue.Undefined);
        }
    }

    /// <summary>
    /// Begins a transform application: the single argument is the input to transform. An undefined input
    /// yields undefined with no cursor. Otherwise the input is deep-cloned (so the caller's value is never
    /// mutated), the clauses evaluate against the clone in a fresh child of the transformer's captured frame,
    /// and a resident <see cref="EvalFrameKind.Transform"/> cursor is pushed to drive the per-clause
    /// evaluation; the location pattern is scheduled over the clone so the cursor's first turn consumes the
    /// matched nodes from it.
    /// </summary>
    /// <param name="transformer">The transformer value being applied.</param>
    /// <param name="arguments">The application's evaluated arguments; the input to transform is the first.</param>
    /// <param name="context">The application-site context, whose root and captured instant the clause evaluations inherit.</param>
    /// <param name="work">The work stack the resident cursor and the pattern evaluation are pushed onto.</param>
    /// <param name="results">The results stack the undefined short-circuit pushes onto.</param>
    private static void BeginTransform(JsonataTransformer transformer, JsonataValue[] arguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue input = arguments.Length > 0 ? arguments[0] : JsonataValue.Undefined;
        if(input.IsUndefined)
        {
            //A transform over the undefined value yields undefined with no clause evaluation.
            results.Push(JsonataValue.Undefined);

            return;
        }

        //The clone is mutated in place by the per-match merge and delete, so the caller's input is untouched.
        //A matched node is a JsonataValue struct (passed by value), but its object backing is the SAME mutable
        //List instance embedded in the clone tree, so merging into / deleting from that list is visible through
        //the returned clone — the mechanism the per-match helpers rely on.
        JsonataValue clone = DeepClone(input);
        JsonataContext transformContext = context.EnterLambda(clone, transformer.CapturedFrame.CreateChild());

        EvalFrame cursor = new()
        {
            Kind = EvalFrameKind.Transform,
            Node = default!,
            Context = transformContext,
            Transformer = transformer,
            TransformResult = clone,
            TransformPhase = TransformPhase.Pattern,
            NextIndex = -1
        };

        work.Push(cursor);
        work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = transformer.Pattern, Context = transformContext });
    }

    /// <summary>Drives one turn of a transform cursor, dispatching on which clause it is collecting (the pattern once, then the update and delete clauses per matched node).</summary>
    /// <param name="frame">The transform cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepTransformFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        switch(frame.TransformPhase)
        {
            case TransformPhase.Pattern:
            {
                CollectTransformMatches(frame, work, results);

                break;
            }
            case TransformPhase.Update:
            {
                ApplyTransformUpdate(frame, work, results);

                break;
            }
            case TransformPhase.Delete:
            {
                ApplyTransformDelete(frame, work, results);

                break;
            }
            default:
            {
                throw new InvalidOperationException("The JSONata transform cursor reached an undefined phase.");
            }
        }
    }

    /// <summary>
    /// Collects the just-evaluated location pattern into the matched-node list (a single match becomes a
    /// one-element list, an array its elements, undefined the empty list — no matches), then schedules the
    /// first match's update clause, or completes the transform with the clone when there are no matches.
    /// </summary>
    /// <param name="frame">The transform cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the pattern value is on top).</param>
    private static void CollectTransformMatches(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue matches = results.Pop();
        frame.Sequence = ToSequenceItems(matches);
        frame.NextIndex = 0;
        ScheduleTransformMatch(frame, work, results);
    }

    /// <summary>
    /// Schedules the update clause for the current matched node under the match's rebound focus, advancing the
    /// cursor to the update phase; when the matches are exhausted it pops the cursor and pushes the mutated
    /// clone as the transform's result.
    /// </summary>
    /// <param name="frame">The transform cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void ScheduleTransformMatch(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.NextIndex < frame.Sequence!.Count)
        {
            JsonataValue match = frame.Sequence[frame.NextIndex];
            frame.TransformPhase = TransformPhase.Update;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = frame.Transformer!.Update, Context = frame.Context.WithFocus(match) });

            return;
        }

        //Every match has been transformed in place, so the clone carries the merges and deletions; pop this
        //resident cursor and hand the clone up as the transform's result.
        work.Pop();
        results.Push(frame.TransformResult);
    }

    /// <summary>
    /// Collects the just-evaluated update value for the current matched node, merges its members into the
    /// match (validating the update is an object — T2011), then schedules the delete clause when the transform
    /// has one or advances to the next match otherwise.
    /// </summary>
    /// <param name="frame">The transform cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the update value is on top).</param>
    /// <exception cref="JsonataErrorException">The update clause evaluated to a defined non-object value (code T2011).</exception>
    private static void ApplyTransformUpdate(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue update = results.Pop();
        JsonataValue match = frame.Sequence![frame.NextIndex];
        MergeTransformUpdate(match, update);

        if(frame.Transformer!.Delete is not null)
        {
            frame.TransformPhase = TransformPhase.Delete;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = frame.Transformer.Delete, Context = frame.Context.WithFocus(match) });

            return;
        }

        frame.NextIndex++;
        ScheduleTransformMatch(frame, work, results);
    }

    /// <summary>
    /// Collects the just-evaluated delete value for the current matched node, removes the named keys from the
    /// match (validating the value is a string or array of strings — T2012), then advances to the next match.
    /// </summary>
    /// <param name="frame">The transform cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the delete value is on top).</param>
    /// <exception cref="JsonataErrorException">The delete clause evaluated to a value other than a string or array of strings (code T2012).</exception>
    private static void ApplyTransformDelete(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue deletions = results.Pop();
        JsonataValue match = frame.Sequence![frame.NextIndex];
        ApplyTransformDeletions(match, deletions);

        frame.NextIndex++;
        ScheduleTransformMatch(frame, work, results);
    }

    /// <summary>
    /// Merges a transform update value into a matched node. An undefined update leaves the match unchanged; a
    /// defined non-object update is T2011. When both the update and the match are objects, each update member
    /// is written into the match's entry list in place — an existing key is overwritten where it sits
    /// (keeping its position), a new key is appended — so the mutation is visible through the shared clone. A
    /// non-object match takes no members (the reference's per-property assignment is a no-op on a non-object),
    /// but the T2011 type check on the update still applies.
    /// </summary>
    /// <param name="match">The matched node to merge into.</param>
    /// <param name="update">The update value.</param>
    /// <exception cref="JsonataErrorException">The update is a defined non-object value (code T2011).</exception>
    private static void MergeTransformUpdate(JsonataValue match, JsonataValue update)
    {
        if(update.IsUndefined)
        {
            return;
        }

        if(update.Kind != JsonataValueKind.Object)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.TransformUpdateNotObject, null, "The update clause of a transform must evaluate to an object.");
        }

        if(match.Kind != JsonataValueKind.Object || match.AsObject is not List<KeyValuePair<string, JsonataValue>> entries)
        {
            //A non-object match, or a match the pattern produced outside the clone (a constructed object whose
            //backing is not the mutable clone list), takes no merge: its in-place mutation would not surface
            //through the returned clone anyway, matching the reference where such a match is updated but never
            //appears in the result. The T2011 type check on the update above still applies in either case.
            return;
        }

        foreach(KeyValuePair<string, JsonataValue> member in update.AsObject)
        {
            UpsertEntry(entries, member.Key, member.Value);
        }
    }

    /// <summary>Writes a key/value into an object's entry list in place: an existing key is overwritten where it sits, a new key is appended.</summary>
    /// <param name="entries">The object's mutable entry list.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value to write.</param>
    private static void UpsertEntry(List<KeyValuePair<string, JsonataValue>> entries, string key, JsonataValue value)
    {
        for(int i = 0; i < entries.Count; i++)
        {
            if(string.Equals(entries[i].Key, key, StringComparison.Ordinal))
            {
                entries[i] = new KeyValuePair<string, JsonataValue>(key, value);

                return;
            }
        }

        entries.Add(new KeyValuePair<string, JsonataValue>(key, value));
    }

    /// <summary>
    /// Removes the keys a transform delete value names from a matched node. An undefined delete value removes
    /// nothing; a value that is neither a string nor an array of strings is T2012 (an empty array is a
    /// well-formed empty key list — it removes nothing). When the match is an object, each named key is
    /// removed from its entry list; a non-object match is left unchanged (the reference guards the deletion on
    /// the match being an object), but the T2012 type check still applies.
    /// </summary>
    /// <param name="match">The matched node to delete keys from.</param>
    /// <param name="deletions">The delete value: a key string or an array of key strings.</param>
    /// <exception cref="JsonataErrorException">The delete value is neither a string nor an array of strings (code T2012).</exception>
    private static void ApplyTransformDeletions(JsonataValue match, JsonataValue deletions)
    {
        if(deletions.IsUndefined)
        {
            return;
        }

        IReadOnlyList<JsonataValue> keys = ToSequenceItems(deletions);
        if(!IsDeletionKeyList(keys))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.TransformDeleteNotStrings, null, "The delete clause of a transform must evaluate to a string or array of strings.");
        }

        if(match.Kind != JsonataValueKind.Object || match.AsObject is not List<KeyValuePair<string, JsonataValue>> entries)
        {
            //A non-object match, or a match outside the clone with a non-clone backing, has no keys to remove
            //that would surface through the returned clone; the T2012 type check on the delete value above
            //still applies in either case.
            return;
        }

        foreach(JsonataValue key in keys)
        {
            RemoveEntry(entries, key.AsString);
        }
    }

    /// <summary>Determines whether every element of a delete-key list is a string (an empty list qualifies — it removes nothing).</summary>
    /// <param name="keys">The candidate key list.</param>
    /// <returns><see langword="true"/> when every element is a string.</returns>
    private static bool IsDeletionKeyList(IReadOnlyList<JsonataValue> keys)
    {
        foreach(JsonataValue key in keys)
        {
            if(key.Kind != JsonataValueKind.String)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes the first entry with the given key from an object's entry list, if present.</summary>
    /// <param name="entries">The object's mutable entry list.</param>
    /// <param name="key">The key to remove.</param>
    private static void RemoveEntry(List<KeyValuePair<string, JsonataValue>> entries, string key)
    {
        for(int i = 0; i < entries.Count; i++)
        {
            if(string.Equals(entries[i].Key, key, StringComparison.Ordinal))
            {
                entries.RemoveAt(i);

                return;
            }
        }
    }

    /// <summary>
    /// Produces a deep copy of a value with fresh, independently-mutable object and array backings, so the
    /// transform can merge into and delete from the clone's matched nodes in place without touching the
    /// caller's input. Scalars, strings, and null are copied by value; a function value (which has no JSON
    /// form) is carried by reference. The copy is built iteratively over an explicit work stack — each child
    /// container is attached to its parent in source order first and filled on a later turn — so it never
    /// recurses.
    /// </summary>
    /// <param name="value">The value to deep-clone.</param>
    /// <returns>The deep clone, sharing no mutable backing with <paramref name="value"/>.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The input's nesting depth exceeded the maximum.</exception>
    private static JsonataValue DeepClone(JsonataValue value)
    {
        if(value.Kind is not JsonataValueKind.Object and not JsonataValueKind.Array)
        {
            //A scalar, string, null, undefined, or function value has no mutable backing to copy.
            return value;
        }

        JsonataValue clone = NewEmptyContainer(value.Kind);
        Stack<(JsonataValue Source, JsonataValue Destination, int Depth)> work = new();
        work.Push((value, clone, 1));

        while(work.Count > 0)
        {
            (JsonataValue source, JsonataValue destination, int depth) = work.Pop();

            //The clone runs before the evaluation loop's own work-stack-depth guard applies, so the nesting
            //depth is bounded here, matching the field-lookup / wildcard / descendant traversals: a deeper
            //input throws the catchable limit rather than exhausting the heap.
            if(depth > JsonataLimits.MaxEvaluationDepth)
            {
                throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "JSONata transform input cloning exceeded the maximum depth.");
            }

            if(source.Kind == JsonataValueKind.Array)
            {
                CloneArrayInto(source.AsArray, (List<JsonataValue>)destination.AsArray, depth, work);

                continue;
            }

            CloneObjectInto(source.AsObject, (List<KeyValuePair<string, JsonataValue>>)destination.AsObject, depth, work);
        }

        return clone;
    }

    /// <summary>Creates an empty, mutable container value of the given kind — a fresh list backing for an array or an object.</summary>
    /// <param name="kind">The container kind (<see cref="JsonataValueKind.Array"/> or <see cref="JsonataValueKind.Object"/>).</param>
    /// <returns>The empty container value.</returns>
    private static JsonataValue NewEmptyContainer(JsonataValueKind kind)
    {
        return kind == JsonataValueKind.Array
            ? JsonataValue.Array(new List<JsonataValue>())
            : JsonataValue.Object(new List<KeyValuePair<string, JsonataValue>>());
    }

    /// <summary>
    /// Copies an array's elements into a fresh destination list in order: a scalar element is copied by value;
    /// a container element gets a fresh empty container appended now and scheduled (on the clone work stack) to
    /// be filled on a later turn, so the structure is built without recursion.
    /// </summary>
    /// <param name="source">The source array.</param>
    /// <param name="destination">The fresh destination list to fill.</param>
    /// <param name="depth">The nesting depth of <paramref name="source"/>; its child containers are scheduled one level deeper.</param>
    /// <param name="work">The clone work stack child containers are scheduled onto.</param>
    private static void CloneArrayInto(IReadOnlyList<JsonataValue> source, List<JsonataValue> destination, int depth, Stack<(JsonataValue Source, JsonataValue Destination, int Depth)> work)
    {
        foreach(JsonataValue item in source)
        {
            if(item.Kind is JsonataValueKind.Array or JsonataValueKind.Object)
            {
                JsonataValue child = NewEmptyContainer(item.Kind);
                destination.Add(child);
                work.Push((item, child, depth + 1));

                continue;
            }

            destination.Add(item);
        }
    }

    /// <summary>
    /// Copies an object's entries into a fresh destination list in order: a scalar value is copied by value; a
    /// container value gets a fresh empty container entry added now and scheduled (on the clone work stack) to
    /// be filled on a later turn.
    /// </summary>
    /// <param name="source">The source object's entries.</param>
    /// <param name="destination">The fresh destination entry list to fill.</param>
    /// <param name="depth">The nesting depth of <paramref name="source"/>; its child containers are scheduled one level deeper.</param>
    /// <param name="work">The clone work stack child containers are scheduled onto.</param>
    private static void CloneObjectInto(IReadOnlyList<KeyValuePair<string, JsonataValue>> source, List<KeyValuePair<string, JsonataValue>> destination, int depth, Stack<(JsonataValue Source, JsonataValue Destination, int Depth)> work)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in source)
        {
            if(entry.Value.Kind is JsonataValueKind.Array or JsonataValueKind.Object)
            {
                JsonataValue child = NewEmptyContainer(entry.Value.Kind);
                destination.Add(new KeyValuePair<string, JsonataValue>(entry.Key, child));
                work.Push((entry.Value, child, depth + 1));

                continue;
            }

            destination.Add(new KeyValuePair<string, JsonataValue>(entry.Key, entry.Value));
        }
    }

    /// <summary>
    /// Drives one turn of an order-by cursor, which stays resident across its turns (the frame is peeked, not
    /// popped, until the sort completes). The seed turn consumes the source sequence and short-circuits a
    /// single value (or a term-less sort) unchanged; each later turn collects the previous key value and, while
    /// keys remain, schedules the next element's next term key under the element's rebound focus; when every key
    /// is collected the elements are stably sorted and pushed.
    /// </summary>
    /// <param name="frame">The order-by cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepOrderByFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        SortExpression sort = (SortExpression)frame.Node;
        if(frame.NextIndex == -1)
        {
            SeedOrderBy(frame, sort, work, results);

            return;
        }

        //The key for the previous (element, term) position is on top of the results stack; collect it and
        //advance the (element, term) cursor before scheduling the next key or sorting.
        frame.SortKeyValues!.Add(results.Pop());
        AdvanceOrderByCursor(frame, sort);
        ScheduleOrderByKey(frame, sort, work, results);
    }

    /// <summary>
    /// Seeds the order-by cursor from the just-evaluated source sequence: a single value (or a term-less sort)
    /// needs no comparison and is returned unchanged (an empty sequence is undefined); otherwise the key
    /// accumulator and the (element, term) cursor are initialised and the first key is scheduled.
    /// </summary>
    /// <param name="frame">The order-by cursor frame.</param>
    /// <param name="sort">The order-by node.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack (the source value is on top).</param>
    private static void SeedOrderBy(EvalFrame frame, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        frame.Sequence = ToSequenceItems(results.Pop());
        if(frame.Sequence.Count <= 1 || sort.Terms.Count == 0)
        {
            work.Pop();
            results.Push(new JsonataSequence(frame.Sequence, KeepArray: false).Normalize());

            return;
        }

        frame.SortKeyValues = [];
        frame.NextIndex = 0;
        frame.PairIndex = 0;
        ScheduleOrderByKey(frame, sort, work, results);
    }

    /// <summary>
    /// Schedules the key expression for the current (element, term) cursor position under the element's rebound
    /// focus; when every key has been collected it pops the resident cursor and pushes the stably-sorted array.
    /// </summary>
    /// <param name="frame">The order-by cursor frame.</param>
    /// <param name="sort">The order-by node.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void ScheduleOrderByKey(EvalFrame frame, SortExpression sort, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.NextIndex < frame.Sequence!.Count)
        {
            JsonataValue item = frame.Sequence[frame.NextIndex];
            JsonataExpression key = sort.Terms[frame.PairIndex].Key;
            work.Push(new EvalFrame { Kind = EvalFrameKind.Expand, Node = key, Context = frame.Context.WithFocus(item) });

            return;
        }

        work.Pop();
        results.Push(SortByOrderKeys(frame.Sequence, frame.SortKeyValues!, sort.Terms));
    }

    /// <summary>Advances the order-by (element, term) cursor to the next term of the current element, else the first term of the next element.</summary>
    /// <param name="frame">The order-by cursor frame.</param>
    /// <param name="sort">The order-by node, consulted for its term count.</param>
    private static void AdvanceOrderByCursor(EvalFrame frame, SortExpression sort)
    {
        frame.PairIndex++;
        if(frame.PairIndex >= sort.Terms.Count)
        {
            frame.PairIndex = 0;
            frame.NextIndex++;
        }
    }

    /// <summary>
    /// Stably orders the elements by their collected keys (stored element-major, term-minor) with an iterative
    /// bottom-up merge sort, so equal elements keep their original order; the result is the normalized sorted
    /// sequence.
    /// </summary>
    /// <param name="items">The source elements.</param>
    /// <param name="flatKeys">The keys for every (element, term), at index <c>element * termCount + term</c>.</param>
    /// <param name="terms">The order-by terms, consulted for each term's direction.</param>
    /// <returns>The sorted sequence, normalized.</returns>
    /// <exception cref="JsonataErrorException">A key is not a number or a string (T2008) or two keys are different types (T2007).</exception>
    private static JsonataValue SortByOrderKeys(IReadOnlyList<JsonataValue> items, List<JsonataValue> flatKeys, IReadOnlyList<SortTerm> terms)
    {
        int[] order = OrderByKeyOrder(items.Count, flatKeys, terms);
        List<JsonataValue> sorted = new(items.Count);
        foreach(int index in order)
        {
            sorted.Add(items[index]);
        }

        return new JsonataSequence(sorted, KeepArray: false).Normalize();
    }

    /// <summary>
    /// Computes the stable sort order of <paramref name="count"/> elements by their collected keys (stored
    /// element-major, term-minor) with an iterative bottom-up merge sort, so equal elements keep their original
    /// order. The result is the permutation: <c>order[i]</c> is the original index of the element that belongs at
    /// position <c>i</c>. Shared by the order-by operator (which reorders values) and the tuple-stream sort step
    /// (which reorders tuples by the same key comparison).
    /// </summary>
    /// <param name="count">The number of elements.</param>
    /// <param name="flatKeys">The keys for every (element, term), at index <c>element * termCount + term</c>.</param>
    /// <param name="terms">The order-by terms, consulted for each term's direction.</param>
    /// <returns>The stable sort permutation of the element indices.</returns>
    /// <exception cref="JsonataErrorException">A key is not a number or a string (T2008) or two keys are different types (T2007).</exception>
    private static int[] OrderByKeyOrder(int count, List<JsonataValue> flatKeys, IReadOnlyList<SortTerm> terms)
    {
        int termCount = terms.Count;
        int[] order = new int[count];
        for(int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        int[] buffer = new int[count];
        for(int width = 1; width < count; width *= 2)
        {
            for(int start = 0; start < count; start += 2 * width)
            {
                int middle = Math.Min(start + width, count);
                int end = Math.Min(start + 2 * width, count);
                MergeOrderRuns(order, buffer, start, middle, end, flatKeys, terms, termCount);
            }

            //The merged buffer becomes the source for the next, wider pass; the roles alternate by swapping.
            (order, buffer) = (buffer, order);
        }

        return order;
    }

    /// <summary>
    /// Merges two adjacent runs <c>[start, middle)</c> and <c>[middle, end)</c> of the index array
    /// <paramref name="source"/> into <paramref name="buffer"/>, stably (a left-run index is taken before an
    /// equal-keyed right-run index) using the order-by key comparison.
    /// </summary>
    /// <param name="source">The index buffer the two runs are read from.</param>
    /// <param name="buffer">The index buffer the merged run is written into at the same positions.</param>
    /// <param name="start">The inclusive start of the left run.</param>
    /// <param name="middle">The exclusive end of the left run and inclusive start of the right run.</param>
    /// <param name="end">The exclusive end of the right run.</param>
    /// <param name="flatKeys">The collected keys.</param>
    /// <param name="terms">The order-by terms.</param>
    /// <param name="termCount">The number of terms.</param>
    /// <exception cref="JsonataErrorException">A key is not a number or a string (T2008) or two keys are different types (T2007).</exception>
    private static void MergeOrderRuns(int[] source, int[] buffer, int start, int middle, int end, List<JsonataValue> flatKeys, IReadOnlyList<SortTerm> terms, int termCount)
    {
        int left = start;
        int right = middle;
        for(int target = start; target < end; target++)
        {
            bool takeLeft = right >= end || (left < middle && CompareOrderItems(source[left], source[right], flatKeys, terms, termCount) <= 0);
            if(takeLeft)
            {
                buffer[target] = source[left];
                left++;

                continue;
            }

            buffer[target] = source[right];
            right++;
        }
    }

    /// <summary>
    /// Compares two elements by their order-by keys term by term — the first non-zero term decides. An
    /// undefined key sorts after a defined one in either direction (the term's direction is not applied to the
    /// undefined case, matching the reference); a key that is not a number or string raises T2008; two keys of
    /// different types raise T2007; otherwise the values are compared (numeric or ordinal) and the term's
    /// direction applied.
    /// </summary>
    /// <param name="left">The left element's index.</param>
    /// <param name="right">The right element's index.</param>
    /// <param name="flatKeys">The collected keys.</param>
    /// <param name="terms">The order-by terms.</param>
    /// <param name="termCount">The number of terms.</param>
    /// <returns>A negative value when the left element orders first, a positive value when the right does, zero when equal.</returns>
    /// <exception cref="JsonataErrorException">A key is not a number or a string (T2008) or two keys are different types (T2007).</exception>
    private static int CompareOrderItems(int left, int right, List<JsonataValue> flatKeys, IReadOnlyList<SortTerm> terms, int termCount)
    {
        for(int t = 0; t < termCount; t++)
        {
            JsonataValue keyLeft = flatKeys[(left * termCount) + t];
            JsonataValue keyRight = flatKeys[(right * termCount) + t];
            if(keyLeft.IsUndefined)
            {
                if(keyRight.IsUndefined)
                {
                    continue;
                }

                return 1;
            }

            if(keyRight.IsUndefined)
            {
                return -1;
            }

            EnsureSortKeyComparable(keyLeft);
            EnsureSortKeyComparable(keyRight);
            if(keyLeft.Kind != keyRight.Kind)
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.OrderByTypeMismatch, null, "Type mismatch when comparing values in the order-by clause.");
            }

            int comparison = keyLeft.Kind == JsonataValueKind.Number
                ? keyLeft.AsNumber.CompareTo(keyRight.AsNumber)
                : string.CompareOrdinal(keyLeft.AsString, keyRight.AsString);
            if(comparison == 0)
            {
                continue;
            }

            int directed = terms[t].Direction == SortDirection.Descending ? -comparison : comparison;

            return directed < 0 ? -1 : 1;
        }

        return 0;
    }

    /// <summary>Validates that an order-by key is a number or a string, raising T2008 otherwise.</summary>
    /// <param name="key">The key value to validate.</param>
    /// <exception cref="JsonataErrorException">The key is not a number or a string (code T2008).</exception>
    private static void EnsureSortKeyComparable(JsonataValue key)
    {
        if(key.Kind is not JsonataValueKind.Number and not JsonataValueKind.String)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.OrderByKeyNotComparable, null, "An expression in the order-by clause did not evaluate to a number or a string.");
        }
    }

    /// <summary>
    /// Begins a higher-order function application, dispatching on whether the kind iterates an array, iterates
    /// an object's entries, or sorts an array: the object kinds (<c>$sift</c>/<c>$each</c>) and <c>$sort</c>
    /// route to their own seed helpers; every other kind opens the per-element array cursor. An undefined
    /// source short-circuits to undefined with no cursor, matching every higher-order function over a missing
    /// source.
    /// </summary>
    /// <param name="higherOrder">The higher-order function being applied.</param>
    /// <param name="arguments">The call site's evaluated arguments: the source, the applied function, and (for <c>$reduce</c>) an optional initial value.</param>
    /// <param name="context">The application-site context the applications run under.</param>
    /// <param name="work">The work stack the resident cursor is pushed onto.</param>
    /// <param name="results">The results stack a short-circuit or a synchronous result pushes onto.</param>
    /// <exception cref="JsonataErrorException">A default-comparator <c>$sort</c> over a non-numeric/non-string array (code D3070).</exception>
    private static void BeginHigherOrder(JsonataHigherOrderFunction higherOrder, JsonataValue[] arguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue source = arguments.Length > 0 ? arguments[0] : JsonataValue.Undefined;
        switch(higherOrder.Kind)
        {
            case HigherOrderKind.Sift:
            case HigherOrderKind.Each:
            {
                BeginObjectHigherOrder(higherOrder.Kind, arguments, context, work, results);

                return;
            }
            case HigherOrderKind.Sort:
            {
                BeginSort(source, arguments, context, work, results);

                return;
            }
            default:
            {
                BeginArrayHigherOrder(higherOrder.Kind, source, arguments, context, work, results);

                return;
            }
        }
    }

    /// <summary>
    /// Begins an array higher-order application (<c>$map</c>/<c>$filter</c>/<c>$single</c>/<c>$reduce</c>): an
    /// undefined source short-circuits to undefined; otherwise a resident <see cref="EvalFrameKind.HigherOrder"/>
    /// cursor is pushed carrying the kind, the source items, the applied function value (the second argument),
    /// and — for <c>$reduce</c> — the supplied-argument count so the seed phase can tell an absent initial value
    /// apart and apply the reducer-arity rule. The cursor itself seeds and schedules each per-element
    /// application on its first turn; nothing is scheduled here beyond the resident frame.
    /// </summary>
    /// <param name="kind">The array higher-order kind.</param>
    /// <param name="source">The source value (its first argument).</param>
    /// <param name="arguments">The call site's evaluated arguments.</param>
    /// <param name="context">The application-site context the per-element applications run under.</param>
    /// <param name="work">The work stack the resident cursor is pushed onto.</param>
    /// <param name="results">The results stack the undefined short-circuit pushes onto.</param>
    private static void BeginArrayHigherOrder(HigherOrderKind kind, JsonataValue source, JsonataValue[] arguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(source.IsUndefined)
        {
            //An undefined source array yields undefined with no per-element work for every array kind.
            results.Push(JsonataValue.Undefined);

            return;
        }

        //$map/$filter/$reduce require a function; $single's predicate is optional (an omitted predicate returns
        //the array's sole element, or D3138/D3139 for many/none), so it is validated only when supplied. A
        //missing or non-function argument otherwise is the T0410 signature error, so $map($add) — the array
        //omitted and the function in its place — is rejected before the source is mapped rather than applying
        //an undefined value per element.
        JsonataValue function = arguments.Length > 1 ? arguments[1] : JsonataValue.Undefined;
        if((kind != HigherOrderKind.Single || arguments.Length > 1) && function.Kind != JsonataValueKind.Function)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ArgumentMismatch, null, "A higher-order array function requires a function argument.");
        }

        EvalFrame cursor = new()
        {
            Kind = EvalFrameKind.HigherOrder,
            Node = default!,
            Context = context,
            HigherOrderKind = kind,
            Sequence = ToSequenceItems(source),
            HigherOrderFunction = function,
            HigherOrderArgumentCount = arguments.Length,
            ReduceAccumulator = arguments.Length > 2 ? arguments[2] : JsonataValue.Undefined,
            NextIndex = -1
        };

        work.Push(cursor);
    }

    /// <summary>
    /// Begins an object higher-order application (<c>$sift</c>/<c>$each</c>). The object parameter is
    /// context-injectable (signatures <c>$sift &lt;o-f?:o&gt;</c> / <c>$each &lt;o-f:a&gt;</c>): when the only
    /// supplied argument is the predicate function the object is taken from the evaluation focus, and an
    /// explicit object is the first of two arguments. A source that is not an object (the undefined value
    /// included) short-circuits to undefined with no cursor; otherwise a resident
    /// <see cref="EvalFrameKind.HigherOrder"/> cursor is pushed carrying the kind, the object's entries in
    /// insertion order, the original object value (passed as each application's third argument), and the
    /// applied function value. The cursor seeds and schedules each per-entry application on its first turn.
    /// </summary>
    /// <param name="kind">The object higher-order kind (<see cref="HigherOrderKind.Sift"/> or <see cref="HigherOrderKind.Each"/>).</param>
    /// <param name="arguments">The call site's evaluated arguments.</param>
    /// <param name="context">The application-site context the per-entry applications run under, whose focus fills the context-injectable object parameter.</param>
    /// <param name="work">The work stack the resident cursor is pushed onto.</param>
    /// <param name="results">The results stack the undefined short-circuit pushes onto.</param>
    private static void BeginObjectHigherOrder(HigherOrderKind kind, JsonataValue[] arguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        (JsonataValue source, JsonataValue function) = ResolveObjectHigherOrderArguments(arguments, context);
        if(source.Kind != JsonataValueKind.Object)
        {
            //A non-object (the undefined value included) source yields undefined with no per-entry work.
            results.Push(JsonataValue.Undefined);

            return;
        }

        EvalFrame cursor = new()
        {
            Kind = EvalFrameKind.HigherOrder,
            Node = default!,
            Context = context,
            HigherOrderKind = kind,
            HigherOrderEntries = source.AsObject,
            HigherOrderObject = source,
            HigherOrderFunction = function,
            NextIndex = -1
        };

        work.Push(cursor);
    }

    /// <summary>
    /// Resolves the (object, predicate) pair for an object higher-order application from its call-site
    /// arguments, honouring the context-injectable object parameter: two arguments are an explicit object and
    /// predicate; a single function argument is the predicate with the object taken from the focus; a single
    /// non-function argument is an explicit object with no predicate; with no arguments both are undefined,
    /// yielding the undefined short-circuit.
    /// </summary>
    /// <param name="arguments">The call site's evaluated arguments.</param>
    /// <param name="context">The application-site context whose focus fills the injectable object parameter.</param>
    /// <returns>The resolved object source and predicate function.</returns>
    private static (JsonataValue Source, JsonataValue Function) ResolveObjectHigherOrderArguments(JsonataValue[] arguments, JsonataContext context)
    {
        return arguments.Length switch
        {
            0 => (JsonataValue.Undefined, JsonataValue.Undefined),
            1 when arguments[0].Kind == JsonataValueKind.Function => (context.Focus, arguments[0]),
            1 => (arguments[0], JsonataValue.Undefined),
            _ => (arguments[0], arguments[1])
        };
    }

    /// <summary>
    /// Begins a <c>$sort</c> application, dispatching on whether a comparator was supplied. An undefined source
    /// short-circuits to undefined; an array of one or zero elements is returned unchanged (no comparator call,
    /// no type validation). With no comparator the default path validates the array is all numbers or all
    /// strings (else D3070), runs the synchronous iterative ascending merge sort, and pushes the result. With a
    /// comparator a resident <see cref="EvalFrameKind.HigherOrder"/> cursor is pushed to drive the
    /// suspended-comparison stable insertion sort.
    /// </summary>
    /// <param name="source">The source value (its first argument).</param>
    /// <param name="arguments">The call site's evaluated arguments: the source array and an optional comparator.</param>
    /// <param name="context">The application-site context the comparator applications run under.</param>
    /// <param name="work">The work stack the resident cursor is pushed onto.</param>
    /// <param name="results">The results stack a short-circuit or the synchronous sort result pushes onto.</param>
    /// <exception cref="JsonataErrorException">A default-comparator sort over a non-numeric/non-string array (code D3070).</exception>
    private static void BeginSort(JsonataValue source, JsonataValue[] arguments, JsonataContext context, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(source.IsUndefined)
        {
            //An undefined source array yields undefined with no sort.
            results.Push(JsonataValue.Undefined);

            return;
        }

        IReadOnlyList<JsonataValue> items = ToSequenceItems(source);
        if(items.Count <= 1)
        {
            //A zero- or one-element array is already sorted; it is returned as an array unchanged, with no
            //comparator call and no type validation.
            results.Push(JsonataValue.Array(items));

            return;
        }

        JsonataValue comparator = arguments.Length > 1 ? arguments[1] : JsonataValue.Undefined;
        if(comparator.IsUndefined)
        {
            results.Push(SortDefault(items));

            return;
        }

        EvalFrame cursor = new()
        {
            Kind = EvalFrameKind.HigherOrder,
            Node = default!,
            Context = context,
            HigherOrderKind = HigherOrderKind.Sort,
            HigherOrderFunction = comparator,
            SortWorking = [.. items],
            NextIndex = 0
        };

        work.Push(cursor);
    }

    /// <summary>
    /// Sorts an array with the native ascending comparator (the default <c>$sort</c> path): the array must be
    /// all numbers or all strings (mixed or other-typed throws D3070), then a synchronous iterative bottom-up
    /// stable merge sort orders it (ascending numeric, or ascending ordinal for strings).
    /// </summary>
    /// <param name="items">The source items (already known to hold two or more elements).</param>
    /// <returns>The sorted array value.</returns>
    /// <exception cref="JsonataErrorException">The array is neither all numbers nor all strings (code D3070).</exception>
    private static JsonataValue SortDefault(IReadOnlyList<JsonataValue> items)
    {
        bool allNumbers = IsArrayOfNumbers(items);
        if(!allNumbers && !IsAllStrings(items))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.SortDefaultComparatorType, null, "The array passed to '$sort' without a comparator must be all numbers or all strings.");
        }

        return JsonataValue.Array(MergeSortAscending(items, allNumbers));
    }

    /// <summary>
    /// Orders a homogeneous (all-number or all-string) array ascending with an iterative bottom-up stable
    /// merge sort: runs of doubling width are merged through a static native comparison, alternating between
    /// two buffers, so the sort is O(n log n) with no recursion.
    /// </summary>
    /// <param name="items">The source items.</param>
    /// <param name="numeric"><see langword="true"/> to compare as numbers; <see langword="false"/> to compare as ordinal strings.</param>
    /// <returns>The ascending-ordered items.</returns>
    private static List<JsonataValue> MergeSortAscending(IReadOnlyList<JsonataValue> items, bool numeric)
    {
        int count = items.Count;
        JsonataValue[] source = [.. items];
        JsonataValue[] buffer = new JsonataValue[count];
        for(int width = 1; width < count; width *= 2)
        {
            for(int start = 0; start < count; start += 2 * width)
            {
                int middle = Math.Min(start + width, count);
                int end = Math.Min(start + 2 * width, count);
                MergeRuns(source, buffer, start, middle, end, numeric);
            }

            //The merged buffer becomes the source for the next, wider pass; the roles alternate by swapping.
            (source, buffer) = (buffer, source);
        }

        return [.. source];
    }

    /// <summary>
    /// Merges two adjacent ascending runs <c>[start, middle)</c> and <c>[middle, end)</c> of
    /// <paramref name="source"/> into <paramref name="buffer"/>, stably (an element of the left run is taken
    /// before an equal element of the right run) using the static native ascending comparison.
    /// </summary>
    /// <param name="source">The buffer the two runs are read from.</param>
    /// <param name="buffer">The buffer the merged run is written into at the same positions.</param>
    /// <param name="start">The inclusive start of the left run.</param>
    /// <param name="middle">The exclusive end of the left run and inclusive start of the right run.</param>
    /// <param name="end">The exclusive end of the right run.</param>
    /// <param name="numeric"><see langword="true"/> to compare as numbers; <see langword="false"/> to compare as ordinal strings.</param>
    private static void MergeRuns(JsonataValue[] source, JsonataValue[] buffer, int start, int middle, int end, bool numeric)
    {
        int left = start;
        int right = middle;
        for(int target = start; target < end; target++)
        {
            bool takeLeft = right >= end || (left < middle && NativeCompare(source[left], source[right], numeric) <= 0);
            if(takeLeft)
            {
                buffer[target] = source[left];
                left++;

                continue;
            }

            buffer[target] = source[right];
            right++;
        }
    }

    /// <summary>Compares two homogeneous values for the native ascending order: numeric for numbers, ordinal for strings.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="numeric"><see langword="true"/> to compare as numbers; <see langword="false"/> to compare as ordinal strings.</param>
    /// <returns>A negative value when <paramref name="left"/> orders before <paramref name="right"/>, zero when equal, a positive value otherwise.</returns>
    private static int NativeCompare(JsonataValue left, JsonataValue right, bool numeric)
    {
        if(numeric)
        {
            return left.AsNumber.CompareTo(right.AsNumber);
        }

        return string.CompareOrdinal(left.AsString, right.AsString);
    }

    /// <summary>Determines whether every element of an array is a string (an empty array is not an array of strings).</summary>
    /// <param name="items">The array items.</param>
    /// <returns><see langword="true"/> when the array is non-empty and every element is a string.</returns>
    private static bool IsAllStrings(IReadOnlyList<JsonataValue> items)
    {
        if(items.Count == 0)
        {
            return false;
        }

        foreach(JsonataValue item in items)
        {
            if(item.Kind != JsonataValueKind.String)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Drives one turn of a higher-order cursor by dispatching on its kind: the object kinds
    /// (<c>$sift</c>/<c>$each</c>) iterate an object's entries, <c>$sort</c> orders an array through its
    /// suspended-comparison insertion sort, and every other kind (<c>$map</c>/<c>$filter</c>/<c>$single</c>/
    /// <c>$reduce</c>) iterates an array's elements. Each per-kind step folds the previous application's result,
    /// schedules the next application through the shared <see cref="ApplyProcedure"/> path (transparently
    /// whether the applied function is a lambda, whose body lands a turn later, or a synchronous built-in, whose
    /// result is pushed immediately), and on exhaustion pops the cursor and pushes the kind's final value. The
    /// applied function evaluates against its OWN captured focus and frame — the source value is an argument,
    /// not a rebound focus.
    /// </summary>
    /// <param name="frame">The higher-order cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    /// <remarks>
    /// <para>
    /// Fragment-relative divergences from the reference apply to the higher-order functions:
    /// </para>
    /// <para>
    /// The applied function is always passed the full argument list (<c>(value, index, array)</c> for the array
    /// functions, <c>(accumulator, value, index, array)</c> for <c>$reduce</c>, <c>(value, key, object)</c> for
    /// the object functions); the reference trims that list to the applied function's arity. This is
    /// observationally identical for every function shape, because a lambda binds its parameters positionally
    /// and ignores surplus arguments while a built-in reads its arguments by index and ignores extras, so a
    /// declared parameter always receives its argument in both models and a non-existent parameter is never
    /// filled in either; no per-function arity metadata is kept here. The <c>$sort</c> comparator is the one
    /// exception: it is always passed EXACTLY two arguments, never an index or array.
    /// </para>
    /// <para>
    /// The <c>$reduce</c> reducer-arity error (D3050) is enforced for a lambda reducer (its declared parameter
    /// count); a non-lambda reducer (a built-in) is not arity-checked, deferred with the signature/arity model.
    /// </para>
    /// <para>
    /// No signature type validation is performed (the inherited E1 divergence): a wrong-typed argument follows
    /// the graceful-undefined / body rule rather than a T0410-T0412 signature error.
    /// </para>
    /// <para>
    /// The <c>$sort</c> custom-comparator path is a stable insertion sort (O(n²) comparisons), observationally
    /// identical in output to the reference's stable merge sort because stable-sort output is unique for a given
    /// comparator; a comparator returns true when its left argument must be ordered after its right argument,
    /// and ties (false both ways) preserve input order. The default-comparator path is an O(n log n) merge sort.
    /// A suspended-comparison merge sort is the future optimization for the custom-comparator path.
    /// </para>
    /// <para>
    /// The <c>$sift</c>/<c>$each</c> context-injected forms (<c>$sift(predicate)</c> / <c>$each(fn)</c>) take the
    /// object from the evaluation focus, honouring the context-injectable object parameter of their signatures;
    /// an explicit object is the first of two arguments (<c>$sift(object, predicate)</c>), and the
    /// <c>object ~&gt; $sift(predicate)</c> chain prepends the object as that first argument.
    /// </para>
    /// </remarks>
    /// <exception cref="JsonataErrorException">A <c>$reduce</c> reducer accepts fewer than two arguments (D3050) or a <c>$single</c> matched more than once (D3138) or never (D3139).</exception>
    private static void StepHigherOrderFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        switch(frame.HigherOrderKind)
        {
            case HigherOrderKind.Sift:
            case HigherOrderKind.Each:
            {
                StepObjectHigherOrderFrame(frame, work, results);

                return;
            }
            case HigherOrderKind.Sort:
            {
                StepSortFrame(frame, work, results);

                return;
            }
            default:
            {
                StepArrayHigherOrderFrame(frame, work, results);

                return;
            }
        }
    }

    /// <summary>
    /// Drives one turn of an array higher-order cursor (<c>$map</c>/<c>$filter</c>/<c>$single</c>/
    /// <c>$reduce</c>): the seed turn (<see cref="EvalFrame.NextIndex"/> is <c>-1</c>) positions the cursor and
    /// — for <c>$reduce</c> — seeds the accumulator and validates the reducer arity; each later turn folds the
    /// previous application's result on top of the results stack per kind; while elements remain it schedules
    /// the next application through the shared <see cref="ApplyProcedure"/> path; when the elements are
    /// exhausted it pops the cursor and pushes the kind's final value. The applied function evaluates against
    /// its OWN captured focus and frame — the element is an argument, not a rebound focus.
    /// </summary>
    /// <param name="frame">The array higher-order cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    /// <exception cref="JsonataErrorException">A <c>$reduce</c> reducer accepts fewer than two arguments (D3050) or a <c>$single</c> matched more than once (D3138) or never (D3139).</exception>
    private static void StepArrayHigherOrderFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.NextIndex == -1)
        {
            SeedHigherOrder(frame);
        }
        else
        {
            FoldHigherOrderResult(frame, results.Pop());
        }

        if(frame.NextIndex < frame.Sequence!.Count)
        {
            ScheduleHigherOrderApplication(frame, work, results);

            return;
        }

        work.Pop();
        results.Push(FinishHigherOrder(frame));
    }

    /// <summary>
    /// Drives one turn of an object higher-order cursor (<c>$sift</c>/<c>$each</c>): the seed turn
    /// (<see cref="EvalFrame.NextIndex"/> is <c>-1</c>) positions the cursor at the first entry; each later turn
    /// folds the previous application's result per kind; while entries remain it schedules the next application
    /// through the shared <see cref="ApplyProcedure"/> path, passing <c>(value, String(key), object)</c>; when
    /// the entries are exhausted it pops the cursor and pushes the kind's final value. The applied function
    /// evaluates against its OWN captured focus and frame — the entry value is an argument, not a rebound focus.
    /// </summary>
    /// <param name="frame">The object higher-order cursor frame.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="results">The results stack.</param>
    private static void StepObjectHigherOrderFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.NextIndex == -1)
        {
            frame.NextIndex = 0;
        }
        else
        {
            FoldObjectHigherOrderResult(frame, results.Pop());
        }

        if(frame.NextIndex < frame.HigherOrderEntries!.Count)
        {
            ScheduleObjectHigherOrderApplication(frame, work, results);

            return;
        }

        work.Pop();
        results.Push(FinishObjectHigherOrder(frame));
    }

    /// <summary>
    /// Folds the previous per-entry application's result into an object higher-order cursor's state per kind:
    /// <c>$each</c> collects a non-undefined result; <c>$sift</c> keeps the entry's original (key, value) pair
    /// when the predicate result is truthy.
    /// </summary>
    /// <param name="frame">The object higher-order cursor frame.</param>
    /// <param name="result">The previous application's result on top of the results stack.</param>
    private static void FoldObjectHigherOrderResult(EvalFrame frame, JsonataValue result)
    {
        //The entry just tested is the one at the position the cursor advanced past.
        KeyValuePair<string, JsonataValue> entry = frame.HigherOrderEntries![frame.NextIndex - 1];
        if(frame.HigherOrderKind == HigherOrderKind.Each)
        {
            if(!result.IsUndefined)
            {
                frame.Accumulator.Add(result);
            }

            return;
        }

        //$sift keeps the entry's ORIGINAL (key, value) pair, not the predicate result, on a truthy result.
        if(JsonataTruthiness.IsTruthy(result))
        {
            (frame.Entries ??= []).Add(entry);
        }
    }

    /// <summary>
    /// Schedules the next per-entry application of an object higher-order cursor through the shared apply path,
    /// passing <c>(value, String(key), object)</c> (always all three), and advances the cursor. The application
    /// runs under the application-site context — the supplied function carries its own captured focus and frame,
    /// so the entry value is an argument, not a rebound focus.
    /// </summary>
    /// <param name="frame">The object higher-order cursor frame.</param>
    /// <param name="work">The work stack the application's body is scheduled onto.</param>
    /// <param name="results">The results stack a synchronous built-in application pushes its value onto.</param>
    private static void ScheduleObjectHigherOrderApplication(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        KeyValuePair<string, JsonataValue> entry = frame.HigherOrderEntries![frame.NextIndex];
        JsonataValue[] hofArguments = [entry.Value, JsonataValue.String(entry.Key), frame.HigherOrderObject];
        frame.NextIndex++;
        ApplyHigherOrderFunction(frame.HigherOrderFunction, hofArguments, frame.Context, work, results);
    }

    /// <summary>
    /// Produces a finished object higher-order cursor's value per kind: <c>$each</c> normalizes its collected
    /// accumulator; <c>$sift</c> returns an object of the kept (key, value) pairs in insertion order, or
    /// undefined when no entry was kept (an empty sift is the "nothing" value, not an empty object).
    /// </summary>
    /// <param name="frame">The exhausted object higher-order cursor frame.</param>
    /// <returns>The cursor's final value.</returns>
    private static JsonataValue FinishObjectHigherOrder(EvalFrame frame)
    {
        if(frame.HigherOrderKind == HigherOrderKind.Each)
        {
            return new JsonataSequence(frame.Accumulator, KeepArray: false).Normalize();
        }

        //$sift: no kept entry yields the "nothing" value rather than an empty object.
        if(frame.Entries is null || frame.Entries.Count == 0)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Object(frame.Entries);
    }

    /// <summary>
    /// Drives one turn of a custom-comparator <c>$sort</c> cursor: a stable insertion sort over the resident
    /// working array, with each comparison suspended as one scheduled comparator application. The seed turn
    /// (<see cref="EvalFrame.NextIndex"/> is <c>0</c>) starts the first insertion; each later turn collects the
    /// previous comparison's boolean result and advances the insertion. When the working array is fully sorted
    /// it pops the cursor and pushes the ordered array.
    /// </summary>
    /// <param name="frame">The sort cursor frame.</param>
    /// <param name="work">The work stack the comparator application's body is scheduled onto.</param>
    /// <param name="results">The results stack the previous comparison's result sits on, and the ordered array is pushed onto.</param>
    private static void StepSortFrame(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        if(frame.NextIndex == 0)
        {
            //Seed: the first element is a trivially sorted prefix; the insertion of element 1 begins.
            frame.NextIndex = 1;
            frame.SortOuterIndex = 1;
            if(!BeginSortInsertion(frame, work, results))
            {
                work.Pop();
                results.Push(JsonataValue.Array(frame.SortWorking!));
            }

            return;
        }

        //A comparison landed: a truthy result means working[j] must move right of the held element.
        bool greater = JsonataTruthiness.IsTruthy(results.Pop());
        List<JsonataValue> working = frame.SortWorking!;
        if(greater)
        {
            working[frame.SortInnerIndex + 1] = working[frame.SortInnerIndex];
            frame.SortInnerIndex--;
            if(frame.SortInnerIndex >= 0)
            {
                ScheduleSortComparison(frame, work, results);

                return;
            }

            //The held element belongs at the front of the sorted prefix.
            working[0] = frame.SortHeld;
        }
        else
        {
            //The held element belongs just after working[j]; stability keeps it after an equal element.
            working[frame.SortInnerIndex + 1] = frame.SortHeld;
        }

        if(AdvanceSortInsertion(frame, work, results))
        {
            return;
        }

        work.Pop();
        results.Push(JsonataValue.Array(working));
    }

    /// <summary>
    /// Begins the insertion of the element at <see cref="EvalFrame.SortOuterIndex"/> into the sorted prefix:
    /// holds that element, positions the inner scan at the last sorted-prefix index, and schedules the first
    /// comparison; returns <see langword="false"/> when there is nothing left to insert (the outer index has
    /// reached the array length).
    /// </summary>
    /// <param name="frame">The sort cursor frame.</param>
    /// <param name="work">The work stack the comparison application is scheduled onto.</param>
    /// <param name="results">The results stack a synchronous comparator application pushes its value onto.</param>
    /// <returns><see langword="true"/> when an insertion was begun; <see langword="false"/> when the sort is complete.</returns>
    private static bool BeginSortInsertion(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        List<JsonataValue> working = frame.SortWorking!;
        if(frame.SortOuterIndex >= working.Count)
        {
            return false;
        }

        frame.SortHeld = working[frame.SortOuterIndex];
        frame.SortInnerIndex = frame.SortOuterIndex - 1;
        ScheduleSortComparison(frame, work, results);

        return true;
    }

    /// <summary>
    /// Advances the sort to the next element's insertion: increments the outer index and begins the next
    /// insertion; returns <see langword="false"/> when no element remains to insert (the sort is complete).
    /// </summary>
    /// <param name="frame">The sort cursor frame.</param>
    /// <param name="work">The work stack the comparison application is scheduled onto.</param>
    /// <param name="results">The results stack a synchronous comparator application pushes its value onto.</param>
    /// <returns><see langword="true"/> when the next insertion was begun; <see langword="false"/> when the sort is complete.</returns>
    private static bool AdvanceSortInsertion(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        frame.SortOuterIndex++;
        return BeginSortInsertion(frame, work, results);
    }

    /// <summary>
    /// Schedules one suspended comparator comparison <c>comp(working[j], held)</c> through the shared apply
    /// path, passing EXACTLY the two arguments (no index, no array). The comparator carries its own captured
    /// focus and frame; its boolean result lands on the next turn.
    /// </summary>
    /// <param name="frame">The sort cursor frame.</param>
    /// <param name="work">The work stack the comparator application's body is scheduled onto.</param>
    /// <param name="results">The results stack a synchronous comparator application pushes its value onto.</param>
    private static void ScheduleSortComparison(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        JsonataValue[] comparisonArguments = [frame.SortWorking![frame.SortInnerIndex], frame.SortHeld];
        ApplyHigherOrderFunction(frame.HigherOrderFunction, comparisonArguments, frame.Context, work, results);
    }

    /// <summary>
    /// Seeds a higher-order cursor: every kind starts the per-element cursor at the first element, except
    /// <c>$reduce</c>, which seeds its accumulator — from the supplied initial value when present, otherwise
    /// from the first element with the cursor advanced past it — and validates that a lambda reducer accepts
    /// at least two arguments (a non-lambda reducer is not arity-checked here).
    /// </summary>
    /// <param name="frame">The higher-order cursor frame.</param>
    /// <exception cref="JsonataErrorException">A lambda <c>$reduce</c> reducer accepts fewer than two arguments (D3050).</exception>
    private static void SeedHigherOrder(EvalFrame frame)
    {
        if(frame.HigherOrderKind != HigherOrderKind.Reduce)
        {
            frame.NextIndex = 0;

            return;
        }

        if(frame.HigherOrderFunction.AsFunction is JsonataLambda reducer && reducer.Parameters.Count < 2)
        {
            //The reducer of a left fold must accept at least the accumulator and the current value.
            throw new JsonataErrorException(WellKnownJsonataErrors.ReduceArity, null, "The function supplied to '$reduce' must accept at least two arguments.");
        }

        if(frame.HigherOrderArgumentCount < 3 && frame.Sequence!.Count > 0)
        {
            //With no initial value the fold seeds from the first element and starts at the second.
            frame.ReduceAccumulator = frame.Sequence[0];
            frame.NextIndex = 1;

            return;
        }

        //With an explicit initial value (or no elements) the fold seeds from that value and starts at the first.
        frame.NextIndex = 0;
    }

    /// <summary>
    /// Folds the previous application's result into a higher-order cursor's state per kind: <c>$map</c>
    /// collects a non-undefined result; <c>$filter</c> keeps the just-tested ELEMENT when the result is
    /// truthy; <c>$single</c> holds the just-tested element on a truthy result and throws D3138 on a second
    /// match; <c>$reduce</c> carries the result forward as the new accumulator.
    /// </summary>
    /// <param name="frame">The higher-order cursor frame.</param>
    /// <param name="result">The previous application's result on top of the results stack.</param>
    /// <exception cref="JsonataErrorException">A <c>$single</c> matched a second element (D3138).</exception>
    private static void FoldHigherOrderResult(EvalFrame frame, JsonataValue result)
    {
        //The element just tested is the one at the position the cursor advanced past.
        JsonataValue element = frame.Sequence![frame.NextIndex - 1];
        switch(frame.HigherOrderKind)
        {
            case HigherOrderKind.Map:
            {
                if(!result.IsUndefined)
                {
                    frame.Accumulator.Add(result);
                }

                break;
            }
            case HigherOrderKind.Filter:
            {
                if(JsonataTruthiness.IsTruthy(result))
                {
                    frame.Accumulator.Add(element);
                }

                break;
            }
            case HigherOrderKind.Single:
            {
                FoldSingleResult(frame, element, result);

                break;
            }
            default:
            {
                //$reduce: the result becomes the running accumulator for the next step.
                frame.ReduceAccumulator = result;

                break;
            }
        }
    }

    /// <summary>Folds one <c>$single</c> predicate result: a truthy result holds the element as the match, or throws D3138 when a match is already held.</summary>
    /// <param name="frame">The higher-order cursor frame.</param>
    /// <param name="element">The just-tested element.</param>
    /// <param name="result">The predicate result for the element.</param>
    /// <exception cref="JsonataErrorException">A match is already held (D3138).</exception>
    private static void FoldSingleResult(EvalFrame frame, JsonataValue element, JsonataValue result)
    {
        if(!JsonataTruthiness.IsTruthy(result))
        {
            return;
        }

        if(frame.HasSingleMatch)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.SingleMultipleMatches, null, "More than one element of the input array matched the supplied predicate in '$single'.");
        }

        frame.HasSingleMatch = true;
        frame.SingleMatch = element;
    }

    /// <summary>
    /// Schedules the next per-element application of a higher-order cursor through the shared apply path,
    /// passing the full argument list (<c>$reduce</c> prepends the running accumulator), and advances the
    /// cursor. The application runs under the application-site context — the supplied function carries its
    /// own captured focus and frame, so the element is an argument, not a rebound focus. A <c>$single</c>
    /// with no supplied predicate treats every element as a match: it pushes a synthetic truthy result and
    /// advances, so the next fold turn records the element as a match exactly as a truthy predicate would.
    /// </summary>
    /// <param name="frame">The higher-order cursor frame.</param>
    /// <param name="work">The work stack the application's body is scheduled onto.</param>
    /// <param name="results">The results stack a synchronous built-in application pushes its value onto.</param>
    private static void ScheduleHigherOrderApplication(EvalFrame frame, Stack<EvalFrame> work, Stack<JsonataValue> results)
    {
        int index = frame.NextIndex;
        JsonataValue element = frame.Sequence![index];
        if(frame.HigherOrderKind == HigherOrderKind.Single && frame.HigherOrderFunction.IsUndefined)
        {
            //With no predicate every element matches, so a synthetic truthy result is folded as a match on
            //the next turn; this asserts exactly-one-element without applying a function.
            frame.NextIndex++;
            results.Push(JsonataValue.Boolean(true));

            return;
        }

        JsonataValue indexValue = JsonataValue.Number(index);
        JsonataValue arrayValue = JsonataValue.Array(frame.Sequence);
        JsonataValue[] hofArguments = frame.HigherOrderKind == HigherOrderKind.Reduce
            ? [frame.ReduceAccumulator, element, indexValue, arrayValue]
            : [element, indexValue, arrayValue];

        frame.NextIndex++;
        ApplyHigherOrderFunction(frame.HigherOrderFunction, hofArguments, frame.Context, work, results);
    }

    /// <summary>
    /// Produces a finished higher-order cursor's value per kind: <c>$map</c> and <c>$filter</c> normalize
    /// their collected accumulator; <c>$single</c> returns the held match or throws D3139 when none was held;
    /// <c>$reduce</c> returns the running accumulator.
    /// </summary>
    /// <param name="frame">The exhausted higher-order cursor frame.</param>
    /// <returns>The cursor's final value.</returns>
    /// <exception cref="JsonataErrorException">A <c>$single</c> matched no element (D3139).</exception>
    private static JsonataValue FinishHigherOrder(EvalFrame frame)
    {
        switch(frame.HigherOrderKind)
        {
            case HigherOrderKind.Map:
            case HigherOrderKind.Filter:
            {
                return new JsonataSequence(frame.Accumulator, KeepArray: false).Normalize();
            }
            case HigherOrderKind.Single:
            {
                if(!frame.HasSingleMatch)
                {
                    throw new JsonataErrorException(WellKnownJsonataErrors.SingleNoMatch, null, "No elements of the input array matched the supplied predicate in '$single'.");
                }

                return frame.SingleMatch;
            }
            default:
            {
                //$reduce: the running accumulator is the fold's result.
                return frame.ReduceAccumulator;
            }
        }
    }

    /// <summary>Converts a group's items to the focus its value expression evaluates under: a single item is that item, a multi-item group is the sequence of items as an array.</summary>
    /// <param name="items">The grouped items.</param>
    /// <returns>The group's value-phase focus.</returns>
    /// <remarks>
    /// A multi-item group focus is built as a plain array of the grouped items, so an array-valued grouped
    /// item is kept whole rather than append-flattened one level into the group focus. The reference
    /// evaluator assembles the group focus through its <c>append</c> path, which spreads an array-valued
    /// item one level (a group of items <c>[[1, 2], [3, 4]]</c> becomes the sequence <c>[1, 2, 3, 4]</c>
    /// there, but stays the nested <c>[[1, 2], [3, 4]]</c> here). This only manifests when grouped focus
    /// items are themselves arrays, an exotic shape since grouping is usually over objects, and ties into
    /// the deferred keepArray model; the common object-valued-item case is unaffected. This is a
    /// fragment-relative divergence from the reference evaluator.
    /// </remarks>
    private static JsonataValue GroupFocus(List<JsonataValue> items)
    {
        if(items.Count == 1)
        {
            return items[0];
        }

        return JsonataValue.Array(items);
    }

    /// <summary>The single-undefined-item sequence a group-by seeds with when the focus normalizes to no items, so a literal object still builds over nothing.</summary>
    private static IReadOnlyList<JsonataValue> SingleUndefinedItem { get; } = [JsonataValue.Undefined];

    /// <summary>
    /// Selects one positional item for a literal numeric index: the index is floored in double space and
    /// taken from the end when negative; an out-of-range index yields undefined; a selected item that is
    /// itself an array becomes the whole result (inner-array promotion), so an empty inner array collapses
    /// to undefined and a singleton inner array unwraps.
    /// </summary>
    /// <param name="source">The source sequence.</param>
    /// <param name="literal">The literal numeric index node.</param>
    /// <returns>The selected item (promoted when it is an array), or undefined when out of range.</returns>
    private static JsonataValue SelectLiteralIndex(IReadOnlyList<JsonataValue> source, LiteralExpression literal)
    {
        double index = Math.Floor(double.Parse(literal.Value.Span, NumberStyles.Float, CultureInfo.InvariantCulture));
        if(index < 0)
        {
            index += source.Count;
        }

        if(index < 0 || index >= source.Count)
        {
            return JsonataValue.Undefined;
        }

        JsonataValue selected = source[(int)index];
        if(selected.Kind == JsonataValueKind.Array)
        {
            return new JsonataSequence(selected.AsArray, KeepArray: false).Normalize();
        }

        return selected;
    }

    /// <summary>
    /// Decides whether the per-item filter keeps the item at a position: a numeric result is a single
    /// positional index; an array of numbers keeps the item when any of those indices selects the
    /// position; any other result keeps the item when it is truthy. Indices are floored in double space
    /// and taken from the end when negative.
    /// </summary>
    /// <param name="filterResult">The per-item filter result.</param>
    /// <param name="position">The item's position in the source sequence.</param>
    /// <param name="length">The source sequence length.</param>
    /// <returns><see langword="true"/> when the item is kept.</returns>
    private static bool KeepsItem(JsonataValue filterResult, int position, int length)
    {
        if(filterResult.Kind == JsonataValueKind.Number)
        {
            return IndexSelectsPosition(filterResult.AsNumber, position, length);
        }

        if(filterResult.Kind == JsonataValueKind.Array && IsArrayOfNumbers(filterResult.AsArray))
        {
            foreach(JsonataValue element in filterResult.AsArray)
            {
                if(IndexSelectsPosition(element.AsNumber, position, length))
                {
                    return true;
                }
            }

            return false;
        }

        return JsonataTruthiness.IsTruthy(filterResult);
    }

    /// <summary>Decides whether a numeric index (floored in double space, negative from the end) selects a position.</summary>
    /// <param name="rawIndex">The raw numeric index.</param>
    /// <param name="position">The candidate position.</param>
    /// <param name="length">The source sequence length.</param>
    /// <returns><see langword="true"/> when the floored index equals the position.</returns>
    private static bool IndexSelectsPosition(double rawIndex, int position, int length)
    {
        double index = Math.Floor(rawIndex);
        if(index < 0)
        {
            index += length;
        }

        return index == position;
    }

    /// <summary>Determines whether every element of an array is a number (an empty array is not an array of numbers).</summary>
    /// <param name="items">The array items.</param>
    /// <returns><see langword="true"/> when the array is non-empty and every element is a number.</returns>
    private static bool IsArrayOfNumbers(IReadOnlyList<JsonataValue> items)
    {
        if(items.Count == 0)
        {
            return false;
        }

        foreach(JsonataValue item in items)
        {
            if(item.Kind != JsonataValueKind.Number)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Normalizes a value into a flat list of sequence items (an array yields its elements; undefined yields nothing).</summary>
    /// <param name="value">The value to flatten into items.</param>
    /// <returns>The item list.</returns>
    private static IReadOnlyList<JsonataValue> ToSequenceItems(JsonataValue value)
    {
        if(value.IsUndefined)
        {
            return [];
        }

        if(value.Kind == JsonataValueKind.Array)
        {
            return value.AsArray;
        }

        return [value];
    }

    /// <summary>Appends a step result to an accumulator, flattening one level (an array spreads its elements; undefined contributes nothing).</summary>
    /// <param name="accumulator">The accumulator to append to.</param>
    /// <param name="value">The value to flatten in.</param>
    private static void AppendFlattened(List<JsonataValue> accumulator, JsonataValue value)
    {
        if(value.IsUndefined)
        {
            return;
        }

        if(value.Kind == JsonataValueKind.Array)
        {
            accumulator.AddRange(value.AsArray);

            return;
        }

        accumulator.Add(value);
    }

    /// <summary>
    /// Appends a per-item dot/map step result to the map accumulator, respecting the JSONata <c>cons</c>
    /// marker: an undefined result contributes nothing; a cons array (an array-constructor used as the step)
    /// is pushed WHOLE as one element so nested constructor steps compose; a normal navigated array flattens
    /// one level (its elements spread); a scalar is added as one element.
    /// </summary>
    /// <param name="accumulator">The accumulator to append to.</param>
    /// <param name="value">The per-item step result to flatten in.</param>
    private static void AppendStepResult(List<JsonataValue> accumulator, JsonataValue value)
    {
        if(value.IsUndefined)
        {
            return;
        }

        if(value.Kind != JsonataValueKind.Array || value.IsConsArray)
        {
            //A scalar — and a cons array (an array-constructor step) — is one element kept whole.
            accumulator.Add(value);

            return;
        }

        accumulator.AddRange(value.AsArray);
    }

    /// <summary>
    /// Normalizes a dot/map or predicate step's accumulator to its result value, propagating the JSONata
    /// <c>keepSingleton</c> marker: when the step is not keep-array-marked it normalizes as usual (empty →
    /// undefined, singleton → its bare value, otherwise an array); when it is keep-array-marked a non-empty
    /// result stays a (keep-singleton) array so a singleton does not auto-unwrap and the marker survives to
    /// the enclosing steps, while an empty result is still undefined (the marker keeps a singleton an array,
    /// it does not synthesize one from nothing).
    /// </summary>
    /// <param name="accumulator">The step's accumulated result items.</param>
    /// <param name="keepArray">Whether the step propagates the keep-array marker.</param>
    /// <returns>The normalized step result.</returns>
    private static JsonataValue NormalizeStepResult(List<JsonataValue> accumulator, bool keepArray)
    {
        if(keepArray && accumulator.Count > 0)
        {
            //A keep-array-marked step keeps its non-empty result a (keep-singleton) array, so a singleton
            //stays an array and the marker rides on the value to the enclosing steps.
            return JsonataValue.KeepSingletonArray(accumulator);
        }

        return new JsonataSequence(accumulator, KeepArray: false).Normalize();
    }

    /// <summary>
    /// Folds a dot/map step's per-item results into the step value, matching the reference <c>evaluateStep</c>
    /// flatten rule: a single navigated (non-cons) array result is kept WHOLE — so a field or sub-path that
    /// yields one array keeps it an array rather than unwrapping a singleton or flattening it away — while any
    /// other shape flattens each per-item result one level, except a cons array (an array-constructor step),
    /// which is kept whole so nested constructor steps compose. The keep-array marker is propagated so a
    /// <c>[]</c>-marked step keeps a singleton an array.
    /// </summary>
    /// <param name="perItemResults">The defined per-item step results, in order, each kept whole.</param>
    /// <param name="keepArray">Whether the step propagates the keep-array marker.</param>
    /// <returns>The folded step result.</returns>
    private static JsonataValue FlattenMapResults(List<JsonataValue> perItemResults, bool keepArray)
    {
        if(perItemResults.Count == 1 && perItemResults[0].Kind == JsonataValueKind.Array && !perItemResults[0].IsConsArray)
        {
            //A single navigated array is the step's value as-is; the keep-array marker re-tags it so it does
            //not later auto-unwrap.
            return keepArray ? KeepAsArray(perItemResults[0]) : perItemResults[0];
        }

        List<JsonataValue> flattened = [];
        foreach(JsonataValue result in perItemResults)
        {
            //A cons array (an array-constructor step) is one element kept whole; a normal navigated array
            //spreads one level; a scalar is one element.
            if(result.Kind != JsonataValueKind.Array || result.IsConsArray)
            {
                flattened.Add(result);

                continue;
            }

            flattened.AddRange(result.AsArray);
        }

        return NormalizeStepResult(flattened, keepArray);
    }

    /// <summary>
    /// Wraps a value as a keep-singleton array (the JSONata <c>keepSingleton</c> marker): undefined stays
    /// undefined (the marker does not synthesize an array from nothing); an existing array is re-tagged
    /// keep-singleton, preserving its elements and its <c>cons</c> marker (so a cons array constructor step
    /// carrying a trailing <c>[]</c> keeps both markers); a scalar becomes a one-element keep-singleton array.
    /// </summary>
    /// <param name="value">The value to keep as an array.</param>
    /// <returns>The keep-singleton array value, or undefined when the value is undefined.</returns>
    private static JsonataValue KeepAsArray(JsonataValue value)
    {
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        return value.Kind == JsonataValueKind.Array
            ? JsonataValue.AsKeepSingletonArray(value)
            : JsonataValue.KeepSingletonArray([value]);
    }

    /// <summary>Applies a binary operator to two evaluated operands.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="op">The operator.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator's result.</returns>
    private static JsonataValue ApplyBinary(JsonataValue left, BinaryOperator op, JsonataValue right)
    {
        return op switch
        {
            BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => Arithmetic(left, op, right),
            BinaryOperator.Concat => Concat(left, right),
            BinaryOperator.Equal => JsonataValue.Boolean(EqualityValue(left, right)),
            BinaryOperator.NotEqual => JsonataValue.Boolean(!EqualityValue(left, right)),
            BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual => Compare(left, op, right),
            BinaryOperator.In => JsonataValue.Boolean(Includes(left, right)),
            BinaryOperator.And => JsonataValue.Boolean(JsonataTruthiness.IsTruthy(left) && JsonataTruthiness.IsTruthy(right)),
            BinaryOperator.Or => JsonataValue.Boolean(JsonataTruthiness.IsTruthy(left) || JsonataTruthiness.IsTruthy(right)),
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unrecognised JSONata binary operator: {op}."))
        };
    }

    /// <summary>
    /// Applies an arithmetic operator: an undefined operand yields undefined; a defined non-numeric left
    /// operand throws T2001 and a defined non-numeric right operand throws T2002; a defined non-finite numeric
    /// operand throws D1001 (the reference's <c>isNumeric</c> guard); the result of the operation itself is not
    /// range-checked, so an operation that overflows or divides by zero yields the IEEE-754 infinity unchanged
    /// (a later operation, the <c>$string</c> cast, or a serializer surfaces it); modulo is the truncated
    /// remainder.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="op">The arithmetic operator.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The numeric result, or undefined.</returns>
    /// <exception cref="JsonataErrorException">A defined operand is not numeric (codes T2001/T2002) or is a non-finite number (code D1001).</exception>
    private static JsonataValue Arithmetic(JsonataValue left, BinaryOperator op, JsonataValue right)
    {
        ValidateArithmeticOperand(left, WellKnownJsonataErrors.ArithmeticLeftNotNumeric, "The left side of an arithmetic operator must evaluate to a number.");
        ValidateArithmeticOperand(right, WellKnownJsonataErrors.ArithmeticRightNotNumeric, "The right side of an arithmetic operator must evaluate to a number.");

        if(left.IsUndefined || right.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        double a = left.AsNumber;
        double b = right.AsNumber;
        double result = op switch
        {
            BinaryOperator.Add => a + b,
            BinaryOperator.Subtract => a - b,
            BinaryOperator.Multiply => a * b,
            BinaryOperator.Divide => a / b,
            _ => a % b
        };

        return JsonataValue.Number(result);
    }

    /// <summary>
    /// Validates an arithmetic operand the way the reference's <c>isNumeric</c> does: a defined non-number
    /// operand is the supplied not-numeric error (T2001 for the left, T2002 for the right), and a defined
    /// number operand that is not finite is D1001 — so an infinity that flowed in from an earlier overflow or
    /// a divide-by-zero is rejected when it is next used as an operand, matching the reference.
    /// </summary>
    /// <param name="operand">The operand to validate.</param>
    /// <param name="notNumericCode">The not-numeric error code to raise for a defined non-number operand.</param>
    /// <param name="notNumericMessage">The message for the not-numeric error.</param>
    /// <exception cref="JsonataErrorException">The operand is a defined non-number (the supplied code) or a defined non-finite number (code D1001).</exception>
    private static void ValidateArithmeticOperand(JsonataValue operand, Utf8String notNumericCode, string notNumericMessage)
    {
        if(operand.IsUndefined)
        {
            return;
        }

        if(operand.Kind != JsonataValueKind.Number)
        {
            throw new JsonataErrorException(notNumericCode, null, notNumericMessage);
        }

        if(!double.IsFinite(operand.AsNumber))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NumberOutOfRange, null, "An arithmetic operand was a number that is out of range.");
        }
    }

    /// <summary>
    /// Applies the concat operator: each operand is coerced to a string with undefined treated as the
    /// empty string, then the two are joined.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The concatenated string.</returns>
    private static JsonataValue Concat(JsonataValue left, JsonataValue right)
    {
        return JsonataValue.String(StringCoerce(left) + StringCoerce(right));
    }

    /// <summary>
    /// Coerces a value to a string for concatenation: undefined to the empty string, null to the text
    /// <c>null</c>, a boolean to <c>true</c>/<c>false</c>, a number to its shortest round-trip form
    /// (integers without a decimal point), a string to itself; arrays and objects serialize as JSON.
    /// </summary>
    /// <param name="value">The value to coerce.</param>
    /// <returns>The coerced string.</returns>
    /// <exception cref="JsonataErrorException">A non-finite number was coerced (code D3001).</exception>
    private static string StringCoerce(JsonataValue value)
    {
        return value.Kind switch
        {
            JsonataValueKind.Undefined => string.Empty,
            JsonataValueKind.Null => "null",
            JsonataValueKind.Boolean => value.AsBoolean ? "true" : "false",
            JsonataValueKind.Number => NumberToString(value.AsNumber),
            JsonataValueKind.String => value.AsString,
            _ => JsonataJsonWriter.Serialize(value).ToString()
        };
    }

    /// <summary>Formats a number for string concatenation coercion, matching the reference's <c>fn.string</c>: the ECMAScript <c>Number::toString</c> form after a <c>toPrecision(15)</c> reduction; a non-finite value is rejected with D3001.</summary>
    /// <param name="number">The number to format.</param>
    /// <returns>The formatted decimal form.</returns>
    /// <exception cref="JsonataErrorException">The number is not finite (code D3001).</exception>
    private static string NumberToString(double number)
    {
        if(!double.IsFinite(number))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NonFiniteString, null, "A non-finite number cannot be coerced to a string.");
        }

        Span<byte> scratch = stackalloc byte[EcmaScriptNumberFormatter.MaxFormattedLength];
        int written = EcmaScriptNumberFormatter.Format(number, applyToPrecision15: true, scratch);

        return Encoding.UTF8.GetString(scratch[..written]);
    }

    /// <summary>
    /// Applies a comparison operator: an undefined operand yields undefined; a non-string/number operand
    /// throws T2010; differing comparable types (string versus number) throw T2009; otherwise the native
    /// order applies (lexicographic for strings, numeric for numbers).
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The boolean result, or undefined.</returns>
    /// <exception cref="JsonataErrorException">An operand is not comparable (T2010) or the types differ (T2009).</exception>
    private static JsonataValue Compare(JsonataValue left, BinaryOperator op, JsonataValue right)
    {
        if(!IsComparable(left) || !IsComparable(right))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ComparisonNotComparable, null, "The operands of a comparison must be strings or numbers.");
        }

        if(left.IsUndefined || right.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        if(left.Kind != right.Kind)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ComparisonTypeMismatch, null, "The operands of a comparison must be the same type.");
        }

        int order = left.Kind == JsonataValueKind.Number
            ? left.AsNumber.CompareTo(right.AsNumber)
            : string.CompareOrdinal(left.AsString, right.AsString);

        return JsonataValue.Boolean(op switch
        {
            BinaryOperator.Less => order < 0,
            BinaryOperator.LessOrEqual => order <= 0,
            BinaryOperator.Greater => order > 0,
            _ => order >= 0
        });
    }

    /// <summary>Determines whether a value may participate in an ordered comparison (string, number, or undefined).</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is a string, a number, or undefined.</returns>
    private static bool IsComparable(JsonataValue value)
    {
        return value.Kind is JsonataValueKind.String or JsonataValueKind.Number or JsonataValueKind.Undefined;
    }

    /// <summary>Computes equality: either operand undefined yields false; otherwise deep/structural equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the operands are deep-equal and neither is undefined.</returns>
    private static bool EqualityValue(JsonataValue left, JsonataValue right)
    {
        if(left.IsUndefined || right.IsUndefined)
        {
            return false;
        }

        return JsonataValue.DeepEquals(left, right);
    }

    /// <summary>
    /// Computes membership: either operand undefined yields false; the right side is treated as a
    /// singleton array when it is not an array; the result is whether some element deep-equals the left.
    /// </summary>
    /// <param name="left">The candidate member.</param>
    /// <param name="right">The collection (or a single value treated as a singleton).</param>
    /// <returns><see langword="true"/> when the left is a member of the right.</returns>
    private static bool Includes(JsonataValue left, JsonataValue right)
    {
        if(left.IsUndefined || right.IsUndefined)
        {
            return false;
        }

        if(right.Kind == JsonataValueKind.Array)
        {
            foreach(JsonataValue element in right.AsArray)
            {
                if(JsonataValue.DeepEquals(left, element))
                {
                    return true;
                }
            }

            return false;
        }

        return JsonataValue.DeepEquals(left, right);
    }

    /// <summary>Applies a unary operator: numeric negation, undefined-passthrough, else D1002.</summary>
    /// <param name="op">The unary operator.</param>
    /// <param name="operand">The operand.</param>
    /// <returns>The negated number, or undefined.</returns>
    /// <exception cref="JsonataErrorException">The operand is defined but not numeric (code D1002).</exception>
    private static JsonataValue ApplyUnary(UnaryOperator op, JsonataValue operand)
    {
        //Negate is the only unary operator in the grammar.
        if(op != UnaryOperator.Negate)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unrecognised JSONata unary operator: {op}."));
        }

        if(operand.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        if(operand.Kind != JsonataValueKind.Number)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NegateNonNumeric, null, "The operand of a unary minus must evaluate to a number.");
        }

        return JsonataValue.Number(-operand.AsNumber);
    }

    /// <summary>
    /// The reference type held in <see cref="PlaceholderMarker"/>'s function slot: a private, identity-only
    /// marker for a partial-application placeholder <c>?</c>. It is never a callable function — a placeholder
    /// is detected by reference identity against <see cref="PlaceholderMarker"/> and converted to an unbound
    /// slot before any apply, so the apply path never sees this type.
    /// </summary>
    private sealed class PlaceholderSlot;

    /// <summary>A cursor over an array's elements during the iterative field-lookup walk, carrying the array-descent depth.</summary>
    private sealed class ArrayLookupCursor
    {
        /// <summary>Initializes a cursor over an array's elements at a given descent depth.</summary>
        /// <param name="items">The array elements to walk.</param>
        /// <param name="depth">The array-descent depth of this array.</param>
        public ArrayLookupCursor(IReadOnlyList<JsonataValue> items, int depth)
        {
            Items = items;
            Depth = depth;
        }

        /// <summary>Gets the array elements being walked.</summary>
        public IReadOnlyList<JsonataValue> Items { get; }

        /// <summary>Gets the array-descent depth of this array.</summary>
        public int Depth { get; }

        /// <summary>Gets or sets the next element index to process.</summary>
        public int NextIndex { get; set; }
    }

    /// <summary>A pending value during the iterative descendant pre-order walk, carrying the traversal depth.</summary>
    private sealed class DescendantCursor
    {
        /// <summary>Initializes a cursor over a value at a given traversal depth.</summary>
        /// <param name="value">The value to visit.</param>
        /// <param name="depth">The traversal depth of this value.</param>
        public DescendantCursor(JsonataValue value, int depth)
        {
            Value = value;
            Depth = depth;
        }

        /// <summary>Gets the value being visited.</summary>
        public JsonataValue Value { get; }

        /// <summary>Gets the traversal depth of this value.</summary>
        public int Depth { get; }
    }

    /// <summary>A cursor over an array's elements during the iterative wildcard deep-flatten, carrying the array-nesting depth.</summary>
    private sealed class DeepFlattenCursor
    {
        /// <summary>Initializes a cursor over an array's elements at a given nesting depth.</summary>
        /// <param name="items">The array elements to flatten.</param>
        /// <param name="depth">The array-nesting depth of this array.</param>
        public DeepFlattenCursor(IReadOnlyList<JsonataValue> items, int depth)
        {
            Items = items;
            Depth = depth;
        }

        /// <summary>Gets the array elements being flattened.</summary>
        public IReadOnlyList<JsonataValue> Items { get; }

        /// <summary>Gets the array-nesting depth of this array.</summary>
        public int Depth { get; }

        /// <summary>Gets or sets the next element index to process.</summary>
        public int NextIndex { get; set; }
    }
}
