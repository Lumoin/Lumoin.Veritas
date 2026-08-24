using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;
using Microsoft.Extensions.Time.Testing;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// An in-process deployment of metadata planes: one recorder host, one runner loop and one
/// <see cref="VeritasMetadataPlane"/> per replica, wired to each other by direct calls so a battery observes
/// exactly what an obligation put on the wire without a socket, a codec or a timer between the two.
/// </summary>
/// <remarks>
/// <para>
/// THE FOOTPRINT IS NAMED because the suite runs method-level parallel. One harness stands up at most four
/// runner loops and four plane loops, both of which are idle channel readers between obligations, and it holds
/// no port, no file and no shared mutable static. Two harnesses in two tests share nothing at all: every axis,
/// deployment, host and plane is built per instance.
/// </para>
/// <para>
/// THE CLOCK IS A FIXED ONE THE ROW ADVANCES. Every plane is built over <see cref="Clock"/> and with a hedging
/// base delay of <see cref="TimeSpan.Zero"/>, so nothing on a write path waits at all and no row's outcome can
/// turn on how fast a machine ran; the timestamp a trace event carries is the same pinned instant, which no
/// assertion reads. The one thing a row does advance the clock for is
/// <see cref="MetadataPlaneHarness.MemberQueryDeadline"/>: a probe this bench hangs answers nothing ever, so the
/// deadline is the only transition that can release the report, and a row reaches it by moving the clock rather
/// than by waiting.
/// </para>
/// <para>
/// EVERY SEAM IS A METHOD GROUP over this instance, over one member's runner, or over an explicit frame this
/// bench builds, so no delegate here captures an enclosing scope. The catch-up resolver hands out the target
/// runner's own sequenced <see cref="QuePaxaVersionedRunner{TValue}.ReadCommittedAsync"/>, and the recorder
/// resolver hands out one leg per writer, which passes the writer through this bench's gates and then reaches
/// the target runner's own sequenced <see cref="QuePaxaVersionedRunner{TValue}.RecordAsync"/>. Either way a
/// member's host answers on its own loop rather than on the caller's, exactly as a transport would.
/// </para>
/// <para>
/// A member the membership names but this bench runs no host for is a deployment this bench models rather than
/// refuses: the resolvers report it unresolvable so the register keeps its quorum slot as an always-faulting
/// endpoint, the readiness query reports it unreachable, and dissemination skips it. That is what lets a
/// battery admit a replica nobody is hosting and still watch the change land under the outgoing membership.
/// </para>
/// <para>
/// THE FAULTS THIS BENCH INJECTS ARE ALL AT ROUTING SEAMS, so a row installs one without reaching inside any
/// component. A cut version probe is the member nothing reaches; a HUNG version probe is the member that neither
/// answers nor refuses and ignores its token while doing so; a misrouted version probe is the endpoint map whose
/// two entries land on one host; and the write rendezvous holds the racing writers at their FIRST record
/// exchange, which is what makes a contended row contend on every run rather than on a lucky schedule.
/// </para>
/// </remarks>
internal sealed class MetadataPlaneHarness: IAsyncDisposable
{
    /// <summary>The most replicas one harness stands up, which is the parallel-footprint bound the battery doc names.</summary>
    private const int MaximumReplicas = 4;

    /// <summary>
    /// How long teardown waits for a runner loop that was told to complete. It is a safety backstop and never a
    /// cadence: the loop ends when its completed queue drains, which is the transition being awaited, and this
    /// bound only turns a wedged loop into a failed test instead of a hung suite. It is the ladder's teardown
    /// bound, which stands outside every in-flight bound a row waits under.
    /// </summary>
    private static TimeSpan TeardownBackstop { get; } = MetadataBatteryBackstops.Teardown;

    /// <summary>
    /// The deadline every plane of this bench gives ONE member to answer a catch-up query or a readiness probe
    /// before that member is given up on. It is spent against <see cref="Clock"/>, so a row reaches it by
    /// advancing the clock and never by waiting.
    /// </summary>
    public static TimeSpan MemberQueryDeadline { get; } = MetadataBatteryBackstops.MemberQuery;

    /// <summary>
    /// Stands up a deployment founded by the first <paramref name="founderCount"/> axes and additionally runs a
    /// host and a plane for <paramref name="outsiderCount"/> axes the membership does not list.
    /// </summary>
    /// <param name="founderCount">How many replicas found the chain; at least one.</param>
    /// <param name="outsiderCount">How many further replicas run a host and a plane without being members; not negative.</param>
    /// <param name="prioritySeed">The seed every replica's priority stream is derived from, so a whole run replays from one number.</param>
    /// <param name="attemptsPerRecorder">How many times one protocol step may send to one recorder before abandoning it for that step; at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if a count is out of range or the replica total exceeds the named parallel footprint.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "Every plane constructed here is attached to its member on the same statement and disposed by this harness's DisposeAsync, which every battery drives through await using. The plane's constructor cannot throw after its loop starts (its throw sites are argument validation over operands this constructor just built and validated), so no construction path strands a started plane.")]
    public MetadataPlaneHarness(int founderCount, int outsiderCount, int prioritySeed, int attemptsPerRecorder)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(founderCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(outsiderCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(founderCount + outsiderCount, MaximumReplicas);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptsPerRecorder, 1);

        ImmutableArray<MetadataFounder>.Builder founders = ImmutableArray.CreateBuilder<MetadataFounder>(founderCount);
        for(int index = 0; index < founderCount; index++)
        {
            founders.Add(FounderFor(index));
        }

        //The explicit factory rather than the canonical one: the founder order is the deployment's own, and a
        //battery that meant to place the bootstrap leader can read it off the first axis.
        Deployment = MetadataPlaneDeployment.Create(founders.MoveToImmutable());

        PlaneMember[] members = new PlaneMember[founderCount + outsiderCount];
        for(int index = 0; index < members.Length; index++)
        {
            members[index] = new PlaneMember(FounderFor(index), Deployment.Genesis, prioritySeed);
        }

        Members = members;

        //The planes are built in a second pass because dissemination addresses a member's PLANE while a member's
        //host is what the plane is built over, so every host exists before any plane can be pushed a record.
        for(int index = 0; index < members.Length; index++)
        {
            PlaneMember member = members[index];
            member.Attach(new VeritasMetadataPlane(
                Deployment,
                member.Axis,
                member.Node,
                member.Runner,
                TimeSpan.Zero,
                attemptsPerRecorder,
                MemberQueryDeadline,
                Clock,
                member.DrawPriority.Next,
                new MemberRecorderRouting(this, index).Resolve,
                ResolveCommittedReader,
                ObserveCommittedVersionAsync,
                ObserveMemberVersionAsync,
                PublishCommittedRecordAsync,
                member.Trace.Capture));
        }
    }

    /// <summary>The chain's genesis: the founding axes in genesis order and the minted chain identity.</summary>
    public MetadataPlaneDeployment Deployment { get; }

    /// <summary>
    /// The pinned clock every plane of this bench runs against, which a row advances to reach a transition rather
    /// than waiting for one.
    /// </summary>
    /// <remarks>
    /// It stands still unless a row moves it. Nothing on a write path reads it — the hedging base delay is
    /// <see cref="TimeSpan.Zero"/> — and the trace timestamps it stamps are read by no assertion, so the only
    /// observable it carries is <see cref="MemberQueryDeadline"/>.
    /// </remarks>
    public FakeTimeProvider Clock { get; } = new();

    /// <summary>Every replica this bench runs, founders first and outsiders after them.</summary>
    private PlaneMember[] Members { get; }

    /// <summary>
    /// The gate the racing writers of a contended row meet at, or <see langword="null"/> while no row has armed
    /// one, which is the state every other row runs in.
    /// </summary>
    /// <remarks>
    /// It is written by the row's own thread before the racing obligations are started and read afterwards by
    /// the writers' loops, and a reference field is written and read whole, so no row observes a half-built
    /// gate. Once armed it is never replaced.
    /// </remarks>
    private WriterRendezvous<int>? Rendezvous { get; set; }

    /// <summary>
    /// The hold one replica's record exchanges stop at, or <see langword="null"/> while no row has installed
    /// one, which is the state every other row runs in.
    /// </summary>
    /// <remarks>
    /// It is written by the row's own thread before the held obligation is started and read afterwards by that
    /// writer's loop, on the same rule <see cref="Rendezvous"/> is.
    /// </remarks>
    private MetadataPlaneRecordHold? Hold { get; set; }

    /// <summary>
    /// A deterministic replica identity axis for one replica index: the axis whose thirty-two bytes all carry
    /// <paramref name="index"/> plus one.
    /// </summary>
    /// <param name="index">The replica index; below <see cref="MaximumReplicas"/>.</param>
    /// <returns>The axis.</returns>
    /// <remarks>
    /// The fill makes the axes both distinct and ordered by index under lexicographic byte comparison, so a
    /// canonical founder order and an explicit one agree here and a battery reading either is reading the same
    /// deployment. The value is a function of the index alone and of nothing this process holds, which is what
    /// keeps two harnesses in two parallel tests from sharing anything but the arithmetic.
    /// </remarks>
    public static ReplicaAxis AxisFor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaximumReplicas);

        byte[] bytes = new byte[ReplicaAxis.ByteWidth];
        Array.Fill(bytes, (byte)(index + 1));

        return new ReplicaAxis(bytes);
    }


    /// <summary>
    /// The store this bench admits for one replica: fixed bytes derived from the replica's index, so a battery
    /// building the same bench twice builds the same membership and can compare the two.
    /// </summary>
    /// <param name="index">The replica index.</param>
    /// <returns>The store incarnation.</returns>
    /// <remarks>
    /// A derived incarnation is a bench convenience and not the shape a deployment uses. A real store mints
    /// its incarnation precisely so that it is not a function of the identity an operator hands out; a battery
    /// about the binding itself therefore states its incarnations explicitly instead of calling this.
    /// </remarks>
    public static StoreIncarnation StoreFor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaximumReplicas);

        Span<byte> bytes = stackalloc byte[StoreIncarnation.Size];
        bytes.Fill((byte)(index + 1));

        return StoreIncarnation.FromSpan(bytes);
    }


    /// <summary>One founder of this bench: the replica's axis beside the store admitted to answer for it.</summary>
    /// <param name="index">The replica index.</param>
    /// <returns>The founder.</returns>
    public static MetadataFounder FounderFor(int index)
    {
        return new MetadataFounder(AxisFor(index), StoreFor(index));
    }

    /// <summary>The identity axis of one replica of this bench.</summary>
    /// <param name="index">The replica index.</param>
    /// <returns>That replica's axis.</returns>
    public ReplicaAxis Axis(int index) => Members[index].Axis;

    /// <summary>The metadata plane of one replica of this bench.</summary>
    /// <param name="index">The replica index.</param>
    /// <returns>That replica's plane.</returns>
    public VeritasMetadataPlane Plane(int index) => Members[index].Plane;

    /// <summary>
    /// The obligation verdicts one replica's plane emitted, in completion order.
    /// </summary>
    /// <param name="index">The replica index.</param>
    /// <returns>That replica's captured events.</returns>
    /// <remarks>
    /// An event is appended on the plane's own loop before the obligation's completion is set, so a caller that
    /// has awaited an obligation observes that obligation's event; reading the list without awaiting first reads
    /// a list another loop is still appending to.
    /// </remarks>
    public IReadOnlyList<MetadataPlaneTraceEvent> TraceOf(int index) => Members[index].Trace.Events;

    /// <summary>
    /// The highest number of consensus attempts any obligation of one replica's plane spent, which is what a
    /// contended row reads its contention off.
    /// </summary>
    /// <param name="index">The replica index.</param>
    /// <returns>The highest attempt count that replica's plane emitted, or zero when it emitted nothing.</returns>
    /// <remarks>
    /// <para>
    /// An obligation that was superseded at the version it addressed spends a further attempt recomposing on the
    /// winner, so a count above one is evidence that this replica's write met another writer AT one version. A
    /// serialized execution cannot produce it: a writer that starts after the other committed adopts the decided
    /// record before it proposes and commits at the next version in one attempt.
    /// </para>
    /// <para>
    /// IT IS READ AFTER THE OBLIGATIONS IT COUNTS HAVE BEEN AWAITED, on the ordering rule
    /// <see cref="TraceOf"/> states: an event is appended on the plane's own loop before the obligation's
    /// completion is set, so a caller that has awaited an obligation counts that obligation, and a caller that
    /// has not is counting a list another loop is still appending to.
    /// </para>
    /// </remarks>
    public int HighestAttemptsOf(int index)
    {
        int highest = 0;
        foreach(MetadataPlaneTraceEvent emitted in Members[index].Trace.Events)
        {
            if(emitted.Attempts > highest)
            {
                highest = emitted.Attempts;
            }
        }

        return highest;
    }

    /// <summary>
    /// Arms the rendezvous the named replicas' planes are held at until every one of them has reached its first
    /// record exchange, so their proposals address ONE version rather than whichever version each happened to
    /// find.
    /// </summary>
    /// <param name="writers">The replica indices that must meet; at least two DISTINCT ones, and every one of them a replica this bench runs.</param>
    /// <returns>The armed gate, which reports afterwards whether every named writer actually arrived.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="writers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if fewer than two distinct writers are named, or if one of them is not a replica of this bench.</exception>
    /// <remarks>
    /// A row arms it AFTER whatever it set up sequentially, because the gate holds every record exchange a named
    /// writer makes while it is closed and a setup write would then wait for a race that has not started. A
    /// writer this bench never ran an obligation for would wedge the gate, so the gate opens on a backstop and
    /// reports that it did, which turns that mistake into a failed row rather than a hung suite. The gate meets
    /// over DISTINCT writers, so the same index named twice is refused here rather than arming a gate whose
    /// target its arrivals can never reach.
    /// </remarks>
    public WriterRendezvous<int> ArmWriteRendezvous(params int[] writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        foreach(int writer in writers)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(writer);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(writer, Members.Length);
        }

        HashSet<int> distinct = [.. writers];
        if(distinct.Count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(writers), "A rendezvous is a meeting of at least two distinct writers; the same writer named twice is one writer.");
        }

        WriterRendezvous<int> armed = new([.. distinct]);
        Rendezvous = armed;

        return armed;
    }

    /// <summary>
    /// Holds every record exchange one replica's plane makes until the row releases it, so a row can queue a
    /// second obligation while the first is PROVABLY still in flight.
    /// </summary>
    /// <param name="writer">The replica index whose exchanges are held.</param>
    /// <returns>The hold, which reports when the writer reached it and is released through it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="writer"/> is not a replica of this bench.</exception>
    /// <remarks>
    /// A held writer holds only itself: no recorder loop is occupied while the hold waits, so every other
    /// replica keeps writing and serving. The hold opens on a backstop rather than waiting forever, so a row
    /// that held a writer it never drove fails rather than hanging.
    /// </remarks>
    public MetadataPlaneRecordHold HoldRecordExchanges(int writer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(writer);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(writer, Members.Length);

        MetadataPlaneRecordHold held = new(writer);
        Hold = held;

        return held;
    }

    /// <summary>
    /// Cuts one replica's version probe, so the readiness report answers about that member the way it answers
    /// about a member nothing reaches.
    /// </summary>
    /// <param name="index">The replica index; a replica this bench runs.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is not a replica of this bench.</exception>
    /// <remarks>
    /// Only the probe is cut. The member keeps recording, keeps serving catch-up reads and keeps receiving
    /// dissemination, which is what separates a report's UNREACHABLE entry from a host that is actually gone: a
    /// readiness report is an availability observation over one query and never a health verdict.
    /// </remarks>
    public void CutVersionProbe(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Members.Length);

        Members[index].ProbeIsCut = true;
    }

    /// <summary>
    /// Hangs one replica's version probe: the probe is entered, says so, and then answers nothing at all,
    /// ignoring the token it was handed.
    /// </summary>
    /// <param name="index">The replica index; a replica this bench runs.</param>
    /// <returns>The hang, which reports when the probe was entered.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is not a replica of this bench.</exception>
    /// <remarks>
    /// It is the case a deadline exists for, and it is not the cut probe under another name: a cut probe refuses
    /// at once, so the report is assembled from what the delegates returned, while a hung probe returns nothing
    /// for the report to be assembled from and honours no cancellation, so only the plane's own deadline can end
    /// it. No delegate contract obliges a query to observe its token, which is why the deadline is a race rather
    /// than a signal, and why hanging the probe token-and-all is what actually exercises it.
    /// </remarks>
    public MetadataPlaneProbeHang HangVersionProbe(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Members.Length);

        MetadataPlaneProbeHang hung = new();
        Members[index].ProbeHang = hung;

        return hung;
    }

    /// <summary>
    /// Moves <see cref="Clock"/> strictly past <see cref="MemberQueryDeadline"/>, which is how a row reaches the
    /// deadline transition without waiting for one.
    /// </summary>
    /// <remarks>
    /// A row advances once the probe it hung has reported that it was ENTERED, and that instant is late enough
    /// by construction: a plane arms one member's deadline against this clock before it invokes that member's
    /// probe, so a probe reporting its own entry is reporting a deadline that already stands to come due. An
    /// arming that ever moved after the invocation would leave the report waiting and the row failing on its
    /// backstop, so the ordering this depends on cannot degrade into a row that passes for the wrong reason.
    /// </remarks>
    public void AdvancePastMemberQueryDeadline()
    {
        Clock.Advance(MemberQueryDeadline + TimeSpan.FromTicks(1));
    }

    /// <summary>
    /// Misroutes one replica's version probe onto another replica's host, which is the endpoint map whose two
    /// entries land on one host.
    /// </summary>
    /// <param name="index">The replica whose probe is misrouted; a replica this bench runs.</param>
    /// <param name="answeringIndex">The replica whose host actually answers it; a replica this bench runs, and not <paramref name="index"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if either index is not a replica of this bench, or if the two name one replica.</exception>
    /// <remarks>
    /// Both the version AND the identity come from the answering host, because that is what a wrong entry in an
    /// endpoint map produces: the probe reaches the wrong host and that host answers for itself. The register
    /// refuses the report on the identity, which is the check that keeps one replica from filling two slots of a
    /// report counted over distinct members. A probe pointed at its own host is the correct routing rather than
    /// a misroute, so it is refused here instead of arming a fault a row would then wait for in vain.
    /// </remarks>
    public void MisrouteVersionProbe(int index, int answeringIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Members.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(answeringIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(answeringIndex, Members.Length);
        ArgumentOutOfRangeException.ThrowIfEqual(answeringIndex, index);

        Members[index].ProbeRoutedTo = Members[answeringIndex];
    }

    /// <summary>
    /// Resolves the catch-up reader of one member, which is that member's runner: this bench's
    /// <see cref="ResolveCommittedRecordReaderDelegate{TValue}"/>.
    /// </summary>
    /// <param name="member">The member to ask.</param>
    /// <returns>That member's sequenced committed read.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this bench runs no host for that member; a catch-up round skips a member it cannot resolve and reads on.</exception>
    public ReadCommittedRecordDelegate<VeritasMetadataRecord> ResolveCommittedReader(ReplicaId member)
    {
        return MemberFor(member).Runner.ReadCommittedAsync;
    }

    /// <summary>
    /// Reports the highest committed version any host of this bench holds: the stand-down signal a delayed
    /// writer reads.
    /// </summary>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The highest committed version known, or <see cref="RegisterVersion.Unwritten"/> when no host holds a record.</returns>
    /// <remarks>
    /// It is wired because the plane requires it and it is never consulted, because the stand-down is evaluated
    /// only by a writer that waited a hedging delay and every plane here waits none. Reporting the whole bench's
    /// maximum is what a perfectly informed observer would answer, which makes the unused signal the strongest
    /// one rather than a weak stub.
    /// </remarks>
    public ValueTask<RegisterVersion> ObserveCommittedVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RegisterVersion highest = RegisterVersion.Unwritten;
        foreach(PlaneMember member in Members)
        {
            if(member.Node.Committed is { } committed && committed.Version > highest)
            {
                highest = committed.Version;
            }
        }

        return new ValueTask<RegisterVersion>(highest);
    }

    /// <summary>Reports how far one named member has caught up, which is what a readiness report is built from.</summary>
    /// <param name="member">The member to ask.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>That member's answer: its committed version, or <see cref="RegisterVersion.Unwritten"/> when it has learned nothing, beside the identity of the host that answered.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this bench runs no host for that member, and when a row has cut that member's probe; the readiness report turns both into an unreachable entry rather than into a version of zero.</exception>
    /// <remarks>
    /// <para>
    /// The answer is labelled with the replica this bench RESOLVED the probe to, which is honest by
    /// construction: the routing is the bench's own and a host is found by comparing its identity, so the label
    /// is the identity of the host that produced the version and not the identity the caller asked about. A
    /// deployment whose routing is an endpoint map it cannot check that way carries the identity in the answer
    /// instead, which is what the register's refusal of a foreign answer is there to catch, and
    /// <see cref="MisrouteVersionProbe"/> is how a row builds exactly that map.
    /// </para>
    /// <para>
    /// A HUNG PROBE IS ANSWERED BEFORE THE TOKEN IS EVEN LOOKED AT, deliberately: the hang models a query that
    /// honours no cancellation, which is the case the plane's deadline is a race against rather than a signal to.
    /// A probe that observed the token here would be a probe the caller could end, and the row asserting the
    /// deadline would then be asserting the token instead.
    /// </para>
    /// </remarks>
    public ValueTask<MemberVersionReport> ObserveMemberVersionAsync(ReplicaId member, CancellationToken cancellationToken)
    {
        PlaneMember local = MemberFor(member);
        if(local.ProbeHang is { } hung)
        {
            return hung.Hang();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if(local.ProbeIsCut)
        {
            throw new InvalidOperationException($"The battery cut the version probe of {member}, so nothing this query can reach answers for it.");
        }

        //A misrouted probe reaches ANOTHER host, which answers for itself: both halves of the answer are that
        //host's, because that is what a wrong entry in an endpoint map produces.
        PlaneMember answering = local.ProbeRoutedTo ?? local;

        return new ValueTask<MemberVersionReport>(new MemberVersionReport(answering.Self, answering.Node.Committed is { } committed ? committed.Version : RegisterVersion.Unwritten));
    }

    /// <summary>
    /// Carries a decided record to its audience, landing on each addressed replica through the inbound half of
    /// the metadata channel.
    /// </summary>
    /// <param name="committed">The decided record.</param>
    /// <param name="audience">The replicas the register computed the record is owed to.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>A task that completes once every addressed replica this bench hosts has learned the record durably.</returns>
    /// <remarks>
    /// It goes through <see cref="VeritasMetadataPlane.ApplyDisseminatedRecordAsync"/> rather than straight to a
    /// runner, because that method is the contract a real push lands on and its durability choice is part of
    /// what a battery is exercising. A member of the audience this bench runs no host for is skipped: at a
    /// membership boundary the audience deliberately names joiners and leavers, and a bench that refused one
    /// would abandon the push to every member listed after it.
    /// </remarks>
    public async ValueTask PublishCommittedRecordAsync(CommittedMetadataRecord committed, ImmutableArray<ReplicaId> audience, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(committed);

        foreach(ReplicaId listener in audience)
        {
            PlaneMember? local = TryMemberFor(listener);
            if(local is null)
            {
                continue;
            }

            _ = await local.Plane.ApplyDisseminatedRecordAsync(committed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes every plane, then completes every runner loop and awaits it, so nothing of this bench outlives
    /// the test that built it.
    /// </summary>
    /// <returns>A task that completes once every loop has drained and ended.</returns>
    /// <remarks>
    /// The order is the deployment's: a plane drains the obligations already queued on it, and those obligations
    /// still need the hosts, so the hosts are told to stop only after every plane has. The runners are completed
    /// in a finally so a plane that faulted its own loop still leaves no runner reading a queue nobody will
    /// write to.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach(PlaneMember member in Members)
            {
                await member.Plane.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            foreach(PlaneMember member in Members)
            {
                member.Runner.Complete();
            }
        }

        foreach(PlaneMember member in Members)
        {
            await member.Loop.WaitAsync(TeardownBackstop, TimeProvider.System).ConfigureAwait(false);
        }
    }

    /// <summary>Holds one writer at whichever gates a row installed, and passes it straight through when none was.</summary>
    /// <param name="writer">The index of the replica whose plane is sending.</param>
    /// <param name="cancellationToken">The sending obligation's token.</param>
    /// <returns>A task that completes once this writer may send.</returns>
    /// <remarks>
    /// The rendezvous comes first because it is the gate a row uses to FORM a race, while the hold is the gate a
    /// row uses to keep one writer in flight; no row installs both.
    /// </remarks>
    private async ValueTask ArriveAtGatesAsync(int writer, CancellationToken cancellationToken)
    {
        if(Rendezvous is { } gate)
        {
            await gate.ArriveAsync(writer, cancellationToken).ConfigureAwait(false);
        }

        if(Hold is { } held)
        {
            await held.ArriveAsync(writer, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The replica of this bench that is <paramref name="member"/>.</summary>
    /// <param name="member">The consensus identity to look for.</param>
    /// <returns>That replica.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this bench runs no host for that member.</exception>
    private PlaneMember MemberFor(ReplicaId member)
    {
        PlaneMember? local = TryMemberFor(member);
        if(local is null)
        {
            throw new InvalidOperationException($"No replica of this bench is {member}, so the deployment's endpoint map does not reach that member.");
        }

        return local;
    }

    /// <summary>The replica of this bench that is <paramref name="member"/>, or <see langword="null"/> when none is.</summary>
    /// <param name="member">The consensus identity to look for.</param>
    /// <returns>That replica, or <see langword="null"/>.</returns>
    private PlaneMember? TryMemberFor(ReplicaId member)
    {
        foreach(PlaneMember candidate in Members)
        {
            if(candidate.Self.Replica.Equals(member))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// One writer's own recorder resolution: the same routing every replica of this bench shares, plus the
    /// identity of the replica doing the resolving.
    /// </summary>
    /// <param name="bench">The bench that owns the routing and the rendezvous.</param>
    /// <param name="writer">The index of the replica whose plane resolves through this frame.</param>
    /// <remarks>
    /// The resolver is per-writer rather than per-bench because a rendezvous has to know WHICH writer is
    /// sending: a gate that could not tell two writers apart would count one writer's two exchanges as two
    /// arrivals and open before the race it exists to create had formed.
    /// </remarks>
    private sealed class MemberRecorderRouting(MetadataPlaneHarness bench, int writer)
    {
        /// <summary>The bench that owns the routing and the rendezvous.</summary>
        private MetadataPlaneHarness Bench { get; } = bench;

        /// <summary>The index of the replica whose plane resolves through this frame.</summary>
        private int Writer { get; } = writer;

        /// <summary>
        /// Resolves the record endpoint of one member, which is that member's runner behind this writer's
        /// rendezvous: this bench's <see cref="ResolveRecorderEndpointDelegate{TValue}"/>.
        /// </summary>
        /// <param name="member">The member to reach.</param>
        /// <returns>That member's sequenced record entry point.</returns>
        /// <exception cref="InvalidOperationException">Thrown when this bench runs no host for that member, which is how a resolver reports one it cannot resolve; the register then keeps the member's quorum slot as an endpoint that always faults.</exception>
        public VersionedRecorderEndpointDelegate<CommittedMetadataRecord> Resolve(ReplicaId member)
        {
            return new RecorderLeg(Bench, Writer, Bench.MemberFor(member)).RecordAsync;
        }
    }

    /// <summary>One writer's leg to one member's recorder: the rendezvous first, then that member's own loop.</summary>
    /// <param name="bench">The bench that owns the rendezvous.</param>
    /// <param name="writer">The index of the replica this leg sends for.</param>
    /// <param name="target">The replica this leg sends to.</param>
    private sealed class RecorderLeg(MetadataPlaneHarness bench, int writer, PlaneMember target)
    {
        /// <summary>The bench that owns the rendezvous.</summary>
        private MetadataPlaneHarness Bench { get; } = bench;

        /// <summary>The index of the replica this leg sends for.</summary>
        private int Writer { get; } = writer;

        /// <summary>The replica this leg sends to.</summary>
        private PlaneMember Target { get; } = target;

        /// <summary>
        /// Sends one consensus record exchange to the target's own loop — a
        /// <see cref="VersionedRecorderEndpointDelegate{TValue}"/>.
        /// </summary>
        /// <param name="request">The versioned record request.</param>
        /// <param name="cancellationToken">The sending obligation's token.</param>
        /// <returns>The target host's reply.</returns>
        /// <remarks>
        /// The gates are awaited BEFORE the request is queued, so a held writer holds nothing but itself: no
        /// recorder loop is occupied while a gate waits, and the target keeps serving every other writer.
        /// </remarks>
        public async ValueTask<VersionedRecordReply<CommittedMetadataRecord>> RecordAsync(VersionedRecordRequest<CommittedMetadataRecord> request, CancellationToken cancellationToken)
        {
            await Bench.ArriveAtGatesAsync(Writer, cancellationToken).ConfigureAwait(false);

            return await Target.Runner.RecordAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>One replica of the bench: its host, the loop that owns the host, its priority stream and its plane.</summary>
    private sealed class PlaneMember
    {
        /// <summary>Builds the replica's host and starts the loop that owns it.</summary>
        /// <param name="founder">The replica: its identity axis, which is also its consensus identity, beside the store this bench admits for it.</param>
        /// <param name="genesis">The chain's genesis membership.</param>
        /// <param name="prioritySeed">The seed this replica's priority stream is mixed from.</param>
        /// <remarks>
        /// The loop claims the host before <see cref="QuePaxaVersionedRunner{TValue}.RunAsync"/> yields, so a
        /// plane built after this constructor returns is built over a host that already has its one owner.
        /// </remarks>
        public PlaneMember(MetadataFounder founder, QuePaxaConfiguration genesis, int prioritySeed)
        {
            Axis = founder.Axis;
            Self = founder.ToHostId();
            Node = new QuePaxaVersionedNode<VeritasMetadataRecord>(genesis, Self);
            Runner = new QuePaxaVersionedRunner<VeritasMetadataRecord>(Node);
            DrawPriority = new MetadataPlanePrioritySource(prioritySeed, Self.Replica);
            Trace = new MetadataPlaneTraceCapture();

            //No persist delegate: this bench keeps nothing durable, so the runner's durability gate is vacuous
            //and a Durable learn completes exactly as an in-memory one does. The loop's own token is never
            //signalled — disposal completes the queue instead, which is the loop's own ending condition.
            Loop = Runner.RunAsync(persistNode: null, cancellationToken: CancellationToken.None);
        }

        /// <summary>The replica's identity axis.</summary>
        public ReplicaAxis Axis { get; }

        /// <summary>The replica's consensus identity, which is the same thirty-two bytes.</summary>
        public HostId Self { get; }

        /// <summary>The recorder host this replica serves peers from.</summary>
        public QuePaxaVersionedNode<VeritasMetadataRecord> Node { get; }

        /// <summary>The loop that owns the host and is the only code that touches it.</summary>
        public QuePaxaVersionedRunner<VeritasMetadataRecord> Runner { get; }

        /// <summary>The task the runner's loop runs as, awaited at teardown.</summary>
        public Task Loop { get; }

        /// <summary>This replica's own deterministic priority stream.</summary>
        public MetadataPlanePrioritySource DrawPriority { get; }

        /// <summary>The verdicts this replica's plane emitted.</summary>
        public MetadataPlaneTraceCapture Trace { get; }

        /// <summary>The plane this replica writes its obligations through.</summary>
        public VeritasMetadataPlane Plane { get; private set; } = null!;

        /// <summary>Whether this replica's version probe answers at all, which a row cuts to make the member unreachable to a readiness report alone.</summary>
        public bool ProbeIsCut { get; set; }

        /// <summary>The hang this replica's version probe stops at, or <see langword="null"/> when the probe answers.</summary>
        public MetadataPlaneProbeHang? ProbeHang { get; set; }

        /// <summary>The replica this replica's version probe actually lands on, or <see langword="null"/> when the routing is the correct one.</summary>
        public PlaneMember? ProbeRoutedTo { get; set; }

        /// <summary>Attaches the plane built over this replica's host, which the harness does in its second pass.</summary>
        /// <param name="plane">The plane.</param>
        public void Attach(VeritasMetadataPlane plane)
        {
            Plane = plane;
        }
    }
}

/// <summary>
/// A one-shot hold on ONE writer's record exchanges: that writer's first exchange stops here and says so, and
/// nothing of that writer proceeds until the row releases it.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT BUYS is the phrase "while the first obligation is still in flight" as a fact of the row rather than
/// as a hope about the scheduler. A write needs a quorum, and a held writer reaches no member at all, so its
/// obligation cannot complete while the hold stands; a row that queues a second obligation after the hold has
/// reported its arrival has therefore queued it against a plane whose register is occupied, which is the state
/// the plane's write queue exists to make survivable.
/// </para>
/// <para>
/// The one duration is a backstop over a row that installed a hold for a writer it never drove, and it releases
/// rather than raising, so such a row fails on its own assertions instead of hanging.
/// </para>
/// </remarks>
/// <param name="writer">The replica index whose exchanges are held.</param>
internal sealed class MetadataPlaneRecordHold(int writer)
{
    /// <summary>
    /// How long a held writer waits for the release. It is a BACKSTOP and never a cadence: a row releases the
    /// hold as its next act, and this bound only turns a row that never releases into a failure. It is the
    /// ladder's in-flight bound, which the teardown bound stands outside of.
    /// </summary>
    private static TimeSpan ReleaseBackstop { get; } = MetadataBatteryBackstops.InFlight;

    /// <summary>The replica index whose exchanges are held.</summary>
    private int Writer { get; } = writer;

    /// <summary>Completes once the held writer's first record exchange has reached this hold.</summary>
    public Task Reached => ReachedSource.Task;

    /// <summary>The completion the arriving writer sets.</summary>
    private TaskCompletionSource ReachedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The completion the row sets to let the held writer go.</summary>
    private TaskCompletionSource ReleasedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets the held writer proceed, and every later exchange of every writer straight through.</summary>
    public void Release()
    {
        _ = ReleasedSource.TrySetResult();
    }

    /// <summary>Stops the held writer here and lets every other writer past.</summary>
    /// <param name="writer">The index of the arriving replica.</param>
    /// <param name="cancellationToken">The arriving obligation's token.</param>
    /// <returns>A task that completes once this writer may send.</returns>
    public async ValueTask ArriveAsync(int writer, CancellationToken cancellationToken)
    {
        if(writer != Writer || ReleasedSource.Task.IsCompleted)
        {
            return;
        }

        _ = ReachedSource.TrySetResult();

        try
        {
            await ReleasedSource.Task.WaitAsync(ReleaseBackstop, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        catch(TimeoutException)
        {
            //A row that never released would hold this writer forever, so the hold opens and the row fails on
            //whatever it asserted about the writer instead.
            Release();
        }
    }
}

/// <summary>
/// One replica's version probe stopped where a real query that never returns stops: it reports that it was
/// entered and then answers nothing at all, for as long as the process lives and whatever any token does.
/// </summary>
/// <remarks>
/// <para>
/// WHAT IT BUYS is the phrase "a member that answers nothing" as a fact of the row rather than as a hope about
/// timing. A cut probe refuses immediately and a report assembled from refusals proves only that a fault becomes
/// an unreachable entry; a hung probe hands the report nothing to assemble from, so the report can complete only
/// because the plane raced the probe against its own deadline and gave up on that member.
/// </para>
/// <para>
/// IT OBSERVES NO CANCELLATION, which is the whole point. Nothing obliges a version query to honour the token it
/// is handed, so a deadline that merely signalled the token would leave exactly this query running forever; the
/// hang is the shape that tells a race apart from a signal.
/// </para>
/// <para>
/// ITS ENTRY IS A SAFE INSTANT TO ADVANCE THE CLOCK FROM, because a plane arms one member's deadline against
/// that clock BEFORE it invokes that member's probe: a probe reporting its own entry is reporting a deadline
/// that already stands to come due. An arming that ever moved after the invocation would leave the report
/// waiting on a probe that answers never, and the row would fail on its backstop rather than pass for the wrong
/// reason.
/// </para>
/// <para>
/// The reply it hands back is never completed and never faulted, so the plane's read abandons it. That is the
/// contract's own price for an answer arriving at all, and it costs this bench nothing: the completion is
/// collected with the row that built it.
/// </para>
/// </remarks>
internal sealed class MetadataPlaneProbeHang
{
    /// <summary>Completes once the hung probe has been entered, which is the transition a row advances the clock after.</summary>
    public Task Reached => ReachedSource.Task;

    /// <summary>The completion the entering probe sets.</summary>
    private TaskCompletionSource ReachedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The reply that never arrives, which every entry into this probe is answered with.</summary>
    private TaskCompletionSource<MemberVersionReport> PendingSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Reports that the probe was entered and answers with the reply that never arrives.</summary>
    /// <returns>A reply that completes never.</returns>
    /// <remarks>
    /// It takes no token on purpose. A row hangs a probe to model a query that cannot be ended from outside, and
    /// a parameter this would ignore anyway would read as a promise it does not keep.
    /// </remarks>
    public ValueTask<MemberVersionReport> Hang()
    {
        _ = ReachedSource.TrySetResult();

        return new ValueTask<MemberVersionReport>(PendingSource.Task);
    }
}

/// <summary>
/// Captures a plane's obligation verdicts through a method group, so a battery can read what an obligation
/// spent as well as what it answered.
/// </summary>
internal sealed class MetadataPlaneTraceCapture
{
    /// <summary>The captured events, in the plane's own completion order.</summary>
    public List<MetadataPlaneTraceEvent> Events { get; } = [];

    /// <summary>The handler entry point, which the plane calls on its write loop.</summary>
    /// <param name="evt">The emitted event.</param>
    public void Capture(in MetadataPlaneTraceEvent evt)
    {
        Events.Add(evt);
    }
}

/// <summary>
/// A deterministic phase-zero priority stream, distinct per replica per seed, so a battery replays its
/// decisions and not merely its delivery order.
/// </summary>
/// <remarks>
/// Xorshift64 over a seed mixed with the replica's identity rather than the cryptographic source: every draw a
/// run makes is reproducible from the printed seed on any runtime, and two writers of one bench never draw the
/// identical sequence. The two reserved endpoints of the priority range are excluded, so the stream honours the
/// source contract exactly.
/// </remarks>
internal sealed class MetadataPlanePrioritySource
{
    /// <summary>The substitute drawn when the generator lands on one of the two reserved endpoints.</summary>
    private const ulong ReplacementDraw = 0x0123_4567_89AB_CDEFUL;

    /// <summary>The generator state; it advances on each draw and is touched only by the drawing replica's own writer.</summary>
    private ulong state;

    /// <summary>Seeds a stream for one replica.</summary>
    /// <param name="seed">The run's seed.</param>
    /// <param name="replica">The replica whose identity the seed is mixed with.</param>
    public MetadataPlanePrioritySource(int seed, ReplicaId replica)
    {
        ulong mixed = ((ulong)(uint)seed * 1_000_003UL) ^ Fingerprint(replica);
        state = mixed == 0UL ? 0x9E37_79B9_7F4A_7C15UL : mixed;
    }

    /// <summary>Draws the next ordinary priority, which is this instance's <see cref="ProposalPrioritySourceDelegate"/>.</summary>
    /// <returns>An ordinary priority, never none and never reserved.</returns>
    public ProposalPriority Next()
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;

        ulong drawn = state is 0UL or ulong.MaxValue ? ReplacementDraw : state;

        return new ProposalPriority(drawn);
    }

    /// <summary>Folds a replica identity into one number, so two replicas seed different streams.</summary>
    /// <param name="replica">The replica identity.</param>
    /// <returns>The fold.</returns>
    private static ulong Fingerprint(ReplicaId replica)
    {
        Span<byte> buffer = stackalloc byte[ReplicaId.Size];
        replica.CopyTo(buffer);

        ulong value = 1469598103934665603UL;
        foreach(byte octet in buffer)
        {
            value = (value ^ octet) * 1099511628211UL;
        }

        return value;
    }
}

/// <summary>
/// The application-value seam of the QuePaxa JSON codec for <see cref="VeritasMetadataRecord"/>: the writer and
/// the reader a battery hands to
/// <see cref="Lumoin.Verisync.Json.QuePaxaMessageJson.CreateVersionedValueWriter{TValue}"/> and its reader
/// sibling, so the decided record a register carries crosses a real codec.
/// </summary>
/// <remarks>
/// <para>
/// EVERY IDENTIFIER THAT SPANS SIXTY-FOUR BITS IS A JSON STRING and never a bare number. A node identifier
/// spans the whole unsigned width and a dictionary epoch the whole signed one, and a bare JSON number above
/// two to the fifty-third is collapsed by any consumer that reparses through an IEEE double — which would make
/// two different lineages compare equal after a round trip through such a pipeline. A register version is a
/// bare number because <see cref="RegisterVersion"/> bounds itself below that width for exactly this reason,
/// so nothing it carries can be collapsed.
/// </para>
/// <para>
/// An absent baseline, an absent confirmation and a vacant lease are written as explicit nulls rather than
/// omitted, on the rule the consensus codec itself follows: an absent slot round-trips as null and stays
/// distinguishable from a field the payload never carried.
/// </para>
/// <para>
/// It lives test-side deliberately. The replication library stays serialization-agnostic — its transport
/// binding takes the codec seams as delegates — so the concrete JSON encoding of the record belongs to the
/// composition root that chose JSON, which here is the battery.
/// </para>
/// </remarks>
internal static class MetadataRecordJson
{
    /// <summary>Writes one coordinated metadata record.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="record">The record to write.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="writer"/> or <paramref name="record"/> is <see langword="null"/>.</exception>
    public static void Write(Utf8JsonWriter writer, VeritasMetadataRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteStartObject();

        writer.WriteStartArray("identityClaims");
        for(int index = 0; index < record.IdentityClaims.Length; index++)
        {
            ReplicaIdentityClaim claim = record.IdentityClaims[index];
            writer.WriteStartObject();
            writer.WriteString("axis", Convert.ToHexStringLower(claim.Axis.Bytes.Span));
            writer.WriteNumber("claimedAt", claim.ClaimedAt.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        if(record.Baseline is { } baseline)
        {
            writer.WriteStartObject("baseline");
            writer.WriteString("claimantAxis", Convert.ToHexStringLower(baseline.ClaimantAxis.Bytes.Span));
            writer.WriteString("causalityDigest", Text(baseline.CausalityDigest.Value));
            writer.WriteNumber("recordedAt", baseline.RecordedAt.Value);
            if(baseline.Confirmation is { } confirmation)
            {
                writer.WriteStartObject("confirmation");
                writer.WriteString("stateId", Text(confirmation.StateId.Value));
                writer.WriteString("dictionaryEpoch", confirmation.DictionaryEpoch.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("confirmation");
            }

            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("baseline");
        }

        writer.WriteStartObject("policy");
        writer.WriteNumber("healCadenceClass", record.Policy.HealCadenceClass);
        writer.WriteNumber("symbolBudgetTier", record.Policy.SymbolBudgetTier);
        writer.WriteEndObject();

        if(record.Coordinator is { } lease)
        {
            writer.WriteStartObject("coordinator");
            writer.WriteString("holder", Convert.ToHexStringLower(lease.Holder.Bytes.Span));
            writer.WriteNumber("term", lease.Term.Value);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("coordinator");
        }

        writer.WriteEndObject();
    }

    /// <summary>Reads one coordinated metadata record back.</summary>
    /// <param name="element">The element the record was written into.</param>
    /// <returns>The record.</returns>
    public static VeritasMetadataRecord Read(JsonElement element)
    {
        JsonElement listed = element.GetProperty("identityClaims");
        ImmutableArray<ReplicaIdentityClaim>.Builder claims = ImmutableArray.CreateBuilder<ReplicaIdentityClaim>(listed.GetArrayLength());
        foreach(JsonElement claim in listed.EnumerateArray())
        {
            claims.Add(new ReplicaIdentityClaim(ReadAxis(claim, "axis"), new RegisterVersion(claim.GetProperty("claimedAt").GetUInt64())));
        }

        LineageBaseline? baseline = null;
        JsonElement recorded = element.GetProperty("baseline");
        if(recorded.ValueKind != JsonValueKind.Null)
        {
            JsonElement confirming = recorded.GetProperty("confirmation");
            LineageConfirmation? confirmation = confirming.ValueKind == JsonValueKind.Null
                ? null
                : new LineageConfirmation(new NodeIdentifier(ReadUnsigned(confirming, "stateId")), ReadSigned(confirming, "dictionaryEpoch"));

            baseline = new LineageBaseline(
                ReadAxis(recorded, "claimantAxis"),
                new NodeIdentifier(ReadUnsigned(recorded, "causalityDigest")),
                confirmation,
                new RegisterVersion(recorded.GetProperty("recordedAt").GetUInt64()));
        }

        JsonElement policy = element.GetProperty("policy");
        CoordinationPolicy coordination = new(policy.GetProperty("healCadenceClass").GetInt32(), policy.GetProperty("symbolBudgetTier").GetInt32());

        CoordinatorLease? lease = null;
        JsonElement coordinator = element.GetProperty("coordinator");
        if(coordinator.ValueKind != JsonValueKind.Null)
        {
            lease = new CoordinatorLease(ReadAxis(coordinator, "holder"), new RegisterVersion(coordinator.GetProperty("term").GetUInt64()));
        }

        return new VeritasMetadataRecord(claims.MoveToImmutable(), baseline, coordination, lease);
    }

    /// <summary>Renders one sixty-four-bit quantity as the invariant decimal text the encoding carries.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The text.</returns>
    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a replica identity axis written as lower-case hexadecimal.</summary>
    /// <param name="element">The object carrying the field.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The axis.</returns>
    private static ReplicaAxis ReadAxis(JsonElement element, string name)
    {
        return new ReplicaAxis(Convert.FromHexString(element.GetProperty(name).GetString()!));
    }

    /// <summary>Reads an unsigned sixty-four-bit quantity written as invariant decimal text.</summary>
    /// <param name="element">The object carrying the field.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value.</returns>
    private static ulong ReadUnsigned(JsonElement element, string name)
    {
        return ulong.Parse(element.GetProperty(name).GetString()!, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a signed sixty-four-bit quantity written as invariant decimal text.</summary>
    /// <param name="element">The object carrying the field.</param>
    /// <param name="name">The field name.</param>
    /// <returns>The value.</returns>
    private static long ReadSigned(JsonElement element, string name)
    {
        return long.Parse(element.GetProperty(name).GetString()!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}
