using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class TriejoinIteratorTests
{
    public TestContext TestContext { get; set; } = null!;

    //A small graph with deliberate sharing across positions, used
    //by most tests so the iterator's descent is exercised on
    //real-shaped data.
    private static EncodedTriple[] SampleTriples { get; } =
    [
        EncodedTriple.FromEncoded(1, 10, 100),
        EncodedTriple.FromEncoded(1, 10, 101),
        EncodedTriple.FromEncoded(1, 11, 100),
        EncodedTriple.FromEncoded(2, 10, 100),
        EncodedTriple.FromEncoded(2, 11, 200),
        EncodedTriple.FromEncoded(3, 12, 300),
    ];

    [TestMethod]
    public async Task IteratorAcquiresSnapshotOnConstruction()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);
        TriplePattern pattern = AllVariables(s, p, o);

        int countBefore = store.Snapshot.RefCount;

        using TriejoinIterator iterator = new(store.Snapshot, pattern, [s, p, o], VeritasClock.System);

        Assert.AreEqual(countBefore + 1, store.Snapshot.RefCount);
    }

    [TestMethod]
    public async Task DisposeReleasesSnapshotReference()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);
        TriplePattern pattern = AllVariables(s, p, o);

        int countBefore = store.Snapshot.RefCount;

        TriejoinIterator iterator = new(store.Snapshot, pattern, [s, p, o], VeritasClock.System);
        iterator.Dispose();

        Assert.AreEqual(countBefore, store.Snapshot.RefCount);
    }

    [TestMethod]
    public async Task DoubleDisposeReleasesOnce()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        int countBefore = store.Snapshot.RefCount;

        TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);
        iterator.Dispose();
        iterator.Dispose();

        Assert.AreEqual(countBefore, store.Snapshot.RefCount);
    }

    [TestMethod]
    public async Task SelfJoinPatternIsRejected()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable x = new(0);
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(10)),
            PatternPosition.OfVariable(x));

        Assert.Throws<ArgumentException>(() => new TriejoinIterator(store.Snapshot, pattern, [x], VeritasClock.System));
    }

    [TestMethod]
    public async Task VariableOrderMissingVariableIsRejected()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);
        TriplePattern pattern = AllVariables(s, p, o);

        //Variable order missing 'o' — must reject.
        Assert.Throws<ArgumentException>(() => new TriejoinIterator(store.Snapshot, pattern, [s, p], VeritasClock.System));
    }

    [TestMethod]
    public async Task VariableOrderWithDuplicateIsRejected()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);
        TriplePattern pattern = AllVariables(s, p, o);

        Assert.Throws<ArgumentException>(() => new TriejoinIterator(store.Snapshot, pattern, [s, p, p], VeritasClock.System));
    }

    [TestMethod]
    public async Task VariableOrderWithExtraVariableIsRejected()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);
        Variable extra = new(99);
        TriplePattern pattern = AllVariables(s, p, o);

        Assert.Throws<ArgumentException>(() => new TriejoinIterator(store.Snapshot, pattern, [s, p, o, extra], VeritasClock.System));
    }

    [TestMethod]
    public async Task AllUnboundIteratorVisitsEverySubject()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        List<TermId> subjects = [];

        while(!iterator.AtEnd)
        {
            subjects.Add(iterator.Key);
            iterator.Next(TestContext.CancellationToken);
        }

        Assert.HasCount(3, subjects);
        Assert.AreSequenceEqual(new TermId[] { TermId.FromEncoded(1), TermId.FromEncoded(2), TermId.FromEncoded(3) }, subjects);
    }

    [TestMethod]
    public async Task OpenSubjectThenIterateOverPredicates()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        Assert.IsTrue(iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken));

        List<TermId> predicates = [];

        while(!iterator.AtEnd)
        {
            predicates.Add(iterator.Key);
            iterator.Next(TestContext.CancellationToken);
        }

        Assert.HasCount(2, predicates);
        Assert.AreSequenceEqual(new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) }, predicates);
    }

    [TestMethod]
    public async Task FullDescentEnumeratesAllTriples()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        HashSet<EncodedTriple> visited = [];
        CancellationToken ct = TestContext.CancellationToken;

        while(!iterator.AtEnd)
        {
            TermId subject = iterator.Key;

            if(!iterator.Open(subject, ct))
            {
                iterator.Next(ct);

                continue;
            }

            while(!iterator.AtEnd)
            {
                TermId predicate = iterator.Key;

                if(!iterator.Open(predicate, ct))
                {
                    iterator.Next(ct);

                    continue;
                }

                while(!iterator.AtEnd)
                {
                    TermId obj = iterator.Key;

                    if(iterator.Open(obj, ct))
                    {
                        visited.Add(EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, obj.Encoded));
                        iterator.Up();
                    }

                    iterator.Next(ct);
                }

                iterator.Up();
                iterator.Next(ct);
            }

            iterator.Up();
            iterator.Next(ct);
        }

        HashSet<EncodedTriple> expected = [.. SampleTriples];
        Assert.IsTrue(expected.SetEquals(visited), $"Expected {expected.Count} triples, got {visited.Count}.");
    }

    [TestMethod]
    public async Task OpenWithMissingValueReturnsFalseAndKeepsState()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        bool opened = iterator.Open(TermId.FromEncoded(999U), TestContext.CancellationToken);

        Assert.IsFalse(opened);
        //State unchanged: cursor still at first key.
        Assert.IsFalse(iterator.AtEnd);
        Assert.AreEqual(0, iterator.DescendedLevels);
    }

    [TestMethod]
    public async Task UpRewindsOneVariableLevel()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken);

        Assert.AreEqual(1, iterator.DescendedLevels);

        iterator.Up();

        Assert.AreEqual(0, iterator.DescendedLevels);
        Assert.AreEqual(TermId.FromEncoded(1U), iterator.Key);
    }

    [TestMethod]
    public async Task UpFromZeroLevelsThrows()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        Assert.Throws<InvalidOperationException>(() => iterator.Up());
    }

    [TestMethod]
    public async Task SeekAdvancesToFirstKeyAtOrAboveTarget()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Seek(TermId.FromEncoded(2U), TestContext.CancellationToken);

        Assert.IsFalse(iterator.AtEnd);
        Assert.AreEqual(TermId.FromEncoded(2U), iterator.Key);
    }

    [TestMethod]
    public async Task SeekToValueGreaterThanAllKeysReachesAtEnd()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Seek(TermId.FromEncoded(999U), TestContext.CancellationToken);

        Assert.IsTrue(iterator.AtEnd);
    }

    [TestMethod]
    public async Task BoundSubjectPatternConstrainsRoot()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable p = new(1);
        Variable o = new(2);
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.OfVariable(p),
            PatternPosition.OfVariable(o));

        using TriejoinIterator iterator = new(store.Snapshot, pattern, [p, o], VeritasClock.System);

        //First level is now ?p, ranged over predicates for subject=1.
        Assert.AreEqual(p, iterator.CurrentVariable);
        Assert.IsFalse(iterator.AtEnd);
        Assert.AreEqual(TermId.FromEncoded(10U), iterator.Key);
    }

    [TestMethod]
    public async Task BoundPositionWithNoMatchPutsIteratorAtEnd()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable p = new(1);
        Variable o = new(2);
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(999)),
            PatternPosition.OfVariable(p),
            PatternPosition.OfVariable(o));

        using TriejoinIterator iterator = new(store.Snapshot, pattern, [p, o], VeritasClock.System);

        Assert.IsTrue(iterator.AtEnd);
    }

    [TestMethod]
    public async Task TwoBoundPositionsConstrainAccordingly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable o = new(2);
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(10)),
            PatternPosition.OfVariable(o));

        using TriejoinIterator iterator = new(store.Snapshot, pattern, [o], VeritasClock.System);

        List<TermId> objects = [];

        while(!iterator.AtEnd)
        {
            objects.Add(iterator.Key);
            iterator.Next(TestContext.CancellationToken);
        }

        //Triples (1,10,100) and (1,10,101) match.
        Assert.HasCount(2, objects);
        Assert.AreSequenceEqual(new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101) }, objects);
    }

    [TestMethod]
    public async Task ValueOfReturnsBoundValue()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken);
        iterator.Open(TermId.FromEncoded(10U), TestContext.CancellationToken);

        Assert.AreEqual(TermId.FromEncoded(1U), iterator.ValueOf(s));
        Assert.AreEqual(TermId.FromEncoded(10U), iterator.ValueOf(p));
    }

    [TestMethod]
    public async Task ValueOfThrowsForUnboundVariable()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken);

        Assert.Throws<ArgumentException>(() => iterator.ValueOf(p));
    }

    [TestMethod]
    public async Task CurrentVariableAtCurrentLevel()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        Assert.AreEqual(s, iterator.CurrentVariable);

        iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken);

        Assert.AreEqual(p, iterator.CurrentVariable);

        iterator.Open(TermId.FromEncoded(10U), TestContext.CancellationToken);

        Assert.AreEqual(o, iterator.CurrentVariable);
    }

    [TestMethod]
    public async Task CurrentVariableThrowsAfterAllVariablesBound()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken);
        iterator.Open(TermId.FromEncoded(10U), TestContext.CancellationToken);
        iterator.Open(TermId.FromEncoded(100U), TestContext.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => _ = iterator.CurrentVariable);
    }

    [TestMethod]
    public async Task OpenWithCustomVariableOrderUsesObjectFirst()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        //Variable order [o, p, s] — descend by object first.
        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [o, p, s], VeritasClock.System);

        Assert.AreEqual(o, iterator.CurrentVariable);

        //First-level keys are objects across the entire dataset:
        //{100, 101, 200, 300}.
        List<TermId> firstLevel = [];

        while(!iterator.AtEnd)
        {
            firstLevel.Add(iterator.Key);
            iterator.Next(TestContext.CancellationToken);
        }

        Assert.HasCount(4, firstLevel);
        Assert.AreSequenceEqual(new TermId[] { TermId.FromEncoded(100), TermId.FromEncoded(101), TermId.FromEncoded(200), TermId.FromEncoded(300) }, firstLevel);
    }

    [TestMethod]
    public async Task OpenAllVariablesProducesValidLeafLevel()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        Assert.IsTrue(iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken));
        Assert.IsTrue(iterator.Open(TermId.FromEncoded(10U), TestContext.CancellationToken));
        Assert.IsTrue(iterator.Open(TermId.FromEncoded(100U), TestContext.CancellationToken));

        //All three variables bound. Iterator can rewind correctly.
        iterator.Up();

        Assert.AreEqual(o, iterator.CurrentVariable);
    }

    [TestMethod]
    public async Task OpenLeafThenUpRestoresPriorLevelCursor()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        iterator.Open(TermId.FromEncoded(1U), TestContext.CancellationToken);
        iterator.Open(TermId.FromEncoded(10U), TestContext.CancellationToken);

        TermId objectBefore = iterator.Key;

        iterator.Open(objectBefore, TestContext.CancellationToken);
        iterator.Up();

        Assert.AreEqual(objectBefore, iterator.Key);
    }

    [TestMethod]
    public async Task EmptyGraphIteratorIsImmediatelyAtEnd()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Variable s = new(0);
        Variable p = new(1);
        Variable o = new(2);

        using TriejoinIterator iterator = new(store.Snapshot, AllVariables(s, p, o), [s, p, o], VeritasClock.System);

        Assert.IsTrue(iterator.AtEnd);
    }

    private static TriplePattern AllVariables(Variable s, Variable p, Variable o)
    {
        return new(
            PatternPosition.OfVariable(s),
            PatternPosition.OfVariable(p),
            PatternPosition.OfVariable(o));
    }
}
