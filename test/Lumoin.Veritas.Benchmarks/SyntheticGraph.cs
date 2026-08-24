using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Deterministic synthetic graph data for benchmarks. Four
/// generators are exposed: <see cref="Random"/> draws each
/// position uniformly from configurable value sets and is the
/// default load for build-time benchmarks; <see cref="GenerateSocial"/>
/// produces a structured "knows / livesIn" shape for query-time
/// benchmarks where realistic selectivity matters;
/// <see cref="GenerateChain"/> produces a linear chain with
/// sparse dead-end branches for property-path depth tests;
/// <see cref="GenerateSmallWorld"/> produces a Watts–Strogatz-style
/// small-world graph with many short cycles for property-path
/// cycle-handling tests.
/// </summary>
[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Synthetic benchmark data — System.Random with a fixed seed is the right choice for reproducible inputs. Cryptographic randomness has no role in benchmark data generation.")]
internal static class SyntheticGraph
{
    /// <summary>Predicate id used by <see cref="GenerateChain"/> and <see cref="GenerateSmallWorld"/> for the primary edge.</summary>
    public const uint PathPredicateP = 4_000_001;

    /// <summary>Predicate id used by <see cref="GenerateChain"/> and <see cref="GenerateSmallWorld"/> for the secondary edge that alternation paths exercise.</summary>
    public const uint PathPredicateQ = 4_000_002;

    /// <summary>Predicate id used by <see cref="GenerateSocial"/> for "knows"-shaped edges.</summary>
    public const uint KnowsPredicate = 1_000_000;

    /// <summary>Predicate id used by <see cref="GenerateSocial"/> for "livesIn"-shaped edges.</summary>
    public const uint LivesInPredicate = 2_000_000;

    /// <summary>Object id used by <see cref="GenerateSocial"/> for the most-populated city — picked so that <c>(?y livesIn PopularCity)</c> selects roughly half the subjects.</summary>
    public const uint PopularCity = 3_000_001;

    //Subject and city id ranges for GenerateSocial. Disjoint so
    //subject ids cannot collide with city ids regardless of how
    //big subjectCount grows.
    private const uint SocialSubjectStart = 1;

    private const uint SocialCityStart = 3_000_000;

    private const int SocialCityCount = 16;

    //Id ranges for Random — wide enough that the random draws
    //stay inside the configured distinct-value counts without
    //wrapping into reserved zones.
    private const uint RandomSubjectStart = 10_000_000;

    private const uint RandomPredicateStart = 20_000_000;

    private const uint RandomObjectStart = 30_000_000;

    //Id range for GenerateChain and GenerateSmallWorld nodes. Disjoint
    //from every other generator's range so a chain or small-world
    //node id cannot collide with a Social subject, a Social city, or
    //a Random draw. Chain branch targets live above the main range so
    //they are unambiguously dead-end nodes.
    private const uint PathNodeStart = 50_000_000;

    private const uint PathBranchOffset = 100_000_000;

    /// <summary>
    /// Generates an array of triples by drawing each position
    /// uniformly from a configurable value space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The realised number of distinct triples may be slightly
    /// less than <paramref name="targetTripleCount"/> because
    /// duplicate draws are silently absorbed: the same
    /// <c>(s, p, o)</c> triple landing twice contributes only
    /// once. This is intentional — we want
    /// <see cref="HypertrieGraphStore"/> and
    /// <see cref="InMemoryGraphStore"/> builds to start from the
    /// same input, and both deduplicate input internally; the
    /// benchmark would otherwise spend effort generating data
    /// that the build paths would discard.
    /// </para>
    /// <para>
    /// The id ranges for subjects, predicates, and objects are
    /// disjoint so that any given <see cref="long"/> value
    /// belongs to exactly one position class. This is not a
    /// correctness requirement of the stores — they are
    /// position-typed at the protocol level — but it makes
    /// generated data easier to read in failure diagnostics.
    /// </para>
    /// </remarks>
    /// <param name="targetTripleCount">The approximate number of distinct triples to produce. Must be positive.</param>
    /// <param name="distinctSubjects">The number of distinct subject ids to draw from. Must be positive.</param>
    /// <param name="distinctPredicates">The number of distinct predicate ids to draw from. Must be positive.</param>
    /// <param name="distinctObjects">The number of distinct object ids to draw from. Must be positive.</param>
    /// <param name="seed">Seed for <see cref="System.Random"/>; the same seed produces the same triple array across runs and machines.</param>
    /// <returns>An array of distinct triples; its length is at most <paramref name="targetTripleCount"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A required count is non-positive.</exception>
    public static EncodedTriple[] Random(
        int targetTripleCount,
        int distinctSubjects,
        int distinctPredicates,
        int distinctObjects,
        int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetTripleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distinctSubjects);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distinctPredicates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distinctObjects);

        Random random = new(seed);
        HashSet<EncodedTriple> seen = new(targetTripleCount);

        //Bounded retry budget — when the value-space is tight the
        //hit-rate of fresh draws drops, and we don't want to spin
        //forever chasing the last few unique triples. Cap at
        //2× the requested count of attempts; in practice the
        //recommended distinct-value sizing in BuildBenchmark
        //gives a near-100% fresh-draw rate and the budget is
        //never approached.
        int attemptBudget = checked(targetTripleCount * 2);

        for(int attempt = 0; attempt < attemptBudget && seen.Count < targetTripleCount; attempt++)
        {
            uint s = RandomSubjectStart + (uint)random.Next(distinctSubjects);
            uint p = RandomPredicateStart + (uint)random.Next(distinctPredicates);
            uint o = RandomObjectStart + (uint)random.Next(distinctObjects);

            seen.Add(EncodedTriple.FromEncoded(s, p, o));
        }

        EncodedTriple[] result = new EncodedTriple[seen.Count];
        seen.CopyTo(result);

        return result;
    }

    /// <summary>
    /// Generates a deterministic social graph with the given
    /// number of subjects. Each subject knows ~3 other subjects
    /// (uniform-random) and lives in one of <see cref="SocialCityCount"/>
    /// cities, weighted so that <see cref="PopularCity"/> houses
    /// roughly half.
    /// </summary>
    /// <param name="subjectCount">The number of distinct subjects to generate. Must be positive.</param>
    /// <param name="seed">Seed for <see cref="System.Random"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="subjectCount"/> is non-positive.</exception>
    public static EncodedTriple[] GenerateSocial(int subjectCount, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(subjectCount);

        Random random = new(seed);
        List<EncodedTriple> triples = new(subjectCount * 4);

        for(uint s = 0; s < (uint)subjectCount; s++)
        {
            uint subject = SocialSubjectStart + s;

            //Three "knows" edges to random subjects.
            for(int k = 0; k < 3; k++)
            {
                uint target = SocialSubjectStart + (uint)random.Next(subjectCount);

                if(target == subject)
                {
                    continue;
                }

                triples.Add(EncodedTriple.FromEncoded(subject, KnowsPredicate, target));
            }

            //One livesIn edge — 50% chance of the popular city,
            //50% of one of the others.
            uint city = random.Next(2) == 0
                ? PopularCity
                : SocialCityStart + (uint)random.Next(SocialCityCount);

            triples.Add(EncodedTriple.FromEncoded(subject, LivesInPredicate, city));
        }

        return [.. triples];
    }

    /// <summary>
    /// Generates a linear chain of <paramref name="linkCount"/> edges
    /// using <see cref="PathPredicateP"/>, with a sparse fan of
    /// dead-end side branches via <see cref="PathPredicateQ"/> every
    /// <paramref name="branchEvery"/> nodes. Node ids live in the
    /// <c>PathNodeStart</c>-rooted range; branch-target ids live above
    /// the main range so the branches are unambiguously dead-end
    /// nodes.
    /// </summary>
    /// <param name="linkCount">The number of <see cref="PathPredicateP"/> edges in the main chain. Must be positive.</param>
    /// <param name="branchEvery">Emit one <see cref="PathPredicateQ"/> branch every this many chain positions. Must be positive.</param>
    /// <param name="seed">Reserved for future variation; currently unused. The generator is deterministic.</param>
    /// <returns>An array of triples: the main chain followed by the branch fan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A required count is non-positive.</exception>
    /// <remarks>
    /// <para>
    /// <b>Path-shape coverage.</b> The chain is the canonical stress
    /// test for transitive-closure operators (<c>:p+</c>, <c>:p*</c>)
    /// where the result-set size equals the chain length. The
    /// branch fan gives <c>(:p|:q)+</c> something to chew on without
    /// changing the depth profile — branches are dead-end side
    /// hops that the alternation BFS visits exactly once.
    /// </para>
    /// </remarks>
    public static EncodedTriple[] GenerateChain(int linkCount, int branchEvery, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(linkCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(branchEvery);
        _ = seed;

        int branchCount = (linkCount + branchEvery - 1) / branchEvery;
        EncodedTriple[] triples = new EncodedTriple[linkCount + branchCount];

        for(int i = 0; i < linkCount; i++)
        {
            uint subject = PathNodeStart + (uint)i;
            uint @object = PathNodeStart + (uint)(i + 1);

            triples[i] = EncodedTriple.FromEncoded(subject, PathPredicateP, @object);
        }

        int branchIndex = 0;
        for(int i = 0; i < linkCount; i += branchEvery)
        {
            uint subject = PathNodeStart + (uint)i;
            uint branchTarget = PathNodeStart + PathBranchOffset + (uint)i;

            triples[linkCount + branchIndex] = EncodedTriple.FromEncoded(subject, PathPredicateQ, branchTarget);
            branchIndex++;
        }

        return triples;
    }

    /// <summary>
    /// Generates a Watts–Strogatz-style small-world graph on
    /// <paramref name="nodeCount"/> nodes: a circular base where each
    /// node connects via <see cref="PathPredicateP"/> to its
    /// <paramref name="neighbours"/> nearest neighbours on each side,
    /// with <paramref name="rewireFraction"/> of those edges rewired
    /// to random non-neighbours. A secondary
    /// <see cref="PathPredicateQ"/> edge connects each node to its
    /// <c>(index + 7) mod nodeCount</c> sibling so alternation paths
    /// have somewhere to go.
    /// </summary>
    /// <param name="nodeCount">The number of distinct nodes. Must be positive.</param>
    /// <param name="neighbours">The number of <see cref="PathPredicateP"/> neighbours per side of the circular base. Must be positive; total <c>p</c> edges per node is twice this.</param>
    /// <param name="rewireFraction">The fraction of <c>p</c> edges rewired to random non-neighbour targets. Must be in <c>[0.0, 1.0]</c>.</param>
    /// <param name="seed">Seed for <see cref="System.Random"/>; the same seed produces the same triple array across runs and machines.</param>
    /// <returns>An array of triples: <c>p</c> circular neighbours (with rewiring) followed by <c>q</c> mod-7 sibling edges. Total length is <c>nodeCount × (2 × neighbours + 1)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A required count is non-positive, or <paramref name="rewireFraction"/> is outside <c>[0.0, 1.0]</c>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Path-shape coverage.</b> The circular base creates many
    /// short cycles, which stresses <c>:p*</c> reflexivity dedup
    /// (the BFS frontier converges quickly and the visited-set
    /// membership test dominates work). The rewired long-range
    /// edges create a "small-world" diameter, which keeps the
    /// transitive closure reachable from any start in O(log
    /// nodeCount) steps despite the local clustering. The mod-7
    /// sibling edge connects every node into a distinct cycle for
    /// <see cref="PathPredicateQ"/> so alternation BFSes cannot
    /// short-circuit on missing edges.
    /// </para>
    /// </remarks>
    public static EncodedTriple[] GenerateSmallWorld(int nodeCount, int neighbours, double rewireFraction, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(neighbours);
        if(rewireFraction < 0.0 || rewireFraction > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rewireFraction), rewireFraction, "Rewire fraction must lie in [0.0, 1.0].");
        }

        Random random = new(seed);
        int pEdgesPerNode = 2 * neighbours;
        int totalLength = nodeCount * (pEdgesPerNode + 1);
        EncodedTriple[] triples = new EncodedTriple[totalLength];
        int cursor = 0;

        for(int i = 0; i < nodeCount; i++)
        {
            uint subject = PathNodeStart + (uint)i;

            for(int k = 1; k <= neighbours; k++)
            {
                int leftIndex = ((i - k) % nodeCount + nodeCount) % nodeCount;
                int rightIndex = (i + k) % nodeCount;

                uint leftTarget = random.NextDouble() < rewireFraction
                    ? PathNodeStart + (uint)random.Next(nodeCount)
                    : PathNodeStart + (uint)leftIndex;
                uint rightTarget = random.NextDouble() < rewireFraction
                    ? PathNodeStart + (uint)random.Next(nodeCount)
                    : PathNodeStart + (uint)rightIndex;

                triples[cursor++] = EncodedTriple.FromEncoded(subject, PathPredicateP, leftTarget);
                triples[cursor++] = EncodedTriple.FromEncoded(subject, PathPredicateP, rightTarget);
            }

            uint sibling = PathNodeStart + (uint)((i + 7) % nodeCount);
            triples[cursor++] = EncodedTriple.FromEncoded(subject, PathPredicateQ, sibling);
        }

        return triples;
    }
}
