using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The verdict of one <see cref="GenerationCommitCoordinator"/> pass: whether a healed generation was
/// published, the generation it concerns, which derived-artifact roles it republished, the item losses it
/// carried through from the repair, and — when it did not commit — the repair refusal reason.
/// </summary>
public sealed class GenerationCommitReport
{
    /// <summary>Creates a generation-commit report.</summary>
    /// <param name="outcome">Whether and why a healed generation was published.</param>
    /// <param name="generation">The healed generation when committed, else the live generation the pass did not supersede.</param>
    /// <param name="republishedRoles">The derived-artifact roles republished in the healed generation; empty unless committed.</param>
    /// <param name="namedLosses">The item losses the repair named, carried through for the durability layer.</param>
    /// <param name="refusal">Why the repair declined when <paramref name="outcome"/> is <see cref="GenerationCommitOutcome.Refused"/>, else <see cref="RepairRefusalReason.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="republishedRoles"/> or <paramref name="namedLosses"/> is <see langword="null"/>.</exception>
    public GenerationCommitReport(GenerationCommitOutcome outcome, long generation, IReadOnlyList<ManifestFileRole> republishedRoles, IReadOnlyList<UnrecoverableItemReport> namedLosses, RepairRefusalReason refusal)
    {
        ArgumentNullException.ThrowIfNull(republishedRoles);
        ArgumentNullException.ThrowIfNull(namedLosses);

        Outcome = outcome;
        Generation = generation;
        RepublishedRoles = republishedRoles;
        NamedLosses = namedLosses;
        Refusal = refusal;
    }

    /// <summary>Whether and why a healed generation was published.</summary>
    public GenerationCommitOutcome Outcome { get; }

    /// <summary>The healed generation when committed, else the live generation the pass did not supersede.</summary>
    public long Generation { get; }

    /// <summary>The derived-artifact roles republished in the healed generation; empty unless committed.</summary>
    public IReadOnlyList<ManifestFileRole> RepublishedRoles { get; }

    /// <summary>The item losses the repair named, carried through for the durability layer; the coordinator publishes the healed generation regardless of them.</summary>
    public IReadOnlyList<UnrecoverableItemReport> NamedLosses { get; }

    /// <summary>Why the repair declined when <see cref="Outcome"/> is <see cref="GenerationCommitOutcome.Refused"/>, else <see cref="RepairRefusalReason.None"/>.</summary>
    public RepairRefusalReason Refusal { get; }

    /// <summary>Whether a healed generation was published.</summary>
    public bool Committed => Outcome == GenerationCommitOutcome.Committed;
}
