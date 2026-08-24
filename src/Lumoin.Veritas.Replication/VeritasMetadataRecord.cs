using System;
using System.Collections.Immutable;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The one coordinated metadata value a deployment evolves on its consensus chain: every replica identity
/// claim taken so far, the lineage baseline, the coordination policy, and the coordinator lease. One record,
/// one chain, one registry — every obligation the plane coordinates is a whole-record proposal, because the
/// consensus procedure decides among whole proposals and never composes two of them.
/// </summary>
/// <param name="IdentityClaims">The identity claims taken so far, in claim order; never default.</param>
/// <param name="Baseline">The lineage baseline, or <see langword="null"/> while no baseline is recorded.</param>
/// <param name="Policy">The coordination facts every host reads identically; never <see langword="null"/>.</param>
/// <param name="Coordinator">The coordinator lease, or <see langword="null"/> while the lease is vacant.</param>
/// <remarks>
/// <para>
/// WHY EQUALITY IS HAND-WRITTEN. The record is the value a consensus register carries, and a recorder compares
/// a carried proposal against the one it already holds. That comparison routes the value through
/// <see cref="System.Collections.Generic.EqualityComparer{T}.Default"/>, so a value whose equality is
/// reference-shaped fails the comparison — and fails it only AFTER a codec round trip, where the proposer's
/// object and the recorder's are no longer one instance. <see cref="ImmutableArray{T}"/> has exactly that
/// defect: its default equality compares the identity of the backing array, so a record decoded from bytes
/// would be unequal to the record that was encoded, and the register would never decide while reporting every
/// one of this host's own writes superseded. A synthesized equality on this record would inherit the defect,
/// invisibly in any bench where both sides share one array instance. The equality below therefore reads the
/// claims ELEMENT-WISE and the remaining members field-wise, and every member type carries content equality of
/// its own so the whole comparison survives the round trip.
/// </para>
/// <para>
/// The claim comparison is ORDER-SENSITIVE. Claims are appended and never rewritten, so the list is the claim
/// history and two records listing the same axes in different orders are different records: a losing writer
/// recomputes its whole proposal against the winner, which reproduces the winner's order, so an order-sensitive
/// comparison costs nothing and an order-independent one would call two different histories equal.
/// </para>
/// <para>
/// Every mutation recomputes its intent against the CURRENT committed record, so a superseded write recomposes
/// on the winner rather than replaying a proposal built from a record that lost. <see cref="Initial"/> is the
/// deterministic bootstrap value every founder proposes.
/// </para>
/// </remarks>
public sealed record VeritasMetadataRecord(
    ImmutableArray<ReplicaIdentityClaim> IdentityClaims,
    LineageBaseline? Baseline,
    CoordinationPolicy Policy,
    CoordinatorLease? Coordinator)
{
    /// <summary>
    /// The identity claims taken so far, in claim order. It is validated on construction and on a <c>with</c>
    /// expression alike, because the initializer writes the backing field directly and no accessor runs for it
    /// — and the write discipline builds every proposal with a <c>with</c> expression.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if the array is default.</exception>
    public ImmutableArray<ReplicaIdentityClaim> IdentityClaims { get; init { field = ValidateIdentityClaims(value); } } = ValidateIdentityClaims(IdentityClaims);

    /// <summary>
    /// The coordination facts every host of the deployment reads identically. It is validated on construction
    /// and on a <c>with</c> expression alike, for the same reason <see cref="IdentityClaims"/> is.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown if the policy is <see langword="null"/>.</exception>
    public CoordinationPolicy Policy { get; init { field = ValidatePolicy(value); } } = ValidatePolicy(Policy);

    /// <summary>
    /// The deterministic record a plane bootstraps with: no claims, no baseline, the default policy, and a
    /// vacant lease. Every founder computes the same value, so founders racing to bootstrap propose one
    /// identical record and the race resolves without anyone's state being lost — a founder that observes the
    /// initial record already committed reports that it was already bootstrapped.
    /// </summary>
    public static VeritasMetadataRecord Initial { get; } = new(
        IdentityClaims: ImmutableArray<ReplicaIdentityClaim>.Empty,
        Baseline: null,
        Policy: CoordinationPolicy.Default,
        Coordinator: null);

    /// <summary>
    /// Determines whether <paramref name="other"/> carries the same claims in the same order and the same
    /// baseline, policy, and lease.
    /// </summary>
    /// <param name="other">The record to compare with.</param>
    /// <returns><see langword="true"/> when both records carry the same coordinated facts.</returns>
    /// <remarks>
    /// Element-wise over the claims and field-wise over the rest, reading each member's content rather than any
    /// backing object's identity, so a record that crossed a codec equals the record that was encoded. The type
    /// remarks state why a synthesized equality cannot be used here.
    /// </remarks>
    public bool Equals(VeritasMetadataRecord? other)
    {
        if(other is null)
        {
            return false;
        }

        if(ReferenceEquals(this, other))
        {
            return true;
        }

        //Neither array is default: construction and every with-expression refuse one, so the length reads here
        //cannot throw.
        if(IdentityClaims.Length != other.IdentityClaims.Length)
        {
            return false;
        }

        for(int i = 0; i < IdentityClaims.Length; i++)
        {
            if(!IdentityClaims[i].Equals(other.IdentityClaims[i]))
            {
                return false;
            }
        }

        if(Baseline != other.Baseline)
        {
            return false;
        }

        if(Coordinator != other.Coordinator)
        {
            return false;
        }

        return Policy.Equals(other.Policy);
    }

    /// <summary>Gets the hash code over the record's content.</summary>
    /// <returns>The hash code.</returns>
    /// <remarks>
    /// Order-sensitive over the claims and derived from every member's content, not from the claim array's
    /// backing object, so two records that compare equal hash equally and a hash-keyed collection finds one
    /// where the other was stored.
    /// </remarks>
    public override int GetHashCode()
    {
        HashCode hash = new();
        for(int i = 0; i < IdentityClaims.Length; i++)
        {
            hash.Add(IdentityClaims[i]);
        }

        hash.Add(Baseline);
        hash.Add(Policy);
        hash.Add(Coordinator);

        return hash.ToHashCode();
    }

    /// <summary>Validates a claim list: an empty list is no claims, but a default array is no list at all.</summary>
    /// <param name="value">The claim list to validate.</param>
    /// <returns>The validated claim list.</returns>
    /// <exception cref="ArgumentException">Thrown if the array is default.</exception>
    private static ImmutableArray<ReplicaIdentityClaim> ValidateIdentityClaims(ImmutableArray<ReplicaIdentityClaim> value)
    {
        if(value.IsDefault)
        {
            throw new ArgumentException($"A metadata record carries a claim list ({nameof(IdentityClaims)}); the default array is no list, and the empty list is how a record carries no claims.");
        }

        return value;
    }

    /// <summary>Validates a coordination policy: a record naming no policy names no agreed facts.</summary>
    /// <param name="value">The policy to validate.</param>
    /// <returns>The validated policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the policy is <see langword="null"/>.</exception>
    private static CoordinationPolicy ValidatePolicy(CoordinationPolicy value)
    {
        //The exception must name the public property, not the validator's parameter.
        ArgumentNullException.ThrowIfNull(value, nameof(Policy));

        return value;
    }
}
