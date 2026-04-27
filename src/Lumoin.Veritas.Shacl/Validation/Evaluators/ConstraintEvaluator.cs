using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// The delegate shape for a SHACL constraint evaluator. One evaluator
/// is registered per constraint component IRI (e.g., the evaluator for
/// <c>sh:MinCountConstraintComponent</c>). The orchestrator invokes an
/// evaluator once per <c>(focus node, constraint)</c> pair.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="ValueTask{TResult}"/>.</b> Many SHACL evaluators
/// are purely synchronous (<see cref="MinCountConstraint"/>,
/// <see cref="PatternConstraint"/>, <see cref="InConstraint"/>,
/// datatype and node-kind checks) — they inspect the value nodes and
/// return immediately. <see cref="ValueTask{TResult}"/> lets those
/// return without allocating a backing <see cref="Task{TResult}"/>.
/// Asynchronous evaluators (class-membership traversal, recursive
/// shape validation via <see cref="NodeConstraint"/> or
/// <see cref="PropertyConstraint"/>) behave as normal async methods.
/// </para>
/// <para>
/// <b>Parameters.</b> The full evaluation context is present as
/// parameters. <paramref name="shape"/> is the enclosing shape (for
/// severity, source-id, and messages). <paramref name="constraint"/> is
/// the specific constraint instance to evaluate.
/// <paramref name="focusNode"/> is the single focus node under
/// evaluation. <paramref name="valueNodes"/> is the value-node set
/// derived either from the focus node itself (for node shapes) or from
/// evaluating <paramref name="path"/> against the focus node (for
/// property shapes). <paramref name="path"/> is <c>null</c> for node
/// shapes and the evaluated path for property shapes.
/// <paramref name="context"/> carries the data-graph match delegate,
/// shape registry, dictionary, and options.
/// </para>
/// <para>
/// <b>Return.</b> An <see cref="ImmutableArray{T}"/> of results, empty
/// when the constraint is satisfied. Evaluators must not throw for
/// data-conformance failures; they return results. Throwing is
/// reserved for validator bugs and cancellation.
/// </para>
/// </remarks>
/// <param name="shape">The shape that owns <paramref name="constraint"/>.</param>
/// <param name="constraint">The constraint component to evaluate.</param>
/// <param name="focusNode">The current focus node.</param>
/// <param name="valueNodes">
/// The value nodes to validate: either the singleton focus node (node
/// shapes) or the image of the focus node under the path (property shapes).
/// </param>
/// <param name="path">The evaluated path, or <c>null</c> for node shapes.</param>
/// <param name="context">The validation-run-wide environment.</param>
/// <param name="cancellationToken">Cancellation.</param>
/// <returns>An immutable array of validation results; empty if the constraint is satisfied.</returns>
public delegate ValueTask<ImmutableArray<ValidationResult>> ConstraintEvaluator(
    Shape shape,
    ConstraintComponent constraint,
    TermId focusNode,
    ImmutableArray<TermId> valueNodes,
    PropertyPath? path,
    ValidationContext context,
    CancellationToken cancellationToken);
