using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.Shacl.Validation.Pipeline;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.ParserTests.Infrastructure;

/// <summary>
/// Fluent extension methods for the two-phase test pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape-stage extensions</b> operate on
/// <see cref="TestShaclPipelineShapeState"/>. Each declares some
/// piece of the shape graph through the wrapped
/// <see cref="ShapeGraphBuilder"/>, returning the state instance so
/// calls chain.
/// </para>
/// <para>
/// <b>Build transition.</b>
/// <see cref="BuildAsync(TestShaclPipelineShapeState, CancellationToken)"/>
/// finishes the shape-graph builder, runs
/// <see cref="ShapeLoader.LoadAsync"/>, and returns a
/// <see cref="TestShaclPipelineDataState"/> with the populated
/// dictionary and registry plus empty data/evaluator accumulators.
/// </para>
/// <para>
/// <b>Data-stage extensions</b> operate on
/// <see cref="TestShaclPipelineDataState"/>. They append triples
/// against the shared dictionary and register evaluator delegates.
/// </para>
/// <para>
/// <b>Terminal.</b> <see cref="RunAsync"/> materialises the
/// in-memory data store, hands the package to the library pipeline,
/// and returns a <see cref="ValidationReport"/>.
/// <see cref="RunWithTraceAsync"/> additionally wires a fresh
/// <see cref="ValidationTrace"/> through
/// <see cref="ShaclValidatorOptions.TraceHandler"/> and returns both
/// the report and the captured trace.
/// </para>
/// <para>
/// <b>Choosing a targeting strategy.</b> Two extensions declare
/// targeted shapes, and they exercise different validator paths.
/// Pick by what your test needs:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b><see cref="WithNodeShapeTargetingPipelineFocus"/>.</b> Pins
///   the focus to the pipeline's configured focus IRI via
///   <c>sh:targetNode</c>. The validator runs constraints on this
///   focus regardless of the data graph's contents. <i>Use this when
///   testing constraints that must fire on empty value sets — for
///   example <c>sh:minCount</c> or <c>sh:qualifiedMinCount</c>
///   violations on zero values.</i> Pair with an untargeted property
///   shape and link the two via <c>sh:property</c> when the
///   constraint is on a property-shape rather than a node-shape.
///   </description></item>
///   <item><description>
///   <b><see cref="WithPropertyShapeTargetingSubjectsOfPath"/>.</b>
///   Targets via <c>sh:targetSubjectsOf &lt;path&gt;</c> — the focus
///   set is "every subject in the data graph that has at least one
///   outgoing triple on the path predicate". Convenient and concise
///   for tests where the data graph is non-empty on the path. <i>Do
///   NOT use this for empty-value-set tests:</i> with no path triples,
///   the focus set is empty and the validator skips the shape per
///   SHACL Core §3.4, so no constraint evaluator runs and no
///   violation is reported. The test would silently pass for the
///   wrong reason.
///   </description></item>
/// </list>
/// <para>
/// <b>Empty-set distinction.</b> SHACL §3.4 defines two empty cases
/// with different semantics. An empty <em>focus-node</em> set means
/// "this shape does not apply to any subject" and the validator
/// produces no results. An empty <em>value-node</em> set on a
/// non-empty focus means "this focus has zero values along the path"
/// and cardinality constraints (§4.5.1, §4.7.4) report violations on
/// it. Test scaffolding that conflates the two — by deriving focus
/// from data — cannot exercise the empty-value-set path at all.
/// </para>
/// </remarks>
internal static class TestShaclPipelineExtensions
{
    //Shape-stage extensions.

    /// <summary>
    /// Declares a property shape targeted via
    /// <c>sh:targetSubjectsOf &lt;path&gt;</c>. The focus-node set
    /// for this shape is "every subject in the data graph that has
    /// at least one outgoing triple on <paramref name="pathIri"/>".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Do not use for empty-value-set tests.</b> When the data
    /// graph contains zero <c>(?, pathIri, ?)</c> triples, the focus
    /// set is empty and the validator skips this shape entirely (per
    /// SHACL Core §3.4). Cardinality constraints
    /// (<c>sh:minCount</c>, <c>sh:qualifiedMinCount</c>) cannot be
    /// exercised on empty value sets through this targeting because
    /// no constraint evaluator runs. Use
    /// <see cref="WithNodeShapeTargetingPipelineFocus"/> with an
    /// <c>sh:property</c> reference to an untargeted property shape
    /// instead.
    /// </para>
    /// </remarks>
    public static TestShapeContextHandle WithPropertyShapeTargetingSubjectsOfPath(
        this TestShaclPipelineShapeState state,
        string shapeIri,
        string pathIri)
    {
        ArgumentNullException.ThrowIfNull(state);

        ShapeGraphBuilder.ShapeContext ctx = state.Builder.PropertyShape(shapeIri, pathIri)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(pathIri));

        return new TestShapeContextHandle(state, ctx);
    }

    public static TestShaclPipelineShapeState WithUntargetedPropertyShape(
        this TestShaclPipelineShapeState state,
        string shapeIri,
        string pathIri)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Builder.PropertyShape(shapeIri, pathIri);

        return state;
    }

    /// <summary>
    /// Declares a node shape targeted via <c>sh:targetNode</c>
    /// pinned to the pipeline's configured focus IRI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Use this for empty-value-set tests.</b> Pinning the focus
    /// directly via <c>sh:targetNode</c> makes the validator run
    /// constraints on this focus regardless of the data graph's
    /// contents. To exercise property-shape constraints on an
    /// empty value-set, declare an untargeted property shape via
    /// <see cref="WithUntargetedPropertyShape"/> and link it from
    /// here with an <c>sh:property</c> reference.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Pipeline was begun without a default focus.
    /// </exception>
    public static TestShapeContextHandle WithNodeShapeTargetingPipelineFocus(
        this TestShaclPipelineShapeState state,
        string shapeIri)
    {
        ArgumentNullException.ThrowIfNull(state);
        if(state.OptionalFocusIri is null)
        {
            throw new InvalidOperationException(
                "WithNodeShapeTargetingPipelineFocus requires the pipeline to have been begun with TestShaclPipeline.BeginWithFocus.");
        }

        ShapeGraphBuilder.ShapeContext ctx = state.Builder.NodeShape(shapeIri)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(state.OptionalFocusIri));

        return new TestShapeContextHandle(state, ctx);
    }

    public static TestShapeContextHandle WithUntargetedNodeShape(
        this TestShaclPipelineShapeState state,
        string shapeIri)
    {
        ArgumentNullException.ThrowIfNull(state);

        ShapeGraphBuilder.ShapeContext ctx = state.Builder.NodeShape(shapeIri);

        return new TestShapeContextHandle(state, ctx);
    }

    //Transition.

    public static async Task<TestShaclPipelineDataState> BuildAsync(
        this TestShaclPipelineShapeState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = state.Builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        NamedNode? focus = state.OptionalFocusIri is null
            ? null
            : new NamedNode(Utf8Strings.From(state.OptionalFocusIri));

        return new TestShaclPipelineDataState(
            registry,
            dictionary,
            focus,
            DataTriples: [],
            Evaluators: []);
    }

    //Data-stage extensions.

    public static TestShaclPipelineDataState WithExplicitTriple(
        this TestShaclPipelineDataState state,
        string subjectIri,
        string predicateIri,
        RdfTerm @object)
    {
        ArgumentNullException.ThrowIfNull(state);

        NamedNode subject = new(Utf8Strings.From(subjectIri));
        NamedNode predicate = new(Utf8Strings.From(predicateIri));
        state.DataTriples.Add(
            new Quad(subject, predicate, @object).Encode(state.Dictionary).AsTriple());

        return state;
    }

    public static TestShaclPipelineDataState WithTripleOnFocus(
        this TestShaclPipelineDataState state,
        string predicateIri,
        RdfTerm @object)
    {
        ArgumentNullException.ThrowIfNull(state);
        if(state.OptionalFocus is null)
        {
            throw new InvalidOperationException(
                "WithTripleOnFocus requires the pipeline to have been begun with TestShaclPipeline.BeginWithFocus.");
        }

        NamedNode predicate = new(Utf8Strings.From(predicateIri));
        state.DataTriples.Add(
            new Quad(state.OptionalFocus, predicate, @object).Encode(state.Dictionary).AsTriple());

        return state;
    }

    public static TestShaclPipelineDataState WithTriplesOnFocus(
        this TestShaclPipelineDataState state,
        string predicateIri,
        IReadOnlyList<RdfTerm> objects)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(objects);
        if(state.OptionalFocus is null)
        {
            throw new InvalidOperationException(
                "WithTriplesOnFocus requires the pipeline to have been begun with TestShaclPipeline.BeginWithFocus.");
        }

        NamedNode predicate = new(Utf8Strings.From(predicateIri));
        foreach(RdfTerm @object in objects)
        {
            state.DataTriples.Add(
                new Quad(state.OptionalFocus, predicate, @object).Encode(state.Dictionary).AsTriple());
        }

        return state;
    }

    public static TestShaclPipelineDataState WithEvaluator(
        this TestShaclPipelineDataState state,
        Utf8String constraintComponentIri,
        ConstraintEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(evaluator);

        state.Evaluators[constraintComponentIri] = evaluator;

        return state;
    }

    //Terminal.

    public static async Task<ValidationReport> RunAsync(
        this TestShaclPipelineDataState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(state.DataTriples);
        ShaclPipelineDataState libraryState = ShaclPipeline.Begin(
            state.Shapes, state.Dictionary, dataStore.AsMatchOps());

        foreach(KeyValuePair<Utf8String, ConstraintEvaluator> kvp in state.Evaluators)
        {
            libraryState.WithEvaluator(kvp.Key, kvp.Value);
        }

        return await libraryState.RunAsync(VeritasClock.System, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs with trace capture enabled. Returns the produced report
    /// alongside the captured <see cref="ValidationTrace"/> for use
    /// by <see cref="ValidationAssertions"/>.
    /// </summary>
    public static async Task<(ValidationReport Report, ValidationTrace Trace)> RunWithTraceAsync(
        this TestShaclPipelineDataState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        ValidationTrace trace = new();

        ShaclValidatorOptions options = ShaclValidatorOptions.Default with
        {
            TraceHandler = trace.Capture,
        };

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(state.DataTriples);
        ShaclPipelineDataState libraryState = ShaclPipeline.Begin(
            state.Shapes, state.Dictionary, dataStore.AsMatchOps());

        foreach(KeyValuePair<Utf8String, ConstraintEvaluator> kvp in state.Evaluators)
        {
            libraryState.WithEvaluator(kvp.Key, kvp.Value);
        }

        ValidationReport report = await libraryState.RunAsync(VeritasClock.System, options, cancellationToken).ConfigureAwait(false);

        return (report, trace);
    }
}
