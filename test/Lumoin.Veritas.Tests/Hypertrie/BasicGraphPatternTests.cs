using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class BasicGraphPatternTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyPatternListYieldsNoVariables()
    {
        VariableRegistry registry = new();
        BasicGraphPattern bgp = new([], registry);

        Assert.IsEmpty(bgp.Patterns);
        Assert.IsEmpty(bgp.Variables);
        Assert.AreSame(registry, bgp.Registry);
    }

    [TestMethod]
    public void SinglePatternExposesItsVariables()
    {
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");

        TriplePattern pattern = new(
            PatternPosition.OfVariable(s),
            PatternPosition.Bound(TermId.FromEncoded(10)),
            PatternPosition.OfVariable(o));

        BasicGraphPattern bgp = new([pattern], registry);

        Assert.HasCount(1, bgp.Patterns);
        Assert.HasCount(2, bgp.Variables);
        Assert.AreEqual(s, bgp.Variables[0]);
        Assert.AreEqual(o, bgp.Variables[1]);
    }

    [TestMethod]
    public void VariablesAppearInLeftToRightSubjectPredicateObjectOrder()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        //First pattern introduces 'a' (subject) and 'b' (object).
        //Second pattern reuses 'b' (subject) and introduces 'c' (predicate).
        //Third pattern introduces 'd' (object); reuses 'a' (subject).
        TriplePattern p1 = new(
            PatternPosition.OfVariable(a),
            PatternPosition.Bound(TermId.FromEncoded(10)),
            PatternPosition.OfVariable(b));

        TriplePattern p2 = new(
            PatternPosition.OfVariable(b),
            PatternPosition.OfVariable(c),
            PatternPosition.Bound(TermId.FromEncoded(20)));

        TriplePattern p3 = new(
            PatternPosition.OfVariable(a),
            PatternPosition.Bound(TermId.FromEncoded(30)),
            PatternPosition.OfVariable(d));

        BasicGraphPattern bgp = new([p1, p2, p3], registry);

        Assert.HasCount(4, bgp.Variables);
        Assert.AreEqual(a, bgp.Variables[0]);
        Assert.AreEqual(b, bgp.Variables[1]);
        Assert.AreEqual(c, bgp.Variables[2]);
        Assert.AreEqual(d, bgp.Variables[3]);
    }

    [TestMethod]
    public void DuplicateVariablesAcrossPatternsAreDeduplicated()
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");

        TriplePattern p1 = new(
            PatternPosition.OfVariable(x),
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(2)));

        TriplePattern p2 = new(
            PatternPosition.Bound(TermId.FromEncoded(3)),
            PatternPosition.Bound(TermId.FromEncoded(4)),
            PatternPosition.OfVariable(x));

        BasicGraphPattern bgp = new([p1, p2], registry);

        Assert.HasCount(1, bgp.Variables);
        Assert.AreEqual(x, bgp.Variables[0]);
    }

    [TestMethod]
    public void FullyBoundPatternsHaveNoVariables()
    {
        VariableRegistry registry = new();
        TriplePattern pattern = new(
            PatternPosition.Bound(TermId.FromEncoded(1)),
            PatternPosition.Bound(TermId.FromEncoded(2)),
            PatternPosition.Bound(TermId.FromEncoded(3)));

        BasicGraphPattern bgp = new([pattern, pattern], registry);

        Assert.HasCount(2, bgp.Patterns);
        Assert.IsEmpty(bgp.Variables);
    }

    [TestMethod]
    public void ConstructorRejectsNullPatternsList()
    {
        VariableRegistry registry = new();

        Assert.Throws<ArgumentNullException>(() => new BasicGraphPattern(null!, registry));
    }

    [TestMethod]
    public void ConstructorRejectsNullRegistry()
    {
        Assert.Throws<ArgumentNullException>(() => new BasicGraphPattern([], null!));
    }
}
