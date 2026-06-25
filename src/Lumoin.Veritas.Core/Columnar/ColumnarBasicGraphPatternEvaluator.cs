using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Evaluates a <see cref="BasicGraphPattern"/> against a
/// <see cref="ColumnarTripleIndex"/> using worst-case-optimal
/// joins — the columnar peer of the hypertrie's evaluator, with the
/// same iterative open-frames state machine, the same
/// <see cref="Planner"/> consultation surface, the same
/// access-control wiring, and the same driver-level trace events.
/// </summary>
/// <remarks>
/// <para>
/// The driver is concrete over
/// <see cref="ColumnarTriejoinIterator"/>: backends integrate as
/// whole index + cursor + driver units compared at the evaluation
/// boundary, so the state machine is instantiated per backend
/// rather than parameterised over one.
/// </para>
/// <para>
/// Fully-bound patterns are pre-checked through
/// <see cref="ColumnarTripleIndex.Contains"/> — constraints, not
/// joins. Cursor-level trace events are not emitted; the columnar
/// cursor carries no trace surface.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarBasicGraphPatternEvaluator Patterns={query.Patterns.Count}")]
public sealed class ColumnarBasicGraphPatternEvaluator
{
    //Window size for the recent-denials list passed to the
    //planner. Bounded so the list does not grow unboundedly during
    //queries against permissive-but-noisy policies.
    private const int RecentDenialsWindow = 32;

    private readonly ColumnarTripleIndex index;

    private readonly BasicGraphPattern query;

    private readonly Planner planner;

    /// <summary>A-priori per-class upper bounds for the planner, or <c>null</c> when the caller supplied none. Stable for the index's generation; handed to the planner unchanged on every consultation.</summary>
    private AprioriCardinalities? Cardinalities { get; }

    /// <summary>The HyperCube cell this evaluator owns; the unpartitioned default accepts every key. Parallel execution runs one evaluator per cell over the shared immutable index.</summary>
    private HyperCubeCell Cell { get; }

    /// <summary>The query's variables in global order, for mapping a descend target to its cell-filter index. Linear scan — variable counts are small.</summary>
    private IReadOnlyList<Variable> GlobalVariables { get; }

    private readonly AccessControlDelegate? accessControl;

    private readonly AccessContext? accessContext;

    private readonly TraceHandler<QueryTraceEvent>? traceHandler;

    private readonly Guid correlationId;

    //Clock used to stamp Ticks on emitted trace events. Injected
    //so tests can pin time deterministically via FakeTimeProvider;
    //production callers pass TimeProvider.System at the
    //composition root.
    private readonly TimeProvider timeProvider;

    //Sequence-number counter for trace events emitted by this
    //evaluator. A field rather than a property because Interlocked
    //requires a ref parameter.
    private long traceSequence;

    /// <summary>
    /// Constructs a new evaluator.
    /// </summary>
    /// <param name="index">The columnar index to query.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="planner">The variable-elimination planner.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Pass <see cref="TimeProvider.System"/> in production; tests pinning trace timing pass a <c>FakeTimeProvider</c>.</param>
    /// <param name="cardinalities">A-priori per-class upper bounds handed to the planner on every consultation, or <c>null</c> when none are known.</param>
    /// <param name="accessControl">Optional access-control policy. <c>null</c> treats every candidate as allowed.</param>
    /// <param name="accessContext">Caller-supplied access context, threaded into every access-control consultation. Required when <paramref name="accessControl"/> is non-<c>null</c>; ignored when <c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler for query-execution events.</param>
    /// <param name="correlationId">Correlation id stamped on every emitted trace event.</param>
    /// <param name="cell">The HyperCube cell this evaluator owns under parallel execution; the default accepts every key.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An access-control delegate is supplied without an access context.</exception>
    public ColumnarBasicGraphPatternEvaluator(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        Planner planner,
        TimeProvider timeProvider,
        AprioriCardinalities? cardinalities = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        HyperCubeCell cell = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if(accessControl is not null && accessContext is null)
        {
            throw new ArgumentException("An access context must be supplied when access control is configured.", nameof(accessContext));
        }

        this.index = index;
        this.query = query;
        Cardinalities = cardinalities;
        this.timeProvider = timeProvider;
        this.accessControl = accessControl;
        this.accessContext = accessContext;
        this.traceHandler = traceHandler;
        this.correlationId = correlationId;
        Cell = cell;
        GlobalVariables = query.Variables;
        IteratorGlobalOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index, query)
            ?? throw new ArgumentException(
                "The query's shape is rotation-incompatible with this index's order set; consult CanEvaluate and route through the system of record.",
                nameof(query));

        //Under a reduced order set the iterators commit to the
        //rotation-compatible order at construction, collapsing the
        //planner's dynamic choice space to exactly that order; the
        //supplied planner is substituted accordingly. Under all six
        //orders the plan IS the query's first-occurrence list and
        //the supplied planner keeps its freedom.
        this.planner = ReferenceEquals(IteratorGlobalOrder, query.Variables)
            ? planner
            : Planners.FixedOrder(IteratorGlobalOrder);
    }

    /// <summary>
    /// The global variable order iterators are constructed on: the
    /// query's first-occurrence order under all six permutations,
    /// or the rotation-compatible order under three. Distinct from
    /// <see cref="GlobalVariables"/>, which keys HyperCube cell
    /// filters by position and must match the cell constructor's
    /// list regardless of elimination order.
    /// </summary>
    private IReadOnlyList<Variable> IteratorGlobalOrder { get; }

    /// <summary>
    /// Whether <paramref name="query"/> is answerable on
    /// <paramref name="index"/> — always under
    /// <see cref="ColumnarOrderSetMode.AllSixOrders"/>; under three
    /// rotations, exactly when a rotation-compatible global
    /// variable order exists (<see cref="ColumnarRotationPlanner"/>).
    /// Callers route rotation-incompatible queries to the system of
    /// record.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns><see langword="true"/> when this evaluator can answer the query.</returns>
    public static bool CanEvaluate(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        return ColumnarRotationPlanner.TryPlanGlobalOrder(index, query) is not null;
    }

    /// <summary>
    /// Evaluates the query, yielding solutions one at a time.
    /// Each enumeration of the returned async sequence performs
    /// a fresh evaluation; the evaluator itself is reusable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token threaded into the driver loop and the access-control consultation.</param>
    /// <returns>An async sequence of solutions, in driver-determined order.</returns>
    public async IAsyncEnumerable<Solution> EvaluateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EmitQueryStarted(query.Patterns.Count);

        int yieldedCount = 0;

        //Pre-check fully-bound patterns. If any fails, no
        //solutions exist; bail immediately.
        if(!FullyBoundPatternsMatch())
        {
            EmitQueryCompleted(yieldedCount);

            yield break;
        }

        //A fully-bound pattern is a candidate triple in its own right: the policy must be able to hide a denied
        //triple's existence, or a membership query (ASK { s p o }) observes it past access control. The
        //variable-bearing patterns are consulted per solution below; the constant ones are consulted once here.
        if(accessControl is not null && !await FullyBoundPatternsAllowedAsync(cancellationToken).ConfigureAwait(false))
        {
            EmitQueryCompleted(yieldedCount);

            yield break;
        }

        //Identify the variable-bearing patterns and build per-pattern
        //iterators. If there are no variable-bearing patterns the
        //query has no joins; the empty solution is the answer
        //(provided the fully-bound check above passed).
        List<int> variablePatternIndices = [];

        for(int i = 0; i < query.Patterns.Count; i++)
        {
            if(query.Patterns[i].Variables().GetEnumerator().MoveNext())
            {
                variablePatternIndices.Add(i);
            }
        }

        if(variablePatternIndices.Count == 0)
        {
            yield return new Solution([]);
            yieldedCount++;

            EmitQueryCompleted(yieldedCount);

            yield break;
        }

        //Build iterators. Each iterator's variable order is the
        //projection of the planned global order onto that pattern's
        //variable set — the first-occurrence order under all six
        //permutations, or the rotation-compatible order the planner
        //found under three (see ColumnarRotationPlanner; a query no
        //order serves was already rejected at construction).
        ColumnarTriejoinIterator[] iterators = new ColumnarTriejoinIterator[variablePatternIndices.Count];

        for(int i = 0; i < variablePatternIndices.Count; i++)
        {
            int patternIndex = variablePatternIndices[i];
            TriplePattern pattern = query.Patterns[patternIndex];
            Variable[] perIteratorOrder = ProjectGlobalOrderOntoPattern(IteratorGlobalOrder, pattern);

            iterators[i] = new ColumnarTriejoinIterator(index, pattern, perIteratorOrder);
        }

        //Drive the state machine.
        await foreach(Solution solution in DriveAsync(iterators, variablePatternIndices, cancellationToken).ConfigureAwait(false))
        {
            yieldedCount++;

            yield return solution;
        }

        EmitQueryCompleted(yieldedCount);
    }

    //The state-machine driver. Maintains the current binding list
    //and the per-level open-frames stack; consults the planner at
    //each step; dispatches on the decision.
    private async IAsyncEnumerable<Solution> DriveAsync(
        ColumnarTriejoinIterator[] iterators,
        IReadOnlyList<int> variablePatternIndices,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<VariableBinding> bindings = [];
        List<ColumnarTriejoinIterator[]> openFrames = [];
        List<EncodedTriple> recentDenials = [];

        //needsAdvance set after a yield or after a failed descent —
        //indicates the next loop iteration should advance the
        //topmost frame instead of consulting the planner fresh.
        bool needsAdvance = false;

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(needsAdvance)
            {
                bool advanced = TryAdvance(iterators, bindings, openFrames, cancellationToken);

                if(!advanced)
                {
                    //Fully unwound — no more solutions.
                    yield break;
                }

                needsAdvance = false;
            }

            PlannerContext context = BuildPlannerContext(iterators, variablePatternIndices, bindings, recentDenials);
            PlannerDecision decision = planner(context, cancellationToken);

            EmitPlannerDecision(decision);

            switch(decision.Kind)
            {
                case PlannerDecisionKind.StopQuery:
                {
                    yield break;
                }
                case PlannerDecisionKind.SkipBranch:
                {
                    needsAdvance = true;

                    continue;
                }
                case PlannerDecisionKind.YieldSolution:
                {
                    bool allowed = await CheckAccessControlAsync(iterators, variablePatternIndices, bindings, recentDenials, cancellationToken).ConfigureAwait(false);

                    if(allowed)
                    {
                        VariableBinding[] snapshotBindings = bindings.ToArray();

                        EmitSolutionYielded();

                        yield return new Solution(snapshotBindings);
                    }

                    needsAdvance = true;

                    continue;
                }
                case PlannerDecisionKind.DescendVariable:
                {
                    Variable target = decision.AsDescendVariable();
                    bool descended = TryDescend(iterators, target, bindings, openFrames, cancellationToken);

                    if(!descended)
                    {
                        //No common key at this variable; treat as a skip.
                        needsAdvance = true;
                    }

                    continue;
                }
                default:
                {
                    throw new InvalidOperationException($"Unknown planner decision kind: {decision.Kind}.");
                }
            }
        }
    }

    //Finds the next common key for `variable` that this evaluator's
    //cell accepts, advancing every participant past rejected keys.
    //With the unpartitioned default cell this is plain leapfrog
    //plus one branch per result.
    private bool TryFindAcceptedCommonKey(
        IReadOnlyList<ColumnarTriejoinIterator> participants,
        Variable variable,
        CancellationToken cancellationToken,
        out TermId commonKey)
    {
        int variableIndex = GlobalIndexOf(variable);

        while(ColumnarLeapfrogIntersection.TryFindNextCommonKey(participants, out commonKey, cancellationToken))
        {
            if(Cell.Accepts(variableIndex, commonKey.Encoded))
            {
                return true;
            }

            //Another cell owns this key; every participant steps
            //past it and the leapfrog resumes.
            for(int i = 0; i < participants.Count; i++)
            {
                participants[i].Next();
            }
        }

        return false;
    }

    //The variable's index in the query's global variable order —
    //the coordinate axis its cell filter partitions.
    private int GlobalIndexOf(Variable variable)
    {
        for(int i = 0; i < GlobalVariables.Count; i++)
        {
            if(GlobalVariables[i] == variable)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Variable id {variable.Id} is not in the query's variable order.");
    }

    //Pops the topmost open frame, advances its iterators past
    //their current keys, and tries to find the next common key at
    //the now-current level. Returns true when a new common key was
    //found and the frame was re-pushed; returns false when every
    //frame has been unwound (no more solutions).
    private bool TryAdvance(
        ColumnarTriejoinIterator[] iterators,
        List<VariableBinding> bindings,
        List<ColumnarTriejoinIterator[]> openFrames,
        CancellationToken cancellationToken)
    {
        while(openFrames.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ColumnarTriejoinIterator[] frame = openFrames[^1];

            //Up out of the level the frame represents and discard
            //its binding, then advance every participant past its
            //current key so the next leapfrog finds the next
            //candidate. The level's variable is read after the
            //rewind, while the frame is positioned on it.
            frame[0].Up();
            Variable frameVariable = frame[0].CurrentVariable;
            frame[0].Next();

            for(int i = 1; i < frame.Length; i++)
            {
                frame[i].Up();
                frame[i].Next();
            }

            bindings.RemoveAt(bindings.Count - 1);

            //Try to find the next accepted common key at this level.
            if(TryFindAcceptedCommonKey(frame, frameVariable, cancellationToken, out TermId commonKey))
            {
                //Re-open every participant on the new common key
                //and push the frame back. The level was popped from
                //bindings above; we now push the new binding for
                //the variable that was at this level. Recover the
                //variable from the frame's first iterator (every
                //participant has the same CurrentVariable).
                Variable variable = frame[0].CurrentVariable;

                for(int i = 0; i < frame.Length; i++)
                {
                    bool opened = frame[i].Open(commonKey);

                    if(!opened)
                    {
                        //Should be unreachable: leapfrog confirmed
                        //the key exists for every participant.
                        throw new InvalidOperationException("Leapfrog intersection reported a common key that an iterator could not open.");
                    }
                }

                bindings.Add(new VariableBinding(variable, commonKey));

                //Re-binding this level supersedes the binding under which independent iterators (those NOT in this
                //frame) enumerated their own deeper variables. Such an iterator may have unwound during this same
                //TryAdvance pass into one of two stale states: fully exhausted (AtEnd), or stranded NOT-AtEnd on a
                //key that no longer joins. Either way its deeper enumeration is invalid for the new binding, so
                //reset the current level of every such iterator so it presents its first key again under the new
                //binding.
                ReseedIndependentIterators(iterators, frame);

                return true;
            }

            //No more keys at this level — pop the frame and
            //continue unwinding.
            openFrames.RemoveAt(openFrames.Count - 1);
        }

        return false;
    }

    //Resets the current variable level of every non-participant iterator that still has a level left to enumerate, so
    //it re-presents its first key under the just-re-bound binding. The participants of the re-bound frame were
    //re-opened by the caller and must not be reset. (Restarting an already-first-key iterator is idempotent, so the
    //broad sweep is safe.)
    private static void ReseedIndependentIterators(ColumnarTriejoinIterator[] iterators, ColumnarTriejoinIterator[] reboundFrame)
    {
        for(int i = 0; i < iterators.Length; i++)
        {
            ColumnarTriejoinIterator iterator = iterators[i];

            if(Array.IndexOf(reboundFrame, iterator) >= 0)
            {
                continue;
            }

            if(iterator.DescendedLevels < iterator.VariableOrder.Count)
            {
                iterator.RestartCurrentLevel();
            }
        }
    }

    //Attempts to descend by binding `target`. Identifies which
    //iterators participate (those whose CurrentVariable equals
    //target), runs the cell-filtered leapfrog to find a common key,
    //opens each participant on that key, and pushes a frame.
    //Returns true on success; false when no accepted common key
    //exists at this variable (in which case the iterators are not
    //advanced past the level).
    private bool TryDescend(
        ColumnarTriejoinIterator[] iterators,
        Variable target,
        List<VariableBinding> bindings,
        List<ColumnarTriejoinIterator[]> openFrames,
        CancellationToken cancellationToken)
    {
        List<ColumnarTriejoinIterator> participants = [];

        for(int i = 0; i < iterators.Length; i++)
        {
            ColumnarTriejoinIterator iterator = iterators[i];

            if(iterator.AtEnd)
            {
                //An at-end iterator cannot participate; the planner
                //should have skipped this branch. Treat the
                //descent as failed.
                return false;
            }

            if(iterator.DescendedLevels < iterator.VariableOrder.Count && iterator.CurrentVariable == target)
            {
                participants.Add(iterator);
            }
        }

        if(participants.Count == 0)
        {
            //Target variable does not appear in any iterator's
            //current level — the planner picked a variable no
            //iterator is positioned to bind. This is a planner
            //error. Treat as failure rather than silently misjoin.
            return false;
        }

        if(!TryFindAcceptedCommonKey(participants, target, cancellationToken, out TermId commonKey))
        {
            return false;
        }

        ColumnarTriejoinIterator[] frame = participants.ToArray();

        for(int i = 0; i < frame.Length; i++)
        {
            bool opened = frame[i].Open(commonKey);

            if(!opened)
            {
                throw new InvalidOperationException("Leapfrog intersection reported a common key that an iterator could not open.");
            }
        }

        bindings.Add(new VariableBinding(target, commonKey));
        openFrames.Add(frame);

        return true;
    }

    //Verifies every fully-bound pattern resolves to at least one
    //matching triple in the index. Returns false if any does
    //not — in which case the query yields no solutions.
    private bool FullyBoundPatternsMatch()
    {
        for(int i = 0; i < query.Patterns.Count; i++)
        {
            TriplePattern pattern = query.Patterns[i];

            //A pattern is fully bound when no position is a variable.
            if(pattern.Subject.IsVariable || pattern.Predicate.IsVariable || pattern.Object.IsVariable)
            {
                continue;
            }

            if(!index.Contains(pattern.Subject.BoundTerm, pattern.Predicate.BoundTerm, pattern.Object.BoundTerm))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Consults the access-control policy on every fully-bound (constant) pattern, each of which is its own
    /// candidate triple. Returns <see langword="true"/> when all are allowed; <see langword="false"/> — hiding
    /// the match — when any returns <see cref="AccessDecision.Deny"/> (audited via the trace stream) or
    /// <see cref="AccessDecision.NotFound"/> (silent), so a membership query cannot observe a triple the policy
    /// forbids. Only called when a policy is configured; mirrors <see cref="CheckAccessControlAsync"/> for the
    /// variable-bearing patterns. The denial window is a join-planning aid and is not needed here, since a denied
    /// constant pattern yields the whole query empty.
    /// </summary>
    /// <param name="cancellationToken">Threaded into the access-control consultation.</param>
    /// <returns><see langword="true"/> when every fully-bound pattern is allowed.</returns>
    private async ValueTask<bool> FullyBoundPatternsAllowedAsync(CancellationToken cancellationToken)
    {
        for(int i = 0; i < query.Patterns.Count; i++)
        {
            TriplePattern pattern = query.Patterns[i];

            if(pattern.Subject.IsVariable || pattern.Predicate.IsVariable || pattern.Object.IsVariable)
            {
                continue;
            }

            EncodedTriple candidate = EncodedTriple.FromEncoded(pattern.Subject.BoundTerm.Encoded, pattern.Predicate.BoundTerm.Encoded, pattern.Object.BoundTerm.Encoded);
            AccessDecision decision = await accessControl!(new AccessRequest(candidate, accessContext!), cancellationToken).ConfigureAwait(false);

            //A denial is audited; NotFound is silent by design. Both hide the triple, failing the membership.
            if(decision is AccessDecision.Deny)
            {
                EmitAccessDenied(candidate);
            }

            if(!Visible(decision))
            {
                return false;
            }
        }

        return true;

        static bool Visible(AccessDecision decision) => decision switch
        {
            AccessDecision.Allow => true,
            AccessDecision.Deny or AccessDecision.NotFound => false,
            _ => throw new InvalidOperationException($"Unknown access decision: {decision}."),
        };
    }

    //Consults the access-control policy on each variable-bearing
    //pattern's full triple at solution-yield time. Returns true
    //when every pattern's triple is allowed; false (and emits an
    //AccessDenied trace event) when any returns Deny; false
    //silently when any returns NotFound.
    private async ValueTask<bool> CheckAccessControlAsync(
        ColumnarTriejoinIterator[] iterators,
        IReadOnlyList<int> variablePatternIndices,
        IReadOnlyList<VariableBinding> bindings,
        List<EncodedTriple> recentDenials,
        CancellationToken cancellationToken)
    {
        if(accessControl is null)
        {
            return true;
        }

        for(int i = 0; i < iterators.Length; i++)
        {
            TriplePattern pattern = query.Patterns[variablePatternIndices[i]];
            EncodedTriple candidate = MaterialiseTriple(pattern, bindings, iterators[i]);
            AccessRequest request = new(candidate, accessContext!);
            AccessDecision decision = await accessControl(request, cancellationToken).ConfigureAwait(false);

            switch(decision)
            {
                case AccessDecision.Allow:
                {
                    continue;
                }
                case AccessDecision.Deny:
                {
                    EmitAccessDenied(candidate);
                    AddDenialToWindow(recentDenials, candidate);

                    return false;
                }
                case AccessDecision.NotFound:
                {
                    return false;
                }
                default:
                {
                    throw new InvalidOperationException($"Unknown access decision: {decision}.");
                }
            }
        }

        return true;
    }

    //Resolves a candidate triple by combining the pattern's bound
    //positions with the iterator's current variable bindings.
    private static EncodedTriple MaterialiseTriple(
        TriplePattern pattern,
        IReadOnlyList<VariableBinding> bindings,
        ColumnarTriejoinIterator iterator)
    {
        uint subject = ResolvePosition(pattern.Subject, bindings, iterator);
        uint predicate = ResolvePosition(pattern.Predicate, bindings, iterator);
        uint obj = ResolvePosition(pattern.Object, bindings, iterator);

        return EncodedTriple.FromEncoded(subject, predicate, obj);
    }

    private static uint ResolvePosition(
        PatternPosition position,
        IReadOnlyList<VariableBinding> bindings,
        ColumnarTriejoinIterator iterator)
    {
        if(position.IsBound)
        {
            return position.BoundTerm.Encoded;
        }

        Variable variable = position.Variable;

        //Try the global bindings list first.
        for(int i = 0; i < bindings.Count; i++)
        {
            VariableBinding binding = bindings[i];

            if(binding.Variable == variable)
            {
                return binding.Value.Encoded;
            }
        }

        //Fall back to the iterator's own bound values — covers
        //variables that exist only in this pattern and have been
        //bound here but did not yet make it into the global
        //bindings list.
        return iterator.ValueOf(variable).Encoded;
    }

    //Bounded sliding window — append, then trim from the head if
    //we've grown past the window size.
    private static void AddDenialToWindow(List<EncodedTriple> recentDenials, EncodedTriple denial)
    {
        recentDenials.Add(denial);

        if(recentDenials.Count > RecentDenialsWindow)
        {
            recentDenials.RemoveAt(0);
        }
    }

    //Builds an immutable PlannerContext for one consultation.
    //Iterator snapshots are constructed fresh per consultation.
    private PlannerContext BuildPlannerContext(
        ColumnarTriejoinIterator[] iterators,
        IReadOnlyList<int> variablePatternIndices,
        IReadOnlyList<VariableBinding> bindings,
        IReadOnlyList<EncodedTriple> recentDenials)
    {
        IteratorSnapshot[] iteratorSnapshots = new IteratorSnapshot[iterators.Length];

        for(int i = 0; i < iterators.Length; i++)
        {
            ColumnarTriejoinIterator iterator = iterators[i];
            Variable currentVariable = iterator.DescendedLevels < iterator.VariableOrder.Count
                ? iterator.CurrentVariable
                : default;
            TermId key = iterator.AtEnd ? TermId.None : iterator.Key;

            iteratorSnapshots[i] = new IteratorSnapshot(
                PatternIndex: variablePatternIndices[i],
                CurrentVariable: currentVariable,
                Key: key,
                AtEnd: iterator.AtEnd,
                DescendedLevels: iterator.DescendedLevels);
        }

        return new PlannerContext(query, bindings, iteratorSnapshots, recentDenials, Cardinalities);
    }

    //Projects the global first-occurrence variable order onto a
    //pattern's variable set. The result is the variables present
    //in the pattern, in the same relative order they appear in
    //the global list.
    private static Variable[] ProjectGlobalOrderOntoPattern(IReadOnlyList<Variable> globalOrder, TriplePattern pattern)
    {
        HashSet<Variable> patternVariables = [.. pattern.Variables()];
        List<Variable> projected = [];

        for(int i = 0; i < globalOrder.Count; i++)
        {
            Variable variable = globalOrder[i];

            if(patternVariables.Contains(variable))
            {
                projected.Add(variable);
            }
        }

        return [.. projected];
    }

    private void EmitQueryStarted(int patternCount)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.QueryStarted(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId, patternCount);

        traceHandler(in evt);
    }

    private void EmitQueryCompleted(int solutionCount)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.QueryCompleted(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId, solutionCount);

        traceHandler(in evt);
    }

    private void EmitPlannerDecision(PlannerDecision decision)
    {
        if(traceHandler is null)
        {
            return;
        }

        Variable variable = decision.Kind == PlannerDecisionKind.DescendVariable ? decision.Variable : default;
        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.PlannerDecision(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId, variable);

        traceHandler(in evt);
    }

    private void EmitSolutionYielded()
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.SolutionYielded(sequence, timeProvider.GetUtcNow().UtcTicks, correlationId);

        traceHandler(in evt);
    }

    private void EmitAccessDenied(EncodedTriple denied)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.AccessDenied(
            sequence,
            timeProvider.GetUtcNow().UtcTicks,
            correlationId,
            denied.Subject.Encoded,
            denied.Predicate.Encoded,
            denied.Object.Encoded);

        traceHandler(in evt);
    }
}
