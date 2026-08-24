using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Verifies the source-aware
/// <see cref="RdfsInference.InferWithProvenanceAsync"/> overload.
/// Mirrors the eight rule-coverage tests in
/// <c>RdfsInferenceTests</c> and adds antecedent-shape assertions:
/// each emitted <see cref="InferredTriple"/> carries the W3C
/// two-premise schema for its rule in the order (schema premise,
/// ABox premise), and the closure-derived premises themselves
/// appear earlier in the same stream so the provenance DAG closes
/// by triple-equality lookup.
/// </summary>
[TestClass]
internal sealed class RdfsInferenceProvenanceTests
{
    public TestContext TestContext { get; set; } = null!;

    //Vocabulary identifiers — same as RdfsInferenceTests.
    private static IriId RdfType { get; } = IriId.FromUnchecked(TermId.FromEncoded(1));
    private static IriId RdfsSubClassOf { get; } = IriId.FromUnchecked(TermId.FromEncoded(2));
    private static IriId RdfsSubPropertyOf { get; } = IriId.FromUnchecked(TermId.FromEncoded(3));
    private static IriId RdfsDomain { get; } = IriId.FromUnchecked(TermId.FromEncoded(4));
    private static IriId RdfsRange { get; } = IriId.FromUnchecked(TermId.FromEncoded(5));

    //Class identifiers begin at 100.
    private static IriId Animal { get; } = IriId.FromUnchecked(TermId.FromEncoded(100));
    private static IriId Mammal { get; } = IriId.FromUnchecked(TermId.FromEncoded(101));
    private static IriId Dog { get; } = IriId.FromUnchecked(TermId.FromEncoded(102));
    private static IriId Person { get; } = IriId.FromUnchecked(TermId.FromEncoded(103));

    //Property identifiers begin at 200.
    private static IriId HasParent { get; } = IriId.FromUnchecked(TermId.FromEncoded(200));
    private static IriId HasMother { get; } = IriId.FromUnchecked(TermId.FromEncoded(201));
    private static IriId OwnsPet { get; } = IriId.FromUnchecked(TermId.FromEncoded(202));

    //Instance identifiers begin at 1000.
    private static TermId Fido { get; } = TermId.FromEncoded(1000);
    private static TermId Alice { get; } = TermId.FromEncoded(1001);
    private static TermId Bob { get; } = TermId.FromEncoded(1002);

    private static RdfsVocabularyIds Vocab =>
        new(RdfType, RdfsSubClassOf, RdfsSubPropertyOf, RdfsDomain, RdfsRange);

    [TestMethod]
    public async Task SubClassOfTransitiveClosureCarriesRdfs11Provenance()
    {
        //Dog ⊑ Mammal, Mammal ⊑ Animal. The closure-derived (Dog ⊑ Animal)
        //must carry the two-hop BFS-predecessor pair as its antecedents.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Dog, RdfsSubClassOf, Mammal),
            new EncodedTriple(Mammal, RdfsSubClassOf, Animal)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple consequent = new(Dog, RdfsSubClassOf, Animal);
        InferredTriple closure = inferred.Single(it => it.Triple == consequent);
        Assert.AreEqual(InferenceRule.Rdfs11, closure.Rule);
        Assert.HasCount(2, closure.Antecedents);
        Assert.AreEqual(new EncodedTriple(Dog, RdfsSubClassOf, Mammal), closure.Antecedents[0]);
        Assert.AreEqual(new EncodedTriple(Mammal, RdfsSubClassOf, Animal), closure.Antecedents[1]);
    }

    [TestMethod]
    public async Task SubClassOfTypeInferenceCarriesRdfs9Provenance()
    {
        //Fido rdf:type Dog, Dog ⊑ Mammal. The direct (Fido rdf:type Mammal)
        //must carry the subClassOf premise plus the type premise.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Fido, RdfType, Dog),
            new EncodedTriple(Dog, RdfsSubClassOf, Mammal)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple consequent = new(Fido, RdfType, Mammal);
        InferredTriple lifted = inferred.Single(it => it.Triple == consequent);
        Assert.AreEqual(InferenceRule.Rdfs9, lifted.Rule);
        Assert.HasCount(2, lifted.Antecedents);
        Assert.AreEqual(new EncodedTriple(Dog, RdfsSubClassOf, Mammal), lifted.Antecedents[0]);
        Assert.AreEqual(new EncodedTriple(Fido, RdfType, Dog), lifted.Antecedents[1]);
    }

    [TestMethod]
    public async Task SubPropertyOfInferenceCarriesRdfs7Provenance()
    {
        //Alice hasMother Bob, hasMother ⊑ hasParent. The (Alice hasParent Bob)
        //must carry the subPropertyOf premise plus the ABox triple.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, HasMother, Bob),
            new EncodedTriple(HasMother, RdfsSubPropertyOf, HasParent)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple consequent = new(Alice, HasParent, Bob);
        InferredTriple promoted = inferred.Single(it => it.Triple == consequent);
        Assert.AreEqual(InferenceRule.Rdfs7, promoted.Rule);
        Assert.HasCount(2, promoted.Antecedents);
        Assert.AreEqual(new EncodedTriple(HasMother, RdfsSubPropertyOf, HasParent), promoted.Antecedents[0]);
        Assert.AreEqual(new EncodedTriple(Alice, HasMother, Bob), promoted.Antecedents[1]);
    }

    [TestMethod]
    public async Task DomainInferenceCarriesRdfs2Provenance()
    {
        //Alice hasParent Bob, hasParent rdfs:domain Person.
        //The (Alice rdf:type Person) must carry the domain premise plus the ABox triple.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, HasParent, Bob),
            new EncodedTriple(HasParent, RdfsDomain, Person)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple consequent = new(Alice, RdfType, Person);
        InferredTriple typed = inferred.Single(it => it.Triple == consequent);
        Assert.AreEqual(InferenceRule.Rdfs2, typed.Rule);
        Assert.HasCount(2, typed.Antecedents);
        Assert.AreEqual(new EncodedTriple(HasParent, RdfsDomain, Person), typed.Antecedents[0]);
        Assert.AreEqual(new EncodedTriple(Alice, HasParent, Bob), typed.Antecedents[1]);
    }

    [TestMethod]
    public async Task RangeInferenceCarriesRdfs3Provenance()
    {
        //Alice ownsPet Fido, ownsPet rdfs:range Animal.
        //The (Fido rdf:type Animal) must carry the range premise plus the ABox triple.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, OwnsPet, Fido),
            new EncodedTriple(OwnsPet, RdfsRange, Animal)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple consequent = new(Fido, RdfType, Animal);
        InferredTriple typed = inferred.Single(it => it.Triple == consequent);
        Assert.AreEqual(InferenceRule.Rdfs3, typed.Rule);
        Assert.HasCount(2, typed.Antecedents);
        Assert.AreEqual(new EncodedTriple(OwnsPet, RdfsRange, Animal), typed.Antecedents[0]);
        Assert.AreEqual(new EncodedTriple(Alice, OwnsPet, Fido), typed.Antecedents[1]);
    }

    [TestMethod]
    public async Task SubPropertyOfTransitiveClosureCarriesRdfs5Provenance()
    {
        //Three-link subPropertyOf chain: hasMother ⊑ hasParent ⊑ ownsPet.
        //Closure must yield (hasMother ⊑ ownsPet) with the BFS-predecessor
        //pair as antecedents.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(HasMother, RdfsSubPropertyOf, HasParent),
            new EncodedTriple(HasParent, RdfsSubPropertyOf, OwnsPet)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple consequent = new(HasMother, RdfsSubPropertyOf, OwnsPet);
        InferredTriple closure = inferred.Single(it => it.Triple == consequent);
        Assert.AreEqual(InferenceRule.Rdfs5, closure.Rule);
        Assert.HasCount(2, closure.Antecedents);
        //W3C rdfs5 schema: (p1 sPO p2) and (p2 sPO p3). BFS reaches OwnsPet
        //from HasMother via HasParent, so the antecedents are exactly the
        //two direct edges that compose into the closure step.
        Assert.AreEqual(new EncodedTriple(HasMother, RdfsSubPropertyOf, HasParent), closure.Antecedents[0]);
        Assert.AreEqual(new EncodedTriple(HasParent, RdfsSubPropertyOf, OwnsPet), closure.Antecedents[1]);
    }

    [TestMethod]
    public async Task Rdfs7DerivedAboxPremiseAppearsEarlierInStream()
    {
        //Alice hasMother Bob, hasMother ⊑ hasParent, hasParent rdfs:domain Person.
        //The rdfs2 derivation of (Alice rdf:type Person) lifts via the
        //rdfs7-derived (Alice hasParent Bob). The DAG closes: the
        //rdfs2 emission's ABox premise is itself present in the stream
        //as an rdfs7-derived InferredTriple, emitted earlier.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, HasMother, Bob),
            new EncodedTriple(HasMother, RdfsSubPropertyOf, HasParent),
            new EncodedTriple(HasParent, RdfsDomain, Person)
        ]);

        List<InferredTriple> inferred = await DrainAsync(
            RdfsInference.InferWithProvenanceAsync(Vocab, store.AsMatchDelegate(), TestContext.CancellationToken),
            TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple aliceHasParentBob = new(Alice, HasParent, Bob);
        EncodedTriple aliceTypePerson = new(Alice, RdfType, Person);

        int rdfs7Index = inferred.FindIndex(it => it.Triple == aliceHasParentBob && it.Rule == InferenceRule.Rdfs7);
        int rdfs2Index = inferred.FindIndex(it => it.Triple == aliceTypePerson && it.Rule == InferenceRule.Rdfs2);

        Assert.IsGreaterThanOrEqualTo(0, rdfs7Index, "Expected an rdfs7-derived (Alice hasParent Bob) earlier in the stream.");
        Assert.IsGreaterThanOrEqualTo(0, rdfs2Index, "Expected an rdfs2-derived (Alice rdf:type Person).");
        Assert.IsLessThan(rdfs2Index, rdfs7Index, "rdfs7 emission must precede the rdfs2 emission that depends on it.");

        InferredTriple rdfs2Inferred = inferred[rdfs2Index];
        Assert.AreEqual(aliceHasParentBob, rdfs2Inferred.Antecedents[1]);
        Assert.AreEqual(aliceHasParentBob, inferred[rdfs7Index].Triple);
    }

    [TestMethod]
    public async Task InferWithProvenanceAsyncRejectsNullMatch()
    {
        //Iterator must throw at first MoveNextAsync — mirror the B3
        //null-rejection pattern.
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach(InferredTriple _ in RdfsInference.InferWithProvenanceAsync(
                Vocab, match: null!, TestContext.CancellationToken).ConfigureAwait(false))
            {
                //Unreachable: the null-guard fires before the first MoveNextAsync.
            }
        }).ConfigureAwait(false);
    }

    private static async Task<List<InferredTriple>> DrainAsync(
        IAsyncEnumerable<InferredTriple> source,
        CancellationToken cancellationToken)
    {
        List<InferredTriple> result = [];
        await foreach(InferredTriple item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            result.Add(item);
        }

        return result;
    }
}
