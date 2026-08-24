using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// A differential property test for the columnar index's
/// delta-main machinery: a chain of
/// <see cref="ColumnarTripleIndex.Apply"/> calls must present
/// exactly the merged view a fresh
/// <see cref="ColumnarTripleIndex.Build"/> over the final triple
/// set presents — same membership, same count, and same cursor
/// walks key for key.
/// </summary>
/// <remarks>
/// <para>
/// The reference replays the batches over a plain set with the
/// same tolerant semantics Apply documents: an addition of a
/// present triple and a removal of an absent one are no-ops.
/// Batches draw from the same small domain as the base, so
/// removals tombstone real base triples, re-additions clear
/// tombstones, additions duplicate base triples, and removals hit
/// accumulated additions — every normalisation branch fires. Small
/// bases cross the compaction threshold naturally, so both the
/// accumulate and the fold-into-fresh-base paths are exercised.
/// </para>
/// <para>
/// Cursor equivalence reuses the exhaustive walk from the
/// iterator differential: ascending keys on both sides make the
/// visit sequences comparable element-wise over random patterns
/// and rotated variable orders.
/// </para>
/// </remarks>
[TestClass]
internal sealed class ColumnarDeltaDifferentialTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const int ConstantCount = 5;

    private const int VariableCount = 3;

    //Matches the repo's property-test budget.
    private const long Iterations = 10_000;

    /// <summary>An Apply chain agrees with a fresh build of the final set on count, membership, and every generated cursor walk.</summary>
    [TestMethod]
    public void AppliedDeltaChainAgreesWithFreshBuild()
    {
        Gen<int[][]> genBase = Gen.Int[1, ConstantCount].Array[3].Array[0, 12];
        Gen<(int[][], int[][])[]> genBatches = Gen.Select(
                Gen.Int[1, ConstantCount].Array[3].Array[0, 5],
                Gen.Int[1, ConstantCount].Array[3].Array[0, 5])
            .Array[1, 4];
        Gen<int[]> genPattern = Gen.Int[0, ConstantCount + VariableCount - 1].Array[3];
        Gen<int> genRotation = Gen.Int[0, VariableCount - 1];

        Gen.Select(genBase, genBatches, genPattern, genRotation)
            .Where(static t => HasVariable(t.Item3) && NoSelfJoin(t.Item3))
            .Sample(t =>
            {
                (int[][] baseRows, (int[][] AddRows, int[][] RemoveRows)[] batches, int[] patternRow, int rotation) = t;

                EncodedTriple[] baseTriples = [.. baseRows.Select(ToTriple).Distinct()];

                ColumnarTripleIndex original = ColumnarTripleIndex.Build(baseTriples);
                ColumnarTripleIndex applied = original;
                HashSet<EncodedTriple> expected = [.. baseTriples];

                foreach((int[][] addRows, int[][] removeRows) in batches)
                {
                    EncodedTriple[] adds = [.. addRows.Select(ToTriple)];
                    EncodedTriple[] removes = [.. removeRows.Select(ToTriple)];

                    applied = applied.Apply(adds, removes);
                    expected.UnionWith(adds);
                    expected.ExceptWith(removes);
                }

                ColumnarTripleIndex rebuilt = ColumnarTripleIndex.Build(expected);

                int baseCount = baseTriples.Length;
                int expectedCount = expected.Count;

                Assert.AreEqual(baseCount, original.TripleCount, "Apply must not mutate the index it was called on.");
                Assert.AreEqual(expectedCount, applied.TripleCount, "Merged count must equal the replayed set's count.");

                //Membership over the whole (small) domain — covers
                //tombstoned, re-added, added, and untouched triples.
                for(uint s = 1; s <= ConstantCount; s++)
                {
                    for(uint p = 1; p <= ConstantCount; p++)
                    {
                        for(uint o = 1; o <= ConstantCount; o++)
                        {
                            bool expectedPresent = expected.Contains(EncodedTriple.FromEncoded(s, p, o));
                            bool actualPresent = applied.Contains(TermId.FromEncoded(s), TermId.FromEncoded(p), TermId.FromEncoded(o));

                            Assert.AreEqual(expectedPresent, actualPresent, $"Membership of ({s} {p} {o}) diverges.");
                        }
                    }
                }

                //Cursor walks must agree key for key.
                VariableRegistry registry = new();
                Variable[] variables = [.. Enumerable.Range(0, VariableCount).Select(i => registry.GetOrAdd($"v{i}"))];

                TriplePattern pattern = new(
                    ToPosition(patternRow[0], variables),
                    ToPosition(patternRow[1], variables),
                    ToPosition(patternRow[2], variables));

                Variable[] patternVariables = [.. pattern.Variables()];
                Variable[] variableOrder = [.. Enumerable.Range(0, patternVariables.Length)
                    .Select(i => patternVariables[(i + rotation) % patternVariables.Length])];

                List<string> appliedBindings = Walk(new ColumnarTriejoinIterator(applied, pattern, variableOrder), variableOrder.Length);
                List<string> rebuiltBindings = Walk(new ColumnarTriejoinIterator(rebuilt, pattern, variableOrder), variableOrder.Length);

                Assert.IsTrue(
                    appliedBindings.SequenceEqual(rebuiltBindings),
                    $"Walks diverge after {batches.Length} batches over a {baseTriples.Length}-triple base: "
                    + $"applied produced [{string.Join("; ", appliedBindings)}], rebuilt produced [{string.Join("; ", rebuiltBindings)}].");
            }, iter: Iterations);
    }

    private static EncodedTriple ToTriple(int[] row)
    {
        return EncodedTriple.FromEncoded((uint)row[0], (uint)row[1], (uint)row[2]);
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

    //Exhaustive depth-first walk over a columnar iterator: visits
    //every full binding in visit order, mixing Next with
    //Seek(key + 1) like the iterator differential does.
    private static List<string> Walk(ColumnarTriejoinIterator iterator, int variableCount)
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
            if(iterator.AtEnd)
            {
                if(iterator.DescendedLevels == 0)
                {
                    return results;
                }

                iterator.Up();
                bound.RemoveAt(bound.Count - 1);
                Advance();

                continue;
            }

            uint key = iterator.Key.Encoded;

            Assert.IsTrue(iterator.Open(TermId.FromEncoded(key)), $"Open must succeed for the key {key} the cursor is positioned on.");
            bound.Add(key);

            if(iterator.DescendedLevels == variableCount)
            {
                results.Add(string.Join(",", bound));
                iterator.Up();
                bound.RemoveAt(bound.Count - 1);
                Advance();
            }
        }

        void Advance()
        {
            if(!iterator.AtEnd && step++ % 4 == 3)
            {
                iterator.Seek(TermId.FromEncoded(iterator.Key.Encoded + 1));
            }
            else
            {
                iterator.Next();
            }
        }
    }
}
