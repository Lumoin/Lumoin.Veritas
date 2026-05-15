namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// The output of <see cref="CborLdDecoder.DecodeAsync"/>: the resolved
/// registry-entry id and the decoded document tree.
/// </summary>
/// <param name="RegistryEntryId">The wire-form registry entry id.</param>
/// <param name="Root">The decoded document tree.</param>
public readonly record struct CborLdDecodeResult(int RegistryEntryId, CborLdInputNode Root);
