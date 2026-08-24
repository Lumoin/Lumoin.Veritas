using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The coordination facts every host of one deployment must read identically: the small immutable set of
/// replication settings that would misbehave if two hosts held different values for them. It is one member of
/// the coordinated metadata record, so a host reads the agreed value rather than its own configuration file.
/// </summary>
/// <param name="HealCadenceClass">The agreed heal-cadence class; not negative.</param>
/// <param name="SymbolBudgetTier">The agreed symbol-budget tier; not negative.</param>
/// <remarks>
/// <para>
/// Both facts are ORDINAL CLASSES rather than raw durations or symbol counts. A class is a name the whole
/// deployment agrees on and each host maps to its own local schedule or budget shape, which keeps a duration —
/// a local scheduling concern, and the one thing a coordination plane must never embed — out of the agreed
/// value. Agreeing on the tier and not on the number also lets a host with a different item count derive a
/// budget its own sketch can serve while both sides still name one shape.
/// </para>
/// <para>
/// The set extends by AMENDMENT through the plane and never by a host-local addition: a new fact becomes a new
/// member here whose value in <see cref="Default"/> reproduces today's behavior, so a deployment that has not
/// amended its record reads the same behavior it read before the member existed, and a host running an older
/// build reads the members it knows. Only facts that must not drift per host belong here; anything a host may
/// legitimately tune for itself stays in that host's own options.
/// </para>
/// <para>
/// Equality is the synthesized record equality, and it is content-based without help: both members are
/// <see langword="int"/>, so a policy decoded from bytes equals the policy that was encoded. The containing
/// record's comparison depends on that, and the dependency is stated here because a later member of a
/// reference-shaped type would break it silently — see the equality remarks on
/// <see cref="VeritasMetadataRecord"/>.
/// </para>
/// </remarks>
public sealed record CoordinationPolicy(int HealCadenceClass, int SymbolBudgetTier)
{
    /// <summary>
    /// The agreed heal-cadence class: the class every host maps to its own anti-entropy heal schedule. It names
    /// how eagerly the deployment heals, never how often in time. It is validated on construction and on a
    /// <c>with</c> expression alike, because the initializer writes the backing field directly and no accessor
    /// runs for it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the class is negative.</exception>
    public int HealCadenceClass { get; init { field = ValidateHealCadenceClass(value); } } = ValidateHealCadenceClass(HealCadenceClass);

    /// <summary>
    /// The agreed symbol-budget tier: the tier every host maps to its own
    /// <see cref="ReplicationPolicy"/> budget shape. Two replicas whose budgets disagree produce sketches that
    /// do not combine into a complete peel, so the tier is one of the facts that must not drift per host. It is
    /// validated on construction and on a <c>with</c> expression alike, for the same reason
    /// <see cref="HealCadenceClass"/> is.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the tier is negative.</exception>
    public int SymbolBudgetTier { get; init { field = ValidateSymbolBudgetTier(value); } } = ValidateSymbolBudgetTier(SymbolBudgetTier);

    /// <summary>
    /// The policy a plane bootstraps with: the base class and the base tier, both zero. Every host maps the
    /// base ordinals to the defaults it would have used without a plane, so bootstrapping a deployment changes
    /// nothing about local behavior until an amendment names another class or tier. It is also what makes the
    /// initial record deterministic — every founder proposing the bootstrap composes the same value.
    /// </summary>
    public static CoordinationPolicy Default { get; } = new(HealCadenceClass: 0, SymbolBudgetTier: 0);

    /// <summary>Validates a heal-cadence class: an ordinal names a class, and no class is below the base one.</summary>
    /// <param name="value">The class to validate.</param>
    /// <returns>The validated class.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the class is negative.</exception>
    private static int ValidateHealCadenceClass(int value)
    {
        //The exception must name the public property, not the validator's parameter.
        ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(HealCadenceClass));

        return value;
    }

    /// <summary>Validates a symbol-budget tier: an ordinal names a tier, and no tier is below the base one.</summary>
    /// <param name="value">The tier to validate.</param>
    /// <returns>The validated tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the tier is negative.</exception>
    private static int ValidateSymbolBudgetTier(int value)
    {
        //The exception must name the public property, not the validator's parameter.
        ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(SymbolBudgetTier));

        return value;
    }
}
