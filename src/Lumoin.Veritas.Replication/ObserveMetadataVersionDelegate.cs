using System.Threading;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Asks ONE host of the metadata plane which committed version it holds, and is answered with that version
/// beside the identity the ANSWERING host asserts for itself. It is the per-member leg a readiness report is
/// assembled from, and one shape serves both of its ends: the sending end asks a member over the metadata
/// channel, and the receiving end asks the local consensus host.
/// </summary>
/// <param name="cancellationToken">Cancels the probe.</param>
/// <returns>The answering host's report: the highest committed version it holds, or
/// <see cref="RegisterVersion.Unwritten"/> when it has learned none, beside the identity it asserts for
/// itself.</returns>
/// <remarks>
/// <para>
/// THE IDENTITY COMES FROM THE ANSWERING SIDE AND IS NEVER ECHOED BACK FROM THE ASK. A report is counted over
/// distinct members of a membership reached through an endpoint map a deployment wires by hand, and two of its
/// entries pointing at one host would let that host fill two slots and clear a decommission gate on fewer
/// distinct replicas than the report claims. The register refuses an answer naming a member other than the one
/// it asked, so an implementation that labelled the answer with the member the caller aimed at would defeat
/// exactly the mis-wiring check the refusal exists for. The identity is the answering host's own claim and is
/// not authentication: it is exact under crash faults and worthless against a host that lies.
/// </para>
/// <para>
/// A HOST THAT CANNOT BE REACHED FAULTS RATHER THAN ANSWERING UNWRITTEN. A host that has learned nothing and a
/// host nothing reaches are the two situations a readiness report exists to separate, and an implementation
/// that reported the second as a version of zero would clear a decommission against a silent cluster. The
/// register turns the fault into the unreachable entry.
/// </para>
/// <para>
/// The version is read through the host's own durable path, the same seam a catch-up read is served from, so
/// what is reported is a version the host's store holds rather than one a crash could take back.
/// </para>
/// </remarks>
public delegate ValueTask<MemberVersionReport> ObserveMetadataVersionDelegate(CancellationToken cancellationToken);
