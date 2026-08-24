using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Targets;
using Lumoin.Veritas.ParserTests.Infrastructure;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// End-to-end tests for <see cref="ShapeLoader.LoadAsync"/>. Build a
/// shape graph as <see cref="Quad"/> objects, encode them into an
/// <see cref="InMemoryGraphStore"/>, run the loader, and inspect the
/// resulting <see cref="ShapeRegistry"/>.
/// </summary>
[TestClass]
internal sealed class ShapeLoaderTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/MyShape";
    private const string ExShape2 = "http://example.org/MyShape2";
    private const string ExClass = "http://example.org/MyClass";
    private const string ExProp = "http://example.org/myProp";

    [TestMethod]
    public async Task EmptyGraphProducesEmptyRegistry()
    {
        TermDictionary dictionary = new();
        InMemoryGraphStore store = InMemoryGraphStore.Build([]);

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(registry.AllShapes);
    }

    [TestMethod]
    public async Task NodeShapeWithMinCountLoads()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        List<Shape> shapes = [];
        foreach(Shape s in registry.AllShapes)
        {
            shapes.Add(s);
        }

        Assert.HasCount(1, shapes);

        Shape shape = shapes[0];
        Assert.IsInstanceOfType<NodeShape>(shape);
        Assert.HasCount(1, shape.Constraints);

        MinCountConstraint constraint = (MinCountConstraint)shape.Constraints[0];
        Assert.AreEqual(1, constraint.MinCount);
    }

    [TestMethod]
    public async Task PropertyShapeWithPathLoads()
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExProp)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        List<Shape> shapes = [];
        foreach(Shape s in registry.AllShapes)
        {
            shapes.Add(s);
        }

        Assert.HasCount(1, shapes);

        PropertyShape ps = (PropertyShape)shapes[0];
        Assert.IsInstanceOfType<PredicatePath>(ps.Path);

        PredicatePath predPath = (PredicatePath)ps.Path;
        RdfTerm pred = dictionary.Resolve(predPath.Predicate);
        Assert.AreEqual(ExProp, ((NamedNode)pred).Iri.ToString());
    }

    [TestMethod]
    public async Task NodeShapeWithTargetClassLoads()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetClass.ToString(), ShapeGraphBuilder.Iri(ExClass))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Shape shape = registry.AllShapes.Single();
        Assert.HasCount(1, shape.Targets);
        Assert.IsInstanceOfType<TargetClass>(shape.Targets[0]);
    }

    [TestMethod]
    public async Task MultipleTargetKindsLoadTogether()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.TargetClass.ToString(), ShapeGraphBuilder.Iri(ExClass))
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri("http://example.org/specific-node"))
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExProp))
            .With(ShaclCoreVocabulary.TargetObjectsOf.ToString(), ShapeGraphBuilder.Iri(ExProp))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Shape shape = registry.AllShapes.Single();
        Assert.HasCount(4, shape.Targets);

        Assert.IsTrue(shape.Targets.Any(t => t is TargetClass));
        Assert.IsTrue(shape.Targets.Any(t => t is TargetNode));
        Assert.IsTrue(shape.Targets.Any(t => t is TargetSubjectsOf));
        Assert.IsTrue(shape.Targets.Any(t => t is TargetObjectsOf));
    }

    [TestMethod]
    public async Task SeverityDeactivatedAndMessageAreRead()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclCoreVocabulary.Severity.ToString(), ShapeGraphBuilder.Iri(ShaclSeverityVocabulary.Warning.ToString()))
            .With(ShaclCoreVocabulary.Deactivated.ToString(), ShapeGraphBuilder.BoolLiteral(true))
            .With(ShaclCoreVocabulary.Message.ToString(), ShapeGraphBuilder.LangString("Hello", "en"))
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Shape shape = registry.AllShapes.Single();
        Assert.AreEqual(Severity.Warning, shape.Severity);
        Assert.IsTrue(shape.Deactivated);
        Assert.AreEqual("Hello", shape.Messages["en"]);
    }

    [TestMethod]
    public async Task PatternWithFlagsLoadsAsSingleConstraint()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclConstraintVocabulary.Pattern.ToString(), ShapeGraphBuilder.StringLiteral("^[a-z]+$"))
            .With(ShaclConstraintVocabulary.Flags.ToString(), ShapeGraphBuilder.StringLiteral("i"));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Shape shape = registry.AllShapes.Single();
        //Pattern is the primary, Flags is its companion; one PatternConstraint results.
        Assert.HasCount(1, shape.Constraints);
        Assert.IsInstanceOfType<PatternConstraint>(shape.Constraints[0]);
    }

    [TestMethod]
    public async Task NotConstraintCapturesInnerShapeId()
    {
        ShapeGraphBuilder builder = new();

        //The shape being referenced:
        builder.NodeShape(ExShape2)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        //The outer shape referencing it via sh:not:
        builder.NodeShape(ExShape)
            .With(ShaclConstraintVocabulary.Not.ToString(), ShapeGraphBuilder.Iri(ExShape2));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, registry.AllShapes);

        TermId exShapeId = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape)));
        TermId exShape2Id = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape2)));

        Assert.IsTrue(registry.TryGetShape(exShapeId, out Shape? outerShape));
        Assert.IsNotNull(outerShape);

        NotConstraint notConstraint = (NotConstraint)outerShape.Constraints.Single();
        Assert.AreEqual(exShape2Id, notConstraint.InnerShapeId);

        Assert.IsTrue(registry.TryGetShape(exShape2Id, out Shape? innerShape));
        Assert.IsNotNull(innerShape);
        Assert.HasCount(1, innerShape.Constraints);
    }

    [TestMethod]
    public async Task AndConstraintWalksRdfListOfShapeIds()
    {
        ShapeGraphBuilder builder = new();

        builder.NodeShape(ExShape2)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        const string ExShape3 = "http://example.org/MyShape3";
        builder.NodeShape(ExShape3)
            .With(ShaclConstraintVocabulary.MaxCount.ToString(), ShapeGraphBuilder.IntLiteral(5));

        RdfTerm listHead = builder.List(ShapeGraphBuilder.Iri(ExShape2), ShapeGraphBuilder.Iri(ExShape3));
        builder.NodeShape(ExShape)
            .With(ShaclConstraintVocabulary.And.ToString(), listHead);

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(3, registry.AllShapes);

        TermId exShapeId = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape)));
        Assert.IsTrue(registry.TryGetShape(exShapeId, out Shape? outerShape));
        Assert.IsNotNull(outerShape);

        AndConstraint andConstraint = (AndConstraint)outerShape.Constraints.Single();
        Assert.HasCount(2, andConstraint.MemberShapeIds);

        TermId exShape2Id = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape2)));
        TermId exShape3Id = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape3)));

        Assert.Contains(exShape2Id, andConstraint.MemberShapeIds);
        Assert.Contains(exShape3Id, andConstraint.MemberShapeIds);
    }

    [TestMethod]
    public async Task InConstraintWalksRdfListOfTerms()
    {
        ShapeGraphBuilder builder = new();

        RdfTerm listHead = builder.List(
            ShapeGraphBuilder.StringLiteral("red"),
            ShapeGraphBuilder.StringLiteral("green"),
            ShapeGraphBuilder.StringLiteral("blue"));

        builder.NodeShape(ExShape)
            .With(ShaclConstraintVocabulary.In.ToString(), listHead);

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Shape shape = registry.AllShapes.Single();
        InConstraint inConstraint = (InConstraint)shape.Constraints.Single();
        Assert.HasCount(3, inConstraint.AllowedValues);
    }

    [TestMethod]
    public async Task DynamicConstraintIntegratesIntoRegistry()
    {
        const string CustomComponentIri = "http://example.org/ns#MinCountComponent";
        const string CustomParamIri = "http://example.org/ns#customMinCount";

        ConstraintComponentInfo customDynamic = ConstraintComponentInfo.CreateDynamic(
            componentIri: Utf8Strings.From(CustomComponentIri),
            primaryParameter: Utf8Strings.From(CustomParamIri),
            shapeTypedParameters: []);

        List<ConstraintComponentInfo> registered =
        [
            .. ShaclBuiltInComponents.All,
            customDynamic,
        ];

        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(CustomParamIri, ShapeGraphBuilder.IntLiteral(7));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            registered,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Shape shape = registry.AllShapes.Single();
        DynamicConstraint dyn = (DynamicConstraint)shape.Constraints.Single();
        Assert.AreEqual(
            Utf8Strings.From(CustomComponentIri),
            dyn.ConstraintComponentIri);
    }

    [TestMethod]
    public async Task NodeAndPropertyShapesAreExposedSeparately()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ExShape)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));
        builder.PropertyShape(ExShape2, pathIri: ExProp)
            .With(ShaclConstraintVocabulary.MaxCount.ToString(), ShapeGraphBuilder.IntLiteral(5));

        (InMemoryGraphStore store, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            store.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, registry.NodeShapes);
        Assert.HasCount(1, registry.PropertyShapes);
    }
}
