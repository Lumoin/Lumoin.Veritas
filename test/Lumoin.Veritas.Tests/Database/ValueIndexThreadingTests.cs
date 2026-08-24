using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Rdf.Indexing;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The end-to-end value-index options-threading pin: the registry composed on
/// <see cref="VeritasEngineOptions.ValueIndexes"/> is the SAME instance observable on the opened
/// database — which reads it off its dataset's rendezvous, so reference equality certifies the whole
/// thread (options → engine → dataset → rendezvous) on the immutable, mutable, and committed paths;
/// and the default composition is the shared empty singleton with unchanged query behavior.
/// </summary>
[TestClass]
internal sealed class ValueIndexThreadingTests
{
    /// <summary>The example-namespace prefix the data shares.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>An immutable open threads the composed registry to the dataset rendezvous, and the default open carries the shared empty singleton.</summary>
    [TestMethod]
    public async Task ImmutableOpenThreadsTheComposedRegistry()
    {
        ValueIndexRegistry registry = ComposedRegistry();
        VeritasEngineOptions options = new() { Reasoning = null, ValueIndexes = registry };

        VeritasEngine composed = await VeritasEngine.OpenAsync(Seed(), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var composedScope = composed.ConfigureAwait(false);
        Assert.AreSame(registry, composed.ValueIndexes);

        VeritasEngine plain = await VeritasEngine.OpenAsync(Seed(), [], new VeritasEngineOptions { Reasoning = null }, TestContext.CancellationToken).ConfigureAwait(false);
        await using var plainScope = plain.ConfigureAwait(false);
        Assert.AreSame(ValueIndexRegistry.Empty, plain.ValueIndexes);
    }

    /// <summary>A mutable open threads the composed registry, the instance survives a committed update (the long-lived rendezvous), and a query with a registered-but-unconsulted method answers unchanged.</summary>
    [TestMethod]
    public async Task MutableOpenThreadsTheComposedRegistryAcrossCommits()
    {
        ValueIndexRegistry registry = ComposedRegistry();
        VeritasEngineOptions options = new() { Reasoning = null, ValueIndexes = registry };

        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(Seed(), options, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);
        Assert.AreSame(registry, engine.ValueIndexes);

        await engine.UpdateAsync(
            Utf8Strings.From($"INSERT DATA {{ <{Ex}b> <{Ex}p> <{Ex}c> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreSame(registry, engine.ValueIndexes);

        VeritasQueryResult result = await engine.QueryAsync(
            Utf8Strings.From($"SELECT ?s WHERE {{ ?s <{Ex}p> ?o }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(result.Bindings);
        Assert.HasCount(2, result.Bindings.Solutions);
    }

    /// <summary>A <c>WITH &lt;g&gt;</c> update's WHERE clause runs over the substituted graph, and the value-index probe DECLINES to that scan rather than answering from the maintained default graph — the wrong-graph-corruption kill: the default graph's matching value must never drive an insert into <c>&lt;g&gt;</c>.</summary>
    [TestMethod]
    public async Task WithGraphUpdateWhereDeclinesToTheSubstitutedGraphScan()
    {
        Utf8String at = Utf8Strings.From(Ex + "at");
        ValueAxisDeclaration axis = ValueAxisDeclaration.PointAxis(at);
        ValueIndexRegistry registry = new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, axis, TimeSpan.Zero),
                axis,
                new EmptySegmentSource(),
                selfTestCases: []))
            .Build();
        VeritasEngineOptions options = new()
        {
            Reasoning = null,
            ValueIndexes = registry,
            SparqlExecution = new SparqlEnginePolicy(PreferValueIndexes: true),
        };

        //The DEFAULT graph carries a value INSIDE the update's window; graph <g> carries only one OUTSIDE
        //it. A probe wrongly served from the default graph would bind ?s and corrupt <g> with the marker.
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync([], options, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        await engine.UpdateAsync(
            Utf8Strings.From($"INSERT DATA {{ <{Ex}d1> <{Ex}at> \"2020-06-01T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime> . GRAPH <{Ex}g> {{ <{Ex}g1> <{Ex}at> \"2019-01-01T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime> }} }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        await engine.UpdateAsync(
            Utf8Strings.From($"WITH <{Ex}g> INSERT {{ ?s <{Ex}hit> <{Ex}yes> }} WHERE {{ ?s <{Ex}at> ?v FILTER(?v >= \"2020-01-01T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        bool corrupted = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ GRAPH <{Ex}g> {{ ?s <{Ex}hit> ?o }} }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(corrupted, "The WITH-substituted WHERE matches nothing in <g>; a marker in <g> means the probe answered from the WRONG graph.");

        //The control: the same WHERE over the DEFAULT graph does match, so the window itself is live.
        bool control = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ ?s <{Ex}at> ?v FILTER(?v >= \"2020-01-01T00:00:00Z\"^^<http://www.w3.org/2001/XMLSchema#dateTime>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(control, "The default graph's value lies inside the window — the decline above was graph-driven, not window-driven.");
    }

    /// <summary>Builds a one-registration registry over a minimal always-accepting method (an empty sample corpus and no cases — the ladder's duplicate and shape rungs still run).</summary>
    /// <returns>The registry.</returns>
    private static ValueIndexRegistry ComposedRegistry()
    {
        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new EmptyAxisMethod(),
                ValueAxisDeclaration.PointAxis(Utf8Strings.From(Ex + "observedAt")),
                new EmptySegmentSource(),
                selfTestCases: []))
            .Build();
    }

    /// <summary>The seed triple set.</summary>
    /// <returns>One triple.</returns>
    private static IReadOnlyList<DataTriple> Seed()
    {
        return [new DataTriple(Iri("a"), Iri("p"), Iri("b"))];
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From(Ex + local));
    }

    /// <summary>An empty registrant-supplied sample corpus.</summary>
    private sealed class EmptySegmentSource: ValueSegmentSource
    {
        /// <summary>Enumerates nothing.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>No entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return [];
        }
    }

    /// <summary>A minimal correct access method over an empty axis: builds trivially and probes empty.</summary>
    private sealed class EmptyAxisMethod: ValueAccessMethod
    {
        /// <summary>The axis datatype: <c>xsd:dateTime</c>.</summary>
        public override Utf8String DatatypeIri => Vocabulary.Xsd.DateTime;

        /// <summary>The mandatory primitive plus the range window.</summary>
        public override ValueIndexShapes DeclaredShapes => ValueIndexShapes.NearestPredecessor | ValueIndexShapes.RangeWindow;

        /// <summary>Builds trivially.</summary>
        /// <param name="source">The entries.</param>
        /// <returns>Built.</returns>
        public override ValueIndexBuildOutcome Build(ValueSegmentSource source)
        {
            return ValueIndexBuildOutcome.Built;
        }

        /// <summary>Opens an empty cursor.</summary>
        /// <param name="request">The probe.</param>
        /// <returns>The empty cursor.</returns>
        public override ValueProbeCursor OpenProbe(in ValueProbeRequest request)
        {
            return new EmptyProbeCursor();
        }
    }

    /// <summary>A cursor with no hits.</summary>
    private sealed class EmptyProbeCursor: ValueProbeCursor
    {
        /// <summary>Never advances.</summary>
        /// <param name="hit">Receives nothing.</param>
        /// <returns><see langword="false"/>.</returns>
        public override bool TryAdvance(out ValueProbeHit hit)
        {
            hit = default;

            return false;
        }
    }
}
