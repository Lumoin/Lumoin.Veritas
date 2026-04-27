using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class PlannerDecisionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DescendVariableFactoryProducesCorrectKindAndPayload()
    {
        Variable v = new(7);

        PlannerDecision decision = PlannerDecision.DescendVariable(v);

        Assert.AreEqual(PlannerDecisionKind.DescendVariable, decision.Kind);
        Assert.AreEqual(v, decision.Variable);
    }

    [TestMethod]
    public void SkipBranchFactoryProducesCorrectKindWithDefaultPayload()
    {
        PlannerDecision decision = PlannerDecision.SkipBranch();

        Assert.AreEqual(PlannerDecisionKind.SkipBranch, decision.Kind);
        Assert.AreEqual(default, decision.Variable);
    }

    [TestMethod]
    public void YieldSolutionFactoryProducesCorrectKindWithDefaultPayload()
    {
        PlannerDecision decision = PlannerDecision.YieldSolution();

        Assert.AreEqual(PlannerDecisionKind.YieldSolution, decision.Kind);
        Assert.AreEqual(default, decision.Variable);
    }

    [TestMethod]
    public void StopQueryFactoryProducesCorrectKindWithDefaultPayload()
    {
        PlannerDecision decision = PlannerDecision.StopQuery();

        Assert.AreEqual(PlannerDecisionKind.StopQuery, decision.Kind);
        Assert.AreEqual(default, decision.Variable);
    }

    [TestMethod]
    public void AsDescendVariableReturnsVariableForDescendDecision()
    {
        Variable v = new(13);
        PlannerDecision decision = PlannerDecision.DescendVariable(v);

        Assert.AreEqual(v, decision.AsDescendVariable());
    }

    [TestMethod]
    public void AsDescendVariableThrowsForSkipBranch()
    {
        PlannerDecision decision = PlannerDecision.SkipBranch();

        Assert.Throws<InvalidOperationException>(() => decision.AsDescendVariable());
    }

    [TestMethod]
    public void AsDescendVariableThrowsForYieldSolution()
    {
        PlannerDecision decision = PlannerDecision.YieldSolution();

        Assert.Throws<InvalidOperationException>(() => decision.AsDescendVariable());
    }

    [TestMethod]
    public void AsDescendVariableThrowsForStopQuery()
    {
        PlannerDecision decision = PlannerDecision.StopQuery();

        Assert.Throws<InvalidOperationException>(() => decision.AsDescendVariable());
    }

    [TestMethod]
    public void DescendDecisionsWithSameVariableCompareEqual()
    {
        PlannerDecision left = PlannerDecision.DescendVariable(new(5));
        PlannerDecision right = PlannerDecision.DescendVariable(new(5));

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void DescendDecisionsWithDifferentVariablesCompareUnequal()
    {
        PlannerDecision left = PlannerDecision.DescendVariable(new(1));
        PlannerDecision right = PlannerDecision.DescendVariable(new(2));

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void DecisionsOfDifferentKindsCompareUnequal()
    {
        PlannerDecision skip = PlannerDecision.SkipBranch();
        PlannerDecision yield = PlannerDecision.YieldSolution();

        Assert.AreNotEqual(skip, yield);
    }
}
