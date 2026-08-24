using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Profiles;

/// <summary>The OWL 2 profiles, combinable as flags.</summary>
[Flags]
public enum OwlProfiles
{
    /// <summary>No profile.</summary>
    None = 0,

    /// <summary>OWL 2 EL.</summary>
    El = 1,

    /// <summary>OWL 2 QL.</summary>
    Ql = 2,

    /// <summary>OWL 2 RL.</summary>
    Rl = 4,
}

/// <summary>
/// One profile violation: the profile excluded, the construct that excludes
/// it, and the origin triple of the violating axiom — the editor-surface
/// anchor.
/// </summary>
/// <param name="Profile">The profile the construct is outside of.</param>
/// <param name="Origin">The violating axiom's root triple, or <c>null</c> when the violation is the document as a whole (a structurally ill-formed graph).</param>
/// <param name="Construct">A short description of the excluding construct.</param>
[DebuggerDisplay("{Profile} violation: {Construct,nq}")]
public sealed record OwlProfileViolation(OwlProfiles Profile, Quad? Origin, string Construct);

/// <summary>
/// The result of checking an ontology document against the OWL 2 profiles:
/// per-profile membership and the violations that exclude each profile.
/// </summary>
[DebuggerDisplay("OwlProfileReport {Memberships}")]
public sealed class OwlProfileReport
{
    /// <summary>The profiles the document belongs to.</summary>
    public OwlProfiles Memberships { get; }

    /// <summary>Every violation found, across all profiles.</summary>
    public IReadOnlyList<OwlProfileViolation> Violations { get; }

    /// <summary>
    /// Initialises the report.
    /// </summary>
    /// <param name="memberships">The profiles the document belongs to.</param>
    /// <param name="violations">The violations found.</param>
    public OwlProfileReport(OwlProfiles memberships, IReadOnlyList<OwlProfileViolation> violations)
    {
        Memberships = memberships;
        Violations = violations;
    }

    /// <summary>Whether the document is in the given profile.</summary>
    /// <param name="profile">The profile to test.</param>
    /// <returns><see langword="true"/> when the document is inside the profile.</returns>
    public bool IsIn(OwlProfiles profile)
    {
        return (Memberships & profile) == profile;
    }
}
