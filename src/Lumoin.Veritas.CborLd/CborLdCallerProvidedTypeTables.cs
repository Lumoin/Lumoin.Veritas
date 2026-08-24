using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// A collection of caller-provided type tables, keyed by type name.
/// Passed to <see cref="CborLdEncoder.EncodeAsync"/> and
/// <see cref="CborLdDecoder.DecodeAsync"/> when the registry entry
/// declares one or more type tables as <c>"callerProvidedTable"</c>
/// per W3C CBOR-LD 1.0.
/// </summary>
/// <remarks>
/// Fluent: <see cref="Add"/> returns the same instance so callers may
/// chain. The empty collection <see cref="Empty"/> is the default for
/// the optional API parameter and stands in for "no caller-provided
/// tables required by this call."
/// </remarks>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public sealed class CborLdCallerProvidedTypeTables
{
    private readonly Dictionary<string, CborLdCallerProvidedTypeTable> tables;

    /// <summary>
    /// Initialises a new empty collection.
    /// </summary>
    public CborLdCallerProvidedTypeTables()
    {
        tables = new Dictionary<string, CborLdCallerProvidedTypeTable>(StringComparer.Ordinal);
    }

    /// <summary>A collection with no tables.</summary>
    public static CborLdCallerProvidedTypeTables Empty { get; } = new();

    /// <summary>
    /// Registers a caller-provided table for a type name, replacing any
    /// previously-added table for the same type. Returns this instance
    /// for fluent chaining.
    /// </summary>
    /// <param name="table">The table to register.</param>
    /// <returns>The same collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <c>null</c>.</exception>
    public CborLdCallerProvidedTypeTables Add(CborLdCallerProvidedTypeTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        tables[table.TypeName] = table;
        return this;
    }

    /// <summary>Gets the number of registered caller-provided tables.</summary>
    public int Count => tables.Count;

    internal bool TryGet(string typeName, out CborLdCallerProvidedTypeTable? table)
    {
        return tables.TryGetValue(typeName, out table);
    }

    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"Tables = {tables.Count}");
}
