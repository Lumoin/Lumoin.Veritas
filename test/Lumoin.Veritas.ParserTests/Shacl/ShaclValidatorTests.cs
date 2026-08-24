using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Diagnostics;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using System.Collections.Immutable;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;

//Explicit aliases to avoid CS0104 ambiguity against any pre-existing
//`ValidationResult` / `ValidationReport` types at the root of the
//Lumoin.Veritas.Shacl namespace. Within this file these short names
//unambiguously refer to the Validation-namespace versions.
using ValidationResult = Lumoin.Veritas.Shacl.Validation.ValidationResult;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// End-to-end tests for <see cref="ShaclValidator.ValidateAsync"/>. Build
/// a shape graph via <see cref="ShapeGraphBuilder"/>, a data graph by
/// hand using the same term dictionary, wire up a
/// <see cref="ConstraintEvaluatorRegistry"/> (either empty, or with a
/// capturing / violation-emitting custom evaluator), run the validator,
/// and inspect the resulting <see cref="ValidationReport"/>.
/// </summary>
[TestClass]
internal sealed class ShaclValidatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExNode = "http://example.org/node1";
    private const string ExPred = "http://example.org/pred";
    private const string ExClass = "http://example.org/Cls";

    [TestMethod]
    public async Task EmptyRegistryProducesEmptyReport()
    {
        TermDictionary dictionary = new();
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);
        ShapeRegistry registry = ShapeRegistry.FromDictionary(new());

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            ConstraintEvaluatorRegistry.Empty,
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task NodeShapeWithTargetNodeEmitsNotImplementedResult()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(ExNode))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Data graph is empty; NotImplementedEvaluator does not inspect
        //it, so any store (including the shape store itself) would do.
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            ConstraintEvaluatorRegistry.Empty,
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //sh:conforms is the absence of any result (§3.6), so one Info result ⇒ Conforms == false.
        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);

        ValidationResult result = report.Results[0];
        Assert.AreEqual(Severity.Info, result.Severity);
        Assert.AreEqual(ShaclComponentVocabulary.MinCount, result.SourceConstraintComponent);
        Assert.IsNull(result.ResultPath);
    }

    [TestMethod]
    public async Task PropertyShapeInvokesEvaluatorWithPathValueNodes()
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Data graph: :foo :pred :obj1 ; :pred :obj2 .
        NamedNode foo = new(Utf8Strings.From("http://example.org/foo"));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        NamedNode obj1 = new(Utf8Strings.From("http://example.org/obj1"));
        NamedNode obj2 = new(Utf8Strings.From("http://example.org/obj2"));
        List<EncodedTriple> dataTriples =
        [
            new Quad(foo, pred, obj1).Encode(dictionary).AsTriple(),
            new Quad(foo, pred, obj2).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        //Capturing evaluator registered for sh:MinCount.
        List<CapturedInvocation> captured = [];
        ConstraintEvaluator capture = (shape, constraint, focusNode, valueNodes, pathArg, ctx, ct) =>
        {
            captured.Add(new CapturedInvocation(focusNode, valueNodes, pathArg));
            return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        };

        Dictionary<Utf8String, ConstraintEvaluator> bindings = new()
        {
            [ShaclComponentVocabulary.MinCount] = capture,
        };
        ConstraintEvaluatorRegistry evaluators = new(bindings);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            evaluators,
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);

        //Focus node is :foo (the subject of :pred), value nodes are
        //:obj1 and :obj2, and path is the PredicatePath for :pred.
        Assert.HasCount(1, captured);
        CapturedInvocation invocation = captured[0];

        TermId fooId = dictionary.GetOrAdd(foo);
        Assert.AreEqual(fooId, invocation.Focus);
        Assert.HasCount(2, invocation.ValueNodes);

        TermId obj1Id = dictionary.GetOrAdd(obj1);
        TermId obj2Id = dictionary.GetOrAdd(obj2);
        Assert.Contains(obj1Id, invocation.ValueNodes);
        Assert.Contains(obj2Id, invocation.ValueNodes);

        Assert.IsInstanceOfType<PredicatePath>(invocation.Path);
    }

    [TestMethod]
    public async Task DeactivatedShapeProducesNoResults()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(ExNode))
            .With(ShaclCoreVocabulary.Deactivated.ToString(), ShapeGraphBuilder.BoolLiteral(true))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            ConstraintEvaluatorRegistry.Empty,
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task MultipleFocusNodesProduceMultipleInvocations()
    {
        //Shape targets instances of :Cls; data graph has three
        //instances. Evaluator is invoked once per instance.
        ShapeGraphBuilder shapeBuilder = new();
        shapeBuilder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetClass.ToString(), ShapeGraphBuilder.Iri(ExClass))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = shapeBuilder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Data graph: three rdf:type :Cls triples.
        NamedNode rdfType = new(Vocabulary.Rdf.Type);
        NamedNode cls = new(Utf8Strings.From(ExClass));
        NamedNode i1 = new(Utf8Strings.From("http://example.org/i1"));
        NamedNode i2 = new(Utf8Strings.From("http://example.org/i2"));
        NamedNode i3 = new(Utf8Strings.From("http://example.org/i3"));
        List<EncodedTriple> dataTriples =
        [
            new Quad(i1, rdfType, cls).Encode(dictionary).AsTriple(),
            new Quad(i2, rdfType, cls).Encode(dictionary).AsTriple(),
            new Quad(i3, rdfType, cls).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        List<CapturedInvocation> captured = [];
        ConstraintEvaluator capture = (shape, constraint, focusNode, valueNodes, pathArg, ctx, ct) =>
        {
            captured.Add(new CapturedInvocation(focusNode, valueNodes, pathArg));
            return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        };
        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.MinCount] = capture,
        });

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            evaluators,
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.HasCount(3, captured);

        HashSet<TermId> focusNodes = [.. captured.Select(c => c.Focus)];
        Assert.Contains(dictionary.GetOrAdd(i1), focusNodes);
        Assert.Contains(dictionary.GetOrAdd(i2), focusNodes);
        Assert.Contains(dictionary.GetOrAdd(i3), focusNodes);
    }

    [TestMethod]
    public async Task ViolationSeverityFlipsConforms()
    {
        //Custom evaluator emits a Violation; report should be non-conforming.
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(ExNode))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ConstraintEvaluator violate = (shape, constraint, focusNode, valueNodes, pathArg, ctx, ct) =>
        {
            ValidationResult vr = new()
            {
                FocusNode = focusNode,
                Severity = Severity.Violation,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
            };
            return ValueTask.FromResult(ImmutableArray.Create(vr));
        };
        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.MinCount] = violate,
        });

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            evaluators,
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(Severity.Violation, report.Results[0].Severity);
    }

    [TestMethod]
    public async Task FailFastStopsAfterFirstViolation()
    {
        //Shape targets three class instances, constraint always
        //violates. FailFast stops after the first result.
        ShapeGraphBuilder shapeBuilder = new();
        shapeBuilder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetClass.ToString(), ShapeGraphBuilder.Iri(ExClass))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = shapeBuilder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        NamedNode rdfType = new(Vocabulary.Rdf.Type);
        NamedNode cls = new(Utf8Strings.From(ExClass));
        NamedNode i1 = new(Utf8Strings.From("http://example.org/i1"));
        NamedNode i2 = new(Utf8Strings.From("http://example.org/i2"));
        NamedNode i3 = new(Utf8Strings.From("http://example.org/i3"));
        List<EncodedTriple> dataTriples =
        [
            new Quad(i1, rdfType, cls).Encode(dictionary).AsTriple(),
            new Quad(i2, rdfType, cls).Encode(dictionary).AsTriple(),
            new Quad(i3, rdfType, cls).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        ConstraintEvaluator violate = (shape, constraint, focusNode, valueNodes, pathArg, ctx, ct) =>
        {
            ValidationResult vr = new()
            {
                FocusNode = focusNode,
                Severity = Severity.Violation,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
            };
            return ValueTask.FromResult(ImmutableArray.Create(vr));
        };
        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.MinCount] = violate,
        });

        ShaclValidatorOptions options = new() { FailFast = true };

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            evaluators,
            VeritasClock.System,
            options: options,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task MaxResultsLimitsOutput()
    {
        ShapeGraphBuilder shapeBuilder = new();
        shapeBuilder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetClass.ToString(), ShapeGraphBuilder.Iri(ExClass))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = shapeBuilder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Five instances of the class.
        NamedNode rdfType = new(Vocabulary.Rdf.Type);
        NamedNode cls = new(Utf8Strings.From(ExClass));
        List<EncodedTriple> dataTriples = [];
        for(int i = 0; i < 5; i++)
        {
            NamedNode instance = new(Utf8Strings.From($"http://example.org/i{i}"));
            dataTriples.Add(new Quad(instance, rdfType, cls).Encode(dictionary).AsTriple());
        }
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        //Evaluator always emits one Info result. With MaxResults=2 we
        //should get exactly 2 despite 5 focus nodes.
        ConstraintEvaluator info = (shape, constraint, focusNode, valueNodes, pathArg, ctx, ct) =>
        {
            ValidationResult r = new()
            {
                FocusNode = focusNode,
                Severity = Severity.Info,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
            };
            return ValueTask.FromResult(ImmutableArray.Create(r));
        };
        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.MinCount] = info,
        });

        ShaclValidatorOptions options = new() { MaxResults = 2 };

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            evaluators,
            VeritasClock.System,
            options: options,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //This test pins the MaxResults cap; Conforms is false because results were produced (§3.6).
        Assert.IsFalse(report.Conforms);
        Assert.HasCount(2, report.Results);
    }

    [TestMethod]
    public async Task TraceHandlerReceivesExpectedEventSequenceForUnregisteredComponent()
    {
        //One node shape, one target node, one sh:minCount constraint,
        //empty evaluator registry → NotImplementedEvaluator path. The
        //expected four-event sequence is:
        //  1. FocusNodeSelected
        //  2. ConstraintNotImplemented
        //  3. ValidationResultProduced (the Info result)
        //  4. ConstraintEvaluationCompleted (Failed — one result)
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(ExNode))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        List<ShaclTraceEvent> events = [];
        TraceHandler<ShaclTraceEvent> collector = CollectEvent;
        ShaclValidatorOptions options = new() { TraceHandler = collector };

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            ConstraintEvaluatorRegistry.Empty,
            VeritasClock.System,
            options: options,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //This test pins the trace-event sequence; Conforms is false because a result was produced (§3.6).
        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);

        Assert.HasCount(4, events);
        Assert.AreEqual(ShaclTraceEventKind.FocusNodeSelected, events[0].Kind);
        Assert.AreEqual(ShaclTraceEventKind.ConstraintNotImplemented, events[1].Kind);
        Assert.AreEqual(ShaclTraceEventKind.ValidationResultProduced, events[2].Kind);
        Assert.AreEqual(ShaclTraceEventKind.ConstraintEvaluationCompleted, events[3].Kind);
        Assert.AreEqual(ConstraintEvaluationStatus.Failed, events[3].Status);

        //Every event shares a single correlation id and sequence
        //numbers are strictly increasing.
        Guid correlationId = events[0].CorrelationId;
        Assert.AreNotEqual(Guid.Empty, correlationId);
        for(int i = 0; i < events.Count; i++)
        {
            Assert.AreEqual(correlationId, events[i].CorrelationId);
            Assert.AreEqual((long)i, events[i].SequenceNumber);
        }

        return;

        void CollectEvent(in ShaclTraceEvent evt) => events.Add(evt);
    }

    [TestMethod]
    public async Task TraceHandlerReceivesConstraintStartedWhenEvaluatorIsRegistered()
    {
        //Same shape as above but with sh:minCount registered against a
        //silent evaluator that returns an empty result array. The
        //expected sequence changes to:
        //  1. FocusNodeSelected
        //  2. ConstraintEvaluationStarted (registered, so not NotImplemented)
        //  3. ConstraintEvaluationCompleted (Passed — zero results)
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(ExNode))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ConstraintEvaluator silent = (shape, constraint, focusNode, valueNodes, pathArg, ctx, ct) =>
            ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.MinCount] = silent,
        });

        List<ShaclTraceEvent> events = [];
        TraceHandler<ShaclTraceEvent> collector = CollectEvent;
        ShaclValidatorOptions options = new() { TraceHandler = collector };

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            evaluators,
            VeritasClock.System,
            options: options,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);

        Assert.HasCount(3, events);
        Assert.AreEqual(ShaclTraceEventKind.FocusNodeSelected, events[0].Kind);
        Assert.AreEqual(ShaclTraceEventKind.ConstraintEvaluationStarted, events[1].Kind);
        Assert.AreEqual(ShaclTraceEventKind.ConstraintEvaluationCompleted, events[2].Kind);
        Assert.AreEqual(ConstraintEvaluationStatus.Passed, events[2].Status);
        Assert.AreEqual(ShaclComponentVocabulary.MinCount.ToString(), events[1].ConstraintIri);

        return;

        void CollectEvent(in ShaclTraceEvent evt) => events.Add(evt);
    }

    /// <summary>
    /// Records one evaluator invocation for later assertions.
    /// </summary>
    private sealed record CapturedInvocation(
        TermId Focus,
        ImmutableArray<TermId> ValueNodes,
        PropertyPath? Path);
}
