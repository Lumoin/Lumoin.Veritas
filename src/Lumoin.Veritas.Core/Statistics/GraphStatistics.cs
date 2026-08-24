using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Statistics;

/// <summary>
/// Store-agnostic structural statistics of a triple set: the counts and degree
/// summaries derivable from the triples alone, independent of how they are
/// indexed. Triple count, distinct subjects, predicates, and objects, the
/// subject out-degree distribution (min, max, mean), and the heaviest single
/// predicate's frequency. Computed in one pass over the triples.
/// </summary>
/// <param name="TripleCount">The number of triples passed in.</param>
/// <param name="DistinctSubjects">The number of distinct subject terms.</param>
/// <param name="DistinctPredicates">The number of distinct predicate terms.</param>
/// <param name="DistinctObjects">The number of distinct object terms.</param>
/// <param name="MinSubjectOutDegree">The fewest triples sharing one subject.</param>
/// <param name="MaxSubjectOutDegree">The most triples sharing one subject.</param>
/// <param name="MeanSubjectOutDegree">The mean triples per distinct subject.</param>
/// <param name="MaxPredicateFrequency">The triple count of the most frequent predicate.</param>
public sealed record GraphStatistics(
    long TripleCount,
    int DistinctSubjects,
    int DistinctPredicates,
    int DistinctObjects,
    int MinSubjectOutDegree,
    int MaxSubjectOutDegree,
    double MeanSubjectOutDegree,
    int MaxPredicateFrequency)
{
    /// <summary>Computes the statistics in one pass over the triples.</summary>
    /// <param name="triples">The triple set.</param>
    /// <returns>The statistics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <see langword="null"/>.</exception>
    public static GraphStatistics From(IReadOnlyCollection<EncodedTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(triples);

        HashSet<uint> subjects = [];
        HashSet<uint> predicates = [];
        HashSet<uint> objects = [];
        Dictionary<uint, int> subjectDegree = [];
        Dictionary<uint, int> predicateFrequency = [];

        foreach(EncodedTriple triple in triples)
        {
            uint subject = triple.Subject.Encoded;
            uint predicate = triple.Predicate.Encoded;
            uint @object = triple.Object.Encoded;

            subjects.Add(subject);
            predicates.Add(predicate);
            objects.Add(@object);
            subjectDegree[subject] = subjectDegree.TryGetValue(subject, out int degree) ? degree + 1 : 1;
            predicateFrequency[predicate] = predicateFrequency.TryGetValue(predicate, out int frequency) ? frequency + 1 : 1;
        }

        int minOutDegree = subjectDegree.Count == 0 ? 0 : int.MaxValue;
        int maxOutDegree = 0;
        foreach(int degree in subjectDegree.Values)
        {
            minOutDegree = Math.Min(minOutDegree, degree);
            maxOutDegree = Math.Max(maxOutDegree, degree);
        }

        int maxPredicateFrequency = 0;
        foreach(int frequency in predicateFrequency.Values)
        {
            maxPredicateFrequency = Math.Max(maxPredicateFrequency, frequency);
        }

        double meanOutDegree = subjects.Count == 0 ? 0 : (double)triples.Count / subjects.Count;

        return new GraphStatistics(
            triples.Count,
            subjects.Count,
            predicates.Count,
            objects.Count,
            minOutDegree,
            maxOutDegree,
            meanOutDegree,
            maxPredicateFrequency);
    }
}
