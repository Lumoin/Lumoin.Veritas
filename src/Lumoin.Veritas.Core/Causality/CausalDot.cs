using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Causality;

/// <summary>
/// One event identifier in the dotted observed-remove regime: the replica axis the event was minted on and the
/// strictly-increasing counter it was minted at. A dot names exactly one assertion event, ever — counters are
/// never reused on an axis — so causal knowledge about the event ("was it observed?") is a stable fact a
/// <see cref="CausalContext"/> can carry forever.
/// </summary>
/// <param name="Axis">The replica identity axis the event was minted on.</param>
/// <param name="Counter">The mint counter on that axis; strictly increasing in commit order, starting at 1.</param>
[DebuggerDisplay("CausalDot {Counter} on axis hash {Axis.GetHashCode()}")]
public readonly record struct CausalDot(ReplicaAxis Axis, ulong Counter)
{
    /// <summary>The serialized byte width of one dot: the axis bytes followed by the little-endian counter.</summary>
    public const int ByteWidth = ReplicaAxis.ByteWidth + sizeof(ulong);
}
