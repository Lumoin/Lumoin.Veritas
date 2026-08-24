using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verifies <see cref="TripleTerm"/>'s hand-written iterative equality, hash code, and rendering: they preserve
/// structural-equality semantics, stay hash-consistent, render the <c>&lt;&lt;s p o&gt;&gt;</c> form, and — unlike
/// the compiler-synthesized recursive members they replace — handle a deeply-nested term without overflowing the
/// call stack (the path that let interning a deep quoted triple overflow on ingest).
/// </summary>
[TestClass]
internal sealed class TripleTermEqualityTests
{
    private static NamedNode Iri(string local) => new(Utf8Strings.From("http://example/" + local));

    /// <summary>Distinct instances of a structurally-identical triple are equal and share a hash code.</summary>
    [TestMethod]
    public void StructurallyEqualTriplesAreEqualWithSameHash()
    {
        TripleTerm a = new(Iri("s"), Iri("p"), Iri("o"));
        TripleTerm b = new(Iri("s"), Iri("p"), Iri("o"));

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>A nested quoted triple compares structurally through its full spine.</summary>
    [TestMethod]
    public void NestedTriplesCompareStructurally()
    {
        TripleTerm a = new(new TripleTerm(Iri("s"), Iri("p"), Iri("o")), Iri("p2"), Iri("o2"));
        TripleTerm b = new(new TripleTerm(Iri("s"), Iri("p"), Iri("o")), Iri("p2"), Iri("o2"));

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Triples differing in any component — including a deeply-nested one — are unequal.</summary>
    [TestMethod]
    public void TriplesDifferingInAnyComponentAreNotEqual()
    {
        TripleTerm baseline = new(Iri("s"), Iri("p"), Iri("o"));

        Assert.AreNotEqual(baseline, new TripleTerm(Iri("S"), Iri("p"), Iri("o")));
        Assert.AreNotEqual(baseline, new TripleTerm(Iri("s"), Iri("P"), Iri("o")));
        Assert.AreNotEqual(baseline, new TripleTerm(Iri("s"), Iri("p"), Iri("O")));

        TripleTerm nestedA = new(new TripleTerm(Iri("s"), Iri("p"), Iri("o")), Iri("p2"), Iri("o2"));
        TripleTerm nestedB = new(new TripleTerm(Iri("s"), Iri("p"), Iri("x")), Iri("p2"), Iri("o2"));
        Assert.AreNotEqual(nestedA, nestedB);
    }

    /// <summary>A quoted triple is never equal to a leaf term, in either component position.</summary>
    [TestMethod]
    public void TripleIsNotEqualToALeaf()
    {
        TripleTerm triple = new(Iri("s"), Iri("p"), Iri("o"));

        Assert.AreNotEqual<RdfTerm>(triple, Iri("s"));
        Assert.AreNotEqual(triple, new TripleTerm(triple, Iri("p"), Iri("o")));
    }

    /// <summary>Rendering nests the <c>&lt;&lt;s p o&gt;&gt;</c> form with leaf terms in their own notation.</summary>
    [TestMethod]
    public void RendersNestedAngleBracketForm()
    {
        TripleTerm term = new(new TripleTerm(Iri("s"), Iri("p"), Iri("o")), Iri("p2"), Iri("o2"));

        Assert.AreEqual(
            "<<<<<http://example/s> <http://example/p> <http://example/o>>> <http://example/p2> <http://example/o2>>>",
            term.ToString());
    }

    /// <summary>
    /// A quoted triple nested far beyond the call-stack limit can be compared, hashed, rendered, and used as a
    /// dictionary key without overflowing — the synthesized recursive members would have thrown a stack overflow.
    /// </summary>
    [TestMethod]
    public void DeeplyNestedTripleDoesNotOverflow()
    {
        const int Depth = 50_000;
        RdfTerm a = NestSubject(Depth);
        RdfTerm b = NestSubject(Depth);

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsGreaterThan(0, a.ToString().Length);

        //The TermDictionary ingest path: keying a dictionary by a deep term exercises GetHashCode then Equals.
        Dictionary<RdfTerm, int> dictionary = new() { [a] = 1 };
        Assert.IsTrue(dictionary.ContainsKey(b));
    }

    /// <summary>Builds a quoted triple nested <paramref name="depth"/> levels deep through the subject.</summary>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term.</returns>
    private static RdfTerm NestSubject(int depth)
    {
        NamedNode predicate = Iri("p");
        RdfTerm leaf = Iri("o");

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }
}
