using System;
using System.Collections.Generic;
using System.Globalization;
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
/// arithmetic, <c>IF</c>, <c>COALESCE</c>, and <c>IN</c>/<c>NOT IN</c>. The named/built-in function library
/// (<see cref="FunctionCallExpression"/> / <see cref="BuiltInCallExpression"/>), <c>EXISTS</c>/<c>NOT EXISTS</c>
/// (which evaluate a graph pattern), and the triple-term expression raise <see cref="NotSupportedException"/>
/// until their later slice. Numeric values are compared and combined as <see cref="double"/>; integer-only
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
            1 => string.CompareOrdinal(((BlankNode)left!).Label.ToString(), ((BlankNode)right!).Label.ToString()),
            2 => string.CompareOrdinal(((NamedNode)left!).Iri.ToString(), ((NamedNode)right!).Iri.ToString()),
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

        if(TryGetString(left, out string leftText) && TryGetString(right, out string rightText))
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        int byDatatype = string.CompareOrdinal(left.Datatype.Iri.ToString(), right.Datatype.Iri.ToString());

        return byDatatype != 0 ? byDatatype : string.CompareOrdinal(left.Value.ToString(), right.Value.ToString());
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
        switch(node)
        {
            case ConstantExpression constant:
            {
                return ExpressionValue.Of(constant.Value);
            }

            case VariableExpression variable:
            {
                //An unbound variable raises an error when its value is needed.
                return solution.TryGetValue(variable.Variable, out RdfTerm value) ? ExpressionValue.Of(value) : ExpressionValue.Error;
            }

            case BoundExpression bound:
            {
                return Boolean(solution.TryGetValue(bound.Variable, out _));
            }

            default:
            {
                throw new NotSupportedException($"SPARQL expression '{node.GetType().Name}' is not yet evaluable; this slice covers constants, variables, BOUND, the logical/comparison/arithmetic operators, IF, COALESCE, and IN/NOT IN. Named and built-in functions, EXISTS/NOT EXISTS, and triple-term expressions land in a later slice.");
            }
        }
    }

    /// <summary>Combines an expression's already-evaluated operand values (read from <paramref name="values"/>) into its own value.</summary>
    /// <param name="node">The expression to combine.</param>
    /// <param name="values">The map of already-evaluated operands to their values.</param>
    /// <returns>The expression's value.</returns>
    private static ExpressionValue CombineValue(ExpressionNode node, Dictionary<ExpressionNode, ExpressionValue> values)
    {
        switch(node)
        {
            case AndExpression and:
            {
                return LogicalAnd(EffectiveState(values[and.Left]), EffectiveState(values[and.Right]));
            }

            case OrExpression or:
            {
                return LogicalOr(EffectiveState(values[or.Left]), EffectiveState(values[or.Right]));
            }

            case NotExpression not:
            {
                bool? state = EffectiveState(values[not.Inner]);

                return state is null ? ExpressionValue.Error : Boolean(!state.Value);
            }

            case ComparisonExpression comparison:
            {
                return Compare(values[comparison.Left], values[comparison.Right], comparison.Op);
            }

            case ArithmeticExpression arithmetic:
            {
                ExpressionValue right = arithmetic.Right is null ? default : values[arithmetic.Right];

                return Arithmetic(values[arithmetic.Left], right, arithmetic.Op);
            }

            case IfExpression conditional:
            {
                bool? state = EffectiveState(values[conditional.Condition]);
                if(state is null)
                {
                    return ExpressionValue.Error;
                }

                return state.Value ? values[conditional.IfTrue] : values[conditional.IfFalse];
            }

            case CoalesceExpression coalesce:
            {
                foreach(ExpressionNode alternative in coalesce.Alternatives)
                {
                    ExpressionValue value = values[alternative];
                    if(!value.IsError)
                    {
                        return value;
                    }
                }

                return ExpressionValue.Error;
            }

            case InExpression test:
            {
                return Membership(test.Value, test.Set, values, negated: false);
            }

            case NotInExpression test:
            {
                return Membership(test.Value, test.Set, values, negated: true);
            }

            default:
            {
                throw new InvalidOperationException($"Expression '{node.GetType().Name}' has operands but no combine rule in the evaluator.");
            }
        }
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

        if(TryGetString(value.Term, out string text))
        {
            return Boolean(text.Length > 0);
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

        if(TryGetString(a, out string sa) && TryGetString(b, out string sb))
        {
            return Boolean(CompareOrdered(string.CompareOrdinal(sa, sb), op));
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

    /// <summary>Returns whether a term is a plain string literal (<c>xsd:string</c>), yielding its text.</summary>
    /// <param name="term">The term to inspect.</param>
    /// <param name="value">Receives the string value on success.</param>
    /// <returns><see langword="true"/> when the term is an <c>xsd:string</c> literal.</returns>
    private static bool TryGetString(RdfTerm term, out string value)
    {
        if(term is Literal literal && literal.Language is null && literal.Datatype.Iri == XsdString)
        {
            value = literal.Value.ToString();

            return true;
        }

        value = string.Empty;

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
