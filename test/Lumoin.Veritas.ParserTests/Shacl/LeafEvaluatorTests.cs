using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using System.Collections.Immutable;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;
using ValidationResult = Lumoin.Veritas.Shacl.Validation.ValidationResult;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for the seven leaf-constraint evaluators introduced in
/// phase 2C-b: <see cref="MinCountEvaluator"/>,
/// <see cref="MaxCountEvaluator"/>, <see cref="NodeKindEvaluator"/>,
/// <see cref="DatatypeEvaluator"/>, <see cref="ClassEvaluator"/>,
/// <see cref="PatternEvaluator"/>, and <see cref="InEvaluator"/>.
/// </summary>
/// <remarks>
/// These evaluators inspect value nodes directly without recursing
/// into other shapes. The shared helper at the bottom of the class
/// builds a property shape with a single constraint, encodes a tiny
/// data graph, and runs validation using only the evaluator under
/// test plus the dispatch infrastructure. <see cref="ClassEvaluator"/>
/// gets dedicated helpers because its data graphs need
/// <c>rdf:type</c> and <c>rdfs:subClassOf</c> wiring and one of its
/// tests inspects the cache directly.
/// </remarks>
[TestClass]
internal sealed class LeafEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExPred = "http://example.org/pred";
    private const string ExFocus = "http://example.org/foo";
    private const string Animal = "http://example.org/Animal";
    private const string Mammal = "http://example.org/Mammal";
    private const string Dog = "http://example.org/Dog";
    private const string Vegetable = "http://example.org/Vegetable";
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    [TestMethod]
    public async Task MinCountValueCountBelowMinimumEmitsSingleSetLevelViolation()
    {
        ValidationReport report = await RunMinMaxAsync(
            ShaclConstraintVocabulary.MinCount.ToString(),
            ShaclComponentVocabulary.MinCount,
            MinCountEvaluator.EvaluateAsync,
            constraintValue: 2,
            valueCount: 1,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.IsNull(report.Results[0].ValueNode);
        Assert.AreEqual(ShaclComponentVocabulary.MinCount, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task MinCountValueCountAtMinimumSatisfiesConstraint()
    {
        ValidationReport report = await RunMinMaxAsync(
            ShaclConstraintVocabulary.MinCount.ToString(),
            ShaclComponentVocabulary.MinCount,
            MinCountEvaluator.EvaluateAsync,
            constraintValue: 2,
            valueCount: 2,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task MinCountValueCountAboveMinimumSatisfiesConstraint()
    {
        ValidationReport report = await RunMinMaxAsync(
            ShaclConstraintVocabulary.MinCount.ToString(),
            ShaclComponentVocabulary.MinCount,
            MinCountEvaluator.EvaluateAsync,
            constraintValue: 1,
            valueCount: 3,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task MaxCountValueCountAboveMaximumEmitsSingleSetLevelViolation()
    {
        ValidationReport report = await RunMinMaxAsync(
            ShaclConstraintVocabulary.MaxCount.ToString(),
            ShaclComponentVocabulary.MaxCount,
            MaxCountEvaluator.EvaluateAsync,
            constraintValue: 1,
            valueCount: 2,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.IsNull(report.Results[0].ValueNode);
        Assert.AreEqual(ShaclComponentVocabulary.MaxCount, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task MaxCountValueCountAtMaximumSatisfiesConstraint()
    {
        ValidationReport report = await RunMinMaxAsync(
            ShaclConstraintVocabulary.MaxCount.ToString(),
            ShaclComponentVocabulary.MaxCount,
            MaxCountEvaluator.EvaluateAsync,
            constraintValue: 2,
            valueCount: 2,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task MaxCountValueCountBelowMaximumSatisfiesConstraint()
    {
        ValidationReport report = await RunMinMaxAsync(
            ShaclConstraintVocabulary.MaxCount.ToString(),
            ShaclComponentVocabulary.MaxCount,
            MaxCountEvaluator.EvaluateAsync,
            constraintValue: 3,
            valueCount: 1,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task NodeKindIriKindAcceptsIriValueNode()
    {
        ValidationReport report = await RunNodeKindAsync(
            NodeKind.IRI,
            [new NamedNode(Utf8Strings.From("http://example.org/bar"))],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task NodeKindIriKindRejectsLiteralValueNode()
    {
        Literal literal = new(
            Utf8Strings.From("literal"),
            new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunNodeKindAsync(
            NodeKind.IRI,
            [literal],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.IsNotNull(report.Results[0].ValueNode);
        Assert.AreEqual(ShaclComponentVocabulary.NodeKind, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task NodeKindIriOrLiteralRejectsBlankNode()
    {
        NamedNode iri = new(Utf8Strings.From("http://example.org/bar"));
        Literal literal = new(
            Utf8Strings.From("42"),
            new NamedNode(Vocabulary.Xsd.Integer));
        BlankNode blank = new(Utf8Strings.From("b1"));

        ValidationReport report = await RunNodeKindAsync(
            NodeKind.IRIOrLiteral,
            [iri, literal, blank],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.IsNotNull(report.Results[0].ValueNode);
    }

    [TestMethod]
    public async Task DatatypeMatchingLiteralSatisfiesConstraint()
    {
        Literal xsdInt = new(
            Utf8Strings.From("42"),
            new NamedNode(Vocabulary.Xsd.Integer));

        ValidationReport report = await RunDatatypeAsync(
            expectedDatatype: Vocabulary.Xsd.Integer.ToString(),
            values: [xsdInt],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task DatatypeMismatchedDatatypeLiteralViolates()
    {
        Literal xsdString = new(
            Utf8Strings.From("42"),
            new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunDatatypeAsync(
            expectedDatatype: Vocabulary.Xsd.Integer.ToString(),
            values: [xsdString],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.IsNotNull(report.Results[0].ValueNode);
        Assert.AreEqual(ShaclComponentVocabulary.Datatype, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task DatatypeIriValueViolatesDatatypeConstraint()
    {
        NamedNode iri = new(Utf8Strings.From("http://example.org/something"));

        ValidationReport report = await RunDatatypeAsync(
            expectedDatatype: Vocabulary.Xsd.String.ToString(),
            values: [iri],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task PatternMatchingLiteralSatisfiesConstraint()
    {
        Literal digits = new(
            Utf8Strings.From("12345"),
            new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunPatternAsync(
            pattern: "^[0-9]+$",
            values: [digits],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task PatternNonMatchingLiteralViolates()
    {
        Literal letters = new(
            Utf8Strings.From("abc"),
            new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunPatternAsync(
            pattern: "^[0-9]+$",
            values: [letters],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Pattern, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task PatternMixedValuesEmitViolationPerMismatch()
    {
        Literal good = new(Utf8Strings.From("42"), new NamedNode(Vocabulary.Xsd.String));
        Literal bad1 = new(Utf8Strings.From("foo"), new NamedNode(Vocabulary.Xsd.String));
        Literal bad2 = new(Utf8Strings.From("x1"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunPatternAsync(
            pattern: "^[0-9]+$",
            values: [good, bad1, bad2],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(2, report.Results);
    }

    [TestMethod]
    public async Task InAllowedValueSatisfiesConstraint()
    {
        NamedNode a = new(Utf8Strings.From("http://example.org/a"));

        ValidationReport report = await RunInAsync(
            allowed: ["http://example.org/a", "http://example.org/b", "http://example.org/c"],
            values: [a],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task InDisallowedValueViolates()
    {
        NamedNode other = new(Utf8Strings.From("http://example.org/other"));

        ValidationReport report = await RunInAsync(
            allowed: ["http://example.org/a", "http://example.org/b", "http://example.org/c"],
            values: [other],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.IsNotNull(report.Results[0].ValueNode);
        Assert.AreEqual(ShaclComponentVocabulary.In, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task InMixedValuesEmitViolationPerDisallowed()
    {
        NamedNode a = new(Utf8Strings.From("http://example.org/a"));
        NamedNode c = new(Utf8Strings.From("http://example.org/c"));
        NamedNode other = new(Utf8Strings.From("http://example.org/other"));

        ValidationReport report = await RunInAsync(
            allowed: ["http://example.org/a", "http://example.org/b", "http://example.org/c"],
            values: [a, c, other],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task ClassDirectInstanceSatisfiesConstraint()
    {
        (ValidationReport report, _) = await RunClassAsync(
            constraintClassIri: Animal,
            dataSetup: DirectAnimalInstanceDataSetup,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task ClassTransitiveSubClassInstanceSatisfiesConstraint()
    {
        (ValidationReport report, _) = await RunClassAsync(
            constraintClassIri: Animal,
            dataSetup: TransitiveDogInstanceDataSetup,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task ClassUnrelatedClassInstanceViolates()
    {
        (ValidationReport report, _) = await RunClassAsync(
            constraintClassIri: Animal,
            dataSetup: VegetableInstanceDataSetup,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Class, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task ClassCacheRetainsDecisionAfterEvaluation()
    {
        ClassMembershipCache cache = new();
        Assert.AreEqual(0, cache.Count);

        (ValidationReport report, _) = await RunClassAsync(
            constraintClassIri: Animal,
            dataSetup: DirectAnimalInstanceDataSetup,
            TestContext.CancellationToken,
            cacheOverride: cache).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.AreEqual(1, cache.Count);
    }

    //Helpers below.

    //Builds a property shape with the given numeric-cardinality
    //constraint, populates the data graph with valueCount path values
    //from a single subject, and runs validation using only the
    //specified evaluator.
    private static async Task<ValidationReport> RunMinMaxAsync(
        string constraintIri,
        Utf8String componentIri,
        ConstraintEvaluator evaluator,
        int constraintValue,
        int valueCount,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(constraintIri, ShapeGraphBuilder.IntLiteral(constraintValue));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(ExFocus));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        List<EncodedTriple> dataTriples = [];
        for(int i = 0; i < valueCount; i++)
        {
            NamedNode obj = new(Utf8Strings.From($"http://example.org/v{i}"));
            dataTriples.Add(new Quad(focus, pred, obj).Encode(dictionary).AsTriple());
        }
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [componentIri] = evaluator,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    //Runs a property shape with sh:nodeKind set to the given kind
    //against the given path-value terms.
    private static async Task<ValidationReport> RunNodeKindAsync(
        NodeKind kind,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.NodeKind.ToString(), ShapeGraphBuilder.Iri(NodeKindIri(kind)));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = BuildPredObjectStore(dictionary, values);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.NodeKind] = NodeKindEvaluator.EvaluateAsync,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ValidationReport> RunDatatypeAsync(
        string expectedDatatype,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.Datatype.ToString(), ShapeGraphBuilder.Iri(expectedDatatype));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = BuildPredObjectStore(dictionary, values);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.Datatype] = DatatypeEvaluator.EvaluateAsync,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ValidationReport> RunPatternAsync(
        string pattern,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.Pattern.ToString(), ShapeGraphBuilder.StringLiteral(pattern));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = BuildPredObjectStore(dictionary, values);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.Pattern] = PatternEvaluator.EvaluateAsync,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ValidationReport> RunInAsync(
        IReadOnlyList<string> allowed,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();

        RdfTerm[] allowedTerms = new RdfTerm[allowed.Count];
        for(int i = 0; i < allowed.Count; i++)
        {
            allowedTerms[i] = ShapeGraphBuilder.Iri(allowed[i]);
        }
        RdfTerm listHead = builder.List(allowedTerms);

        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.In.ToString(), listHead);

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = BuildPredObjectStore(dictionary, values);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.In] = InEvaluator.EvaluateAsync,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    //Wraps a focus and a path predicate around the given values into a
    //one-subject data graph used by NodeKind/Datatype/Pattern/In tests.
    private static InMemoryGraphStore BuildPredObjectStore(TermDictionary dictionary, IReadOnlyList<RdfTerm> values)
    {
        NamedNode focus = new(Utf8Strings.From(ExFocus));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        List<EncodedTriple> dataTriples = [];
        foreach(RdfTerm value in values)
        {
            dataTriples.Add(new Quad(focus, pred, value).Encode(dictionary).AsTriple());
        }
        return InMemoryGraphStore.Build(dataTriples);
    }

    //Maps a NodeKind enum value to its SHACL IRI string. Used during
    //shape construction when a NodeKind constraint is being declared
    //in the shape graph.
    private static string NodeKindIri(NodeKind kind)
        => kind switch
        {
            NodeKind.BlankNode => "http://www.w3.org/ns/shacl#BlankNode",
            NodeKind.IRI => "http://www.w3.org/ns/shacl#IRI",
            NodeKind.Literal => "http://www.w3.org/ns/shacl#Literal",
            NodeKind.BlankNodeOrIRI => "http://www.w3.org/ns/shacl#BlankNodeOrIRI",
            NodeKind.BlankNodeOrLiteral => "http://www.w3.org/ns/shacl#BlankNodeOrLiteral",
            NodeKind.IRIOrLiteral => "http://www.w3.org/ns/shacl#IRIOrLiteral",
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Unknown node kind."),
        };

    //Class-evaluator helpers below. The class evaluator's tests build
    //richer data graphs with rdf:type and rdfs:subClassOf assertions,
    //and one test inspects the cache directly, so RunClassAsync is
    //separate from the simpler value-list helpers above.
    private delegate List<EncodedTriple> DataSetup(
        TermDictionary dictionary,
        NamedNode focus,
        NamedNode predicate);

    private static List<EncodedTriple> DirectAnimalInstanceDataSetup(
        TermDictionary dictionary, NamedNode focus, NamedNode pred)
    {
        List<EncodedTriple> data = [];
        NamedNode fido = new(Utf8Strings.From("http://example.org/fido"));
        NamedNode animal = new(Utf8Strings.From(Animal));
        data.Add(new Quad(focus, pred, fido).Encode(dictionary).AsTriple());
        data.Add(new Quad(fido, new NamedNode(Vocabulary.Rdf.Type), animal).Encode(dictionary).AsTriple());
        return data;
    }

    private static List<EncodedTriple> TransitiveDogInstanceDataSetup(
        TermDictionary dictionary, NamedNode focus, NamedNode pred)
    {
        List<EncodedTriple> data = [];
        NamedNode fido = new(Utf8Strings.From("http://example.org/fido"));
        NamedNode dogCls = new(Utf8Strings.From(Dog));
        NamedNode mammalCls = new(Utf8Strings.From(Mammal));
        NamedNode animalCls = new(Utf8Strings.From(Animal));
        NamedNode rdfType = new(Vocabulary.Rdf.Type);
        NamedNode subClassOf = new(Utf8Strings.From(RdfsSubClassOf));

        data.Add(new Quad(focus, pred, fido).Encode(dictionary).AsTriple());
        data.Add(new Quad(fido, rdfType, dogCls).Encode(dictionary).AsTriple());
        data.Add(new Quad(dogCls, subClassOf, mammalCls).Encode(dictionary).AsTriple());
        data.Add(new Quad(mammalCls, subClassOf, animalCls).Encode(dictionary).AsTriple());
        return data;
    }

    private static List<EncodedTriple> VegetableInstanceDataSetup(
        TermDictionary dictionary, NamedNode focus, NamedNode pred)
    {
        List<EncodedTriple> data = [];
        NamedNode fido = new(Utf8Strings.From("http://example.org/fido"));
        NamedNode veg = new(Utf8Strings.From(Vegetable));
        data.Add(new Quad(focus, pred, fido).Encode(dictionary).AsTriple());
        data.Add(new Quad(fido, new NamedNode(Vocabulary.Rdf.Type), veg).Encode(dictionary).AsTriple());
        return data;
    }

    //When cacheOverride is supplied, this helper bypasses the normal
    //orchestrator and dispatches to ClassEvaluator.EvaluateAsync
    //directly so the caller's cache is observable. The orchestrator
    //path is used otherwise; both paths return the same report shape.
    private static async Task<(ValidationReport Report, ClassMembershipCache Cache)> RunClassAsync(
        string constraintClassIri,
        DataSetup dataSetup,
        CancellationToken cancellationToken,
        ClassMembershipCache? cacheOverride = null)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.Class.ToString(), ShapeGraphBuilder.Iri(constraintClassIri));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(ExFocus));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        List<EncodedTriple> dataTriples = dataSetup(dictionary, focus, pred);
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.Class] = ClassEvaluator.EvaluateAsync,
        });

        if(cacheOverride is not null)
        {
            ValidationContext context = new()
            {
                DataMatchOps = dataStore.AsMatchOps(),
                Shapes = registry,
                Dictionary = dictionary,
                Options = ShaclValidatorOptions.Default,
                RdfFirstId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.First)),
                RdfRestId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Rest)),
                RdfNilId = dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Nil)),
                ClassMembershipCache = cacheOverride,
            };

            Shape shape = System.Linq.Enumerable.First(registry.AllShapes);
            ConstraintComponent constraint = shape.Constraints[0];

            List<TermId> focusNodes = [];
            await foreach(TermId f in System.Linq.Enumerable.First(shape.Targets).ExpandAsync(
                dataStore.AsMatchDelegate(), cancellationToken).ConfigureAwait(false))
            {
                focusNodes.Add(f);
            }

            PropertyShape propertyShape = (PropertyShape)shape;
            List<TermId> values = [];
            await foreach(TermId v in PropertyPathEvaluator.EvaluateAsync(
                focusNodes[0], propertyShape.Path, dataStore.AsMatchOps(), cancellationToken).ConfigureAwait(false))
            {
                values.Add(v);
            }

            ImmutableArray<ValidationResult> results = await ClassEvaluator.EvaluateAsync(
                shape, constraint, focusNodes[0], [.. values], propertyShape.Path,
                context, cancellationToken).ConfigureAwait(false);

            ValidationReport directReport = new()
            {
                Conforms = results.IsEmpty,
                Results = results,
            };
            return (directReport, cacheOverride);
        }

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return (report, new ClassMembershipCache());
    }
}
