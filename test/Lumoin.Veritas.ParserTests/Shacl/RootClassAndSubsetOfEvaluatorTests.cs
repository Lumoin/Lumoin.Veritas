using Lumoin.Veritas.Core;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for <see cref="RootClassEvaluator"/>. Per SHACL 1.2 Core
/// §6.3.4: each value node must be a SHACL instance of the
/// <c>sh:rootClass</c> AND no proper SHACL superclass of the root
/// class may apply to the value.
/// </summary>
[TestClass]
internal sealed class RootClassAndSubsetOfEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/foo";
    private const string ExOuterShape = "http://example.org/Outer";
    private const string ExPropShape = "http://example.org/PS";
    private const string ExPath = "http://example.org/typed";

    private const string ExAnimal = "http://example.org/Animal";
    private const string ExMammal = "http://example.org/Mammal";
    private const string ExDog = "http://example.org/Dog";
    private const string ExFido = "http://example.org/Fido";

    [TestMethod]
    public async Task RootClassDirectInstanceOfRootWithNoSuperclassPasses()
    {
        //Hierarchy: just ex:Mammal as root, no superclass.
        //Value: Fido rdf:type Mammal.
        //sh:rootClass ex:Mammal → conform.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            rootClassIri: ExMammal,
            classHierarchy: [],
            valueDirectTypes: [(ExFido, ExMammal)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task RootClassInstanceViaSubclassWithNoSuperclassPasses()
    {
        //Hierarchy: Dog rdfs:subClassOf Mammal. Mammal has no super.
        //Value: Fido rdf:type Dog.
        //sh:rootClass ex:Mammal → conform.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            rootClassIri: ExMammal,
            classHierarchy: [(ExDog, ExMammal)],
            valueDirectTypes: [(ExFido, ExDog)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task RootClassWithSuperclassAboveRootFails()
    {
        //Hierarchy: Dog :> Mammal :> Animal. Now Animal is above the
        //declared root Mammal. Value Fido rdf:type Dog.
        //sh:rootClass ex:Mammal → fail because Animal applies.
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunAsync(
            rootClassIri: ExMammal,
            classHierarchy: [(ExDog, ExMammal), (ExMammal, ExAnimal)],
            valueDirectTypes: [(ExFido, ExDog)],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance because a class above the root applies; trace:\n{trace}");
        Assert.HasCount(1, report.Results,
            "Expected one result for the single non-conforming value.");
        Assert.AreEqual(
            ShaclComponentVocabulary.RootClass,
            report.Results[0].SourceConstraintComponent,
            "Source constraint component should be sh:RootClassConstraintComponent.");
    }

    [TestMethod]
    public async Task RootClassValueIsNotInstanceOfRootClassFails()
    {
        //Hierarchy: just Dog :> Mammal.
        //Value: Fido rdf:type ex:Vehicle (unrelated to Mammal).
        //sh:rootClass ex:Mammal → fail because Fido is not a SHACL
        //instance of Mammal at all.
        const string ExVehicle = "http://example.org/Vehicle";
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunAsync(
            rootClassIri: ExMammal,
            classHierarchy: [(ExDog, ExMammal)],
            valueDirectTypes: [(ExFido, ExVehicle)],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance because value is not a SHACL instance of the root class; trace:\n{trace}");
        Assert.HasCount(1, report.Results,
            "Expected one result for the single non-conforming value.");
    }

    [TestMethod]
    public async Task RootClassNoValuesAtPathConformsVacuously()
    {
        //No values at the path means the rootClass evaluator is
        //never invoked (PropertyEvaluator's value-node set is
        //empty). Vacuous conformance is the spec-correct outcome
        //and this test pins it.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            rootClassIri: ExMammal,
            classHierarchy: [],
            valueDirectTypes: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task RootClassMultipleValuesAllFailingProduceOneResultEach()
    {
        //Two values both typed Dog; the hierarchy is
        //Dog :> Mammal :> Animal. With root = Mammal, the Animal
        //superclass applies to both → both violate. Pins the
        //per-value-result-emission semantics: 2 results expected.
        const string ExRex = "http://example.org/Rex";
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunAsync(
            rootClassIri: ExMammal,
            classHierarchy: [(ExDog, ExMammal), (ExMammal, ExAnimal)],
            valueDirectTypes: [(ExFido, ExDog), (ExRex, ExDog)],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance for both values; trace:\n{trace}");
        Assert.HasCount(2, report.Results,
            "Expected one result per non-conforming value.");
    }

    //RunAsync helpers below.

    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    //Builds:
    //  ExOuterShape (NodeShape)
    //      sh:targetNode ExFocus
    //      sh:property ExPropShape
    //  ExPropShape (PropertyShape)
    //      sh:path ExPath
    //      sh:rootClass <rootClassIri>
    //
    //Data graph:
    //  ExFocus ExPath subjectIri    for each (subjectIri, _) in valueDirectTypes
    //  subjectIri rdf:type typeIri  for each (subjectIri, typeIri) in valueDirectTypes
    //  subIri rdfs:subClassOf superIri  for each (subIri, superIri) in classHierarchy
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunAsync(
        string rootClassIri,
        IReadOnlyList<(string SubClass, string SuperClass)> classHierarchy,
        IReadOnlyList<(string Value, string Type)> valueDirectTypes,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(ShaclConstraintVocabulary.RootClass.ToString(),
                ShapeGraphBuilder.Iri(rootClassIri));

        TestShaclPipelineShapeState shapeState = scenario
            .WithNodeShapeTargetingPipelineFocus(ExOuterShape)
            .With(ShaclConstraintVocabulary.Property.ToString(),
                ShapeGraphBuilder.Iri(ExPropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        //Wire focus -> ExPath -> each value, then each value's
        //direct type, then the class hierarchy.
        foreach((string valueIri, string typeIri) in valueDirectTypes)
        {
            dataState = dataState.WithTripleOnFocus(
                ExPath, new NamedNode(Utf8Strings.From(valueIri)));

            dataState = dataState.WithExplicitTriple(
                subjectIri: valueIri,
                predicateIri: RdfType,
                @object: new NamedNode(Utf8Strings.From(typeIri)));
        }

        foreach((string subIri, string superIri) in classHierarchy)
        {
            dataState = dataState.WithExplicitTriple(
                subjectIri: subIri,
                predicateIri: RdfsSubClassOf,
                @object: new NamedNode(Utf8Strings.From(superIri)));
        }

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.RootClass, RootClassEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }
}

/// <summary>
/// Tests for <see cref="SubsetOfEvaluator"/>. Per SHACL 1.2 Core §6.8.5:
/// the value-node set must be a subset of the values found at the focus
/// node's other predicate.
/// </summary>
/// <remarks>
/// Follows the convention established by
/// <see cref="PairPropertyEvaluatorTests"/>: a property shape directly
/// targeting subjects-of-path (no outer node-shape wrapper), values
/// at both the path predicate and the comparison predicate emitted on
/// the focus, and the
/// <see cref="ValidationAssertions.AssertSingleViolationFromComponent"/>
/// shorthand for single-violation tests.
/// </remarks>
[TestClass]
internal sealed class SubsetOfEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExPath = "http://example.org/sub";
    private const string ExOther = "http://example.org/super";
    private const string ExFocus = "http://example.org/foo";

    [TestMethod]
    public async Task SubsetOfEmptyValueSetPasses()
    {
        //No values at ExPath; subset of anything.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            values: [],
            comparisons: [IriTerm("http://example.org/A"), IriTerm("http://example.org/B")],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task SubsetOfStrictSubsetPasses()
    {
        //Values: {A}. Super: {A, B}. Subset → conform.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            values: [IriTerm("http://example.org/A")],
            comparisons: [IriTerm("http://example.org/A"), IriTerm("http://example.org/B")],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task SubsetOfEqualSetsPasses()
    {
        //Values: {A, B}. Super: {A, B}. Equal → still subset.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            values: [IriTerm("http://example.org/A"), IriTerm("http://example.org/B")],
            comparisons: [IriTerm("http://example.org/A"), IriTerm("http://example.org/B")],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task SubsetOfSingleMissingValueFails()
    {
        //Values: {A, C}. Super: {A, B}. C is missing → one violation.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            values: [IriTerm("http://example.org/A"), IriTerm("http://example.org/C")],
            comparisons: [IriTerm("http://example.org/A"), IriTerm("http://example.org/B")],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.SubsetOf);
    }

    [TestMethod]
    public async Task SubsetOfMultipleMissingValuesProduceMultipleResults()
    {
        //Values: {C, D}. Super: {A, B}. Both missing.
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunAsync(
            values: [IriTerm("http://example.org/C"), IriTerm("http://example.org/D")],
            comparisons: [IriTerm("http://example.org/A"), IriTerm("http://example.org/B")],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance; trace:\n{trace}");
        Assert.HasCount(2, report.Results,
            "Expected one result per missing value.");
        Assert.IsTrue(
            report.Results.All(r => r.SourceConstraintComponent.Equals(ShaclComponentVocabulary.SubsetOf)),
            "All results must attribute to sh:SubsetOfConstraintComponent.");
    }

    [TestMethod]
    public async Task SubsetOfEmptySupersetWithNonEmptyValuesFails()
    {
        //Values: {A}. Super: {} (no triples at ExOther). Every value violates.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            values: [IriTerm("http://example.org/A")],
            comparisons: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.SubsetOf);
    }

    //Helpers below.

    private static NamedNode IriTerm(string iri) => new(Utf8Strings.From(iri));

    //Mirrors PairPropertyEvaluatorTests.RunPairAsync. The property
    //shape directly targets subjects-of-path; values are emitted at
    //both ExPath (the constraint's value nodes) and ExOther (the
    //comparison set named via sh:subsetOf). No outer node-shape is
    //needed because the property shape is the targeted shape.
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunAsync(
        IReadOnlyList<RdfTerm> values,
        IReadOnlyList<RdfTerm> comparisons,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState shapeState = TestShaclPipeline
            .BeginWithFocus(ExFocus)
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, ExPath)
            .With(ShaclConstraintVocabulary.SubsetOf.ToString(),
                ShapeGraphBuilder.Iri(ExOther))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithTriplesOnFocus(ExPath, values)
            .WithTriplesOnFocus(ExOther, comparisons)
            .WithEvaluator(ShaclComponentVocabulary.SubsetOf, SubsetOfEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }
}
