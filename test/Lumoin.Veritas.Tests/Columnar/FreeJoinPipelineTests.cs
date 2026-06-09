using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The Free Join pipeline — the reusable builder that scans the columnar index
/// into full-depth GHTs and drives the generic join — is answer-identical to the
/// system-of-record leapfrog engine, on a cyclic (triangle) and an acyclic
/// (chain) shape. This is the executor's oracle exercised through the pipeline a
/// rendezvous route will consume; a mis-build is caught, never a wrong answer.
/// </summary>
[TestClass]
internal sealed class FreeJoinPipelineTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Drains a solution stream into order-insensitive per-solution fingerprints.</summary>
    /// <param name="solutions">The stream.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static async Task<List<string>> LeapfrogFingerprintsAsync(IAsyncEnumerable<Solution> solutions)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Flattens a Free Join batch stream into the same fingerprint form, each row's cells sorted by variable id.</summary>
    /// <param name="batches">The pipeline's batch stream (or <see langword="null"/>).</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> PipelineFingerprints(IEnumerable<SolutionBatch>? batches)
    {
        Assert.IsNotNull(batches, "The pipeline returned no plan for a shape the index can answer.");

        List<string> fingerprints = [];
        foreach(SolutionBatch batch in batches)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                List<string> cells = [];
                for(int column = 0; column < batch.Schema.Count; column++)
                {
                    cells.Add($"{batch.Schema[column].Id}={batch.ColumnOf(column)[row]}");
                }

                cells.Sort(StringComparer.Ordinal);
                fingerprints.Add(string.Join(";", cells));
            }
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Builds the index and the leapfrog store from the fixture, runs the query on both, and asserts the pipeline's answers equal leapfrog's.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    private async Task AssertPipelineAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await LeapfrogFingerprintsAsync(store.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> pipeline = PipelineFingerprints(FreeJoinPipeline.Run(index, query));

        Assert.IsGreaterThan(0, oracle.Count);
        CollectionAssert.AreEqual(oracle, pipeline);
    }

    /// <summary>A directed graph over one predicate carrying some 3-cycles.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> TriangleFixture()
    {
        HashSet<EncodedTriple> seen = [];
        List<EncodedTriple> triples = [];
        for(uint i = 0; i < 14; i++)
        {
            for(uint j = 0; j < 14; j++)
            {
                if(i != j && (((i * 7) + (j * 3)) % 5) == 0 && seen.Add(EncodedTriple.FromEncoded(i, 200, j)))
                {
                    triples.Add(EncodedTriple.FromEncoded(i, 200, j));
                }
            }
        }

        return triples;
    }

    /// <summary>The triangle query <c>?x p ?y . ?y p ?z . ?z p ?x</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern TriangleQuery(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(x)),
            ],
            registry);
    }

    /// <summary>A connected chain fixture: each subject reaches an object on p1, and that object reaches a terminal on p2.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ChainFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 20; subject++)
        {
            uint @object = 100 + subject;
            triples.Add(EncodedTriple.FromEncoded(subject, 200, @object));
            triples.Add(EncodedTriple.FromEncoded(@object, 201, 500 + subject));
        }

        return triples;
    }

    /// <summary>The chain query <c>?s p1 ?o . ?o p2 ?t</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable t = registry.GetOrAdd("t");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(o), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(t)),
            ],
            registry);
    }

    [TestMethod]
    public async Task TriangleAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(TriangleFixture(), TriangleQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(ChainFixture(), ChainQuery).ConfigureAwait(false);
    }
}
