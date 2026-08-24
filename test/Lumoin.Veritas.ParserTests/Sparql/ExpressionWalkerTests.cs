using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="ExpressionWalker"/>'s structural equality and hashing: equal expressions hash
/// equal (the contract), and the leaf nodes that fold an <c>Inner</c> term into equality
/// (<see cref="TripleTermExpression"/>, <see cref="ExistsExpression"/>, <see cref="NotExistsExpression"/>)
/// also fold it into the hash, so two such nodes differing only in <c>Inner</c> do not collide.
/// </summary>
[TestClass]
internal sealed class ExpressionWalkerTests
{
    /// <summary>Two structurally equal triple-term expressions hash equal.</summary>
    [TestMethod]
    public void EqualTripleTermExpressionsHashEqual()
    {
        TripleTermExpression a = new(SourceSpan.None, Pattern("o"));
        TripleTermExpression b = new(SourceSpan.None, Pattern("o"));

        Assert.IsTrue(ExpressionWalker.StructurallyEqual(a, b));
        Assert.AreEqual(ExpressionWalker.StructuralHashCode(a), ExpressionWalker.StructuralHashCode(b));
    }

    /// <summary>Triple-term expressions differing only in their inner triple are unequal and do not share a hash (the inner term is folded into the hash, mirroring equality).</summary>
    [TestMethod]
    public void TripleTermExpressionsDifferingInInnerDoNotCollide()
    {
        TripleTermExpression a = new(SourceSpan.None, Pattern("first"));
        TripleTermExpression b = new(SourceSpan.None, Pattern("second"));

        Assert.IsFalse(ExpressionWalker.StructurallyEqual(a, b));
        Assert.AreNotEqual(ExpressionWalker.StructuralHashCode(a), ExpressionWalker.StructuralHashCode(b));
    }

    /// <summary>Builds a single-triple pattern <c>:s :p :{object}</c> for use as a triple-term expression's inner triple.</summary>
    /// <param name="objectLocal">The local part of the object IRI, varying the pattern.</param>
    /// <returns>The triple pattern.</returns>
    private static TriplePattern Pattern(string objectLocal)
    {
        return new TriplePattern(SourceSpan.None, Constant("s"), Constant("p"), Constant(objectLocal));
    }

    /// <summary>Builds a constant IRI term <c>http://e/{local}</c>.</summary>
    /// <param name="local">The local part of the IRI.</param>
    /// <returns>The constant term.</returns>
    private static ConstantTerm Constant(string local)
    {
        return new ConstantTerm(SourceSpan.None, new NamedNode(Utf8Strings.From("http://e/" + local)));
    }
}
