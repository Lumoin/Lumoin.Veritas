using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Query;

/// <summary>
/// Identity of a query variable. The variable's textual name is
/// held in a <see cref="VariableRegistry"/>; this struct carries
/// only an integer identity for cheap equality comparisons in the
/// inner loops of triejoin descent and leapfrog intersection.
/// </summary>
/// <remarks>
/// <para>
/// Variables are query-scoped: a single query parses or constructs
/// its own <see cref="VariableRegistry"/>, registers each named
/// variable once, and uses the returned <see cref="Variable"/> ids
/// for the rest of the query's life. Different queries do not
/// share variables; the registry instance defines the scope.
/// </para>
/// <para>
/// Equality is value-based by <see cref="Id"/>. Two
/// <see cref="Variable"/> instances with the same id are equal
/// regardless of which registry minted them; consumers are
/// responsible for not mixing ids across registries.
/// </para>
/// </remarks>
[DebuggerDisplay("Variable(Id={Id})")]
public readonly record struct Variable(int Id);
