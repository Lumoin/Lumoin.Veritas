using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <see cref="SparqlComponentConstraint"/> — a use of a SPARQL-based constraint component
/// (SHACL-SPARQL §6). Selects the component's node/property/generic validator, pre-binds <c>$this</c>,
/// <c>$PATH</c>, the parameter variables (and, for an ASK validator, <c>$value</c>), runs the validator query, and
/// maps results to violations.
/// </summary>
/// <remarks>
/// <para>
/// <b>ASK validator (§6.2.1):</b> the ASK query is evaluated once per value node with <c>$value</c> pre-bound; an
/// ASK that does not hold (no solution) is a violation for that value node.
/// </para>
/// <para>
/// <b>SELECT validator (§6.2.2):</b> the SELECT query runs once with the parameters/<c>$this</c>/<c>$PATH</c>
/// pre-bound; each result row is a violation (<c>?value</c>→<c>sh:value</c> or the focus node, <c>?path</c>→
/// <c>sh:resultPath</c> or the shape's path).
/// </para>
/// </remarks>
public static class SparqlComponentConstraintEvaluator
{
    /// <summary>The pre-bound <c>$this</c> variable (the focus node).</summary>
    private static SparqlVariable ThisVariable { get; } = new(new Utf8String("this"u8.ToArray()));

    /// <summary>The <c>$value</c> variable — pre-bound per value node for an ASK validator; a result variable for a SELECT validator.</summary>
    private static SparqlVariable ValueVariable { get; } = new(new Utf8String("value"u8.ToArray()));

    /// <summary>The pre-bound <c>$PATH</c> variable (the property shape's predicate path).</summary>
    private static SparqlVariable PathVariable { get; } = new(new Utf8String("PATH"u8.ToArray()));

    /// <summary>The pre-bound <c>$currentShape</c> variable (the shape being validated, §5.2.1).</summary>
    private static SparqlVariable CurrentShapeVariable { get; } = new(new Utf8String("currentShape"u8.ToArray()));

    /// <summary>The pre-bound <c>$shapesGraph</c> variable (the IRI of the shapes graph, §5.2.1).</summary>
    private static SparqlVariable ShapesGraphVariable { get; } = new(new Utf8String("shapesGraph"u8.ToArray()));

    /// <summary>The evaluator function. Matches the <see cref="ConstraintEvaluator"/> delegate shape.</summary>
    /// <param name="shape">The enclosing shape (for severity, source id, fallback message).</param>
    /// <param name="constraint">The <see cref="SparqlComponentConstraint"/> being evaluated.</param>
    /// <param name="focusNode">The focus node, pre-bound to <c>$this</c>.</param>
    /// <param name="valueNodes">The value nodes (the ASK validator runs per value node; the focus itself for a node shape).</param>
    /// <param name="path">The property shape's path, pre-bound to <c>$PATH</c> when it is a predicate path; <c>null</c> for a node shape.</param>
    /// <param name="context">The validation context (data graph, dictionary, per-run engine cache).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One <see cref="ValidationResult"/> per violation; empty when the constraint is satisfied or not executable.</returns>
    public static async ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        SparqlComponentConstraint componentConstraint = (SparqlComponentConstraint)constraint;
        SparqlComponentValidator? validator = componentConstraint.Definition.SelectValidator(isPropertyShape: path is not null);
        if(validator is null)
        {
            return [];
        }

        SparqlQueryEngine engine = await context.SparqlEngines.GetOrBuildAsync(
            context.DataMatchOps, context.ShapesGraphMatchOps, context.ShapesGraphIri, context.Dictionary, cancellationToken).ConfigureAwait(false);
        List<(SparqlVariable Variable, RdfTerm Value)> baseBindings = BuildBaseBindings(shape, componentConstraint, focusNode, path, context);

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();
        try
        {
            if(validator.IsAsk)
            {
                await EvaluateAskAsync(shape, componentConstraint, validator, focusNode, valueNodes, path, baseBindings, engine, context, builder, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await EvaluateSelectAsync(shape, componentConstraint, validator, focusNode, path, baseBindings, engine, context, builder, cancellationToken).ConfigureAwait(false);
            }
        }
        catch(NotSupportedException)
        {
            //The validator query uses a feature the engine does not yet execute; under-validate rather than abort.
            return [];
        }

        return builder.ToImmutable();
    }

    /// <summary>Runs an ASK validator once per value node, recording a violation for each value node whose ASK does not hold.</summary>
    private static async Task EvaluateAskAsync(
        Shape shape,
        SparqlComponentConstraint constraint,
        SparqlComponentValidator validator,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        List<(SparqlVariable Variable, RdfTerm Value)> baseBindings,
        SparqlQueryEngine engine,
        ValidationContext context,
        ImmutableArray<ValidationResult>.Builder builder,
        CancellationToken cancellationToken)
    {
        foreach(TermId valueNode in valueNodes)
        {
            List<(SparqlVariable Variable, RdfTerm Value)> bindings = new(baseBindings) { (ValueVariable, context.Dictionary.Resolve(valueNode)) };
            AlgebraOperator algebra = SparqlTranslator.Translate(SparqlPreBinding.Substitute(validator.Query, ToValues(bindings)), engine.ExtensionFunctions.AggregateIris);
            IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

            //ASK holds when the query has a solution; a non-holding ASK is a violation for this value node.
            if(solutions.Count == 0)
            {
                builder.Add(new ValidationResult
                {
                    FocusNode = focusNode,
                    ValueNode = valueNode,
                    ResultPath = path,
                    Severity = shape.Severity,
                    SourceShape = shape.Id,
                    SourceConstraintComponent = constraint.ConstraintComponentIri,
                    Messages = Messages(validator, shape),
                });
            }
        }
    }

    /// <summary>Runs a SELECT validator once, recording a violation per result row.</summary>
    private static async Task EvaluateSelectAsync(
        Shape shape,
        SparqlComponentConstraint constraint,
        SparqlComponentValidator validator,
        TermId focusNode,
        PropertyPath? path,
        List<(SparqlVariable Variable, RdfTerm Value)> baseBindings,
        SparqlQueryEngine engine,
        ValidationContext context,
        ImmutableArray<ValidationResult>.Builder builder,
        CancellationToken cancellationToken)
    {
        AlgebraOperator algebra = SparqlTranslator.Translate(SparqlPreBinding.Substitute(validator.Query, ToValues(baseBindings)), engine.ExtensionFunctions.AggregateIris);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        foreach(SparqlSolution solution in solutions)
        {
            TermId valueNode = solution.TryGetValue(ValueVariable, out RdfTerm value) ? context.Dictionary.GetOrAdd(value) : focusNode;
            PropertyPath? resultPath = solution.TryGetValue(PathVariable, out RdfTerm pathTerm) && pathTerm is NamedNode
                ? new PredicatePath(IriId.FromUnchecked(context.Dictionary.GetOrAdd(pathTerm)))
                : path;

            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ValueNode = valueNode,
                ResultPath = resultPath,
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = Messages(validator, shape),
            });
        }
    }

    /// <summary>Builds the per-evaluation pre-bindings shared by every value node: <c>$this</c>, <c>$currentShape</c>, <c>$PATH</c> (predicate-path property shapes), <c>$shapesGraph</c> (when the run supplies a shapes graph), and each provided parameter under its variable name (§5.2.1).</summary>
    private static List<(SparqlVariable Variable, RdfTerm Value)> BuildBaseBindings(Shape shape, SparqlComponentConstraint constraint, TermId focusNode, PropertyPath? path, ValidationContext context)
    {
        List<(SparqlVariable Variable, RdfTerm Value)> bindings =
        [
            (ThisVariable, context.Dictionary.Resolve(focusNode)),
            (CurrentShapeVariable, context.Dictionary.Resolve(shape.Id)),
        ];
        if(path is PredicatePath predicatePath)
        {
            bindings.Add((PathVariable, context.Dictionary.Resolve((TermId)predicatePath.Predicate)));
        }

        if(context.ShapesGraphIri is RdfTerm shapesGraphIri)
        {
            bindings.Add((ShapesGraphVariable, shapesGraphIri));
        }

        foreach(SparqlComponentParameter parameter in constraint.Definition.Parameters)
        {
            if(constraint.ParameterValues.TryGetValue(parameter.Path, out TermId value))
            {
                bindings.Add((new SparqlVariable(parameter.VariableName), context.Dictionary.Resolve(value)));
            }
        }

        return bindings;
    }

    /// <summary>Builds a single-row <c>VALUES</c> block from the given variable bindings.</summary>
    private static ValuesClause ToValues(List<(SparqlVariable Variable, RdfTerm Value)> bindings)
    {
        SparqlVariable[] variables = new SparqlVariable[bindings.Count];
        RdfTerm?[] row = new RdfTerm?[bindings.Count];
        for(int i = 0; i < bindings.Count; i++)
        {
            variables[i] = bindings[i].Variable;
            row[i] = bindings[i].Value;
        }

        return new ValuesClause(SourceSpan.None, variables, [row]);
    }

    /// <summary>The result messages: the validator's <c>sh:message</c> values if any, else the shape's.</summary>
    private static ImmutableDictionary<string, string> Messages(SparqlComponentValidator validator, Shape shape)
    {
        return validator.Messages.IsEmpty ? shape.Messages : validator.Messages;
    }
}
