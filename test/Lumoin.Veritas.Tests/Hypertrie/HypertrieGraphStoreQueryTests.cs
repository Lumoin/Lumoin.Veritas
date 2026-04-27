using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class HypertrieGraphStoreQueryTests
{
    public TestContext TestContext { get; set; } = null!;

    //A small social-graph-shaped dataset used by most tests.
    //Predicate 100 = "knows", 200 = "livesIn".
    //(1 knows 2), (2 knows 3), (1 livesIn 10), (2 livesIn 10),
    //(3 livesIn 20).
    private static readonly EncodedTriple[] SocialTriples =
    [
        EncodedTriple.FromEncoded(1, 100, 2),
        EncodedTriple.FromEncoded(2, 100, 3),
        EncodedTriple.FromEncoded(1, 200, 10),
        EncodedTriple.FromEncoded(2, 200, 10),
        EncodedTriple.FromEncoded(3, 200, 20),
    ];

    [TestMethod]
    public async Task SinglePatternQueryYieldsAllMatchingTriples()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(2, solutions);

        HashSet<(long, long)> pairs = [.. solutions.Select(sol => (sol.Get(s), sol.Get(o)))];
        Assert.IsTrue(pairs.SetEquals(new[] { (1L, 2L), (2L, 3L) }));
    }

    [TestMethod]
    public async Task TwoPatternJoinReturnsCorrectIntersection()
    {
        //Find ?x such that (?x knows ?y) and (?y livesIn 10).
        //knows: (1,2), (2,3); livesIn 10: (1, 2). So ?x where
        //?y in {2}: ?x = 1. One solution: ?x=1, ?y=2.
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");

        TriplePattern p1 = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(y));

        TriplePattern p2 = new(
            PatternPosition.OfVariable(y),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.Bound(TermId.FromEncoded(10)));

        BasicGraphPattern bgp = new([p1, p2], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, solutions);
        Assert.AreEqual(1L, solutions[0].Get(x));
        Assert.AreEqual(2L, solutions[0].Get(y));
    }

    [TestMethod]
    public async Task ThreePatternTriangleJoin()
    {
        //(?a knows ?b) ∧ (?b knows ?c) ∧ (?a livesIn ?city) ∧
        //(?c livesIn ?city). With our data: a=1, b=2, c=3.
        //a livesIn 10, c livesIn 20. They differ; no solutions.
        //Use a different triple set so we get a hit.
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, 100, 2),
            EncodedTriple.FromEncoded(2, 100, 3),
            EncodedTriple.FromEncoded(1, 200, 10),
            EncodedTriple.FromEncoded(3, 200, 10),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable city = registry.GetOrAdd("city");

        TriplePattern p1 = new(
            PatternPosition.OfVariable(a),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(b));

        TriplePattern p2 = new(
            PatternPosition.OfVariable(b),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(c));

        TriplePattern p3 = new(
            PatternPosition.OfVariable(a),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.OfVariable(city));

        TriplePattern p4 = new(
            PatternPosition.OfVariable(c),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.OfVariable(city));

        BasicGraphPattern bgp = new([p1, p2, p3, p4], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, solutions);
        Assert.AreEqual(1L, solutions[0].Get(a));
        Assert.AreEqual(2L, solutions[0].Get(b));
        Assert.AreEqual(3L, solutions[0].Get(c));
        Assert.AreEqual(10L, solutions[0].Get(city));
    }

    [TestMethod]
    public async Task EmptyBgpYieldsSingleEmptySolution()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        BasicGraphPattern bgp = new([], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, solutions);
        Assert.IsEmpty(solutions[0].Bindings);
    }

    [TestMethod]
    public async Task FullyBoundPatternThatExistsYieldsSingleEmptySolution()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();

        TriplePattern bound = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.Bound(TermId.FromEncoded(2)));

        BasicGraphPattern bgp = new([bound], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, solutions);
        Assert.IsEmpty(solutions[0].Bindings);
    }

    [TestMethod]
    public async Task FullyBoundPatternThatDoesNotExistYieldsNothing()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();

        TriplePattern absent = new(
            PatternPosition.Bound(TermId.FromEncoded(999)),
            PatternPosition.Bound(TermId.FromEncoded(999)),
            PatternPosition.Bound(TermId.FromEncoded(999)));

        BasicGraphPattern bgp = new([absent], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsEmpty(solutions);
    }

    [TestMethod]
    public async Task MixedFullyBoundAndVariableBearingPatterns()
    {
        //Mix: ?x knows ?y AND (1 livesIn 10 — verifies, exists).
        //Should produce all (?x, ?y) from the knows pattern.
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");

        TriplePattern variableBearing = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(y));

        TriplePattern existsCheck = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(200)),
            PatternPosition.Bound(TermId.FromEncoded(10)));

        BasicGraphPattern bgp = new([variableBearing, existsCheck], registry);

        List<Solution> solutions = await CollectAsync(store.Query(bgp, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
    }

    [TestMethod]
    public async Task AccessControlAllowReturnsAllSolutions()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        AccessControlDelegate allowAll = static (request, ct) => ValueTask.FromResult(AccessDecision.Allow);
        AccessContext context = new TestAccessContext("user");

        List<Solution> solutions = await CollectAsync(store.Query(
            bgp,
            VeritasClock.System,
            accessControl: allowAll,
            accessContext: context,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
    }

    [TestMethod]
    public async Task AccessControlDenyOnSubjectFiltersOutSolution()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        //Deny anything where the subject is 1.
        AccessControlDelegate policy = static (request, ct) =>
        {
            AccessDecision result = request.Triple.Subject.Encoded == 1
                ? AccessDecision.Deny
                : AccessDecision.Allow;

            return ValueTask.FromResult(result);
        };

        AccessContext context = new TestAccessContext("user");

        List<Solution> solutions = await CollectAsync(store.Query(
            bgp,
            VeritasClock.System,
            accessControl: policy,
            accessContext: context,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        //Of (1,2) and (2,3), only (2,3) survives.
        Assert.HasCount(1, solutions);
        Assert.AreEqual(2L, solutions[0].Get(s));
        Assert.AreEqual(3L, solutions[0].Get(o));
    }

    [TestMethod]
    public async Task AccessControlNotFoundFiltersOutSolutionSilently()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(
            request.Triple.Subject.Encoded == 1 ? AccessDecision.NotFound : AccessDecision.Allow);

        AccessContext context = new TestAccessContext("user");

        List<Solution> solutions = await CollectAsync(store.Query(
            bgp,
            VeritasClock.System,
            accessControl: policy,
            accessContext: context,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        //Same observable result as Deny: one solution.
        Assert.HasCount(1, solutions);
    }

    [TestMethod]
    public async Task AccessControlDenyEmitsAuditTraceEvent()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(AccessDecision.Deny);

        List<QueryTraceEvent> events = [];
        TraceHandler<QueryTraceEvent> handler = (in QueryTraceEvent evt) => events.Add(evt);

        AccessContext context = new TestAccessContext("user");

        await CollectAsync(store.Query(
            bgp,
            VeritasClock.System,
            accessControl: policy,
            accessContext: context,
            traceHandler: handler,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        //Deny decisions must produce AccessDenied trace events for the audit channel.
        List<QueryTraceEventKind> kinds = [.. events.Select(e => e.Kind)];
        Assert.Contains(QueryTraceEventKind.AccessDenied, kinds);
    }

    [TestMethod]
    public async Task AccessControlNotFoundDoesNotEmitTraceEvent()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(AccessDecision.NotFound);

        List<QueryTraceEvent> events = [];
        TraceHandler<QueryTraceEvent> handler = (in QueryTraceEvent evt) => events.Add(evt);

        AccessContext context = new TestAccessContext("user");

        await CollectAsync(store.Query(
            bgp,
            VeritasClock.System,
            accessControl: policy,
            accessContext: context,
            traceHandler: handler,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        //NotFound is privacy-preserving — no audit event must be
        //emitted that would let the requester infer denied data.
        List<QueryTraceEventKind> kinds = [.. events.Select(e => e.Kind)];
        Assert.DoesNotContain(QueryTraceEventKind.AccessDenied, kinds);
    }

    [TestMethod]
    public async Task AccessControlWithoutContextIsRejected()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        BasicGraphPattern bgp = new([], registry);

        AccessControlDelegate policy = static (request, ct) => ValueTask.FromResult(AccessDecision.Allow);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach(Solution _ in store.Query(bgp, VeritasClock.System, accessControl: policy, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
            {
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task QueryStartedAndCompletedTraceEventsAreEmitted()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(100)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        List<QueryTraceEvent> events = [];
        TraceHandler<QueryTraceEvent> handler = (in QueryTraceEvent evt) => events.Add(evt);

        await CollectAsync(store.Query(bgp, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        List<QueryTraceEventKind> kinds = [.. events.Select(e => e.Kind)];
        Assert.Contains(QueryTraceEventKind.QueryStarted, kinds);
        Assert.Contains(QueryTraceEventKind.QueryCompleted, kinds);
    }

    [TestMethod]
    public async Task QueryRejectsNullBgp()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(SocialTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach(Solution _ in store.Query(null!, VeritasClock.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
            {
            }
        }).ConfigureAwait(false);
    }

    private static async Task<List<Solution>> CollectAsync(IAsyncEnumerable<Solution> source)
    {
        List<Solution> results = [];

        await foreach(Solution solution in source.ConfigureAwait(false))
        {
            results.Add(solution);
        }

        return results;
    }

    private sealed record TestAccessContext(string Subject): AccessContext;
}
