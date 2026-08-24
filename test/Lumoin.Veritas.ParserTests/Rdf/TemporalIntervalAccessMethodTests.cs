using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Rdf.Indexing;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Method-level pins for <see cref="TemporalIntervalAccessMethod"/>, keyed to the temporal method's
/// certification families: coincident endpoints under all four open/closed window forms, single-point
/// intervals, empty and singleton bases, the R5 inner-join partial-occurrence rule, the R7
/// drop-not-index rule, the H1 shared implicit-timezone normalization, deterministic hit order, and
/// acceptance of the REAL method through the registry ladder with independently derivable sample cases.
/// </summary>
[TestClass]
internal sealed class TemporalIntervalAccessMethodTests
{
    /// <summary>The point-axis predicate.</summary>
    private static Utf8String At => Utf8Strings.From("http://example.org/at");

    /// <summary>The interval start predicate.</summary>
    private static Utf8String From => Utf8Strings.From("http://example.org/from");

    /// <summary>The interval end predicate.</summary>
    private static Utf8String Until => Utf8Strings.From("http://example.org/until");

    /// <summary>Expected hits: occurrence 1 alone.</summary>
    private static long[] OccurrenceOneOnly { get; } = [1L];

    /// <summary>Expected hits: occurrence 2 alone.</summary>
    private static long[] OccurrenceTwoOnly { get; } = [2L];

    /// <summary>Expected hits: occurrence 4 alone.</summary>
    private static long[] OccurrenceFourOnly { get; } = [4L];

    /// <summary>Expected hits: occurrence 7 alone.</summary>
    private static long[] OccurrenceSevenOnly { get; } = [7L];

    /// <summary>Expected hits: occurrences 1 and 2, in axis order.</summary>
    private static long[] OccurrencesOneAndTwo { get; } = [1L, 2L];

    /// <summary>Expected hits: occurrences 1 and 3, in axis order.</summary>
    private static long[] OccurrencesOneAndThree { get; } = [1L, 3L];

    /// <summary>Expected hits: occurrences 2 and 3, in axis order.</summary>
    private static long[] OccurrencesTwoAndThree { get; } = [2L, 3L];

    /// <summary>Expected hits for the determinism pin: subjects in ascending axis order, not source order.</summary>
    private static long[] AxisOrderedSubjects { get; } = [2L, 3L, 1L];

    /// <summary>Expected hits: occurrence 3 alone.</summary>
    private static long[] OccurrenceThreeOnly { get; } = [3L];

    /// <summary>Expected hits for the sentinel pin: all three occurrences in ascending axis order (early sentinel, middle, late sentinel).</summary>
    private static long[] AllThreeInAxisOrder { get; } = [1L, 2L, 3L];

    /// <summary>[a,b] meets [b,c]: the shared endpoint b is hit or missed exactly per the window's open/closed forms — the sweep off-by-one killer.</summary>
    [TestMethod]
    public void CoincidentEndpointsHonourAllFourWindowForms()
    {
        //Interval 1 = [01-01, 01-02], interval 2 = [01-02, 01-03]; the probe window pivots on 01-02.
        TemporalIntervalAccessMethod method = IntervalMethod();
        method.Build(new MapSource(
            (From, [Entry(1, 101, "2020-01-01T00:00:00Z"), Entry(2, 102, "2020-01-02T00:00:00Z")]),
            (Until, [Entry(1, 111, "2020-01-02T00:00:00Z"), Entry(2, 112, "2020-01-03T00:00:00Z")])));

        Literal pivot = DateTime("2020-01-02T00:00:00Z");

        //Closed-closed [b,b]: both intervals touch b.
        Assert.AreSequenceEqual(OccurrencesOneAndTwo, Occurrences(method, ValueProbeRequest.Range(pivot, true, pivot, true)));

        //Open lower (b,∞): interval 1 ends AT b and is excluded; interval 2 remains.
        Assert.AreSequenceEqual(OccurrenceTwoOnly, Occurrences(method, ValueProbeRequest.Range(pivot, false, null, false)));

        //Open upper (-∞,b): interval 2 starts AT b and is excluded; interval 1 remains.
        Assert.AreSequenceEqual(OccurrenceOneOnly, Occurrences(method, ValueProbeRequest.Range(null, false, pivot, false)));

        //Open-open (b,b): nothing.
        Assert.IsEmpty(Occurrences(method, ValueProbeRequest.Range(pivot, false, pivot, false)));
    }

    /// <summary>A single-point interval (start == end) behaves as one instant: covered exactly when the window touches it.</summary>
    [TestMethod]
    public void SinglePointIntervalIsOneInstant()
    {
        TemporalIntervalAccessMethod method = IntervalMethod();
        method.Build(new MapSource(
            (From, [Entry(1, 101, "2020-06-15T12:00:00Z")]),
            (Until, [Entry(1, 111, "2020-06-15T12:00:00Z")])));

        Assert.HasCount(1, Occurrences(method, ValueProbeRequest.AtInstant(DateTime("2020-06-15T12:00:00Z"))));
        Assert.IsEmpty(Occurrences(method, ValueProbeRequest.AtInstant(DateTime("2020-06-15T12:00:00.000000001Z"))));
        Assert.IsEmpty(Occurrences(method, ValueProbeRequest.Range(DateTime("2020-06-15T12:00:00Z"), false, null, false)));
    }

    /// <summary>Empty and singleton bases: an empty build probes empty everywhere; a singleton base answers exactly its own membership.</summary>
    [TestMethod]
    public void EmptyAndSingletonBasesProbeExactly()
    {
        TemporalIntervalAccessMethod empty = PointMethod();
        empty.Build(new MapSource((At, [])));
        Assert.IsEmpty(Occurrences(empty, ValueProbeRequest.Range(null, false, null, false)));

        TemporalIntervalAccessMethod singleton = PointMethod();
        singleton.Build(new MapSource((At, [Entry(7, 70, "2021-03-01T00:00:00Z")])));
        Assert.AreSequenceEqual(OccurrenceSevenOnly, Occurrences(singleton, ValueProbeRequest.Range(null, false, null, false)));
        Assert.IsEmpty(Occurrences(singleton, ValueProbeRequest.Range(DateTime("2021-03-01T00:00:01Z"), true, null, false)));
    }

    /// <summary>R5: a half-assembled occurrence (start committed, end absent) is INVISIBLE — the inner join, exactly the two-pattern scan baseline; completing the end in a later build makes it visible.</summary>
    [TestMethod]
    public void PartialOccurrenceIsInvisibleUntilCompleted()
    {
        TemporalIntervalAccessMethod method = IntervalMethod();
        method.Build(new MapSource(
            (From, [Entry(1, 101, "2020-01-01T00:00:00Z"), Entry(2, 102, "2020-01-01T06:00:00Z")]),
            (Until, [Entry(1, 111, "2020-01-02T00:00:00Z")])));

        //Occurrence 2 has no end: invisible to every probe.
        Assert.AreSequenceEqual(OccurrenceOneOnly, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));

        //A later rebuild with the end present makes it visible — drop-and-rebuild, never a partial answer.
        method.Build(new MapSource(
            (From, [Entry(1, 101, "2020-01-01T00:00:00Z"), Entry(2, 102, "2020-01-01T06:00:00Z")]),
            (Until, [Entry(1, 111, "2020-01-02T00:00:00Z"), Entry(2, 112, "2020-01-02T06:00:00Z")])));
        Assert.AreSequenceEqual(OccurrencesOneAndTwo, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>A multi-valued occurrence contributes every (start, end) combination — the two-pattern scan's cross product, never a paired subset.</summary>
    [TestMethod]
    public void MultiValuedOccurrenceMatchesTheCrossProduct()
    {
        TemporalIntervalAccessMethod method = IntervalMethod();
        method.Build(new MapSource(
            (From, [Entry(1, 101, "2020-01-01T00:00:00Z"), Entry(1, 102, "2020-01-05T00:00:00Z")]),
            (Until, [Entry(1, 111, "2020-01-03T00:00:00Z"), Entry(1, 112, "2020-01-07T00:00:00Z")])));

        //2 starts x 2 ends = 4 combinations, all with occurrence 1.
        Assert.HasCount(4, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>R7: a lexical form invalid for the declared datatype is dropped at build — the scan errors that row out of a temporal comparison, so dropping preserves probe/scan identity; a dateTimeStamp axis additionally drops timezone-less entries.</summary>
    [TestMethod]
    public void MalformedLexicalFormsAreDroppedNotIndexed()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        method.Build(new MapSource((At,
        [
            Entry(1, 10, "2020-01-01T00:00:00Z"),
            Entry(2, 20, "2020-13-45T99:99:99"),
            Entry(3, 30, "2020-01-03T00:00:00Z"),
        ])));
        Assert.AreSequenceEqual(OccurrencesOneAndThree, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));

        TemporalIntervalAccessMethod stampAxis = new(
            Vocabulary.Xsd.DateTimeStamp,
            ValueAxisDeclaration.PointAxis(At),
            TimeSpan.Zero);
        stampAxis.Build(new MapSource((At,
        [
            new ValueSegmentEntry(TermId.FromEncoded(1), TermId.FromEncoded(10), StampLiteral("2020-01-01T00:00:00Z")),
            new ValueSegmentEntry(TermId.FromEncoded(2), TermId.FromEncoded(20), StampLiteral("2020-01-02T00:00:00")),
        ])));
        Assert.AreSequenceEqual(OccurrenceOneOnly, Occurrences(stampAxis, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>H1: the axis normalizes naive values with the method's implicit timezone through the SAME routine the evaluator uses — a UTC method and a +02:00 method place the same naive value on provably different instants, and each agrees with the comparator under its own timezone.</summary>
    [TestMethod]
    public void ImplicitTimezoneNormalizationMatchesTheEvaluatorRoutine()
    {
        const string Naive = "2020-01-01T02:30:00";
        const string Aware = "2020-01-01T01:00:00Z";

        //Under UTC the naive 02:30 is AFTER 01:00Z; under +02:00 it normalizes to 00:30Z, BEFORE. The probe
        //window (aware, +inf) must flip accordingly — and each verdict equals the comparator's under the same
        //timezone, because both run DateTimeValue.ToInstant.
        TemporalIntervalAccessMethod utc = PointMethod(TimeSpan.Zero);
        utc.Build(new MapSource((At, [Entry(1, 10, Naive)])));
        Assert.HasCount(1, Occurrences(utc, ValueProbeRequest.Range(DateTime(Aware), false, null, false)));
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(DateTime(Naive), DateTime(Aware), TimeSpan.Zero));

        TemporalIntervalAccessMethod plusTwo = PointMethod(TimeSpan.FromHours(2));
        plusTwo.Build(new MapSource((At, [Entry(1, 10, Naive)])));
        Assert.IsEmpty(Occurrences(plusTwo, ValueProbeRequest.Range(DateTime(Aware), false, null, false)));
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(DateTime(Naive), DateTime(Aware), TimeSpan.FromHours(2)));
    }

    /// <summary>The as-of point probe is the nearest-predecessor seek: an exact hit, a between-keys probe answering the earlier key, every entry AT the predecessor key, and empty before the first key.</summary>
    [TestMethod]
    public void AsOfPointProbeSeeksTheNearestPredecessor()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        method.Build(new MapSource((At,
        [
            Entry(1, 10, "2020-01-01T00:00:00Z"),
            Entry(2, 20, "2020-01-03T00:00:00Z"),
            Entry(3, 30, "2020-01-03T02:00:00+02:00"),
            Entry(4, 40, "2020-01-05T00:00:00Z"),
        ])));

        //Between keys: the 01-03 instant wins; entries 2 and 3 are two lexical forms of that ONE instant and
        //both are "in effect".
        Assert.AreSequenceEqual(OccurrencesTwoAndThree, Occurrences(method, ValueProbeRequest.AtInstant(DateTime("2020-01-04T12:00:00Z"))));

        //Exact hit.
        Assert.AreSequenceEqual(OccurrenceFourOnly, Occurrences(method, ValueProbeRequest.AtInstant(DateTime("2020-01-05T00:00:00Z"))));

        //Before the first key: nothing is in effect.
        Assert.IsEmpty(Occurrences(method, ValueProbeRequest.AtInstant(DateTime("2019-12-31T23:59:59Z"))));
    }

    /// <summary>Hits arrive in ascending axis order with the deterministic id tiebreak, independent of source enumeration order.</summary>
    [TestMethod]
    public void HitOrderIsDeterministicAndAscending()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        method.Build(new MapSource((At,
        [
            Entry(3, 30, "2020-01-02T00:00:00Z"),
            Entry(1, 10, "2020-01-03T00:00:00Z"),
            Entry(2, 20, "2020-01-01T00:00:00Z"),
        ])));

        Assert.AreSequenceEqual(AxisOrderedSubjects, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>The REAL method passes the registry acceptance ladder with independently derivable sample cases — the registration path exercised by its first genuine consumer.</summary>
    [TestMethod]
    public void RealMethodPassesRegistryAcceptance()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        MapSource sample = new((At,
        [
            Entry(1, 10, "2020-01-01T00:00:00Z"),
            Entry(2, 20, "2020-01-02T00:00:00Z"),
        ]));
        List<ValueIndexSelfTestCase> cases =
        [
            new ValueIndexSelfTestCase(
                ValueProbeRequest.Range(DateTime("2020-01-02T00:00:00Z"), true, null, false),
                [new ValueProbeHit(TermId.FromEncoded(2), TermId.FromEncoded(20), TermId.None)]),
            new ValueIndexSelfTestCase(
                ValueProbeRequest.AtInstant(DateTime("2020-01-01T12:00:00Z")),
                [new ValueProbeHit(TermId.FromEncoded(1), TermId.FromEncoded(10), TermId.None)]),
        ];

        ValueIndexRegistry registry = new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(method, ValueAxisDeclaration.PointAxis(At), sample, cases))
            .Build();

        Assert.HasCount(1, registry.Registrations);
        Assert.IsNotNull(registry.FindByPredicate(At));
    }

    /// <summary>A literal whose OWN datatype is outside the axis family is dropped at build even when its lexical form parses as a timestamp — the scan errors a foreign-typed literal out of a temporal comparison regardless of its lexical, so indexing it would make the probe answer rows the scan refuses.</summary>
    [TestMethod]
    public void ForeignTypedParseableLexicalIsNotIndexed()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        method.Build(new MapSource((At,
        [
            Entry(1, 10, "2020-01-01T00:00:00Z"),
            new ValueSegmentEntry(TermId.FromEncoded(2), TermId.FromEncoded(20), new Literal(Utf8Strings.From("2020-06-01T00:00:00Z"), new NamedNode(Vocabulary.Xsd.String))),
            Entry(3, 30, "2020-01-03T00:00:00Z"),
        ])));

        Assert.AreSequenceEqual(OccurrencesOneAndThree, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>The sentinel family: entries adjacent to the proleptic axis extremes (five-digit negative and positive years) index, window, and order exactly — no overflow or off-by-one at the representable edges.</summary>
    [TestMethod]
    public void AxisExtremeSentinelsProbeExactly()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        method.Build(new MapSource((At,
        [
            Entry(2, 20, "2020-01-01T00:00:00Z"),
            Entry(3, 30, "99999-12-31T23:59:59Z"),
            Entry(1, 10, "-99999-01-01T00:00:00Z"),
        ])));

        Assert.AreSequenceEqual(AllThreeInAxisOrder, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));
        Assert.AreSequenceEqual(OccurrenceThreeOnly, Occurrences(method, ValueProbeRequest.Range(DateTime("2020-01-02T00:00:00Z"), true, null, false)));
        Assert.AreSequenceEqual(OccurrenceOneOnly, Occurrences(method, ValueProbeRequest.Range(null, false, DateTime("-99999-01-01T00:00:00Z"), true)));
        Assert.AreSequenceEqual(OccurrencesOneAndTwo, Occurrences(method, ValueProbeRequest.Range(null, false, DateTime("99999-12-31T23:59:59Z"), false)));
    }

    /// <summary>The leap-second row: XSD 1.1 requires the second field below 60 (the spec carries no leap-second representation), so a <c>:60</c> lexical form is invalid and R7 drops it at build — the scan errors the same row out of any temporal comparison, preserving probe/scan identity.</summary>
    [TestMethod]
    public void LeapSecondLexicalFormIsDroppedAtBuild()
    {
        TemporalIntervalAccessMethod method = PointMethod();
        method.Build(new MapSource((At,
        [
            Entry(1, 10, "2016-12-31T23:59:59Z"),
            Entry(2, 20, "2016-12-31T23:59:60Z"),
            Entry(3, 30, "2017-01-01T00:00:00Z"),
        ])));

        Assert.AreSequenceEqual(OccurrencesOneAndThree, Occurrences(method, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>A point-axis snapshot round-trips: built PURE from a source (the writer instance stays unbuilt), it installs into a second method whose probes answer exactly as a direct build's.</summary>
    [TestMethod]
    public void SnapshotRoundTripsThePointAxis()
    {
        MapSource source = new((At,
        [
            Entry(3, 30, "2020-01-02T00:00:00Z"),
            Entry(1, 10, "2020-01-03T00:00:00Z"),
            Entry(2, 20, "2020-01-01T00:00:00Z"),
        ]));

        TemporalIntervalAccessMethod writer = PointMethod();
        ValueIndexSnapshot snapshot = writer.BuildSnapshot(source);
        Assert.IsEmpty(Occurrences(writer, ValueProbeRequest.Range(null, false, null, false)));

        byte[] payload = new byte[snapshot.PayloadSize];
        snapshot.WriteTo(payload);

        TemporalIntervalAccessMethod restored = PointMethod();
        Assert.IsTrue(restored.TryInstallSnapshot(payload));

        TemporalIntervalAccessMethod direct = PointMethod();
        direct.Build(source);

        Assert.AreSequenceEqual(
            Occurrences(direct, ValueProbeRequest.Range(null, false, null, false)),
            Occurrences(restored, ValueProbeRequest.Range(null, false, null, false)));
        Assert.AreSequenceEqual(
            Occurrences(direct, ValueProbeRequest.AtInstant(DateTime("2020-01-02T12:00:00Z"))),
            Occurrences(restored, ValueProbeRequest.AtInstant(DateTime("2020-01-02T12:00:00Z"))));
    }

    /// <summary>An interval-pair snapshot round-trips, cross product and all, answering exactly as a direct build.</summary>
    [TestMethod]
    public void SnapshotRoundTripsTheIntervalPair()
    {
        MapSource source = new(
            (From, [Entry(1, 101, "2020-01-01T00:00:00Z"), Entry(1, 102, "2020-01-05T00:00:00Z"), Entry(2, 103, "2020-01-02T00:00:00Z")]),
            (Until, [Entry(1, 111, "2020-01-03T00:00:00Z"), Entry(1, 112, "2020-01-07T00:00:00Z"), Entry(2, 113, "2020-01-04T00:00:00Z")]));

        TemporalIntervalAccessMethod writer = IntervalMethod();
        ValueIndexSnapshot snapshot = writer.BuildSnapshot(source);
        byte[] payload = new byte[snapshot.PayloadSize];
        snapshot.WriteTo(payload);

        TemporalIntervalAccessMethod restored = IntervalMethod();
        Assert.IsTrue(restored.TryInstallSnapshot(payload));

        TemporalIntervalAccessMethod direct = IntervalMethod();
        direct.Build(source);

        Assert.AreSequenceEqual(
            Occurrences(direct, ValueProbeRequest.Range(null, false, null, false)),
            Occurrences(restored, ValueProbeRequest.Range(null, false, null, false)));
        Assert.AreSequenceEqual(
            Occurrences(direct, ValueProbeRequest.AtInstant(DateTime("2020-01-03T12:00:00Z"))),
            Occurrences(restored, ValueProbeRequest.AtInstant(DateTime("2020-01-03T12:00:00Z"))));
    }

    /// <summary>R4: a snapshot normalized under one implicit timezone REFUSES to install into a method configured with another — the persist/recover totalization boundary; the refusing method stays unbuilt rather than serving mismatched instants.</summary>
    [TestMethod]
    public void SnapshotInstallRefusesAForeignTimezoneStamp()
    {
        MapSource source = new((At, [Entry(1, 10, "2020-01-01T02:30:00")]));

        TemporalIntervalAccessMethod utcWriter = PointMethod(TimeSpan.Zero);
        ValueIndexSnapshot snapshot = utcWriter.BuildSnapshot(source);
        byte[] payload = new byte[snapshot.PayloadSize];
        snapshot.WriteTo(payload);

        TemporalIntervalAccessMethod plusTwo = PointMethod(TimeSpan.FromHours(2));
        Assert.IsFalse(plusTwo.TryInstallSnapshot(payload));
        Assert.IsEmpty(Occurrences(plusTwo, ValueProbeRequest.Range(null, false, null, false)));

        TemporalIntervalAccessMethod utcReader = PointMethod(TimeSpan.Zero);
        Assert.IsTrue(utcReader.TryInstallSnapshot(payload));
        Assert.HasCount(1, Occurrences(utcReader, ValueProbeRequest.Range(null, false, null, false)));
    }

    /// <summary>A structurally invalid payload is refused whole: truncation, a foreign version, the wrong axis form, and rows out of sorted order all leave the method unbuilt.</summary>
    [TestMethod]
    public void SnapshotInstallRefusesMalformedPayloads()
    {
        MapSource source = new((At,
        [
            Entry(1, 10, "2020-01-01T00:00:00Z"),
            Entry(2, 20, "2020-01-02T00:00:00Z"),
        ]));
        TemporalIntervalAccessMethod writer = PointMethod();
        ValueIndexSnapshot snapshot = writer.BuildSnapshot(source);
        byte[] payload = new byte[snapshot.PayloadSize];
        snapshot.WriteTo(payload);

        Assert.IsFalse(PointMethod().TryInstallSnapshot([]));
        Assert.IsFalse(PointMethod().TryInstallSnapshot(payload.AsSpan(..^1)));

        byte[] foreignVersion = [.. payload];
        foreignVersion[0] = 9;
        Assert.IsFalse(PointMethod().TryInstallSnapshot(foreignVersion));

        Assert.IsFalse(IntervalMethod().TryInstallSnapshot(payload));

        //The two 24-byte rows sit after the 14-byte header in ascending order; swapping them breaks it.
        byte[] shuffled = [.. payload];
        payload.AsSpan(14, 24).CopyTo(shuffled.AsSpan(38));
        payload.AsSpan(38, 24).CopyTo(shuffled.AsSpan(14));
        Assert.IsFalse(PointMethod().TryInstallSnapshot(shuffled));
    }

    /// <summary>A non-temporal axis datatype is a composition invariant violation, as is an implicit timezone beyond the XSD bound.</summary>
    [TestMethod]
    public void CompositionInvariantsAreEnforced()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new TemporalIntervalAccessMethod(Vocabulary.Xsd.Integer, ValueAxisDeclaration.PointAxis(At), TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.FromHours(15)));
    }

    /// <summary>Drains a probe's occurrence subjects in cursor order.</summary>
    /// <param name="method">The built method.</param>
    /// <param name="request">The probe.</param>
    /// <returns>The hit subjects' encoded ids, in order.</returns>
    private static long[] Occurrences(TemporalIntervalAccessMethod method, ValueProbeRequest request)
    {
        List<long> subjects = [];
        using(ValueProbeCursor cursor = method.OpenProbe(in request))
        {
            while(cursor.TryAdvance(out ValueProbeHit hit))
            {
                subjects.Add(hit.Subject.Encoded);
            }
        }

        return [.. subjects];
    }

    /// <summary>Builds a point-axis method over <c>xsd:dateTime</c>.</summary>
    /// <param name="implicitTimezone">The implicit timezone; UTC unless overridden.</param>
    /// <returns>The method.</returns>
    private static TemporalIntervalAccessMethod PointMethod(TimeSpan? implicitTimezone = null)
    {
        return new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), implicitTimezone ?? TimeSpan.Zero);
    }

    /// <summary>Builds an interval-pair method over <c>xsd:dateTime</c> under UTC.</summary>
    /// <returns>The method.</returns>
    private static TemporalIntervalAccessMethod IntervalMethod()
    {
        return new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.IntervalPair(From, Until), TimeSpan.Zero);
    }

    /// <summary>Builds a segment entry with an <c>xsd:dateTime</c> value.</summary>
    /// <param name="subject">The subject's encoded id.</param>
    /// <param name="valueTerm">The value term's encoded id.</param>
    /// <param name="lexical">The value lexical form.</param>
    /// <returns>The entry.</returns>
    private static ValueSegmentEntry Entry(uint subject, uint valueTerm, string lexical)
    {
        return new ValueSegmentEntry(TermId.FromEncoded(subject), TermId.FromEncoded(valueTerm), DateTime(lexical));
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTime(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>Builds an <c>xsd:dateTimeStamp</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StampLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTimeStamp));
    }

    /// <summary>An in-memory source over per-predicate entry lists.</summary>
    private sealed class MapSource: ValueSegmentSource
    {
        /// <summary>The entries keyed by predicate IRI.</summary>
        private Dictionary<Utf8String, IReadOnlyList<ValueSegmentEntry>> Entries { get; } = [];

        /// <summary>Constructs the source from predicate/entry pairs.</summary>
        /// <param name="predicates">The per-predicate entry lists.</param>
        public MapSource(params (Utf8String Predicate, IReadOnlyList<ValueSegmentEntry> Entries)[] predicates)
        {
            foreach((Utf8String predicate, IReadOnlyList<ValueSegmentEntry> entries) in predicates)
            {
                Entries[predicate] = entries;
            }
        }

        /// <summary>Enumerates one predicate's entries; an undeclared predicate is empty.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>The entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return Entries.TryGetValue(predicateIri, out IReadOnlyList<ValueSegmentEntry>? entries) ? entries : [];
        }
    }
}
