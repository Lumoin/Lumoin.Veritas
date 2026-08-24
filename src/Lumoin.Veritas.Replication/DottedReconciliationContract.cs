using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The one dotted reconciliation contract every remove-aware surface in this library binds: the content-hash
/// item domain (the dotted projection digests a variable-length frame, so the fixed-width structural domain is
/// out of scope by construction), the 16-byte dotted item, and the eight-byte well-known-keyed checksum. The
/// dotted channel's sessions and framing all reconcile under byte-identical contract values or their streams
/// would not combine, so the value lives here once, beside its structural sibling.
/// </summary>
internal static class DottedReconciliationContract
{
    /// <summary>The shared contract value.</summary>
    internal static ReconciliationContract Value { get; } = new(
        ReconciliationItemDomain.ContentHash,
        ContentKey128.ByteWidth,
        8,
        ReconciliationContract.WellKnownChecksumKeyLow,
        ReconciliationContract.WellKnownChecksumKeyHigh);
}
