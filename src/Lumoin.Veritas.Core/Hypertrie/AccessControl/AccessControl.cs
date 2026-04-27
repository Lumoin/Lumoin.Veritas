using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Hypertrie.AccessControl;

/// <summary>
/// Consults an access-control policy for a candidate triple.
/// Returns the decision the policy reaches —
/// <see cref="AccessDecision.Allow"/>,
/// <see cref="AccessDecision.Deny"/>, or
/// <see cref="AccessDecision.NotFound"/>.
/// </summary>
/// <remarks>
/// <para>
/// The type is named <c>AccessControlDelegate</c> rather than the
/// shorter <c>AccessControl</c> because the containing namespace is
/// also <c>AccessControl</c>. C# resolves an unqualified
/// <c>AccessControl</c> in a consumer to the namespace before any
/// type with the same name, so a delegate named <c>AccessControl</c>
/// would be unusable from any file that says
/// <c>using Lumoin.Veritas.Core.Hypertrie.AccessControl;</c>.
/// The <c>Delegate</c> suffix is consistent with the .NET naming
/// convention for standalone delegate types where the natural noun
/// would clash (compare <c>EventHandler</c>,
/// <c>WaitCallback</c>).
/// </para>
/// <para>
/// <b>Where consulted.</b> The driver consults this delegate at
/// descent leaf — the point where a complete candidate triple has
/// been pinned by the iterators but before it is yielded as a
/// solution. Pre-decision consultation (rather than a post-filter
/// wrapping the storage delegate) is essential to performance under
/// access control: a denied triple does not pay the cost of being
/// produced as a candidate by every iterator and then thrown away.
/// </para>
/// <para>
/// <b>Async by default.</b> Most policies will be synchronous
/// (in-memory rule evaluation) and return a completed
/// <see cref="ValueTask{TResult}"/>; the
/// <see cref="ValueTask{TResult}"/> shape allows policies that need
/// to consult external systems — capability servers, revocation
/// lists, agent-identity verifiers — without pessimising the
/// synchronous case.
/// </para>
/// <para>
/// <b>Cancellation.</b> The <paramref name="cancellationToken"/> is
/// the same token threaded through query execution. Synchronous
/// policies typically ignore it; remote-consulting policies should
/// honour it.
/// </para>
/// <para>
/// <b>Default behaviour.</b> A <c>null</c> delegate, where the
/// driver accepts one, is treated as
/// <see cref="AccessDecision.Allow"/> for every candidate. The hot
/// path checks for <c>null</c> and skips the entire consultation in
/// that case, so unconfigured queries pay zero access-control cost.
/// </para>
/// </remarks>
/// <param name="request">The candidate triple and caller-supplied context.</param>
/// <param name="cancellationToken">Cancellation token threaded from query execution.</param>
/// <returns>The decision the policy reached for this candidate.</returns>
public delegate ValueTask<AccessDecision> AccessControlDelegate(
    AccessRequest request,
    CancellationToken cancellationToken);
