using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Utilities for IRI manipulation in JSON-LD processing.
/// </summary>
internal static class IriUtils
{
    private static readonly HashSet<string> Keywords =
    [
        "@base", "@container", "@context", "@direction", "@graph", "@id",
        "@import", "@included", "@index", "@json", "@language", "@list",
        "@nest", "@none", "@prefix", "@propagate", "@protected", "@reverse",
        "@set", "@type", "@value", "@version", "@vocab"
    ];

    /// <summary>
    /// Determines whether a string is a JSON-LD keyword.
    /// </summary>
    internal static bool IsKeyword(string? value)
    {
        return value is not null && Keywords.Contains(value);
    }

    /// <summary>
    /// Determines whether a string looks like a JSON-LD keyword (starts with '@'
    /// and contains only alpha characters after). Used for keyword-like detection.
    /// </summary>
    internal static bool IsKeywordLike(string value)
    {
        if(value.Length < 2 || value[0] != '@')
        {
            return false;
        }

        for(int i = 1; i < value.Length; i++)
        {
            if(!char.IsAsciiLetter(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a string is an absolute IRI (has a scheme).
    /// </summary>
    internal static bool IsAbsoluteIri(string value)
    {
        //An absolute IRI has a scheme followed by ':'.
        //The scheme must start with a letter and contain only letters, digits, '+', '-', or '.'.
        int colonIndex = value.IndexOf(':', StringComparison.Ordinal);
        if(colonIndex <= 0)
        {
            return false;
        }

        for(int i = 0; i < colonIndex; i++)
        {
            char c = value[i];
            if(i == 0 && !char.IsAsciiLetter(c))
            {
                return false;
            }

            if(i > 0 && !char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a string is a relative IRI (does not start with a scheme or '#').
    /// </summary>
    internal static bool IsRelativeIri(string value)
    {
        return !IsAbsoluteIri(value) && !IsKeyword(value);
    }

    /// <summary>
    /// Resolves a relative IRI against a base IRI.
    /// </summary>
    /// <param name="baseIri">The base IRI to resolve against.</param>
    /// <param name="relativeIri">The relative IRI to resolve.</param>
    /// <returns>The resolved absolute IRI, or the relative IRI if resolution fails.</returns>
    internal static string ResolveIri(string baseIri, string relativeIri)
    {
        if(string.IsNullOrEmpty(relativeIri))
        {
            return baseIri;
        }

        if(IsAbsoluteIri(relativeIri))
        {
            return relativeIri;
        }

        try
        {
            Uri baseUri = new(baseIri);
            Uri resolved = new(baseUri, relativeIri);

            return resolved.ToString();
        }
        catch(UriFormatException)
        {
            //If resolution fails, return the relative IRI as-is.
            return relativeIri;
        }
    }

    /// <summary>
    /// Expands a compact IRI, keyword, or term against the active context.
    /// </summary>
    /// <param name="value">The value to expand.</param>
    /// <param name="context">The active context.</param>
    /// <param name="vocab">
    /// When <c>true</c>, expansion uses the vocabulary mapping for unknown terms.
    /// When <c>false</c>, unknown terms expand using the base IRI.
    /// </param>
    /// <param name="documentRelative">
    /// When <c>true</c>, relative IRIs are resolved against the base IRI.
    /// </param>
    /// <returns>The expanded IRI or keyword, or <c>null</c> if the value cannot be expanded.</returns>
    internal static string? ExpandIri(
        string? value,
        JsonLdContext context,
        bool vocab = false,
        bool documentRelative = false)
    {
        if(value is null || IsKeyword(value))
        {
            return value;
        }

        //If a keyword-like value appears that is not a known keyword, return null.
        if(IsKeywordLike(value))
        {
            return null;
        }

        //If the value is in the term definitions, resolve it.
        if(context.TryGetTerm(value, out TermDefinition? definition))
        {
            if(definition?.IriMapping is { } iriMapping && IsKeyword(iriMapping))
            {
                return iriMapping;
            }

            if(definition?.IriMapping is not null)
            {
                //Only return the mapping directly if it is already an absolute IRI or a keyword.
                //If it is still a compact IRI (e.g. "foaf:name" stored before foaf was defined),
                //fall through to the compact IRI expansion below.
                if(IsAbsoluteIri(definition.IriMapping) || IsKeyword(definition.IriMapping))
                {
                    return definition.IriMapping;
                }
            }
        }

        //Handle compact IRIs of the form prefix:suffix.
        int colonIndex = value.IndexOf(':', StringComparison.Ordinal);
        if(colonIndex > 0)
        {
            string prefix = value[..colonIndex];
            string suffix = value[(colonIndex + 1)..];

            //_: is the blank node prefix — do not expand.
            if(prefix == "_")
            {
                return value;
            }

            //A double-slash indicates an absolute IRI.
            if(suffix.StartsWith("//", StringComparison.Ordinal))
            {
                return value;
            }

            if(context.TryGetTerm(prefix, out TermDefinition? prefixDefinition)
                && prefixDefinition?.IriMapping is not null
                && !IsKeyword(prefixDefinition.IriMapping))
            {
                return prefixDefinition.IriMapping + suffix;
            }

            //If the prefix is not a mapped term, check if the whole value is an absolute IRI.
            if(IsAbsoluteIri(value))
            {
                return value;
            }
        }

        //Vocabulary-relative expansion.
        if(vocab && context.VocabularyMapping is not null)
        {
            return context.VocabularyMapping + value;
        }

        //Document-relative expansion.
        if(documentRelative && context.BaseIri is not null)
        {
            return ResolveIri(context.BaseIri, value);
        }

        return value;
    }
}
