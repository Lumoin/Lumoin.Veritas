using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;

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
        //A quoted triple is the only nesting term; everything else is a leaf appended directly. Walking the
        //triple over an explicit stack keeps deep nesting off the call stack, and the depth guard turns a
        //pathological term into a catchable exception rather than unbounded growth.
        if(term is not TripleTerm root)
        {
            AppendLeaf(sb, term, blankNodeMap);

            return;
        }

        Stack<TermStep> work = new();
        work.Push(new TermStep(StepKind.Term, root, null));
        int depth = 0;

        while(work.Count > 0)
        {
            TermStep step = work.Pop();
            switch(step.Kind)
            {
                case(StepKind.Term):
                {
                    if(step.Term is TripleTerm triple)
                    {
                        depth++;
                        if(depth > QuotedTripleLimits.MaxNestingDepth)
                        {
                            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                        }

                        //Push the components in reverse so they pop in serialization order:
                        //<<( subject SPACE predicate SPACE object )>>.
                        work.Push(TermStep.Close);
                        work.Push(new TermStep(StepKind.Term, triple.Object, null));
                        work.Push(TermStep.Space);
                        work.Push(new TermStep(StepKind.Predicate, null, triple.Predicate));
                        work.Push(TermStep.Space);
                        work.Push(new TermStep(StepKind.Term, triple.Subject, null));
                        work.Push(TermStep.Open);
                    }
                    else
                    {
                        AppendLeaf(sb, step.Term!, blankNodeMap);
                    }

                    break;
                }
                case(StepKind.Open):
                {
                    sb.Append(TripleTermOpen);
                    break;
                }
                case(StepKind.Space):
                {
                    sb.Append(' ');
                    break;
                }
                case(StepKind.Predicate):
                {
                    AppendNamedNode(sb, step.Predicate!);
                    break;
                }
                default:
                {
                    //StepKind.Close: the triple's components are appended; close it and unwind one nesting level.
                    sb.Append(TripleTermClose);
                    depth--;
                    break;
                }
            }
        }
    }

    private static void AppendLeaf(StringBuilder sb, RdfTerm term, IReadOnlyDictionary<string, string> blankNodeMap)
    {
        switch(term)
        {
            case(NamedNode namedNode):
            {
                AppendNamedNode(sb, namedNode);
                break;
            }
            case(BlankNode blankNode):
            {
                string label = blankNode.Label.ToString();
                string canonical = blankNodeMap.TryGetValue(label, out string? mapped) ? mapped : label;
                sb.Append("_:");
                sb.Append(canonical);
                break;
            }
            case(Literal literal):
            {
                AppendLiteral(sb, literal);
                break;
            }
            case(EngineNode engine):
            {
                //An engine-minted node canonicalizes as its deterministic Skolem IRI: its identity is
                //content-fixed, so it participates like a named node, never in blank-node relabeling.
                sb.Append('<');
                sb.Append(engine.SkolemIri().ToString());
                sb.Append('>');
                break;
            }
            default:
            {
                //An unknown term kind appends nothing, preserving the original serializer's behaviour.
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
        AppendCanonicalString(sb, literal.Value.ToString());
        sb.Append('"');

        if(literal.Language is { } lang)
        {
            sb.Append('@');
            AppendLowercaseAscii(sb, lang.ToString());

            if(literal.BaseDirection is { } direction)
            {
                sb.Append("--");
                sb.Append(TextDirections.ToText(direction));
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
        //Iterate by Unicode scalar value so noncharacters above the basic multilingual plane,
        //which are surrogate pairs in UTF-16, are recognised as single code points.
        foreach(System.Text.Rune rune in value.EnumerateRunes())
        {
            int codePoint = rune.Value;
            switch(codePoint)
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
                    AppendCodePoint(sb, codePoint);

                    break;
                }
            }
        }
    }

    private static void AppendCodePoint(StringBuilder sb, int codePoint)
    {
        if(!RequiresUnicodeEscape(codePoint))
        {
            sb.Append(new System.Text.Rune(codePoint).ToString());

            return;
        }

        //Code points within the basic multilingual plane use the four-digit \u form; those above
        //it use the eight-digit \U form. Hexadecimal digits are uppercase.
        if(codePoint <= 0xFFFF)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{codePoint:X4}"));

            return;
        }

        sb.Append(string.Create(CultureInfo.InvariantCulture, $"\\U{codePoint:X8}"));
    }

    private static bool RequiresUnicodeEscape(int codePoint)
    {
        //The canonical form escapes every C0 control (below U+0020), the DEL character, and the
        //Unicode noncharacters via UCHAR when no shorter ECHAR named escape applies. C1 controls
        //(U+0080..U+009F) are not escaped — they pass through as their UTF-8 encoding — so
        //char.IsControl, which also matches the C1 range, would be too broad here.
        return codePoint < UnicodeConstants.FirstNonControlCodePoint
            || codePoint == UnicodeConstants.Delete
            || UnicodeConstants.IsNoncharacter(codePoint);
    }

    /// <summary>The RDF-star quoted-triple opening delimiter <c>&lt;&lt;( </c>.</summary>
    private const string TripleTermOpen = "<<( ";

    /// <summary>The RDF-star quoted-triple closing delimiter <c> )&gt;&gt;</c>.</summary>
    private const string TripleTermClose = " )>>";

    /// <summary>The kind of step on the quoted-triple serialization work-stack.</summary>
    private enum StepKind
    {
        /// <summary>Visit a term: append a leaf directly, or expand a quoted triple into its component steps.</summary>
        Term,

        /// <summary>Append the quoted-triple opening delimiter.</summary>
        Open,

        /// <summary>Append a single component separator.</summary>
        Space,

        /// <summary>Append a quoted triple's predicate, which is always a named node.</summary>
        Predicate,

        /// <summary>Append the quoted-triple closing delimiter and unwind one nesting level.</summary>
        Close
    }

    /// <summary>One step on the quoted-triple serialization work-stack.</summary>
    /// <param name="Kind">The step kind.</param>
    /// <param name="Term">The term to visit; set only for a <c>Term</c> step.</param>
    /// <param name="Predicate">The predicate to append; set only for a <c>Predicate</c> step.</param>
    private readonly record struct TermStep(StepKind Kind, RdfTerm? Term, NamedNode? Predicate)
    {
        /// <summary>The payload-less step that appends the quoted-triple opening delimiter.</summary>
        public static TermStep Open { get; } = new(StepKind.Open, null, null);

        /// <summary>The payload-less step that appends a single component separator.</summary>
        public static TermStep Space { get; } = new(StepKind.Space, null, null);

        /// <summary>The payload-less step that appends the quoted-triple closing delimiter and unwinds one nesting level.</summary>
        public static TermStep Close { get; } = new(StepKind.Close, null, null);
    }
}
