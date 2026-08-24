using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Tests for <see cref="RdfAdjacencyAdapter"/>. Construct a small
/// encoded graph, wrap its <c>MatchTriplesAsync</c> with the adapter,
/// and exercise the three adjacency methods both directly and
/// composed with <see cref="TraversalPrimitives"/>.
/// </summary>
[TestClass]
internal sealed class RdfAdjacencyAdapterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ForwardAsyncYieldsObjectsFilteredByPredicate()
    {
        //Graph: Alice knows Bob, Alice knows Carol, Alice worksAt ACME.
        //Forward from Alice along knows should yield Bob and Carol, not ACME.
        TermDictionary dictionary = new();
        NamedNode alice = new(Utf8Strings.From("http://example.org/alice"));
        NamedNode bob = new(Utf8Strings.From("http://example.org/bob"));
        NamedNode carol = new(Utf8Strings.From("http://example.org/carol"));
        NamedNode acme = new(Utf8Strings.From("http://example.org/acme"));
        NamedNode knows = new(Utf8Strings.From("http://example.org/knows"));
        NamedNode worksAt = new(Utf8Strings.From("http://example.org/worksAt"));

        List<EncodedTriple> triples =
        [
            new Quad(alice, knows, bob).Encode(dictionary).AsTriple(),
            new Quad(alice, knows, carol).Encode(dictionary).AsTriple(),
            new Quad(alice, worksAt, acme).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);

        RdfAdjacencyAdapter adapter = new(store.AsMatchDelegate());
        TermId aliceId = dictionary.GetOrAdd(alice).Value;
        IriId knowsId = dictionary.GetOrAdd(knows);

        HashSet<TermId> yielded = [];
        await foreach(TermId n in adapter.ForwardAsync(aliceId, knowsId, TestContext.CancellationToken).ConfigureAwait(false))
        {
            yielded.Add(n);
        }

        Assert.HasCount(2, yielded);
        Assert.Contains(dictionary.GetOrAdd(bob).Value, yielded);
        Assert.Contains(dictionary.GetOrAdd(carol).Value, yielded);
        Assert.DoesNotContain(dictionary.GetOrAdd(acme).Value, yielded);
    }

    [TestMethod]
    public async Task BackwardAsyncYieldsSubjectsFilteredByPredicate()
    {
        //Bob and Carol both know Alice. Backward from Alice along knows
        //should yield Bob and Carol.
        TermDictionary dictionary = new();
        NamedNode alice = new(Utf8Strings.From("http://example.org/alice"));
        NamedNode bob = new(Utf8Strings.From("http://example.org/bob"));
        NamedNode carol = new(Utf8Strings.From("http://example.org/carol"));
        NamedNode knows = new(Utf8Strings.From("http://example.org/knows"));

        List<EncodedTriple> triples =
        [
            new Quad(bob, knows, alice).Encode(dictionary).AsTriple(),
            new Quad(carol, knows, alice).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);

        RdfAdjacencyAdapter adapter = new(store.AsMatchDelegate());
        TermId aliceId = dictionary.GetOrAdd(alice).Value;
        IriId knowsId = dictionary.GetOrAdd(knows);

        HashSet<TermId> yielded = [];
        await foreach(TermId n in adapter.BackwardAsync(aliceId, knowsId, TestContext.CancellationToken).ConfigureAwait(false))
        {
            yielded.Add(n);
        }

        Assert.HasCount(2, yielded);
        Assert.Contains(dictionary.GetOrAdd(bob).Value, yielded);
        Assert.Contains(dictionary.GetOrAdd(carol).Value, yielded);
    }

    [TestMethod]
    public async Task AnyForwardAsyncYieldsObjectsRegardlessOfPredicate()
    {
        //Mixed predicates from the same subject — all objects returned.
        TermDictionary dictionary = new();
        NamedNode alice = new(Utf8Strings.From("http://example.org/alice"));
        NamedNode bob = new(Utf8Strings.From("http://example.org/bob"));
        NamedNode acme = new(Utf8Strings.From("http://example.org/acme"));
        NamedNode knows = new(Utf8Strings.From("http://example.org/knows"));
        NamedNode worksAt = new(Utf8Strings.From("http://example.org/worksAt"));

        List<EncodedTriple> triples =
        [
            new Quad(alice, knows, bob).Encode(dictionary).AsTriple(),
            new Quad(alice, worksAt, acme).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);

        RdfAdjacencyAdapter adapter = new(store.AsMatchDelegate());
        TermId aliceId = dictionary.GetOrAdd(alice).Value;

        HashSet<TermId> yielded = [];
        await foreach(TermId n in adapter.AnyForwardAsync(aliceId, TestContext.CancellationToken).ConfigureAwait(false))
        {
            yielded.Add(n);
        }

        Assert.HasCount(2, yielded);
    }

    [TestMethod]
    public async Task ComposesWithTransitiveClosureOnRdfsSubClassOfChain()
    {
        //Dog subClassOf Mammal subClassOf Animal.
        //Closure from Dog along subClassOf should yield Mammal, Animal.
        TermDictionary dictionary = new();
        NamedNode dog = new(Utf8Strings.From("http://example.org/Dog"));
        NamedNode mammal = new(Utf8Strings.From("http://example.org/Mammal"));
        NamedNode animal = new(Utf8Strings.From("http://example.org/Animal"));
        NamedNode subClassOf = new(Utf8Strings.From("http://www.w3.org/2000/01/rdf-schema#subClassOf"));

        List<EncodedTriple> triples =
        [
            new Quad(dog, subClassOf, mammal).Encode(dictionary).AsTriple(),
            new Quad(mammal, subClassOf, animal).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);

        RdfAdjacencyAdapter adapter = new(store.AsMatchDelegate());
        TermId dogId = dictionary.GetOrAdd(dog).Value;
        IriId subClassOfId = dictionary.GetOrAdd(subClassOf);

        HashSet<TermId> reached = [];
        await foreach(TermId n in TraversalPrimitives.TransitiveClosureAsync(
            dogId, subClassOfId, adapter.ForwardAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(n);
        }

        Assert.HasCount(2, reached);
        Assert.Contains(dictionary.GetOrAdd(mammal).Value, reached);
        Assert.Contains(dictionary.GetOrAdd(animal).Value, reached);
        Assert.DoesNotContain(dogId, reached);
    }
}
