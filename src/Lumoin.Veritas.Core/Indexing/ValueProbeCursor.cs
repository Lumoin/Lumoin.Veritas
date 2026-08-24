using System;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>One probe hit: locators into the store's original terms.</summary>
/// <remarks>
/// Hits carry encoded ids, never terms reconstructed from parsed values — the dictionary is
/// lexical-identity-keyed, so two lexical forms of one instant are distinct terms with equal values, and
/// term-identity operators (<c>DISTINCT</c>, <c>sameTerm</c>, <c>GROUP BY</c>) must be indistinguishable
/// index-on versus scan.
/// </remarks>
/// <param name="Subject">The subject (the occurrence subject on an interval pair) of the located entry.</param>
/// <param name="Value">The located axis value term as it appears in the store — the start term on an interval pair.</param>
/// <param name="UpperValue">The interval end term on an interval-pair hit; <see cref="TermId.None"/> on a point-axis hit.</param>
public readonly record struct ValueProbeHit(TermId Subject, TermId Value, TermId UpperValue);

/// <summary>
/// The cursor a <see cref="ValueAccessMethod"/> probe returns: a forward, single-pass walk over the hits.
/// </summary>
/// <remarks>
/// The ordering contract is prose, pinned by behavior: hits arrive in ascending axis order, and two hits
/// on the same normalized axis position arrive in a deterministic order that is stable across probes of
/// one built index. A cursor is owned by its caller and disposed exactly once.
/// </remarks>
public abstract class ValueProbeCursor: IDisposable
{
    /// <summary>Advances to the next hit.</summary>
    /// <param name="hit">Receives the next hit when one exists.</param>
    /// <returns><see langword="true"/> while hits remain.</returns>
    public abstract bool TryAdvance(out ValueProbeHit hit);

    /// <summary>Releases the cursor's resources.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the cursor's resources.</summary>
    /// <param name="disposing">Whether the call is a deterministic dispose.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
