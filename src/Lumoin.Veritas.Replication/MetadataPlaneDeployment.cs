using System;
using System.Collections.Immutable;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The genesis of a deployment's metadata chain: the founding replicas in the order the chain is minted from,
/// and the genesis configuration minted from them. It is the composition root's value — a host builds one, and
/// every replica of the deployment builds an equal one — so the chain identity is a function of an agreed
/// member list rather than of whatever each host happened to configure.
/// </summary>
/// <remarks>
/// <para>
/// GENESIS CANNOT BE AGREED, because agreement is what genesis bootstraps. What the minted chain identity buys
/// is that disagreement fails closed instead of forking: two hosts built from different founder lists — or from
/// the same founders in different orders — mint different chains, decline each other's records, and lose
/// progress rather than agreement.
/// </para>
/// <para>
/// IDENTITY MAPPING. A member's consensus identity IS its replica identity axis: both are exactly 32 bytes, so
/// <see cref="ReplicaIdFor(ReplicaAxis)"/> converts allocation-free through a span, while
/// <see cref="AxisFor(ReplicaId)"/> pays one 32-byte array per surfaced identity. The asymmetry is named here
/// rather than hidden: an axis holds its bytes as memory and an inline identity has none to hand out, and the
/// cost is paid at control-plane write rates.
/// </para>
/// </remarks>
public sealed class MetadataPlaneDeployment
{
    /// <summary>Creates a deployment over validated founders, minting the genesis configuration from them.</summary>
    /// <param name="founders">The founders, already validated and in the order the chain is minted from.</param>
    private MetadataPlaneDeployment(ImmutableArray<MetadataFounder> founders)
    {
        Founders = founders;

        ImmutableArray<HostId>.Builder members = ImmutableArray.CreateBuilder<HostId>(founders.Length);
        for(int i = 0; i < founders.Length; i++)
        {
            members.Add(founders[i].ToHostId());
        }

        //Minting and listing are one call in the consensus library, so a host cannot mint an identity from one
        //list and then run under another.
        Genesis = QuePaxaConfiguration.CreateGenesis(members.MoveToImmutable());
    }

    /// <summary>The founding members, in genesis order: each one a replica identity axis beside the store admitted to answer for it.</summary>
    public ImmutableArray<MetadataFounder> Founders { get; }

    /// <summary>The genesis configuration the chain runs its first instance under: the minted chain identity and the founders as consensus identities, in the same order.</summary>
    public QuePaxaConfiguration Genesis { get; }

    /// <summary>The chain identity minted from the founder list — the deployment's chain name, surfaced in plane diagnostics so an operator can tell two chains apart by value.</summary>
    public ClusterId Cluster => Genesis.Cluster;

    /// <summary>The founder that leads the chain's first instance: the first member of <see cref="Founders"/>.</summary>
    public MetadataFounder BootstrapLeader => Founders[0];

    /// <summary>
    /// Creates a deployment from founders in an EXPLICIT order. The order is load-bearing twice over: the first
    /// member is the bootstrap leader, and the chain identity is a digest over the member list in order, so two
    /// hosts listing the same founders in different orders mint different chains and decline each other rather
    /// than agreeing on a chain while splitting its leader. Use this when the operator means to place the
    /// leader; use <see cref="CreateCanonical(ImmutableArray{ReplicaAxis})"/> when the operators agree on a set
    /// only.
    /// </summary>
    /// <param name="orderedFounders">The founding members, in the order the chain is minted from; at least one, all well-formed, no axis repeated.</param>
    /// <returns>The deployment.</returns>
    /// <exception cref="ArgumentException"><paramref name="orderedFounders"/> is default or empty, carries a default axis, or lists the same axis twice.</exception>
    public static MetadataPlaneDeployment Create(ImmutableArray<MetadataFounder> orderedFounders)
    {
        Validate(orderedFounders, nameof(orderedFounders));

        return new MetadataPlaneDeployment(orderedFounders);
    }

    /// <summary>
    /// Creates a deployment from founders in CANONICAL order: the list is sorted by lexicographic byte order
    /// before the chain is minted, so operators who agree on a SET of founders — in any order — mint one chain.
    /// The sort also fixes the bootstrap leader, which is then the byte-smallest founder rather than an operator's
    /// choice; <see cref="Create(ImmutableArray{ReplicaAxis})"/> is the factory that keeps that lever.
    /// </summary>
    /// <param name="founders">The founding members, in any order; at least one, all well-formed, no axis repeated.</param>
    /// <returns>The deployment.</returns>
    /// <exception cref="ArgumentException"><paramref name="founders"/> is default or empty, carries a default axis, or lists the same axis twice.</exception>
    public static MetadataPlaneDeployment CreateCanonical(ImmutableArray<MetadataFounder> founders)
    {
        Validate(founders, nameof(founders));

        return new MetadataPlaneDeployment(founders.Sort(CompareFounders));
    }

    /// <summary>
    /// The consensus identity of a replica identity axis: the same 32 bytes, read through a span, so the
    /// conversion allocates nothing.
    /// </summary>
    /// <param name="axis">The replica identity axis.</param>
    /// <returns>The consensus identity naming the same replica.</returns>
    /// <exception cref="ArgumentException"><paramref name="axis"/> is the default axis, which carries no bytes.</exception>
    public static ReplicaId ReplicaIdFor(ReplicaAxis axis)
    {
        return ReplicaId.FromSpan(axis.Bytes.Span);
    }

    /// <summary>
    /// The replica identity axis of a consensus identity: the same 32 bytes. This direction ALLOCATES one
    /// 32-byte array per surfaced identity, because an axis holds its bytes as memory while a consensus
    /// identity stores them inline and has no memory to hand out. It is a control-plane cost, paid per surfaced
    /// identity and never per data-lane event.
    /// </summary>
    /// <param name="replica">The consensus identity.</param>
    /// <returns>The replica identity axis naming the same replica.</returns>
    public static ReplicaAxis AxisFor(ReplicaId replica)
    {
        return new ReplicaAxis(replica.ToArray());
    }

    /// <summary>
    /// Compares two founders by the lexicographic byte order of their axes — the total order the canonical
    /// founder list is sorted by. The store is not compared, and must not be: canonical order exists so that
    /// operators agreeing on a set of replicas mint one chain, and ordering by a value each store minted for
    /// itself would make the order depend on which stores happened to be created.
    /// </summary>
    /// <param name="left">The left founder.</param>
    /// <param name="right">The right founder.</param>
    /// <returns>A negative value, zero, or a positive value per the standard comparison contract.</returns>
    private static int CompareFounders(MetadataFounder left, MetadataFounder right)
    {
        return left.Axis.Bytes.Span.SequenceCompareTo(right.Axis.Bytes.Span);
    }

    /// <summary>Validates a founder list: non-empty, every axis well-formed, no axis listed twice.</summary>
    /// <param name="founders">The founder list to validate.</param>
    /// <param name="parameterName">The name of the caller's parameter the exception names.</param>
    /// <exception cref="ArgumentException"><paramref name="founders"/> is default or empty, carries a default axis, or lists the same axis twice.</exception>
    private static void Validate(ImmutableArray<MetadataFounder> founders, string parameterName)
    {
        if(founders.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A metadata-plane deployment is founded by at least one replica; a chain with no members can neither decide nor be reconfigured into existence.", parameterName);
        }

        for(int i = 0; i < founders.Length; i++)
        {
            if(founders[i].Axis.Bytes.Length != ReplicaAxis.ByteWidth)
            {
                throw new ArgumentException("A metadata-plane founder carries a well-formed replica identity axis; the default axis carries no bytes and names no replica.", parameterName);
            }

            for(int j = i + 1; j < founders.Length; j++)
            {
                //The duplicate refusal is quorum injectivity and not hygiene: a replica listed twice would
                //answer twice and count twice, and a decision would be taken by fewer replicas than the
                //arithmetic claims. It is refused over the AXIS and not over the pair, because two stores
                //listed under one axis is exactly the duplicate that arithmetic cannot see: replacing a
                //member's store retires one member and admits another rather than listing both.
                if(founders[i].Axis.Equals(founders[j].Axis))
                {
                    throw new ArgumentException("A metadata-plane deployment cannot list the same founder twice, under one store or under two.", parameterName);
                }
            }
        }
    }
}
