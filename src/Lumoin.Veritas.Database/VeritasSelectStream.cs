using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// A streaming SELECT result from <see cref="VeritasEngine.StreamSelectAsync"/>: the head variables for the result
/// header and the solutions yielded incrementally (server-sent events, paging, large scans). It holds the query's
/// parse resources, so dispose it once the solutions have been consumed — a <c>using</c> scoped around the
/// enumeration. Enumerate <see cref="Solutions"/> before disposing; the decoded terms remain valid afterwards
/// (they belong to the engine's dictionary), but the head variables and the lazy evaluation do not.
/// </summary>
public sealed class VeritasSelectStream: IDisposable
{
    /// <summary>Initializes the streaming result.</summary>
    /// <param name="variables">The SELECT head variables, in column order.</param>
    /// <param name="solutions">The solutions, yielded as they are produced.</param>
    /// <param name="pool">The parse pool kept alive until this result is disposed.</param>
    internal VeritasSelectStream(IReadOnlyList<Utf8String> variables, IAsyncEnumerable<SparqlSolution> solutions, Utf8StringPool pool)
    {
        Variables = variables;
        Solutions = solutions;
        Pool = pool;
    }

    /// <summary>The SELECT head variables, in column order — the result header.</summary>
    public IReadOnlyList<Utf8String> Variables { get; }

    /// <summary>The solutions, yielded incrementally as they are produced.</summary>
    public IAsyncEnumerable<SparqlSolution> Solutions { get; }

    /// <summary>The query's parse pool, kept alive for the lifetime of this result and released on <see cref="Dispose"/>.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Releases the query's parse resources. Enumerate <see cref="Solutions"/> first.</summary>
    public void Dispose()
    {
        Pool.Dispose();
    }
}
