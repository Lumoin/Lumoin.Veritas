namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A contiguous row interval <c>[Low, High)</c> of one rotation's conceptual
/// sorted table inside a <see cref="TripleSelfIndex"/> — the working state of
/// a pattern's descent: each bound symbol narrows or re-tables the range, and
/// its length is the matching triple count.
/// </summary>
/// <param name="Rotation">The rotation whose table the interval addresses.</param>
/// <param name="Low">The inclusive interval start.</param>
/// <param name="High">The exclusive interval end.</param>
public readonly record struct SelfIndexRange(SelfIndexRotation Rotation, int Low, int High)
{
    /// <summary>The number of rows — the count of triples matching the bindings the range represents.</summary>
    public int Length => High - Low;

    /// <summary>Whether the range holds no rows.</summary>
    public bool IsEmpty => High <= Low;
}
