namespace Lumoin.Veritas.Core.Hypertrie.AccessControl;

/// <summary>
/// The context an <see cref="AccessControl"/> policy needs to
/// reach a decision: caller identity, purpose, audit
/// correlation, capability tokens, agent attestations, and
/// whatever else the policy depends on. Defined here as an
/// empty abstract record so consumers can derive concrete
/// types carrying exactly the content their policy needs;
/// the library never inspects the context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why empty.</b> The PIC framing the access-control design
/// follows treats the context as opaque to the channel — the
/// channel passes it through to the policy and the policy
/// owns its content. Codifying any specific shape here would
/// either constrain consumers (forcing them to fit their
/// content into our chosen fields) or push them toward a
/// dictionary-of-objects bag, which the project's style guide
/// explicitly opposes. An empty type lets each consumer
/// declare a record carrying exactly the typed fields its
/// policy depends on.
/// </para>
/// <para>
/// <b>How to use.</b> Each application defines a concrete
/// record inheriting <see cref="AccessContext"/>:
/// <code>
/// public sealed record FleetAuditContext(
///     string AgentId,
///     string Purpose,
///     Guid AuditCorrelationId)
///     : AccessContext;
/// </code>
/// The <see cref="AccessControl"/> delegate receives the
/// abstract <see cref="AccessContext"/> reference and casts
/// (or pattern-matches) to the expected concrete type.
/// </para>
/// <para>
/// <b>No instances from the library.</b> The library never
/// constructs an <see cref="AccessContext"/> directly because
/// it has no fields to populate. The driver in a later batch
/// will accept a context instance from the caller and pass it
/// through unchanged.
/// </para>
/// </remarks>
public abstract record AccessContext;
