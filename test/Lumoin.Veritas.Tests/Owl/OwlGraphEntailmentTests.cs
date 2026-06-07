using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Tests for the store-backed embedding: a conclusion graph with
/// existential blanks is a basic graph pattern, and
/// <see cref="OwlGraphEntailment.EmbedsAsync"/> through the join engine
/// agrees with the list-scan <see cref="OwlGraphEntailment.Embeds"/> —
/// including the comprehension mode, the absent-constant fast path, and
/// the per-pattern self-join fallback.
/// </summary>
[TestClass]
internal sealed class OwlGraphEntailmentTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";
    private const string OwlNs = "http://www.w3.org/2002/07/owl#";
    private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>A blank joining two conclusion triples binds consistently on both paths.</summary>
    [TestMethod]
    public async Task BlankJoinsAgreeAcrossBothPaths()
    {
        //a p b; b q c — the conclusion asks for some x with a p x and x q c.
        (HypertrieGraphStore store, TermDictionary dictionary, List<Quad> data) = await BuildAsync(
            [("a", "p", "b"), ("b", "q", "c")]).ConfigureAwait(false);

        List<Quad> positive =
        [
            new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null),
            new Quad(Blank("x"), Predicate("q"), Named("c"), Graph: null),
        ];
        List<Quad> negative =
        [
            new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null),
            new Quad(Blank("x"), Predicate("q"), Named("a"), Graph: null),
        ];

        Assert.IsTrue(OwlGraphEntailment.Embeds(positive, data));
        Assert.IsTrue(await OwlGraphEntailment.EmbedsAsync(positive, store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(OwlGraphEntailment.Embeds(negative, data));
        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(negative, store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>A constant the dictionary never minted answers false without evaluation.</summary>
    [TestMethod]
    public async Task AbsentConstantCannotMatch()
    {
        (HypertrieGraphStore store, TermDictionary dictionary, _) = await BuildAsync([("a", "p", "b")]).ConfigureAwait(false);

        List<Quad> conclusion = [new Quad(Named("never"), Predicate("p"), Named("b"), Graph: null)];

        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(conclusion, store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>The comprehension mode strips pure-existence scaffolds on the store path exactly as on the list path.</summary>
    [TestMethod]
    public async Task ComprehensionStripsScaffoldsOnTheStorePath()
    {
        (HypertrieGraphStore store, TermDictionary dictionary, _) = await BuildAsync([("p", RdfNs + "type", OwlNs + "ObjectProperty")]).ConfigureAwait(false);

        //The minCardinality-1 restriction scaffold over p: granted under
        //the informative conditions, unmatched under the normative mode.
        List<Quad> conclusion =
        [
            new Quad(Blank("n"), Predicate(RdfNs + "type"), new NamedNode(Utf8String.FromString(OwlNs + "Restriction")), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "onProperty"), Named("p"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "minCardinality"), new Literal(Utf8String.FromString("1"), new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#int"))), Graph: null),
        ];

        Assert.IsTrue(await OwlGraphEntailment.EmbedsAsync(conclusion, store, dictionary, TimeProvider.System, OwlComprehension.InformativeConditions, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(conclusion, store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>A conclusion triple with one blank in two positions takes the self-join fallback and still answers correctly.</summary>
    [TestMethod]
    public async Task SelfJoinFallsBackToTheListScan()
    {
        (HypertrieGraphStore store, TermDictionary dictionary, _) = await BuildAsync([("r", "loves", "r"), ("a", "loves", "b")]).ConfigureAwait(false);

        List<Quad> reflexive = [new Quad(Blank("x"), Predicate("loves"), Blank("x"), Graph: null)];

        Assert.IsTrue(await OwlGraphEntailment.EmbedsAsync(reflexive, store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));

        (HypertrieGraphStore loveless, TermDictionary dictionary2, _) = await BuildAsync([("a", "loves", "b")]).ConfigureAwait(false);

        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(reflexive, loveless, dictionary2, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>An empty conclusion embeds trivially; a fully ground one resolves by containment.</summary>
    [TestMethod]
    public async Task GroundAndEmptyConclusions()
    {
        (HypertrieGraphStore store, TermDictionary dictionary, _) = await BuildAsync([("a", "p", "b")]).ConfigureAwait(false);

        Assert.IsTrue(await OwlGraphEntailment.EmbedsAsync([], store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await OwlGraphEntailment.EmbedsAsync(
            [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)],
            store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(
            [new Quad(Named("b"), Predicate("p"), Named("a"), Graph: null)],
            store, dictionary, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>Builds a store, its dictionary, and the decoded quad list from (subject, predicate, object) names.</summary>
    /// <param name="triples">The triples; names without a scheme prefix mint in the example namespace.</param>
    /// <returns>The store, dictionary, and quads.</returns>
    private async Task<(HypertrieGraphStore Store, TermDictionary Dictionary, List<Quad> Quads)> BuildAsync(
        IReadOnlyList<(string Subject, string Predicate, string Object)> triples)
    {
        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = [];
        List<Quad> quads = [];
        foreach((string subject, string predicate, string @object) in triples)
        {
            Quad quad = new(Resolve(subject), (NamedNode)Resolve(predicate), Resolve(@object), Graph: null);
            quads.Add(quad);
            encoded.Add(EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(quad.Subject).Encoded,
                dictionary.GetOrAdd(quad.Predicate).Encoded,
                dictionary.GetOrAdd(quad.Object).Encoded));
        }

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        return (store, dictionary, quads);
    }

    /// <summary>Resolves a name: absolute IRIs pass through, bare names mint in the example namespace.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Resolve(string name)
    {
        return new NamedNode(Utf8String.FromString(name.Contains("://", StringComparison.Ordinal) ? name : Example + name));
    }

    /// <summary>A predicate node; bare names mint in the example namespace.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Predicate(string name)
    {
        return Resolve(name);
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8String.FromString(Example + local));
    }

    /// <summary>A blank node with the label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8String.FromString(label));
    }
}
