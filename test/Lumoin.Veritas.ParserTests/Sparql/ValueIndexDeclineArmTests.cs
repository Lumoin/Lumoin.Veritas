using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Rdf.Indexing;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The value-index full-corpus decline arm: with <see cref="SparqlEnginePolicy.PreferValueIndexes"/>
/// ON and a registered temporal index whose (datatype, predicate) matches NOTHING in any suite, every
/// evaluation and update fixture produces the SAME outcome as the baseline — the decline path is
/// total across the corpus, certified distinctly from the empty-registry no-op (the registry is
/// non-empty; every probe consultation must decline cleanly). The IsolationArm template; a standing
/// differential the seam must keep green.
/// </summary>
[TestClass]
internal sealed class ValueIndexDeclineArmTests
{
    /// <summary>The SPARQL evaluation suite folders the arm sweeps.</summary>
    private static string[] EvalSuites { get; } =
    [
        "eval-smoke", "aggregates", "bind", "bindings", "cast", "construct", "exists", "functions",
        "grouping", "negation", "project-expression", "property-path", "subquery", "expression",
        "eval-triple-terms", "sparql12-grouping", "sparql12-rdf11", "lang-basedir", "entailment",
        "json-res", "csv-tsv-res",
    ];

    /// <summary>The SPARQL update suite folders the arm sweeps.</summary>
    private static string[] UpdateSuites { get; } =
    [
        "basic-update", "delete-data", "delete", "delete-where", "delete-insert", "add", "clear",
        "copy", "move", "drop", "update-silent",
    ];

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every evaluation and update fixture in the corpus produces the SAME outcome with the never-matching registry and the probe flag ON as the baseline — decline-path totality.</summary>
    [TestMethod]
    public async Task DeclineArmMatchesBaselineAcrossTheCorpus()
    {
        SparqlEnginePolicy preferring = new(PreferValueIndexes: true);
        ValueIndexRegistry neverMatching = NeverMatchingRegistry();
        MethodInfo self = typeof(ValueIndexDeclineArmTests).GetMethod(nameof(DeclineArmMatchesBaselineAcrossTheCorpus))!;
        int compared = 0;
        foreach(string suite in EvalSuites)
        {
            foreach(object[] row in new W3cManifestDataAttribute("Sparql", suite).GetData(self))
            {
                W3cTestCase testCase = (W3cTestCase)row[0];
                if(testCase.Type != W3cTestType.SparqlQueryEvaluation)
                {
                    continue;
                }

                W3cOutcome baseline = await W3cSparqlEvalRunner.RunAsync(testCase, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                W3cOutcome declined = await W3cSparqlEvalRunner.RunAsync(testCase, preferring, valueIndexes: neverMatching, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(baseline.Status, declined.Status, $"{suite}/{testCase.Name}: baseline '{baseline.Message}' vs decline arm '{declined.Message}'.");
                compared++;
            }
        }

        foreach(string suite in UpdateSuites)
        {
            foreach(object[] row in new W3cManifestDataAttribute("Sparql", suite).GetData(self))
            {
                W3cTestCase testCase = (W3cTestCase)row[0];
                if(testCase.Type != W3cTestType.SparqlUpdateEvaluation)
                {
                    continue;
                }

                W3cOutcome baseline = await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                W3cOutcome declined = await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, preferring, neverMatching, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(baseline.Status, declined.Status, $"{suite}/{testCase.Name}: baseline '{baseline.Message}' vs decline arm '{declined.Message}'.");
                compared++;
            }
        }

        //The corpus holds 462 evaluation fixtures plus the update fixtures across these suites at the pinned
        //rdf-tests commit; the floor is a tripwire against an accidentally truncated suite list, not an exact census.
        Assert.IsGreaterThan(400, compared, "The decline arm must sweep the corpus, not a slice of it.");
    }

    /// <summary>Composes a real, non-empty registry whose declared predicate exists in NO corpus fixture, so every consultation exercises the decline path rather than the empty-registry branch.</summary>
    /// <returns>The registry.</returns>
    private static ValueIndexRegistry NeverMatchingRegistry()
    {
        Utf8String neverDeclared = Utf8Strings.From("http://veritas.invalid/never-in-any-suite");
        ValueAxisDeclaration axis = ValueAxisDeclaration.PointAxis(neverDeclared);

        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, axis, TimeSpan.Zero),
                axis,
                new EmptySource(),
                selfTestCases: []))
            .Build();
    }

    /// <summary>An empty registrant sample corpus (the method's semantics are certified by its own battery).</summary>
    private sealed class EmptySource: ValueSegmentSource
    {
        /// <summary>Enumerates nothing.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>No entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return [];
        }
    }
}
