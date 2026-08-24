using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.Sparql.Results;

/// <summary>
/// Renders RDF terms to the textual term syntax the SPARQL Query Results CSV and TSV serializations use
/// (<see href="https://www.w3.org/TR/sparql11-results-csv-tsv/">SPARQL 1.1 Query Results CSV and TSV Formats</see>).
/// TSV encodes each value in Turtle term syntax (round-trippable); CSV encodes a lossy plain-text rendering.
/// </summary>
/// <remarks>
/// Both renderings descend into RDF 1.2 triple terms over an explicit post-order stack (no recursion).
/// </remarks>
internal static class SparqlResultTermText
{
    private static NamedNode XsdString { get; } = new(Vocabulary.Xsd.String);

    /// <summary>Renders a term in Turtle term syntax for TSV: <c>&lt;iri&gt;</c>, <c>"lex"</c>/<c>"lex"@lang</c>/<c>"lex"^^&lt;dt&gt;</c>, <c>_:label</c>, or <c>&lt;&lt; s p o &gt;&gt;</c>.</summary>
    /// <param name="term">The term to render.</param>
    /// <returns>The Turtle term text.</returns>
    public static string Turtle(RdfTerm term)
    {
        return Render(term, csv: false);
    }

    /// <summary>Renders a term in the lossy CSV plain-text form: a bare IRI, a literal's bare lexical value, <c>_:label</c>, or (best-effort) the Turtle form for a triple term.</summary>
    /// <param name="term">The term to render.</param>
    /// <returns>The CSV cell text (before CSV quoting).</returns>
    public static string Csv(RdfTerm term)
    {
        return Render(term, csv: true);
    }

    /// <summary>Renders a term over an explicit post-order stack, in either CSV (lossy, bare) or TSV (Turtle) form.</summary>
    /// <param name="root">The term to render.</param>
    /// <param name="csv">Whether to render the lossy CSV leaf form (bare IRIs/literals) rather than the Turtle form.</param>
    /// <returns>The rendered term.</returns>
    private static string Render(RdfTerm root, bool csv)
    {
        Dictionary<RdfTerm, string> rendered = new(ReferenceEqualityComparer.Instance);
        Stack<(RdfTerm Node, bool Combine, int Depth)> work = new();
        work.Push((root, Combine: false, Depth: 1));

        while(work.Count > 0)
        {
            (RdfTerm node, bool combine, int depth) = work.Pop();
            if(node is TripleTerm triple)
            {
                if(combine)
                {
                    //A triple term has no lossy CSV form, so both modes render it in Turtle term syntax.
                    rendered[node] = "<< " + rendered[triple.Subject] + " " + rendered[triple.Predicate] + " " + rendered[triple.Object] + " >>";
                }
                else
                {
                    if(depth > QuotedTripleLimits.MaxNestingDepth)
                    {
                        throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                    }

                    work.Push((node, Combine: true, depth));
                    work.Push((triple.Object, Combine: false, depth + 1));
                    work.Push((triple.Predicate, Combine: false, depth + 1));
                    work.Push((triple.Subject, Combine: false, depth + 1));
                }
            }
            else
            {
                rendered[node] = csv ? RenderCsvLeaf(node) : RenderTurtleLeaf(node);
            }
        }

        return rendered[root];
    }

    /// <summary>Renders a leaf term in Turtle term syntax.</summary>
    /// <param name="term">The leaf term.</param>
    /// <returns>The Turtle text.</returns>
    private static string RenderTurtleLeaf(RdfTerm term)
    {
        switch(term)
        {
            case NamedNode named:
            {
                return "<" + named.Iri.ToString() + ">";
            }
            case BlankNode blank:
            {
                return "_:" + blank.Label.ToString();
            }
            case Literal literal when literal.Language is { } language:
            {
                return "\"" + EscapeString(literal.Value.ToString()) + "\"@" + language.ToString();
            }
            case Literal literal when literal.Datatype.Iri.Equals(XsdString.Iri):
            {
                return "\"" + EscapeString(literal.Value.ToString()) + "\"";
            }
            case Literal literal:
            {
                return "\"" + EscapeString(literal.Value.ToString()) + "\"^^<" + literal.Datatype.Iri.ToString() + ">";
            }
            case EngineNode engine:
            {
                //An engine-minted node renders as its deterministic Skolem IRI; the text re-parses as an
                //ordinary IRI, never back into an engine mint.
                return "<" + engine.SkolemIri().ToString() + ">";
            }
            default:
            {
                return term.ToString() ?? string.Empty;
            }
        }
    }

    /// <summary>Renders a leaf term in the lossy CSV form (bare IRI, bare lexical value, or <c>_:label</c>).</summary>
    /// <param name="term">The leaf term.</param>
    /// <returns>The CSV cell text.</returns>
    private static string RenderCsvLeaf(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => named.Iri.ToString(),
            BlankNode blank => "_:" + blank.Label.ToString(),
            Literal literal => literal.Value.ToString(),
            EngineNode engine => engine.SkolemIri().ToString(),
            _ => term.ToString() ?? string.Empty
        };
    }

    /// <summary>Escapes a literal's lexical value for a Turtle double-quoted string (backslash, quote, and the control characters that cannot appear raw in a TSV cell).</summary>
    /// <param name="value">The lexical value.</param>
    /// <returns>The escaped value.</returns>
    private static string EscapeString(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach(char character in value)
        {
            switch(character)
            {
                case '\\':
                {
                    builder.Append("\\\\");
                    break;
                }
                case '"':
                {
                    builder.Append("\\\"");
                    break;
                }
                case '\n':
                {
                    builder.Append("\\n");
                    break;
                }
                case '\r':
                {
                    builder.Append("\\r");
                    break;
                }
                case '\t':
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
