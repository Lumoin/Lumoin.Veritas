using System;

namespace Lumoin.Veritas.Core.Iris;

/// <summary>
/// Format-agnostic IRI resolution shared by every syntax layer (RDF/XML, Turtle,
/// TriG, JSON-LD, CBOR-LD), byte-native over UTF-8. Resolves a relative reference
/// against a base IRI per RFC 3986 §5 by performing the §5.2 transform directly —
/// not via <see cref="Uri"/>, which applies §6 normalization and mangles the §5.4
/// abnormal examples — and reports whether an IRI carries a scheme. The structural
/// delimiters are ASCII, and ASCII bytes never occur inside a UTF-8 multi-byte
/// sequence, so the byte-level scans are exact over IRIs.
/// </summary>
/// <remarks>
/// The resolver is deliberately policy-free: it resolves, or returns the
/// reference unchanged when resolution is not possible. Whether an
/// unresolved (still-relative) IRI is an error is a per-format decision
/// made by the calling layer — N-Triples and Turtle reject it, JSON-LD
/// tolerates it. Input is assumed to be well-formed UTF-8 (the surrounding
/// readers' own contract); bytes are compared as bytes, which is the IRI's
/// identity.
/// </remarks>
public static class IriResolver
{
    /// <summary>The largest scratch span the transform takes from the stack; a longer IRI falls back to a transient heap buffer.</summary>
    private const int StackScratchLimit = 512;

    /// <summary>
    /// Determines whether an IRI is absolute: it carries a scheme per the RFC 3986
    /// §3.1 grammar (a leading letter followed by letters, digits, <c>'+'</c>,
    /// <c>'-'</c>, or <c>'.'</c>, terminated by <c>':'</c>).
    /// </summary>
    /// <param name="value">The IRI's UTF-8 bytes.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> has a scheme.</returns>
    public static bool IsAbsoluteIri(ReadOnlySpan<byte> value)
    {
        int colonIndex = value.IndexOf(UriDelimiters.SchemeSeparatorByte);

        return colonIndex > 0 && IsScheme(value.Slice(0, colonIndex));
    }

    /// <summary>
    /// Parses a base IRI once into its component ranges, so every reference resolved
    /// under the same base amortizes the parse. Any fragment on the base is ignored,
    /// as §5.2.2 never reads it. An empty base parses to a present, empty, schemeless
    /// base (which resolves nothing), distinct from <see cref="IriBase.None"/>.
    /// </summary>
    /// <param name="baseIri">The base IRI.</param>
    /// <returns>The parsed base.</returns>
    public static IriBase ParseBase(Utf8String baseIri)
    {
        ParsedIri parsed = Parse(baseIri.Span);

        return new IriBase(baseIri.Memory, parsed.Scheme, parsed.Authority, parsed.Path, parsed.Query);
    }

    /// <summary>
    /// Resolves a relative reference against a parsed base per the RFC 3986 §5.2
    /// transform-references algorithm (the same algorithm RFC 3987 §5.3 applies to
    /// IRIs), with no scheme/host case folding, default-port removal, or
    /// query/fragment dot-segment processing. When there is no base in scope, or the
    /// base carries no scheme (resolution is impossible per §5.1), the reference is
    /// returned unchanged — the same instance, so the caller can detect
    /// non-resolution cheaply.
    /// </summary>
    /// <param name="baseIri">The parsed base, or <see cref="IriBase.None"/>.</param>
    /// <param name="reference">The reference to resolve.</param>
    /// <returns>The resolved IRI, or <paramref name="reference"/> unchanged when resolution is not possible.</returns>
    public static Utf8String ResolveIri(in IriBase baseIri, Utf8String reference)
    {
        if(!baseIri.HasValue || !baseIri.Scheme.IsPresent)
        {
            return reference;
        }

        ReadOnlySpan<byte> baseSpan = baseIri.Bytes.Span;
        ReadOnlySpan<byte> referenceSpan = reference.Span;
        ParsedIri parsedReference = Parse(referenceSpan);

        //Merge (§5.3) fabricates at most one '/', so base+reference+1 bounds both the
        //merged-path and the dot-segment scratch. They are declared ahead of the spans
        //they feed (ref safety), stack-backed for ordinary IRI lengths.
        int scratchBound = baseSpan.Length + referenceSpan.Length + 1;
        Span<byte> mergedScratch = scratchBound <= StackScratchLimit ? stackalloc byte[StackScratchLimit] : new byte[scratchBound];
        Span<byte> pathScratch = scratchBound <= StackScratchLimit ? stackalloc byte[StackScratchLimit] : new byte[scratchBound];

        //RFC 3986 §5.2.2 Transform References: pick each target component's source,
        //preserving absent-vs-empty per component.
        ReadOnlySpan<byte> scheme;
        bool hasAuthority;
        ReadOnlySpan<byte> authority;
        bool hasQuery;
        ReadOnlySpan<byte> query;
        scoped ReadOnlySpan<byte> pathInput;
        bool removeDotSegments;
        bool mergePaths = false;
        if(parsedReference.Scheme.IsPresent)
        {
            scheme = Slice(referenceSpan, parsedReference.Scheme);
            hasAuthority = parsedReference.Authority.IsPresent;
            authority = Slice(referenceSpan, parsedReference.Authority);
            pathInput = Slice(referenceSpan, parsedReference.Path);
            removeDotSegments = true;
            hasQuery = parsedReference.Query.IsPresent;
            query = Slice(referenceSpan, parsedReference.Query);
        }
        else
        {
            scheme = Slice(baseSpan, baseIri.Scheme);
            if(parsedReference.Authority.IsPresent)
            {
                hasAuthority = true;
                authority = Slice(referenceSpan, parsedReference.Authority);
                pathInput = Slice(referenceSpan, parsedReference.Path);
                removeDotSegments = true;
                hasQuery = parsedReference.Query.IsPresent;
                query = Slice(referenceSpan, parsedReference.Query);
            }
            else
            {
                hasAuthority = baseIri.Authority.IsPresent;
                authority = Slice(baseSpan, baseIri.Authority);
                if(parsedReference.Path.Length == 0)
                {
                    pathInput = Slice(baseSpan, baseIri.Path);
                    removeDotSegments = false;
                    if(parsedReference.Query.IsPresent)
                    {
                        hasQuery = true;
                        query = Slice(referenceSpan, parsedReference.Query);
                    }
                    else
                    {
                        hasQuery = baseIri.Query.IsPresent;
                        query = Slice(baseSpan, baseIri.Query);
                    }
                }
                else
                {
                    pathInput = Slice(referenceSpan, parsedReference.Path);
                    removeDotSegments = true;
                    mergePaths = pathInput[0] != UriDelimiters.PathSeparatorByte;
                    hasQuery = parsedReference.Query.IsPresent;
                    query = Slice(referenceSpan, parsedReference.Query);
                }
            }
        }

        if(mergePaths)
        {
            int mergedLength = MergePaths(Slice(baseSpan, baseIri.Path), baseIri.Authority.IsPresent, pathInput, mergedScratch);
            pathInput = mergedScratch.Slice(0, mergedLength);
        }

        scoped ReadOnlySpan<byte> path;
        if(removeDotSegments)
        {
            int pathLength = RemoveDotSegments(pathInput, pathScratch);
            path = pathScratch.Slice(0, pathLength);
        }
        else
        {
            path = pathInput;
        }

        bool hasFragment = parsedReference.Fragment.IsPresent;
        ReadOnlySpan<byte> fragment = Slice(referenceSpan, parsedReference.Fragment);

        //Recompose (§5.3) into one exact-size owned value.
        int total = scheme.Length + 1
            + (hasAuthority ? UriDelimiters.AuthorityPrefixU8.Length + authority.Length : 0)
            + path.Length
            + (hasQuery ? 1 + query.Length : 0)
            + (hasFragment ? 1 + fragment.Length : 0);
        byte[] result = new byte[total];
        Span<byte> destination = result;
        int written = 0;
        scheme.CopyTo(destination);
        written += scheme.Length;
        destination[written++] = UriDelimiters.SchemeSeparatorByte;
        if(hasAuthority)
        {
            UriDelimiters.AuthorityPrefixU8.CopyTo(destination.Slice(written));
            written += UriDelimiters.AuthorityPrefixU8.Length;
            authority.CopyTo(destination.Slice(written));
            written += authority.Length;
        }

        path.CopyTo(destination.Slice(written));
        written += path.Length;
        if(hasQuery)
        {
            destination[written++] = UriDelimiters.QueryPrefixByte;
            query.CopyTo(destination.Slice(written));
            written += query.Length;
        }

        if(hasFragment)
        {
            destination[written++] = UriDelimiters.FragmentPrefixByte;
            fragment.CopyTo(destination.Slice(written));
        }

        return new Utf8String(result);
    }

    /// <summary>The bytes of a component within its IRI, or the empty span for an absent component.</summary>
    /// <param name="iri">The IRI bytes the component indexes into.</param>
    /// <param name="component">The component.</param>
    /// <returns>The component's bytes.</returns>
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> iri, IriComponent component)
    {
        return component.IsPresent ? iri.Slice(component.Start, component.Length) : default;
    }

    /// <summary>An IRI or reference parsed into its RFC 3986 §3 component ranges; an absent component's delimiter never appeared, distinct from a present-but-empty one.</summary>
    /// <param name="Scheme">The scheme range (without <c>':'</c>), or absent.</param>
    /// <param name="Authority">The authority range (without <c>//</c>), or absent.</param>
    /// <param name="Path">The path range; always present, possibly empty.</param>
    /// <param name="Query">The query range (without <c>'?'</c>), or absent.</param>
    /// <param name="Fragment">The fragment range (without <c>'#'</c>), or absent.</param>
    private readonly record struct ParsedIri(IriComponent Scheme, IriComponent Authority, IriComponent Path, IriComponent Query, IriComponent Fragment);

    /// <summary>Parses an IRI/reference into component ranges over its bytes: fragment first, then query, then the scheme (guarded to precede the first path separator), then the authority, the remainder being the path.</summary>
    /// <param name="value">The IRI/reference bytes.</param>
    /// <returns>The component ranges.</returns>
    private static ParsedIri Parse(ReadOnlySpan<byte> value)
    {
        int start = 0;
        int end = value.Length;

        IriComponent fragment = IriComponent.Absent;
        int hash = IndexOfInRange(value, start, end, UriDelimiters.FragmentPrefixByte);
        if(hash >= 0)
        {
            fragment = new IriComponent(hash + 1, end - hash - 1);
            end = hash;
        }

        IriComponent query = IriComponent.Absent;
        int question = IndexOfInRange(value, start, end, UriDelimiters.QueryPrefixByte);
        if(question >= 0)
        {
            query = new IriComponent(question + 1, end - question - 1);
            end = question;
        }

        IriComponent scheme = IriComponent.Absent;
        int colon = IndexOfInRange(value, start, end, UriDelimiters.SchemeSeparatorByte);
        int firstSlash = IndexOfInRange(value, start, end, UriDelimiters.PathSeparatorByte);
        if(colon > 0 && (firstSlash < 0 || colon < firstSlash) && IsScheme(value.Slice(0, colon)))
        {
            scheme = new IriComponent(0, colon);
            start = colon + 1;
        }

        IriComponent authority = IriComponent.Absent;
        if(end - start >= UriDelimiters.AuthorityPrefixU8.Length && value[start] == UriDelimiters.PathSeparatorByte && value[start + 1] == UriDelimiters.PathSeparatorByte)
        {
            int rest = start + UriDelimiters.AuthorityPrefixU8.Length;
            int slash = IndexOfInRange(value, rest, end, UriDelimiters.PathSeparatorByte);
            if(slash < 0)
            {
                authority = new IriComponent(rest, end - rest);
                start = end;
            }
            else
            {
                authority = new IriComponent(rest, slash - rest);
                start = slash;
            }
        }

        return new ParsedIri(scheme, authority, new IriComponent(start, end - start), query, fragment);
    }

    /// <summary>The first index of a byte within a half-open range, or <c>-1</c>.</summary>
    /// <param name="value">The bytes to search.</param>
    /// <param name="from">The inclusive start offset.</param>
    /// <param name="to">The exclusive end offset.</param>
    /// <param name="target">The byte to find.</param>
    /// <returns>The absolute index, or <c>-1</c>.</returns>
    private static int IndexOfInRange(ReadOnlySpan<byte> value, int from, int to, byte target)
    {
        int relative = value.Slice(from, to - from).IndexOf(target);

        return relative < 0 ? -1 : from + relative;
    }

    /// <summary>Indicates whether a candidate is a well-formed RFC 3986 scheme: a leading ASCII letter, then letters, digits, <c>'+'</c>, <c>'-'</c>, or <c>'.'</c>.</summary>
    /// <param name="candidate">The candidate bytes.</param>
    /// <returns><see langword="true"/> when the candidate is a scheme.</returns>
    private static bool IsScheme(ReadOnlySpan<byte> candidate)
    {
        if(candidate.Length == 0 || !char.IsAsciiLetter((char)candidate[0]))
        {
            return false;
        }

        for(int i = 1; i < candidate.Length; i++)
        {
            byte value = candidate[i];
            if(!char.IsAsciiLetterOrDigit((char)value) && value != (byte)'+' && value != (byte)'-' && value != (byte)'.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Merges a reference path onto the base path per RFC 3986 §5.3, writing the merged path into the scratch.</summary>
    /// <param name="basePath">The base's path bytes.</param>
    /// <param name="baseHasAuthority">Whether the base carries an authority (a present authority with an empty path merges under a fabricated root <c>'/'</c>).</param>
    /// <param name="referencePath">The reference's path bytes.</param>
    /// <param name="merged">The scratch the merged path is written into.</param>
    /// <returns>The merged path's length.</returns>
    private static int MergePaths(ReadOnlySpan<byte> basePath, bool baseHasAuthority, ReadOnlySpan<byte> referencePath, Span<byte> merged)
    {
        if(baseHasAuthority && basePath.Length == 0)
        {
            merged[0] = UriDelimiters.PathSeparatorByte;
            referencePath.CopyTo(merged.Slice(1));

            return 1 + referencePath.Length;
        }

        int lastSlash = basePath.LastIndexOf(UriDelimiters.PathSeparatorByte);
        if(lastSlash < 0)
        {
            referencePath.CopyTo(merged);

            return referencePath.Length;
        }

        basePath.Slice(0, lastSlash + 1).CopyTo(merged);
        referencePath.CopyTo(merged.Slice(lastSlash + 1));

        return lastSlash + 1 + referencePath.Length;
    }

    /// <summary>Removes the <c>.</c> and <c>..</c> complete path segments per the RFC 3986 §5.2.4 algorithm, writing the output path into the scratch.</summary>
    /// <param name="input">The path to process.</param>
    /// <param name="output">The scratch the processed path is written into.</param>
    /// <returns>The processed path's length.</returns>
    private static int RemoveDotSegments(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int written = 0;
        while(input.Length > 0)
        {
            if(input.StartsWith("../"u8))
            {
                input = input.Slice(3);
            }
            else if(input.StartsWith("./"u8))
            {
                input = input.Slice(2);
            }
            else if(input.StartsWith("/./"u8))
            {
                //Replacing the "/./" prefix with "/" is slicing to the '/' at index 2.
                input = input.Slice(2);
            }
            else if(input.SequenceEqual("/."u8))
            {
                input = "/"u8;
            }
            else if(input.StartsWith("/../"u8))
            {
                //Replacing the "/../" prefix with "/" is slicing to the '/' at index 3.
                input = input.Slice(3);
                written = RemoveLastOutputSegment(output, written);
            }
            else if(input.SequenceEqual("/.."u8))
            {
                input = "/"u8;
                written = RemoveLastOutputSegment(output, written);
            }
            else if(input.SequenceEqual("."u8) || input.SequenceEqual(".."u8))
            {
                input = default;
            }
            else
            {
                int start = input[0] == UriDelimiters.PathSeparatorByte ? 1 : 0;
                int next = IndexOfInRange(input, start, input.Length, UriDelimiters.PathSeparatorByte);
                if(next < 0)
                {
                    input.CopyTo(output.Slice(written));
                    written += input.Length;
                    input = default;
                }
                else
                {
                    input.Slice(0, next).CopyTo(output.Slice(written));
                    written += next;
                    input = input.Slice(next);
                }
            }
        }

        return written;
    }

    /// <summary>Removes the last <c>/segment</c> (or trailing segment) from the output path.</summary>
    /// <param name="output">The output path scratch.</param>
    /// <param name="written">The output path's current length.</param>
    /// <returns>The output path's length after the removal.</returns>
    private static int RemoveLastOutputSegment(Span<byte> output, int written)
    {
        int lastSlash = output.Slice(0, written).LastIndexOf(UriDelimiters.PathSeparatorByte);

        return lastSlash < 0 ? 0 : lastSlash;
    }
}
