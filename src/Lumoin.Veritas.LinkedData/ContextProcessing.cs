using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Static implementations of the format-agnostic active-context processing
/// algorithm steps from W3C JSON-LD 1.1 §4.1 and W3C CBOR-LD 1.0 §5.3.
/// Methods here are pure transforms over
/// <see cref="LinkedDataContext"/> and primitive inputs.
/// Format-specific shells extract the inputs from their tree and call
/// these methods; the shells own format-specific error reporting and
/// exception wrapping.
/// </summary>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#context-processing-algorithm"/>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#active-context-processing"/>
public static partial class ContextProcessing
{
    private static FrozenSet<string> ValidContainerKeywords { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            JsonLdKeywords.List, JsonLdKeywords.Set, JsonLdKeywords.Language, JsonLdKeywords.Index, JsonLdKeywords.Id, JsonLdKeywords.Graph, JsonLdKeywords.Type, JsonLdKeywords.None
        }.ToFrozenSet(StringComparer.Ordinal);

    private static FrozenSet<string> ValidReverseContainerKeywords { get; } =
        new HashSet<string>(StringComparer.Ordinal) { JsonLdKeywords.Set, JsonLdKeywords.Index }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Indicates whether <paramref name="candidate"/> is one of the eight
    /// keywords accepted as a JSON-LD <c>@container</c> mapping value:
    /// <c>@list</c>, <c>@set</c>, <c>@language</c>, <c>@index</c>,
    /// <c>@id</c>, <c>@graph</c>, <c>@type</c>, <c>@none</c>.
    /// </summary>
    /// <param name="candidate">The string to test.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is a valid container keyword.</returns>
    public static bool IsValidContainerKeyword(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return ValidContainerKeywords.Contains(candidate);
    }

    /// <summary>
    /// Indicates whether <paramref name="candidate"/> is acceptable as the
    /// <c>@container</c> value for a reverse-property term definition.
    /// Per W3C JSON-LD 1.1 §4.1.2 only <c>@set</c> and <c>@index</c> are
    /// permitted in that position.
    /// </summary>
    /// <param name="candidate">The string to test; <see langword="null"/> is treated as "no container specified" and returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is acceptable as a reverse-property container.</returns>
    public static bool IsValidReverseContainerKeyword(string? candidate)
    {
        return candidate is null || ValidReverseContainerKeywords.Contains(candidate);
    }

    /// <summary>
    /// Attempts to reset <paramref name="activeContext"/> to the empty
    /// context per W3C JSON-LD 1.1 §4.1.4 step 4.1 (a <c>null</c> context
    /// entry). When <paramref name="overrideProtected"/> is <see langword="false"/>
    /// and the active context contains any protected term, the reset
    /// fails: the method returns <see langword="null"/> and emits the
    /// offending term name through <paramref name="protectedTermName"/>.
    /// The format-specific caller is responsible for translating that
    /// failure into its own processing exception.
    /// </summary>
    /// <param name="activeContext">The context to reset.</param>
    /// <param name="overrideProtected">When <see langword="true"/>, protected
    /// terms may be discarded; otherwise their presence aborts the reset.</param>
    /// <param name="protectedTermName">On failure, receives the name of the
    /// first protected term that would be overridden; <see langword="null"/>
    /// on success.</param>
    /// <returns>The reset context on success, or <see langword="null"/>
    /// when a protected term would be overridden.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="activeContext"/> is <see langword="null"/>.</exception>
    public static LinkedDataContext? TryResetToNull(
        LinkedDataContext activeContext,
        bool overrideProtected,
        out string? protectedTermName)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        if(!overrideProtected)
        {
            foreach(string term in activeContext.Terms)
            {
                if(activeContext.TryGetTerm(term, out TermDefinition? def) && def?.Protected == true)
                {
                    protectedTermName = term;
                    return null;
                }
            }
        }

        protectedTermName = null;

        //A null context resets to a fresh context, but both the base IRI and
        //the original base URL are restored to the active context's original
        //base URL (W3C JSON-LD 1.1 §4.1 step 5.1), so document-relative IRIs
        //still resolve against the document base after the reset.
        LinkedDataContext empty = LinkedDataContext.Empty
            .WithOriginalBaseUrl(activeContext.OriginalBaseUrl)
            .WithBaseIri(activeContext.OriginalBaseUrl);
        if(!activeContext.Propagate)
        {
            empty = empty.WithPreviousContext(activeContext);
        }

        return empty;
    }

    /// <summary>
    /// Attempts to apply <paramref name="definition"/> for term
    /// <paramref name="term"/> on top of <paramref name="activeContext"/>.
    /// When the existing term in the context is marked
    /// <see cref="TermDefinition.Protected"/> and <paramref name="overrideProtected"/>
    /// is <see langword="false"/>, the IRI mapping of the new definition
    /// must match the existing one; otherwise the operation fails and
    /// emits the conflict detail through
    /// <paramref name="existingIriMapping"/>. Failure callers translate
    /// to their own processing exception.
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="term">The term name to set.</param>
    /// <param name="definition">The new term definition.</param>
    /// <param name="overrideProtected">When <see langword="true"/>, protected
    /// terms may be redefined unconditionally.</param>
    /// <param name="existingIriMapping">On protected-redefinition failure,
    /// receives the existing IRI mapping; <see langword="null"/> on success.</param>
    /// <returns>The updated context on success, or <see langword="null"/>
    /// when a protected term would be redefined with a different IRI.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static LinkedDataContext? TrySetTermDefinition(
        LinkedDataContext activeContext,
        string term,
        TermDefinition definition,
        bool overrideProtected,
        out string? existingIriMapping)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(definition);

        if(activeContext.TryGetTerm(term, out TermDefinition? existing)
            && existing?.Protected == true
            && !overrideProtected)
        {
            //Protected term: a redefinition is accepted only when the new
            //definition is identical to the existing one apart from the
            //@protected flag itself (W3C JSON-LD 1.1 §4.1.2 step on protected
            //term redefinition).
            if(!AreTermDefinitionsEquivalentIgnoringProtected(existing, definition))
            {
                existingIriMapping = existing.IriMapping;
                return null;
            }
            existingIriMapping = null;
            return activeContext;
        }

        existingIriMapping = null;
        return activeContext.WithTerm(term, definition);
    }

    /// <summary>
    /// Compares two term definitions for equality across every field that
    /// distinguishes a protected redefinition, ignoring only the
    /// <see cref="TermDefinition.Protected"/> flag. Scoped contexts are
    /// compared by presence (a definition that gains or loses a scoped context
    /// is a different definition).
    /// </summary>
    private static bool AreTermDefinitionsEquivalentIgnoringProtected(TermDefinition a, TermDefinition b)
    {
        return string.Equals(a.IriMapping, b.IriMapping, StringComparison.Ordinal)
            && a.HasNullMapping == b.HasNullMapping
            && string.Equals(a.TypeMapping, b.TypeMapping, StringComparison.Ordinal)
            && string.Equals(a.LanguageMapping, b.LanguageMapping, StringComparison.Ordinal)
            && a.HasLanguageMapping == b.HasLanguageMapping
            && string.Equals(a.DirectionMapping, b.DirectionMapping, StringComparison.Ordinal)
            && a.HasDirectionMapping == b.HasDirectionMapping
            && string.Equals(a.ReverseProperty, b.ReverseProperty, StringComparison.Ordinal)
            && string.Equals(a.NestValue, b.NestValue, StringComparison.Ordinal)
            && string.Equals(a.IndexMapping, b.IndexMapping, StringComparison.Ordinal)
            && a.Prefix == b.Prefix
            && (a.ScopedContextEntries is null) == (b.ScopedContextEntries is null)
            && a.ContainerMapping.SequenceEqual(b.ContainerMapping, StringComparer.Ordinal);
    }

    /// <summary>
    /// Applies a JSON-LD <c>@base</c> entry to <paramref name="activeContext"/>.
    /// <paramref name="baseValue"/> is the already-extracted string value
    /// (<see langword="null"/> resets the base IRI). Absolute IRIs become
    /// the new base directly; relative IRIs are resolved against the
    /// existing base; a relative value with no existing base fails with
    /// <see langword="null"/> returned and the offending relative value
    /// emitted through <paramref name="unresolvableRelative"/>.
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="baseValue">The extracted base value; <see langword="null"/> clears the base.</param>
    /// <param name="unresolvableRelative">On failure, receives the relative value that has no base to resolve against; <see langword="null"/> on success.</param>
    /// <returns>The updated context on success, or <see langword="null"/> on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="activeContext"/> is <see langword="null"/>.</exception>
    public static LinkedDataContext? TryApplyBase(
        LinkedDataContext activeContext,
        string? baseValue,
        out string? unresolvableRelative)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        unresolvableRelative = null;
        if(baseValue is null)
        {
            return activeContext.WithBaseIri(null);
        }
        if(IriUtils.IsAbsoluteIri(baseValue))
        {
            return activeContext.WithBaseIri(baseValue);
        }
        if(activeContext.BaseIri is not null)
        {
            return activeContext.WithBaseIri(IriUtils.ResolveIri(activeContext.BaseIri, baseValue));
        }
        unresolvableRelative = baseValue;
        return null;
    }

    /// <summary>
    /// Applies a JSON-LD <c>@vocab</c> entry to <paramref name="activeContext"/>.
    /// <paramref name="vocabValue"/> is the already-extracted string value
    /// (<see langword="null"/> clears the vocabulary mapping). Absolute
    /// IRIs and blank-node-like values (those starting with <c>_</c>) are
    /// applied directly; other values are expanded against the current
    /// context's vocabulary mapping or base before being stored.
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="vocabValue">The extracted vocab value; <see langword="null"/> clears the mapping.</param>
    /// <returns>The updated context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="activeContext"/> is <see langword="null"/>.</exception>
    public static LinkedDataContext ApplyVocab(
        LinkedDataContext activeContext,
        string? vocabValue)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        if(vocabValue is null)
        {
            return activeContext.WithVocabularyMapping(null);
        }

        //@vocab is expanded both vocab-relative (so a relative value chains
        //off a prior @vocab by concatenation) and document-relative (so an
        //empty or relative value resolves against the base IRI). IRI
        //expansion already handles absolute IRIs, compact IRIs (ex:ns/ via
        //the prefix mapping), and blank-node identifiers.
        string? expanded = activeContext.ExpandIri(vocabValue, vocab: true, documentRelative: true);
        return activeContext.WithVocabularyMapping(expanded ?? vocabValue);
    }

    /// <summary>
    /// Applies a JSON-LD <c>@language</c> entry to <paramref name="activeContext"/>.
    /// <paramref name="languageValue"/> is the already-extracted string
    /// value (<see langword="null"/> clears the default language). The
    /// language tag is normalised to lowercase per BCP47 and JSON-LD 1.1.
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="languageValue">The extracted language tag; <see langword="null"/> clears the default.</param>
    /// <returns>The updated context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="activeContext"/> is <see langword="null"/>.</exception>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "BCP47 language tags are canonically lowercase per the JSON-LD 1.1 specification.")]
    public static LinkedDataContext ApplyLanguage(
        LinkedDataContext activeContext,
        string? languageValue)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        if(languageValue is null)
        {
            return activeContext.WithDefaultLanguage(null);
        }
        return activeContext.WithDefaultLanguage(languageValue.ToLowerInvariant());
    }

    /// <summary>
    /// Applies a JSON-LD <c>@direction</c> entry to <paramref name="activeContext"/>.
    /// Valid values are <c>"ltr"</c>, <c>"rtl"</c>, or <see langword="null"/>;
    /// any other value fails with <see langword="null"/> returned.
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="directionValue">The extracted direction value.</param>
    /// <returns>The updated context on success, or <see langword="null"/> when the value is invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="activeContext"/> is <see langword="null"/>.</exception>
    public static LinkedDataContext? TryApplyDirection(
        LinkedDataContext activeContext,
        string? directionValue)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        if(directionValue is null)
        {
            return activeContext.WithDefaultBaseDirection(null);
        }
        if(directionValue is "ltr" or "rtl")
        {
            return activeContext.WithDefaultBaseDirection(directionValue);
        }
        return null;
    }

    /// <summary>
    /// Expands and validates a JSON-LD <c>@type</c> term-mapping value.
    /// The four reserved keywords <c>@id</c>, <c>@vocab</c>, <c>@json</c>,
    /// and <c>@none</c> are returned unchanged; other values are expanded
    /// via the active context's vocabulary and must resolve to either an
    /// absolute IRI or a JSON-LD keyword. Returns the expanded value on
    /// success, or <see langword="null"/> when the value cannot be resolved
    /// to an acceptable IRI.
    /// </summary>
    /// <param name="activeContext">The active context used for vocabulary expansion.</param>
    /// <param name="typeValue">The raw <c>@type</c> string from the term definition.</param>
    /// <returns>The expanded type IRI, or <see langword="null"/> on validation failure.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static string? TryExpandTermType(LinkedDataContext activeContext, string typeValue)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(typeValue);

        if(JsonLdKeywords.IsId(typeValue) || JsonLdKeywords.IsVocab(typeValue) || JsonLdKeywords.IsJson(typeValue) || JsonLdKeywords.IsNone(typeValue))
        {
            return typeValue;
        }
        string? expanded = activeContext.ExpandIri(typeValue, vocab: true);
        if(expanded is null)
        {
            return null;
        }
        if(!IriUtils.IsAbsoluteIri(expanded) && !IriUtils.IsKeyword(expanded))
        {
            return null;
        }
        return expanded;
    }

    /// <summary>
    /// Expands and validates a JSON-LD <c>@reverse</c> property mapping
    /// value. The value is expanded via the active context's vocabulary
    /// and must resolve to an absolute IRI. Returns the expanded IRI on
    /// success, or <see langword="null"/> when validation fails.
    /// </summary>
    /// <param name="activeContext">The active context used for vocabulary expansion.</param>
    /// <param name="reverseValue">The raw <c>@reverse</c> string from the term definition.</param>
    /// <returns>The expanded reverse IRI, or <see langword="null"/> on validation failure.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static string? TryExpandReverseProperty(LinkedDataContext activeContext, string reverseValue)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(reverseValue);

        string? expanded = activeContext.ExpandIri(reverseValue, vocab: true);
        if(expanded is null || !IriUtils.IsAbsoluteIri(expanded))
        {
            return null;
        }
        return expanded;
    }

    /// <summary>
    /// Indicates whether a term definition's <c>@prefix</c>: <see langword="true"/>
    /// assertion is compatible with its IRI mapping per W3C JSON-LD 1.1
    /// §4.1.2: a term whose IRI mapping is a JSON-LD keyword cannot be
    /// marked as a prefix. Returns <see langword="true"/> when the
    /// assertion is acceptable, <see langword="false"/> when the term
    /// must not be flagged as a prefix.
    /// </summary>
    /// <param name="iriMapping">The expanded IRI mapping for the term, or <see langword="null"/> if the term has none.</param>
    /// <returns><see langword="true"/> when <c>@prefix</c>: true is acceptable; <see langword="false"/> when the IRI maps to a keyword.</returns>
    public static bool IsPrefixCompatibleWithIri(string? iriMapping)
    {
        return !IriUtils.IsKeyword(iriMapping);
    }

    /// <summary>
    /// Normalises a JSON-LD term-definition <c>@language</c> value. The
    /// tag is lowercased per BCP47 / JSON-LD 1.1 §4.1.2;
    /// <see langword="null"/> is preserved (clears the language mapping).
    /// </summary>
    /// <param name="languageValue">The extracted language tag value.</param>
    /// <returns>The normalised language tag, or <see langword="null"/>.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "BCP47 language tags are canonically lowercase per the JSON-LD 1.1 specification.")]
    public static string? NormalizeTermLanguage(string? languageValue)
    {
        return languageValue?.ToLowerInvariant();
    }

    /// <summary>
    /// Indicates whether a JSON-LD <c>@nest</c> value is acceptable per
    /// W3C JSON-LD 1.1 §4.1.2: the value must be either the literal
    /// <c>"@nest"</c> keyword or a non-keyword string.
    /// </summary>
    /// <param name="nestValue">The extracted <c>@nest</c> value.</param>
    /// <returns><see langword="true"/> when the value is acceptable; <see langword="false"/> when it is a keyword other than <c>@nest</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nestValue"/> is <see langword="null"/>.</exception>
    public static bool IsValidNestValue(string nestValue)
    {
        ArgumentNullException.ThrowIfNull(nestValue);
        return JsonLdKeywords.IsNest(nestValue) || !IriUtils.IsKeyword(nestValue);
    }

    /// <summary>
    /// Expands an <c>@id</c> value into the IRI mapping for a term
    /// definition per W3C JSON-LD 1.1 §4.1.2. When the <paramref name="idValue"/>
    /// is a keyword identical to <paramref name="term"/>, the keyword is
    /// returned unchanged (self-mapping). Otherwise the value is expanded
    /// against the active context's vocabulary; a result that is neither
    /// an absolute IRI nor a keyword is rejected by returning
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="activeContext">The active context used for vocabulary expansion.</param>
    /// <param name="term">The term being defined.</param>
    /// <param name="idValue">The raw <c>@id</c> value from the term definition.</param>
    /// <returns>The expanded IRI mapping, or <see langword="null"/> when expansion fails to produce an absolute IRI or keyword.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static string? ExpandTermIdMapping(LinkedDataContext activeContext, string term, string idValue)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(idValue);

        if(IriUtils.IsKeyword(idValue) && idValue == term)
        {
            return idValue;
        }
        string? expanded = activeContext.ExpandIri(idValue, vocab: true);
        if(expanded is null)
        {
            return null;
        }
        if(!IriUtils.IsAbsoluteIri(expanded) && !IriUtils.IsKeyword(expanded))
        {
            return null;
        }
        return expanded;
    }

    /// <summary>
    /// Indicates whether the shape of <paramref name="term"/> implies it
    /// should be treated as a prefix term, per W3C JSON-LD 1.1 §4.1.2:
    /// a term ending in <c>':'</c>, <c>'/'</c>, or <c>'?'</c> is
    /// implicitly a prefix when it has an IRI mapping.
    /// </summary>
    /// <param name="term">The term name.</param>
    /// <param name="hasIriMapping">Whether the term has a non-null IRI mapping.</param>
    /// <returns><see langword="true"/> when the term should be flagged as a prefix.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <see langword="null"/>.</exception>
    public static bool TermShapeIndicatesPrefix(string term, bool hasIriMapping)
    {
        ArgumentNullException.ThrowIfNull(term);
        return hasIriMapping && (term.EndsWith(':') || term.EndsWith('/') || term.EndsWith('?'));
    }

    /// <summary>
    /// Expands a compact-IRI term of the form <c>"prefix:suffix"</c>
    /// against <paramref name="activeContext"/>'s already-defined prefix
    /// mapping. When the prefix is defined and has an IRI mapping, the
    /// returned value is <c>prefixIri + suffix</c>; otherwise the term
    /// is returned unchanged (the JSON-LD spec leaves such terms as
    /// no-op IRIs that will be rejected or resolved at use time).
    /// </summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="term">The compact-IRI term (must contain a colon).</param>
    /// <returns>The expanded IRI when the prefix is defined; otherwise the term unchanged.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="term"/> does not contain a colon.</exception>
    public static string ExpandCompactIriTerm(LinkedDataContext activeContext, string term)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(term);

        int colonPos = term.IndexOf(':', StringComparison.Ordinal);
        if(colonPos < 0)
        {
            throw new ArgumentException("Term does not contain a colon; ExpandCompactIriTerm is only valid for compact-IRI terms.", nameof(term));
        }
        string prefix = term[..colonPos];
        string suffix = term[(colonPos + 1)..];

        if(activeContext.TryGetTerm(prefix, out TermDefinition? prefixDef)
            && prefixDef?.IriMapping is not null)
        {
            return prefixDef.IriMapping + suffix;
        }
        return term;
    }

    /// <summary>
    /// Inspects <paramref name="iriMapping"/> for a compact-IRI shape
    /// (<c>prefix:suffix</c> where the prefix is non-empty, neither a
    /// keyword nor the blank-node marker <c>"_"</c>, and the value does
    /// not start with <c>"//"</c> which would indicate a scheme-relative
    /// IRI). Returns the prefix when the shape matches and
    /// <see langword="null"/> otherwise. The caller uses the returned
    /// prefix to drive recursive term-definition resolution before
    /// final expansion.
    /// </summary>
    /// <param name="iriMapping">The candidate IRI mapping.</param>
    /// <returns>The prefix string when <paramref name="iriMapping"/> is a compact-IRI shape, otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="iriMapping"/> is <see langword="null"/>.</exception>
    public static string? TryGetCompactIriPrefix(string iriMapping)
    {
        ArgumentNullException.ThrowIfNull(iriMapping);

        int colonPos = iriMapping.IndexOf(':', StringComparison.Ordinal);
        if(colonPos <= 0)
        {
            return null;
        }

        //A "scheme://" value is an absolute IRI, not a compact IRI whose
        //suffix happens to begin with "//".
        string suffix = iriMapping[(colonPos + 1)..];
        if(suffix.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }
        string prefix = iriMapping[..colonPos];
        if(IriUtils.IsKeyword(prefix) || prefix == "_")
        {
            return null;
        }
        return prefix;
    }

    /// <summary>
    /// Builds a <see cref="TermDefinition"/> for the JSON-LD 1.1
    /// §4.1.2 simple-string form (<c>"term": "iriMapping"</c>): the IRI
    /// mapping is the active context's vocabulary expansion of
    /// <paramref name="iriMapping"/>; the term is implicitly a prefix
    /// when the original raw value ends with <c>':'</c> and does not
    /// expand to a keyword; protected-state inherits from the surrounding
    /// context.
    /// </summary>
    /// <param name="activeContext">The active context used for vocabulary expansion.</param>
    /// <param name="iriMapping">The raw simple-string value.</param>
    /// <param name="contextProtected">Whether the surrounding context is protected.</param>
    /// <returns>The resulting term definition.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static TermDefinition BuildSimpleStringTermDefinition(
        LinkedDataContext activeContext,
        string termName,
        string iriMapping,
        bool contextProtected)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(termName);
        ArgumentNullException.ThrowIfNull(iriMapping);

        string? expanded = activeContext.ExpandIri(iriMapping, vocab: true);
        return new TermDefinition
        {
            IriMapping = expanded,
            //A simple-string term may be used as a compact-IRI prefix only
            //when the term itself has no colon and its IRI mapping ends with a
            //gen-delim character (W3C JSON-LD 1.1 §4.1.2 Create Term Definition).
            Prefix = !IriUtils.IsKeyword(expanded)
                && !termName.Contains(':', StringComparison.Ordinal)
                && EndsWithGenDelim(expanded),
            Protected = contextProtected
        };
    }

    /// <summary>
    /// Indicates whether <paramref name="iri"/> ends with an RFC 3987
    /// gen-delim character (<c>: / ? # [ ] @</c>), the condition under which a
    /// simple-string term qualifies as a compact-IRI prefix.
    /// </summary>
    /// <param name="iri">The candidate IRI mapping, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the IRI ends with a gen-delim character.</returns>
    private static bool EndsWithGenDelim(string? iri)
    {
        return iri is { Length: > 0 } && iri[^1] is ':' or '/' or '?' or '#' or '[' or ']' or '@';
    }

    /// <summary>
    /// Attempts to remove <paramref name="term"/> from
    /// <paramref name="activeContext"/> per the JSON-LD 1.1 §4.1.2
    /// term-clearing rule. When the existing term is marked
    /// <see cref="TermDefinition.Protected"/> and
    /// <paramref name="overrideProtected"/> is <see langword="false"/>,
    /// removal fails: the method returns <see langword="null"/> and emits
    /// <see langword="true"/> through
    /// <paramref name="wasProtectedConflict"/>.
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="term">The term to remove.</param>
    /// <param name="overrideProtected">When <see langword="true"/>, protected terms may be removed unconditionally.</param>
    /// <param name="wasProtectedConflict">On failure, set to <see langword="true"/>.</param>
    /// <returns>The context without the term on success, or <see langword="null"/> on protected conflict.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static LinkedDataContext? TryRemoveTerm(
        LinkedDataContext activeContext,
        string term,
        bool overrideProtected,
        out bool wasProtectedConflict)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(term);

        if(activeContext.TryGetTerm(term, out TermDefinition? existing)
            && existing?.Protected == true
            && !overrideProtected)
        {
            wasProtectedConflict = true;
            return null;
        }
        wasProtectedConflict = false;
        return activeContext.WithoutTerm(term);
    }

    /// <summary>
    /// Re-expands a term's IRI mapping when, after subsequent context
    /// processing, the mapping is still in compact-IRI form (a prefix
    /// that has now become resolvable). Returns the updated context
    /// with the re-expanded term, or the unchanged context when no
    /// re-expansion is needed (because the term has no mapping, the
    /// mapping is already absolute or keyword, or expansion does not
    /// produce a different value).
    /// </summary>
    /// <param name="activeContext">The current context.</param>
    /// <param name="term">The term whose mapping to consider re-expanding.</param>
    /// <returns>The updated (or unchanged) context.</returns>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static LinkedDataContext TryReexpandCompactIriTerm(LinkedDataContext activeContext, string term)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(term);

        if(!activeContext.TryGetTerm(term, out TermDefinition? existing)
            || existing?.IriMapping is null
            || IriUtils.IsAbsoluteIri(existing.IriMapping)
            || IriUtils.IsKeyword(existing.IriMapping))
        {
            return activeContext;
        }

        string? reExpanded = activeContext.ExpandIri(existing.IriMapping, vocab: true);
        if(reExpanded is null || reExpanded == existing.IriMapping)
        {
            return activeContext;
        }

        TermDefinition updated = new()
        {
            IriMapping = reExpanded,
            Prefix = existing.Prefix,
            TypeMapping = existing.TypeMapping,
            LanguageMapping = existing.LanguageMapping,
            HasLanguageMapping = existing.HasLanguageMapping,
            ContainerMapping = existing.ContainerMapping,
            Protected = existing.Protected,
            ReverseProperty = existing.ReverseProperty,
            NestValue = existing.NestValue,
            IndexMapping = existing.IndexMapping,
            ScopedContextEntries = existing.ScopedContextEntries
        };
        return activeContext.WithTerm(term, updated);
    }

    /// <summary>
    /// Converts a parsed-dictionary term value (the shape produced by a
    /// <see cref="ParseRemoteContextDelegate"/>) into a
    /// <see cref="LinkedDataTermSource"/> POCO suitable for the term-
    /// definition algorithm. The four input shapes per JSON-LD 1.1 §4.1.2:
    /// a plain string for a simple-IRI term, <see langword="null"/> for
    /// a term-removal marker, a nested
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> for an expanded
    /// term definition, or anything else (rejected as invalid).
    /// </summary>
    /// <param name="termName">The term name (for diagnostics).</param>
    /// <param name="value">The parsed value.</param>
    /// <param name="syntheticKey">A stable deduplication key.</param>
    /// <returns>The converted term source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="termName"/> or <paramref name="syntheticKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The value is not one of the four valid shapes.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static LinkedDataTermSource ConvertToTermSource(
        string termName,
        object? value,
        string syntheticKey,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(termName);
        ArgumentNullException.ThrowIfNull(syntheticKey);

        if(value is null)
        {
            //Term-removal marker. Caller distinguishes via the otherwise-empty source.
            return new LinkedDataTermSource(syntheticKey);
        }

        if(value is string s)
        {
            return new LinkedDataTermSource(syntheticKey) { Iri = s };
        }

        if(value is IReadOnlyDictionary<string, object?> dict)
        {
            return ConvertExpandedTermSource(termName, dict, syntheticKey, baseUrl);
        }

        throw new InvalidOperationException(
            $"Term definition for '{termName}' must be a string, object, or null. Got {value.GetType().Name}.");
    }

    private static LinkedDataTermSource ConvertExpandedTermSource(
        string termName,
        IReadOnlyDictionary<string, object?> dict,
        string syntheticKey,
        string? baseUrl = null)
    {
        //Each term-definition field is read directly from the map by its
        //keyword key — a declarative projection in place of a per-field
        //dispatch switch. A term inside a REMOTE context may carry a scoped
        //@context (the inline JSON-LD shell extracts this through its own
        //walker, but remote contexts arrive through this format-neutral path);
        //its relative references resolve against the defining remote base URL.
        static string? Text(IReadOnlyDictionary<string, object?> source, string keyword) =>
            source.TryGetValue(keyword, out object? value) ? value as string : null;

        static bool Flag(IReadOnlyDictionary<string, object?> source, string keyword) =>
            source.TryGetValue(keyword, out object? value) && value is true;

        _ = termName;

        return new LinkedDataTermSource(syntheticKey)
        {
            Iri = Text(dict, JsonLdKeywords.Id),
            Type = Text(dict, JsonLdKeywords.Type),
            Containers = dict.TryGetValue(JsonLdKeywords.Container, out object? container) ? ConvertContainerValue(container) : null,
            Language = Text(dict, JsonLdKeywords.Language),
            HasLanguageMapping = dict.ContainsKey(JsonLdKeywords.Language),
            Direction = Text(dict, JsonLdKeywords.Direction),
            Reverse = dict.ContainsKey(JsonLdKeywords.Reverse),
            ReverseIri = Text(dict, JsonLdKeywords.Reverse),
            Protected = Flag(dict, JsonLdKeywords.Protected),
            Prefix = Flag(dict, JsonLdKeywords.Prefix),
            Nest = Text(dict, JsonLdKeywords.Nest),
            Index = Text(dict, JsonLdKeywords.Index),
            ScopedContext = dict.TryGetValue(JsonLdKeywords.Context, out object? scoped)
                ? scoped switch
                {
                    IReadOnlyDictionary<string, object?> contextMap => ConvertParsedContextToEntries(contextMap, baseUrl),
                    _ => ConvertWrappedContextValue(scoped, baseUrl)
                }
                : null
        };
    }

    private static List<string> ConvertContainerValue(object? value)
    {
        List<string> result = [];
        switch(value)
        {
            case string s:
            {
                result.Add(s);
                break;
            }
            case IReadOnlyList<object?> list:
            {
                foreach(object? element in list)
                {
                    if(element is string el)
                    {
                        result.Add(el);
                    }
                }
                break;
            }
            default:
            {
                break;
            }
        }
        return result;
    }
}
