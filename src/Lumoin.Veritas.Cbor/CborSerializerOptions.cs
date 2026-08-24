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
    /// Gets or sets whether <see cref="CborWriter.WriteDouble(double)"/>
    /// suppresses the shortest-form float reduction. When <see langword="true"/>,
    /// double values are always emitted as binary64 (9 wire bytes) — the
    /// behaviour required by IPLD DAG-CBOR §Strictness rule 4. When
    /// <see langword="false"/> (the default for canonical modes), the
    /// writer reduces to single- or half-precision when the round-trip
    /// is lossless per RFC 8949 §4.2.2.
    /// </summary>
    public bool SuppressFloatReduction { get; set; }

    /// <summary>
    /// Gets the converter list. Tag-based dispatch uses the first converter
    /// in this list whose <see cref="CborConverter.HandledType"/> matches
    /// the runtime type of the value being written.
    /// </summary>
    public IList<CborConverter> Converters { get; } = [];

    /// <summary>
    /// Gets or sets the maximum byte-string length the reader will accept
    /// or the writer will emit. Defaults to 256 MiB. A wire form declaring
    /// a larger byte string is rejected as a denial-of-service hazard.
    /// </summary>
    public int MaxByteStringLength { get; set; } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum text-string length the reader will accept
    /// or the writer will emit. Defaults to 256 MiB.
    /// </summary>
    public int MaxTextStringLength { get; set; } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum array item count the reader will accept or
    /// the writer will emit. Defaults to 1,000,000.
    /// </summary>
    public int MaxArrayLength { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets the maximum map key/value pair count the reader will
    /// accept or the writer will emit. Defaults to 1,000,000.
    /// </summary>
    public int MaxMapEntryCount { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets the maximum container-nesting depth. Defaults to 64,
    /// matching the System.Text.Json default.
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum chain-length of tag introducers preceding
    /// a data item. Defaults to 16.
    /// </summary>
    public int MaxTagDepth { get; set; } = 16;

    /// <summary>
    /// Gets or sets the maximum number of chunks an indefinite-length
    /// byte- or text-string may contain. Defaults to 65,536.
    /// </summary>
    public int MaxIndefiniteStringChunks { get; set; } = 65_536;

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
