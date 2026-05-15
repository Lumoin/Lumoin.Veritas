namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// The direction of a CBOR-LD conversion: compress JSON-LD-shaped input to
/// CBOR-LD wire form, or decompress wire form back to a JSON-LD-shaped
/// tree. Per W3C CBOR-LD 1.0 §5.2.1 step 2.
/// </summary>
public enum CborLdStrategy
{
    /// <summary>Compression: convert document tree to CBOR-LD wire form.</summary>
    Compression,

    /// <summary>Decompression: convert CBOR-LD wire form back to a document tree.</summary>
    Decompression
}
