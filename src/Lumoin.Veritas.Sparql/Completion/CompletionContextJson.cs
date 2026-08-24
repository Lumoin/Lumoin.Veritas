using System;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;

namespace Lumoin.Veritas.Sparql.Completion;

/// <summary>
/// Projects a <see cref="CompletionContext"/> into the compact JSON an editor's completion popup consumes:
/// the caret byte offset, the expected next token kinds and the enclosing productions (as their names), the
/// in-scope variables (each with its resolved datatype when known), and the variable→predicate pairs. This is
/// the editor's wire shape, not a canonical format — every host that answers completion answers this one
/// document, so the popup reads one shape whichever tier produced it.
/// </summary>
public static class CompletionContextJson
{
    /// <summary>Serializes a completion context as the editor's completion JSON.</summary>
    /// <param name="context">The completion context.</param>
    /// <returns>The completion JSON.</returns>
    public static string Write(CompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        StringBuilder json = new();
        json.Append("{\"caret\":").Append(context.CaretByteOffset).Append(",\"expectedTokens\":[");

        bool first = true;
        foreach(SparqlTokenKind token in context.ExpectedTokens)
        {
            AppendSeparator(json, ref first);
            json.Append(JsonString(token.ToString()));
        }

        json.Append("],\"enclosingProductions\":[");

        first = true;
        foreach(ParseFrameKind production in context.EnclosingProductions)
        {
            AppendSeparator(json, ref first);
            json.Append(JsonString(production.ToString()));
        }

        json.Append("],\"inScopeVariables\":[");

        first = true;
        foreach(ScopeVariable variable in context.InScopeVariables)
        {
            AppendSeparator(json, ref first);
            json.Append("{\"name\":").Append(JsonString(variable.Variable.Name.ToString()))
                .Append(",\"datatype\":").Append(variable.Datatype is { } datatype ? JsonString(datatype.ToString()) : "null")
                .Append(",\"datatypeSource\":").Append(JsonString(variable.DatatypeSource.ToString()))
                .Append('}');
        }

        json.Append("],\"variablePredicates\":[");

        first = true;
        foreach(VariablePredicate predicate in context.VariablePredicates)
        {
            AppendSeparator(json, ref first);
            json.Append("{\"variable\":").Append(JsonString(predicate.Variable.Name.ToString()))
                .Append(",\"predicate\":").Append(JsonString(predicate.Predicate.ToString()))
                .Append(",\"position\":").Append(JsonString(predicate.Position.ToString()))
                .Append('}');
        }

        json.Append("]}");

        return json.ToString();
    }

    /// <summary>Appends a comma before every element after the first, then clears the first-element flag.</summary>
    /// <param name="json">The buffer being built.</param>
    /// <param name="first">Whether the next element is the first in its array.</param>
    private static void AppendSeparator(StringBuilder json, ref bool first)
    {
        if(!first)
        {
            json.Append(',');
        }

        first = false;
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
