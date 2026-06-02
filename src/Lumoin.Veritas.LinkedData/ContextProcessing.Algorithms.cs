using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// The W3C JSON-LD 1.1 §4.1 Context Processing Algorithm and §4.1.2
/// Create Term Definition algorithm, over the format-neutral POCO
/// inputs. Format-specific shells extract their document tree into
/// <see cref="LinkedDataContextEntry"/> + <see cref="LinkedDataTermSource"/>
/// POCOs and call these methods.
/// </summary>
public static partial class ContextProcessing
{
    private const int MaxContextDepth = 50;

    /// <summary>
    /// Applies a list of pre-extracted <see cref="LinkedDataContextEntry"/>
    /// values to <paramref name="activeContext"/>, mirroring the JSON-LD 1.1
    /// §4.1 step 5 loop. Entries are processed in order; each entry is
    /// either a reset, a remote URL (dereferenced via
    /// <paramref name="fetcher"/> + <paramref name="parser"/>), or an
    /// inline-term collection.
    /// </summary>
    /// <param name="activeContext">The context active before processing begins.</param>
    /// <param name="entries">The pre-extracted entries to apply, in order.</param>
    /// <param name="baseUrl">The document base URL for resolving relative IRIs in entries.</param>
    /// <param name="fetcher">Delegate that fetches remote-context resources.</param>
    /// <param name="parser">Delegate that parses fetched bytes into the format-neutral dict shape.</param>
    /// <param name="cache">Optional delegate that probes the application's cache before fetching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active context after applying the entries.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static ValueTask<LinkedDataContext> ApplyEmbeddedContextsAsync(
        LinkedDataContext activeContext,
        IReadOnlyList<LinkedDataContextEntry> entries,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        CancellationToken cancellationToken,
        bool propagate = true,
        bool overrideProtected = false)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(parser);

        return ApplyEmbeddedContextsCoreAsync(
            activeContext, entries, baseUrl, fetcher, parser, cache,
            overrideProtected: overrideProtected, propagate: propagate,
            remoteContexts: [], depth: 0, cancellationToken);
    }

    private static async ValueTask<LinkedDataContext> ApplyEmbeddedContextsCoreAsync(
        LinkedDataContext activeContext,
        IReadOnlyList<LinkedDataContextEntry> entries,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        bool overrideProtected,
        bool propagate,
        List<string> remoteContexts,
        int depth,
        CancellationToken cancellationToken)
    {
        if(depth > MaxContextDepth)
        {
            throw new LinkedDataProcessingException(
                "context overflow",
                "Maximum context processing depth exceeded. A context may contain a recursive reference.");
        }

        //Seed the propagation default for this application (type-scoped
        //contexts pass false). A per-entry @propagate overrides it in
        //ApplyContextDirectives.
        LinkedDataContext result = activeContext.Clone().WithPropagate(propagate);

        foreach(LinkedDataContextEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(entry.IsReset)
            {
                LinkedDataContext? reset = TryResetToNull(result, overrideProtected, out _);
                result = reset ?? throw new LinkedDataProcessingException(
                    "invalid context nullification",
                    "Cannot reset context: protected terms would be overridden.");
                continue;
            }

            if(entry.Url is not null)
            {
                result = await ProcessRemoteContextCoreAsync(
                    result, entry.Url, baseUrl, fetcher, parser, cache,
                    remoteContexts, overrideProtected, propagate, depth,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if(entry.Terms is null)
            {
                continue;
            }

            //Inline context: apply directives then iterate terms.
            result = ApplyContextDirectives(result, entry, overrideProtected);

            //Handle @import recursively (resolve and merge before processing terms).
            if(entry.Import is not null)
            {
                result = await ProcessRemoteContextCoreAsync(
                    result, entry.Import, baseUrl, fetcher, parser, cache,
                    remoteContexts, overrideProtected, propagate, depth,
                    cancellationToken, isImport: true).ConfigureAwait(false);
            }

            bool contextProtected = entry.Protected ?? false;
            Dictionary<string, bool> defined = [];
            foreach(string termName in entry.Terms.Keys)
            {
                result = await CreateTermDefinitionCoreAsync(
                    result, entry.Terms, termName, entry.BaseUrl ?? baseUrl,
                    fetcher, parser, cache, remoteContexts, defined,
                    overrideProtected, contextProtected, depth,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    private static LinkedDataContext ApplyContextDirectives(
        LinkedDataContext activeContext,
        LinkedDataContextEntry entry,
        bool overrideProtected)
    {
        _ = overrideProtected;
        LinkedDataContext result = activeContext;

        if(entry.HasBase)
        {
            LinkedDataContext? updated = TryApplyBase(result, entry.Base, out string? unresolvable);
            if(updated is null)
            {
                throw new LinkedDataProcessingException(
                    "invalid base IRI",
                    string.Create(CultureInfo.InvariantCulture, $"@base value '{unresolvable}' is relative but no base IRI is available to resolve against."));
            }
            result = updated;
        }

        if(entry.HasVocab)
        {
            result = ApplyVocab(result, entry.Vocab);
        }

        if(entry.HasLanguage)
        {
            result = ApplyLanguage(result, entry.Language);
        }

        if(entry.HasDirection)
        {
            LinkedDataContext? updated = TryApplyDirection(result, entry.Direction);
            if(updated is null)
            {
                throw new LinkedDataProcessingException(
                    "invalid base direction",
                    string.Create(CultureInfo.InvariantCulture, $"@direction must be 'ltr', 'rtl', or null. Got '{entry.Direction}'."));
            }
            result = updated;
        }

        if(entry.Propagate is bool propagateValue)
        {
            result = result.WithPropagate(propagateValue);
        }

        return result;
    }

    /// <summary>
    /// Implements W3C JSON-LD 1.1 §4.1 step 5.2 — fetches a remote
    /// context document, parses it, and applies the embedded
    /// <c>@context</c> entry to <paramref name="activeContext"/>.
    /// </summary>
    /// <param name="activeContext">The context active before processing.</param>
    /// <param name="contextUrl">The remote-context URL.</param>
    /// <param name="baseUrl">The document base URL.</param>
    /// <param name="fetcher">Fetch delegate.</param>
    /// <param name="parser">Parse delegate.</param>
    /// <param name="cache">Optional cache delegate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active context after applying the resolved context.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static ValueTask<LinkedDataContext> ProcessRemoteContextAsync(
        LinkedDataContext activeContext,
        string contextUrl,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentException.ThrowIfNullOrEmpty(contextUrl);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(parser);

        return ProcessRemoteContextCoreAsync(
            activeContext, contextUrl, baseUrl, fetcher, parser, cache,
            remoteContexts: [], overrideProtected: false, propagate: true,
            depth: 0, cancellationToken);
    }

    private static async ValueTask<LinkedDataContext> ProcessRemoteContextCoreAsync(
        LinkedDataContext activeContext,
        string contextUrl,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        List<string> remoteContexts,
        bool overrideProtected,
        bool propagate,
        int depth,
        CancellationToken cancellationToken,
        bool isImport = false)
    {
        string resolvedUri = baseUrl is not null
            ? IriUtils.ResolveIri(baseUrl, contextUrl)
            : contextUrl;

        if(remoteContexts.Contains(resolvedUri, StringComparer.Ordinal))
        {
            throw new LinkedDataProcessingException(
                "context overflow",
                string.Create(CultureInfo.InvariantCulture, $"Recursive context reference detected for '{resolvedUri}'."));
        }
        remoteContexts.Add(resolvedUri);

        RemoteResource? resource = cache is null
            ? null
            : await cache(resolvedUri, cancellationToken).ConfigureAwait(false);
        if(resource is null)
        {
            resource = await fetcher(resolvedUri, baseUrl, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyDictionary<string, object?> parsedContext =
            await parser(resource, cancellationToken).ConfigureAwait(false);

        //@import may only reference a context whose @context is a single map;
        //an array-shaped remote @context (surfaced as a lone "@context" list
        //entry) is an invalid remote context.
        if(isImport
            && parsedContext.Count == 1
            && parsedContext.TryGetValue("@context", out object? importedContext)
            && importedContext is not IReadOnlyDictionary<string, object?>)
        {
            throw new LinkedDataProcessingException(
                "invalid remote context",
                string.Create(CultureInfo.InvariantCulture, $"@import '{contextUrl}' must reference a single context map."));
        }

        //The parser hands back the structure of the resolved
        //document's @context value. Convert each entry to a
        //LinkedDataContextEntry. The remote document's effective base
        //is the resource's final URL (after redirects) when present.
        string effectiveBase = resource.FinalUrl ?? resolvedUri;
        IReadOnlyList<LinkedDataContextEntry> embeddedEntries =
            ConvertParsedContextToEntries(parsedContext, effectiveBase);

        return await ApplyEmbeddedContextsCoreAsync(
            activeContext, embeddedEntries, effectiveBase, fetcher, parser, cache,
            overrideProtected, propagate, remoteContexts, depth + 1,
            cancellationToken).ConfigureAwait(false);
    }

    private static LinkedDataContextEntry[] ConvertParsedContextToEntries(
        IReadOnlyDictionary<string, object?> parsedContext,
        string? baseUrl)
    {
        //The dict-of-object shape's top level represents an inline-context
        //object. Walk its entries: known directives go on the entry
        //itself; everything else becomes a term source.
        Dictionary<string, LinkedDataTermSource> terms = [];
        string? @base = null;
        bool hasBase = false;
        string? vocab = null;
        bool hasVocab = false;
        string? language = null;
        bool hasLanguage = false;
        string? direction = null;
        bool hasDirection = false;
        bool? propagate = null;
        bool? @protected = null;
        string? import = null;
        int keyCounter = 0;

        foreach(KeyValuePair<string, object?> entry in parsedContext)
        {
            switch(entry.Key)
            {
                case "@base":
                {
                    hasBase = true;
                    @base = entry.Value as string;
                    break;
                }
                case "@vocab":
                {
                    hasVocab = true;
                    vocab = entry.Value as string;
                    break;
                }
                case "@language":
                {
                    hasLanguage = true;
                    language = entry.Value as string;
                    break;
                }
                case "@direction":
                {
                    hasDirection = true;
                    direction = entry.Value as string;
                    break;
                }
                case "@propagate":
                {
                    if(entry.Value is bool p) propagate = p;
                    break;
                }
                case "@protected":
                {
                    if(entry.Value is bool prot) @protected = prot;
                    break;
                }
                case "@import":
                {
                    import = entry.Value as string;
                    break;
                }
                case "@version":
                {
                    break;
                }
                default:
                {
                    keyCounter++;
                    terms[entry.Key] = ConvertToTermSource(
                        entry.Key, entry.Value, $"remote-k{keyCounter}-{entry.Key}");
                    break;
                }
            }
        }

        return new[]
        {
            new LinkedDataContextEntry(terms, baseUrl, "remote-inline")
            {
                Base = @base,
                HasBase = hasBase,
                Vocab = vocab,
                HasVocab = hasVocab,
                Language = language,
                HasLanguage = hasLanguage,
                Direction = direction,
                HasDirection = hasDirection,
                Propagate = propagate,
                Protected = @protected,
                Import = import
            }
        };
    }

    /// <summary>
    /// Builds or updates the term definition for <paramref name="termName"/>
    /// in <paramref name="activeContext"/>, per the W3C JSON-LD 1.1
    /// §4.1.2 Create Term Definition algorithm, using the pre-extracted
    /// POCO inputs.
    /// </summary>
    /// <param name="activeContext">The current active context.</param>
    /// <param name="localContext">The complete inline-term map this term belongs to (used for recursive prefix-term resolution).</param>
    /// <param name="termName">The term name being defined.</param>
    /// <param name="baseUrl">The document base URL.</param>
    /// <param name="fetcher">Fetch delegate.</param>
    /// <param name="parser">Parse delegate.</param>
    /// <param name="cache">Optional cache delegate.</param>
    /// <param name="overrideProtected">Whether protected terms may be overridden.</param>
    /// <param name="contextProtected">Whether the surrounding context applies <c>@protected</c> to terms.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active context after the term-definition update.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static ValueTask<LinkedDataContext> CreateTermDefinitionAsync(
        LinkedDataContext activeContext,
        IReadOnlyDictionary<string, LinkedDataTermSource> localContext,
        string termName,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        bool overrideProtected,
        bool contextProtected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(localContext);
        ArgumentNullException.ThrowIfNull(termName);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(parser);

        Dictionary<string, bool> defined = [];
        return CreateTermDefinitionCoreAsync(
            activeContext, localContext, termName, baseUrl,
            fetcher, parser, cache, remoteContexts: [], defined,
            overrideProtected, contextProtected, depth: 0,
            cancellationToken);
    }

    /// <summary>
    /// Ensures the prefix of a compact-IRI value (e.g. <c>xsd</c> in
    /// <c>xsd:dateTime</c>) is defined before that value is expanded, by
    /// recursively creating the prefix's term definition when it is declared
    /// later in the same local context. Leaves the context unchanged when the
    /// value is not a compact IRI or its prefix is not a pending local term.
    /// </summary>
    private static async ValueTask<LinkedDataContext> EnsurePrefixDefinedAsync(
        LinkedDataContext activeContext,
        IReadOnlyDictionary<string, LinkedDataTermSource> localContext,
        string? compactValue,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        List<string> remoteContexts,
        Dictionary<string, bool> defined,
        bool overrideProtected,
        bool contextProtected,
        int depth,
        CancellationToken cancellationToken)
    {
        string? prefix = compactValue is null ? null : TryGetCompactIriPrefix(compactValue);
        if(prefix is not null
            && localContext.ContainsKey(prefix)
            && (!defined.TryGetValue(prefix, out bool prefixDone) || !prefixDone))
        {
            activeContext = await CreateTermDefinitionCoreAsync(
                activeContext, localContext, prefix, baseUrl,
                fetcher, parser, cache, remoteContexts, defined,
                overrideProtected, contextProtected, depth, cancellationToken)
                .ConfigureAwait(false);
        }

        return activeContext;
    }

    private static async ValueTask<LinkedDataContext> CreateTermDefinitionCoreAsync(
        LinkedDataContext activeContext,
        IReadOnlyDictionary<string, LinkedDataTermSource> localContext,
        string termName,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        List<string> remoteContexts,
        Dictionary<string, bool> defined,
        bool overrideProtected,
        bool contextProtected,
        int depth,
        CancellationToken cancellationToken)
    {
        //A term definition for the empty string is not allowed.
        if(termName.Length == 0)
        {
            throw new LinkedDataProcessingException(
                "invalid term definition",
                "A term definition for the empty string is not allowed.");
        }

        //If already defined (or being defined), skip / handle re-expansion.
        if(defined.TryGetValue(termName, out bool isComplete))
        {
            if(isComplete)
            {
                activeContext = TryReexpandCompactIriTerm(activeContext, termName);
                return activeContext;
            }
            throw new LinkedDataProcessingException(
                "cyclic IRI mapping",
                string.Create(CultureInfo.InvariantCulture, $"Cyclic IRI mapping detected for term '{termName}'."));
        }

        defined[termName] = false;

        if(IriUtils.IsKeyword(termName))
        {
            throw new LinkedDataProcessingException(
                "keyword redefinition",
                string.Create(CultureInfo.InvariantCulture, $"Cannot redefine keyword '{termName}'."));
        }

        if(IriUtils.IsKeywordLike(termName))
        {
            defined[termName] = true;
            return activeContext;
        }

        LinkedDataTermSource value = localContext[termName];

        //Removal marker.
        if(value.IsRemoval)
        {
            LinkedDataContext? cleared = TryRemoveTerm(
                activeContext, termName, overrideProtected, out _);
            if(cleared is null)
            {
                throw new LinkedDataProcessingException(
                    "protected term redefinition",
                    string.Create(CultureInfo.InvariantCulture, $"Cannot override protected term '{termName}'."));
            }
            activeContext = cleared;
            defined[termName] = true;
            return activeContext;
        }

        //Simple-string form.
        if(value.IsSimpleString)
        {
            string iriMapping = value.Iri ?? string.Empty;

            //A term redefined to its own name (e.g. "term": "term") maps
            //through the vocabulary, not the term's prior definition.
            if(string.Equals(iriMapping, termName, StringComparison.Ordinal) && activeContext.VocabularyMapping is not null)
            {
                TermDefinition selfDefinition = new()
                {
                    IriMapping = activeContext.VocabularyMapping + termName,
                    Protected = contextProtected
                };
                activeContext = SetTermDefinitionInternal(activeContext, termName, selfDefinition, overrideProtected);
                defined[termName] = true;

                return activeContext;
            }

            string? compactPrefix = TryGetCompactIriPrefix(iriMapping);
            if(compactPrefix is not null
                && localContext.ContainsKey(compactPrefix)
                && (!defined.TryGetValue(compactPrefix, out bool prefixDone) || !prefixDone))
            {
                activeContext = await CreateTermDefinitionCoreAsync(
                    activeContext, localContext, compactPrefix, baseUrl,
                    fetcher, parser, cache, remoteContexts, defined,
                    overrideProtected, contextProtected, depth, cancellationToken)
                    .ConfigureAwait(false);
            }

            string? simpleExpandedId = activeContext.ExpandIri(iriMapping, vocab: true);
            activeContext = await EnsureTermIriFormConsistentAsync(
                activeContext, localContext, termName, iriMapping, simpleExpandedId,
                baseUrl, fetcher, parser, cache, remoteContexts, defined,
                overrideProtected, contextProtected, depth, cancellationToken).ConfigureAwait(false);

            TermDefinition definition = BuildSimpleStringTermDefinition(
                activeContext, termName, iriMapping, contextProtected);
            activeContext = SetTermDefinitionInternal(activeContext, termName, definition, overrideProtected);
            defined[termName] = true;
            return activeContext;
        }

        //Full expanded-object form.
        string? termIriMapping = null;
        string? typeMapping = null;
        string? languageMapping = null;
        bool hasLanguageMapping = value.HasLanguageMapping;
        string? directionMapping = null;
        bool hasDirectionMapping = value.HasDirectionMapping;
        List<string> containerMapping = [];
        bool isPrefix = value.Prefix;
        //A term-level @protected (including an explicit false) overrides the
        //context-level @protected default.
        bool isProtected = value.HasProtected ? value.Protected : contextProtected;
        bool isReverse = false;
        string? reverseProperty = null;
        string? nestValue = value.Nest;
        string? indexMapping = value.Index;

        if(value.Type is not null)
        {
            //Resolve a compact-IRI @type's prefix dependency first (the
            //prefix may be defined later in the same local context), so the
            //type mapping expands to its absolute IRI.
            activeContext = await EnsurePrefixDefinedAsync(
                activeContext, localContext, value.Type, baseUrl,
                fetcher, parser, cache, remoteContexts, defined,
                overrideProtected, contextProtected, depth, cancellationToken).ConfigureAwait(false);

            string? expandedType = TryExpandTermType(activeContext, value.Type);
            if(expandedType is null)
            {
                throw new LinkedDataProcessingException(
                    "invalid type mapping",
                    string.Create(CultureInfo.InvariantCulture, $"@type in term definition for '{termName}' must expand to an absolute IRI."));
            }
            typeMapping = expandedType;
        }

        if(value.Reverse)
        {
            //@reverse cannot be combined with @id or @nest.
            if(value.Iri is not null || value.Nest is not null)
            {
                throw new LinkedDataProcessingException(
                    "invalid reverse property",
                    string.Create(CultureInfo.InvariantCulture, $"@reverse in term definition for '{termName}' cannot be combined with @id or @nest."));
            }
            if(value.ReverseIri is null)
            {
                throw new LinkedDataProcessingException(
                    "invalid IRI mapping",
                    string.Create(CultureInfo.InvariantCulture, $"@reverse in term definition for '{termName}' must be a string."));
            }
            string? expandedReverse = TryExpandReverseProperty(activeContext, value.ReverseIri);
            if(expandedReverse is null)
            {
                //A reverse value in the form of a keyword (e.g. "@ignoreMe")
                //is not an error: the term is simply not created.
                if(IriUtils.IsKeywordLike(value.ReverseIri))
                {
                    defined[termName] = true;

                    return activeContext;
                }

                throw new LinkedDataProcessingException(
                    "invalid IRI mapping",
                    string.Create(CultureInfo.InvariantCulture, $"@reverse value '{value.ReverseIri}' in term definition for '{termName}' does not expand to an absolute IRI."));
            }
            isReverse = true;
            reverseProperty = expandedReverse;
            termIriMapping = expandedReverse;

            //Reverse-property @container restrictions.
            if(value.Containers is not null && value.Containers.Count > 0)
            {
                if(value.Containers.Count > 1
                    || !IsValidReverseContainerKeyword(value.Containers[0]))
                {
                    throw new LinkedDataProcessingException(
                        "invalid reverse property",
                        string.Create(CultureInfo.InvariantCulture, $"@container for reverse property '{termName}' must be '@set' or '@index'."));
                }
                containerMapping.Add(value.Containers[0]);
            }
        }
        else
        {
            //Handle @id resolution.
            if(value.Iri is not null)
            {
                //@context may not be aliased by a term.
                if(string.Equals(value.Iri, "@context", StringComparison.Ordinal))
                {
                    throw new LinkedDataProcessingException(
                        "invalid keyword alias",
                        string.Create(CultureInfo.InvariantCulture, $"Term '{termName}' may not alias the keyword @context."));
                }

                //Resolve a compact-IRI @id's prefix dependency first.
                activeContext = await EnsurePrefixDefinedAsync(
                    activeContext, localContext, value.Iri, baseUrl,
                    fetcher, parser, cache, remoteContexts, defined,
                    overrideProtected, contextProtected, depth, cancellationToken).ConfigureAwait(false);

                termIriMapping = ExpandTermIdMapping(activeContext, termName, value.Iri);

                //A term whose name has the form of an IRI (it contains a
                //slash or a colon followed by a non-colon) must expand to the
                //same IRI as its @id mapping; otherwise the definition is an
                //invalid IRI mapping (W3C JSON-LD 1.1 §4.1.2). This also
                //rejects an absolute-IRI-shaped term whose @id is a keyword.
                activeContext = await EnsureTermIriFormConsistentAsync(
                    activeContext, localContext, termName, value.Iri, termIriMapping,
                    baseUrl, fetcher, parser, cache, remoteContexts, defined,
                    overrideProtected, contextProtected, depth, cancellationToken).ConfigureAwait(false);
            }
            else if(termName.Contains(':', StringComparison.Ordinal))
            {
                //Compact IRI: ensure prefix term is defined first.
                int colonPos = termName.IndexOf(':', StringComparison.Ordinal);
                string prefix = termName[..colonPos];
                if(localContext.ContainsKey(prefix))
                {
                    activeContext = await CreateTermDefinitionCoreAsync(
                        activeContext, localContext, prefix, baseUrl,
                        fetcher, parser, cache, remoteContexts, defined,
                        overrideProtected, contextProtected, depth, cancellationToken)
                        .ConfigureAwait(false);
                }
                termIriMapping = ExpandCompactIriTerm(activeContext, termName);
            }
            else if(termName == "@type")
            {
                termIriMapping = "@type";
            }
            else
            {
                termIriMapping = activeContext.ExpandIri(termName, vocab: true);

                //A term with no @id and no colon must expand through a
                //vocabulary mapping to an absolute IRI (or keyword); with no
                //vocabulary mapping it has no valid IRI mapping at all.
                if(termIriMapping is null
                    || (!IriUtils.IsAbsoluteIri(termIriMapping) && !IriUtils.IsKeyword(termIriMapping)))
                {
                    throw new LinkedDataProcessingException(
                        "invalid IRI mapping",
                        string.Create(CultureInfo.InvariantCulture, $"Term '{termName}' has no @id and cannot be expanded to an IRI."));
                }
            }
        }

        //@container (non-reverse path).
        if(!value.Reverse && value.Containers is not null)
        {
            foreach(string container in value.Containers)
            {
                if(!IsValidContainerKeyword(container))
                {
                    throw new LinkedDataProcessingException(
                        "invalid container mapping",
                        string.Create(CultureInfo.InvariantCulture, $"Invalid @container value '{container}' for term '{termName}'."));
                }
                containerMapping.Add(container);
            }

            //@list is exclusive: it may not be combined with any other
            //container keyword.
            if(containerMapping.Contains("@list") && containerMapping.Count > 1)
            {
                throw new LinkedDataProcessingException(
                    "invalid container mapping",
                    string.Create(CultureInfo.InvariantCulture, $"@container for term '{termName}' may not combine @list with another container keyword."));
            }
        }

        //A @type-container term may only coerce values to @id or @vocab.
        if(containerMapping.Contains("@type") && typeMapping is not null && typeMapping is not ("@id" or "@vocab"))
        {
            throw new LinkedDataProcessingException(
                "invalid type mapping",
                string.Create(CultureInfo.InvariantCulture, $"A @type-container term '{termName}' may only set @type to @id or @vocab."));
        }

        //A property-valued @index requires an @index container and a
        //non-keyword index property.
        if(indexMapping is not null
            && (!containerMapping.Contains("@index") || IriUtils.IsKeyword(indexMapping)))
        {
            throw new LinkedDataProcessingException(
                "invalid term definition",
                string.Create(CultureInfo.InvariantCulture, $"@index in term definition for '{termName}' requires an @index container and a non-keyword value."));
        }

        //@prefix - if explicitly set, validate. A compact-IRI-shaped term
        //(containing ':' or '/') may not be flagged a prefix, and a term whose
        //IRI maps to a keyword may not be a prefix (W3C JSON-LD 1.1 §4.1.2).
        if(value.Prefix)
        {
            if(termName.Contains(':', StringComparison.Ordinal) || termName.Contains('/', StringComparison.Ordinal))
            {
                throw new LinkedDataProcessingException(
                    "invalid term definition",
                    string.Create(CultureInfo.InvariantCulture, $"@prefix may not be used on the compact-IRI term '{termName}'."));
            }
            if(!IsPrefixCompatibleWithIri(termIriMapping))
            {
                throw new LinkedDataProcessingException(
                    "invalid term definition",
                    string.Create(CultureInfo.InvariantCulture, $"Term '{termName}' maps to a keyword and cannot be a prefix."));
            }
        }

        //@language with normalisation.
        if(hasLanguageMapping)
        {
            languageMapping = NormalizeTermLanguage(value.Language);
        }

        //@direction must be 'ltr', 'rtl', or null.
        if(hasDirectionMapping)
        {
            directionMapping = value.Direction switch
            {
                null or "ltr" or "rtl" => value.Direction,
                _ => throw new LinkedDataProcessingException(
                    "invalid base direction",
                    string.Create(CultureInfo.InvariantCulture, $"@direction in term definition for '{termName}' must be 'ltr', 'rtl', or null."))
            };
        }

        //@nest validity check.
        if(nestValue is not null && !IsValidNestValue(nestValue))
        {
            throw new LinkedDataProcessingException(
                "invalid @nest value",
                string.Create(CultureInfo.InvariantCulture, $"@nest value '{nestValue}' in term definition for '{termName}' must be '@nest' or a non-keyword term."));
        }

        //Scoped @context: recursively pre-validate so spec errors fire
        //at term-definition time. The scoped-context POCOs themselves
        //ride on the resulting TermDefinition.ScopedContextEntries
        //(set below). Consumers (JSON-LD expander, CBOR-LD scope helper)
        //re-apply the entries at use-site against the then-current
        //active context.
        if(value.ScopedContext is not null)
        {
            //Pre-validate scoped context by attempting to apply it
            //into a throw-away context (catches errors early at
            //term-definition time per spec).
            //A scoped context is applied at use-site with override protected
            //enabled, so pre-validation mirrors that (a scoped null context
            //may clear protected terms).
            _ = await ApplyEmbeddedContextsCoreAsync(
                activeContext, value.ScopedContext, baseUrl,
                fetcher, parser, cache, overrideProtected: true, propagate: true,
                remoteContexts, depth + 1, cancellationToken).ConfigureAwait(false);
        }

        TermDefinition termDefinition = new()
        {
            IriMapping = termIriMapping,
            Prefix = isPrefix,
            TypeMapping = typeMapping,
            LanguageMapping = languageMapping,
            HasLanguageMapping = hasLanguageMapping,
            DirectionMapping = directionMapping,
            HasDirectionMapping = hasDirectionMapping,
            ContainerMapping = containerMapping,
            Protected = isProtected,
            ReverseProperty = isReverse ? reverseProperty : null,
            NestValue = nestValue,
            IndexMapping = indexMapping,
            ScopedContextEntries = value.ScopedContext
        };

        activeContext = SetTermDefinitionInternal(activeContext, termName, termDefinition, overrideProtected);
        defined[termName] = true;
        return activeContext;
    }

    /// <summary>
    /// Enforces the W3C JSON-LD 1.1 §4.1.2 rule that a term whose name has the
    /// form of an IRI must expand to the same IRI as its <c>@id</c> mapping.
    /// The check runs only when the raw <c>@id</c> differs from the term name;
    /// when the term contains a colon, its prefix is defined first so the
    /// term's own expansion is accurate. A mismatch is an invalid IRI mapping.
    /// </summary>
    private static async ValueTask<LinkedDataContext> EnsureTermIriFormConsistentAsync(
        LinkedDataContext activeContext,
        IReadOnlyDictionary<string, LinkedDataTermSource> localContext,
        string termName,
        string? rawId,
        string? expandedId,
        string? baseUrl,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        List<string> remoteContexts,
        Dictionary<string, bool> defined,
        bool overrideProtected,
        bool contextProtected,
        int depth,
        CancellationToken cancellationToken)
    {
        if(rawId is null
            || string.Equals(rawId, termName, StringComparison.Ordinal)
            || !TermHasIriForm(termName))
        {
            return activeContext;
        }

        int colon = termName.IndexOf(':', StringComparison.Ordinal);
        if(colon > 0)
        {
            string prefix = termName[..colon];
            if(localContext.ContainsKey(prefix)
                && (!defined.TryGetValue(prefix, out bool prefixDone) || !prefixDone))
            {
                activeContext = await CreateTermDefinitionCoreAsync(
                    activeContext, localContext, prefix, baseUrl,
                    fetcher, parser, cache, remoteContexts, defined,
                    overrideProtected, contextProtected, depth, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        string? termIri = activeContext.ExpandIri(termName, vocab: true);
        if(!string.Equals(termIri, expandedId, StringComparison.Ordinal))
        {
            throw new LinkedDataProcessingException(
                "invalid IRI mapping",
                string.Create(CultureInfo.InvariantCulture, $"Term '{termName}' has the form of an IRI but does not expand to its @id mapping."));
        }

        return activeContext;
    }

    /// <summary>
    /// Indicates whether <paramref name="term"/> has the form of an IRI per the
    /// W3C JSON-LD 1.1 regex <c>(?::[^:])|\/</c>: it contains a slash, or a
    /// colon followed by a non-colon character.
    /// </summary>
    private static bool TermHasIriForm(string term)
    {
        if(term.Contains('/', StringComparison.Ordinal))
        {
            return true;
        }

        int colon = term.IndexOf(':', StringComparison.Ordinal);
        while(colon >= 0 && colon + 1 < term.Length)
        {
            if(term[colon + 1] != ':')
            {
                return true;
            }

            colon = term.IndexOf(':', colon + 1);
        }

        return false;
    }

    private static LinkedDataContext SetTermDefinitionInternal(
        LinkedDataContext activeContext,
        string term,
        TermDefinition definition,
        bool overrideProtected)
    {
        LinkedDataContext? updated = TrySetTermDefinition(
            activeContext, term, definition, overrideProtected, out _);
        return updated ?? throw new LinkedDataProcessingException(
            "protected term redefinition",
            string.Create(CultureInfo.InvariantCulture, $"Cannot redefine protected term '{term}' with a different IRI mapping."));
    }
}
