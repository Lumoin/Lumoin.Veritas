using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// Reconstructs a dataset's committed head state from a durable dataset journal, either alone (a self-contained
/// log replayed from empty) or as the delta a persisted generation is brought current by (the suffix after the
/// generation's state). It folds the linear log's per-graph transitions exactly as
/// <see cref="DatasetGraphTransition"/> prescribes and reports the resulting per-graph content, so the caller can
/// rebuild the head state and confirm it against the journal head — the content-addressed recovery oracle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Anchoring.</b> A recovered generation binds a dataset state identifier (the manifest's provenance epoch).
/// The replay resumes AFTER the LAST journal entry whose child is that state: content-addressed states can
/// recur — a cycle of edits nets to zero and returns to a prior state — so the last occurrence is always the
/// correct, and cheapest, resume point. With no anchor (<see cref="NodeIdentifier.Empty"/>) the whole log
/// replays from empty, and the first entry must be an <see cref="EditSessionEntryKind.Initial"/> build: a durable
/// dataset log is self-contained.
/// </para>
/// <para>
/// <b>Loud refusals belong to the caller.</b> When the anchor is never found the states come from different
/// histories; this returns <see cref="DatasetJournalReplayOutcome.Diverged"/> carrying the anchor and head
/// rather than throwing, and the caller refuses. The only throw here is the structural one a self-contained log
/// violates (a first entry that is not <see cref="EditSessionEntryKind.Initial"/>, or a drop of the default
/// graph).
/// </para>
/// </remarks>
public static class DatasetJournalRecovery
{
    /// <summary>
    /// Folds a durable dataset journal into the per-graph content of its head state, resuming after a recovered
    /// generation's anchor (or from empty when there is none).
    /// </summary>
    /// <param name="read">The journal read seam; entries are read in sequence order from the start.</param>
    /// <param name="journalHead">The journal head the reconstructed content is expected to reproduce; carried into the result for the caller's head check.</param>
    /// <param name="anchorStateId">The recovered generation's dataset state identifier the replay resumes after; <see cref="NodeIdentifier.Empty"/> replays the whole self-contained log from empty.</param>
    /// <param name="headerAnchor">The onboarding anchor a v2 attached log's header records — the persisted state the log continues from. When the generation's state appears in no record BUT this equals it, the whole log is an attached suffix over that generation: the replay folds from the first record, whose parent must be the anchor. <see cref="NodeIdentifier.Empty"/> for a v1 log or a self-contained create-path log, so a v1 reopen is unaffected. The anchor is content-addressed, so it does not by itself discriminate identically-built histories — two independently built generations with the same encoded content share it; the caller cross-checks the header's dictionary replication epoch for identity before trusting the pivot.</param>
    /// <param name="baseContent">Resolves a graph's content in the anchored base generation — the default graph under <see cref="TermId.None"/>, named graphs by their term id — returning <see langword="null"/> when the graph is absent in the base. Not consulted when there is no anchor.</param>
    /// <param name="cancellationToken">A token that aborts the replay, honoured per entry.</param>
    /// <returns>The replay result: the outcome, the touched and dropped graphs, the count of folded content entries, and the anchor and head.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> or <paramref name="baseContent"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A no-anchor replay's first entry is not an <see cref="EditSessionEntryKind.Initial"/> build, or a transition drops the always-present default graph.</exception>
    public static async ValueTask<DatasetJournalReplayResult> ReplayAsync(
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync read,
        NodeIdentifier journalHead,
        NodeIdentifier anchorStateId,
        NodeIdentifier headerAnchor,
        Func<TermId, IEnumerable<EncodedTriple>?> baseContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(baseContent);

        //Materialise the log once: the anchor's last occurrence is found by scanning all entries, and content-
        //addressed states can recur, so a single pass to gather then a second to fold is both correct and simplest.
        List<DatasetJournalEntry> entries = [];
        await foreach(DatasetJournalEntry entry in read(0, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        bool hasAnchor = anchorStateId != NodeIdentifier.Empty;
        int foldFrom;
        Func<TermId, IEnumerable<EncodedTriple>?> resolveBase;
        if(hasAnchor)
        {
            int anchorIndex = LastIndexOfChild(entries, anchorStateId);
            if(anchorIndex >= 0)
            {
                //The generation's state is a record's child: the suffix after its last occurrence folds over the base.
                foldFrom = anchorIndex + 1;
                resolveBase = baseContent;
            }
            else if(headerAnchor == anchorStateId)
            {
                //An attached log: the generation is the base the header anchors to, and every record is a
                //post-attach suffix. The first record's parent must be the anchor, else the log continues from a
                //different state than the generation names — divergence the caller refuses.
                if(entries.Count > 0 && entries[0].ParentId != anchorStateId)
                {
                    return Diverged(anchorStateId, journalHead);
                }

                foldFrom = 0;
                resolveBase = baseContent;
            }
            else
            {
                //The generation's state is nowhere in the journal and the log's header does not anchor to it —
                //different histories. The caller refuses.
                return Diverged(anchorStateId, journalHead);
            }
        }
        else
        {
            //A durable dataset log is self-contained: it must open with its Initial build, and it replays from empty.
            if(entries.Count == 0 || entries[0].EntryKind != EditSessionEntryKind.Initial)
            {
                throw new InvalidDataException("A durable dataset journal replayed with no generation anchor must begin with an Initial build entry; the log is not self-contained.");
            }

            foldFrom = 0;
            resolveBase = static _ => null;
        }

        Dictionary<TermId, HashSet<EncodedTriple>> touched = [];
        HashSet<TermId> dropped = [];
        long entriesReplayed = 0;
        for(int i = foldFrom; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DatasetJournalEntry entry = entries[i];
            if(entry.EntryKind is not (EditSessionEntryKind.Initial or EditSessionEntryKind.Committed))
            {
                //Started and Abandoned entries carry no transitions; a self-contained log excludes Forked.
                continue;
            }

            entriesReplayed++;
            FoldEntry(touched, dropped, entry.Transitions, resolveBase);
        }

        DatasetJournalReplayOutcome outcome = entriesReplayed == 0
            ? DatasetJournalReplayOutcome.UpToDate
            : DatasetJournalReplayOutcome.Replayed;

        return new DatasetJournalReplayResult(outcome, touched, dropped, entriesReplayed, anchorStateId, journalHead);
    }

    /// <summary>Builds the diverged result the caller refuses: the recovered generation and the journal come from different histories.</summary>
    /// <param name="anchorStateId">The recovered generation's dataset state identifier.</param>
    /// <param name="journalHead">The journal head, carried for the caller's message.</param>
    /// <returns>A <see cref="DatasetJournalReplayOutcome.Diverged"/> result with no folded content.</returns>
    private static DatasetJournalReplayResult Diverged(NodeIdentifier anchorStateId, NodeIdentifier journalHead)
    {
        return new DatasetJournalReplayResult(
            DatasetJournalReplayOutcome.Diverged,
            ImmutableDictionary<TermId, HashSet<EncodedTriple>>.Empty,
            [],
            EntriesReplayed: 0,
            Anchor: anchorStateId,
            Head: journalHead);
    }

    /// <summary>Finds the index of the last entry whose child state equals <paramref name="stateId"/>, or -1 when none does.</summary>
    /// <param name="entries">The journal entries in sequence order.</param>
    /// <param name="stateId">The state identifier to match.</param>
    /// <returns>The last matching index, or -1.</returns>
    private static int LastIndexOfChild(List<DatasetJournalEntry> entries, NodeIdentifier stateId)
    {
        for(int i = entries.Count - 1; i >= 0; i--)
        {
            if(entries[i].ChildId == stateId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Folds one entry's per-graph transitions into the working sets, materialising a graph on first touch:
    /// a creation (<see cref="DatasetGraphTransition.ParentRoot"/> null) starts empty, otherwise the working set
    /// starts from the base generation's content for that graph (null treated as empty). A drop
    /// (<see cref="DatasetGraphTransition.ChildRoot"/> null) removes the graph; a later re-create brings it back.
    /// </summary>
    /// <param name="touched">The per-graph working sets, keyed by graph-name term id.</param>
    /// <param name="dropped">The graphs currently dropped.</param>
    /// <param name="transitions">The entry's per-graph transitions, applied in order.</param>
    /// <param name="resolveBase">The base-generation content resolver consulted on first touch of a mutated graph.</param>
    /// <exception cref="InvalidDataException">A transition drops the default graph, which always exists.</exception>
    private static void FoldEntry(
        Dictionary<TermId, HashSet<EncodedTriple>> touched,
        HashSet<TermId> dropped,
        ImmutableArray<DatasetGraphTransition> transitions,
        Func<TermId, IEnumerable<EncodedTriple>?> resolveBase)
    {
        foreach(DatasetGraphTransition transition in transitions)
        {
            TermId graph = transition.Graph;
            if(transition.ChildRoot is null)
            {
                if(graph == TermId.None)
                {
                    throw new InvalidDataException("A durable dataset journal drops the default graph, which always exists.");
                }

                touched.Remove(graph);
                dropped.Add(graph);

                continue;
            }

            if(!touched.TryGetValue(graph, out HashSet<EncodedTriple>? working))
            {
                working = [];
                if(transition.ParentRoot is not null)
                {
                    IEnumerable<EncodedTriple>? baseTriples = resolveBase(graph);
                    if(baseTriples is not null)
                    {
                        foreach(EncodedTriple triple in baseTriples)
                        {
                            working.Add(triple);
                        }
                    }
                }

                touched[graph] = working;
            }

            dropped.Remove(graph);
            foreach(EncodedTriple removal in transition.Removals)
            {
                working.Remove(removal);
            }

            foreach(EncodedTriple addition in transition.Additions)
            {
                working.Add(addition);
            }
        }
    }
}
