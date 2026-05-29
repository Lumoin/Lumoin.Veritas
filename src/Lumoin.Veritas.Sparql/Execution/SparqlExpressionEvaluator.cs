using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
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

    /// <summary>The <c>xsd:boolean</c> datatype IRI.</summary>
    private static Utf8String XsdBoolean { get; } = Utf8String.FromString(XsdNamespace + "boolean");

    /// <summary>The <c>xsd:string</c> datatype IRI.</summary>
    private static Utf8String XsdString { get; } = Utf8String.FromString(XsdNamespace + "string");

    /// <summary>The <c>xsd:integer</c> datatype IRI.</summary>
    private static Utf8String XsdInteger { get; } = Utf8String.FromString(XsdNamespace + "integer");

    /// <summary>The <c>xsd:double</c> datatype IRI.</summary>
    private static Utf8String XsdDouble { get; } = Utf8String.FromString(XsdNamespace + "double");

    /// <summary>The <c>rdf:langString</c> datatype IRI (the datatype of a language-tagged literal).</summary>
    private static Utf8String RdfLangString { get; } = Utf8String.FromString("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString");

    /// <summary>The <c>xsd:dateTime</c> datatype IRI (the argument type of the date-time accessor functions).</summary>
    private static Utf8String XsdDateTime { get; } = Utf8String.FromString(XsdNamespace + "dateTime");

    /// <summary>The <c>xsd:decimal</c> datatype IRI (the result type of <c>SECONDS</c>).</summary>
    private static Utf8String XsdDecimal { get; } = Utf8String.FromString(XsdNamespace + "decimal");

    /// <summary>The <c>xsd:dayTimeDuration</c> datatype IRI (the result type of <c>TIMEZONE</c>).</summary>
    private static Utf8String XsdDayTimeDuration { get; } = Utf8String.FromString(XsdNamespace + "dayTimeDuration");

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
    private static Literal True { get; } = new(Utf8String.FromString("true"), new NamedNode(XsdBoolean));

    /// <summary>The boolean literal <c>"false"^^xsd:boolean</c>.</summary>
    private static Literal False { get; } = new(Utf8String.FromString("false"), new NamedNode(XsdBoolean));

    /// <summary>The empty UTF-8 lexical form (the <c>LANG</c> of a non-language literal, the failure value of a string lookup).</summary>
    private static Utf8String EmptyText { get; } = Utf8String.FromString(string.Empty);

    /// <summary>The single-space UTF-8 string — the default <c>GROUP_CONCAT</c> separator.</summary>
    private static Utf8String SpaceText { get; } = Utf8String.FromString(" ");

    /// <summary>
    /// Returns whether an expression's effective boolean value is <see langword="true"/> over a solution. An
    /// error or a value with no effective boolean value yields <see langword="false"/> — the behaviour
    /// <c>FILTER</c> and the <c>OPTIONAL</c> join condition require.
    /// </summary>
    /// <param name="expression">The expression to test.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <returns><see langword="true"/> when the effective boolean value is true; otherwise <see langword="false"/>.</returns>
    public static bool Satisfies(ExpressionNode expression, SparqlSolution solution)
    {
        ExpressionValue value = EffectiveBooleanValue(Evaluate(expression, solution));

        return !value.IsError && ReferenceEquals(value.Term, True);
    }

    /// <summary>
    /// Evaluates an expression over a solution to its RDF-term value, yielding <see langword="false"/> when the
    /// expression raises an error or evaluates to an unbound result (the behaviour <c>BIND</c>/Extend requires —
    /// an errored binding is simply not added).
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <param name="value">Receives the evaluated term on success.</param>
    /// <returns><see langword="true"/> when the expression evaluates to a bound term; otherwise <see langword="false"/>.</returns>
    public static bool TryEvaluate(ExpressionNode expression, SparqlSolution solution, out RdfTerm value)
    {
        ExpressionValue result = Evaluate(expression, solution);
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
    /// nodes and IRIs order by their label/IRI text, and literals order by value (numeric / boolean / string) or, for
    /// otherwise-incomparable literals, deterministically by datatype IRI then lexical form.
    /// </summary>
    /// <param name="left">The left term, or <see langword="null"/> for an unbound/errored key.</param>
    /// <param name="right">The right term, or <see langword="null"/> for an unbound/errored key.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    internal static int CompareForOrdering(RdfTerm? left, RdfTerm? right)
    {
        int leftRank = OrderRank(left);
        int rightRank = OrderRank(right);
        if(leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        return leftRank switch
        {
            1 => ((BlankNode)left!).Label.CompareTo(((BlankNode)right!).Label),
            2 => ((NamedNode)left!).Iri.CompareTo(((NamedNode)right!).Iri),
            3 => CompareLiterals((Literal)left!, (Literal)right!),

            //Both unbound (0) or both triple terms (4) — treat as equal for ordering.
            _ => 0
        };
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

    /// <summary>Orders two literals by value where a common value space exists, else deterministically by datatype IRI then lexical form.</summary>
    /// <param name="left">The left literal.</param>
    /// <param name="right">The right literal.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    private static int CompareLiterals(Literal left, Literal right)
    {
        if(TryGetNumeric(left, out double leftNumber, out _) && TryGetNumeric(right, out double rightNumber, out _))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if(TryGetBoolean(left, out bool leftBoolean) && TryGetBoolean(right, out bool rightBoolean))
        {
            return leftBoolean.CompareTo(rightBoolean);
        }

        if(TryGetText(left, out Utf8String leftText) && TryGetText(right, out Utf8String rightText))
        {
            return leftText.CompareTo(rightText);
        }

        int byDatatype = left.Datatype.Iri.CompareTo(right.Datatype.Iri);

        return byDatatype != 0 ? byDatatype : left.Value.CompareTo(right.Value);
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
    /// <returns>The aggregate's term value, or <see langword="null"/> when it has none.</returns>
    internal static RdfTerm? EvaluateAggregate(AggregateExpression aggregate, IReadOnlyList<SparqlSolution> group)
    {
        if(aggregate.IsCountStar)
        {
            return Numeric(group.Count, integral: true).Term;
        }

        List<RdfTerm> values = [];
        if(aggregate.Argument is not null)
        {
            foreach(SparqlSolution solution in group)
            {
                if(TryEvaluate(aggregate.Argument, solution, out RdfTerm value))
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
            AggregateFunction.Count => Numeric(values.Count, integral: true).Term,
            AggregateFunction.Sum => SumTerm(values),
            AggregateFunction.Avg => AverageTerm(values),
            AggregateFunction.Min => Extremum(values, minimum: true),
            AggregateFunction.Max => Extremum(values, minimum: false),
            AggregateFunction.Sample => values.Count > 0 ? values[0] : null,
            AggregateFunction.GroupConcat => GroupConcatTerm(values, aggregate.GroupConcatSeparator),
            _ => throw new NotSupportedException($"SPARQL aggregate function '{aggregate.Function}' is not yet evaluable.")
        };
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
        double sum = 0;
        bool integral = true;
        foreach(RdfTerm value in values)
        {
            if(!TryGetNumeric(value, out double number, out bool valueIntegral))
            {
                return null;
            }

            sum += number;
            integral &= valueIntegral;
        }

        return Numeric(sum, integral).Term;
    }

    /// <summary>Averages numeric values as an xsd:double; an empty set averages to 0, a non-numeric value yields an error (null).</summary>
    /// <param name="values">The values to average.</param>
    /// <returns>The average term, or <see langword="null"/> on a non-numeric value.</returns>
    private static RdfTerm? AverageTerm(List<RdfTerm> values)
    {
        if(values.Count == 0)
        {
            return Numeric(0, integral: true).Term;
        }

        double sum = 0;
        foreach(RdfTerm value in values)
        {
            if(!TryGetNumeric(value, out double number, out _))
            {
                return null;
            }

            sum += number;
        }

        return Numeric(sum / values.Count, integral: false).Term;
    }

    /// <summary>Returns the minimum or maximum value by the ORDER BY ordering, or <see langword="null"/> for an empty set.</summary>
    /// <param name="values">The values.</param>
    /// <param name="minimum">Whether to take the minimum (otherwise the maximum).</param>
    /// <returns>The extreme value, or <see langword="null"/> when there are none.</returns>
    private static RdfTerm? Extremum(List<RdfTerm> values, bool minimum)
    {
        if(values.Count == 0)
        {
            return null;
        }

        RdfTerm best = values[0];
        for(int i = 1; i < values.Count; i++)
        {
            int comparison = CompareForOrdering(values[i], best);
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

        return new Literal(new Utf8String(output.ToArray()), new NamedNode(XsdString));
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
    /// <returns>The evaluated value.</returns>
    private static ExpressionValue Evaluate(ExpressionNode root, SparqlSolution solution)
    {
        Dictionary<ExpressionNode, ExpressionValue> values = new(ReferenceEqualityComparer.Instance);
        Stack<(ExpressionNode Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (ExpressionNode node, bool combine) = work.Pop();
            if(combine)
            {
                values[node] = CombineValue(node, values);

                continue;
            }

            IReadOnlyList<ExpressionNode> children = Operands(node);
            if(children.Count == 0)
            {
                values[node] = EvaluateLeaf(node, solution);
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

            //Leaves (ConstantExpression, VariableExpression, BoundExpression) and the unsupported forms.
            _ => []
        };
    }

    /// <summary>Evaluates a leaf expression (or rejects an unsupported form): a constant, a variable, or a <c>BOUND</c> test.</summary>
    /// <param name="node">The leaf node.</param>
    /// <param name="solution">The solution the expression is evaluated over.</param>
    /// <returns>The leaf's value.</returns>
    /// <exception cref="NotSupportedException">The node is an unsupported form (a function call, EXISTS, or a triple-term expression).</exception>
    private static ExpressionValue EvaluateLeaf(ExpressionNode node, SparqlSolution solution)
    {
        return node switch
        {
            ConstantExpression constant => ExpressionValue.Of(constant.Value),

            //An unbound variable raises an error when its value is needed.
            VariableExpression variable => solution.TryGetValue(variable.Variable, out RdfTerm value) ? ExpressionValue.Of(value) : ExpressionValue.Error,
            BoundExpression bound => Boolean(solution.TryGetValue(bound.Variable, out _)),
            _ => throw new NotSupportedException($"SPARQL expression '{node.GetType().Name}' is not yet evaluable; this slice covers constants, variables, BOUND, the logical/comparison/arithmetic operators, IF, COALESCE, IN/NOT IN, and a core of the built-in functions. Named functions, EXISTS/NOT EXISTS, and triple-term expressions land in a later slice.")
        };
    }

    /// <summary>Combines an expression's already-evaluated operand values (read from <paramref name="values"/>) into its own value.</summary>
    /// <param name="node">The expression to combine.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <returns>The expression's value.</returns>
    private static ExpressionValue CombineValue(ExpressionNode node, Dictionary<ExpressionNode, ExpressionValue> values)
    {
        return node switch
        {
            AndExpression and => LogicalAnd(EffectiveState(values[and.Left]), EffectiveState(values[and.Right])),
            OrExpression or => LogicalOr(EffectiveState(values[or.Left]), EffectiveState(values[or.Right])),
            NotExpression not => EffectiveState(values[not.Inner]) is bool state ? Boolean(!state) : ExpressionValue.Error,
            ComparisonExpression comparison => Compare(values[comparison.Left], values[comparison.Right], comparison.Op),
            ArithmeticExpression arithmetic => Arithmetic(values[arithmetic.Left], arithmetic.Right is null ? default : values[arithmetic.Right], arithmetic.Op),
            IfExpression conditional => EffectiveState(values[conditional.Condition]) is bool taken ? (taken ? values[conditional.IfTrue] : values[conditional.IfFalse]) : ExpressionValue.Error,
            CoalesceExpression coalesce => FirstBound(coalesce.Alternatives, values),
            InExpression test => Membership(test.Value, test.Set, values, negated: false),
            NotInExpression test => Membership(test.Value, test.Set, values, negated: true),
            BuiltInCallExpression call => EvaluateBuiltInCall(call, values),
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
    /// <returns>The call's value.</returns>
    private static ExpressionValue EvaluateBuiltInCall(BuiltInCallExpression call, Dictionary<ExpressionNode, ExpressionValue> values)
    {
        ExpressionValue[] arguments = new ExpressionValue[call.Arguments.Count];
        for(int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = values[call.Arguments[i]];
        }

        return EvaluateBuiltIn(call.Function, arguments);
    }

    /// <summary>Evaluates <c>IN</c> / <c>NOT IN</c>: membership of the tested value among the candidate set under <c>=</c> semantics.</summary>
    /// <param name="valueNode">The tested-value expression.</param>
    /// <param name="set">The candidate-set expressions.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <param name="negated">Whether this is the <c>NOT IN</c> form.</param>
    /// <returns>The membership result: a boolean, or an error when the tested value errs or a comparison errs with no match found.</returns>
    private static ExpressionValue Membership(ExpressionNode valueNode, IReadOnlyList<ExpressionNode> set, Dictionary<ExpressionNode, ExpressionValue> values, bool negated)
    {
        ExpressionValue value = values[valueNode];
        if(value.IsError)
        {
            return ExpressionValue.Error;
        }

        bool anyError = false;
        foreach(ExpressionNode candidate in set)
        {
            ExpressionValue comparison = Compare(value, values[candidate], ComparisonOp.Equal);
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
    /// Evaluates a built-in function over its already-evaluated arguments (§17.4). This slice covers the term-type
    /// tests, <c>sameTerm</c>, <c>STR</c>/<c>LANG</c>/<c>DATATYPE</c>, the common string functions
    /// (<c>STRLEN</c>/<c>UCASE</c>/<c>LCASE</c>/<c>CONTAINS</c>/<c>STRSTARTS</c>/<c>STRENDS</c>), and the unary
    /// numeric functions (<c>ABS</c>/<c>CEIL</c>/<c>FLOOR</c>/<c>ROUND</c>); the rest raise
    /// <see cref="NotSupportedException"/> until their later slice.
    /// </summary>
    /// <param name="function">The built-in function.</param>
    /// <param name="arguments">The evaluated argument values, in order.</param>
    /// <returns>The function's value, or an error.</returns>
    /// <exception cref="NotSupportedException">The function is not yet evaluable.</exception>
    private static ExpressionValue EvaluateBuiltIn(BuiltInFunction function, ExpressionValue[] arguments)
    {
        return function switch
        {
            BuiltInFunction.IsIri or BuiltInFunction.IsUri => TypeTest(arguments[0], static term => term is NamedNode),
            BuiltInFunction.IsBlank => TypeTest(arguments[0], static term => term is BlankNode),
            BuiltInFunction.IsLiteral => TypeTest(arguments[0], static term => term is Literal),
            BuiltInFunction.IsTriple => TypeTest(arguments[0], static term => term is Lumoin.Veritas.Core.TripleTerm),
            BuiltInFunction.IsNumeric => TypeTest(arguments[0], static term => TryGetNumeric(term, out _, out _)),
            BuiltInFunction.SameTerm => arguments[0].IsError || arguments[1].IsError ? ExpressionValue.Error : Boolean(arguments[0].Term.Equals(arguments[1].Term)),
            BuiltInFunction.Str => Str(arguments[0]),
            BuiltInFunction.Lang => arguments[0].IsError || arguments[0].Term is not Literal language ? ExpressionValue.Error : StringLiteral(language.Language ?? EmptyText),
            BuiltInFunction.LangMatches => LangMatches(arguments[0], arguments[1]),
            BuiltInFunction.Datatype => arguments[0].IsError || arguments[0].Term is not Literal datatype ? ExpressionValue.Error : ExpressionValue.Of(datatype.Datatype),
            BuiltInFunction.StrLen => TryGetLexicalText(arguments[0], out Utf8String text) ? Numeric(CodePointCount(text), integral: true) : ExpressionValue.Error,
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
            BuiltInFunction.Regex => RegexMatch(arguments),
            BuiltInFunction.Replace => Replace(arguments),
            BuiltInFunction.Iri or BuiltInFunction.Uri => Iri(arguments[0]),
            BuiltInFunction.StrDt => StrDt(arguments),
            BuiltInFunction.StrLang => StrLang(arguments),
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
            BuiltInFunction.Abs => UnaryNumeric(arguments[0], static value => Math.Abs(value)),
            BuiltInFunction.Ceil => UnaryNumeric(arguments[0], Math.Ceiling),
            BuiltInFunction.Floor => UnaryNumeric(arguments[0], Math.Floor),
            BuiltInFunction.Round => UnaryNumeric(arguments[0], static value => Math.Round(value, MidpointRounding.AwayFromZero)),
            _ => throw new NotSupportedException($"SPARQL built-in function '{function}' is not yet evaluable; this slice covers the term-type tests, sameTerm, STR/LANG/LANGMATCHES/DATATYPE, the string functions (STRLEN/UCASE/LCASE/CONTAINS/STRSTARTS/STRENDS/CONCAT/SUBSTR/STRBEFORE/STRAFTER/ENCODE_FOR_URI/REGEX/REPLACE), the term constructors (IRI/URI/STRDT/STRLANG), the date-time accessors (YEAR/MONTH/DAY/HOURS/MINUTES/SECONDS/TIMEZONE/TZ), the triple builders (TRIPLE/SUBJECT/PREDICATE/OBJECT), and ABS/CEIL/FLOOR/ROUND. The seam-dependent functions (BNODE, RAND, UUID/STRUUID, NOW, the hash functions — all through the Veritas injection seams) and the named functions and EXISTS/NOT EXISTS land in a later slice.")
        };
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

    /// <summary>Evaluates <c>UCASE</c>/<c>LCASE</c>: the cased lexical form of a string argument as an <c>xsd:string</c>, mapping over Unicode runes (locale-independent, per the XPath case functions SPARQL defers to).</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="toUpper">Whether to upper-case (otherwise lower-case).</param>
    /// <returns>The cased string value, or an error when the argument is not a string.</returns>
    private static ExpressionValue MapCaseValue(ExpressionValue argument, bool toUpper)
    {
        return TryGetLexicalText(argument, out Utf8String text) ? StringLiteral(MapCase(text, toUpper)) : ExpressionValue.Error;
    }

    /// <summary>Applies a term-type predicate to a single argument, propagating an argument error.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="test">The predicate on the bound term.</param>
    /// <returns>A boolean value, or an error when the argument errs.</returns>
    private static ExpressionValue TypeTest(ExpressionValue argument, Func<RdfTerm, bool> test)
    {
        return argument.IsError ? ExpressionValue.Error : Boolean(test(argument.Term));
    }

    /// <summary>Applies a binary predicate over two string arguments' UTF-8 values, erring when either is not a string or errs.</summary>
    /// <param name="arguments">The argument values (two strings expected).</param>
    /// <param name="test">The predicate over the two UTF-8 values (operates on their byte spans).</param>
    /// <returns>A boolean value, or an error.</returns>
    private static ExpressionValue StringTest(ExpressionValue[] arguments, Func<Utf8String, Utf8String, bool> test)
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
    /// keeps a common language tag when every argument shares it, otherwise it is a plain <c>xsd:string</c>; a
    /// non-string argument errs.
    /// </summary>
    /// <param name="arguments">The string arguments.</param>
    /// <returns>The concatenated string value, or an error.</returns>
    private static ExpressionValue Concat(ExpressionValue[] arguments)
    {
        ArrayBufferWriter<byte> buffer = new();
        Utf8String? commonLanguage = null;
        bool languageConsistent = arguments.Length > 0;
        for(int i = 0; i < arguments.Length; i++)
        {
            if(!TryGetStringArgument(arguments[i], out Utf8String text, out Utf8String? language))
            {
                return ExpressionValue.Error;
            }

            buffer.Write(text.Span);
            if(i == 0)
            {
                commonLanguage = language;
            }
            else if(!SameLanguage(language, commonLanguage))
            {
                languageConsistent = false;
            }
        }

        return StringLiteral(new Utf8String(buffer.WrittenSpan.ToArray()), languageConsistent ? commonLanguage : null);
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
            || arguments[1].IsError || !TryGetNumeric(arguments[1].Term, out double startValue, out _))
        {
            return ExpressionValue.Error;
        }

        int start = (int)Math.Round(startValue, MidpointRounding.AwayFromZero);
        int? upperExclusive = null;
        if(arguments.Length >= 3)
        {
            if(arguments[2].IsError || !TryGetNumeric(arguments[2].Term, out double lengthValue, out _))
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
    /// <returns>A boolean value, or an error.</returns>
    private static ExpressionValue RegexMatch(ExpressionValue[] arguments)
    {
        if(!TryGetStringArgument(arguments[0], out Utf8String text, out _)
            || !TryGetStringArgument(arguments[1], out Utf8String pattern, out _)
            || !TryGetOptionalFlags(arguments, 2, out string? flags)
            || !TryBuildRegex(pattern.ToString(), flags, out Regex regex))
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
    /// <returns>The replaced string value, or an error.</returns>
    private static ExpressionValue Replace(ExpressionValue[] arguments)
    {
        if(!TryGetStringArgument(arguments[0], out Utf8String source, out Utf8String? language)
            || !TryGetStringArgument(arguments[1], out Utf8String pattern, out _)
            || !TryGetStringArgument(arguments[2], out Utf8String replacement, out _)
            || !TryGetOptionalFlags(arguments, 3, out string? flags)
            || !TryBuildRegex(pattern.ToString(), flags, out Regex regex))
        {
            return ExpressionValue.Error;
        }

        try
        {
            return StringLiteral(Utf8String.FromString(regex.Replace(source.ToString(), replacement.ToString())), language);
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

    /// <summary>Builds a non-backtracking <see cref="Regex"/> from a pattern and XPath flag string (<c>i</c>/<c>s</c>/<c>m</c>/<c>x</c>); a bad pattern, an unsupported construct, or an unknown flag fails.</summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="flags">The XPath flag string, or <see langword="null"/>.</param>
    /// <param name="regex">Receives the compiled regex on success.</param>
    /// <returns><see langword="true"/> when the regex compiled.</returns>
    private static bool TryBuildRegex(string pattern, string? flags, out Regex regex)
    {
        RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if(flags is not null)
        {
            foreach(char flag in flags)
            {
                RegexOptions? mapped = flag switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    's' => RegexOptions.Singleline,
                    'm' => RegexOptions.Multiline,
                    'x' => RegexOptions.IgnorePatternWhitespace,
                    _ => null
                };

                if(mapped is not RegexOptions option)
                {
                    regex = null!;

                    return false;
                }

                options |= option;
            }
        }

        try
        {
            regex = new Regex(pattern, options);

            return true;
        }
        catch(Exception exception) when(exception is ArgumentException or NotSupportedException)
        {
            regex = null!;

            return false;
        }
    }

    /// <summary>
    /// Evaluates <c>IRI</c>/<c>URI</c> (§17.4.2.8): an IRI argument passes through; a plain-string argument becomes
    /// an IRI of that lexical form. (Resolution of a relative IRI against the query base is a later refinement.) A
    /// blank node, a tagged/typed literal, or an error argument errs.
    /// </summary>
    /// <param name="argument">The argument value.</param>
    /// <returns>The IRI value, or an error.</returns>
    private static ExpressionValue Iri(ExpressionValue argument)
    {
        if(argument.IsError)
        {
            return ExpressionValue.Error;
        }

        return argument.Term switch
        {
            NamedNode named => ExpressionValue.Of(named),
            Literal literal when literal.Language is null && literal.Datatype.Iri == XsdString => ExpressionValue.Of(new NamedNode(literal.Value)),
            _ => ExpressionValue.Error
        };
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
            || arguments[0].Term is not Literal lexical || lexical.Language is not null || lexical.Datatype.Iri != XsdString
            || arguments[1].Term is not NamedNode datatype)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Literal(lexical.Value, datatype));
    }

    /// <summary>
    /// Evaluates <c>STRLANG</c> (§17.4.2.10): builds a language-tagged literal from a simple-string lexical form
    /// and a simple-string language tag. A non-simple-string argument errs.
    /// </summary>
    /// <param name="arguments">The arguments: the lexical form and the language tag (both simple strings).</param>
    /// <returns>The language-tagged-literal value, or an error.</returns>
    private static ExpressionValue StrLang(ExpressionValue[] arguments)
    {
        if(arguments[0].IsError || arguments[1].IsError
            || arguments[0].Term is not Literal lexical || lexical.Language is not null || lexical.Datatype.Iri != XsdString
            || arguments[1].Term is not Literal tag || tag.Language is not null || tag.Datatype.Iri != XsdString)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Literal(lexical.Value, new NamedNode(RdfLangString), tag.Value));
    }

    /// <summary>Evaluates a date-time accessor (<c>YEAR</c>/<c>MONTH</c>/<c>DAY</c>/<c>HOURS</c>/<c>MINUTES</c>, §17.4.5) that returns an integer component of an <c>xsd:dateTime</c>; a non-dateTime argument errs.</summary>
    /// <param name="argument">The <c>xsd:dateTime</c> argument.</param>
    /// <param name="component">The component selector, reading the field in the value's own timezone.</param>
    /// <returns>The component as an <c>xsd:integer</c>, or an error.</returns>
    private static ExpressionValue DateComponent(ExpressionValue argument, Func<DateTimeOffset, int> component)
    {
        return TryGetDateTime(argument, out DateTimeOffset value, out _) ? Numeric(component(value), integral: true) : ExpressionValue.Error;
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

        return ExpressionValue.Of(new Literal(Utf8String.FromString(lexical), new NamedNode(XsdDecimal)));
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

        return ExpressionValue.Of(new Literal(Utf8String.FromString(FormatDayTimeDuration(value.Offset)), new NamedNode(XsdDayTimeDuration)));
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

        return StringLiteral(Utf8String.FromString(value.Offset == TimeSpan.Zero ? "Z" : FormatOffset(value.Offset)));
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
        if(argument.IsError || argument.Term is not Literal literal || literal.Datatype.Iri != XsdDateTime)
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
    /// subject must not be a literal and the predicate must be an IRI; otherwise the result errs.
    /// </summary>
    /// <param name="arguments">The arguments: subject, predicate, object.</param>
    /// <returns>The triple-term value, or an error.</returns>
    private static ExpressionValue BuildTriple(ExpressionValue[] arguments)
    {
        if(arguments[0].IsError || arguments[1].IsError || arguments[2].IsError
            || arguments[0].Term is Literal
            || arguments[1].Term is not NamedNode predicate)
        {
            return ExpressionValue.Error;
        }

        return ExpressionValue.Of(new Lumoin.Veritas.Core.TripleTerm(arguments[0].Term, predicate, arguments[2].Term));
    }

    /// <summary>Evaluates <c>SUBJECT</c>/<c>PREDICATE</c>/<c>OBJECT</c> (RDF 1.2): extracts a component of a triple term; a non-triple-term argument errs.</summary>
    /// <param name="argument">The argument value (expected to be a triple term).</param>
    /// <param name="component">The component selector.</param>
    /// <returns>The selected component, or an error.</returns>
    private static ExpressionValue TripleComponent(ExpressionValue argument, Func<Lumoin.Veritas.Core.TripleTerm, RdfTerm> component)
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
        if(!argument.IsError && argument.Term is Literal literal && (literal.Datatype.Iri == XsdString || literal.Language is not null))
        {
            text = literal.Value;
            language = literal.Language;

            return true;
        }

        text = EmptyText;
        language = null;

        return false;
    }

    /// <summary>Builds a string literal value: a language-tagged literal when <paramref name="language"/> is present, otherwise an <c>xsd:string</c>.</summary>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <param name="language">The language tag, or <see langword="null"/> for a plain string.</param>
    /// <returns>The string-literal value.</returns>
    private static ExpressionValue StringLiteral(Utf8String value, Utf8String? language)
    {
        return language is Utf8String tag
            ? ExpressionValue.Of(new Literal(value, new NamedNode(RdfLangString), tag))
            : StringLiteral(value);
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

    /// <summary>Applies a unary numeric operation to a numeric argument, preserving integer typing, erring on a non-numeric argument.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="operation">The numeric operation.</param>
    /// <returns>The numeric result, or an error.</returns>
    private static ExpressionValue UnaryNumeric(ExpressionValue argument, Func<double, double> operation)
    {
        if(argument.IsError || !TryGetNumeric(argument.Term, out double value, out bool integral))
        {
            return ExpressionValue.Error;
        }

        return Numeric(operation(value), integral);
    }

    /// <summary>Builds an <c>xsd:string</c> literal value from a UTF-8 lexical form (no <see cref="string"/> round-trip).</summary>
    /// <param name="value">The UTF-8 lexical form.</param>
    /// <returns>The string-literal value.</returns>
    private static ExpressionValue StringLiteral(Utf8String value)
    {
        return ExpressionValue.Of(new Literal(value, new NamedNode(XsdString)));
    }

    /// <summary>Extracts the UTF-8 lexical form of an argument that is an <c>xsd:string</c> or a language-tagged literal; fails for an error or a non-string term.</summary>
    /// <param name="argument">The argument value.</param>
    /// <param name="value">Receives the UTF-8 lexical form on success.</param>
    /// <returns><see langword="true"/> when the argument is a string-valued literal.</returns>
    private static bool TryGetLexicalText(ExpressionValue argument, out Utf8String value)
    {
        if(!argument.IsError && argument.Term is Literal literal && (literal.Datatype.Iri == XsdString || literal.Language is not null))
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

        if(TryGetNumeric(value.Term, out double number, out _))
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

    /// <summary>Compares two values under a relational operator (§17.3), choosing numeric, boolean, string, or term-equality semantics by the operands' kinds.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="op">The comparison operator.</param>
    /// <returns>A boolean result, or an error when either operand errs or the operands are not comparable under <paramref name="op"/>.</returns>
    private static ExpressionValue Compare(ExpressionValue left, ExpressionValue right, ComparisonOp op)
    {
        if(left.IsError || right.IsError)
        {
            return ExpressionValue.Error;
        }

        RdfTerm a = left.Term;
        RdfTerm b = right.Term;

        if(TryGetNumeric(a, out double na, out _) && TryGetNumeric(b, out double nb, out _))
        {
            return Boolean(CompareOrdered(na.CompareTo(nb), op));
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

        //Outside a common value space only equality is defined, via RDF term identity (sameTerm).
        return op switch
        {
            ComparisonOp.Equal => Boolean(a.Equals(b)),
            ComparisonOp.NotEqual => Boolean(!a.Equals(b)),
            _ => ExpressionValue.Error
        };
    }

    /// <summary>Maps a sign-of-comparison (<c>&lt;0</c>/<c>0</c>/<c>&gt;0</c>) and an operator to the boolean the operator asserts.</summary>
    /// <param name="sign">The comparison sign.</param>
    /// <param name="op">The comparison operator.</param>
    /// <returns>The boolean the operator asserts for the sign.</returns>
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
        if(left.IsError || !TryGetNumeric(left.Term, out double a, out bool aIntegral))
        {
            return ExpressionValue.Error;
        }

        if(op == ArithmeticOp.UnaryMinus)
        {
            return Numeric(-a, aIntegral);
        }

        if(op == ArithmeticOp.UnaryPlus)
        {
            return Numeric(a, aIntegral);
        }

        if(right.IsError || !TryGetNumeric(right.Term, out double b, out bool bIntegral))
        {
            return ExpressionValue.Error;
        }

        bool integral = aIntegral && bIntegral;

        return op switch
        {
            ArithmeticOp.Add => Numeric(a + b, integral),
            ArithmeticOp.Subtract => Numeric(a - b, integral),
            ArithmeticOp.Multiply => Numeric(a * b, integral),

            //Division never preserves integer typing (xsd:integer / xsd:integer is xsd:decimal); zero divisor errs.
            ArithmeticOp.Divide => b == 0 ? ExpressionValue.Error : Numeric(a / b, integral: false),
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

    /// <summary>Wraps a numeric result as an <c>xsd:integer</c> (when integral) or <c>xsd:double</c> literal value.</summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="integral">Whether the result keeps integer typing.</param>
    /// <returns>The numeric literal value.</returns>
    private static ExpressionValue Numeric(double value, bool integral)
    {
        if(integral)
        {
            return ExpressionValue.Of(new Literal(Utf8String.FromString(((long)value).ToString(CultureInfo.InvariantCulture)), new NamedNode(XsdInteger)));
        }

        return ExpressionValue.Of(new Literal(Utf8String.FromString(value.ToString("R", CultureInfo.InvariantCulture)), new NamedNode(XsdDouble)));
    }

    /// <summary>Returns whether a term is a boolean literal, yielding its value.</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the boolean value on success.</param>
    /// <returns><see langword="true"/> when the term is an <c>xsd:boolean</c> literal.</returns>
    private static bool TryGetBoolean(RdfTerm term, out bool value)
    {
        if(term is Literal literal && literal.Datatype.Iri == XsdBoolean)
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

    /// <summary>Returns whether a term is a numeric literal, yielding its value as a double and whether it is integer-typed.</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the numeric value on success.</param>
    /// <param name="integral">Receives whether the literal's datatype is an integral type.</param>
    /// <returns><see langword="true"/> when the term is a numeric literal with a parseable lexical value.</returns>
    private static bool TryGetNumeric(RdfTerm term, out double value, out bool integral)
    {
        if(term is Literal literal)
        {
            string datatype = literal.Datatype.Iri.ToString();
            if(NumericDatatypes.Contains(datatype)
                && double.TryParse(literal.Value.ToString(), NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                integral = IntegralDatatypes.Contains(datatype);

                return true;
            }
        }

        value = 0;
        integral = false;

        return false;
    }

    /// <summary>Returns whether a term is a plain string literal (<c>xsd:string</c>), yielding its UTF-8 lexical form.</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the UTF-8 lexical form on success.</param>
    /// <returns><see langword="true"/> when the term is an <c>xsd:string</c> literal.</returns>
    private static bool TryGetText(RdfTerm term, out Utf8String value)
    {
        if(term is Literal literal && literal.Language is null && literal.Datatype.Iri == XsdString)
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
