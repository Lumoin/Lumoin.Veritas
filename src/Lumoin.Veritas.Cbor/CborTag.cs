using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// A CBOR tag — a 64-bit unsigned numeric identifier associated with a
/// tagged data item, as defined by RFC 8949 §3.4.
/// </summary>
/// <remarks>
/// <para>
/// Tags assign semantic interpretation to the tagged content without
/// changing its CBOR encoding. The IANA CBOR Tags registry
/// (<see href="https://www.iana.org/assignments/cbor-tags/cbor-tags.xhtml"/>)
/// records standardised tag numbers and their meanings; the static
/// properties on this type expose the small subset that have first-class
/// converter support in this project. Any other tag value can be carried
/// by constructing a <see cref="CborTag"/> directly.
/// </para>
/// <para>
/// The type is a <c>readonly record struct</c>; equality is value-based.
/// </para>
/// </remarks>
/// <param name="Value">The 64-bit unsigned tag identifier.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborTag(ulong Value)
{
    /// <summary>Tag 0 — date/time string per RFC 3339.</summary>
    public static CborTag DateTimeString => new(0);

    /// <summary>Tag 1 — POSIX epoch-based date/time as an integer or float.</summary>
    public static CborTag EpochTime => new(1);

    /// <summary>Tag 2 — unsigned big integer carried as a byte string in big-endian order.</summary>
    public static CborTag UnsignedBigInteger => new(2);

    /// <summary>Tag 3 — negative big integer carried as a byte string in big-endian order.</summary>
    public static CborTag NegativeBigInteger => new(3);

    /// <summary>Tag 4 — decimal fraction (mantissa, base-10 exponent) pair.</summary>
    public static CborTag DecimalFraction => new(4);

    /// <summary>Tag 5 — bigfloat (mantissa, base-2 exponent) pair.</summary>
    public static CborTag Bigfloat => new(5);

    /// <summary>Tag 32 — URI as a text string per RFC 3986.</summary>
    public static CborTag Uri => new(32);

    /// <summary>Tag 33 — base64url-encoded text string per RFC 4648 §5.</summary>
    public static CborTag Base64Url => new(33);

    /// <summary>Tag 34 — base64-encoded text string per RFC 4648 §4.</summary>
    public static CborTag Base64 => new(34);

    /// <summary>Tag 42 — DASL CID. Content is a byte string with a leading <c>0x00</c> multibase prefix followed by the 36-byte CID binary form.</summary>
    public static CborTag Cid => new(42);

    /// <summary>Tag 55799 — self-described CBOR. The tag carries no semantics beyond announcing that the enclosing bytes are CBOR.</summary>
    public static CborTag SelfDescribe => new(55799);

    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"CborTag({Value})");
}
