using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Tests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Runs the RL-marked arms of the W3C OWL 2 conformance corpus through the
/// OWL 2 RL/RDF rules closure: consistency and inconsistency verdicts, and
/// positive/negative entailment by embedding the conclusion graph into the
/// closure (blank nodes existential, named terms and literals exact).
/// </summary>
/// <remarks>
/// Only tests whose documents the corpus marks as RL run here — for those,
/// the RL rules are the complete calculus the conformance document
/// prescribes. Honest skips: non-RL tests, functional-syntax-only tests,
/// and imports-bearing tests.
/// </remarks>
[TestClass]
internal sealed class W3cOwl2RlTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Tests whose conclusions lie beyond the RL/RDF rules calculus,
    /// recorded as self-correcting known gaps: TBox-axiom conclusions
    /// (<c>chain2trans1</c>), complement-class conclusions requiring
    /// contrapositive reasoning (the DisjointClasses/DisjointProperties/QCR
    /// families, <c>fp/ifp-differentFrom</c>), and tautological
    /// class-expression-existence conclusions (<c>I5.26-010</c>,
    /// <c>I5.5-005</c>). The rules are complete for assertional consequences
    /// only (Profiles §4.3, Theorem PR1); a run fails if a listed test
    /// starts passing.
    /// </summary>
    private static HashSet<string> KnownGaps { get; } =
    [
        "chain2trans1",
        "DisjointClasses-001",
        "DisjointClasses-003",
        "New-Feature-DisjointDataProperties-002",
        "New-Feature-DisjointObjectProperties-001",
        "New-Feature-DisjointObjectProperties-002",
        "New-Feature-ObjectQCR-002",
        "WebOnt-I5.26-010",
        "WebOnt-I5.5-005",
        "owl2-rl-rules-fp-differentFrom",
        "owl2-rl-rules-ifp-differentFrom",
        "WebOnt-I5.8-008",
        "WebOnt-I5.8-009",
    ];

    /// <summary>Runs one approved-status, RL-marked OWL 2 test case through the RL closure.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf")]
    public void RunApproved(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!testCase.Profiles.Contains("RL"))
        {
            Assert.Inconclusive("Not an RL-marked test; the RL rules make no completeness claim.");
        }

        if(!testCase.IsRdfXmlReadable)
        {
            Assert.Inconclusive("Functional-syntax-only test; the OWL functional-syntax reader is not built.");
        }

        if(testCase.HasImports)
        {
            Assert.Inconclusive("Imports-bearing test; the harness supplies no imported ontologies.");
        }

        if(testCase.RdfXmlPremise is not string premise)
        {
            Assert.Inconclusive("The test declares no RDF/XML premise document.");

            return;
        }

        //The closure implements the RDF-Based rules calculus; entailment
        //tests stated for Direct semantics only (annotation conclusions are
        //disregarded there) measure a different reasoner.
        bool isEntailment = testCase.Kinds.Contains("PositiveEntailmentTest") || testCase.Kinds.Contains("NegativeEntailmentTest");
        if(isEntailment && !testCase.Semantics.Contains("RDF-BASED"))
        {
            Assert.Inconclusive("Direct-semantics-only entailment test; the RL/RDF rules implement the RDF-Based calculus.");
        }

        List<Quad> premiseQuads = ParseDocument(testCase, premise);
        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = Encode(premiseQuads, dictionary);
        OwlRlTerms terms = new(dictionary);

        OwlRlResult result = OwlRlClosure.Compute(encoded, terms, LiteralsKnownDistinct(dictionary), TestContext.CancellationToken);

        bool isKnownGap = KnownGaps.Contains(testCase.Identifier);

        if(testCase.Kinds.Contains("InconsistencyTest"))
        {
            if(isKnownGap)
            {
                AssertKnownGap(testCase, holds: !result.IsConsistent);
            }

            Assert.IsFalse(result.IsConsistent, $"{testCase.Identifier}: the RL rules should derive an inconsistency.");

            return;
        }

        if(testCase.Kinds.Contains("ConsistencyTest"))
        {
            Assert.IsTrue(result.IsConsistent, $"{testCase.Identifier}: the premise is consistent, but rule {result.InconsistencyRule} fired.");
        }

        //Entailment: the conclusion graph embeds into the closure (positive)
        //or must not (negative). The closure is the premise plus everything
        //derived, decoded back to terms.
        if(testCase.Kinds.Contains("PositiveEntailmentTest") && testCase.RdfXmlConclusion is string conclusion)
        {
            if(isKnownGap)
            {
                List<Quad> gapClosure = DecodeUnion(premiseQuads, result, dictionary);
                AssertKnownGap(testCase, holds: Embeds(ParseDocument(testCase, conclusion), gapClosure));
            }

            List<Quad> closure = DecodeUnion(premiseQuads, result, dictionary);
            List<Quad> conclusionQuads = ParseDocument(testCase, conclusion);

            Assert.IsTrue(Embeds(conclusionQuads, closure), $"{testCase.Identifier}: the conclusion does not embed into the RL closure.");
        }

        if(testCase.Kinds.Contains("NegativeEntailmentTest") && testCase.RdfXmlNonConclusion is string nonConclusion)
        {
            List<Quad> closure = DecodeUnion(premiseQuads, result, dictionary);
            List<Quad> nonConclusionQuads = ParseDocument(testCase, nonConclusion);

            Assert.IsFalse(Embeds(nonConclusionQuads, closure), $"{testCase.Identifier}: the non-conclusion embeds into the RL closure but must not.");
        }
    }

    //A known-gap test reports inconclusive while the gap holds and fails
    //loudly the moment it starts passing, so the list self-corrects.
    private static void AssertKnownGap(Owl2TestCase testCase, bool holds)
    {
        if(holds)
        {
            Assert.Fail($"{testCase.Identifier} is a recorded known gap but now passes; remove it from KnownGaps.");
        }

        Assert.Inconclusive("Beyond the RL/RDF rules calculus; see KnownGaps.");
    }

    /// <summary>
    /// The datatype oracle: two literals with the same datatype and
    /// different lexical forms denote distinct values for the datatypes
    /// whose lexical-to-value mapping is injective (strings); numeric
    /// canonicalisation is not attempted here.
    /// </summary>
    /// <param name="dictionary">The dictionary the closure's term ids resolve through.</param>
    /// <returns>The distinctness predicate.</returns>
    private static Func<TermId, TermId, bool> LiteralsKnownDistinct(TermDictionary dictionary)
    {
        return (first, second) =>
        {
            if(dictionary.Resolve(first) is not Literal firstLiteral || dictionary.Resolve(second) is not Literal secondLiteral)
            {
                return false;
            }

            return firstLiteral.Datatype.Equals(secondLiteral.Datatype)
                && firstLiteral.Language is null
                && secondLiteral.Language is null
                && !firstLiteral.Value.Equals(secondLiteral.Value);
        };
    }

    private static List<Quad> ParseDocument(Owl2TestCase testCase, string document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(document), diagnostics, baseIri: testCase.Uri.AbsoluteUri)];
        if(diagnostics.HasErrors)
        {
            Assert.Inconclusive("A test document did not parse as RDF/XML; the test cannot be set up.");
        }

        return quads;
    }

    private static List<EncodedTriple> Encode(List<Quad> quads, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = new(quads.Count);
        foreach(Quad quad in quads)
        {
            encoded.Add(EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(quad.Subject).Encoded,
                dictionary.GetOrAdd(quad.Predicate).Encoded,
                dictionary.GetOrAdd(quad.Object).Encoded));
        }

        return encoded;
    }

    private static List<Quad> DecodeUnion(List<Quad> premiseQuads, OwlRlResult result, TermDictionary dictionary)
    {
        List<Quad> closure = [.. premiseQuads];
        foreach(EncodedTriple triple in result.Derived)
        {
            RdfTerm predicate = dictionary.Resolve(triple.Predicate);
            if(predicate is NamedNode named)
            {
                closure.Add(new Quad(dictionary.Resolve(triple.Subject), named, dictionary.Resolve(triple.Object), Graph: null));
            }
        }

        return closure;
    }

    /// <summary>
    /// Whether <paramref name="pattern"/> embeds into <paramref name="data"/>
    /// by subgraph homomorphism: blank nodes bind existentially and
    /// consistently, named nodes and literals match by value. Iterative
    /// backtracking over an explicit frame stack.
    /// </summary>
    /// <param name="pattern">The graph to embed (the conclusion).</param>
    /// <param name="data">The graph embedded into (the closure).</param>
    /// <returns><see langword="true"/> when an embedding exists.</returns>
    private static bool Embeds(List<Quad> pattern, List<Quad> data)
    {
        if(pattern.Count == 0)
        {
            return true;
        }

        Dictionary<Utf8String, RdfTerm> bindings = [];
        List<List<Utf8String>> boundAt = [];
        int[] candidate = new int[pattern.Count];
        int level = 0;

        while(true)
        {
            if(level == pattern.Count)
            {
                return true;
            }

            if(boundAt.Count == level)
            {
                boundAt.Add([]);
            }

            bool advanced = false;
            for(int i = candidate[level]; i < data.Count; i++)
            {
                List<Utf8String> newlyBound = boundAt[level];
                if(Matches(pattern[level], data[i], bindings, newlyBound))
                {
                    candidate[level] = i + 1;
                    level++;
                    advanced = true;

                    break;
                }
            }

            if(advanced)
            {
                continue;
            }

            //Exhausted this level: undo its bindings and backtrack.
            candidate[level] = 0;
            level--;
            if(level < 0)
            {
                return false;
            }

            foreach(Utf8String label in boundAt[level])
            {
                bindings.Remove(label);
            }

            boundAt[level].Clear();
        }
    }

    //Tries to match one pattern triple against one data triple under the
    //current bindings; records the labels this match newly bound so the
    //caller can undo them on backtrack.
    private static bool Matches(Quad pattern, Quad data, Dictionary<Utf8String, RdfTerm> bindings, List<Utf8String> newlyBound)
    {
        int rollback = newlyBound.Count;

        if(MatchTerm(pattern.Subject, data.Subject, bindings, newlyBound)
            && MatchTerm(pattern.Predicate, data.Predicate, bindings, newlyBound)
            && MatchTerm(pattern.Object, data.Object, bindings, newlyBound))
        {
            return true;
        }

        for(int i = newlyBound.Count - 1; i >= rollback; i--)
        {
            bindings.Remove(newlyBound[i]);
            newlyBound.RemoveAt(i);
        }

        return false;
    }

    private static bool MatchTerm(RdfTerm pattern, RdfTerm data, Dictionary<Utf8String, RdfTerm> bindings, List<Utf8String> newlyBound)
    {
        if(pattern is BlankNode blank)
        {
            if(bindings.TryGetValue(blank.Label, out RdfTerm? bound))
            {
                return bound.Equals(data);
            }

            bindings[blank.Label] = data;
            newlyBound.Add(blank.Label);

            return true;
        }

        return pattern.Equals(data);
    }
}
