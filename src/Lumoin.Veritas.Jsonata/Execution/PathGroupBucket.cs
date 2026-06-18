using System.Collections.Generic;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// One bucket of a path-end group-by over a tuple stream (the reference's <c>evaluateGroupExpression</c>
/// reduce branch): every tuple whose member key evaluated to this string key, recorded together with the
/// member pair index that produced the key so a same-pair collision can append while a different-pair
/// collision is the D1009 error. Unlike the focus-only <see cref="GroupByBucket"/>, this bucket keeps the
/// whole tuples so the value phase can merge their bindings (<c>reduceTupleStream</c>) and evaluate the value
/// under the merged tuple's frame.
/// </summary>
/// <remarks>
/// Buckets are held in first-seen key order so the constructed object preserves that ordering. See
/// <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.
/// </remarks>
internal sealed class PathGroupBucket
{
    /// <summary>Initialises a bucket for a string key first produced by a given member pair.</summary>
    /// <param name="key">The string key the member's key expression evaluated to.</param>
    /// <param name="pairIndex">The index of the member pair that first produced this key.</param>
    public PathGroupBucket(string key, int pairIndex)
    {
        Key = key;
        PairIndex = pairIndex;
        Tuples = [];
    }

    /// <summary>Gets the string key this bucket groups tuples under.</summary>
    public string Key { get; }

    /// <summary>Gets the index of the member pair that first produced this key (the pair whose value expression evaluates the group).</summary>
    public int PairIndex { get; }

    /// <summary>Gets the tuples grouped under this key, in first-seen order; the value phase merges them via <c>reduceTupleStream</c>.</summary>
    public List<PathTuple> Tuples { get; }
}
