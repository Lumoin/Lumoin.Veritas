using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The RL closure's derivation records: every derived triple announces the
/// rule that fired and the premises it matched on the inference trace
/// stream, and an inconsistent closure reports the falsity's premises on
/// the result.
/// </summary>
[TestClass]
internal sealed class OwlRlTracingTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void SubclassDerivationCarriesRuleAndPremises()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c1 = Term(dictionary, "http://example.org/C1");
        TermId c2 = Term(dictionary, "http://example.org/C2");
        TermId x = Term(dictionary, "http://example.org/x");

        EncodedTriple subClass = Triple(c1, terms.SubClassOf, c2);
        EncodedTriple membership = Triple(x, terms.Type, c1);
        List<InferenceTraceEvent> events = [];

        OwlRlResult result = OwlRlClosure.Compute(
            [subClass, membership],
            terms,
            traceHandler: (in InferenceTraceEvent evt) => events.Add(evt),
            timeProvider: VeritasClock.System,
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        InferenceTraceEvent derivation = Find(events, EntailmentRules.CaxSco, Triple(x, terms.Type, c2));
        Assert.Contains(subClass, derivation.Premises);
        Assert.Contains(membership, derivation.Premises);
    }

    [TestMethod]
    public void TransitiveCompositionCarriesAllThreePremises()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = Term(dictionary, "http://example.org/p");
        TermId a = Term(dictionary, "http://example.org/a");
        TermId b = Term(dictionary, "http://example.org/b");
        TermId c = Term(dictionary, "http://example.org/c");

        EncodedTriple typing = Triple(p, terms.Type, terms.TransitiveProperty);
        EncodedTriple first = Triple(a, p, b);
        EncodedTriple second = Triple(b, p, c);
        List<InferenceTraceEvent> events = [];

        OwlRlResult result = OwlRlClosure.Compute(
            [typing, first, second],
            terms,
            traceHandler: (in InferenceTraceEvent evt) => events.Add(evt),
            timeProvider: VeritasClock.System,
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        InferenceTraceEvent derivation = Find(events, EntailmentRules.PrpTrp, Triple(a, p, c));
        Assert.Contains(typing, derivation.Premises);
        Assert.Contains(first, derivation.Premises);
        Assert.Contains(second, derivation.Premises);
    }

    [TestMethod]
    public void InconsistencyReportsTheFalsityPremises()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = Term(dictionary, "http://example.org/x");
        TermId y = Term(dictionary, "http://example.org/y");

        EncodedTriple same = Triple(x, terms.SameAs, y);
        EncodedTriple different = Triple(x, terms.DifferentFrom, y);

        OwlRlResult result = OwlRlClosure.Compute(
            [same, different],
            terms,
            cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.EqDiff1, result.InconsistencyRule);
        Assert.Contains(same, result.InconsistencyPremises);
        Assert.Contains(different, result.InconsistencyPremises);
    }

    [TestMethod]
    public void NoHandlerDerivesTheSameClosureSilently()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId c1 = Term(dictionary, "http://example.org/C1");
        TermId c2 = Term(dictionary, "http://example.org/C2");
        TermId x = Term(dictionary, "http://example.org/x");

        List<EncodedTriple> triples = [Triple(c1, terms.SubClassOf, c2), Triple(x, terms.Type, c1)];

        OwlRlResult untraced = OwlRlClosure.Compute(triples, terms, cancellationToken: TestContext.CancellationToken);
        List<InferenceTraceEvent> events = [];
        OwlRlResult traced = OwlRlClosure.Compute(
            triples,
            terms,
            traceHandler: (in InferenceTraceEvent evt) => events.Add(evt),
            timeProvider: VeritasClock.System,
            cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(untraced.Derived.Count, traced.Derived);
        Assert.IsNotEmpty(events);
    }

    private static InferenceTraceEvent Find(List<InferenceTraceEvent> events, string rule, EncodedTriple conclusion)
    {
        foreach(InferenceTraceEvent evt in events)
        {
            if(evt.Rule == rule && evt.Conclusion == conclusion)
            {
                return evt;
            }
        }

        Assert.Fail($"No {rule} derivation concluding {conclusion} was traced.");

        return default;
    }

    private static TermId Term(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    private static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }
}
