namespace Lumoin.Veritas.Core.Hypertrie.AccessControl;

/// <summary>
/// One consultation of an <see cref="AccessControl"/> policy.
/// </summary>
/// <remarks>
/// <para>
/// The request carries the candidate triple and the
/// caller-supplied <see cref="AccessContext"/>. The policy
/// reaches a decision based on these and any external state it
/// has access to — capability servers, revocation lists, agent
/// attestations — and returns an <see cref="AccessDecision"/>.
/// </para>
/// <para>
/// The triple's positions are encoded
/// <see cref="EncodedTriple"/> values; the policy is
/// responsible for resolving them through whatever
/// <see cref="TermDictionary"/> or directory it consults if it
/// needs textual identifiers.
/// </para>
/// </remarks>
public readonly record struct AccessRequest(
    EncodedTriple Triple,
    AccessContext Context);
