using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Deterministic synthetic graph data for benchmarks. Two
/// generators are exposed: <see cref="Random"/> draws each
/// position uniformly from configurable value sets and is the
/// default load for build-time benchmarks; <see cref="GenerateSocial"/>
/// produces a structured "knows / livesIn" shape for query-time
/// benchmarks where realistic selectivity matters.
/// </summary>
[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Synthetic benchmark data — System.Random with a fixed seed is the right choice for reproducible inputs. Cryptographic randomness has no role in benchmark data generation.")]
internal static class SyntheticGraph
{
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
}
