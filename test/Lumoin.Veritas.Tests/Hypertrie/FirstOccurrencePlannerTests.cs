using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class FirstOccurrencePlannerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DescendsByFirstOccurrenceVariableWhenNothingBound()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable p = registry.GetOrAdd("p");
        Variable o = registry.GetOrAdd("o");
        BasicGraphPattern bgp = new([AllVariables(s, p, o)], registry);

        FirstOccurrencePlanner planner = new(bgp);

        PlannerContext context = new(bgp, [], [], []);
        PlannerDecision decision = planner.Plan(context, TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.DescendVariable, decision.Kind);
        Assert.AreEqual(s, decision.AsDescendVariable());
    }

    [TestMethod]
    public void DescendsByNextUnboundVariableAsBindingsAccumulate()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable p = registry.GetOrAdd("p");
        Variable o = registry.GetOrAdd("o");
        BasicGraphPattern bgp = new([AllVariables(s, p, o)], registry);

        FirstOccurrencePlanner planner = new(bgp);

        VariableBinding[] oneBound = [new(s, TermId.FromEncoded(42))];
        PlannerContext afterFirst = new(bgp, oneBound, [], []);
        PlannerDecision decision = planner.Plan(afterFirst, TestContext.CancellationToken);

        Assert.AreEqual(p, decision.AsDescendVariable());

        VariableBinding[] twoBound = [new(s, TermId.FromEncoded(42)), new(p, TermId.FromEncoded(99))];
        PlannerContext afterSecond = new(bgp, twoBound, [], []);
        decision = planner.Plan(afterSecond, TestContext.CancellationToken);

        Assert.AreEqual(o, decision.AsDescendVariable());
    }

    [TestMethod]
    public void YieldsSolutionWhenAllVariablesBound()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable p = registry.GetOrAdd("p");
        Variable o = registry.GetOrAdd("o");
        BasicGraphPattern bgp = new([AllVariables(s, p, o)], registry);

        FirstOccurrencePlanner planner = new(bgp);

        VariableBinding[] allBound = [new(s, TermId.FromEncoded(1)), new(p, TermId.FromEncoded(2)), new(o, TermId.FromEncoded(3))];
        PlannerContext context = new(bgp, allBound, [], []);
        PlannerDecision decision = planner.Plan(context, TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.YieldSolution, decision.Kind);
    }

    [TestMethod]
    public void SkipsBranchWhenAnyIteratorAtEnd()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        BasicGraphPattern bgp = new([], registry);

        FirstOccurrencePlanner planner = new(bgp);

        IteratorSnapshot[] snapshots =
        [
            new(PatternIndex: 0, CurrentVariable: s, Key: TermId.FromEncoded(0), AtEnd: true, DescendedLevels: 0),
        ];

        PlannerContext context = new(bgp, [], snapshots, []);
        PlannerDecision decision = planner.Plan(context, TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.SkipBranch, decision.Kind);
    }

    [TestMethod]
    public void EndedIteratorCheckTakesPrecedenceOverYield()
    {
        //If all variables happen to be bound but an iterator has
        //also reached end, skip-branch wins. This shouldn't arise
        //from a well-formed driver — yielding never coexists with
        //an at-end iterator — but the planner is defensive.
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        BasicGraphPattern bgp = new([], registry);

        FirstOccurrencePlanner planner = new(bgp);

        IteratorSnapshot[] snapshots =
        [
            new(PatternIndex: 0, CurrentVariable: s, Key: TermId.FromEncoded(0), AtEnd: true, DescendedLevels: 0),
        ];

        PlannerContext context = new(bgp, [new(s, TermId.FromEncoded(1))], snapshots, []);
        PlannerDecision decision = planner.Plan(context, TestContext.CancellationToken);

        Assert.AreEqual(PlannerDecisionKind.SkipBranch, decision.Kind);
    }

    [TestMethod]
    public void HonoursCancellationToken()
    {
        VariableRegistry registry = new();
        BasicGraphPattern bgp = new([], registry);
        FirstOccurrencePlanner planner = new(bgp);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => planner.Plan(new(bgp, [], [], []), cts.Token));
    }

    [TestMethod]
    public void ConstructorRejectsNullQuery()
    {
        Assert.Throws<ArgumentNullException>(() => new FirstOccurrencePlanner(null!));
    }

    [TestMethod]
    public void PlannersFactoryReturnsDelegateBackedByInstance()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        BasicGraphPattern bgp = new([], registry);

        Planner planner = Planners.FirstOccurrence(bgp);

        Assert.IsNotNull(planner.Target);
        Assert.IsInstanceOfType<FirstOccurrencePlanner>(planner.Target);
    }

    [TestMethod]
    public void PlannersFactoryRejectsNullQuery()
    {
        Assert.Throws<ArgumentNullException>(() => Planners.FirstOccurrence(null!));
    }

    [TestMethod]
    public void PlannersFactoryProducedDelegateBehavesLikeInstance()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        BasicGraphPattern bgp = new([], registry);

        Planner planner = Planners.FirstOccurrence(bgp);

        PlannerDecision decision = planner(new(bgp, [], [], []), TestContext.CancellationToken);

        //Empty BGP has zero variables → zero bindings is also zero
        //of zero, so the planner yields immediately.
        Assert.AreEqual(PlannerDecisionKind.YieldSolution, decision.Kind);
    }

    private static TriplePattern AllVariables(Variable s, Variable p, Variable o)
    {
        return new(
            PatternPosition.OfVariable(s),
            PatternPosition.OfVariable(p),
            PatternPosition.OfVariable(o));
    }
}