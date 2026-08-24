using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The per-node map from a content-hash reconciliation item back to the triple it was projected from — the recovery
/// path a non-invertible item domain needs. A <see cref="ContentHashReconciliationProjection"/> hash discards the
/// triple, so a recovered item names "a triple a replica holds" without revealing which; a node builds this map over
/// its own triples and uses it two ways: a serving node <see cref="TryResolve"/>s a requested key to the triple it
/// sends, and a reconciling node asks <see cref="Contains"/> of each recovered key to tell the items it already
/// holds (local-only differences, no action) from the items it lacks (peer-only differences, fetched from the peer
/// by key). It is the content-hash counterpart of the structural domain's invertible key.
/// </summary>
/// <remarks>
/// Building the map projects every local triple, so it costs one hash per triple — the price the content-hash
/// domain pays for cross-node identity that the structural domain avoids with an invertible key. A node rebuilds it
/// per reconcile from its current index (caching or incremental maintenance is a later optimization).
/// </remarks>
public sealed class ContentHashSideMap
{
    /// <summary>The content-key to triple map.</summary>
    private Dictionary<ContentKey128, EncodedTriple> Map { get; }

    /// <summary>Creates a side-map over a prebuilt content-key to triple map.</summary>
    /// <param name="map">The map this owns.</param>
    private ContentHashSideMap(Dictionary<ContentKey128, EncodedTriple> map)
    {
        Map = map;
    }

    /// <summary>The number of distinct items the map holds.</summary>
    public int Count => Map.Count;

    /// <summary>Builds a side-map over an index's triples, projecting each through the content-hash projection.</summary>
    /// <param name="index">The local index whose triples are mapped.</param>
    /// <param name="projection">The content-hash projection (a triple's items must match the keys reconciliation recovers).</param>
    /// <returns>The side-map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">A triple holds a term the content-hash projection does not project (a blank node or an RDF 1.2 triple term).</exception>
    public static ContentHashSideMap Build(ColumnarTripleIndex index, ProjectReconciliationItemDelegate projection)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(projection);

        Dictionary<ContentKey128, EncodedTriple> map = new(index.TripleCount);
        foreach(EncodedTriple triple in index.EnumerateTriples())
        {
            map[projection(triple)] = triple;
        }

        return new ContentHashSideMap(map);
    }

    /// <summary>Resolves a recovered item to the triple it was projected from, when this node holds it.</summary>
    /// <param name="item">The content-hash item.</param>
    /// <param name="triple">The triple this node holds for the item, when present.</param>
    /// <returns><see langword="true"/> when this node holds a triple for the item.</returns>
    public bool TryResolve(ContentKey128 item, out EncodedTriple triple)
    {
        return Map.TryGetValue(item, out triple);
    }

    /// <summary>Whether this node holds a triple for the item — used to tell a local-only recovered difference (held) from a peer-only one (lacked, to be fetched).</summary>
    /// <param name="item">The content-hash item.</param>
    /// <returns><see langword="true"/> when this node holds a triple for the item.</returns>
    public bool Contains(ContentKey128 item)
    {
        return Map.ContainsKey(item);
    }
}
