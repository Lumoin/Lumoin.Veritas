using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// A variable bound to a <see cref="TermId"/> value during query
/// descent — the unit element of
/// <see cref="PlannerContext.Bindings"/>.
/// </summary>
/// <remarks>
/// Equality is value-based on both fields. The driver
/// constructs these fresh for each planner consultation; they
/// are not retained across consultations.
/// </remarks>
[DebuggerDisplay("{Variable.Id}={Value.Encoded}")]
public readonly record struct VariableBinding(Variable Variable, TermId Value);
