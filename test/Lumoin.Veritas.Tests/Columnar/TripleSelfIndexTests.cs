using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The triple self-index's contract: every binding step — leader block,
/// backward (preceding) step, forward (following) step, and their chains
/// through all three rotations — yields ranges whose lengths equal naive
/// filtering of the triple list, and every seek returns exactly the smallest
/// qualifying symbol the filtered list contains. Dense and sparse identifier
/// fixtures, membership of present and absent triples, and the empty index.
/// </summary>
[TestClass]
internal sealed class TripleSelfIndexTests
{
    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    /// <summary>A triple's positions in a rotation's (leader, second, third) order.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <param name="triple">The triple.</param>
    /// <returns>The decomposed identifiers.</returns>
    private static (uint Leader, uint Second, uint Third) Decompose(SelfIndexRotation rotation, EncodedTriple triple) => rotation switch
    {
        SelfIndexRotation.SubjectPredicateObject => (triple.Subject.Encoded, triple.Predicate.Encoded, triple.Object.Encoded),
        SelfIndexRotation.ObjectSubjectPredicate => (triple.Object.Encoded, triple.Subject.Encoded, triple.Predicate.Encoded),
        _ => (triple.Predicate.Encoded, triple.Object.Encoded, triple.Subject.Encoded),
    };

    /// <summary>The triples whose rotation-decomposed leader matches.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="rotation">The rotation.</param>
    /// <param name="leader">The leader identifier.</param>
    /// <returns>The matching triples.</returns>
    private static List<EncodedTriple> WithLeader(List<EncodedTriple> triples, SelfIndexRotation rotation, uint leader)
    {
        List<EncodedTriple> matches = [];
        foreach(EncodedTriple triple in triples)
        {
            if(Decompose(rotation, triple).Leader == leader)
            {
                matches.Add(triple);
            }
        }

        return matches;
    }

    /// <summary>The smallest decomposed position value at or above the target among the triples, or none — the seek oracle.</summary>
    /// <param name="triples">The candidate triples.</param>
    /// <param name="rotation">The rotation.</param>
    /// <param name="position">Which decomposed position: 0 leader, 1 second, 2 third.</param>
    /// <param name="target">The sought lower bound.</param>
    /// <returns>The successor, or <see langword="null"/> when none exists.</returns>
    private static uint? SmallestAtLeast(List<EncodedTriple> triples, SelfIndexRotation rotation, int position, uint target)
    {
        uint? best = null;
        foreach(EncodedTriple triple in triples)
        {
            (uint leader, uint second, uint third) = Decompose(rotation, triple);
            uint value = position switch
            {
                0 => leader,
                1 => second,
                _ => third,
            };

            if(value >= target && (best is null || value < best.Value))
            {
                best = value;
            }
        }

        return best;
    }

    /// <summary>Asserts every binding step, chain, and seek against naive filtering, for every rotation.</summary>
    /// <param name="triples">The distinct triples.</param>
    private static void AssertMatchesOracle(List<EncodedTriple> triples)
    {
        TripleSelfIndex index = TripleSelfIndex.Build(triples);

        Assert.AreEqual(triples.Count, index.Count);

        foreach(SelfIndexRotation rotation in (ReadOnlySpan<SelfIndexRotation>)[SelfIndexRotation.SubjectPredicateObject, SelfIndexRotation.ObjectSubjectPredicate, SelfIndexRotation.PredicateObjectSubject])
        {
            //The distinct leaders plus absent near-misses drive the block,
            //forward, backward, and seek checks.
            HashSet<uint> leaders = [];
            HashSet<uint> seconds = [];
            HashSet<uint> thirds = [];
            foreach(EncodedTriple triple in triples)
            {
                (uint leader, uint second, uint third) = Decompose(rotation, triple);
                leaders.Add(leader);
                seconds.Add(second);
                thirds.Add(third);
            }

            uint absentLeader = 0;
            while(leaders.Contains(absentLeader))
            {
                absentLeader++;
            }

            foreach(uint leader in leaders)
            {
                List<EncodedTriple> block = WithLeader(triples, rotation, leader);
                SelfIndexRange blockRange = index.BindFirst(rotation, new TermId(leader));
                Assert.AreEqual(block.Count, blockRange.Length, $"BindFirst length disagreed for leader {leader} in {rotation}");

                //Forward: every second value present under the leader, plus a miss.
                HashSet<uint> blockSeconds = [];
                HashSet<uint> blockThirds = [];
                foreach(EncodedTriple triple in block)
                {
                    (_, uint second, uint third) = Decompose(rotation, triple);
                    blockSeconds.Add(second);
                    blockThirds.Add(third);
                }

                foreach(uint second in blockSeconds)
                {
                    int expected = 0;
                    foreach(EncodedTriple triple in block)
                    {
                        if(Decompose(rotation, triple).Second == second)
                        {
                            expected++;
                        }
                    }

                    SelfIndexRange narrowed = index.BindFollowing(blockRange, new TermId(leader), new TermId(second));
                    Assert.AreEqual(expected, narrowed.Length, $"BindFollowing length disagreed for ({leader}, {second}) in {rotation}");

                    //Chain to membership: bind the third backward on the
                    //forward-narrowed range; each present third yields 1.
                    foreach(uint third in blockThirds)
                    {
                        bool present = false;
                        foreach(EncodedTriple triple in block)
                        {
                            (_, uint s2, uint t3) = Decompose(rotation, triple);
                            if(s2 == second && t3 == third)
                            {
                                present = true;

                                break;
                            }
                        }

                        SelfIndexRange membership = index.BindPreceding(narrowed, new TermId(third));
                        Assert.AreEqual(present ? 1 : 0, membership.Length, $"Membership disagreed for ({leader}, {second}, {third}) in {rotation}");
                    }
                }

                //Backward from the block: each present third value, plus a miss.
                foreach(uint third in blockThirds)
                {
                    int expected = 0;
                    foreach(EncodedTriple triple in block)
                    {
                        if(Decompose(rotation, triple).Third == third)
                        {
                            expected++;
                        }
                    }

                    SelfIndexRange narrowed = index.BindPreceding(blockRange, new TermId(third));
                    Assert.AreEqual(expected, narrowed.Length, $"BindPreceding length disagreed for leader {leader}, third {third} in {rotation}");
                }

                //Seeks under the leader: targets around each present value.
                foreach(uint second in blockSeconds)
                {
                    foreach(uint target in (ReadOnlySpan<uint>)[second, second + 1])
                    {
                        uint? expected = SmallestAtLeast(block, rotation, 1, target);
                        bool found = index.TrySeekFollowing(blockRange, new TermId(leader), new TermId(target), out TermId actual);
                        Assert.AreEqual(expected is not null, found, $"TrySeekFollowing existence disagreed for leader {leader}, target {target} in {rotation}");
                        if(expected is not null)
                        {
                            Assert.AreEqual(expected.Value, actual.Encoded, $"TrySeekFollowing value disagreed for leader {leader}, target {target} in {rotation}");
                        }
                    }
                }

                foreach(uint third in blockThirds)
                {
                    foreach(uint target in (ReadOnlySpan<uint>)[third, third + 1])
                    {
                        uint? expected = SmallestAtLeast(block, rotation, 2, target);
                        bool found = index.TrySeekPreceding(blockRange, new TermId(target), out TermId actual);
                        Assert.AreEqual(expected is not null, found, $"TrySeekPreceding existence disagreed for leader {leader}, target {target} in {rotation}");
                        if(expected is not null)
                        {
                            Assert.AreEqual(expected.Value, actual.Encoded, $"TrySeekPreceding value disagreed for leader {leader}, target {target} in {rotation}");
                        }
                    }
                }
            }

            Assert.IsTrue(index.BindFirst(rotation, new TermId(absentLeader)).IsEmpty, $"An absent leader bound non-empty in {rotation}");

            //Leader seeks across the whole rotation: around every present
            //leader and past the maximum.
            uint maxLeader = 0;
            foreach(uint leader in leaders)
            {
                maxLeader = Math.Max(maxLeader, leader);
            }

            foreach(uint leader in leaders)
            {
                foreach(uint target in (ReadOnlySpan<uint>)[leader, leader + 1])
                {
                    uint? expected = SmallestAtLeast(triples, rotation, 0, target);
                    bool found = index.TrySeekFirst(rotation, new TermId(target), out TermId actual);
                    Assert.AreEqual(expected is not null, found, $"TrySeekFirst existence disagreed for target {target} in {rotation}");
                    if(expected is not null)
                    {
                        Assert.AreEqual(expected.Value, actual.Encoded, $"TrySeekFirst value disagreed for target {target} in {rotation}");
                    }
                }
            }

            Assert.IsFalse(index.TrySeekFirst(rotation, new TermId(maxLeader + 1), out _), $"TrySeekFirst past the maximum found a leader in {rotation}");

            //The pure backward chain through all three rotations: sampled
            //(third, second, leader) constraint accumulation down to membership.
            foreach(EncodedTriple triple in triples)
            {
                if((Mix((ulong)triple.Subject.Encoded * 31 + triple.Object.Encoded) % 19) != 0)
                {
                    continue;
                }

                (uint leader, uint second, uint third) = Decompose(rotation, triple);
                SelfIndexRange step1 = index.BindPreceding(index.FullRange(rotation), new TermId(third));
                int expected1 = 0;
                foreach(EncodedTriple candidate in triples)
                {
                    if(Decompose(rotation, candidate).Third == third)
                    {
                        expected1++;
                    }
                }

                Assert.AreEqual(expected1, step1.Length, $"Backward chain step 1 disagreed for third {third} in {rotation}");

                SelfIndexRange step2 = index.BindPreceding(step1, new TermId(second));
                int expected2 = 0;
                foreach(EncodedTriple candidate in triples)
                {
                    (_, uint s2, uint t3) = Decompose(rotation, candidate);
                    if(t3 == third && s2 == second)
                    {
                        expected2++;
                    }
                }

                Assert.AreEqual(expected2, step2.Length, $"Backward chain step 2 disagreed for (second {second}, third {third}) in {rotation}");

                SelfIndexRange step3 = index.BindPreceding(step2, new TermId(leader));
                Assert.AreEqual(1, step3.Length, $"Backward chain membership disagreed for ({leader}, {second}, {third}) in {rotation}");
            }
        }
    }

    /// <summary>Distinct triples with identifiers drawn below the per-position bounds.</summary>
    /// <param name="count">The target triple count.</param>
    /// <param name="subjectBound">The exclusive subject bound.</param>
    /// <param name="predicateBound">The exclusive predicate bound.</param>
    /// <param name="objectBound">The exclusive object bound.</param>
    /// <param name="seed">The mixer seed.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> Fixture(int count, uint subjectBound, uint predicateBound, uint objectBound, ulong seed)
    {
        HashSet<EncodedTriple> distinct = [];
        ulong state = seed;
        while(distinct.Count < count)
        {
            state = Mix(state);
            uint subject = 1 + (uint)(state % subjectBound);
            state = Mix(state);
            uint predicate = 1 + (uint)(state % predicateBound);
            state = Mix(state);
            uint @object = 1 + (uint)(state % objectBound);
            distinct.Add(new EncodedTriple(new TermId(subject), new TermId(predicate), new TermId(@object)));
        }

        return [.. distinct];
    }

    [TestMethod]
    public void DenseIdentifiersMatchOracle()
    {
        AssertMatchesOracle(Fixture(600, 25, 6, 40, 3));
    }

    [TestMethod]
    public void SparseIdentifiersMatchOracle()
    {
        AssertMatchesOracle(Fixture(150, 90_000, 50_000, 70_000, 7));
    }

    [TestMethod]
    public void StarAndChainShapedDataMatchOracle()
    {
        //A hub object shared by many subjects plus chains through it — heavy
        //duplicate pressure on single columns.
        List<EncodedTriple> triples = [];
        for(uint i = 1; i <= 80; i++)
        {
            triples.Add(new EncodedTriple(new TermId(i), new TermId(1), new TermId(500)));
            triples.Add(new EncodedTriple(new TermId(500), new TermId(2), new TermId(1000 + i)));
        }

        AssertMatchesOracle(triples);
    }

    [TestMethod]
    public void EmptyIndexBindsEmptyAndSeeksNothing()
    {
        TripleSelfIndex index = TripleSelfIndex.Build([]);

        Assert.AreEqual(0, index.Count);
        foreach(SelfIndexRotation rotation in (ReadOnlySpan<SelfIndexRotation>)[SelfIndexRotation.SubjectPredicateObject, SelfIndexRotation.ObjectSubjectPredicate, SelfIndexRotation.PredicateObjectSubject])
        {
            Assert.IsTrue(index.FullRange(rotation).IsEmpty);
            Assert.IsTrue(index.BindFirst(rotation, new TermId(1)).IsEmpty);
            Assert.IsTrue(index.BindPreceding(index.FullRange(rotation), new TermId(1)).IsEmpty);
            Assert.IsFalse(index.TrySeekFirst(rotation, new TermId(0), out _));
            Assert.IsFalse(index.TrySeekPreceding(index.FullRange(rotation), new TermId(0), out _));
            Assert.IsFalse(index.TrySeekFollowing(index.FullRange(rotation), new TermId(1), new TermId(0), out _));
        }
    }

    [TestMethod]
    public void DuplicateInputTriplesCollapse()
    {
        EncodedTriple triple = new(new TermId(3), new TermId(4), new TermId(5));
        TripleSelfIndex index = TripleSelfIndex.Build([triple, triple, triple]);

        Assert.AreEqual(1, index.Count);
        Assert.AreEqual(1, index.BindFirst(SelfIndexRotation.SubjectPredicateObject, new TermId(3)).Length);
    }

    [TestMethod]
    public void BindFollowingRejectsANonBlockRange()
    {
        TripleSelfIndex index = TripleSelfIndex.Build(
        [
            new EncodedTriple(new TermId(1), new TermId(2), new TermId(3)),
            new EncodedTriple(new TermId(1), new TermId(2), new TermId(4)),
            new EncodedTriple(new TermId(5), new TermId(2), new TermId(3)),
        ]);

        SelfIndexRange nonBlock = new(SelfIndexRotation.SubjectPredicateObject, 0, 3);
        Assert.ThrowsExactly<ArgumentException>(() => index.BindFollowing(nonBlock, new TermId(1), new TermId(2)));
    }
}
