using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Rdf.Indexing;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The value-index sidecar's persist/recover pins: the store drops an image whose dataset-state stamp
/// differs from the generation's provenance epoch (the staleness gate) and serves a bound one; a
/// reopened database warm-installs a matching sidecar so the first probe pays no rebuild; and a
/// sidecar persisted under one implicit timezone REFUSES to install into a registry configured with
/// another — the reopened database rebuilds cold and answers per its OWN timezone, never the stamp's.
/// </summary>
[TestClass]
internal sealed class ValueIndexSidecarRecoveryTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The point-axis predicate the registrations declare.</summary>
    private static Utf8String At => Utf8Strings.From($"{Ex}at");

    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>H4: an image stamped with a foreign dataset state is dropped at load while the rest of the generation serves; an image stamped with the generation's own state is served whole.</summary>
    [TestMethod]
    public void StaleImageIsDroppedAtLoadAndABoundOneServes()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-vidx-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore records = new(persistence, pool);

            TermDictionary dictionary = new(0xD15C0DE);
            TermId subject = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}s")));
            TermId predicate = dictionary.GetOrAdd((RdfTerm)new NamedNode(At));
            TermId @object = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}o")));
            EncodedTriple[] triples = [new(subject, predicate, @object)];
            ValueIndexImageEntry entry = new(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#dateTime"), At, null, new byte[] { 1, 2, 3 });

            //Generation 0: the image's stamp names a dataset state the generation does not — dropped at load.
            records.Persist(dictionary, triples, null, null, new ValueIndexImage(0xDEAD, [entry]), provenanceEpoch: 42);
            DurableSystemOfRecordLoad stale = records.TryLoad(termPool, triplePool);
            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, stale.Outcome);
            Assert.IsNull(stale.ValueIndexes, "An image stamped with a foreign dataset state must be dropped at load.");
            stale.Triples!.Dispose();

            //Generation 1: the stamp equals the provenance epoch — the image is served whole.
            records.Persist(dictionary, triples, null, null, new ValueIndexImage(42UL, [entry]), provenanceEpoch: 42);
            DurableSystemOfRecordLoad bound = records.TryLoad(termPool, triplePool);
            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, bound.Outcome);
            Assert.IsNotNull(bound.ValueIndexes, "An image stamped with the generation's own dataset state is served.");
            Assert.AreEqual(42UL, bound.ValueIndexes!.StateId);
            Assert.HasCount(1, bound.ValueIndexes.Entries);
            bound.Triples!.Dispose();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>H4: a BOUND image (stamp equal to the generation's provenance epoch) whose artifact bytes were tampered on disk fails the manifest's length+digest verification and is dropped at load — the integrity gate is the digest, not the staleness stamp alone. The flipped byte lies in the method payload block, so the tamper is invisible to the structural parse and to the stamp.</summary>
    [TestMethod]
    public void TamperedImageIsDroppedByTheDigestCheck()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-vidx-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore records = new(persistence, pool);

            TermDictionary dictionary = new(0xD15C0DE);
            TermId subject = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}s")));
            TermId predicate = dictionary.GetOrAdd((RdfTerm)new NamedNode(At));
            TermId @object = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"{Ex}o")));
            EncodedTriple[] triples = [new(subject, predicate, @object)];
            ValueIndexImageEntry entry = new(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#dateTime"), At, null, new byte[] { 1, 2, 3 });

            records.Persist(dictionary, triples, null, null, new ValueIndexImage(42UL, [entry]), provenanceEpoch: 42);

            //The artifact's LAST byte is the payload block's tail: flipping it leaves the structural
            //parse and the state stamp satisfied, so only the manifest's digest can catch the tamper.
            string[] artifacts = Directory.GetFiles(directory, "vidx-*.vidx");
            Assert.HasCount(1, artifacts, "One value-index artifact is staged per generation.");
            byte[] bytes = File.ReadAllBytes(artifacts[0]);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(artifacts[0], bytes);

            DurableSystemOfRecordLoad tampered = records.TryLoad(termPool, triplePool);
            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, tampered.Outcome, "The tamper hits only the re-derivable sidecar; the generation itself serves.");
            Assert.IsNull(tampered.ValueIndexes, "A tampered artifact must fail the manifest's digest check and be dropped at load.");
            tampered.Triples!.Dispose();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A reopened database warm-installs a matching sidecar: exactly one install lands at open, and the first index-routed probe pays no rebuild.</summary>
    [TestMethod]
    public async Task ReopenedDatabaseWarmInstallsTheSidecar()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-vidx-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            {
                CountingTemporalMethod writer = new(new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.Zero));
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], new VeritasEngineOptions { ValueIndexes = RegistryOver(writer) }, TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}s1> <{Ex}at> \"2020-01-01T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime> . <{Ex}s2> <{Ex}at> \"2020-01-05T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                mutable.Persist(store);
            }

            CountingTemporalMethod reader = new(new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.Zero));
            VeritasEngineOptions options = new()
            {
                ValueIndexes = RegistryOver(reader),
                SparqlExecution = new SparqlEnginePolicy(PreferValueIndexes: true),
            };
            int buildsAfterAcceptance = reader.BuildCalls;

            VeritasEngine reopened = await VeritasEngine
                .OpenAsync(store, options, TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var scope = reopened.ConfigureAwait(false);

            Assert.AreEqual(1, reader.InstallCalls, "The reopened database installs the sidecar snapshot exactly once at open.");
            Assert.AreEqual(buildsAfterAcceptance, reader.BuildCalls, "The warm install replaces the rebuild — no build lands at open.");

            VeritasQueryResult answered = await reopened
                .QueryAsync(Utf8Strings.From($"SELECT ?s ?v WHERE {{ ?s <{Ex}at> ?v FILTER(?v > \"2019-12-31T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime>) }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.HasCount(2, answered.Bindings!.Solutions, "The warm index answers the probe-shaped filter.");
            Assert.AreEqual(buildsAfterAcceptance, reader.BuildCalls, "The first probe serves warm — it pays no rebuild.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>R4: a sidecar persisted under UTC refuses to install into a +02:00 registry — the reopened database rebuilds cold at the first probe and answers per ITS OWN timezone (the naive value sorts before the aware bound), never the stamp's.</summary>
    [TestMethod]
    public async Task TimezoneMismatchedSidecarIsRefusedAndRebuiltUnderTheEngineTimezone()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-vidx-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            {
                CountingTemporalMethod writer = new(new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.Zero));
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync([], new VeritasEngineOptions { ValueIndexes = RegistryOver(writer) }, TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var mutableScope = mutable.ConfigureAwait(false);

                //One NAIVE value: under UTC it is 02:30Z (after the 01:00Z bound); under +02:00 it is 00:30Z (before it).
                await mutable
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}s1> <{Ex}at> \"2020-01-01T02:30:00\"^^<http://www.w3.org/2001/XMLSchema#dateTime> }}"), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                mutable.Persist(store);
            }

            Utf8String probe = Utf8Strings.From($"SELECT ?s WHERE {{ ?s <{Ex}at> ?v FILTER(?v > \"2020-01-01T01:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime>) }}");

            {
                CountingTemporalMethod utcReader = new(new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.Zero));
                VeritasEngine reopened = await VeritasEngine
                    .OpenAsync(store, new VeritasEngineOptions { ValueIndexes = RegistryOver(utcReader), SparqlExecution = new SparqlEnginePolicy(PreferValueIndexes: true) }, TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var scope = reopened.ConfigureAwait(false);

                Assert.AreEqual(1, utcReader.InstallCalls, "The matching-timezone sidecar installs.");
                VeritasQueryResult underUtc = await reopened.QueryAsync(probe, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                Assert.HasCount(1, underUtc.Bindings!.Solutions, "Under UTC the naive 02:30 lies after the 01:00Z bound.");
            }

            {
                CountingTemporalMethod plusTwoReader = new(new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, ValueAxisDeclaration.PointAxis(At), TimeSpan.FromHours(2)));

                //ONE timezone drives both the engine's expression context and the registered method — the
                //H1 composition guard refuses anything else, so probe and scan agree under +02:00.
                VeritasEngineOptions plusTwoOptions = new()
                {
                    ValueIndexes = RegistryOver(plusTwoReader),
                    SparqlExecution = new SparqlEnginePolicy(PreferValueIndexes: true),
                    ImplicitTimezone = TimeSpan.FromHours(2),
                };

                //Captured AFTER the acceptance ladder's self-test build, so the deltas below count open-time work only.
                int buildsAfterAcceptance = plusTwoReader.BuildCalls;
                VeritasEngine reopened = await VeritasEngine
                    .OpenAsync(store, plusTwoOptions, TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var scope = reopened.ConfigureAwait(false);

                Assert.AreEqual(1, plusTwoReader.InstallCalls, "The install is attempted and the timezone stamp refuses it.");
                Assert.AreEqual(buildsAfterAcceptance, plusTwoReader.BuildCalls, "The refusal leaves the method unbuilt at open.");

                VeritasQueryResult underPlusTwo = await reopened.QueryAsync(probe, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                Assert.IsEmpty(underPlusTwo.Bindings!.Solutions, "Under the +02:00 engine the naive 02:30 normalizes to 00:30Z, before the bound — the UTC-stamped sidecar was not served, and probe and scan agree because ONE timezone drives both.");
                Assert.AreEqual(buildsAfterAcceptance + 1, plusTwoReader.BuildCalls, "The first probe paid the cold rebuild under the engine's own timezone.");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Builds a one-registration registry over the counting method, with aware-valued acceptance material valid under any implicit timezone.</summary>
    /// <param name="method">The counting method.</param>
    /// <returns>The frozen registry.</returns>
    private static ValueIndexRegistry RegistryOver(CountingTemporalMethod method)
    {
        SampleSource sample = new(At,
        [
            SampleEntry(1, 10, "2020-01-01T00:00:00Z"),
            SampleEntry(2, 20, "2020-01-02T00:00:00Z"),
        ]);
        List<ValueIndexSelfTestCase> cases =
        [
            new ValueIndexSelfTestCase(
                ValueProbeRequest.Range(DateTimeLiteral("2020-01-02T00:00:00Z"), true, null, false),
                [new ValueProbeHit(TermId.FromEncoded(2), TermId.FromEncoded(20), TermId.None)]),
            new ValueIndexSelfTestCase(
                ValueProbeRequest.AtInstant(DateTimeLiteral("2020-01-01T12:00:00Z")),
                [new ValueProbeHit(TermId.FromEncoded(1), TermId.FromEncoded(10), TermId.None)]),
        ];

        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(method, ValueAxisDeclaration.PointAxis(At), sample, cases))
            .Build();
    }

    /// <summary>Builds a sample-corpus entry with an <c>xsd:dateTime</c> value.</summary>
    /// <param name="subject">The subject's encoded id.</param>
    /// <param name="valueTerm">The value term's encoded id.</param>
    /// <param name="lexical">The value lexical form.</param>
    /// <returns>The entry.</returns>
    private static ValueSegmentEntry SampleEntry(uint subject, uint valueTerm, string lexical)
    {
        return new ValueSegmentEntry(TermId.FromEncoded(subject), TermId.FromEncoded(valueTerm), DateTimeLiteral(lexical));
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTimeLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>A delegating temporal access method counting builds and snapshot installs — the warm-vs-cold witness the recovery pins read.</summary>
    private sealed class CountingTemporalMethod: ValueAccessMethod
    {
        /// <summary>The real temporal method every call delegates to.</summary>
        private TemporalIntervalAccessMethod Inner { get; }

        /// <summary>Constructs the double over a real method.</summary>
        /// <param name="inner">The real temporal method.</param>
        public CountingTemporalMethod(TemporalIntervalAccessMethod inner)
        {
            Inner = inner;
        }

        /// <summary>The number of <see cref="Build"/> calls observed.</summary>
        public int BuildCalls { get; private set; }

        /// <summary>The number of <see cref="TryInstallSnapshot"/> calls observed.</summary>
        public int InstallCalls { get; private set; }

        /// <summary>The inner method's axis datatype IRI.</summary>
        public override Utf8String DatatypeIri => Inner.DatatypeIri;

        /// <summary>The inner method's declared shapes.</summary>
        public override ValueIndexShapes DeclaredShapes => Inner.DeclaredShapes;

        /// <summary>The inner method's implicit-timezone declaration, forwarded so the engine's composition guard sees it through the counting wrapper.</summary>
        public override TimeSpan? DeclaredImplicitTimezone => Inner.DeclaredImplicitTimezone;

        /// <summary>Counts the call and delegates the build.</summary>
        /// <param name="source">The declared predicates' entries.</param>
        /// <returns>The inner build outcome.</returns>
        public override ValueIndexBuildOutcome Build(ValueSegmentSource source)
        {
            BuildCalls++;

            return Inner.Build(source);
        }

        /// <summary>Delegates the probe.</summary>
        /// <param name="request">The probe.</param>
        /// <returns>The inner cursor.</returns>
        public override ValueProbeCursor OpenProbe(in ValueProbeRequest request)
        {
            return Inner.OpenProbe(in request);
        }

        /// <summary>Delegates the snapshot build.</summary>
        /// <param name="source">The declared predicates' entries.</param>
        /// <returns>The inner snapshot.</returns>
        public override ValueIndexSnapshot? BuildSnapshot(ValueSegmentSource source)
        {
            return Inner.BuildSnapshot(source);
        }

        /// <summary>Counts the call and delegates the install (the inner method validates the payload's stamps).</summary>
        /// <param name="payload">The persisted snapshot payload.</param>
        /// <returns>The inner verdict.</returns>
        public override bool TryInstallSnapshot(ReadOnlySpan<byte> payload)
        {
            InstallCalls++;

            return Inner.TryInstallSnapshot(payload);
        }
    }

    /// <summary>An in-memory sample corpus over one predicate's entries.</summary>
    private sealed class SampleSource: ValueSegmentSource
    {
        /// <summary>The declared predicate.</summary>
        private Utf8String Predicate { get; }

        /// <summary>The predicate's entries.</summary>
        private IReadOnlyList<ValueSegmentEntry> Entries { get; }

        /// <summary>Constructs the corpus.</summary>
        /// <param name="predicate">The declared predicate.</param>
        /// <param name="entries">The predicate's entries.</param>
        public SampleSource(Utf8String predicate, IReadOnlyList<ValueSegmentEntry> entries)
        {
            Predicate = predicate;
            Entries = entries;
        }

        /// <summary>Enumerates the declared predicate's entries; any other predicate is empty.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>The entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return predicateIri.Equals(Predicate) ? Entries : [];
        }
    }
}
