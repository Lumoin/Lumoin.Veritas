using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Runs a basic graph pattern through the worst-case-optimal join over the
/// succinct triple self-index: one <see cref="SelfIndexTriejoinIterator"/> per
/// pattern on a single global variable order, intersected level by level with
/// the track-max agreement loop, emitting <see cref="SolutionBatch"/>es over
/// that order. Because the self-index serves every rotation from one
/// structure, ANY variable order — and so any join shape, the cyclic ones a
/// reduced rotation set cannot plan included — is evaluable; the global order
/// is simply first appearance across the patterns.
/// </summary>
/// <remarks>
/// <para>
/// The result is answer-identical to the other join engines, so the
/// conformance corpus is the oracle once a rendezvous routes here. A query
/// whose every position is ground, or one with a per-pattern self-join (the
/// same variable at two positions of one pattern, which the iterator
/// rejects), declines with <see langword="null"/> and stays on the engines
/// that carry those cases.
/// </para>
/// </remarks>
public static class SelfIndexPipeline
{
    /// <summary>
    /// Runs the query over the self-index, or returns <see langword="null"/>
    /// when the shape declines — a per-pattern self-join, or a query binding
    /// no variables at all.
    /// </summary>
    /// <param name="index">The self-index the patterns navigate.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The result batches over the first-appearance variable order, or <see langword="null"/> when the shape declines.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<SolutionBatch>? Run(TripleSelfIndex index, BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        if(!QueryEngineRendezvous.IsColumnarCapable(query))
        {
            return null;
        }

        List<Variable> globalOrder = [];
        HashSet<Variable> seen = [];
        foreach(TriplePattern pattern in query.Patterns)
        {
            foreach(Variable variable in pattern.Variables())
            {
                if(seen.Add(variable))
                {
                    globalOrder.Add(variable);
                }
            }
        }

        if(globalOrder.Count == 0)
        {
            return null;
        }

        //Each pattern descends its own variables in their global-order
        //restriction; a fully ground pattern has no levels and contributes
        //only its membership, decided at construction.
        bool groundPatternAbsent = false;
        SelfIndexTriejoinIterator[] iterators = new SelfIndexTriejoinIterator[query.Patterns.Count];
        for(int pattern = 0; pattern < query.Patterns.Count; pattern++)
        {
            List<Variable> restriction = [];
            foreach(Variable variable in globalOrder)
            {
                foreach(Variable patternVariable in query.Patterns[pattern].Variables())
                {
                    if(patternVariable == variable)
                    {
                        restriction.Add(variable);

                        break;
                    }
                }
            }

            iterators[pattern] = new SelfIndexTriejoinIterator(index, query.Patterns[pattern], restriction);

            if(restriction.Count == 0 && iterators[pattern].AtEnd)
            {
                groundPatternAbsent = true;
            }
        }

        List<SelfIndexTriejoinIterator>[] participants = new List<SelfIndexTriejoinIterator>[globalOrder.Count];
        for(int level = 0; level < globalOrder.Count; level++)
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
        }

        return Drive(participants, globalOrder, groundPatternAbsent);
    }

    /// <summary>
    /// The worst-case-optimal descent: per level a track-max agreement over
    /// the level's participants, an <c>Open</c> descent on agreement, an
    /// <c>Up</c>-and-advance on exhaustion — iteratively, emitting each full
    /// binding into the output batches.
    /// </summary>
    /// <param name="participants">Per global level, the iterators whose pattern binds that level's variable.</param>
    /// <param name="globalOrder">The global variable order — the batch schema.</param>
    /// <param name="groundPatternAbsent">Whether a fully ground pattern is absent from the index, emptying the result.</param>
    /// <returns>The result batches; full except the last.</returns>
    private static IEnumerable<SolutionBatch> Drive(
        List<SelfIndexTriejoinIterator>[] participants,
        List<Variable> globalOrder,
        bool groundPatternAbsent)
    {
        if(groundPatternAbsent)
        {
            yield break;
        }

        int levelCount = globalOrder.Count;
        int lastLevel = levelCount - 1;
        uint[] bindings = new uint[levelCount];
        SolutionBatch output = new(globalOrder);
        int rows = 0;

        int level = 0;
        bool entering = true;
        while(level >= 0)
        {
            List<SelfIndexTriejoinIterator> active = participants[level];
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
                found = bindings[level] != uint.MaxValue && TryAgree(active, bindings[level] + 1, out key);
            }

            if(!found)
            {
                level--;
                if(level >= 0)
                {
                    foreach(SelfIndexTriejoinIterator iterator in participants[level])
                    {
                        iterator.Up();
                    }

                    entering = false;
                }

                continue;
            }

            bindings[level] = key;
            if(level == lastLevel)
            {
                for(int column = 0; column < levelCount; column++)
                {
                    output.ColumnSpan(column)[rows] = bindings[column];
                }

                rows++;
                if(rows == SolutionBatch.BatchLength)
                {
                    output.SetCount(rows);

                    yield return output;

                    output = new SolutionBatch(globalOrder);
                    rows = 0;
                }

                entering = false;

                continue;
            }

            foreach(SelfIndexTriejoinIterator iterator in active)
            {
                if(!iterator.Open(TermId.FromEncoded(key)))
                {
                    throw new InvalidOperationException("An agreed key declined to open; the agreement loop and the iterator disagree on visibility.");
                }
            }

            level++;
            entering = true;
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }

    /// <summary>The track-max agreement loop: lower-bounds every participant, then raises stragglers to the running maximum until all participants agree on a key or one ends.</summary>
    /// <param name="active">The level's participants.</param>
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
}
