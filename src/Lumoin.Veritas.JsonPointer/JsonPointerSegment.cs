using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Lumoin.Veritas.JsonPointer;

/// <summary>
/// Represents a single reference token in a JSON Pointer (RFC 6901).
/// </summary>
/// <remarks>
/// <para>
/// A reference token is an unescaped string that identifies a location within a JSON document.
/// Per RFC 6901 §4, interpretation depends on the document node encountered during evaluation:
/// against a JSON object the token is a property name; against a JSON array it must be a valid
/// non-negative integer index or <c>"-"</c>.
/// </para>
/// <para>
/// This type stores only the raw token string. It does not decide whether the token represents a
/// property name or an array index — that determination is made at evaluation time by the code that
/// navigates a specific document format. Numeric property keys (common in JSON-LD and JSON Schema)
/// are therefore handled correctly because classification is deferred.
/// </para>
/// <para><strong>Thread Safety:</strong> this type is immutable and thread-safe.</para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct JsonPointerSegment: IEquatable<JsonPointerSegment>, IComparable<JsonPointerSegment>
{
    /// <summary>Gets the raw, unescaped reference token (escape sequences already resolved; may be empty, which is a valid property name).</summary>
    public string Value { get; }

    /// <summary>Gets a value indicating whether this token could be interpreted as a non-negative array index (digits, no leading zeros, fits in an <see cref="int"/>). It does not decide whether it <em>is</em> an index — that depends on the document node at evaluation time.</summary>
    public bool CanBeArrayIndex => TryGetArrayIndex(out _);

    /// <summary>Gets a value indicating whether this token is the append marker (<c>"-"</c>), which per RFC 6901 §4 references the member after the last array element.</summary>
    public bool IsAppendMarker => string.Equals(Value, "-", StringComparison.Ordinal);

    /// <summary>Gets the append-marker segment (<c>"-"</c>).</summary>
    public static JsonPointerSegment AppendMarker { get; } = new("-");

    private JsonPointerSegment(string value)
    {
        Value = value;
    }

    /// <summary>Creates a segment from an unescaped reference token.</summary>
    /// <param name="token">The unescaped reference token; may be empty but not <see langword="null"/>.</param>
    /// <returns>A segment representing the token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    public static JsonPointerSegment Create(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new JsonPointerSegment(token);
    }

    /// <summary>Creates a segment from a non-negative array index, storing it as its decimal string.</summary>
    /// <param name="index">The array index (must be non-negative).</param>
    /// <returns>A segment whose token is the decimal representation of the index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public static JsonPointerSegment FromIndex(int index)
    {
        if(index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Array index must be non-negative.");
        }

        return new JsonPointerSegment(index.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Attempts to interpret this token as a non-negative array index (RFC 6901: <c>"0"</c>, or <c>1-9</c> followed by digits — no leading zeros).</summary>
    /// <param name="index">On success, the parsed index.</param>
    /// <returns><see langword="true"/> when the token is a valid array index.</returns>
    public bool TryGetArrayIndex(out int index)
    {
        index = 0;

        if(Value.Length == 0)
        {
            return false;
        }

        if(Value.Length == 1)
        {
            if(char.IsAsciiDigit(Value[0]))
            {
                index = Value[0] - '0';

                return true;
            }

            return false;
        }

        //No leading zeros per RFC 6901.
        if(Value[0] == '0')
        {
            return false;
        }

        foreach(char character in Value)
        {
            if(!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return int.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
    }

    /// <summary>Returns the escaped form of this token for use in a JSON Pointer string (<c>~</c> → <c>~0</c>, <c>/</c> → <c>~1</c>).</summary>
    /// <returns>The escaped token.</returns>
    public string ToEscapedString() => JsonPointer.Escape(Value);

    /// <summary>Returns the raw, unescaped token value.</summary>
    /// <returns>The token.</returns>
    public override string ToString() => Value;

    /// <summary>Gets the debugger display string.</summary>
    private string DebuggerDisplay
    {
        get
        {
            if(Value.Length == 0)
            {
                return "(empty)";
            }

            if(IsAppendMarker)
            {
                return "[-]";
            }

            return TryGetArrayIndex(out int parsedIndex) ? $"{Value} (index {parsedIndex})" : Value;
        }
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool Equals(JsonPointerSegment other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsonPointerSegment other && Equals(other);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => string.GetHashCode(Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    /// <remarks>Ordering is ordinal string comparison of the raw token; index-shaped tokens are compared as strings, not numerically.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int CompareTo(JsonPointerSegment other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    /// <summary>Determines whether two segments are equal.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(JsonPointerSegment left, JsonPointerSegment right) => left.Equals(right);

    /// <summary>Determines whether two segments are not equal.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(JsonPointerSegment left, JsonPointerSegment right) => !left.Equals(right);

    /// <summary>Determines whether the left segment precedes the right segment.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> precedes <paramref name="right"/>.</returns>
    public static bool operator <(JsonPointerSegment left, JsonPointerSegment right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether the left segment precedes or equals the right segment.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> precedes or equals <paramref name="right"/>.</returns>
    public static bool operator <=(JsonPointerSegment left, JsonPointerSegment right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether the left segment follows the right segment.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> follows <paramref name="right"/>.</returns>
    public static bool operator >(JsonPointerSegment left, JsonPointerSegment right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether the left segment follows or equals the right segment.</summary>
    /// <param name="left">The first segment.</param>
    /// <param name="right">The second segment.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> follows or equals <paramref name="right"/>.</returns>
    public static bool operator >=(JsonPointerSegment left, JsonPointerSegment right) => left.CompareTo(right) >= 0;

    /// <summary>Implicitly converts an unescaped token to a segment.</summary>
    /// <param name="token">The unescaped reference token.</param>
    public static implicit operator JsonPointerSegment(string token) => Create(token);

    /// <summary>Implicitly converts an array index to a segment.</summary>
    /// <param name="index">The array index.</param>
    public static implicit operator JsonPointerSegment(int index) => FromIndex(index);

    /// <summary>Creates a segment from an unescaped token (named alternative to the implicit string conversion).</summary>
    /// <param name="token">The unescaped reference token.</param>
    /// <returns>The segment.</returns>
    public static JsonPointerSegment ToJsonPointerSegment(string token) => Create(token);

    /// <summary>Creates a segment from an array index (named alternative to the implicit int conversion).</summary>
    /// <param name="index">The array index.</param>
    /// <returns>The segment.</returns>
    public static JsonPointerSegment ToJsonPointerSegment(int index) => FromIndex(index);
}
