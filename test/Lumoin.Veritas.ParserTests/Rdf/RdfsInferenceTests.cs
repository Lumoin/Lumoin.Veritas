using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Rdf;

[TestClass]
internal sealed class RdfsInferenceTests
{
    public TestContext TestContext { get; set; } = null!;

    //Fixed identifiers for the vocabulary terms. Real code resolves these through a TermDictionary.
    //RDFS vocabulary terms (rdf:type, rdfs:subClassOf, etc.) are IRIs by definition, so the
    //narrowed IriId wrapper is used. Class IRIs and property IRIs are likewise IriId. Instance
    //identifiers could be IRIs or blank nodes in general, so they remain the wider TermId.
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

    //Instance identifiers begin at 1000. Kept as TermId since instances can be
    //either IRIs or blank nodes in general RDF data.
    private static TermId Fido { get; } = TermId.FromEncoded(1000);
    private static TermId Alice { get; } = TermId.FromEncoded(1001);
    private static TermId Bob { get; } = TermId.FromEncoded(1002);

    private static RdfsVocabularyIds Vocab =>
        new(RdfType, RdfsSubClassOf, RdfsSubPropertyOf, RdfsDomain, RdfsRange);

    [TestMethod]
    public async Task SubClassOfTransitiveClosureIsEmitted()
    {
        //Dog rdfs:subClassOf Mammal. Mammal rdfs:subClassOf Animal. Expect Dog rdfs:subClassOf Animal emitted.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Dog, RdfsSubClassOf, Mammal),
            new EncodedTriple(Mammal, RdfsSubClassOf, Animal)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.Contains(new EncodedTriple(Dog, RdfsSubClassOf, Animal), inferred);
    }

    [TestMethod]
    public async Task SubClassOfInferencePropagatesRdfType()
    {
        //Fido rdf:type Dog. Dog rdfs:subClassOf Mammal. Mammal rdfs:subClassOf Animal.
        //Expect Fido rdf:type Mammal, Fido rdf:type Animal.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Fido, RdfType, Dog),
            new EncodedTriple(Dog, RdfsSubClassOf, Mammal),
            new EncodedTriple(Mammal, RdfsSubClassOf, Animal)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.Contains(new EncodedTriple(Fido, RdfType, Mammal), inferred);
        Assert.Contains(new EncodedTriple(Fido, RdfType, Animal), inferred);
    }

    [TestMethod]
    public async Task SubPropertyOfInferenceProducesSuperPropertyTriples()
    {
        //Alice hasMother Bob. hasMother rdfs:subPropertyOf hasParent.
        //Expect Alice hasParent Bob.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, HasMother, Bob),
            new EncodedTriple(HasMother, RdfsSubPropertyOf, HasParent)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.Contains(new EncodedTriple(Alice, HasParent, Bob), inferred);
    }

    [TestMethod]
    public async Task DomainInferenceProducesSubjectType()
    {
        //Alice ownsPet Fido. ownsPet rdfs:domain Person. Expect Alice rdf:type Person.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, OwnsPet, Fido),
            new EncodedTriple(OwnsPet, RdfsDomain, Person)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.Contains(new EncodedTriple(Alice, RdfType, Person), inferred);
    }

    [TestMethod]
    public async Task RangeInferenceProducesObjectType()
    {
        //Alice ownsPet Fido. ownsPet rdfs:range Animal. Expect Fido rdf:type Animal.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, OwnsPet, Fido),
            new EncodedTriple(OwnsPet, RdfsRange, Animal)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.Contains(new EncodedTriple(Fido, RdfType, Animal), inferred);
    }

    [TestMethod]
    public async Task DomainInferenceWithClassHierarchyProducesSuperTypes()
    {
        //Alice ownsPet Fido. ownsPet rdfs:domain Person. Person rdfs:subClassOf Animal.
        //Expect Alice rdf:type Person AND Alice rdf:type Animal.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Alice, OwnsPet, Fido),
            new EncodedTriple(OwnsPet, RdfsDomain, Person),
            new EncodedTriple(Person, RdfsSubClassOf, Animal)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.Contains(new EncodedTriple(Alice, RdfType, Person), inferred);
        Assert.Contains(new EncodedTriple(Alice, RdfType, Animal), inferred);
    }

    [TestMethod]
    public async Task InferenceIsIdempotentAcrossRules()
    {
        //Check no duplicate triples are emitted even when multiple rules agree.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Fido, RdfType, Dog),
            new EncodedTriple(Dog, RdfsSubClassOf, Mammal)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        int distinct = new HashSet<EncodedTriple>(inferred).Count;
        Assert.HasCount(distinct, inferred, "Inferred triples should be emitted at most once.");
    }

    [TestMethod]
    public async Task DirectlyAssertedSubClassOfIsNotEmittedAgain()
    {
        //Direct assertions must not be echoed back; only the derived transitive closure.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            new EncodedTriple(Dog, RdfsSubClassOf, Mammal),
            new EncodedTriple(Mammal, RdfsSubClassOf, Animal)
        ]);

        List<EncodedTriple> inferred = [];
        await foreach(EncodedTriple triple in RdfsInference.InferAsync(
            Vocab, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            inferred.Add(triple);
        }

        Assert.DoesNotContain(new EncodedTriple(Dog, RdfsSubClassOf, Mammal), inferred);
        Assert.DoesNotContain(new EncodedTriple(Mammal, RdfsSubClassOf, Animal), inferred);
        Assert.Contains(new EncodedTriple(Dog, RdfsSubClassOf, Animal), inferred);
    }
}
