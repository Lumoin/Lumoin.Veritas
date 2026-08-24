using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Production and tooling <see cref="BlankNodeDelegate"/> defaults.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System"/> is the default the parsers use: it allocates a fresh
/// <c>b0</c>, <c>b1</c>, … label per call, with the counter scoped to the request's
/// <see cref="Utf8StringPool"/>. Because each parse typically runs against a fresh
/// pool, the labels restart from <c>b0</c> per document — matching the previous
/// per-parser counter behaviour. When a request carries a non-empty
/// <see cref="BlankNodeRequest.CorrelationKey"/>, the same key yields the same
/// label within a given <see cref="BlankNodeRequest.SolutionId"/>, implementing the
/// SPARQL <c>BNODE(literal)</c> per-solution-correlated semantics.
/// </para>
/// <para>
/// <see cref="ByCallSite"/> derives the label from the occurrence's source byte
/// offset (label shape <c>b&lt;startByte&gt;</c>). It is allocation-order
/// independent and reproducible, which suits tooling that must produce stable
/// labels for the same source text.
/// </para>
/// </remarks>
public static class VeritasBlankNodes
{
    private static ConditionalWeakTable<Utf8StringPool, PoolState> PoolStates { get; } = new();

    /// <summary>Fresh-per-call labels (<c>b0</c>, <c>b1</c>, …) counted per pool; correlated labels when a key is supplied. The production default.</summary>
    public static BlankNodeDelegate System { get; } = SystemImpl;

    /// <summary>Labels derived from the call-site byte offset (<c>b&lt;startByte&gt;</c>); allocation-order independent and reproducible.</summary>
    public static BlankNodeDelegate ByCallSite { get; } = ByCallSiteImpl;

    private static Utf8String SystemImpl(in BlankNodeRequest request)
    {
        PoolState state = PoolStates.GetValue(request.Pool, static _ => new PoolState());
        lock(state.Gate)
        {
            if(request.CorrelationKey.IsEmpty)
            {
                return InternCounter(request.Pool, state.Next++);
            }

            Utf8String correlationKey = request.Pool.Intern(request.CorrelationKey.Span);
            (Guid SolutionId, Utf8String Key) cacheKey = (request.SolutionId, correlationKey);
            if(state.Correlated.TryGetValue(cacheKey, out Utf8String existing))
            {
                return existing;
            }

            Utf8String fresh = InternCounter(request.Pool, state.Next++);
            state.Correlated[cacheKey] = fresh;

            return fresh;
        }
    }

    private static Utf8String ByCallSiteImpl(in BlankNodeRequest request)
    {
        string raw = string.Create(CultureInfo.InvariantCulture, $"b{request.CallSiteSpan.StartByte}");

        return request.Pool.Intern(raw);
    }

    /// <summary>Interns a counter-based label (<c>b&lt;counter&gt;</c>) into the pool.</summary>
    /// <param name="pool">The pool to intern into.</param>
    /// <param name="counter">The counter value.</param>
    /// <returns>The interned label.</returns>
    private static Utf8String InternCounter(Utf8StringPool pool, int counter)
    {
        string raw = string.Create(CultureInfo.InvariantCulture, $"b{counter}");

        return pool.Intern(raw);
    }

    /// <summary>Per-pool allocation state: a monotonic counter and the per-solution correlation cache.</summary>
    private sealed class PoolState
    {
        /// <summary>Gets the lock guarding <see cref="Next"/> and <see cref="Correlated"/>.</summary>
        public object Gate { get; } = new();

        /// <summary>Gets or sets the next fresh-label counter value.</summary>
        public int Next { get; set; }

        /// <summary>Gets the cache mapping (solution, correlation key) to its assigned label.</summary>
        public Dictionary<(Guid SolutionId, Utf8String Key), Utf8String> Correlated { get; } = [];
    }
}
