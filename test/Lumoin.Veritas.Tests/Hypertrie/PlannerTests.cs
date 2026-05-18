using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class PlannerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PlannerCanReturnDescendVariableDecision()
    {
        Variable target = new(0);
        Planner planner = (context, ct) => PlannerDecision.DescendVariable(target);

        PlannerContext context = MakeMinimalContext();
        PlannerDecision decision = planner(context, TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.DescendVariable, decision.Kind);
        Assert.AreEqual(target, decision.AsDescendVariable());
    }

    [TestMethod]
    public void PlannerCanReturnSkipBranchDecision()
    {
        Planner planner = (context, ct) => PlannerDecision.SkipBranch();

        PlannerDecision decision = planner(MakeMinimalContext(), TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.SkipBranch, decision.Kind);
    }

    [TestMethod]
    public void PlannerCanReturnYieldSolutionDecision()
    {
        Planner planner = (context, ct) => PlannerDecision.YieldSolution();

        PlannerDecision decision = planner(MakeMinimalContext(), TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.YieldSolution, decision.Kind);
    }

    [TestMethod]
    public void PlannerCanReturnStopQueryDecision()
    {
        Planner planner = (context, ct) => PlannerDecision.StopQuery();

        PlannerDecision decision = planner(MakeMinimalContext(), TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.StopQuery, decision.Kind);
    }

    [TestMethod]
    public void PlannerCanInspectQueryFromContext()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(2)));

        BasicGraphPattern bgp = new([pattern], registry);

        Planner planner = (context, ct) =>
        {
            //Pick the first variable from the query.
            return PlannerDecision.DescendVariable(context.Query.Variables[0]);
        };

        PlannerContext context = new(bgp, [], [], []);
        PlannerDecision decision = planner(context, TestContext.CancellationToken);

        Assert.AreEqual(s, decision.AsDescendVariable());
    }

    [TestMethod]
    public void PlannerCanInspectBindings()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        BasicGraphPattern bgp = new([], registry);
        VariableBinding[] bindings = [new(s, TermId.FromEncoded(42))];

        Planner planner = (context, ct) =>
        {
            //If subject is bound, descend by object next; otherwise descend by subject.
            foreach(VariableBinding binding in context.Bindings)
            {
                if(binding.Variable == s)
                {
                    return PlannerDecision.DescendVariable(o);
                }
            }

            return PlannerDecision.DescendVariable(s);
        };

        PlannerContext context = new(bgp, bindings, [], []);
        PlannerDecision decision = planner(context, TestContext.CancellationToken);

        Assert.AreEqual(o, decision.AsDescendVariable());
    }

    [TestMethod]
    public void PlannerCanInspectIteratorSnapshots()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");

        BasicGraphPattern bgp = new([], registry);
        IteratorSnapshot[] snapshots = [new(PatternIndex: 0, CurrentVariable: s, Key: TermId.FromEncoded(100), AtEnd: false, DescendedLevels: 0)];

        Planner planner = (context, ct) =>
        {
            //If any iterator is at end, stop the query.
            foreach(IteratorSnapshot snapshot in context.Iterators)
            {
                if(snapshot.AtEnd)
                {
                    return PlannerDecision.StopQuery();
                }
            }

            return PlannerDecision.DescendVariable(context.Iterators[0].CurrentVariable);
        };

        PlannerContext context = new(bgp, [], snapshots, []);
        PlannerDecision decision = planner(context, TestContext.CancellationToken);

        Assert.AreEqual(s, decision.AsDescendVariable());
    }

    [TestMethod]
    public void PlannerCanInspectRecentDenials()
    {
        VariableRegistry registry = new();
        BasicGraphPattern bgp = new([], registry);

        EncodedTriple[] denials = [EncodedTriple.FromEncoded(1, 2, 3), EncodedTriple.FromEncoded(4, 5, 6)];

        int observedCount = -1;
        Planner planner = (context, ct) =>
        {
            observedCount = context.RecentDenials.Count;

            return PlannerDecision.SkipBranch();
        };

        PlannerContext context = new(bgp, [], [], denials);
        planner(context, TestContext.CancellationToken);

        Assert.AreEqual(2, observedCount);
    }

    [TestMethod]
    public void PlannerHonoursCancellationToken()
    {
        Planner planner = static (context, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            return PlannerDecision.SkipBranch();
        };

        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => planner(MakeMinimalContext(), cts.Token));
    }

    private static PlannerContext MakeMinimalContext()
    {
        VariableRegistry registry = new();
        BasicGraphPattern bgp = new([], registry);

        return new(bgp, [], [], []);
    }
}
