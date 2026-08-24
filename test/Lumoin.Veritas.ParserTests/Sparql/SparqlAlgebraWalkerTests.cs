using System;
using System.Collections.Generic;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="AlgebraWalker"/>: pre-order <see cref="AlgebraWalker.Traverse"/> enumeration and the
/// bottom-up <see cref="AlgebraWalker.Transform"/> rewrite (rebuild-on-change, reference-preservation of
/// untouched subtrees, and the order in which the rewrite sees operators).
/// </summary>
[TestClass]
internal sealed class SparqlAlgebraWalkerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Traverse visits an operator before its children, children in evaluation order.</summary>
    [TestMethod]
    public void TraverseYieldsPreOrder()
    {
        Bgp left = MakeBgp("a");
        Bgp right = MakeBgp("b");
        Join join = new(left, right);
        Project project = new(join, [new SparqlVariable(Utf8Strings.From("a"))]);

        List<AlgebraOperator> visited = new(AlgebraWalker.Traverse(project));

        Assert.HasCount(4, visited);
        Assert.AreSame(project, visited[0]);
        Assert.AreSame(join, visited[1]);
        Assert.AreSame(left, visited[2]);
        Assert.AreSame(right, visited[3]);
    }

    /// <summary>Traverse on a leaf yields just that operator.</summary>
    [TestMethod]
    public void TraverseLeafYieldsItself()
    {
        Bgp bgp = MakeBgp("a");

        List<AlgebraOperator> visited = new(AlgebraWalker.Traverse(bgp));

        Assert.HasCount(1, visited);
        Assert.AreSame(bgp, visited[0]);
    }

    /// <summary>An identity rewrite returns the very same tree instance — unchanged subtrees are never rebuilt.</summary>
    [TestMethod]
    public void TransformIdentityReturnsSameInstance()
    {
        Project project = new(new Join(MakeBgp("a"), MakeBgp("b")), [new SparqlVariable(Utf8Strings.From("a"))]);

        AlgebraOperator result = AlgebraWalker.Transform(project, static op => op);

        Assert.AreSame(project, result);
    }

    /// <summary>Replacing one leaf rebuilds its ancestors but preserves the untouched sibling by reference.</summary>
    [TestMethod]
    public void TransformReplacesLeafAndRebuildsAncestors()
    {
        Bgp left = MakeBgp("a");
        Bgp right = MakeBgp("b");
        Join join = new(left, right);
        Bgp replacement = MakeBgp("c");

        AlgebraOperator result = AlgebraWalker.Transform(join, op => ReferenceEquals(op, left) ? replacement : op);

        Join rebuilt = Assert.IsInstanceOfType<Join>(result);
        Assert.AreNotSame(join, rebuilt);
        Assert.AreSame(replacement, rebuilt.Left);
        Assert.AreSame(right, rebuilt.Right);
    }

    /// <summary>The rewrite sees each operator after its children (bottom-up order).</summary>
    [TestMethod]
    public void TransformAppliesBottomUp()
    {
        Bgp bgp = MakeBgp("a");
        Project project = new(bgp, [new SparqlVariable(Utf8Strings.From("a"))]);

        List<AlgebraOperator> order = [];
        AlgebraOperator result = AlgebraWalker.Transform(
            project,
            op =>
            {
                order.Add(op);

                return op;
            });

        Assert.AreSame(project, result);
        Assert.HasCount(2, order);
        Assert.AreSame(bgp, order[0]);
        Assert.AreSame(project, order[1]);
    }

    /// <summary>A binary operator is rebuilt with both transformed children when both change.</summary>
    [TestMethod]
    public void TransformRebuildsBinaryOperatorWithBothChildren()
    {
        Bgp left = MakeBgp("a");
        Bgp right = MakeBgp("b");
        Union union = new(left, right);
        Bgp newLeft = MakeBgp("c");
        Bgp newRight = MakeBgp("d");

        AlgebraOperator result = AlgebraWalker.Transform(
            union,
            op =>
            {
                if(ReferenceEquals(op, left))
                {
                    return newLeft;
                }

                return ReferenceEquals(op, right) ? newRight : op;
            });

        Union rebuilt = Assert.IsInstanceOfType<Union>(result);
        Assert.AreSame(newLeft, rebuilt.Left);
        Assert.AreSame(newRight, rebuilt.Right);
    }

    /// <summary>Tree semantics: two value-equal sibling subtrees are distinct positions and are both visited (no dedupe).</summary>
    [TestMethod]
    public void TraverseVisitsValueEqualSiblingsSeparately()
    {
        UnitTable first = new();
        UnitTable second = new();
        Assert.AreEqual(first, second, "Two UnitTables are value-equal (the record has no fields).");
        Assert.AreNotSame(first, second, "...but they are distinct instances.");
        Union union = new(first, second);

        List<AlgebraOperator> visited = new(AlgebraWalker.Traverse(union));

        //A deduping graph walk would yield 2 nodes; the plan walk keeps both positions, so 3.
        Assert.HasCount(3, visited);
        Assert.AreSame(union, visited[0]);
        Assert.AreSame(first, visited[1]);
        Assert.AreSame(second, visited[2]);
    }

    /// <summary>A tree far deeper than the call stack tolerates is traversed and transformed iteratively, without overflowing.</summary>
    [TestMethod]
    public void TraverseAndTransformHandleDeepTreesWithoutRecursion()
    {
        const int depth = 50_000;
        AlgebraOperator tree = MakeBgp("leaf");
        for(int i = 0; i < depth; i++)
        {
            tree = new Join(tree, MakeBgp("r"));
        }

        int count = 0;
        foreach(AlgebraOperator _ in AlgebraWalker.Traverse(tree))
        {
            count++;
        }

        //depth Join operators + depth right-hand BGP leaves + the one initial leaf.
        Assert.AreEqual((depth * 2) + 1, count);

        AlgebraOperator same = AlgebraWalker.Transform(tree, static op => op);
        Assert.AreSame(tree, same);
    }

    /// <summary>Builds a single-triple BGP whose subject variable carries the given name (a distinct instance per call).</summary>
    /// <param name="subjectName">The subject variable name.</param>
    /// <returns>The BGP.</returns>
    private static Bgp MakeBgp(string subjectName)
    {
        return new([new TriplePattern(default, Var(subjectName), Var("p"), Var("o"))]);
    }

    /// <summary>Builds a variable term with the given name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable term.</returns>
    private static VariableTerm Var(string name)
    {
        return new VariableTerm(default, new SparqlVariable(Utf8Strings.From(name)));
    }
}
