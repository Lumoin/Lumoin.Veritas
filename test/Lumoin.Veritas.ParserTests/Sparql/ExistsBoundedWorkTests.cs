using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Execution.Streaming;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The bounded-work pins for the <c>EXISTS</c> probe machinery, keyed to the streaming observables
/// (<see cref="SolutionCursor.RowsProduced"/> and a pool's active-rental gauge — observables that
/// exist on every source path): the first-row short-circuit over a large graph, the seeded probe's indexed
/// lookup (exactly the matching rows are produced, never the unconstrained scan), and the M-binding Reset
/// probe under <c>PreferFactorizedStar</c> — every intermediate binding's factorised-arena rentals are
/// returned AT <see cref="SolutionCursor.ResetAsync"/> time, not accumulated until the final pipeline
/// disposal. Each arena row threads its OWN <see cref="VeritasMemoryPool{T}"/> into its rendezvous and the
/// probe filters measurements to that instance's tag, so the gauge deltas are unaffected by any concurrent
/// test and the class runs parallel by construction.
/// </summary>
[TestClass]
internal sealed class ExistsBoundedWorkTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The on-mode policy under test.</summary>
    private static SparqlEnginePolicy StreamingOn { get; } = new(PreferStreamingOperators: true);

    /// <summary>A single pull over a large graph produces exactly one row — the first-row short-circuit the per-binding probe leans on (the leaf may drain internally by batch; RowsProduced counts rows HANDED OUT).</summary>
    [TestMethod]
    public async Task FirstPullOverALargeGraphProducesOneRow()
    {
        SparqlQueryEngine engine = await LargeChainEngineAsync(rows: 2000).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator bgp = FindBgp(Translate("SELECT * WHERE { ?x :knows ?y }", pool));

        StreamingPipeline pipeline = StreamingPipeline.TryCompile(engine, engine.Machinery, bgp, TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        try
        {
            Assert.IsTrue(await pipeline.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(1, pipeline.Root.RowsProduced);
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The seeded probe is an indexed lookup: with the pre-binding naming one subject of 2000, the seeded source produces EXACTLY that subject's row — the unconstrained skeleton would produce all 2000.</summary>
    [TestMethod]
    public async Task SeededProbeProducesOnlyTheMatchingRows()
    {
        SparqlQueryEngine engine = await LargeChainEngineAsync(rows: 2000).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator bgp = FindBgp(Translate("SELECT * WHERE { ?x :knows ?y }", pool));

        BgpSeedPlan? seedPlan = BgpSeedPlan.TryBuild(bgp, engine.Machinery);
        Assert.IsNotNull(seedPlan, "a bare single-pattern BGP core must build a seed plan");

        StreamingPipeline pipeline = StreamingPipeline.TryCompileSeededExists(engine.Machinery, seedPlan, TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth))!;
        try
        {
            SparqlSolution preBinding = new([new SparqlBinding(Variable("x"), Iri("s1500"))]);
            await pipeline.Root.ResetAsync(preBinding).ConfigureAwait(false);

            int produced = 0;
            while(await pipeline.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false))
            {
                produced++;
            }

            Assert.AreEqual(1, produced, "the seeded probe must produce exactly the seeded subject's row");
            Assert.AreEqual(1, pipeline.Root.RowsProduced);

            //A seed term absent from the data decides the binding false without opening a source.
            SparqlSolution absent = new([new SparqlBinding(Variable("x"), Iri("nowhere"))]);
            await pipeline.Root.ResetAsync(absent).ConfigureAwait(false);
            Assert.IsFalse(await pipeline.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(0, pipeline.Root.RowsProduced);
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The M-binding Reset probe (the reuse contract's disposal-ownership pin): an EXISTS-shaped probe
    /// pipeline over a genuinely factorising star under <c>PreferFactorizedStar</c>, driven over M bindings
    /// with per-binding abandonment after ONE pull. At every <see cref="SolutionCursor.ResetAsync"/> the
    /// prior binding's live batch source — and with it the factorised arena's pooled rentals — must already
    /// be returned (this test's own arena pool's active-rental gauge back at baseline), and only the LAST
    /// binding's source is torn down by the pipeline's disposal.
    /// </summary>
    [TestMethod]
    public async Task ResetReturnsEachAbandonedBindingsArenaRentals()
    {
        //A 3-pattern star with branch fan-out 2 per predicate: the factorised route builds
        //product-of-unions buffers in the arena, so abandoning mid-drain leaves live rentals to observe.
        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = [];
        for(int subject = 0; subject < 30; subject++)
        {
            for(int branch = 0; branch < 2; branch++)
            {
                AddTriple(encoded, dictionary, $"x{subject}", "a", $"pa{subject}_{branch}");
                AddTriple(encoded, dictionary, $"x{subject}", "b", $"pb{subject}_{branch}");
                AddTriple(encoded, dictionary, $"x{subject}", "c", $"pc{subject}_{branch}");
            }
        }

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEnginePolicy policy = QueryEnginePolicy.Default with { PreferFactorizedStar = true };
        using VeritasMemoryPool<uint> arenaPool = new();
        QueryEngineRendezvous rendezvous = new(store, policy, factorizedArenaPool: arenaPool);
        SparqlDataset dataset = new(store, new Dictionary<TermId, HypertrieGraphStore>(), rendezvous);
        SparqlQueryEngine engine = new(dataset, dictionary, enginePolicy: StreamingOn);

        using Utf8StringPool pool = new();
        AlgebraOperator star = FindBgp(Translate("SELECT * WHERE { ?x :a ?p1 . ?x :b ?p2 . ?x :c ?p3 }", pool));

        using ActiveRentalsProbe rentals = new(arenaPool.InstanceId);
        long baseline = rentals.Snapshot();

        StreamingPipeline pipeline = StreamingPipeline.TryCompile(engine, engine.Machinery, star, TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        try
        {
            bool arenaObserved = false;
            for(int binding = 0; binding < 3; binding++)
            {
                //The pre-binding shares no variable with the star, so the probe re-arms unseeded and the
                //compatibility above would accept the first row — the abandonment-after-one-pull shape.
                SparqlSolution preBinding = new([new SparqlBinding(Variable("m"), Iri($"m{binding}"))]);
                await pipeline.Root.ResetAsync(preBinding).ConfigureAwait(false);
                Assert.AreEqual(baseline, rentals.Snapshot(), $"binding {binding}: the prior binding's source must be disposed AT Reset, not later");

                Assert.IsTrue(await pipeline.Root.MoveNextAsync(TestContext.CancellationToken).ConfigureAwait(false));
                arenaObserved |= rentals.Snapshot() > baseline;
            }

            Assert.IsTrue(arenaObserved, "the factorised-arena route never engaged; the probe is vacuous — re-derive the star eligibility");
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }

        Assert.AreEqual(baseline, rentals.Snapshot(), "the last binding's source must be torn down by the pipeline's disposal");
    }

    /// <summary>
    /// The mid-stream abandonment probe over the REAL arena path (disposal hygiene 4.5a+c): the public
    /// streaming entry drained ONE row of a genuinely factorising star under <c>PreferFactorizedStar</c>,
    /// then abandoned — leaving the enumerator's <c>finally</c> to tear the whole chain down. The shared
    /// pool's active-rental gauge must return to baseline once the abandoned enumerator disposes: the
    /// factorised arena's pooled buffers came back through the iterator-scoped disposal the abandonment
    /// premise leans on.
    /// </summary>
    [TestMethod]
    public async Task AbandoningTheStreamingEntryReturnsTheArenaRentals()
    {
        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = [];
        for(int subject = 0; subject < 30; subject++)
        {
            for(int branch = 0; branch < 2; branch++)
            {
                AddTriple(encoded, dictionary, $"x{subject}", "a", $"pa{subject}_{branch}");
                AddTriple(encoded, dictionary, $"x{subject}", "b", $"pb{subject}_{branch}");
                AddTriple(encoded, dictionary, $"x{subject}", "c", $"pc{subject}_{branch}");
            }
        }

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEnginePolicy policy = QueryEnginePolicy.Default with { PreferFactorizedStar = true };
        using VeritasMemoryPool<uint> arenaPool = new();
        QueryEngineRendezvous rendezvous = new(store, policy, factorizedArenaPool: arenaPool);
        SparqlDataset dataset = new(store, new Dictionary<TermId, HypertrieGraphStore>(), rendezvous);
        SparqlQueryEngine engine = new(dataset, dictionary, enginePolicy: StreamingOn);

        using Utf8StringPool pool = new();
        AlgebraOperator star = Translate("SELECT * WHERE { ?x :a ?p1 . ?x :b ?p2 . ?x :c ?p3 }", pool);

        using ActiveRentalsProbe rentals = new(arenaPool.InstanceId);
        long baseline = rentals.Snapshot();

        bool arenaObserved = false;
        await foreach(SparqlSolution row in engine.EvaluateStreamingAsync(star, TestContext.CancellationToken).ConfigureAwait(false))
        {
            arenaObserved = rentals.Snapshot() > baseline;

            break;
        }

        Assert.IsTrue(arenaObserved, "the factorised-arena route never engaged mid-stream; the probe is vacuous");
        Assert.AreEqual(baseline, rentals.Snapshot(), "abandoning the streaming entry must return the arena's rentals through the chain teardown");
    }

    /// <summary>A cancelled pull at a lazy materialise boundary mid-first-pull still tears the chain down exactly once (disposal hygiene 4.5b): the throw propagates, and disposal is idempotent afterwards.</summary>
    [TestMethod]
    public async Task CancelledBoundaryFirstPullTearsDownExactlyOnce()
    {
        SparqlQueryEngine engine = await LargeChainEngineAsync(rows: 50).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator ordered = Translate("SELECT * WHERE { ?x :knows ?y } ORDER BY ?x", pool);

        StreamingPipeline pipeline = StreamingPipeline.TryCompile(engine, engine.Machinery, ordered, TermId.None, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, AlgebraRewritePipeline.Empty)!;
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(false);

        OperationCanceledException? thrown = null;
        try
        {
            await pipeline.Root.MoveNextAsync(cancelled.Token).ConfigureAwait(false);
        }
        catch(OperationCanceledException ex)
        {
            thrown = ex;
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }

        Assert.IsNotNull(thrown, "the cancelled first pull must propagate the cancellation");
        await pipeline.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Reads ONE pool instance's active-rental gauge on demand: every instrument named <see cref="VeritasMetrics.MemoryPoolActiveRentals"/> is enabled, and only measurements carrying the observed instance's <see cref="VeritasMetrics.MemoryPoolInstanceTag"/> accumulate per snapshot — so the deltas are unaffected by any other pool in the process and the rows run parallel-safe.</summary>
    private sealed class ActiveRentalsProbe : IDisposable
    {
        private readonly MeterListener listener;

        /// <summary>The observed pool instance's identity; measurements with any other tag are ignored.</summary>
        private readonly int instanceId;

        private long sum;

        /// <summary>Starts the listener over the active-rental instruments of one pool instance.</summary>
        /// <param name="instanceId">The observed pool's <see cref="VeritasMemoryPool{T}.InstanceId"/>.</param>
        public ActiveRentalsProbe(int instanceId)
        {
            this.instanceId = instanceId;
            listener = new MeterListener();
            listener.InstrumentPublished = OnInstrumentPublished;
            listener.SetMeasurementEventCallback<int>(OnIntMeasurement);
            listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
            listener.Start();
        }

        /// <summary>Enables measurement events for the active-rental instruments.</summary>
        /// <param name="instrument">The published instrument.</param>
        /// <param name="meterListener">The listener to enable on.</param>
        private void OnInstrumentPublished(Instrument instrument, MeterListener meterListener)
        {
            if(string.Equals(instrument.Name, VeritasMetrics.MemoryPoolActiveRentals, StringComparison.Ordinal))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        }

        /// <summary>Accumulates one observed gauge value when it carries the observed instance's tag.</summary>
        /// <param name="instrument">The reporting instrument.</param>
        /// <param name="measurement">The observed value.</param>
        /// <param name="tags">The measurement tags, carrying the reporting pool's identity.</param>
        /// <param name="state">The enablement state (unused).</param>
        private void OnIntMeasurement(Instrument instrument, int measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            if(CarriesObservedInstance(tags))
            {
                sum += measurement;
            }
        }

        /// <summary>Accumulates one observed gauge value when it carries the observed instance's tag.</summary>
        /// <param name="instrument">The reporting instrument.</param>
        /// <param name="measurement">The observed value.</param>
        /// <param name="tags">The measurement tags, carrying the reporting pool's identity.</param>
        /// <param name="state">The enablement state (unused).</param>
        private void OnLongMeasurement(Instrument instrument, long measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            if(CarriesObservedInstance(tags))
            {
                sum += measurement;
            }
        }

        /// <summary>Whether the measurement's tags name the observed pool instance.</summary>
        /// <param name="tags">The measurement tags.</param>
        /// <returns><see langword="true"/> when the instance tag matches.</returns>
        private bool CarriesObservedInstance(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach(KeyValuePair<string, object?> tag in tags)
            {
                if(string.Equals(tag.Key, VeritasMetrics.MemoryPoolInstanceTag, StringComparison.Ordinal))
                {
                    return tag.Value is int id && id == instanceId;
                }
            }

            return false;
        }

        /// <summary>Reads the observed instance's current active-rental gauge.</summary>
        /// <returns>The gauge value.</returns>
        public long Snapshot()
        {
            sum = 0;
            listener.RecordObservableInstruments();

            return sum;
        }

        /// <summary>Stops the listener.</summary>
        public void Dispose()
        {
            listener.Dispose();
        }
    }

    /// <summary>Encodes one example-namespace triple into the store's build list.</summary>
    /// <param name="encodedToAppendTo">The accumulating encoded triples.</param>
    /// <param name="dictionary">The dictionary the terms intern into.</param>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="object">The object local name.</param>
    private static void AddTriple(List<EncodedTriple> encodedToAppendTo, TermDictionary dictionary, string subject, string predicate, string @object)
    {
        uint s = dictionary.GetOrAdd(Iri(subject)).Encoded;
        uint p = dictionary.GetOrAdd(Iri(predicate)).Encoded;
        uint o = dictionary.GetOrAdd(Iri(@object)).Encoded;
        encodedToAppendTo.Add(EncodedTriple.FromEncoded(s, p, o));
    }

    /// <summary>Builds an on-mode engine over a chain graph of the given size: <c>si :knows oi</c> per row.</summary>
    /// <param name="rows">The row count.</param>
    /// <returns>The engine.</returns>
    private static async Task<SparqlQueryEngine> LargeChainEngineAsync(int rows)
    {
        List<DataTriple> data = new(rows);
        for(int i = 0; i < rows; i++)
        {
            data.Add(new DataTriple(Iri($"s{i}"), Iri("knows"), Iri($"o{i}")));
        }

        return await SparqlQueryEngine.BuildAsync(data, enginePolicy: StreamingOn).ConfigureAwait(false);
    }

    /// <summary>Finds the BGP leaf of a translated SELECT plan.</summary>
    /// <param name="algebra">The translated plan.</param>
    /// <returns>The BGP leaf.</returns>
    private static Bgp FindBgp(AlgebraOperator algebra)
    {
        foreach(AlgebraOperator op in AlgebraWalker.Traverse(algebra))
        {
            if(op is Bgp bgp)
            {
                return bgp;
            }
        }

        throw new InvalidOperationException("The plan carries no BGP leaf.");
    }

    /// <summary>Builds a SPARQL variable from its name.</summary>
    /// <param name="name">The variable name (without the marker).</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Parses, normalizes, and translates a query (the shared example prefix prepended) to its algebra.</summary>
    /// <param name="text">The query text without the prefix.</param>
    /// <param name="pool">The pool the parse interns into.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> " + text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }
}
