using System;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>The outcome of a value-index build.</summary>
public enum ValueIndexBuildOutcome
{
    /// <summary>The index is built and probes may be opened against it.</summary>
    Built,

    /// <summary>The build declined; every probe falls through to the scan until a later build succeeds.</summary>
    Declined,
}

/// <summary>
/// A value-typed access method: an index over the value space of (predicate, datatype) declarations that
/// answers query shapes the triple engines cannot — locating, never bucketing, aggregating, or filling.
/// </summary>
/// <remarks>
/// <para>
/// The contract has two primitives. The nearest-predecessor seek is MANDATORY (every method declares
/// <see cref="ValueIndexShapes.NearestPredecessor"/>); interval overlap is OPT-IN via
/// <see cref="DeclaredShapes"/>. A method builds from a <see cref="ValueSegmentSource"/> — the
/// engine's post-commit predicate access, or a registrant-supplied sample corpus during acceptance —
/// and answers probes with <see cref="ValueProbeCursor"/> locators into the store's original terms.
/// </para>
/// <para>
/// A lexical form that fails to parse under <see cref="DatatypeIri"/> is DROPPED at build (never a
/// throw): the scan errors such a row out of a value comparison, so dropping preserves probe/scan
/// answer identity. A probe against an unbuilt or invalidated index declines so the caller scans.
/// The method owns its own segment lifecycle — its freeze points and its compaction — decoupled from
/// the store's columnar index.
/// </para>
/// </remarks>
public abstract class ValueAccessMethod
{
    /// <summary>The datatype IRI of the axis values this method indexes.</summary>
    public abstract Utf8String DatatypeIri { get; }

    /// <summary>The query shapes this method declares it answers; must include the mandatory <see cref="ValueIndexShapes.NearestPredecessor"/> primitive.</summary>
    public abstract ValueIndexShapes DeclaredShapes { get; }

    /// <summary>
    /// The implicit timezone this method normalizes naive values with, or <see langword="null"/> for a
    /// method whose value space has no timezone notion. A non-null declaration is a composition
    /// invariant: the engine refuses to compose the method with an evaluator whose implicit timezone
    /// differs, because a probe and a scan normalizing under different timezones would order naive
    /// values differently — the divergence must fail loudly at composition, never silently at query time.
    /// </summary>
    public virtual TimeSpan? DeclaredImplicitTimezone => null;

    /// <summary>Builds (or rebuilds, replacing prior state wholesale) the index from a source.</summary>
    /// <param name="source">The declared predicates' entries.</param>
    /// <returns>Whether the index is built or the build declined.</returns>
    public abstract ValueIndexBuildOutcome Build(ValueSegmentSource source);

    /// <summary>Opens a probe cursor over the built index.</summary>
    /// <param name="request">The probe.</param>
    /// <returns>The hit cursor; empty when nothing matches, and declining (empty with the index unbuilt) is surfaced by the registry consumer falling back to the scan.</returns>
    public abstract ValueProbeCursor OpenProbe(in ValueProbeRequest request);

    /// <summary>
    /// Builds a serializable snapshot of the index state a durable sidecar persists, WITHOUT touching
    /// this instance's live built state — the snapshot derives wholly from <paramref name="source"/>,
    /// so a persist can never tear a concurrently served index. Snapshot support is opt-in: the
    /// default declines, and a declining method simply rebuilds from the served store at the first
    /// probe after recovery.
    /// </summary>
    /// <param name="source">The declared predicates' entries the snapshot is built from.</param>
    /// <returns>The snapshot, or <see langword="null"/> when the method does not persist snapshots.</returns>
    public virtual ValueIndexSnapshot? BuildSnapshot(ValueSegmentSource source)
    {
        return null;
    }

    /// <summary>
    /// Installs a previously persisted snapshot payload as this instance's built state, validating the
    /// payload structurally and against the method's own configuration stamps before accepting it — a
    /// snapshot built under a different configuration (for the temporal method, a different implicit
    /// timezone) is REFUSED, never served. A refusal leaves the method unbuilt, so the consuming route
    /// rebuilds from the served store at the first probe.
    /// </summary>
    /// <param name="payload">The persisted snapshot payload.</param>
    /// <returns><see langword="true"/> when the payload installed as the built state.</returns>
    public virtual bool TryInstallSnapshot(ReadOnlySpan<byte> payload)
    {
        return false;
    }
}
