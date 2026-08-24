using System.Buffers;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Encodes a typed CBOR-LD value into its wire-form byte representation.
/// Returned memory is owned by the caller; the caller is responsible for
/// disposing it.
/// </summary>
/// <param name="value">The typed value to encode. Typically a
/// <see cref="CborLdInputString"/> for URL / date / dateTime / base64url
/// values, or a <see cref="CborLdInputInt"/> for integer-valued types.</param>
/// <param name="pool">The memory pool from which to rent the returned buffer.</param>
/// <returns>The wire-form bytes as an owned, pool-rented buffer.</returns>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#value-codecs"/>
public delegate IMemoryOwner<byte> CborLdTypedValueEncodeDelegate(
    CborLdInputNode value,
    MemoryPool<byte> pool);
