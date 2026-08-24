using System;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.Turtle.Completion;

/// <summary>
/// Projects a Turtle / TriG <see cref="CompletionContext"/> into the compact JSON an editor's completion popup
/// consumes: the caret byte offset, the expected next token kinds, and the enclosing productions (as their
/// names). The type is pure — it describes nothing and reads no buffer, so the caret transcode and the
/// <see cref="TurtleCompletion.Describe"/> call stay with the host that owns the editor's text. The syntax
/// token the editor sends over the same wire maps to the parser flavour through <see cref="ParseSyntax"/>, so
/// one vocabulary answers on every tier.
/// </summary>
public static class TurtleCompletionJson
{
    /// <summary>Serializes a Turtle / TriG completion context as the editor's completion JSON.</summary>
    /// <param name="context">The completion context.</param>
    /// <returns>The completion JSON.</returns>
    public static string Write(CompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        StringBuilder json = new();
        json.Append("{\"caret\":").Append(context.CaretByteOffset).Append(",\"expectedTokens\":[");

        bool first = true;
        foreach(TurtleTokenKind token in context.ExpectedTokens)
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

        json.Append("]}");

        return json.ToString();
    }

    /// <summary>Maps the editor's syntax token to the parser flavour; an unrecognised value falls back to Turtle.</summary>
    /// <param name="syntax">The syntax token: <c>trig</c> for TriG, otherwise Turtle.</param>
    /// <returns>The parser syntax flavour.</returns>
    public static TurtleSyntax ParseSyntax(string syntax)
    {
        return syntax switch
        {
            "trig" => TurtleSyntax.TriG,
            _ => TurtleSyntax.Turtle
        };
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
