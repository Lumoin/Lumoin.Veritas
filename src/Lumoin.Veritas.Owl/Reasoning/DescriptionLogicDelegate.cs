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
/// external description-logic reasoner. The module is the axiom set the caller
/// intends to be reasoned over, closed as given: resolving an
/// <c>owl:imports</c> closure is the caller's obligation, discharged before
/// construction, so an <see cref="OwlImportAxiom"/> row is a non-logical
/// marker and every verdict is relative to the module handed in.
/// </summary>
/// <param name="Axioms">The module's axioms, origin-anchored for reporting.</param>
/// <param name="Violations">The profile findings that put each axiom beyond the in-engine ceiling.</param>
[DebuggerDisplay("ReasoningModule Axioms={Axioms.Count}")]
public sealed record ReasoningModule(
    IReadOnlyList<OwlAxiom> Axioms,
    IReadOnlyList<Profiles.OwlProfileViolation> Violations);

/// <summary>
/// A reasoner's verdict over a module: consistency and any module-local
/// subsumptions between named classes, surfaced for the caller.
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

    /// <summary>
    /// Whether the verdict covers the module whole. An inconsistency found in
    /// the supported fragment condemns the module regardless of the remainder,
    /// so an inconsistent verdict is always decisive; a consistent verdict is
    /// decisive only when no construct was excluded — otherwise the unsupported
    /// remainder could still clash and the consistency claim is scoped to the
    /// supported fragment.
    /// </summary>
    public bool IsDecisive => !IsConsistent || UnsupportedConstructs.Count == 0;
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
/// <returns>The module decision: the verdict and the work it spent, or an abstention when the decision's budget ran out before a verdict.</returns>
public delegate ValueTask<ModuleDecision> DescriptionLogicDelegate(
    ReasoningModule module,
    CancellationToken cancellationToken);
