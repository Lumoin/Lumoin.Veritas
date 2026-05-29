using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Parses a SHACL-SPARQL constraint node — the object of a shape's <c>sh:sparql</c> triple — into a
/// <see cref="SparqlConstraint"/>. Called once per <c>sh:sparql</c> occurrence during population, mirroring how
/// <see cref="ShapePathParser"/> resolves a <c>sh:path</c> sub-graph.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structure (SHACL-SPARQL §5).</b> The constraint node carries <c>sh:select</c> (the query text),
/// optionally <c>sh:message</c> (one per language tag), <c>sh:deactivated</c>, and <c>sh:prefixes</c>. Each
/// <c>sh:prefixes</c> object is a prefix-declaration subject whose <c>sh:declare</c> triples each name a
/// <c>sh:prefix</c>/<c>sh:namespace</c> pair (§5.2.1). The parser resolves the prefix bindings, prepends them as
/// <c>PREFIX</c> declarations, and parses + normalizes the resulting query once so evaluation re-uses the parsed
/// <see cref="SparqlQuery"/>.
/// </para>
/// <para>
/// <b>Slice scope.</b> Only the <c>sh:select</c> form is built. A constraint that is deactivated, has no
/// <c>sh:select</c> (e.g. an <c>sh:ask</c>-only validator, a later slice), or whose query does not parse cleanly
/// to a query (an update, or a recovered error tree) yields <see langword="null"/> — the constraint is not
/// enforced rather than throwing, so a shape graph the engine cannot yet handle under-validates instead of
/// aborting the whole load. The diagnostics path firms up when SPARQL-based constraint components land.
/// </para>
/// </remarks>
internal static class SparqlConstraintParser
{
    /// <summary>
    /// Parses the constraint node into a <see cref="SparqlConstraint"/>, or returns <see langword="null"/> when
    /// it is deactivated, carries no <c>sh:select</c>, or its query does not parse to a clean SELECT query.
    /// </summary>
    /// <param name="constraintNode">The object of the shape's <c>sh:sparql</c> triple.</param>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="dictionary">Term dictionary for decoding literals and IRIs.</param>
    /// <param name="ids">Pre-resolved SHACL vocabulary identifiers.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The parsed constraint, or <see langword="null"/> when it is not (yet) enforceable.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The interning pool's arena backs the Utf8Strings retained by the returned SparqlQuery; disposing it would invalidate them (and return still-referenced shared buffers). The pool is intentionally not disposed, mirroring SparqlParser.ParseRequest's whole-buffer facade — the retained slices root the underlying arrays.")]
    public static async Task<SparqlConstraint?> ParseAsync(
        TermId constraintNode,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        ShaclPopulationIds ids,
        CancellationToken cancellationToken)
    {
        Utf8String? selectText = null;
        bool deactivated = false;
        ImmutableDictionary<string, string>.Builder messages = ImmutableDictionary.CreateBuilder<string, string>();
        List<TermId> prefixSubjects = [];

        await foreach(EncodedTriple triple in shapeGraphMatch(constraintNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            IriId predicate = IriId.FromUnchecked(triple.Predicate);

            if(predicate.Equals(ids.Select) && dictionary.Resolve(triple.Object) is Literal selectLiteral)
            {
                selectText = selectLiteral.Value;
            }
            else if(predicate.Equals(ids.Deactivated) && dictionary.Resolve(triple.Object) is Literal deactivatedLiteral)
            {
                deactivated = IsTrue(deactivatedLiteral.Value);
            }
            else if(predicate.Equals(ids.Message) && dictionary.Resolve(triple.Object) is Literal messageLiteral)
            {
                string language = messageLiteral.Language is { } tag ? tag.ToString() : string.Empty;
                messages[language] = messageLiteral.Value.ToString();
            }
            else if(predicate.Equals(ids.Prefixes))
            {
                prefixSubjects.Add(triple.Object);
            }
        }

        if(deactivated || selectText is not Utf8String select)
        {
            return null;
        }

        List<(Utf8String Prefix, Utf8String Namespace)> declarations = await ResolvePrefixesAsync(
            prefixSubjects, shapeGraphMatch, dictionary, ids, cancellationToken).ConfigureAwait(false);

        Utf8StringPool pool = new();
        ParseResult<SparqlRequest> parseResult = SparqlParser.ParseRequest(BuildQuerySource(declarations, select), pool);
        if(parseResult.HasErrors)
        {
            return null;
        }

        if(parseResult.Tree is not SparqlQuery query)
        {
            return null;
        }

        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);

        return new SparqlConstraint(constraintNode, select, normalized, messages.ToImmutable());
    }

    /// <summary>
    /// Resolves every <c>sh:prefixes</c> subject's <c>sh:declare</c> namespace declarations into
    /// (prefix, namespace) pairs (SHACL-SPARQL §5.2.1). Declarations missing either component are skipped.
    /// </summary>
    /// <param name="prefixSubjects">The <c>sh:prefixes</c> objects (prefix-declaration subjects).</param>
    /// <param name="shapeGraphMatch">The shape-graph triple source.</param>
    /// <param name="dictionary">Term dictionary for decoding the prefix string and namespace IRI.</param>
    /// <param name="ids">Pre-resolved SHACL vocabulary identifiers.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The resolved namespace bindings, in discovery order.</returns>
    private static async Task<List<(Utf8String Prefix, Utf8String Namespace)>> ResolvePrefixesAsync(
        List<TermId> prefixSubjects,
        StorageDelegates.MatchTriplesAsync shapeGraphMatch,
        TermDictionary dictionary,
        ShaclPopulationIds ids,
        CancellationToken cancellationToken)
    {
        List<(Utf8String Prefix, Utf8String Namespace)> declarations = [];

        foreach(TermId prefixSubject in prefixSubjects)
        {
            List<TermId> declarationNodes = [];
            await foreach(EncodedTriple triple in shapeGraphMatch(prefixSubject, ids.Declare, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                declarationNodes.Add(triple.Object);
            }

            foreach(TermId declarationNode in declarationNodes)
            {
                Utf8String? prefix = null;
                Utf8String? namespaceIri = null;
                await foreach(EncodedTriple triple in shapeGraphMatch(declarationNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
                {
                    IriId predicate = IriId.FromUnchecked(triple.Predicate);
                    if(predicate.Equals(ids.Prefix) && dictionary.Resolve(triple.Object) is Literal prefixLiteral)
                    {
                        prefix = prefixLiteral.Value;
                    }
                    else if(predicate.Equals(ids.Namespace))
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

    /// <summary>
    /// Builds the query source bytes: each resolved binding as a <c>PREFIX p: &lt;ns&gt;</c> declaration, then
    /// the verbatim <c>sh:select</c> text. Assembled directly over UTF-8 bytes (no string round-trip).
    /// </summary>
    /// <param name="declarations">The resolved namespace bindings.</param>
    /// <param name="selectText">The verbatim <c>sh:select</c> query text.</param>
    /// <returns>The query source to parse.</returns>
    private static ReadOnlyMemory<byte> BuildQuerySource(
        List<(Utf8String Prefix, Utf8String Namespace)> declarations,
        Utf8String selectText)
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

        buffer.Write(selectText.Span);

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

    /// <summary>Returns whether an <c>xsd:boolean</c> lexical form is true (the lexical space is exactly <c>{true, false, 1, 0}</c>).</summary>
    /// <param name="lexical">The literal's lexical value.</param>
    /// <returns><see langword="true"/> when the value is <c>true</c> or <c>1</c>.</returns>
    private static bool IsTrue(Utf8String lexical) => lexical.Span.SequenceEqual("true"u8) || lexical.Span.SequenceEqual("1"u8);
}
