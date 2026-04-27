using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.JsonLd.Document;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the JSON-LD 1.1 context processing algorithm.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the "Context Processing Algorithm" from the
/// JSON-LD 1.1 Processing Algorithms and API specification.
/// </para>
/// <para>
/// The algorithm takes an active context and a local context value (which may be a string URI,
/// an object, an array, or null) and produces a new active context incorporating the local
/// context's definitions.
/// </para>
/// <para>
/// Remote contexts are fetched via the <see cref="ContextResolverDelegate"/> and parsed via
/// the <see cref="JsonLdParserDelegate"/>. All other operations are pure transforms over the
/// context data structures.
/// </para>
/// </remarks>
public static class ContextProcessor
{
    private const int MaxContextDepth = 50;

    /// <summary>
    /// Processes a JSON-LD <c>@context</c> value and returns the resulting active context.
    /// </summary>
    /// <param name="activeContext">The context active before processing begins.</param>
    /// <param name="localContext">
    /// The value of the <c>@context</c> entry. May be a string, object, array, or null
    /// represented as a <see cref="JsonLdNode"/>.
    /// </param>
    /// <param name="baseUrl">
    /// The base URL of the document being processed, used for resolving relative context URIs.
    /// </param>
    /// <param name="resolver">
    /// The delegate used to fetch remote context documents.
    /// </param>
    /// <param name="parser">
    /// The delegate used to parse fetched UTF-8 JSON bytes into a <see cref="JsonLdNode"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The active context after processing the local context.</returns>
    /// <exception cref="JsonLdProcessingException">
    /// The local context contained an invalid value or could not be processed.
    /// </exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows JSON-LD spec which uses string URIs throughout the processing algorithm.")]
    public static async ValueTask<JsonLdContext> ProcessAsync(
        JsonLdContext activeContext,
        JsonLdNode localContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(parser);

        return await ProcessAsync(
            activeContext,
            localContext,
            baseUrl,
            resolver,
            parser,
            remoteContexts: [],
            overrideProtected: false,
            propagate: true,
            depth: 0,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<JsonLdContext> ProcessAsync(
        JsonLdContext activeContext,
        JsonLdNode localContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        List<string> remoteContexts,
        bool overrideProtected,
        bool propagate,
        int depth,
        CancellationToken cancellationToken)
    {
        if(depth > MaxContextDepth)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.ContextOverflow,
                "Maximum context processing depth exceeded. A context may contain a recursive reference.");
        }

        //Step 1: initialise result as a copy of active context.
        JsonLdContext result = activeContext.Clone();

        //Step 2: if local context is not an array, wrap it in one for uniform processing.
        JsonLdNode[] contexts;
        if(localContext.Kind == JsonLdNodeKind.Array)
        {
            contexts = localContext.EnumerateArray().ToArray();
        }
        else
        {
            contexts = [localContext];
        }

        //Step 3: process each context in the array.
        foreach(JsonLdNode context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            result = context.Kind switch
            {
                //Null resets the active context.
                JsonLdNodeKind.Null => ProcessNullContext(result, overrideProtected),

                //String is a remote context URI.
                JsonLdNodeKind.String => await ProcessRemoteContextAsync(
                    result, context.GetString(), baseUrl, resolver, parser, remoteContexts,
                    overrideProtected, propagate, depth, cancellationToken).ConfigureAwait(false),

                //Object is an inline context definition.
                JsonLdNodeKind.Object => await ProcessInlineContextAsync(
                    result, context, baseUrl, resolver, parser, remoteContexts,
                    overrideProtected, propagate, depth, cancellationToken).ConfigureAwait(false),

                _ => throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidLocalContext,
                    $"A context must be a string, object, array, or null. Got '{context.Kind}'.")
            };
        }

        return result;
    }

    private static JsonLdContext ProcessNullContext(JsonLdContext result, bool overrideProtected)
    {
        //A null context resets to empty, but protected terms cannot be overridden.
        if(!overrideProtected)
        {
            foreach(string term in result.Terms)
            {
                if(result.TryGetTerm(term, out TermDefinition? def) && def?.Protected == true)
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidContextNullification,
                        $"Null context would override protected term '{term}'.");
                }
            }
        }

        JsonLdContext empty = JsonLdContext.Empty;

        //Preserve the previous context if propagate was false.
        if(!result.Propagate)
        {
            empty = empty.WithPreviousContext(result);
        }

        return empty;
    }

    /// <summary>
    /// Fetches a remote context document, parses it, and processes the embedded
    /// <c>@context</c> entry.
    /// </summary>
    /// <remarks>
    /// The catch-all on <see cref="Exception"/> is required because the parser
    /// delegate's failure type is unspecified — adapters may throw any exception
    /// type when parsing fails. The when-clause excludes
    /// <see cref="JsonLdProcessingException"/> so JSON-LD-specific errors are
    /// not silently re-wrapped as loading failures.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The parser delegate's failure type is unspecified by contract; the catch-all wraps any parse failure with a JSON-LD-specific error code.")]
    private static async ValueTask<JsonLdContext> ProcessRemoteContextAsync(
        JsonLdContext activeContext,
        string contextUri,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        List<string> remoteContexts,
        bool overrideProtected,
        bool propagate,
        int depth,
        CancellationToken cancellationToken)
    {
        //Resolve the URI against the base URL.
        string resolvedUri = baseUrl is not null
            ? IriUtils.ResolveIri(baseUrl, contextUri)
            : contextUri;

        //Check for recursive context loading.
        if(remoteContexts.Contains(resolvedUri, StringComparer.Ordinal))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.ContextOverflow,
                $"Recursive context reference detected for '{resolvedUri}'.");
        }

        remoteContexts.Add(resolvedUri);

        Utf8String? resolved = await resolver(new Uri(resolvedUri), cancellationToken).ConfigureAwait(false);
        if(resolved is null)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.LoadingRemoteContextFailed,
                $"Failed to load remote context from '{resolvedUri}'.");
        }

        JsonLdNode rootNode;
        try
        {
            rootNode = parser(resolved.Value);
        }
        catch(Exception ex) when(ex is not JsonLdProcessingException)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.LoadingRemoteContextFailed,
                $"Remote context at '{resolvedUri}' is not valid JSON.",
                ex);
        }

        if(!rootNode.TryGetProperty("@context", out JsonLdNode innerContext))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.LoadingRemoteContextFailed,
                $"Remote context document at '{resolvedUri}' has no '@context' property.");
        }

        return await ProcessAsync(
            activeContext,
            innerContext,
            resolvedUri,
            resolver,
            parser,
            remoteContexts,
            overrideProtected,
            propagate,
            depth + 1,
            cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Language tags must be lowercase per BCP47 and the JSON-LD 1.1 specification.")]
    private static async ValueTask<JsonLdContext> ProcessInlineContextAsync(
        JsonLdContext activeContext,
        JsonLdNode contextObject,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        List<string> remoteContexts,
        bool overrideProtected,
        bool propagate,
        int depth,
        CancellationToken cancellationToken)
    {
        JsonLdContext result = activeContext.Clone();

        //Handle @version: must be 1.1 if present.
        if(contextObject.TryGetProperty("@version", out JsonLdNode version))
        {
            if(version.Kind == JsonLdNodeKind.Number)
            {
                double versionNumber = double.Parse(version.GetRawNumber(), CultureInfo.InvariantCulture);
                if(Math.Abs(versionNumber - 1.1) > 0.001)
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidContextEntry,
                        $"Unsupported JSON-LD version: {versionNumber}. Only version 1.1 is supported.");
                }
            }
        }

        //Handle @import: load the referenced context and merge it first.
        if(contextObject.TryGetProperty("@import", out JsonLdNode importElement))
        {
            if(importElement.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidContextEntry,
                    "@import must be a string.");
            }

            result = await ProcessRemoteContextAsync(
                result, importElement.GetString(), baseUrl, resolver, parser, remoteContexts,
                overrideProtected, propagate, depth, cancellationToken).ConfigureAwait(false);
        }

        //Handle @base.
        if(contextObject.TryGetProperty("@base", out JsonLdNode baseElement))
        {
            if(baseElement.Kind == JsonLdNodeKind.Null)
            {
                result = result.WithBaseIri(null);
            }
            else if(baseElement.Kind == JsonLdNodeKind.String)
            {
                string baseValue = baseElement.GetString();
                if(IriUtils.IsAbsoluteIri(baseValue))
                {
                    result = result.WithBaseIri(baseValue);
                }
                else if(result.BaseIri is not null)
                {
                    result = result.WithBaseIri(IriUtils.ResolveIri(result.BaseIri, baseValue));
                }
                else
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidBaseIri,
                        $"@base value '{baseValue}' is relative but no base IRI is available to resolve against.");
                }
            }
            else
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidBaseIri,
                    "@base must be a string or null.");
            }
        }

        //Handle @vocab.
        if(contextObject.TryGetProperty("@vocab", out JsonLdNode vocabElement))
        {
            if(vocabElement.Kind == JsonLdNodeKind.Null)
            {
                result = result.WithVocabularyMapping(null);
            }
            else if(vocabElement.Kind == JsonLdNodeKind.String)
            {
                string vocabValue = vocabElement.GetString();

                if(IriUtils.IsAbsoluteIri(vocabValue) || vocabValue.StartsWith('_'))
                {
                    result = result.WithVocabularyMapping(vocabValue);
                }
                else
                {
                    //Relative vocab is expanded against the current vocabulary or base.
                    string? expanded = IriUtils.ExpandIri(vocabValue, result, vocab: true);
                    result = result.WithVocabularyMapping(expanded ?? vocabValue);
                }
            }
            else
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidContextEntry,
                    "@vocab must be a string or null.");
            }
        }

        //Handle @language.
        if(contextObject.TryGetProperty("@language", out JsonLdNode langElement))
        {
            if(langElement.Kind == JsonLdNodeKind.Null)
            {
                result = result.WithDefaultLanguage(null);
            }
            else if(langElement.Kind == JsonLdNodeKind.String)
            {
                result = result.WithDefaultLanguage(langElement.GetString().ToLowerInvariant());
            }
            else
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidLanguageMapping,
                    "@language must be a string or null.");
            }
        }

        //Handle @direction.
        if(contextObject.TryGetProperty("@direction", out JsonLdNode directionElement))
        {
            if(directionElement.Kind == JsonLdNodeKind.Null)
            {
                result = result.WithDefaultBaseDirection(null);
            }
            else if(directionElement.Kind == JsonLdNodeKind.String)
            {
                string direction = directionElement.GetString();
                if(direction is not "ltr" and not "rtl")
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidBaseDirection,
                        $"@direction must be 'ltr', 'rtl', or null. Got '{direction}'.");
                }

                result = result.WithDefaultBaseDirection(direction);
            }
            else
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidBaseDirection,
                    "@direction must be a string or null.");
            }
        }

        //Handle @propagate.
        if(contextObject.TryGetProperty("@propagate", out JsonLdNode propagateElement))
        {
            if(propagateElement.Kind is not JsonLdNodeKind.True and not JsonLdNodeKind.False)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidPropagateValue,
                    "@propagate must be a boolean.");
            }

            result = result.WithPropagate(propagateElement.GetBoolean());
        }

        //Process term definitions: collect all non-keyword properties.
        Dictionary<string, JsonLdNode> termEntries = [];
        foreach((string name, JsonLdNode value) in contextObject.EnumerateObject())
        {
            if(name is "@base" or "@vocab" or "@language" or "@direction"
                or "@propagate" or "@version" or "@import" or "@protected")
            {
                continue;
            }

            termEntries[name] = value;
        }

        //Track which terms are currently being defined to detect cycles.
        Dictionary<string, bool> defined = [];
        bool contextProtected = contextObject.TryGetProperty("@protected", out JsonLdNode protectedElement)
            && protectedElement.Kind == JsonLdNodeKind.True;

        foreach(string term in termEntries.Keys)
        {
            result = await CreateTermDefinitionAsync(
                result, termEntries, term, baseUrl, resolver, parser, remoteContexts,
                defined, overrideProtected, contextProtected, depth, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Implements the "Create Term Definition" algorithm from the JSON-LD 1.1 specification.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Language tags must be lowercase per BCP47 and the JSON-LD 1.1 specification.")]
    private static async ValueTask<JsonLdContext> CreateTermDefinitionAsync(
        JsonLdContext activeContext,
        Dictionary<string, JsonLdNode> localContext,
        string term,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        List<string> remoteContexts,
        Dictionary<string, bool> defined,
        bool overrideProtected,
        bool contextProtected,
        int depth,
        CancellationToken cancellationToken)
    {
        //If already defined (or being defined), skip.
        if(defined.TryGetValue(term, out bool isComplete))
        {
            if(isComplete)
            {
                //If the term was already defined but its IRI mapping is still a compact IRI
                //(not yet expanded because the prefix was not yet defined when this term was
                //first processed), re-expand it now.
                if(activeContext.TryGetTerm(term, out TermDefinition? existingDef)
                    && existingDef?.IriMapping is not null
                    && !IriUtils.IsAbsoluteIri(existingDef.IriMapping)
                    && !IriUtils.IsKeyword(existingDef.IriMapping))
                {
                    string? reExpanded = IriUtils.ExpandIri(existingDef.IriMapping, activeContext, vocab: true);
                    if(reExpanded is not null && reExpanded != existingDef.IriMapping)
                    {
                        TermDefinition updated = new()
                        {
                            IriMapping = reExpanded,
                            Prefix = existingDef.Prefix,
                            TypeMapping = existingDef.TypeMapping,
                            LanguageMapping = existingDef.LanguageMapping,
                            HasLanguageMapping = existingDef.HasLanguageMapping,
                            ContainerMapping = existingDef.ContainerMapping,
                            Protected = existingDef.Protected,
                            ReverseProperty = existingDef.ReverseProperty,
                            NestValue = existingDef.NestValue,
                            IndexMapping = existingDef.IndexMapping,
                            LocalContext = existingDef.LocalContext
                        };
                        activeContext = activeContext.WithTerm(term, updated);
                    }
                }

                return activeContext;
            }

            throw new JsonLdProcessingException(
                JsonLdErrorCode.CyclicIriMapping,
                $"Cyclic IRI mapping detected for term '{term}'.");
        }

        //Mark as in-progress.
        defined[term] = false;

        if(IriUtils.IsKeyword(term))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.KeywordRedefinition,
                $"Cannot redefine keyword '{term}'.");
        }

        if(IriUtils.IsKeywordLike(term))
        {
            //Keyword-like terms are ignored with a warning in the spec; we skip silently.
            defined[term] = true;

            return activeContext;
        }

        JsonLdNode value = localContext[term];

        //A null value removes the term.
        if(value.Kind == JsonLdNodeKind.Null
            || (value.Kind == JsonLdNodeKind.Object
                && value.TryGetProperty("@id", out JsonLdNode idEl)
                && idEl.Kind == JsonLdNodeKind.Null))
        {
            if(activeContext.TryGetTerm(term, out TermDefinition? existing)
                && existing?.Protected == true
                && !overrideProtected)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.ProtectedTermRedefinition,
                    $"Cannot override protected term '{term}'.");
            }

            activeContext = activeContext.WithoutTerm(term);
            defined[term] = true;

            return activeContext;
        }

        //Expand simple string value: "term": "http://example.org/value".
        if(value.Kind == JsonLdNodeKind.String)
        {
            string iriMapping = value.GetString();

            //If the value looks like a compact IRI (prefix:suffix), ensure the prefix
            //term is defined first so expansion can succeed. This mirrors the W3C algorithm
            //which recursively calls CreateTermDefinition on the prefix before expanding.
            int colonPos = iriMapping.IndexOf(':', StringComparison.Ordinal);
            if(colonPos > 0 && !iriMapping.StartsWith("//", StringComparison.Ordinal))
            {
                string prefix = iriMapping[..colonPos];
                if(!IriUtils.IsKeyword(prefix)
                    && prefix != "_"
                    && localContext.ContainsKey(prefix)
                    && (!defined.TryGetValue(prefix, out bool prefixDone) || !prefixDone))
                {
                    activeContext = await CreateTermDefinitionAsync(
                        activeContext, localContext, prefix, baseUrl, resolver, parser, remoteContexts,
                        defined, overrideProtected, contextProtected, depth, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            string? expanded = IriUtils.ExpandIri(iriMapping, activeContext, vocab: true);

            TermDefinition definition = new()
            {
                IriMapping = expanded,
                Prefix = !IriUtils.IsKeyword(expanded) && iriMapping.EndsWith(':'),
                Protected = contextProtected
            };

            activeContext = SetTermDefinition(activeContext, term, definition, overrideProtected);
            defined[term] = true;

            return activeContext;
        }

        if(value.Kind != JsonLdNodeKind.Object)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidTermDefinition,
                $"Term definition for '{term}' must be a string, object, or null.");
        }

        //Full expanded term definition object.
        string? termIriMapping = null;
        string? typeMapping = null;
        string? languageMapping = null;
        bool hasLanguageMapping = false;
        List<string> containerMapping = [];
        bool isPrefix = false;
        bool isProtected = contextProtected;
        bool isReverse = false;
        string? reverseProperty = null;
        string? nestValue = null;
        string? indexMapping = null;
        JsonLdNode? localContextValue = null;

        //Handle @protected override.
        if(value.TryGetProperty("@protected", out JsonLdNode termProtected))
        {
            if(termProtected.Kind is not JsonLdNodeKind.True and not JsonLdNodeKind.False)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidProtectedValue,
                    $"@protected must be a boolean in term definition for '{term}'.");
            }

            isProtected = termProtected.GetBoolean();
        }

        //Handle @type.
        if(value.TryGetProperty("@type", out JsonLdNode typeElement))
        {
            if(typeElement.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidTypeMappingTermDefinition,
                    $"@type in term definition for '{term}' must be a string.");
            }

            string typeValue = typeElement.GetString();
            string? expandedType = typeValue is "@id" or "@vocab" or "@json" or "@none"
                ? typeValue
                : IriUtils.ExpandIri(typeValue, activeContext, vocab: true);

            if(expandedType is null || (!IriUtils.IsAbsoluteIri(expandedType) && !IriUtils.IsKeyword(expandedType)))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidTypeMappingTermDefinition,
                    $"@type in term definition for '{term}' must expand to an absolute IRI.");
            }

            typeMapping = expandedType;
        }

        //Handle @reverse.
        if(value.TryGetProperty("@reverse", out JsonLdNode reverseElement))
        {
            if(value.TryGetProperty("@id", out _) || value.TryGetProperty("@nest", out _))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidReverseProperty,
                    $"@reverse in term definition for '{term}' cannot be combined with @id or @nest.");
            }

            if(reverseElement.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidIriMapping,
                    $"@reverse in term definition for '{term}' must be a string.");
            }

            string reverseValue = reverseElement.GetString();
            string? expandedReverse = IriUtils.ExpandIri(reverseValue, activeContext, vocab: true);

            if(!IriUtils.IsAbsoluteIri(expandedReverse ?? ""))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidIriMapping,
                    $"@reverse value '{reverseValue}' in term definition for '{term}' does not expand to an absolute IRI.");
            }

            isReverse = true;
            reverseProperty = expandedReverse;
            termIriMapping = expandedReverse;

            if(value.TryGetProperty("@container", out JsonLdNode reverseContainer))
            {
                string? containerValue = reverseContainer.Kind == JsonLdNodeKind.String
                    ? reverseContainer.GetString()
                    : null;

                if(containerValue is not null and not "@set" and not "@index")
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidReverseProperty,
                        $"@container for reverse property '{term}' must be '@set' or '@index'.");
                }

                if(containerValue is not null)
                {
                    containerMapping.Add(containerValue);
                }
            }
        }
        else
        {
            //Handle @id.
            if(value.TryGetProperty("@id", out JsonLdNode idElement))
            {
                if(idElement.Kind == JsonLdNodeKind.Null)
                {
                    //Explicit null @id: term is not mapped.
                    termIriMapping = null;
                }
                else if(idElement.Kind == JsonLdNodeKind.String)
                {
                    string idValue = idElement.GetString();

                    if(!IriUtils.IsKeyword(idValue) || idValue != term)
                    {
                        string? expanded = IriUtils.ExpandIri(idValue, activeContext, vocab: true);
                        termIriMapping = expanded;

                        if(termIriMapping is not null && !IriUtils.IsAbsoluteIri(termIriMapping) && !IriUtils.IsKeyword(termIriMapping))
                        {
                            termIriMapping = null;
                        }
                    }
                    else
                    {
                        termIriMapping = idValue;
                    }

                    //A term ending with ':' or '/' or '?' can be a prefix.
                    isPrefix = termIriMapping is not null
                        && (term.EndsWith(':') || term.EndsWith('/') || term.EndsWith('?'));
                }
                else
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidIriMapping,
                        $"@id in term definition for '{term}' must be a string or null.");
                }
            }
            else if(term.Contains(':', StringComparison.Ordinal))
            {
                //Compact IRI: expand the prefix.
                int colonPos = term.IndexOf(':', StringComparison.Ordinal);
                string prefix = term[..colonPos];
                string suffix = term[(colonPos + 1)..];

                if(localContext.ContainsKey(prefix))
                {
                    activeContext = await CreateTermDefinitionAsync(
                        activeContext, localContext, prefix, baseUrl, resolver, parser, remoteContexts,
                        defined, overrideProtected, contextProtected, depth, cancellationToken)
                        .ConfigureAwait(false);
                }

                if(activeContext.TryGetTerm(prefix, out TermDefinition? prefixDef)
                    && prefixDef?.IriMapping is not null)
                {
                    termIriMapping = prefixDef.IriMapping + suffix;
                }
                else
                {
                    termIriMapping = term;
                }
            }
            else if(term == "@type")
            {
                termIriMapping = "@type";
            }
            else
            {
                //No @id: expand the term against vocabulary.
                termIriMapping = IriUtils.ExpandIri(term, activeContext, vocab: true);
            }
        }

        //Handle @container.
        if(value.TryGetProperty("@container", out JsonLdNode containerElement))
        {
            HashSet<string> validContainers = ["@list", "@set", "@language", "@index", "@id", "@graph", "@type", "@none"];
            List<string> containers = [];

            if(containerElement.Kind == JsonLdNodeKind.String)
            {
                string container = containerElement.GetString();
                if(!validContainers.Contains(container, StringComparer.Ordinal))
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidContainerMapping,
                        $"Invalid @container value '{container}' for term '{term}'.");
                }

                containers.Add(container);
            }
            else if(containerElement.Kind == JsonLdNodeKind.Array)
            {
                foreach(JsonLdNode el in containerElement.EnumerateArray())
                {
                    if(el.Kind != JsonLdNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidContainerMapping,
                            $"@container array for term '{term}' must contain only strings.");
                    }

                    string container = el.GetString();
                    if(!validContainers.Contains(container, StringComparer.Ordinal))
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidContainerMapping,
                            $"Invalid @container value '{container}' for term '{term}'.");
                    }

                    containers.Add(container);
                }
            }
            else
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidContainerMapping,
                    $"@container for term '{term}' must be a string or array.");
            }

            containerMapping = containers;
        }

        //Handle @prefix.
        if(value.TryGetProperty("@prefix", out JsonLdNode prefixElement))
        {
            if(prefixElement.Kind is not JsonLdNodeKind.True and not JsonLdNodeKind.False)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidPrefixValue,
                    $"@prefix in term definition for '{term}' must be a boolean.");
            }

            isPrefix = prefixElement.GetBoolean();

            if(isPrefix && IriUtils.IsKeyword(termIriMapping))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidTermDefinition,
                    $"Term '{term}' maps to a keyword and cannot be a prefix.");
            }
        }

        //Handle @language.
        if(value.TryGetProperty("@language", out JsonLdNode langElement))
        {
            hasLanguageMapping = true;
            if(langElement.Kind == JsonLdNodeKind.Null)
            {
                languageMapping = null;
            }
            else if(langElement.Kind == JsonLdNodeKind.String)
            {
                languageMapping = langElement.GetString().ToLower(CultureInfo.InvariantCulture);
            }
            else
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidLanguageMapping,
                    $"@language in term definition for '{term}' must be a string or null.");
            }
        }

        //Handle @nest.
        if(value.TryGetProperty("@nest", out JsonLdNode nestElement))
        {
            if(nestElement.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidNestValue,
                    $"@nest in term definition for '{term}' must be a string.");
            }

            nestValue = nestElement.GetString();

            if(IriUtils.IsKeyword(nestValue) && nestValue != "@nest")
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidNestValue,
                    $"@nest value '{nestValue}' in term definition for '{term}' must be '@nest' or a non-keyword term.");
            }
        }

        //Handle @index.
        if(value.TryGetProperty("@index", out JsonLdNode indexElement))
        {
            if(indexElement.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidTermDefinition,
                    $"@index in term definition for '{term}' must be a string.");
            }

            indexMapping = indexElement.GetString();
        }

        //Handle @context for type-scoped contexts. Clone so the stored node has a
        //lifetime independent of the document it was extracted from.
        if(value.TryGetProperty("@context", out JsonLdNode contextElement))
        {
            localContextValue = contextElement.Clone();
        }

        TermDefinition termDefinition = new()
        {
            IriMapping = termIriMapping,
            Prefix = isPrefix,
            TypeMapping = typeMapping,
            LanguageMapping = languageMapping,
            HasLanguageMapping = hasLanguageMapping,
            ContainerMapping = containerMapping,
            Protected = isProtected,
            ReverseProperty = isReverse ? reverseProperty : null,
            NestValue = nestValue,
            IndexMapping = indexMapping,
            LocalContext = localContextValue
        };

        activeContext = SetTermDefinition(activeContext, term, termDefinition, overrideProtected);
        defined[term] = true;

        return activeContext;
    }

    private static JsonLdContext SetTermDefinition(
        JsonLdContext activeContext,
        string term,
        TermDefinition definition,
        bool overrideProtected)
    {
        if(activeContext.TryGetTerm(term, out TermDefinition? existing)
            && existing?.Protected == true
            && !overrideProtected)
        {
            //Protected term: new definition must be identical to the existing one.
            //For simplicity we compare IRI mappings only; a full implementation would
            //compare all fields.
            if(existing.IriMapping != definition.IriMapping)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.ProtectedTermRedefinition,
                    $"Cannot redefine protected term '{term}' with a different IRI mapping.");
            }

            return activeContext;
        }

        return activeContext.WithTerm(term, definition);
    }
}
