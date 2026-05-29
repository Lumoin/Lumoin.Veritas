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
/// Evaluator for <see cref="SparqlConstraint"/> (<c>sh:SPARQLConstraintComponent</c>): runs the constraint's
/// SELECT query against the data graph with <c>$this</c> pre-bound to the focus node, and maps each result row
/// to a violation (SHACL-SPARQL §5.2/§5.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pre-binding.</b> SHACL-SPARQL §5.2 pre-binds <c>$this</c> to the focus node. This is realized by setting
/// the query's trailing <c>VALUES (?this) { (focus) }</c> block, which the translator joins into the WHERE
/// pattern below the projection, so every result row has <c>?this</c> = the focus node. (A constraint query that
/// already carries a trailing <c>VALUES</c> is not expected in this slice; the pre-binding overwrites it.)
/// </para>
/// <para>
/// <b>Result mapping (§5.3).</b> Each result row is one violation. The row's <c>?value</c> binding becomes
/// <c>sh:value</c>; the result message is the row's <c>?message</c> binding, else the constraint's
/// <c>sh:message</c>, else the shape's. The result path is the property shape's path; honoring a per-row
/// <c>?path</c> binding is a later refinement.
/// </para>
/// <para>
/// <b>Unsupported query features.</b> If translating or executing the query reaches an operator the engine does
/// not yet support (named graphs, <c>SERVICE</c>, <c>EXISTS</c>/<c>NOT EXISTS</c>), the constraint produces no
/// results — the shape under-validates rather than aborting the run. Those features land in later engine slices.
/// </para>
/// </remarks>
public static class SparqlConstraintEvaluator
{
    /// <summary>The SHACL-SPARQL <c>$this</c> variable, pre-bound to the focus node.</summary>
    private static SparqlVariable ThisVariable { get; } = new(new Utf8String("this"u8.ToArray()));

    /// <summary>The SHACL-SPARQL <c>$PATH</c> variable, pre-bound (on a property shape with a predicate path) to the path predicate IRI (§5.2.1).</summary>
    private static SparqlVariable PathPreBindingVariable { get; } = new(new Utf8String("PATH"u8.ToArray()));

    /// <summary>The SHACL-SPARQL <c>?value</c> result variable, mapped to <c>sh:value</c>.</summary>
    private static SparqlVariable ValueVariable { get; } = new(new Utf8String("value"u8.ToArray()));

    /// <summary>The SHACL-SPARQL <c>?path</c> result variable, mapped to <c>sh:resultPath</c> when bound to an IRI.</summary>
    private static SparqlVariable PathVariable { get; } = new(new Utf8String("path"u8.ToArray()));

    /// <summary>The SHACL-SPARQL <c>?message</c> result variable, mapped to <c>sh:resultMessage</c>.</summary>
    private static SparqlVariable MessageVariable { get; } = new(new Utf8String("message"u8.ToArray()));

    /// <summary>
    /// The evaluator function. Matches the <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    /// <param name="shape">The enclosing shape (for severity, source id, fallback message).</param>
    /// <param name="constraint">The <see cref="SparqlConstraint"/> being evaluated.</param>
    /// <param name="focusNode">The focus node, pre-bound to <c>$this</c>.</param>
    /// <param name="valueNodes">The value nodes (unused; a SPARQL constraint selects its own violating nodes).</param>
    /// <param name="path">The property shape's path, used as the result path; <c>null</c> for a node shape.</param>
    /// <param name="context">The validation context (data graph, dictionary, per-run engine cache).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One <see cref="ValidationResult"/> per result row; empty when the constraint is satisfied or not yet executable.</returns>
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
        _ = valueNodes;
        cancellationToken.ThrowIfCancellationRequested();

        SparqlConstraint sparqlConstraint = (SparqlConstraint)constraint;
        SparqlQueryEngine engine = await context.SparqlEngines.GetOrBuildAsync(
            context.DataMatchOps, context.Dictionary, cancellationToken).ConfigureAwait(false);

        //Pre-bind $this (and, on a predicate-path property shape, $PATH) via an inline VALUES at the front of the
        //WHERE group (so a lifted FILTER sees the bindings — a trailing VALUES would join too late).
        RdfTerm focusTerm = context.Dictionary.Resolve(focusNode);
        SparqlQuery bound = SparqlPreBinding.WithInlineValues(sparqlConstraint.Query, BuildPreBindings(focusTerm, path, context));

        IReadOnlyList<SparqlSolution> solutions;
        try
        {
            AlgebraOperator algebra = SparqlTranslator.Translate(bound);
            solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);
        }
        catch(NotSupportedException)
        {
            //The query uses a feature the engine does not yet execute; under-validate rather than abort.
            return [];
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();
        foreach(SparqlSolution solution in solutions)
        {
            //SHACL-SPARQL §5.3: sh:value is the row's ?value binding, or the focus node when ?value is unbound.
            TermId valueNode = solution.TryGetValue(ValueVariable, out RdfTerm value)
                ? context.Dictionary.GetOrAdd(value)
                : focusNode;

            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ValueNode = valueNode,
                ResultPath = ResolveResultPath(solution, path, context),
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                SourceConstraint = sparqlConstraint.ConstraintNode,
                Messages = ResolveMessages(solution, sparqlConstraint, shape),
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Resolves the result path (§5.3): a row's <c>?path</c> binding to an IRI becomes a predicate path; otherwise
    /// the property shape's path is used (and is <see langword="null"/> for a node shape). A non-IRI <c>?path</c>
    /// binding is ignored — encoding an arbitrary SHACL path expression from a result term is a later refinement.
    /// </summary>
    /// <param name="solution">The result row.</param>
    /// <param name="shapePath">The enclosing shape's path, or <see langword="null"/> for a node shape.</param>
    /// <param name="context">The validation context (for encoding the path IRI into the run's dictionary).</param>
    /// <returns>The result path, or <see langword="null"/> when there is none.</returns>
    private static PropertyPath? ResolveResultPath(SparqlSolution solution, PropertyPath? shapePath, ValidationContext context)
    {
        if(solution.TryGetValue(PathVariable, out RdfTerm path) && path is NamedNode)
        {
            return new PredicatePath(IriId.FromUnchecked(context.Dictionary.GetOrAdd(path)));
        }

        return shapePath;
    }

    /// <summary>
    /// Builds the single-row trailing <c>VALUES</c> block that pre-binds <c>$this</c> to the focus node, and — on a
    /// property shape whose path is a single predicate — <c>$PATH</c> to that predicate IRI (SHACL-SPARQL §5.2.1).
    /// A complex path (sequence/alternative/inverse/cardinality) is not pre-bound as a single IRI (a later refinement).
    /// </summary>
    /// <param name="focusTerm">The focus node as an RDF term.</param>
    /// <param name="path">The property shape's path, or <see langword="null"/> for a node shape.</param>
    /// <param name="context">The validation context (for resolving the path predicate IRI to a term).</param>
    /// <returns>The inline-data block pre-binding <c>$this</c> (and <c>$PATH</c> when applicable).</returns>
    private static ValuesClause BuildPreBindings(RdfTerm focusTerm, PropertyPath? path, ValidationContext context)
    {
        if(path is PredicatePath predicatePath)
        {
            RdfTerm pathTerm = context.Dictionary.Resolve((TermId)predicatePath.Predicate);

            return new ValuesClause(SourceSpan.None, [ThisVariable, PathPreBindingVariable], [new RdfTerm?[] { focusTerm, pathTerm }]);
        }

        return new ValuesClause(SourceSpan.None, [ThisVariable], [new RdfTerm?[] { focusTerm }]);
    }

    /// <summary>
    /// Resolves the result message (§5.3): the row's <c>?message</c> literal if bound, else the constraint's
    /// <c>sh:message</c> values, else the shape's.
    /// </summary>
    /// <param name="solution">The result row.</param>
    /// <param name="constraint">The SPARQL constraint (carries the constraint-node <c>sh:message</c>).</param>
    /// <param name="shape">The enclosing shape (carries the fallback <c>sh:message</c>).</param>
    /// <returns>The messages keyed by language tag.</returns>
    private static ImmutableDictionary<string, string> ResolveMessages(SparqlSolution solution, SparqlConstraint constraint, Shape shape)
    {
        if(solution.TryGetValue(MessageVariable, out RdfTerm message) && message is Literal literal)
        {
            string language = literal.Language is { } tag ? tag.ToString() : string.Empty;

            return ImmutableDictionary<string, string>.Empty.Add(language, literal.Value.ToString());
        }

        if(!constraint.Messages.IsEmpty)
        {
            return constraint.Messages;
        }

        return shape.Messages;
    }
}
