using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// The multicodec identifier carried in byte 1 of a DASL CID. Determines what
/// the bytes referenced by the CID's digest are intended to mean: an opaque
/// blob, or a structured CBOR value following the project's deterministic
/// CBOR profile.
/// </summary>
/// <remarks>
/// <para>
/// The codec affects only the meaning of the referenced bytes, not the
/// behaviour of the <see cref="Cid"/> type itself. Two CIDs with identical
/// digests but different codecs reference the same bytes interpreted under
/// different schemas; they are not equal as identifiers.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "The codec values are wire-format constants defined by the DASL CID specification (0x55 raw, 0x71 DRISL). A synthetic 'None = 0' member would not correspond to any valid codec byte and would be misleading on the wire.")]
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "The codec occupies exactly one byte on the wire per the DASL CID specification. Sizing the enum's underlying type as System.Byte preserves that contract; widening to Int32 would obscure the wire shape.")]
public enum CidCodec: byte
{
    /// <summary>
    /// Raw bytes. The referenced content has no inherent structure beyond
    /// its byte sequence. Multicodec value <c>0x55</c>.
    /// </summary>
    Raw = 0x55,

    /// <summary>
    /// DRISL — the project's deterministic CBOR profile. The referenced
    /// content is a CBOR value encoded under the DRISL discipline.
    /// Multicodec value <c>0x71</c>.
    /// </summary>
    Drisl = 0x71
}
