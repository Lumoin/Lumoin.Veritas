using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class TriplePatternTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void AtReturnsCorrectPositionByIndex()
    {
        PatternPosition s = PatternPosition.OfVariable(new(0));
        PatternPosition p = PatternPosition.Bound(TermId.FromEncoded(10));
        PatternPosition o = PatternPosition.OfVariable(new(1));
        TriplePattern pattern = new(s, p, o);

        Assert.AreEqual(s, pattern.At(0));
        Assert.AreEqual(p, pattern.At(1));
        Assert.AreEqual(o, pattern.At(2));
    }

    [TestMethod]
    public void AtThrowsForOutOfRangeIndex()
    {
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(2)),
            PatternPosition.Bound(TermId.FromEncoded(3)));

        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.At(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.At(3));
    }

    [TestMethod]
    public void VariablesYieldsDistinctVariablesInOrder()
    {
        Variable x = new(0);
        Variable y = new(1);
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(99)),
            PatternPosition.OfVariable(y));

        Variable[] variables = [.. pattern.Variables()];

        Assert.HasCount(2, variables);
        Assert.AreEqual(x, variables[0]);
        Assert.AreEqual(y, variables[1]);
    }

    [TestMethod]
    public void VariablesDeduplicatesRepeatedVariables()
    {
        Variable x = new(0);
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(99)),
            PatternPosition.OfVariable(x));

        Variable[] variables = [.. pattern.Variables()];

        Assert.HasCount(1, variables);
        Assert.AreEqual(x, variables[0]);
    }

    [TestMethod]
    public void VariablesYieldsNothingForFullyBoundPattern()
    {
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(2)),
            PatternPosition.Bound(TermId.FromEncoded(3)));

        Assert.IsEmpty(pattern.Variables());
    }

    [TestMethod]
    public void HasSelfJoinFalseForDistinctVariables()
    {
        TriplePattern pattern = new(
            PatternPosition.OfVariable(new(0)),
            PatternPosition.OfVariable(new(1)),
            PatternPosition.OfVariable(new(2)));

        Assert.IsFalse(pattern.HasSelfJoin());
    }

    [TestMethod]
    public void HasSelfJoinTrueWhenSubjectAndObjectShareVariable()
    {
        Variable x = new(0);
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(10)),
            PatternPosition.OfVariable(x));

        Assert.IsTrue(pattern.HasSelfJoin());
    }

    [TestMethod]
    public void HasSelfJoinTrueWhenSubjectAndPredicateShareVariable()
    {
        Variable x = new(0);
        TriplePattern pattern = new(
            PatternPosition.OfVariable(x),
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(10)));

        Assert.IsTrue(pattern.HasSelfJoin());
    }

    [TestMethod]
    public void HasSelfJoinTrueWhenPredicateAndObjectShareVariable()
    {
        Variable x = new(0);
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(10)),
            PatternPosition.OfVariable(x),
            PatternPosition.OfVariable(x));

        Assert.IsTrue(pattern.HasSelfJoin());
    }

    [TestMethod]
    public void HasSelfJoinFalseForFullyBoundPattern()
    {
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(2)),
            PatternPosition.Bound(TermId.FromEncoded(3)));

        Assert.IsFalse(pattern.HasSelfJoin());
    }
}
