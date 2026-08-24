using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The self-index triejoin iterator's contract: a worst-case-optimal
/// intersection driven over the <c>Open</c>/<c>Up</c>/<c>Next</c>/<c>Seek</c>
/// surface enumerates exactly the naive join of the patterns — for the
/// triangle (the cyclic shape three CSR rotations cannot co-serve), chains and
/// stars under several global variable orders, and every bound-position
/// combination of a single pattern under every variable order; plus the
/// stateful cursor contract (failed <c>Open</c> leaves state unchanged,
/// <c>Up</c> restores the cursor, <c>Seek</c> never moves backwards).
/// </summary>
[TestClass]
internal sealed class SelfIndexTriejoinIteratorTests
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

    /// <summary>Runs the worst-case-optimal join over self-index iterators: per level a track-max agreement via <c>Seek</c>, an <c>Open</c> descent on agreement, and an <c>Up</c>-and-advance on exhaustion — iteratively, no recursion.</summary>
    /// <param name="index">The self-index.</param>
    /// <param name="patterns">The patterns.</param>
    /// <param name="globalOrder">The global variable order; every variable must appear in some pattern.</param>
    /// <returns>The solutions as value tuples in global-order positions.</returns>
    private static List<uint[]> LeapfrogJoin(TripleSelfIndex index, TriplePattern[] patterns, Variable[] globalOrder)
    {
        SelfIndexTriejoinIterator[] iterators = new SelfIndexTriejoinIterator[patterns.Length];
        for(int i = 0; i < patterns.Length; i++)
        {
            List<Variable> restriction = [];
            foreach(Variable variable in globalOrder)
            {
                foreach(Variable patternVariable in patterns[i].Variables())
                {
                    if(patternVariable == variable)
                    {
                        restriction.Add(variable);

                        break;
                    }
                }
            }

            iterators[i] = new SelfIndexTriejoinIterator(index, patterns[i], restriction);
        }

        List<SelfIndexTriejoinIterator>[] participants = new List<SelfIndexTriejoinIterator>[globalOrder.Length];
        for(int level = 0; level < globalOrder.Length; level++)
        {
            participants[level] = [];
            foreach(SelfIndexTriejoinIterator iterator in iterators)
            {
                foreach(Variable variable in iterator.VariableOrder)
                {
                    if(variable == globalOrder[level])
                    {
                        participants[level].Add(iterator);

                        break;
                    }
                }
            }

            Assert.IsNotEmpty(participants[level], $"No pattern carries global variable {globalOrder[level].Id}");
        }

        List<uint[]> results = [];
        uint[] bindings = new uint[globalOrder.Length];
        int currentLevel = 0;
        bool entering = true;
        while(currentLevel >= 0)
        {
            List<SelfIndexTriejoinIterator> active = participants[currentLevel];
            bool found;
            uint key = 0;
            if(entering)
            {
                foreach(SelfIndexTriejoinIterator iterator in active)
                {
                    iterator.RestartCurrentLevel();
                }

                found = TryAgree(active, 0, out key);
            }
            else
            {
                found = bindings[currentLevel] != uint.MaxValue && TryAgree(active, bindings[currentLevel] + 1, out key);
            }

            if(!found)
            {
                currentLevel--;
                if(currentLevel >= 0)
                {
                    foreach(SelfIndexTriejoinIterator iterator in participants[currentLevel])
                    {
                        iterator.Up();
                    }

                    entering = false;
                }

                continue;
            }

            bindings[currentLevel] = key;
            foreach(SelfIndexTriejoinIterator iterator in active)
            {
                Assert.IsTrue(iterator.Open(TermId.FromEncoded(key)), $"Open declined an agreed key {key}");
            }

            if(currentLevel == globalOrder.Length - 1)
            {
                results.Add((uint[])bindings.Clone());
                foreach(SelfIndexTriejoinIterator iterator in active)
                {
                    iterator.Up();
                }

                entering = false;

                continue;
            }

            currentLevel++;
            entering = true;
        }

        return results;
    }

    /// <summary>The track-max agreement loop: lower-bounds every participant, then raises stragglers to the running maximum until all agree or one ends.</summary>
    /// <param name="active">The participants at the level.</param>
    /// <param name="lowerBound">The starting lower bound.</param>
    /// <param name="key">Receives the agreed key.</param>
    /// <returns><see langword="true"/> when all participants agree on a key.</returns>
    private static bool TryAgree(List<SelfIndexTriejoinIterator> active, uint lowerBound, out uint key)
    {
        key = 0;
        uint maxKey = 0;
        foreach(SelfIndexTriejoinIterator iterator in active)
        {
            iterator.Seek(TermId.FromEncoded(lowerBound));
            if(iterator.AtEnd)
            {
                return false;
            }

            maxKey = Math.Max(maxKey, iterator.Key.Encoded);
        }

        while(true)
        {
            bool stable = true;
            foreach(SelfIndexTriejoinIterator iterator in active)
            {
                if(iterator.Key.Encoded < maxKey)
                {
                    iterator.Seek(TermId.FromEncoded(maxKey));
                    if(iterator.AtEnd)
                    {
                        return false;
                    }

                    if(iterator.Key.Encoded > maxKey)
                    {
                        maxKey = iterator.Key.Encoded;
                        stable = false;
                    }
                }
            }

            if(stable)
            {
                key = maxKey;

                return true;
            }
        }
    }

    /// <summary>The naive join oracle: extends binding environments pattern by pattern over the full triple list, then projects to the global order and deduplicates.</summary>
    /// <param name="corpus">The triples.</param>
    /// <param name="patterns">The patterns.</param>
    /// <param name="globalOrder">The global variable order.</param>
    /// <returns>The distinct solutions in global-order positions.</returns>
    private static List<uint[]> NaiveJoin(List<EncodedTriple> corpus, TriplePattern[] patterns, Variable[] globalOrder)
    {
        List<Dictionary<int, uint>> environments = [[]];
        foreach(TriplePattern pattern in patterns)
        {
            List<Dictionary<int, uint>> extended = [];
            foreach(Dictionary<int, uint> environment in environments)
            {
                foreach(EncodedTriple triple in corpus)
                {
                    if(TryMatch(pattern, triple, environment, out Dictionary<int, uint>? grown))
                    {
                        extended.Add(grown);
                    }
                }
            }

            environments = extended;
        }

        HashSet<string> seen = [];
        List<uint[]> results = [];
        foreach(Dictionary<int, uint> environment in environments)
        {
            uint[] tuple = new uint[globalOrder.Length];
            for(int level = 0; level < globalOrder.Length; level++)
            {
                tuple[level] = environment[globalOrder[level].Id];
            }

            if(seen.Add(string.Join(',', tuple)))
            {
                results.Add(tuple);
            }
        }

        return results;
    }

    /// <summary>Matches one triple against a pattern under an environment, growing the environment on success.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="triple">The triple.</param>
    /// <param name="environment">The bindings so far.</param>
    /// <param name="grown">Receives the extended environment on success.</param>
    /// <returns><see langword="true"/> when the triple matches.</returns>
    private static bool TryMatch(TriplePattern pattern, EncodedTriple triple, Dictionary<int, uint> environment, out Dictionary<int, uint> grown)
    {
        grown = new Dictionary<int, uint>(environment);
        for(int position = 0; position < 3; position++)
        {
            PatternPosition slot = pattern.At(position);
            uint value = position switch
            {
                0 => triple.Subject.Encoded,
                1 => triple.Predicate.Encoded,
                _ => triple.Object.Encoded,
            };

            if(slot.IsBound)
            {
                if(slot.BoundTerm.Encoded != value)
                {
                    return false;
                }

                continue;
            }

            if(grown.TryGetValue(slot.Variable.Id, out uint bound))
            {
                if(bound != value)
                {
                    return false;
                }

                continue;
            }

            grown[slot.Variable.Id] = value;
        }

        return true;
    }

    /// <summary>Sorts solution tuples lexicographically and asserts both sides are identical.</summary>
    /// <param name="expected">The oracle's solutions.</param>
    /// <param name="actual">The driver's solutions.</param>
    private static void AssertSameSolutions(List<uint[]> expected, List<uint[]> actual)
    {
        Comparison<uint[]> byLex = (left, right) =>
        {
            for(int i = 0; i < left.Length; i++)
            {
                int cell = left[i].CompareTo(right[i]);
                if(cell != 0)
                {
                    return cell;
                }
            }

            return 0;
        };

        expected.Sort(byLex);
        actual.Sort(byLex);

        Assert.HasCount(expected.Count, actual, "Solution counts differ");
        for(int i = 0; i < expected.Count; i++)
        {
            Assert.AreSequenceEqual(expected[i], actual[i], $"Solution {i} differs");
        }
    }

    /// <summary>An edge corpus over one predicate with guaranteed triangles plus mixed noise edges.</summary>
    /// <param name="seed">The mixer seed.</param>
    /// <returns>The corpus.</returns>
    private static List<EncodedTriple> EdgeCorpus(ulong seed)
    {
        HashSet<EncodedTriple> corpus = [];
        for(uint i = 0; i < 10; i++)
        {
            uint a = 1 + (i * 3);
            corpus.Add(EncodedTriple.FromEncoded(a, 7, a + 1));
            corpus.Add(EncodedTriple.FromEncoded(a + 1, 7, a + 2));
            corpus.Add(EncodedTriple.FromEncoded(a + 2, 7, a));
        }

        ulong state = seed;
        for(int i = 0; i < 120; i++)
        {
            state = Mix(state);
            uint from = 1 + (uint)(state % 40);
            state = Mix(state);
            uint to = 1 + (uint)(state % 40);
            corpus.Add(EncodedTriple.FromEncoded(from, 7, to));
        }

        return [.. corpus];
    }

    /// <summary>A multi-predicate corpus for star, chain, and single-pattern checks.</summary>
    /// <param name="seed">The mixer seed.</param>
    /// <returns>The corpus.</returns>
    private static List<EncodedTriple> MixedCorpus(ulong seed)
    {
        HashSet<EncodedTriple> corpus = [];
        ulong state = seed;
        for(int i = 0; i < 300; i++)
        {
            state = Mix(state);
            uint subject = 1 + (uint)(state % 30);
            state = Mix(state);
            uint predicate = 1 + (uint)(state % 4);
            state = Mix(state);
            uint @object = 1 + (uint)(state % 35);
            corpus.Add(EncodedTriple.FromEncoded(subject, predicate, @object));
        }

        return [.. corpus];
    }

    [TestMethod]
    public void TriangleJoinMatchesNaive()
    {
        List<EncodedTriple> corpus = EdgeCorpus(11);
        TripleSelfIndex index = TripleSelfIndex.Build(corpus);

        Variable a = new(1);
        Variable b = new(2);
        Variable c = new(3);
        TriplePattern[] patterns =
        [
            new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(new TermId(7)), PatternPosition.OfVariable(b)),
            new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(new TermId(7)), PatternPosition.OfVariable(c)),
            new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.Bound(new TermId(7)), PatternPosition.OfVariable(a)),
        ];

        List<uint[]> expected = NaiveJoin(corpus, patterns, [a, b, c]);
        List<uint[]> actual = LeapfrogJoin(index, patterns, [a, b, c]);

        Assert.IsGreaterThanOrEqualTo(30, expected.Count, "The fixture must contain triangles");
        AssertSameSolutions(expected, actual);
    }

    [TestMethod]
    public void ChainJoinMatchesNaiveUnderEveryGlobalOrder()
    {
        List<EncodedTriple> corpus = MixedCorpus(13);
        TripleSelfIndex index = TripleSelfIndex.Build(corpus);

        Variable x = new(1);
        Variable y = new(2);
        Variable z = new(3);
        TriplePattern[] patterns =
        [
            new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(new TermId(1)), PatternPosition.OfVariable(y)),
            new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(new TermId(2)), PatternPosition.OfVariable(z)),
        ];

        List<uint[]> expected = NaiveJoin(corpus, patterns, [x, y, z]);
        Assert.IsNotEmpty(expected, "The fixture must contain chains");

        foreach(Variable[] order in (Variable[][])[[x, y, z], [y, x, z], [y, z, x], [z, y, x]])
        {
            List<uint[]> reprojected = NaiveJoin(corpus, patterns, order);
            List<uint[]> actual = LeapfrogJoin(index, patterns, order);
            AssertSameSolutions(reprojected, actual);
        }
    }

    [TestMethod]
    public void StarJoinMatchesNaive()
    {
        List<EncodedTriple> corpus = MixedCorpus(17);
        TripleSelfIndex index = TripleSelfIndex.Build(corpus);

        Variable s = new(1);
        Variable x = new(2);
        Variable y = new(3);
        TriplePattern[] patterns =
        [
            new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(new TermId(1)), PatternPosition.OfVariable(x)),
            new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(new TermId(3)), PatternPosition.OfVariable(y)),
        ];

        foreach(Variable[] order in (Variable[][])[[s, x, y], [x, s, y], [x, y, s]])
        {
            List<uint[]> expected = NaiveJoin(corpus, patterns, order);
            List<uint[]> actual = LeapfrogJoin(index, patterns, order);
            Assert.IsNotEmpty(expected, "The fixture must contain star matches");
            AssertSameSolutions(expected, actual);
        }
    }

    [TestMethod]
    public void SinglePatternEveryBoundComboAndOrderMatchesNaive()
    {
        List<EncodedTriple> corpus = MixedCorpus(19);
        TripleSelfIndex index = TripleSelfIndex.Build(corpus);
        EncodedTriple present = corpus[0];

        Variable[] all = [new Variable(1), new Variable(2), new Variable(3)];
        for(int boundMask = 0; boundMask < 8; boundMask++)
        {
            PatternPosition[] slots = new PatternPosition[3];
            List<Variable> free = [];
            for(int position = 0; position < 3; position++)
            {
                if((boundMask & (1 << position)) != 0)
                {
                    TermId constant = position switch
                    {
                        0 => present.Subject,
                        1 => present.Predicate,
                        _ => present.Object,
                    };

                    slots[position] = PatternPosition.Bound(constant);
                }
                else
                {
                    slots[position] = PatternPosition.OfVariable(all[position]);
                    free.Add(all[position]);
                }
            }

            TriplePattern pattern = new(slots[0], slots[1], slots[2]);
            if(free.Count == 0)
            {
                SelfIndexTriejoinIterator boundIterator = new(index, pattern, []);
                Assert.IsFalse(boundIterator.AtEnd, "A fully bound present pattern must not start at end");

                continue;
            }

            foreach(Variable[] order in Permutations(free))
            {
                List<uint[]> expected = NaiveJoin(corpus, [pattern], order);
                List<uint[]> actual = LeapfrogJoin(index, [pattern], order);
                AssertSameSolutions(expected, actual);
            }
        }

        //A fully bound ABSENT pattern starts at end.
        TriplePattern absent = new(
            PatternPosition.Bound(present.Subject),
            PatternPosition.Bound(present.Predicate),
            PatternPosition.Bound(new TermId(uint.MaxValue - 5)));
        SelfIndexTriejoinIterator absentIterator = new(index, absent, []);
        Assert.IsTrue(absentIterator.AtEnd, "A fully bound absent pattern must start at end");
    }

    /// <summary>All permutations of up to three variables.</summary>
    /// <param name="variables">The variables.</param>
    /// <returns>The permutations.</returns>
    private static List<Variable[]> Permutations(List<Variable> variables)
    {
        List<Variable[]> all = [];
        if(variables.Count == 1)
        {
            all.Add([variables[0]]);

            return all;
        }

        if(variables.Count == 2)
        {
            all.Add([variables[0], variables[1]]);
            all.Add([variables[1], variables[0]]);

            return all;
        }

        foreach(int first in (ReadOnlySpan<int>)[0, 1, 2])
        {
            foreach(int second in (ReadOnlySpan<int>)[0, 1, 2])
            {
                if(second == first)
                {
                    continue;
                }

                int third = 3 - first - second;
                all.Add([variables[first], variables[second], variables[third]]);
            }
        }

        return all;
    }

    [TestMethod]
    public void CursorContractHoldsOnFailedOpenUpAndSeek()
    {
        List<EncodedTriple> corpus =
        [
            EncodedTriple.FromEncoded(1, 5, 10),
            EncodedTriple.FromEncoded(1, 5, 20),
            EncodedTriple.FromEncoded(2, 5, 30),
            EncodedTriple.FromEncoded(4, 5, 10),
        ];
        TripleSelfIndex index = TripleSelfIndex.Build(corpus);

        Variable s = new(1);
        Variable o = new(2);
        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(new TermId(5)),
            PatternPosition.OfVariable(o));
        SelfIndexTriejoinIterator iterator = new(index, pattern, [s, o]);

        //Level 0 enumerates subjects 1, 2, 4 ascending.
        Assert.IsFalse(iterator.AtEnd);
        Assert.AreEqual(1u, iterator.Key.Encoded);

        //A failed Open leaves the cursor and depth unchanged.
        Assert.IsFalse(iterator.Open(new TermId(3)));
        Assert.AreEqual(0, iterator.DescendedLevels);
        Assert.AreEqual(1u, iterator.Key.Encoded);

        //Seek never moves backwards; Next walks ascending.
        iterator.Seek(new TermId(2));
        Assert.AreEqual(2u, iterator.Key.Encoded);
        iterator.Seek(new TermId(1));
        Assert.AreEqual(2u, iterator.Key.Encoded);
        iterator.Next();
        Assert.AreEqual(4u, iterator.Key.Encoded);

        //Open descends; the child level rests on its first key; Up restores
        //the parent cursor exactly.
        Assert.IsTrue(iterator.Open(new TermId(4)));
        Assert.AreEqual(1, iterator.DescendedLevels);
        Assert.AreEqual(o, iterator.CurrentVariable);
        Assert.AreEqual(10u, iterator.Key.Encoded);
        Assert.AreEqual(4u, iterator.ValueOf(s).Encoded);
        iterator.Up();
        Assert.AreEqual(4u, iterator.Key.Encoded);

        //Restart re-enumerates the level from its first key.
        iterator.RestartCurrentLevel();
        Assert.AreEqual(1u, iterator.Key.Encoded);

        //Exhaustion at the level.
        iterator.Seek(new TermId(5));
        Assert.IsTrue(iterator.AtEnd);
    }
}
