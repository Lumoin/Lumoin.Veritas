using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The lighter-weight reference for the two-CLI wiring: a <see cref="FastProposer{TValue}"/>
/// drives a Fast CASPaxos round over two <see cref="ConsensusNode{TValue}"/> acceptors
/// connected by in-memory channels, reaching a fast-quorum agreement. The CLI two-node
/// path mirrors this exactly — the same proposer and acceptors over the same
/// <see cref="ConsensusEndpointDelegate{TValue}"/> seam — swapping the in-memory channel for
/// a socket. Verisync verifies the protocol's safety (its own suite, TLA+ included); the
/// only thing asserted here is that the wiring connects and the agreement is observable at
/// this level — the property the CLI tier must also expose.
/// </summary>
[TestClass]
internal sealed class VerisyncConsensusConnectionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Two acceptor nodes, each connected to the proposer by an in-memory request/reply
    /// channel pair, reach a fast-quorum agreement on a value. The committed outcome and the
    /// accept count are observable from the proposer side — the same observability the CLI
    /// tier must provide over sockets.
    /// </summary>
    [TestMethod]
    public async Task TwoChannelConnectedAcceptorsReachFastAgreement()
    {
        CancellationToken cancellationToken = TestContext.CancellationToken;

        List<ConsensusEndpointDelegate<int>> endpoints = new(2);
        List<Channel<ConsensusRequest<int>>> requestChannels = new(2);
        List<Task> nodeLoops = new(2);

        for(int i = 0; i < 2; i++)
        {
            //One node, driven over its own in-memory request/reply channel pair — the
            //"connection" the proposer talks through, in place of a socket.
            Channel<ConsensusRequest<int>> requests = Channel.CreateUnbounded<ConsensusRequest<int>>();
            Channel<ConsensusReply<int>> replies = Channel.CreateUnbounded<ConsensusReply<int>>();
            ConsensusNode<int> node = new();

            nodeLoops.Add(node.RunAsync(
                requests.Reader.ReadAllAsync(cancellationToken),
                (reply, token) =>
                {
                    replies.Writer.TryWrite(reply);

                    return ValueTask.CompletedTask;
                },
                //No durable acceptor state in this in-memory wiring; persistence is the
                //CLI tier's concern.
                persistAcceptor: null,
                cancellationToken));

            requestChannels.Add(requests);
            endpoints.Add(async (request, token) =>
            {
                await requests.Writer.WriteAsync(request, token).ConfigureAwait(false);

                return await replies.Reader.ReadAsync(token).ConfigureAwait(false);
            });
        }

        FastProposer<int> proposer = new(endpoints);

        (int acceptedCount, bool isCommitted) = await proposer.TryFastWriteAsync(FastBallot.InitialFast(), 42, cancellationToken).ConfigureAwait(false);

        Assert.IsTrue(isCommitted, "A fast write reaching both connected acceptors must commit.");
        Assert.AreEqual(2, acceptedCount, "Both connected acceptors must accept.");

        //Close the connections so the node loops complete, then drain them.
        foreach(Channel<ConsensusRequest<int>> requests in requestChannels)
        {
            requests.Writer.Complete();
        }

        await Task.WhenAll(nodeLoops).ConfigureAwait(false);
    }
}
