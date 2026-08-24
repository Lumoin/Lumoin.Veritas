namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// A typed refusal reported by value from the serialization codec family:
/// the reason and, for reader refusals, where. Never thrown — refusals are
/// returned, a refused read rents nothing, and a refused write leaves the
/// destination untouched because every writer validates the whole geometry
/// before its first destination write.
/// </summary>
/// <param name="Kind">
/// The refusal reason, or <see cref="GeometryCodecRefusalKind.None"/> on
/// success.
/// </param>
/// <param name="ByteOffset">
/// For reader refusals, the offset into the input UTF-8 span of the first
/// offending byte. For offenses of absence or shortfall the offset names the
/// byte at which the violation became inevitable: the input length for
/// truncation and for the zero-length input, the byte terminating a
/// coordinate run for count or divisibility shortfalls, and the byte closing
/// a start-tag's attribute list for a required attribute that never
/// appeared. The character-span convenience overloads report offsets into
/// the transcoded UTF-8 representation of their input, not character
/// indices. Writer refusals carry minus one — a writer refusal names no byte
/// of any caller-visible document, a carried opaque extent's bytes included:
/// one refusal shape per side.
/// </param>
public readonly record struct GeometryCodecRefusal(
    GeometryCodecRefusalKind Kind,
    int ByteOffset)
{
    /// <summary>
    /// The value a successful call reports: no refusal, no offending byte.
    /// Note that <c>default(GeometryCodecRefusal)</c> is NOT this value —
    /// zero-initialization yields a byte offset of zero, a real offset — so
    /// success is tested against the codec surface's boolean return or
    /// against this property, never against the default value.
    /// </summary>
    public static GeometryCodecRefusal None { get; } = new(GeometryCodecRefusalKind.None, -1);
}
