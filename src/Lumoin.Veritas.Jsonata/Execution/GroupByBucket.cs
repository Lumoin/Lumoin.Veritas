using System.Collections.Generic;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Execution;

/// <summary>
/// One bucket of the object constructor's group-by: every focus item whose member key evaluated to this
/// string key, recorded together with the member pair index that produced the key so a same-pair collision
/// can append while a different-pair collision is the D1009 error.
/// </summary>
/// <remarks>
/// Buckets are held in first-seen key order so the constructed object preserves that ordering; the grouped
/// items feed the value expression's rebound focus once bucketing is complete.
/// </remarks>
internal sealed class GroupByBucket
{
    /// <summary>Initialises a bucket for a string key first produced by a given member pair.</summary>
    /// <param name="key">The string key the member's key expression evaluated to.</param>
    /// <param name="pairIndex">The index of the member pair that first produced this key.</param>
    public GroupByBucket(string key, int pairIndex)
    {
        Key = key;
        PairIndex = pairIndex;
        Items = [];
    }

    /// <summary>Gets the string key this bucket groups items under.</summary>
    public string Key { get; }

    /// <summary>Gets the index of the member pair that first produced this key (the pair whose value expression evaluates the group).</summary>
    public int PairIndex { get; }

    /// <summary>Gets the focus items grouped under this key, in first-seen order.</summary>
    public List<JsonataValue> Items { get; }
}
