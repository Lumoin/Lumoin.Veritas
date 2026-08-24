using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql.Federation;

/// <summary>
/// End-to-end SPARQL federation (<c>SERVICE</c>) tests over a multi-endpoint <see cref="SparqlTestHostShell"/>.
/// Each case runs the identical federated query under both the in-process transport and the real
/// Kestrel/HttpClient transport and asserts the two agree — the wire-fidelity check that the query the engine
/// renders for a remote endpoint round-trips through SPARQL Results JSON unchanged.
/// </summary>
[TestClass]
internal sealed class SparqlFederationTests
{
    /// <summary>The ambient test context (carries the cancellation token).</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A constant-endpoint SERVICE joins a local pattern with a remote endpoint, identically in-process and over HTTP.</summary>
    [TestMethod]
    public async Task FederatesConstantEndpointUnderBothTransports()
    {
        SparqlTestHostShell shell = new();
        await using(shell.ConfigureAwait(false))
        {
            await shell.AddEndpointAsync("companies", [new DataTriple(N("acme"), N("city"), N("helsinki"))], TestContext.CancellationToken).ConfigureAwait(false);
            await shell.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

            IReadOnlyList<DataTriple> local = [new DataTriple(N("alice"), N("worksAt"), N("acme"))];
            string companies = shell.EndpointIri("companies").Value.ToString();
            string query = $"PREFIX : <http://example.org/> SELECT * WHERE {{ :alice :worksAt ?c . SERVICE <{companies}> {{ ?c :city ?city }} }}";

            IReadOnlyList<SparqlSolution> inProcess = await RunAsync(local, shell.InProcessTransport, query).ConfigureAwait(false);
            IReadOnlyList<SparqlSolution> overHttp = await RunAsync(local, shell.HttpTransport, query).ConfigureAwait(false);

            Assert.AreEqual(Canonical(inProcess), Canonical(overHttp));
            SparqlSolution solution = inProcess.Single();
            Assert.AreEqual("http://example.org/acme", Value(solution, "c"));
            Assert.AreEqual("http://example.org/helsinki", Value(solution, "city"));
        }
    }

    /// <summary>A variable-endpoint SERVICE routes each binding to the endpoint its solution names; the results match in-process and over HTTP.</summary>
    [TestMethod]
    public async Task FederatesVariableEndpointUnderBothTransports()
    {
        SparqlTestHostShell shell = new();
        await using(shell.ConfigureAwait(false))
        {
            await shell.AddEndpointAsync("e1", [new DataTriple(N("alice"), N("city"), N("helsinki"))], TestContext.CancellationToken).ConfigureAwait(false);
            await shell.AddEndpointAsync("e2", [new DataTriple(N("bob"), N("city"), N("tampere"))], TestContext.CancellationToken).ConfigureAwait(false);
            await shell.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

            IReadOnlyList<DataTriple> local =
            [
                new DataTriple(N("alice"), N("endpoint"), new NamedNode(shell.EndpointIri("e1").Value)),
                new DataTriple(N("bob"), N("endpoint"), new NamedNode(shell.EndpointIri("e2").Value)),
            ];
            string query = "PREFIX : <http://example.org/> SELECT * WHERE { ?p :endpoint ?ep . SERVICE ?ep { ?p :city ?city } }";

            IReadOnlyList<SparqlSolution> inProcess = await RunAsync(local, shell.InProcessTransport, query).ConfigureAwait(false);
            IReadOnlyList<SparqlSolution> overHttp = await RunAsync(local, shell.HttpTransport, query).ConfigureAwait(false);

            Assert.AreEqual(Canonical(inProcess), Canonical(overHttp));
            Assert.HasCount(2, inProcess);
            Assert.AreEqual("http://example.org/helsinki", Value(inProcess.Single(s => Value(s, "p") == "http://example.org/alice"), "city"));
            Assert.AreEqual("http://example.org/tampere", Value(inProcess.Single(s => Value(s, "p") == "http://example.org/bob"), "city"));
        }
    }

    /// <summary>A SERVICE SILENT against an unreachable endpoint leaves the surrounding solutions intact — under both transports (the HTTP one a genuine connection refusal).</summary>
    [TestMethod]
    public async Task SilentServiceSurvivesUnreachableEndpointUnderBothTransports()
    {
        SparqlTestHostShell shell = new();
        await using(shell.ConfigureAwait(false))
        {
            await shell.StartAsync(TestContext.CancellationToken).ConfigureAwait(false);

            IReadOnlyList<DataTriple> local = [new DataTriple(N("alice"), N("worksAt"), N("acme"))];

            //Port 1 is not listening: the in-process transport finds no endpoint, the HTTP transport's connection is refused.
            string query = "PREFIX : <http://example.org/> SELECT * WHERE { :alice :worksAt ?c . SERVICE SILENT <http://127.0.0.1:1/> { ?c :city ?city } }";

            IReadOnlyList<SparqlSolution> inProcess = await RunAsync(local, shell.InProcessTransport, query).ConfigureAwait(false);
            IReadOnlyList<SparqlSolution> overHttp = await RunAsync(local, shell.HttpTransport, query).ConfigureAwait(false);

            Assert.AreEqual(Canonical(inProcess), Canonical(overHttp));
            SparqlSolution solution = inProcess.Single();
            Assert.AreEqual("http://example.org/acme", Value(solution, "c"));
            Assert.IsFalse(solution.TryGetValue(new SparqlVariable(Utf8Strings.From("city")), out _));
        }
    }

    /// <summary>Builds a driver engine over the local graph with a SERVICE transport, then parses, translates, and evaluates the query.</summary>
    /// <param name="local">The driver's local graph.</param>
    /// <param name="transport">The transport backing the driver's SERVICE client.</param>
    /// <param name="query">The federated query.</param>
    /// <returns>The driver's solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> RunAsync(IReadOnlyList<DataTriple> local, SparqlServiceTransport transport, string query)
    {
        SparqlClient client = new(transport);
        SparqlQueryEngine driver = await SparqlQueryEngine.BuildAsync(local, serviceClient: client, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(query), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());
        AlgebraOperator algebra = SparqlTranslator.Translate(normalized);

        return await driver.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>A stable, order-independent string form of a solution sequence, for comparing the two transports' results.</summary>
    /// <param name="solutions">The solutions to canonicalise.</param>
    /// <returns>The canonical form.</returns>
    private static string Canonical(IReadOnlyList<SparqlSolution> solutions)
    {
        return string.Join("\n", solutions
            .Select(solution => string.Join(" | ", solution.Bindings
                .OrderBy(binding => binding.Variable.Name.ToString(), StringComparer.Ordinal)
                .Select(binding => $"{binding.Variable.Name}={binding.Value}")))
            .OrderBy(row => row, StringComparer.Ordinal));
    }

    /// <summary>The IRI string a variable is bound to in a solution.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name (without the leading marker).</param>
    /// <returns>The bound IRI.</returns>
    private static string Value(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(new SparqlVariable(Utf8Strings.From(variableName)), out RdfTerm value), $"Expected ?{variableName} to be bound.");

        return ((NamedNode)value).Iri.ToString();
    }

    /// <summary>Builds an example-namespace named node from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named node.</returns>
    private static NamedNode N(string localName)
    {
        return new NamedNode(Utf8Strings.From("http://example.org/" + localName));
    }
}
