using System;
using System.Collections.Generic;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// The named rules the engine ships. Every catalog rule is default-off — none is in
/// <see cref="AlgebraRewritePipeline.Default"/> — and enters a caller's pipeline explicitly; a rule joins
/// the default set only on a measured flip. Each rule is a static method bound as a method group (no
/// captured state), certified by its independently derived ground truths, its unit pins, and the
/// conformance-corpus differential arm.
/// </summary>
public static class AlgebraRewriteCatalog
{
    /// <summary>
    /// Unit-join elimination: <c>Join(UnitTable, A) → A</c> and <c>Join(A, UnitTable) → A</c> — the unit
    /// table is the join identity (one empty solution, compatible with every solution, contributing no
    /// variables), so eliminating it preserves the multiset exactly. The translator already collapses the
    /// unit joins its own construction produces; this rule additionally collapses unit tables that other
    /// rewrite rules synthesize, which is why it participates in fixpoint passes.
    /// </summary>
    public static AlgebraRewriteEntry UnitJoinElimination { get; } = new("unit-join-elimination", ApplyUnitJoinElimination, Fixpoint: true);

    /// <summary>
    /// Slice fusion: <c>Slice(o2,l2, Slice(o1,l1, X)) → Slice(o1+o2, combined, X)</c> — nested positional
    /// windows compose exactly over any fixed base order, because the materialising slice is positional over
    /// its input and both plans window the SAME subtree. The combined limit is the min over the PRESENT
    /// limits of the outer limit and the inner remainder <c>max(0, l1−o2)</c>, null only when both are null;
    /// the combined offset saturates at <see cref="int.MaxValue"/> (an offset past every representable row
    /// leaves both plans empty). Widens the leaf-cap and window interceptions' reach — a fused slice is a
    /// single window their chain walks can see.
    /// </summary>
    public static AlgebraRewriteEntry SliceFusion { get; } = new("slice-fusion", ApplySliceFusion, Fixpoint: true);

    /// <summary>
    /// Distinct idempotence: <c>Distinct(Distinct(X)) → Distinct(X)</c> and
    /// <c>Distinct(Reduced(X)) → Distinct(X)</c> — Distinct maps a multiset to its support, the support of a
    /// set is itself, and every legal Reduced output preserves support, so a Distinct above absorbs it. A
    /// tower reduces fully in one bottom-up pass, so the rule does not participate in fixpoint iteration.
    /// </summary>
    public static AlgebraRewriteEntry DistinctIdempotence { get; } = new("distinct-idempotence", ApplyDistinctIdempotence, Fixpoint: false);

    /// <summary>
    /// No-op projection collapse, PARENT-KEYED: at a Join/LeftJoin/Union/Minus/Filter/Extend/Graph parent, a
    /// direct child <c>Project(X, vars)</c> with <c>set(vars) == X.OutputVariables</c> collapses to X. The
    /// guard makes the projection a STRICT DOMAIN NO-OP — rows always bind a subset of OutputVariables, so
    /// every row's binding set survives intact and only binding ORDER can change — and the listed parents
    /// all combine by variable identity, never position. The query-form terminators
    /// (Distinct/Reduced/Slice/ToList/ToMultiSet) and the tree root are NOT collapse-permitting parents:
    /// they are exactly where binding order is semantically visible (positional dedup, positional windows,
    /// the SELECT column list). Never loosen the set-equality guard: a narrowing projection drops bindings
    /// and is not a no-op.
    /// </summary>
    public static AlgebraRewriteEntry NoopProjectCollapse { get; } = new("noop-project-collapse", ApplyNoopProjectCollapse, Fixpoint: true);

    /// <summary>
    /// Empty-table annihilation: <c>Join(Table0, X) → Table0</c> (either side) when X's subtree is composed
    /// solely of Bgp/Join/Union/Table/UnitTable/Path — the error-free allowlist, because the un-rewritten
    /// plan evaluates X and eliding an error-capable subtree (a non-silent SERVICE, an expression operator)
    /// would suppress an observable error; <c>Filter(cond, Table0) → Table0</c> unconditionally (zero rows =
    /// zero condition evaluations = no possible error); <c>Union(Table0, X) → X</c> (either side)
    /// unconditionally — X is KEPT, errors and all. Table0 is the zero-row inline-data table, distinct from
    /// the unit table's single empty solution.
    /// </summary>
    public static AlgebraRewriteEntry EmptyTableAnnihilation { get; } = new("empty-table-annihilation", ApplyEmptyTableAnnihilation, Fixpoint: true);

    /// <summary>Applies unit-join elimination at one position.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused — the rule is unconditional on its pattern).</param>
    /// <returns>The surviving operand when the pattern matches, else not-applicable.</returns>
    private static AlgebraRewriteOutcome ApplyUnitJoinElimination(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node switch
        {
            Join { Left: UnitTable } join => AlgebraRewriteOutcome.Applied(join.Right),
            Join { Right: UnitTable } join => AlgebraRewriteOutcome.Applied(join.Left),
            _ => AlgebraRewriteOutcome.NotApplicable(node)
        };
    }

    /// <summary>Applies slice fusion at one position.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused — the rule is unconditional on its pattern).</param>
    /// <returns>The fused window when the pattern matches, else not-applicable.</returns>
    private static AlgebraRewriteOutcome ApplySliceFusion(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        if(node is not Slice { Input: Slice inner } outer)
        {
            return AlgebraRewriteOutcome.NotApplicable(node);
        }

        //The min over the PRESENT terms of { outer limit, inner remainder }; a null term is dropped and
        //both-null stays unbounded (null).
        int offset = (int)Math.Min((long)inner.Offset + outer.Offset, int.MaxValue);
        long candidate = long.MaxValue;
        bool bounded = false;
        if(inner.Limit is int innerLimit)
        {
            candidate = Math.Max(0L, (long)innerLimit - outer.Offset);
            bounded = true;
        }

        if(outer.Limit is int outerLimit)
        {
            candidate = Math.Min(candidate, outerLimit);
            bounded = true;
        }

        int? limit = bounded ? (int)Math.Min(candidate, int.MaxValue) : null;

        return AlgebraRewriteOutcome.Applied(new Slice(inner.Input, offset, limit));
    }

    /// <summary>Applies distinct idempotence at one position.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused — the rule is unconditional on its pattern).</param>
    /// <returns>The absorbed form when the pattern matches, else not-applicable.</returns>
    private static AlgebraRewriteOutcome ApplyDistinctIdempotence(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node switch
        {
            Distinct { Input: Distinct inner } => AlgebraRewriteOutcome.Applied(inner),
            Distinct { Input: Reduced reduced } => AlgebraRewriteOutcome.Applied(new Distinct(reduced.Input)),
            _ => AlgebraRewriteOutcome.NotApplicable(node)
        };
    }

    /// <summary>Applies the parent-keyed no-op projection collapse at one position.</summary>
    /// <param name="node">The operator position — matched as the PARENT whose child projections collapse.</param>
    /// <param name="context">The rule context (unused — the guard is purely structural).</param>
    /// <returns>The parent rebuilt over unwrapped children when any collapsed, else not-applicable.</returns>
    private static AlgebraRewriteOutcome ApplyNoopProjectCollapse(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        if(node is not (Join or LeftJoin or Union or Minus or Filter or Extend or Graph))
        {
            return AlgebraRewriteOutcome.NotApplicable(node);
        }

        IReadOnlyList<AlgebraOperator> children = node.Children;
        AlgebraOperator[]? rebuilt = null;
        for(int i = 0; i < children.Count; i++)
        {
            if(children[i] is Project project && IsDomainNoOp(project))
            {
                if(rebuilt is null)
                {
                    rebuilt = new AlgebraOperator[children.Count];
                    for(int j = 0; j < children.Count; j++)
                    {
                        rebuilt[j] = children[j];
                    }
                }

                rebuilt[i] = project.Input;
            }
        }

        return rebuilt is null
            ? AlgebraRewriteOutcome.NotApplicable(node)
            : AlgebraRewriteOutcome.Applied(node.RebuildWithChildren(rebuilt));
    }

    /// <summary>Whether a projection keeps exactly its input's visible output set — the strict domain no-op the collapse guard demands.</summary>
    /// <param name="project">The candidate projection.</param>
    /// <returns><see langword="true"/> when the projected set equals the input's output-variable set.</returns>
    private static bool IsDomainNoOp(Project project)
    {
        HashSet<SparqlVariable> projected = new(project.Variables);

        return projected.SetEquals(project.Input.OutputVariables);
    }

    /// <summary>Applies empty-table annihilation at one position.</summary>
    /// <param name="node">The operator position.</param>
    /// <param name="context">The rule context (unused — the guards are purely structural).</param>
    /// <returns>The annihilated or surviving form when a pattern matches, else not-applicable.</returns>
    private static AlgebraRewriteOutcome ApplyEmptyTableAnnihilation(AlgebraOperator node, in AlgebraRewriteContext context)
    {
        return node switch
        {
            Join { Left: Table left } join when IsEmptyTable(left) && IsErrorFree(join.Right) => AlgebraRewriteOutcome.Applied(join.Left),
            Join { Right: Table right } join when IsEmptyTable(right) && IsErrorFree(join.Left) => AlgebraRewriteOutcome.Applied(join.Right),
            Filter { Input: Table table } filter when IsEmptyTable(table) => AlgebraRewriteOutcome.Applied(filter.Input),
            Union { Left: Table left } union when IsEmptyTable(left) => AlgebraRewriteOutcome.Applied(union.Right),
            Union { Right: Table right } union when IsEmptyTable(right) => AlgebraRewriteOutcome.Applied(union.Left),
            _ => AlgebraRewriteOutcome.NotApplicable(node)
        };
    }

    /// <summary>Whether an inline-data table carries zero rows.</summary>
    /// <param name="table">The table to inspect.</param>
    /// <returns><see langword="true"/> for the zero-row table.</returns>
    private static bool IsEmptyTable(Table table)
    {
        return table.Data.Rows.Count == 0;
    }

    /// <summary>
    /// Whether a subtree is composed solely of operators that cannot raise — Bgp/Join/Union/Table/UnitTable/
    /// Path — so eliding its evaluation removes no observable error. An explicit-stack walk per the
    /// no-recursion discipline (via the walker's iterative traversal).
    /// </summary>
    /// <param name="subtree">The subtree to inspect.</param>
    /// <returns><see langword="true"/> when every operator is on the error-free allowlist.</returns>
    private static bool IsErrorFree(AlgebraOperator subtree)
    {
        foreach(AlgebraOperator node in AlgebraWalker.Traverse(subtree))
        {
            if(node is not (Bgp or Join or Union or Table or UnitTable or Path))
            {
                return false;
            }
        }

        return true;
    }
}
