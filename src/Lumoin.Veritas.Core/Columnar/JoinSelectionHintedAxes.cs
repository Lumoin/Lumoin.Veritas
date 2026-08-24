using System;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Which axes of a join-route decision a per-query hint overlaid. The decision's kind and reason name who
/// decided its ROUTE; this names every axis a hint set, so a consumer reading a mixed decision off the
/// trace bus tells a hint-set axis from a selector-set one without convention. Only an axis a hint
/// actually overlaid is named: an axis where a policy force outranked the hint is not.
/// </summary>
[Flags]
public enum JoinSelectionHintedAxes
{
    /// <summary>No axis was overlaid by a hint. The default.</summary>
    None = 0,

    /// <summary>The route axis carries a hint's value.</summary>
    Route = 1,

    /// <summary>The depth axis carries a hint's value.</summary>
    Depth = 2,

    /// <summary>The trie-build axis carries a hint's value.</summary>
    Build = 4,

    /// <summary>The factorisation axis carries a hint's value.</summary>
    Factorization = 8
}
