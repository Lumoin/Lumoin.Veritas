using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Confirms the lineage baseline on the deployment's coordinated metadata record AFTER the minting host's local
/// durable commit: the dataset StateId and the term-dictionary epoch, matched to the recorded intent by a
/// byte-identical causality digest. The host binds this to
/// <see cref="VeritasMetadataPlane.ConfirmBaselineAsync"/>; a crash between the commit and this confirm leaves
/// a visible unconfirmed intent, and the host's next open re-issues the confirm idempotently.
/// </summary>
/// <param name="causalityDigest">The digest of the minted baseline causality, which matches this confirm to its intent.</param>
/// <param name="stateId">The dataset StateId the committed baseline produced.</param>
/// <param name="dictionaryEpoch">The term-dictionary epoch the committed baseline was written under.</param>
/// <param name="cancellationToken">The token that cancels the consultation.</param>
/// <returns>
/// The confirm's value-based outcome. The local commit already happened when this runs, so no outcome refuses
/// anything here: <see cref="BaselineRecordOutcome.ConflictingLineage"/> marks the coordination status
/// CONTESTED for the operator, and <see cref="BaselineRecordOutcome.Undecided"/> leaves it pending for the next
/// open's retry.
/// </returns>
public delegate ValueTask<BaselineRecordOutcome> ConfirmLineageBaselineDelegate(NodeIdentifier causalityDigest, NodeIdentifier stateId, long dictionaryEpoch, CancellationToken cancellationToken);
