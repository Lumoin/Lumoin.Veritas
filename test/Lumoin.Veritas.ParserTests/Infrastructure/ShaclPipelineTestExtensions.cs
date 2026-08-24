using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Pipeline;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Test-only extensions for <see cref="ShaclPipelineDataState"/>.
/// </summary>
/// <remarks>
/// <para>
/// The library's
/// <see cref="ShaclPipelineExtensions.RunAsync(ShaclPipelineDataState, CancellationToken)"/>
/// is trace-agnostic. Tests that want trace-on-failure use
/// <see cref="RunWithTraceAsync"/> instead — it allocates a fresh
/// <see cref="ValidationTrace"/>, plumbs it through the validator's
/// options, and returns both report and trace so
/// <see cref="ValidationAssertions"/> can dump them on failure.
/// </para>
/// </remarks>
internal static class ShaclPipelineTestExtensions
{
    /// <summary>
    /// Runs the pipeline with trace capture enabled and returns the
    /// produced report alongside the captured trace.
    /// </summary>
    public static async Task<(ValidationReport Report, ValidationTrace Trace)> RunWithTraceAsync(
        this ShaclPipelineDataState state,
        CancellationToken cancellationToken)
    {
        ValidationTrace trace = new();

        ShaclValidatorOptions options = ShaclValidatorOptions.Default with
        {
            TraceHandler = trace.Capture,
        };

        ValidationReport report = await state.RunAsync(VeritasClock.System, options, cancellationToken).ConfigureAwait(false);

        return (report, trace);
    }
}
