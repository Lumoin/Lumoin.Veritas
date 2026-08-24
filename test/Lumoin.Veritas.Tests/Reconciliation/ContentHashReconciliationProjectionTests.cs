using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Tests.Reconciliation;

/// <summary>
/// The content-hash projection is cross-node stable: the same RDF triple hashes to the same key even when two
/// dictionaries assigned different identifiers to its terms — the property the structural projection lacks (its
/// packed identifiers diverge across dictionaries). Distinct content hashes distinctly, and literal objects are
/// stable too.
/// </summary>
[TestClass]
internal sealed class ContentHashReconciliationProjectionTests
{
    /// <summary>A named node for an IRI string.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>The same RDF triple, encoded under two dictionaries that numbered its terms differently, hashes to the same content key — while the structural keys differ.</summary>
    [TestMethod]
    public void SameContentAcrossDivergentDictionariesProducesTheSameKey()
    {
        using VeritasMemoryPool<byte> pool = new();
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        NamedNode @object = Iri("http://example.org/o");

        TermDictionary dictionaryA = new();
        EncodedTriple tripleA = EncodedTriple.FromEncoded(dictionaryA.GetOrAdd((RdfTerm)subject).Encoded, dictionaryA.GetOrAdd((RdfTerm)predicate).Encoded, dictionaryA.GetOrAdd((RdfTerm)@object).Encoded);

        //Dictionary B sees the same terms in a different order, so it assigns them different identifiers.
        TermDictionary dictionaryB = new();
        uint objectB = dictionaryB.GetOrAdd((RdfTerm)@object).Encoded;
        uint predicateB = dictionaryB.GetOrAdd((RdfTerm)predicate).Encoded;
        uint subjectB = dictionaryB.GetOrAdd((RdfTerm)subject).Encoded;
        EncodedTriple tripleB = EncodedTriple.FromEncoded(subjectB, predicateB, objectB);

        ContentHashReconciliationProjection projectionA = new(dictionaryA, VeritasHashing.Default, pool);
        ContentHashReconciliationProjection projectionB = new(dictionaryB, VeritasHashing.Default, pool);

        Assert.AreEqual(projectionA.Project(tripleA), projectionB.Project(tripleB), "The same RDF content hashes to the same key across divergent dictionaries.");
        Assert.AreNotEqual(StructuralReconciliationProjection.Project(tripleA), StructuralReconciliationProjection.Project(tripleB), "The structural key differs across dictionaries — the cross-node problem the content hash solves.");
    }

    /// <summary>Distinct triples hash to distinct keys.</summary>
    [TestMethod]
    public void DifferentContentProducesDifferentKeys()
    {
        using VeritasMemoryPool<byte> pool = new();
        TermDictionary dictionary = new();
        uint subject = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/s")).Encoded;
        uint predicate = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/p")).Encoded;
        uint object1 = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/o1")).Encoded;
        uint object2 = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/o2")).Encoded;
        ContentHashReconciliationProjection projection = new(dictionary, VeritasHashing.Default, pool);

        Assert.AreNotEqual(projection.Project(EncodedTriple.FromEncoded(subject, predicate, object1)), projection.Project(EncodedTriple.FromEncoded(subject, predicate, object2)), "Different objects produce different keys.");
    }

    /// <summary>A literal object is cross-node stable too: a triple with a literal hashes the same under divergent dictionaries.</summary>
    [TestMethod]
    public void LiteralObjectIsCrossNodeStable()
    {
        using VeritasMemoryPool<byte> pool = new();
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        Literal @object = new(Utf8Strings.From("hello"), Iri("http://www.w3.org/2001/XMLSchema#string"));

        TermDictionary dictionaryA = new();
        EncodedTriple tripleA = EncodedTriple.FromEncoded(dictionaryA.GetOrAdd((RdfTerm)subject).Encoded, dictionaryA.GetOrAdd((RdfTerm)predicate).Encoded, dictionaryA.GetOrAdd((RdfTerm)@object).Encoded);

        TermDictionary dictionaryB = new();
        uint objectB = dictionaryB.GetOrAdd((RdfTerm)@object).Encoded;
        uint predicateB = dictionaryB.GetOrAdd((RdfTerm)predicate).Encoded;
        uint subjectB = dictionaryB.GetOrAdd((RdfTerm)subject).Encoded;
        EncodedTriple tripleB = EncodedTriple.FromEncoded(subjectB, predicateB, objectB);

        ContentHashReconciliationProjection projectionA = new(dictionaryA, VeritasHashing.Default, pool);
        ContentHashReconciliationProjection projectionB = new(dictionaryB, VeritasHashing.Default, pool);

        Assert.AreEqual(projectionA.Project(tripleA), projectionB.Project(tripleB), "A literal object hashes stably across dictionaries.");
    }

    /// <summary>A blank-node triple is rejected rather than hashed to a silently epoch-local key (the rejection is explicit, like a triple term).</summary>
    [TestMethod]
    public void BlankNodeTripleIsRejected()
    {
        using VeritasMemoryPool<byte> pool = new();
        TermDictionary dictionary = new();
        uint subject = dictionary.GetOrAdd((RdfTerm)new BlankNode(Utf8Strings.From("b0"))).Encoded;
        uint predicate = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/p")).Encoded;
        uint @object = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/o")).Encoded;
        ContentHashReconciliationProjection projection = new(dictionary, VeritasHashing.Default, pool);

        Assert.ThrowsExactly<NotSupportedException>(() => projection.Project(EncodedTriple.FromEncoded(subject, predicate, @object)));
    }

    /// <summary>An absent language tag and a present-but-empty one hash distinctly — the encoding is injective for the kinds it accepts.</summary>
    [TestMethod]
    public void AbsentAndEmptyLanguageHashDistinctly()
    {
        using VeritasMemoryPool<byte> pool = new();
        NamedNode datatype = Iri("http://www.w3.org/2001/XMLSchema#string");
        Literal plain = new(Utf8Strings.From("x"), datatype);
        Literal emptyLanguage = new(Utf8Strings.From("x"), datatype, Utf8Strings.From(""));

        TermDictionary dictionary = new();
        uint subject = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/s")).Encoded;
        uint predicate = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/p")).Encoded;
        uint plainObject = dictionary.GetOrAdd((RdfTerm)plain).Encoded;
        uint emptyLanguageObject = dictionary.GetOrAdd((RdfTerm)emptyLanguage).Encoded;
        ContentHashReconciliationProjection projection = new(dictionary, VeritasHashing.Default, pool);

        Assert.AreNotEqual(projection.Project(EncodedTriple.FromEncoded(subject, predicate, plainObject)), projection.Project(EncodedTriple.FromEncoded(subject, predicate, emptyLanguageObject)), "An absent language and a present-but-empty language hash distinctly.");
    }
}
