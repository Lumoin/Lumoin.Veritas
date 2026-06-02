using System;
using System.Text;

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

        int colonIndex = value.IndexOf(UriDelimiters.SchemeSeparator, StringComparison.Ordinal);
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
    /// Resolves a relative reference against a base IRI per the RFC 3986 §5.2
    /// transform-references algorithm (the same algorithm RFC 3987 §5.3 applies
    /// to IRIs). The transform is performed directly — not via
    /// <see cref="Uri"/>, which applies §6 normalization and mangles the §5.4
    /// abnormal examples — so the output is the exact reference-resolution
    /// result with no scheme/host case folding, default-port removal, or
    /// query/fragment dot-segment processing. When the base carries no scheme
    /// (resolution is impossible) the reference is returned unchanged, so the
    /// caller can detect non-resolution by comparing the result to the input.
    /// </summary>
    /// <param name="baseIri">The base IRI to resolve against.</param>
    /// <param name="relativeIri">The reference to resolve.</param>
    /// <returns>The resolved IRI, or the reference unchanged when resolution is not possible.</returns>
    public static string ResolveIri(string baseIri, string relativeIri)
    {
        ArgumentNullException.ThrowIfNull(baseIri);
        ArgumentNullException.ThrowIfNull(relativeIri);

        UriParts @base = Parse(baseIri);
        if(@base.Scheme is null)
        {
            //A relative base cannot resolve a reference (RFC 3986 §5.1 requires
            //an absolute base); leave the reference unchanged.
            return relativeIri;
        }

        UriParts reference = Parse(relativeIri);

        //RFC 3986 §5.2.2 Transform References.
        string? scheme;
        string? authority;
        string path;
        string? query;
        if(reference.Scheme is not null)
        {
            scheme = reference.Scheme;
            authority = reference.Authority;
            path = RemoveDotSegments(reference.Path);
            query = reference.Query;
        }
        else
        {
            scheme = @base.Scheme;
            if(reference.Authority is not null)
            {
                authority = reference.Authority;
                path = RemoveDotSegments(reference.Path);
                query = reference.Query;
            }
            else
            {
                authority = @base.Authority;
                if(reference.Path.Length == 0)
                {
                    path = @base.Path;
                    query = reference.Query ?? @base.Query;
                }
                else
                {
                    path = reference.Path[0] == '/'
                        ? RemoveDotSegments(reference.Path)
                        : RemoveDotSegments(Merge(@base, reference.Path));
                    query = reference.Query;
                }
            }
        }

        return Recompose(scheme, authority, path, query, reference.Fragment);
    }

    /// <summary>Parses an IRI/reference into its RFC 3986 §3 components; a component is <see langword="null"/> when its delimiter is absent (an undefined component), distinct from a present-but-empty one.</summary>
    private static UriParts Parse(string value)
    {
        string? fragment = null;
        int hash = value.IndexOf(UriDelimiters.FragmentPrefix, StringComparison.Ordinal);
        if(hash >= 0)
        {
            fragment = value[(hash + 1)..];
            value = value[..hash];
        }

        string? query = null;
        int question = value.IndexOf(UriDelimiters.QueryPrefix, StringComparison.Ordinal);
        if(question >= 0)
        {
            query = value[(question + 1)..];
            value = value[..question];
        }

        string? scheme = null;
        int colon = value.IndexOf(UriDelimiters.SchemeSeparator, StringComparison.Ordinal);
        int firstSlash = value.IndexOf(UriDelimiters.PathSeparator, StringComparison.Ordinal);
        if(colon > 0 && (firstSlash < 0 || colon < firstSlash) && IsScheme(value[..colon]))
        {
            scheme = value[..colon];
            value = value[(colon + 1)..];
        }

        string? authority = null;
        if(value.StartsWith(UriDelimiters.AuthorityPrefix, StringComparison.Ordinal))
        {
            string rest = value[UriDelimiters.AuthorityPrefix.Length..];
            int slash = rest.IndexOf(UriDelimiters.PathSeparator, StringComparison.Ordinal);
            authority = slash < 0 ? rest : rest[..slash];
            value = slash < 0 ? string.Empty : rest[slash..];
        }

        return new UriParts(scheme, authority, value, query, fragment);
    }

    /// <summary>Indicates whether <paramref name="candidate"/> is a well-formed RFC 3986 scheme.</summary>
    private static bool IsScheme(string candidate)
    {
        if(candidate.Length == 0 || !char.IsAsciiLetter(candidate[0]))
        {
            return false;
        }

        for(int i = 1; i < candidate.Length; i++)
        {
            char c = candidate[i];
            if(!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Merges a reference path onto the base path per RFC 3986 §5.3.</summary>
    private static string Merge(UriParts @base, string referencePath)
    {
        if(@base.Authority is not null && @base.Path.Length == 0)
        {
            return UriDelimiters.PathSeparator + referencePath;
        }

        int lastSlash = @base.Path.LastIndexOf(UriDelimiters.PathSeparator);

        return lastSlash < 0 ? referencePath : string.Concat(@base.Path[..(lastSlash + 1)], referencePath);
    }

    /// <summary>Removes the <c>.</c> and <c>..</c> complete path segments per the RFC 3986 §5.2.4 algorithm.</summary>
    private static string RemoveDotSegments(string input)
    {
        StringBuilder output = new(input.Length);
        while(input.Length > 0)
        {
            if(input.StartsWith("../", StringComparison.Ordinal))
            {
                input = input[3..];
            }
            else if(input.StartsWith("./", StringComparison.Ordinal))
            {
                input = input[2..];
            }
            else if(input.StartsWith("/./", StringComparison.Ordinal))
            {
                input = "/" + input[3..];
            }
            else if(input == "/.")
            {
                input = "/";
            }
            else if(input.StartsWith("/../", StringComparison.Ordinal))
            {
                input = "/" + input[4..];
                RemoveLastOutputSegment(output);
            }
            else if(input == "/..")
            {
                input = "/";
                RemoveLastOutputSegment(output);
            }
            else if(input is "." or "..")
            {
                input = string.Empty;
            }
            else
            {
                int start = input[0] == UriDelimiters.PathSeparator ? 1 : 0;
                int next = input.IndexOf(UriDelimiters.PathSeparator, start);
                if(next < 0)
                {
                    output.Append(input);
                    input = string.Empty;
                }
                else
                {
                    output.Append(input[..next]);
                    input = input[next..];
                }
            }
        }

        return output.ToString();
    }

    /// <summary>Removes the last <c>/segment</c> (or trailing segment) from the output buffer.</summary>
    private static void RemoveLastOutputSegment(StringBuilder output)
    {
        int lastSlash = -1;
        for(int i = output.Length - 1; i >= 0; i--)
        {
            if(output[i] == UriDelimiters.PathSeparator)
            {
                lastSlash = i;
                break;
            }
        }

        output.Length = lastSlash < 0 ? 0 : lastSlash;
    }

    /// <summary>Recomposes the resolved components into an IRI per RFC 3986 §5.3.</summary>
    private static string Recompose(string? scheme, string? authority, string path, string? query, string? fragment)
    {
        StringBuilder result = new();
        if(scheme is not null)
        {
            result.Append(scheme).Append(UriDelimiters.SchemeSeparator);
        }

        if(authority is not null)
        {
            result.Append(UriDelimiters.AuthorityPrefix).Append(authority);
        }

        result.Append(path);
        if(query is not null)
        {
            result.Append(UriDelimiters.QueryPrefix).Append(query);
        }

        if(fragment is not null)
        {
            result.Append(UriDelimiters.FragmentPrefix).Append(fragment);
        }

        return result.ToString();
    }

    /// <summary>The RFC 3986 §3 components of an IRI; a <see langword="null"/> component is undefined (its delimiter was absent).</summary>
    /// <param name="Scheme">The scheme, or <see langword="null"/>.</param>
    /// <param name="Authority">The authority, or <see langword="null"/>.</param>
    /// <param name="Path">The path (always present, possibly empty).</param>
    /// <param name="Query">The query, or <see langword="null"/>.</param>
    /// <param name="Fragment">The fragment, or <see langword="null"/>.</param>
    private readonly record struct UriParts(string? Scheme, string? Authority, string Path, string? Query, string? Fragment);
}
