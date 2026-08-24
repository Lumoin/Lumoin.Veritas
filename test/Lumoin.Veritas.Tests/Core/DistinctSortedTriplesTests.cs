using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Tests for <see cref="DistinctSortedTriples"/>. Cover construction
/// from empty input, sort and dedup invariants, idempotence on
/// already-sorted input, lexicographic ordering at the predicate and
/// object positions, and null-argument rejection.
/// </summary>
[TestClass]
internal sealed class DistinctSortedTriplesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyInputProducesEmptyWrapper()
    {
        DistinctSortedTriples wrapper = DistinctSortedTriples.Create([]);

        Assert.AreEqual(0, wrapper.Count);
        Assert.IsTrue(wrapper.IsEmpty);
        Assert.IsTrue(wrapper.AsSpan().IsEmpty);
    }

    [TestMethod]
    public void UnsortedInputIsSortedAfterCreate()
    {
        EncodedTriple a = EncodedTriple.FromEncoded(3, 1, 1);
        EncodedTriple b = EncodedTriple.FromEncoded(1, 1, 1);
        EncodedTriple c = EncodedTriple.FromEncoded(2, 1, 1);

        DistinctSortedTriples wrapper = DistinctSortedTriples.Create([a, b, c]);

        Assert.AreEqual(3, wrapper.Count);
        Assert.IsFalse(wrapper.IsEmpty);
        Assert.AreEqual(b, wrapper.AsSpan()[0]);
        Assert.AreEqual(c, wrapper.AsSpan()[1]);
        Assert.AreEqual(a, wrapper.AsSpan()[2]);
    }

    [TestMethod]
    public void DuplicateTriplesAreCollapsed()
    {
        EncodedTriple t = EncodedTriple.FromEncoded(7, 8, 9);

        DistinctSortedTriples wrapper = DistinctSortedTriples.Create([t, t, t, t]);

        Assert.AreEqual(1, wrapper.Count);
        Assert.AreEqual(t, wrapper.AsSpan()[0]);
    }

    [TestMethod]
    public void AlreadySortedInputRoundTripsUnchanged()
    {
        EncodedTriple a = EncodedTriple.FromEncoded(1, 1, 1);
        EncodedTriple b = EncodedTriple.FromEncoded(2, 1, 1);
        EncodedTriple c = EncodedTriple.FromEncoded(3, 1, 1);

        DistinctSortedTriples wrapper = DistinctSortedTriples.Create([a, b, c]);

        Assert.AreEqual(3, wrapper.Count);
        Assert.AreEqual(a, wrapper.AsSpan()[0]);
        Assert.AreEqual(b, wrapper.AsSpan()[1]);
        Assert.AreEqual(c, wrapper.AsSpan()[2]);
    }

    [TestMethod]
    public void OrderingTieBreaksByPredicateWhenSubjectsMatch()
    {
        //Same subject, different predicates: ordering must fall to
        //the predicate position. Object held constant.
        EncodedTriple highPredicate = EncodedTriple.FromEncoded(5, 9, 1);
        EncodedTriple lowPredicate = EncodedTriple.FromEncoded(5, 2, 1);
        EncodedTriple midPredicate = EncodedTriple.FromEncoded(5, 4, 1);

        DistinctSortedTriples wrapper = DistinctSortedTriples.Create([highPredicate, lowPredicate, midPredicate]);

        Assert.AreEqual(3, wrapper.Count);
        Assert.AreEqual(lowPredicate, wrapper.AsSpan()[0]);
        Assert.AreEqual(midPredicate, wrapper.AsSpan()[1]);
        Assert.AreEqual(highPredicate, wrapper.AsSpan()[2]);
    }

    [TestMethod]
    public void OrderingTieBreaksByObjectWhenSubjectAndPredicateMatch()
    {
        //Same subject and predicate, different objects: ordering
        //must fall to the object position.
        EncodedTriple highObject = EncodedTriple.FromEncoded(5, 5, 9);
        EncodedTriple lowObject = EncodedTriple.FromEncoded(5, 5, 2);
        EncodedTriple midObject = EncodedTriple.FromEncoded(5, 5, 4);

        DistinctSortedTriples wrapper = DistinctSortedTriples.Create([highObject, lowObject, midObject]);

        Assert.AreEqual(3, wrapper.Count);
        Assert.AreEqual(lowObject, wrapper.AsSpan()[0]);
        Assert.AreEqual(midObject, wrapper.AsSpan()[1]);
        Assert.AreEqual(highObject, wrapper.AsSpan()[2]);
    }

    [TestMethod]
    public void NullEnumerableThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DistinctSortedTriples.Create((IEnumerable<EncodedTriple>)null!));
    }
}
