using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Skos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Skos;

[TestClass]
internal sealed class SkosConceptSchemeTests
{
    public TestContext TestContext { get; set; } = null!;

    private static List<T> Collect<T>(IEnumerable<T> source)
    {
        List<T> list = source is ICollection<T> col ? new List<T>(col.Count) : [];
        foreach(T item in source)
        {
            list.Add(item);
        }

        return list;
    }

    //Test scheme IRI.
    private const string SchemeIri = "http://example.org/scheme";
    private const string Animal = "http://example.org/Animal";
    private const string Mammal = "http://example.org/Mammal";
    private const string Bird = "http://example.org/Bird";
    private const string Dog = "http://example.org/Dog";
    private const string Cat = "http://example.org/Cat";

    [TestMethod]
    public async Task LoadReturnsNullForUnknownScheme()
    {
        using Utf8StringPool pool = new();
        TermDictionary dictionary = new();
        InMemoryGraphStore store = InMemoryGraphStore.Build([]);

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            "http://example.org/nonexistent",
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNull(scheme);
    }

    [TestMethod]
    public async Task LoadReturnsSchemeWithConcepts()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        Assert.AreEqual(SchemeIri, scheme.SchemeIri);
        Assert.AreEqual(5, scheme.ConceptCount);
    }

    [TestMethod]
    public async Task LoadIdentifiesTopConcepts()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> topConcepts = Collect(scheme.TopConcepts);

        //Animal and Bird are top concepts (no broader in the scheme).
        //Dog and Cat are narrower than Mammal; Mammal is narrower than Animal.
        Assert.Contains(Animal, topConcepts, "Animal should be a top concept.");
        Assert.DoesNotContain(Dog, topConcepts, "Dog should not be a top concept.");
    }

    [TestMethod]
    public async Task GetBroaderReturnsDirectBroaderConcepts()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> broaderOfDog = Collect(scheme.GetBroader(Dog));

        Assert.HasCount(1, broaderOfDog);
        Assert.AreEqual(Mammal, broaderOfDog[0]);
    }

    [TestMethod]
    public async Task GetNarrowerReturnsDirectNarrowerConcepts()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> narrowerOfMammal = Collect(scheme.GetNarrower(Mammal));

        Assert.HasCount(2, narrowerOfMammal);
        Assert.Contains(Dog, narrowerOfMammal);
        Assert.Contains(Cat, narrowerOfMammal);
    }

    [TestMethod]
    public async Task GetAllBroaderReturnsTransitiveAncestors()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> ancestors = Collect(scheme.GetAllBroader(Dog));

        //Dog → Mammal → Animal.
        Assert.HasCount(2, ancestors);
        Assert.Contains(Mammal, ancestors);
        Assert.Contains(Animal, ancestors);
    }

    [TestMethod]
    public async Task GetAllNarrowerReturnsTransitiveDescendants()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> descendants = Collect(scheme.GetAllNarrower(Animal));

        //Animal → Mammal → Dog, Cat.
        Assert.HasCount(3, descendants);
        Assert.Contains(Mammal, descendants);
        Assert.Contains(Dog, descendants);
        Assert.Contains(Cat, descendants);
    }

    [TestMethod]
    public async Task GetBroaderReturnsEmptyForTopConcept()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> broaderOfAnimal = Collect(scheme.GetBroader(Animal));

        Assert.IsEmpty(broaderOfAnimal);
    }

    [TestMethod]
    public async Task GetAllBroaderReturnsEmptyForUnknownConcept()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<string> ancestors = Collect(scheme.GetAllBroader("http://example.org/Unknown"));

        Assert.IsEmpty(ancestors);
    }

    [TestMethod]
    public async Task GetPrefLabelsReturnsLabelsForConcept()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<Literal> labels = Collect(scheme.GetPrefLabels(Dog));

        Assert.HasCount(2, labels);
    }

    [TestMethod]
    public async Task GetPrefLabelsFiltersByLanguage()
    {
        (InMemoryGraphStore store, TermDictionary dictionary, Utf8StringPool pool) = BuildTestGraph();

        SkosConceptScheme? scheme = await SkosConceptScheme.LoadAsync(
            SchemeIri,
            store.AsMatchDelegate(),
            dictionary,
            pool,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(scheme);
        List<Literal> englishLabels = Collect(scheme.GetPrefLabels(Dog, "en"));

        Assert.HasCount(1, englishLabels);
        Assert.AreEqual("Dog", englishLabels[0].Value.ToString());
    }

    [TestMethod]
    public async Task NullArgumentsThrow()
    {
        using Utf8StringPool pool = new();
        TermDictionary dictionary = new();
        InMemoryGraphStore store = InMemoryGraphStore.Build([]);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await SkosConceptScheme.LoadAsync(
                null!, store.AsMatchDelegate(), dictionary, pool, TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () =>
            await SkosConceptScheme.LoadAsync(
                "http://example.org/s", null!, dictionary, pool, TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    //Builds a small test graph.
    private static (InMemoryGraphStore Store, TermDictionary Dictionary, Utf8StringPool Pool) BuildTestGraph()
    {
        Utf8StringPool pool = new();
        TermDictionary dictionary = new();

        Utf8String InternIri(string iri) => pool.Intern(iri);

        NamedNode rdfType = new(InternIri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"));
        NamedNode langString = new(InternIri("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"));

        NamedNode scheme = new(InternIri(SchemeIri));
        NamedNode animal = new(InternIri(Animal));
        NamedNode mammal = new(InternIri(Mammal));
        NamedNode bird = new(InternIri(Bird));
        NamedNode dog = new(InternIri(Dog));
        NamedNode cat = new(InternIri(Cat));

        NamedNode skosConcept = new(InternIri(SkosVocabulary.Core.Concept.ToString()));
        NamedNode skosConceptScheme = new(InternIri(SkosVocabulary.Core.ConceptScheme.ToString()));
        NamedNode skosInScheme = new(InternIri(SkosVocabulary.Core.InScheme.ToString()));
        NamedNode skosBroader = new(InternIri(SkosVocabulary.Core.Broader.ToString()));
        NamedNode skosNarrower = new(InternIri(SkosVocabulary.Core.Narrower.ToString()));
        NamedNode skosPrefLabel = new(InternIri(SkosVocabulary.Core.PrefLabel.ToString()));

        List<Quad> quads =
        [
            new(scheme, rdfType, skosConceptScheme),
            new(animal, rdfType, skosConcept),
            new(mammal, rdfType, skosConcept),
            new(bird, rdfType, skosConcept),
            new(dog, rdfType, skosConcept),
            new(cat, rdfType, skosConcept),
            new(animal, skosInScheme, scheme),
            new(mammal, skosInScheme, scheme),
            new(bird, skosInScheme, scheme),
            new(dog, skosInScheme, scheme),
            new(cat, skosInScheme, scheme),
            new(mammal, skosBroader, animal),
            new(dog, skosBroader, mammal),
            new(cat, skosBroader, mammal),
            new(animal, skosNarrower, mammal),
            new(mammal, skosNarrower, dog),
            new(mammal, skosNarrower, cat),
            new(dog, skosPrefLabel,
                new Literal(InternIri("Dog"), new NamedNode(InternIri(langString.Iri.ToString())), InternIri("en"))),
            new(dog, skosPrefLabel,
                new Literal(InternIri("Hund"), new NamedNode(InternIri(langString.Iri.ToString())), InternIri("de")))
        ];

        List<EncodedTriple> encoded = new(quads.Count);
        foreach(Quad q in quads)
        {
            encoded.Add(q.Encode(dictionary).AsTriple());
        }

        InMemoryGraphStore store = InMemoryGraphStore.Build(encoded);

        return (store, dictionary, pool);
    }
}