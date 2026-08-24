using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Planning;

namespace Lumoin.Veritas.Owl.El;

/// <summary>
/// Builds the planner's a-priori cardinality statistics from an EL
/// classification and a store generation: subclass closure × per-class
/// asserted extent counts → a sound upper bound per class.
/// </summary>
/// <remarks>
/// <para>
/// <b>The arithmetic.</b> The entailed extent of a class is the union
/// of the asserted extents of its subsumees (an instance of a subclass
/// is an instance of every subsumer). The bound computed here is the
/// sum over <see cref="ElClassification.SubsumeesOf"/> of each
/// subsumee's asserted <c>rdf:type</c> count — at least as large as
/// the union, so the planner never works from an undercount.
/// </para>
/// <para>
/// <b>Coverage.</b> Every classified class present in the dictionary
/// receives a bound, zero included (a classified class with no
/// asserted instances anywhere below it can match nothing). A class
/// asserted in the data but absent from the TBox receives its asserted
/// count — exact, since no subclass structure feeds it. A class IRI
/// the dictionary has never seen carries no entry: no triple can
/// reference it, so no pattern constant can either.
/// </para>
/// <para>
/// <b>Generation binding.</b> The result describes exactly the store
/// generation it was built from; callers rebuild per commit the same
/// way the classification itself rebuilds.
/// </para>
/// </remarks>
public static class ElPlannerStatistics
{
    /// <summary>
    /// Computes the per-class upper bounds for one store generation.
    /// </summary>
    /// <param name="classification">The TBox classification supplying the subclass closure.</param>
    /// <param name="store">The store generation supplying asserted extent counts.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <returns>The statistics for <paramref name="store"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static AprioriCardinalities Build(
        ElClassification classification,
        HypertrieGraphStore store,
        TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        TermId typeId = dictionary.GetIdOrDefault(new NamedNode(Vocabulary.Rdf.Type));

        //One sweep over the asserted class-membership triples buckets
        //the per-class counts the closure sums below.
        Dictionary<TermId, long> assertedCounts = [];
        if(typeId != TermId.None)
        {
            foreach(EncodedTriple triple in store.Match(TermId.None, typeId, TermId.None))
            {
                assertedCounts[triple.Object] = assertedCounts.TryGetValue(triple.Object, out long count) ? count + 1 : 1;
            }
        }

        //Closure × counts: each classified class's bound is the sum of
        //its subsumees' asserted extents.
        Dictionary<TermId, long> bounds = [];
        foreach(Utf8String classIri in classification.Classes)
        {
            TermId classId = dictionary.GetIdOrDefault(new NamedNode(classIri));
            if(classId == TermId.None)
            {
                continue;
            }

            long bound = 0;
            foreach(Utf8String subsumee in classification.SubsumeesOf(classIri))
            {
                TermId subsumeeId = dictionary.GetIdOrDefault(new NamedNode(subsumee));
                if(subsumeeId != TermId.None && assertedCounts.TryGetValue(subsumeeId, out long count))
                {
                    bound += count;
                }
            }

            bounds[classId] = bound;
        }

        //A class asserted in the data the TBox never mentions: no
        //subclass structure feeds it, so its asserted extent is exact.
        foreach(KeyValuePair<TermId, long> asserted in assertedCounts)
        {
            if(!bounds.ContainsKey(asserted.Key))
            {
                bounds[asserted.Key] = asserted.Value;
            }
        }

        return new AprioriCardinalities(typeId, bounds);
    }
}
