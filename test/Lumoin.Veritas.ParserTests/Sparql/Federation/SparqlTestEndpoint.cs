using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Lumoin.Veritas.ParserTests.Sparql.Federation;

/// <summary>
/// One SPARQL endpoint in a <see cref="SparqlTestHostShell"/>: a query engine over a fixed graph plus the
/// loopback Kestrel server (and its <see cref="HttpClient"/>) that exposes it once the shell is started. The same
/// endpoint is reachable both in-process (call <see cref="ExecuteAsync"/> directly) and over HTTP (POST to
/// <see cref="BaseAddress"/>), which is what lets the federation tests assert the two transports agree.
/// </summary>
internal sealed class SparqlTestEndpoint
{
    /// <summary>The endpoint's logical name within the shell.</summary>
    public string Name { get; }

    /// <summary>The engine evaluating queries against this endpoint's graph.</summary>
    public SparqlQueryEngine Engine { get; }

    /// <summary>The loopback Kestrel server exposing the endpoint, or <see langword="null"/> until the shell is started.</summary>
    public KestrelServer? Server { get; set; }

    /// <summary>The endpoint's HTTP base address (with the OS-assigned port), or <see langword="null"/> until started.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>An HTTP client bound to <see cref="BaseAddress"/>, or <see langword="null"/> until started.</summary>
    public HttpClient? Client { get; set; }

    /// <summary>Initialises an endpoint over the given engine.</summary>
    /// <param name="name">The endpoint's logical name.</param>
    /// <param name="engine">The engine evaluating queries against the endpoint's graph.</param>
    public SparqlTestEndpoint(string name, SparqlQueryEngine engine)
    {
        Name = name;
        Engine = engine;
    }

    /// <summary>Parses, translates, and evaluates a self-contained query against this endpoint's graph and packages the result set (ASK boolean or SELECT projection).</summary>
    /// <param name="query">The self-contained SPARQL query.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The endpoint's result set.</returns>
    public async ValueTask<SparqlResultSet> ExecuteAsync(string query, CancellationToken cancellationToken)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(query), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());
        AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
        IReadOnlyList<SparqlSolution> solutions = await Engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        return normalized.Form is AskQuery
            ? SparqlResultSet.ForAsk(solutions.Count > 0)
            : SparqlResultSet.ForSelect([.. algebra.OutputVariables.Select(variable => variable.Name)], solutions);
    }
}
