using System;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Non-generic base for CBOR converters. The generic
/// <see cref="CborConverter{T}"/> derives from this type so a converter
/// collection can hold heterogeneous converter instances behind a common
/// reference type.
/// </summary>
/// <remarks>
/// User code authors converters by deriving from <see cref="CborConverter{T}"/>;
/// the non-generic surface exists for the converter-registry plumbing that
/// dispatches to the right typed converter at runtime.
/// </remarks>
public abstract class CborConverter
{
    /// <summary>
    /// Initialises a new converter. Sealed so direct derivation is
    /// channelled through <see cref="CborConverter{T}"/>.
    /// </summary>
    private protected CborConverter()
    {
    }

    /// <summary>
    /// Gets the runtime type the converter handles. Used by the converter
    /// registry to dispatch based on a value's runtime type.
    /// </summary>
    public abstract Type HandledType { get; }
}
