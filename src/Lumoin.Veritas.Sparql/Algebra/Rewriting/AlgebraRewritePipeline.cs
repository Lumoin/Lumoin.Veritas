using System;
using System.Collections.Generic;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// An ordered, immutable pipeline of algebraic rewrite rules run once per engine evaluation entry, between
/// translation and evaluation: each pass is one bottom-up <see cref="AlgebraWalker.Transform"/> whose
/// per-node rewrite applies the enabled rules in list order (node-level chaining — each rule sees the
/// previous rule's output for that node). Passes beyond the first run only while the previous pass applied
/// something AND at least one rule participates in fixpoint iteration, bounded by
/// <see cref="MaxRewritePasses"/>; a budget breach stops early on a semantics-identical intermediate tree,
/// so the bound is free soundness. A rule is one of two kinds, and every replacement must preserve the
/// semantics its kind declares: a PLAN rule (the shipped catalog) is answer-preserving and only ever
/// changes the plan the engine evaluates, while a SEMANTIC rule implements a declared entailment
/// extension — its replacement realizes exactly that extension's specified BGP semantics, it ships in no
/// default pipeline, and it enters a pipeline only by the caller's explicit composition.
/// </summary>
/// <remarks>
/// The pipeline is the logical (algebra-to-algebra) half of query optimization; physical join-strategy
/// selection is the Core <c>JoinStrategySelector</c>'s own seam with its own acceptance rules. Rules are
/// certified by answer-identity differential gates over the W3C conformance corpus and carry per-application
/// trace provenance (<see cref="SparqlExecutionEventKind.RewriteApplied"/>).
/// </remarks>
public sealed class AlgebraRewritePipeline
{
    /// <summary>The fixpoint pass bound: at most this many <see cref="AlgebraWalker.Transform"/> passes run per <see cref="Rewrite(AlgebraOperator, in AlgebraRewriteContext)"/> call; stopping early is sound because every intermediate tree is answer-identical.</summary>
    public const int MaxRewritePasses = 4;

    /// <summary>The frozen ordered rule list — the array backing <see cref="Entries"/>, kept as a field so the per-node loop indexes it directly.</summary>
    private readonly AlgebraRewriteEntry[] entries;

    /// <summary>Whether any entry participates in fixpoint passes; when false the pipeline never runs a second pass.</summary>
    private readonly bool anyFixpoint;

    /// <summary>Constructs the frozen pipeline; reachable only through <see cref="Create"/> and the shared instances.</summary>
    /// <param name="entries">The validated, defensively-copied ordered rule list.</param>
    /// <param name="anyFixpoint">Whether any entry participates in fixpoint passes.</param>
    private AlgebraRewritePipeline(AlgebraRewriteEntry[] entries, bool anyFixpoint)
    {
        this.entries = entries;
        this.anyFixpoint = anyFixpoint;
    }

    /// <summary>The pipeline with no rules: rewriting off. <see cref="Rewrite(AlgebraOperator, in AlgebraRewriteContext)"/> returns its input by reference with no walk and no trace emission — the per-call force-disable value.</summary>
    public static AlgebraRewritePipeline Empty { get; } = new AlgebraRewritePipeline([], anyFixpoint: false);

    /// <summary>
    /// The default pipeline an unconfigured engine resolves: EMPTY — every catalog rule ships default-off
    /// until measured, so the default engine behaves exactly as one with no rewriter. A rule enters the
    /// default set only on a measured flip.
    /// </summary>
    public static AlgebraRewritePipeline Default => Empty;

    /// <summary>The pipeline's ordered rule list; exposed for diagnostics and tests.</summary>
    public IReadOnlyList<AlgebraRewriteEntry> Entries => entries;

    /// <summary>
    /// Freezes an ordered rule list into a pipeline. Rule names must be non-empty and unique — they are the
    /// trace provenance labels — and every rule delegate must be non-null; a violation throws, because a
    /// malformed pipeline is a construction defect, not an expected condition.
    /// </summary>
    /// <param name="entries">The rules, in application order.</param>
    /// <returns>The frozen pipeline.</returns>
    /// <exception cref="ArgumentException">A rule name is null, empty, or duplicated, or a rule delegate is null.</exception>
    public static AlgebraRewritePipeline Create(params ReadOnlySpan<AlgebraRewriteEntry> entries)
    {
        if(entries.Length == 0)
        {
            return Empty;
        }

        AlgebraRewriteEntry[] frozen = entries.ToArray();
        HashSet<string> names = new(StringComparer.Ordinal);
        bool anyFixpoint = false;
        for(int i = 0; i < frozen.Length; i++)
        {
            AlgebraRewriteEntry entry = frozen[i];
            if(string.IsNullOrEmpty(entry.Name))
            {
                throw new ArgumentException($"Rule at index {i} has a null or empty name.", nameof(entries));
            }

            if(entry.Rule is null)
            {
                throw new ArgumentException($"Rule '{entry.Name}' has a null delegate.", nameof(entries));
            }

            if(!names.Add(entry.Name))
            {
                throw new ArgumentException($"Rule name '{entry.Name}' is duplicated.", nameof(entries));
            }

            anyFixpoint |= entry.Fixpoint;
        }

        return new AlgebraRewritePipeline(frozen, anyFixpoint);
    }

    /// <summary>Rewrites a tree without trace emission — the surface for callers outside the engine.</summary>
    /// <param name="root">The translated algebra to rewrite.</param>
    /// <param name="context">The read-only facts the rules consult; its pass index is re-stamped per pass.</param>
    /// <returns>The rewritten tree, or <paramref name="root"/> by reference when nothing applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    public AlgebraOperator Rewrite(AlgebraOperator root, in AlgebraRewriteContext context)
    {
        return Rewrite(root, in context, trace: null);
    }

    /// <summary>
    /// Rewrites a tree, emitting one <see cref="SparqlExecutionEventKind.RewriteApplied"/> event per applied
    /// or abstained rule invocation into the evaluation's trace. An empty pipeline short-circuits: the input
    /// returns by reference with no walk and no emission.
    /// </summary>
    /// <param name="root">The translated algebra to rewrite.</param>
    /// <param name="context">The read-only facts the rules consult; its pass index is re-stamped per pass.</param>
    /// <param name="trace">The evaluation's trace sink, or <see langword="null"/> for no emission.</param>
    /// <returns>The rewritten tree, or <paramref name="root"/> by reference when nothing applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    internal AlgebraOperator Rewrite(AlgebraOperator root, in AlgebraRewriteContext context, SparqlExecutionTrace? trace)
    {
        ArgumentNullException.ThrowIfNull(root);

        if(entries.Length == 0)
        {
            return root;
        }

        AlgebraOperator current = root;
        for(int pass = 0; pass < MaxRewritePasses; pass++)
        {
            PassFrame frame = new(entries, new AlgebraRewriteContext(context.Policy, context.Statistics, pass), trace);
            current = AlgebraWalker.Transform(current, frame.Rewrite);
            if(!frame.Dirty || !anyFixpoint)
            {
                break;
            }
        }

        return current;
    }

    /// <summary>
    /// One pass's per-node rewrite state, bound as the <see cref="AlgebraRewrite"/> method group the walker
    /// invokes — the explicit frame that replaces a capturing lambda: the ordered rules, the pass-stamped
    /// context, the trace sink, and the pass-dirty flag the fixpoint decision reads.
    /// </summary>
    private sealed class PassFrame
    {
        /// <summary>The pipeline's ordered rules, indexed directly by the per-node loop.</summary>
        private readonly AlgebraRewriteEntry[] entries;

        /// <summary>The pass-stamped context threaded to every rule by <c>in</c> reference — a field because a property cannot be passed by reference.</summary>
        private readonly AlgebraRewriteContext context;

        /// <summary>The evaluation's trace sink, or <see langword="null"/> for no emission.</summary>
        private SparqlExecutionTrace? Trace { get; }

        /// <summary>Constructs the frame for one pass.</summary>
        /// <param name="entries">The pipeline's ordered rules.</param>
        /// <param name="context">The pass-stamped context threaded to every rule.</param>
        /// <param name="trace">The evaluation's trace sink, or <see langword="null"/> for no emission.</param>
        public PassFrame(AlgebraRewriteEntry[] entries, AlgebraRewriteContext context, SparqlExecutionTrace? trace)
        {
            this.entries = entries;
            this.context = context;
            Trace = trace;
        }

        /// <summary>Whether any rule applied during this pass — the fixpoint decision's signal.</summary>
        public bool Dirty { get; private set; }

        /// <summary>
        /// Applies the enabled rules in list order at one operator position, chaining each rule onto the
        /// previous rule's output. Passes beyond the first run only fixpoint-participating rules. A rule
        /// returning <see cref="AlgebraRewriteApplication.Applied"/> with a reference-equal tree is a rule
        /// defect treated as not-applicable for dirty tracking, so a defective rule cannot spin the fixpoint.
        /// </summary>
        /// <param name="node">The operator position, its children already rewritten by this pass.</param>
        /// <returns>The operator that flows onward.</returns>
        public AlgebraOperator Rewrite(AlgebraOperator node)
        {
            AlgebraOperator current = node;
            for(int i = 0; i < entries.Length; i++)
            {
                AlgebraRewriteEntry entry = entries[i];
                if(context.Pass > 0 && !entry.Fixpoint)
                {
                    continue;
                }

                AlgebraRewriteOutcome outcome = entry.Rule(current, in context);
                switch(outcome.Application)
                {
                    case(AlgebraRewriteApplication.Applied):
                    {
                        if(ReferenceEquals(outcome.Algebra, current))
                        {
                            break;
                        }

                        Trace?.EmitRewrite(entry.Name, SparqlExecutionOperators.Of(current), AlgebraRewriteApplication.Applied, context.Pass);
                        current = outcome.Algebra;
                        Dirty = true;

                        break;
                    }

                    case(AlgebraRewriteApplication.Abstained):
                    {
                        Trace?.EmitRewrite(entry.Name, SparqlExecutionOperators.Of(current), AlgebraRewriteApplication.Abstained, context.Pass);

                        break;
                    }

                    default:
                    {
                        break;
                    }
                }
            }

            return current;
        }
    }

}
