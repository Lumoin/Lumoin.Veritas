using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Centralised throw helpers used by the CBOR codec. Routing exception
/// construction through a small set of helpers keeps message text consistent
/// and lets the JIT inline the call sites that do not throw.
/// </summary>
public static class CborThrowHelper
{
    /// <summary>Throws an <see cref="InvalidOperationException"/> indicating the requested operation is not valid in the codec's current state.</summary>
    [DoesNotReturn]
    public static void ThrowInvalidState(string operation, string state)
    {
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"Cannot perform '{operation}' with the CBOR codec in state '{state}'."));
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> indicating an open container has the wrong number of items written.</summary>
    [DoesNotReturn]
    public static void ThrowContainerLengthMismatch(string container, int expected, int actual)
    {
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"CBOR {container} declared {expected} items but {actual} were written before close."));
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> indicating a feature is disabled by the active conformance mode.</summary>
    [DoesNotReturn]
    public static void ThrowFeatureDisabledByConformanceMode(string feature, CborConformanceMode mode)
    {
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"CBOR feature '{feature}' is disabled under conformance mode '{mode}'."));
    }

    /// <summary>Throws an <see cref="ArgumentOutOfRangeException"/> for a length that exceeds <c>Int32.MaxValue</c>.</summary>
    [DoesNotReturn]
    public static void ThrowLengthExceedsInt32(string parameterName)
    {
        throw new ArgumentOutOfRangeException(parameterName, "CBOR length values must fit in Int32 for buffer-friendly encoding paths.");
    }

    /// <summary>Throws a <see cref="FormatException"/> when text data does not validate as UTF-8.</summary>
    [DoesNotReturn]
    public static void ThrowInvalidUtf8()
    {
        throw new FormatException("CBOR text string contains invalid UTF-8.");
    }

    /// <summary>Throws an <see cref="InvalidOperationException"/> for a write that produces a non-finite or otherwise forbidden float value.</summary>
    [DoesNotReturn]
    public static void ThrowDisallowedFloatValue(double value, CborConformanceMode mode)
    {
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"CBOR conformance mode '{mode}' forbids the float value {value}."));
    }
}
