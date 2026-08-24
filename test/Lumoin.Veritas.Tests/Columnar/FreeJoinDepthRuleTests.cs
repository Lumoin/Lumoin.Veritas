using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The Free Join depth rule and the per-key group statistics it reads: a connected run of two or
/// more tail-bearing relations extends a relation through its private tail when that relation's
/// heaviest cover key multiplied by the run's tail-bearing count clears the product bar; a
/// connected run holding one tail-bearing relation extends it when its degree-weighted mean key
/// fan clears the single-tail bar; a disconnected run extends a tail-bearing relation when that
/// mean clears the lower disconnected bar. The reader half is the mean matches per key value, the
/// largest, and the degree-weighted mean, all located through one search over one materialised
/// order and all confined to the view's own level-0 slice. Every expected depth vector and every
/// expected statistic is computed by hand from the fixture the row builds.
/// </summary>
[TestClass]
internal sealed class FreeJoinDepthRuleTests
{
    /// <summary>The first star arm's predicate.</summary>
    private const uint ArmZero = 200;

    /// <summary>The second star arm's predicate.</summary>
    private const uint ArmOne = 201;

    /// <summary>The third star arm's predicate.</summary>
    private const uint ArmTwo = 202;

    /// <summary>The chain's first hop predicate.</summary>
    private const uint HopOne = 210;

    /// <summary>The chain's second hop predicate.</summary>
    private const uint HopTwo = 211;

    /// <summary>The chain's third hop predicate.</summary>
    private const uint HopThree = 212;

    /// <summary>The predicate the reader rows measure a key group on.</summary>
    private const uint Measured = 300;

    /// <summary>A neighbouring predicate whose heavier group must not leak into the measured one's reading.</summary>
    private const uint Neighbour = 301;

    /// <summary>The predicate a cyclic fixture's edges carry.</summary>
    private const uint Edge = 320;

    /// <summary>The first component's first hop predicate in the two-component fixtures.</summary>
    private const uint LeftHopOne = 400;

    /// <summary>The first component's second hop predicate — the tail-bearing relation's predicate in that component.</summary>
    private const uint LeftHopTwo = 401;

    /// <summary>The second component's first hop predicate in the two-component fixtures.</summary>
    private const uint RightHopOne = 402;

    /// <summary>The second component's second hop predicate — the tail-bearing relation's predicate in that component.</summary>
    private const uint RightHopTwo = 403;

    /// <summary>The predicate a bare degree-sequence fixture carries.</summary>
    private const uint Sequence = 410;

    /// <summary>A predicate no fixture writes, so a pattern binding it matches nothing.</summary>
    private const uint Absent = 999;

    /// <summary>A star whose every arm concentrates the engagement fan on one key value builds every arm through its private tail, while the cover rule keeps all three at one level.</summary>
    [TestMethod]
    public void HighFanRelationsBuildFullDepthUnderTheEngagedRule()
    {
        //Three subjects with eight objects per arm each: every arm's heaviest key carries eight and all
        //three arms bear a private tail, so each arm's product is 8 x 3 = 24.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 3, fan: 8));
        BasicGraphPattern query = StarQuery(new VariableRegistry());
        int[] full = [2, 2, 2];
        int[] cover = [1, 1, 1];

        Assert.AreSequenceEqual(full, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.JoinCover));
    }

    /// <summary>A product below the boundary keeps every arm at its join-cover depth.</summary>
    [TestMethod]
    public void AProductBelowTheBoundaryKeepsJoinCoverDepth()
    {
        //Three objects per arm per subject over three tail-bearing arms: product 9, below the boundary,
        //where the cover depth is the measured winner.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 3, fan: 3));
        VariableRegistry registry = new();
        BasicGraphPattern query = StarQuery(registry);
        int[] cover = [1, 1, 1];

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[0], registry.GetOrAdd("k"), out int maximum));
        Assert.AreEqual(3, maximum);
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A run holding a single tail-bearing relation whose key concentrates past the single-tail bar extends that relation through its private tail.</summary>
    [TestMethod]
    public void AHeavySingleTailEngagesFullDepthAtTheWeightedFanBar()
    {
        //The tail relation's hub is its only key value and carries a hundred second-hop objects, so its
        //degree-weighted mean fan is a hundred — past the single-tail bar.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedChainFixture());
        VariableRegistry registry = new();
        BasicGraphPattern query = ChainQuery(registry);
        int[] engaged = [2, 2];
        int[] cover = [2, 1];

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, query.Patterns[1], registry.GetOrAdd("o"), out double weightedMean));
        Assert.AreEqual(100.0, weightedMean, 0.0001);
        Assert.AreSequenceEqual(engaged, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.JoinCover));
    }

    /// <summary>An arm the view exposes no statistic for keeps its join-cover depth while its readable siblings extend.</summary>
    [TestMethod]
    public void AnUnreadableStatisticKeepsJoinCoverDepth()
    {
        //The first arm binds no constant, so it carries three variables and no key group to read; the two
        //bound-predicate arms carry eight over three tail-bearing relations, a product of twenty-four.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 3, fan: 8));
        VariableRegistry registry = new();
        Variable key = registry.GetOrAdd("k");
        BasicGraphPattern query = new(
            [
                new TriplePattern(PatternPosition.OfVariable(key), PatternPosition.OfVariable(registry.GetOrAdd("p")), PatternPosition.OfVariable(registry.GetOrAdd("b0"))),
                EdgePattern(key, ArmOne, registry.GetOrAdd("b1")),
                EdgePattern(key, ArmTwo, registry.GetOrAdd("b2"))
            ],
            registry);
        int[] expected = [1, 2, 2];

        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[0], key, out int unreadable));
        Assert.AreEqual(0, unreadable);
        Assert.AreSequenceEqual(expected, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A cyclic core carries no private tail, so the arithmetic forecloses engagement even where the fan clears it.</summary>
    [TestMethod]
    public void ACyclicShapeIsUntouchedByArithmetic()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(DenseRingFixture());
        VariableRegistry registry = new();
        BasicGraphPattern query = TriangleQuery(registry);
        int[] expected = [2, 2, 2];

        //The fixture's heaviest key clears the product boundary at even the minimum engageable tail
        //multiplicity of two, so the equality below is the arithmetic foreclosure and not a product the
        //rule declines.
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[0], registry.GetOrAdd("x"), out int maximum));
        Assert.IsGreaterThanOrEqualTo(FreeJoinPipeline.FullDepthEngageFanTailProduct, maximum * 2, "The cyclic fixture must clear the product boundary for this row to pin the arithmetic.");
        Assert.AreSequenceEqual(expected, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
        Assert.AreSequenceEqual(expected, DepthsUnder(index, query, FreeJoinDepthRule.JoinCover));
    }

    /// <summary>Engagement is decided per relation: an arm clearing the product extends while its low-fan siblings keep cover.</summary>
    [TestMethod]
    public void EngagementIsPerRelationNotPerQuery()
    {
        //Ten objects on the first arm and three on each of the others over three tail-bearing relations,
        //so the arms' products are thirty, nine, and nine — exactly one clears the boundary.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(MixedStarFixture());
        BasicGraphPattern query = StarQuery(new VariableRegistry());
        int[] expected = [2, 1, 1];

        Assert.AreSequenceEqual(expected, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A product exactly at the boundary engages: the boundary is inclusive.</summary>
    [TestMethod]
    public void AProductAtTheBoundaryEngagesFullDepthInclusive()
    {
        //Four objects per arm per subject over three tail-bearing arms: product exactly twelve.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 3, fan: 4));
        VariableRegistry registry = new();
        BasicGraphPattern query = StarQuery(registry);
        int[] engaged = [2, 2, 2];
        int[] cover = [1, 1, 1];

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[0], registry.GetOrAdd("k"), out int maximum));
        Assert.AreEqual(4, maximum);
        Assert.AreSequenceEqual(engaged, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.JoinCover));
    }

    /// <summary>Two tail-bearing relations are enough for the product branch: the connected gate needs no third tail.</summary>
    [TestMethod]
    public void ExactlyTwoTailBearingRelationsEngageByTheProduct()
    {
        //Six objects per arm per subject over exactly two tail-bearing arms: product exactly twelve.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(TwoArmStarFixture(subjects: 3, fan: 6));
        BasicGraphPattern query = TwoArmStarQuery(new VariableRegistry());
        int[] engaged = [2, 2];

        Assert.AreEqual(2, TailsOf(index, query));
        Assert.AreSequenceEqual(engaged, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>The product multiplies the tail-bearing relation count, not the query's pattern count.</summary>
    [TestMethod]
    public void TheProductCountsTailBearingRelationsNotPatterns()
    {
        //Three patterns, of which exactly two bear a tail, each tail relation's heaviest key carrying five:
        //the tails-product is ten and stays below the boundary, while a patterns-product would read fifteen
        //and engage.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(BranchedStarFixture());
        VariableRegistry registry = new();
        BasicGraphPattern query = BranchedStarQuery(registry);
        int[] cover = [1, 2, 1];

        Assert.HasCount(3, query.Patterns);
        Assert.AreEqual(2, TailsOf(index, query));
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[0], registry.GetOrAdd("k"), out int maximum));
        Assert.AreEqual(5, maximum);
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A single-tail relation whose heaviest key is heavy but whose weighted mean is light keeps its join-cover depth: the gate reads the mean, not the maximum.</summary>
    [TestMethod]
    public void AHighMaximumWithALightWeightedFanKeepsJoinCoverDepth()
    {
        //The tail relation carries one hub of a hundred beside nine hundred keys of one: the maximum is a
        //hundred, while the degree-weighted mean is (100^2 + 900) / (100 + 900) = 10.9.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(HeavyHubLightTailFixture());
        VariableRegistry registry = new();
        BasicGraphPattern query = ChainQuery(registry);
        int[] cover = [2, 1];

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[1], registry.GetOrAdd("o"), out int maximum));
        Assert.AreEqual(100, maximum);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, query.Patterns[1], registry.GetOrAdd("o"), out double weightedMean));
        Assert.AreEqual(10.9, weightedMean, 0.0001);
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A single-tail relation the view exposes no weighted fan for keeps its join-cover depth.</summary>
    [TestMethod]
    public void AnUnreadableWeightedFanKeepsJoinCoverDepth()
    {
        //The tail relation binds no constant, so it carries three variables and exposes no key group at all.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedChainFixture());
        VariableRegistry registry = new();
        Variable hub = registry.GetOrAdd("o");
        BasicGraphPattern query = new(
            [
                EdgePattern(registry.GetOrAdd("s"), HopOne, hub),
                new TriplePattern(PatternPosition.OfVariable(hub), PatternPosition.OfVariable(registry.GetOrAdd("p")), PatternPosition.OfVariable(registry.GetOrAdd("t")))
            ],
            registry);
        int[] cover = [2, 1];

        Assert.AreEqual(1, TailsOf(index, query));
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, query.Patterns[1], hub, out double unreadable));
        Assert.AreEqual(0.0, unreadable, 0.0001);
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A disconnected run extends a tail-bearing relation whose weighted fan clears the lower disconnected bar, and leaves its mild partner at cover.</summary>
    [TestMethod]
    public void ASkewedDisconnectedTailEngagesAtTheWeightedFanBar()
    {
        //Two components, one tail-bearing relation each. The skewed component's tail carries degrees ten,
        //ten, and four: its degree-weighted mean is 216 / 24 = 9.0, past the disconnected bar. The mild
        //component's tail carries ones, a mean of one.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedDisjointFixture());
        VariableRegistry registry = new();
        BasicGraphPattern query = DisjointChainsQuery(registry);
        int[] expected = [2, 2, 2, 1];

        Assert.AreEqual(2, ComponentsOf(query));
        Assert.AreEqual(2, TailsOf(index, query));
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, query.Patterns[1], registry.GetOrAdd("b"), out double weightedMean));
        Assert.AreEqual(9.0, weightedMean, 0.0001);
        Assert.AreSequenceEqual(expected, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>A disconnected run whose tail is mildly skewed keeps its join-cover depth even where the heaviest key alone would have engaged it.</summary>
    [TestMethod]
    public void AMildDisconnectedTailStaysAtJoinCover()
    {
        //Two components, one tail-bearing relation each — so a rule gated on the tail-bearing count alone
        //admits this shape. The skewed component's tail carries one key of eight beside eight keys of one:
        //the maximum is eight, while the degree-weighted mean is 72 / 16 = 4.5, below the disconnected bar.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(MildDisjointFixture());
        VariableRegistry registry = new();
        BasicGraphPattern query = DisjointChainsQuery(registry);
        int[] cover = [2, 1, 2, 1];

        Assert.AreEqual(2, ComponentsOf(query));
        Assert.AreEqual(2, TailsOf(index, query));
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, query.Patterns[1], registry.GetOrAdd("b"), out int maximum));
        Assert.AreEqual(8, maximum);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, query.Patterns[1], registry.GetOrAdd("b"), out double weightedMean));
        Assert.AreEqual(4.5, weightedMean, 0.0001);
        Assert.AreSequenceEqual(cover, DepthsUnder(index, query, FreeJoinDepthRule.FanOutEngaged));
    }

    /// <summary>The connected product boundary is twelve, pinned on the value the rule reads at runtime.</summary>
    [TestMethod]
    public void TheProductBoundaryConstantIsTwelve()
    {
        Assert.AreEqual(12, FreeJoinPipeline.FullDepthEngageFanTailProduct);
    }

    /// <summary>The connected single-tail weighted-fan bar is sixty-four, pinned on the value the rule reads at runtime.</summary>
    [TestMethod]
    public void TheSingleTailWeightedFanConstantIsSixtyFour()
    {
        Assert.AreEqual(64.0, FreeJoinPipeline.SingleTailEngageWeightedFan, 0.0001);
    }

    /// <summary>The disconnected weighted-fan bar is eight, pinned on the value the rule reads at runtime.</summary>
    [TestMethod]
    public void TheDisconnectedWeightedFanConstantIsEight()
    {
        Assert.AreEqual(8.0, FreeJoinPipeline.DisconnectedEngageWeightedFan, 0.0001);
    }

    /// <summary>The weighted fan is the sum of squared degrees over the sum of degrees of the pattern's own key group.</summary>
    [TestMethod]
    public void TheWeightedFanReadsItsOwnGroupsSquareOverSum()
    {
        //Degrees five, two, and one: the weighted mean is (25 + 4 + 1) / 8 = 3.75, a value distinct from the
        //maximum (5), from the arithmetic mean (8 / 3), and from the swapped ratio (8 / 30).
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern measured = EdgePattern(subject, Measured, registry.GetOrAdd("o"));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, measured, subject, out double weightedMean));
        Assert.AreEqual(3.75, weightedMean, 0.0001);
    }

    /// <summary>A pattern with no bound position exposes no key group, and the weighted fan declines it at every key position.</summary>
    [TestMethod]
    public void TheWeightedFanDeclinesTheShapesTheMaximumDeclines()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        Variable @object = registry.GetOrAdd("o");
        TriplePattern unbound = new(
            PatternPosition.OfVariable(subject),
            PatternPosition.OfVariable(registry.GetOrAdd("p")),
            PatternPosition.OfVariable(@object));

        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, unbound, subject, out double subjectWeighted));
        Assert.AreEqual(0.0, subjectWeighted, 0.0001);
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, unbound, subject, out int subjectMaximum));
        Assert.AreEqual(0, subjectMaximum);

        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, unbound, @object, out double objectWeighted));
        Assert.AreEqual(0.0, objectWeighted, 0.0001);
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, unbound, @object, out int objectMaximum));
        Assert.AreEqual(0, objectMaximum);
    }

    /// <summary>A bound prefix matching nothing reads a weighted fan of zero rather than a decline, so a real empty group stays distinguishable from an unreadable statistic.</summary>
    [TestMethod]
    public void TheWeightedFanReadsZeroForAnEmptyKeyGroup()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern empty = EdgePattern(subject, Absent, registry.GetOrAdd("o"));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, empty, subject, out double weightedMean));
        Assert.AreEqual(0.0, weightedMean, 0.0001);
    }

    /// <summary>A pattern binding the key as its only variable contributes each key value at most once, so its weighted fan is one.</summary>
    [TestMethod]
    public void TheWeightedFanOfASemijoinArmIsOne()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern semijoin = new(
            PatternPosition.OfVariable(subject),
            PatternPosition.Bound(TermId.FromEncoded(Measured)),
            PatternPosition.Bound(TermId.FromEncoded(2_000)));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, semijoin, subject, out double weightedMean));
        Assert.AreEqual(1.0, weightedMean, 0.0001);
    }

    /// <summary>The weighted fan and the maximum locate one group through one search, so they read the same order for each key position and decline the same patterns.</summary>
    [TestMethod]
    public void TheWeightedFanAndTheMaximumShareOneLocator()
    {
        //Five subjects with four distinct objects each: keyed on the subject the group is
        //objects-per-subject — five groups of four, a weighted mean of 80 / 20 = 4.0 beside a maximum of
        //four — while keyed on the object it is subjects-per-object, twenty groups of one, a weighted mean
        //of 20 / 20 = 1.0 beside a maximum of one. The two readings come from different permutations, so a
        //search that drifted would move one of them.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(RegularFixture(subjects: 5, fan: 4));
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        Variable @object = registry.GetOrAdd("o");
        Variable unbound = registry.GetOrAdd("z");
        TriplePattern measured = EdgePattern(subject, Measured, @object);

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, measured, subject, out double subjectWeighted));
        Assert.AreEqual(4.0, subjectWeighted, 0.0001);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, subject, out int subjectMaximum));
        Assert.AreEqual(4, subjectMaximum);

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, measured, @object, out double objectWeighted));
        Assert.AreEqual(1.0, objectWeighted, 0.0001);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, @object, out int objectMaximum));
        Assert.AreEqual(1, objectMaximum);

        //A key the pattern does not bind is unreadable, and both statistics say so.
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, measured, unbound, out double unboundWeighted));
        Assert.AreEqual(0.0, unboundWeighted, 0.0001);
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, unbound, out int unboundMaximum));
        Assert.AreEqual(0, unboundMaximum);
    }

    /// <summary>The shipped accumulator reads exactly the degree sequences the fitted bars were placed on, so the fit's x-coordinates are measured and not merely intended.</summary>
    [TestMethod]
    public void TheWeightedFanMatchesTheIntendedSequenceOnCensusMirroredHubs()
    {
        //A test-scale mirror of the heavy-head construction: ten hubs sharing a quarter of a thousand-edge
        //budget — twenty-five each — beside seven hundred and fifty keys of one. The weighted mean is
        //(10 x 625 + 750) / 1000 = 7.0.
        int[] heads = new int[760];
        for(int key = 0; key < heads.Length; key++)
        {
            heads[key] = key < 10 ? 25 : 1;
        }

        //The two disconnected sequences the disconnected rows are placed on: 216 / 24 = 9.0 and 72 / 16 = 4.5.
        int[] skewed = [10, 10, 4];
        int[] mild = [8, 1, 1, 1, 1, 1, 1, 1, 1];

        Assert.AreEqual(7.0, WeightedFanOfSequence(heads), 0.0001);
        Assert.AreEqual(9.0, WeightedFanOfSequence(skewed), 0.0001);
        Assert.AreEqual(4.5, WeightedFanOfSequence(mild), 0.0001);
    }

    /// <summary>The maximum is the heaviest child group of the pattern's own key group, and the mean over that same group is the shipped estimate.</summary>
    [TestMethod]
    public void MaximumKeyFanOutReadsTheHeaviestKeyOfItsOwnGroup()
    {
        //Degrees five, two, and one on the measured predicate — eight matches over three keys — beside a
        //neighbouring predicate whose single key carries nine.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern measured = EdgePattern(subject, Measured, registry.GetOrAdd("o"));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, subject, out int maximum));
        Assert.AreEqual(5, maximum);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, measured, subject, out double mean));
        Assert.AreEqual(8.0 / 3.0, mean, 0.0001);
    }

    /// <summary>A pattern with no bound position exposes no key group, and both statistics decline it.</summary>
    [TestMethod]
    public void MaximumKeyFanOutDeclinesTheShapesTheMeanDeclines()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        Variable @object = registry.GetOrAdd("o");
        TriplePattern unbound = new(
            PatternPosition.OfVariable(subject),
            PatternPosition.OfVariable(registry.GetOrAdd("p")),
            PatternPosition.OfVariable(@object));

        //Every key position is declined, so widening the readable set to admit this shape cannot pass
        //unnoticed at whichever position the key sits.
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, unbound, subject, out int subjectMaximum));
        Assert.AreEqual(0, subjectMaximum);
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, unbound, subject, out double subjectMean));
        Assert.AreEqual(0.0, subjectMean, 0.0001);

        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, unbound, @object, out int objectMaximum));
        Assert.AreEqual(0, objectMaximum);
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, unbound, @object, out double objectMean));
        Assert.AreEqual(0.0, objectMean, 0.0001);
    }

    /// <summary>A bound prefix matching nothing reads as a fan-out of zero rather than a decline, so a real zero stays distinguishable from an unreadable statistic.</summary>
    [TestMethod]
    public void MaximumKeyFanOutReadsZeroForAnEmptyKeyGroup()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern empty = EdgePattern(subject, Absent, registry.GetOrAdd("o"));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, empty, subject, out int maximum));
        Assert.AreEqual(0, maximum);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, empty, subject, out double mean));
        Assert.AreEqual(0.0, mean, 0.0001);
    }

    /// <summary>A pattern binding the key as its only variable contributes each key value at most once.</summary>
    [TestMethod]
    public void MaximumKeyFanOutOfASemijoinArmIsOne()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SkewedGroupFixture());
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern semijoin = new(
            PatternPosition.OfVariable(subject),
            PatternPosition.Bound(TermId.FromEncoded(Measured)),
            PatternPosition.Bound(TermId.FromEncoded(2_000)));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, semijoin, subject, out int maximum));
        Assert.AreEqual(1, maximum);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, semijoin, subject, out double mean));
        Assert.AreEqual(1.0, mean, 0.0001);
    }

    /// <summary>A graph view reads its own graph's key group rather than another graph's run in the shared column.</summary>
    [TestMethod]
    public void MaximumKeyFanOutReadsAGraphViewsOwnSlice()
    {
        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = new()
        {
            [TermId.FromEncoded(1)] = GraphRun(3),
            [TermId.FromEncoded(2)] = GraphRun(9)
        };
        ColumnarGraphSetIndex set = ColumnarGraphSetIndex.Build(graphs);

        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern measured = EdgePattern(subject, Measured, registry.GetOrAdd("o"));

        ColumnarTripleIndex? light = set.GetView(TermId.FromEncoded(1));
        ColumnarTripleIndex? heavy = set.GetView(TermId.FromEncoded(2));

        Assert.IsNotNull(light);
        Assert.IsNotNull(heavy);

        //Both graphs carry the same predicate, so a level-0 search spanning the shared column would read
        //the first run's group for both views; each view must read its own.
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(light, measured, subject, out int lightMaximum));
        Assert.AreEqual(3, lightMaximum);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(heavy, measured, subject, out int heavyMaximum));
        Assert.AreEqual(9, heavyMaximum);
    }

    /// <summary>The mean and the maximum locate one group through one search, so they read the same order for each key position and decline the same patterns.</summary>
    [TestMethod]
    public void TheMeanAndTheMaximumShareOneLocator()
    {
        //Five subjects with four distinct objects each: keyed on the subject the group is
        //objects-per-subject (four), keyed on the object it is subjects-per-object (one). The two
        //readings come from different permutations, so a search that drifted would move one of them.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(RegularFixture(subjects: 5, fan: 4));
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        Variable @object = registry.GetOrAdd("o");
        Variable unbound = registry.GetOrAdd("z");
        TriplePattern measured = EdgePattern(subject, Measured, @object);

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, measured, subject, out double subjectMean));
        Assert.AreEqual(4.0, subjectMean, 0.0001);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, subject, out int subjectMaximum));
        Assert.AreEqual(4, subjectMaximum);

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, measured, @object, out double objectMean));
        Assert.AreEqual(1.0, objectMean, 0.0001);
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, @object, out int objectMaximum));
        Assert.AreEqual(1, objectMaximum);

        //A key the pattern does not bind is unreadable, and both statistics say so.
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, measured, unbound, out double unboundMean));
        Assert.AreEqual(0.0, unboundMean, 0.0001);
        Assert.IsFalse(ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, measured, unbound, out int unboundMaximum));
        Assert.AreEqual(0, unboundMaximum);
    }

    /// <summary>The tail-bearing count is a count of relations over the join-cover plan: not every relation, and not the leaf columns they carry.</summary>
    [TestMethod]
    public void TheTailBearingCountReadsTheJoinCoverPlansTails()
    {
        ColumnarTripleIndex star = ColumnarTripleIndex.Build(StarFixture(subjects: 3, fan: 8));
        ColumnarTripleIndex chain = ColumnarTripleIndex.Build(ThreeHopChainFixture());
        ColumnarTripleIndex ring = ColumnarTripleIndex.Build(DenseRingFixture());

        VariableRegistry mixedRegistry = new();
        Variable key = mixedRegistry.GetOrAdd("k");
        BasicGraphPattern mixed = new(
            [
                new TriplePattern(PatternPosition.OfVariable(key), PatternPosition.OfVariable(mixedRegistry.GetOrAdd("p")), PatternPosition.OfVariable(mixedRegistry.GetOrAdd("b0"))),
                EdgePattern(key, ArmOne, mixedRegistry.GetOrAdd("b1")),
                EdgePattern(key, ArmTwo, mixedRegistry.GetOrAdd("b2"))
            ],
            mixedRegistry);

        //Each of the star's three arms leaves its branch in a leaf vector; the chain's tail sits on its last
        //hop alone, the two earlier hops ending on join variables; the ring's every variable is a join
        //variable, so cover depth is already full depth and zero is the shape's own reading.
        Assert.AreEqual(3, TailsOf(star, StarQuery(new VariableRegistry())));
        Assert.AreEqual(1, TailsOf(chain, ThreeHopChainQuery(new VariableRegistry())));
        Assert.AreEqual(0, TailsOf(ring, TriangleQuery(new VariableRegistry())));

        //The three-variable arm carries two leaf columns of its own beside the other two arms' one each, so
        //a sum of leaf columns would read four where the relation count reads three.
        Assert.AreEqual(3, TailsOf(star, mixed));
    }

    /// <summary>The join-cover plan's tail-bearing relation count for the query on the view.</summary>
    /// <param name="index">The columnar view the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The number of relations a join-cover build leaves a private tail on.</returns>
    private static int TailsOf(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index, query);

        Assert.IsNotNull(variableOrder, "The fixture's shape has no global variable order on this view.");

        FreeJoinRelationPlan[] plans = FreeJoinPipeline.PlanRelations(index, query, variableOrder, FreeJoinPipeline.JoinVariablesOf(index, query), FreeJoinDepthRule.JoinCover);

        return FreeJoinPipeline.TailBearingRelationCount(plans);
    }

    /// <summary>The connected-component count the depth rule's disconnected branch reads, through the one shape analysis the seam reads.</summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The number of connected components over shared variables.</returns>
    private static int ComponentsOf(BasicGraphPattern query)
    {
        return JoinShapeAnalysis.ComponentCount(JoinShapeAnalysis.BuildEdgeSets(query).Edges);
    }

    /// <summary>The shipped accumulator's weighted fan over a fixture built to carry exactly the given degree sequence.</summary>
    /// <param name="degrees">The intended per-key degrees.</param>
    /// <returns>The degree-weighted mean key fan-out the accumulator reads.</returns>
    private static double WeightedFanOfSequence(int[] degrees)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(DegreeSequenceFixture(degrees));
        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        TriplePattern measured = EdgePattern(subject, Sequence, registry.GetOrAdd("o"));

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, measured, subject, out double weightedMean));

        return weightedMean;
    }

    /// <summary>The per-relation trie depths one rule plans for the query on the view, in pattern order.</summary>
    /// <param name="index">The columnar view the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="depthRule">The depth rule the run plans under.</param>
    /// <returns>The depths in pattern order.</returns>
    private static int[] DepthsUnder(ColumnarTripleIndex index, BasicGraphPattern query, FreeJoinDepthRule depthRule)
    {
        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index, query);

        Assert.IsNotNull(variableOrder, "The fixture's shape has no global variable order on this view.");

        FreeJoinRelationPlan[] plans = FreeJoinPipeline.PlanRelations(index, query, variableOrder, FreeJoinPipeline.JoinVariablesOf(index, query), depthRule);
        int[] depths = new int[plans.Length];
        for(int plan = 0; plan < plans.Length; plan++)
        {
            depths[plan] = plans[plan].Depth;
        }

        return depths;
    }

    /// <summary>A three-arm star over a shared subject, every arm carrying the same fan per subject.</summary>
    /// <param name="subjects">The centre subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarFixture(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= subjects; subject++)
        {
            AppendArm(triples, subject, ArmZero, fan, 10_000);
            AppendArm(triples, subject, ArmOne, fan, 20_000);
            AppendArm(triples, subject, ArmTwo, fan, 30_000);
        }

        return triples;
    }

    /// <summary>A two-arm star over a shared subject, both arms carrying the same fan per subject.</summary>
    /// <param name="subjects">The centre subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> TwoArmStarFixture(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= subjects; subject++)
        {
            AppendArm(triples, subject, ArmZero, fan, 10_000);
            AppendArm(triples, subject, ArmOne, fan, 20_000);
        }

        return triples;
    }

    /// <summary>A two-arm star whose second arm is extended one hop: three patterns, of which the first arm and the extension bear a tail, each carrying a fan of five.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> BranchedStarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 3; subject++)
        {
            AppendArm(triples, subject, ArmZero, 5, 10_000);
            triples.Add(EncodedTriple.FromEncoded(subject, ArmOne, 5_000 + subject));
            AppendArm(triples, 5_000 + subject, HopOne, 5, 60_000);
        }

        return triples;
    }

    /// <summary>A three-arm star whose first arm fans out past the engagement fan while the other two stay well below it.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> MixedStarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 3; subject++)
        {
            AppendArm(triples, subject, ArmZero, 10, 10_000);
            AppendArm(triples, subject, ArmOne, 3, 20_000);
            AppendArm(triples, subject, ArmTwo, 3, 30_000);
        }

        return triples;
    }

    /// <summary>A two-hop chain whose single hub carries a hundred second-hop objects: one tail-bearing relation, heavily concentrated.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> SkewedChainFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 3; subject++)
        {
            triples.Add(EncodedTriple.FromEncoded(subject, HopOne, 500));
        }

        AppendArm(triples, 500, HopTwo, 100, 1_000);

        return triples;
    }

    /// <summary>A two-hop chain whose second hop carries one hub of a hundred beside nine hundred keys of one: a heavy maximum over a light weighted mean.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> HeavyHubLightTailFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 3; subject++)
        {
            triples.Add(EncodedTriple.FromEncoded(subject, HopOne, 500));
        }

        AppendArm(triples, 500, HopTwo, 100, 1_000_000);
        for(uint key = 0; key < 900; key++)
        {
            AppendArm(triples, 501 + key, HopTwo, 1, 1_000_000);
        }

        return triples;
    }

    /// <summary>Two independent two-hop chains, the first component's tail carrying degrees ten, ten, and four and the second's carrying ones.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> SkewedDisjointFixture()
    {
        return DisjointFixture([10, 10, 4]);
    }

    /// <summary>Two independent two-hop chains, the first component's tail carrying one key of eight beside eight keys of one and the second's carrying ones.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> MildDisjointFixture()
    {
        return DisjointFixture([8, 1, 1, 1, 1, 1, 1, 1, 1]);
    }

    /// <summary>Two independent two-hop chains: the first component's tail carries the given degree sequence, the second's carries three keys of one.</summary>
    /// <param name="leftDegrees">The first component's tail degrees, one per hub.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> DisjointFixture(int[] leftDegrees)
    {
        List<EncodedTriple> triples = [];
        for(uint hub = 0; hub < leftDegrees.Length; hub++)
        {
            triples.Add(EncodedTriple.FromEncoded(1 + hub, LeftHopOne, 500 + hub));
            AppendArm(triples, 500 + hub, LeftHopTwo, leftDegrees[(int)hub], 200_000);
        }

        for(uint hub = 0; hub < 3; hub++)
        {
            triples.Add(EncodedTriple.FromEncoded(2_000 + hub, RightHopOne, 2_500 + hub));
            AppendArm(triples, 2_500 + hub, RightHopTwo, 1, 400_000);
        }

        return triples;
    }

    /// <summary>One predicate carrying exactly the given per-key degree sequence, one key per entry.</summary>
    /// <param name="degrees">The intended per-key degrees.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> DegreeSequenceFixture(int[] degrees)
    {
        List<EncodedTriple> triples = [];
        for(int key = 0; key < degrees.Length; key++)
        {
            AppendArm(triples, (uint)(key + 1), Sequence, degrees[key], 100_000);
        }

        return triples;
    }

    /// <summary>A three-hop chain: one subject reaching one hub, that hub reaching one node, and that node carrying four objects of its own.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ThreeHopChainFixture()
    {
        List<EncodedTriple> triples =
        [
            EncodedTriple.FromEncoded(1, HopOne, 500),
            EncodedTriple.FromEncoded(500, HopTwo, 600)
        ];

        AppendArm(triples, 600, HopThree, 4, 1_000);

        return triples;
    }

    /// <summary>Degrees five, two, and one on the measured predicate, beside a heavier single group on a neighbouring predicate.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> SkewedGroupFixture()
    {
        List<EncodedTriple> triples = [];
        AppendArm(triples, 10, Measured, 5, 1_000);
        AppendArm(triples, 11, Measured, 2, 2_000);
        AppendArm(triples, 12, Measured, 1, 3_000);
        AppendArm(triples, 13, Neighbour, 9, 4_000);

        return triples;
    }

    /// <summary>A regular fixture: every subject carries the same number of distinct objects on the measured predicate.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The object count per subject.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> RegularFixture(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= subjects; subject++)
        {
            AppendArm(triples, subject, Measured, fan, 7_000);
        }

        return triples;
    }

    /// <summary>One graph's run for the graph-set row: a single subject carrying the given fan on the measured predicate.</summary>
    /// <param name="fan">The subject's object count.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> GraphRun(int fan)
    {
        List<EncodedTriple> triples = [];
        AppendArm(triples, 20, Measured, fan, 6_000);

        return triples;
    }

    /// <summary>A dense directed ring over one predicate: every node reaches every other, so a triangle query has answers and every subject's out-degree clears the engagement fan.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> DenseRingFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 0; subject < 13; subject++)
        {
            for(uint @object = 0; @object < 13; @object++)
            {
                if(subject != @object)
                {
                    triples.Add(EncodedTriple.FromEncoded(subject, Edge, @object));
                }
            }
        }

        return triples;
    }

    /// <summary>Appends one arm's objects for one subject; the objects are distinct across subjects and arms.</summary>
    /// <param name="triplesToAppendTo">The fixture being built.</param>
    /// <param name="subject">The subject the arm hangs off.</param>
    /// <param name="predicate">The arm's predicate.</param>
    /// <param name="fan">The object count.</param>
    /// <param name="objectBase">The arm's object-id base.</param>
    private static void AppendArm(List<EncodedTriple> triplesToAppendTo, uint subject, uint predicate, int fan, uint objectBase)
    {
        for(uint offset = 0; offset < fan; offset++)
        {
            triplesToAppendTo.Add(EncodedTriple.FromEncoded(subject, predicate, objectBase + (subject * 100) + offset));
        }
    }

    /// <summary>The three-arm star query over the shared centre variable.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        Variable key = registry.GetOrAdd("k");

        return new BasicGraphPattern(
            [
                EdgePattern(key, ArmZero, registry.GetOrAdd("b0")),
                EdgePattern(key, ArmOne, registry.GetOrAdd("b1")),
                EdgePattern(key, ArmTwo, registry.GetOrAdd("b2"))
            ],
            registry);
    }

    /// <summary>The two-arm star query over the shared centre variable.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern TwoArmStarQuery(VariableRegistry registry)
    {
        Variable key = registry.GetOrAdd("k");

        return new BasicGraphPattern(
            [
                EdgePattern(key, ArmZero, registry.GetOrAdd("b0")),
                EdgePattern(key, ArmOne, registry.GetOrAdd("b1"))
            ],
            registry);
    }

    /// <summary>The branched-star query: two arms on one centre, the second arm's branch extended one hop, so the middle relation carries no tail.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern BranchedStarQuery(VariableRegistry registry)
    {
        Variable key = registry.GetOrAdd("k");
        Variable branch = registry.GetOrAdd("b0");
        Variable extended = registry.GetOrAdd("b1");

        return new BasicGraphPattern(
            [
                EdgePattern(key, ArmZero, branch),
                EdgePattern(key, ArmOne, extended),
                EdgePattern(extended, HopOne, registry.GetOrAdd("c"))
            ],
            registry);
    }

    /// <summary>The two-hop chain query, whose shared object is its only join variable.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        Variable hub = registry.GetOrAdd("o");

        return new BasicGraphPattern(
            [
                EdgePattern(registry.GetOrAdd("s"), HopOne, hub),
                EdgePattern(hub, HopTwo, registry.GetOrAdd("t"))
            ],
            registry);
    }

    /// <summary>Two independent two-hop chains: four patterns forming two components, one tail-bearing relation in each.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern DisjointChainsQuery(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");
        Variable e = registry.GetOrAdd("e");
        Variable f = registry.GetOrAdd("f");

        return new BasicGraphPattern(
            [
                EdgePattern(a, LeftHopOne, b),
                EdgePattern(b, LeftHopTwo, c),
                EdgePattern(d, RightHopOne, e),
                EdgePattern(e, RightHopTwo, f)
            ],
            registry);
    }

    /// <summary>The three-hop chain query: two shared variables join the three hops, and only the last hop's fresh object sits outside the cover.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ThreeHopChainQuery(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        return new BasicGraphPattern(
            [
                EdgePattern(a, HopOne, b),
                EdgePattern(b, HopTwo, c),
                EdgePattern(c, HopThree, d)
            ],
            registry);
    }

    /// <summary>The triangle query over one predicate, whose every variable is a join variable.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern TriangleQuery(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                EdgePattern(x, Edge, y),
                EdgePattern(y, Edge, z),
                EdgePattern(z, Edge, x)
            ],
            registry);
    }

    /// <summary>The pattern binding one subject variable, one bound predicate, and one object variable.</summary>
    /// <param name="subject">The subject variable.</param>
    /// <param name="predicate">The bound predicate's encoded term.</param>
    /// <param name="object">The object variable.</param>
    /// <returns>The pattern.</returns>
    private static TriplePattern EdgePattern(Variable subject, uint predicate, Variable @object)
    {
        return new TriplePattern(
            PatternPosition.OfVariable(subject),
            PatternPosition.Bound(TermId.FromEncoded(predicate)),
            PatternPosition.OfVariable(@object));
    }
}
