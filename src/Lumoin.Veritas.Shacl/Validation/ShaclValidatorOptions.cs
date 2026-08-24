using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Diagnostics;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Tuning options for <see cref="ShaclValidator.ValidateAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Options are immutable; use <c>with</c>-expressions to derive a
/// modified instance from <see cref="Default"/>.
/// </para>
/// <para>
/// Additional options may be added as the validator grows — for example
/// an <c>EnableRdfsInference</c> flag once
/// <see cref="Shacl.Constraints.ClassConstraint"/> has an evaluator. This
/// record exists now so the option-passing shape of
/// <see cref="ShaclValidator.ValidateAsync"/> does not change when later
/// options are introduced.
/// </para>
/// </remarks>
public sealed record ShaclValidatorOptions
{
    /// <summary>
    /// Maximum number of results to collect before the validator stops
    /// eagerly. <c>null</c> means no limit. When the cap is reached the
    /// run halts and the partial report is returned.
    /// </summary>
    public int? MaxResults { get; init; }

    /// <summary>
    /// When <c>true</c>, the validator halts as soon as the first
    /// validation result is produced — of any severity. Since
    /// <c>sh:conforms</c> is the absence of <em>any</em> result (SHACL §3.6),
    /// the first result already settles conformance. Useful for CI gates
    /// that only care whether the data conforms at all.
    /// </summary>
    public bool FailFast { get; init; }

    /// <summary>
    /// Optional structured-trace sink. When non-<c>null</c>, the
    /// orchestrator emits <see cref="ShaclTraceEvent"/>s at focus-node
    /// selection, constraint start / completion / not-implemented, and
    /// per-result production points. When <c>null</c>, tracing is
    /// zero-cost — the orchestrator skips event construction entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler is invoked synchronously on the validator's thread.
    /// Handlers must not block; if persistence or cross-thread dispatch
    /// is needed, forward into a <c>System.Threading.Channels.Channel</c>
    /// or equivalent via <see cref="TraceHandlers"/> adapters.
    /// </para>
    /// <para>
    /// All events in a single validation run share the same
    /// <see cref="Core.Diagnostics.ITraceEvent.CorrelationId"/>. Sequence
    /// numbers are monotonically increasing within the run.
    /// </para>
    /// </remarks>
    public TraceHandler<ShaclTraceEvent>? TraceHandler { get; init; }

    /// <summary>
    /// The identifier source used to mint the per-run correlation id shared by
    /// every trace event in the run. <c>null</c> uses
    /// <see cref="VeritasIdentifiers.System"/> (a fresh <see cref="System.Guid"/>
    /// per run). Pass <see cref="VeritasIdentifiers.Sequential"/> or a constant
    /// source for deterministic correlation ids in tests.
    /// </summary>
    public IdentifierDelegate? Identifiers { get; init; }

    /// <summary>
    /// The execution-strategy policy the run's <c>sh:sparql</c> constraint engine is built under
    /// (<see cref="SparqlEngineCache"/>); the default keeps the materialising executor. A host embedding the
    /// validator passes its engine-wide policy so constraint evaluation follows the same strategy as its queries.
    /// </summary>
    public SparqlEnginePolicy SparqlExecution { get; init; } = SparqlEnginePolicy.Default;

    /// <summary>
    /// The value-layer datatype registry <c>sh:datatype</c> consults for lexical forms whose datatype IRI is
    /// outside the modelled XSD set, and that the run's <c>sh:sparql</c> constraint engine carries so a
    /// constraint query's <c>=</c>/<c>!=</c> comparisons answer the same as the embedding host's queries.
    /// The default is <see cref="ValueDatatypeRegistry.Empty"/>, under which validation is IRI identity plus
    /// XSD well-formedness with every unmodelled datatype accepted — the consult costs one predicted branch
    /// and cannot change any result.
    /// </summary>
    public ValueDatatypeRegistry ValueDatatypes { get; init; } = ValueDatatypeRegistry.Empty;

    /// <summary>
    /// The extension-function registry the run's <c>sh:sparql</c> constraint engine carries, so a constraint
    /// query can invoke the same IRI-named functions (SPARQL §17.6) as the embedding host's queries. The
    /// default is <see cref="SparqlFunctionRegistry.Empty"/>, under which every extension-function IRI in a
    /// constraint query evaluates to the expression error value.
    /// </summary>
    public SparqlFunctionRegistry ExtensionFunctions { get; init; } = SparqlFunctionRegistry.Empty;

    /// <summary>The default options — no limit, no fail-fast, no tracing.</summary>
    public static ShaclValidatorOptions Default { get; } = new();
}
