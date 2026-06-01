using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Sparql.Results;

/// <summary>
/// Reads the SPARQL Query Results TSV serialization
/// (<see href="https://www.w3.org/TR/sparql11-results-csv-tsv/">SPARQL 1.1 Query Results CSV and TSV Formats</see>)
/// into a <see cref="SparqlResultSet"/>. Unlike CSV, TSV is round-trippable: each field is an RDF term in Turtle term
/// syntax, so it parses back to typed terms. The companion CSV format is lossy and has no reader.
/// </summary>
/// <remarks>
/// The header line names the variables (each <c>?</c>-prefixed), tab-separated; each subsequent line is one solution
/// whose tab-separated fields are Turtle terms, an empty field denoting an unbound variable. Raw tabs and newlines
/// never appear inside a value (they are escaped), so the line and field splits are unambiguous.
/// </remarks>
public static class SparqlResultsTsvReader
{
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    private static NamedNode XsdInteger { get; } = new(Vocabulary.Xsd.Integer);

    private static NamedNode XsdDecimal { get; } = new(Vocabulary.Xsd.Decimal);

    private static NamedNode XsdDouble { get; } = new(Vocabulary.Xsd.Double);

    private static NamedNode XsdBoolean { get; } = new(Vocabulary.Xsd.Boolean);

    /// <summary>Reads a TSV results document into a <see cref="SparqlResultSet"/>.</summary>
    /// <param name="bytes">The UTF-8 TSV document.</param>
    /// <returns>The parsed SELECT result set.</returns>
    /// <exception cref="FormatException">The document has no header line.</exception>
    public static SparqlResultSet Read(ReadOnlyMemory<byte> bytes)
    {
        string text = Encoding.UTF8.GetString(bytes.Span).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        string[] lines = text.Split('\n');
        if(lines.Length == 0 || lines[0].Length == 0 && lines.Length == 1)
        {
            throw new FormatException("A SPARQL TSV results document must begin with a header line of variable names.");
        }

        List<Utf8String> variables = [];
        foreach(string column in lines[0].Split('\t'))
        {
            string name = column.StartsWith('?') ? column[1..] : column;
            variables.Add(Utf8String.FromString(name));
        }

        List<SparqlSolution> solutions = [];
        for(int i = 1; i < lines.Length; i++)
        {
            //A trailing terminator leaves a final empty element; it is not a solution row.
            if(i == lines.Length - 1 && lines[i].Length == 0)
            {
                continue;
            }

            string[] fields = lines[i].Split('\t');
            List<SparqlBinding> bindings = [];
            for(int column = 0; column < fields.Length && column < variables.Count; column++)
            {
                if(fields[column].Length == 0)
                {
                    continue;
                }

                bindings.Add(new SparqlBinding(new SparqlVariable(variables[column]), ParseTerm(fields[column])));
            }

            solutions.Add(new SparqlSolution(bindings));
        }

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Parses one Turtle term field into an RDF term.</summary>
    /// <param name="field">The non-empty field text.</param>
    /// <returns>The parsed term.</returns>
    private static RdfTerm ParseTerm(string field)
    {
        return field switch
        {
            ['<', ..] when field.EndsWith('>') => new NamedNode(Utf8String.FromString(field[1..^1])),
            ['_', ':', ..] => new BlankNode(Utf8String.FromString(field[2..])),
            ['"', ..] => ParseQuotedLiteral(field),
            "true" or "false" => new Literal(Utf8String.FromString(field), XsdBoolean),
            _ => ParseNumericOrPlain(field)
        };
    }

    /// <summary>Parses a double-quoted literal with its optional <c>@lang</c>(<c>--dir</c>) or <c>^^&lt;datatype&gt;</c> suffix.</summary>
    /// <param name="field">The field beginning with a double quote.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseQuotedLiteral(string field)
    {
        StringBuilder value = new(field.Length);
        int index = 1;
        while(index < field.Length)
        {
            char character = field[index];
            if(character == '"')
            {
                index++;
                break;
            }

            if(character == '\\' && index + 1 < field.Length)
            {
                value.Append(Unescape(field[index + 1]));
                index += 2;

                continue;
            }

            value.Append(character);
            index++;
        }

        Utf8String lexical = Utf8String.FromString(value.ToString());
        string suffix = field[index..];

        if(suffix.StartsWith('@'))
        {
            return LanguageTaggedLiteral(lexical, suffix[1..]);
        }

        if(suffix.StartsWith("^^<", StringComparison.Ordinal) && suffix.EndsWith('>'))
        {
            return new Literal(lexical, new NamedNode(Utf8String.FromString(suffix[3..^1])));
        }

        return new Literal(lexical, XsdString);
    }

    /// <summary>Builds a language-tagged literal, honouring an RDF 1.2 base-direction suffix (<c>--ltr</c>/<c>--rtl</c>).</summary>
    /// <param name="lexical">The literal's lexical value.</param>
    /// <param name="tag">The language tag, possibly with a <c>--direction</c> suffix.</param>
    /// <returns>The (directional) language-tagged literal.</returns>
    private static Literal LanguageTaggedLiteral(Utf8String lexical, string tag)
    {
        int separator = tag.IndexOf("--", StringComparison.Ordinal);
        if(separator >= 0 && TextDirections.TryParse(tag[(separator + 2)..], out TextDirection direction))
        {
            return new Literal(lexical, new NamedNode(Vocabulary.Rdf.DirLangString), Utf8String.FromString(tag[..separator]), direction);
        }

        return new Literal(lexical, new NamedNode(Vocabulary.Rdf.LangString), Utf8String.FromString(tag));
    }

    /// <summary>Maps an unrecognised bare token to a numeric typed literal (integer/decimal/double) or, failing that, a plain string literal.</summary>
    /// <param name="field">The bare field text.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseNumericOrPlain(string field)
    {
        NumericShape shape = ClassifyNumeric(field);

        return shape switch
        {
            NumericShape.Integer => new Literal(Utf8String.FromString(field), XsdInteger),
            NumericShape.Decimal => new Literal(Utf8String.FromString(field), XsdDecimal),
            NumericShape.Double => new Literal(Utf8String.FromString(field), XsdDouble),
            _ => new Literal(Utf8String.FromString(field), XsdString)
        };
    }

    /// <summary>Classifies a bare token as a Turtle integer, decimal, double, or none of these.</summary>
    /// <param name="field">The token.</param>
    /// <returns>The numeric shape.</returns>
    private static NumericShape ClassifyNumeric(string field)
    {
        int index = 0;
        if(index < field.Length && (field[index] == '+' || field[index] == '-'))
        {
            index++;
        }

        bool digitsBeforePoint = ConsumeDigits(field, ref index);
        bool point = index < field.Length && field[index] == '.';
        bool digitsAfterPoint = false;
        if(point)
        {
            index++;
            digitsAfterPoint = ConsumeDigits(field, ref index);
        }

        if(!digitsBeforePoint && !digitsAfterPoint)
        {
            return NumericShape.None;
        }

        bool exponent = false;
        if(index < field.Length && (field[index] == 'e' || field[index] == 'E'))
        {
            exponent = true;
            index++;
            if(index < field.Length && (field[index] == '+' || field[index] == '-'))
            {
                index++;
            }

            if(!ConsumeDigits(field, ref index))
            {
                return NumericShape.None;
            }
        }

        if(index != field.Length)
        {
            return NumericShape.None;
        }

        return (point || exponent) switch
        {
            false => NumericShape.Integer,
            true when exponent => NumericShape.Double,
            _ => NumericShape.Decimal
        };
    }

    /// <summary>Advances past a run of ASCII digits, reporting whether any were consumed.</summary>
    /// <param name="field">The token.</param>
    /// <param name="index">The position, advanced past the digits.</param>
    /// <returns><see langword="true"/> when at least one digit was consumed.</returns>
    private static bool ConsumeDigits(string field, ref int index)
    {
        int start = index;
        while(index < field.Length && field[index] is >= '0' and <= '9')
        {
            index++;
        }

        return index > start;
    }

    /// <summary>Maps a Turtle string escape character to its literal value.</summary>
    /// <param name="escape">The character following the backslash.</param>
    /// <returns>The unescaped character.</returns>
    private static char Unescape(char escape)
    {
        return escape switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'b' => '\b',
            'f' => '\f',
            _ => escape
        };
    }

    /// <summary>The numeric shape of a bare Turtle token.</summary>
    private enum NumericShape
    {
        /// <summary>Not a numeric literal.</summary>
        None,

        /// <summary>An <c>xsd:integer</c>.</summary>
        Integer,

        /// <summary>An <c>xsd:decimal</c>.</summary>
        Decimal,

        /// <summary>An <c>xsd:double</c>.</summary>
        Double
    }
}
