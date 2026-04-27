using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Rdf;

[TestClass]
internal sealed class GraphKFoldTests
{
    public TestContext TestContext { get; set; } = null!;

    //Per-instance mutable state used by the test algebras below.
    //MSTest creates a fresh instance per test method so there is no
    //cross-test contamination.
    private int algebraInvocationCount;
    private readonly Dictionary<TermId, int> invocationsByNode = [];

    //----------------------------------------------------------------------
    //Algebra 1: Sum-of-subtree. Count this node as 1 plus the sum of all
    //children's results. Always forces every child. Semantically equivalent
    //to a plain GraphFold.
    //----------------------------------------------------------------------
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
        //  0 -p-> 1 -p-> 2
        //  0 -p-> 3
        //All four nodes exist; algebra visits each exactly once; result is 4.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(0, 100, 3)
        ]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
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
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(1, 100, 2)]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(42),
            algebra: SumAllChildrenAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, result);
        Assert.AreEqual(1, algebraInvocationCount);
    }

    //----------------------------------------------------------------------
    //Algebra 2: Short-circuits at the first "positive" child. Leaf nodes
    //return their own id as the value; parents return 1 if any subtree has
    //a positive leaf, else 0.
    //----------------------------------------------------------------------
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
        //Root 5 has three children with positive ids 10, 20, 30. The algebra
        //forces child 0, sees a positive value, short-circuits. Only two
        //algebra invocations should occur: root + first child.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(5, 100, 10),
            EncodedTriple.FromEncoded(5, 100, 20),
            EncodedTriple.FromEncoded(5, 100, 30)
        ]);

        int result = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(5),
            algebra: AnyPositiveAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, result);
        Assert.AreEqual(2, algebraInvocationCount);
    }

    //----------------------------------------------------------------------
    //Algebra 3: Per-node invocation counting to verify shared-child dedup.
    //----------------------------------------------------------------------
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
        //  5 -p-> 1 -p-> 3
        //  5 -p-> 2 -p-> 3
        //Node 3 is shared. Must be reduced exactly once.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(5, 100, 1),
            EncodedTriple.FromEncoded(5, 100, 2),
            EncodedTriple.FromEncoded(1, 100, 3),
            EncodedTriple.FromEncoded(2, 100, 3)
        ]);

        _ = await GraphKFold.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(5),
            algebra: RecordInvocationAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(5)]);
        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(1)]);
        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(2)]);
        Assert.AreEqual(1, invocationsByNode[TermId.FromEncoded(3)]);
    }

    //----------------------------------------------------------------------
    //Algebra 4: Force-through cycle. Should trip recursion detection.
    //Marked static — touches no instance state (CA1822).
    //----------------------------------------------------------------------
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
        //Cycle 0 -> 1 -> 2 -> 0. Driver must detect and throw.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(2, 100, 0)
        ]);

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await GraphKFold.FoldAsync<int>(
                rootNodeId: TermId.FromEncoded(0),
                algebra: ForceAllAlgebra,
                match: store.AsMatchDelegate(),
                pool: null,
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Contains("recursion", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    //----------------------------------------------------------------------
    //Algebra 5: Skip request. Marked static — touches no instance state (CA1822).
    //----------------------------------------------------------------------
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
            rootNodeId: TermId.FromEncoded(0),
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
            EncodedTriple.FromEncoded(0, 100, 1),
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(0, 100, 3)
        ]);

        int result = await GraphCata.FoldAsync<int>(
            rootNodeId: TermId.FromEncoded(0),
            algebra: (GraphAlgebras.GraphKAlgebra<int>)SumAllChildrenAlgebra,
            match: store.AsMatchDelegate(),
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(4, result);
    }
}
