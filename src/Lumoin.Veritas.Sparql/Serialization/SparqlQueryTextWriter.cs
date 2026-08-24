using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Serialization;

/// <summary>
/// Renders a (normalised) SPARQL graph pattern back to a self-contained <c>SELECT * WHERE { … }</c> query
/// string: every IRI is written absolute (no <c>PREFIX</c>/<c>BASE</c>), so the result depends on no prologue
/// and can be sent verbatim to a remote endpoint — the text a <c>SERVICE</c> federation step transmits.
/// </summary>
/// <remarks>
/// <para>
/// The walk is iterative (no recursion, <c>[[feedback_no_recursion]]</c>): a work stack holds either a literal
/// fragment to emit (<see cref="string"/>) or an AST node to expand. Expanding a node pushes its rendered
/// pieces — literal fragments interleaved with child nodes — in reverse, so they emit in source order.
/// </para>
/// <para>
/// The input is expected to be the normalised pattern the translator sees (RDF 1.2 sugar — collections,
/// blank-node property lists, annotations, reified triples — already lowered to plain triples). Forms outside
/// the federatable subset throw <see cref="NotSupportedException"/> so an unrenderable <c>SERVICE</c> block
/// fails loudly (and, under <c>SILENT</c>, is swallowed) rather than producing a malformed query.
/// </para>
/// </remarks>
public static class SparqlQueryTextWriter
{
    /// <summary>Renders a graph pattern as a self-contained <c>SELECT * WHERE { … }</c> query string with absolute IRIs.</summary>
    /// <param name="where">The WHERE pattern (typically a <see cref="GroupGraphPattern"/>).</param>
    /// <returns>The query text.</returns>
    /// <exception cref="NotSupportedException">The pattern contains a form outside the renderable subset.</exception>
    public static string ToSelectQuery(GraphPattern where)
    {
        ArgumentNullException.ThrowIfNull(where);

        StringBuilder builder = new();
        builder.Append("SELECT * WHERE ");
        if(where is not GroupGraphPattern)
        {
            builder.Append("{ ");
        }

        Render(where, builder);

        if(where is not GroupGraphPattern)
        {
            builder.Append(" }");
        }

        return builder.ToString();
    }

    /// <summary>Renders any supported node (pattern, triple, term, expression, or path) into the builder over a work stack.</summary>
    /// <param name="root">The node to render.</param>
    /// <param name="builder">The output builder.</param>
    private static void Render(object root, StringBuilder builder)
    {
        Stack<object> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            object node = work.Pop();
            if(node is string literal)
            {
                builder.Append(literal);

                continue;
            }

            Expand(node, work);
        }
    }

    /// <summary>Pushes a node's rendered pieces (literal fragments and child nodes) so they emit in order.</summary>
    /// <param name="node">The node to expand.</param>
    /// <param name="work">The work stack.</param>
    private static void Expand(object node, Stack<object> work)
    {
        switch(node)
        {
            case GroupGraphPattern group: ExpandGroup(group, work); break;
            case BasicGraphPatternBlock block: ExpandBlock(block, work); break;
            case OptionalPattern optional: PushSequence(work, "OPTIONAL ", optional.Inner); break;
            case MinusPattern minus: PushSequence(work, "MINUS ", minus.Inner); break;
            case UnionPattern union: PushSequence(work, union.Left, " UNION ", union.Right); break;
            case GraphGraphPattern graph: PushSequence(work, "GRAPH ", graph.GraphTerm, " ", graph.Inner); break;
            case ServicePattern service: PushSequence(work, service.IsSilent ? "SERVICE SILENT " : "SERVICE ", service.Endpoint, " ", service.Inner); break;
            case FilterPattern filter: PushSequence(work, "FILTER(", filter.Expression, ")"); break;
            case BindPattern bind: PushSequence(work, "BIND(", bind.Expression, " AS ?" + bind.AsVariable.Name + ")"); break;
            case ValuesPattern values: ExpandValues(values.Data, work); break;
            case SubSelectPattern: throw new NotSupportedException("A sub-SELECT inside a federated SERVICE pattern is not yet rendered.");

            case TriplePattern triple: PushSequence(work, triple.Subject, " ", triple.Predicate, " ", triple.Object); break;

            case GraphIriTerm graphIri: work.Push(Iri(graphIri.Iri)); break;
            case GraphVariableTerm graphVariable: work.Push(Variable(graphVariable.Variable)); break;

            case ConstantTerm constant: work.Push(SparqlResultTermText.Turtle(constant.Term)); break;
            case VariableTerm variableTerm: work.Push(Variable(variableTerm.Variable)); break;
            case PropertyPathTerm pathTerm: work.Push(pathTerm.Path); break;
            case Ast.TripleTerm tripleTerm: PushSequence(work, "<<( ", tripleTerm.Inner.Subject, " ", tripleTerm.Inner.Predicate, " ", tripleTerm.Inner.Object, " )>>"); break;

            case PropertyPathExpression path: ExpandPath(path, work); break;
            case ExpressionNode expression: ExpandExpression(expression, work); break;

            default: throw new NotSupportedException($"Cannot render '{node.GetType().Name}' to a federated SPARQL query.");
        }
    }

    /// <summary>Expands a group graph pattern: <c>{ member member … }</c>, members space-separated.</summary>
    /// <param name="group">The group.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandGroup(GroupGraphPattern group, Stack<object> work)
    {
        List<object> pieces = ["{ "];
        for(int i = 0; i < group.Members.Count; i++)
        {
            if(i > 0)
            {
                pieces.Add(" ");
            }

            pieces.Add(group.Members[i]);
        }

        pieces.Add(" }");
        PushSequence(work, [.. pieces]);
    }

    /// <summary>Expands a basic graph pattern block: each triple rendered <c>s p o .</c></summary>
    /// <param name="block">The block.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandBlock(BasicGraphPatternBlock block, Stack<object> work)
    {
        if(block.StandaloneNodes.Count > 0)
        {
            throw new NotSupportedException("A standalone reified triple inside a federated SERVICE pattern is not yet rendered.");
        }

        List<object> pieces = [];
        foreach(TriplePattern triple in block.Triples)
        {
            pieces.Add(triple);
            pieces.Add(" . ");
        }

        PushSequence(work, [.. pieces]);
    }

    /// <summary>Expands an inline <c>VALUES (vars) { rows }</c> block.</summary>
    /// <param name="values">The values clause.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandValues(ValuesClause values, Stack<object> work)
    {
        StringBuilder builder = new();
        builder.Append("VALUES (");
        foreach(SparqlVariable variable in values.Variables)
        {
            builder.Append(Variable(variable)).Append(' ');
        }

        builder.Append(") { ");
        foreach(IReadOnlyList<RdfTerm?> row in values.Rows)
        {
            builder.Append("( ");
            foreach(RdfTerm? cell in row)
            {
                builder.Append(cell is null ? "UNDEF" : SparqlResultTermText.Turtle(cell)).Append(' ');
            }

            builder.Append(") ");
        }

        builder.Append('}');
        work.Push(builder.ToString());
    }

    /// <summary>Expands a property path. Compound paths are parenthesised to preserve grouping.</summary>
    /// <param name="path">The path.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandPath(PropertyPathExpression path, Stack<object> work)
    {
        switch(path)
        {
            case PathPredicate predicate: work.Push(Iri(predicate.Predicate)); break;
            case PathInverse inverse: PushSequence(work, "^(", inverse.Inner, ")"); break;
            case PathZeroOrMore star: PushSequence(work, "(", star.Inner, ")*"); break;
            case PathOneOrMore plus: PushSequence(work, "(", plus.Inner, ")+"); break;
            case PathZeroOrOne option: PushSequence(work, "(", option.Inner, ")?"); break;
            case PathSequence sequence: ExpandPathList(sequence.Steps, "/", work); break;
            case PathAlternative alternative: ExpandPathList(alternative.Alternatives, "|", work); break;
            case PathNegatedSet negated: work.Push(NegatedSet(negated)); break;
            default: throw new NotSupportedException($"Cannot render property path '{path.GetType().Name}'.");
        }
    }

    /// <summary>Expands a sequence/alternative path: <c>( a SEP b SEP … )</c>.</summary>
    /// <param name="steps">The sub-paths.</param>
    /// <param name="separator">The infix separator (<c>/</c> or <c>|</c>).</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandPathList(IReadOnlyList<PropertyPathExpression> steps, string separator, Stack<object> work)
    {
        List<object> pieces = ["("];
        for(int i = 0; i < steps.Count; i++)
        {
            if(i > 0)
            {
                pieces.Add(separator);
            }

            pieces.Add(steps[i]);
        }

        pieces.Add(")");
        PushSequence(work, [.. pieces]);
    }

    /// <summary>Renders a negated property set <c>!( a | ^b | … )</c> (no nesting, so rendered directly).</summary>
    /// <param name="negated">The negated set.</param>
    /// <returns>The rendered text.</returns>
    private static string NegatedSet(PathNegatedSet negated)
    {
        StringBuilder builder = new();
        builder.Append("!(");
        for(int i = 0; i < negated.Elements.Count; i++)
        {
            if(i > 0)
            {
                builder.Append('|');
            }

            PathNegatedElement element = negated.Elements[i];
            if(element is PathNegatedInverse)
            {
                builder.Append('^');
            }

            builder.Append(Iri(element.Predicate));
        }

        builder.Append(')');

        return builder.ToString();
    }

    /// <summary>Expands an expression. Binary forms are fully parenthesised so precedence never changes.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandExpression(ExpressionNode expression, Stack<object> work)
    {
        switch(expression)
        {
            case ConstantExpression constant: work.Push(SparqlResultTermText.Turtle(constant.Value)); break;
            case VariableExpression variable: work.Push(Variable(variable.Variable)); break;
            case BoundExpression bound: work.Push("BOUND(" + Variable(bound.Variable) + ")"); break;
            case AndExpression and: PushSequence(work, "(", and.Left, " && ", and.Right, ")"); break;
            case OrExpression or: PushSequence(work, "(", or.Left, " || ", or.Right, ")"); break;
            case NotExpression not: PushSequence(work, "(!", not.Inner, ")"); break;
            case ComparisonExpression comparison: PushSequence(work, "(", comparison.Left, " " + ComparisonText(comparison.Op) + " ", comparison.Right, ")"); break;
            case ArithmeticExpression { Right: null } unary: PushSequence(work, "(" + ArithmeticText(unary.Op), unary.Left, ")"); break;
            case ArithmeticExpression arithmetic: PushSequence(work, "(", arithmetic.Left, " " + ArithmeticText(arithmetic.Op) + " ", arithmetic.Right!, ")"); break;
            case InExpression inExpression: ExpandCall(inExpression.Value, " IN ", inExpression.Set, work); break;
            case NotInExpression notIn: ExpandCall(notIn.Value, " NOT IN ", notIn.Set, work); break;
            case IfExpression conditional: ExpandArguments("IF", [conditional.Condition, conditional.IfTrue, conditional.IfFalse], work); break;
            case CoalesceExpression coalesce: ExpandArguments("COALESCE", coalesce.Alternatives, work); break;
            case BuiltInCallExpression builtIn: ExpandArguments(SparqlFunctions.ToCanonicalName(builtIn.Function), builtIn.Arguments, work); break;
            case FunctionCallExpression call: ExpandArguments(Iri(call.Function), call.Arguments, work, call.IsDistinct); break;
            case TripleTermExpression tripleTerm: PushSequence(work, "<<( ", tripleTerm.Inner.Subject, " ", tripleTerm.Inner.Predicate, " ", tripleTerm.Inner.Object, " )>>"); break;
            case ExistsExpression exists: PushSequence(work, "EXISTS ", exists.Inner); break;
            case NotExistsExpression notExists: PushSequence(work, "NOT EXISTS ", notExists.Inner); break;
            default: throw new NotSupportedException($"Cannot render expression '{expression.GetType().Name}' (an aggregate or unsupported form) in a federated SERVICE pattern.");
        }
    }

    /// <summary>Expands a <c>NAME(arg, arg, …)</c> call, with the argument list's leading <c>DISTINCT</c> when the call carries it.</summary>
    /// <param name="name">The function name or IRI text.</param>
    /// <param name="arguments">The argument expressions.</param>
    /// <param name="work">The work stack.</param>
    /// <param name="isDistinct">Whether the argument list opens with <c>DISTINCT</c>.</param>
    private static void ExpandArguments(string name, IReadOnlyList<ExpressionNode> arguments, Stack<object> work, bool isDistinct = false)
    {
        List<object> pieces = [name, isDistinct ? "(DISTINCT " : "("];
        for(int i = 0; i < arguments.Count; i++)
        {
            if(i > 0)
            {
                pieces.Add(", ");
            }

            pieces.Add(arguments[i]);
        }

        pieces.Add(")");
        PushSequence(work, [.. pieces]);
    }

    /// <summary>Expands an <c>(value IN (a, b, …))</c> / <c>NOT IN</c> membership test.</summary>
    /// <param name="value">The tested value expression.</param>
    /// <param name="keyword">The infix keyword (<c> IN </c> or <c> NOT IN </c>).</param>
    /// <param name="set">The candidate set.</param>
    /// <param name="work">The work stack.</param>
    private static void ExpandCall(ExpressionNode value, string keyword, IReadOnlyList<ExpressionNode> set, Stack<object> work)
    {
        List<object> pieces = ["(", value, keyword, "("];
        for(int i = 0; i < set.Count; i++)
        {
            if(i > 0)
            {
                pieces.Add(", ");
            }

            pieces.Add(set[i]);
        }

        pieces.Add("))");
        PushSequence(work, [.. pieces]);
    }

    /// <summary>Pushes the given pieces so they emit left-to-right (the stack is LIFO, so they go on in reverse).</summary>
    /// <param name="work">The work stack.</param>
    /// <param name="pieces">The pieces, in emit order.</param>
    private static void PushSequence(Stack<object> work, params object[] pieces)
    {
        for(int i = pieces.Length - 1; i >= 0; i--)
        {
            work.Push(pieces[i]);
        }
    }

    /// <summary>Renders a variable as <c>?name</c>.</summary>
    /// <param name="variable">The variable.</param>
    /// <returns>The rendered variable.</returns>
    private static string Variable(SparqlVariable variable)
    {
        return "?" + variable.Name.ToString();
    }

    /// <summary>Renders an IRI reference as an absolute <c>&lt;iri&gt;</c>.</summary>
    /// <param name="iri">The IRI reference.</param>
    /// <returns>The rendered IRI.</returns>
    private static string Iri(IriRef iri)
    {
        return "<" + iri.Value.ToString() + ">";
    }

    /// <summary>The SPARQL text of a comparison operator.</summary>
    /// <param name="op">The operator.</param>
    /// <returns>The operator text.</returns>
    private static string ComparisonText(ComparisonOp op)
    {
        return op switch
        {
            ComparisonOp.Equal => "=",
            ComparisonOp.NotEqual => "!=",
            ComparisonOp.LessThan => "<",
            ComparisonOp.LessOrEqual => "<=",
            ComparisonOp.GreaterThan => ">",
            ComparisonOp.GreaterOrEqual => ">=",
            _ => throw new NotSupportedException($"Unknown comparison operator {op}.")
        };
    }

    /// <summary>The SPARQL text of an arithmetic operator.</summary>
    /// <param name="op">The operator.</param>
    /// <returns>The operator text.</returns>
    private static string ArithmeticText(ArithmeticOp op)
    {
        return op switch
        {
            ArithmeticOp.Add => "+",
            ArithmeticOp.Subtract => "-",
            ArithmeticOp.Multiply => "*",
            ArithmeticOp.Divide => "/",
            ArithmeticOp.UnaryMinus => "-",
            ArithmeticOp.UnaryPlus => "+",
            _ => throw new NotSupportedException($"Unknown arithmetic operator {op}.")
        };
    }
}
