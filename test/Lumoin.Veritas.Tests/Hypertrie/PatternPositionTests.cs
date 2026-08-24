using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class PatternPositionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BoundFactoryProducesBoundPosition()
    {
        TermId term = TermId.FromEncoded(42);

        PatternPosition position = PatternPosition.Bound(term);

        Assert.AreEqual(PatternPositionKind.Bound, position.Kind);
        Assert.IsTrue(position.IsBound);
        Assert.IsFalse(position.IsVariable);
        Assert.AreEqual(term, position.BoundTerm);
        Assert.AreEqual(default, position.Variable);
    }

    [TestMethod]
    public void OfVariableFactoryProducesVariablePosition()
    {
        Variable variable = new(7);

        PatternPosition position = PatternPosition.OfVariable(variable);

        Assert.AreEqual(PatternPositionKind.Variable, position.Kind);
        Assert.IsTrue(position.IsVariable);
        Assert.IsFalse(position.IsBound);
        Assert.AreEqual(variable, position.Variable);
        Assert.AreEqual(default, position.BoundTerm);
    }

    [TestMethod]
    public void AsBoundReturnsTermFromBoundPosition()
    {
        TermId term = TermId.FromEncoded(99);
        PatternPosition position = PatternPosition.Bound(term);

        Assert.AreEqual(term, position.AsBound());
    }

    [TestMethod]
    public void AsVariableReturnsVariableFromVariablePosition()
    {
        Variable variable = new(13);
        PatternPosition position = PatternPosition.OfVariable(variable);

        Assert.AreEqual(variable, position.AsVariable());
    }

    [TestMethod]
    public void AsBoundThrowsOnVariablePosition()
    {
        PatternPosition position = PatternPosition.OfVariable(new(0));

        Assert.Throws<InvalidOperationException>(() => position.AsBound());
    }

    [TestMethod]
    public void AsVariableThrowsOnBoundPosition()
    {
        PatternPosition position = PatternPosition.Bound(TermId.FromEncoded(1));

        Assert.Throws<InvalidOperationException>(() => position.AsVariable());
    }

    [TestMethod]
    public void EqualBoundPositionsCompareEqual()
    {
        PatternPosition left = PatternPosition.Bound(TermId.FromEncoded(5));
        PatternPosition right = PatternPosition.Bound(TermId.FromEncoded(5));

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void EqualVariablePositionsCompareEqual()
    {
        PatternPosition left = PatternPosition.OfVariable(new(3));
        PatternPosition right = PatternPosition.OfVariable(new(3));

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void BoundAndVariableWithSameUnderlyingValueCompareUnequal()
    {
        //A bound term with encoded 7 and a variable with id 7 share
        //a numeric value but have different kinds; equality must
        //distinguish them.
        PatternPosition bound = PatternPosition.Bound(TermId.FromEncoded(7));
        PatternPosition variable = PatternPosition.OfVariable(new(7));

        Assert.AreNotEqual(bound, variable);
    }
}
