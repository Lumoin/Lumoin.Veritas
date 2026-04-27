using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class QueryTraceEventTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void QueryStartedFactoryProducesQueryStartedKind()
    {
        Guid correlation = Guid.NewGuid();

        QueryTraceEvent evt = QueryTraceEvent.QueryStarted(
            sequenceNumber: 1,
            timestampTicks: 1000,
            correlationId: correlation,
            patternCount: 3);

        Assert.AreEqual(QueryTraceEventKind.QueryStarted, evt.Kind);
        Assert.AreEqual(1L, evt.SequenceNumber);
        Assert.AreEqual(1000L, evt.TimestampTicks);
        Assert.AreEqual(correlation, evt.CorrelationId);
        Assert.AreEqual(3, evt.Count);
        Assert.AreEqual(-1, evt.PatternIndex);
    }

    [TestMethod]
    public void QueryCompletedFactoryProducesQueryCompletedKind()
    {
        QueryTraceEvent evt = QueryTraceEvent.QueryCompleted(
            sequenceNumber: 99,
            timestampTicks: 2000,
            correlationId: Guid.Empty,
            solutionCount: 17);

        Assert.AreEqual(QueryTraceEventKind.QueryCompleted, evt.Kind);
        Assert.AreEqual(17, evt.Count);
    }

    [TestMethod]
    public void IteratorOpenedCarriesPatternIndex()
    {
        QueryTraceEvent evt = QueryTraceEvent.IteratorOpened(
            sequenceNumber: 2,
            timestampTicks: 1100,
            correlationId: Guid.Empty,
            patternIndex: 1);

        Assert.AreEqual(QueryTraceEventKind.IteratorOpened, evt.Kind);
        Assert.AreEqual(1, evt.PatternIndex);
    }

    [TestMethod]
    public void IteratorAdvancedCarriesVariableAndValue()
    {
        Variable variable = new(2);

        QueryTraceEvent evt = QueryTraceEvent.IteratorAdvanced(
            sequenceNumber: 5,
            timestampTicks: 1500,
            correlationId: Guid.Empty,
            patternIndex: 0,
            variable: variable,
            value: 42);

        Assert.AreEqual(QueryTraceEventKind.IteratorAdvanced, evt.Kind);
        Assert.AreEqual(0, evt.PatternIndex);
        Assert.AreEqual(variable, evt.Variable);
        Assert.AreEqual(42L, evt.Value);
    }

    [TestMethod]
    public void IteratorReachedEndCarriesVariable()
    {
        Variable variable = new(3);

        QueryTraceEvent evt = QueryTraceEvent.IteratorReachedEnd(
            sequenceNumber: 6,
            timestampTicks: 1600,
            correlationId: Guid.Empty,
            patternIndex: 1,
            variable: variable);

        Assert.AreEqual(QueryTraceEventKind.IteratorReachedEnd, evt.Kind);
        Assert.AreEqual(variable, evt.Variable);
    }

    [TestMethod]
    public void LeapfrogStepCarriesVariableAndValue()
    {
        Variable variable = new(0);

        QueryTraceEvent evt = QueryTraceEvent.LeapfrogStep(
            sequenceNumber: 10,
            timestampTicks: 2000,
            correlationId: Guid.Empty,
            variable: variable,
            value: 100);

        Assert.AreEqual(QueryTraceEventKind.LeapfrogStep, evt.Kind);
        Assert.AreEqual(variable, evt.Variable);
        Assert.AreEqual(100L, evt.Value);
    }

    [TestMethod]
    public void SolutionYieldedCarriesNoPayload()
    {
        QueryTraceEvent evt = QueryTraceEvent.SolutionYielded(
            sequenceNumber: 20,
            timestampTicks: 3000,
            correlationId: Guid.Empty);

        Assert.AreEqual(QueryTraceEventKind.SolutionYielded, evt.Kind);
        Assert.AreEqual(-1, evt.PatternIndex);
        Assert.AreEqual(0L, evt.Value);
        Assert.AreEqual(0, evt.Count);
    }

    [TestMethod]
    public void PlannerDecisionCarriesChosenVariable()
    {
        Variable chosen = new(7);

        QueryTraceEvent evt = QueryTraceEvent.PlannerDecision(
            sequenceNumber: 4,
            timestampTicks: 1400,
            correlationId: Guid.Empty,
            variable: chosen);

        Assert.AreEqual(QueryTraceEventKind.PlannerDecision, evt.Kind);
        Assert.AreEqual(chosen, evt.Variable);
    }

    [TestMethod]
    public void AccessDeniedCarriesFullTriple()
    {
        QueryTraceEvent evt = QueryTraceEvent.AccessDenied(
            sequenceNumber: 50,
            timestampTicks: 5000,
            correlationId: Guid.Empty,
            subject: 100,
            predicate: 200,
            @object: 300);

        Assert.AreEqual(QueryTraceEventKind.AccessDenied, evt.Kind);
        Assert.AreEqual(100L, evt.DeniedSubject);
        Assert.AreEqual(200L, evt.DeniedPredicate);
        Assert.AreEqual(300L, evt.DeniedObject);
    }

    [TestMethod]
    public void EventsOfSameKindWithSameFieldsCompareEqual()
    {
        QueryTraceEvent left = QueryTraceEvent.IteratorOpened(1, 1000, Guid.Empty, 2);
        QueryTraceEvent right = QueryTraceEvent.IteratorOpened(1, 1000, Guid.Empty, 2);

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void EventsOfDifferentKindsCompareUnequal()
    {
        Guid correlation = Guid.NewGuid();
        QueryTraceEvent started = QueryTraceEvent.QueryStarted(1, 1000, correlation, 0);
        QueryTraceEvent completed = QueryTraceEvent.QueryCompleted(1, 1000, correlation, 0);

        Assert.AreNotEqual(started, completed);
    }
}
