using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="RdfsMaterialization"/>: example cases for
/// each rule family, the schema-rewriting corner where derivation
/// produces new schema statements, and a differential property
/// test pinning the closure-driven materializer against a naive
/// rule-at-a-time fixpoint over random graphs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference.</b> The naive oracle applies the textbook
/// rules — rdfs2 (domain), rdfs3 (range), rdfs5 (subproperty
/// transitivity), rdfs7 (subproperty inheritance), rdfs9 (type
/// inheritance), rdfs11 (subclass transitivity) — one binding at a
/// time over the whole accumulated set until nothing new appears.
/// It shares no code with the closure machinery under test.
/// </para>
/// <para>
/// <b>Generator shape.</b> Term ids are drawn from a domain that
/// overlaps the vocabulary ids themselves, so generated graphs use
/// <c>rdfs:subClassOf</c> and friends as ordinary subjects and
/// objects, declare domains on schema predicates, and chain
/// subproperties into the vocabulary — the corners where a
/// precomputed-closure implementation diverges from the rules if
/// its schema-rewrite handling is wrong.
/// </para>
/// </remarks>
[TestClass]
internal sealed class RdfsMaterializationTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static TermId Type { get; } = TermId.FromEncoded(1);

    private static TermId SubClassOf { get; } = TermId.FromEncoded(2);

    private static TermId SubPropertyOf { get; } = TermId.FromEncoded(3);

    private static TermId Domain { get; } = TermId.FromEncoded(4);

    private static TermId Range { get; } = TermId.FromEncoded(5);

    private static RdfsVocabularyTerms Terms { get; } = new(Type, SubClassOf, SubPropertyOf, Domain, Range);

    //Matches the repo's property-test budget.
    private const long Iterations = 10_000;

    /// <summary>A subclass chain types an instance with every strict ancestor (rdfs9 + rdfs11).</summary>
    [TestMethod]
    public void SubClassChainDerivesAncestorTypesAndClosureEdges()
    {
        EncodedTriple[] triples =
        [
            T(10, SubClassOf.Encoded, 11),
            T(11, SubClassOf.Encoded, 12),
            T(20, Type.Encoded, 10),
        ];

        HashSet<EncodedTriple> derived = [.. RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken)];

        HashSet<EncodedTriple> expected =
        [
            T(20, Type.Encoded, 11),
            T(20, Type.Encoded, 12),
            T(10, SubClassOf.Encoded, 12),
        ];

        Assert.IsTrue(expected.SetEquals(derived), Describe(expected, derived));
    }

    /// <summary>A subproperty chain re-asserts the triple under every strict superproperty (rdfs7 + rdfs5).</summary>
    [TestMethod]
    public void SubPropertyChainDerivesInheritedStatementsAndClosureEdges()
    {
        EncodedTriple[] triples =
        [
            T(10, SubPropertyOf.Encoded, 11),
            T(11, SubPropertyOf.Encoded, 12),
            T(20, 10, 21),
        ];

        HashSet<EncodedTriple> derived = [.. RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken)];

        HashSet<EncodedTriple> expected =
        [
            T(20, 11, 21),
            T(20, 12, 21),
            T(10, SubPropertyOf.Encoded, 12),
        ];

        Assert.IsTrue(expected.SetEquals(derived), Describe(expected, derived));
    }

    /// <summary>Domain and range declared on a superproperty type the subject and object, expanded through the class hierarchy (rdfs2/rdfs3 composed with rdfs7 and rdfs9).</summary>
    [TestMethod]
    public void DomainAndRangeComposeThroughPropertyAndClassHierarchies()
    {
        EncodedTriple[] triples =
        [
            T(10, SubPropertyOf.Encoded, 11),
            T(11, Domain.Encoded, 30),
            T(11, Range.Encoded, 31),
            T(30, SubClassOf.Encoded, 32),
            T(20, 10, 21),
        ];

        HashSet<EncodedTriple> derived = [.. RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken)];

        HashSet<EncodedTriple> expected =
        [
            T(20, 11, 21),
            T(20, Type.Encoded, 30),
            T(20, Type.Encoded, 32),
            T(21, Type.Encoded, 31),
            T(30, SubClassOf.Encoded, 32),
        ];

        //The (30 subClassOf 32) base triple is not derived; remove
        //it from the expectation — it is in the base. The closure
        //edge list above contains only what the base lacks.
        expected.Remove(T(30, SubClassOf.Encoded, 32));

        Assert.IsTrue(expected.SetEquals(derived), Describe(expected, derived));
    }

    /// <summary>Subclass cycles terminate and type every member of the cycle as every other member.</summary>
    [TestMethod]
    public void SubClassCycleTerminatesAndDerivesMutualTypes()
    {
        EncodedTriple[] triples =
        [
            T(10, SubClassOf.Encoded, 11),
            T(11, SubClassOf.Encoded, 10),
            T(20, Type.Encoded, 10),
        ];

        HashSet<EncodedTriple> derived = [.. RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken)];

        Assert.Contains(T(20, Type.Encoded, 11), derived, "Cycle member 11 must type the instance.");
    }

    /// <summary>A property declared a subproperty of rdfs:subClassOf derives schema edges from instance triples; the schema re-extracts and the wider hierarchy applies (the schema-rewrite corner).</summary>
    [TestMethod]
    public void DerivedSchemaStatementsWidenTheSchemaMidRun()
    {
        EncodedTriple[] triples =
        [
            T(10, SubPropertyOf.Encoded, SubClassOf.Encoded),
            T(30, 10, 31),
            T(20, Type.Encoded, 30),
        ];

        HashSet<EncodedTriple> derived = [.. RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken)];

        Assert.Contains(T(30, SubClassOf.Encoded, 31), derived, "rdfs7 must lift the instance triple into a subClassOf statement.");
        Assert.Contains(T(20, Type.Encoded, 31), derived, "The derived subclass edge must feed rdfs9 after schema re-extraction.");
    }

    /// <summary>A triple set with no schema statements derives nothing.</summary>
    [TestMethod]
    public void EmptySchemaDerivesNothing()
    {
        EncodedTriple[] triples =
        [
            T(20, 10, 21),
            T(21, 10, 22),
        ];

        IReadOnlyCollection<EncodedTriple> derived = RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken);

        Assert.IsEmpty(derived);
    }

    /// <summary>The closure-driven materializer agrees with a naive rule-at-a-time fixpoint on every generated graph.</summary>
    [TestMethod]
    public void MaterializerAgreesWithNaiveFixpointOverRandomGraphs()
    {
        //Term domain [1, 9] overlaps the vocabulary ids (1..5), so
        //schemas mention the vocabulary itself.
        Gen<int[][]> genTriples = Gen.Int[1, 9].Array[3].Array[1, 12];

        genTriples.Sample(rows =>
        {
            EncodedTriple[] triples = [.. rows
                .Select(static r => EncodedTriple.FromEncoded((uint)r[0], (uint)r[1], (uint)r[2]))
                .Distinct()];

            HashSet<EncodedTriple> actual = [.. RdfsMaterialization.MaterializeToFixpoint(triples, Terms, cancellationToken: TestContext.CancellationToken)];
            HashSet<EncodedTriple> expected = NaiveFixpoint(triples);

            Assert.IsTrue(
                expected.SetEquals(actual),
                $"Materializer and naive fixpoint disagree over {triples.Length} triples: {Describe(expected, actual)}");
        }, iter: Iterations);
    }

    /// <summary>Every derivation announces its rule, premise, and conclusion on the inference trace stream.</summary>
    [TestMethod]
    public void DerivationsAnnounceRulePremiseAndConclusion()
    {
        EncodedTriple typeTriple = T(20, Type.Encoded, 10);
        EncodedTriple[] triples =
        [
            T(10, SubClassOf.Encoded, 11),
            typeTriple,
        ];

        List<InferenceTraceEvent> events = [];

        RdfsMaterialization.MaterializeToFixpoint(
            triples,
            Terms,
            (in InferenceTraceEvent evt) => events.Add(evt),
            VeritasClock.System,
            cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(1, events);
        Assert.AreEqual(EntailmentRules.Rdfs9, events[0].Rule);
        Assert.HasCount(1, events[0].Premises);
        Assert.AreEqual(typeTriple, events[0].Premises[0]);
        Assert.AreEqual(T(20, Type.Encoded, 11), events[0].Conclusion);
        Assert.AreEqual(1L, events[0].SequenceNumber);
    }

    /// <summary>The commit helper writes exactly the derived triples through an edit session; the new store answers with them and the original is untouched.</summary>
    [TestMethod]
    public async Task MaterializeAndCommitWritesDerivedTriplesToANewSnapshot()
    {
        EncodedTriple[] triples =
        [
            T(10, SubClassOf.Encoded, 11),
            T(20, Type.Encoded, 10),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        (HypertrieGraphStore materialized, int derivedCount) = await RdfsMaterialization
            .MaterializeAndCommitAsync(store, Terms, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, derivedCount);
        Assert.AreEqual(2, store.Count, "The base store must be untouched.");
        Assert.AreEqual(3, materialized.Count, "The committed store carries base plus derived triples.");

        bool derivedPresent = false;

        foreach(EncodedTriple triple in materialized.Match(TermId.FromEncoded(20), Type, TermId.FromEncoded(11)))
        {
            derivedPresent = true;
        }

        Assert.IsTrue(derivedPresent, "The derived type statement must be queryable in the committed store.");
    }

    private static EncodedTriple T(uint subject, uint predicate, uint @object)
    {
        return EncodedTriple.FromEncoded(subject, predicate, @object);
    }

    //The naive oracle: apply each rule over the whole accumulated
    //set, one binding at a time, until a full pass adds nothing.
    //Returns only the derived triples.
    private static HashSet<EncodedTriple> NaiveFixpoint(EncodedTriple[] baseTriples)
    {
        HashSet<EncodedTriple> all = [.. baseTriples];
        bool changed = true;

        while(changed)
        {
            changed = false;
            EncodedTriple[] snapshot = [.. all];

            foreach(EncodedTriple left in snapshot)
            {
                //rdfs5: (p subProp q), (q subProp r) → (p subProp r)
                //rdfs11: (c subClass d), (d subClass e) → (c subClass e)
                //rdfs7: (p subProp q), (s p o) → (s q o)
                //rdfs9: (c subClass d), (s type c) → (s type d)
                //rdfs2: (p domain C), (s p o) → (s type C)
                //rdfs3: (p range C), (s p o) → (o type C)
                foreach(EncodedTriple right in snapshot)
                {
                    if(left.Predicate == SubPropertyOf && right.Predicate == SubPropertyOf && right.Subject == left.Object)
                    {
                        changed |= all.Add(EncodedTriple.FromEncoded(left.Subject.Encoded, SubPropertyOf.Encoded, right.Object.Encoded));
                    }

                    if(left.Predicate == SubClassOf && right.Predicate == SubClassOf && right.Subject == left.Object)
                    {
                        changed |= all.Add(EncodedTriple.FromEncoded(left.Subject.Encoded, SubClassOf.Encoded, right.Object.Encoded));
                    }

                    if(left.Predicate == SubPropertyOf && right.Predicate == left.Subject)
                    {
                        changed |= all.Add(EncodedTriple.FromEncoded(right.Subject.Encoded, left.Object.Encoded, right.Object.Encoded));
                    }

                    if(left.Predicate == SubClassOf && right.Predicate == Type && right.Object == left.Subject)
                    {
                        changed |= all.Add(EncodedTriple.FromEncoded(right.Subject.Encoded, Type.Encoded, left.Object.Encoded));
                    }

                    if(left.Predicate == Domain && right.Predicate == left.Subject)
                    {
                        changed |= all.Add(EncodedTriple.FromEncoded(right.Subject.Encoded, Type.Encoded, left.Object.Encoded));
                    }

                    if(left.Predicate == Range && right.Predicate == left.Subject)
                    {
                        changed |= all.Add(EncodedTriple.FromEncoded(right.Object.Encoded, Type.Encoded, left.Object.Encoded));
                    }
                }
            }
        }

        all.ExceptWith(baseTriples);

        return all;
    }

    private static string Describe(HashSet<EncodedTriple> expected, HashSet<EncodedTriple> actual)
    {
        HashSet<EncodedTriple> missing = [.. expected];
        missing.ExceptWith(actual);

        HashSet<EncodedTriple> extra = [.. actual];
        extra.ExceptWith(expected);

        string missingText = string.Join(", ", missing.Select(static t => $"({t.Subject.Encoded} {t.Predicate.Encoded} {t.Object.Encoded})"));
        string extraText = string.Join(", ", extra.Select(static t => $"({t.Subject.Encoded} {t.Predicate.Encoded} {t.Object.Encoded})"));

        return $"missing: [{missingText}]; extra: [{extraText}]";
    }
}
