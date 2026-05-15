using System;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Decodes wire-form bytes back into a typed CBOR-LD value.
/// </summary>
/// <param name="wireBytes">The wire-form bytes. Typically a slice of the
/// reader's source memory, so the implementation should treat the input
/// as transient — copy out anything it retains beyond the call's
/// duration.</param>
/// <returns>The decoded value as a <see cref="CborLdInputNode"/>.</returns>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#value-codecs"/>
public delegate CborLdInputNode CborLdTypedValueDecodeDelegate(
    ReadOnlyMemory<byte> wireBytes);
