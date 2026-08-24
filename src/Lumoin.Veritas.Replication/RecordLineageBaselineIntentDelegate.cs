using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Records the lineage baseline's INTENT on the deployment's coordinated metadata record: the claimant axis and
/// the causality digest, written BEFORE the minting host's local durable commit, because the digest over the
/// minted commit causality is the only lineage identity that exists at that point. The engine computes the
/// digest through <see cref="LineageDigests"/> so every consultation digests identically; the host binds this
/// to <see cref="VeritasMetadataPlane.RecordBaselineIntentAsync"/>.
/// </summary>
/// <param name="claimantAxis">The identity axis the baseline dots are minted on.</param>
/// <param name="causalityDigest">The digest of the minted baseline causality, from <see cref="LineageDigests.DigestOf"/>.</param>
/// <param name="cancellationToken">The token that cancels the consultation.</param>
/// <returns>
/// The intent's value-based outcome. <see cref="BaselineRecordOutcome.Undecided"/> fails open — the open
/// proceeds with the intent pending; only the definite <see cref="BaselineRecordOutcome.ConflictingLineage"/>
/// refuses, because a second independent baseline for one lineage is the storm the intent write exists to close.
/// </returns>
public delegate ValueTask<BaselineRecordOutcome> RecordLineageBaselineIntentDelegate(ReplicaAxis claimantAxis, NodeIdentifier causalityDigest, CancellationToken cancellationToken);
