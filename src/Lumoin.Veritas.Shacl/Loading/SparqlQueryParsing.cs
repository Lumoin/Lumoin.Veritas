using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Shared parsing of a SHACL-SPARQL query string (a <c>sh:select</c>/<c>sh:ask</c> body) with its
/// <c>sh:prefixes</c> namespace declarations: resolves the prefixes, prepends them as <c>PREFIX</c> lines, parses,
/// and normalizes. Used by both the <c>sh:sparql</c> constraint loader and the SPARQL-based constraint-component
/// loader.
/// </summary>
internal static class SparqlQueryParsing
{
    /// <summary>The <c>owl:imports</c> predicate IRI, followed transitively to gather imported prefix declarations (SHACL-SPARQL §5.2.1).</summary>
    private static Utf8String OwlImports { get; } = new("http://www.w3.org/2002/07/owl#imports"u8.ToArray());

    /// <summary>
    /// Resolves the <c>sh:prefixes</c> subjects' namespace declarations, prepends them to the query text, and
    /// parses + normalizes the result. Returns <see langword="null"/> when the text does not parse cleanly to a
    /// query (a recovered error tree, or an update request).
    /// </summary>
    /// <param name="queryText">The verbatim <c>sh:select</c>/<c>sh:ask</c> query text.</param>
    /// <param name="prefixSubjects">The <c>sh:prefixes</c> objects (prefix-declaration subjects).</param>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="dictionary">Term dictionary for decoding literals and IRIs.</param>
    /// <param name="declareId">The pre-resolved <c>sh:declare</c> predicate id.</param>
    /// <param name="prefixId">The pre-resolved <c>sh:prefix</c> predicate id.</param>
    /// <param name="namespaceId">The pre-resolved <c>sh:namespace</c> predicate id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The parsed and normalized query, or <see langword="null"/> when it does not parse to a query.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The interning pool's arena backs the Utf8Strings retained by the returned SparqlQuery; disposing it would invalidate them (and return still-referenced shared buffers). The pool is intentionally not disposed, mirroring SparqlParser.ParseRequest's whole-buffer facade — the retained slices root the underlying arrays.")]
    public static async Task<SparqlQuery?> ParseAsync(
        Utf8String queryText,
        IReadOnlyList<TermId> prefixSubjects,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        IriId declareId,
        IriId prefixId,
        IriId namespaceId,
        CancellationToken cancellationToken)
    {
        List<(Utf8String Prefix, Utf8String Namespace)> declarations = await ResolvePrefixesAsync(
            prefixSubjects, shapeGraphMatch, dictionary, declareId, prefixId, namespaceId, cancellationToken).ConfigureAwait(false);

        Utf8StringPool pool = new();
        ParseResult<SparqlRequest> parseResult = SparqlParser.ParseRequest(BuildQuerySource(declarations, queryText), pool);
        if(parseResult.HasErrors)
        {
            return null;
        }

        if(parseResult.Tree is not SparqlQuery query)
        {
            return null;
        }

        return (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);
    }

    /// <summary>Resolves every <c>sh:prefixes</c> subject's <c>sh:declare</c> namespace declarations into (prefix, namespace) pairs (SHACL-SPARQL §5.2.1).</summary>
    /// <param name="prefixSubjects">The <c>sh:prefixes</c> objects.</param>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="dictionary">Term dictionary for decoding.</param>
    /// <param name="declareId">The <c>sh:declare</c> predicate id.</param>
    /// <param name="prefixId">The <c>sh:prefix</c> predicate id.</param>
    /// <param name="namespaceId">The <c>sh:namespace</c> predicate id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The resolved namespace bindings, in discovery order.</returns>
    private static async Task<List<(Utf8String Prefix, Utf8String Namespace)>> ResolvePrefixesAsync(
        IReadOnlyList<TermId> prefixSubjects,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        IriId declareId,
        IriId prefixId,
        IriId namespaceId,
        CancellationToken cancellationToken)
    {
        List<(Utf8String Prefix, Utf8String Namespace)> declarations = [];

        //SHACL-SPARQL §5.2.1: a prefix-declaration subject may pull in further
        //declarations through owl:imports (transitively). Expand the subject set
        //by following owl:imports with an explicit work-list (no recursion),
        //guarding against import cycles with a visited set.
        TermId owlImportsId = dictionary.GetIdOrDefault(new NamedNode(OwlImports));
        HashSet<TermId> visited = [];
        Queue<TermId> pending = new();
        foreach(TermId prefixSubject in prefixSubjects)
        {
            if(visited.Add(prefixSubject))
            {
                pending.Enqueue(prefixSubject);
            }
        }

        while(pending.Count > 0)
        {
            TermId prefixSubject = pending.Dequeue();

            List<TermId> declarationNodes = [];
            await foreach(EncodedTriple triple in shapeGraphMatch(prefixSubject, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                IriId subjectPredicate = IriId.FromUnchecked(triple.Predicate);
                if(subjectPredicate.Equals(declareId))
                {
                    declarationNodes.Add(triple.Object);
                }
                else if(!owlImportsId.IsNone && triple.Predicate.Equals(owlImportsId) && visited.Add(triple.Object))
                {
                    pending.Enqueue(triple.Object);
                }
            }

            foreach(TermId declarationNode in declarationNodes)
            {
                Utf8String? prefix = null;
                Utf8String? namespaceIri = null;
                await foreach(EncodedTriple triple in shapeGraphMatch(declarationNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
                {
                    IriId predicate = IriId.FromUnchecked(triple.Predicate);
                    if(predicate.Equals(prefixId) && dictionary.Resolve(triple.Object) is Literal prefixLiteral)
                    {
                        prefix = prefixLiteral.Value;
                    }
                    else if(predicate.Equals(namespaceId))
                    {
                        namespaceIri = AsIriText(dictionary.Resolve(triple.Object));
                    }
                }

                if(prefix is Utf8String prefixText && namespaceIri is Utf8String namespaceText)
                {
                    declarations.Add((prefixText, namespaceText));
                }
            }
        }

        return declarations;
    }

    /// <summary>Builds the query source bytes: each resolved binding as a <c>PREFIX p: &lt;ns&gt;</c> line, then the verbatim query text (assembled over UTF-8 bytes, no string round-trip).</summary>
    /// <param name="declarations">The resolved namespace bindings.</param>
    /// <param name="queryText">The verbatim query text.</param>
    /// <returns>The query source to parse.</returns>
    private static ReadOnlyMemory<byte> BuildQuerySource(List<(Utf8String Prefix, Utf8String Namespace)> declarations, Utf8String queryText)
    {
        ArrayBufferWriter<byte> buffer = new();
        foreach((Utf8String prefix, Utf8String namespaceIri) in declarations)
        {
            buffer.Write("PREFIX "u8);
            buffer.Write(prefix.Span);
            buffer.Write(": <"u8);
            buffer.Write(namespaceIri.Span);
            buffer.Write(">\n"u8);
        }

        buffer.Write(queryText.Span);

        return buffer.WrittenMemory;
    }

    /// <summary>Returns the IRI text of a namespace term: a <see cref="NamedNode"/>'s IRI or a literal's lexical value (<c>sh:namespace</c> is an <c>xsd:anyURI</c> literal).</summary>
    /// <param name="term">The resolved <c>sh:namespace</c> object.</param>
    /// <returns>The IRI text, or <see langword="null"/> when the term is neither an IRI nor a literal.</returns>
    private static Utf8String? AsIriText(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => named.Iri,
            Literal literal => literal.Value,
            _ => null
        };
    }
}
