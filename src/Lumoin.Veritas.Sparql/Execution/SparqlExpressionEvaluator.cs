using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Evaluates a SPARQL <see cref="ExpressionNode"/> over a <see cref="SparqlSolution"/> to an RDF term, applying
/// the value semantics of SPARQL 1.2 §17 (operator mapping, effective boolean value, error propagation). Used by
/// the executor for <c>FILTER</c>, <c>BIND</c> (Extend), and the <c>OPTIONAL</c> (LeftJoin) condition.
/// </summary>
/// <remarks>
/// <para>
/// <b>No recursion.</b> Expressions are evaluated bottom-up over an explicit work stack (mirroring the project's
/// iterative discipline), so a deep expression cannot overflow the stack. Because expressions are pure, the
/// branch-selecting forms (<see cref="IfExpression"/>, <see cref="CoalesceExpression"/>) are evaluated by
/// computing every sub-expression and then selecting — an untaken branch's error is simply discarded, which is
/// observationally identical to the spec's lazy selection.
/// </para>
/// <para>
/// <b>Scope.</b> This slice covers constants, variables, <c>BOUND</c>, the logical connectives (with §17.2
/// three-valued truth tables), the relational comparisons (numeric / boolean / string / term equality),
/// arithmetic, <c>IF</c>, <c>COALESCE</c>, <c>IN</c>/<c>NOT IN</c>, and a core of the built-in function library
/// (the term-type tests, <c>sameTerm</c>, <c>STR</c>/<c>LANG</c>/<c>DATATYPE</c>, the common string functions, and
/// the unary numeric functions). The remaining built-ins, the named functions
/// (<see cref="FunctionCallExpression"/>), <c>EXISTS</c>/<c>NOT EXISTS</c> (which evaluate a graph pattern), and
/// the triple-term expression raise <see cref="NotSupportedException"/> until their later slice. Numeric values are compared and combined as <see cref="double"/>; integer-only
/// arithmetic yields an <c>xsd:integer</c>, otherwise an <c>xsd:double</c> (full decimal/precision typing is a
/// later refinement).
/// </para>
/// </remarks>
public static class SparqlExpressionEvaluator
{
    private const string XsdNamespace = "http://www.w3.org/2001/XMLSchema#";

    /// <summary>The integral numeric datatype IRIs (a literal of one of these is treated as an integer for arithmetic result typing).</summary>
    private static HashSet<string> IntegralDatatypes { get; } = new()
    {
        XsdNamespace + "integer",
        XsdNamespace + "int",
        XsdNamespace + "long",
        XsdNamespace + "short",
        XsdNamespace + "byte",
        XsdNamespace + "nonNegativeInteger",
        XsdNamespace + "nonPositiveInteger",
        XsdNamespace + "negativeInteger",
        XsdNamespace + "positiveInteger",
        XsdNamespace + "unsignedInt",
        XsdNamespace + "unsignedLong",
        XsdNamespace + "unsignedShort",
        XsdNamespace + "unsignedByte"
    };

    /// <summary>The numeric datatype IRIs (integral plus decimal/float/double).</summary>
    private static HashSet<string> NumericDatatypes { get; } = new(IntegralDatatypes)
    {
        XsdNamespace + "decimal",
        XsdNamespace + "float",
        XsdNamespace + "double"
    };

    /// <summary>The boolean literal <c>"true"^^xsd:boolean</c>.</summary>
    private static Literal True { get; } = new(Utf8Strings.From("true"), new NamedNode(Vocabulary.Xsd.Boolean));

    /// <summary>The boolean literal <c>"false"^^xsd:boolean</c>.</summary>
    private static Literal False { get; } = new(Utf8Strings.From("false"), new NamedNode(Vocabulary.Xsd.Boolean));

    /// <summary>The empty UTF-8 lexical form (the <c>LANG</c> of a non-language literal, the failure value of a string lookup).</summary>
    private static Utf8String EmptyText { get; } = Utf8Strings.From(string.Empty);

    /// <summary>The single-space UTF-8 string — the default <c>GROUP_CONCAT</c> separator.</summary>
    private static Utf8String SpaceText { get; } = Utf8Strings.From(" ");

    /// <summary>The shared empty operand map a zero-argument extension-function call evaluates through on the leaf path; never mutated.</summary>
    private static Dictionary<ExpressionNode, ExpressionValue> EmptyOperandValues { get; } = [];

    /// <summary>
    /// Returns whether an expression's effective boolean value is <see langword="true"/> over a solution. An
    /// error or a value with no effective boolean value yields <see langword="false"/> — the behaviour
    /// <c>FILTER</c> and the <c>OPTIONAL</c> join condition require.
    /// </summary>
    /// <param name="expression">The expression to test.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the non-pure functions consume.</param>
    /// <returns><see langword="true"/> when the effective boolean value is true; otherwise <see langword="false"/>.</returns>
    public static bool Satisfies(ExpressionNode expression, SparqlSolution solution, SparqlExpressionContext context)
    {
        ExpressionValue value = EffectiveBooleanValue(Evaluate(expression, solution, context));

        return !value.IsError && ReferenceEquals(value.Term, True);
    }

    /// <summary>
    /// Evaluates an expression over a solution to its RDF-term value, yielding <see langword="false"/> when the
    /// expression raises an error or evaluates to an unbound result (the behaviour <c>BIND</c>/Extend requires —
    /// an errored binding is simply not added).
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the non-pure functions consume.</param>
    /// <param name="value">Receives the evaluated term on success.</param>
    /// <returns><see langword="true"/> when the expression evaluates to a bound term; otherwise <see langword="false"/>.</returns>
    public static bool TryEvaluate(ExpressionNode expression, SparqlSolution solution, SparqlExpressionContext context, out RdfTerm value)
    {
        ExpressionValue result = Evaluate(expression, solution, context);
        if(result.IsError)
        {
            value = null!;

            return false;
        }

        value = result.Term;

        return true;
    }

    /// <summary>
    /// Compares two RDF terms for <c>ORDER BY</c> (§15.1): the ascending order is unbound (a <see langword="null"/>
    /// term — an unbound variable or an errored key) &lt; blank node &lt; IRI &lt; literal; within a category, blank
    /// nodes and IRIs order by their label/IRI text, and literals order on the total class-rank axis of
    /// <see cref="RdfValueComparer.CompareForSort"/> (value order within a class, deterministic tiebreaks, no
    /// intransitive mixed-datatype comparison).
    /// </summary>
    /// <param name="left">The left term, or <see langword="null"/> for an unbound/errored key.</param>
    /// <param name="right">The right term, or <see langword="null"/> for an unbound/errored key.</param>
    /// <param name="implicitTimezone">The implicit timezone temporal comparisons normalize naive operands with (§17.3), captured once per evaluation.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    internal static int CompareForOrdering(RdfTerm? left, RdfTerm? right, TimeSpan implicitTimezone)
    {
        //Triple terms order component-wise (subject, then predicate, then object), each component by this same
        //ordering and itself possibly a triple term. An explicit stack walks the pending component comparisons in
        //order — no call-stack recursion: a triple-term pair expands to its three component pairs, pushed so the
        //subject is taken first, and the first component that differs decides the whole comparison. Non-triple
        //terms compare by category rank, then within a rank by label / IRI / literal value.
        Stack<(RdfTerm? Left, RdfTerm? Right, int Depth)> pending = new();
        pending.Push((left, right, 1));

        while(pending.Count > 0)
        {
            (RdfTerm? leftTerm, RdfTerm? rightTerm, int depth) = pending.Pop();
            int leftRank = OrderRank(leftTerm);
            int rightRank = OrderRank(rightTerm);
            if(leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            int comparison = leftRank switch
            {
                1 => ((BlankNode)leftTerm!).Label.CompareTo(((BlankNode)rightTerm!).Label),
                2 => ((NamedNode)leftTerm!).Iri.CompareTo(((NamedNode)rightTerm!).Iri),
                3 => CompareLiterals((Literal)leftTerm!, (Literal)rightTerm!, implicitTimezone),
                4 => ExpandTripleTermComparison(pending, (Lumoin.Veritas.Core.TripleTerm)leftTerm!, (Lumoin.Veritas.Core.TripleTerm)rightTerm!, depth),

                //Both unbound (0): equal at this position.
                _ => 0
            };
            if(comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    /// <summary>Pushes a triple-term pair's object, predicate, and subject component comparisons (subject on top, so it is taken first) onto the pending stack and yields 0, deferring the decision to those components. Nesting beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/> raises <see cref="TripleTermDepthLimitException"/>.</summary>
    /// <param name="pending">The pending-comparison stack to push the three component pairs onto.</param>
    /// <param name="left">The left triple term.</param>
    /// <param name="right">The right triple term.</param>
    /// <param name="depth">The quoted-triple nesting depth of this pair.</param>
    /// <returns>Always 0 — the comparison is decided by the pushed component pairs.</returns>
    private static int ExpandTripleTermComparison(Stack<(RdfTerm? Left, RdfTerm? Right, int Depth)> pending, Lumoin.Veritas.Core.TripleTerm left, Lumoin.Veritas.Core.TripleTerm right, int depth)
    {
        if(depth > QuotedTripleLimits.MaxNestingDepth)
        {
            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
        }

        pending.Push((left.Object, right.Object, depth + 1));
        pending.Push((left.Predicate, right.Predicate, depth + 1));
        pending.Push((left.Subject, right.Subject, depth + 1));

        return 0;
    }

    /// <summary>Returns the <c>ORDER BY</c> category rank of a term: unbound 0, blank node 1, IRI 2, literal 3, triple term 4.</summary>
    /// <param name="term">The term, or <see langword="null"/> for unbound.</param>
    /// <returns>The category rank.</returns>
    private static int OrderRank(RdfTerm? term)
    {
        return term switch
        {
            null => 0,
            BlankNode => 1,
            NamedNode => 2,
            Literal => 3,
            _ => 4
        };
    }

    /// <summary>Orders two literals on the total <c>ORDER BY</c> axis — the class-rank partition and within-class value orders of <see cref="RdfValueComparer.CompareForSort"/>, the one comparator every ordering consumer shares.</summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <param name="implicitTimezone">The implicit timezone temporal comparisons normalize naive operands with.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    private static int CompareLiterals(Literal left, Literal right, TimeSpan implicitTimezone)
    {
        return RdfValueComparer.CompareForSort(left, right, implicitTimezone);
    }

    /// <summary>
    /// Computes one aggregate over a group's solutions (§18.5 set functions): the argument expression is evaluated
    /// per solution and the bound values gathered (deduplicated when <c>DISTINCT</c>), then reduced by the
    /// function. <c>COUNT(*)</c> counts solutions; <c>MIN</c>/<c>MAX</c>/<c>SAMPLE</c> over an empty value set, and
    /// <c>SUM</c>/<c>AVG</c> over a non-numeric value, yield <see langword="null"/> (an unbound result, left out of
    /// the group's solution).
    /// </summary>
    /// <param name="aggregate">The aggregate expression.</param>
    /// <param name="group">The group's solutions.</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the argument expression's non-pure functions consume.</param>
    /// <returns>The aggregate's term value, or <see langword="null"/> when it has none.</returns>
    /// <summary>The <c>xsd:integer</c> literal for a count — the same construction <c>COUNT</c> produces, exposed for the executor's count-only fast path.</summary>
    /// <param name="value">The count.</param>
    /// <returns>The integer literal term.</returns>
    internal static RdfTerm IntegerTerm(long value)
    {
        return Integer(value).Term!;
    }

    /// <summary>Computes one aggregate's value over a group's member solutions, dispatching on the aggregate form; <see langword="null"/> is the expression error value, which binds nothing.</summary>
    /// <param name="aggregate">The aggregate expression.</param>
    /// <param name="group">The group's member solutions, in member order.</param>
    /// <param name="context">The evaluation context the argument expressions and extension folds consume.</param>
    /// <returns>The aggregate's value, or <see langword="null"/> for the error value.</returns>
    internal static RdfTerm? EvaluateAggregate(AggregateExpression aggregate, IReadOnlyList<SparqlSolution> group, SparqlExpressionContext context)
    {
        return aggregate switch
        {
            BuiltInAggregateExpression builtIn => EvaluateBuiltInAggregate(builtIn, group, context),
            ExtensionAggregateExpression extension => EvaluateExtensionAggregate(extension, group, context),
            _ => throw new NotSupportedException($"SPARQL aggregate node '{aggregate.GetType().Name}' is not evaluable.")
        };
    }

    /// <summary>Computes a built-in keyword aggregate: the argument is evaluated per member (a member whose evaluation fails contributes no value), <c>DISTINCT</c> deduplicates by RDF term equality, and the named fold answers.</summary>
    /// <param name="aggregate">The built-in aggregate.</param>
    /// <param name="group">The group's member solutions, in member order.</param>
    /// <param name="context">The evaluation context the argument expression consumes.</param>
    /// <returns>The aggregate's value, or <see langword="null"/> for the error value.</returns>
    private static RdfTerm? EvaluateBuiltInAggregate(BuiltInAggregateExpression aggregate, IReadOnlyList<SparqlSolution> group, SparqlExpressionContext context)
    {
        if(aggregate.IsCountStar)
        {
            return Integer(group.Count).Term;
        }

        List<RdfTerm> values = [];
        if(aggregate.Argument is not null)
        {
            foreach(SparqlSolution solution in group)
            {
                if(TryEvaluate(aggregate.Argument, solution, context, out RdfTerm value))
                {
                    values.Add(value);
                }
            }
        }

        if(aggregate.IsDistinct)
        {
            values = DistinctTerms(values);
        }

        return aggregate.Function switch
        {
            AggregateFunction.Count => Integer(values.Count).Term,
            AggregateFunction.Sum => SumTerm(values),
            AggregateFunction.Avg => AverageTerm(values),
            AggregateFunction.Min => Extremum(values, minimum: true, context.ImplicitTimezone),
            AggregateFunction.Max => Extremum(values, minimum: false, context.ImplicitTimezone),
            AggregateFunction.Sample => values.Count > 0 ? values[0] : null,
            AggregateFunction.GroupConcat => GroupConcatTerm(values, aggregate.GroupConcatSeparator),
            _ => throw new NotSupportedException($"SPARQL aggregate function '{aggregate.Function}' is not yet evaluable.")
        };
    }

    /// <summary>
    /// Computes an IRI-named extension aggregate by consulting the context's extension-function
    /// registry for the IRI's aggregate face. The seam folds exactly one aggregated expression, so any
    /// other parsed arity — and an unregistered IRI — answers the error value. Per member, an argument
    /// evaluation failure over a solution that binds every free variable of the argument is a genuine
    /// expression error and fails the whole aggregate (an answer over silently fewer members would
    /// describe a different group); a failure with an unbound argument variable drops the member, the
    /// discipline <c>COUNT</c> shares over <c>OPTIONAL</c>-shaped data. <c>DISTINCT</c> deduplicates by
    /// RDF term equality — two lexically distinct forms of one value stay two members.
    /// </summary>
    /// <param name="aggregate">The extension aggregate.</param>
    /// <param name="group">The group's member solutions, in member order.</param>
    /// <param name="context">The evaluation context carrying the extension-function registry.</param>
    /// <returns>The aggregate's value, or <see langword="null"/> for the error value.</returns>
    private static RdfTerm? EvaluateExtensionAggregate(ExtensionAggregateExpression aggregate, IReadOnlyList<SparqlSolution> group, SparqlExpressionContext context)
    {
        if(aggregate.Arguments.Count != 1)
        {
            return null;
        }

        if(context.ExtensionFunctions.IsEmpty || !context.ExtensionFunctions.TryGetAggregate(aggregate.FunctionIri.Value, out SparqlAggregateDelegate? registered))
        {
            return null;
        }

        ExpressionNode argument = aggregate.Arguments[0];
        List<SparqlVariable> freeVariables = [];
        foreach(ExpressionNode node in ExpressionWalker.Traverse(argument))
        {
            if(node is VariableExpression variable)
            {
                freeVariables.Add(variable.Variable);
            }
        }

        List<RdfTerm> values = [];
        foreach(SparqlSolution solution in group)
        {
            if(TryEvaluate(argument, solution, context, out RdfTerm value))
            {
                values.Add(value);

                continue;
            }

            bool everyVariableBound = true;
            foreach(SparqlVariable variable in freeVariables)
            {
                if(!solution.TryGetValue(variable, out _))
                {
                    everyVariableBound = false;

                    break;
                }
            }

            if(everyVariableBound)
            {
                return null;
            }
        }

        if(aggregate.IsDistinct)
        {
            values = DistinctTerms(values);
        }

        SparqlFunctionResult result = registered(aggregate.FunctionIri.Value, new SparqlAggregateGroup(values.ToArray()), context);

        return result.IsError ? null : result.Term;
    }

    /// <summary>Deduplicates a value list by RDF term equality, preserving first-appearance order.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The distinct values.</returns>
    private static List<RdfTerm> DistinctTerms(List<RdfTerm> values)
    {
        List<RdfTerm> distinct = new(values.Count);
        HashSet<RdfTerm> seen = [];
        foreach(RdfTerm value in values)
        {
            if(seen.Add(value))
            {
                distinct.Add(value);
            }
        }

        return distinct;
    }

    /// <summary>Sums numeric values (xsd:integer when every value is integral, else xsd:double); an empty set sums to 0, a non-numeric value yields an error (null).</summary>
    /// <param name="values">The values to sum.</param>
    /// <returns>The sum term, or <see langword="null"/> on a non-numeric value.</returns>
    private static RdfTerm? SumTerm(List<RdfTerm> values)
    {
        //SUM starts from xsd:integer 0 (so an empty/all-integer set sums to an integer); each value promotes the
        //running total to the higher kind, and the tower keeps decimal sums exact.
        NumericValue sum = new(BigInteger.Zero);
        foreach(RdfTerm value in values)
        {
            if(!TryGetNumericValue(value, out NumericValue number))
            {
                return null;
            }

            sum = NumericValue.Add(sum, number);
        }

        return Numeric(sum).Term;
    }

    /// <summary>Averages numeric values as an xsd:double; an empty set averages to 0, a non-numeric value yields an error (null).</summary>
    /// <param name="values">The values to average.</param>
    /// <returns>The average term, or <see langword="null"/> on a non-numeric value.</returns>
    private static RdfTerm? AverageTerm(List<RdfTerm> values)
    {
        //AVG of the empty set is xsd:integer 0 (§18.5.1.5); otherwise it is SUM ÷ count, which the tower types as
        //xsd:decimal for an all-integer/decimal group and promotes to float/double when a member is float/double.
        if(values.Count == 0)
        {
            return Integer(0).Term;
        }

        NumericValue sum = new(BigInteger.Zero);
        foreach(RdfTerm value in values)
        {
            if(!TryGetNumericValue(value, out NumericValue number))
            {
                return null;
            }

            sum = NumericValue.Add(sum, number);
        }

        return NumericValue.TryDivide(sum, new NumericValue(new BigInteger(values.Count)), out NumericValue average)
            ? Numeric(average).Term
            : null;
    }

    /// <summary>Returns the minimum or maximum value by the ORDER BY ordering, or <see langword="null"/> for an empty set; the totalized ordering makes the result independent of encounter order.</summary>
    /// <param name="values">The values.</param>
    /// <param name="minimum">Whether to take the minimum (otherwise the maximum).</param>
    /// <param name="implicitTimezone">The implicit timezone temporal comparisons normalize naive operands with.</param>
    /// <returns>The extreme value, or <see langword="null"/> when there are none.</returns>
    private static RdfTerm? Extremum(List<RdfTerm> values, bool minimum, TimeSpan implicitTimezone)
    {
        if(values.Count == 0)
        {
            return null;
        }

        RdfTerm best = values[0];
        for(int i = 1; i < values.Count; i++)
        {
            int comparison = CompareForOrdering(values[i], best, implicitTimezone);
            if(minimum ? comparison < 0 : comparison > 0)
            {
                best = values[i];
            }
        }

        return best;
    }

    /// <summary>Concatenates the lexical forms of the values with a separator (default a single space) as an xsd:string, accumulating UTF-8 bytes (no <see cref="string"/> round-trip).</summary>
    /// <param name="values">The values to concatenate.</param>
    /// <param name="separator">The separator, or <see langword="null"/> for the default single space.</param>
    /// <returns>The concatenated string term.</returns>
    private static Literal GroupConcatTerm(List<RdfTerm> values, Utf8String? separator)
    {
        Utf8String delimiter = separator ?? SpaceText;
        List<byte> output = [];
        for(int i = 0; i < values.Count; i++)
        {
            if(i > 0)
            {
                AppendBytes(output, delimiter.Span);
            }

            AppendBytes(output, LexicalForm(values[i]).Span);
        }

        return new Literal(new Utf8String(output.ToArray()), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>Appends a byte span to a byte list.</summary>
    /// <param name="output">The accumulating byte list.</param>
    /// <param name="bytes">The bytes to append.</param>
    private static void AppendBytes(List<byte> output, ReadOnlySpan<byte> bytes)
    {
        foreach(byte value in bytes)
        {
            output.Add(value);
        }
    }

    /// <summary>Returns the UTF-8 lexical form of a term for string contexts: a literal's value, a named node's IRI, otherwise empty.</summary>
    /// <param name="term">The term.</param>
    /// <returns>The UTF-8 lexical form.</returns>
    private static Utf8String LexicalForm(RdfTerm term)
    {
        return term switch
        {
            Literal literal => literal.Value,
            NamedNode named => named.Iri,
            _ => EmptyText
        };
    }

    /// <summary>Evaluates an expression to a value (a bound term or an error) over an explicit bottom-up work stack.</summary>
    /// <param name="root">The expression to evaluate.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the non-pure functions consume.</param>
    /// <returns>The evaluated value.</returns>
    private static ExpressionValue Evaluate(ExpressionNode root, SparqlSolution solution, SparqlExpressionContext context)
    {
        Dictionary<ExpressionNode, ExpressionValue> values = new(ReferenceEqualityComparer.Instance);
        Stack<(ExpressionNode Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (ExpressionNode node, bool combine) = work.Pop();
            if(combine)
            {
                values[node] = CombineValue(node, values, solution, context);

                continue;
            }

            IReadOnlyList<ExpressionNode> children = Operands(node);
            if(children.Count == 0)
            {
                values[node] = EvaluateLeaf(node, solution, context);
            }
            else
            {
                work.Push((node, Combine: true));
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    work.Push((children[i], Combine: false));
                }
            }
        }

        return values[root];
    }

    /// <summary>Returns the sub-expressions an expression evaluates from; empty for a leaf or an unsupported form (handled in <see cref="EvaluateLeaf"/>).</summary>
    /// <param name="node">The expression node.</param>
    /// <returns>The operand sub-expressions, in evaluation order.</returns>
    private static IReadOnlyList<ExpressionNode> Operands(ExpressionNode node)
    {
        return node switch
        {
            AndExpression and => [and.Left, and.Right],
            OrExpression or => [or.Left, or.Right],
            NotExpression not => [not.Inner],
            ComparisonExpression comparison => [comparison.Left, comparison.Right],
            ArithmeticExpression arithmetic => arithmetic.Right is null ? [arithmetic.Left] : [arithmetic.Left, arithmetic.Right],
            IfExpression conditional => [conditional.Condition, conditional.IfTrue, conditional.IfFalse],
            CoalesceExpression coalesce => coalesce.Alternatives,
            InExpression test => Prepend(test.Value, test.Set),
            NotInExpression test => Prepend(test.Value, test.Set),
            BuiltInCallExpression call => call.Arguments,
            FunctionCallExpression call => call.Arguments,

            //Leaves (ConstantExpression, VariableExpression, BoundExpression) and the unsupported forms.
            _ => []
        };
    }

    /// <summary>Evaluates a leaf expression (or rejects an unsupported form): a constant, a variable, a <c>BOUND</c> test, a zero-argument built-in (<c>NOW</c>/<c>RAND</c>/<c>UUID</c>/<c>STRUUID</c>), or a zero-argument extension-function call.</summary>
    /// <param name="node">The leaf node.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the non-pure functions consume.</param>
    /// <returns>The leaf's value.</returns>
    /// <exception cref="NotSupportedException">The node is an unsupported form (an EXISTS occurrence the resolver did not rewrite).</exception>
    private static ExpressionValue EvaluateLeaf(ExpressionNode node, SparqlSolution solution, SparqlExpressionContext context)
    {
        return node switch
        {
            ConstantExpression constant => ExpressionValue.Of(constant.Value),

            //An unbound variable raises an error when its value is needed.
            VariableExpression variable => solution.TryGetValue(variable.Variable, out RdfTerm value) ? ExpressionValue.Of(value) : ExpressionValue.Error,
            BoundExpression bound => Boolean(solution.TryGetValue(bound.Variable, out _)),

            //A zero-argument built-in (NOW/RAND/UUID/STRUUID/BNODE) has no operands, so it reaches the leaf path.
            BuiltInCallExpression call => EvaluateBuiltIn(call.Function, [], solution, context),

            //A zero-argument extension-function call has no operands either, so it reaches the leaf path with
            //nothing to look up — the shared empty operand map satisfies the consult without a per-call allocation.
            FunctionCallExpression call => EvaluateFunctionCall(call, EmptyOperandValues, context),

            //COALESCE() with no alternatives reaches here (no operands) — it has no bound value, so it errs.
            CoalesceExpression => ExpressionValue.Error,

            //A quoted triple term in an expression: build the triple-term value from its resolved components.
            TripleTermExpression tripleTerm => EvaluateTripleTermExpression(tripleTerm, solution),
            _ => throw new NotSupportedException($"SPARQL expression '{node.GetType().Name}' is not yet evaluable; this slice covers constants, variables, BOUND, the logical/comparison/arithmetic operators, IF, COALESCE, IN/NOT IN, named XSD constructor casts, triple-term construction, and a core of the built-in functions. EXISTS/NOT EXISTS and arbitrary extension functions land in a later slice.")
        };
    }

    /// <summary>Combines an expression's already-evaluated operand values (read from <paramref name="values"/>) into its own value.</summary>
    /// <param name="node">The expression to combine.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <param name="solution">The solution the expression is evaluated over (the correlation scope for <c>BNODE</c>).</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the non-pure functions consume.</param>
    /// <returns>The expression's value.</returns>
    private static ExpressionValue CombineValue(ExpressionNode node, Dictionary<ExpressionNode, ExpressionValue> values, SparqlSolution solution, SparqlExpressionContext context)
    {
        return node switch
        {
            AndExpression and => LogicalAnd(EffectiveState(values[and.Left]), EffectiveState(values[and.Right])),
            OrExpression or => LogicalOr(EffectiveState(values[or.Left]), EffectiveState(values[or.Right])),
            NotExpression not => EffectiveState(values[not.Inner]) is bool state ? Boolean(!state) : ExpressionValue.Error,
            ComparisonExpression comparison => Compare(values[comparison.Left], values[comparison.Right], comparison.Op, context),
            ArithmeticExpression arithmetic => Arithmetic(values[arithmetic.Left], arithmetic.Right is null ? default : values[arithmetic.Right], arithmetic.Op),
            IfExpression conditional => EffectiveState(values[conditional.Condition]) is bool taken ? (taken ? values[conditional.IfTrue] : values[conditional.IfFalse]) : ExpressionValue.Error,
            CoalesceExpression coalesce => FirstBound(coalesce.Alternatives, values),
            InExpression test => Membership(test.Value, test.Set, values, negated: false, context),
            NotInExpression test => Membership(test.Value, test.Set, values, negated: true, context),
            BuiltInCallExpression call => EvaluateBuiltInCall(call, values, solution, context),
            FunctionCallExpression call => EvaluateFunctionCall(call, values, context),
            _ => throw new InvalidOperationException($"Expression '{node.GetType().Name}' has operands but no combine rule in the evaluator.")
        };
    }

    /// <summary>Returns the first non-error alternative value (the §17.4 <c>COALESCE</c> semantics), or an error when every alternative errs.</summary>
    /// <param name="alternatives">The candidate expressions, in order.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <returns>The first non-error value, or an error.</returns>
    private static ExpressionValue FirstBound(IReadOnlyList<ExpressionNode> alternatives, Dictionary<ExpressionNode, ExpressionValue> values)
    {
        foreach(ExpressionNode alternative in alternatives)
        {
            ExpressionValue value = values[alternative];
            if(!value.IsError)
            {
                return value;
            }
        }

        return ExpressionValue.Error;
    }

    /// <summary>Gathers a built-in call's already-evaluated argument values and dispatches to the function.</summary>
    /// <param name="call">The built-in call.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <param name="solution">The solution the call is evaluated over (the correlation scope for <c>BNODE</c>).</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp) the non-pure functions consume.</param>
    /// <returns>The call's value.</returns>
    private static ExpressionValue EvaluateBuiltInCall(BuiltInCallExpression call, Dictionary<ExpressionNode, ExpressionValue> values, SparqlSolution solution, SparqlExpressionContext context)
    {
        ExpressionValue[] arguments = new ExpressionValue[call.Arguments.Count];
        for(int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = values[call.Arguments[i]];
        }

        return EvaluateBuiltIn(call.Function, arguments, solution, context);
    }

    /// <summary>Evaluates <c>IN</c> / <c>NOT IN</c>: membership of the tested value among the candidate set under <c>=</c> semantics.</summary>
    /// <param name="valueNode">The tested-value expression.</param>
    /// <param name="set">The candidate-set expressions.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <param name="negated">Whether this is the <c>NOT IN</c> form.</param>
    /// <param name="context">The seams the comparison consumes (the implicit timezone).</param>
    /// <returns>The membership result: a boolean, or an error when the tested value errs or a comparison errs with no match found.</returns>
    private static ExpressionValue Membership(ExpressionNode valueNode, IReadOnlyList<ExpressionNode> set, Dictionary<ExpressionNode, ExpressionValue> values, bool negated, SparqlExpressionContext context)
    {
        ExpressionValue value = values[valueNode];
        if(value.IsError)
        {
            return ExpressionValue.Error;
        }

        bool anyError = false;
        foreach(ExpressionNode candidate in set)
        {
            ExpressionValue comparison = Compare(value, values[candidate], ComparisonOp.Equal, context);
            if(comparison.IsError)
            {
                anyError = true;

                continue;
            }

            if(ReferenceEquals(comparison.Term, True))
            {
                return Boolean(!negated);
            }
        }

        //No match: an error in any comparison makes the overall result an error; otherwise it is a clean miss.
        return anyError ? ExpressionValue.Error : Boolean(negated);
    }

    /// <summary>
    /// Evaluates a named function call. The XSD constructor functions (§17.5) cast their single argument to
    /// that datatype; every other IRI consults the context's extension-function registry (§17.6). An
    /// unregistered IRI — and a registered invocation whose arguments carry an error — evaluates to the
    /// expression error value, never an engine fault: an error is a value in SPARQL expression evaluation,
    /// so a <c>FILTER</c> drops the row and a <c>BIND</c> leaves the variable unbound.
    /// </summary>
    /// <param name="call">The function call.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <param name="context">The evaluation context carrying the extension-function registry.</param>
    /// <returns>The call's value, or an error.</returns>
    private static ExpressionValue EvaluateFunctionCall(FunctionCallExpression call, Dictionary<ExpressionNode, ExpressionValue> values, SparqlExpressionContext context)
    {
        //A DISTINCT-marked call that reaches scalar evaluation was not recognized as an aggregate; the
        //grammar reserves the argument-list DISTINCT for aggregate calls, so honoring the call as a
        //scalar — cast and extension paths alike — would silently mean something else. The check runs
        //ahead of every dispatch branch.
        if(call.IsDistinct)
        {
            return ExpressionValue.Error;
        }

        Utf8String target = call.Function.Value;
        if(call.Arguments.Count == 1 && IsXsdCastTarget(target))
        {
            return Cast(target, values[call.Arguments[0]]);
        }

        if(!context.ExtensionFunctions.IsEmpty && context.ExtensionFunctions.TryGet(target, out SparqlFunctionDelegate? function))
        {
            //Extension functions take argument VALUES: an error argument makes the invocation an error
            //before the function is consulted, so the function body only ever sees bound terms.
            RdfTerm[] arguments = new RdfTerm[call.Arguments.Count];
            for(int i = 0; i < arguments.Length; i++)
            {
                ExpressionValue argument = values[call.Arguments[i]];
                if(argument.IsError)
                {
                    return ExpressionValue.Error;
                }

                arguments[i] = argument.Term;
            }

            SparqlFunctionResult result = function(target, arguments, context);
            return result.IsError ? ExpressionValue.Error : ExpressionValue.Of(result.Term);
        }

        return ExpressionValue.Error;
    }

    /// <summary>Returns whether an IRI names an XSD constructor (cast) function this evaluator implements.</summary>
    /// <param name="iri">The function IRI.</param>
    /// <returns><see langword="true"/> for the supported XSD datatype constructors.</returns>
    private static bool IsXsdCastTarget(Utf8String iri)
    {
        return iri.Equals(Vocabulary.Xsd.Integer) || iri.Equals(Vocabulary.Xsd.Decimal) || iri.Equals(Vocabulary.Xsd.Float)
            || iri.Equals(Vocabulary.Xsd.Double) || iri.Equals(Vocabulary.Xsd.Boolean) || iri.Equals(Vocabulary.Xsd.String);
    }

    /// <summary>Casts a value to an XSD datatype (§17.1 / XPath casting): the string, boolean, or numeric constructor.</summary>
    /// <param name="target">The XSD datatype IRI to cast to.</param>
    /// <param name="arg">The argument value.</param>
    /// <returns>The cast literal, or an error when the value cannot be cast.</returns>
    private static ExpressionValue Cast(Utf8String target, ExpressionValue arg)
    {
        if(arg.IsError)
        {
            return ExpressionValue.Error;
        }

        if(target.Equals(Vocabulary.Xsd.String))
        {
            return CastToString(arg.Term);
        }

        if(target.Equals(Vocabulary.Xsd.Boolean))
        {
            return CastToBoolean(arg.Term);
        }

        return CastToNumeric(arg.Term, target);
    }

    /// <summary>Casts to <c>xsd:string</c>: a literal's lexical form, or an IRI's text.</summary>
    /// <param name="term">The term to cast.</param>
    /// <returns>The string literal, or an error.</returns>
    private static ExpressionValue CastToString(RdfTerm term)
    {
        switch(term)
        {
            case NamedNode iri:
            {
                return StringLiteral(iri.Iri);
            }

            case Literal literal when TryGetNumericValue(literal, out NumericValue number):
            {
                //Casting a number to string yields its XPath string form — the value with no trailing zeros and no
                //exponent for ordinary magnitudes (e.g. double "1E0" → "1", decimal "1.0" → "1") — NOT the XSD
                //canonical scientific lexical and NOT the source lexical.
                return StringLiteral(Utf8Strings.From(NumberToString(number)));
            }

            case Literal literal when literal.Datatype.Iri.Equals(Vocabulary.Xsd.Boolean):
            {
                return StringLiteral(Utf8Strings.From(literal.Value.ToString() is "1" or "true" ? "true" : "false"));
            }

            case Literal literal:
            {
                return StringLiteral(literal.Value);
            }

            default:
            {
                return ExpressionValue.Error;
            }
        }
    }

    /// <summary>Casts to <c>xsd:boolean</c>: a boolean lexical, a numeric (zero/NaN → false), or a string <c>true</c>/<c>false</c>/<c>1</c>/<c>0</c>.</summary>
    /// <param name="term">The term to cast.</param>
    /// <returns>The boolean value, or an error.</returns>
    private static ExpressionValue CastToBoolean(RdfTerm term)
    {
        if(term is not Literal literal)
        {
            return ExpressionValue.Error;
        }

        if(literal.Datatype.Iri.Equals(Vocabulary.Xsd.Boolean) || (literal.Language is null && literal.Datatype.Iri.Equals(Vocabulary.Xsd.String)))
        {
            return BooleanFromLexical(literal.Value);
        }

        return TryGetNumericValue(literal, out NumericValue number) ? Boolean(!IsZeroOrNaN(number)) : ExpressionValue.Error;
    }

    /// <summary>Casts to a numeric XSD datatype: a numeric (converted to the target kind), a boolean (1/0), or a string parsed as that datatype.</summary>
    /// <param name="term">The term to cast.</param>
    /// <param name="target">The target numeric XSD datatype IRI.</param>
    /// <returns>The numeric literal, or an error.</returns>
    private static ExpressionValue CastToNumeric(RdfTerm term, Utf8String target)
    {
        if(term is not Literal literal)
        {
            return ExpressionValue.Error;
        }

        if(TryGetNumericValue(literal, out NumericValue source))
        {
            return Numeric(ConvertNumeric(source, target));
        }

        if(literal.Datatype.Iri.Equals(Vocabulary.Xsd.Boolean))
        {
            ExpressionValue boolean = BooleanFromLexical(literal.Value);

            return boolean.IsError
                ? ExpressionValue.Error
                : Numeric(ConvertNumeric(new NumericValue(ReferenceEquals(boolean.Term, True) ? BigInteger.One : BigInteger.Zero), target));
        }

        if(literal.Language is null && literal.Datatype.Iri.Equals(Vocabulary.Xsd.String))
        {
            return NumericValue.TryParse(literal.Value.ToString(), target, out NumericValue parsed) ? Numeric(parsed) : ExpressionValue.Error;
        }

        return ExpressionValue.Error;
    }

    /// <summary>Converts a numeric value to the kind named by an XSD numeric datatype IRI (truncating toward zero for the integer cast).</summary>
    /// <param name="number">The source numeric.</param>
    /// <param name="target">The target XSD numeric datatype IRI.</param>
    /// <returns>The converted numeric.</returns>
    private static NumericValue ConvertNumeric(NumericValue number, Utf8String target)
    {
        if(target.Equals(Vocabulary.Xsd.Integer))
        {
            return new NumericValue(ToBigInteger(number));
        }

        if(target.Equals(Vocabulary.Xsd.Decimal))
        {
            return new NumericValue(ToDecimal(number));
        }

        return target.Equals(Vocabulary.Xsd.Float) ? new NumericValue(ToFloat(number)) : new NumericValue(ToDouble(number));
    }

    /// <summary>Converts any numeric (regardless of its kind) to a CLR <see cref="double"/> — the kind-specific <c>As*</c> accessors are only valid for their own kind.</summary>
    /// <param name="number">The numeric to convert.</param>
    /// <returns>The double value.</returns>
    private static double ToDouble(NumericValue number)
    {
        return number.Kind switch
        {
            NumericKind.Integer => (double)number.AsInteger(),
            NumericKind.Decimal => (double)number.AsDecimal(),
            NumericKind.Float => number.AsFloat(),
            _ => number.AsDouble()
        };
    }

    /// <summary>Converts any numeric (regardless of its kind) to a CLR <see cref="decimal"/>.</summary>
    /// <param name="number">The numeric to convert.</param>
    /// <returns>The decimal value.</returns>
    private static decimal ToDecimal(NumericValue number)
    {
        return number.Kind switch
        {
            NumericKind.Integer => (decimal)number.AsInteger(),
            NumericKind.Decimal => number.AsDecimal(),
            NumericKind.Float => (decimal)number.AsFloat(),
            _ => (decimal)number.AsDouble()
        };
    }

    /// <summary>Converts any numeric (regardless of its kind) to a CLR <see cref="float"/>.</summary>
    /// <param name="number">The numeric to convert.</param>
    /// <returns>The float value.</returns>
    private static float ToFloat(NumericValue number)
    {
        return number.Kind switch
        {
            NumericKind.Integer => (float)number.AsInteger(),
            NumericKind.Decimal => (float)number.AsDecimal(),
            NumericKind.Float => number.AsFloat(),
            _ => (float)number.AsDouble()
        };
    }

    /// <summary>Formats a numeric as a string for the <c>xsd:string</c> cast (XPath number→string): the value with trailing zeros and a redundant fraction/exponent removed (so <c>1.0</c> / <c>1E0</c> → <c>1</c>, <c>1.25</c> → <c>1.25</c>).</summary>
    /// <param name="number">The numeric to format.</param>
    /// <returns>The XPath string form.</returns>
    private static string NumberToString(NumericValue number)
    {
        return number.Kind switch
        {
            NumericKind.Integer => number.AsInteger().ToString(CultureInfo.InvariantCulture),
            NumericKind.Decimal => TrimDecimal(number.AsDecimal().ToString(CultureInfo.InvariantCulture)),
            NumericKind.Float => number.AsFloat().ToString(CultureInfo.InvariantCulture),
            _ => number.AsDouble().ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Strips a decimal's trailing fractional zeros (and a now-redundant point), so <c>"1.0"</c> → <c>"1"</c> and <c>"2.50"</c> → <c>"2.5"</c>.</summary>
    /// <param name="lexical">The decimal lexical form.</param>
    /// <returns>The trimmed form.</returns>
    private static string TrimDecimal(string lexical)
    {
        if(!lexical.Contains('.', StringComparison.Ordinal))
        {
            return lexical;
        }

        string trimmed = lexical.TrimEnd('0').TrimEnd('.');

        return trimmed.Length == 0 || trimmed == "-" ? "0" : trimmed;
    }

    /// <summary>Truncates a numeric toward zero to a <see cref="BigInteger"/> (the <c>xsd:integer</c> cast).</summary>
    /// <param name="number">The numeric to truncate.</param>
    /// <returns>The integer value.</returns>
    private static BigInteger ToBigInteger(NumericValue number)
    {
        return number.Kind switch
        {
            NumericKind.Integer => number.AsInteger(),
            NumericKind.Decimal => (BigInteger)decimal.Truncate(number.AsDecimal()),
            NumericKind.Float => new BigInteger(MathF.Truncate(number.AsFloat())),
            _ => new BigInteger(Math.Truncate(number.AsDouble()))
        };
    }

    /// <summary>Interprets an XSD boolean lexical form (<c>true</c>/<c>1</c> → true, <c>false</c>/<c>0</c> → false), erroring on any other.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The boolean value, or an error.</returns>
    private static ExpressionValue BooleanFromLexical(Utf8String lexical)
    {
        return lexical.ToString() switch
        {
            "true" or "1" => Boolean(true),
            "false" or "0" => Boolean(false),
            _ => ExpressionValue.Error
        };
    }

    /// <summary>Returns whether a numeric is zero or NaN (for the <c>xsd:boolean</c> cast of a number).</summary>
    /// <param name="number">The numeric to test.</param>
    /// <returns><see langword="true"/> when the numeric is zero or NaN.</returns>
    private static bool IsZeroOrNaN(NumericValue number)
    {
        return number.Kind switch
        {
            NumericKind.Integer => number.AsInteger().IsZero,
            NumericKind.Decimal => number.AsDecimal() == decimal.Zero,
            NumericKind.Float => number.AsFloat() == 0f || float.IsNaN(number.AsFloat()),
            _ => number.AsDouble() == 0d || double.IsNaN(number.AsDouble())
        };
    }

    /// <summary>Evaluates a quoted triple term in an expression: builds the <see cref="Lumoin.Veritas.Core.TripleTerm"/> from its resolved components (a variable from the solution, a constant in place, a nested triple term recursively), erroring when a component is unbound or the result is not a legal triple.</summary>
    /// <param name="node">The triple-term expression.</param>
    /// <param name="solution">The solution supplying variable bindings.</param>
    /// <returns>The triple-term value, or an error.</returns>
    private static ExpressionValue EvaluateTripleTermExpression(TripleTermExpression node, SparqlSolution solution)
    {
        Lumoin.Veritas.Core.TripleTerm? triple = BuildExpressionTripleTerm(
            ResolveTripleTermComponent(node.Inner.Subject, solution),
            ResolveTripleTermComponent(node.Inner.Predicate, solution),
            ResolveTripleTermComponent(node.Inner.Object, solution));

        return triple is null ? ExpressionValue.Error : ExpressionValue.Of(triple);
    }

    /// <summary>Resolves a triple-term component to an RDF term over an explicit post-order stack (no recursion): a variable from the solution (unbound → <see langword="null"/>), a constant in place, a nested quoted triple term to a built triple term.</summary>
    /// <param name="root">The component term.</param>
    /// <param name="solution">The solution supplying variable bindings.</param>
    /// <returns>The resolved term, or <see langword="null"/>.</returns>
    private static RdfTerm? ResolveTripleTermComponent(TriplePatternTerm root, SparqlSolution solution)
    {
        Dictionary<TriplePatternTerm, RdfTerm?> resolved = new(ReferenceEqualityComparer.Instance);
        Stack<(TriplePatternTerm Term, bool Combine, int Depth)> work = new();
        work.Push((root, Combine: false, Depth: 1));

        while(work.Count > 0)
        {
            (TriplePatternTerm term, bool combine, int depth) = work.Pop();
            if(combine)
            {
                Lumoin.Veritas.Sparql.Ast.TripleTerm nested = (Lumoin.Veritas.Sparql.Ast.TripleTerm)term;
                resolved[term] = BuildExpressionTripleTerm(resolved[nested.Inner.Subject], resolved[nested.Inner.Predicate], resolved[nested.Inner.Object]);

                continue;
            }

            switch(term)
            {
                case VariableTerm variable:
                {
                    resolved[term] = solution.TryGetValue(variable.Variable, out RdfTerm value) ? value : null;

                    break;
                }

                case ConstantTerm constant:
                {
                    resolved[term] = constant.Term;

                    break;
                }

                case Lumoin.Veritas.Sparql.Ast.TripleTerm nested:
                {
                    if(depth > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                    }

                    work.Push((term, Combine: true, depth));
                    work.Push((nested.Inner.Object, Combine: false, depth + 1));
                    work.Push((nested.Inner.Predicate, Combine: false, depth + 1));
                    work.Push((nested.Inner.Subject, Combine: false, depth + 1));

                    break;
                }

                default:
                {
                    resolved[term] = null;

                    break;
                }
            }
        }

        return resolved[root];
    }

    /// <summary>Builds a triple term from resolved components, or <see langword="null"/> when they cannot form one (unbound, literal subject, or non-IRI predicate).</summary>
    /// <param name="subject">The resolved subject.</param>
    /// <param name="predicate">The resolved predicate.</param>
    /// <param name="object">The resolved object.</param>
    /// <returns>The triple term, or <see langword="null"/>.</returns>
    private static Lumoin.Veritas.Core.TripleTerm? BuildExpressionTripleTerm(RdfTerm? subject, RdfTerm? predicate, RdfTerm? @object)
    {
        if(subject is null or Literal || predicate is not NamedNode predicateIri || @object is null)
        {
            return null;
        }

        return new Lumoin.Veritas.Core.TripleTerm(subject, predicateIri, @object);
    }

    /// <summary>
    /// Evaluates a built-in function over its already-evaluated arguments (§17.4). This slice covers the term-type
    /// tests, <c>sameTerm</c>, <c>STR</c>/<c>LANG</c>/<c>DATATYPE</c>, the common string functions
    /// (<c>STRLEN</c>/<c>UCASE</c>/<c>LCASE</c>/<c>CONTAINS</c>/<c>STRSTARTS</c>/<c>STRENDS</c>), and the unary
    /// numeric functions (<c>ABS</c>/<c>CEIL</c>/<c>FLOOR</c>/<c>ROUND</c>); the rest raise
    /// <see cref="NotSupportedException"/> until their later slice.
    /// </summary>
    /// <param name="function">The built-in function.</param>
    /// <param name="arguments">The evaluated argument values, in order.</param>
    /// <param name="solution">The solution the call is evaluated over (the correlation scope for <c>BNODE</c>).</param>
    /// <param name="context">The seams (randomness, digests, the query timestamp, blank-node identity) the non-pure functions consume.</param>
    /// <returns>The function's value, or an error.</returns>
    /// <exception cref="NotSupportedException">The function is not yet evaluable.</exception>
    private static ExpressionValue EvaluateBuiltIn(BuiltInFunction function, ExpressionValue[] arguments, SparqlSolution solution, SparqlExpressionContext context)
    {
        return function switch
        {
            BuiltInFunction.IsIri or BuiltInFunction.IsUri => TypeTest(arguments[0], static term => term is NamedNode),
            BuiltInFunction.IsBlank => TypeTest(arguments[0], static term => term is BlankNode),
            BuiltInFunction.IsLiteral => TypeTest(arguments[0], static term => term is Literal),
            BuiltInFunction.IsTriple => TypeTest(arguments[0], static term => term is Lumoin.Veritas.Core.TripleTerm),
            BuiltInFunction.IsNumeric => TypeTest(arguments[0], static term => TryGetNumericValue(term, out _)),
            BuiltInFunction.SameTerm => arguments[0].IsError || arguments[1].IsError ? ExpressionValue.Error : Boolean(arguments[0].Term.Equals(arguments[1].Term)),
            BuiltInFunction.Str => Str(arguments[0]),
            BuiltInFunction.Lang => arguments[0].IsError || arguments[0].Term is not Literal language ? ExpressionValue.Error : StringLiteral(language.Language ?? EmptyText),
            BuiltInFunction.LangDir => arguments[0].IsError || arguments[0].Term is not Literal directional ? ExpressionValue.Error : StringLiteral(DirectionText(directional.BaseDirection)),
            BuiltInFunction.HasLang => arguments[0].IsError ? ExpressionValue.Error : Boolean(arguments[0].Term is Literal { Language: not null }),
            BuiltInFunction.HasLangDir => arguments[0].IsError ? ExpressionValue.Error : Boolean(arguments[0].Term is Literal { BaseDirection: not null }),
            BuiltInFunction.LangMatches => LangMatches(arguments[0], arguments[1]),
            BuiltInFunction.Datatype => arguments[0].IsError || arguments[0].Term is not Literal datatype ? ExpressionValue.Error : ExpressionValue.Of(datatype.Datatype),
            BuiltInFunction.StrLen => TryGetLexicalText(arguments[0], out Utf8String text) ? Integer(CodePointCount(text)) : ExpressionValue.Error,
            BuiltInFunction.UCase => MapCaseValue(arguments[0], toUpper: true),
            BuiltInFunction.LCase => MapCaseValue(arguments[0], toUpper: false),
            BuiltInFunction.Contains => StringTest(arguments, static (haystack, needle) => haystack.Span.IndexOf(needle.Span) >= 0),
            BuiltInFunction.StrStarts => StringTest(arguments, static (text, prefix) => text.Span.StartsWith(prefix.Span)),
            BuiltInFunction.StrEnds => StringTest(arguments, static (text, suffix) => text.Span.EndsWith(suffix.Span)),
            BuiltInFunction.Concat => Concat(arguments),
            BuiltInFunction.Substr => Substr(arguments),
            BuiltInFunction.StrBefore => StrBeforeAfter(arguments, before: true),
            BuiltInFunction.StrAfter => StrBeforeAfter(arguments, before: false),
            BuiltInFunction.EncodeForUri => EncodeForUri(arguments[0]),
            BuiltInFunction.Regex => RegexMatch(arguments, context),
            BuiltInFunction.Replace => Replace(arguments, context),
            BuiltInFunction.Iri or BuiltInFunction.Uri => Iri(arguments[0], context),
            BuiltInFunction.StrDt => StrDt(arguments),
            BuiltInFunction.StrLang => StrLang(arguments),
            BuiltInFunction.StrLangDir => StrLangDir(arguments),
            BuiltInFunction.Year => DateComponent(arguments[0], static dateTime => dateTime.Year),
            BuiltInFunction.Month => DateComponent(arguments[0], static dateTime => dateTime.Month),
            BuiltInFunction.Day => DateComponent(arguments[0], static dateTime => dateTime.Day),
            BuiltInFunction.Hours => DateComponent(arguments[0], static dateTime => dateTime.Hour),
            BuiltInFunction.Minutes => DateComponent(arguments[0], static dateTime => dateTime.Minute),
            BuiltInFunction.Seconds => Seconds(arguments[0]),
            BuiltInFunction.Timezone => Timezone(arguments[0]),
            BuiltInFunction.Tz => Tz(arguments[0]),
            BuiltInFunction.Triple => BuildTriple(arguments),
            BuiltInFunction.Subject => TripleComponent(arguments[0], static triple => triple.Subject),
            BuiltInFunction.Predicate => TripleComponent(arguments[0], static triple => triple.Predicate),
            BuiltInFunction.Object => TripleComponent(arguments[0], static triple => triple.Object),
            BuiltInFunction.Now => ExpressionValue.Of(new Literal(Utf8Strings.From(context.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffK", CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.DateTime))),
            BuiltInFunction.Rand => Double(context.Randomness(new RandomnessRequest(RandomnessKind.UniformDouble, default, 0, default)).Double),
            BuiltInFunction.Uuid => ExpressionValue.Of(new NamedNode(Utf8Strings.From("urn:uuid:" + context.Randomness(new RandomnessRequest(RandomnessKind.Uuid, default, 0, default)).Uuid.ToString("D", CultureInfo.InvariantCulture)))),
            BuiltInFunction.StrUuid => StringLiteral(Utf8Strings.From(context.Randomness(new RandomnessRequest(RandomnessKind.Uuid, default, 0, default)).Uuid.ToString("D", CultureInfo.InvariantCulture))),
            BuiltInFunction.Md5 => Hash(arguments[0], SparqlHashAlgorithm.Md5, context),
            BuiltInFunction.Sha1 => Hash(arguments[0], SparqlHashAlgorithm.Sha1, context),
            BuiltInFunction.Sha256 => Hash(arguments[0], SparqlHashAlgorithm.Sha256, context),
            BuiltInFunction.Sha384 => Hash(arguments[0], SparqlHashAlgorithm.Sha384, context),
            BuiltInFunction.Sha512 => Hash(arguments[0], SparqlHashAlgorithm.Sha512, context),
            BuiltInFunction.Abs => UnaryNumeric(arguments[0], UnaryNumericOp.Abs),
            BuiltInFunction.Ceil => UnaryNumeric(arguments[0], UnaryNumericOp.Ceil),
            BuiltInFunction.Floor => UnaryNumeric(arguments[0], UnaryNumericOp.Floor),
            BuiltInFunction.Round => UnaryNumeric(arguments[0], UnaryNumericOp.Round),
            BuiltInFunction.BNode => BNode(arguments, solution, context),
            _ => throw new NotSupportedException($"SPARQL built-in function '{function}' is not yet evaluable; this slice covers the term-type tests, sameTerm, STR/LANG/LANGMATCHES/DATATYPE, the string functions (STRLEN/UCASE/LCASE/CONTAINS/STRSTARTS/STRENDS/CONCAT/SUBSTR/STRBEFORE/STRAFTER/ENCODE_FOR_URI/REGEX/REPLACE), the term constructors (IRI/URI/STRDT/STRLANG), the date-time accessors (YEAR/MONTH/DAY/HOURS/MINUTES/SECONDS/TIMEZONE/TZ), the triple builders (TRIPLE/SUBJECT/PREDICATE/OBJECT), the seam-backed RAND/UUID/STRUUID/NOW and MD5/SHA* hashes, ABS/CEIL/FLOOR/ROUND, and BNODE. The named functions and EXISTS/NOT EXISTS land in a later slice.")
        };
    }

    /// <summary>
    /// Evaluates <c>BNODE</c> (§17.4.2.3): the zero-argument form yields a fresh blank node on every call, while the
    /// one-argument form returns the blank node correlated to its simple-literal argument within this solution (the
    /// same key in the same solution gives the same node; in another solution, a different one). A non-string or
    /// errored argument yields an error.
    /// </summary>
    /// <param name="arguments">The evaluated argument values (zero or one).</param>
    /// <param name="solution">The solution the call is evaluated over — the correlation scope.</param>
    /// <param name="context">The context carrying the per-solution blank-node scope.</param>
    /// <returns>The blank node, or an error.</returns>
    private static ExpressionValue BNode(ExpressionValue[] arguments, SparqlSolution solution, SparqlExpressionContext context)
    {
        if(arguments.Length == 0)
        {
            return ExpressionValue.Of(context.BlankNodeScope.Fresh());
        }

        ExpressionValue argument = arguments[0];
        if(argument.IsError || argument.Term is not Literal { Language: null } literal || literal.Datatype.Iri != Vocabulary.Xsd.String)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(context.BlankNodeScope.Correlated(solution, literal.Value));
    }

    /// <summary>Evaluates <c>STR</c>: the lexical form of a literal or the IRI text of a named node as an <c>xsd:string</c>; a blank node or triple term errs.</summary>
    /// <param name="argument">The argument value.</param>
    /// <returns>The string value, or an error.</returns>
    private static ExpressionValue Str(ExpressionValue argument)
    {
        if(argument.IsError)
        {
            return ExpressionValue.Error;
        }

        return argument.Term switch
        {
            Literal literal => StringLiteral(literal.Value),
            NamedNode named => StringLiteral(named.Iri),
            _ => ExpressionValue.Error
        };
    }

    /// <summary>Evaluates <c>UCASE</c>/<c>LCASE</c> (§17.4.4.3/4): the cased lexical form of a string argument, mapping over Unicode runes (locale-independent, per the XPath case functions SPARQL defers to). The result keeps the argument's language tag (a plain or <c>xsd:string</c> argument yields an <c>xsd:string</c>).</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="toUpper">Whether to upper-case (otherwise lower-case).</param>
    /// <returns>The cased string value, or an error when the argument is not a string.</returns>
    private static ExpressionValue MapCaseValue(ExpressionValue argument, bool toUpper)
    {
        return TryGetStringArgument(argument, out Utf8String text, out Utf8String? language)
            ? StringLiteral(MapCase(text, toUpper), language)
            : ExpressionValue.Error;
    }

    /// <summary>Tests a property of an evaluated RDF term — its kind, for the term-type predicates.</summary>
    /// <param name="term">The bound term to test.</param>
    /// <returns><see langword="true"/> when the property holds.</returns>
    private delegate bool RdfTermPredicate(RdfTerm term);

    /// <summary>Applies a term-type predicate to a single argument, propagating an argument error.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="test">The predicate on the bound term.</param>
    /// <returns>A boolean value, or an error when the argument errs.</returns>
    private static ExpressionValue TypeTest(ExpressionValue argument, RdfTermPredicate test)
    {
        return argument.IsError ? ExpressionValue.Error : Boolean(test(argument.Term));
    }

    /// <summary>Tests a binary property over two UTF-8 string values (operating on their byte spans).</summary>
    /// <param name="left">The first UTF-8 value.</param>
    /// <param name="right">The second UTF-8 value.</param>
    /// <returns><see langword="true"/> when the property holds.</returns>
    private delegate bool Utf8StringBinaryPredicate(Utf8String left, Utf8String right);

    /// <summary>Applies a binary predicate over two string arguments' UTF-8 values, erring when either is not a string or errs.</summary>
    /// <param name="arguments">The argument values (two strings expected).</param>
    /// <param name="test">The predicate over the two UTF-8 values (operates on their byte spans).</param>
    /// <returns>A boolean value, or an error.</returns>
    private static ExpressionValue StringTest(ExpressionValue[] arguments, Utf8StringBinaryPredicate test)
    {
        return TryGetLexicalText(arguments[0], out Utf8String left) && TryGetLexicalText(arguments[1], out Utf8String right)
            ? Boolean(test(left, right))
            : ExpressionValue.Error;
    }

    /// <summary>
    /// Evaluates <c>LANGMATCHES</c> (§17.4.1.10): whether a language tag matches a language range under RFC 4647
    /// basic filtering. The range <c>"*"</c> matches any non-empty tag; otherwise the tag matches when it equals
    /// the range or extends it at a subtag boundary (<c>"-"</c>), compared case-insensitively (language tags are
    /// ASCII). Both arguments must be string-valued; otherwise the result is an error.
    /// </summary>
    /// <param name="tagValue">The language-tag argument (typically <c>LANG(?x)</c>).</param>
    /// <param name="rangeValue">The language-range argument.</param>
    /// <returns>A boolean value, or an error when either argument is not a string.</returns>
    private static ExpressionValue LangMatches(ExpressionValue tagValue, ExpressionValue rangeValue)
    {
        if(!TryGetLexicalText(tagValue, out Utf8String tag) || !TryGetLexicalText(rangeValue, out Utf8String range))
        {
            return ExpressionValue.Error;
        }

        return Boolean(LanguageRangeMatches(tag.Span, range.Span));
    }

    /// <summary>Returns whether a language tag matches a language range under RFC 4647 basic filtering.</summary>
    /// <param name="tag">The language tag's UTF-8 bytes.</param>
    /// <param name="range">The language range's UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when the tag matches the range.</returns>
    private static bool LanguageRangeMatches(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> range)
    {
        if(range.Length == 1 && range[0] == (byte)'*')
        {
            return tag.Length > 0;
        }

        if(tag.Length < range.Length || !Ascii.EqualsIgnoreCase(tag[..range.Length], range))
        {
            return false;
        }

        //A match is the whole tag or a prefix ending at a subtag boundary ('-'), so "de" matches "de-DE" but "den".
        return tag.Length == range.Length || tag[range.Length] == (byte)'-';
    }

    /// <summary>
    /// Evaluates <c>CONCAT</c> (§17.4.3.4): the concatenation of the string arguments' lexical forms. The result
    /// keeps a common language tag <em>and</em> base direction when every argument shares the same (tag, direction)
    /// pair (RDF 1.2 — a left-to-right concatenation of <c>"a"@en--ltr</c> and <c>"b"@en--ltr</c> stays
    /// <c>rdf:dirLangString</c>); a differing direction (or a plain/typed-string argument) drops the result to a
    /// plain <c>xsd:string</c>. A non-string argument errs.
    /// </summary>
    /// <param name="arguments">The string arguments.</param>
    /// <returns>The concatenated string value, or an error.</returns>
    private static ExpressionValue Concat(ExpressionValue[] arguments)
    {
        ArrayBufferWriter<byte> buffer = new();
        Utf8String? commonLanguage = null;
        TextDirection? commonDirection = null;
        bool tagConsistent = arguments.Length > 0;
        for(int i = 0; i < arguments.Length; i++)
        {
            if(!TryGetStringArgument(arguments[i], out Utf8String text, out Utf8String? language, out TextDirection? direction))
            {
                return ExpressionValue.Error;
            }

            buffer.Write(text.Span);
            if(i == 0)
            {
                commonLanguage = language;
                commonDirection = direction;
            }
            else if(!SameLanguage(language, commonLanguage) || direction != commonDirection)
            {
                tagConsistent = false;
            }
        }

        return tagConsistent
            ? StringLiteral(new Utf8String(buffer.WrittenSpan.ToArray()), commonLanguage, commonDirection)
            : StringLiteral(new Utf8String(buffer.WrittenSpan.ToArray()));
    }

    /// <summary>
    /// Evaluates <c>SUBSTR</c> (§17.4.3.3): the code-point substring of the source from a 1-based start for an
    /// optional length, with the XPath rounding/clamping of <c>fn:substring</c>. The result keeps the source's
    /// language tag; a non-string source or non-numeric index errs.
    /// </summary>
    /// <param name="arguments">The arguments: source string, 1-based start, optional length.</param>
    /// <returns>The substring value, or an error.</returns>
    private static ExpressionValue Substr(ExpressionValue[] arguments)
    {
        if(!TryGetStringArgument(arguments[0], out Utf8String source, out Utf8String? language)
            || arguments[1].IsError || !TryGetDouble(arguments[1].Term, out double startValue))
        {
            return ExpressionValue.Error;
        }

        int start = (int)Math.Round(startValue, MidpointRounding.AwayFromZero);
        int? upperExclusive = null;
        if(arguments.Length >= 3)
        {
            if(arguments[2].IsError || !TryGetDouble(arguments[2].Term, out double lengthValue))
            {
                return ExpressionValue.Error;
            }

            upperExclusive = start + (int)Math.Round(lengthValue, MidpointRounding.AwayFromZero);
        }

        return StringLiteral(SliceByCodePoint(source, start, upperExclusive), language);
    }

    /// <summary>Returns the code points of a UTF-8 string whose 1-based positions fall in <c>[from, upperExclusive)</c> (the whole tail when <paramref name="upperExclusive"/> is <see langword="null"/>).</summary>
    /// <param name="source">The source UTF-8 string.</param>
    /// <param name="from">The 1-based start position (inclusive).</param>
    /// <param name="upperExclusive">The 1-based end position (exclusive), or <see langword="null"/> for the whole tail.</param>
    /// <returns>The selected substring as UTF-8.</returns>
    private static Utf8String SliceByCodePoint(Utf8String source, int from, int? upperExclusive)
    {
        ReadOnlySpan<byte> span = source.Span;
        ArrayBufferWriter<byte> buffer = new();
        int position = 0;
        int index = 0;
        while(index < span.Length)
        {
            int consumed = Rune.DecodeFromUtf8(span[index..], out _, out int width) == OperationStatus.Done ? width : 1;
            position++;
            if(position >= from && (upperExclusive is not int upper || position < upper))
            {
                buffer.Write(span.Slice(index, consumed));
            }

            index += consumed;
        }

        return new Utf8String(buffer.WrittenSpan.ToArray());
    }

    /// <summary>
    /// Evaluates <c>STRBEFORE</c>/<c>STRAFTER</c> (§17.4.3.8/9): the part of the first argument before (or after)
    /// the first occurrence of the second. The result keeps the first argument's language tag; a not-found match
    /// yields the empty <c>xsd:string</c>; incompatible argument languages (the matcher tagged differently from
    /// the source) err.
    /// </summary>
    /// <param name="arguments">The arguments: the source string and the substring to locate.</param>
    /// <param name="before">Whether to return the part before the match (otherwise after).</param>
    /// <returns>The resulting string value, or an error.</returns>
    private static ExpressionValue StrBeforeAfter(ExpressionValue[] arguments, bool before)
    {
        if(!TryGetStringArgument(arguments[0], out Utf8String source, out Utf8String? language)
            || !TryGetStringArgument(arguments[1], out Utf8String needle, out Utf8String? needleLanguage)
            || !Compatible(language, needleLanguage))
        {
            return ExpressionValue.Error;
        }

        //An empty needle matches at the start: STRBEFORE yields "" (with the source's tag), STRAFTER yields the whole source.
        if(needle.Span.Length == 0)
        {
            return before ? StringLiteral(EmptyText, language) : StringLiteral(source, language);
        }

        int index = source.Span.IndexOf(needle.Span);
        if(index < 0)
        {
            return StringLiteral(EmptyText);
        }

        ReadOnlySpan<byte> part = before ? source.Span[..index] : source.Span[(index + needle.Span.Length)..];

        return StringLiteral(new Utf8String(part.ToArray()), language);
    }

    /// <summary>
    /// Evaluates <c>ENCODE_FOR_URI</c> (§17.4.3.5): percent-encodes a string's UTF-8 bytes, leaving the unreserved
    /// characters (<c>ALPHA</c>/<c>DIGIT</c>/<c>-</c>/<c>_</c>/<c>.</c>/<c>~</c>) intact. The result is an
    /// <c>xsd:string</c>; a non-string argument errs.
    /// </summary>
    /// <param name="argument">The string argument.</param>
    /// <returns>The percent-encoded string value, or an error.</returns>
    private static ExpressionValue EncodeForUri(ExpressionValue argument)
    {
        if(!TryGetLexicalText(argument, out Utf8String text))
        {
            return ExpressionValue.Error;
        }

        ReadOnlySpan<byte> hex = "0123456789ABCDEF"u8;
        ReadOnlySpan<byte> source = text.Span;
        ArrayBufferWriter<byte> buffer = new();
        Span<byte> escape = stackalloc byte[3];
        escape[0] = (byte)'%';
        foreach(byte value in source)
        {
            if(IsUnreservedUriByte(value))
            {
                buffer.Write([value]);

                continue;
            }

            escape[1] = hex[value >> 4];
            escape[2] = hex[value & 0xF];
            buffer.Write(escape);
        }

        return StringLiteral(new Utf8String(buffer.WrittenSpan.ToArray()));
    }

    /// <summary>Returns whether a byte is an unreserved URI character (left un-escaped by <c>ENCODE_FOR_URI</c>): ASCII letter, digit, or one of <c>- _ . ~</c>.</summary>
    /// <param name="value">The byte to classify.</param>
    /// <returns><see langword="true"/> when the byte is unreserved.</returns>
    private static bool IsUnreservedUriByte(byte value)
    {
        return value is (>= (byte)'A' and <= (byte)'Z')
            or (>= (byte)'a' and <= (byte)'z')
            or (>= (byte)'0' and <= (byte)'9')
            or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~';
    }

    /// <summary>
    /// Evaluates <c>REGEX</c> (§17.4.3.14): whether the text matches the pattern under the optional flags. The
    /// pattern is compiled non-backtracking (ReDoS-safe, mirroring the SHACL pattern constraint); an unparseable
    /// or unsupported pattern, an unknown flag, or a non-string argument errs.
    /// </summary>
    /// <param name="arguments">The arguments: the text, the pattern, and an optional flag string.</param>
    /// <param name="context">The expression context, carrying the regular-expression seam.</param>
    /// <returns>A boolean value, or an error.</returns>
    private static ExpressionValue RegexMatch(ExpressionValue[] arguments, SparqlExpressionContext context)
    {
        if(!TryGetStringArgument(arguments[0], out Utf8String text, out _)
            || !TryGetStringArgument(arguments[1], out Utf8String pattern, out _)
            || !TryGetOptionalFlags(arguments, 2, out string? flags)
            || context.RegexResolver(pattern.ToString(), flags) is not Regex regex)
        {
            return ExpressionValue.Error;
        }

        return Boolean(regex.IsMatch(text.ToString()));
    }

    /// <summary>
    /// Evaluates <c>REPLACE</c> (§17.4.3.15): replaces every match of the pattern in the source with the
    /// replacement (with <c>$N</c> group references), keeping the source's language tag. An unparseable or
    /// unsupported pattern, an unknown flag, or a non-string argument errs.
    /// </summary>
    /// <param name="arguments">The arguments: the source, the pattern, the replacement, and an optional flag string.</param>
    /// <param name="context">The expression context, carrying the regular-expression seam.</param>
    /// <returns>The replaced string value, or an error.</returns>
    private static ExpressionValue Replace(ExpressionValue[] arguments, SparqlExpressionContext context)
    {
        if(!TryGetStringArgument(arguments[0], out Utf8String source, out Utf8String? language)
            || !TryGetStringArgument(arguments[1], out Utf8String pattern, out _)
            || !TryGetStringArgument(arguments[2], out Utf8String replacement, out _)
            || !TryGetOptionalFlags(arguments, 3, out string? flags)
            || context.RegexResolver(pattern.ToString(), flags) is not Regex regex)
        {
            return ExpressionValue.Error;
        }

        try
        {
            return StringLiteral(Utf8Strings.From(regex.Replace(source.ToString(), replacement.ToString())), language);
        }
        catch(ArgumentException)
        {
            //A malformed replacement (e.g. a bad $-group reference) is a value error, not a crash.
            return ExpressionValue.Error;
        }
    }

    /// <summary>Reads an optional trailing flag-string argument at <paramref name="index"/>, if present.</summary>
    /// <param name="arguments">The argument values.</param>
    /// <param name="index">The position of the optional flag argument.</param>
    /// <param name="flags">Receives the flag string, or <see langword="null"/> when absent.</param>
    /// <returns><see langword="true"/> when the flag argument is absent or a valid string; <see langword="false"/> when present but not a string.</returns>
    private static bool TryGetOptionalFlags(ExpressionValue[] arguments, int index, out string? flags)
    {
        if(arguments.Length <= index)
        {
            flags = null;

            return true;
        }

        if(TryGetStringArgument(arguments[index], out Utf8String flagText, out _))
        {
            flags = flagText.ToString();

            return true;
        }

        flags = null;

        return false;
    }


    /// <summary>
    /// Evaluates <c>IRI</c>/<c>URI</c> (§17.4.2.8): an IRI argument passes through; a plain-string argument becomes
    /// an IRI, resolving a relative reference against the query's base IRI (<see cref="SparqlExpressionContext.BaseIri"/>)
    /// per RFC 3986. A relative reference with no base in scope, a blank node, a tagged/typed literal, or an error
    /// argument errs.
    /// </summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="context">The evaluation context supplying the query's base IRI.</param>
    /// <returns>The IRI value, or an error.</returns>
    private static ExpressionValue Iri(ExpressionValue argument, SparqlExpressionContext context)
    {
        if(argument.IsError)
        {
            return ExpressionValue.Error;
        }

        return argument.Term switch
        {
            NamedNode named => ExpressionValue.Of(named),
            Literal literal when literal.Language is null && literal.Datatype.Iri == Vocabulary.Xsd.String => ResolveIri(literal.Value, context.BaseIri),
            _ => ExpressionValue.Error
        };
    }

    /// <summary>Builds the IRI value of an <c>IRI</c>/<c>URI</c> string argument: an absolute reference unchanged, a relative reference resolved against <paramref name="baseIri"/> (an error when no base is in scope).</summary>
    /// <param name="reference">The IRI reference's lexical form.</param>
    /// <param name="baseIri">The query's base IRI, or <see langword="null"/> when none is in scope.</param>
    /// <returns>The IRI value, or an error.</returns>
    private static ExpressionValue ResolveIri(Utf8String reference, Utf8String? baseIri)
    {
        if(IriResolver.IsAbsoluteIri(reference.Span))
        {
            return ExpressionValue.Of(new NamedNode(reference));
        }

        if(baseIri is not Utf8String @base)
        {
            //A relative IRI with no base to resolve against is an error per §17.4.2.8.
            return ExpressionValue.Error;
        }

        IriBase parsedBase = IriResolver.ParseBase(@base);

        return ExpressionValue.Of(new NamedNode(IriResolver.ResolveIri(in parsedBase, reference)));
    }

    /// <summary>
    /// Evaluates <c>STRDT</c> (§17.4.2.9): builds a literal from a simple-string lexical form and an IRI datatype.
    /// A non-simple-string first argument or a non-IRI datatype errs.
    /// </summary>
    /// <param name="arguments">The arguments: the lexical form (a simple string) and the datatype IRI.</param>
    /// <returns>The typed-literal value, or an error.</returns>
    private static ExpressionValue StrDt(ExpressionValue[] arguments)
    {
        if(arguments[0].IsError || arguments[1].IsError
            || arguments[0].Term is not Literal lexical || lexical.Language is not null || lexical.Datatype.Iri != Vocabulary.Xsd.String
            || arguments[1].Term is not NamedNode datatype)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Literal(lexical.Value, datatype));
    }

    /// <summary>
    /// Evaluates <c>STRLANG</c> (§17.4.2.10): builds a language-tagged literal from a simple-string lexical form
    /// and a simple-string, non-empty language tag. A non-simple-string argument or an empty language tag errs (RDF
    /// has no empty language tag).
    /// </summary>
    /// <param name="arguments">The arguments: the lexical form and the language tag (both simple strings).</param>
    /// <returns>The language-tagged-literal value, or an error.</returns>
    private static ExpressionValue StrLang(ExpressionValue[] arguments)
    {
        if(arguments[0].IsError || arguments[1].IsError
            || arguments[0].Term is not Literal lexical || lexical.Language is not null || lexical.Datatype.Iri != Vocabulary.Xsd.String
            || arguments[1].Term is not Literal tag || tag.Language is not null || tag.Datatype.Iri != Vocabulary.Xsd.String || tag.Value.Span.Length == 0)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Literal(lexical.Value, new NamedNode(Vocabulary.Rdf.LangString), tag.Value));
    }

    /// <summary>
    /// Evaluates <c>STRLANGDIR</c> (RDF 1.2): builds a directional language-tagged literal
    /// (<c>rdf:dirLangString</c>) from a simple-string lexical form, a non-empty simple-string language tag, and a
    /// base direction that must be exactly <c>"ltr"</c> or <c>"rtl"</c> (lower-case). A non-simple-string argument,
    /// an empty language tag, or an unrecognised direction errs.
    /// </summary>
    /// <param name="arguments">The arguments: the lexical form, the language tag, and the base direction (all simple strings).</param>
    /// <returns>The directional language-tagged-literal value, or an error.</returns>
    private static ExpressionValue StrLangDir(ExpressionValue[] arguments)
    {
        if(arguments[0].IsError || arguments[1].IsError || arguments[2].IsError
            || arguments[0].Term is not Literal lexical || lexical.Language is not null || lexical.Datatype.Iri != Vocabulary.Xsd.String
            || arguments[1].Term is not Literal tag || tag.Language is not null || tag.Datatype.Iri != Vocabulary.Xsd.String || tag.Value.Span.Length == 0
            || arguments[2].Term is not Literal direction || direction.Language is not null || direction.Datatype.Iri != Vocabulary.Xsd.String
            || !TextDirections.TryParse(direction.Value.Span, out TextDirection baseDirection))
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Literal(lexical.Value, new NamedNode(Vocabulary.Rdf.DirLangString), tag.Value, baseDirection));
    }

    /// <summary>Returns the <c>LANGDIR</c> string form of an optional base direction: the canonical <c>"ltr"</c>/<c>"rtl"</c> token, or the empty string when absent.</summary>
    /// <param name="direction">The base direction, or <see langword="null"/>.</param>
    /// <returns>The direction's string form.</returns>
    private static Utf8String DirectionText(TextDirection? direction)
    {
        return direction is TextDirection baseDirection ? TextDirections.ToToken(baseDirection) : EmptyText;
    }

    /// <summary>
    /// Evaluates a hash function (<c>MD5</c>/<c>SHA1</c>/<c>SHA256</c>/<c>SHA384</c>/<c>SHA512</c>, §17.4): the
    /// lowercase hex digest of the argument's UTF-8 lexical form, computed through the swappable digest seam. A
    /// non-simple-string argument errs.
    /// </summary>
    /// <param name="argument">The simple-string argument to digest.</param>
    /// <param name="algorithm">The digest algorithm.</param>
    /// <param name="context">The context carrying the digest seam.</param>
    /// <returns>The lowercase hex digest as an <c>xsd:string</c>, or an error.</returns>
    private static ExpressionValue Hash(ExpressionValue argument, SparqlHashAlgorithm algorithm, SparqlExpressionContext context)
    {
        if(argument.IsError || argument.Term is not Literal literal || literal.Language is not null || literal.Datatype.Iri != Vocabulary.Xsd.String)
        {
            return ExpressionValue.Error;
        }

        return StringLiteral(Utf8Strings.From(Convert.ToHexStringLower(context.Hash(algorithm, literal.Value.Span))));
    }

    /// <summary>Selects an integer component of a date-time value, reading the field in the value's own timezone.</summary>
    /// <param name="value">The date-time value.</param>
    /// <returns>The selected integer component.</returns>
    private delegate int DateTimeComponentSelector(DateTimeOffset value);

    /// <summary>Evaluates a date-time accessor (<c>YEAR</c>/<c>MONTH</c>/<c>DAY</c>/<c>HOURS</c>/<c>MINUTES</c>, §17.4.5) that returns an integer component of an <c>xsd:dateTime</c>; a non-dateTime argument errs.</summary>
    /// <param name="argument">The <c>xsd:dateTime</c> argument.</param>
    /// <param name="component">The component selector, reading the field in the value's own timezone.</param>
    /// <returns>The component as an <c>xsd:integer</c>, or an error.</returns>
    private static ExpressionValue DateComponent(ExpressionValue argument, DateTimeComponentSelector component)
    {
        return TryGetDateTime(argument, out DateTimeOffset value, out _) ? Integer(component(value)) : ExpressionValue.Error;
    }

    /// <summary>Evaluates <c>SECONDS</c> (§17.4.5.6): the seconds component of an <c>xsd:dateTime</c> as an <c>xsd:decimal</c> (including a fractional part); a non-dateTime argument errs.</summary>
    /// <param name="argument">The <c>xsd:dateTime</c> argument.</param>
    /// <returns>The seconds as an <c>xsd:decimal</c>, or an error.</returns>
    private static ExpressionValue Seconds(ExpressionValue argument)
    {
        if(!TryGetDateTime(argument, out DateTimeOffset value, out _))
        {
            return ExpressionValue.Error;
        }

        decimal seconds = value.Second + (value.Millisecond / 1000m);
        string lexical = seconds.ToString(value.Millisecond == 0 ? "0" : "0.###", CultureInfo.InvariantCulture);

        return ExpressionValue.Of(new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.Decimal)));
    }

    /// <summary>Evaluates <c>TIMEZONE</c> (§17.4.5.7): the timezone of an <c>xsd:dateTime</c> as an <c>xsd:dayTimeDuration</c>; a value with no timezone (or a non-dateTime argument) errs.</summary>
    /// <param name="argument">The <c>xsd:dateTime</c> argument.</param>
    /// <returns>The timezone as an <c>xsd:dayTimeDuration</c>, or an error.</returns>
    private static ExpressionValue Timezone(ExpressionValue argument)
    {
        if(!TryGetDateTime(argument, out DateTimeOffset value, out bool hasTimezone) || !hasTimezone)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Literal(Utf8Strings.From(FormatDayTimeDuration(value.Offset)), new NamedNode(Vocabulary.Xsd.DayTimeDuration)));
    }

    /// <summary>Evaluates <c>TZ</c> (§17.4.5.8): the timezone of an <c>xsd:dateTime</c> as a simple literal (<c>"Z"</c>, <c>"±hh:mm"</c>, or <c>""</c> when there is none); a non-dateTime argument errs.</summary>
    /// <param name="argument">The <c>xsd:dateTime</c> argument.</param>
    /// <returns>The timezone string, or an error.</returns>
    private static ExpressionValue Tz(ExpressionValue argument)
    {
        if(!TryGetDateTime(argument, out DateTimeOffset value, out bool hasTimezone))
        {
            return ExpressionValue.Error;
        }

        if(!hasTimezone)
        {
            return StringLiteral(EmptyText);
        }

        return StringLiteral(Utf8Strings.From(value.Offset == TimeSpan.Zero ? "Z" : FormatOffset(value.Offset)));
    }

    /// <summary>Parses an <c>xsd:dateTime</c> argument, reporting whether its lexical form carried a timezone (a tz-less value is parsed as UTC for component extraction).</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="value">Receives the parsed value on success.</param>
    /// <param name="hasTimezone">Receives whether the lexical form carried a timezone designator.</param>
    /// <returns><see langword="true"/> when the argument is a parseable <c>xsd:dateTime</c> literal.</returns>
    private static bool TryGetDateTime(ExpressionValue argument, out DateTimeOffset value, out bool hasTimezone)
    {
        value = default;
        hasTimezone = false;
        if(argument.IsError || argument.Term is not Literal literal || literal.Datatype.Iri != Vocabulary.Xsd.DateTime)
        {
            return false;
        }

        string lexical = literal.Value.ToString();

        //The timezone designator (Z or ±hh:mm) is in the time part, after 'T' — the date part's '-' separators
        //must not be mistaken for a negative offset.
        int timeIndex = lexical.IndexOf('T', StringComparison.Ordinal);
        ReadOnlySpan<char> timePart = timeIndex >= 0 ? lexical.AsSpan(timeIndex + 1) : lexical.AsSpan();
        hasTimezone = timePart.EndsWith("Z") || timePart.Contains('+') || timePart.Contains('-');

        return DateTimeOffset.TryParse(lexical, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    /// <summary>Formats a timezone offset as an <c>xsd:dayTimeDuration</c> (<c>PT0S</c> for UTC, e.g. <c>-PT5H</c>).</summary>
    /// <param name="offset">The timezone offset.</param>
    /// <returns>The duration lexical form.</returns>
    private static string FormatDayTimeDuration(TimeSpan offset)
    {
        if(offset == TimeSpan.Zero)
        {
            return "PT0S";
        }

        TimeSpan magnitude = offset.Duration();
        StringBuilder builder = new();
        builder.Append(offset < TimeSpan.Zero ? "-PT" : "PT");
        if(magnitude.Hours > 0)
        {
            builder.Append(magnitude.Hours).Append('H');
        }

        if(magnitude.Minutes > 0)
        {
            builder.Append(magnitude.Minutes).Append('M');
        }

        return builder.ToString();
    }

    /// <summary>Formats a timezone offset as the <c>±hh:mm</c> designator <c>TZ</c> returns.</summary>
    /// <param name="offset">The timezone offset.</param>
    /// <returns>The offset designator.</returns>
    private static string FormatOffset(TimeSpan offset)
    {
        TimeSpan magnitude = offset.Duration();

        return string.Create(CultureInfo.InvariantCulture, $"{(offset < TimeSpan.Zero ? '-' : '+')}{magnitude.Hours:D2}:{magnitude.Minutes:D2}");
    }

    /// <summary>
    /// Evaluates <c>TRIPLE</c> (RDF 1.2): builds an RDF triple term from a subject, predicate, and object. The
    /// subject must be an IRI or a blank node (not a literal and not itself a triple term), and the predicate must
    /// be an IRI; the object may be any term (including a triple term). Any other shape errs (RDF 1.2 Concepts §3.5
    /// — a triple term's subject is an IRI or blank node, its predicate an IRI, its object any term).
    /// </summary>
    /// <param name="arguments">The arguments: subject, predicate, object.</param>
    /// <returns>The triple-term value, or an error.</returns>
    private static ExpressionValue BuildTriple(ExpressionValue[] arguments)
    {
        if(arguments[0].IsError || arguments[1].IsError || arguments[2].IsError
            || arguments[0].Term is not (NamedNode or BlankNode)
            || arguments[1].Term is not NamedNode predicate)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Lumoin.Veritas.Core.TripleTerm(arguments[0].Term, predicate, arguments[2].Term));
    }

    /// <summary>Selects a component (subject, predicate, or object) of a triple term.</summary>
    /// <param name="triple">The triple term.</param>
    /// <returns>The selected component term.</returns>
    private delegate RdfTerm TripleComponentSelector(Lumoin.Veritas.Core.TripleTerm triple);

    /// <summary>Evaluates <c>SUBJECT</c>/<c>PREDICATE</c>/<c>OBJECT</c> (RDF 1.2): extracts a component of a triple term; a non-triple-term argument errs.</summary>
    /// <param name="argument">The argument value (expected to be a triple term).</param>
    /// <param name="component">The component selector.</param>
    /// <returns>The selected component, or an error.</returns>
    private static ExpressionValue TripleComponent(ExpressionValue argument, TripleComponentSelector component)
    {
        return !argument.IsError && argument.Term is Lumoin.Veritas.Core.TripleTerm triple ? ExpressionValue.Of(component(triple)) : ExpressionValue.Error;
    }

    /// <summary>Extracts the UTF-8 lexical form and language tag of a string-valued argument (<c>xsd:string</c> or language-tagged); fails for an error or a non-string term.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="text">Receives the UTF-8 lexical form on success.</param>
    /// <param name="language">Receives the language tag (or <see langword="null"/> for a plain string) on success.</param>
    /// <returns><see langword="true"/> when the argument is a string-valued literal.</returns>
    private static bool TryGetStringArgument(ExpressionValue argument, out Utf8String text, out Utf8String? language)
    {
        return TryGetStringArgument(argument, out text, out language, out _);
    }

    /// <summary>Extracts the UTF-8 lexical form, language tag, and base direction of a string-valued argument (<c>xsd:string</c>, language-tagged, or directional language-tagged); fails for an error or a non-string term.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="text">Receives the UTF-8 lexical form on success.</param>
    /// <param name="language">Receives the language tag (or <see langword="null"/> for a plain string) on success.</param>
    /// <param name="direction">Receives the base direction (or <see langword="null"/> for a non-directional string) on success.</param>
    /// <returns><see langword="true"/> when the argument is a string-valued literal.</returns>
    private static bool TryGetStringArgument(ExpressionValue argument, out Utf8String text, out Utf8String? language, out TextDirection? direction)
    {
        if(!argument.IsError && argument.Term is Literal literal && (literal.Datatype.Iri == Vocabulary.Xsd.String || literal.Language is not null))
        {
            text = literal.Value;
            language = literal.Language;
            direction = literal.BaseDirection;

            return true;
        }

        text = EmptyText;
        language = null;
        direction = null;

        return false;
    }

    /// <summary>Builds a string literal value: a language-tagged literal when <paramref name="language"/> is present, otherwise an <c>xsd:string</c>.</summary>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <param name="language">The language tag, or <see langword="null"/> for a plain string.</param>
    /// <returns>The string-literal value.</returns>
    private static ExpressionValue StringLiteral(Utf8String value, Utf8String? language)
    {
        return language is Utf8String tag
            ? ExpressionValue.Of(new Literal(value, new NamedNode(Vocabulary.Rdf.LangString), tag))
            : StringLiteral(value);
    }

    /// <summary>Builds a string literal value: a directional language-tagged literal (<c>rdf:dirLangString</c>) when both a language tag and a base direction are present, a plain language-tagged literal when only the tag is, otherwise an <c>xsd:string</c>.</summary>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <param name="language">The language tag, or <see langword="null"/> for a plain string.</param>
    /// <param name="direction">The base direction, or <see langword="null"/> for a non-directional string.</param>
    /// <returns>The string-literal value.</returns>
    private static ExpressionValue StringLiteral(Utf8String value, Utf8String? language, TextDirection? direction)
    {
        return language is Utf8String tag && direction is TextDirection baseDirection
            ? ExpressionValue.Of(new Literal(value, new NamedNode(Vocabulary.Rdf.DirLangString), tag, baseDirection))
            : StringLiteral(value, language);
    }

    /// <summary>Returns whether two optional language tags are the same (both absent, or both present and byte-equal).</summary>
    /// <param name="left">The first language tag, or <see langword="null"/>.</param>
    /// <param name="right">The second language tag, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the tags match.</returns>
    private static bool SameLanguage(Utf8String? left, Utf8String? right)
    {
        return left is Utf8String l && right is Utf8String r ? l == r : left is null && right is null;
    }

    /// <summary>Returns whether a matcher argument's language is compatible with a source's (§17.4.3): a plain matcher always is; a tagged matcher requires the same tag on the source.</summary>
    /// <param name="sourceLanguage">The source argument's language tag, or <see langword="null"/>.</param>
    /// <param name="matchLanguage">The matcher argument's language tag, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the arguments are compatible.</returns>
    private static bool Compatible(Utf8String? sourceLanguage, Utf8String? matchLanguage)
    {
        return matchLanguage is null || SameLanguage(sourceLanguage, matchLanguage);
    }

    /// <summary>The unary numeric functions (§17.4.4) that preserve their operand's numeric kind.</summary>
    private enum UnaryNumericOp
    {
        /// <summary><c>ABS</c> — absolute value.</summary>
        Abs,

        /// <summary><c>CEIL</c> — round toward +∞.</summary>
        Ceil,

        /// <summary><c>FLOOR</c> — round toward −∞.</summary>
        Floor,

        /// <summary><c>ROUND</c> — round half away from zero.</summary>
        Round
    }

    /// <summary>
    /// Applies a unary numeric function (<c>ABS</c>/<c>CEIL</c>/<c>FLOOR</c>/<c>ROUND</c>, §17.4.4) to a numeric
    /// argument, preserving the operand's XSD kind (an integer stays integer, a decimal stays decimal, …) per the
    /// XPath <c>op:numeric-*</c> signatures; a non-numeric argument errs.
    /// </summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="operation">The unary numeric function to apply.</param>
    /// <returns>The numeric result, in the operand's kind, or an error.</returns>
    private static ExpressionValue UnaryNumeric(ExpressionValue argument, UnaryNumericOp operation)
    {
        if(argument.IsError || !TryGetNumericValue(argument.Term, out NumericValue value))
        {
            return ExpressionValue.Error;
        }

        //ABS over a negative integer/decimal stays exact; CEIL/FLOOR/ROUND are identities on an integer (already
        //whole) and round the decimal/float/double in their own kind. ROUND is half-away-from-zero (XPath fn:round
        //rounds half toward +∞, but the prior behaviour and the W3C result fixtures use away-from-zero).
        NumericValue result = value.Kind switch
        {
            NumericKind.Integer => operation == UnaryNumericOp.Abs ? new NumericValue(BigInteger.Abs(value.AsInteger())) : value,
            NumericKind.Decimal => new NumericValue(ApplyDecimal(operation, value.AsDecimal())),
            NumericKind.Float => new NumericValue(ApplyFloat(operation, value.AsFloat())),
            _ => new NumericValue(ApplyDouble(operation, value.AsDouble()))
        };

        return Numeric(result);
    }

    /// <summary>Applies a unary numeric function in the <see cref="decimal"/> kind.</summary>
    /// <param name="operation">The function.</param>
    /// <param name="value">The decimal operand.</param>
    /// <returns>The decimal result.</returns>
    private static decimal ApplyDecimal(UnaryNumericOp operation, decimal value)
    {
        return operation switch
        {
            UnaryNumericOp.Abs => Math.Abs(value),
            UnaryNumericOp.Ceil => Math.Ceiling(value),
            UnaryNumericOp.Floor => Math.Floor(value),
            _ => Math.Round(value, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>Applies a unary numeric function in the <see cref="float"/> kind.</summary>
    /// <param name="operation">The function.</param>
    /// <param name="value">The float operand.</param>
    /// <returns>The float result.</returns>
    private static float ApplyFloat(UnaryNumericOp operation, float value)
    {
        return operation switch
        {
            UnaryNumericOp.Abs => MathF.Abs(value),
            UnaryNumericOp.Ceil => MathF.Ceiling(value),
            UnaryNumericOp.Floor => MathF.Floor(value),
            _ => MathF.Round(value, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>Applies a unary numeric function in the <see cref="double"/> kind.</summary>
    /// <param name="operation">The function.</param>
    /// <param name="value">The double operand.</param>
    /// <returns>The double result.</returns>
    private static double ApplyDouble(UnaryNumericOp operation, double value)
    {
        return operation switch
        {
            UnaryNumericOp.Abs => Math.Abs(value),
            UnaryNumericOp.Ceil => Math.Ceiling(value),
            UnaryNumericOp.Floor => Math.Floor(value),
            _ => Math.Round(value, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>Builds an <c>xsd:string</c> literal value from a UTF-8 lexical form (no <see cref="string"/> round-trip).</summary>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <returns>The string-literal value.</returns>
    private static ExpressionValue StringLiteral(Utf8String value)
    {
        return ExpressionValue.Of(new Literal(value, new NamedNode(Vocabulary.Xsd.String)));
    }

    /// <summary>Extracts the UTF-8 lexical form of an argument that is an <c>xsd:string</c> or a language-tagged literal; fails for an error or a non-string term.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="value">Receives the UTF-8 lexical form on success.</param>
    /// <returns><see langword="true"/> when the argument is a string-valued literal.</returns>
    private static bool TryGetLexicalText(ExpressionValue argument, out Utf8String value)
    {
        if(!argument.IsError && argument.Term is Literal literal && (literal.Datatype.Iri == Vocabulary.Xsd.String || literal.Language is not null))
        {
            value = literal.Value;

            return true;
        }

        value = EmptyText;

        return false;
    }

    /// <summary>
    /// Maps a UTF-8 string to upper or lower case rune-by-rune (§17.4.4.3/4 <c>LCASE</c>/<c>UCASE</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why rune-by-rune over UTF-8, and why no culture.</b> SPARQL defines <c>LCASE</c>/<c>UCASE</c> by
    /// reference to the XPath/XQuery functions <c>fn:lower-case</c>/<c>fn:upper-case</c>, which apply the Unicode
    /// <em>default</em> case mappings and are explicitly <em>not</em> locale-sensitive — there is no collation or
    /// culture parameter. <see cref="Rune.ToUpperInvariant(Rune)"/>/<see cref="Rune.ToLowerInvariant(Rune)"/> are
    /// exactly that: the invariant, per-codepoint Unicode mapping. Working over runes (a) stays in UTF-8 with no
    /// <see cref="string"/> (UTF-16) round-trip, matching the project's <c>Utf8String</c> discipline, and (b)
    /// avoids <c>string.ToLowerInvariant</c> — so CA1308 (which guards lower-casing for <em>normalization</em>,
    /// a different concern) never applies and needs no suppression. A case mapping can change the byte length
    /// (e.g. U+212A KELVIN SIGN → U+006B 'k'), so the result is accumulated rather than written in place.
    /// </para>
    /// </remarks>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <param name="toUpper">Whether to upper-case (otherwise lower-case).</param>
    /// <returns>The cased UTF-8 lexical form.</returns>
    private static Utf8String MapCase(Utf8String value, bool toUpper)
    {
        ReadOnlySpan<byte> source = value.Span;
        List<byte> mapped = new(source.Length);
        Span<byte> encoded = stackalloc byte[4];
        int index = 0;
        while(index < source.Length)
        {
            if(Rune.DecodeFromUtf8(source[index..], out Rune rune, out int consumed) != OperationStatus.Done)
            {
                //Ill-formed UTF-8 (not expected in a well-formed literal): pass the byte through and advance.
                mapped.Add(source[index]);
                index++;

                continue;
            }

            Rune cased = toUpper ? Rune.ToUpperInvariant(rune) : Rune.ToLowerInvariant(rune);
            int written = cased.EncodeToUtf8(encoded);
            for(int i = 0; i < written; i++)
            {
                mapped.Add(encoded[i]);
            }

            index += consumed;
        }

        return new Utf8String(mapped.ToArray());
    }

    /// <summary>Counts the Unicode code points (runes) in a UTF-8 string — the character count <c>STRLEN</c> returns, not the byte length.</summary>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <returns>The number of code points.</returns>
    private static int CodePointCount(Utf8String value)
    {
        ReadOnlySpan<byte> source = value.Span;
        int count = 0;
        int index = 0;
        while(index < source.Length)
        {
            index += Rune.DecodeFromUtf8(source[index..], out _, out int consumed) == OperationStatus.Done ? consumed : 1;
            count++;
        }

        return count;
    }

    /// <summary>Reduces a value to its effective boolean value (§17.2.2): a boolean, or an error when the value has none.</summary>
    /// <param name="value">The value to reduce.</param>
    /// <returns>A boolean literal value, or an error.</returns>
    private static ExpressionValue EffectiveBooleanValue(ExpressionValue value)
    {
        if(value.IsError)
        {
            return ExpressionValue.Error;
        }

        if(TryGetBoolean(value.Term, out bool boolean))
        {
            return Boolean(boolean);
        }

        if(TryGetDouble(value.Term, out double number))
        {
            return Boolean(number != 0 && !double.IsNaN(number));
        }

        if(TryGetText(value.Term, out Utf8String text))
        {
            return Boolean(!text.IsEmpty);
        }

        return ExpressionValue.Error;
    }

    /// <summary>Reduces a value to a three-state effective boolean: <see langword="true"/>, <see langword="false"/>, or <see langword="null"/> for an error.</summary>
    /// <param name="value">The value to reduce.</param>
    /// <returns>The three-state effective boolean.</returns>
    private static bool? EffectiveState(ExpressionValue value)
    {
        ExpressionValue effective = EffectiveBooleanValue(value);
        if(effective.IsError)
        {
            return null;
        }

        return ReferenceEquals(effective.Term, True);
    }

    /// <summary>Applies the §17.2 three-valued <c>&amp;&amp;</c> truth table over two effective-boolean states.</summary>
    /// <param name="left">The left operand's effective boolean state (<see langword="null"/> = error).</param>
    /// <param name="right">The right operand's effective boolean state (<see langword="null"/> = error).</param>
    /// <returns>The conjunction: false when either is false, true when both are true, otherwise an error.</returns>
    private static ExpressionValue LogicalAnd(bool? left, bool? right)
    {
        if(left == false || right == false)
        {
            return False;
        }

        if(left == true && right == true)
        {
            return True;
        }

        return ExpressionValue.Error;
    }

    /// <summary>Applies the §17.2 three-valued <c>||</c> truth table over two effective-boolean states.</summary>
    /// <param name="left">The left operand's effective boolean state (<see langword="null"/> = error).</param>
    /// <param name="right">The right operand's effective boolean state (<see langword="null"/> = error).</param>
    /// <returns>The disjunction: true when either is true, false when both are false, otherwise an error.</returns>
    private static ExpressionValue LogicalOr(bool? left, bool? right)
    {
        if(left == true || right == true)
        {
            return True;
        }

        if(left == false && right == false)
        {
            return False;
        }

        return ExpressionValue.Error;
    }

    /// <summary>Compares two values under a relational operator (§17.3), choosing numeric, boolean, string, temporal, registered-value, or term-equality semantics by the operands' kinds. The ordering operators on the temporal families run on the implicit-timezone-totalized axis; <c>=</c>/<c>!=</c> on temporal literals keep record equality (a recorded conformance boundary — ordering is what this increment scopes). <c>=</c>/<c>!=</c> on two literals of one registered value-layer datatype consult the registration's declared value equality, an abstention falling through to term identity.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="context">The seams the comparison consumes (the implicit timezone and the value-layer datatype registry).</param>
    /// <returns>A boolean result, or an error when either operand errs or the operands are not comparable under <paramref name="op"/>.</returns>
    private static ExpressionValue Compare(ExpressionValue left, ExpressionValue right, ComparisonOp op, SparqlExpressionContext context)
    {
        if(left.IsError || right.IsError)
        {
            return ExpressionValue.Error;
        }

        RdfTerm a = left.Term;
        RdfTerm b = right.Term;

        if(TryGetNumericValue(a, out NumericValue na) && TryGetNumericValue(b, out NumericValue nb))
        {
            ComparisonResult numericComparison = NumericValue.Compare(na, nb);

            //An incomparable result (a NaN operand) makes an ordering comparison error; equality treats NaN as unequal.
            return numericComparison == ComparisonResult.Incomparable
                ? op is ComparisonOp.Equal ? Boolean(false) : op is ComparisonOp.NotEqual ? Boolean(true) : ExpressionValue.Error
                : Boolean(CompareOrdered(ComparisonSign(numericComparison), op));
        }

        if(TryGetBoolean(a, out bool ba) && TryGetBoolean(b, out bool bb))
        {
            return Boolean(CompareOrdered(ba.CompareTo(bb), op));
        }

        //String comparison is by UTF-8 bytes, which is the Unicode code-point order the XPath codepoint collation
        //(SPARQL's default string ordering) mandates — and which a UTF-16 ordinal compare would get wrong for
        //astral (> U+FFFF) characters. No string allocation, no culture.
        if(TryGetText(a, out Utf8String sa) && TryGetText(b, out Utf8String sb))
        {
            return Boolean(CompareOrdered(sa.CompareTo(sb), op));
        }

        //Temporal ordering (§17.3 op:dateTime-less-than and the sanctioned §17.3.1 date/time extensions): both
        //operands in a temporal family makes the ordering operators value comparisons on the totalized axis. A
        //cross-family pair or an ill-formed operand is a type error. Equality deliberately falls through to the
        //record-equality arm below.
        if(op is not (ComparisonOp.Equal or ComparisonOp.NotEqual)
            && RdfValueComparer.TryCompareTemporal(a, b, context.ImplicitTimezone, out ComparisonResult temporalComparison))
        {
            return temporalComparison == ComparisonResult.Incomparable
                ? ExpressionValue.Error
                : Boolean(CompareOrdered(ComparisonSign(temporalComparison), op));
        }

        //Two triple terms compare for equality by VALUE, component-wise (§17.4.1.x): the subjects, predicates, and
        //objects each compare under these same rules — so <<( :a :b 123 )>> = <<( :a :b 123.0 )>> (integer vs decimal
        //value-equal), even though the terms are not identical. Ordering of triple terms is not defined here.
        if(a is Lumoin.Veritas.Core.TripleTerm ta && b is Lumoin.Veritas.Core.TripleTerm tb)
        {
            bool? equal = TripleTermValueEqual(ta, tb, context);

            return equal is bool result
                ? op switch
                {
                    ComparisonOp.Equal => Boolean(result),
                    ComparisonOp.NotEqual => Boolean(!result),
                    _ => ExpressionValue.Error
                }
                : ExpressionValue.Error;
        }

        //The value-layer consult for = / !=: both operands literals of one registered datatype IRI whose
        //definition declares the value-equality facet — checked here, so the capability declaration is binding
        //at the seam. The reservation gate keeps XSD-namespace, RDF-namespace, and classifier-modelled IRIs
        //unregistrable, so no operand an arm above already decided can reach this consult; an Indeterminate
        //answer falls through, leaving the term-identity semantics below standing.
        if(!context.ValueDatatypes.IsEmpty
            && op is ComparisonOp.Equal or ComparisonOp.NotEqual
            && a is Literal la
            && b is Literal lb
            && la.Datatype.Iri.Equals(lb.Datatype.Iri)
            && context.ValueDatatypes.TryGet(la.Datatype.Iri, out ValueDatatype? registered)
            && (registered.Facets & ValueDatatypeFacets.ValueEquality) != ValueDatatypeFacets.None)
        {
            ValueIdentity identity = registered.SameValue(la.Value, lb.Value);
            if(identity != ValueIdentity.Indeterminate)
            {
                bool same = identity == ValueIdentity.Same;

                return Boolean(op is ComparisonOp.Equal ? same : !same);
            }
        }

        //Outside a common value space only equality is defined, via RDF term identity (sameTerm).
        return op switch
        {
            ComparisonOp.Equal => Boolean(a.Equals(b)),
            ComparisonOp.NotEqual => Boolean(!a.Equals(b)),
            _ => ExpressionValue.Error
        };
    }

    /// <summary>
    /// Compares two triple terms for SPARQL <c>=</c> value equality, component-wise over an explicit stack (no
    /// recursion): predicates compare as IRIs (term identity); subjects and objects compare by value via
    /// <see cref="Compare"/>'s rules (numeric value across the tower, string by code point, nested triple terms
    /// recursively). Returns <see langword="null"/> when a component comparison is a type error (no shared value
    /// space and not term-identical) so the caller can propagate the error.
    /// </summary>
    /// <param name="left">The left triple term.</param>
    /// <param name="right">The right triple term.</param>
    /// <param name="context">The seams the component comparisons consume.</param>
    /// <returns><see langword="true"/>/<see langword="false"/> for a decided comparison, or <see langword="null"/> for a type error.</returns>
    private static bool? TripleTermValueEqual(Lumoin.Veritas.Core.TripleTerm left, Lumoin.Veritas.Core.TripleTerm right, SparqlExpressionContext context)
    {
        Stack<(RdfTerm Left, RdfTerm Right, int Depth)> work = new();
        work.Push((left, right, 1));

        while(work.Count > 0)
        {
            (RdfTerm a, RdfTerm b, int depth) = work.Pop();
            switch(a)
            {
                case Lumoin.Veritas.Core.TripleTerm at when b is Lumoin.Veritas.Core.TripleTerm bt:
                {
                    if(depth > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                    }

                    work.Push((at.Subject, bt.Subject, depth + 1));
                    work.Push((at.Predicate, bt.Predicate, depth + 1));
                    work.Push((at.Object, bt.Object, depth + 1));
                    break;
                }
                case Lumoin.Veritas.Core.TripleTerm:
                {
                    return false;
                }
                default:
                {
                    ExpressionValue comparison = Compare(ExpressionValue.Of(a), ExpressionValue.Of(b), ComparisonOp.Equal, context);
                    if(comparison.IsError)
                    {
                        return null;
                    }

                    if(!TryGetBoolean(comparison.Term, out bool componentEqual) || !componentEqual)
                    {
                        return false;
                    }
                    break;
                }
            }
        }

        return true;
    }

    /// <summary>Maps a sign-of-comparison (<c>&lt;0</c>/<c>0</c>/<c>&gt;0</c>) and an operator to the boolean the operator asserts.</summary>
    /// <param name="sign">The comparison sign.</param>
    /// <param name="op">The comparison operator.</param>
    /// <returns>The boolean the operator asserts for the sign.</returns>
    /// <summary>Maps a numeric-tower <see cref="ComparisonResult"/> to a sign (<c>-1</c>/<c>0</c>/<c>+1</c>); the incomparable case is handled by callers before this is reached.</summary>
    /// <param name="result">The comparison result.</param>
    /// <returns>The comparison sign.</returns>
    private static int ComparisonSign(ComparisonResult result)
    {
        return result switch
        {
            ComparisonResult.Less => -1,
            ComparisonResult.Greater => 1,
            _ => 0
        };
    }

    private static bool CompareOrdered(int sign, ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.Equal => sign == 0,
            ComparisonOp.NotEqual => sign != 0,
            ComparisonOp.LessThan => sign < 0,
            ComparisonOp.LessOrEqual => sign <= 0,
            ComparisonOp.GreaterThan => sign > 0,
            ComparisonOp.GreaterOrEqual => sign >= 0,
            _ => throw new InvalidOperationException($"Unexpected comparison operator {op}.")
        };
    }

    /// <summary>Evaluates an arithmetic expression (§17.4 operator mapping) over numeric operands; non-numeric operands yield an error.</summary>
    /// <param name="left">The left (or sole, for unary) operand value.</param>
    /// <param name="right">The right operand value; ignored for the unary operators.</param>
    /// <param name="op">The arithmetic operator.</param>
    /// <returns>The numeric result, or an error when an operand is non-numeric or a division by zero occurs.</returns>
    private static ExpressionValue Arithmetic(ExpressionValue left, ExpressionValue right, ArithmeticOp op)
    {
        if(left.IsError || !TryGetNumericValue(left.Term, out NumericValue a))
        {
            return ExpressionValue.Error;
        }

        if(op == ArithmeticOp.UnaryMinus)
        {
            return Numeric(a.Negate());
        }

        if(op == ArithmeticOp.UnaryPlus)
        {
            return Numeric(a);
        }

        if(right.IsError || !TryGetNumericValue(right.Term, out NumericValue b))
        {
            return ExpressionValue.Error;
        }

        return op switch
        {
            ArithmeticOp.Add => Numeric(NumericValue.Add(a, b)),
            ArithmeticOp.Subtract => Numeric(NumericValue.Subtract(a, b)),
            ArithmeticOp.Multiply => Numeric(NumericValue.Multiply(a, b)),

            //Division promotes integer/integer to xsd:decimal; a zero divisor in the exact (integer/decimal) kinds errs.
            ArithmeticOp.Divide => NumericValue.TryDivide(a, b, out NumericValue quotient) ? Numeric(quotient) : ExpressionValue.Error,
            _ => throw new InvalidOperationException($"Unexpected arithmetic operator {op}.")
        };
    }

    /// <summary>Wraps a CLR boolean as the corresponding cached <c>xsd:boolean</c> literal value.</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The boolean literal value.</returns>
    private static ExpressionValue Boolean(bool value)
    {
        return ExpressionValue.Of(value ? True : False);
    }

    /// <summary>Wraps a numeric-tower value as a typed literal in its XSD canonical lexical form and matching datatype (<c>xsd:integer</c>/<c>decimal</c>/<c>float</c>/<c>double</c>).</summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The numeric literal value.</returns>
    private static ExpressionValue Numeric(NumericValue value)
    {
        return ExpressionValue.Of(new Literal(Utf8Strings.From(value.ToCanonicalLexical()), new NamedNode(value.DatatypeIri)));
    }

    /// <summary>Wraps a CLR <c>long</c> count/length as an <c>xsd:integer</c> literal value (for COUNT, STRLEN, the date-time accessors).</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The integer literal value.</returns>
    private static ExpressionValue Integer(long value)
    {
        return Numeric(new NumericValue(new BigInteger(value)));
    }

    /// <summary>Wraps a CLR <c>double</c> result (RAND) as an <c>xsd:double</c> literal value.</summary>
    /// <param name="value">The double value.</param>
    /// <returns>The double literal value.</returns>
    private static ExpressionValue Double(double value)
    {
        return Numeric(new NumericValue(value));
    }

    /// <summary>Returns whether a term is a boolean literal, yielding its value.</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the boolean value on success.</param>
    /// <returns><see langword="true"/> when the term is an <c>xsd:boolean</c> literal.</returns>
    private static bool TryGetBoolean(RdfTerm term, out bool value)
    {
        if(term is Literal literal && literal.Datatype.Iri == Vocabulary.Xsd.Boolean)
        {
            string lexical = literal.Value.ToString();
            if(lexical is "true" or "1")
            {
                value = true;

                return true;
            }

            if(lexical is "false" or "0")
            {
                value = false;

                return true;
            }
        }

        value = false;

        return false;
    }

    /// <summary>
    /// Returns whether a term is a numeric literal, yielding its value in the numeric tower (<see cref="NumericValue"/>),
    /// which preserves the operand's exact kind (integer/decimal/float/double) for promotion and canonical-form
    /// result typing.
    /// </summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the numeric value on success.</param>
    /// <returns><see langword="true"/> when the term is a numeric literal with a parseable lexical value.</returns>
    private static bool TryGetNumericValue(RdfTerm term, out NumericValue value)
    {
        if(term is Literal literal && NumericValue.TryParse(literal.Value.ToString(), literal.Datatype.Iri, out value))
        {
            return true;
        }

        value = default;

        return false;
    }

    /// <summary>Returns whether a term is a numeric literal, yielding its value as a <see cref="double"/> — the convenience for the integer/double-only consumers (SUBSTR positions, the numeric effective-boolean-value test).</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the value as a double on success.</param>
    /// <returns><see langword="true"/> when the term is a numeric literal.</returns>
    private static bool TryGetDouble(RdfTerm term, out double value)
    {
        if(TryGetNumericValue(term, out NumericValue numeric))
        {
            value = numeric.Kind switch
            {
                NumericKind.Integer => (double)numeric.AsInteger(),
                NumericKind.Decimal => (double)numeric.AsDecimal(),
                NumericKind.Float => numeric.AsFloat(),
                _ => numeric.AsDouble()
            };

            return true;
        }

        value = 0;

        return false;
    }

    /// <summary>Returns whether a term is a plain string literal (<c>xsd:string</c>), yielding its UTF-8 lexical form.</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the UTF-8 lexical form on success.</param>
    /// <returns><see langword="true"/> when the term is an <c>xsd:string</c> literal.</returns>
    private static bool TryGetText(RdfTerm term, out Utf8String value)
    {
        if(term is Literal literal && literal.Language is null && literal.Datatype.Iri == Vocabulary.Xsd.String)
        {
            value = literal.Value;

            return true;
        }

        value = EmptyText;

        return false;
    }

    /// <summary>Builds an operand list of a leading tested value followed by a candidate set (the <c>IN</c> / <c>NOT IN</c> shape).</summary>
    /// <param name="head">The tested value.</param>
    /// <param name="tail">The candidate set.</param>
    /// <returns>A list holding <paramref name="head"/> then every item of <paramref name="tail"/>.</returns>
    private static List<ExpressionNode> Prepend(ExpressionNode head, IReadOnlyList<ExpressionNode> tail)
    {
        List<ExpressionNode> operands = new(tail.Count + 1) { head };
        operands.AddRange(tail);

        return operands;
    }

    /// <summary>The result of evaluating an expression: either a bound RDF term or the error value SPARQL expression evaluation can produce.</summary>
    /// <param name="Term">The bound term, or <see langword="null"/> when <see cref="IsError"/> is set.</param>
    /// <param name="IsError">Whether evaluation produced an error.</param>
    private readonly record struct ExpressionValue(RdfTerm? TermOrNull, bool IsError)
    {
        /// <summary>The shared error value.</summary>
        public static ExpressionValue Error { get; } = new(null, IsError: true);

        /// <summary>The bound term; valid only when <see cref="IsError"/> is <see langword="false"/>.</summary>
        public RdfTerm Term => TermOrNull!;

        /// <summary>Wraps a bound term as a (non-error) value.</summary>
        /// <param name="term">The bound term.</param>
        /// <returns>The value.</returns>
        public static ExpressionValue Of(RdfTerm term) => new(term, IsError: false);

        /// <summary>Implicitly wraps a literal as a (non-error) value, for the cached <see cref="True"/>/<see cref="False"/> returns.</summary>
        /// <param name="literal">The literal.</param>
        public static implicit operator ExpressionValue(Literal literal) => Of(literal);
    }
}
