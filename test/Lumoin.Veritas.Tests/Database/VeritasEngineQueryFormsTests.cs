using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Tests the <see cref="VeritasEngine"/> query surface's four query forms and dataset handling:
/// CONSTRUCT and DESCRIBE answer result graphs (template instantiation with fresh blank nodes per
/// solution; the Concise Bounded Description with its blank-node closure; the star form's union of
/// bound resources), a dataset clause resolves through the engine's store-local graph source against
/// the LOADED named graphs (refusing unknown IRIs by name), a protocol-supplied dataset overrides the
/// query's own clause, and a configured <see cref="VeritasEngineOptions.GraphSource"/> resolver
/// overrides the store-local default.
/// </summary>
[TestClass]
internal sealed class VeritasEngineQueryFormsTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A CONSTRUCT instantiates its template per solution and answers the distinct union: the two-variable template triple yields one quad per solution while the single-variable one collapses its duplicate instantiations.</summary>
    [TestMethod]
    public async Task ConstructAnswersTheInstantiatedDistinctGraph()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(KnowsChain(), NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The three knows solutions instantiate the two-variable template triple three ways and the
        //?s-only typing triple over two distinct subjects — alice's DUPLICATE typing instantiation
        //collapses, so six instantiations answer five distinct quads.
        VeritasQueryResult result = await database
            .QueryAsync(
                Utf8Strings.From($"CONSTRUCT {{ ?s <{Ex}linked> ?o . ?s <{RdfType}> <{Ex}Subject> }} WHERE {{ ?s <{Ex}knows> ?o }}"),
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.IsGraph, "A CONSTRUCT answers a graph result.");
        Assert.IsFalse(result.IsAsk, "A graph result is not an ASK.");
        Assert.IsNull(result.Bindings, "A graph result carries no bindings.");
        Assert.HasCount(5, result.Graph!, "Three linked quads plus two typing quads: the duplicate typing instantiation collapses.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "alice", Ex + "linked", Ex + "bob")), "alice linked bob is instantiated.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "alice", Ex + "linked", Ex + "carol")), "alice linked carol is instantiated.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "bob", Ex + "linked", Ex + "carol")), "bob linked carol is instantiated.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "alice", RdfType, Ex + "Subject")), "alice's typing quad answers once.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "bob", RdfType, Ex + "Subject")), "bob's typing quad answers once.");
    }

    /// <summary>A template blank node mints a FRESH blank node per solution: the same label answers distinct nodes across rows.</summary>
    [TestMethod]
    public async Task ConstructMintsFreshBlankNodesPerSolution()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(KnowsChain(), NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        VeritasQueryResult result = await database
            .QueryAsync(
                Utf8Strings.From($"CONSTRUCT {{ ?o <{Ex}reachedVia> _:hop }} WHERE {{ <{Ex}alice> <{Ex}knows> ?o }}"),
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.HasCount(2, result.Graph!, "One instantiation per solution.");
        Assert.IsInstanceOfType<BlankNode>(result.Graph![0].Object, "The template blank node instantiates as a blank node.");
        Assert.IsInstanceOfType<BlankNode>(result.Graph![1].Object, "The template blank node instantiates as a blank node.");
        Assert.AreNotEqual(result.Graph![0].Object, result.Graph![1].Object, "The same template label mints DISTINCT blank nodes across solutions.");
    }

    /// <summary>DESCRIBE of an IRI answers its Concise Bounded Description: the resource's subject triples plus the closure of blank-node objects, and nothing from unrelated subjects.</summary>
    [TestMethod]
    public async Task DescribeIriAnswersTheConciseBoundedDescription()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([], NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The blank-node address hangs off alice, so her CBD must follow into it; bob's own triple must not ride.
        await database
            .UpdateAsync(
                Utf8Strings.From($"INSERT DATA {{ <{Ex}alice> <{Ex}hasAddress> _:addr . _:addr <{Ex}street> \"Main\" . <{Ex}alice> <{Ex}knows> <{Ex}bob> . <{Ex}bob> <{Ex}knows> <{Ex}carol> }}"),
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"DESCRIBE <{Ex}alice>"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.HasCount(3, result.Graph!, "alice's two subject triples plus the followed blank node's one.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "alice", Ex + "knows", Ex + "bob")), "alice's own triple is described.");
        Assert.IsTrue(ContainsPredicate(result.Graph!, Ex + "hasAddress"), "The blank-node-valued triple is described.");
        Assert.IsTrue(ContainsPredicate(result.Graph!, Ex + "street"), "The blank node's own triple rides the closure.");
        Assert.IsFalse(ContainsQuad(result.Graph!, Quad(Ex + "bob", Ex + "knows", Ex + "carol")), "An unrelated subject's triple does not ride.");
    }

    /// <summary>DESCRIBE of a variable describes the resources the variable binds across the WHERE solutions.</summary>
    [TestMethod]
    public async Task DescribeVariableDescribesItsBoundResources()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(KnowsChain(), NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        VeritasQueryResult result = await database
            .QueryAsync(
                Utf8Strings.From($"DESCRIBE ?x WHERE {{ <{Ex}alice> <{Ex}knows> ?x . ?x <{Ex}knows> ?y }}"),
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        //Only bob both is known by alice and knows someone; his description is his subject triple.
        Assert.HasCount(1, result.Graph!, "The one bound resource's description.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "bob", Ex + "knows", Ex + "carol")), "The bound resource's subject triple is described.");
    }

    /// <summary>DESCRIBE of a resource absent from the data answers the empty graph rather than failing.</summary>
    [TestMethod]
    public async Task DescribeAbsentResourceAnswersTheEmptyGraph()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(KnowsChain(), NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"DESCRIBE <{Ex}ghost>"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.IsGraph, "An absent resource still answers the graph shape.");
        Assert.IsEmpty(result.Graph!, "Nothing describes an absent resource.");
    }

    /// <summary>DESCRIBE * takes the union of EVERY bound variable's resources — not just the first variable's.</summary>
    [TestMethod]
    public async Task DescribeStarDescribesEveryBoundResource()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(KnowsChain(withDave: true), NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The one solution binds ?x = bob and ?y = carol; the star form must describe BOTH.
        VeritasQueryResult result = await database
            .QueryAsync(
                Utf8Strings.From($"DESCRIBE * WHERE {{ <{Ex}alice> <{Ex}knows> ?x . ?x <{Ex}knows> ?y . ?y <{Ex}knows> <{Ex}dave> }}"),
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.HasCount(2, result.Graph!, "Both bound resources' descriptions.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "bob", Ex + "knows", Ex + "carol")), "The first variable's resource is described.");
        Assert.IsTrue(ContainsQuad(result.Graph!, Quad(Ex + "carol", Ex + "knows", Ex + "dave")), "The second variable's resource is described too.");
    }

    /// <summary>With no configured resolver, FROM and FROM NAMED resolve against the engine's OWN loaded named graphs: the clause overrides the default dataset with the named graph's content.</summary>
    [TestMethod]
    public async Task FromNamedResolvesAgainstTheLoadedNamedGraphs()
    {
        VeritasEngine database = await OpenWithNamedGraphsAsync().ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //FROM <g1> makes g1's content the effective default graph.
        bool fromSeesGraph = await database
            .AskAsync(Utf8Strings.From($"ASK FROM <{Ex}g1> WHERE {{ <{Ex}x> <{Ex}p> <{Ex}y> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(fromSeesGraph, "FROM over a loaded named graph serves its triples as the default graph.");

        //The effective dataset REPLACES the original default graph, so its triple is gone under the clause.
        bool fromHidesOriginalDefault = await database
            .AskAsync(Utf8Strings.From($"ASK FROM <{Ex}g1> WHERE {{ <{Ex}alice> <{Ex}knows> <{Ex}bob> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(fromHidesOriginalDefault, "The dataset clause overrides the default dataset entirely.");

        //FROM NAMED keys the loaded graph's content under its IRI for GRAPH patterns.
        bool fromNamedServesGraphPattern = await database
            .AskAsync(Utf8Strings.From($"ASK FROM NAMED <{Ex}g1> WHERE {{ GRAPH <{Ex}g1> {{ <{Ex}x> <{Ex}p> <{Ex}y> }} }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(fromNamedServesGraphPattern, "FROM NAMED serves the loaded graph under its IRI.");
    }

    /// <summary>A dataset-clause IRI naming no loaded graph refuses loudly by name — never an empty guess.</summary>
    [TestMethod]
    public async Task FromUnknownGraphRefusesByName()
    {
        VeritasEngine database = await OpenWithNamedGraphsAsync().ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        UnknownGraphSourceException? refusal = null;
        try
        {
            await database
                .QueryAsync(Utf8Strings.From($"ASK FROM <{Ex}nowhere> WHERE {{ ?s ?p ?o }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.Fail("An unknown FROM graph must refuse.");
        }
        catch(UnknownGraphSourceException ex)
        {
            refusal = ex;
        }

        Assert.Contains($"{Ex}nowhere", refusal!.Message, "The refusal names the unresolvable IRI.");
    }

    /// <summary>A protocol-supplied dataset REPLACES the query's own FROM clause, per the protocol's precedence rule.</summary>
    [TestMethod]
    public async Task ProtocolDatasetOverridesTheQuerysOwnClause()
    {
        VeritasEngine database = await OpenWithNamedGraphsAsync().ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Utf8String query = Utf8Strings.From($"ASK FROM <{Ex}g1> WHERE {{ <{Ex}a> <{Ex}b> <{Ex}c> }}");

        //Under the query's own clause the g2 triple is invisible.
        VeritasQueryResult ownClause = await database
            .QueryAsync(query, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(ownClause.Boolean!.Value, "The query's own FROM <g1> does not see g2's triple.");

        //The protocol dataset replaces the clause, so the same query text now reads g2.
        DatasetClause protocolDataset = new(SourceSpan.None, [new IriRef(Utf8Strings.From(Ex + "g2"), SourceSpan.None)], []);
        VeritasQueryResult overridden = await database
            .QueryAsync(query, protocolDataset: protocolDataset, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(overridden.Boolean!.Value, "The protocol dataset takes precedence over the query's own clause.");
    }

    /// <summary>A configured <see cref="VeritasEngineOptions.GraphSource"/> resolver overrides the store-local default entirely, so a clause IRI the store could not serve resolves through it.</summary>
    [TestMethod]
    public async Task GraphSourceOptionOverridesTheEngineDefault()
    {
        VeritasEngineOptions withResolver = NoReasoning with { GraphSource = ConstantTripleSource };
        VeritasEngine database = await VeritasEngine
            .OpenAsync(KnowsChain(), withResolver, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The IRI names no loaded graph; the configured resolver serves it anyway, proving the override.
        bool resolved = await database
            .AskAsync(Utf8Strings.From($"ASK FROM <{Ex}anywhere> WHERE {{ <{Ex}x> <{Ex}p> <{Ex}y> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(resolved, "A configured graph source overrides the store-local default.");
    }

    /// <summary>The reasoning-unwired options every row opens under, so the asserted graphs stay exact.</summary>
    private static VeritasEngineOptions NoReasoning { get; } = VeritasEngineOptions.Default with { Reasoning = null };

    /// <summary>Builds the shared knows chain: alice knows bob, alice knows carol, bob knows carol (and carol knows dave when <paramref name="withDave"/>).</summary>
    /// <param name="withDave">Whether the chain extends to dave.</param>
    /// <returns>The default-graph triples.</returns>
    private static List<DataTriple> KnowsChain(bool withDave = false)
    {
        List<DataTriple> triples =
        [
            new DataTriple(Iri(Ex + "alice"), Iri(Ex + "knows"), Iri(Ex + "bob")),
            new DataTriple(Iri(Ex + "alice"), Iri(Ex + "knows"), Iri(Ex + "carol")),
            new DataTriple(Iri(Ex + "bob"), Iri(Ex + "knows"), Iri(Ex + "carol"))
        ];
        if(withDave)
        {
            triples.Add(new DataTriple(Iri(Ex + "carol"), Iri(Ex + "knows"), Iri(Ex + "dave")));
        }

        return triples;
    }

    /// <summary>Opens an immutable engine whose default graph is the knows chain and whose named graphs are <c>g1</c> (<c>x p y</c>) and <c>g2</c> (<c>a b c</c>).</summary>
    /// <returns>The opened engine.</returns>
    private async Task<VeritasEngine> OpenWithNamedGraphsAsync()
    {
        IReadOnlyList<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named =
        [
            (Iri(Ex + "g1"), new List<DataTriple> { new(Iri(Ex + "x"), Iri(Ex + "p"), Iri(Ex + "y")) }),
            (Iri(Ex + "g2"), new List<DataTriple> { new(Iri(Ex + "a"), Iri(Ex + "b"), Iri(Ex + "c")) })
        ];

        return await VeritasEngine
            .OpenAsync(KnowsChain(), named, NoReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The constant-answer graph source the override row configures: any IRI resolves to the one <c>x p y</c> triple.</summary>
    /// <param name="source">The requested graph IRI, ignored.</param>
    /// <param name="accessContext">The forwarded access context, ignored.</param>
    /// <param name="cancellationToken">A token that aborts the stream.</param>
    /// <returns>The single-triple stream.</returns>
    private static async IAsyncEnumerable<DataTriple> ConstantTripleSource(IriRef source, AccessContext? accessContext, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        yield return new DataTriple(Iri(Ex + "x"), Iri(Ex + "p"), Iri(Ex + "y"));

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Builds a named node over an IRI string.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Builds a default-graph quad over three IRIs.</summary>
    /// <param name="subject">The subject IRI.</param>
    /// <param name="predicate">The predicate IRI.</param>
    /// <param name="object">The object IRI.</param>
    /// <returns>The quad.</returns>
    private static Quad Quad(string subject, string predicate, string @object)
    {
        return new Quad(Iri(subject), Iri(predicate), Iri(@object));
    }

    /// <summary>Whether <paramref name="graph"/> contains <paramref name="quad"/>.</summary>
    /// <param name="graph">The graph to search.</param>
    /// <param name="quad">The quad to look for.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsQuad(IReadOnlyList<Quad> graph, Quad quad)
    {
        foreach(Quad candidate in graph)
        {
            if(candidate == quad)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any quad in <paramref name="graph"/> carries the predicate <paramref name="predicateIri"/>.</summary>
    /// <param name="graph">The graph to search.</param>
    /// <param name="predicateIri">The predicate IRI.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsPredicate(IReadOnlyList<Quad> graph, string predicateIri)
    {
        NamedNode predicate = Iri(predicateIri);
        foreach(Quad candidate in graph)
        {
            if(candidate.Predicate == predicate)
            {
                return true;
            }
        }

        return false;
    }
}
