using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.Tests.Geo.GeoFunctionCalls;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The region-connection composition calculus: the relation-set operations, the converse map, the
/// composition table's internal laws (identity, converse symmetry, the pinned singleton roster), the
/// geometric certification of the table against the computed topological predicates, and the closure's
/// derivation behaviour — exact converses, singleton-cell materialization, silence on disjunctive cells,
/// fixpoint over chains, input exclusion, deterministic emission, and the consistency verdict.
/// </summary>
[TestClass]
internal sealed class Rcc8CompositionTests
{
    /// <summary>The example-namespace prefix of the closure tests' terms.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The eight base relations in enum order.</summary>
    private static Rcc8Relation[] AllRelations { get; } =
    [
        Rcc8Relation.Dc, Rcc8Relation.Ec, Rcc8Relation.Po, Rcc8Relation.Tpp,
        Rcc8Relation.Ntpp, Rcc8Relation.Tppi, Rcc8Relation.Ntppi, Rcc8Relation.Eq,
    ];

    /// <summary>The predicate catalog entry deciding each base relation, indexed by the relation's numeric value.</summary>
    private static SparqlFunctionEntry[] PredicatesByRelation { get; } =
    [
        GeoFunctions.Rcc8Dc, GeoFunctions.Rcc8Ec, GeoFunctions.Rcc8Po, GeoFunctions.Rcc8Tpp,
        GeoFunctions.Rcc8Ntpp, GeoFunctions.Rcc8Tppi, GeoFunctions.Rcc8Ntppi, GeoFunctions.Rcc8Eq,
    ];

    /// <summary>
    /// The witness-region family: rectangles standing in all eight base relations to the first member (the
    /// middle region), so the ordered triples through the middle exercise every composition-table cell.
    /// </summary>
    private static string[] Regions { get; } =
    [
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
        "POLYGON ((20 20, 30 20, 30 30, 20 30, 20 20))",
        "POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))",
        "POLYGON ((5 0, 15 0, 15 10, 5 10, 5 0))",
        "POLYGON ((0 0, 5 0, 5 5, 0 5, 0 0))",
        "POLYGON ((2 2, 8 2, 8 8, 2 8, 2 2))",
        "POLYGON ((0 0, 20 0, 20 10, 0 10, 0 0))",
        "POLYGON ((-5 -5, 15 -5, 15 15, -5 15, -5 -5))",
    ];

    /// <summary>The set operations answer membership, count, extension, and the singleton read.</summary>
    [TestMethod]
    public void RelationSetOperationsAnswerMembershipAndSingletons()
    {
        Assert.AreEqual(0, Rcc8RelationSet.Empty.Count);
        Assert.AreEqual(8, Rcc8RelationSet.All.Count);
        Assert.IsFalse(Rcc8RelationSet.Empty.TryGetSingleton(out _), "The empty set has no sole member.");
        Assert.IsFalse(Rcc8RelationSet.All.TryGetSingleton(out _), "The full set has no sole member.");

        Rcc8RelationSet one = Rcc8RelationSet.Empty.With(Rcc8Relation.Tpp);
        Assert.AreEqual(1, one.Count);
        Assert.IsTrue(one.Contains(Rcc8Relation.Tpp));
        Assert.IsFalse(one.Contains(Rcc8Relation.Tppi));
        Assert.IsTrue(one.TryGetSingleton(out Rcc8Relation sole));
        Assert.AreEqual(Rcc8Relation.Tpp, sole);

        Rcc8RelationSet two = one.With(Rcc8Relation.Ntpp);
        Assert.AreEqual(2, two.Count);
        Assert.IsFalse(two.TryGetSingleton(out _), "A two-member set has no sole member.");
        Assert.AreEqual(two, two.With(Rcc8Relation.Ntpp), "Adding a member twice is idempotent.");
    }

    /// <summary>The converse map is an involution: the proper-part members pair with their inverses and the symmetric members are self-converse.</summary>
    [TestMethod]
    public void ConverseIsAnInvolutionPairingTheProperPartFamilies()
    {
        Assert.AreEqual(Rcc8Relation.Dc, Rcc8Composition.Converse(Rcc8Relation.Dc));
        Assert.AreEqual(Rcc8Relation.Ec, Rcc8Composition.Converse(Rcc8Relation.Ec));
        Assert.AreEqual(Rcc8Relation.Po, Rcc8Composition.Converse(Rcc8Relation.Po));
        Assert.AreEqual(Rcc8Relation.Eq, Rcc8Composition.Converse(Rcc8Relation.Eq));
        Assert.AreEqual(Rcc8Relation.Tppi, Rcc8Composition.Converse(Rcc8Relation.Tpp));
        Assert.AreEqual(Rcc8Relation.Tpp, Rcc8Composition.Converse(Rcc8Relation.Tppi));
        Assert.AreEqual(Rcc8Relation.Ntppi, Rcc8Composition.Converse(Rcc8Relation.Ntpp));
        Assert.AreEqual(Rcc8Relation.Ntpp, Rcc8Composition.Converse(Rcc8Relation.Ntppi));

        foreach(Rcc8Relation relation in AllRelations)
        {
            Assert.AreEqual(relation, Rcc8Composition.Converse(Rcc8Composition.Converse(relation)), $"{relation}: the converse is an involution.");
        }
    }

    /// <summary>Composition with equality is the identity on both sides.</summary>
    [TestMethod]
    public void CompositionWithEqualityIsTheIdentity()
    {
        foreach(Rcc8Relation relation in AllRelations)
        {
            Rcc8RelationSet expected = Rcc8RelationSet.Empty.With(relation);
            Assert.AreEqual(expected, Rcc8Composition.Compose(Rcc8Relation.Eq, relation), $"eq ∘ {relation} is the identity.");
            Assert.AreEqual(expected, Rcc8Composition.Compose(relation, Rcc8Relation.Eq), $"{relation} ∘ eq is the identity.");
        }
    }

    /// <summary>No composition cell is empty: two relations through a shared middle always admit some outcome.</summary>
    [TestMethod]
    public void EveryCompositionCellIsNonEmpty()
    {
        foreach(Rcc8Relation first in AllRelations)
        {
            foreach(Rcc8Relation second in AllRelations)
            {
                Assert.IsGreaterThan(0, Rcc8Composition.Compose(first, second).Count, $"{first} ∘ {second}: a cell is never empty.");
            }
        }
    }

    /// <summary>Every cell satisfies the converse-symmetry law: <c>comp(r1, r2) = conv(comp(conv(r2), conv(r1)))</c>.</summary>
    [TestMethod]
    public void CompositionObeysTheConverseSymmetryLaw()
    {
        foreach(Rcc8Relation first in AllRelations)
        {
            foreach(Rcc8Relation second in AllRelations)
            {
                Rcc8RelationSet direct = Rcc8Composition.Compose(first, second);
                Rcc8RelationSet mirrored = Rcc8Composition.Converse(
                    Rcc8Composition.Compose(Rcc8Composition.Converse(second), Rcc8Composition.Converse(first)));

                Assert.AreEqual(direct, mirrored, $"{first} ∘ {second}: the converse-symmetry law holds.");
            }
        }
    }

    /// <summary>
    /// The singleton cells — the only cells the closure materializes from — are exactly the pinned
    /// twenty-seven: the equality row and column, the disconnection singletons, and the proper-part
    /// transitivity singletons of both families.
    /// </summary>
    [TestMethod]
    public void TheSingletonCellRosterIsExactlyThePinnedTwentySeven()
    {
        List<(Rcc8Relation First, Rcc8Relation Second, Rcc8Relation Result)> pinned =
        [
            (Rcc8Relation.Dc, Rcc8Relation.Tppi, Rcc8Relation.Dc),
            (Rcc8Relation.Dc, Rcc8Relation.Ntppi, Rcc8Relation.Dc),
            (Rcc8Relation.Dc, Rcc8Relation.Eq, Rcc8Relation.Dc),
            (Rcc8Relation.Ec, Rcc8Relation.Ntppi, Rcc8Relation.Dc),
            (Rcc8Relation.Ec, Rcc8Relation.Eq, Rcc8Relation.Ec),
            (Rcc8Relation.Po, Rcc8Relation.Eq, Rcc8Relation.Po),
            (Rcc8Relation.Tpp, Rcc8Relation.Dc, Rcc8Relation.Dc),
            (Rcc8Relation.Tpp, Rcc8Relation.Ntpp, Rcc8Relation.Ntpp),
            (Rcc8Relation.Tpp, Rcc8Relation.Eq, Rcc8Relation.Tpp),
            (Rcc8Relation.Ntpp, Rcc8Relation.Dc, Rcc8Relation.Dc),
            (Rcc8Relation.Ntpp, Rcc8Relation.Ec, Rcc8Relation.Dc),
            (Rcc8Relation.Ntpp, Rcc8Relation.Tpp, Rcc8Relation.Ntpp),
            (Rcc8Relation.Ntpp, Rcc8Relation.Ntpp, Rcc8Relation.Ntpp),
            (Rcc8Relation.Ntpp, Rcc8Relation.Eq, Rcc8Relation.Ntpp),
            (Rcc8Relation.Tppi, Rcc8Relation.Ntppi, Rcc8Relation.Ntppi),
            (Rcc8Relation.Tppi, Rcc8Relation.Eq, Rcc8Relation.Tppi),
            (Rcc8Relation.Ntppi, Rcc8Relation.Tppi, Rcc8Relation.Ntppi),
            (Rcc8Relation.Ntppi, Rcc8Relation.Ntppi, Rcc8Relation.Ntppi),
            (Rcc8Relation.Ntppi, Rcc8Relation.Eq, Rcc8Relation.Ntppi),
            (Rcc8Relation.Eq, Rcc8Relation.Dc, Rcc8Relation.Dc),
            (Rcc8Relation.Eq, Rcc8Relation.Ec, Rcc8Relation.Ec),
            (Rcc8Relation.Eq, Rcc8Relation.Po, Rcc8Relation.Po),
            (Rcc8Relation.Eq, Rcc8Relation.Tpp, Rcc8Relation.Tpp),
            (Rcc8Relation.Eq, Rcc8Relation.Ntpp, Rcc8Relation.Ntpp),
            (Rcc8Relation.Eq, Rcc8Relation.Tppi, Rcc8Relation.Tppi),
            (Rcc8Relation.Eq, Rcc8Relation.Ntppi, Rcc8Relation.Ntppi),
            (Rcc8Relation.Eq, Rcc8Relation.Eq, Rcc8Relation.Eq),
        ];

        Assert.HasCount(27, pinned, "The pinned roster is the full twenty-seven.");

        HashSet<(Rcc8Relation, Rcc8Relation)> pinnedCells = [];
        foreach((Rcc8Relation first, Rcc8Relation second, Rcc8Relation result) in pinned)
        {
            Assert.IsTrue(pinnedCells.Add((first, second)), $"{first} ∘ {second}: each pinned cell appears once.");
            Assert.IsTrue(Rcc8Composition.Compose(first, second).TryGetSingleton(out Rcc8Relation sole), $"{first} ∘ {second}: the pinned cell is a singleton.");
            Assert.AreEqual(result, sole, $"{first} ∘ {second}: the pinned singleton member.");
        }

        foreach(Rcc8Relation first in AllRelations)
        {
            foreach(Rcc8Relation second in AllRelations)
            {
                if(Rcc8Composition.Compose(first, second).TryGetSingleton(out _))
                {
                    Assert.Contains((first, second), pinnedCells, $"{first} ∘ {second}: every singleton cell is pinned.");
                }
            }
        }
    }

    /// <summary>
    /// The geometric certification: over the witness-region family, every ordered pair answers exactly one
    /// base relation through the computed predicates (joint exhaustiveness and pairwise disjointness), every
    /// ordered triple's outcome relation is a member of its composition cell (table soundness), and the
    /// triples exercise all sixty-four cells (nothing certified by omission).
    /// </summary>
    [TestMethod]
    public void CompositionSoundnessHoldsOverTheRectangleFamilySweep()
    {
        int count = Regions.Length;
        Rcc8Relation[][] holds = new Rcc8Relation[count][];
        for(int i = 0; i < count; i++)
        {
            holds[i] = new Rcc8Relation[count];
            for(int j = 0; j < count; j++)
            {
                holds[i][j] = RelationOf(Regions[i], Regions[j]);
            }
        }

        HashSet<(Rcc8Relation, Rcc8Relation)> exercised = [];
        for(int i = 0; i < count; i++)
        {
            for(int j = 0; j < count; j++)
            {
                for(int k = 0; k < count; k++)
                {
                    Rcc8Relation first = holds[i][j];
                    Rcc8Relation second = holds[j][k];
                    Rcc8Relation outcome = holds[i][k];
                    exercised.Add((first, second));

                    Assert.IsTrue(
                        Rcc8Composition.Compose(first, second).Contains(outcome),
                        $"Regions {i}, {j}, {k}: the computed outcome {outcome} is a member of {first} ∘ {second}.");
                }
            }
        }

        Assert.HasCount(64, exercised, "The witness family exercises every composition cell.");
    }

    /// <summary>A single assertion derives exactly its converse.</summary>
    [TestMethod]
    public void DeriveEmitsTheExactConverse()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive([Assertion("a", Rcc8Relation.Tpp, "b")], derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(1, report.DerivedCount);
        Assert.ContainsSingle(derived);
        Assert.AreEqual(Assertion("b", Rcc8Relation.Tppi, "a"), derived[0]);
    }

    /// <summary>A tangential part of a non-tangential part composes through the singleton cell: the closure materializes the composed relation and its converse beside the premise converses.</summary>
    [TestMethod]
    public void DeriveMaterializesTheSingletonChain()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [Assertion("a", Rcc8Relation.Tpp, "b"), Assertion("b", Rcc8Relation.Ntpp, "c")],
            derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(4, report.DerivedCount);
        Assert.Contains(Assertion("b", Rcc8Relation.Tppi, "a"), derived);
        Assert.Contains(Assertion("c", Rcc8Relation.Ntppi, "b"), derived);
        Assert.Contains(Assertion("a", Rcc8Relation.Ntpp, "c"), derived);
        Assert.Contains(Assertion("c", Rcc8Relation.Ntppi, "a"), derived);
    }

    /// <summary>A disjunctive composition cell derives nothing between the chain's endpoints: two tangential proper parts in sequence leave the endpoint relation open, so only the premise converses appear.</summary>
    [TestMethod]
    public void DeriveStaysSilentOnADisjunctiveCell()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [Assertion("a", Rcc8Relation.Tpp, "b"), Assertion("b", Rcc8Relation.Tpp, "c")],
            derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(2, report.DerivedCount);
        Assert.Contains(Assertion("b", Rcc8Relation.Tppi, "a"), derived);
        Assert.Contains(Assertion("c", Rcc8Relation.Tppi, "b"), derived);
    }

    /// <summary>An equality premise propagates the other premise across the identity cells, and the equality pair's own converse composition derives the reflexive equalities.</summary>
    [TestMethod]
    public void DeriveWithEqualityPropagatesTheRelation()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [Assertion("a", Rcc8Relation.Eq, "b"), Assertion("b", Rcc8Relation.Tpp, "c")],
            derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(6, report.DerivedCount);
        Assert.Contains(Assertion("b", Rcc8Relation.Eq, "a"), derived);
        Assert.Contains(Assertion("c", Rcc8Relation.Tppi, "b"), derived);
        Assert.Contains(Assertion("a", Rcc8Relation.Tpp, "c"), derived);
        Assert.Contains(Assertion("c", Rcc8Relation.Tppi, "a"), derived);
        Assert.Contains(Assertion("a", Rcc8Relation.Eq, "a"), derived);
        Assert.Contains(Assertion("b", Rcc8Relation.Eq, "b"), derived);
    }

    /// <summary>The closure reaches the fixpoint over a four-link non-tangential chain: every forward pair and every converse materializes.</summary>
    [TestMethod]
    public void DeriveReachesTheFixpointOverTheChain()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [
                Assertion("a1", Rcc8Relation.Ntpp, "a2"),
                Assertion("a2", Rcc8Relation.Ntpp, "a3"),
                Assertion("a3", Rcc8Relation.Ntpp, "a4"),
                Assertion("a4", Rcc8Relation.Ntpp, "a5"),
            ],
            derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(16, report.DerivedCount, "Six composed forward pairs plus ten converses.");
        Assert.Contains(Assertion("a1", Rcc8Relation.Ntpp, "a5"), derived);
        Assert.Contains(Assertion("a2", Rcc8Relation.Ntpp, "a4"), derived);
        Assert.Contains(Assertion("a5", Rcc8Relation.Ntppi, "a1"), derived);
        Assert.Contains(Assertion("a2", Rcc8Relation.Ntppi, "a1"), derived);
    }

    /// <summary>An assertion present in the input — here a converse the input already carries — is never re-emitted.</summary>
    [TestMethod]
    public void DeriveExcludesInputAssertionsFromTheOutput()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [Assertion("a", Rcc8Relation.Tpp, "b"), Assertion("b", Rcc8Relation.Tppi, "a")],
            derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(0, report.DerivedCount);
        Assert.IsEmpty(derived);
    }

    /// <summary>An equality cycle closes without divergence: the missing converses, the composed pair, and the reflexive equalities materialize, and nothing else.</summary>
    [TestMethod]
    public void DeriveHandlesTheEqualityCycleWithoutDivergence()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [
                Assertion("a", Rcc8Relation.Eq, "b"),
                Assertion("b", Rcc8Relation.Eq, "c"),
                Assertion("c", Rcc8Relation.Eq, "a"),
            ],
            derived);

        Assert.IsTrue(report.Consistent);
        Assert.AreEqual(6, report.DerivedCount, "Three converses plus the three reflexive equalities.");
        Assert.Contains(Assertion("b", Rcc8Relation.Eq, "a"), derived);
        Assert.Contains(Assertion("a", Rcc8Relation.Eq, "c"), derived);
        Assert.Contains(Assertion("a", Rcc8Relation.Eq, "a"), derived);
        Assert.Contains(Assertion("c", Rcc8Relation.Eq, "c"), derived);
    }

    /// <summary>Two distinct base relations asserted over one ordered pair violate pairwise disjointness: the run completes and reports the inconsistency.</summary>
    [TestMethod]
    public void DeriveReportsInconsistentInput()
    {
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(
            [Assertion("a", Rcc8Relation.Dc, "b"), Assertion("a", Rcc8Relation.Po, "b")],
            derived);

        Assert.IsFalse(report.Consistent, "One ordered pair carrying two base relations is inconsistent.");
        Assert.AreEqual(2, report.DerivedCount, "The two converses still derive.");
    }

    /// <summary>Builds a closure assertion over example-namespace terms.</summary>
    /// <param name="subject">The subject's local name.</param>
    /// <param name="relation">The base relation.</param>
    /// <param name="object">The object's local name.</param>
    /// <returns>The assertion.</returns>
    private static Rcc8Assertion Assertion(string subject, Rcc8Relation relation, string @object)
    {
        return new Rcc8Assertion(new NamedNode(Utf8Strings.From(Ex + subject)), relation, new NamedNode(Utf8Strings.From(Ex + @object)));
    }

    /// <summary>
    /// Determines the base relation between two regions through the computed predicate catalog, asserting
    /// joint exhaustiveness and pairwise disjointness on the way: exactly one predicate answers true.
    /// </summary>
    /// <param name="first">The first region's WKT.</param>
    /// <param name="second">The second region's WKT.</param>
    /// <returns>The sole holding relation.</returns>
    private static Rcc8Relation RelationOf(string first, string second)
    {
        Rcc8Relation found = default;
        int trueCount = 0;
        for(int i = 0; i < PredicatesByRelation.Length; i++)
        {
            SparqlFunctionResult result = Invoke(PredicatesByRelation[i], Wkt(first), Wkt(second));

            Assert.IsFalse(result.IsError, $"{PredicatesByRelation[i].FunctionIri}: the pair always evaluates.");
            Assert.IsInstanceOfType<Literal>(result.Term);
            if(((Literal)result.Term).Value.ToString() == "true")
            {
                found = (Rcc8Relation)i;
                trueCount++;
            }
        }

        Assert.AreEqual(1, trueCount, $"Exactly one base relation holds between '{first}' and '{second}'.");

        return found;
    }
}
