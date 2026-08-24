using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for <see cref="UniqueValuesForEvaluator"/>. Per SHACL 1.2
/// Core §6.10.2: each value node must not also appear as a value of
/// any other focus node at any of the listed predicates.
/// </summary>
/// <remarks>
/// <para>
/// The shape targets subjects-of-path, so every subject in the data
/// graph that has the path predicate becomes a focus. A collision
/// between two focuses sharing a value produces violations at
/// <em>both</em> focuses (each runs the constraint independently and
/// each finds the other as the colliding focus).
/// </para>
/// </remarks>
[TestClass]
internal sealed class UniqueValuesForEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExPath = "http://example.org/email";
    private const string ExSecondary = "http://example.org/secondaryEmail";

    private const string ExFocusA = "http://example.org/personA";
    private const string ExFocusB = "http://example.org/personB";

    [TestMethod]
    public async Task UniqueValuesForDistinctValuesAcrossFocusesPasses()
    {
        //personA has email "a@x"; personB has email "b@x". Distinct →
        //no collision.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunSinglePredicateAsync(
            valuesByFocus: new() { [ExFocusA] = "a@x", [ExFocusB] = "b@x" },
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task UniqueValuesForSharedValueProducesTwoViolations()
    {
        //Both personA and personB have email "shared@x". Each focus
        //evaluates the constraint and finds the other as the colliding
        //focus → two violations, one per focus, both for the same
        //value.
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunSinglePredicateAsync(
            valuesByFocus: new() { [ExFocusA] = "shared@x", [ExFocusB] = "shared@x" },
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance for shared value across focuses; trace:\n{trace}");
        Assert.HasCount(2, report.Results,
            "Expected one violation per focus that holds the shared value.");
        Assert.IsTrue(
            report.Results.All(r => r.SourceConstraintComponent.Equals(ShaclComponentVocabulary.UniqueValuesFor)),
            "All results must attribute to sh:UniqueValuesForConstraintComponent.");
    }

    [TestMethod]
    public async Task UniqueValuesForSingleFocusTriviallyUnique()
    {
        //One focus, one value; no other focus exists to collide with.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunSinglePredicateAsync(
            valuesByFocus: new() { [ExFocusA] = "only@x" },
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task UniqueValuesForMultiPredicateCollisionViaSecondaryFails()
    {
        //personA has ex:email "shared@x"; personB has
        //ex:secondaryEmail "shared@x". Constraint lists both
        //predicates; the value "shared@x" of personA collides with
        //personB at ex:secondaryEmail. Per the spec, "any of the
        //listed predicates" — the cross-predicate collision counts.
        //Asymmetric outcome: only personA is a focus (its sh:path is
        //ex:email and personB has no ex:email value), so only personA
        //runs the constraint and only personA produces a result.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunMultiPredicateAsync(
            primaryEmailByFocus: new() { [ExFocusA] = "shared@x" },
            secondaryEmailByFocus: new() { [ExFocusB] = "shared@x" },
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.UniqueValuesFor);
    }

    [TestMethod]
    public async Task UniqueValuesForSameFocusDoesNotSelfCollide()
    {
        //personA has the same value at both ex:email and
        //ex:secondaryEmail. The constraint checks for "any other
        //focus node" — same focus is not a collision. Trivially
        //conforming.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunMultiPredicateAsync(
            primaryEmailByFocus: new() { [ExFocusA] = "x@x" },
            secondaryEmailByFocus: new() { [ExFocusA] = "x@x" },
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //Helpers below.

    private static NamedNode IriTerm(string iri) => new(Utf8Strings.From(iri));

    private static Literal StringLit(string s)
        => new(Utf8Strings.From(s), new NamedNode(Vocabulary.Xsd.String));

    //Builds:
    //  ExShape (PropertyShape) targeting subjects-of-path on ExPath
    //  with sh:uniqueValuesFor (ExPath)
    //
    //Data:
    //  for each (focus, email) in valuesByFocus:
    //    focus ExPath email
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunSinglePredicateAsync(
        Dictionary<string, string> valuesByFocus,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocusA);
        RdfTerm predicateList = scenario.Builder.List(ShapeGraphBuilder.Iri(ExPath));

        TestShaclPipelineShapeState shapeState = scenario
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, ExPath)
            .With(ShaclConstraintVocabulary.UniqueValuesFor.ToString(), predicateList)
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        foreach((string focus, string email) in valuesByFocus)
        {
            dataState = dataState.WithExplicitTriple(
                subjectIri: focus,
                predicateIri: ExPath,
                @object: StringLit(email));
        }

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.UniqueValuesFor, UniqueValuesForEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    //Builds:
    //  ExShape (PropertyShape) targeting subjects-of-path on ExPath
    //  with sh:uniqueValuesFor (ExPath ExSecondary)
    //
    //Data:
    //  primaryEmailByFocus  → focus ExPath  email
    //  secondaryEmailByFocus → focus ExSecondary email
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunMultiPredicateAsync(
        Dictionary<string, string> primaryEmailByFocus,
        Dictionary<string, string> secondaryEmailByFocus,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocusA);
        RdfTerm predicateList = scenario.Builder.List(
            ShapeGraphBuilder.Iri(ExPath),
            ShapeGraphBuilder.Iri(ExSecondary));

        TestShaclPipelineShapeState shapeState = scenario
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, ExPath)
            .With(ShaclConstraintVocabulary.UniqueValuesFor.ToString(), predicateList)
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        foreach((string focus, string email) in primaryEmailByFocus)
        {
            dataState = dataState.WithExplicitTriple(
                subjectIri: focus,
                predicateIri: ExPath,
                @object: StringLit(email));
        }

        foreach((string focus, string email) in secondaryEmailByFocus)
        {
            dataState = dataState.WithExplicitTriple(
                subjectIri: focus,
                predicateIri: ExSecondary,
                @object: StringLit(email));
        }

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.UniqueValuesFor, UniqueValuesForEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }
}
