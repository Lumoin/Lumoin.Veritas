namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Categorical description of the data item the reader is currently
/// positioned at, surfaced by <see cref="CborReader.PeekState"/>. Callers
/// dispatch on the state to choose the matching <c>Read*</c> method.
/// </summary>
public enum CborReaderState
{
    /// <summary>An unsigned integer (major type 0).</summary>
    UnsignedInteger,

    /// <summary>A negative integer (major type 1).</summary>
    NegativeInteger,

    /// <summary>A definite-length byte string (major type 2).</summary>
    ByteString,

    /// <summary>A definite-length text string (major type 3).</summary>
    TextString,

    /// <summary>An array introducer (major type 4).</summary>
    StartArray,

    /// <summary>A map introducer (major type 5).</summary>
    StartMap,

    /// <summary>A tag (major type 6); the next state is the tagged content.</summary>
    Tag,

    /// <summary>A Boolean simple value (false = 20, true = 21).</summary>
    Boolean,

    /// <summary>The CBOR null simple value (22).</summary>
    Null,

    /// <summary>The CBOR undefined simple value (23).</summary>
    Undefined,

    /// <summary>A simple value other than the standardised four.</summary>
    SimpleValue,

    /// <summary>A half-precision (binary16) floating-point value.</summary>
    HalfPrecisionFloat,

    /// <summary>A single-precision (binary32) floating-point value.</summary>
    SinglePrecisionFloat,

    /// <summary>A double-precision (binary64) floating-point value.</summary>
    DoublePrecisionFloat,

    /// <summary>The current open array's break point (for indefinite length) or final-item edge (for definite length).</summary>
    EndArray,

    /// <summary>The current open map's break point (for indefinite length) or final-pair edge (for definite length).</summary>
    EndMap,

    /// <summary>The source has been fully consumed and the reader is at top-level depth.</summary>
    Finished
}
