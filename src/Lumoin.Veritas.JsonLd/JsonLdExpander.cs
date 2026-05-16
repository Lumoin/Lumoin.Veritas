using Lumoin.Veritas.Core;
using Lumoin.Veritas.JsonLd.Document;
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
/// <see cref="JsonLdNode"/> tree, then processed to produce quads.
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
    /// The root <see cref="JsonLdNode"/> of the JSON-LD document. Callers obtain
    /// this by parsing UTF-8 JSON bytes with the parser provided by their chosen
    /// JSON adapter (e.g. <c>StjJsonLdAdapter.Parse</c> in the bundled adapter).
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
        JsonLdNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
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
        if(document.Kind == JsonLdNodeKind.Object
            && document.TryGetProperty("@context", out JsonLdNode contextElement))
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

    private static async Task ExpandNodeAsync(
        JsonLdNode element,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken)
    {
        switch(element.Kind)
        {
            case JsonLdNodeKind.Array:
                foreach(JsonLdNode item in element.EnumerateArray())
                {
                    await ExpandNodeAsync(item, activeContext, baseUrl, resolver, parser,
                        graphName, quads, pool, cancellationToken).ConfigureAwait(false);
                }
                break;

            case JsonLdNodeKind.Object:
                await ExpandObjectAsync(element, activeContext, baseUrl, resolver, parser,
                    graphName, quads, pool, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static async Task ExpandObjectAsync(
        JsonLdNode obj,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken,
        RdfTerm? providedSubject = null)
    {
        //Process @context if present, updating the active context for this object.
        if(obj.TryGetProperty("@context", out JsonLdNode localContextElement))
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, localContextElement, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        //Determine the subject (@id), unless a subject was provided by the caller.
        RdfTerm subject = providedSubject ?? DetermineSubject(obj, activeContext, pool);

        //Process @type.
        if(obj.TryGetProperty("@type", out JsonLdNode typeElement))
        {
            ProcessTypeValues(typeElement, subject, activeContext, graphName, quads, pool);
        }

        //Process @graph: creates a named graph scope.
        if(obj.TryGetProperty("@graph", out JsonLdNode graphElement))
        {
            RdfTerm namedGraph = subject;
            await ExpandNodeAsync(graphElement, activeContext, baseUrl, resolver, parser,
                namedGraph, quads, pool, cancellationToken).ConfigureAwait(false);
        }

        //Process all other properties.
        foreach((string name, JsonLdNode propertyValue) in obj.EnumerateObject())
        {
            if(name is "@context" or "@id" or "@type" or "@graph")
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
        JsonLdNode value,
        RdfTerm subject,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
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
        JsonLdNode value,
        RdfTerm subject,
        NamedNode predicate,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        RdfTerm? graphName,
        List<Quad> quads,
        Utf8StringPool pool,
        CancellationToken cancellationToken)
    {
        bool isList = termDef?.ContainerMapping.Contains("@list") == true;

        //Handle array values.
        if(value.Kind == JsonLdNodeKind.Array)
        {
            if(isList)
            {
                await ExpandListAsync(value, subject, predicate, activeContext, baseUrl, resolver,
                    termDef, graphName, quads, pool, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                foreach(JsonLdNode item in value.EnumerateArray())
                {
                    await ExpandValueAsync(item, subject, predicate, activeContext, baseUrl, resolver, parser,
                        termDef, graphName, quads, pool, cancellationToken).ConfigureAwait(false);
                }
            }

            return;
        }

        //Handle @value objects.
        if(value.Kind == JsonLdNodeKind.Object && value.TryGetProperty("@value", out _))
        {
            RdfTerm? objectTerm = ProcessValueObject(value, activeContext, termDef, pool);
            if(objectTerm is not null)
            {
                EmitQuad(subject, predicate, objectTerm, graphName, quads);
            }

            return;
        }

        //Handle @id objects (node references or anonymous nodes).
        if(value.Kind == JsonLdNodeKind.Object && value.TryGetProperty("@id", out JsonLdNode refId))
        {
            string idStr = refId.Kind == JsonLdNodeKind.String ? refId.GetString() : "";
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
        if(value.Kind == JsonLdNodeKind.Object)
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
        JsonLdNode array,
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
        foreach(JsonLdNode item in array.EnumerateArray())
        {
            RdfTerm? objectTerm = null;

            if(item.Kind == JsonLdNodeKind.Object && item.TryGetProperty("@value", out _))
            {
                objectTerm = ProcessValueObject(item, activeContext, termDef, pool);
            }
            else if(item.Kind is not JsonLdNodeKind.Object)
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

    private static RdfTerm DetermineSubject(JsonLdNode obj, LinkedDataContext activeContext, Utf8StringPool pool)
    {
        if(obj.TryGetProperty("@id", out JsonLdNode idElement)
            && idElement.Kind == JsonLdNodeKind.String)
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
        JsonLdNode typeElement,
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

        if(typeElement.Kind == JsonLdNodeKind.String)
        {
            EmitType(typeElement.GetString());
        }
        else if(typeElement.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode typeItem in typeElement.EnumerateArray())
            {
                if(typeItem.Kind == JsonLdNodeKind.String)
                {
                    EmitType(typeItem.GetString());
                }
            }
        }
    }

    private static Literal? ProcessValueObject(
        JsonLdNode valueObject,
        LinkedDataContext activeContext,
        TermDefinition? termDef,
        Utf8StringPool pool)
    {
        _ = activeContext;
        _ = termDef;

        if(!valueObject.TryGetProperty("@value", out JsonLdNode valueElement))
        {
            return null;
        }

        //Get the datatype.
        string? datatype = null;
        if(valueObject.TryGetProperty("@type", out JsonLdNode typeEl) && typeEl.Kind == JsonLdNodeKind.String)
        {
            datatype = typeEl.GetString();
        }

        //Get the language.
        string? language = null;
        if(valueObject.TryGetProperty("@language", out JsonLdNode langEl) && langEl.Kind == JsonLdNodeKind.String)
        {
            language = langEl.GetString();
        }

        //Convert the @value lexical form. Per JSON-LD 1.1, @value must be a
        //string, number, boolean, or null; an object or array at @value is a
        //spec violation and we surface it as InvalidValueObject.
        string valueStr = valueElement.Kind switch
        {
            JsonLdNodeKind.String => valueElement.GetString(),
            JsonLdNodeKind.True => "true",
            JsonLdNodeKind.False => "false",
            JsonLdNodeKind.Number => valueElement.GetRawNumber(),
            JsonLdNodeKind.Null => "null",
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
        JsonLdNode value,
        LinkedDataContext activeContext,
        TermDefinition? termDef,
        Utf8StringPool pool)
    {
        switch(value.Kind)
        {
            case JsonLdNodeKind.String:
            {
                string str = value.GetString();

                //Type coercion to @id.
                if(termDef?.TypeMapping is "@id" or "@vocab")
                {
                    bool useVocab = termDef.TypeMapping == "@vocab";
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

                if(datatypeIri is not null && datatypeIri is not "@id" and not "@vocab" and not "@none")
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

            case JsonLdNodeKind.True:
            {
                NamedNode boolType = new(pool.Intern(XsdBoolean));

                return new Literal(pool.Intern("true"), boolType);
            }

            case JsonLdNodeKind.False:
            {
                NamedNode boolType = new(pool.Intern(XsdBoolean));

                return new Literal(pool.Intern("false"), boolType);
            }

            case JsonLdNodeKind.Number:
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
