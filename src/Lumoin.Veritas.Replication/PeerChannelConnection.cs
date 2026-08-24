using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One duplex connection to a peer's reconciliation channel endpoint — the shard-difference or the
/// dotted-difference serve: the pipe the request frames are written to, the pipe the response frames are read
/// from, and the transport that backs them. The connection's unconditional disposal is the channel's LIVENESS
/// mechanism — a session wind-down cannot unstick a backpressure-blocked peer send, so the exchange disposes
/// the connection on every exit (success, cap trip, fault, cancellation) and the peer treats the teardown as a
/// normal end of serve. Disposal is idempotent.
/// </summary>
public sealed class PeerChannelConnection: IAsyncDisposable
{
    /// <summary>Whether the connection is already disposed; guarded by an atomic exchange so disposal is idempotent.</summary>
    private int disposed;

    /// <summary>Creates a connection over a duplex pipe pair and its backing transport.</summary>
    /// <param name="requestWriter">The pipe request frames are written to.</param>
    /// <param name="responseReader">The pipe response frames are read from.</param>
    /// <param name="transport">The backing transport disposed with the connection, or <see langword="null"/> when the pipes stand alone (an in-process pipe pair).</param>
    /// <exception cref="ArgumentNullException"><paramref name="requestWriter"/> or <paramref name="responseReader"/> is <see langword="null"/>.</exception>
    public PeerChannelConnection(PipeWriter requestWriter, PipeReader responseReader, IAsyncDisposable? transport = null)
    {
        ArgumentNullException.ThrowIfNull(requestWriter);
        ArgumentNullException.ThrowIfNull(responseReader);

        RequestWriter = requestWriter;
        ResponseReader = responseReader;
        Transport = transport;
    }

    /// <summary>The pipe request frames are written to.</summary>
    public PipeWriter RequestWriter { get; }

    /// <summary>The pipe response frames are read from.</summary>
    public PipeReader ResponseReader { get; }

    /// <summary>The backing transport disposed with the connection, or <see langword="null"/>.</summary>
    private IAsyncDisposable? Transport { get; }

    /// <summary>Completes both pipe ends and disposes the backing transport; idempotent, so an explicit teardown and a scope's disposal may both run.</summary>
    /// <returns>A task that completes when the connection is torn down.</returns>
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await RequestWriter.CompleteAsync().ConfigureAwait(false);
        await ResponseReader.CompleteAsync().ConfigureAwait(false);
        if(Transport is not null)
        {
            await Transport.DisposeAsync().ConfigureAwait(false);
        }
    }
}
