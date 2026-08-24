using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ValidationResult = Lumoin.Veritas.Shacl.Validation.ValidationResult;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for the six shape-recursion evaluators introduced in phase
/// 2C-c (<see cref="NodeEvaluator"/>, <see cref="PropertyEvaluator"/>,
/// <see cref="AndEvaluator"/>, <see cref="OrEvaluator"/>,
/// <see cref="NotEvaluator"/>, <see cref="XoneEvaluator"/>) plus the
/// orchestrator's recursion-guard cycle-handling.
/// </summary>
/// <remarks>
/// These evaluators delegate inner-shape validation back to the
/// orchestrator via <see cref="ValidationContext.ShapeValidator"/>.
/// Each test runs end-to-end through
/// <see cref="ShaclValidator.ValidateAsync"/> with the full
/// <see cref="ShaclBuiltInEvaluators.All"/> registry, so dispatch and
/// recursion wiring are exercised together with the per-evaluator
/// semantics.
/// </remarks>
[TestClass]
internal sealed class ShapeRecursionEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string OuterShape = "http://example.org/OuterShape";
    private const string InnerShape = "http://example.org/InnerShape";
    private const string InnerPropertyShape = "http://example.org/InnerPropertyShape";
    private const string MemberA = "http://example.org/MemberA";
    private const string MemberB = "http://example.org/MemberB";
    private const string ShapeA = "http://example.org/ShapeA";
    private const string ShapeB = "http://example.org/ShapeB";
    private const string Pred = "http://example.org/pred";
    private const string Focus = "http://example.org/foo";

    [TestMethod]
    public async Task NodeAllValueNodesConformProducesNoViolation()
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(OuterShape, pathIri: Pred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(Pred))
            .With(ShaclConstraintVocabulary.Node.ToString(), ShapeGraphBuilder.Iri(InnerShape));
        builder.NodeShape(InnerShape)
            .With(ShaclConstraintVocabulary.NodeKind.ToString(), ShapeGraphBuilder.Iri("http://www.w3.org/ns/shacl#IRI"));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(Focus));
        NamedNode pred = new(Utf8Strings.From(Pred));
        NamedNode iriValue = new(Utf8Strings.From("http://example.org/value1"));
        EncodedTriple t = new Quad(focus, pred, iriValue).Encode(dictionary).AsTriple();
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([t]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task NodeNonConformingValueEmitsOneOuterViolation()
    {
        //Inner: sh:nodeKind sh:IRI. Path value is a literal ⇒ inner
        //fails ⇒ outer sh:node emits one violation. Inner result is
        //not surfaced.
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(OuterShape, pathIri: Pred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(Pred))
            .With(ShaclConstraintVocabulary.Node.ToString(), ShapeGraphBuilder.Iri(InnerShape));
        builder.NodeShape(InnerShape)
            .With(ShaclConstraintVocabulary.NodeKind.ToString(), ShapeGraphBuilder.Iri("http://www.w3.org/ns/shacl#IRI"));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(Focus));
        NamedNode pred = new(Utf8Strings.From(Pred));
        Literal literalValue = new(
            Utf8Strings.From("abc"),
            new NamedNode(Vocabulary.Xsd.String));
        EncodedTriple t = new Quad(focus, pred, literalValue).Encode(dictionary).AsTriple();
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([t]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Node, report.Results[0].SourceConstraintComponent);
        Assert.IsNotNull(report.Results[0].ValueNode);
    }

    [TestMethod]
    public async Task PropertyInnerShapeSatisfiedProducesNoViolation()
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(OuterShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(Focus))
            .With(ShaclConstraintVocabulary.Property.ToString(), ShapeGraphBuilder.Iri(InnerPropertyShape));
        builder.PropertyShape(InnerPropertyShape, pathIri: Pred)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(Focus));
        NamedNode pred = new(Utf8Strings.From(Pred));
        NamedNode value = new(Utf8Strings.From("http://example.org/v"));
        EncodedTriple t = new Quad(focus, pred, value).Encode(dictionary).AsTriple();
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([t]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task PropertyInnerShapeViolationSurfacesAsMinCountResult()
    {
        //Inner property shape's sh:minCount violation surfaces with
        //the inner component IRI (MinCount), not the outer (Property).
        //This is the defining behaviour of sh:property: sub-shape
        //results pass through.
        ShapeGraphBuilder builder = new();
        builder.NodeShape(OuterShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(Focus))
            .With(ShaclConstraintVocabulary.Property.ToString(), ShapeGraphBuilder.Iri(InnerPropertyShape));
        builder.PropertyShape(InnerPropertyShape, pathIri: Pred)
            .With(ShaclConstraintVocabulary.MinCount.ToString(), ShapeGraphBuilder.IntLiteral(1));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.MinCount, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task AndFocusConformingToBothMembersProducesNoViolation()
    {
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.And.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#IRI",
            memberBKind: "http://www.w3.org/ns/shacl#IRIOrLiteral",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task AndFocusFailingOneMemberEmitsViolation()
    {
        //IRI focus passes MemberA (sh:IRI) but fails MemberB (sh:Literal).
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.And.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#IRI",
            memberBKind: "http://www.w3.org/ns/shacl#Literal",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.And, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task OrFocusConformingToOneMemberProducesNoViolation()
    {
        //One member passes ⇒ disjunction satisfied.
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.Or.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#IRI",
            memberBKind: "http://www.w3.org/ns/shacl#Literal",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task OrFocusFailingBothMembersEmitsViolation()
    {
        //Neither kind matches an IRI focus.
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.Or.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#BlankNode",
            memberBKind: "http://www.w3.org/ns/shacl#Literal",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Or, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task NotFocusNotConformingToInnerProducesNoViolation()
    {
        //Inner: sh:nodeKind sh:Literal. IRI focus does NOT conform ⇒
        //sh:not satisfied.
        ValidationReport report = await RunNotAsync(
            innerKind: "http://www.w3.org/ns/shacl#Literal",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task NotFocusConformingToInnerEmitsViolation()
    {
        //Inner: sh:nodeKind sh:IRI. IRI focus DOES conform ⇒ sh:not
        //violated.
        ValidationReport report = await RunNotAsync(
            innerKind: "http://www.w3.org/ns/shacl#IRI",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Not, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task XoneFocusConformingToExactlyOneMemberProducesNoViolation()
    {
        //IRI focus passes MemberA (sh:IRI), fails MemberB (sh:Literal).
        //Count = 1 ⇒ sh:xone satisfied.
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.Xone.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#IRI",
            memberBKind: "http://www.w3.org/ns/shacl#Literal",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task XoneFocusConformingToBothMembersEmitsViolation()
    {
        //IRI focus passes both ⇒ count = 2 ⇒ sh:xone violated.
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.Xone.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#IRI",
            memberBKind: "http://www.w3.org/ns/shacl#IRIOrLiteral",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Xone, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task XoneFocusConformingToNoMemberEmitsViolation()
    {
        //IRI focus fails both ⇒ count = 0 ⇒ sh:xone violated.
        ValidationReport report = await RunBooleanJunctionAsync(
            ShaclConstraintVocabulary.Xone.ToString(),
            memberAKind: "http://www.w3.org/ns/shacl#BlankNode",
            memberBKind: "http://www.w3.org/ns/shacl#Literal",
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.Xone, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task RecursionGuardMutuallyRecursiveShapesTerminate()
    {
        //ShapeA sh:node ShapeB; ShapeB sh:node ShapeA. Termination is
        //the property under test — a broken guard would stack-overflow
        //or deadlock. Per the recursion guard, the re-entered pair is
        //treated as conforming, so no violation surfaces.
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ShapeA)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(Focus))
            .With(ShaclConstraintVocabulary.Node.ToString(), ShapeGraphBuilder.Iri(ShapeB));
        builder.NodeShape(ShapeB)
            .With(ShaclConstraintVocabulary.Node.ToString(), ShapeGraphBuilder.Iri(ShapeA));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        _ = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Focus)));
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task RecursionGuardSelfRecursiveShapeTerminates()
    {
        //A single shape that references itself. Same cycle-breaking
        //behaviour as mutual recursion.
        ShapeGraphBuilder builder = new();
        builder.NodeShape(ShapeA)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(Focus))
            .With(ShaclConstraintVocabulary.Node.ToString(), ShapeGraphBuilder.Iri(ShapeA));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        _ = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Focus)));
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    //Helpers below.

    //Builds a node shape whose constraint (And, Or, or Xone) wraps a
    //two-member list of node shapes with the given sh:nodeKind values.
    //The focus is an IRI (Focus), so the test scenarios are determined
    //by which IRI-relevant kinds the members declare.
    private static async Task<ValidationReport> RunBooleanJunctionAsync(
        string junctionConstraintIri,
        string memberAKind,
        string memberBKind,
        System.Threading.CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        RdfTerm memberList = builder.List(
            ShapeGraphBuilder.Iri(MemberA),
            ShapeGraphBuilder.Iri(MemberB));

        builder.NodeShape(OuterShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(Focus))
            .With(junctionConstraintIri, memberList);
        builder.NodeShape(MemberA)
            .With(ShaclConstraintVocabulary.NodeKind.ToString(), ShapeGraphBuilder.Iri(memberAKind));
        builder.NodeShape(MemberB)
            .With(ShaclConstraintVocabulary.NodeKind.ToString(), ShapeGraphBuilder.Iri(memberBKind));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _ = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Focus)));
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    //Builds a node shape with a sh:not constraint pointing at an inner
    //shape that asserts the given sh:nodeKind. Focus is the IRI Focus,
    //so the scenario is driven by whether innerKind matches IRI.
    private static async Task<ValidationReport> RunNotAsync(
        string innerKind,
        System.Threading.CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.NodeShape(OuterShape)
            .With(ShaclCoreVocabulary.TargetNode.ToString(), ShapeGraphBuilder.Iri(Focus))
            .With(ShaclConstraintVocabulary.Not.ToString(), ShapeGraphBuilder.Iri(InnerShape));
        builder.NodeShape(InnerShape)
            .With(ShaclConstraintVocabulary.NodeKind.ToString(), ShapeGraphBuilder.Iri(innerKind));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _ = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Focus)));
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([]);

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary,
            ShaclBuiltInEvaluators.All,
            VeritasClock.System, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
