using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Rdf.Indexing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Execution.Interception;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The value-index pushdown pins: with <see cref="SparqlEnginePolicy.PreferValueIndexes"/> ON and a
/// registered temporal axis, the probe-answered route yields EXACTLY the scan's solutions on every
/// recognized shape (the answer-neutrality invariant), and every decline arm — cross-family constant
/// (R8), equality operators, undeclared predicates or shapes, the flag off — falls through to the
/// unchanged scan.
/// </summary>
[TestClass]
internal sealed class ValueIndexPushdownTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The XSD namespace.</summary>
    private const string XsdNamespace = "http://www.w3.org/2001/XMLSchema#";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A recognized point-axis window probe answers identically to the scan, in both constant orientations.</summary>
    [TestMethod]
    public async Task PointAxisProbeMatchesTheScan()
    {
        const string VariableLeft = "SELECT ?s ?v WHERE { ?s <http://example.org/at> ?v FILTER(?v >= \"2020-01-02T00:00:00Z\"^^xsd:dateTime) }";
        const string ConstantLeft = "SELECT ?s ?v WHERE { ?s <http://example.org/at> ?v FILTER(\"2020-01-02T00:00:00Z\"^^xsd:dateTime <= ?v) }";

        foreach(string query in new[] { VariableLeft, ConstantLeft })
        {
            (string probed, string scanned) = await BothRoutesAsync(query).ConfigureAwait(false);
            Assert.AreEqual(scanned, probed, $"Probe/scan divergence on: {query}");
            Assert.Contains("2020-01-02T10:00:00Z", probed);
            Assert.DoesNotContain("2020-01-01T10:00:00Z", probed);

            //The foreign-typed row: an xsd:string carrying a parseable timestamp — the scan errors it out
            //of the temporal comparison, so the index must not have indexed it.
            Assert.DoesNotContain(Ex + "s7", probed, "A parseable-but-foreign-typed literal must not answer from the probe.");
        }
    }

    /// <summary>H1's composition guard: a registry whose method normalizes under one implicit timezone REFUSES to compose with an engine whose expression context captures another — loud at construction, never a silent probe/scan divergence.</summary>
    [TestMethod]
    public async Task TimezoneMismatchedCompositionIsRefused()
    {
        List<DataTriple> data = [new DataTriple(Iri("s1"), Iri("at"), DateTimeLiteral("2020-01-01T10:00:00Z"))];

        await Assert.ThrowsExactlyAsync<Lumoin.Veritas.Core.Indexing.ValueIndexRegistrationException>(async () =>
            _ = await SparqlQueryEngine.BuildAsync(
                data,
                expressionContext: SparqlExpressionContext.CreateDefault(),
                enginePolicy: new SparqlEnginePolicy(PreferValueIndexes: true),
                valueIndexes: ComposedRegistry(TimeSpan.FromHours(2)),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>The declared interval-pair two-pattern shape with the overlap conjunction answers identically to the scan, in both conjunct orders.</summary>
    [TestMethod]
    public async Task IntervalPairProbeMatchesTheScan()
    {
        const string StartFirst = "SELECT ?o ?s ?e WHERE { ?o <http://example.org/from> ?s . ?o <http://example.org/until> ?e FILTER(?s <= \"2020-01-15T00:00:00Z\"^^xsd:dateTime && ?e >= \"2020-01-10T00:00:00Z\"^^xsd:dateTime) }";
        const string EndFirst = "SELECT ?o ?s ?e WHERE { ?o <http://example.org/from> ?s . ?o <http://example.org/until> ?e FILTER(?e >= \"2020-01-10T00:00:00Z\"^^xsd:dateTime && ?s <= \"2020-01-15T00:00:00Z\"^^xsd:dateTime) }";

        foreach(string query in new[] { StartFirst, EndFirst })
        {
            (string probed, string scanned) = await BothRoutesAsync(query).ConfigureAwait(false);
            Assert.AreEqual(scanned, probed, $"Probe/scan divergence on: {query}");
            Assert.Contains(Ex + "o1", probed);
            Assert.DoesNotContain(Ex + "o2", probed);
        }
    }

    /// <summary>The decline arms: a cross-family constant (R8 — the scan errors every row, so both routes are empty), an equality operator, and an undeclared two-pattern shape all answer identically to the scan.</summary>
    [TestMethod]
    public async Task DeclineArmsFallThroughToTheScan()
    {
        //R8: a date constant against the dateTime axis — the scan errors every row out; the probe must not
        //answer what the scan refuses.
        const string CrossFamily = "SELECT ?s WHERE { ?s <http://example.org/at> ?v FILTER(?v >= \"2020-01-02\"^^xsd:date) }";

        //Equality keeps record semantics and stays outside the ordering-scoped axis.
        const string Equality = "SELECT ?s WHERE { ?s <http://example.org/at> ?v FILTER(?v = \"2020-01-02T10:00:00Z\"^^xsd:dateTime) }";

        //Two patterns whose predicates are NOT the declared pair: the recognizer declines cleanly.
        const string UndeclaredPair = "SELECT ?o WHERE { ?o <http://example.org/at> ?s . ?o <http://example.org/until> ?e FILTER(?s <= \"2020-01-15T00:00:00Z\"^^xsd:dateTime && ?e >= \"2020-01-10T00:00:00Z\"^^xsd:dateTime) }";

        foreach(string query in new[] { CrossFamily, Equality, UndeclaredPair })
        {
            (string probed, string scanned) = await BothRoutesAsync(query).ConfigureAwait(false);
            Assert.AreEqual(scanned, probed, $"Probe/scan divergence on: {query}");
        }
    }

    /// <summary>C17 leap-second row: XSD 1.1 caps the second field below 60, so a <c>:60</c> lexical form is INVALID — the build drops it (R7), the scan errors it out of the comparison, and probe==scan holds with the value absent from both.</summary>
    [TestMethod]
    public async Task LeapSecondLexicalFormIsDroppedByBothRoutes()
    {
        //A window every valid 2016 instant satisfies: if either route parsed the :60 form, s6 would appear.
        const string Window = "SELECT ?s WHERE { ?s <http://example.org/at> ?v FILTER(?v >= \"2016-01-01T00:00:00Z\"^^xsd:dateTime) }";

        (string probed, string scanned) = await BothRoutesAsync(Window).ConfigureAwait(false);
        Assert.AreEqual(scanned, probed, "Probe/scan divergence on the leap-second window.");
        Assert.DoesNotContain(Ex + "s6", probed, "The XSD-invalid :60 lexical form must be dropped, not indexed.");
        Assert.Contains(Ex + "s2", probed, "The valid instants still answer.");
    }

    /// <summary>H1 live: the engine's implicit timezone and the registered method normalize a NAIVE value through the same routine end to end — the +02:00 pair flips the UTC pair's verdict, and probe==scan holds under BOTH configurations.</summary>
    [TestMethod]
    public async Task ImplicitTimezoneFlipsTheLiveVerdictWithProbeScanIdentity()
    {
        //The naive 02:30 is after 01:00Z under UTC and normalizes to 00:30Z (before it) under +02:00.
        const string Probe = "SELECT ?s WHERE { ?s <http://example.org/at> ?v FILTER(?v > \"2020-01-01T01:00:00Z\"^^xsd:dateTime) }";
        List<DataTriple> naive = [new DataTriple(Iri("n1"), Iri("at"), DateTimeLiteral("2020-01-01T02:30:00"))];

        (string probedUtc, string scannedUtc) = await BothRoutesAsync(Probe, TimeSpan.Zero, naive).ConfigureAwait(false);
        Assert.AreEqual(scannedUtc, probedUtc, "Probe/scan divergence under UTC.");
        Assert.Contains(Ex + "n1", probedUtc, "Under UTC the naive 02:30 lies after the 01:00Z bound.");

        (string probedPlusTwo, string scannedPlusTwo) = await BothRoutesAsync(Probe, TimeSpan.FromHours(2), naive).ConfigureAwait(false);
        Assert.AreEqual(scannedPlusTwo, probedPlusTwo, "Probe/scan divergence under +02:00.");
        Assert.DoesNotContain(Ex + "n1", probedPlusTwo, "Under +02:00 the naive 02:30 normalizes to 00:30Z, before the bound.");
    }

    /// <summary>R10: the interval recognizer requires the declared pair's two patterns in ONE Bgp under the Filter — an OPTIONAL separating them declines cleanly (the direct method returns null; both routes agree).</summary>
    [TestMethod]
    public async Task SplitBgpIntervalShapeDeclines()
    {
        const string Split = "SELECT ?o ?s ?e WHERE { ?o <http://example.org/from> ?s . OPTIONAL { ?o <http://example.org/note> ?n } ?o <http://example.org/until> ?e FILTER(?s <= \"2020-01-15T00:00:00Z\"^^xsd:dateTime && ?e >= \"2020-01-10T00:00:00Z\"^^xsd:dateTime) }";

        (string probed, string scanned) = await BothRoutesAsync(Split).ConfigureAwait(false);
        Assert.AreEqual(scanned, probed, "Probe/scan divergence on the split-Bgp shape.");
        Assert.Contains(Ex + "o1", probed, "The overlap answer itself is unchanged.");

        //The direct witness: the filter's child is not one bare Bgp, so the recognizer must decline.
        SparqlQueryEngine engine = await BuildEngineAsync(preferValueIndexes: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        Filter? filter = FindFilter(Translate($"PREFIX xsd: <{XsdNamespace}> {Split}", pool));
        Assert.IsNotNull(filter);
        Assert.IsNull(engine.TryEvaluateValueIndexProbe(filter, Lumoin.Veritas.Core.Encoding.TermId.None));
    }

    /// <summary>The recognizer's non-default-graph gate: the engine method declines outright for a named graph.</summary>
    [TestMethod]
    public async Task NamedGraphProbeDeclines()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(preferValueIndexes: true).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"PREFIX xsd: <{XsdNamespace}> SELECT ?s ?v WHERE {{ ?s <http://example.org/at> ?v FILTER(?v >= \"2020-01-02T00:00:00Z\"^^xsd:dateTime) }}", pool);

        //The translated root is Project over Filter(Bgp); dig the Filter out and consult the engine method
        //directly with a non-default graph id.
        Filter? filter = FindFilter(algebra);
        Assert.IsNotNull(filter);
        Assert.IsNull(engine.TryEvaluateValueIndexProbe(filter, Lumoin.Veritas.Core.Encoding.TermId.FromEncoded(42)));
        Assert.IsNotNull(engine.TryEvaluateValueIndexProbe(filter, Lumoin.Veritas.Core.Encoding.TermId.None));
    }

    /// <summary>The interception-level firing witness: with the flag ON the value-index-probe entry announces itself in the execution trace on a recognized shape, and with the flag OFF over the same data and registry it stays silent — both operands of the probe's guard are observable, not only answer-neutral.</summary>
    [TestMethod]
    public async Task ValueIndexProbeInterceptionAnnouncesItselfOnlyUnderTheFlag()
    {
        Assert.IsTrue(await ProbeEventObservedAsync(preferValueIndexes: true).ConfigureAwait(false), "The flag-on engine must announce the value-index probe in its trace.");
        Assert.IsFalse(await ProbeEventObservedAsync(preferValueIndexes: false).ConfigureAwait(false), "The flag-off engine must not consult the probe entry.");
    }

    /// <summary>Runs the recognized point-axis window with an execution trace attached and reports whether the value-index-probe entry announced itself.</summary>
    /// <param name="preferValueIndexes">Whether the probe route is preferred.</param>
    /// <returns>Whether an <see cref="SparqlExecutionEventKind.InterceptionApplied"/> event carried the value-index-probe label.</returns>
    private async Task<bool> ProbeEventObservedAsync(bool preferValueIndexes)
    {
        List<DataTriple> data =
        [
            new DataTriple(Iri("s1"), Iri("at"), DateTimeLiteral("2020-01-01T10:00:00Z")),
            new DataTriple(Iri("s2"), Iri("at"), DateTimeLiteral("2020-01-02T10:00:00Z")),
        ];

        TraceSink sink = new();
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            data,
            expressionContext: SparqlExpressionContext.CreateDefault(),
            executionTrace: sink.Append,
            enginePolicy: new SparqlEnginePolicy(PreferValueIndexes: preferValueIndexes),
            valueIndexes: ComposedRegistry(TimeSpan.Zero),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(
            Translate($"PREFIX xsd: <{XsdNamespace}> SELECT ?s ?v WHERE {{ ?s <http://example.org/at> ?v FILTER(?v >= \"2020-01-02T00:00:00Z\"^^xsd:dateTime) }}", pool),
            TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, solutions, "The window admits exactly the later instant on either route.");

        foreach(SparqlExecutionTraceEvent traceEvent in sink.Events)
        {
            if(traceEvent.Kind == SparqlExecutionEventKind.InterceptionApplied && traceEvent.Label == SparqlInterceptions.ValueIndexProbeName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs one query through the probe-enabled engine and the scan-only engine over the shared UTC dataset, rendering both solution sets canonically.</summary>
    /// <param name="query">The query text, without the XSD prefix line.</param>
    /// <returns>The canonical renderings (probed, scanned).</returns>
    private Task<(string Probed, string Scanned)> BothRoutesAsync(string query)
    {
        return BothRoutesAsync(query, TimeSpan.Zero, extraData: null);
    }

    /// <summary>Runs one query through both routes under an explicit implicit timezone (threaded to BOTH the expression context and the registered methods — the H1 shared configuration) and optional extra data.</summary>
    /// <param name="query">The query text, without the XSD prefix line.</param>
    /// <param name="implicitTimezone">The implicit timezone both the engine and the methods normalize naive values with.</param>
    /// <param name="extraData">Extra triples appended to the shared dataset, or <see langword="null"/>.</param>
    /// <returns>The canonical renderings (probed, scanned).</returns>
    private async Task<(string Probed, string Scanned)> BothRoutesAsync(string query, TimeSpan implicitTimezone, IReadOnlyList<DataTriple>? extraData)
    {
        SparqlQueryEngine probeEngine = await BuildEngineAsync(preferValueIndexes: true, implicitTimezone, extraData).ConfigureAwait(false);
        SparqlQueryEngine scanEngine = await BuildEngineAsync(preferValueIndexes: false, implicitTimezone, extraData).ConfigureAwait(false);

        string prefixed = $"PREFIX xsd: <{XsdNamespace}> {query}";
        using Utf8StringPool probePool = new();
        IReadOnlyList<SparqlSolution> probed = await probeEngine.EvaluateAsync(Translate(prefixed, probePool), TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool scanPool = new();
        IReadOnlyList<SparqlSolution> scanned = await scanEngine.EvaluateAsync(Translate(prefixed, scanPool), TestContext.CancellationToken).ConfigureAwait(false);

        return (Render(probed), Render(scanned));
    }

    /// <summary>Builds the engine over the shared temporal dataset with the registry composed and the probe flag set per <paramref name="preferValueIndexes"/>, under UTC.</summary>
    /// <param name="preferValueIndexes">Whether the probe route is preferred.</param>
    /// <returns>The engine.</returns>
    private Task<SparqlQueryEngine> BuildEngineAsync(bool preferValueIndexes)
    {
        return BuildEngineAsync(preferValueIndexes, TimeSpan.Zero, extraData: null);
    }

    /// <summary>Builds the engine over the shared temporal dataset plus optional extra triples, threading ONE implicit timezone into the expression context and the registered methods alike.</summary>
    /// <param name="preferValueIndexes">Whether the probe route is preferred.</param>
    /// <param name="implicitTimezone">The implicit timezone both the engine and the methods normalize naive values with.</param>
    /// <param name="extraData">Extra triples appended to the shared dataset, or <see langword="null"/>.</param>
    /// <returns>The engine.</returns>
    private async Task<SparqlQueryEngine> BuildEngineAsync(bool preferValueIndexes, TimeSpan implicitTimezone, IReadOnlyList<DataTriple>? extraData)
    {
        List<DataTriple> data =
        [
            new DataTriple(Iri("s1"), Iri("at"), DateTimeLiteral("2020-01-01T10:00:00Z")),
            new DataTriple(Iri("s2"), Iri("at"), DateTimeLiteral("2020-01-02T10:00:00Z")),
            new DataTriple(Iri("s3"), Iri("at"), DateTimeLiteral("not a date")),
            new DataTriple(Iri("s4"), Iri("at"), Iri("notAValue")),
            new DataTriple(Iri("s6"), Iri("at"), DateTimeLiteral("2016-12-31T23:59:60Z")),
            new DataTriple(Iri("s7"), Iri("at"), new Literal(Utf8Strings.From("2020-01-03T00:00:00Z"), new NamedNode(Vocabulary.Xsd.String))),
            new DataTriple(Iri("o1"), Iri("from"), DateTimeLiteral("2020-01-05T00:00:00Z")),
            new DataTriple(Iri("o1"), Iri("until"), DateTimeLiteral("2020-01-12T00:00:00Z")),
            new DataTriple(Iri("o1"), Iri("note"), Iri("annotationTarget")),
            new DataTriple(Iri("o2"), Iri("from"), DateTimeLiteral("2020-02-01T00:00:00Z")),
            new DataTriple(Iri("o2"), Iri("until"), DateTimeLiteral("2020-02-05T00:00:00Z")),
        ];

        if(extraData is not null)
        {
            data.AddRange(extraData);
        }

        return await SparqlQueryEngine.BuildAsync(
            data,
            expressionContext: SparqlExpressionContext.CreateDefault(implicitTimezone: implicitTimezone),
            enginePolicy: new SparqlEnginePolicy(PreferValueIndexes: preferValueIndexes),
            valueIndexes: ComposedRegistry(implicitTimezone),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Composes the registry: the point axis over <c>:at</c> and the interval pair over <c>:from</c>/<c>:until</c>, both under one implicit timezone.</summary>
    /// <param name="implicitTimezone">The implicit timezone the methods normalize naive values with — the SAME one the engine's expression context captures.</param>
    /// <returns>The registry.</returns>
    private static ValueIndexRegistry ComposedRegistry(TimeSpan implicitTimezone)
    {
        Utf8String at = Utf8Strings.From(Ex + "at");
        Utf8String from = Utf8Strings.From(Ex + "from");
        Utf8String until = Utf8Strings.From(Ex + "until");
        ValueAxisDeclaration pointAxis = ValueAxisDeclaration.PointAxis(at);
        ValueAxisDeclaration intervalAxis = ValueAxisDeclaration.IntervalPair(from, until);

        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, pointAxis, implicitTimezone),
                pointAxis,
                new EmptySource(),
                selfTestCases: []))
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, intervalAxis, implicitTimezone),
                intervalAxis,
                new EmptySource(),
                selfTestCases: []))
            .Build();
    }

    /// <summary>Finds the first <see cref="Filter"/> in an algebra tree by an explicit work stack.</summary>
    /// <param name="root">The tree root.</param>
    /// <returns>The filter, or <see langword="null"/>.</returns>
    private static Filter? FindFilter(AlgebraOperator root)
    {
        Stack<AlgebraOperator> pending = new();
        pending.Push(root);
        while(pending.Count > 0)
        {
            AlgebraOperator node = pending.Pop();
            if(node is Filter filter)
            {
                return filter;
            }

            foreach(AlgebraOperator child in node.Children)
            {
                pending.Push(child);
            }
        }

        return null;
    }

    /// <summary>Renders solutions canonically: one line per solution, bindings sorted within the line, lines sorted — a multiset comparison surface.</summary>
    /// <param name="solutions">The solutions.</param>
    /// <returns>The canonical rendering.</returns>
    private static string Render(IReadOnlyList<SparqlSolution> solutions)
    {
        List<string> lines = [];
        foreach(SparqlSolution solution in solutions)
        {
            List<string> cells = [];
            foreach(SparqlBinding binding in solution.Bindings)
            {
                cells.Add($"{binding.Variable.Name}={binding.Value}");
            }

            cells.Sort(StringComparer.Ordinal);
            lines.Add(string.Join("|", cells));
        }

        lines.Sort(StringComparer.Ordinal);

        return string.Join("\n", lines);
    }

    /// <summary>Parses and translates a query to algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The string pool the parse allocates from.</param>
    /// <returns>The algebra root.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Builds an example-namespace IRI term.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTimeLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>An execution-trace sink collecting events for the firing witness — the explicit binding frame the trace handler binds through.</summary>
    private sealed class TraceSink
    {
        /// <summary>The collected events.</summary>
        public List<SparqlExecutionTraceEvent> Events { get; } = [];

        /// <summary>Appends one event.</summary>
        /// <param name="traceEvent">The event.</param>
        public void Append(in SparqlExecutionTraceEvent traceEvent)
        {
            Events.Add(traceEvent);
        }
    }

    /// <summary>An empty registrant sample corpus (the method's semantics are certified by its own battery).</summary>
    private sealed class EmptySource: ValueSegmentSource
    {
        /// <summary>Enumerates nothing.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>No entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return [];
        }
    }
}
