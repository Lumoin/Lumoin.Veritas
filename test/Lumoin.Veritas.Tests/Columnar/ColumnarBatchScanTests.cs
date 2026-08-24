using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The batch scan's contract: for every bound-position mask, on
/// delta-free and delta-carrying indexes alike, the flattened
/// batch stream equals a naive filter of the merged triple set —
/// same rows, same bindings, batches full except the last.
/// </summary>
[TestClass]
internal sealed class ColumnarBatchScanTests
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

    /// <summary>A fixture large enough to cross batch boundaries, with skewed term reuse.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> Fixture()
    {
        List<EncodedTriple> triples = [];
        ulong state = 21;
        for(int i = 0; i < 5_000; i++)
        {
            state = Mix(state);
            triples.Add(EncodedTriple.FromEncoded(
                100 + (uint)(state % 40),
                200 + (uint)((state >> 8) % 4),
                300 + (uint)((state >> 16) % 200)));
        }

        return triples;
    }

    /// <summary>Builds the pattern for a bound mask, binding positions to known-present constants.</summary>
    /// <param name="mask">Bit 0 = subject bound, bit 1 = predicate, bit 2 = object.</param>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static TriplePattern PatternOf(int mask, VariableRegistry registry)
    {
        PatternPosition subject = (mask & 1) != 0 ? PatternPosition.Bound(TermId.FromEncoded(110)) : PatternPosition.OfVariable(registry.GetOrAdd("s"));
        PatternPosition predicate = (mask & 2) != 0 ? PatternPosition.Bound(TermId.FromEncoded(201)) : PatternPosition.OfVariable(registry.GetOrAdd("p"));
        PatternPosition @object = (mask & 4) != 0 ? PatternPosition.Bound(TermId.FromEncoded(333)) : PatternPosition.OfVariable(registry.GetOrAdd("o"));

        return new TriplePattern(subject, predicate, @object);
    }

    /// <summary>Flattens a batch stream to sorted row fingerprints over the scan schema.</summary>
    /// <param name="index">The index to scan.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The sorted fingerprints, and the observed batch sizes.</returns>
    private static (List<string> Rows, List<int> BatchSizes) ScanFingerprints(ColumnarTripleIndex index, TriplePattern pattern)
    {
        IReadOnlyList<Variable> schema = ColumnarBatchScan.ScanSchemaOf(index, pattern);
        List<string> rows = [];
        List<int> sizes = [];

        foreach(SolutionBatch batch in ColumnarBatchScan.Scan(index, pattern))
        {
            sizes.Add(batch.Count);

            for(int row = 0; row < batch.Count; row++)
            {
                List<string> parts = [];
                for(int column = 0; column < schema.Count; column++)
                {
                    parts.Add($"{schema[column].Id}={batch.ColumnOf(column)[row]}");
                }

                parts.Sort(StringComparer.Ordinal);
                rows.Add(string.Join(";", parts));
            }
        }

        rows.Sort(StringComparer.Ordinal);

        return (rows, sizes);
    }

    /// <summary>The naive reference: filter the merged triples and bind the pattern's variables.</summary>
    /// <param name="index">The index.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="schema">The scan schema (for variable identity).</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> ReferenceFingerprints(ColumnarTripleIndex index, TriplePattern pattern, IReadOnlyList<Variable> schema)
    {
        List<string> rows = [];

        foreach(EncodedTriple triple in index.EnumerateTriples())
        {
            bool matches = true;
            for(int rdfPosition = 0; rdfPosition < 3 && matches; rdfPosition++)
            {
                PatternPosition slot = pattern.At(rdfPosition);
                if(slot.IsBound)
                {
                    uint actual = rdfPosition switch { 0 => triple.Subject.Encoded, 1 => triple.Predicate.Encoded, _ => triple.Object.Encoded };
                    matches = slot.BoundTerm.Encoded == actual;
                }
            }

            if(!matches)
            {
                continue;
            }

            List<string> parts = [];
            foreach(Variable variable in schema)
            {
                for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
                {
                    PatternPosition slot = pattern.At(rdfPosition);
                    if(slot.IsVariable && slot.Variable == variable)
                    {
                        uint value = rdfPosition switch { 0 => triple.Subject.Encoded, 1 => triple.Predicate.Encoded, _ => triple.Object.Encoded };
                        parts.Add($"{variable.Id}={value}");
                    }
                }
            }

            parts.Sort(StringComparer.Ordinal);
            rows.Add(string.Join(";", parts));
        }

        rows.Sort(StringComparer.Ordinal);

        return rows;
    }

    /// <summary>Runs every bound mask against one index and asserts scan ≡ reference plus the full-except-last batch shape.</summary>
    /// <param name="index">The index under test.</param>
    private static void AssertAllMasksAgree(ColumnarTripleIndex index)
    {
        for(int mask = 0; mask < 8; mask++)
        {
            VariableRegistry registry = new();
            TriplePattern pattern = PatternOf(mask, registry);
            IReadOnlyList<Variable> schema = ColumnarBatchScan.ScanSchemaOf(index, pattern);

            (List<string> rows, List<int> sizes) = ScanFingerprints(index, pattern);
            List<string> reference = ReferenceFingerprints(index, pattern, schema);

            Assert.AreSequenceEqual(reference, rows, $"mask {mask} disagreed");

            for(int i = 0; i < sizes.Count - 1; i++)
            {
                Assert.AreEqual(SolutionBatch.BatchLength, sizes[i], $"mask {mask}: only the last batch may be partial");
            }
        }
    }

    [TestMethod]
    public void EveryBoundMaskAgreesWithTheNaiveReferenceOnTheDeltaFreeIndex()
    {
        AssertAllMasksAgree(ColumnarTripleIndex.Build(Fixture()));
    }

    [TestMethod]
    public void EveryBoundMaskAgreesWithTheNaiveReferenceUnderAnAccumulatedDelta()
    {
        List<EncodedTriple> triples = Fixture();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples.Take(4_500));

        //A small delta keeps the index below the compaction
        //threshold: the merged fallback path is what runs.
        ColumnarTripleIndex withDelta = index.Apply(triples.Skip(4_500), triples.Take(120));

        Assert.IsTrue(withDelta.HasDelta);
        AssertAllMasksAgree(withDelta);
    }

    [TestMethod]
    public void ThreeRotationIndexesScanEveryBoundMaskToo()
    {
        AssertAllMasksAgree(ColumnarTripleIndex.Build(Fixture(), ColumnarOrderSetMode.ThreeRotations));
    }
}
