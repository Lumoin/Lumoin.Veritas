namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// One named loss a <see cref="DurableLossRecord"/> persists: the durable face of an
/// <see cref="UnrecoverableItemReport"/> a repair could not recover, reduced to the fields that survive a
/// restart — its granularity, the role and store name of the artifact it belonged to, and the item range lost.
/// A whole-artifact loss carries no item range (<see cref="StartItem"/> is -1 and <see cref="ItemCount"/> is 0);
/// a default-graph item-set loss carries a range but no artifact name (the default graph is named implicitly by
/// its role).
/// </summary>
/// <param name="Kind">The granularity of the loss.</param>
/// <param name="RoleCode">The <see cref="Lumoin.Veritas.Core.Persistence.Manifest.ManifestFileRole"/> code of the lost artifact; 0 when not applicable.</param>
/// <param name="ArtifactFileName">The store name of the lost artifact, or <see langword="null"/> for the default graph's segment and for non-persistence losses.</param>
/// <param name="StartItem">The index of the first lost item for an item-set loss; -1 when not applicable.</param>
/// <param name="ItemCount">The number of contiguous lost items for an item-set loss; 0 when not applicable.</param>
public readonly record struct DurableLossEntry(UnrecoverableItemReportKind Kind, int RoleCode, string? ArtifactFileName, long StartItem, long ItemCount);
