using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Diagnostics;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Targets;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Orchestrator that validates a data graph against a loaded
/// <see cref="ShapeRegistry"/>, emitting a <see cref="ValidationReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Specification.</b> Implements the validation algorithm defined
/// in SHACL 1.2 Core §3.4
/// (<a href="https://www.w3.org/TR/shacl12-core/#validation-definition">Validation</a>).
/// Every behavioural choice in this orchestrator is pinned by a spec
/// section; references are inline at each decision point.
/// </para>
/// <para>
/// <b>The algorithm.</b> Per §3.4 the validator iterates every
/// non-deactivated shape in the registry, expands its targets into
/// focus nodes per §2.1.3
/// (<a href="https://www.w3.org/TR/shacl12-core/#targets">Targets</a>),
/// and for each focus runs every registered constraint evaluator.
/// Results are collected into a report whose
/// <see cref="ValidationReport.Conforms"/> flag is <c>true</c> iff no
/// result of <see cref="Severity.Violation"/> severity was produced
/// (§3.6,
/// <a href="https://www.w3.org/TR/shacl12-core/#results-conforms">Conformance</a>).
/// </para>
/// <para>
/// <b>Two distinct empty-set cases.</b> The validator treats two
/// "empty" cases differently and the distinction matters:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Empty focus-node set.</b> A shape whose targets expand to
///   zero focus nodes is not validated — there is nothing to validate
///   against. This is consistent with §3.4's per-focus iteration: an
///   empty iteration produces no results. The orchestrator skips such
///   shapes outright.
///   </description></item>
///   <item><description>
///   <b>Empty value-node set.</b> A focus whose property-path
///   evaluation produces zero value nodes <em>still</em> dispatches
///   every constraint on the shape. Cardinality constraints
///   (<c>sh:minCount</c>, <c>sh:qualifiedMinCount</c>) report
///   violations exactly in this case (§4.5.1, §4.7.4); skipping
///   dispatch on empty value sets would silently miss them.
///   </description></item>
/// </list>
/// <para>
/// <b>Recursion.</b> The shape-recursion evaluators
/// (<c>sh:node</c>, <c>sh:property</c>, <c>sh:and</c>, <c>sh:or</c>,
/// <c>sh:not</c>, <c>sh:xone</c>, <c>sh:qualifiedValueShape</c>) need
/// to validate inner shapes at arbitrary focus nodes. The
/// orchestrator exposes this capability through the
/// <see cref="ValidationContext.ShapeValidator"/> delegate, which
/// wraps the per-focus validation body. A recursion guard — a
/// <see cref="HashSet{T}"/> of in-progress <c>(shape.Id, focus)</c>
/// pairs — breaks cycles by treating a re-entered pair as
/// conforming. SHACL 1.2 Core §3.5
/// (<a href="https://www.w3.org/TR/shacl12-core/#shape-recursion">Recursion</a>)
/// permits multiple interpretations of recursive shapes; this
/// orchestrator chooses the "treat re-entry as conforming" semantics,
/// which guarantees termination on every shape graph and is the
/// behaviour W3C SHACL test cases of recursive shapes exercise.
/// </para>
/// <para>
/// <b>Tracing.</b> When
/// <see cref="ShaclValidatorOptions.TraceHandler"/> is non-<c>null</c>
/// the orchestrator emits <see cref="ShaclTraceEvent"/>s as it runs,
/// both at the top level and inside recursive inner validations.
/// Trace events from recursion carry the same correlation ID, so
/// consumers can reconstruct the full call tree from the event stream.
/// </para>
/// <para>
/// <b>Early termination.</b> <see cref="ShaclValidatorOptions.MaxResults"/>
/// caps the total result count; <see cref="ShaclValidatorOptions.FailFast"/>
/// halts the run on the first violation. Termination is observed by
/// the outer loop; recursion evaluators operate below the
/// result-collection layer and do not participate in early termination.
/// Neither option is required by the spec — both are pragmatic
/// extensions for CI gates and large-graph diagnosis.
/// </para>
/// </remarks>
public static class ShaclValidator
{
    /// <summary>
    /// Runs validation over <paramref name="shapes"/> against the data
    /// graph exposed by <paramref name="dataGraphMatch"/>, dispatching
    /// constraints through <paramref name="evaluators"/>.
    /// </summary>
    /// <param name="shapes">The loaded shape registry.</param>
    /// <param name="dataGraphMatch">Match delegate over the data graph.</param>
    /// <param name="dictionary">
    /// The term dictionary shared by the shape graph and the data graph.
    /// </param>
    /// <param name="evaluators">The evaluator registry.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Pass <see cref="TimeProvider.System"/> in production; tests pinning trace timing pass a <c>FakeTimeProvider</c>.</param>
    /// <param name="options">Tuning options; <c>null</c> uses <see cref="ShaclValidatorOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A populated <see cref="ValidationReport"/>.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
    public static async Task<ValidationReport> ValidateAsync(
        ShapeRegistry shapes,
        StorageDelegates.MatchTriplesAsync dataGraphMatch,
        TermDictionary dictionary,
        ConstraintEvaluatorRegistry evaluators,
        TimeProvider timeProvider,
        ShaclValidatorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(dataGraphMatch);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ShaclValidatorOptions effectiveOptions = options ?? ShaclValidatorOptions.Default;

        //Pre-resolve the RDF list vocabulary once per run so
        //list-walking evaluators (sh:minListLength, sh:maxListLength,
        //sh:uniqueMembers, sh:memberShape) consume the IDs directly
        //via ValidationContext rather than repeating the dictionary
        //lookup on each invocation.
        IriId rdfFirstId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.First));
        IriId rdfRestId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Rest));
        IriId rdfNilId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Nil));

        //Gather all shared per-run state onto RunState. The
        //ShapeValidator delegate installed onto ValidationContext is
        //method-group-converted from RunState.InvokeRecurseAsync, so
        //its closure captures only the runState local — never a
        //parameter of this method. The convention forbids lambda
        //closures over method parameters; RunState is the indirection
        //that makes the delegate conform to it.
        RunState runState = new()
        {
            CorrelationId = Guid.NewGuid(),
            SequenceCounter = 0,
            TraceHandler = effectiveOptions.TraceHandler,
            InProgress = [],
            Evaluators = evaluators,
            DataGraphMatch = dataGraphMatch,
            TimeProvider = timeProvider,
        };

        ValidationContext context = new()
        {
            DataGraphMatch = dataGraphMatch,
            Shapes = shapes,
            Dictionary = dictionary,
            Options = effectiveOptions,
            RdfFirstId = rdfFirstId,
            RdfRestId = rdfRestId,
            RdfNilId = rdfNilId,
            ShapeValidator = runState.InvokeRecurseAsync,
        };

        runState.Context = context;

        List<ValidationResult> collected = [];
        bool conforms = true;
        bool stop = false;

        foreach(Shape shape in shapes.AllShapes)
        {
            if(stop)
            {
                break;
            }

            if(shape.Deactivated)
            {
                continue;
            }

            HashSet<TermId> focusNodes = [];
            foreach(Target target in shape.Targets)
            {
                await foreach(TermId focus in target.ExpandAsync(dataGraphMatch, cancellationToken).ConfigureAwait(false))
                {
                    focusNodes.Add(focus);
                }
            }

            if(focusNodes.Count == 0)
            {
                //Empty focus-node set: this shape's targets did not
                //match any subject in the data graph, so there is
                //nothing to validate against. Spec-correct skip per
                //§3.4 (validation iterates over focus nodes — an
                //empty iteration produces no results). NOTE: this is
                //distinct from the empty-VALUE-set case, which the
                //per-focus body (ValidateAtFocusAsync) handles by
                //dispatching constraints regardless of value-node
                //count. Cardinality constraints depend on that
                //distinction.
                continue;
            }

            foreach(TermId focus in focusNodes)
            {
                if(stop)
                {
                    break;
                }

                ImmutableArray<ValidationResult> focusResults = await ValidateAtFocusAsync(
                    shape, focus, runState, cancellationToken).ConfigureAwait(false);

                foreach(ValidationResult result in focusResults)
                {
                    if(effectiveOptions.MaxResults is int cap && collected.Count >= cap)
                    {
                        stop = true;
                        break;
                    }

                    if(result.Severity == Severity.Violation)
                    {
                        conforms = false;
                        if(effectiveOptions.FailFast)
                        {
                            stop = true;
                        }
                    }

                    collected.Add(result);
                }
            }
        }

        return new ValidationReport
        {
            Conforms = conforms,
            Results = [.. collected],
        };
    }

    //Per-focus validation body — runs every constraint on this shape
    //for this focus, emits trace events, returns the results. The
    //outer loop invokes this at top level; recursion evaluators
    //reach it through RunState.InvokeRecurseAsync, which wraps it in
    //the cycle guard.
    //
    //Per §3.4: constraints are dispatched for every (shape, focus)
    //pair, regardless of how many value nodes the shape's path
    //evaluates to. Cardinality constraints (sh:minCount §4.5.1,
    //sh:qualifiedMinCount §4.7.4) report violations precisely on the
    //zero-value case, so the constraint loop must iterate even when
    //valueNodes is empty.
    private static async ValueTask<ImmutableArray<ValidationResult>> ValidateAtFocusAsync(
        Shape shape,
        TermId focus,
        RunState runState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TraceHandler<ShaclTraceEvent>? traceHandler = runState.TraceHandler;

        if(traceHandler is not null)
        {
            ShaclTraceEvent evt = ShaclTraceEvent.FocusNodeSelected(
                runState.NextSequence(),
                runState.TimeProvider.GetUtcNow().UtcTicks,
                runState.CorrelationId,
                focus,
                shape.Id);
            traceHandler(in evt);
        }

        PropertyPath? path = shape is PropertyShape propertyShape ? propertyShape.Path : null;
        ImmutableArray<TermId> valueNodes = await ComputeValueNodesAsync(
            focus, path, runState.DataGraphMatch, cancellationToken).ConfigureAwait(false);

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(ConstraintComponent constraint in shape.Constraints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Utf8String componentIri = constraint.ConstraintComponentIri;
            bool isRegistered = runState.Evaluators.IsRegistered(componentIri);

            if(traceHandler is not null)
            {
                string componentIriString = componentIri.ToString();
                ShaclTraceEvent evt = isRegistered
                    ? ShaclTraceEvent.ConstraintStarted(
                        runState.NextSequence(),
                        runState.TimeProvider.GetUtcNow().UtcTicks,
                        runState.CorrelationId,
                        focus,
                        shape.Id,
                        componentIriString)
                    : ShaclTraceEvent.ConstraintNotImplemented(
                        runState.NextSequence(),
                        runState.TimeProvider.GetUtcNow().UtcTicks,
                        runState.CorrelationId,
                        focus,
                        shape.Id,
                        componentIriString);
                traceHandler(in evt);
            }

            ConstraintEvaluator evaluator = runState.Evaluators.GetOrDefault(componentIri);
            ImmutableArray<ValidationResult> results = await evaluator(
                shape,
                constraint,
                focus,
                valueNodes,
                path,
                runState.Context!,
                cancellationToken).ConfigureAwait(false);

            foreach(ValidationResult result in results)
            {
                if(traceHandler is not null)
                {
                    ShaclTraceEvent evt = ShaclTraceEvent.ResultProduced(
                        runState.NextSequence(),
                        runState.TimeProvider.GetUtcNow().UtcTicks,
                        runState.CorrelationId,
                        focus,
                        shape.Id,
                        componentIri.ToString(),
                        result.ValueNode ?? TermId.None,
                        result.Severity);
                    traceHandler(in evt);
                }

                builder.Add(result);
            }

            if(traceHandler is not null)
            {
                ConstraintEvaluationStatus status = results.IsEmpty
                    ? ConstraintEvaluationStatus.Passed
                    : ConstraintEvaluationStatus.Failed;
                ShaclTraceEvent evt = ShaclTraceEvent.ConstraintCompleted(
                    runState.NextSequence(),
                    runState.TimeProvider.GetUtcNow().UtcTicks,
                    runState.CorrelationId,
                    focus,
                    shape.Id,
                    componentIri.ToString(),
                    status);
                traceHandler(in evt);
            }
        }

        return builder.ToImmutable();
    }

    //Per-focus value-node computation. Per §2.4 (Value Nodes):
    //  - Node shapes have value-node set [focus].
    //  - Property shapes have the value-node set produced by
    //    evaluating the path from the focus per §2.3
    //    (Property Paths).
    //PropertyPathEvaluator dedupes internally; the result is the
    //distinct value-node set required by the spec.
    private static async Task<ImmutableArray<TermId>> ComputeValueNodesAsync(
        TermId focus,
        PropertyPath? path,
        StorageDelegates.MatchTriplesAsync dataGraphMatch,
        CancellationToken cancellationToken)
    {
        if(path is null)
        {
            return [focus];
        }

        List<TermId> values = [];
        await foreach(TermId value in PropertyPathEvaluator.EvaluateAsync(
            focus, path, dataGraphMatch, cancellationToken).ConfigureAwait(false))
        {
            values.Add(value);
        }
        return [.. values];
    }

    //Per-run mutable state. Installed on ValidationContext via
    //InvokeRecurseAsync, which is method-group-converted to the
    //ShapeValidator delegate. Because RunState is a local allocated
    //inside ValidateAsync, the delegate captures only a local
    //reference — never a parameter — satisfying the
    //no-closure-over-parameters convention.
    private sealed class RunState
    {
        /// <summary>
        /// Correlation id stamped on every <see cref="ShaclTraceEvent"/>
        /// emitted by this validation run. One id per call to
        /// <see cref="ValidateAsync"/>; consumers join trace events
        /// from the same run by this value.
        /// </summary>
        public required Guid CorrelationId { get; init; }

        /// <summary>
        /// Monotonically-increasing event sequence stamped on each
        /// emitted <see cref="ShaclTraceEvent"/>. Mutable because
        /// every emission post-increments it; see
        /// <see cref="NextSequence"/>.
        /// </summary>
        public required long SequenceCounter { get; set; }

        /// <summary>
        /// Caller-supplied trace handler, or <c>null</c> when the
        /// run is untraced. Every emission site checks this for
        /// <c>null</c> before constructing the event payload.
        /// </summary>
        public required TraceHandler<ShaclTraceEvent>? TraceHandler { get; init; }

        /// <summary>
        /// Cycle-detection set carrying the
        /// <c>(shape, focus)</c> pairs currently in flight on this
        /// validation's call stack. <see cref="InvokeRecurseAsync"/>
        /// adds on entry and removes on exit; re-entering a pair
        /// triggers the SHACL §3.5 recursion termination rule.
        /// </summary>
        public required HashSet<(TermId ShapeId, TermId Focus)> InProgress { get; init; }

        /// <summary>
        /// The pluggable registry of constraint-component
        /// evaluators keyed by component IRI. Looked up once per
        /// constraint during <c>ValidateAtFocusAsync</c>.
        /// </summary>
        public required ConstraintEvaluatorRegistry Evaluators { get; init; }

        /// <summary>
        /// The match delegate over the data graph (the graph being
        /// validated, distinct from the shape graph). Threaded into
        /// per-focus value-node computation and into individual
        /// constraint evaluators via <see cref="Context"/>.
        /// </summary>
        public required StorageDelegates.MatchTriplesAsync DataGraphMatch { get; init; }

        /// <summary>
        /// Clock used to stamp <see cref="ShaclTraceEvent.TimestampTicks"/>
        /// on emitted trace events. Pass <see cref="System.TimeProvider.System"/>
        /// in production; tests pinning trace timing pass a
        /// <c>FakeTimeProvider</c>.
        /// </summary>
        public required TimeProvider TimeProvider { get; init; }

        /// <summary>
        /// The composed <see cref="ValidationContext"/> handed to
        /// constraint evaluators. Set lazily after construction
        /// because the context's <see cref="ValidationContext.ShapeValidator"/>
        /// is the method-group-converted <see cref="InvokeRecurseAsync"/>,
        /// which can only be referenced after the
        /// <see cref="RunState"/> instance exists. Non-null
        /// throughout validation.
        /// </summary>
        public ValidationContext? Context { get; set; }

        /// <summary>
        /// Post-increments <see cref="SequenceCounter"/> and returns
        /// the prior value, producing a monotonically-increasing
        /// sequence number stamped on every emitted trace event.
        /// </summary>
        public long NextSequence() => SequenceCounter++;

        //Method-group-converted to ShapeValidatorDelegate and stored
        //on ValidationContext.ShapeValidator. Adds the cycle guard
        //and dispatches to ValidateAtFocusAsync; keeping the guard
        //here localises the "am I already in this pair" bookkeeping.
        public async ValueTask<ImmutableArray<ValidationResult>> InvokeRecurseAsync(
            Shape shape,
            TermId focus,
            CancellationToken cancellationToken)
        {
            if(shape.Deactivated)
            {
                return [];
            }

            (TermId ShapeId, TermId Focus) key = (shape.Id, focus);
            if(!InProgress.Add(key))
            {
                //Cycle: (shape, focus) is already on the stack. Per
                //§3.5 (Recursion), a SHACL processor may interpret
                //recursive shape definitions in several ways. This
                //orchestrator chooses "treat re-entered (shape,
                //focus) as conforming", which (a) guarantees
                //termination on every shape graph regardless of
                //recursion depth and (b) matches the behaviour W3C
                //recursive-shape test cases exercise.
                return [];
            }

            try
            {
                return await ValidateAtFocusAsync(shape, focus, this, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                InProgress.Remove(key);
            }
        }
    }
}
