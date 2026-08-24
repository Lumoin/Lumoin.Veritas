using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// The eight CBOR major types defined by RFC 8949 §3. The major type is the
/// top three bits of the initial byte of every CBOR data item and selects
/// the structural category of the value that follows.
/// </summary>
/// <remarks>
/// See <see href="https://www.rfc-editor.org/rfc/rfc8949#name-major-types"/>
/// for the canonical semantics of each major type.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "Major types occupy the top three bits of the initial byte of every CBOR data item; sizing the enum's underlying type as System.Byte preserves that wire-format contract.")]
public enum CborMajorType: byte
{
    /// <summary>
    /// Major type 0: an unsigned integer in the range <c>[0, 2^64-1]</c>.
    /// </summary>
    UnsignedInteger = 0,

    /// <summary>
    /// Major type 1: a negative integer in the range <c>[-2^64, -1]</c>,
    /// encoded as <c>-1 - n</c> where <c>n</c> is the encoded unsigned value.
    /// </summary>
    NegativeInteger = 1,

    /// <summary>
    /// Major type 2: a byte string (octet sequence with no implied
    /// character semantics).
    /// </summary>
    ByteString = 2,

    /// <summary>
    /// Major type 3: a UTF-8 text string.
    /// </summary>
    TextString = 3,

    /// <summary>
    /// Major type 4: an array of any data items.
    /// </summary>
    Array = 4,

    /// <summary>
    /// Major type 5: a map of key-value pairs.
    /// </summary>
    Map = 5,

    /// <summary>
    /// Major type 6: a tagged data item — a numeric tag followed by the
    /// tagged content.
    /// </summary>
    Tag = 6,

    /// <summary>
    /// Major type 7: simple values, floating-point numbers, and the
    /// break code that terminates indefinite-length items.
    /// </summary>
    SimpleAndFloat = 7
}
