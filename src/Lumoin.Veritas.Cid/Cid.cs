using System.Diagnostics;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// A DASL Content Identifier. Pairs a <see cref="CidCodec"/> with a 32-byte
/// SHA-256 digest of some referenced content. The combination forms a stable,
/// content-addressed name that any party can recompute from the same bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Wire form.</b> A CID is canonically a 36-byte sequence: a 4-byte header
/// (version <c>0x01</c>, codec, hash type <c>0x12</c> for SHA-256, hash
/// length <c>0x20</c> for 32 bytes) followed by the 32-byte digest. The
/// string form prefixes the lowercase RFC 4648 base32 (no padding) of those
/// 36 bytes with the literal character <c>b</c>. CBOR Tag 42 prepends a
/// historical <c>0x00</c> multibase byte; that prefix is added by the
/// CBOR Tag 42 converter, not by this type.
/// </para>
/// <para>
/// <b>Mutable POCO.</b> The type is mutable to admit construction by
/// deserialisers that fill properties after instantiation. Producers that
/// hand a CID to a consumer should treat the object as immutable from that
/// point on; in-place mutation of a shared <see cref="Cid"/> is the caller's
/// responsibility to avoid.
/// </para>
/// <para>
/// See the DASL CID specification at
/// <see href="https://dasl.ing/cid.html"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("Cid {Codec} {DigestHex,nq}")]
public sealed class Cid
{
    /// <summary>
    /// Gets or sets the multicodec value identifying what the referenced
    /// bytes are. <see cref="CidCodec.Raw"/> for opaque content;
    /// <see cref="CidCodec.Drisl"/> for content encoded under the DRISL
    /// CBOR profile.
    /// </summary>
    public CidCodec Codec { get; set; }

    /// <summary>
    /// Gets or sets the 32-byte SHA-256 digest of the referenced content.
    /// Stored inline as a <see cref="Digest32"/>, so each <see cref="Cid"/>
    /// avoids a per-instance heap allocation for its digest. The default
    /// value is all-zero, which is the conventional "unset" sentinel for
    /// deserialisers that fill the value incrementally.
    /// </summary>
    public Digest32 Digest { get; set; }

    /// <summary>
    /// Gets a short hexadecimal preview of <see cref="Digest"/> for the
    /// debugger display. The preview shows the first eight bytes.
    /// </summary>
    private string DigestHex
    {
        get
        {
            System.ReadOnlySpan<byte> span = Digest.AsSpan();
            return System.Convert.ToHexString(span[..8]) + "...";
        }
    }
}
