using System;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The three bounds every waiting point of the metadata-plane batteries runs under: the deadline one member is
/// given to answer a query of its own, the one an in-flight wait runs under, and the one a teardown join runs
/// under, all three derived from one margin.
/// </summary>
/// <remarks>
/// <para>
/// ALL THREE ARE REFUSAL BOUNDS AND NONE IS A CADENCE. Every waiting point of these batteries ends on the
/// transition it is about — a writer's arrival at a gate, a hold's release, an obligation's own completion, a
/// clock a row moved past a deadline, a loop that has drained — and a bound only turns a wedged run into a
/// failed row instead of a hung suite. Nothing in a passing row reaches one.
/// </para>
/// <para>
/// A GATE OPENS BEFORE A TEARDOWN GIVES UP. <see cref="Teardown"/> is <see cref="InFlight"/> plus a further
/// margin, so a gate a row left closed opens while the teardown draining behind it is still waiting: the row
/// then fails on the contention or the hold it asserted, which names what regressed, rather than on a teardown
/// join that says only that something did not finish.
/// </para>
/// <para>
/// A MEMBER IS GIVEN UP ON BEFORE A ROW GIVES UP ON THE REPORT. <see cref="MemberQuery"/> is
/// <see cref="InFlight"/> less that same margin, so it stands strictly INSIDE the bound a row awaits a report
/// under: a member that fell silent is turned into an unreachable entry while the row is still waiting, and the
/// row then fails on the entry it asserted rather than on a backstop that names only that something did not
/// finish. Deriving all three from one margin is what keeps that order from inverting when any of them is
/// retuned.
/// </para>
/// <para>
/// <see cref="MemberQuery"/> IS SPENT AGAINST THE PLANE'S OWN INJECTED CLOCK, so its magnitude is a bound and
/// never a duration for any row that supplies a fixed clock: such a row reaches the deadline by advancing that
/// clock, and the number here only has to be one no answering member could ever exceed.
/// </para>
/// </remarks>
internal static class MetadataBatteryBackstops
{
    /// <summary>The margin each bound stands outside the one below it by.</summary>
    private static TimeSpan Margin => TimeSpan.FromSeconds(10);

    /// <summary>
    /// The bound one IN-FLIGHT wait runs under: a gate a writer is held at, a hold a row is waiting to be
    /// reached, an observation crossing routes a row cut, or a report a row hung one member's probe under.
    /// </summary>
    public static TimeSpan InFlight { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The deadline one MEMBER's catch-up query or readiness probe is given before that member is given up on,
    /// which is the plane's own member-query deadline throughout these batteries. It stands strictly inside
    /// <see cref="InFlight"/> by construction.
    /// </summary>
    public static TimeSpan MemberQuery { get; } = InFlight - Margin;

    /// <summary>
    /// The bound one TEARDOWN join runs under, which stands strictly outside <see cref="InFlight"/> by
    /// construction.
    /// </summary>
    public static TimeSpan Teardown { get; } = InFlight + Margin;
}
