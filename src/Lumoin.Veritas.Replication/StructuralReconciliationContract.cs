using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The one structural reconciliation contract every structural-domain surface in this library binds: the
/// structural item domain, the 16-byte structural item, and the eight-byte well-known-keyed checksum. The
/// maintainer's encoder, the rateless codec's sessions, and the shard-difference channel all reconcile under
/// byte-identical contract values or their streams would not combine, so the value lives here once.
/// </summary>
internal static class StructuralReconciliationContract
{
    /// <summary>The shared contract value.</summary>
    internal static ReconciliationContract Value { get; } = new(
        ReconciliationItemDomain.Structural,
        ContentKey128.ByteWidth,
        8,
        ReconciliationContract.WellKnownChecksumKeyLow,
        ReconciliationContract.WellKnownChecksumKeyHigh);
}
