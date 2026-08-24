using System;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One frame of the shard-difference channel: exactly one of the request header, the reply header, or a
/// reconciliation envelope. The headers open the connection (request out, reply back) and every following
/// frame carries an envelope of the per-shard session, so one message type serves the whole connection and
/// one reader/writer pair frames it.
/// </summary>
/// <typeparam name="TElement">The element type the envelope's elements messages carry.</typeparam>
/// <param name="RequestHeader">The opening request header, or <see langword="null"/>.</param>
/// <param name="ReplyHeader">The serving endpoint's reply header, or <see langword="null"/>.</param>
/// <param name="Envelope">A session envelope, or <see langword="null"/>.</param>
internal sealed record ShardDifferenceFrame<TElement>(
    ShardDifferenceRequestHeader? RequestHeader,
    ShardDifferenceReplyHeader? ReplyHeader,
    ReconciliationEnvelope<TElement>? Envelope)
{
    /// <summary>Wraps a request header as a frame.</summary>
    /// <param name="header">The request header.</param>
    /// <returns>The frame.</returns>
    public static ShardDifferenceFrame<TElement> ForRequestHeader(ShardDifferenceRequestHeader header)
    {
        return new ShardDifferenceFrame<TElement>(header, null, null);
    }

    /// <summary>Wraps a reply header as a frame.</summary>
    /// <param name="header">The reply header.</param>
    /// <returns>The frame.</returns>
    public static ShardDifferenceFrame<TElement> ForReplyHeader(ShardDifferenceReplyHeader header)
    {
        return new ShardDifferenceFrame<TElement>(null, header, null);
    }

    /// <summary>Wraps a session envelope as a frame.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
    public static ShardDifferenceFrame<TElement> ForEnvelope(ReconciliationEnvelope<TElement> envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new ShardDifferenceFrame<TElement>(null, null, envelope);
    }
}
