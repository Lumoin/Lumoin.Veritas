using System.Threading;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Offers one decided metadata record to a host, completing when that host has learned it DURABLY. It is the
/// dissemination seam of the metadata plane, and one shape serves both of its ends: the sending end offers the
/// record to a member over the metadata channel, and the receiving end offers it to the local consensus host.
/// </summary>
/// <param name="committed">The decided record to offer.</param>
/// <param name="cancellationToken">Cancels the offer.</param>
/// <returns>A task that completes when the record has been offered and the receiving host has learned it durably.</returns>
/// <remarks>
/// <para>
/// DURABILITY IS PART OF THE CONTRACT AND NOT A PARAMETER. The plane's records are control-plane facts a host
/// builds its next write on, and a host that adopted one in memory and crashed would come back serving under
/// facts a peer has already moved past — so the seam takes no durability knob and every implementation learns
/// durably before it completes.
/// </para>
/// <para>
/// Completion marks the offer rather than agreement. Whether a quorum has learned a version is a readiness
/// question the register answers, and the confirmation that matters is the NEXT consensus write itself; no
/// irreversible act rides on this call's completion alone.
/// </para>
/// <para>
/// Faulting does not fail a decided write. The register awaits dissemination after the decision is taken and
/// returns the decided outcome whatever happens here, so an unreachable or refusing member slows the cluster
/// rather than endangering it — a caller told its committed write failed would retry a write that had already
/// landed.
/// </para>
/// </remarks>
public delegate ValueTask OfferMetadataRecordDelegate(VersionedValue<VeritasMetadataRecord> committed, CancellationToken cancellationToken);
