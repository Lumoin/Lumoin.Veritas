using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Hypertrie.AccessControl;

/// <summary>
/// The decision an <see cref="AccessControl"/> policy returns
/// for a candidate triple.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three-valued, by design.</b> A policy may return
/// <see cref="Allow"/> to grant access, <see cref="Deny"/> to
/// refuse it (with audit visibility — the refusal is recorded
/// to the trace channel), or <see cref="NotFound"/> to refuse
/// it with no audit trace. The
/// <see cref="Deny"/> / <see cref="NotFound"/> distinction is
/// the heart of the privacy guarantee: from the requester's
/// perspective both produce the same observable effect (the
/// triple is omitted from results), but
/// <see cref="NotFound"/> produces no audit signal that would
/// let the requester infer the existence of the data through a
/// side channel.
/// </para>
/// <para>
/// Operators who want an audit trail of every refused access
/// configure their policy to return <see cref="Deny"/>.
/// Operators who need privacy-preserving refusals — where the
/// requester must not be able to distinguish "denied" from
/// "no such triple" — return <see cref="NotFound"/>.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "AccessDecision is returned from the access-control delegate on every candidate triple in a query, often through ValueTask. The byte underlying type keeps the boxed and ValueTask-wrapped representation compact at no expressive cost — three values fit trivially.")]
public enum AccessDecision: byte
{
    /// <summary>The candidate triple is visible to the requester.</summary>
    Allow = 0,

    /// <summary>The candidate triple is refused; the refusal is audited via the query trace stream.</summary>
    Deny = 1,

    /// <summary>The candidate triple is refused with no audit trace; observationally indistinguishable from a real miss.</summary>
    NotFound = 2,
}
