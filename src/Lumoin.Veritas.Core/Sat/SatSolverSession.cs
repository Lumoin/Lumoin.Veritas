using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Lumoin.Veritas.Core.Sat;

/// <summary>
/// A satisfiability solver built once over a permanent formula that answers a
/// sequence of solves under varying assumptions, reusing the work of the earlier
/// solves. Across calls it keeps the clauses it learned, the variable order its
/// conflicts shaped, and the polarities its assignments settled on, so a related
/// run starts warm rather than from scratch — the amortization that pays off when
/// the same formula is interrogated under assumption set after assumption set, as
/// a description-logic reasoner does per world.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a session and not a call.</b> The stateless <see cref="SatSolver"/>
/// decides one formula-and-assumptions pair from nothing. A session is the
/// conflict-driven, two-watched-literal engine made stateful: it holds the clause
/// arena, the watch lists, the variable-move-to-front order, and the saved phases
/// between calls, and a <see cref="Solve"/> reuses all of them.
/// </para>
/// <para>
/// <b>Why reuse is sound.</b> Assumptions are decided as ordinary branch decisions
/// at levels at and above one, never as level-0 facts. First-UIP analysis omits
/// only level-0 literals, so every learned clause is a consequence of the
/// permanent formula alone — never of the assumptions that happened to be in force
/// when it was learned — and is therefore sound to keep across any later
/// assumption set. A learned clause may mention assumption variables; that is
/// still a formula consequence and merely constrains those variables as the
/// formula requires.
/// </para>
/// <para>
/// <b>Verdicts and the core.</b> A <see cref="SatVerdict"/> carries satisfiability,
/// a model honouring the formula and every assumption when satisfiable, and the
/// failed-assumption core when not — the subset of the supplied assumptions the
/// refutation depends on. Once a conflict proves the formula unsatisfiable on its
/// own, the session latches: every later call returns unsatisfiable with an empty
/// core. The statistics on a verdict are for that single call; a caller folds them
/// across the sequence with <see cref="SatSolveStatistics.Combine"/>.
/// </para>
/// <para>
/// <b>Learned-clause deletion is available but off by default.</b> A very long-lived
/// session would otherwise accumulate learned clauses without bound; an optional
/// literal-block-distance deletion round (enabled through the threshold constructor)
/// caps the live set. It is off by default because, on the bounded modules measured,
/// it costs more than it saves — bounding the database is a memory choice for a
/// long-lived session, not a per-solve speed win — so a disabled session does no
/// per-conflict deletion work and runs no deletion round, and is left for the case
/// that needs it.
/// </para>
/// <para>
/// <b>Each solve can restart on a Luby schedule, off by default.</b> When a positive
/// restart unit is configured, once a solve has spent its conflict budget it abandons
/// the current trail back to the permanent formula's level-0 closure and resumes from
/// the reused learned clauses, variable order, and saved phases — insurance against an
/// unlucky early branch on a heavy-tailed instance, keeping the learned clauses across
/// each restart so the search stays complete. It is off by default because restarts do
/// not measure as a speed win on the bounded, structured modules this solver decides;
/// the capability is retained tunable and as a comparand.
/// </para>
/// <para>
/// <b>The formula grows between solves.</b> A consumer that discovers entailed
/// clauses as it explores — a reasoner learning a modal conflict clause, or minting a
/// fresh atom for a successor world — appends them through <see cref="AddClause"/> and
/// <see cref="EnsureVariableCount"/> between solves and keeps interrogating the same
/// session. Growth only ever adds constraints and fresh variables, so every clause the
/// session already learned stays a consequence of the formula and is sound to keep: a
/// learned clause is a consequence of the clauses present when it was learned, and
/// remains one of any superset. Growing is a between-solves operation — never
/// re-entrant with a <see cref="Solve"/> in progress.
/// </para>
/// </remarks>
public sealed class SatSolverSession : IDisposable
{
    /// <summary>The number of variables; every literal's variable lies below it. Grows through <see cref="EnsureVariableCount"/>.</summary>
    private int VariableCount { get; set; }

    /// <summary>The rented width of the variable-indexed working columns; at least the variable count, doubled ahead of demand so a run of small growths costs amortized constant work per variable.</summary>
    private int Capacity { get; set; }

    /// <summary>The pool the variable-indexed working columns rent from; kept so a growth re-rents them wider from the same pool.</summary>
    private MemoryPool<int> Pool { get; }

    /// <summary>The clause database: the permanent formula then every learned clause, kept across calls.</summary>
    private SatSolver.ClauseArena Arena { get; }

    /// <summary>Per-clause first watched literal code; grows as clauses are learned.</summary>
    private List<int> Watch0 { get; } = [];

    /// <summary>Per-clause second watched literal code; grows as clauses are learned.</summary>
    private List<int> Watch1 { get; } = [];

    /// <summary>Per-clause literal-block distance (distinct decision levels at learning), parallel to <see cref="Watch0"/>; <see cref="NonDeletableLbd"/> for formula clauses and learned units, which deletion never touches.</summary>
    private List<int> ClauseLbd { get; } = [];

    /// <summary>Per-clause deletion flag, parallel to <see cref="Watch0"/>; once set, the clause is detached from every watch list and never inspected again (its arena bytes remain — reclaiming them is deferred).</summary>
    private List<bool> ClauseDeleted { get; } = [];

    /// <summary>Per-literal-code watchers, kept across calls — the watch structure is assignment-independent; re-sized when the variable count grows.</summary>
    private List<SatSolver.Watcher>?[] Watches { get; set; }

    /// <summary>The literal codes of every unit clause — the permanent formula's and every learned one — re-derived at level 0 each call.</summary>
    private List<int> UnitFactCodes { get; } = [];

    /// <summary>The reusable scratch a conflict analysis collects its lower-level literals into.</summary>
    private List<SatLiteral> Learned { get; } = [];

    /// <summary>The reusable scratch a conflict analysis collects its resolution set into, for the move-to-front bump.</summary>
    private List<int> BumpScratch { get; } = [];

    /// <summary>The LBD sentinel marking a clause deletion never removes: a permanent formula clause or a learned unit.</summary>
    private const int NonDeletableLbd = -1;

    /// <summary>
    /// The <c>reduceThreshold</c> value that disables learned-clause deletion: the live
    /// count can never exceed it, so no deletion round ever fires. Pass it to the tunables
    /// constructor to build a session that keeps every learned clause.
    /// </summary>
    public const int DeletionDisabled = int.MaxValue;

    /// <summary>The amount <see cref="ReduceThreshold"/> grows by after each deletion round, so deletion becomes less frequent as the database settles.</summary>
    private const int ReduceIncrement = 300;

    /// <summary>The reusable scratch a deletion round collects its candidate clause indices into.</summary>
    private List<int> ReduceScratch { get; } = [];

    /// <summary>The cached worst-first comparison a deletion round sorts candidates with, so the sort allocates no delegate per round.</summary>
    private Comparison<int> ReduceComparison { get; }

    /// <summary>The per-decision-level generation-stamp column LBD computation dedups distinct levels with, sized one beyond the variable count (a decision level can reach it); a generation bump avoids clearing it per conflict.</summary>
    private IMemoryOwner<int> LbdStampOwner { get; set; }

    /// <summary>The current LBD-stamp generation.</summary>
    private int LbdGeneration { get; set; }

    /// <summary>The number of live (non-deleted) learned non-unit clauses; a deletion round fires when it exceeds <see cref="ReduceThreshold"/>.</summary>
    private int LiveLearnedCount { get; set; }

    /// <summary>The live-learned-clause count at which the next deletion round fires.</summary>
    private int ReduceThreshold { get; set; }

    /// <summary>Whether learned-clause deletion is enabled; when off (the public default) the per-conflict LBD work and every deletion round are skipped.</summary>
    private bool DeletionEnabled { get; }

    /// <summary>
    /// Whether the saved-phase column persists across <see cref="Solve"/> calls. When on,
    /// a solve reuses the polarity each variable last settled on — the similar-solve
    /// accelerator. When off, the column re-fills to the default polarity at each solve
    /// entry, so no solve's branching is seeded by earlier solves' polarities — the
    /// dissimilar-solve trajectory guard. The verdict is history-independent given the
    /// formula and the assumptions under either policy; the returned model is not a
    /// contract under either, because the learned clauses, the variable order, and the
    /// watch structure carry across calls and steer which satisfying assignment the
    /// search lands on.
    /// </summary>
    private bool CarryPhases { get; }

    /// <summary>The Luby restart base unit each solve restarts on; non-positive disables restarts.</summary>
    private int RestartUnit { get; }

    /// <summary>Whether learned clauses are minimized by self-subsumption before being added.</summary>
    private bool Minimize { get; }

    /// <summary>The reusable work stack the self-subsumption minimization walks over, kept across calls.</summary>
    private List<int> MinimizeStack { get; } = [];

    /// <summary>The reusable marked-variable list the minimization leaves the marker column cleared through, kept across calls.</summary>
    private List<int> MinimizeToClear { get; } = [];

    /// <summary>The number of deletion rounds this session has run — a test and measurement hook.</summary>
    internal int ReduceRounds { get; private set; }

    /// <summary>The number of clauses deletion has removed across this session — a test and measurement hook.</summary>
    internal int DeletedClauseTotal { get; private set; }

    /// <summary>The assignment column (-1 unassigned, 0 false, 1 true), reset per call.</summary>
    private IMemoryOwner<int> ValuesOwner { get; set; }

    /// <summary>The trail of assigned variables, rebuilt per call.</summary>
    private IMemoryOwner<int> TrailOwner { get; set; }

    /// <summary>The per-variable decision-level column.</summary>
    private IMemoryOwner<int> LevelsOwner { get; set; }

    /// <summary>The per-variable reason column (the clause that forced a variable, or <see cref="SatSolver.NoReason"/>).</summary>
    private IMemoryOwner<int> ReasonsOwner { get; set; }

    /// <summary>The analysis marker column, entered and left cleared.</summary>
    private IMemoryOwner<int> SeenOwner { get; set; }

    /// <summary>The move-to-front next-link column, kept across calls.</summary>
    private IMemoryOwner<int> NextOwner { get; set; }

    /// <summary>The move-to-front previous-link column, kept across calls.</summary>
    private IMemoryOwner<int> PreviousOwner { get; set; }

    /// <summary>The move-to-front stamp column, kept across calls.</summary>
    private IMemoryOwner<int> StampOwner { get; set; }

    /// <summary>The saved-phase column, kept across calls so a decision reuses the polarity a variable last held.</summary>
    private IMemoryOwner<int> SavedPhaseOwner { get; set; }

    /// <summary>The move-to-front head carried between calls; <c>-1</c> when there are no variables.</summary>
    private int VmtfHead { get; set; }

    /// <summary>The move-to-front bump counter carried between calls.</summary>
    private int VmtfLastStamp { get; set; }

    /// <summary>Whether the permanent formula has been proven unsatisfiable on its own; once set, every call refutes with an empty core.</summary>
    private bool FormulaUnsatisfiable { get; set; }

    /// <summary>Whether the session has been disposed.</summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Builds a session over a permanent formula: installs the watch state, records
    /// the unit clauses for per-call level-0 re-derivation, and seeds the
    /// variable-move-to-front order. An empty clause latches the formula
    /// unsatisfiable.
    /// </summary>
    /// <param name="clauses">The permanent formula, each clause a disjunction of literals.</param>
    /// <param name="variableCount">The number of variables; every literal's <see cref="SatLiteral.Variable"/> must lie below it.</param>
    /// <param name="pool">The pool the variable-indexed working columns rent from; <c>null</c> uses <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative or a literal indexes beyond it.</exception>
    public SatSolverSession(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses, int variableCount, MemoryPool<int>? pool = null)
        : this(clauses, variableCount, DeletionDisabled, SatSolver.DefaultRestartUnit, SatSolver.DefaultMinimize, carryPhases: true, pool: pool)
    {
    }

    /// <summary>
    /// Builds a session with explicit tunables: the learned-clause deletion threshold
    /// (<see cref="DeletionDisabled"/> to keep every clause), the Luby restart base unit,
    /// self-subsumption minimization, and whether saved phases carry across solves. The
    /// simple constructor delegates here with production defaults; a consumer that drives
    /// dissimilar solves over one formula passes <paramref name="carryPhases"/> false so
    /// no solve's branching is seeded by earlier solves' polarities, and tests pass a low
    /// threshold or a low restart unit to exercise deletion or restarts on small instances
    /// an oracle can check.
    /// </summary>
    /// <param name="clauses">The permanent formula, each clause a disjunction of literals.</param>
    /// <param name="variableCount">The number of variables; every literal's <see cref="SatLiteral.Variable"/> must lie below it.</param>
    /// <param name="reduceThreshold">The live-learned-clause count at which the first deletion round fires; <see cref="DeletionDisabled"/> disables deletion.</param>
    /// <param name="restartUnit">The Luby restart base unit — the conflict budget of a solve's first restart interval; a non-positive value disables restarts, the retained comparand.</param>
    /// <param name="minimize">Whether to minimize learned clauses by self-subsumption before adding them; the on/off comparand.</param>
    /// <param name="carryPhases">Whether the saved-phase column persists across solves; <see langword="false"/> re-fills it to the default polarity at each solve so no solve's branching is seeded by earlier solves' polarities. The verdict is history-independent under either policy; the returned model is a contract under neither, because carried learned clauses steer which satisfying assignment the search lands on.</param>
    /// <param name="pool">The pool the variable-indexed working columns rent from; <c>null</c> uses <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative or a literal indexes beyond it.</exception>
    public SatSolverSession(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses, int variableCount, int reduceThreshold, int restartUnit = SatSolver.DefaultRestartUnit, bool minimize = SatSolver.DefaultMinimize, bool carryPhases = true, MemoryPool<int>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentOutOfRangeException.ThrowIfNegative(variableCount);

        for(int clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            IReadOnlyList<SatLiteral> clause = clauses[clauseIndex];
            for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
            {
                SatLiteral literal = clause[literalIndex];
                if(literal.Variable < 0 || literal.Variable >= variableCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(clauses), literal.Variable, $"A literal indexes variable {literal.Variable} outside [0, {variableCount}).");
                }
            }
        }

        VariableCount = variableCount;
        ReduceThreshold = reduceThreshold;
        DeletionEnabled = reduceThreshold != DeletionDisabled;
        RestartUnit = restartUnit;
        Minimize = minimize;
        CarryPhases = carryPhases;
        ReduceComparison = CompareForReduction;
        int width = Math.Max(variableCount, 1);
        Capacity = width;
        MemoryPool<int> effectivePool = pool ?? MemoryPool<int>.Shared;
        Pool = effectivePool;
        ValuesOwner = effectivePool.Rent(width);
        TrailOwner = effectivePool.Rent(width);
        LevelsOwner = effectivePool.Rent(width);
        ReasonsOwner = effectivePool.Rent(width);
        SeenOwner = effectivePool.Rent(width);
        NextOwner = effectivePool.Rent(width);
        PreviousOwner = effectivePool.Rent(width);
        StampOwner = effectivePool.Rent(width);
        SavedPhaseOwner = effectivePool.Rent(width);

        //One beyond the variable count: a decision level can reach the variable count
        //when every variable is a decision. Cleared so the first generation (1) is fresh.
        LbdStampOwner = effectivePool.Rent(width + 1);
        LbdStampOwner.Memory.Span[..(width + 1)].Clear();

        Arena = new SatSolver.ClauseArena(clauses);
        Watches = new List<SatSolver.Watcher>?[2 * width];

        //The watch structure is built once and kept: install watches for every
        //clause of width >= 2 on its first two literal codes, and record each unit
        //clause's code so it is forced at level 0 on every call. An empty clause
        //refutes the formula outright.
        for(int clauseIndex = 0; clauseIndex < Arena.Count; clauseIndex++)
        {
            ReadOnlySpan<int> clause = Arena.Literals(clauseIndex);
            Watch0.Add(-1);
            Watch1.Add(-1);
            ClauseLbd.Add(NonDeletableLbd);
            ClauseDeleted.Add(false);
            if(clause.Length == 0)
            {
                FormulaUnsatisfiable = true;

                continue;
            }

            if(clause.Length == 1)
            {
                UnitFactCodes.Add(clause[0]);

                continue;
            }

            int code0 = clause[0];
            int code1 = clause[1];
            Watch0[clauseIndex] = code0;
            Watch1[clauseIndex] = code1;
            (Watches[code0] ??= []).Add(new SatSolver.Watcher(clauseIndex, code1));
            (Watches[code1] ??= []).Add(new SatSolver.Watcher(clauseIndex, code0));
        }

        //Seed the move-to-front order over the columns and capture the head and bump
        //counter; later calls resume from these through the adopt constructor.
        SatSolver.VmtfQueue seed = new(NextOwner.Memory.Span[..variableCount], PreviousOwner.Memory.Span[..variableCount], StampOwner.Memory.Span[..variableCount], variableCount);
        VmtfHead = seed.Head;
        VmtfLastStamp = seed.LastStamp;

        //The first decision of an untouched variable reuses the true-first polarity
        //the scan engines branch on.
        SavedPhaseOwner.Memory.Span[..variableCount].Fill(1);
    }

    /// <summary>
    /// Grows the session to cover at least <paramref name="variableCount"/> variables,
    /// so a consumer that mints fresh variables between solves — a reasoner spawning
    /// successor worlds, say — keeps interrogating one session. Growth never shrinks; a
    /// request at or below the current count does nothing. Each new variable enters the
    /// move-to-front order at the head with a fresh maximum stamp — the move a bump
    /// makes, so the strictly-decreasing stamp order the decision walk relies on is
    /// preserved — and takes a true-first saved phase; it carries no clauses until
    /// <see cref="AddClause"/> adds them. Call between solves, never mid-<see cref="Solve"/>.
    /// </summary>
    /// <param name="variableCount">The variable count to cover; every later literal's variable must lie below it.</param>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void EnsureVariableCount(int variableCount)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if(variableCount <= VariableCount)
        {
            return;
        }

        if(variableCount > Capacity)
        {
            GrowCapacity(variableCount);
        }

        Span<int> next = NextOwner.Memory.Span;
        Span<int> previous = PreviousOwner.Memory.Span;
        Span<int> stamp = StampOwner.Memory.Span;
        Span<int> savedPhase = SavedPhaseOwner.Memory.Span;
        for(int variable = VariableCount; variable < variableCount; variable++)
        {
            //Splice at the head with the new maximum stamp, exactly as Bump does: the
            //last stamp handed out bounds every existing stamp, so one above it is a
            //strict maximum and the list stays sorted decreasing from the head. As with
            //Bump's counter, a single bounded decision cannot approach the int range —
            //the watch array would exhaust memory near a billion variables first — so no
            //renumber guard is reached here.
            stamp[variable] = ++VmtfLastStamp;
            previous[variable] = -1;
            next[variable] = VmtfHead;
            if(VmtfHead != -1)
            {
                previous[VmtfHead] = variable;
            }

            VmtfHead = variable;
            savedPhase[variable] = 1;
        }

        VariableCount = variableCount;
    }

    /// <summary>
    /// Re-rents the variable-indexed working columns and the watch array at a larger
    /// capacity, copying the state that carries across solves — the move-to-front links
    /// and stamps, the saved phases, and the watch lists — while the per-solve scratch
    /// columns re-rent uncopied because the next solve clears or rebuilds each. Capacity
    /// at least doubles so a run of small growths costs amortized constant work.
    /// </summary>
    /// <param name="required">The variable count the new capacity must cover.</param>
    private void GrowCapacity(int required)
    {
        int newCapacity = Math.Max(required, Capacity * 2);
        int oldVariableCount = VariableCount;

        //Rent every replacement buffer and allocate the wider watch array FIRST, while the
        //session's fields still point at the live buffers. Only once every allocation has
        //succeeded are the old owners disposed and the fields swapped, so the growth is
        //failure-atomic: a failed rent (an out-of-memory) disposes whatever it had already
        //rented and rethrows, leaving the session exactly as it was — no field left
        //pointing at a disposed owner, nothing half-swapped.
        IMemoryOwner<int>? newNext = null;
        IMemoryOwner<int>? newPrevious = null;
        IMemoryOwner<int>? newStamp = null;
        IMemoryOwner<int>? newSavedPhase = null;
        IMemoryOwner<int>? newValues = null;
        IMemoryOwner<int>? newTrail = null;
        IMemoryOwner<int>? newLevels = null;
        IMemoryOwner<int>? newReasons = null;
        IMemoryOwner<int>? newSeen = null;
        IMemoryOwner<int>? newLbdStamp = null;
        List<SatSolver.Watcher>?[] newWatches;
        try
        {
            newNext = Pool.Rent(newCapacity);
            newPrevious = Pool.Rent(newCapacity);
            newStamp = Pool.Rent(newCapacity);
            newSavedPhase = Pool.Rent(newCapacity);
            newValues = Pool.Rent(newCapacity);
            newTrail = Pool.Rent(newCapacity);
            newLevels = Pool.Rent(newCapacity);
            newReasons = Pool.Rent(newCapacity);
            newSeen = Pool.Rent(newCapacity);

            //The LBD stamp column is indexed by decision level, which can reach the
            //variable count, so it needs one beyond capacity.
            newLbdStamp = Pool.Rent(newCapacity + 1);

            //Watches are indexed by literal code (2*variable + polarity).
            newWatches = new List<SatSolver.Watcher>?[2 * newCapacity];
        }
        catch
        {
            newNext?.Dispose();
            newPrevious?.Dispose();
            newStamp?.Dispose();
            newSavedPhase?.Dispose();
            newValues?.Dispose();
            newTrail?.Dispose();
            newLevels?.Dispose();
            newReasons?.Dispose();
            newSeen?.Dispose();
            newLbdStamp?.Dispose();

            throw;
        }

        //Every allocation succeeded, so the remaining work does not throw. Copy the state
        //that carries across solves — the move-to-front links and stamps, the saved
        //phases, and the watch lists (their existing-variable codes are unchanged, so the
        //live prefix copies over and the new slots stay null) — into the new buffers. The
        //per-solve scratch columns (values, trail, levels, reasons, seen) carry nothing
        //between solves, so they are left for the next solve to fill. A generation stamp
        //dedups the LBD column without clearing per conflict, so the fresh buffer is
        //cleared once to a value no live generation matches.
        NextOwner.Memory.Span[..oldVariableCount].CopyTo(newNext.Memory.Span);
        PreviousOwner.Memory.Span[..oldVariableCount].CopyTo(newPrevious.Memory.Span);
        StampOwner.Memory.Span[..oldVariableCount].CopyTo(newStamp.Memory.Span);
        SavedPhaseOwner.Memory.Span[..oldVariableCount].CopyTo(newSavedPhase.Memory.Span);
        newLbdStamp.Memory.Span[..(newCapacity + 1)].Clear();
        Array.Copy(Watches, newWatches, 2 * oldVariableCount);

        NextOwner.Dispose();
        NextOwner = newNext;
        PreviousOwner.Dispose();
        PreviousOwner = newPrevious;
        StampOwner.Dispose();
        StampOwner = newStamp;
        SavedPhaseOwner.Dispose();
        SavedPhaseOwner = newSavedPhase;
        ValuesOwner.Dispose();
        ValuesOwner = newValues;
        TrailOwner.Dispose();
        TrailOwner = newTrail;
        LevelsOwner.Dispose();
        LevelsOwner = newLevels;
        ReasonsOwner.Dispose();
        ReasonsOwner = newReasons;
        SeenOwner.Dispose();
        SeenOwner = newSeen;
        LbdStampOwner.Dispose();
        LbdStampOwner = newLbdStamp;
        Watches = newWatches;

        Capacity = newCapacity;
    }

    /// <summary>
    /// Appends a clause to the permanent formula between solves, so a consumer that
    /// discovers entailed clauses as it explores keeps interrogating one session. The
    /// clause joins the arena and the watch structure exactly as a formula clause did at
    /// construction; because growth only adds a constraint, every clause the session has
    /// already learned stays a consequence of the formula and is sound to keep. An empty
    /// clause latches the formula unsatisfiable; a unit joins the level-0 facts
    /// re-derived each solve. Every literal's variable must lie below the current
    /// variable count — grow first with <see cref="EnsureVariableCount"/>. Call between
    /// solves, never mid-<see cref="Solve"/>.
    /// </summary>
    /// <param name="clause">The clause to append, a disjunction of literals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clause"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A literal indexes a variable outside the session's range.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void AddClause(IReadOnlyList<SatLiteral> clause)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentNullException.ThrowIfNull(clause);

        for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
        {
            SatLiteral literal = clause[literalIndex];
            if(literal.Variable < 0 || literal.Variable >= VariableCount)
            {
                throw new ArgumentOutOfRangeException(nameof(clause), literal.Variable, $"A clause literal indexes variable {literal.Variable} outside [0, {VariableCount}).");
            }
        }

        int clauseIndex = Arena.Add(clause);
        Watch0.Add(-1);
        Watch1.Add(-1);
        ClauseLbd.Add(NonDeletableLbd);
        ClauseDeleted.Add(false);
        if(clause.Count == 0)
        {
            FormulaUnsatisfiable = true;

            return;
        }

        if(clause.Count == 1)
        {
            UnitFactCodes.Add(SatSolver.LiteralCode(clause[0]));

            return;
        }

        //Nothing is assigned between solves — the next solve clears the assignment
        //before propagating — so watching the first two literals is a valid watch state,
        //exactly as at construction.
        int code0 = SatSolver.LiteralCode(clause[0]);
        int code1 = SatSolver.LiteralCode(clause[1]);
        Watch0[clauseIndex] = code0;
        Watch1[clauseIndex] = code1;
        (Watches[code0] ??= []).Add(new SatSolver.Watcher(clauseIndex, code1));
        (Watches[code1] ??= []).Add(new SatSolver.Watcher(clauseIndex, code0));
    }

    /// <summary>
    /// Decides the permanent formula under a set of assumptions, reusing the
    /// learned clauses, variable order, and saved phases of every earlier call.
    /// </summary>
    /// <remarks>
    /// A satisfiable verdict carries a model honouring the formula and every
    /// assumption; an unsatisfiable verdict carries the failed-assumption core. The
    /// verdict matches what <see cref="SatSolver.SolveUnderAssumptions"/> would
    /// decide for the same formula and assumptions; the model or core need not be
    /// identical, since the reused state changes the search path, not the answer.
    /// </remarks>
    /// <param name="assumptions">The literals fixed for this call; each variable must lie below the session's variable count.</param>
    /// <param name="progress">An optional observer invoked once per propagation round with this call's search counters, before the round's cancellation check; <see langword="null"/> observes nothing at the cost of one null check per round.</param>
    /// <param name="cancellationToken">A token that aborts this call between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists, or the failed-assumption core when none does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assumptions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An assumption indexes a variable outside the session's range.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public SatVerdict Solve(IReadOnlyList<SatLiteral> assumptions, SatSolveProgressDelegate? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentNullException.ThrowIfNull(assumptions);

        for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
        {
            SatLiteral assumption = assumptions[assumptionIndex];
            if(assumption.Variable < 0 || assumption.Variable >= VariableCount)
            {
                throw new ArgumentOutOfRangeException(nameof(assumptions), assumption.Variable, $"An assumption indexes variable {assumption.Variable} outside [0, {VariableCount}).");
            }
        }

        //A formula already refuted on its own stays refuted; no assumption participates.
        if(FormulaUnsatisfiable)
        {
            return new SatVerdict(false, null, [], SatSolveStatistics.Empty);
        }

        Span<int> values = ValuesOwner.Memory.Span[..VariableCount];
        Span<int> trail = TrailOwner.Memory.Span[..VariableCount];
        Span<int> levels = LevelsOwner.Memory.Span[..VariableCount];
        Span<int> reasons = ReasonsOwner.Memory.Span[..VariableCount];
        Span<int> seen = SeenOwner.Memory.Span[..VariableCount];
        Span<int> savedPhase = SavedPhaseOwner.Memory.Span[..VariableCount];

        //Reset the assignment but not the reused state: clear values and re-derive
        //the level-0 closure (the permanent unit facts and their propagation), while
        //the learned clauses, the watch lists, and the move-to-front order carry over
        //from earlier calls. The saved-phase column carries too when phase carry is on;
        //when it is off it re-fills to the default true-first polarity here — the same
        //value the constructor and the grow path seed — so no branching decision in this
        //solve is seeded by an earlier solve's polarities.
        values.Fill(-1);
        seen.Clear();
        if(!CarryPhases)
        {
            savedPhase.Fill(1);
        }

        int trailCount = 0;
        int propagatedCount = 0;
        int currentLevel = 0;
        int decisions = 0;
        long propagations = 0;
        int conflicts = 0;
        int learnedClauses = 0;
        int maxDecisionLevel = 0;

        //This solve's restart schedule, fresh per call while the learned clauses,
        //variable order, and saved phases carry over: conflicts since the last
        //restart, the reluctant-doubling Luby state, and the next restart's conflict
        //budget. A non-positive unit leaves the budget unused and never restarts.
        int restarts = 0;
        int conflictsSinceRestart = 0;
        long lubyU = 1;
        long lubyV = 1;
        long restartLimit = (long)RestartUnit * lubyV;

        //Bound the learned-clause database before the search (only when deletion is
        //enabled): with nothing assigned (values just cleared, the trail empty), no
        //learned clause is a live reason, so the worst clauses can be dropped without
        //the "never delete a locked reason" constraint a mid-search deletion imposes.
        if(DeletionEnabled && LiveLearnedCount > ReduceThreshold)
        {
            ReduceDb();
        }

        foreach(int code in UnitFactCodes)
        {
            int variable = code >> 1;
            int wanted = code & 1;
            int current = values[variable];
            if(current == -1)
            {
                values[variable] = wanted;
                levels[variable] = 0;
                reasons[variable] = SatSolver.NoReason;
                trail[trailCount++] = variable;
            }
            else if(current != wanted)
            {
                FormulaUnsatisfiable = true;

                return new SatVerdict(false, null, [], SatSolveStatistics.Empty);
            }
        }

        SatSolver.VmtfQueue queue = new(NextOwner.Memory.Span[..VariableCount], PreviousOwner.Memory.Span[..VariableCount], StampOwner.Memory.Span[..VariableCount], VmtfHead, VmtfLastStamp);

        while(true)
        {
            if(progress is not null)
            {
                progress(new SatSolveProgress(decisions, conflicts, propagations, learnedClauses, restarts, currentLevel));
            }

            cancellationToken.ThrowIfCancellationRequested();

            int conflict = SatSolver.PropagateWatched(Arena, Watch0, Watch1, Watches, values, levels, reasons, trail, ref trailCount, ref propagatedCount, currentLevel, ref propagations);
            if(conflict >= 0)
            {
                conflicts++;
                conflictsSinceRestart++;

                //A conflict with no decision on the trail uses only the permanent
                //formula and its level-0 closure: the formula is unsatisfiable on its
                //own. Latch it so every later call refutes immediately.
                if(currentLevel == 0)
                {
                    FormulaUnsatisfiable = true;

                    return new SatVerdict(false, null, [], new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
                }

                int backjumpLevel = SatSolver.Analyze(Arena, conflict, values, levels, reasons, trail, trailCount, currentLevel, seen, Learned, BumpScratch, Minimize ? MinimizeStack : null, Minimize ? MinimizeToClear : null, out SatLiteral asserting);

                //The learned clause's literal-block distance — the count of distinct
                //decision levels among its literals — read now while levels[] still
                //holds the asserting variable's level (its assignment below overwrites
                //it). A generation stamp dedups levels without clearing the column.
                //Computed only when deletion is enabled (a disabled session pays
                //nothing); a unit is never deleted, so it carries no real LBD.
                int learnedLbd;
                if(!DeletionEnabled || Learned.Count == 1)
                {
                    learnedLbd = NonDeletableLbd;
                }
                else
                {
                    Span<int> lbdStamp = LbdStampOwner.Memory.Span[..(Math.Max(VariableCount, 1) + 1)];
                    if(LbdGeneration == int.MaxValue)
                    {
                        lbdStamp.Clear();
                        LbdGeneration = 0;
                    }

                    int generation = ++LbdGeneration;
                    learnedLbd = 0;
                    foreach(SatLiteral literal in Learned)
                    {
                        int level = levels[literal.Variable];
                        if(lbdStamp[level] != generation)
                        {
                            lbdStamp[level] = generation;
                            learnedLbd++;
                        }
                    }
                }

                queue.BumpAll(BumpScratch);

                //The bump is the only step in a solve that moves the queue's head or
                //advances its stamp counter, and it mutates the session's columns in
                //place; persist the carry-over scalars right after it so they never
                //diverge from the columns, even if this call later exits by
                //cancellation and the session is reused.
                VmtfHead = queue.Head;
                VmtfLastStamp = queue.LastStamp;

                while(trailCount > 0 && levels[trail[trailCount - 1]] > backjumpLevel)
                {
                    int unassigned = trail[trailCount - 1];
                    savedPhase[unassigned] = values[unassigned];
                    values[unassigned] = -1;
                    queue.NotifyUnassigned(unassigned);
                    trailCount--;
                }

                propagatedCount = trailCount;
                currentLevel = backjumpLevel;

                //Append the learned clause to the permanent database. A unit is a new
                //formula-entailed fact, recorded so it is re-derived at level 0 on
                //every later call; a longer clause watches the asserting literal and
                //the highest-level remaining literal so it propagates after backjump.
                int learnedIndex = Arena.Add(Learned);
                Watch0.Add(-1);
                Watch1.Add(-1);
                ClauseLbd.Add(learnedLbd);
                ClauseDeleted.Add(false);
                learnedClauses++;
                if(Learned.Count == 1)
                {
                    UnitFactCodes.Add(SatSolver.LiteralCode(Learned[0]));
                }
                else
                {
                    LiveLearnedCount++;
                    int secondWatch = 1;
                    int secondLevel = levels[Learned[1].Variable];
                    for(int index = 2; index < Learned.Count; index++)
                    {
                        int level = levels[Learned[index].Variable];
                        if(level > secondLevel)
                        {
                            secondLevel = level;
                            secondWatch = index;
                        }
                    }

                    int code0 = SatSolver.LiteralCode(Learned[0]);
                    int code1 = SatSolver.LiteralCode(Learned[secondWatch]);
                    Watch0[learnedIndex] = code0;
                    Watch1[learnedIndex] = code1;
                    (Watches[code0] ??= []).Add(new SatSolver.Watcher(learnedIndex, code1));
                    (Watches[code1] ??= []).Add(new SatSolver.Watcher(learnedIndex, code0));
                }

                int assertVariable = asserting.Variable;
                values[assertVariable] = asserting.IsPositive ? 1 : 0;
                levels[assertVariable] = backjumpLevel;
                reasons[assertVariable] = learnedIndex;
                trail[trailCount++] = assertVariable;

                continue;
            }

            //Propagation is at a conflict-free fixpoint. Restart first when this call
            //has spent its Luby conflict budget: abandon the current trail back to
            //the permanent formula's level-0 closure — dropping the assumption
            //decisions and search decisions alike, saving each one's polarity — and
            //resume from the learned clauses, variable order, and saved phases this
            //session has accumulated. The loop below then re-places the assumptions
            //from the reused state. It stays complete because the learned clauses are
            //kept across the restart, so the search cannot cycle. Only above level 0
            //(a positive level has something to abandon).
            if(RestartUnit > 0 && currentLevel > 0 && conflictsSinceRestart >= restartLimit)
            {
                while(trailCount > 0 && levels[trail[trailCount - 1]] > 0)
                {
                    int unassigned = trail[trailCount - 1];
                    savedPhase[unassigned] = values[unassigned];
                    values[unassigned] = -1;
                    queue.NotifyUnassigned(unassigned);
                    trailCount--;
                }

                propagatedCount = trailCount;
                currentLevel = 0;
                conflictsSinceRestart = 0;
                restarts++;
                restartLimit = (long)RestartUnit * SatSolver.LubyNext(ref lubyU, ref lubyV);

                continue;
            }

            //Place the next assumption that is not yet satisfied: a falsified one is
            //the refutation, an unassigned one becomes the next decision. Assumptions
            //decided this way sit at levels >= 1, so the learned clauses stay
            //formula-entailed.
            bool placed = false;
            bool refuted = false;
            SatLiteral refutedAssumption = default;
            for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
            {
                SatLiteral assumption = assumptions[assumptionIndex];
                int variable = assumption.Variable;
                int wanted = assumption.IsPositive ? 1 : 0;
                int current = values[variable];
                if(current == wanted)
                {
                    continue;
                }

                if(current == -1)
                {
                    currentLevel++;
                    values[variable] = wanted;
                    levels[variable] = currentLevel;
                    reasons[variable] = SatSolver.NoReason;
                    trail[trailCount++] = variable;
                    decisions++;
                    if(currentLevel > maxDecisionLevel)
                    {
                        maxDecisionLevel = currentLevel;
                    }

                    placed = true;
                }
                else
                {
                    refuted = true;
                    refutedAssumption = assumption;
                }

                break;
            }

            if(refuted)
            {
                List<SatLiteral> core = AnalyzeFinal(refutedAssumption, values, levels, reasons, trail, trailCount, seen);

                return new SatVerdict(false, null, core, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
            }

            if(placed)
            {
                continue;
            }

            //Every assumption is satisfied: branch on the highest-stamped unassigned
            //variable, reusing its saved phase. No variable left means a full model.
            int decisionVariable = queue.Decide(values);
            if(decisionVariable < 0)
            {
                bool[] assignment = new bool[VariableCount];
                for(int i = 0; i < VariableCount; i++)
                {
                    assignment[i] = values[i] == 1;
                }

                return new SatVerdict(true, assignment, AssumptionCore: null, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
            }

            currentLevel++;
            values[decisionVariable] = savedPhase[decisionVariable];
            levels[decisionVariable] = currentLevel;
            reasons[decisionVariable] = SatSolver.NoReason;
            trail[trailCount++] = decisionVariable;
            decisions++;
            if(currentLevel > maxDecisionLevel)
            {
                maxDecisionLevel = currentLevel;
            }
        }
    }

    /// <summary>
    /// Extracts the failed-assumption core from a falsified assumption: starting at
    /// that assumption, walks the trail top-down through reason antecedents and
    /// collects every assumption decision the refutation depends on.
    /// </summary>
    /// <remarks>
    /// Assumptions are the only decisions at levels at and above one when this runs
    /// (a conflict's analysis has already backjumped every search decision away), so
    /// a level-one-or-deeper trail variable with no reason is an assumption. The
    /// returned core is a subset of the supplied assumptions, and re-solving on it
    /// alone reproduces the same forcing of the refuted assumption, so it stays
    /// unsatisfiable. A variable an assumption directly contradicts at level 0
    /// yields the lone refuted assumption.
    /// </remarks>
    /// <param name="refutedAssumption">The assumption found falsified at a propagation fixpoint.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="levels">The per-variable decision-level column.</param>
    /// <param name="reasons">The per-variable reason column.</param>
    /// <param name="trail">The trail.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="seen">The marker column, entered and left cleared.</param>
    /// <returns>The assumptions the refutation depends on, the refuted one first, each once.</returns>
    private List<SatLiteral> AnalyzeFinal(SatLiteral refutedAssumption, Span<int> values, Span<int> levels, Span<int> reasons, Span<int> trail, int trailCount, Span<int> seen)
    {
        List<SatLiteral> core = [refutedAssumption];
        int seedVariable = refutedAssumption.Variable;
        if(levels[seedVariable] == 0)
        {
            return core;
        }

        seen[seedVariable] = 1;
        List<int> touched = [seedVariable];
        for(int index = trailCount - 1; index >= 0; index--)
        {
            int variable = trail[index];
            if(levels[variable] == 0 || seen[variable] == 0)
            {
                continue;
            }

            if(reasons[variable] == SatSolver.NoReason)
            {
                core.Add(new SatLiteral(variable, values[variable] == 1));
            }
            else
            {
                foreach(int code in Arena.Literals(reasons[variable]))
                {
                    int antecedent = code >> 1;
                    if(antecedent != variable && levels[antecedent] > 0 && seen[antecedent] == 0)
                    {
                        seen[antecedent] = 1;
                        touched.Add(antecedent);
                    }
                }
            }

            seen[variable] = 0;
        }

        foreach(int variable in touched)
        {
            seen[variable] = 0;
        }

        return core;
    }

    /// <summary>
    /// Bounds the learned-clause database: marks the worst half of the deletable
    /// learned clauses (highest literal-block distance first) deleted and detaches
    /// them from every watch list in one pass, so propagation never inspects them
    /// again. It runs only at a solve's start with nothing assigned, so no deleted
    /// clause can be a live reason and no reason-locking is needed. A clause is
    /// deletable when its LBD exceeds two, which by the sentinel and the LBD
    /// definition already excludes the permanent formula, learned units, glue
    /// clauses, and binaries. Deleting these entailed, redundant clauses changes the
    /// search path but never the verdict; their arena bytes are not reclaimed.
    /// </summary>
    private void ReduceDb()
    {
        ReduceScratch.Clear();
        for(int clauseIndex = 0; clauseIndex < ClauseLbd.Count; clauseIndex++)
        {
            if(ClauseLbd[clauseIndex] > 2 && !ClauseDeleted[clauseIndex])
            {
                ReduceScratch.Add(clauseIndex);
            }
        }

        //Back off the threshold now, before any early return: the live count includes
        //glue clauses the deletion never removes, so a round can find too few
        //deletable clauses to halve. Raising the threshold regardless keeps such a
        //low-yield round from re-firing its full candidate scan on every later solve.
        ReduceThreshold += ReduceIncrement;
        if(ReduceScratch.Count < 2)
        {
            return;
        }

        ReduceScratch.Sort(ReduceComparison);

        int deleteCount = ReduceScratch.Count / 2;
        for(int index = 0; index < deleteCount; index++)
        {
            int clauseIndex = ReduceScratch[index];
            ClauseDeleted[clauseIndex] = true;
            LiveLearnedCount--;
            DeletedClauseTotal++;
        }

        foreach(List<SatSolver.Watcher>? list in Watches)
        {
            if(list is null)
            {
                continue;
            }

            int write = 0;
            for(int read = 0; read < list.Count; read++)
            {
                if(!ClauseDeleted[list[read].ClauseIndex])
                {
                    list[write] = list[read];
                    write++;
                }
            }

            if(write < list.Count)
            {
                list.RemoveRange(write, list.Count - write);
            }
        }

        ReduceRounds++;
    }

    /// <summary>
    /// Orders deletion candidates worst first: highest literal-block distance, then
    /// longest, then highest index. The order is fully deterministic so the search
    /// stays reproducible; the tie-break is heuristic-only and cannot change the verdict.
    /// </summary>
    /// <param name="leftIndex">The first clause index.</param>
    /// <param name="rightIndex">The second clause index.</param>
    /// <returns>A negative, zero, or positive value placing the worse clause first.</returns>
    private int CompareForReduction(int leftIndex, int rightIndex)
    {
        int byLbd = ClauseLbd[rightIndex].CompareTo(ClauseLbd[leftIndex]);
        if(byLbd != 0)
        {
            return byLbd;
        }

        int byLength = Arena.Literals(rightIndex).Length.CompareTo(Arena.Literals(leftIndex).Length);
        if(byLength != 0)
        {
            return byLength;
        }

        return rightIndex.CompareTo(leftIndex);
    }

    /// <summary>Returns the rented working columns to the pool. Safe to call more than once.</summary>
    public void Dispose()
    {
        if(Disposed)
        {
            return;
        }

        Disposed = true;
        ValuesOwner.Dispose();
        TrailOwner.Dispose();
        LevelsOwner.Dispose();
        ReasonsOwner.Dispose();
        SeenOwner.Dispose();
        NextOwner.Dispose();
        PreviousOwner.Dispose();
        StampOwner.Dispose();
        SavedPhaseOwner.Dispose();
        LbdStampOwner.Dispose();
    }
}
