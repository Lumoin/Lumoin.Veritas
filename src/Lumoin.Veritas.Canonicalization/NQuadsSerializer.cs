using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Serializes RDF terms to N-Quads canonical form for use within the RDFC-1.0 algorithm.
/// </summary>
/// <remarks>
/// This is a self-contained serializer used internally by <see cref="RdfCanonicalizer"/>.
/// It operates on <see cref="string"/> rather than <see cref="Utf8String"/> because the
/// canonicalization algorithm works with string-form blank node identifiers that may be
/// rewritten during processing.
/// </remarks>
internal static class NQuadsSerializer
{
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
        }
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
        AppendEscapedString(sb, literal.Value.ToString());

        if(literal.Language is { } lang)
        {
            sb.Append("\"@");
            sb.Append(lang.ToString());
        }
        else
        {
            sb.Append("\"^^<");
            AppendEscapedIri(sb, literal.Datatype.Iri.ToString());
            sb.Append('>');
        }
    }

    private static void AppendEscapedIri(StringBuilder sb, string iri)
    {
        //IRIs in well-formed RDF rarely require escaping. Scan and escape only when needed.
        foreach(char c in iri)
        {
            if(c < 0x20 || c == '\\')
            {
                sb.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"));
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    private static void AppendEscapedString(StringBuilder sb, string value)
    {
        foreach(char c in value)
        {
            switch(c)
            {
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default: sb.Append(c); break;
            }
        }
    }
}