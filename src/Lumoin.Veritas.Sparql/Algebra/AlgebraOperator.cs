using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Algebra;

/// <summary>
/// The base of the SPARQL algebra — the intermediate representation a query is translated into after
/// parsing and normalization, and the contract the executor and optimizer operate over. Each concrete
/// operator is an immutable record; the translator (SPARQL 1.2 §18.2) builds a tree of them from the
/// normalized AST.
/// </summary>
/// <remarks>
/// <para>
/// Two members are common to every operator and computed once, lazily, per instance:
/// <see cref="Children"/> gives the uniform traversal surface an <c>AlgebraWalker</c> drives, so no
/// operator needs bespoke walking code; <see cref="OutputVariables"/> is the set of variables the
/// operator's solution multiset may bind, which scope analysis and the optimizer consume.
/// </para>
/// <para>
/// Both are derived deterministically from the operator's immutable structure, so they are cached.
/// The cache lives <em>off</em> the record — in a static <see cref="ConditionalWeakTable{TKey,TValue}"/>
/// keyed by instance reference — rather than in an instance field, because a record's synthesized value
/// equality compares all instance fields and would then treat a traversed operator (cache populated)
/// as unequal to an otherwise-identical fresh one. Keeping the cache off-instance preserves record value
/// equality. The table keys by reference (independent of the overridden value equality), so each distinct
/// instance gets its own entry. (Same off-instance-cache pattern as <c>VeritasBlankNodes</c>.)
/// </para>
/// <para>
/// Algebra nodes deliberately carry no source spans: they are a post-translation semantic IR, and the
/// span-bearing surface for tooling is the AST. SPARQL 1.2 §18.6 [SPARQL Algebra].
/// </para>
/// </remarks>
public abstract record AlgebraOperator
{
    private static ConditionalWeakTable<AlgebraOperator, ComputedMembers> Computed { get; } = new();

    /// <summary>Gets the operator's direct child operators, in evaluation order — computed once and cached. Leaf operators have none.</summary>
    public IReadOnlyList<AlgebraOperator> Children => Computed.GetValue(this, static self => new ComputedMembers(self)).Children;

    /// <summary>Gets the variables the operator's solution multiset may bind — computed once and cached. Consumed by scope analysis and the optimizer.</summary>
    public IReadOnlySet<SparqlVariable> OutputVariables => Computed.GetValue(this, static self => new ComputedMembers(self)).OutputVariables;

    /// <summary>Computes the direct child operators backing <see cref="Children"/>.</summary>
    /// <returns>The direct children, in evaluation order; empty for a leaf operator.</returns>
    protected abstract IReadOnlyList<AlgebraOperator> ComputeChildren();

    /// <summary>Computes the output variables backing <see cref="OutputVariables"/>.</summary>
    /// <returns>The set of variables the operator may bind.</returns>
    protected abstract IReadOnlySet<SparqlVariable> ComputeOutputVariables();

    /// <summary>The per-instance lazy cache of the two computed members, held off the record so it never enters value equality.</summary>
    private sealed class ComputedMembers
    {
        private readonly AlgebraOperator owner;
        private IReadOnlyList<AlgebraOperator>? children;
        private IReadOnlySet<SparqlVariable>? outputVariables;

        /// <summary>Initialises the cache for an operator.</summary>
        /// <param name="owner">The operator whose computed members are cached.</param>
        public ComputedMembers(AlgebraOperator owner)
        {
            this.owner = owner;
        }

        /// <summary>Gets the cached child operators, computing them on first access.</summary>
        public IReadOnlyList<AlgebraOperator> Children => children ??= owner.ComputeChildren();

        /// <summary>Gets the cached output variables, computing them on first access.</summary>
        public IReadOnlySet<SparqlVariable> OutputVariables => outputVariables ??= owner.ComputeOutputVariables();
    }
}
