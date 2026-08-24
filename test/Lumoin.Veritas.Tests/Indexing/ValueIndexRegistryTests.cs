using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Indexing;

/// <summary>
/// The value-index seam's Stage B gate: the registry acceptance ladder red/green against a genuine
/// test-double access method (duplicate check, shape sanity, the differential self-test over the
/// registrant-supplied sample corpus), the empty-registry singleton, and the dataset-level carry of one
/// composed registry across both rendezvous.
/// </summary>
[TestClass]
internal sealed class ValueIndexRegistryTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The axis predicate the test double declares.</summary>
    private static Utf8String AxisPredicate => Utf8Strings.From("http://example.org/observedAt");

    /// <summary>A second predicate for conflict cases.</summary>
    private static Utf8String OtherPredicate => Utf8Strings.From("http://example.org/recordedAt");

    /// <summary>A correct registration passes the whole ladder and the frozen registry serves predicate lookup.</summary>
    [TestMethod]
    public void AcceptanceLadderPassesACorrectMethod()
    {
        ValueIndexRegistry registry = new ValueIndexRegistryBuilder().Add(CorrectRegistration()).Build();

        Assert.IsFalse(registry.IsEmpty);
        Assert.HasCount(1, registry.Registrations);
        Assert.IsNotNull(registry.FindByPredicate(AxisPredicate));
        Assert.IsNull(registry.FindByPredicate(OtherPredicate));
    }

    /// <summary>The self-test rung is RED for a method that suppresses a hit: acceptance throws naming the diverging case.</summary>
    [TestMethod]
    public void SelfTestRejectsAHitSuppressingMethod()
    {
        SortedLexicalAccessMethod broken = new(sabotage: MethodSabotage.SuppressFirstHit);
        ValueIndexRegistration registration = RegistrationOver(broken);

        ValueIndexRegistrationException error = Assert.ThrowsExactly<ValueIndexRegistrationException>(
            () => new ValueIndexRegistryBuilder().Add(registration).Build());
        Assert.Contains("self-test", error.Message);
    }

    /// <summary>The self-test rung is RED for a method whose sample build declines.</summary>
    [TestMethod]
    public void SelfTestRejectsADecliningBuild()
    {
        SortedLexicalAccessMethod broken = new(sabotage: MethodSabotage.DeclineBuild);
        ValueIndexRegistration registration = RegistrationOver(broken);

        ValueIndexRegistrationException error = Assert.ThrowsExactly<ValueIndexRegistrationException>(
            () => new ValueIndexRegistryBuilder().Add(registration).Build());
        Assert.Contains("declined", error.Message);
    }

    /// <summary>The shape-sanity rung rejects a method missing the mandatory nearest-predecessor primitive, and one whose overlap declaration is inconsistent with its axis.</summary>
    [TestMethod]
    public void ShapeSanityRejectsInconsistentDeclarations()
    {
        SortedLexicalAccessMethod noPredecessor = new(sabotage: MethodSabotage.OmitMandatoryShape);
        Assert.ThrowsExactly<ValueIndexRegistrationException>(
            () => new ValueIndexRegistryBuilder().Add(RegistrationOver(noPredecessor)).Build());

        //A point axis must not declare interval overlap (and an interval pair must declare it).
        SortedLexicalAccessMethod overlapOnPoint = new(sabotage: MethodSabotage.DeclareOverlap);
        Assert.ThrowsExactly<ValueIndexRegistrationException>(
            () => new ValueIndexRegistryBuilder().Add(RegistrationOver(overlapOnPoint)).Build());
    }

    /// <summary>The duplicate rung rejects a repeated (datatype, axis) pair and a predicate claimed by two registrations.</summary>
    [TestMethod]
    public void DuplicateRungRejectsConflictingRegistrations()
    {
        Assert.ThrowsExactly<ValueIndexRegistrationException>(
            () => new ValueIndexRegistryBuilder().Add(CorrectRegistration()).Add(CorrectRegistration()).Build());

        //A different axis (interval pair) that CLAIMS the point axis's predicate as its start is a conflict too.
        SortedLexicalAccessMethod intervalMethod = new(sabotage: MethodSabotage.None, declaredShapes: ValueIndexShapes.NearestPredecessor | ValueIndexShapes.IntervalOverlap);
        ValueIndexRegistration overlapping = new(
            intervalMethod,
            ValueAxisDeclaration.IntervalPair(AxisPredicate, OtherPredicate),
            EmptySample(),
            selfTestCases: []);
        Assert.ThrowsExactly<ValueIndexRegistrationException>(
            () => new ValueIndexRegistryBuilder().Add(CorrectRegistration()).Add(overlapping).Build());
    }

    /// <summary>An empty builder freezes to the process-wide <see cref="ValueIndexRegistry.Empty"/> singleton — the zero-overhead default composition.</summary>
    [TestMethod]
    public void EmptyBuilderFreezesToTheSharedSingleton()
    {
        Assert.AreSame(ValueIndexRegistry.Empty, new ValueIndexRegistryBuilder().Build());
        Assert.IsTrue(ValueIndexRegistry.Empty.IsEmpty);
    }

    /// <summary>A dataset built with a composed registry carries the SAME instance on both its rendezvous — one registry per dataset, never a divergent pair.</summary>
    [TestMethod]
    public async Task DatasetCarriesOneRegistryAcrossBothRendezvous()
    {
        ValueIndexRegistry registry = new ValueIndexRegistryBuilder().Add(CorrectRegistration()).Build();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlDataset dataset = new(store, new Dictionary<TermId, HypertrieGraphStore>(), computeLane: null, initialColumnarView: null, registry);

        Assert.AreSame(registry, dataset.DefaultGraphRendezvous.ValueIndexes);
        Assert.AreSame(registry, dataset.NamedGraphRendezvous.ValueIndexes);

        //The default composition is the shared empty singleton on both rendezvous, allocation-free.
        SparqlDataset defaultDataset = new(store, new Dictionary<TermId, HypertrieGraphStore>());
        Assert.AreSame(ValueIndexRegistry.Empty, defaultDataset.DefaultGraphRendezvous.ValueIndexes);
        Assert.AreSame(ValueIndexRegistry.Empty, defaultDataset.NamedGraphRendezvous.ValueIndexes);
    }

    /// <summary>The sabotage knobs of the test double: each breaks exactly one acceptance rung.</summary>
    private enum MethodSabotage
    {
        /// <summary>A correct method.</summary>
        None,

        /// <summary>The build declines instead of building.</summary>
        DeclineBuild,

        /// <summary>The first probe hit is suppressed, so an expected hit goes missing.</summary>
        SuppressFirstHit,

        /// <summary>The mandatory nearest-predecessor shape is not declared.</summary>
        OmitMandatoryShape,

        /// <summary>Interval overlap is declared on a point axis.</summary>
        DeclareOverlap,
    }

    /// <summary>Builds the well-formed registration: the correct double, the point axis, the three-entry sample corpus, and two derivable self-test cases.</summary>
    /// <returns>The registration.</returns>
    private static ValueIndexRegistration CorrectRegistration()
    {
        return RegistrationOver(new SortedLexicalAccessMethod(sabotage: MethodSabotage.None));
    }

    /// <summary>Builds a registration over <paramref name="method"/> with the standard sample corpus and self-test cases.</summary>
    /// <param name="method">The access method under acceptance.</param>
    /// <returns>The registration.</returns>
    private static ValueIndexRegistration RegistrationOver(SortedLexicalAccessMethod method)
    {
        //Three chronologically ordered instants; same-timezone ISO lexicals sort chronologically as bytes.
        SampleSegmentSource sample = new(AxisPredicate,
        [
            new ValueSegmentEntry(TermId.FromEncoded(11), TermId.FromEncoded(21), DateTimeLiteral("2020-01-01T00:00:00Z")),
            new ValueSegmentEntry(TermId.FromEncoded(12), TermId.FromEncoded(22), DateTimeLiteral("2020-01-02T00:00:00Z")),
            new ValueSegmentEntry(TermId.FromEncoded(13), TermId.FromEncoded(23), DateTimeLiteral("2020-01-03T00:00:00Z")),
        ]);

        //The expected hit sets are stated against the sample — ground truth derived OUTSIDE the method under test.
        List<ValueIndexSelfTestCase> cases =
        [
            new ValueIndexSelfTestCase(
                ValueProbeRequest.Range(lowerBound: null, lowerInclusive: false, upperBound: null, upperInclusive: false),
                [new ValueProbeHit(TermId.FromEncoded(11), TermId.FromEncoded(21), TermId.None), new ValueProbeHit(TermId.FromEncoded(12), TermId.FromEncoded(22), TermId.None), new ValueProbeHit(TermId.FromEncoded(13), TermId.FromEncoded(23), TermId.None)]),
            new ValueIndexSelfTestCase(
                ValueProbeRequest.Range(DateTimeLiteral("2020-01-02T00:00:00Z"), lowerInclusive: true, upperBound: null, upperInclusive: false),
                [new ValueProbeHit(TermId.FromEncoded(12), TermId.FromEncoded(22), TermId.None), new ValueProbeHit(TermId.FromEncoded(13), TermId.FromEncoded(23), TermId.None)]),
        ];

        return new ValueIndexRegistration(method, ValueAxisDeclaration.PointAxis(AxisPredicate), sample, cases);
    }

    /// <summary>An empty sample corpus for cases that never reach the self-test rung.</summary>
    /// <returns>The source.</returns>
    private static SampleSegmentSource EmptySample()
    {
        return new SampleSegmentSource(AxisPredicate, []);
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTimeLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>An in-memory registrant-supplied sample corpus: one declared predicate's entries.</summary>
    /// <param name="predicateIri">The declared predicate.</param>
    /// <param name="entries">The entries.</param>
    private sealed class SampleSegmentSource(Utf8String predicateIri, IReadOnlyList<ValueSegmentEntry> entries): ValueSegmentSource
    {
        /// <summary>The declared predicate.</summary>
        private Utf8String PredicateIri { get; } = predicateIri;

        /// <summary>The entries.</summary>
        private IReadOnlyList<ValueSegmentEntry> Entries { get; } = entries;

        /// <summary>Enumerates the declared predicate's entries; any other predicate is empty.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>The entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return predicateIri.Equals(PredicateIri) ? Entries : [];
        }
    }

    /// <summary>
    /// The Stage B test double: a point-axis access method ordering entries by lexical bytes (chronological for
    /// same-timezone ISO lexicals), with sabotage knobs that each break exactly one acceptance rung so the
    /// ladder's red arms are certified against genuine failures rather than stubs.
    /// </summary>
    private sealed class SortedLexicalAccessMethod: ValueAccessMethod
    {
        /// <summary>The active sabotage knob.</summary>
        private MethodSabotage Sabotage { get; }

        /// <summary>The declared shapes.</summary>
        private ValueIndexShapes Shapes { get; }

        /// <summary>The built entries in axis order, or <see langword="null"/> before a successful build.</summary>
        private List<ValueSegmentEntry>? Built { get; set; }

        /// <summary>Constructs the double.</summary>
        /// <param name="sabotage">The rung to break, or <see cref="MethodSabotage.None"/>.</param>
        /// <param name="declaredShapes">An explicit shape declaration, or <see langword="null"/> to derive one from <paramref name="sabotage"/>.</param>
        public SortedLexicalAccessMethod(MethodSabotage sabotage, ValueIndexShapes? declaredShapes = null)
        {
            Sabotage = sabotage;
            Shapes = declaredShapes ?? sabotage switch
            {
                MethodSabotage.OmitMandatoryShape => ValueIndexShapes.RangeWindow | ValueIndexShapes.AsOfPoint,
                MethodSabotage.DeclareOverlap => ValueIndexShapes.NearestPredecessor | ValueIndexShapes.RangeWindow | ValueIndexShapes.IntervalOverlap,
                _ => ValueIndexShapes.NearestPredecessor | ValueIndexShapes.RangeWindow | ValueIndexShapes.AsOfPoint | ValueIndexShapes.LastPerSeries,
            };
        }

        /// <summary>The axis datatype: <c>xsd:dateTime</c>.</summary>
        public override Utf8String DatatypeIri => Vocabulary.Xsd.DateTime;

        /// <summary>The declared shapes, honouring the sabotage knob.</summary>
        public override ValueIndexShapes DeclaredShapes => Shapes;

        /// <summary>Builds by sorting the declared predicate's entries on lexical byte order.</summary>
        /// <param name="source">The entries.</param>
        /// <returns>Built, or Declined under the decline sabotage.</returns>
        public override ValueIndexBuildOutcome Build(ValueSegmentSource source)
        {
            if(Sabotage == MethodSabotage.DeclineBuild)
            {
                return ValueIndexBuildOutcome.Declined;
            }

            List<ValueSegmentEntry> entries = [.. source.EnumerateDeclared(AxisPredicate)];
            entries.Sort(static (left, right) => left.Value.Value.CompareTo(right.Value.Value));
            Built = entries;

            return ValueIndexBuildOutcome.Built;
        }

        /// <summary>Opens a probe over the built entries, filtering by lexical bounds; the suppress sabotage drops the first matching hit.</summary>
        /// <param name="request">The probe.</param>
        /// <returns>The hit cursor.</returns>
        public override ValueProbeCursor OpenProbe(in ValueProbeRequest request)
        {
            List<ValueProbeHit> hits = [];
            if(Built is { } built)
            {
                foreach(ValueSegmentEntry entry in built)
                {
                    if(WithinBounds(entry.Value, request))
                    {
                        hits.Add(new ValueProbeHit(entry.Subject, entry.ValueTerm, TermId.None));
                    }
                }
            }

            if(Sabotage == MethodSabotage.SuppressFirstHit && hits.Count > 0)
            {
                hits.RemoveAt(0);
            }

            return new ListProbeCursor(hits);
        }

        /// <summary>Whether a value literal lies within the request's lexical bounds.</summary>
        /// <param name="value">The entry's value literal.</param>
        /// <param name="request">The probe.</param>
        /// <returns><see langword="true"/> when in bounds.</returns>
        private static bool WithinBounds(Literal value, in ValueProbeRequest request)
        {
            if(request.LowerBound is { } lower)
            {
                int comparison = value.Value.CompareTo(lower.Value);
                if(comparison < 0 || (comparison == 0 && !request.LowerInclusive))
                {
                    return false;
                }
            }

            if(request.UpperBound is { } upper)
            {
                int comparison = value.Value.CompareTo(upper.Value);
                if(comparison > 0 || (comparison == 0 && !request.UpperInclusive))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>A cursor over a materialized hit list.</summary>
    /// <param name="hits">The hits, in axis order.</param>
    private sealed class ListProbeCursor(List<ValueProbeHit> hits): ValueProbeCursor
    {
        /// <summary>The hits, in axis order.</summary>
        private List<ValueProbeHit> Hits { get; } = hits;

        /// <summary>The next hit's index.</summary>
        private int Position { get; set; }

        /// <summary>Advances to the next hit.</summary>
        /// <param name="hit">Receives the next hit.</param>
        /// <returns><see langword="true"/> while hits remain.</returns>
        public override bool TryAdvance(out ValueProbeHit hit)
        {
            if(Position >= Hits.Count)
            {
                hit = default;

                return false;
            }

            hit = Hits[Position];
            Position++;

            return true;
        }
    }
}
