using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Algebra;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Tests for <see cref="GraphPersistence"/> writers. Exercise the
/// <see cref="Stream"/> convenience overloads, which wrap a
/// <see cref="System.IO.Pipelines.PipeWriter"/> internally; the
/// PipeWriter-direct path is exercised on the same code path through
/// the convenience wrapper.
/// </summary>
[TestClass]
internal sealed class GraphPersistenceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task WriteEdgeListProducesTabSeparatedLines()
    {
        AdjacencyList<int> list = new();
        list.AddEdge(1, 2).AddEdge(1, 3).AddEdge(2, 4);

        using MemoryStream stream = new();
        await GraphPersistence.WriteEdgeListAsync(
            list.AsGraphSource(),
            stream,
            static n => n.ToString(CultureInfo.InvariantCulture),
            TestContext.CancellationToken).ConfigureAwait(false);

        string output = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Assert.AreEqual("1\t2\n1\t3\n2\t4\n", output);
    }

    [TestMethod]
    public async Task WriteAdjacencyListGroupsEdgesPerSource()
    {
        AdjacencyList<int> list = new();
        list.AddEdge(1, 2).AddEdge(1, 3).AddEdge(2, 4);

        using MemoryStream stream = new();
        await GraphPersistence.WriteAdjacencyListAsync(
            list.AsGraphSource(),
            [1, 2, 3],
            stream,
            static n => n.ToString(CultureInfo.InvariantCulture),
            TestContext.CancellationToken).ConfigureAwait(false);

        string output = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        //Node 1 → {2, 3}; node 2 → {4}; node 3 → {} (no outgoing).
        Assert.AreEqual("1\t2\t3\n2\t4\n3\n", output);
    }

    [TestMethod]
    public async Task WriteEdgeListStreamsLargeGraphsWithoutMaterialising()
    {
        //Path of 10000 nodes → 9999 edges. The point of this test is
        //not to verify content exhaustively (we trust Path's edges)
        //but to confirm the writer copes with a non-trivial input
        //size without OOM. The test passing in finite memory is the
        //assertion.
        GraphSource<int> source = GraphGenerators.Path(10_000);

        using MemoryStream stream = new();
        await GraphPersistence.WriteEdgeListAsync(
            source,
            stream,
            static n => n.ToString(CultureInfo.InvariantCulture),
            TestContext.CancellationToken).ConfigureAwait(false);

        //9999 edges; each line is "i\ti+1\n" — count newlines.
        byte[] bytes = stream.ToArray();
        int newlineCount = 0;
        foreach(byte b in bytes)
        {
            if(b == (byte)'\n')
            {
                newlineCount++;
            }
        }
        Assert.AreEqual(9_999, newlineCount);
    }
}
