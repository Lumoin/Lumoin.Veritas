using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Represents the source of a CBOR-LD type table associated with a
/// registry entry. A registry entry's <c>typeTables</c> may contain
/// either fully-specified table objects or the sentinel string
/// <c>"callerProvidedTable"</c>; this discriminated union models
/// both cases in the type system rather than carrying a stringly-
/// typed payload.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="FromRegistry"/> to construct a registry-supplied
/// table source and <see cref="CallerProvided"/> to construct a
/// caller-provided marker. The encoder/decoder pattern-match on the
/// concrete subclass at dispatch time.
/// </para>
/// <para>
/// See <see href="https://www.w3.org/TR/cbor-ld-10/">W3C CBOR-LD 1.0</see>
/// — CBOR-LD Registry Entries.
/// </para>
/// </remarks>
public abstract class CborLdTypeTableSource
{
    private protected CborLdTypeTableSource()
    {
    }

    /// <summary>
    /// Creates a registry-supplied table source from the given mappings.
    /// </summary>
    /// <param name="mappings">The string-to-integer mappings.</param>
    /// <returns>A <see cref="CborLdRegistryProvidedTypeTable"/> wrapping the mappings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mappings"/> is <c>null</c>.</exception>
    public static CborLdTypeTableSource FromRegistry(IReadOnlyDictionary<string, int> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        return new CborLdRegistryProvidedTypeTable(mappings);
    }

    /// <summary>
    /// Returns the singleton caller-provided marker. The actual table
    /// content is supplied at encode/decode time via
    /// <see cref="CborLdCallerProvidedTypeTables"/>.
    /// </summary>
    public static CborLdTypeTableSource CallerProvided()
    {
        return CborLdCallerProvidedTypeTableMarker.Instance;
    }

    /// <summary>Gets whether this source is a caller-provided marker.</summary>
    public abstract bool IsCallerProvided { get; }

    /// <summary>
    /// Returns the registry-supplied mappings, or <c>null</c> when this
    /// source is a caller-provided marker.
    /// </summary>
    public abstract IReadOnlyDictionary<string, int>? Mappings { get; }
}

/// <summary>
/// A type table source carrying fully-specified mappings drawn from
/// the registry entry itself.
/// </summary>
[DebuggerDisplay("Registry: {Mappings.Count} mappings")]
public sealed class CborLdRegistryProvidedTypeTable: CborLdTypeTableSource
{
    internal CborLdRegistryProvidedTypeTable(IReadOnlyDictionary<string, int> mappings)
    {
        Mappings = mappings;
    }

    /// <inheritdoc/>
    public override bool IsCallerProvided => false;

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1721:Property names should not match get methods",
        Justification = "Mappings overrides the abstract property; the get-method overload does not exist on this type.")]
    public override IReadOnlyDictionary<string, int> Mappings { get; }
}

/// <summary>
/// A type table source marking the table as caller-provided. The
/// marker carries no mappings; the caller supplies them at encode or
/// decode time via <see cref="CborLdCallerProvidedTypeTables"/>.
/// </summary>
[DebuggerDisplay("CallerProvided")]
public sealed class CborLdCallerProvidedTypeTableMarker: CborLdTypeTableSource
{
    internal static CborLdCallerProvidedTypeTableMarker Instance { get; } = new();

    private CborLdCallerProvidedTypeTableMarker()
    {
    }

    /// <summary>The spec-defined sentinel string used in the
    /// <c>typeTables</c> array when the table is caller-provided.</summary>
    public const string SentinelValue = "callerProvidedTable";

    /// <inheritdoc/>
    public override bool IsCallerProvided => true;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, int>? Mappings => null;

    /// <inheritdoc/>
    public override string ToString()
    {
        return SentinelValue;
    }
}
