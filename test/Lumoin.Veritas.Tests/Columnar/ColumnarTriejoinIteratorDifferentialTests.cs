using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// A differential property test pinning
/// <see cref="ColumnarTriejoinIterator"/> against the hypertrie's
/// <see cref="TriejoinIterator"/>: both implement the same level
/// contract, so an identical exhaustive walk over the same pattern,
/// variable order, and triple set must visit identical keys in
/// identical order and produce identical binding sequences.
/// </summary>
/// <remarks>
/// <para>
/// The walk descends depth-first: at each level it reads the
/// current key, opens it, and either records a full binding (at the
/// deepest level) or continues one level down; exhausted levels
/// rewind. Both iterators present keys in ascending order, so the
/// visit sequences are comparable element-wise — an order-sensitive
/// equality, stronger than set equality. Every fourth advance uses
/// <c>Seek(key + 1)</c> instead of <c>Next()</c>, which is
/// equivalent on distinct ascending keys and keeps both sides in
/// lockstep while exercising the seek path.
/// </para>
/// <para>
/// The generator draws term ids from a small domain so keys recur
/// across positions, patterns mix bound and variable positions, and
/// the variable order is rotated so non-first-occurrence orders are
/// exercised. Patterns with self-joins are excluded — both
/// iterators reject them by contract.
/// </para>
/// </remarks>
[TestClass]
internal sealed class ColumnarTriejoinIteratorDifferentialTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const int ConstantCount = 6;

    private const int VariableCount = 3;

    //Matches the repo's property-test budget.
    private const long Iterations = 10_000;

    /// <summary>The columnar iterator visits the same keys in the same order as the hypertrie iterator on every generated walk.</summary>
    [TestMethod]
    public async Task ColumnarIteratorAgreesWithHypertrieIteratorOverRandomWalks()
    {
        Gen<int[][]> genTriples = Gen.Int[1, ConstantCount].Array[3].Array[1, 16];
        Gen<int[]> genPattern = Gen.Int[0, ConstantCount + VariableCount - 1].Array[3];
        Gen<int> genRotation = Gen.Int[0, VariableCount - 1];

        await Gen.Select(genTriples, genPattern, genRotation)
            .Where(static t => HasVariable(t.Item2) && NoSelfJoin(t.Item2))
            .SampleAsync(async t =>
            {
                (int[][] tripleRows, int[] patternRow, int rotation) = t;

                EncodedTriple[] triples = [.. tripleRows
                    .Select(static r => EncodedTriple.FromEncoded((uint)r[0], (uint)r[1], (uint)r[2]))
                    .Distinct()];

                VariableRegistry registry = new();
                Variable[] variables = [.. Enumerable.Range(0, VariableCount).Select(i => registry.GetOrAdd($"v{i}"))];

                TriplePattern pattern = new(
                    ToPosition(patternRow[0], variables),
                    ToPosition(patternRow[1], variables),
                    ToPosition(patternRow[2], variables));

                //The pattern's distinct variables, rotated so the
                //walk exercises orders other than first-occurrence.
                Variable[] patternVariables = [.. pattern.Variables()];
                Variable[] variableOrder = [.. Enumerable.Range(0, patternVariables.Length)
                    .Select(i => patternVariables[(i + rotation) % patternVariables.Length])];

                HypertrieGraphStore store = await HypertrieGraphStore
                    .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken)
                    .ConfigureAwait(false);

                //The property loop builds one store per iteration;
                //deterministic disposal returns the pool slabs so
                //ten thousand iterations stay flat in memory
                //instead of racing the garbage collector.
                using NodeStore nodeStore = store.Snapshot.Store;

                ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(triples);

                using TriejoinIterator hypertrieIterator = new(store.Snapshot, pattern, variableOrder, TimeProvider.System);
                ColumnarTriejoinIterator columnarIterator = new(columnar, pattern, variableOrder);

                List<string> hypertrieBindings = Walk(
                    new IteratorOps
                    {
                        AtEnd = () => hypertrieIterator.AtEnd,
                        Key = () => hypertrieIterator.Key.Encoded,
                        Open = value => hypertrieIterator.Open(TermId.FromEncoded(value), TestContext.CancellationToken),
                        Up = hypertrieIterator.Up,
                        Next = () => hypertrieIterator.Next(TestContext.CancellationToken),
                        Seek = value => hypertrieIterator.Seek(TermId.FromEncoded(value), TestContext.CancellationToken),
                        Descended = () => hypertrieIterator.DescendedLevels,
                    },
                    variableOrder.Length);

                List<string> columnarBindings = Walk(
                    new IteratorOps
                    {
                        AtEnd = () => columnarIterator.AtEnd,
                        Key = () => columnarIterator.Key.Encoded,
                        Open = value => columnarIterator.Open(TermId.FromEncoded(value)),
                        Up = columnarIterator.Up,
                        Next = columnarIterator.Next,
                        Seek = value => columnarIterator.Seek(TermId.FromEncoded(value)),
                        Descended = () => columnarIterator.DescendedLevels,
                    },
                    variableOrder.Length);

                Assert.IsTrue(
                    hypertrieBindings.SequenceEqual(columnarBindings),
                    $"Iterators diverge over {triples.Length} triples, pattern ({patternRow[0]},{patternRow[1]},{patternRow[2]}), rotation {rotation}: "
                    + $"hypertrie produced [{string.Join("; ", hypertrieBindings)}], columnar produced [{string.Join("; ", columnarBindings)}].");
            }, iter: Iterations).ConfigureAwait(false);
    }

    //A pattern qualifies when at least one token is a variable.
    private static bool HasVariable(int[] pattern)
    {
        return pattern.Any(static token => token >= ConstantCount);
    }

    //A pattern has no self-join when its variable tokens are distinct.
    private static bool NoSelfJoin(int[] pattern)
    {
        HashSet<int> seenVariables = [];

        foreach(int token in pattern)
        {
            if(token >= ConstantCount && !seenVariables.Add(token))
            {
                return false;
            }
        }

        return true;
    }

    private static PatternPosition ToPosition(int token, Variable[] variables)
    {
        return token < ConstantCount
            ? PatternPosition.Bound(TermId.FromEncoded((uint)(token + 1)))
            : PatternPosition.OfVariable(variables[token - ConstantCount]);
    }

    //The uniform operation surface the walk drives; both iterators
    //adapt onto it.
    private sealed class IteratorOps
    {
        /// <summary>Reads whether the current level is exhausted.</summary>
        public required Func<bool> AtEnd { get; init; }

        /// <summary>Reads the current key.</summary>
        public required Func<uint> Key { get; init; }

        /// <summary>Binds the current variable to the given key.</summary>
        public required Func<uint, bool> Open { get; init; }

        /// <summary>Rewinds one level.</summary>
        public required Action Up { get; init; }

        /// <summary>Advances to the next key.</summary>
        public required Action Next { get; init; }

        /// <summary>Advances to the first key at or above the target.</summary>
        public required Action<uint> Seek { get; init; }

        /// <summary>Reads the number of descended levels.</summary>
        public required Func<int> Descended { get; init; }
    }

    //Exhaustive depth-first walk: visits every full binding the
    //iterator can produce, in visit order. Every fourth advance
    //seeks to key + 1 instead of stepping, exercising the seek path
    //with an operation equivalent to Next on distinct ascending
    //keys.
    private static List<string> Walk(IteratorOps iterator, int variableCount)
    {
        List<string> results = [];

        if(variableCount == 0)
        {
            return results;
        }

        List<uint> bound = [];
        int step = 0;

        while(true)
        {
            if(iterator.AtEnd())
            {
                if(iterator.Descended() == 0)
                {
                    return results;
                }

                iterator.Up();
                bound.RemoveAt(bound.Count - 1);
                Advance();

                continue;
            }

            uint key = iterator.Key();

            Assert.IsTrue(iterator.Open(key), $"Open must succeed for the key {key} the cursor is positioned on.");
            bound.Add(key);

            if(iterator.Descended() == variableCount)
            {
                results.Add(string.Join(",", bound));
                iterator.Up();
                bound.RemoveAt(bound.Count - 1);
                Advance();
            }
        }

        void Advance()
        {
            if(!iterator.AtEnd() && step++ % 4 == 3)
            {
                iterator.Seek(iterator.Key() + 1);
            }
            else
            {
                iterator.Next();
            }
        }
    }
}
