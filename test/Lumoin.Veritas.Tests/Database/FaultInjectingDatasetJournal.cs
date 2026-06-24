using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Decides whether the journal injects an optimistic-concurrency conflict on a given append, by the count of
/// appends since the plan was armed (1 for the first armed append). Returning <see langword="true"/> makes that
/// append fail as though a competing committer advanced the head.
/// </summary>
/// <param name="armedAppendIndex">The 1-based index of the append since the plan was armed.</param>
/// <returns><see langword="true"/> to inject a conflict on that append.</returns>
internal delegate bool JournalAppendFaultPlan(int armedAppendIndex);

/// <summary>
/// A dataset-journal decorator that injects optimistic-concurrency conflicts on demand, the journal counterpart of
/// the sketch-fetch fault injector: it wraps a real journal's append/read delegates and, once armed with a
/// <see cref="JournalAppendFaultPlan"/>, throws <see cref="EditSessionConcurrencyException"/> on the planned
/// appends instead of delegating — deterministically forcing the head-CAS conflict paths (write-back retry and
/// exhaustion) with no real concurrency. Arming after construction lets a dataset be built conflict-free and only
/// the operation under test see injected conflicts.
/// </summary>
internal sealed class FaultInjectingDatasetJournal
{
    /// <summary>The wrapped journal's append delegate.</summary>
    private DatasetJournalDelegates.AppendDatasetJournalEntryAsync InnerAppend { get; }

    /// <summary>The wrapped journal's read delegate.</summary>
    private DatasetJournalDelegates.ReadDatasetJournalEntriesAsync InnerRead { get; }

    /// <summary>The active fault plan, or <see langword="null"/> while disarmed (every append passes through).</summary>
    private JournalAppendFaultPlan? Plan { get; set; }

    /// <summary>The number of appends seen since the current plan was armed.</summary>
    private int ArmedAppendCount { get; set; }

    /// <summary>Wraps a real journal's append and read delegates, disarmed (passthrough) until <see cref="Arm"/> is called.</summary>
    /// <param name="innerAppend">The wrapped journal's append delegate.</param>
    /// <param name="innerRead">The wrapped journal's read delegate.</param>
    public FaultInjectingDatasetJournal(DatasetJournalDelegates.AppendDatasetJournalEntryAsync innerAppend, DatasetJournalDelegates.ReadDatasetJournalEntriesAsync innerRead)
    {
        InnerAppend = innerAppend;
        InnerRead = innerRead;
    }

    /// <summary>The append delegate to wire into a dataset; injects conflicts per the armed plan.</summary>
    public DatasetJournalDelegates.AppendDatasetJournalEntryAsync AppendDelegate => AppendAsync;

    /// <summary>The read delegate to wire into a dataset; passes straight through.</summary>
    public DatasetJournalDelegates.ReadDatasetJournalEntriesAsync ReadDelegate => InnerRead;

    /// <summary>Arms the injector with a fault plan, resetting the armed-append counter so the plan's first index is the next append.</summary>
    /// <param name="plan">The plan deciding which armed appends inject a conflict.</param>
    public void Arm(JournalAppendFaultPlan plan)
    {
        Plan = plan;
        ArmedAppendCount = 0;
    }

    /// <summary>Appends through the wrapped journal, except on a planned append it throws a concurrency conflict instead of appending — so the head does not advance and the caller re-bases, exactly as a real lost head-CAS race.</summary>
    /// <param name="entry">The entry to append.</param>
    /// <param name="expectedHead">The head the caller's append expects.</param>
    /// <param name="cancellationToken">A token that aborts the append.</param>
    /// <returns>The wrapped journal's assigned sequence number when the append is not faulted.</returns>
    /// <exception cref="EditSessionConcurrencyException">The plan injects a conflict on this append.</exception>
    private ValueTask<long> AppendAsync(DatasetJournalEntry entry, NodeIdentifier expectedHead, CancellationToken cancellationToken)
    {
        if(Plan is { } plan)
        {
            ArmedAppendCount++;
            if(plan(ArmedAppendCount))
            {
                //A fabricated different actual head stands in for the competing committer that advanced it.
                throw new EditSessionConcurrencyException(expectedHead, new NodeIdentifier(expectedHead.Value ^ 1UL));
            }
        }

        return InnerAppend(entry, expectedHead, cancellationToken);
    }
}
