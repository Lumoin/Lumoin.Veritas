using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// Adapts an owned <see cref="TcpClient"/> to a <see cref="PeerChannelConnection"/>'s transport seam, so
/// disposing the connection closes the socket under it — the teardown that releases both ends of an exchange.
/// Every outbound connection this command hands to a channel client carries one of these, whether the channel
/// is the one-shot shard and dotted kind or the long-lived metadata kind.
/// </summary>
/// <param name="client">The owned client.</param>
internal sealed class OwnedTcpClient(TcpClient client): IAsyncDisposable
{
    /// <summary>The owned client.</summary>
    private TcpClient Client { get; } = client;

    /// <summary>Closes the socket. Disposal is idempotent, as the connection's own is.</summary>
    /// <returns>A completed task; closing a socket has no asynchronous form.</returns>
    public ValueTask DisposeAsync()
    {
        Client.Dispose();

        return ValueTask.CompletedTask;
    }
}
