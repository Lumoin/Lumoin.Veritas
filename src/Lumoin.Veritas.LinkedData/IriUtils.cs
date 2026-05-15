using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Pure IRI utilities shared by JSON-LD, CBOR-LD, and any other Linked
/// Data format. Operates on strings only; does not consult an active
/// context. Context-aware IRI expansion lives on <see cref="JsonLdContext"/>
/// as an instance method.
/// </summary>
public static class IriUtils
{
    private static HashSet<string> Keywords { get; } =
    [
        "@base", "@container", "@context", "@direction", "@graph", "@id",
        "@import", "@included", "@index", "@json", "@language", "@list",
        "@nest", "@none", "@prefix", "@propagate", "@protected", "@reverse",
        "@set", "@type", "@value", "@version", "@vocab"
    ];

    /// <summary>Determines whether a string is a JSON-LD keyword.</summary>
    public static bool IsKeyword(string? value)
    {
        return value is not null && Keywords.Contains(value);
    }

    /// <summary>
    /// Determines whether a string looks like a JSON-LD keyword: starts
    /// with <c>'@'</c> and is followed only by ASCII letters. Used for
    /// reserved-keyword-shape detection on otherwise-unknown strings.
    /// </summary>
    public static bool IsKeywordLike(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
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

    /// <summary>Determines whether a string is an absolute IRI (carries a scheme).</summary>
    public static bool IsAbsoluteIri(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        //An absolute IRI has a scheme followed by ':'. The scheme starts with a
        //letter and contains only letters, digits, '+', '-', or '.'.
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

    /// <summary>Determines whether a string is a relative IRI (not absolute, not a keyword).</summary>
    public static bool IsRelativeIri(string value)
    {
        return !IsAbsoluteIri(value) && !IsKeyword(value);
    }

    /// <summary>
    /// Resolves a relative IRI against a base IRI per RFC 3986 §5. If the
    /// supplied <paramref name="relativeIri"/> is empty, returns the base.
    /// If it is already absolute, returns it unchanged. If resolution
    /// fails for any reason, returns the relative IRI as-is rather than
    /// throwing.
    /// </summary>
    /// <param name="baseIri">The base IRI to resolve against.</param>
    /// <param name="relativeIri">The relative IRI to resolve.</param>
    /// <returns>The resolved IRI.</returns>
    public static string ResolveIri(string baseIri, string relativeIri)
    {
        ArgumentNullException.ThrowIfNull(baseIri);
        ArgumentNullException.ThrowIfNull(relativeIri);

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
            //If resolution fails, return the relative IRI as-is rather than crashing
            //the surrounding pipeline. The caller can detect non-resolution because
            //the result equals the input.
            return relativeIri;
        }
    }
}
