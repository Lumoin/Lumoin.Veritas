using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl;

namespace Lumoin.Veritas.ParserTests.Owl;

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
            new Quad(Blank("n"), Predicate(RdfNs + "type"), new NamedNode(Utf8Strings.From(OwlNs + "Restriction")), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "onProperty"), Named("p"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "minCardinality"), new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#int"))), Graph: null),
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

    /// <summary>A pattern that embeds answers an empty unembedded remainder.</summary>
    [TestMethod]
    public void TryEmbedAnswersEmptyRemainderWhenTheEmbeddingExists()
    {
        List<Quad> data =
        [
            new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null),
            new Quad(Named("b"), Predicate("q"), Named("c"), Graph: null),
        ];
        List<Quad> pattern =
        [
            new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null),
            new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null),
            new Quad(Blank("x"), Predicate("q"), Named("c"), Graph: null),
        ];

        Assert.IsTrue(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.IsEmpty(unembedded);
    }

    /// <summary>A missing ground triple is the exact remainder; the present ones never appear.</summary>
    [TestMethod]
    public void TryEmbedNamesTheMissingGroundTripleExactly()
    {
        List<Quad> data = [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)];
        Quad missing = new(Named("never"), Predicate("p"), Named("b"), Graph: null);
        List<Quad> pattern =
        [
            new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null),
            missing,
        ];

        Assert.IsFalse(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.ContainsSingle(unembedded);
        Assert.AreEqual(missing, unembedded[0]);
    }

    /// <summary>A blank component with no consistent joint binding is reported whole, even when one of its triples matches alone.</summary>
    [TestMethod]
    public void TryEmbedReportsAFailingBlankComponentWhole()
    {
        //a p _:x matches alone (x -> b), but no q-successor of b exists, so
        //the joint binding fails and both component triples are unembedded.
        List<Quad> data = [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)];
        List<Quad> pattern =
        [
            new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null),
            new Quad(Blank("x"), Predicate("q"), Named("c"), Graph: null),
        ];

        Assert.IsFalse(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.HasCount(2, unembedded);
    }

    /// <summary>Independent components fail alone: the satisfiable component never enters the remainder.</summary>
    [TestMethod]
    public void TryEmbedReportsOnlyTheFailingComponent()
    {
        List<Quad> data = [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)];
        Quad satisfiable = new(Named("a"), Predicate("p"), Blank("x"), Graph: null);
        Quad unsatisfiable = new(Blank("y"), Predicate("q"), Named("d"), Graph: null);
        List<Quad> pattern = [satisfiable, unsatisfiable];

        Assert.IsFalse(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.ContainsSingle(unembedded);
        Assert.AreEqual(unsatisfiable, unembedded[0]);
    }

    /// <summary>The graph position does not participate: a data quad in a named graph satisfies a default-graph pattern, matching the embedding's contract.</summary>
    [TestMethod]
    public void TryEmbedIgnoresTheGraphPositionLikeTheEmbedding()
    {
        List<Quad> data = [new Quad(Named("a"), Predicate("p"), Named("b"), Named("g"))];
        List<Quad> pattern =
        [
            new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null),
            new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null),
        ];

        Assert.IsTrue(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.IsEmpty(unembedded);
    }

    /// <summary>A pure-existence scaffold stripped by the comprehension mode never appears in the remainder; the missing ground triple does.</summary>
    [TestMethod]
    public void TryEmbedNeverListsAComprehensionScaffold()
    {
        List<Quad> data = [new Quad(Named("p"), Predicate(RdfNs + "type"), new NamedNode(Utf8Strings.From(OwlNs + "ObjectProperty")), Graph: null)];
        Quad missing = new(Named("a"), Predicate(RdfNs + "type"), new NamedNode(Utf8Strings.From(OwlNs + "Thing")), Graph: null);
        List<Quad> pattern =
        [
            new Quad(Blank("n"), Predicate(RdfNs + "type"), new NamedNode(Utf8Strings.From(OwlNs + "Restriction")), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "onProperty"), Named("p"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "minCardinality"), new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#int"))), Graph: null),
            missing,
        ];

        Assert.IsFalse(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.InformativeConditions, out IReadOnlyList<Quad> unembedded));
        Assert.ContainsSingle(unembedded);
        Assert.AreEqual(missing, unembedded[0]);
    }

    /// <summary>A scaffold node stating two constraint forms is no grant: its triples stay in the remainder instead of stripping.</summary>
    [TestMethod]
    public void TryEmbedListsAMultiConstructorScaffoldInTheRemainder()
    {
        List<Quad> data = [new Quad(Named("p"), Predicate(RdfNs + "type"), new NamedNode(Utf8Strings.From(OwlNs + "ObjectProperty")), Graph: null)];
        List<Quad> pattern =
        [
            new Quad(Blank("n"), Predicate(OwlNs + "onProperty"), Named("p"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "someValuesFrom"), Named("C"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "minCardinality"), new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#int"))), Graph: null),
        ];

        Assert.IsFalse(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.InformativeConditions, out IReadOnlyList<Quad> unembedded));
        Assert.HasCount(3, unembedded);
    }

    /// <summary>The store path refuses the same multi-constraint scaffold the list path refuses.</summary>
    [TestMethod]
    public async Task MultiConstructorScaffoldSurvivesTheStorePath()
    {
        (HypertrieGraphStore store, TermDictionary dictionary, _) = await BuildAsync([("p", RdfNs + "type", OwlNs + "ObjectProperty")]).ConfigureAwait(false);

        List<Quad> conclusion =
        [
            new Quad(Blank("n"), Predicate(OwlNs + "onProperty"), Named("p"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "someValuesFrom"), Named("C"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "minCardinality"), new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#int"))), Graph: null),
        ];

        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(conclusion, store, dictionary, TimeProvider.System, OwlComprehension.InformativeConditions, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>The store path refuses a scaffold whose property the store never forces into standing.</summary>
    [TestMethod]
    public async Task UnforcedScaffoldSurvivesTheStorePath()
    {
        (HypertrieGraphStore store, TermDictionary dictionary, _) = await BuildAsync([("a", "knows", "b")]).ConfigureAwait(false);

        List<Quad> conclusion =
        [
            new Quad(Blank("n"), Predicate(OwlNs + "onProperty"), Named("p"), Graph: null),
            new Quad(Blank("n"), Predicate(OwlNs + "minCardinality"), new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#int"))), Graph: null),
        ];

        Assert.IsFalse(await OwlGraphEntailment.EmbedsAsync(conclusion, store, dictionary, TimeProvider.System, OwlComprehension.InformativeConditions, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>The remainder preserves pattern order across ground and component failures.</summary>
    [TestMethod]
    public void TryEmbedPreservesConclusionOrderInTheRemainder()
    {
        List<Quad> data = [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)];
        Quad firstMissing = new(Named("c"), Predicate("p"), Named("d"), Graph: null);
        Quad componentTriple = new(Blank("y"), Predicate("q"), Named("d"), Graph: null);
        Quad secondMissing = new(Named("e"), Predicate("p"), Named("f"), Graph: null);
        List<Quad> pattern = [firstMissing, componentTriple, secondMissing];

        Assert.IsFalse(OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.HasCount(3, unembedded);
        Assert.AreEqual(firstMissing, unembedded[0]);
        Assert.AreEqual(componentTriple, unembedded[1]);
        Assert.AreEqual(secondMissing, unembedded[2]);
    }

    /// <summary>The remainder verdict agrees with the embedding verdict across the shape battery.</summary>
    [TestMethod]
    public void TryEmbedVerdictAgreesWithEmbeds()
    {
        List<Quad> data =
        [
            new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null),
            new Quad(Named("b"), Predicate("q"), Named("c"), Graph: null),
        ];
        List<List<Quad>> patterns =
        [
            [],
            [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)],
            [new Quad(Named("b"), Predicate("p"), Named("a"), Graph: null)],
            [new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null), new Quad(Blank("x"), Predicate("q"), Named("c"), Graph: null)],
            [new Quad(Named("a"), Predicate("p"), Blank("x"), Graph: null), new Quad(Blank("x"), Predicate("q"), Named("a"), Graph: null)],
            [new Quad(Blank("x"), Predicate("p"), Blank("x"), Graph: null)],
        ];

        foreach(List<Quad> pattern in patterns)
        {
            bool embeds = OwlGraphEntailment.Embeds(pattern, data);
            bool tryEmbeds = OwlGraphEntailment.TryEmbed(pattern, data, OwlComprehension.None, out IReadOnlyList<Quad> unembedded);
            Assert.AreEqual(embeds, tryEmbeds);
            Assert.AreEqual(embeds, unembedded.Count == 0);
        }
    }

    /// <summary>An empty pattern embeds with an empty remainder.</summary>
    [TestMethod]
    public void TryEmbedAnswersEmptyPatternEmbedded()
    {
        Assert.IsTrue(OwlGraphEntailment.TryEmbed([], [new Quad(Named("a"), Predicate("p"), Named("b"), Graph: null)], OwlComprehension.None, out IReadOnlyList<Quad> unembedded));
        Assert.IsEmpty(unembedded);
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
        return new NamedNode(Utf8Strings.From(name.Contains("://", StringComparison.Ordinal) ? name : Example + name));
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
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A blank node with the label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }
}
