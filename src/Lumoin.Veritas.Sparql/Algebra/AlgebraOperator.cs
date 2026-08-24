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
    public IReadOnlyList<AlgebraOperator> Children => Computed.GetValue(this, CreateComputedMembers).Children;

    /// <summary>Gets the variables the operator's solution multiset may bind — computed once and cached. Consumed by scope analysis and the optimizer.</summary>
    /// <remarks>
    /// An operator's output variables are a function of its children's, so the value is warmed
    /// <em>bottom-up over an explicit post-order stack</em> rather than by letting one operator's
    /// <see cref="ComputeOutputVariables"/> read its children's <see cref="OutputVariables"/> and recurse down
    /// the tree: a deep plan would otherwise overflow the call stack. After warming, each
    /// <see cref="ComputeOutputVariables"/> reads only already-cached child values.
    /// </remarks>
    public IReadOnlySet<SparqlVariable> OutputVariables
    {
        get
        {
            ComputedMembers members = Computed.GetValue(this, CreateComputedMembers);
            if(members.HasOutputVariables)
            {
                return members.OutputVariables;
            }

            //Post-order over Children (itself non-recursive), warming each node's output variables before any
            //ancestor's, so the per-operator combine reads cached children rather than recursing through them.
            Stack<(AlgebraOperator Node, bool Emit)> stack = new();
            stack.Push((this, Emit: false));

            while(stack.Count > 0)
            {
                (AlgebraOperator node, bool emit) = stack.Pop();
                if(emit)
                {
                    Computed.GetValue(node, CreateComputedMembers).EnsureOutputVariables();

                    continue;
                }

                stack.Push((node, Emit: true));
                IReadOnlyList<AlgebraOperator> children = node.Children;
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push((children[i], Emit: false));
                }
            }

            return members.OutputVariables;
        }
    }

    /// <summary>Creates the off-instance cache entry for an operator (the <see cref="ConditionalWeakTable{TKey,TValue}"/> factory).</summary>
    /// <param name="self">The operator the cache entry belongs to.</param>
    /// <returns>A fresh cache entry.</returns>
    private static ComputedMembers CreateComputedMembers(AlgebraOperator self) => new(self);

    /// <summary>Computes the direct child operators backing <see cref="Children"/>.</summary>
    /// <returns>The direct children, in evaluation order; empty for a leaf operator.</returns>
    protected abstract IReadOnlyList<AlgebraOperator> ComputeChildren();

    /// <summary>Computes the output variables backing <see cref="OutputVariables"/>.</summary>
    /// <returns>The set of variables the operator may bind.</returns>
    protected abstract IReadOnlySet<SparqlVariable> ComputeOutputVariables();

    /// <summary>
    /// Reconstructs this operator with the given replacement children, preserving its non-child data (the
    /// structural dual of <see cref="ComputeChildren"/> that lets <see cref="AlgebraWalker.Transform"/>
    /// rewrite a subtree without a per-operator switch).
    /// </summary>
    /// <param name="children">The replacement children, in the same count and order as <see cref="Children"/>. A leaf operator ignores them and returns itself.</param>
    /// <returns>The reconstructed operator, or this instance when the operator is a leaf.</returns>
    /// <remarks>
    /// Internal because the SPARQL algebra is a closed operator set: keeping this member non-public seals the
    /// hierarchy to this assembly (an external type cannot satisfy the abstract), so every operator the walker
    /// rebuilds is one defined here. Declared abstract so adding an operator is a compile error until it is
    /// implemented — there is no silent default to mishandle a new shape.
    /// </remarks>
    internal abstract AlgebraOperator RebuildWithChildren(IReadOnlyList<AlgebraOperator> children);

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

        /// <summary>Gets whether the output variables have already been computed and cached.</summary>
        public bool HasOutputVariables => outputVariables is not null;

        /// <summary>Gets the cached output variables (warmed bottom-up by <see cref="AlgebraOperator.OutputVariables"/> before access).</summary>
        public IReadOnlySet<SparqlVariable> OutputVariables => outputVariables ??= owner.ComputeOutputVariables();

        /// <summary>Computes and caches the output variables if not already cached; called in post-order so the owner's children are already cached.</summary>
        public void EnsureOutputVariables() => outputVariables ??= owner.ComputeOutputVariables();
    }
}
