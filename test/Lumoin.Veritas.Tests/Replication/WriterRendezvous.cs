using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The gate a contended row's writers meet at: arrivals are held until the armed set has met, and then all of
/// them are released together, so their proposals address one and the same version — the batteries' way of
/// making a contended row contend on every run rather than on a lucky schedule. One shape serves both keys a
/// battery holds writers by — the in-process bench's replica index and the socket cluster's proposal owner —
/// armed either for NAMED participants, where any other writer passes straight through, or for a COUNT of
/// distinct arrivals, where every arrival is held toward the target.
/// </summary>
/// <typeparam name="TWriter">What one arriving writer is named by.</typeparam>
/// <remarks>
/// <para>
/// IT IS A BARRIER AND NEVER A DELAY. What opens it is the arrival of the last writer, which is the very
/// transition a contended row is about; nothing here reads a clock to decide anything. The one duration is a
/// backstop over a writer that never arrives, and it opens the gate rather than raising: the row then fails the
/// contention it asserts instead of hanging, and <see cref="EveryParticipantArrived"/> says which happened.
/// </para>
/// <para>
/// WHAT IT BUYS is a race that is a fact of the row rather than of the schedule. Two writers released at one
/// version cannot both commit there, so exactly one of them is superseded and spends a further attempt
/// recomposing on the winner — an observable no serialized execution can produce.
/// </para>
/// <para>
/// An arrival after the gate opened returns at once, which is what lets a superseded writer's second attempt
/// run without meeting a gate that has already done its work.
/// </para>
/// </remarks>
internal sealed class WriterRendezvous<TWriter>
    where TWriter: notnull
{
    /// <summary>
    /// How long a held writer waits for the ones it is waiting on. It is a BACKSTOP and never a cadence:
    /// nothing in a passing row reaches it, and a row that armed the gate for a writer it never drove surfaces
    /// here as a failed contention assertion rather than as a hung suite. It is the ladder's in-flight bound,
    /// which the teardown bound stands outside of, so a gate a row left closed opens before a teardown join
    /// gives up on the loop draining behind it.
    /// </summary>
    private static TimeSpan ArrivalBackstop { get; } = MetadataBatteryBackstops.InFlight;

    /// <summary>Arms a gate for the named writers; any other writer passes straight through it.</summary>
    /// <param name="participants">The distinct writers that must meet before any of them proceeds.</param>
    public WriterRendezvous(ImmutableArray<TWriter> participants)
    {
        Named = participants;
        Target = participants.Length;
    }

    /// <summary>Arms a gate for a count of distinct writers; every arrival is held toward the target.</summary>
    /// <param name="participants">How many distinct writers must arrive before any of them proceeds.</param>
    public WriterRendezvous(int participants)
    {
        Named = default;
        Target = participants;
    }

    /// <summary>Whether the armed set actually met, rather than the backstop having opened the gate.</summary>
    public bool EveryParticipantArrived { get; private set; }

    /// <summary>The named writers that must meet, or the default array when the gate counts instead of naming.</summary>
    private ImmutableArray<TWriter> Named { get; }

    /// <summary>How many distinct writers must arrive.</summary>
    private int Target { get; }

    /// <summary>The writers that have arrived so far, read and written under <see cref="Gate"/>.</summary>
    private HashSet<TWriter> Arrived { get; } = [];

    /// <summary>The gate <see cref="Arrived"/> is read and written under.</summary>
    private Lock Gate { get; } = new();

    /// <summary>The completion every held writer awaits, set once and never reset.</summary>
    private TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Records one writer's arrival and holds it until the armed set has met.</summary>
    /// <param name="writer">The arriving writer.</param>
    /// <param name="cancellationToken">The arriving call's token.</param>
    /// <returns>A task that completes once the armed set has met, or once the backstop has opened the gate.</returns>
    public async ValueTask ArriveAsync(TWriter writer, CancellationToken cancellationToken)
    {
        if(Released.Task.IsCompleted || (!Named.IsDefault && !Named.Contains(writer)))
        {
            return;
        }

        bool complete;
        lock(Gate)
        {
            _ = Arrived.Add(writer);
            complete = Arrived.Count >= Target;
            if(complete)
            {
                EveryParticipantArrived = true;
            }
        }

        if(complete)
        {
            _ = Released.TrySetResult();

            return;
        }

        try
        {
            await Released.Task.WaitAsync(ArrivalBackstop, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        catch(TimeoutException)
        {
            //A writer that never arrived would hold its fellows forever, so the gate opens and says it was not
            //the armed set that opened it.
            _ = Released.TrySetResult();
        }
    }
}
