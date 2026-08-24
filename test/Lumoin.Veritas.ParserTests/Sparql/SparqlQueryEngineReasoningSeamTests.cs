using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests the engine's reasoning-materialisation seam — the construction hook a
/// composition root supplies a reasoner through. A wired delegate's returned
/// store is the one the engine serves, so a query sees the materialised
/// entailments; with no delegate the source graph is served untouched.
/// </summary>
[TestClass]
internal sealed class SparqlQueryEngineReasoningSeamTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NoSeamServesTheSourceGraphUntouched()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //The triple :a :q :c was never derived, so it is absent from the served graph.
        Assert.IsEmpty(await SolveAsync(engine, "PREFIX : <http://example.org/> SELECT * WHERE { :a :q ?o }").ConfigureAwait(false));
    }

    [TestMethod]
    public async Task WiredSeamMaterialisesEntailmentsIntoTheServedGraph()
    {
        //A stand-in reasoner that derives :a :q :c into the graph, building the
        //post-materialisation store over the same dictionary the engine encoded with.
        ReasoningMaterializationDelegate materialise = (store, dictionary, token) =>
        {
            List<EncodedTriple> derived =
            [
                new EncodedTriple(dictionary.GetOrAdd(Iri("a")), dictionary.GetOrAdd(Iri("p")), dictionary.GetOrAdd(Iri("b"))),
                new EncodedTriple(dictionary.GetOrAdd(Iri("a")), dictionary.GetOrAdd(Iri("q")), dictionary.GetOrAdd(Iri("c"))),
            ];

            return HypertrieGraphStore.BuildAsync(derived, VeritasHashing.Default, token);
        };

        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))],
            reasoning: materialise,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //The derived triple is queryable, where the source graph alone did not carry it.
        Assert.HasCount(1, await SolveAsync(engine, "PREFIX : <http://example.org/> SELECT * WHERE { :a :q ?o }").ConfigureAwait(false));
    }

    /// <summary>Parses, normalises, translates, and evaluates a query against the engine.</summary>
    /// <param name="engine">The engine to query.</param>
    /// <param name="query">The query text.</param>
    /// <returns>The solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> SolveAsync(SparqlQueryEngine engine, string query)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(query), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery parsed = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());
        AlgebraOperator algebra = SparqlTranslator.Translate(parsed);

        return await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an example-namespace IRI term.</summary>
    /// <param name="localName">The local name appended to the example namespace.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }
}
