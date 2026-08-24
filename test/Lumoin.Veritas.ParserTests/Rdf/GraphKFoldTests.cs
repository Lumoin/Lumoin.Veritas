using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

[TestClass]
internal sealed class GraphKFoldTests
{
    public TestContext TestContext { get; set; } = null!;

    //Encoded node ids start at 10 (well clear of the TermId.None
    //sentinel at 0) so bound-to-X match queries inside the fold
    //do not alias with unbound queries.

    //Per-instance mutable state used by the test algebras below.
    //MSTest creates a fresh instance per test method so there is no
    //cross-test contamination.
    private int algebraInvocationCount;
    private readonly Dictionary<TermId, int> invocationsByNode = [];

    //Algebra 1: Sum-of-subtree. Count this node as 1 plus the sum of all
    //children's results. Always forces every child. Semantically equivalent
    //to a plain GraphFold.
    private IEnumerator<ForceRequest> SumAllChildrenAlgebra(
        TermId nodeId,
        ReadOnlyMemory<EncodedTriple> outgoingTriples,
        ChildHandles<int> children)
    {
        _ = nodeId;
        _ = outgoingTriples;

        algebraInvocationCount++;

        int sum = 1;
        for(int i = 0; i < children.Count; i++)
        {
            yield return ForceRequest.Force(i);
            sum += children.Get(i);
        }

        children.SetResult(sum);
    }

    [TestMethod]
    public async Task SumAllChildrenOnTreeReturnsTreeSize()
    {
        //Tree:
        //  10 -p-> 11 -p-> 12
        //  10 -p-> 13
        //All four nodes exist; algebra visits each exactly once; result is 4.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(10, 100, 13)
        ]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(10),
            algebra: SumAllChildrenAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(4, result);
        Assert.AreEqual(4, algebraInvocationCount);
    }

    [TestMethod]
    public async Task SumAllChildrenOnSingletonReturnsOne()
    {
        //Root 42 has no outgoing edges. Algebra sees zero children; result = 1.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(11, 100, 12)]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(42),
            algebra: SumAllChildrenAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, result);
        Assert.AreEqual(1, algebraInvocationCount);
    }

    //Algebra 2: Short-circuits at the first "positive" child. Leaf nodes
    //return their own id as the value; parents return 1 if any subtree has
    //a positive leaf, else 0.
    private IEnumerator<ForceRequest> AnyPositiveAlgebra(
        TermId nodeId,
        ReadOnlyMemory<EncodedTriple> outgoingTriples,
        ChildHandles<int> children)
    {
        _ = outgoingTriples;

        algebraInvocationCount++;

        if(children.Count == 0)
        {
            children.SetResult((int)nodeId.Encoded);
            yield break;
        }

        for(int i = 0; i < children.Count; i++)
        {
            yield return ForceRequest.Force(i);
            if(children.Get(i) > 0)
            {
                children.SetResult(1);
                yield break;
            }
        }

        children.SetResult(0);
    }

    [TestMethod]
    public async Task ShortCircuitStopsAfterFirstPositiveChild()
    {
        //Root 50 has three children with positive ids 60, 70, 80. The algebra
        //forces child 0, sees a positive value, short-circuits. Only two
        //algebra invocations should occur: root + first child.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(50, 100, 60),
            EncodedTriple.FromEncoded(50, 100, 70),
            EncodedTriple.FromEncoded(50, 100, 80)
        ]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(50),
            algebra: AnyPositiveAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, result);
        Assert.AreEqual(2, algebraInvocationCount);
    }

    //Algebra 3: Per-node invocation counting to verify shared-child dedup.
    private IEnumerator<ForceRequest> RecordInvocationAlgebra(
        TermId nodeId,
        ReadOnlyMemory<EncodedTriple> outgoingTriples,
        ChildHandles<int> children)
    {
        _ = outgoingTriples;

        invocationsByNode.TryGetValue(nodeId, out int prior);
        invocationsByNode[nodeId] = prior + 1;

        for(int i = 0; i < children.Count; i++)
        {
            yield return ForceRequest.Force(i);
            _ = children.Get(i);
        }

        children.SetResult(0);
    }

    [TestMethod]
    public async Task SharedChildInDagReducedExactlyOnce()
    {
        //DAG:
        //  15 -p-> 11 -p-> 13
        //  15 -p-> 12 -p-> 13
        //Node 13 is shared. Must be reduced exactly once.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(15, 100, 11),
            EncodedTriple.FromEncoded(15, 100, 12),
            EncodedTriple.FromEncoded(11, 100, 13),
            EncodedTriple.FromEncoded(12, 100, 13)
        ]);

        _ = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(15),
            algebra: RecordInvocationAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(15)]);
        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(11)]);
        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(12)]);
        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(13)]);
    }

    //Algebra 4: Force-through cycle. Should trip recursion detection.
    //Marked static — touches no instance state (CA1822).
    private static IEnumerator<ForceRequest> ForceAllAlgebra(
        TermId nodeId,
        ReadOnlyMemory<EncodedTriple> outgoingTriples,
        ChildHandles<int> children)
    {
        _ = nodeId;
        _ = outgoingTriples;

        for(int i = 0; i < children.Count; i++)
        {
            yield return ForceRequest.Force(i);
            _ = children.Get(i);
        }

        children.SetResult(1);
    }

    [TestMethod]
    public async Task ForcingThroughCycleThrowsInvalidOperationException()
    {
        //Cycle 10 -> 11 -> 12 -> 10. Driver must detect and throw.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(12, 100, 10)
        ]);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await GraphKFold.FoldAsync<int>(
                rootNodeId: TermId.FromEncoded(10),
                algebra: ForceAllAlgebra,
                match: store.AsMatchDelegate(),
                pool: null,
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Contains("recursion", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    //Algebra 5: Skip request. Marked static — touches no instance state (CA1822).
    private static IEnumerator<ForceRequest> YieldsSkipAlgebra(
        TermId nodeId,
        ReadOnlyMemory<EncodedTriple> outgoingTriples,
        ChildHandles<int> children)
    {
        _ = nodeId;
        _ = outgoingTriples;

        yield return ForceRequest.Skip();
        children.SetResult(42);
    }

    [TestMethod]
    public async Task SkipRequestResumesAlgebraWithoutChildForce()
    {
        //Root has no outgoing edges. Algebra yields Skip then SetResult.
        InMemoryGraphStore store = InMemoryGraphStore.Build([]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(10),
            algebra: YieldsSkipAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task GraphCataFacadeRoutesToKFoldForKAlgebra()
    {
        //Same tree as SumAllChildrenOnTreeReturnsTreeSize, but invoked through
        //GraphCata rather than GraphKFold directly. Overload resolution should
        //pick the KFold implementation.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(10, 100, 13)
        ]);

        int result = await GraphCata.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(10),
            algebra: (GraphAlgebras.GraphKAlgebra<int>)SumAllChildrenAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(4, result);
    }
}
