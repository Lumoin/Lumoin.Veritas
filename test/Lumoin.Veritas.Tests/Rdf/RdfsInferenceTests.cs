using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Tests.Rdf;

[TestClass]
internal sealed class RdfsInferenceTests
{
    public TestContext TestContext { get; set; } = null!;

    //Fixed identifiers for the vocabulary terms. Real code resolves these through a TermDictionary.
    //RDFS vocabulary terms (rdf:type, rdfs:subClassOf, etc.) are IRIs by definition, so the
    //narrowed IriId wrapper is used. Class IRIs and property IRIs are likewise IriId. Instance
    //identifiers could be IRIs or blank nodes in general, so they remain the wider TermId.
    private static readonly IriId RdfType = IriId.FromUnchecked(TermId.FromEncoded(1));
    private static readonly IriId RdfsSubClassOf = IriId.FromUnchecked(TermId.FromEncoded(2));
    private static readonly IriId RdfsSubPropertyOf = IriId.FromUnchecked(TermId.FromEncoded(3));
    private static readonly IriId RdfsDomain = IriId.FromUnchecked(TermId.FromEncoded(4));
    private static readonly IriId RdfsRange = IriId.FromUnchecked(TermId.FromEncoded(5));

    //Class identifiers begin at 100.
    private static readonly IriId Animal = IriId.FromUnchecked(TermId.FromEncoded(100));
    private static readonly IriId Mammal = IriId.FromUnchecked(TermId.FromEncoded(101));
    private static readonly IriId Dog = IriId.FromUnchecked(TermId.FromEncoded(102));
    private static readonly IriId Person = IriId.FromUnchecked(TermId.FromEncoded(103));

    //Property identifiers begin at 200.
    private static readonly IriId HasParent = IriId.FromUnchecked(TermId.FromEncoded(200));
    private static readonly IriId HasMother = IriId.FromUnchecked(TermId.FromEncoded(201));
    private static readonly IriId OwnsPet = IriId.FromUnchecked(TermId.FromEncoded(202));

    //Instance identifiers begin at 1000. Kept as TermId since instances can be
    //either IRIs or blank nodes in general RDF data.
    private static readonly TermId Fido = TermId.FromEncoded(1000);
    private static readonly TermId Alice = TermId.FromEncoded(1001);
    private static readonly TermId Bob = TermId.FromEncoded(1002);

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

        CollectionAssert.Contains(inferred, new EncodedTriple(Dog, RdfsSubClassOf, Animal));
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

        CollectionAssert.Contains(inferred, new EncodedTriple(Fido, RdfType, Mammal));
        CollectionAssert.Contains(inferred, new EncodedTriple(Fido, RdfType, Animal));
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

        CollectionAssert.Contains(inferred, new EncodedTriple(Alice, HasParent, Bob));
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

        CollectionAssert.Contains(inferred, new EncodedTriple(Alice, RdfType, Person));
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

        CollectionAssert.Contains(inferred, new EncodedTriple(Fido, RdfType, Animal));
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

        CollectionAssert.Contains(inferred, new EncodedTriple(Alice, RdfType, Person));
        CollectionAssert.Contains(inferred, new EncodedTriple(Alice, RdfType, Animal));
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
