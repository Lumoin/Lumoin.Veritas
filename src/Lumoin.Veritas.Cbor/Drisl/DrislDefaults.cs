namespace Lumoin.Veritas.Cbor.Drisl;

/// <summary>
/// Pre-configured <see cref="CborSerializerOptions"/> for the DRISL profile.
/// DRISL is the project's deterministic CBOR profile; its options pin
/// <see cref="CborSerializerOptions.ConformanceMode"/> to
/// <see cref="CborConformanceMode.Cde"/>, disable indefinite-length items,
/// and register the CID converter for Tag 42.
/// </summary>
public static class DrislDefaults
{
    /// <summary>
    /// Returns a fresh <see cref="CborSerializerOptions"/> instance with
    /// the DRISL discipline applied. Each call returns a new instance so
    /// callers can register additional converters without affecting
    /// other consumers.
    /// </summary>
    public static CborSerializerOptions CreateOptions()
    {
        CborSerializerOptions options = CborSerializerOptions.Default(CborConformanceMode.Cde);
        options.AllowIndefiniteLength = false;
        options.ValidateUtf8 = true;
        options.Converters.Add(new CidCborConverter());
        return options;
    }
}
