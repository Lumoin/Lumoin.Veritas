using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Iris;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Pure IRI utilities shared by JSON-LD, CBOR-LD, and any other Linked
/// Data format. Operates on strings only; does not consult an active
/// context. Context-aware IRI expansion lives on <see cref="LinkedDataContext{TNode}"/>
/// as an instance method.
/// </summary>
public static class IriUtils
{
    /// <summary>Determines whether a string is a JSON-LD keyword.</summary>
    public static bool IsKeyword(string? value)
    {
        return JsonLdKeywords.IsKeyword(value);
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

        //The byte-native resolver is the one scheme grammar; the string face encodes
        //once on the stack for ordinary IRI lengths.
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> bytes = byteCount <= 512 ? stackalloc byte[512] : new byte[byteCount];
        int written = Encoding.UTF8.GetBytes(value, bytes);

        return IriResolver.IsAbsoluteIri(bytes.Slice(0, written));
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

        IriBase parsedBase = IriResolver.ParseBase(Utf8Strings.From(baseIri));

        return IriResolver.ResolveIri(in parsedBase, Utf8Strings.From(relativeIri)).ToString();
    }
}
