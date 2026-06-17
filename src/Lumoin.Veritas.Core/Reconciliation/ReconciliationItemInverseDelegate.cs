using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// Recovers the triple a reconciliation item was projected from — the inverse of a
/// <see cref="ProjectReconciliationItemDelegate"/>. It exists only for an INVERTIBLE item domain: one
/// whose projection loses no information, so the triple is recovered from the item alone with no side
/// map. A content-hash item domain is not invertible (the hash discards the triple) and recovers the
/// triple another way; such a domain supplies no inverse. The recovered triple is what the repair path
/// re-applies through the ordinary ingest seam.
/// </summary>
/// <param name="item">The reconciliation item.</param>
/// <returns>The triple the item was projected from.</returns>
public delegate EncodedTriple ReconciliationItemInverseDelegate(ContentKey128 item);
