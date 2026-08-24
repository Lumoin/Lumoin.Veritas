using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The built-in STRUCTURAL reconciliation item domain: a triple's three 32-bit term identifiers pack
/// losslessly into the two 64-bit words of a <see cref="ContentKey128"/> — subject and predicate into
/// the low word (subject in the low 32 bits, predicate in the high 32), object into the low 32 bits of
/// the high word (its top 32 bits stay zero). The projection is therefore pure, injective, AND
/// invertible with no side map, so a recovered item recovers its exact triple.
/// </summary>
/// <remarks>
/// <para>
/// <b>The frozen item-key encoding.</b> This packing is the canonical reconciliation and
/// system-of-record item identity, and it is frozen: it holds only while term identifiers fit in 32
/// bits, an item is a triple (not a quad / named-graph member), and both replicas share one dictionary
/// epoch. A wider dictionary or a quad breaks the lossless pack and forces a content-hash projection —
/// not invertible, needing a side map from hash back to triple — which is a different item domain
/// selected behind a required-feature flag, never a silent change to this one.
/// </para>
/// <para>
/// <b>Epoch-local and single-node.</b> Two replicas produce equal items for equal triples only when
/// they encode terms against the same dictionary epoch; the structural domain reconciles replicas of
/// one epoch. A cross-fleet content-hash domain (stable across independently-built dictionaries) is the
/// replication arc's concern, plugged in through the same <see cref="ProjectReconciliationItemDelegate"/>
/// seam.
/// </para>
/// </remarks>
public static class StructuralReconciliationProjection
{
    /// <summary>The structural projection as an injectable delegate — the built-in default for the reconciliation and system-of-record item domain.</summary>
    public static ProjectReconciliationItemDelegate Projection { get; } = Project;

    /// <summary>The structural inverse as an injectable delegate — recovers a triple from its packed item.</summary>
    public static ReconciliationItemInverseDelegate Inversion { get; } = Invert;

    /// <summary>Projects a triple to its packed content key: subject in bits 0..31 and predicate in bits 32..63 of the low word, object in bits 0..31 of the high word.</summary>
    /// <param name="triple">The triple to project.</param>
    /// <returns>The packed content key.</returns>
    public static ContentKey128 Project(EncodedTriple triple)
    {
        ulong low = triple.Subject.Encoded | ((ulong)triple.Predicate.Encoded << 32);
        ulong high = triple.Object.Encoded;

        return new ContentKey128(low, high);
    }

    /// <summary>Recovers the triple a packed content key was projected from.</summary>
    /// <param name="item">The packed content key.</param>
    /// <returns>The triple the key was projected from.</returns>
    public static EncodedTriple Invert(ContentKey128 item)
    {
        return EncodedTriple.FromEncoded((uint)item.Low, (uint)(item.Low >> 32), (uint)item.High);
    }
}
