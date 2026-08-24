using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The identity-collision tripwire both dotted channel ends run over every inbound envelope BEFORE it reaches
/// the session: the local replica is the only minter on its own axis, so a peer context entry, pushed element,
/// or drop dot whose counter EXCEEDS the local context's LIVE own-axis maximum proves a second minter under the
/// same identity — the exchange refuses loudly by name before applying the colliding knowledge. Peer coverage
/// up to the local maximum is normal (the peer observed this replica's dots), and the LIVE maximum is the
/// comparison value because a concurrent session may legitimately have taught the peer newer local dots than
/// any one session's pinned snapshot carries.
/// </summary>
internal static class DottedIdentityTripwire
{
    /// <summary>Inspects one inbound envelope for coverage or dots beyond the local axis's own maximum.</summary>
    /// <param name="envelope">The inbound envelope.</param>
    /// <param name="localAxis">The local host identity axis.</param>
    /// <param name="readOwnAxisMaximum">The live own-axis maximum seam.</param>
    /// <returns><see langword="true"/> when the envelope proves a second minter under the local identity.</returns>
    public static bool Violates(ReconciliationEnvelope<DottedEntry<EncodedTriple>> envelope, ReplicaAxis localAxis, ReadOwnAxisMaximumDelegate readOwnAxisMaximum)
    {
        if(envelope.Context is { } context)
        {
            foreach(ReplicaCounterEntry entry in context.Clock.Entries)
            {
                if(entry.Count > 0 && IsLocalAxis(entry.Replica.AsSpan(), localAxis) && (ulong)entry.Count > readOwnAxisMaximum())
                {
                    return true;
                }
            }

            return false;
        }

        if(envelope.Elements is { } elements)
        {
            foreach(ReconciliationElementEntry<DottedEntry<EncodedTriple>> entry in elements.Entries)
            {
                if(entry.Element.Counter > 0 && IsLocalAxis(entry.Element.Replica.AsSpan(), localAxis) && (ulong)entry.Element.Counter > readOwnAxisMaximum())
                {
                    return true;
                }
            }

            return false;
        }

        if(envelope.Drop is { } drop)
        {
            foreach(DotState dot in drop.Dots)
            {
                if(dot.Counter > 0 && IsLocalAxis(dot.Replica.AsSpan(), localAxis) && (ulong)dot.Counter > readOwnAxisMaximum())
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    /// <summary>Whether replica bytes name the local identity axis.</summary>
    /// <param name="replica">The replica bytes to compare.</param>
    /// <param name="localAxis">The local host identity axis.</param>
    /// <returns><see langword="true"/> when the bytes are the local axis's.</returns>
    private static bool IsLocalAxis(ReadOnlySpan<byte> replica, ReplicaAxis localAxis)
    {
        return replica.SequenceEqual(localAxis.Bytes.Span);
    }
}
