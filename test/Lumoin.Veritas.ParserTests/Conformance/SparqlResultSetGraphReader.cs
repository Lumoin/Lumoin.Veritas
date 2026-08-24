using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Reads a SELECT/ASK result expressed as an RDF graph in the W3C
/// <see href="http://www.w3.org/2001/sw/DataAccess/tests/result-set#">result-set vocabulary</see> (some older W3C
/// query-evaluation tests give <c>mf:result</c> as a <c>.ttl</c> graph rather than an <c>.srx</c>/<c>.srj</c>
/// document) into a <see cref="SparqlResultSet"/> for value comparison.
/// </summary>
/// <remarks>
/// The graph has a <c>rs:ResultSet</c> node carrying <c>rs:resultVariable</c> head names and either an
/// <c>rs:boolean</c> (an ASK result) or a set of <c>rs:solution</c> nodes, each a bag of <c>rs:binding</c>
/// <c>[ rs:variable "v" ; rs:value term ]</c> pairs. Solution and binding order is not significant; the comparer is
/// bag-based, so <c>rs:index</c> is ignored.
/// </remarks>
internal static class SparqlResultSetGraphReader
{
    private const string ResultSetNamespace = "http://www.w3.org/2001/sw/DataAccess/tests/result-set#";

    private static NamedNode RdfType { get; } = new(Vocabulary.Rdf.Type);

    private static NamedNode ResultSetType { get; } = Rs("ResultSet");

    private static NamedNode ResultVariable { get; } = Rs("resultVariable");

    private static NamedNode Solution { get; } = Rs("solution");

    private static NamedNode Binding { get; } = Rs("binding");

    private static NamedNode Variable { get; } = Rs("variable");

    private static NamedNode Value { get; } = Rs("value");

    private static NamedNode Boolean { get; } = Rs("boolean");

    /// <summary>Builds a <see cref="SparqlResultSet"/> from the quads of an <c>rs:ResultSet</c> graph.</summary>
    /// <param name="quads">The expected result graph.</param>
    /// <returns>The SELECT or ASK result set.</returns>
    /// <exception cref="FormatException">The graph holds no <c>rs:ResultSet</c> node.</exception>
    public static SparqlResultSet Read(IReadOnlyList<Quad> quads)
    {
        ArgumentNullException.ThrowIfNull(quads);

        Dictionary<RdfTerm, List<Quad>> bySubject = [];
        foreach(Quad quad in quads)
        {
            if(!bySubject.TryGetValue(quad.Subject, out List<Quad>? statements))
            {
                statements = [];
                bySubject[quad.Subject] = statements;
            }

            statements.Add(quad);
        }

        RdfTerm resultSet = FindResultSet(quads)
            ?? throw new FormatException("The expected result graph holds no rs:ResultSet node.");

        if(FirstObject(bySubject, resultSet, Boolean) is Literal booleanLiteral)
        {
            return SparqlResultSet.ForAsk(string.Equals(booleanLiteral.Value.ToString(), "true", StringComparison.Ordinal));
        }

        List<Utf8String> variables = [];
        HashSet<Utf8String> seen = [];
        foreach(RdfTerm variable in Objects(bySubject, resultSet, ResultVariable))
        {
            if(variable is Literal name && seen.Add(name.Value))
            {
                variables.Add(name.Value);
            }
        }

        List<SparqlSolution> solutions = [];
        foreach(RdfTerm solutionNode in Objects(bySubject, resultSet, Solution))
        {
            List<SparqlBinding> bindings = [];
            foreach(RdfTerm bindingNode in Objects(bySubject, solutionNode, Binding))
            {
                if(FirstObject(bySubject, bindingNode, Variable) is Literal variableName
                    && FirstObject(bySubject, bindingNode, Value) is { } value)
                {
                    bindings.Add(new SparqlBinding(new SparqlVariable(variableName.Value), value));
                }
            }

            solutions.Add(new SparqlSolution(bindings));
        }

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Finds the <c>rs:ResultSet</c> node — the subject typed <c>rs:ResultSet</c>.</summary>
    /// <param name="quads">The graph quads.</param>
    /// <returns>The result-set node, or <see langword="null"/> when absent.</returns>
    private static RdfTerm? FindResultSet(IReadOnlyList<Quad> quads)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Predicate.Equals(RdfType) && quad.Object.Equals(ResultSetType))
            {
                return quad.Subject;
            }
        }

        return null;
    }

    /// <summary>The objects of all <c>(subject, predicate, ?)</c> statements.</summary>
    /// <param name="bySubject">The quads indexed by subject.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <returns>The matching objects.</returns>
    private static IEnumerable<RdfTerm> Objects(Dictionary<RdfTerm, List<Quad>> bySubject, RdfTerm subject, NamedNode predicate)
    {
        if(!bySubject.TryGetValue(subject, out List<Quad>? statements))
        {
            yield break;
        }

        foreach(Quad quad in statements)
        {
            if(quad.Predicate.Equals(predicate))
            {
                yield return quad.Object;
            }
        }
    }

    /// <summary>The first object of a <c>(subject, predicate, ?)</c> statement, or <see langword="null"/>.</summary>
    /// <param name="bySubject">The quads indexed by subject.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <returns>The first matching object, or <see langword="null"/>.</returns>
    private static RdfTerm? FirstObject(Dictionary<RdfTerm, List<Quad>> bySubject, RdfTerm subject, NamedNode predicate)
    {
        foreach(RdfTerm value in Objects(bySubject, subject, predicate))
        {
            return value;
        }

        return null;
    }

    /// <summary>Builds a named node in the result-set vocabulary.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Rs(string localName)
    {
        return new NamedNode(Utf8Strings.From(ResultSetNamespace + localName));
    }
}
