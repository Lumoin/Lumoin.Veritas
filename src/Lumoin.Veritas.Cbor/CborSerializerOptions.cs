using System.Collections.Generic;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Configures a <see cref="CborWriter"/> or <see cref="CborReader"/>: the
/// conformance mode it operates under, whether indefinite-length items are
/// allowed, whether UTF-8 is validated in text strings, and the set of
/// converters available for tagged values.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mode versus flags.</b> <see cref="ConformanceMode"/> is the high-level
/// selector; the flags <see cref="AllowIndefiniteLength"/> and
/// <see cref="ValidateUtf8"/> tighten or loosen specific aspects. The
/// <see cref="Default(CborConformanceMode)"/> factory returns options pre-set
/// to the rules associated with the chosen mode.
/// </para>
/// <para>
/// <b>Converters.</b> <see cref="Converters"/> exposes the converter list
/// the writer or reader consults. Tag-based dispatch uses the converters in
/// declaration order; the first converter whose <see cref="CborConverter.HandledType"/>
/// matches the value's runtime type wins.
/// </para>
/// </remarks>
public sealed class CborSerializerOptions
{
    /// <summary>Gets or sets the conformance mode. Defaults to <see cref="CborConformanceMode.Lax"/>.</summary>
    public CborConformanceMode ConformanceMode { get; set; } = CborConformanceMode.Lax;

    /// <summary>
    /// Gets or sets whether indefinite-length arrays, maps, byte strings,
    /// and text strings are allowed. The deterministic conformance modes
    /// set this to <c>false</c>; <see cref="CborConformanceMode.Lax"/> and
    /// <see cref="CborConformanceMode.Strict"/> default to <c>true</c>.
    /// </summary>
    public bool AllowIndefiniteLength { get; set; } = true;

    /// <summary>
    /// Gets or sets whether text-string write and read paths validate that
    /// the bytes are well-formed UTF-8. Required by all modes other than
    /// <see cref="CborConformanceMode.Lax"/>.
    /// </summary>
    public bool ValidateUtf8 { get; set; }

    /// <summary>
    /// Gets the converter list. Tag-based dispatch uses the first converter
    /// in this list whose <see cref="CborConverter.HandledType"/> matches
    /// the runtime type of the value being written.
    /// </summary>
    public IList<CborConverter> Converters { get; } = [];

    /// <summary>
    /// Creates a <see cref="CborSerializerOptions"/> pre-configured for the
    /// requested <paramref name="mode"/>.
    /// </summary>
    /// <param name="mode">The conformance mode to apply.</param>
    /// <returns>A new options instance.</returns>
    public static CborSerializerOptions Default(CborConformanceMode mode)
    {
        CborSerializerOptions options = new()
        {
            ConformanceMode = mode
        };

        switch(mode)
        {
            case CborConformanceMode.Lax:
            {
                options.AllowIndefiniteLength = true;
                options.ValidateUtf8 = false;
                break;
            }
            case CborConformanceMode.Strict:
            {
                options.AllowIndefiniteLength = true;
                options.ValidateUtf8 = true;
                break;
            }
            case CborConformanceMode.RfcCanonical:
            case CborConformanceMode.Ctap2Canonical:
            case CborConformanceMode.Cde:
            {
                options.AllowIndefiniteLength = false;
                options.ValidateUtf8 = true;
                break;
            }
        }

        return options;
    }
}
