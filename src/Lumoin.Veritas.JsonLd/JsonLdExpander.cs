using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the JSON-LD 1.1 expansion algorithm and RDF extraction (ToRdf).
/// </summary>
/// <remarks>
/// <para>
/// Expansion converts a JSON-LD document from its compact, context-dependent form
/// into a fully expanded form where all terms are replaced by their absolute IRI
/// equivalents. After expansion, the document can be converted to RDF quads.
/// </para>
/// <para>
/// The implementation follows the JSON-LD 1.1 Processing Algorithms specification.
/// This is the non-streaming implementation: the full document is parsed into a
/// <see cref="JsonNode"/> tree, then processed to produce quads.
/// </para>
/// </remarks>
public static class JsonLdExpander
{
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string RdfLangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";
    private const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";
    private const string XsdDouble = "http://www.w3.org/2001/XMLSchema#double";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    private static long blankNodeCounter;

    /// <summary>
    /// Expands a JSON-LD document and extracts all RDF quads.
    /// </summary>
    /// <param name="document">
    /// The root <see cref="JsonNode"/> of the JSON-LD document. Callers obtain
    /// this by parsing UTF-8 JSON bytes with the parser provided by their chosen
    /// JSON adapter (e.g. <c>StjJsonAdapter.Parse</c> in the bundled adapter).
    /// </param>
    /// <param name="baseUrl">The base URL of the document.</param>
    /// <param name="resolver">The delegate for resolving remote contexts.</param>
    /// <param name="parser">
    /// The delegate for parsing remote-context bodies. The same adapter that
    /// produced <paramref name="document"/> typically supplies this.
    /// </param>
    /// <param name="pool">Pool for interning UTF-8 term strings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async enumerable of extracted quads.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows JSON-LD spec which uses string URIs throughout the processing algorithm.")]
    public static async IAsyncEnumerable<Quad> ExtractQuadsAsync(
        JsonNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        Utf8StringPool pool,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(pool);

        LinkedDataContext activeContext = LinkedDataContext.Empty;

        if(baseUrl is not null)
        {
            activeContext = activeContext.WithBaseIri(baseUrl);
        }

        //Process document-level @context if present.
        if(document.Kind == JsonNodeKind.Object
            && document.TryGetProperty(JsonLdKeywords.Context, out JsonNode contextElement))
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, contextElement, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        List<Quad> quads = [];
        await ExpandNodeAsync(document, activeContext, baseUrl, resolver, parser,
            graphName: null, quads, pool, cancellationToken).ConfigureAwait(false);

        foreach(Quad quad in quads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return quad;
        }
    }

    /// <summary>
    /// Expands a JSON-LD document and extracts all RDF quads, attaching
    /// per-quad provenance via <see cref="EmittedQuad"/>.
    /// </summary>
    /// <param name="document">
    /// The root <see cref="JsonNode"/> of the JSON-LD document. Callers obtain
    /// this by parsing UTF-8 JSON bytes with the parser provided by their chosen
    /// JSON adapter (e.g. <c>StjJsonAdapter.Parse</c> in the bundled adapter).
    /// </param>
    /// <param name="documentId">
    /// Identity of the parsed document. By convention this is the application's
    /// chosen <c>VeritasHash</c> applied to the document's canonical bytes,
    /// minted by the caller (the expander does not have the hash delegate in
    /// scope). The same <paramref name="documentId"/> is carried on every
    /// emitted quad's <see cref="EmittedQuad.Source"/>.
    /// </param>
    /// <param name="baseUrl">The base URL of the document.</param>
    /// <param name="resolver">The delegate for resolving remote contexts.</param>
    /// <param name="parser">
    /// The delegate for parsing remote-context bodies. The same adapter that
    /// produced <paramref name="document"/> typically supplies this.
    /// </param>
    /// <param name="pool">Pool for interning UTF-8 term strings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async enumerable of emitted quads, each paired with a
    /// <see cref="DocumentNodeRef"/> identifying its document and ordinal
    /// position in expansion order.</returns>
    /// <remarks>
    /// <para>
    /// Each emitted quad's <see cref="EmittedQuad.Source"/> carries a
    /// <see cref="DocumentNodeRef"/> whose
    /// <see cref="DocumentNodeRef.DocumentId"/> equals
    /// <paramref name="documentId"/> and whose
    /// <see cref="DocumentNodeRef.Index"/> is the zero-based position of the
    /// quad in expansion order. Indexes are assigned sequentially after the
    /// full expansion completes; the relative order matches the order in
    /// which the existing <see cref="ExtractQuadsAsync"/> overload would
    /// yield the same quads.
    /// </para>
    /// <para>
    /// <b>No <c>SourceSpan</c> at this layer.</b> The underlying
    /// <see cref="JsonNode"/> abstraction is parser-neutral and the
    /// bundled <c>StjJsonAdapter</c> uses
    /// <c>System.Text.Json.JsonElement</c>, which does not expose byte
    /// offsets, line numbers, or column numbers. Source attribution at the
    /// JSON-LD layer is therefore document-level only. A future span-aware
    /// JSON parser would let each emit site carry a populated
    /// <c>SourceSpan</c>; that is out of scope for this overload.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows JSON-LD spec which uses string URIs throughout the processing algorithm.")]
    public static async IAsyncEnumerable<EmittedQuad> ExtractQuadsWithSourceAsync(
        JsonNode document,
        DocumentId documentId,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        Utf8StringPool pool,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(pool);

        LinkedDataContext activeContext = LinkedDataContext.Empty;

        if(baseUrl is not null)
        {
            activeContext = activeContext.WithBaseIri(baseUrl);
        }

        //Process document-level @context if present.
        if(document.Kind == JsonNodeKind.Object
            && document.TryGetProperty(JsonLdKeywords.Context, out JsonNode contextElement))
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, contextElement, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        List<Quad> quads = [];
        await ExpandNodeAsync(document, activeContext, baseUrl, resolver, parser,
            graphName: null, quads, pool, cancellationToken).ConfigureAwait(false);

        int index = 0;
        foreach(Quad quad in quads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DocumentNodeRef nodeRef = new(documentId, index);
            EmittedQuad emitted = new(quad, nodeRef);
            index++;

            yield return emitted;
        }
    }

    private static async Task ExpandNodeAsync(
        JsonNode element,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken)
    {
        switch(element.Kind)
        {
            case JsonNodeKind.Array:
                foreach(JsonNode item in element.EnumerateArray())
                {
                    await ExpandNodeAsync(item, activeContext, baseUrl, resolver, parser,
                        graphName, quads, pool, cancellationToken).ConfigureAwait(false);
                }
                break;

            case JsonNodeKind.Object:
                await ExpandObjectAsync(element, activeContext, baseUrl, resolver, parser,
                    graphName, quads, pool, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static async Task ExpandObjectAsync(
        JsonNode obj,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken,
        RdfTerm? providedSubject = null)
    {
        //Process @context if present, updating the active context for this object.
        if(obj.TryGetProperty(JsonLdKeywords.Context, out JsonNode localContextElement))
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, localContextElement, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        //Determine the subject (@id), unless a subject was provided by the caller.
        RdfTerm subject = providedSubject ?? DetermineSubject(obj, activeContext, pool);

        //Process @type.
        if(obj.TryGetProperty(JsonLdKeywords.Type, out JsonNode typeElement))
        {
            ProcessTypeValues(typeElement, subject, activeContext, graphName, quads, pool);
        }

        //Process @graph: creates a named graph scope.
        if(obj.TryGetProperty(JsonLdKeywords.Graph, out JsonNode graphElement))
        {
            RdfTerm namedGraph = subject;
            await ExpandNodeAsync(graphElement, activeContext, baseUrl, resolver, parser,
                namedGraph, quads, pool, cancellationToken).ConfigureAwait(false);
        }

        //Process all other properties.
        foreach((string name, JsonNode propertyValue) in obj.EnumerateObject())
        {
            if(JsonLdKeywords.IsContext(name) || JsonLdKeywords.IsId(name) || JsonLdKeywords.IsType(name) || JsonLdKeywords.IsGraph(name))
            {
                continue;
            }

            await ExpandPropertyAsync(name, propertyValue, subject,
                activeContext, baseUrl, resolver, parser, graphName, quads, pool, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ExpandPropertyAsync(
        string propertyName,
        JsonNode value,
        RdfTerm subject,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken)
    {
        string? expandedProperty = activeContext.ExpandIri(propertyName, vocab: true);

        if(expandedProperty is null || (!expandedProperty.Contains(':', StringComparison.Ordinal) && !IriUtils.IsKeyword(expandedProperty)))
        {
            return;
        }

        //Skip JSON-LD keywords that are handled elsewhere.
        if(IriUtils.IsKeyword(expandedProperty))
        {
            return;
        }

        NamedNode predicate = new(pool.Intern(expandedProperty));

        //Property-scoped context: when the term carries pre-extracted
        //ScopedContextEntries, apply them on top of the active context
        //for the duration of this property's value subtree. Siblings see
        //the unchanged activeContext.
        LinkedDataContext propertyContext = activeContext;
        if(activeContext.TryGetTerm(propertyName, out TermDefinition? termDef)
            && termDef is { ScopedContextEntries: { } scopedEntries })
        {
            propertyContext = await JsonLdScopedContextHelper.ApplyAsync(
                activeContext, scopedEntries, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        //Expand the value based on the term's container and type mappings.
        await ExpandValueAsync(value, subject, predicate, propertyContext, baseUrl, resolver, parser,
            termDef, graphName, quads, pool, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExpandValueAsync(
        JsonNode value,
        RdfTerm subject,
        NamedNode predicate,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        TermDefinition? termDef,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken)
    {
        bool isList = termDef?.ContainerMapping.Contains(JsonLdKeywords.List) == true;

        //Handle array values.
        if(value.Kind == JsonNodeKind.Array)
        {
            if(isList)
            {
                await ExpandListAsync(value, subject, predicate, activeContext, baseUrl, resolver,
                    termDef, graphName, quads, pool, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                foreach(JsonNode item in value.EnumerateArray())
                {
                    await ExpandValueAsync(item, subject, predicate, activeContext, baseUrl, resolver, parser,
                        termDef, graphName, quads, pool, cancellationToken).ConfigureAwait(false);
                }
            }

            return;
        }

        //Handle @value objects.
        if(value.Kind == JsonNodeKind.Object && value.TryGetProperty(JsonLdKeywords.Value, out _))
        {
            RdfTerm? objectTerm = ProcessValueObject(value, activeContext, termDef, pool);
            if(objectTerm is not null)
            {
                EmitQuad(subject, predicate, objectTerm, graphName, quads);
            }

            return;
        }

        //Handle @id objects (node references or anonymous nodes).
        if(value.Kind == JsonNodeKind.Object && value.TryGetProperty(JsonLdKeywords.Id, out JsonNode refId))
        {
            string idStr = refId.Kind == JsonNodeKind.String ? refId.GetString() : "";
            string? expandedId = activeContext.ExpandIri(idStr, documentRelative: true);

            RdfTerm objectNode = expandedId is not null
                ? expandedId.StartsWith("_:", StringComparison.Ordinal)
                    ? new BlankNode(pool.Intern(expandedId[2..]))
                    : (RdfTerm)new NamedNode(pool.Intern(expandedId))
                : NewBlankNode(pool);

            EmitQuad(subject, predicate, objectNode, graphName, quads);

            //Recurse into the object to expand its properties using the same subject.
            await ExpandObjectAsync(value, activeContext, baseUrl, resolver, parser,
                graphName, quads, pool, cancellationToken, providedSubject: objectNode).ConfigureAwait(false);

            return;
        }

        //Recurse into nested objects.
        if(value.Kind == JsonNodeKind.Object)
        {
            BlankNode nestedNode = NewBlankNode(pool);
            EmitQuad(subject, predicate, nestedNode, graphName, quads);
            await ExpandObjectAsync(value, activeContext, baseUrl, resolver, parser,
                graphName, quads, pool, cancellationToken, providedSubject: nestedNode).ConfigureAwait(false);

            return;
        }

        //Scalar values.
        RdfTerm? scalar = ExpandScalarValue(value, activeContext, termDef, pool);
        if(scalar is not null)
        {
            EmitQuad(subject, predicate, scalar, graphName, quads);
        }
    }

    private static async Task ExpandListAsync(
        JsonNode array,
        RdfTerm subject,
        NamedNode predicate,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        TermDefinition? termDef,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken)
    {
        //The baseUrl, resolver, and cancellationToken parameters are accepted for
        //call-site uniformity with the rest of the expansion pipeline. List items
        //are processed as @value objects or scalars; neither path performs context
        //processing or remote I/O, so the parameters are not threaded further.
        _ = baseUrl;
        _ = resolver;
        cancellationToken.ThrowIfCancellationRequested();

        //Collect list items first, then build the linked list structure.
        List<RdfTerm> items = [];
        foreach(JsonNode item in array.EnumerateArray())
        {
            RdfTerm? objectTerm = null;

            if(item.Kind == JsonNodeKind.Object && item.TryGetProperty(JsonLdKeywords.Value, out _))
            {
                objectTerm = ProcessValueObject(item, activeContext, termDef, pool);
            }
            else if(item.Kind is not JsonNodeKind.Object)
            {
                objectTerm = ExpandScalarValue(item, activeContext, termDef, pool);
            }

            if(objectTerm is not null)
            {
                items.Add(objectTerm);
            }
        }

        //Build rdf:first/rdf:rest linked list.
        NamedNode rdfFirst = new(pool.Intern(RdfFirst));
        NamedNode rdfRest = new(pool.Intern(RdfRest));
        NamedNode rdfNil = new(pool.Intern(RdfNil));

        if(items.Count == 0)
        {
            EmitQuad(subject, predicate, rdfNil, graphName, quads);

            return;
        }

        BlankNode listHead = NewBlankNode(pool);
        EmitQuad(subject, predicate, listHead, graphName, quads);

        for(int i = 0; i < items.Count; i++)
        {
            EmitQuad(listHead, rdfFirst, items[i], graphName, quads);

            if(i < items.Count - 1)
            {
                BlankNode next = NewBlankNode(pool);
                EmitQuad(listHead, rdfRest, next, graphName, quads);
                listHead = next;
            }
            else
            {
                EmitQuad(listHead, rdfRest, rdfNil, graphName, quads);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static RdfTerm DetermineSubject(JsonNode obj, LinkedDataContext activeContext, Utf8StringPool pool)
    {
        if(obj.TryGetProperty(JsonLdKeywords.Id, out JsonNode idElement)
            && idElement.Kind == JsonNodeKind.String)
        {
            string idValue = idElement.GetString();
            string? expanded = activeContext.ExpandIri(idValue, documentRelative: true);

            if(expanded is not null)
            {
                if(expanded.StartsWith("_:", StringComparison.Ordinal))
                {
                    return new BlankNode(pool.Intern(expanded[2..]));
                }

                return new NamedNode(pool.Intern(expanded));
            }
        }

        return NewBlankNode(pool);
    }

    private static void ProcessTypeValues(
        JsonNode typeElement,
        RdfTerm subject,
        LinkedDataContext activeContext,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool)
    {
        NamedNode rdfType = new(pool.Intern(RdfType));

        void EmitType(string typeValue)
        {
            string? expanded = activeContext.ExpandIri(typeValue, vocab: true);
            if(expanded is null)
            {
                return;
            }

            NamedNode typeNode = new(pool.Intern(expanded));
            EmitQuad(subject, rdfType, typeNode, graphName, quads);
        }

        if(typeElement.Kind == JsonNodeKind.String)
        {
            EmitType(typeElement.GetString());
        }
        else if(typeElement.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode typeItem in typeElement.EnumerateArray())
            {
                if(typeItem.Kind == JsonNodeKind.String)
                {
                    EmitType(typeItem.GetString());
                }
            }
        }
    }

    private static Literal? ProcessValueObject(
        JsonNode valueObject,
        LinkedDataContext activeContext,
        TermDefinition? termDef,
        Utf8StringPool pool)
    {
        _ = activeContext;
        _ = termDef;

        if(!valueObject.TryGetProperty(JsonLdKeywords.Value, out JsonNode valueElement))
        {
            return null;
        }

        //Get the datatype.
        string? datatype = null;
        if(valueObject.TryGetProperty(JsonLdKeywords.Type, out JsonNode typeEl) && typeEl.Kind == JsonNodeKind.String)
        {
            datatype = typeEl.GetString();
        }

        //Get the language.
        string? language = null;
        if(valueObject.TryGetProperty(JsonLdKeywords.Language, out JsonNode langEl) && langEl.Kind == JsonNodeKind.String)
        {
            language = langEl.GetString();
        }

        //Convert the @value lexical form. Per JSON-LD 1.1, @value must be a
        //string, number, boolean, or null; an object or array at @value is a
        //spec violation and we surface it as InvalidValueObject.
        string valueStr = valueElement.Kind switch
        {
            JsonNodeKind.String => valueElement.GetString(),
            JsonNodeKind.True => "true",
            JsonNodeKind.False => "false",
            JsonNodeKind.Number => valueElement.GetRawNumber(),
            JsonNodeKind.Null => "null",
            _ => throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                $"@value must be a string, number, boolean, or null. Got '{valueElement.Kind}'.")
        };

        if(language is not null)
        {
            NamedNode langStringType = new(pool.Intern(RdfLangString));

            return new Literal(pool.Intern(valueStr), langStringType, pool.Intern(language));
        }

        if(datatype is not null)
        {
            NamedNode datatypeNode = new(pool.Intern(datatype));

            return new Literal(pool.Intern(valueStr), datatypeNode);
        }

        //Plain string.
        NamedNode xsdStr = new(pool.Intern(XsdString));

        return new Literal(pool.Intern(valueStr), xsdStr);
    }

    private static RdfTerm? ExpandScalarValue(
        JsonNode value,
        LinkedDataContext activeContext,
        TermDefinition? termDef,
        Utf8StringPool pool)
    {
        switch(value.Kind)
        {
            case JsonNodeKind.String:
            {
                string str = value.GetString();

                //Type coercion to @id.
                if(JsonLdKeywords.IsId(termDef?.TypeMapping) || JsonLdKeywords.IsVocab(termDef?.TypeMapping))
                {
                    bool useVocab = JsonLdKeywords.IsVocab(termDef?.TypeMapping);
                    string? expanded = activeContext.ExpandIri(str, vocab: useVocab, documentRelative: !useVocab);
                    if(expanded is null)
                    {
                        return null;
                    }

                    return expanded.StartsWith("_:", StringComparison.Ordinal)
                        ? new BlankNode(pool.Intern(expanded[2..]))
                        : new NamedNode(pool.Intern(expanded));
                }

                //Language tagging.
                string? lang = termDef?.HasLanguageMapping == true
                    ? termDef.LanguageMapping
                    : activeContext.DefaultLanguage;

                //Explicit datatype from term definition.
                string? datatypeIri = termDef?.TypeMapping;

                if(datatypeIri is not null
                    && !JsonLdKeywords.IsId(datatypeIri) && !JsonLdKeywords.IsVocab(datatypeIri) && !JsonLdKeywords.IsNone(datatypeIri))
                {
                    NamedNode datatypeNode = new(pool.Intern(datatypeIri));

                    return new Literal(pool.Intern(str), datatypeNode);
                }

                if(lang is not null)
                {
                    NamedNode langStringType = new(pool.Intern(RdfLangString));

                    return new Literal(pool.Intern(str), langStringType, pool.Intern(lang));
                }

                NamedNode xsdStr = new(pool.Intern(XsdString));

                return new Literal(pool.Intern(str), xsdStr);
            }

            case JsonNodeKind.True:
            {
                NamedNode boolType = new(pool.Intern(XsdBoolean));

                return new Literal(pool.Intern("true"), boolType);
            }

            case JsonNodeKind.False:
            {
                NamedNode boolType = new(pool.Intern(XsdBoolean));

                return new Literal(pool.Intern("false"), boolType);
            }

            case JsonNodeKind.Number:
            {
                string raw = value.GetRawNumber();
                string datatypeIri = raw.Contains('.', StringComparison.Ordinal) || raw.Contains('e', StringComparison.Ordinal) || raw.Contains('E', StringComparison.Ordinal)
                    ? XsdDouble
                    : XsdInteger;
                NamedNode numType = new(pool.Intern(datatypeIri));

                return new Literal(pool.Intern(raw), numType);
            }

            default:
                return null;
        }
    }

    private static void EmitQuad(
        RdfTerm subject,
        NamedNode predicate,
        RdfTerm obj,
        RdfTerm? graphName,
        List<Quad> quads)
    {
        quads.Add(new Quad(subject, predicate, obj, graphName));
    }

    private static BlankNode NewBlankNode(Utf8StringPool pool)
    {
        long id = Interlocked.Increment(ref blankNodeCounter);

        return new BlankNode(pool.Intern($"b{id}"));
    }
}
