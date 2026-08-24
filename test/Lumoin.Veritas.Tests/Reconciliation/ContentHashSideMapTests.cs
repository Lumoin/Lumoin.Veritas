using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Tests.Reconciliation;

/// <summary>
/// The content-hash side-map resolves the keys a node holds back to their triples and reports a key the node lacks
/// as absent — the two operations the content-hash reconcile needs from a non-invertible item domain: a serving
/// node resolves a requested key, a reconciling node tells local-only differences (held) from peer-only ones
/// (lacked, to be fetched).
/// </summary>
[TestClass]
internal sealed class ContentHashSideMapTests
{
    /// <summary>A named node for an IRI string.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>A held key resolves to its triple and is contained; a key the node lacks neither resolves nor is contained.</summary>
    [TestMethod]
    public void ResolvesHeldKeysAndDetectsMissingOnes()
    {
        using VeritasMemoryPool<byte> pool = new();
        TermDictionary dictionary = new();
        uint subject = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/s")).Encoded;
        uint predicate = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/p")).Encoded;
        uint object1 = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/o1")).Encoded;
        uint object2 = dictionary.GetOrAdd((RdfTerm)Iri("http://example.org/o2")).Encoded;

        EncodedTriple held = EncodedTriple.FromEncoded(subject, predicate, object1);
        EncodedTriple missing = EncodedTriple.FromEncoded(subject, predicate, object2);
        ColumnarTripleIndex index = ColumnarTripleIndex.Build([held]);

        ContentHashReconciliationProjection projection = new(dictionary, VeritasHashing.Default, pool);
        ContentHashSideMap sideMap = ContentHashSideMap.Build(index, projection.Projection);

        Assert.AreEqual(1, sideMap.Count, "The side-map holds the one indexed triple.");

        ContentKey128 heldKey = projection.Project(held);
        Assert.IsTrue(sideMap.Contains(heldKey), "The held key is contained.");
        Assert.IsTrue(sideMap.TryResolve(heldKey, out EncodedTriple resolved), "The held key resolves.");
        Assert.AreEqual(held, resolved, "The held key resolves to its triple.");

        ContentKey128 missingKey = projection.Project(missing);
        Assert.IsFalse(sideMap.Contains(missingKey), "A key the node lacks is detected as missing (a peer-only difference).");
        Assert.IsFalse(sideMap.TryResolve(missingKey, out _), "A missing key does not resolve.");
    }
}
