using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The reasoned-engine production-wiring concurrency arms exercised against the REAL reasoning binding — not
/// the stub the Sparql-layer wiring tests use. Arm (b) is the D3 divergence-discard backstop through a
/// <see cref="ReasoningMaintenance"/> composed exactly as the Database layer composes it: a forced journal-append
/// failure after a successful delegate call fires the single outcome seam <c>landed=false</c> once, invalidates the
/// instance, and the next commit rebuilds from the committed base and serves the correct closure. Arm (c) is the
/// linearizability harness over a reasoned mutable engine, modelling the SERVED SET as the register value: writers
/// overwrite an individual's class (a commit's net delta) and readers observe the maintained served-type set's
/// cardinality (a digest of the served triple set at a query snapshot), and every recorded concurrent history must
/// be linearizable against an integer-register spec.
/// </summary>
/// <remarks>
/// SUBSTITUTION FLAG (§7.4 c): the harness's own checker (<c>LinearizabilityHarnessTests.RegisterLinearizability</c>)
/// is a <see langword="bool"/>-register Wing-Gong checker, private to that test. This arm reuses the SAME iterative
/// Wing-Gong algorithm generalised to an integer register state (<see cref="IntRegisterLinearizability"/>) — a
/// test-harness generalisation, NOT a production change — so the served set's cardinality digest can be the register
/// value. The served-set digest chosen is <c>x</c>'s served <c>rdf:type</c> cardinality: each writer overwrites
/// <c>x</c>'s asserted class to one of three classes whose subclass chains have distinct lengths, so the maintained
/// closure serves a distinct type-set size per world (2, 3, or 4), which distinguishes the register values while a
/// single query snapshot reads them atomically.
/// </remarks>
[TestClass]
internal sealed class ReasonedMutableConcurrencyTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI, canonical so the RL closure recognises it.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdfs:subClassOf</c> IRI, canonical so the RL closure recognises it.</summary>
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The register individual whose served type-set cardinality is the linearizability register's value.</summary>
    private const string RegisterSubject = Ex + "x";

    /// <summary>The three register worlds: a class each writer can overwrite <c>x</c> to, paired with the served type-set cardinality its subclass chain yields.</summary>
    private static readonly (string ClassIri, int ServedTypeCount)[] Worlds =
    [
        (Ex + "A", 2),
        (Ex + "C", 3),
        (Ex + "F", 4),
    ];

    /// <summary>A logical clock giving every operation a strictly increasing call/return tick — a sound coarsening of real time.</summary>
    //A naked field: Interlocked.Increment requires a ref to the storage location.
    private long historyClock;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Encodes a triple over three resolved term identifiers.</summary>
    /// <param name="subject">The subject identifier.</param>
    /// <param name="predicate">The predicate identifier.</param>
    /// <param name="value">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermId subject, TermId predicate, TermId value)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, value.Encoded);
    }

    /// <summary>Resolves an IRI to a term identifier in a dictionary, minting it if new.</summary>
    /// <param name="dictionary">The dictionary the term encodes into.</param>
    /// <param name="iri">The IRI.</param>
    /// <returns>The resolved identifier.</returns>
    private static TermId Resolve(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    /// <summary>Reads all of a store's encoded triples into a set.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The store's triples.</returns>
    private static HashSet<EncodedTriple> Triples(HypertrieGraphStore store)
    {
        return [.. store.Match(TermId.None, TermId.None, TermId.None)];
    }

    /// <summary>
    /// (b) The D3 backstop THROUGH THE REAL BINDING: a fault-injecting journal fails the linearising append AFTER the
    /// maintenance delegate ran, so the closure's atomic base edit stands while the commit does not land. The single
    /// outcome seam fires <c>landed=false</c> exactly once and invalidates the instance; the NEXT commit rebuilds
    /// from the committed base (a <see cref="ReasoningMaintenanceMode.RebuildRequested"/> commit) and serves the
    /// correct closure — query answers equal base ∪ <c>ComputeNaive(base).Derived</c>. This certifies the
    /// discard-recovery loop through the real Owl engine, where the Sparql-layer seam is pinned only with a stub.
    /// </summary>
    [TestMethod]
    public async Task DiscardRecoveryRebuildsAndServesTheCorrectClosureAfterAForcedAppendFailure()
    {
        TermDictionary dictionary = new();
        TermId type = Resolve(dictionary, RdfType);
        TermId subClassOf = Resolve(dictionary, RdfsSubClassOf);
        TermId dog = Resolve(dictionary, Ex + "Dog");
        TermId animal = Resolve(dictionary, Ex + "Animal");
        TermId rex = Resolve(dictionary, Ex + "rex");
        TermId pluto = Resolve(dictionary, Ex + "pluto");

        EncodedTriple dogSubClassOfAnimal = Encode(dog, subClassOf, animal);
        EncodedTriple rexIsDog = Encode(rex, type, dog);
        EncodedTriple plutoIsDog = Encode(pluto, type, dog);
        List<EncodedTriple> initialBase = [dogSubClassOfAnimal, rexIsDog];

        ReasoningMaintenance maintenance = await ReasoningMaintenance
            .CreateAsync(initialBase, dictionary, ReasoningPolicy.Default, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        CapturingReasoningBinding binding = new(maintenance);

        InMemoryDatasetJournal inner = new();
        FaultInjectingDatasetJournal journal = new(inner.AppendDelegate, inner.ReadDelegate);

        MutableSparqlDataset dataset = await MutableSparqlDataset
            .CreateAsync(
                dictionary,
                initialBase,
                [.. maintenance.InitialState.ServedAdditions],
                initialReasoningState: maintenance.InitialState,
                namedGraphs: null,
                journalAppend: journal.AppendDelegate,
                journalRead: journal.ReadDelegate,
                cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        dataset.RegisterMaintenance(binding.Delegate, binding.Outcome);

        //At open the served store carries the entailment the asserted store never does.
        EncodedTriple rexIsAnimal = Encode(rex, type, animal);
        Assert.Contains(rexIsAnimal, Triples(dataset.Snapshot().DefaultGraph!), "The reasoned open serves the subclass entailment.");

        //Force the linearising append to fail after the delegate has run: the session-open Started entry is written
        //before arming, so the next armed append is the Committed one.
        DatasetEditSession failing = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(failing.ConfigureAwait(false))
        {
            await failing.ApplyDeltaAsync(TermId.None, [plutoIsDog], [], TestContext.CancellationToken).ConfigureAwait(false);
            journal.Arm(static _ => true);

            await Assert.ThrowsExactlyAsync<EditSessionConcurrencyException>(
                async () => await failing.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        //D3: the delegate ran, the commit did not land, the outcome fired landed=false exactly once, and the instance
        //is invalidated. The failed commit published nothing, so the served store still holds the open generation.
        Assert.HasCount(1, binding.Outcomes, "The delegate ran before the append failed, so exactly one outcome fired.");
        Assert.IsFalse(binding.Outcomes[0], "A ran-but-did-not-land commit is an invoked, not-landed outcome.");
        Assert.DoesNotContain(Encode(pluto, type, animal), Triples(dataset.Snapshot().DefaultGraph!), "The failed commit published nothing to the served store.");

        //The next commit disarms the journal, re-adds the same triple, rebuilds from the committed base (the diverged
        //instance is discarded), and serves the correct closure of the final committed base.
        journal.Arm(static _ => false);
        DatasetEditSession recovering = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await using(recovering.ConfigureAwait(false))
        {
            await recovering.ApplyDeltaAsync(TermId.None, [plutoIsDog], [], TestContext.CancellationToken).ConfigureAwait(false);
            await recovering.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }

        Assert.HasCount(2, binding.Outcomes, "The recovery commit fired its own outcome.");
        Assert.IsTrue(binding.Outcomes[1], "The recovery commit landed.");
        Assert.IsTrue(binding.LastCommit.RebuildClass, "The invalidated instance rebuilt from the committed base rather than an incremental apply.");
        Assert.AreEqual(ReasoningMaintenanceMode.RebuildRequested, binding.LastCommit.Statistics.Mode, "The discard-recovery commit reports the wiring-rebuild mode.");

        //Query answers == base ∪ ComputeNaive(base).Derived of the FINAL committed base.
        List<EncodedTriple> finalBase = [dogSubClassOfAnimal, rexIsDog, plutoIsDog];
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        OwlRlResult remat = OwlRlClosure.Compute(finalBase, terms, oracle, cancellationToken: TestContext.CancellationToken);
        HashSet<EncodedTriple> expectedServed = [.. finalBase, .. remat.Derived];

        Assert.IsTrue(Triples(dataset.Snapshot().DefaultGraph!).SetEquals(expectedServed), "The rebuilt closure serves exactly base ∪ ComputeNaive(base).Derived of the committed base.");
        Assert.Contains(Encode(pluto, type, animal), Triples(dataset.Snapshot().DefaultGraph!), "The rebuilt closure rederives the new individual's entailment.");
        Assert.Contains(rexIsAnimal, Triples(dataset.Snapshot().DefaultGraph!), "The rebuilt closure keeps the original entailment.");
    }

    /// <summary>(c) A concurrent served-set-register history recorded over a REASONED mutable engine is linearizable for every run: concurrent writers overwrite the individual's class through <c>UpdateAsync</c> while readers observe the maintained served-type cardinality through a query snapshot.</summary>
    [TestMethod]
    public async Task ConcurrentServedSetRegisterHistoryIsLinearizableOverAReasonedEngine()
    {
        const int Actors = 4;
        const int OpsPerActor = 6;

        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(RegisterBaseGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The open seeds x in world A, so the register starts at that world's served type cardinality.
        Assert.AreEqual(Worlds[0].ServedTypeCount, await ReadServedTypeCountAsync(database).ConfigureAwait(false), "The reasoned open serves the initial world's type set.");

        ConcurrentQueue<IntHistoryEvent> history = new();
        Task[] actors = new Task[Actors];
        for(int actor = 0; actor < Actors; actor++)
        {
            actors[actor] = RunRegisterActorAsync(database, actor, OpsPerActor, history);
        }

        await Task.WhenAll(actors).ConfigureAwait(false);

        IntHistoryEvent[] recorded = [.. history];
        Assert.IsTrue(
            IntRegisterLinearizability.IsLinearizable(recorded, initialState: Worlds[0].ServedTypeCount),
            $"The recorded concurrent served-set-register history of {recorded.Length} operations must be linearizable.");
    }

    /// <summary>The integer-register checker accepts a history that has a valid linearization, including one where a read overlaps a write and is ordered before it.</summary>
    [TestMethod]
    public void CheckerAcceptsALinearizableIntegerHistory()
    {
        //W(3) spans ticks 1..4; R()=2 spans 2..3, fully inside the write — it can be linearized before the write
        //(register still 2), then the write. A valid order exists.
        IntHistoryEvent[] history =
        [
            new IntHistoryEvent(0, IntOperationKind.Write, 3, 1, 4),
            new IntHistoryEvent(1, IntOperationKind.Read, 2, 2, 3),
        ];

        Assert.IsTrue(IntRegisterLinearizability.IsLinearizable(history, initialState: 2));
    }

    /// <summary>The integer-register checker rejects a history with no valid linearization — a read strictly after a completed write, observing a value no preceding write produced.</summary>
    [TestMethod]
    public void CheckerRejectsANonLinearizableIntegerHistory()
    {
        //W(3) returns at tick 2; R()=2 is invoked at tick 3 — strictly after the write completed — so the write must
        //precede the read, which then cannot observe 2. No valid order exists.
        IntHistoryEvent[] history =
        [
            new IntHistoryEvent(0, IntOperationKind.Write, 3, 1, 2),
            new IntHistoryEvent(1, IntOperationKind.Read, 2, 3, 4),
        ];

        Assert.IsFalse(IntRegisterLinearizability.IsLinearizable(history, initialState: 2));
    }

    /// <summary>Drives one actor: a deterministic mix of overwrites (a MODIFY that sets the individual's class, a commit's net delta) and reads (a query snapshot of the served type cardinality), recording each as a real-time-stamped history event.</summary>
    /// <param name="database">The shared reasoned mutable database.</param>
    /// <param name="process">The actor's index, which seeds its deterministic op sequence.</param>
    /// <param name="opsPerActor">The number of operations the actor issues.</param>
    /// <param name="history">The shared history sink.</param>
    /// <returns>The actor's completion.</returns>
    private async Task RunRegisterActorAsync(VeritasEngine database, int process, int opsPerActor, ConcurrentQueue<IntHistoryEvent> history)
    {
        for(int op = 0; op < opsPerActor; op++)
        {
            //Two of every three operations overwrite the register; the third reads it. Concurrent writers flip the
            //individual's world while readers observe the served type set the maintained closure produces.
            bool isRead = op % 3 == 2;

            if(isRead)
            {
                long call = NextTick();
                int observed = await ReadServedTypeCountAsync(database).ConfigureAwait(false);
                long ret = NextTick();
                history.Enqueue(new IntHistoryEvent(process, IntOperationKind.Read, observed, call, ret));
            }
            else
            {
                (string classIri, int servedTypeCount) = Worlds[(process + op) % Worlds.Length];
                long call = NextTick();
                await database
                    .UpdateAsync(
                        Utf8Strings.From($"DELETE {{ <{RegisterSubject}> <{RdfType}> ?t }} INSERT {{ <{RegisterSubject}> <{RdfType}> <{classIri}> }} WHERE {{ <{RegisterSubject}> <{RdfType}> ?t }}"),
                        cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                long ret = NextTick();
                history.Enqueue(new IntHistoryEvent(process, IntOperationKind.Write, servedTypeCount, call, ret));
            }
        }
    }

    /// <summary>Reads the register value: the number of <c>rdf:type</c> triples the SERVED store answers for the individual, at a single query snapshot (an atomic digest of the served triple set).</summary>
    /// <param name="database">The database to query.</param>
    /// <returns>The served type-set cardinality of the individual.</returns>
    private async Task<int> ReadServedTypeCountAsync(VeritasEngine database)
    {
        VeritasQueryResult result = await database
            .QueryAsync(Utf8Strings.From($"SELECT ?t WHERE {{ <{RegisterSubject}> <{RdfType}> ?t }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        return result.Bindings!.Solutions.Count;
    }

    /// <summary>The next logical clock tick.</summary>
    /// <returns>A strictly increasing tick.</returns>
    private long NextTick()
    {
        return Interlocked.Increment(ref historyClock);
    }

    /// <summary>
    /// The register base graph: three subclass chains of distinct length (A ⊑ B; C ⊑ D ⊑ E; F ⊑ G ⊑ H ⊑ I) plus the
    /// individual seeded into world A. Overwriting the individual's class to A, C, or F drives the maintained closure
    /// to serve 2, 3, or 4 <c>rdf:type</c> triples for it respectively, so the served-set cardinality distinguishes
    /// the three register values.
    /// </summary>
    /// <returns>The base graph triples.</returns>
    private static IReadOnlyList<DataTriple> RegisterBaseGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "A"), Iri(RdfsSubClassOf), Iri(Ex + "B")),
            new DataTriple(Iri(Ex + "C"), Iri(RdfsSubClassOf), Iri(Ex + "D")),
            new DataTriple(Iri(Ex + "D"), Iri(RdfsSubClassOf), Iri(Ex + "E")),
            new DataTriple(Iri(Ex + "F"), Iri(RdfsSubClassOf), Iri(Ex + "G")),
            new DataTriple(Iri(Ex + "G"), Iri(RdfsSubClassOf), Iri(Ex + "H")),
            new DataTriple(Iri(Ex + "H"), Iri(RdfsSubClassOf), Iri(Ex + "I")),
            new DataTriple(Iri(RegisterSubject), Iri(RdfType), Iri(Ex + "A")),
        ];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>The kind of a recorded register operation.</summary>
    private enum IntOperationKind
    {
        /// <summary>An overwrite of the register (a commit setting the individual's world).</summary>
        Write = 0,

        /// <summary>A read of the register (a query snapshot of the served type cardinality).</summary>
        Read = 1,
    }

    /// <summary>One recorded operation in a concurrent integer-register history.</summary>
    /// <param name="Process">The actor that issued the operation.</param>
    /// <param name="Kind">Whether the operation is a write or a read.</param>
    /// <param name="Value">For a write, the value written; for a read, the value observed.</param>
    /// <param name="Call">The logical tick at which the operation was invoked.</param>
    /// <param name="Return">The logical tick at which the operation returned.</param>
    private readonly record struct IntHistoryEvent(int Process, IntOperationKind Kind, int Value, long Call, long Return);

    /// <summary>
    /// The wrapping maintenance binding used by arm (b): it composes <see cref="ReasoningMaintenance"/> onto the
    /// Sparql-layer <see cref="ClosureMaintenanceDelegate"/> exactly as the Database layer does, and additionally
    /// captures each commit's <see cref="ReasoningMaintainedCommit"/> and records every outcome notification so the
    /// test can assert the single-seam discard-recovery contract.
    /// </summary>
    private sealed class CapturingReasoningBinding
    {
        /// <summary>The owned maintenance object one instance drives.</summary>
        private ReasoningMaintenance Maintenance { get; }

        /// <summary>Every recorded outcome notification's landed value, in order.</summary>
        public List<bool> Outcomes { get; } = [];

        /// <summary>The most recently maintained commit, captured before mapping onto the served delta.</summary>
        public ReasoningMaintainedCommit LastCommit { get; private set; }

        /// <summary>Constructs the binding over a maintenance object.</summary>
        /// <param name="maintenance">The owned maintenance object.</param>
        public CapturingReasoningBinding(ReasoningMaintenance maintenance)
        {
            Maintenance = maintenance;
        }

        /// <summary>The maintenance delegate to register.</summary>
        public ClosureMaintenanceDelegate Delegate => MaintainAsync;

        /// <summary>The outcome delegate to register.</summary>
        public ClosureMaintenanceOutcomeDelegate Outcome => OnOutcome;

        /// <summary>Runs the real maintenance, captures the commit, and maps it onto the served delta.</summary>
        /// <param name="baseAdded">The commit's net asserted additions.</param>
        /// <param name="baseRemoved">The commit's net asserted removals.</param>
        /// <param name="tentativeAssertedStore">The session's tentative post-op asserted default-graph store.</param>
        /// <param name="wholesaleReplace">Whether the caller detected a wholesale default-graph replacement.</param>
        /// <param name="cancellationToken">A token that aborts maintenance.</param>
        /// <returns>The served delta, the overlay flag, and the captured commit as the opaque payload.</returns>
        private async ValueTask<MaintainedCommitDelta> MaintainAsync(
            IReadOnlyCollection<EncodedTriple> baseAdded,
            IReadOnlyCollection<EncodedTriple> baseRemoved,
            HypertrieGraphStore tentativeAssertedStore,
            bool wholesaleReplace,
            CancellationToken cancellationToken)
        {
            ReasoningMaintainedCommit commit = await Maintenance
                .MaintainCommit(baseAdded, baseRemoved, tentativeAssertedStore, wholesaleReplace, cancellationToken)
                .ConfigureAwait(false);

            LastCommit = commit;

            return new MaintainedCommitDelta
            {
                ServedAdditions = commit.ServedAdditions,
                ServedRemovals = commit.ServedRemovals,
                OverlayOn = commit.OverlayOn,
                ReasoningState = commit,
            };
        }

        /// <summary>Records the outcome notification and forwards it to the maintenance object (rolling forward on landing, discarding otherwise).</summary>
        /// <param name="landed">Whether the commit landed.</param>
        private void OnOutcome(bool landed)
        {
            Outcomes.Add(landed);
            Maintenance.OnCommitOutcome(landed);
        }
    }

    /// <summary>
    /// An iterative (non-recursive) Wing-Gong linearizability checker for an INTEGER register — the harness's
    /// boolean-register checker generalised to an integer state so a served-set-cardinality digest can be the
    /// register value. It searches for a total order of the operations that extends the real-time precedence
    /// (operation A before B when A returned before B was invoked) and satisfies the sequential register spec (a read
    /// returns the last written value). Backtracking uses an explicit stack; failed (remaining-set, state) pairs are
    /// memoised, and only operations minimal in the precedence order are tried next.
    /// </summary>
    private static class IntRegisterLinearizability
    {
        /// <summary>One node of the backtracking search.</summary>
        private sealed class SearchFrame
        {
            /// <summary>Initializes a frame.</summary>
            /// <param name="remaining">The bitmask of operations not yet linearized.</param>
            /// <param name="state">The register state reached by the operations linearized so far.</param>
            public SearchFrame(long remaining, int state)
            {
                Remaining = remaining;
                State = state;
                Cursor = 0;
            }

            /// <summary>The bitmask of operations not yet linearized at this node.</summary>
            public long Remaining { get; }

            /// <summary>The register state reached by the operations linearized before this node.</summary>
            public int State { get; }

            /// <summary>The next operation index to consider as the candidate to linearize next.</summary>
            public int Cursor { get; set; }
        }

        /// <summary>Determines whether a recorded history is linearizable against the integer-register spec.</summary>
        /// <param name="history">The recorded operations, in any order (their call/return ticks carry the real-time precedence).</param>
        /// <param name="initialState">The register's value before any operation.</param>
        /// <returns><see langword="true"/> when a valid linearization exists.</returns>
        public static bool IsLinearizable(IReadOnlyList<IntHistoryEvent> history, int initialState)
        {
            int count = history.Count;
            if(count == 0)
            {
                return true;
            }

            if(count > 62)
            {
                throw new System.ArgumentException("The history is too large for the bitmask search; keep it under 63 operations.", nameof(history));
            }

            long full = (1L << count) - 1;
            HashSet<(long Remaining, int State)> failed = [];
            Stack<SearchFrame> stack = new();
            stack.Push(new SearchFrame(full, initialState));

            while(stack.Count > 0)
            {
                SearchFrame frame = stack.Peek();
                if(frame.Remaining == 0)
                {
                    return true;
                }

                bool descended = false;
                while(frame.Cursor < count)
                {
                    int index = frame.Cursor;
                    frame.Cursor++;

                    long bit = 1L << index;
                    if((frame.Remaining & bit) == 0 || !IsMinimal(history, frame.Remaining, index))
                    {
                        continue;
                    }

                    IntHistoryEvent candidate = history[index];
                    int childState;
                    if(candidate.Kind == IntOperationKind.Read)
                    {
                        //A read can be linearized here only if it observed the current register state.
                        if(candidate.Value != frame.State)
                        {
                            continue;
                        }

                        childState = frame.State;
                    }
                    else
                    {
                        childState = candidate.Value;
                    }

                    long childRemaining = frame.Remaining & ~bit;
                    if(failed.Contains((childRemaining, childState)))
                    {
                        continue;
                    }

                    stack.Push(new SearchFrame(childRemaining, childState));
                    descended = true;
                    break;
                }

                if(!descended)
                {
                    //This (remaining, state) node is a dead end: record it so no other path re-explores it.
                    failed.Add((frame.Remaining, frame.State));
                    stack.Pop();
                }
            }

            return false;
        }

        /// <summary>Determines whether an operation is minimal in the real-time precedence among the remaining operations — no remaining operation is required to precede it.</summary>
        /// <param name="history">The recorded operations.</param>
        /// <param name="remaining">The bitmask of operations not yet linearized.</param>
        /// <param name="index">The candidate operation's index.</param>
        /// <returns><see langword="true"/> when the candidate may be linearized next.</returns>
        private static bool IsMinimal(IReadOnlyList<IntHistoryEvent> history, long remaining, int index)
        {
            long candidateCall = history[index].Call;
            for(int other = 0; other < history.Count; other++)
            {
                if(other == index || (remaining & (1L << other)) == 0)
                {
                    continue;
                }

                //An operation that returned before the candidate was invoked must precede it, so the candidate is not
                //minimal while that operation is still unlinearized.
                if(history[other].Return < candidateCall)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
