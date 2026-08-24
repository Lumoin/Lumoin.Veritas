namespace Lumoin.Veritas.Cbor.Dcbor;

/// <summary>
/// Pre-configured <see cref="CborSerializerOptions"/> for the dCBOR profile
/// (<see href="https://datatracker.ietf.org/doc/draft-mcnally-deterministic-cbor/"/>).
/// dCBOR is a deterministic CBOR profile compatible with the project's CDE
/// conformance mode for the rules it shares with CDE; it is more permissive
/// than DRISL on key types and tags.
/// </summary>
/// <remarks>
/// The current implementation of the wrapper covers the deterministic-encoding
/// rules that overlap with <see cref="CborConformanceMode.Cde"/>: shortest
/// integer encoding, sorted map keys (bytewise), no indefinite-length items,
/// and rejection of NaN and infinity in floats. Float-reduction (emitting
/// the smallest IEEE 754 form that round-trips losslessly) is left to a
/// future iteration; in the meantime the wrapper emits double-precision
/// floats unchanged.
/// </remarks>
public static class DcborDefaults
{
    /// <summary>Returns a fresh <see cref="CborSerializerOptions"/> with dCBOR-overlapping rules applied.</summary>
    public static CborSerializerOptions CreateOptions()
    {
        CborSerializerOptions options = CborSerializerOptions.Default(CborConformanceMode.Cde);
        options.AllowIndefiniteLength = false;
        options.ValidateUtf8 = true;
        return options;
    }
}
