namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// A typed refusal reported by value from the transform surface: the reason
/// and, for coordinate-level refusals, where. Never thrown — refusals are
/// returned, and the destination span is left untouched whenever one is
/// reported.
/// </summary>
/// <param name="Kind">
/// The refusal reason, or <see cref="CoordinateTransformRefusalKind.None"/>
/// on success.
/// </param>
/// <param name="ElementIndex">
/// The index of the first offending double in the source span for the
/// coordinate-level kinds; minus one for the identifier-level kinds and for
/// the success value, where no element is at fault.
/// </param>
public readonly record struct CoordinateTransformRefusal(
    CoordinateTransformRefusalKind Kind,
    int ElementIndex)
{
    /// <summary>
    /// The value a successful call reports: no refusal, no offending
    /// element. Note that <c>default(CoordinateTransformRefusal)</c> is NOT
    /// this value — zero-initialization yields an element index of zero, a
    /// real index — so success is tested against the transform surface's
    /// boolean return or against this property, never against the default
    /// value.
    /// </summary>
    public static CoordinateTransformRefusal None { get; } = new(CoordinateTransformRefusalKind.None, -1);
}
