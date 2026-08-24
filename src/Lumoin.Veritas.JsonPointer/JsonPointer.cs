using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lumoin.Veritas.JsonPointer;

/// <summary>
/// Represents a JSON Pointer as defined in RFC 6901: a sequence of reference tokens, separated by
/// <c>'/'</c>, that identifies a value within a JSON document. The empty string points to the root;
/// every other pointer starts with <c>'/'</c>; <c>'~'</c> is escaped as <c>~0</c> and <c>'/'</c> as
/// <c>~1</c>.
/// </summary>
/// <remarks>
/// An immutable value type: equality and comparison are over the token sequence, segments are stored
/// as an array for O(1) depth queries and efficient slicing, and there are no external dependencies —
/// evaluation against a concrete document format is layered on top. Thread-safe.
/// </remarks>
[DebuggerDisplay("{ToString()}")]
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces",
    Justification = "JsonPointer is the namespace's primary type; the namespace is named for it by design.")]
public readonly struct JsonPointer: IEquatable<JsonPointer>, IComparable<JsonPointer>
{
    private readonly JsonPointerSegment[]? segments;
    private readonly string? cachedString;

    /// <summary>Gets the root pointer, representing the entire document.</summary>
    public static JsonPointer Root { get; } = new([], string.Empty);

    /// <summary>Gets the segments comprising this pointer.</summary>
    public ReadOnlySpan<JsonPointerSegment> Segments => segments ?? [];

    /// <summary>Gets the number of segments in this pointer (its depth in the tree).</summary>
    public int Depth => segments?.Length ?? 0;

    /// <summary>Gets a value indicating whether this is the root pointer (the empty string).</summary>
    public bool IsRoot => Depth == 0;

    /// <summary>Gets the final segment of this pointer, or <see langword="null"/> for the root.</summary>
    public JsonPointerSegment? LastSegment => Depth > 0 ? segments![^1] : default(JsonPointerSegment?);

    /// <summary>Gets the parent pointer, or <see langword="null"/> when this is the root.</summary>
    public JsonPointer? Parent
    {
        get
        {
            if(Depth == 0)
            {
                return null;
            }

            return Depth == 1 ? Root : new JsonPointer(segments![..^1]);
        }
    }

    private JsonPointer(JsonPointerSegment[] pointerSegments, string? cached = null)
    {
        segments = pointerSegments;
        cachedString = cached;
    }

    /// <summary>Parses a JSON Pointer string per RFC 6901.</summary>
    /// <param name="pointer">The pointer string; must be empty (root) or start with <c>'/'</c>.</param>
    /// <returns>The parsed pointer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The format is invalid.</exception>
    public static JsonPointer Parse(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        if(pointer.Length == 0)
        {
            return Root;
        }

        if(pointer[0] != '/')
        {
            throw new FormatException(
                $"JSON Pointer must be empty or start with '/'. Got: \"{Truncate(pointer, 50)}\"");
        }

        if(pointer.Length == 1)
        {
            return new JsonPointer([JsonPointerSegment.Create(string.Empty)], pointer);
        }

        return ParseCore(pointer);
    }

    /// <summary>Attempts to parse a JSON Pointer string.</summary>
    /// <param name="pointer">The pointer string to parse.</param>
    /// <param name="result">On success, the parsed pointer.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(string? pointer, out JsonPointer result)
    {
        if(pointer is null)
        {
            result = default;

            return false;
        }

        if(pointer.Length == 0)
        {
            result = Root;

            return true;
        }

        if(pointer[0] != '/')
        {
            result = default;

            return false;
        }

        try
        {
            result = ParseCore(pointer);

            return true;
        }
        catch(FormatException)
        {
            result = default;

            return false;
        }
    }

    private static JsonPointer ParseCore(string pointer)
    {
        List<JsonPointerSegment> parsed = [];
        int start = 1;

        for(int i = 1; i <= pointer.Length; i++)
        {
            if(i == pointer.Length || pointer[i] == '/')
            {
                string raw = pointer[start..i];
                parsed.Add(JsonPointerSegment.Create(Unescape(raw)));
                start = i + 1;
            }
        }

        return new JsonPointer([.. parsed], pointer);
    }

    /// <summary>Creates a pointer from a sequence of segments.</summary>
    /// <param name="pointerSegments">The segments.</param>
    /// <returns>The pointer (the root when the sequence is empty).</returns>
    public static JsonPointer FromSegments(ReadOnlySpan<JsonPointerSegment> pointerSegments)
    {
        return pointerSegments.Length == 0 ? Root : new JsonPointer(pointerSegments.ToArray());
    }

    /// <summary>Creates a pointer from a single property name.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The single-segment pointer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="propertyName"/> is <see langword="null"/>.</exception>
    public static JsonPointer FromProperty(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return new JsonPointer([JsonPointerSegment.Create(propertyName)]);
    }

    /// <summary>Creates a pointer from a single array index.</summary>
    /// <param name="index">The non-negative array index.</param>
    /// <returns>The single-segment pointer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static JsonPointer FromIndex(int index)
    {
        if(index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Array index must be non-negative.");
        }

        return new JsonPointer([JsonPointerSegment.FromIndex(index)]);
    }

    /// <summary>Enumerates the ancestor pointers from root to parent (exclusive of this pointer).</summary>
    /// <returns>The ancestor pointers.</returns>
    public IEnumerable<JsonPointer> Ancestors()
    {
        for(int i = 0; i < Depth; i++)
        {
            yield return i == 0 ? Root : new JsonPointer(segments![..i]);
        }
    }

    /// <summary>Enumerates this pointer and all of its ancestors.</summary>
    /// <returns>The ancestor pointers followed by this pointer.</returns>
    public IEnumerable<JsonPointer> SelfAndAncestors()
    {
        for(int i = 0; i <= Depth; i++)
        {
            yield return i == 0 ? Root : new JsonPointer(segments![..i]);
        }
    }

    /// <summary>Creates a new pointer by appending a property-name segment.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The extended pointer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="propertyName"/> is <see langword="null"/>.</exception>
    public JsonPointer Append(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        JsonPointerSegment[] extended = new JsonPointerSegment[Depth + 1];
        Segments.CopyTo(extended);
        extended[Depth] = JsonPointerSegment.Create(propertyName);

        return new JsonPointer(extended);
    }

    /// <summary>Creates a new pointer by appending an array-index segment.</summary>
    /// <param name="index">The non-negative array index.</param>
    /// <returns>The extended pointer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public JsonPointer Append(int index)
    {
        if(index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Array index must be non-negative.");
        }

        JsonPointerSegment[] extended = new JsonPointerSegment[Depth + 1];
        Segments.CopyTo(extended);
        extended[Depth] = JsonPointerSegment.FromIndex(index);

        return new JsonPointer(extended);
    }

    /// <summary>Creates a new pointer by appending a segment.</summary>
    /// <param name="segment">The segment to append.</param>
    /// <returns>The extended pointer.</returns>
    public JsonPointer Append(JsonPointerSegment segment)
    {
        JsonPointerSegment[] extended = new JsonPointerSegment[Depth + 1];
        Segments.CopyTo(extended);
        extended[Depth] = segment;

        return new JsonPointer(extended);
    }

    /// <summary>Creates a new pointer by appending another pointer's segments.</summary>
    /// <param name="other">The pointer whose segments are appended.</param>
    /// <returns>The concatenated pointer.</returns>
    public JsonPointer Append(JsonPointer other)
    {
        if(other.IsRoot)
        {
            return this;
        }

        if(IsRoot)
        {
            return other;
        }

        JsonPointerSegment[] extended = new JsonPointerSegment[Depth + other.Depth];
        Segments.CopyTo(extended);
        other.Segments.CopyTo(extended.AsSpan(Depth));

        return new JsonPointer(extended);
    }

    /// <summary>Indicates whether this pointer is a strict ancestor of <paramref name="other"/>.</summary>
    /// <param name="other">The candidate descendant.</param>
    /// <returns><see langword="true"/> when this pointer is a strict prefix of <paramref name="other"/>.</returns>
    public bool IsAncestorOf(JsonPointer other)
    {
        if(Depth >= other.Depth)
        {
            return false;
        }

        ReadOnlySpan<JsonPointerSegment> theseSegments = Segments;
        ReadOnlySpan<JsonPointerSegment> otherSegments = other.Segments;
        for(int i = 0; i < Depth; i++)
        {
            if(!theseSegments[i].Equals(otherSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Indicates whether this pointer is a strict descendant of <paramref name="other"/>.</summary>
    /// <param name="other">The candidate ancestor.</param>
    /// <returns><see langword="true"/> when this pointer is a strict extension of <paramref name="other"/>.</returns>
    public bool IsDescendantOf(JsonPointer other) => other.IsAncestorOf(this);

    /// <summary>Indicates whether this pointer is an ancestor of or equal to <paramref name="other"/>.</summary>
    /// <param name="other">The candidate descendant-or-equal.</param>
    /// <returns><see langword="true"/> when equal to or an ancestor of <paramref name="other"/>.</returns>
    public bool IsAncestorOfOrEqualTo(JsonPointer other) => Equals(other) || IsAncestorOf(other);

    /// <summary>Indicates whether this pointer is a descendant of or equal to <paramref name="other"/>.</summary>
    /// <param name="other">The candidate ancestor-or-equal.</param>
    /// <returns><see langword="true"/> when equal to or a descendant of <paramref name="other"/>.</returns>
    public bool IsDescendantOfOrEqualTo(JsonPointer other) => other.IsAncestorOfOrEqualTo(this);

    /// <summary>Computes the pointer relative to an ancestor (the suffix from the ancestor to this pointer).</summary>
    /// <param name="ancestor">An ancestor of (or equal to) this pointer.</param>
    /// <returns>The relative pointer.</returns>
    /// <exception cref="ArgumentException"><paramref name="ancestor"/> is not an ancestor of this pointer.</exception>
    public JsonPointer RelativeTo(JsonPointer ancestor)
    {
        if(!ancestor.IsAncestorOfOrEqualTo(this))
        {
            throw new ArgumentException(
                $"Pointer \"{ancestor}\" is not an ancestor of \"{this}\".",
                nameof(ancestor));
        }

        if(ancestor.Equals(this))
        {
            return Root;
        }

        return new JsonPointer(segments![ancestor.Depth..]);
    }

    private static string Unescape(string token)
    {
        if(!token.Contains('~', StringComparison.Ordinal))
        {
            return token;
        }

        StringBuilder result = new(token.Length);
        for(int i = 0; i < token.Length; i++)
        {
            if(token[i] == '~')
            {
                if(i + 1 >= token.Length)
                {
                    throw new FormatException("Invalid escape sequence: '~' at end of token.");
                }

                char next = token[i + 1];
                result.Append(next switch
                {
                    '0' => '~',
                    '1' => '/',
                    _ => throw new FormatException(
                        $"Invalid escape sequence: '~{next}'. Only '~0' and '~1' are valid.")
                });

                i++;
            }
            else
            {
                result.Append(token[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>Escapes a string for use as a JSON Pointer reference token (<c>~</c> → <c>~0</c>, <c>/</c> → <c>~1</c>).</summary>
    /// <param name="value">The raw token value.</param>
    /// <returns>The escaped token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if(!value.Contains('~', StringComparison.Ordinal) && !value.Contains('/', StringComparison.Ordinal))
        {
            return value;
        }

        return value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    /// <summary>Converts this pointer to its RFC 6901 string representation.</summary>
    /// <returns>The pointer string (the empty string for the root).</returns>
    public override string ToString()
    {
        if(cachedString is not null)
        {
            return cachedString;
        }

        if(Depth == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach(JsonPointerSegment segment in Segments)
        {
            builder.Append('/');
            builder.Append(segment.ToEscapedString());
        }

        return builder.ToString();
    }

    /// <summary>Converts this pointer to a URI fragment identifier (starting with <c>'#'</c>) per RFC 6901 §6.</summary>
    /// <returns>The URI fragment.</returns>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings",
        Justification = "RFC 6901 §6 defines the fragment as a string form of the pointer.")]
    public string ToUriFragment()
    {
        string pointerString = ToString();
        StringBuilder result = new("#");
        foreach(char character in pointerString)
        {
            if(RequiresPercentEncoding(character))
            {
                foreach(byte b in Encoding.UTF8.GetBytes([character]))
                {
                    result.Append('%');
                    result.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    /// <summary>Parses a JSON Pointer from a URI fragment identifier (which must start with <c>'#'</c>).</summary>
    /// <param name="fragment">The URI fragment.</param>
    /// <returns>The parsed pointer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fragment"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The fragment does not start with <c>'#'</c> or is otherwise invalid.</exception>
    public static JsonPointer ParseUriFragment(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        if(fragment.Length == 0 || fragment[0] != '#')
        {
            throw new FormatException("URI fragment identifier must start with '#'.");
        }

        return Parse(Uri.UnescapeDataString(fragment[1..]));
    }

    /// <summary>Attempts to parse a JSON Pointer from a URI fragment identifier.</summary>
    /// <param name="fragment">The URI fragment.</param>
    /// <param name="result">On success, the parsed pointer.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParseUriFragment(string? fragment, out JsonPointer result)
    {
        if(fragment is null || fragment.Length == 0 || fragment[0] != '#')
        {
            result = default;

            return false;
        }

        try
        {
            return TryParse(Uri.UnescapeDataString(fragment[1..]), out result);
        }
        catch(UriFormatException)
        {
            result = default;

            return false;
        }
    }

    private static bool RequiresPercentEncoding(char character)
    {
        if(char.IsAsciiLetterOrDigit(character))
        {
            return false;
        }

        return character switch
        {
            '-' or '.' or '_' or '~' => false,
            '/' or '?' => false,
            ':' or '@' => false,
            '!' or '$' or '&' or '\'' or '(' or ')' => false,
            '*' or '+' or ',' or ';' or '=' => false,
            _ => true
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool Equals(JsonPointer other)
    {
        if(Depth != other.Depth)
        {
            return false;
        }

        ReadOnlySpan<JsonPointerSegment> theseSegments = Segments;
        ReadOnlySpan<JsonPointerSegment> otherSegments = other.Segments;
        for(int i = 0; i < Depth; i++)
        {
            if(!theseSegments[i].Equals(otherSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsonPointer other && Equals(other);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Depth);
        foreach(JsonPointerSegment segment in Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int CompareTo(JsonPointer other)
    {
        int minDepth = Math.Min(Depth, other.Depth);
        for(int i = 0; i < minDepth; i++)
        {
            int comparison = segments![i].CompareTo(other.segments![i]);
            if(comparison != 0)
            {
                return comparison;
            }
        }

        return Depth.CompareTo(other.Depth);
    }

    /// <summary>Determines whether two pointers are equal.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(JsonPointer left, JsonPointer right) => left.Equals(right);

    /// <summary>Determines whether two pointers are not equal.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(JsonPointer left, JsonPointer right) => !left.Equals(right);

    /// <summary>Determines whether the left pointer precedes the right pointer.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> precedes <paramref name="right"/>.</returns>
    public static bool operator <(JsonPointer left, JsonPointer right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether the left pointer precedes or equals the right pointer.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> precedes or equals <paramref name="right"/>.</returns>
    public static bool operator <=(JsonPointer left, JsonPointer right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether the left pointer follows the right pointer.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> follows <paramref name="right"/>.</returns>
    public static bool operator >(JsonPointer left, JsonPointer right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether the left pointer follows or equals the right pointer.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> follows or equals <paramref name="right"/>.</returns>
    public static bool operator >=(JsonPointer left, JsonPointer right) => left.CompareTo(right) >= 0;

    /// <summary>Implicitly parses a string into a JSON Pointer.</summary>
    /// <param name="pointer">The pointer string.</param>
    public static implicit operator JsonPointer(string pointer) => Parse(pointer);

    /// <summary>Explicitly converts a JSON Pointer to its string representation.</summary>
    /// <param name="pointer">The pointer.</param>
    public static explicit operator string(JsonPointer pointer) => pointer.ToString();
}
