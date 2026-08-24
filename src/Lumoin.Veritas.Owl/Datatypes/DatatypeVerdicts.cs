namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The three-valued membership of a value in a datatype's value space. The
/// abstention value is <see cref="Indeterminate"/> at ordinal zero, so
/// <c>default(DatatypeMembership)</c> never asserts or denies membership.
/// </summary>
public enum DatatypeMembership
{
    /// <summary>Membership could not be decided — the sound abstention, and the zero default.</summary>
    Indeterminate = 0,

    /// <summary>The value is provably in the datatype's value space.</summary>
    In,

    /// <summary>The value is provably outside the datatype's value space.</summary>
    Out,
}

/// <summary>
/// The three-valued value identity of two literals within one datatype. The
/// abstention value is <see cref="Indeterminate"/> at ordinal zero, so
/// <c>default(DatatypeValueIdentity)</c> never asserts sameness or distinctness.
/// </summary>
public enum DatatypeValueIdentity
{
    /// <summary>Identity could not be decided — the sound abstention, and the zero default.</summary>
    Indeterminate = 0,

    /// <summary>The two literals denote the same data value.</summary>
    Same,

    /// <summary>The two literals denote distinct data values.</summary>
    Distinct,
}

/// <summary>
/// Whether a distinct-value count is a finite number, an unbounded (infinite)
/// count, or a count that could not be sized. The abstention value is
/// <see cref="Unknown"/> at ordinal zero.
/// </summary>
public enum DatatypeCountKind
{
    /// <summary>The count could not be sized — the sound abstention, and the zero default.</summary>
    Unknown = 0,

    /// <summary>A finite count, carried in the accompanying value.</summary>
    Finite,

    /// <summary>An unbounded (infinite) count.</summary>
    Infinite,
}

/// <summary>
/// A bound on the number of distinct values a datatype conjunction admits. The
/// zero default is <see cref="DatatypeCountKind.Unknown"/> — never a decisive
/// count of zero — so a handler that returns <c>default</c> abstains rather than
/// asserting an empty value space.
/// </summary>
/// <param name="Kind">Whether the count is finite, infinite, or unsized.</param>
/// <param name="Value">The finite count, meaningful only when <paramref name="Kind"/> is <see cref="DatatypeCountKind.Finite"/>.</param>
public readonly record struct DatatypeCountBound(DatatypeCountKind Kind, long Value)
{
    /// <summary>A finite count of the given size.</summary>
    /// <param name="value">The distinct-value count.</param>
    /// <returns>The bound.</returns>
    public static DatatypeCountBound Of(long value)
    {
        return new DatatypeCountBound(DatatypeCountKind.Finite, value);
    }

    /// <summary>An unbounded (infinite) count.</summary>
    public static DatatypeCountBound Infinite { get; } = new(DatatypeCountKind.Infinite, 0);

    /// <summary>A count that could not be sized — the abstention.</summary>
    public static DatatypeCountBound Unknown { get; } = new(DatatypeCountKind.Unknown, 0);
}
