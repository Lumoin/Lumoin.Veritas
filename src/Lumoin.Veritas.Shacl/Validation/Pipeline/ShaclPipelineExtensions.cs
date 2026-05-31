using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Pipeline;

/// <summary>
/// Fluent extension methods for <see cref="ShaclPipelineDataState"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Evaluator registration.</b>
/// <see cref="WithEvaluator"/> appends to the state's accumulating
/// evaluator dictionary, returning the same state instance so calls
/// chain.
/// </para>
/// <para>
/// <b>Terminal.</b> <see cref="RunAsync(ShaclPipelineDataState, CancellationToken)"/>
/// builds a <see cref="ConstraintEvaluatorRegistry"/> from the
/// accumulator and runs
/// <see cref="ShaclValidator.ValidateAsync"/> with default options.
/// The
/// <see cref="RunAsync(ShaclPipelineDataState, ShaclValidatorOptions, CancellationToken)"/>
/// overload accepts pre-built options for trace handlers, fail-fast,
/// or result caps.
/// </para>
/// </remarks>
public static class ShaclPipelineExtensions
{
    /// <summary>
    /// Registers an evaluator for the given constraint-component
    /// IRI. The terminal <see cref="RunAsync(ShaclPipelineDataState, CancellationToken)"/>
    /// uses these registrations to build the
    /// <see cref="ConstraintEvaluatorRegistry"/>.
    /// </summary>
    public static ShaclPipelineDataState WithEvaluator(
        this ShaclPipelineDataState state,
        Utf8String constraintComponentIri,
        ConstraintEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(evaluator);

        state.Evaluators[constraintComponentIri] = evaluator;

        return state;
    }

    /// <summary>
    /// Runs the pipeline with default options. Consumers needing
    /// trace handlers, fail-fast, or result caps should use the
    /// overload that accepts <see cref="ShaclValidatorOptions"/>.
    /// </summary>
    public static async Task<ValidationReport> RunAsync(
        this ShaclPipelineDataState state,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ConstraintEvaluatorRegistry evaluators = new(state.Evaluators);

        return await ShaclValidator.ValidateAsync(
            state.Shapes,
            state.DataMatchOps,
            state.Dictionary,
            evaluators,
            timeProvider,
            options: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the pipeline with caller-supplied
    /// <see cref="ShaclValidatorOptions"/>. Used by test fixtures to
    /// inject a trace handler, and by production consumers for
    /// fail-fast and result-cap configuration.
    /// </summary>
    public static async Task<ValidationReport> RunAsync(
        this ShaclPipelineDataState state,
        TimeProvider timeProvider,
        ShaclValidatorOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        ConstraintEvaluatorRegistry evaluators = new(state.Evaluators);

        return await ShaclValidator.ValidateAsync(
            state.Shapes,
            state.DataMatchOps,
            state.Dictionary,
            evaluators,
            timeProvider,
            options: options,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
