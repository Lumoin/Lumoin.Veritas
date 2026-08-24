using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// The collection walker's honest outcomes: a well-formed chain reads whole,
/// and a broken, cyclic, or ambiguous chain names its ending instead of
/// silently truncating — the members returned are always the determined
/// prefix, and the outcome states what the graph left undetermined.
/// </summary>
[TestClass]
internal sealed class RdfCollectionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    //Fixed IDs used across all tests to represent rdf:first, rdf:rest, rdf:nil.
    //rdf:first and rdf:rest are properties (IRIs); rdf:nil is an IRI as well.
    //Typing them as IriId satisfies the RdfCollection API which takes IriId for
    //all three. For rdf:nil the head overload also accepts a TermId, so it
    //converts implicitly from IriId → TermId where needed.

    /// <summary>The fixed <c>rdf:first</c> identifier of the fixture graphs.</summary>
    private static IriId FirstId { get; } = IriId.FromUnchecked(TermId.FromEncoded(1));

    /// <summary>The fixed <c>rdf:rest</c> identifier of the fixture graphs.</summary>
    private static IriId RestId { get; } = IriId.FromUnchecked(TermId.FromEncoded(2));

    /// <summary>The fixed <c>rdf:nil</c> identifier of the fixture graphs.</summary>
    private static IriId NilId { get; } = IriId.FromUnchecked(TermId.FromEncoded(3));

    /// <summary>The nil head reads as the empty well-formed list.</summary>
    [TestMethod]
    public async Task ReadReturnsEmptyWellFormedForNilHead()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            NilId, FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(RdfCollectionOutcome.WellFormed, read.Outcome);
        Assert.HasCount(0, read.Members);
    }

    /// <summary>A well-formed chain reads its members in list order with the well-formed outcome.</summary>
    [TestMethod]
    public async Task ReadReturnsMembersInOrder()
    {
        //List head at 10, containing [100, 101, 102].
        //Cells are 10 -> 11 -> 12 -> nil.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, RestId.Encoded, 11),
            EncodedTriple.FromEncoded(11, FirstId.Encoded, 101),
            EncodedTriple.FromEncoded(11, RestId.Encoded, 12),
            EncodedTriple.FromEncoded(12, FirstId.Encoded, 102),
            EncodedTriple.FromEncoded(12, RestId.Encoded, NilId.Encoded)
        ]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(RdfCollectionOutcome.WellFormed, read.Outcome);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101), TermId.FromEncoded(102) },
            (System.Collections.ICollection)read.Members);
    }

    /// <summary>A chain whose cell lacks <c>rdf:rest</c> reads the determined prefix and names the break — the truncation is never silent.</summary>
    [TestMethod]
    public async Task ReadNamesTheBrokenChainAtCellWithoutRest()
    {
        //Malformed: second cell has first but no rest. The determined
        //prefix is both members; the outcome names the break.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, RestId.Encoded, 11),
            EncodedTriple.FromEncoded(11, FirstId.Encoded, 101)
        ]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(RdfCollectionOutcome.BrokenChain, read.Outcome);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101) },
            (System.Collections.ICollection)read.Members);
    }

    /// <summary>A cyclic chain reads each cell once and names the cycle.</summary>
    [TestMethod]
    public async Task ReadNamesTheCycleWithoutInfiniteLoop()
    {
        //Malformed cyclic list: 10.rest -> 11, 11.rest -> 10.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, RestId.Encoded, 11),
            EncodedTriple.FromEncoded(11, FirstId.Encoded, 101),
            EncodedTriple.FromEncoded(11, RestId.Encoded, 10)
        ]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        //Each cell visited once; the loop back to 10 is rejected by the
        //visited set and named as the cycle.
        Assert.AreEqual(RdfCollectionOutcome.CyclicChain, read.Outcome);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101) },
            (System.Collections.ICollection)read.Members);
    }

    /// <summary>A cell carrying two distinct <c>rdf:first</c> values reads the prefix before the cell and names the ambiguity.</summary>
    [TestMethod]
    public async Task ReadNamesTheAmbiguousCellOnDuplicateFirst()
    {
        //Malformed: the second cell asserts two distinct rdf:first values,
        //so its member is undetermined; the read stops before it.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, RestId.Encoded, 11),
            EncodedTriple.FromEncoded(11, FirstId.Encoded, 101),
            EncodedTriple.FromEncoded(11, FirstId.Encoded, 102),
            EncodedTriple.FromEncoded(11, RestId.Encoded, NilId.Encoded)
        ]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(RdfCollectionOutcome.AmbiguousCell, read.Outcome);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(100) },
            (System.Collections.ICollection)read.Members);
    }

    /// <summary>A cell carrying two distinct <c>rdf:rest</c> continuations reads through its determined member and names the ambiguity.</summary>
    [TestMethod]
    public async Task ReadNamesTheAmbiguousCellOnDuplicateRest()
    {
        //Malformed: the first cell asserts two distinct continuations; its
        //own member is determined, the continuation is not.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, RestId.Encoded, 11),
            EncodedTriple.FromEncoded(10, RestId.Encoded, 12),
            EncodedTriple.FromEncoded(11, FirstId.Encoded, 101),
            EncodedTriple.FromEncoded(11, RestId.Encoded, NilId.Encoded)
        ]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(RdfCollectionOutcome.AmbiguousCell, read.Outcome);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(100) },
            (System.Collections.ICollection)read.Members);
    }

    /// <summary>An identical repeated cell triple is one fact: the chain reads well-formed.</summary>
    [TestMethod]
    public async Task ReadAbsorbsIdenticalRepeatedCellTriples()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, FirstId.Encoded, 100),
            EncodedTriple.FromEncoded(10, RestId.Encoded, NilId.Encoded)
        ]);

        RdfCollectionRead read = await RdfCollection.ReadAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(RdfCollectionOutcome.WellFormed, read.Outcome);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(100) },
            (System.Collections.ICollection)read.Members);
    }
}
