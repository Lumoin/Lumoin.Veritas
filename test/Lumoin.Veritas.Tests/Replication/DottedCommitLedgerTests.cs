using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The dotted commit ledger's semantics over a live dataset: local commits mint per net addition and drops
/// cancel present dots; re-asserting a present triple is a set-semantics no-op with no event identity; the
/// commit-time adopt-guard skips peer dots the live context covers; a dot union onto a present triple and a
/// partial drop are causality-only commits; and replayed peer knowledge cannot resurrect an observed remove —
/// the resurrection defect the dotted lane exists to kill, at the ledger and write-back level.
/// </summary>
[TestClass]
internal sealed class DottedCommitLedgerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A deterministic replica axis whose 32 bytes all carry <paramref name="seed"/>.</summary>
    /// <param name="seed">The byte every position of the identity carries.</param>
    /// <returns>The axis.</returns>
    private static ReplicaAxis Axis(byte seed)
    {
        byte[] bytes = new byte[ReplicaAxis.ByteWidth];
        Array.Fill(bytes, seed);

        return new ReplicaAxis(bytes);
    }

    /// <summary>A remove-aware dataset and its ledger, wired the way the engine wires them: the baseline annotation rides the Initial entry AND seeds the ledger, the ledger observes the committed delta, and the causality builder mints for local commits.</summary>
    /// <param name="identity">The host identity axis.</param>
    /// <param name="seed">The seed triples the store is created with.</param>
    /// <param name="cancellationToken">A token that aborts the create.</param>
    /// <returns>The dataset and its ledger.</returns>
    private static async Task<(MutableSparqlDataset Dataset, DottedCommitLedger Ledger)> CreateRemoveAwareAsync(ReplicaAxis identity, IReadOnlyList<EncodedTriple> seed, CancellationToken cancellationToken)
    {
        TermDictionary dictionary = new(0xD07);
        CommitCausality baseline = DottedCommitLedger.MintBaseline(identity, seed);
        MutableSparqlDataset dataset = await MutableSparqlDataset.CreateAsync(
            dictionary,
            seed,
            namedGraphs: null,
            journalAppend: null,
            journalRead: null,
            valueIndexes: null,
            initialCausality: baseline,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        DottedCommitLedger ledger = new(identity, baseline, dataset.StateId);
        dataset.ObserveDefaultGraphDelta(ledger.OnDefaultGraphDelta);
        dataset.RegisterCausalityBuilder(ledger.BuildLocalCausality);

        return (dataset, ledger);
    }

    /// <summary>Commits one default-graph delta through an edit session.</summary>
    /// <param name="dataset">The dataset to commit into.</param>
    /// <param name="additions">The triples to add.</param>
    /// <param name="removals">The triples to remove.</param>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    private static async Task CommitAsync(MutableSparqlDataset dataset, IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, CancellationToken cancellationToken)
    {
        DatasetEditSession session = await dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await session.ApplyDeltaAsync(TermId.None, additions, removals, cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Finds a triple's dotted entry in a snapshot, or fails the assertion when it is absent.</summary>
    /// <param name="snapshot">The ledger snapshot.</param>
    /// <param name="triple">The triple to find.</param>
    /// <returns>The entry.</returns>
    private static DottedTripleAssignment FindEntry(DottedLedgerSnapshot snapshot, EncodedTriple triple)
    {
        foreach(DottedTripleAssignment entry in snapshot.Entries)
        {
            if(entry.Triple == triple)
            {
                return entry;
            }
        }

        Assert.Fail($"The snapshot holds no entry for triple {triple.Subject.Encoded} {triple.Predicate.Encoded} {triple.Object.Encoded}.");

        return default;
    }

    /// <summary>Whether the committed default graph holds a triple, probed by exact match.</summary>
    /// <param name="dataset">The dataset whose committed default graph is probed.</param>
    /// <param name="triple">The triple to probe.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool GraphHolds(MutableSparqlDataset dataset, EncodedTriple triple)
    {
        foreach(EncodedTriple _ in dataset.DefaultGraph.Match(triple.Subject, triple.Predicate, triple.Object))
        {
            return true;
        }

        return false;
    }

    /// <summary>Whether a snapshot holds an entry for a triple.</summary>
    /// <param name="snapshot">The ledger snapshot.</param>
    /// <param name="triple">The triple to probe.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasEntry(DottedLedgerSnapshot snapshot, EncodedTriple triple)
    {
        foreach(DottedTripleAssignment entry in snapshot.Entries)
        {
            if(entry.Triple == triple)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The baseline dots every seed triple exactly once on the supplied axis, with counters 1 through the seed count, and the context covers exactly those mints.</summary>
    [TestMethod]
    public async Task BaselineDotsEverySeedTripleOnTheSuppliedAxis()
    {
        ReplicaAxis identity = Axis(0x0A);
        EncodedTriple one = EncodedTriple.FromEncoded(1, 100, 2);
        EncodedTriple two = EncodedTriple.FromEncoded(2, 100, 3);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [one, two], TestContext.CancellationToken).ConfigureAwait(false);

        DottedLedgerSnapshot snapshot = ledger.Snapshot();
        Assert.HasCount(2, snapshot.Entries);
        Assert.HasCount(1, FindEntry(snapshot, one).Dots, "A baseline mints exactly one dot per seed triple.");
        Assert.AreEqual(2UL, snapshot.Context.PrefixMaxOn(identity), "The baseline's context covers exactly the minted counters.");
        Assert.AreEqual(dataset.StateId, snapshot.StateId, "The ledger is seeded with the dataset's actual StateId, never a default stamp.");
    }

    /// <summary>A local commit mints one fresh dot per net addition with counter continuity from the context, and a retraction drops the triple's present dots while the context keeps covering them — coverage plus absence IS the tombstone.</summary>
    [TestMethod]
    public async Task LocalCommitsMintPerNetAdditionAndDropsCancelPresentDots()
    {
        ReplicaAxis identity = Axis(0x0A);
        EncodedTriple seeded = EncodedTriple.FromEncoded(1, 100, 2);
        EncodedTriple added = EncodedTriple.FromEncoded(2, 100, 3);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [seeded], TestContext.CancellationToken).ConfigureAwait(false);

        await CommitAsync(dataset, [added], [], TestContext.CancellationToken).ConfigureAwait(false);

        DottedLedgerSnapshot afterAdd = ledger.Snapshot();
        CausalDot mintedDot = FindEntry(afterAdd, added).Dots[0];
        Assert.AreEqual(new CausalDot(identity, 2), mintedDot, "The mint continues the axis's counter past the baseline.");
        Assert.AreEqual(dataset.StateId, afterAdd.StateId, "The ledger advances to the committed StateId inside the same publish.");

        await CommitAsync(dataset, [], [added], TestContext.CancellationToken).ConfigureAwait(false);

        DottedLedgerSnapshot afterDrop = ledger.Snapshot();
        Assert.IsFalse(HasEntry(afterDrop, added), "A retraction removes the triple's entry.");
        Assert.IsTrue(afterDrop.Context.Covers(mintedDot), "The context still covers the dropped dot — that coverage is the tombstone.");
        Assert.IsTrue(HasEntry(afterDrop, seeded), "An unrelated entry is untouched.");
    }

    /// <summary>Re-asserting a present triple is a set-semantics no-op: no journal transition, no StateId movement, no fresh dot — the decided semantics that a peer's observed remove of the existing dots takes the triple.</summary>
    [TestMethod]
    public async Task ReassertingAPresentTripleMintsNothing()
    {
        ReplicaAxis identity = Axis(0x0A);
        EncodedTriple present = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [present], TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier stateBefore = dataset.StateId;

        await CommitAsync(dataset, [present], [], TestContext.CancellationToken).ConfigureAwait(false);

        DottedLedgerSnapshot snapshot = ledger.Snapshot();
        Assert.AreEqual(stateBefore, dataset.StateId, "A re-assert of a present triple commits no transition.");
        Assert.AreEqual(1UL, snapshot.Context.MaxOn(identity), "No fresh dot is minted — the re-assert is unobservable in principle.");
        Assert.AreEqual(new CausalDot(identity, 1), FindEntry(snapshot, present).Dots[0], "The entry keeps its original dot.");
    }

    /// <summary>The commit-time adopt-guard: a peer dot the live context covers is skipped (it became a tombstone here), while an uncovered dot on an absent triple is a genuine add — planned per attempt against the live ledger.</summary>
    [TestMethod]
    public async Task AdoptGuardSkipsDotsTheLiveContextCovers()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple retracted = EncodedTriple.FromEncoded(1, 100, 2);
        EncodedTriple fresh = EncodedTriple.FromEncoded(2, 100, 3);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [retracted], TestContext.CancellationToken).ConfigureAwait(false);

        //Retract locally: the triple's baseline dot becomes covered-but-absent — the tombstone.
        await CommitAsync(dataset, [], [retracted], TestContext.CancellationToken).ConfigureAwait(false);

        //The peer re-presents the tombstoned entry under the dot this replica already observed, plus a genuine
        //add under a dot it has never seen.
        CausalContext peerContext = new();
        peerContext.Fold(new CausalDot(identity, 1));
        peerContext.Fold(new CausalDot(peer, 1));
        LedgerAdoptPlan plan = ledger.PrepareAdopt(
            [new DottedTripleAssignment(retracted, [new CausalDot(identity, 1)]), new DottedTripleAssignment(fresh, [new CausalDot(peer, 1)])],
            [],
            peerContext);

        Assert.IsTrue(plan.HasWork, "The genuine add gives the plan work.");
        Assert.HasCount(1, plan.EffectiveAdditions, "Only the uncovered entry enters the dataset delta.");
        Assert.HasCount(1, plan.Causality!.Additions, "Only the uncovered entry is adopted.");
        Assert.AreEqual(fresh, plan.Causality.Additions[0].Triple, "The tombstoned entry is skipped, value-based, by context coverage.");
    }

    /// <summary>One adopt names a triple as either an addition or a drop, never both: the two effects are each planned against the same pre-commit entries, so the combined shape could commit a dataset removal the annotation's surviving dots contradict — the ledger refuses it loudly at the contract.</summary>
    [TestMethod]
    public async Task PrepareAdoptRefusesATripleNamedAsBothAdditionAndDrop()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple contested = EncodedTriple.FromEncoded(1, 100, 2);
        (_, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [contested], TestContext.CancellationToken).ConfigureAwait(false);

        CausalContext peerContext = new();
        peerContext.Fold(new CausalDot(peer, 1));
        try
        {
            ledger.PrepareAdopt(
                [new DottedTripleAssignment(contested, [new CausalDot(peer, 1)])],
                [new DottedTripleAssignment(contested, [new CausalDot(identity, 1)])],
                peerContext);
            Assert.Fail("A triple named as both an addition and a drop must refuse at the contract.");
        }
        catch(ArgumentException)
        {
            //The loud refusal IS the pinned contract: the apply seams plan one side per call.
        }
    }

    /// <summary>A peer's independent assert of a PRESENT triple adopts as a dot union with no dataset delta — a causality-only commit: the StateId and committed set are unchanged while the entry carries both dots durably.</summary>
    [TestMethod]
    public async Task AdoptedDotUnionOnAPresentTripleIsACausalityOnlyCommit()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple shared = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [shared], TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier stateBefore = dataset.StateId;

        CausalContext peerContext = new();
        peerContext.Fold(new CausalDot(peer, 1));
        DottedAdoptReceipt outcome = await ReconcileWriteBack.ApplyAdoptAsync(
            dataset,
            ledger,
            [new DottedTripleAssignment(shared, [new CausalDot(peer, 1)])],
            [],
            peerContext,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(WriteBackOutcome.Committed, outcome.Outcome, "The dot union commits durably even though the triple set is unchanged.");
        Assert.AreEqual(1, outcome.AdoptedAdditions, "The receipt counts the committed plan's one adopted addition assignment.");
        Assert.AreEqual(stateBefore, dataset.StateId, "A causality-only commit does not move the committed state.");
        DottedLedgerSnapshot snapshot = ledger.Snapshot();
        Assert.HasCount(2, FindEntry(snapshot, shared).Dots, "The entry carries both concurrent assertion events.");
        Assert.IsTrue(snapshot.Context.Covers(new CausalDot(peer, 1)), "The peer's dot and context are folded.");
    }

    /// <summary>Add-wins over assertion events: a peer drop naming only part of an entry's dots is a causality-only commit that leaves the triple standing under the surviving dot; a later drop of the survivor removes it.</summary>
    [TestMethod]
    public async Task PartialPeerDropLeavesTheConcurrentAssertStanding()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple shared = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [shared], TestContext.CancellationToken).ConfigureAwait(false);

        //Union in the peer's concurrent assertion event first, so the entry stands under two dots.
        CausalContext unionContext = new();
        unionContext.Fold(new CausalDot(peer, 1));
        await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [new DottedTripleAssignment(shared, [new CausalDot(peer, 1)])], [], unionContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //A drop that observed ONLY the local baseline dot: the peer's concurrent assert survives — add-wins.
        CausalContext partialDropContext = new();
        partialDropContext.Fold(new CausalDot(identity, 1));
        DottedAdoptReceipt partial = await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [], [new DottedTripleAssignment(shared, [new CausalDot(identity, 1)])], partialDropContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(WriteBackOutcome.Committed, partial.Outcome, "The partial drop lands durably as a causality-only commit.");
        DottedLedgerSnapshot afterPartial = ledger.Snapshot();
        Assert.AreEqual(new CausalDot(peer, 1), FindEntry(afterPartial, shared).Dots[0], "The triple stands under exactly the surviving concurrent dot.");

        //A drop that observed the survivor removes the entry and the triple.
        CausalContext finalDropContext = new();
        finalDropContext.Fold(new CausalDot(peer, 1));
        DottedAdoptReceipt final = await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [], [new DottedTripleAssignment(shared, [new CausalDot(peer, 1)])], finalDropContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(WriteBackOutcome.Committed, final.Outcome, "The full drop commits.");
        Assert.IsFalse(HasEntry(ledger.Snapshot(), shared), "A drop covering every present dot removes the triple.");
        Assert.IsFalse(GraphHolds(dataset, shared), "The committed default graph honors the removal.");
    }

    /// <summary>Two adopt write-backs that each drop a DIFFERENT part of one entry's dots converge to the triple removed everywhere: the second plan builds against the post-first ledger under the causality commit gate, sees the sole survivor, and commands the dataset removal — ledger and committed graph never diverge.</summary>
    [TestMethod]
    public async Task ConvergentPartialDropsRemoveTheTripleEverywhere()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple shared = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [shared], TestContext.CancellationToken).ConfigureAwait(false);

        CausalContext unionContext = new();
        unionContext.Fold(new CausalDot(peer, 1));
        await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [new DottedTripleAssignment(shared, [new CausalDot(peer, 1)])], [], unionContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //One drop observed only the local dot, the other only the peer dot — the second, planned after the
        //first landed, finds the survivor alone and removes the triple from the dataset too.
        CausalContext dropLocalContext = new();
        dropLocalContext.Fold(new CausalDot(identity, 1));
        await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [], [new DottedTripleAssignment(shared, [new CausalDot(identity, 1)])], dropLocalContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CausalContext dropPeerContext = new();
        dropPeerContext.Fold(new CausalDot(peer, 1));
        await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [], [new DottedTripleAssignment(shared, [new CausalDot(peer, 1)])], dropPeerContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(HasEntry(ledger.Snapshot(), shared), "The two drops together cancel every dot, removing the entry.");
        Assert.IsFalse(GraphHolds(dataset, shared), "The committed graph is removed in the same commit that cancelled the last dot — ledger and dataset never diverge.");
    }

    /// <summary>The same two partial drops fired CONCURRENTLY converge identically: the causality commit gate serializes each whole plan-commit attempt, so whichever lands second plans against the first's outcome — the final state is the triple absent from BOTH the ledger and the committed graph, in either arrival order.</summary>
    [TestMethod]
    public async Task ConcurrentPartialDropsKeepLedgerAndDatasetConsistent()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple shared = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [shared], TestContext.CancellationToken).ConfigureAwait(false);

        CausalContext unionContext = new();
        unionContext.Fold(new CausalDot(peer, 1));
        await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [new DottedTripleAssignment(shared, [new CausalDot(peer, 1)])], [], unionContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        CausalContext dropLocalContext = new();
        dropLocalContext.Fold(new CausalDot(identity, 1));
        CausalContext dropPeerContext = new();
        dropPeerContext.Fold(new CausalDot(peer, 1));
        Task<DottedAdoptReceipt> dropLocal = ApplyDropAsync(dataset, ledger, shared, new CausalDot(identity, 1), dropLocalContext, TestContext.CancellationToken);
        Task<DottedAdoptReceipt> dropPeer = ApplyDropAsync(dataset, ledger, shared, new CausalDot(peer, 1), dropPeerContext, TestContext.CancellationToken);
        await Task.WhenAll(dropLocal, dropPeer).ConfigureAwait(false);

        Assert.IsFalse(HasEntry(ledger.Snapshot(), shared), "Both dots are cancelled whichever drop lands second.");
        Assert.IsFalse(GraphHolds(dataset, shared), "The committed graph agrees with the ledger under concurrency — the gate kept the second plan fresh.");
    }

    /// <summary>Refolding an already-incorporated mint over a ledger that has since dropped the entry is a no-op ON ITS OWN: the context covers the dot, so the fold skips it and the tombstone stands — per-entry idempotence, with no reliance on the drop being refolded afterwards.</summary>
    [TestMethod]
    public async Task RefoldOfAnIncorporatedMintCannotResurrectTheEntry()
    {
        ReplicaAxis identity = Axis(0x0A);
        EncodedTriple minted = EncodedTriple.FromEncoded(2, 100, 3);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [], TestContext.CancellationToken).ConfigureAwait(false);

        await CommitAsync(dataset, [minted], [], TestContext.CancellationToken).ConfigureAwait(false);
        await CommitAsync(dataset, [], [minted], TestContext.CancellationToken).ConfigureAwait(false);

        //Restore a candidate from the post-drop snapshot and refold ONLY the mint's annotated entry — the
        //shape a whole-log recovery refold reaches for every pre-artifact entry.
        DottedCommitLedger candidate = DottedCommitLedger.RestoreSnapshot(identity, ledger.Snapshot());
        CommitCausality mintAnnotation = new([new DottedTripleAssignment(minted, [new CausalDot(identity, 1)])], [], FoldedContext: null, IsBaseline: false);
        DatasetJournalEntry mintEntry = DatasetJournalEntry.Committed(VeritasHashing.Default, new NodeIdentifier(1), new NodeIdentifier(2), SessionId.NewId(), [], mintAnnotation);
        candidate.FoldRecoveredEntry(mintEntry);

        Assert.IsFalse(HasEntry(candidate.Snapshot(), minted), "The covered mint refold skips — the tombstone stands without the drop entry's help.");
    }

    /// <summary>Two sessions racing to retract the same triple resolve under the ordinary retry contract: the loser surfaces the concurrency exception the commit facades key on, never a spurious divergence fault.</summary>
    [TestMethod]
    public async Task ConcurrentDeletersSurfaceTheRetryContract()
    {
        ReplicaAxis identity = Axis(0x0A);
        EncodedTriple contested = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [contested], TestContext.CancellationToken).ConfigureAwait(false);

        DatasetEditSession first = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        DatasetEditSession second = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await first.ApplyDeltaAsync(TermId.None, [], [contested], TestContext.CancellationToken).ConfigureAwait(false);
            await second.ApplyDeltaAsync(TermId.None, [], [contested], TestContext.CancellationToken).ConfigureAwait(false);
            await first.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            bool concurrencySurfaced = false;
            try
            {
                await second.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }
            catch(EditSessionConcurrencyException)
            {
                concurrencySurfaced = true;
            }

            Assert.IsTrue(concurrencySurfaced, "The losing deleter sees the concurrency contract's exception, retryable by every commit facade.");
            Assert.IsFalse(GraphHolds(dataset, contested), "The winning retraction stands.");
            Assert.IsFalse(HasEntry(ledger.Snapshot(), contested), "The ledger agrees with the committed graph.");
        }
        finally
        {
            await first.DisposeAsync().ConfigureAwait(false);
            await second.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Runs one adopt write-back dropping one dot of a triple, hopping to the thread pool first so two of these genuinely race.</summary>
    /// <param name="dataset">The dataset the write-back commits into.</param>
    /// <param name="ledger">The dataset's ledger.</param>
    /// <param name="triple">The triple whose dot the peer drop cancels.</param>
    /// <param name="dot">The dot the peer drop names.</param>
    /// <param name="peerContext">The peer context covering the named dot.</param>
    /// <param name="cancellationToken">A token that aborts the write-back.</param>
    /// <returns>The write-back receipt.</returns>
    private static async Task<DottedAdoptReceipt> ApplyDropAsync(MutableSparqlDataset dataset, DottedCommitLedger ledger, EncodedTriple triple, CausalDot dot, CausalContext peerContext, CancellationToken cancellationToken)
    {
        await Task.Yield();

        return await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, [], [new DottedTripleAssignment(triple, [dot])], peerContext, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The lane's point, at the write-back level: peer knowledge already adopted once cannot re-apply after a local retract observed it — the re-run plans against the live context, finds every dot covered, and adopts nothing. No resurrection.</summary>
    [TestMethod]
    public async Task ReplayedPeerKnowledgeCannotResurrectAnObservedRemove()
    {
        ReplicaAxis identity = Axis(0x0A);
        ReplicaAxis peer = Axis(0x0B);
        EncodedTriple contested = EncodedTriple.FromEncoded(1, 100, 2);
        (MutableSparqlDataset dataset, DottedCommitLedger ledger) = await CreateRemoveAwareAsync(identity, [], TestContext.CancellationToken).ConfigureAwait(false);

        CausalContext peerContext = new();
        peerContext.Fold(new CausalDot(peer, 1));
        DottedTripleAssignment[] peerKnowledge = [new DottedTripleAssignment(contested, [new CausalDot(peer, 1)])];

        DottedAdoptReceipt first = await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, peerKnowledge, [], peerContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(WriteBackOutcome.Committed, first.Outcome, "The first exchange adopts the peer's triple.");
        Assert.IsTrue(GraphHolds(dataset, contested), "The adopted triple is committed.");

        //The local retract observes the adopted dot: covered-but-absent, the tombstone.
        await CommitAsync(dataset, [], [contested], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(GraphHolds(dataset, contested), "The retract lands.");

        //The same peer knowledge arrives again — a stale exchange, a retried write-back, a second session.
        DottedAdoptReceipt replay = await ReconcileWriteBack.ApplyAdoptAsync(dataset, ledger, peerKnowledge, [], peerContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(WriteBackOutcome.NoOp, replay.Outcome, "Replayed knowledge the context covers adopts nothing.");
        Assert.IsFalse(GraphHolds(dataset, contested), "The observed remove stands — no resurrection through the write-back lane.");
    }
}
