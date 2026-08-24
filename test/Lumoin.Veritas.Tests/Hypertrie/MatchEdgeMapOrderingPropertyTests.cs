using CsCheck;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Pins the structural contract <see cref="HypertrieOps.Match(HypertrieNode, TermId, TermId, TermId)"/>
/// relies on: at every depth, a node's edge maps are indexed in
/// ascending order of the remaining original positions. The
/// contract is established by <c>BuildPathDepth2</c>'s internal
/// derivation of the inner positions; a regression that swapped
/// the inner order would cause <c>Match</c> to look up values in
/// the wrong edge map for queries that descend through inner edge
/// maps. This test exercises the eight bound/unbound query
/// combinations over randomly generated triple sets and compares
/// against a brute-force ground truth.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why behavioural and not structural.</b> The invariant lives
/// inside <see cref="HypertrieNode"/>'s edge map array, but the
/// node does not carry "which original positions does each edge
/// map represent" labels. Reconstructing the labels from the
/// build path would duplicate the very logic under test. A
/// behavioural property — every query pattern returns exactly the
/// matching triples — catches the same regressions and does not
/// rely on inspecting node internals.
/// </para>
/// <para>
/// <b>Term-id range.</b> The generator picks term ids from a
/// small range (1..6) so distinct triples reuse subjects,
/// predicates, and objects across the set. That maximises the
/// fan-out at every depth — depth-2 nodes carry multiple inner
/// edge map entries, depth-1 leaves carry multiple keys — which
/// is exactly the structure where an edge-map-ordering bug would
/// surface. The range starts at <c>1</c> because <c>0</c> is the
/// <see cref="TermId.None"/> sentinel used to signal "unbound" in
/// the Match signature; including <c>0</c> as a generated term id
/// would alias bound queries with unbound ones.
/// </para>
/// </remarks>
[TestClass]
internal sealed class MatchEdgeMapOrderingPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MatchAgreesWithBruteForceOverAllBindPatterns()
    {
        await Gen.Int[1, 6].Array[3].Array[1, 30].SampleAsync(async tripleArrays =>
        {
            EncodedTriple[] triples = [.. tripleArrays
                .Select(static a => EncodedTriple.FromEncoded((uint)a[0], (uint)a[1], (uint)a[2]))
                .Distinct()];

            //BuildAsync is async because of the mutation gate, so the
            //property sample runs as an async lambda and awaits each build.
            HypertrieGraphStore graph = await HypertrieGraphStore
                .BuildAsync(triples, VeritasHashing.Default, default)
                .ConfigureAwait(false);

            HashSet<EncodedTriple> ground = [.. triples];
            EncodedTriple binding = triples[0];

            //Iterate every bind pattern in {0, 1}^3. Bit 0 = subject
            //bound, bit 1 = predicate bound, bit 2 = object bound.
            //All eight combinations are exercised, which covers
            //every edge map descent path the hypertrie can take.
            for(int patternBits = 0; patternBits < 8; patternBits++)
            {
                bool sBound = (patternBits & 1) != 0;
                bool pBound = (patternBits & 2) != 0;
                bool oBound = (patternBits & 4) != 0;

                TermId s = sBound ? binding.Subject : TermId.None;
                TermId p = pBound ? binding.Predicate : TermId.None;
                TermId o = oBound ? binding.Object : TermId.None;

                HashSet<EncodedTriple> actual = [.. HypertrieOps.Match(graph.Snapshot.Store.GetByHandle(graph.Snapshot.Root), graph.Snapshot.Store, s, p, o)];
                HashSet<EncodedTriple> expected = [.. ground.Where(t =>
                    (!sBound || t.Subject == s) &&
                    (!pBound || t.Predicate == p) &&
                    (!oBound || t.Object == o))];

                Assert.IsTrue(
                    expected.SetEquals(actual),
                    $"Pattern (s={s}, p={p}, o={o}) over {triples.Length} triples: expected {expected.Count}, actual {actual.Count}.");
            }
        }).ConfigureAwait(false);
    }
}
