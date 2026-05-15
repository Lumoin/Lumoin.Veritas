using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Rdf;

[TestClass]
internal sealed class RdfCollectionTests
{
    public TestContext TestContext { get; set; } = null!;

    //Fixed IDs used across all tests to represent rdf:first, rdf:rest, rdf:nil.
    //rdf:first and rdf:rest are properties (IRIs); rdf:nil is an IRI as well.
    //Typing them as IriId satisfies the RdfCollection API which takes IriId for
    //all three. For rdf:nil the head overload also accepts a TermId, so it
    //converts implicitly from IriId → TermId where needed.
    private static IriId FirstId { get; } = IriId.FromUnchecked(TermId.FromEncoded(1));
    private static IriId RestId { get; } = IriId.FromUnchecked(TermId.FromEncoded(2));
    private static IriId NilId { get; } = IriId.FromUnchecked(TermId.FromEncoded(3));

    [TestMethod]
    public async Task EnumerateReturnsEmptyForNilHead()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([]);

        List<TermId> members = [];
        await foreach(TermId member in RdfCollection.EnumerateAsync(
            NilId, FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            members.Add(member);
        }

        Assert.HasCount(0, members);
    }

    [TestMethod]
    public async Task EnumerateReturnsMembersInOrder()
    {
        //List head at 10, containing [100, 101, 102].
        //Cells are 10 -> 11 -> 12 -> nil.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId, 100),
            EncodedTriple.FromEncoded(10, RestId, 11),
            EncodedTriple.FromEncoded(11, FirstId, 101),
            EncodedTriple.FromEncoded(11, RestId, 12),
            EncodedTriple.FromEncoded(12, FirstId, 102),
            EncodedTriple.FromEncoded(12, RestId, NilId)
        ]);

        IReadOnlyList<TermId> members = await RdfCollection.ToListAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        CollectionAssert.AreEqual(
            new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101), TermId.FromEncoded(102) },
            (System.Collections.ICollection)members);
    }

    [TestMethod]
    public async Task EnumerateStopsAtCellWithoutRest()
    {
        //Malformed: second cell has first but no rest. We should still yield the first two.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId, 100),
            EncodedTriple.FromEncoded(10, RestId, 11),
            EncodedTriple.FromEncoded(11, FirstId, 101)
        ]);

        IReadOnlyList<TermId> members = await RdfCollection.ToListAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        CollectionAssert.AreEqual(
            new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101) },
            (System.Collections.ICollection)members);
    }

    [TestMethod]
    public async Task EnumerateHaltsOnCycleWithoutInfiniteLoop()
    {
        //Malformed cyclic list: 10.rest -> 11, 11.rest -> 10.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, FirstId, 100),
            EncodedTriple.FromEncoded(10, RestId, 11),
            EncodedTriple.FromEncoded(11, FirstId, 101),
            EncodedTriple.FromEncoded(11, RestId, 10)
        ]);

        IReadOnlyList<TermId> members = await RdfCollection.ToListAsync(
            TermId.FromEncoded(10), FirstId, RestId, NilId, store.AsMatchDelegate(), TestContext.CancellationToken).ConfigureAwait(false);

        //Each cell visited once; the second loop back to 10 is rejected by the visited set.
        CollectionAssert.AreEqual(
            new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101) },
            (System.Collections.ICollection)members);
    }
}
