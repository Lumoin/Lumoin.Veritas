using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The black-box read of a reasoned mutable engine's SERVED default graph, shared
/// by the production-path certification arms. It reads the served store back
/// through the public query surface — a whole-graph <c>SELECT ?s ?p ?o</c>, the
/// cheapest faithful read the facade exposes (CONSTRUCT is not rendered by the
/// database query surface) — decoding each solution to its RDF terms. It also
/// serialises RDF terms to the SPARQL update syntax the arms build <c>INSERT
/// DATA</c>/<c>DELETE DATA</c> from, and folds a served triple set into its
/// cross-dictionary-portable portion and dictionary-scoped count: an engine's
/// served minted nodes (the transitivity chain list nodes RL materialises) carry
/// content keys over dictionary-scoped term identifiers that a from-scratch
/// oracle over a different dictionary cannot reproduce verbatim — as any served
/// blank node's label equally cannot — so the arms compare the portable portion
/// exactly and the dictionary-scoped structure by count, its exact shape riding
/// the engine-level lane.
/// </summary>
internal static class ProductionPathServedReader
{
    /// <summary>The whole-graph scan of the served default graph.</summary>
    private const string ScanQuery = "SELECT ?s ?p ?o WHERE { ?s ?p ?o }";

    /// <summary>Reads every triple the engine's served default graph answers, decoded to RDF terms.</summary>
    /// <param name="engine">The reasoned mutable engine whose served store to scan.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The served triples as decoded RDF-term tuples.</returns>
    public static async Task<List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)>> ReadServedAsync(
        VeritasEngine engine,
        CancellationToken cancellationToken)
    {
        VeritasQueryResult result = await engine
            .QueryAsync(Utf8Strings.From(ScanQuery), cancellationToken: cancellationToken).ConfigureAwait(false);
        SparqlResultSet bindings = result.Bindings!;
        IReadOnlyList<Utf8String> variables = bindings.Variables;
        SparqlVariable subjectVariable = new(variables[0]);
        SparqlVariable predicateVariable = new(variables[1]);
        SparqlVariable objectVariable = new(variables[2]);

        List<(RdfTerm, RdfTerm, RdfTerm)> served = new(bindings.Solutions.Count);
        foreach(SparqlSolution solution in bindings.Solutions)
        {
            solution.TryGetValue(subjectVariable, out RdfTerm subject);
            solution.TryGetValue(predicateVariable, out RdfTerm predicate);
            solution.TryGetValue(objectVariable, out RdfTerm @object);
            served.Add((subject, predicate, @object));
        }

        return served;
    }

    /// <summary>Reads the served default graph back as a quad list, for the graph-embedding entailment checker.</summary>
    /// <param name="engine">The reasoned mutable engine whose served store to scan.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The served triples as default-graph quads.</returns>
    public static async Task<List<Quad>> ReadServedQuadsAsync(VeritasEngine engine, CancellationToken cancellationToken)
    {
        List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> served = await ReadServedAsync(engine, cancellationToken).ConfigureAwait(false);
        List<Quad> quads = new(served.Count);
        foreach((RdfTerm subject, RdfTerm predicate, RdfTerm @object) in served)
        {
            quads.Add(new Quad(subject, (NamedNode)predicate, @object, Graph: null));
        }

        return quads;
    }

    /// <summary>Whether a triple carries a dictionary-scoped node in any position — a blank node, or an engine-minted node whose content key embeds dictionary-scoped term identifiers (the transitivity chain list nodes RL materialises).</summary>
    /// <param name="triple">The triple to test.</param>
    /// <returns><see langword="true"/> when a blank or engine-minted node appears in the subject, predicate, or object.</returns>
    public static bool HasDictionaryScopedNode((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) triple)
    {
        return triple.Subject is BlankNode or EngineNode || triple.Predicate is BlankNode or EngineNode || triple.Object is BlankNode or EngineNode;
    }

    /// <summary>The triples of a set free of dictionary-scoped nodes, as a value-equatable set comparable across term dictionaries.</summary>
    /// <param name="triples">The triples to fold.</param>
    /// <returns>The cross-dictionary-portable triples.</returns>
    public static HashSet<(RdfTerm, RdfTerm, RdfTerm)> PortablePortion(IEnumerable<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> triples)
    {
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> free = [];
        foreach((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) triple in triples)
        {
            if(!HasDictionaryScopedNode(triple))
            {
                free.Add((triple.Subject, triple.Predicate, triple.Object));
            }
        }

        return free;
    }

    /// <summary>The number of dictionary-scoped triples in a set — the count the arms compare when exact minted identities cannot cross a dictionary boundary.</summary>
    /// <param name="triples">The triples to count over.</param>
    /// <returns>The count of triples carrying a blank or engine-minted node.</returns>
    public static int DictionaryScopedCount(IEnumerable<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> triples)
    {
        int count = 0;
        foreach((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) triple in triples)
        {
            if(HasDictionaryScopedNode(triple))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Renders an RDF term to the SPARQL update syntax an <c>INSERT DATA</c>/<c>DELETE DATA</c> quad builds from.</summary>
    /// <param name="term">The term to render; a named node or a literal (blank nodes are never serialised by the arms).</param>
    /// <returns>The SPARQL term text.</returns>
    /// <exception cref="NotSupportedException">The term is a blank node or a triple term, which the arms never route through the update text.</exception>
    public static string SparqlTerm(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => $"<{named.Iri}>",
            Literal literal => RenderLiteral(literal),
            _ => throw new NotSupportedException($"The production-path arms serialise only named nodes and literals into update text; got {term.GetType().Name}.")
        };
    }

    /// <summary>Renders a literal to its SPARQL syntax, escaping the lexical form.</summary>
    /// <param name="literal">The literal to render.</param>
    /// <returns>The SPARQL literal text.</returns>
    private static string RenderLiteral(Literal literal)
    {
        string lexical = Escape(literal.Value.ToString());

        return literal.Language is { } language
            ? $"\"{lexical}\"@{language}"
            : $"\"{lexical}\"^^<{literal.Datatype.Iri}>";
    }

    /// <summary>Escapes a literal's lexical form for the SPARQL double-quoted string production.</summary>
    /// <param name="lexical">The raw lexical form.</param>
    /// <returns>The escaped lexical form.</returns>
    private static string Escape(string lexical)
    {
        StringBuilder builder = new(lexical.Length);
        foreach(char character in lexical)
        {
            switch(character)
            {
                case('\\'):
                {
                    builder.Append("\\\\");
                    break;
                }
                case('"'):
                {
                    builder.Append("\\\"");
                    break;
                }
                case('\n'):
                {
                    builder.Append("\\n");
                    break;
                }
                case('\r'):
                {
                    builder.Append("\\r");
                    break;
                }
                case('\t'):
                {
                    builder.Append("\\t");
                    break;
                }
                default:
                {
                    builder.Append(character);
                    break;
                }
            }
        }

        return builder.ToString();
    }
}
