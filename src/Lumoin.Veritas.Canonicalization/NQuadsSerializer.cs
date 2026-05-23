using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Serializes RDF terms to canonical N-Quads form for use within the RDFC-1.0 algorithm.
/// </summary>
/// <remarks>
/// <para>
/// This is a self-contained serializer used internally by <see cref="RdfCanonicalizer"/>.
/// It operates on <see cref="string"/> rather than <see cref="Utf8String"/> because the
/// canonicalization algorithm works with string-form blank node identifiers that may be
/// rewritten during processing.
/// </para>
/// <para>
/// The output follows the canonical N-Triples / N-Quads rules: literal lexical forms use
/// the <c>ECHAR</c> named escapes for backspace, tab, line feed, form feed, carriage return,
/// double quote, and backslash; other characters below <c>U+0020</c> plus the delete
/// character use a <c>UCHAR</c> escape with uppercase hexadecimal digits; all other
/// characters (printable ASCII and every non-ASCII code point) pass through unescaped.
/// Language tags are lower-cased, the implicit <c>xsd:string</c> datatype is omitted, and
/// triple terms render in the <c>&lt;&lt;( s p o )&gt;&gt;</c> form with single-space separators.
/// </para>
/// </remarks>
internal static class NQuadsSerializer
{
    private const string XsdStringIri = "http://www.w3.org/2001/XMLSchema#string";

    //Named control characters. The BCL has no char constants for these (the runtime exposes
    //escape sequences in C# source — '\b', '\t', '\n', '\f', '\r' — but no named char values
    //for the backspace, vertical-tab, or delete code points), so the canonical escaping rules
    //are spelled out here against named constants rather than bare hexadecimal literals.
    private const char Backspace = '\b';
    private const char CharacterTabulation = '\t';
    private const char LineFeed = '\n';
    private const char FormFeed = '\f';
    private const char CarriageReturn = '\r';
    private const char Space = ' ';
    private const char Delete = '\u007F';

    /// <summary>
    /// Serializes a quad to its canonical N-Quads line, using the given blank node
    /// identifier mapping to replace blank node labels.
    /// </summary>
    /// <param name="quad">The quad to serialize.</param>
    /// <param name="blankNodeMap">
    /// Maps original blank node labels to their canonical identifiers.
    /// If a label is not in the map, the original label is used.
    /// </param>
    /// <returns>The canonical N-Quads line, including the trailing <c> .\n</c>.</returns>
    internal static string SerializeQuad(Quad quad, IReadOnlyDictionary<string, string> blankNodeMap)
    {
        StringBuilder sb = new();

        AppendTerm(sb, quad.Subject, blankNodeMap);
        sb.Append(' ');
        AppendNamedNode(sb, quad.Predicate);
        sb.Append(' ');
        AppendTerm(sb, quad.Object, blankNodeMap);

        if(quad.Graph is { } graph)
        {
            sb.Append(' ');
            AppendTerm(sb, graph, blankNodeMap);
        }

        sb.Append(" .\n");
        return sb.ToString();
    }

    /// <summary>
    /// Serializes a single quad term for hashing purposes, applying blank node relabelling.
    /// </summary>
    internal static string SerializeTerm(RdfTerm term, IReadOnlyDictionary<string, string> blankNodeMap)
    {
        StringBuilder sb = new();
        AppendTerm(sb, term, blankNodeMap);
        return sb.ToString();
    }

    private static void AppendTerm(StringBuilder sb, RdfTerm term, IReadOnlyDictionary<string, string> blankNodeMap)
    {
        switch(term)
        {
            case NamedNode namedNode:
            {
                AppendNamedNode(sb, namedNode);

                break;
            }

            case BlankNode blankNode:
            {
                string label = blankNode.Label.ToString();
                string canonical = blankNodeMap.TryGetValue(label, out string? mapped) ? mapped : label;
                sb.Append("_:");
                sb.Append(canonical);

                break;
            }

            case Literal literal:
            {
                AppendLiteral(sb, literal);

                break;
            }

            case TripleTerm tripleTerm:
            {
                AppendTripleTerm(sb, tripleTerm, blankNodeMap);

                break;
            }

            default:
            {
                break;
            }
        }
    }

    private static void AppendTripleTerm(StringBuilder sb, TripleTerm tripleTerm, IReadOnlyDictionary<string, string> blankNodeMap)
    {
        sb.Append("<<( ");
        AppendTerm(sb, tripleTerm.Subject, blankNodeMap);
        sb.Append(' ');
        AppendNamedNode(sb, tripleTerm.Predicate);
        sb.Append(' ');
        AppendTerm(sb, tripleTerm.Object, blankNodeMap);
        sb.Append(" )>>");
    }

    private static void AppendNamedNode(StringBuilder sb, NamedNode node)
    {
        sb.Append('<');
        AppendEscapedIri(sb, node.Iri.ToString());
        sb.Append('>');
    }

    private static void AppendLiteral(StringBuilder sb, Literal literal)
    {
        sb.Append('"');
        AppendCanonicalString(sb, literal.Value.ToString());
        sb.Append('"');

        if(literal.Language is { } lang)
        {
            sb.Append('@');
            AppendLowercaseAscii(sb, lang.ToString());

            if(literal.BaseDirection is { } direction)
            {
                sb.Append("--");
                sb.Append(direction == TextDirection.Ltr ? "ltr" : "rtl");
            }

            return;
        }

        string datatypeIri = literal.Datatype.Iri.ToString();
        if(string.Equals(datatypeIri, XsdStringIri, System.StringComparison.Ordinal))
        {
            //The implicit datatype of a plain literal is xsd:string; canonical form omits it.
            return;
        }

        sb.Append("^^<");
        AppendEscapedIri(sb, datatypeIri);
        sb.Append('>');
    }

    private static void AppendLowercaseAscii(StringBuilder sb, string value)
    {
        foreach(char c in value)
        {
            if(c is >= 'A' and <= 'Z')
            {
                sb.Append((char)(c + ('a' - 'A')));
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    private static void AppendEscapedIri(StringBuilder sb, string iri)
    {
        //IRIs in well-formed RDF rarely require escaping. Scan and escape only when needed.
        foreach(char c in iri)
        {
            if(c < 0x20 || c == '\\')
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"));
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    private static void AppendCanonicalString(StringBuilder sb, string value)
    {
        foreach(char c in value)
        {
            switch(c)
            {
                case Backspace:
                {
                    sb.Append("\\b");

                    break;
                }

                case CharacterTabulation:
                {
                    sb.Append("\\t");

                    break;
                }

                case LineFeed:
                {
                    sb.Append("\\n");

                    break;
                }

                case FormFeed:
                {
                    sb.Append("\\f");

                    break;
                }

                case CarriageReturn:
                {
                    sb.Append("\\r");

                    break;
                }

                case '"':
                {
                    sb.Append("\\\"");

                    break;
                }

                case '\\':
                {
                    sb.Append("\\\\");

                    break;
                }

                default:
                {
                    if(RequiresUnicodeEscape(c))
                    {
                        sb.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
                }
            }
        }
    }

    private static bool RequiresUnicodeEscape(char c)
    {
        //The canonical form escapes every C0 control (below U+0020) and the DEL character via UCHAR
        //when no shorter ECHAR named escape applies. C1 controls (U+0080..U+009F) are not escaped —
        //they pass through as their UTF-8 encoding — so char.IsControl, which also matches the C1
        //range, would be too broad here.
        return c < Space || c == Delete;
    }
}
