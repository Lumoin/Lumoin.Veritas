using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Studio.Wasm;

/// <summary>
/// Serializes a query result to the W3C SPARQL results-JSON the Studio result view reads. The app emits
/// its own UI wire format here, walking only the public result API, rather than referencing the engine's
/// serialization project (which production projects do not depend on). Mirrors the term discrimination
/// the engine's CSV/TSV renderer uses.
/// </summary>
internal static class SparqlResultsJson
{
    /// <summary>The default literal datatype (<c>xsd:string</c>), whose presence is implicit and so not emitted.</summary>
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    /// <summary>Serializes an ASK boolean as a results-JSON document.</summary>
    /// <param name="value">The ASK answer.</param>
    /// <returns>The results-JSON.</returns>
    public static string ForAsk(bool value)
    {
        return value ? """{"head":{},"boolean":true}""" : """{"head":{},"boolean":false}""";
    }

    /// <summary>Serializes a SELECT result set as a results-JSON document (head variables + solution bindings).</summary>
    /// <param name="results">The result set.</param>
    /// <returns>The results-JSON.</returns>
    public static string ForSelect(SparqlResultSet results)
    {
        IReadOnlyList<Utf8String> variables = results.Variables;
        StringBuilder json = new();
        json.Append("{\"head\":{\"vars\":[");
        for(int i = 0; i < variables.Count; i++)
        {
            if(i > 0)
            {
                json.Append(',');
            }

            json.Append(JsonString(variables[i].ToString()));
        }

        json.Append("]},\"results\":{\"bindings\":[");
        for(int s = 0; s < results.Solutions.Count; s++)
        {
            if(s > 0)
            {
                json.Append(',');
            }

            AppendSolution(json, results.Solutions[s], variables);
        }

        json.Append("]}}");

        return json.ToString();
    }

    /// <summary>Appends one solution object: each bound variable mapped to its term JSON.</summary>
    /// <param name="json">The buffer.</param>
    /// <param name="solution">The solution.</param>
    /// <param name="variables">The head variables, in order.</param>
    private static void AppendSolution(StringBuilder json, SparqlSolution solution, IReadOnlyList<Utf8String> variables)
    {
        json.Append('{');
        bool first = true;
        foreach(Utf8String variable in variables)
        {
            if(!solution.TryGetValue(new SparqlVariable(variable), out RdfTerm value))
            {
                continue;
            }

            if(!first)
            {
                json.Append(',');
            }

            first = false;
            json.Append(JsonString(variable.ToString())).Append(':').Append(TermJson(value));
        }

        json.Append('}');
    }

    /// <summary>The results-JSON object for one term: <c>uri</c>, <c>bnode</c>, or a plain/typed/lang <c>literal</c>.</summary>
    /// <param name="term">The term.</param>
    /// <returns>The term JSON object.</returns>
    private static string TermJson(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => "{\"type\":\"uri\",\"value\":" + JsonString(named.Iri.ToString()) + "}",
            BlankNode blank => "{\"type\":\"bnode\",\"value\":" + JsonString(blank.Label.ToString()) + "}",
            Literal literal when literal.Language is { } language =>
                "{\"type\":\"literal\",\"value\":" + JsonString(literal.Value.ToString()) + ",\"xml:lang\":" + JsonString(language.ToString()) + "}",
            Literal literal when literal.Datatype.Iri.Equals(XsdString.Iri) =>
                "{\"type\":\"literal\",\"value\":" + JsonString(literal.Value.ToString()) + "}",
            Literal literal =>
                "{\"type\":\"literal\",\"value\":" + JsonString(literal.Value.ToString()) + ",\"datatype\":" + JsonString(literal.Datatype.Iri.ToString()) + "}",
            _ => "{\"type\":\"literal\",\"value\":" + JsonString(term.ToString() ?? string.Empty) + "}"
        };
    }

    /// <summary>A JSON string literal: the value escaped per RFC 8259 and double-quoted.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The quoted, escaped JSON string.</returns>
    private static string JsonString(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');
        foreach(char character in value)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when character < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }

        builder.Append('"');

        return builder.ToString();
    }
}
