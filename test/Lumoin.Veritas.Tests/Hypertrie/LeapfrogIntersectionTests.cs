using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class LeapfrogIntersectionTests
{
    public TestContext TestContext { get; set; } = null!;

    //Synthetic predicate and object the test graphs share. Using
    //fixed values keeps the iterator's first level (subject) the
    //one varying with the key sequence under test.
    private const uint SharedPredicate = 1000;

    private const uint SharedObject = 2000;

    [TestMethod]
    public async Task SingleParticipantReturnsItsCurrentKey()
    {
        uint[] keys = [10, 20, 30];

        StoreScope scope = await StoreScope.CreateAsync(keys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator iterator = scope.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [iterator],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsTrue(found);
        Assert.AreEqual(TermId.FromEncoded(10U), commonKey);
    }

    [TestMethod]
    public async Task TwoParticipantsWithIdenticalSequencesAgreeOnFirstKey()
    {
        uint[] keys = [5, 10, 15];

        StoreScope left = await StoreScope.CreateAsync(keys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope right = await StoreScope.CreateAsync(keys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator a = left.OpenSubjectIterator();
        using TriejoinIterator b = right.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [a, b],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsTrue(found);
        Assert.AreEqual(TermId.FromEncoded(5U), commonKey);
    }

    [TestMethod]
    public async Task TwoParticipantsWithDisjointSequencesReturnFalse()
    {
        uint[] leftKeys = [1, 3, 5];
        uint[] rightKeys = [2, 4, 6];

        StoreScope left = await StoreScope.CreateAsync(leftKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope right = await StoreScope.CreateAsync(rightKeys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator a = left.OpenSubjectIterator();
        using TriejoinIterator b = right.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [a, b],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsFalse(found);
        Assert.AreEqual(TermId.None, commonKey);
    }

    [TestMethod]
    public async Task TwoParticipantsAdvanceToFirstSharedKey()
    {
        uint[] leftKeys = [1, 4, 7, 10];
        uint[] rightKeys = [2, 4, 8, 10];

        StoreScope left = await StoreScope.CreateAsync(leftKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope right = await StoreScope.CreateAsync(rightKeys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator a = left.OpenSubjectIterator();
        using TriejoinIterator b = right.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [a, b],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsTrue(found);
        Assert.AreEqual(TermId.FromEncoded(4U), commonKey);
    }

    [TestMethod]
    public async Task RepeatedCallsEnumerateEntireIntersection()
    {
        //Drive the algorithm in a loop, pulling out every common
        //key and advancing past it. This is the shape the WCOJ
        //driver will use.
        uint[] leftKeys = [1, 4, 7, 10, 13];
        uint[] rightKeys = [2, 4, 8, 10, 14];

        StoreScope left = await StoreScope.CreateAsync(leftKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope right = await StoreScope.CreateAsync(rightKeys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator a = left.OpenSubjectIterator();
        using TriejoinIterator b = right.OpenSubjectIterator();

        List<TermId> intersection = [];

        while(LeapfrogIntersection.TryFindNextCommonKey([a, b], out TermId commonKey, TestContext.CancellationToken))
        {
            intersection.Add(commonKey);

            //Advance every participant past the agreed key so the
            //next call finds the next intersection element.
            a.Next(TestContext.CancellationToken);
            b.Next(TestContext.CancellationToken);
        }

        Assert.AreSequenceEqual(new TermId[] { TermId.FromEncoded(4), TermId.FromEncoded(10) }, intersection);
    }

    [TestMethod]
    public async Task ThreeParticipantsAgreeOnlyOnCommonElement()
    {
        uint[] aKeys = [1, 5, 10, 15];
        uint[] bKeys = [3, 5, 10, 20];
        uint[] cKeys = [5, 7, 10, 25];

        StoreScope storeA = await StoreScope.CreateAsync(aKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope storeB = await StoreScope.CreateAsync(bKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope storeC = await StoreScope.CreateAsync(cKeys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator iterA = storeA.OpenSubjectIterator();
        using TriejoinIterator iterB = storeB.OpenSubjectIterator();
        using TriejoinIterator iterC = storeC.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [iterA, iterB, iterC],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsTrue(found);
        Assert.AreEqual(TermId.FromEncoded(5U), commonKey);
    }

    [TestMethod]
    public async Task ThreeParticipantsWithEmptyIntersectionReturnFalse()
    {
        uint[] aKeys = [1, 2, 3];
        uint[] bKeys = [4, 5, 6];
        uint[] cKeys = [7, 8, 9];

        StoreScope storeA = await StoreScope.CreateAsync(aKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope storeB = await StoreScope.CreateAsync(bKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope storeC = await StoreScope.CreateAsync(cKeys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator iterA = storeA.OpenSubjectIterator();
        using TriejoinIterator iterB = storeB.OpenSubjectIterator();
        using TriejoinIterator iterC = storeC.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [iterA, iterB, iterC],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsFalse(found);
        Assert.AreEqual(TermId.None, commonKey);
    }

    [TestMethod]
    public async Task OvershootCausesRestartAndStillConverges()
    {
        //Designed to force a restart on the track-max loop. A
        //starts at 1, B at 100, so target = 100. Seek(100) on
        //A's sorted subject set [1, 50, 200, 500] lands on 200 —
        //overshoot. The algorithm promotes target to 200 and
        //rescans; B (at 100) seeks to 200 and lands exactly on
        //it (B contains 200). Common key = 200.
        uint[] aKeys = [1, 50, 200, 500];
        uint[] bKeys = [100, 200, 500];

        StoreScope storeA = await StoreScope.CreateAsync(aKeys, TestContext.CancellationToken).ConfigureAwait(false);
        StoreScope storeB = await StoreScope.CreateAsync(bKeys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator iterA = storeA.OpenSubjectIterator();
        using TriejoinIterator iterB = storeB.OpenSubjectIterator();

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [iterA, iterB],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsTrue(found);
        Assert.AreEqual(TermId.FromEncoded(200U), commonKey);
    }

    [TestMethod]
    public async Task OneParticipantStartingAtEndReturnsFalseImmediately()
    {
        //Construct an iterator over an empty graph — it is
        //immediately AtEnd.
        StoreScope storeA = await StoreScope.CreateAsync([], TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator iterA = storeA.OpenSubjectIterator();

        Assert.IsTrue(iterA.AtEnd);

        bool found = LeapfrogIntersection.TryFindNextCommonKey(
            [iterA],
            out TermId commonKey,
            TestContext.CancellationToken);

        Assert.IsFalse(found);
        Assert.AreEqual(TermId.None, commonKey);
    }

    [TestMethod]
    public void EmptyParticipantListIsRejected()
    {
        Assert.Throws<ArgumentException>(() => LeapfrogIntersection.TryFindNextCommonKey(
            [],
            out _,
            TestContext.CancellationToken));
    }

    [TestMethod]
    public void NullParticipantListIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => LeapfrogIntersection.TryFindNextCommonKey(
            null!,
            out _,
            TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task HonoursCancellationToken()
    {
        uint[] keys = [1, 2, 3];

        StoreScope store = await StoreScope.CreateAsync(keys, TestContext.CancellationToken).ConfigureAwait(false);
        using TriejoinIterator iterator = store.OpenSubjectIterator();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(false);

        Assert.Throws<OperationCanceledException>(() => LeapfrogIntersection.TryFindNextCommonKey(
            [iterator],
            out _,
            cts.Token));
    }

    //Owns a HypertrieGraphStore whose subject-position values are
    //exactly the supplied keys, paired with a fixed predicate and
    //object. The store is not IDisposable in the current design
    //(it holds its snapshot for its lifetime); callers do not need
    //to dispose this scope.
    private sealed class StoreScope
    {
        private HypertrieGraphStore Store { get; }

        private StoreScope(HypertrieGraphStore store)
        {
            Store = store;
        }

        //Async factory because building the underlying store
        //serialises against the node store's mutation gate, which
        //is async-only.
        public static async ValueTask<StoreScope> CreateAsync(uint[] subjects, CancellationToken cancellationToken)
        {
            EncodedTriple[] triples = new EncodedTriple[subjects.Length];

            for(int i = 0; i < subjects.Length; i++)
            {
                triples[i] = EncodedTriple.FromEncoded(subjects[i], SharedPredicate, SharedObject);
            }

            HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
            return new StoreScope(store);
        }

        //Returns an iterator over a pattern (?x SharedPredicate
        //SharedObject), so the iterator's first level enumerates
        //the supplied subject sequence in ascending order.
        public TriejoinIterator OpenSubjectIterator()
        {
            Variable x = new(0);
            TriplePattern pattern = new(
                PatternPosition.OfVariable(x),
                PatternPosition.Bound(TermId.FromEncoded(SharedPredicate)),
                PatternPosition.Bound(TermId.FromEncoded(SharedObject)));

            return new TriejoinIterator(Store.Snapshot, pattern, [x], VeritasClock.System);
        }
    }
}
