using System;
using System.Threading.Tasks;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

[TestClass]
internal sealed class W3cTestRunnerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PositiveSyntaxPassesOnCleanInput()
    {
        W3cTestCase testCase = NewCase(W3cTestType.PositiveSyntax, "positive-input.ttl", expected: null);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Passed, outcome.Status, outcome.Message);
    }

    [TestMethod]
    public async Task PositiveSyntaxFailsOnInvalidInput()
    {
        W3cTestCase testCase = NewCase(W3cTestType.PositiveSyntax, "negative-input.ttl", expected: null);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Failed, outcome.Status);
    }

    [TestMethod]
    public async Task NegativeSyntaxPassesOnInvalidInput()
    {
        W3cTestCase testCase = NewCase(W3cTestType.NegativeSyntax, "negative-input.ttl", expected: null);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Passed, outcome.Status, outcome.Message);
    }

    [TestMethod]
    public async Task NegativeSyntaxFailsOnCleanInput()
    {
        W3cTestCase testCase = NewCase(W3cTestType.NegativeSyntax, "positive-input.ttl", expected: null);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Failed, outcome.Status);
    }

    [TestMethod]
    public async Task EvaluationPassesOnMatchingQuadSets()
    {
        W3cTestCase testCase = NewCase(W3cTestType.Evaluation, "positive-input.ttl", expected: "expected-output.nt");

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Passed, outcome.Status, outcome.Message);
    }

    [TestMethod]
    public async Task EvaluationFailsOnMismatchedQuadSets()
    {
        W3cTestCase testCase = NewCase(W3cTestType.Evaluation, "positive-input.ttl", expected: "unexpected-output.nt");

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Failed, outcome.Status);
    }

    [TestMethod]
    public async Task EvaluationHandlesBlankNodeIsomorphism()
    {
        W3cTestCase testCase = NewCase(W3cTestType.Evaluation, "bnode-input.ttl", expected: "bnode-expected.nt");

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Passed, outcome.Status, outcome.Message);
    }

    [TestMethod]
    public async Task PositiveC14NPassesOnMatchingCanonicalForm()
    {
        W3cTestCase testCase = NewCase(W3cTestType.PositiveC14N, "c14n-input.nt", expected: "c14n-expected.nt");

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => NQuadsReader.ReadAsync(stream, pool: null, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Passed, outcome.Status, outcome.Message);
    }

    [TestMethod]
    public async Task PositiveC14NFailsOnDifferentCanonicalForm()
    {
        W3cTestCase testCase = NewCase(W3cTestType.PositiveC14N, "c14n-input.nt", expected: "unexpected-output.nt");

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => NQuadsReader.ReadAsync(stream, pool: null, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Failed, outcome.Status);
    }

    [TestMethod]
    public async Task MissingFixtureFails()
    {
        W3cTestCase testCase = NewCase(W3cTestType.PositiveSyntax, "no-such-file.ttl", expected: null);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Failed, outcome.Status);
    }

    [TestMethod]
    public async Task UnknownTestTypeBecomesSkipped()
    {
        W3cTestCase testCase = new(
            new Uri("http://example.org/unknown-test"),
            W3cTestType.Unknown,
            "http://example.org/unknown-iri",
            "unknown",
            "comment",
            W3cCorpusPath.FixturePath("positive-input.ttl"),
            null);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(W3cOutcomeStatus.Skipped, outcome.Status);
    }

    private static W3cTestCase NewCase(W3cTestType type, string inputName, string? expected)
    {
        string typeIri = type switch
        {
            W3cTestType.PositiveSyntax => "http://www.w3.org/ns/rdftest#TestTurtlePositiveSyntax",
            W3cTestType.NegativeSyntax => "http://www.w3.org/ns/rdftest#TestTurtleNegativeSyntax",
            W3cTestType.Evaluation => "http://www.w3.org/ns/rdftest#TestTurtleEval",
            W3cTestType.NegativeEvaluation => "http://www.w3.org/ns/rdftest#TestTurtleNegativeEval",
            W3cTestType.PositiveC14N => "http://www.w3.org/ns/rdftest#TestNTriplesPositiveC14N",
            _ => "http://example.org/unknown"
        };

        return new W3cTestCase(
            new Uri("http://example.org/test#" + inputName),
            type,
            typeIri,
            "synthetic",
            "synthetic test case",
            W3cCorpusPath.FixturePath(inputName),
            expected is null ? null : W3cCorpusPath.FixturePath(expected));
    }
}
