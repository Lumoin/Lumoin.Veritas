using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The per-solution blank-node identity scope SPARQL <c>BNODE</c> (§17.4.2.3) builds against: within one solution
/// mapping the same correlation key yields the same blank node, while the same key in a different solution — and
/// every keyless allocation — yields a fresh one. The mechanism generalises beyond <c>BNODE</c>: SPARQL Update's
/// <c>INSERT … WHERE</c> instantiates a template's blank nodes once per solution with exactly this per-solution
/// correlation, so the same scope is the substrate there too.
/// </summary>
/// <remarks>
/// <para>
/// Labels are minted through a <see cref="BlankNodeDelegate"/> seam (never allocated raw), so the identity source
/// stays observable and swappable; the scope owns the correlation <em>policy</em>, the seam owns the <em>label</em>.
/// </para>
/// <para>
/// Correlation is keyed on the <see cref="SparqlSolution"/> object identity via a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, so a solution's correlation map is reclaimed when the solution
/// itself is — the scope can outlive any single query without retaining dead solutions. A scope is safe to share
/// across threads: each solution's map is guarded by its own lock.
/// </para>
/// </remarks>
public sealed class SolutionBlankNodeScope
{
    /// <summary>The seam that mints blank-node labels.</summary>
    private BlankNodeDelegate Allocator { get; }

    /// <summary>The pool minted labels are interned into.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Per-solution correlation maps (key → blank node), keyed weakly on the solution so dead solutions are reclaimed.</summary>
    private ConditionalWeakTable<SparqlSolution, Dictionary<Utf8String, BlankNode>> PerSolution { get; } = new();

    /// <summary>Constructs a scope over a label seam and the pool its labels are interned into.</summary>
    /// <param name="allocator">The seam that mints blank-node labels (a keyless request per mint; correlation is this scope's concern).</param>
    /// <param name="pool">The pool minted labels are interned into.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SolutionBlankNodeScope(BlankNodeDelegate allocator, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(pool);

        Allocator = allocator;
        Pool = pool;
    }

    /// <summary>Mints a fresh blank node, distinct on every call — the <c>BNODE()</c> (no-argument) form.</summary>
    /// <returns>A fresh blank node.</returns>
    public BlankNode Fresh()
    {
        return new BlankNode(Mint());
    }

    /// <summary>
    /// Returns the blank node correlated to <paramref name="key"/> within <paramref name="solution"/> — the same node
    /// for the same key in the same solution, a fresh node for a new key or a different solution. The <c>BNODE(key)</c>
    /// form.
    /// </summary>
    /// <param name="solution">The solution the correlation is scoped to.</param>
    /// <param name="key">The correlation key (the lexical form of <c>BNODE</c>'s string argument).</param>
    /// <returns>The blank node correlated to the key within the solution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solution"/> is <see langword="null"/>.</exception>
    public BlankNode Correlated(SparqlSolution solution, Utf8String key)
    {
        ArgumentNullException.ThrowIfNull(solution);

        Dictionary<Utf8String, BlankNode> map = PerSolution.GetValue(solution, static _ => []);
        lock(map)
        {
            if(map.TryGetValue(key, out BlankNode? existing))
            {
                return existing;
            }

            BlankNode created = new(Mint());
            map[key] = created;

            return created;
        }
    }

    /// <summary>
    /// Links <paramref name="child"/> to <paramref name="parent"/>'s correlation map so they share one blank-node
    /// scope. A solution that derives another — <c>BIND</c>/projection <c>Extend</c> copies a row's bindings forward
    /// into a new <see cref="SparqlSolution"/> — must keep the same per-row <c>BNODE</c> correlation across the chain:
    /// the same key resolves to the same blank node before and after the row is extended.
    /// </summary>
    /// <param name="parent">The solution being extended.</param>
    /// <param name="child">The derived solution that should share <paramref name="parent"/>'s correlation.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Link(SparqlSolution parent, SparqlSolution child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);

        Dictionary<Utf8String, BlankNode> map = PerSolution.GetValue(parent, static _ => []);
        PerSolution.AddOrUpdate(child, map);
    }

    /// <summary>Mints a fresh, keyless label through the seam.</summary>
    /// <returns>The interned label.</returns>
    private Utf8String Mint() => Allocator(new BlankNodeRequest(Guid.Empty, ReadOnlyMemory<byte>.Empty, default, Pool));
}
