using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Encoding;

/// <summary>
/// A handle to an RDF term stored in a <see cref="TermDictionary"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every RDF term — IRI, blank node, literal, or triple term — is assigned an
/// encoded <see cref="uint"/> identifier by a term dictionary. Rather than pass
/// raw <see cref="uint"/>s through the algorithms that operate on graphs,
/// passing <see cref="TermId"/> makes the role of that value obvious at every
/// call site: "this is an encoded RDF-term handle," not "this is just a number."
/// </para>
/// <para>
/// <b>Conversion semantics.</b> Both widening and narrowing conversions
/// between <see cref="TermId"/> and raw integer types are deliberate:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="TermId"/> → <see cref="uint"/> is <em>not</em> implicit. To
/// access the raw encoded value, use the <see cref="Encoded"/> property
/// explicitly. This keeps every boundary between "handle into a term
/// dictionary" and "just a number" visible at the call site.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="uint"/> → <see cref="TermId"/> is <em>not</em> implicit. A raw
/// <see cref="uint"/> becomes a <see cref="TermId"/> only via
/// <see cref="FromEncoded(uint)"/> or the explicit constructor. This prevents
/// arbitrary integers from flowing into the term-id pipeline without
/// consideration.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Narrowing to a specific term kind.</b> Concrete wrappers such as
/// <see cref="IriId"/> and <see cref="BlankNodeId"/> carry the additional
/// invariant that the underlying term is of a specific kind. Use their
/// validating factory methods (e.g. <see cref="IriId.From(TermId, TermDictionary)"/>)
/// to assert the narrowing.
/// </para>
/// <para>
/// <b>Cost.</b> <see cref="TermId"/> is a <c>readonly record struct</c> wrapping
/// one <see cref="uint"/>. The runtime treats it as the same size and layout
/// as <see cref="uint"/>: four bytes. No allocations, no boxing in generic
/// containers of known type. Equality and hash semantics match
/// <see cref="uint"/> semantics exactly.
/// </para>
/// <para>
/// <b>Sentinel.</b> <see cref="None"/> uses the encoded value <c>0</c>. This
/// makes the default <c>new TermId()</c> value, and uninitialised
/// <see cref="TermId"/> slots in freshly-allocated arrays, safely equivalent
/// to <see cref="None"/>. <see cref="TermDictionary"/> assigns external
/// identifiers starting at <c>1</c>, reserving <c>0</c> for this sentinel.
/// </para>
/// </remarks>
/// <param name="Encoded">The raw encoded identifier produced by a term dictionary.</param>
[DebuggerDisplay("TermId({Encoded})")]
public readonly record struct TermId(uint Encoded): IComparable<TermId>, IComparable
{
    /// <summary>
    /// A sentinel representing the absence of a term, encoded as <c>0</c>.
    /// </summary>
    /// <remarks>
    /// Equal to <c>default(TermId)</c>. Uninitialised <see cref="TermId"/>
    /// values, including elements of freshly-allocated arrays, are
    /// indistinguishable from this sentinel by construction.
    /// </remarks>
    public static TermId None { get; } = new(0);

    /// <summary>
    /// Creates a <see cref="TermId"/> from a raw encoded value.
    /// </summary>
    /// <param name="encoded">The raw encoded identifier.</param>
    /// <returns>The wrapped identifier.</returns>
    /// <remarks>
    /// Call this at the boundary between raw-<see cref="uint"/> storage and
    /// the typed pipeline. Do not use this to bypass the
    /// <see cref="IriId"/> / <see cref="BlankNodeId"/> / <see cref="LiteralId"/>
    /// narrowing factories when term kind matters.
    /// </remarks>
    public static TermId FromEncoded(uint encoded) => new(encoded);

    /// <summary>
    /// Returns <c>true</c> when this identifier is the <see cref="None"/> sentinel.
    /// </summary>
    public bool IsNone => Encoded == 0;

    /// <summary>Orders term identifiers by their encoded value.</summary>
    public int CompareTo(TermId other) => Encoded.CompareTo(other.Encoded);

    /// <inheritdoc/>
    public int CompareTo(object? obj)
    {
        if(obj is null)
        {
            return 1;
        }

        if(obj is TermId other)
        {
            return CompareTo(other);
        }

        throw new ArgumentException($"Object must be of type {nameof(TermId)}.", nameof(obj));
    }

    /// <summary>Returns <c>true</c> when <paramref name="left"/> orders before <paramref name="right"/>.</summary>
    public static bool operator <(TermId left, TermId right) => left.Encoded < right.Encoded;

    /// <summary>Returns <c>true</c> when <paramref name="left"/> orders before or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(TermId left, TermId right) => left.Encoded <= right.Encoded;

    /// <summary>Returns <c>true</c> when <paramref name="left"/> orders after <paramref name="right"/>.</summary>
    public static bool operator >(TermId left, TermId right) => left.Encoded > right.Encoded;

    /// <summary>Returns <c>true</c> when <paramref name="left"/> orders after or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(TermId left, TermId right) => left.Encoded >= right.Encoded;

    /// <inheritdoc/>
    public override string ToString() => Encoded.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
