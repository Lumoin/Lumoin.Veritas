using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// A module of axioms the in-engine calculi cannot soundly answer — the
/// SROIQ(D)-hard fragment a reasoning rendezvous extracts and hands to an
/// external description-logic reasoner.
/// </summary>
/// <param name="Axioms">The module's axioms, origin-anchored for reporting.</param>
/// <param name="Violations">The profile findings that put each axiom beyond the in-engine ceiling.</param>
[DebuggerDisplay("ReasoningModule Axioms={Axioms.Count}")]
public sealed record ReasoningModule(
    IReadOnlyList<OwlAxiom> Axioms,
    IReadOnlyList<Profiles.OwlProfileViolation> Violations);

/// <summary>
/// A reasoner's verdict over a module: consistency and any module-local
/// subsumptions, which fold back into the closures the planner reads.
/// </summary>
/// <param name="IsConsistent">Whether the module is consistent — relative to the deciding calculus when <see cref="UnsupportedConstructs"/> is non-empty.</param>
/// <param name="Subsumptions">Module-local subsumptions between named classes, as (subclass, superclass) pairs.</param>
[DebuggerDisplay("ModuleVerdict Consistent={IsConsistent} Subsumptions={Subsumptions.Count}")]
public sealed record ModuleVerdict(
    bool IsConsistent,
    IReadOnlyList<(NamedNode SubClass, NamedNode SuperClass)> Subsumptions)
{
    /// <summary>
    /// The module axioms beyond the deciding calculus, named — never
    /// silently dropped. A non-empty list scopes
    /// <see cref="IsConsistent"/> to the supported fragment: an
    /// inconsistency found there condemns the whole module, while a
    /// consistency claim says nothing about the unsupported remainder.
    /// </summary>
    public IReadOnlyList<string> UnsupportedConstructs { get; init; } = [];
}

/// <summary>
/// The seam an external SROIQ(D) reasoner plugs into: the engine builds no
/// tableau itself — it extracts modules and delegates. The default is no
/// delegate, in which case modules requiring one are reported, never
/// silently dropped. Wiring a concrete reasoner behind this delegate is the
/// library user's integration choice, never a Veritas dependency.
/// </summary>
/// <param name="module">The SROIQ(D)-hard module.</param>
/// <param name="cancellationToken">A token that aborts external reasoning.</param>
/// <returns>The module verdict.</returns>
public delegate ValueTask<ModuleVerdict> DescriptionLogicDelegate(
    ReasoningModule module,
    CancellationToken cancellationToken);
