using System;

namespace Lumoin.Veritas.Core.Iris;

/// <summary>
/// Format-agnostic IRI resolution shared by every syntax layer (Turtle,
/// TriG, JSON-LD, CBOR-LD). Resolves a relative reference against a base
/// IRI per RFC 3986 §5 by delegating to <see cref="Uri"/>, and reports
/// whether an IRI carries a scheme.
/// </summary>
/// <remarks>
/// The resolver is deliberately policy-free: it resolves, or returns the
/// reference unchanged when resolution is not possible. Whether an
/// unresolved (still-relative) IRI is an error is a per-format decision
/// made by the calling layer — N-Triples and Turtle reject it, JSON-LD
/// tolerates it.
/// </remarks>
public static class IriResolver
{
    /// <summary>
    /// Determines whether a string is an absolute IRI: it carries a
    /// scheme per the RFC 3986 §3.1 grammar (a leading letter followed by
    /// letters, digits, <c>'+'</c>, <c>'-'</c>, or <c>'.'</c>, terminated
    /// by <c>':'</c>).
    /// </summary>
    /// <param name="value">The IRI to test.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> has a scheme.</returns>
    public static bool IsAbsoluteIri(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

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
    /// Resolves a relative reference against a base IRI per RFC 3986 §5.
    /// An empty reference returns the base; an already-absolute reference
    /// returns unchanged. If resolution fails, the reference is returned
    /// as-is rather than throwing, so the caller can detect non-resolution
    /// by comparing the result to the input.
    /// </summary>
    /// <param name="baseIri">The base IRI to resolve against.</param>
    /// <param name="relativeIri">The reference to resolve.</param>
    /// <returns>The resolved IRI, or the reference unchanged when resolution is not possible.</returns>
    public static string ResolveIri(string baseIri, string relativeIri)
    {
        ArgumentNullException.ThrowIfNull(baseIri);
        ArgumentNullException.ThrowIfNull(relativeIri);

        if(string.IsNullOrEmpty(relativeIri))
        {
            //An empty reference is a same-document reference (RFC 3986 §4.4 / §5.3): the target is the
            //base with its fragment removed (the query, if any, is retained as it precedes the fragment).
            int fragment = baseIri.IndexOf('#', StringComparison.Ordinal);
            return fragment >= 0 ? baseIri[..fragment] : baseIri;
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
            //Resolution failed; return the reference unchanged so the caller can detect
            //non-resolution because the result equals the input.
            return relativeIri;
        }
    }
}
