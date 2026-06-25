using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Database;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Test-only string conveniences over the byte-native (<see cref="Utf8String"/>) query surface of
/// <see cref="VeritasEngine"/>. Production is strictly UTF-8 — query text reaches the parser as bytes with no
/// string round-trip — so these live in the test assembly only, letting tests keep readable string literals
/// while exercising the real <see cref="Utf8String"/> entry points (each just encodes the literal once).
/// </summary>
internal static class VeritasEngineStringQueryExtensions
{
    /// <summary>Runs a SELECT or ASK query from a .NET string.</summary>
    /// <param name="engine">The engine under test.</param>
    /// <param name="sparql">The query text.</param>
    /// <param name="baseIri">The base IRI, or <see langword="null"/> for none.</param>
    /// <param name="accessContext">The opaque access context, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The query result.</returns>
    public static ValueTask<VeritasQueryResult> QueryAsync(this VeritasEngine engine, string sparql, string? baseIri = null, AccessContext? accessContext = null, CancellationToken cancellationToken = default)
    {
        return engine.QueryAsync(Utf8String.FromString(sparql), baseIri is null ? null : Utf8String.FromString(baseIri), accessContext, cancellationToken);
    }

    /// <summary>Runs an ASK query from a .NET string.</summary>
    /// <param name="engine">The engine under test.</param>
    /// <param name="sparql">The ASK query text.</param>
    /// <param name="accessContext">The opaque access context, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the query has a solution.</returns>
    public static ValueTask<bool> AskAsync(this VeritasEngine engine, string sparql, AccessContext? accessContext = null, CancellationToken cancellationToken = default)
    {
        return engine.AskAsync(Utf8String.FromString(sparql), accessContext, cancellationToken);
    }

    /// <summary>Executes a SPARQL Update from a .NET string.</summary>
    /// <param name="engine">The engine under test.</param>
    /// <param name="sparql">The update text.</param>
    /// <param name="baseIri">The base IRI, or <see langword="null"/> for none.</param>
    /// <param name="accessContext">The opaque access context, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>A task that completes when the update has committed.</returns>
    public static ValueTask UpdateAsync(this VeritasEngine engine, string sparql, string? baseIri = null, AccessContext? accessContext = null, CancellationToken cancellationToken = default)
    {
        return engine.UpdateAsync(Utf8String.FromString(sparql), baseIri is null ? null : Utf8String.FromString(baseIri), accessContext, cancellationToken);
    }

    /// <summary>Opens a streaming SELECT from a .NET string.</summary>
    /// <param name="engine">The engine under test.</param>
    /// <param name="sparql">The SELECT query text.</param>
    /// <param name="baseIri">The base IRI, or <see langword="null"/> for none.</param>
    /// <param name="accessContext">The opaque access context, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts streaming.</param>
    /// <returns>The streaming SELECT result.</returns>
    public static VeritasSelectStream StreamSelect(this VeritasEngine engine, string sparql, string? baseIri = null, AccessContext? accessContext = null, CancellationToken cancellationToken = default)
    {
        return engine.StreamSelect(Utf8String.FromString(sparql), baseIri is null ? null : Utf8String.FromString(baseIri), accessContext, cancellationToken);
    }
}
