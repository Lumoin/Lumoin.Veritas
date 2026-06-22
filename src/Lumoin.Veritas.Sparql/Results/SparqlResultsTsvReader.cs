using System;
using System.Collections.Generic;
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
/// never appear inside a value (they are escaped), so the line and field splits are unambiguous. The reader is
/// byte-native: it scans the UTF-8 input directly and copies each term's bytes into an owned <see cref="Utf8String"/>,
/// so the result set is independent of the input buffer.
/// </remarks>
public static class SparqlResultsTsvReader
{
    /// <summary>The <c>xsd:string</c> datatype of a plain literal.</summary>
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    /// <summary>The <c>xsd:integer</c> datatype of a bare integer token.</summary>
    private static NamedNode XsdInteger { get; } = new(Vocabulary.Xsd.Integer);

    /// <summary>The <c>xsd:decimal</c> datatype of a bare decimal token.</summary>
    private static NamedNode XsdDecimal { get; } = new(Vocabulary.Xsd.Decimal);

    /// <summary>The <c>xsd:double</c> datatype of a bare double token.</summary>
    private static NamedNode XsdDouble { get; } = new(Vocabulary.Xsd.Double);

    /// <summary>The <c>xsd:boolean</c> datatype of the <c>true</c>/<c>false</c> tokens.</summary>
    private static NamedNode XsdBoolean { get; } = new(Vocabulary.Xsd.Boolean);

    /// <summary>Reads a TSV results document into a <see cref="SparqlResultSet"/>.</summary>
    /// <param name="bytes">The UTF-8 TSV document.</param>
    /// <returns>The parsed SELECT result set.</returns>
    /// <exception cref="FormatException">The document has no header line.</exception>
    public static SparqlResultSet Read(ReadOnlyMemory<byte> bytes)
    {
        List<ReadOnlyMemory<byte>> lines = SplitLines(bytes);
        if(lines.Count == 0 || (lines[0].Length == 0 && lines.Count == 1))
        {
            throw new FormatException("A SPARQL TSV results document must begin with a header line of variable names.");
        }

        List<Utf8String> variables = [];
        foreach(ReadOnlyMemory<byte> column in SplitFields(lines[0]))
        {
            ReadOnlySpan<byte> name = column.Span;
            ReadOnlyMemory<byte> bare = name.Length > 0 && name[0] == (byte)'?' ? column.Slice(1) : column;
            variables.Add(new Utf8String(bare.ToArray()));
        }

        List<SparqlSolution> solutions = [];
        for(int i = 1; i < lines.Count; i++)
        {
            //A trailing terminator leaves a final empty line; it is not a solution row.
            if(i == lines.Count - 1 && lines[i].Length == 0)
            {
                continue;
            }

            List<ReadOnlyMemory<byte>> fields = SplitFields(lines[i]);
            List<SparqlBinding> bindings = [];
            for(int column = 0; column < fields.Count && column < variables.Count; column++)
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

    /// <summary>Splits the document into lines on <c>\n</c>, treating a preceding or lone <c>\r</c> as the same boundary.</summary>
    /// <param name="bytes">The document bytes.</param>
    /// <returns>The line slices, in order; a trailing terminator yields a final empty slice.</returns>
    private static List<ReadOnlyMemory<byte>> SplitLines(ReadOnlyMemory<byte> bytes)
    {
        List<ReadOnlyMemory<byte>> lines = [];
        ReadOnlySpan<byte> span = bytes.Span;
        int start = 0;
        int i = 0;
        while(i < span.Length)
        {
            byte b = span[i];
            if(b == (byte)'\n')
            {
                lines.Add(bytes.Slice(start, i - start));
                i++;
                start = i;
            }
            else if(b == (byte)'\r')
            {
                lines.Add(bytes.Slice(start, i - start));
                i++;
                if(i < span.Length && span[i] == (byte)'\n')
                {
                    i++;
                }

                start = i;
            }
            else
            {
                i++;
            }
        }

        lines.Add(bytes.Slice(start, span.Length - start));

        return lines;
    }

    /// <summary>Splits one line into tab-separated fields.</summary>
    /// <param name="line">The line slice.</param>
    /// <returns>The field slices, in order.</returns>
    private static List<ReadOnlyMemory<byte>> SplitFields(ReadOnlyMemory<byte> line)
    {
        List<ReadOnlyMemory<byte>> fields = [];
        ReadOnlySpan<byte> span = line.Span;
        int start = 0;
        for(int i = 0; i < span.Length; i++)
        {
            if(span[i] == (byte)'\t')
            {
                fields.Add(line.Slice(start, i - start));
                start = i + 1;
            }
        }

        fields.Add(line.Slice(start, span.Length - start));

        return fields;
    }

    /// <summary>Parses one Turtle term field into an RDF term.</summary>
    /// <param name="field">The non-empty field slice.</param>
    /// <returns>The parsed term.</returns>
    private static RdfTerm ParseTerm(ReadOnlyMemory<byte> field)
    {
        ReadOnlySpan<byte> span = field.Span;

        return span switch
        {
            [(byte)'<', .., (byte)'>'] => new NamedNode(new Utf8String(field.Slice(1, field.Length - 2).ToArray())),
            [(byte)'_', (byte)':', ..] => new BlankNode(new Utf8String(field.Slice(2).ToArray())),
            [(byte)'"', ..] => ParseQuotedLiteral(field),
            _ when span.SequenceEqual("true"u8) || span.SequenceEqual("false"u8) => new Literal(new Utf8String(field.ToArray()), XsdBoolean),
            _ => ParseNumericOrPlain(field)
        };
    }

    /// <summary>Parses a double-quoted literal with its optional <c>@lang</c>(<c>--dir</c>) or <c>^^&lt;datatype&gt;</c> suffix.</summary>
    /// <param name="field">The field beginning with a double quote.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseQuotedLiteral(ReadOnlyMemory<byte> field)
    {
        ReadOnlySpan<byte> span = field.Span;

        //Unescaping only ever shortens a value, so the field length bounds the decoded byte count.
        byte[] buffer = new byte[span.Length];
        int written = 0;
        int index = 1;
        while(index < span.Length)
        {
            byte b = span[index];
            if(b == (byte)'"')
            {
                index++;
                break;
            }

            if(b == (byte)'\\' && index + 1 < span.Length)
            {
                buffer[written++] = Unescape(span[index + 1]);
                index += 2;

                continue;
            }

            buffer[written++] = b;
            index++;
        }

        Utf8String lexical = new(new ReadOnlyMemory<byte>(buffer, 0, written));

        return BuildLiteral(lexical, field.Slice(index));
    }

    /// <summary>Applies a quoted literal's suffix: a language tag, a datatype IRI, or none (a plain string).</summary>
    /// <param name="lexical">The decoded lexical value.</param>
    /// <param name="suffix">The bytes following the closing quote.</param>
    /// <returns>The literal term.</returns>
    private static Literal BuildLiteral(Utf8String lexical, ReadOnlyMemory<byte> suffix)
    {
        ReadOnlySpan<byte> span = suffix.Span;
        if(span.Length > 0 && span[0] == (byte)'@')
        {
            return LanguageTaggedLiteral(lexical, suffix.Slice(1));
        }

        if(span.Length >= 4 && span[0] == (byte)'^' && span[1] == (byte)'^' && span[2] == (byte)'<' && span[^1] == (byte)'>')
        {
            return new Literal(lexical, new NamedNode(new Utf8String(suffix.Slice(3, suffix.Length - 4).ToArray())));
        }

        return new Literal(lexical, XsdString);
    }

    /// <summary>Builds a language-tagged literal, honouring an RDF 1.2 base-direction suffix (<c>--ltr</c>/<c>--rtl</c>).</summary>
    /// <param name="lexical">The literal's lexical value.</param>
    /// <param name="tag">The language tag, possibly with a <c>--direction</c> suffix.</param>
    /// <returns>The (directional) language-tagged literal.</returns>
    private static Literal LanguageTaggedLiteral(Utf8String lexical, ReadOnlyMemory<byte> tag)
    {
        ReadOnlySpan<byte> span = tag.Span;
        int separator = span.IndexOf("--"u8);
        if(separator >= 0 && TextDirections.TryParse(span[(separator + 2)..], out TextDirection direction))
        {
            return new Literal(lexical, new NamedNode(Vocabulary.Rdf.DirLangString), new Utf8String(tag.Slice(0, separator).ToArray()), direction);
        }

        return new Literal(lexical, new NamedNode(Vocabulary.Rdf.LangString), new Utf8String(tag.ToArray()));
    }

    /// <summary>Maps an unrecognised bare token to a numeric typed literal (integer/decimal/double) or, failing that, a plain string literal.</summary>
    /// <param name="field">The bare field slice.</param>
    /// <returns>The literal term.</returns>
    private static Literal ParseNumericOrPlain(ReadOnlyMemory<byte> field)
    {
        return ClassifyNumeric(field.Span) switch
        {
            NumericShape.Integer => new Literal(new Utf8String(field.ToArray()), XsdInteger),
            NumericShape.Decimal => new Literal(new Utf8String(field.ToArray()), XsdDecimal),
            NumericShape.Double => new Literal(new Utf8String(field.ToArray()), XsdDouble),
            _ => new Literal(new Utf8String(field.ToArray()), XsdString)
        };
    }

    /// <summary>Classifies a bare token as a Turtle integer, decimal, double, or none of these.</summary>
    /// <param name="field">The token bytes.</param>
    /// <returns>The numeric shape.</returns>
    private static NumericShape ClassifyNumeric(ReadOnlySpan<byte> field)
    {
        int index = 0;
        if(index < field.Length && (field[index] == (byte)'+' || field[index] == (byte)'-'))
        {
            index++;
        }

        bool digitsBeforePoint = ConsumeDigits(field, ref index);
        bool point = index < field.Length && field[index] == (byte)'.';
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
        if(index < field.Length && (field[index] == (byte)'e' || field[index] == (byte)'E'))
        {
            exponent = true;
            index++;
            if(index < field.Length && (field[index] == (byte)'+' || field[index] == (byte)'-'))
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
    /// <param name="field">The token bytes.</param>
    /// <param name="index">The position, advanced past the digits.</param>
    /// <returns><see langword="true"/> when at least one digit was consumed.</returns>
    private static bool ConsumeDigits(ReadOnlySpan<byte> field, ref int index)
    {
        int start = index;
        while(index < field.Length && field[index] is >= (byte)'0' and <= (byte)'9')
        {
            index++;
        }

        return index > start;
    }

    /// <summary>Maps a Turtle string escape byte to its literal value.</summary>
    /// <param name="escape">The byte following the backslash.</param>
    /// <returns>The unescaped byte.</returns>
    private static byte Unescape(byte escape)
    {
        return escape switch
        {
            (byte)'n' => (byte)'\n',
            (byte)'r' => (byte)'\r',
            (byte)'t' => (byte)'\t',
            (byte)'b' => (byte)'\b',
            (byte)'f' => (byte)'\f',
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
