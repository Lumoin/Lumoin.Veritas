using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Query;

/// <summary>
/// A basic graph pattern: an ordered list of
/// <see cref="TriplePattern"/> values that share a single
/// <see cref="VariableRegistry"/>. This is the AST root for a
/// query — the driver consumes it together with a planner to
/// produce solutions.
/// </summary>
/// <remarks>
/// <para>
/// The variable registry is owned by the pattern: every
/// <see cref="Variable"/> appearing in any of the patterns must
/// have been minted by this same registry. Sharing a registry
/// across multiple basic graph patterns is allowed but rarely
/// useful — it would only make sense for a SPARQL-like query
/// where multiple BGPs share a variable scope, which the current
/// query engine does not yet model.
/// </para>
/// <para>
/// The list of patterns is ordered for determinism in trace
/// output and pattern-index assignment, but the engine is free
/// to evaluate them in any order — the leapfrog driver in a
/// later batch picks an evaluation order from the planner.
/// </para>
/// <para>
/// The pattern list and the registry are exposed as read-only
/// references; <see cref="BasicGraphPattern"/> itself is
/// immutable, but the registry it carries is the same mutable
/// instance the caller passed in. Consumers should treat the
/// registry as effectively read-only after constructing the
/// pattern.
/// </para>
/// </remarks>
[DebuggerDisplay("BasicGraphPattern Patterns={Patterns.Count} Variables={Registry.Count}")]
public sealed class BasicGraphPattern
{
    /// <summary>The triple patterns making up this BGP, in source order.</summary>
    public IReadOnlyList<TriplePattern> Patterns { get; }

    /// <summary>The registry that minted every variable used in <see cref="Patterns"/>.</summary>
    public VariableRegistry Registry { get; }

    /// <summary>
    /// The distinct variables appearing across all patterns, in
    /// the order they are first encountered when walking the
    /// patterns left-to-right and within each pattern in
    /// subject-predicate-object order.
    /// </summary>
    public IReadOnlyList<Variable> Variables { get; }

    /// <summary>
    /// Constructs a new basic graph pattern.
    /// </summary>
    /// <param name="patterns">The triple patterns; must not be <c>null</c>; may be empty.</param>
    /// <param name="registry">The variable registry that minted every variable in the patterns; must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> or <paramref name="registry"/> is <c>null</c>.</exception>
    public BasicGraphPattern(IReadOnlyList<TriplePattern> patterns, VariableRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(registry);

        Patterns = patterns;
        Registry = registry;
        Variables = ComputeDistinctVariables(patterns);
    }

    private static List<Variable> ComputeDistinctVariables(IReadOnlyList<TriplePattern> patterns)
    {
        List<Variable> ordered = [];
        HashSet<Variable> seen = [];

        foreach(TriplePattern pattern in patterns)
        {
            foreach(Variable variable in pattern.Variables())
            {
                if(seen.Add(variable))
                {
                    ordered.Add(variable);
                }
            }
        }

        return ordered;
    }
}
