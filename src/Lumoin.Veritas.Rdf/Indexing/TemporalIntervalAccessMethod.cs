using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Rdf.Indexing;

/// <summary>
/// The temporal value access method: an immutable sorted-endpoint index over the values of a declared
/// point axis or interval pair, on the same implicit-timezone-normalized instant axis the SPARQL
/// evaluator compares by.
/// </summary>
/// <remarks>
/// <para>
/// Values parse once at build through <see cref="DateTimeValue"/> and normalize through
/// <see cref="DateTimeValue.ToInstant"/> with the SAME implicit timezone the evaluator captured — one
/// shared routine, so a probe and a scan can never disagree on order. A lexical form that fails to parse
/// under the declared datatype is DROPPED at build (never a throw): the scan errors such a row out of a
/// temporal comparison, so dropping preserves probe/scan answer identity. An interval pair assembles by
/// an INNER join on the occurrence subject — a half-assembled occurrence is invisible, exactly matching
/// the two-pattern scan baseline — and a subject with several start or end values contributes every
/// combination, matching the baseline's cross product.
/// </para>
/// <para>
/// Probes are EXACT on the totalized axis: no envelope, no residual re-verification. A range probe
/// honours each bound's open/closed form; an as-of probe seeks the nearest predecessor on a point axis
/// and the covering intervals on a pair. Hits carry the store's original term ids in ascending axis
/// order with a deterministic tiebreak. A build replaces the prior state wholesale (the drop-and-rebuild
/// lifecycle); a probe against an unbuilt index yields no hits, and the consuming route declines to the
/// scan before ever reaching that state.
/// </para>
/// </remarks>
public sealed class TemporalIntervalAccessMethod: ValueAccessMethod
{
    /// <summary>The declared axis datatype IRI.</summary>
    private readonly Utf8String datatypeIri;

    /// <summary>The declared axis form.</summary>
    private readonly ValueAxisDeclaration axis;

    /// <summary>The temporal family the axis datatype classifies into.</summary>
    private readonly ValueSpace family;

    /// <summary>The built point-axis entries in axis order, or <see langword="null"/> when unbuilt or interval-shaped.</summary>
    private PointEntry[]? pointEntries;

    /// <summary>The built interval entries in start order, or <see langword="null"/> when unbuilt or point-shaped.</summary>
    private IntervalEntry[]? intervalEntries;

    /// <summary>Constructs the method over its declared axis.</summary>
    /// <param name="datatypeIri">The axis datatype IRI; must name a temporal family (<c>xsd:dateTime</c>/<c>xsd:dateTimeStamp</c>, <c>xsd:date</c>, or <c>xsd:time</c>).</param>
    /// <param name="axis">The declared axis form.</param>
    /// <param name="implicitTimezone">The implicit timezone naive values normalize with — the SAME one the evaluator's expression context captures; bound into any persisted form of this index.</param>
    /// <exception cref="ArgumentException"><paramref name="datatypeIri"/> is not a temporal family.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="implicitTimezone"/> exceeds the XSD ±14:00 offset bound.</exception>
    public TemporalIntervalAccessMethod(Utf8String datatypeIri, ValueAxisDeclaration axis, TimeSpan implicitTimezone)
    {
        family = ValueSpaceClassifier.Classify(datatypeIri);
        if(family is not (ValueSpace.DateTime or ValueSpace.Date or ValueSpace.Time))
        {
            throw new ArgumentException($"The temporal access method indexes the dateTime, date, and time families; '{datatypeIri}' is not one of them.", nameof(datatypeIri));
        }

        if(implicitTimezone < TimeSpan.FromHours(-14) || implicitTimezone > TimeSpan.FromHours(14))
        {
            throw new ArgumentOutOfRangeException(nameof(implicitTimezone), implicitTimezone, "The implicit timezone must lie within the XSD +/-14:00 offset bound.");
        }

        this.datatypeIri = datatypeIri;
        this.axis = axis;
        ImplicitTimezone = implicitTimezone;
    }

    /// <summary>The implicit timezone the axis normalizes naive values with — the value a persisted sidecar stamps and recovery validates against the engine's configuration.</summary>
    public TimeSpan ImplicitTimezone { get; }

    /// <summary>The composition-guard surface: the engine refuses to compose this method with an expression context whose implicit timezone differs (H1 must hold in process, not only across the persist boundary).</summary>
    public override TimeSpan? DeclaredImplicitTimezone => ImplicitTimezone;

    /// <summary>The declared axis form.</summary>
    public ValueAxisDeclaration Axis => axis;

    /// <summary>The declared axis datatype IRI.</summary>
    public override Utf8String DatatypeIri => datatypeIri;

    /// <summary>The shapes the axis form serves: the point families for a point axis, the overlap families for an interval pair — the nearest-predecessor primitive always.</summary>
    public override ValueIndexShapes DeclaredShapes =>
        axis.IsIntervalPair
            ? ValueIndexShapes.NearestPredecessor | ValueIndexShapes.IntervalOverlap
            : ValueIndexShapes.NearestPredecessor | ValueIndexShapes.RangeWindow | ValueIndexShapes.AsOfPoint | ValueIndexShapes.LastPerSeries;

    /// <summary>Builds (or rebuilds wholesale) the sorted index from the declared predicates' entries, dropping unparseable lexical forms.</summary>
    /// <param name="source">The declared predicates' entries.</param>
    /// <returns>Always <see cref="ValueIndexBuildOutcome.Built"/>.</returns>
    public override ValueIndexBuildOutcome Build(ValueSegmentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if(axis.IsIntervalPair)
        {
            intervalEntries = BuildIntervalEntries(source);
            pointEntries = null;
        }
        else
        {
            pointEntries = BuildPointEntries(source);
            intervalEntries = null;
        }

        return ValueIndexBuildOutcome.Built;
    }

    /// <summary>Opens a probe cursor over the built index; an unbuilt index or an unparseable probe bound yields no hits (the consuming route declines to the scan before either state is served).</summary>
    /// <param name="request">The probe.</param>
    /// <returns>The hit cursor.</returns>
    public override ValueProbeCursor OpenProbe(in ValueProbeRequest request)
    {
        if(pointEntries is { } points)
        {
            return OpenPointProbe(points, in request);
        }

        if(intervalEntries is { } intervals)
        {
            return OpenIntervalProbe(intervals, in request);
        }

        return new ArrayProbeCursor([]);
    }

    /// <summary>Builds a serializable snapshot from a source without touching this instance's live built state: the same parse-drop-sort the in-process build runs, stamped with the method's implicit timezone so recovery can never install it under another.</summary>
    /// <param name="source">The declared predicates' entries.</param>
    /// <returns>The snapshot.</returns>
    public override ValueIndexSnapshot BuildSnapshot(ValueSegmentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return axis.IsIntervalPair
            ? TemporalSnapshot.OverIntervals(BuildIntervalEntries(source), ImplicitTimezone)
            : TemporalSnapshot.OverPoints(BuildPointEntries(source), ImplicitTimezone);
    }

    /// <summary>
    /// Installs a persisted snapshot payload as the built state after full validation: the format
    /// version, the axis form, the implicit-timezone stamp (a payload normalized under another
    /// timezone is REFUSED — the persist/recover totalization boundary), the exact byte length, and
    /// the entries' sorted order are all checked before any state changes. A refusal leaves the
    /// method unbuilt, so the first probe rebuilds from the served store.
    /// </summary>
    /// <param name="payload">The persisted snapshot payload.</param>
    /// <returns><see langword="true"/> when the payload installed as the built state.</returns>
    public override bool TryInstallSnapshot(ReadOnlySpan<byte> payload)
    {
        if(payload.Length < SnapshotHeaderSize || payload[0] != SnapshotVersion)
        {
            return false;
        }

        if(payload[1] is not (SnapshotPointForm or SnapshotIntervalForm))
        {
            return false;
        }

        bool intervalForm = payload[1] == SnapshotIntervalForm;
        if(intervalForm != axis.IsIntervalPair)
        {
            return false;
        }

        if(BinaryPrimitives.ReadInt64LittleEndian(payload[2..]) != ImplicitTimezone.Ticks)
        {
            return false;
        }

        int rowCount = BinaryPrimitives.ReadInt32LittleEndian(payload[10..]);
        int rowSize = intervalForm ? IntervalRowSize : PointRowSize;
        if(rowCount < 0 || payload.Length != SnapshotHeaderSize + (long)rowCount * rowSize)
        {
            return false;
        }

        if(intervalForm)
        {
            IntervalEntry[] intervals = new IntervalEntry[rowCount];
            for(int i = 0; i < rowCount; i++)
            {
                ReadOnlySpan<byte> row = payload.Slice(SnapshotHeaderSize + (i * IntervalRowSize), IntervalRowSize);
                intervals[i] = new IntervalEntry(
                    new TimelineInstant(BinaryPrimitives.ReadInt64LittleEndian(row), BinaryPrimitives.ReadInt64LittleEndian(row[8..])),
                    new TimelineInstant(BinaryPrimitives.ReadInt64LittleEndian(row[16..]), BinaryPrimitives.ReadInt64LittleEndian(row[24..])),
                    TermId.FromEncoded(BinaryPrimitives.ReadUInt32LittleEndian(row[32..])),
                    TermId.FromEncoded(BinaryPrimitives.ReadUInt32LittleEndian(row[36..])),
                    TermId.FromEncoded(BinaryPrimitives.ReadUInt32LittleEndian(row[40..])));
                if(i > 0 && CompareIntervalEntries(intervals[i - 1], intervals[i]) > 0)
                {
                    return false;
                }
            }

            intervalEntries = intervals;
            pointEntries = null;

            return true;
        }

        PointEntry[] points = new PointEntry[rowCount];
        for(int i = 0; i < rowCount; i++)
        {
            ReadOnlySpan<byte> row = payload.Slice(SnapshotHeaderSize + (i * PointRowSize), PointRowSize);
            points[i] = new PointEntry(
                new TimelineInstant(BinaryPrimitives.ReadInt64LittleEndian(row), BinaryPrimitives.ReadInt64LittleEndian(row[8..])),
                TermId.FromEncoded(BinaryPrimitives.ReadUInt32LittleEndian(row[16..])),
                TermId.FromEncoded(BinaryPrimitives.ReadUInt32LittleEndian(row[20..])));
            if(i > 0 && ComparePointEntries(points[i - 1], points[i]) > 0)
            {
                return false;
            }
        }

        pointEntries = points;
        intervalEntries = null;

        return true;
    }

    /// <summary>The snapshot payload format version this implementation writes and accepts.</summary>
    private const byte SnapshotVersion = 1;

    /// <summary>The snapshot axis-form marker for a point axis.</summary>
    private const byte SnapshotPointForm = 0;

    /// <summary>The snapshot axis-form marker for an interval pair.</summary>
    private const byte SnapshotIntervalForm = 1;

    /// <summary>The snapshot payload header size: the version and form bytes, the implicit-timezone tick stamp, and the row count.</summary>
    private const int SnapshotHeaderSize = 1 + 1 + 8 + 4;

    /// <summary>One serialized point row's byte size: the instant's day and nanosecond, the subject id, and the value-term id.</summary>
    private const int PointRowSize = 8 + 8 + 4 + 4;

    /// <summary>One serialized interval row's byte size: both endpoints' days and nanoseconds, the occurrence id, and both value-term ids.</summary>
    private const int IntervalRowSize = 8 + 8 + 8 + 8 + 4 + 4 + 4;

    /// <summary>One point-axis entry: the normalized instant and the located terms.</summary>
    /// <param name="Key">The normalized axis instant.</param>
    /// <param name="Subject">The entry's subject.</param>
    /// <param name="Value">The value term's locator.</param>
    private readonly record struct PointEntry(TimelineInstant Key, TermId Subject, TermId Value);

    /// <summary>One assembled interval entry: the normalized endpoints and the located terms.</summary>
    /// <param name="Start">The normalized start instant.</param>
    /// <param name="End">The normalized end instant.</param>
    /// <param name="Occurrence">The occurrence subject joining the pair.</param>
    /// <param name="StartTerm">The start value term's locator.</param>
    /// <param name="EndTerm">The end value term's locator.</param>
    private readonly record struct IntervalEntry(TimelineInstant Start, TimelineInstant End, TermId Occurrence, TermId StartTerm, TermId EndTerm);

    /// <summary>Builds the sorted point-axis entries: parse (drop on failure), normalize, sort by instant with a deterministic id tiebreak.</summary>
    /// <param name="source">The entries.</param>
    /// <returns>The sorted entries.</returns>
    private PointEntry[] BuildPointEntries(ValueSegmentSource source)
    {
        List<PointEntry> entries = [];
        foreach(ValueSegmentEntry entry in source.EnumerateDeclared(axis.StartPredicateIri))
        {
            if(TryNormalize(entry.Value, out TimelineInstant key))
            {
                entries.Add(new PointEntry(key, entry.Subject, entry.ValueTerm));
            }
        }

        PointEntry[] built = [.. entries];
        Array.Sort(built, static (left, right) => ComparePointEntries(left, right));

        return built;
    }

    /// <summary>The point-axis entry order: the instant, then the subject and value ids as the deterministic tiebreak.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns>The comparison result.</returns>
    private static int ComparePointEntries(PointEntry left, PointEntry right)
    {
        int byKey = left.Key.CompareTo(right.Key);
        if(byKey != 0)
        {
            return byKey;
        }

        int bySubject = left.Subject.Encoded.CompareTo(right.Subject.Encoded);

        return bySubject != 0 ? bySubject : left.Value.Encoded.CompareTo(right.Value.Encoded);
    }

    /// <summary>Builds the sorted interval entries: the INNER join of the start and end predicates on the occurrence subject (every combination for a multi-valued subject, matching the two-pattern scan's cross product), unparseable endpoints dropped, sorted by start with a deterministic tiebreak.</summary>
    /// <param name="source">The entries.</param>
    /// <returns>The sorted entries.</returns>
    private IntervalEntry[] BuildIntervalEntries(ValueSegmentSource source)
    {
        Dictionary<TermId, List<(TimelineInstant Key, TermId Term)>> startsBySubject = [];
        foreach(ValueSegmentEntry entry in source.EnumerateDeclared(axis.StartPredicateIri))
        {
            if(TryNormalize(entry.Value, out TimelineInstant key))
            {
                if(!startsBySubject.TryGetValue(entry.Subject, out List<(TimelineInstant, TermId)>? starts))
                {
                    starts = [];
                    startsBySubject[entry.Subject] = starts;
                }

                starts.Add((key, entry.ValueTerm));
            }
        }

        List<IntervalEntry> entries = [];
        foreach(ValueSegmentEntry entry in source.EnumerateDeclared(axis.EndPredicateIri!.Value))
        {
            if(TryNormalize(entry.Value, out TimelineInstant endKey)
                && startsBySubject.TryGetValue(entry.Subject, out List<(TimelineInstant Key, TermId Term)>? starts))
            {
                foreach((TimelineInstant startKey, TermId startTerm) in starts)
                {
                    entries.Add(new IntervalEntry(startKey, endKey, entry.Subject, startTerm, entry.ValueTerm));
                }
            }
        }

        IntervalEntry[] built = [.. entries];
        Array.Sort(built, static (left, right) => CompareIntervalEntries(left, right));

        return built;
    }

    /// <summary>The interval entry order: the start, the end, then the occurrence and start-term ids as the deterministic tiebreak.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns>The comparison result.</returns>
    private static int CompareIntervalEntries(IntervalEntry left, IntervalEntry right)
    {
        int byStart = left.Start.CompareTo(right.Start);
        if(byStart != 0)
        {
            return byStart;
        }

        int byEnd = left.End.CompareTo(right.End);
        if(byEnd != 0)
        {
            return byEnd;
        }

        int byOccurrence = left.Occurrence.Encoded.CompareTo(right.Occurrence.Encoded);

        return byOccurrence != 0 ? byOccurrence : left.StartTerm.Encoded.CompareTo(right.StartTerm.Encoded);
    }

    /// <summary>Opens a point-axis probe: a range walks the sorted slice the bounds select; an as-of seeks the greatest key at or before the instant and reports every entry at that key.</summary>
    /// <param name="points">The built entries.</param>
    /// <param name="request">The probe.</param>
    /// <returns>The cursor.</returns>
    private ValueProbeCursor OpenPointProbe(PointEntry[] points, in ValueProbeRequest request)
    {
        if(request.Kind == ValueProbeKind.AsOf)
        {
            if(request.AsOf is null || !TryNormalize(request.AsOf, out TimelineInstant instant))
            {
                return new ArrayProbeCursor([]);
            }

            //The nearest-predecessor seek: the greatest key at or below the instant; every entry AT that key is
            //"the value in effect".
            int upper = UpperBoundIndex(points, instant, inclusive: true);
            if(upper == 0)
            {
                return new ArrayProbeCursor([]);
            }

            TimelineInstant predecessor = points[upper - 1].Key;
            int first = upper - 1;
            while(first > 0 && points[first - 1].Key.CompareTo(predecessor) == 0)
            {
                first--;
            }

            return new PointSliceCursor(points, first, upper);
        }

        int lower = 0;
        if(request.LowerBound is { } lowerLiteral)
        {
            if(!TryNormalize(lowerLiteral, out TimelineInstant lowerInstant))
            {
                return new ArrayProbeCursor([]);
            }

            lower = LowerBoundIndex(points, lowerInstant, request.LowerInclusive);
        }

        int upperExclusive = points.Length;
        if(request.UpperBound is { } upperLiteral)
        {
            if(!TryNormalize(upperLiteral, out TimelineInstant upperInstant))
            {
                return new ArrayProbeCursor([]);
            }

            upperExclusive = UpperBoundIndex(points, upperInstant, request.UpperInclusive);
        }

        return lower >= upperExclusive ? new ArrayProbeCursor([]) : new PointSliceCursor(points, lower, upperExclusive);
    }

    /// <summary>Opens an interval probe: overlap for a range window (start within the upper bound AND end within the lower), cover for an as-of instant; the walk stops at the first start past the window.</summary>
    /// <param name="intervals">The built entries.</param>
    /// <param name="request">The probe.</param>
    /// <returns>The cursor.</returns>
    private ValueProbeCursor OpenIntervalProbe(IntervalEntry[] intervals, in ValueProbeRequest request)
    {
        TimelineInstant? lower = null;
        bool lowerInclusive = true;
        TimelineInstant? upper = null;
        bool upperInclusive = true;

        if(request.Kind == ValueProbeKind.AsOf)
        {
            if(request.AsOf is null || !TryNormalize(request.AsOf, out TimelineInstant instant))
            {
                return new ArrayProbeCursor([]);
            }

            //Cover: start <= t <= end, the closed as-of reading.
            lower = instant;
            upper = instant;
        }
        else
        {
            if(request.LowerBound is { } lowerLiteral)
            {
                if(!TryNormalize(lowerLiteral, out TimelineInstant lowerInstant))
                {
                    return new ArrayProbeCursor([]);
                }

                lower = lowerInstant;
                lowerInclusive = request.LowerInclusive;
            }

            if(request.UpperBound is { } upperLiteral)
            {
                if(!TryNormalize(upperLiteral, out TimelineInstant upperInstant))
                {
                    return new ArrayProbeCursor([]);
                }

                upper = upperInstant;
                upperInclusive = request.UpperInclusive;
            }
        }

        return new IntervalOverlapCursor(intervals, lower, lowerInclusive, upper, upperInclusive);
    }

    /// <summary>Parses and normalizes a value literal onto the axis, honouring the family parser and the dateTimeStamp timezone requirement; false drops the entry (build) or empties the probe (bounds).</summary>
    /// <remarks>
    /// The literal's OWN datatype must classify into the axis family before its lexical form is even
    /// consulted: the scan errors a foreign-typed literal out of a temporal comparison regardless of
    /// how its lexical reads, so indexing a parseable-but-foreign-typed value (an <c>xsd:string</c>
    /// carrying an ISO timestamp) would make the probe answer rows the scan refuses.
    /// </remarks>
    /// <param name="value">The value literal.</param>
    /// <param name="instant">Receives the normalized instant.</param>
    /// <returns><see langword="true"/> when the literal is family-typed and its lexical form is valid.</returns>
    private bool TryNormalize(Literal value, out TimelineInstant instant)
    {
        instant = default;
        if(ValueSpaceClassifier.Classify(value.Datatype.Iri) != family)
        {
            return false;
        }

        bool parsed = family switch
        {
            ValueSpace.DateTime => DateTimeValue.TryParseDateTime(value.Value.Span, value.Datatype.Iri == Vocabulary.Xsd.DateTimeStamp, out DateTimeValue parsedValue) && Normalize(parsedValue, out instant),
            ValueSpace.Date => DateTimeValue.TryParseDate(value.Value.Span, out DateTimeValue parsedDate) && Normalize(parsedDate, out instant),
            _ => DateTimeValue.TryParseTime(value.Value.Span, out DateTimeValue parsedTime) && Normalize(parsedTime, out instant),
        };

        return parsed;
    }

    /// <summary>Normalizes a parsed value with the method's implicit timezone.</summary>
    /// <param name="value">The parsed value.</param>
    /// <param name="instant">Receives the instant.</param>
    /// <returns>Always <see langword="true"/>; shaped for the parse-and-normalize conjunction.</returns>
    private bool Normalize(DateTimeValue value, out TimelineInstant instant)
    {
        instant = value.ToInstant(ImplicitTimezone);

        return true;
    }

    /// <summary>The first index whose key is at or past <paramref name="bound"/> (past it when exclusive).</summary>
    /// <param name="points">The sorted entries.</param>
    /// <param name="bound">The lower bound.</param>
    /// <param name="inclusive">Whether the bound itself is included.</param>
    /// <returns>The slice start.</returns>
    private static int LowerBoundIndex(PointEntry[] points, TimelineInstant bound, bool inclusive)
    {
        int low = 0;
        int high = points.Length;
        while(low < high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = points[middle].Key.CompareTo(bound);
            if(comparison < 0 || (comparison == 0 && !inclusive))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>The exclusive end index of the slice bounded above by <paramref name="bound"/> (including entries AT it when inclusive).</summary>
    /// <param name="points">The sorted entries.</param>
    /// <param name="bound">The upper bound.</param>
    /// <param name="inclusive">Whether the bound itself is included.</param>
    /// <returns>The slice end, exclusive.</returns>
    private static int UpperBoundIndex(PointEntry[] points, TimelineInstant bound, bool inclusive)
    {
        int low = 0;
        int high = points.Length;
        while(low < high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = points[middle].Key.CompareTo(bound);
            if(comparison < 0 || (comparison == 0 && inclusive))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>A cursor over a materialized hit array.</summary>
    /// <param name="hits">The hits in axis order.</param>
    private sealed class ArrayProbeCursor(ValueProbeHit[] hits): ValueProbeCursor
    {
        /// <summary>The hits in axis order.</summary>
        private ValueProbeHit[] Hits { get; } = hits;

        /// <summary>The next hit's index.</summary>
        private int Position { get; set; }

        /// <summary>Advances to the next hit.</summary>
        /// <param name="hit">Receives the next hit.</param>
        /// <returns><see langword="true"/> while hits remain.</returns>
        public override bool TryAdvance(out ValueProbeHit hit)
        {
            if(Position >= Hits.Length)
            {
                hit = default;

                return false;
            }

            hit = Hits[Position];
            Position++;

            return true;
        }
    }

    /// <summary>A cursor over a contiguous slice of the sorted point entries.</summary>
    /// <param name="points">The sorted entries.</param>
    /// <param name="start">The slice start.</param>
    /// <param name="endExclusive">The slice end, exclusive.</param>
    private sealed class PointSliceCursor(PointEntry[] points, int start, int endExclusive): ValueProbeCursor
    {
        /// <summary>The sorted entries.</summary>
        private PointEntry[] Points { get; } = points;

        /// <summary>The slice end, exclusive.</summary>
        private int EndExclusive { get; } = endExclusive;

        /// <summary>The next entry's index.</summary>
        private int Position { get; set; } = start;

        /// <summary>Advances to the next hit.</summary>
        /// <param name="hit">Receives the next hit.</param>
        /// <returns><see langword="true"/> while hits remain.</returns>
        public override bool TryAdvance(out ValueProbeHit hit)
        {
            if(Position >= EndExclusive)
            {
                hit = default;

                return false;
            }

            PointEntry entry = Points[Position];
            hit = new ValueProbeHit(entry.Subject, entry.Value, TermId.None);
            Position++;

            return true;
        }
    }

    /// <summary>A cursor over the start-sorted interval entries, yielding those whose interval intersects the probe window; the walk ends at the first start past the window's upper bound.</summary>
    /// <param name="intervals">The sorted entries.</param>
    /// <param name="lower">The window's lower bound, or <see langword="null"/> for none.</param>
    /// <param name="lowerInclusive">Whether an interval ending exactly at the lower bound intersects.</param>
    /// <param name="upper">The window's upper bound, or <see langword="null"/> for none.</param>
    /// <param name="upperInclusive">Whether an interval starting exactly at the upper bound intersects.</param>
    private sealed class IntervalOverlapCursor(IntervalEntry[] intervals, TimelineInstant? lower, bool lowerInclusive, TimelineInstant? upper, bool upperInclusive): ValueProbeCursor
    {
        /// <summary>The sorted entries.</summary>
        private IntervalEntry[] Intervals { get; } = intervals;

        /// <summary>The window's lower bound, or <see langword="null"/>.</summary>
        private TimelineInstant? Lower { get; } = lower;

        /// <summary>Whether an interval ending exactly at the lower bound intersects.</summary>
        private bool LowerInclusive { get; } = lowerInclusive;

        /// <summary>The window's upper bound, or <see langword="null"/>.</summary>
        private TimelineInstant? Upper { get; } = upper;

        /// <summary>Whether an interval starting exactly at the upper bound intersects.</summary>
        private bool UpperInclusive { get; } = upperInclusive;

        /// <summary>The next entry's index.</summary>
        private int Position { get; set; }

        /// <summary>Advances to the next intersecting interval.</summary>
        /// <param name="hit">Receives the next hit: the occurrence subject and its start term.</param>
        /// <returns><see langword="true"/> while intersecting intervals remain.</returns>
        public override bool TryAdvance(out ValueProbeHit hit)
        {
            while(Position < Intervals.Length)
            {
                IntervalEntry entry = Intervals[Position];

                //Entries are start-sorted, so the first start past the window ends the walk.
                if(Upper is { } upperBound)
                {
                    int startVersusUpper = entry.Start.CompareTo(upperBound);
                    if(startVersusUpper > 0 || (startVersusUpper == 0 && !UpperInclusive))
                    {
                        break;
                    }
                }

                Position++;
                if(Lower is { } lowerBound)
                {
                    int endVersusLower = entry.End.CompareTo(lowerBound);
                    if(endVersusLower < 0 || (endVersusLower == 0 && !LowerInclusive))
                    {
                        continue;
                    }
                }

                hit = new ValueProbeHit(entry.Occurrence, entry.StartTerm, entry.EndTerm);

                return true;
            }

            hit = default;

            return false;
        }
    }

    /// <summary>
    /// The temporal method's serializable snapshot: the sorted entries of one axis form and the
    /// implicit-timezone stamp they were normalized under, in the payload format
    /// <see cref="TryInstallSnapshot"/> validates and installs.
    /// </summary>
    private sealed class TemporalSnapshot: ValueIndexSnapshot
    {
        /// <summary>The sorted point entries, or <see langword="null"/> for an interval snapshot.</summary>
        private PointEntry[]? Points { get; }

        /// <summary>The sorted interval entries, or <see langword="null"/> for a point snapshot.</summary>
        private IntervalEntry[]? Intervals { get; }

        /// <summary>The implicit timezone the entries were normalized under — the persist/recover totalization stamp.</summary>
        private TimeSpan ImplicitTimezone { get; }

        /// <summary>Constructs the snapshot over exactly one axis form's entries.</summary>
        /// <param name="points">The sorted point entries, or <see langword="null"/>.</param>
        /// <param name="intervals">The sorted interval entries, or <see langword="null"/>.</param>
        /// <param name="implicitTimezone">The implicit timezone the entries were normalized under.</param>
        private TemporalSnapshot(PointEntry[]? points, IntervalEntry[]? intervals, TimeSpan implicitTimezone)
        {
            Points = points;
            Intervals = intervals;
            ImplicitTimezone = implicitTimezone;
        }

        /// <summary>Wraps sorted point entries as a snapshot.</summary>
        /// <param name="points">The sorted point entries.</param>
        /// <param name="implicitTimezone">The implicit timezone the entries were normalized under.</param>
        /// <returns>The snapshot.</returns>
        public static TemporalSnapshot OverPoints(PointEntry[] points, TimeSpan implicitTimezone)
        {
            return new TemporalSnapshot(points, intervals: null, implicitTimezone);
        }

        /// <summary>Wraps sorted interval entries as a snapshot.</summary>
        /// <param name="intervals">The sorted interval entries.</param>
        /// <param name="implicitTimezone">The implicit timezone the entries were normalized under.</param>
        /// <returns>The snapshot.</returns>
        public static TemporalSnapshot OverIntervals(IntervalEntry[] intervals, TimeSpan implicitTimezone)
        {
            return new TemporalSnapshot(points: null, intervals, implicitTimezone);
        }

        /// <summary>The serialized payload's byte size.</summary>
        public override int PayloadSize => SnapshotHeaderSize + (Intervals is { } intervals
            ? intervals.Length * IntervalRowSize
            : Points!.Length * PointRowSize);

        /// <summary>Writes the payload: the version and form bytes, the timezone tick stamp, the row count, and the sorted rows.</summary>
        /// <param name="destination">The destination buffer.</param>
        public override void WriteTo(Span<byte> destination)
        {
            destination[0] = SnapshotVersion;
            destination[1] = Intervals is not null ? SnapshotIntervalForm : SnapshotPointForm;
            BinaryPrimitives.WriteInt64LittleEndian(destination[2..], ImplicitTimezone.Ticks);

            if(Intervals is { } intervals)
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination[10..], intervals.Length);
                for(int i = 0; i < intervals.Length; i++)
                {
                    Span<byte> row = destination.Slice(SnapshotHeaderSize + (i * IntervalRowSize), IntervalRowSize);
                    IntervalEntry entry = intervals[i];
                    BinaryPrimitives.WriteInt64LittleEndian(row, entry.Start.Day);
                    BinaryPrimitives.WriteInt64LittleEndian(row[8..], entry.Start.NanosecondOfDay);
                    BinaryPrimitives.WriteInt64LittleEndian(row[16..], entry.End.Day);
                    BinaryPrimitives.WriteInt64LittleEndian(row[24..], entry.End.NanosecondOfDay);
                    BinaryPrimitives.WriteUInt32LittleEndian(row[32..], entry.Occurrence.Encoded);
                    BinaryPrimitives.WriteUInt32LittleEndian(row[36..], entry.StartTerm.Encoded);
                    BinaryPrimitives.WriteUInt32LittleEndian(row[40..], entry.EndTerm.Encoded);
                }

                return;
            }

            PointEntry[] points = Points!;
            BinaryPrimitives.WriteInt32LittleEndian(destination[10..], points.Length);
            for(int i = 0; i < points.Length; i++)
            {
                Span<byte> row = destination.Slice(SnapshotHeaderSize + (i * PointRowSize), PointRowSize);
                PointEntry entry = points[i];
                BinaryPrimitives.WriteInt64LittleEndian(row, entry.Key.Day);
                BinaryPrimitives.WriteInt64LittleEndian(row[8..], entry.Key.NanosecondOfDay);
                BinaryPrimitives.WriteUInt32LittleEndian(row[16..], entry.Subject.Encoded);
                BinaryPrimitives.WriteUInt32LittleEndian(row[20..], entry.Value.Encoded);
            }
        }
    }
}
