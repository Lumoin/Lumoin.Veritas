using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>The generator family one census fixture is built from.</summary>
internal enum CensusFixtureKind
{
    /// <summary>A three-arm star at a fixed per-arm fan.</summary>
    RegularStar = 0,

    /// <summary>A two-hop chain, one link per subject and one terminal per link.</summary>
    RegularChain = 1,

    /// <summary>A single-predicate digraph at a fixed out-degree, queried as a triangle.</summary>
    RegularTriangle = 2,

    /// <summary>A three-arm star whose subjects carry a profiled per-arm fan.</summary>
    SkewedStar = 3,

    /// <summary>A two-hop chain whose hubs carry a profiled out-degree on the second hop.</summary>
    SkewedChain = 4,

    /// <summary>A single-predicate digraph with a profiled out-degree, queried as a triangle.</summary>
    SkewedTriangle = 5,

    /// <summary>The triangle digraph with private tails at a fixed leaf fan on named core positions.</summary>
    Satellite = 6,

    /// <summary>The triangle digraph queried as a four-cycle.</summary>
    FourCycle = 7,

    /// <summary>Two star arms plus a third arm extended one hop.</summary>
    StarChain = 8,

    /// <summary>Two independent two-hop chains on disjoint predicates.</summary>
    DisjointChains = 9,

    /// <summary>Two independent two-hop chains on disjoint predicates, each component's hubs carrying a profiled out-degree on the second hop.</summary>
    SkewedDisjointChains = 10,
}

/// <summary>
/// One census fixture: the identity its lines carry, the intended generator
/// parameters those lines report, the degree sequence a profiled family emits
/// from, and the a-priori output predictor the size guards refuse it by.
/// </summary>
internal sealed class CensusFixture
{
    /// <summary>The exact-predictor cap: a fixture whose generator fixes its answer size above this is not built.</summary>
    internal const long ExactRowCap = 5_000_000;

    /// <summary>The fractional-edge-cover cap: a fixture whose bound exceeds this is not built. A bound is not a prediction, so it carries its own cap.</summary>
    internal const long CoverBoundCap = 1_000_000_000;

    /// <summary>Constructs a fixture.</summary>
    /// <param name="id">The fixture and query id its lines join on.</param>
    /// <param name="shape">The closed-vocabulary shape token.</param>
    /// <param name="kind">The generator family.</param>
    /// <param name="primaryPredicate">The predicate every degree statistic is measured on.</param>
    /// <param name="nodeCount">The generator node count.</param>
    /// <param name="edgeTarget">The generator edge target on the primary predicate.</param>
    /// <param name="profileExponent">The degree profile exponent: zero regular, one harmonic, two quadratic.</param>
    /// <param name="fan">The per-arm fan for the star families and the per-tail leaf fan for the satellite family; unused elsewhere.</param>
    /// <param name="satellites">The tail count for the satellite family; unused elsewhere.</param>
    /// <param name="degrees">The intended out-degree sequence for a profiled family, or <see langword="null"/> for a closed-form family.</param>
    /// <param name="predictor">The a-priori output predictor.</param>
    /// <param name="predictorIsBound">Whether the predictor is a fractional-edge-cover bound rather than an exact count.</param>
    /// <param name="smokeScale">Whether this fixture is the quick protocol's large-magnitude probe.</param>
    /// <param name="timedRepetitions">The timed repetition target the cells of this fixture aim for.</param>
    internal CensusFixture(
        string id,
        string shape,
        CensusFixtureKind kind,
        uint primaryPredicate,
        int nodeCount,
        long edgeTarget,
        int profileExponent,
        int fan,
        int satellites,
        int[]? degrees,
        long predictor,
        bool predictorIsBound,
        bool smokeScale,
        int timedRepetitions)
    {
        Id = id;
        Shape = shape;
        Kind = kind;
        PrimaryPredicate = primaryPredicate;
        NodeCount = nodeCount;
        EdgeTarget = edgeTarget;
        ProfileExponent = profileExponent;
        Fan = fan;
        Satellites = satellites;
        Degrees = degrees;
        Predictor = predictor;
        PredictorIsBound = predictorIsBound;
        SmokeScale = smokeScale;
        TimedRepetitions = timedRepetitions;
    }

    /// <summary>The fixture and query id its lines join on.</summary>
    internal string Id { get; }

    /// <summary>The closed-vocabulary shape token.</summary>
    internal string Shape { get; }

    /// <summary>The generator family.</summary>
    internal CensusFixtureKind Kind { get; }

    /// <summary>The predicate every degree statistic on this fixture's line is measured on.</summary>
    internal uint PrimaryPredicate { get; }

    /// <summary>The generator node count.</summary>
    internal int NodeCount { get; }

    /// <summary>The generator edge target on the primary predicate.</summary>
    internal long EdgeTarget { get; }

    /// <summary>The degree profile exponent: zero regular, one harmonic, two quadratic.</summary>
    internal int ProfileExponent { get; }

    /// <summary>The per-arm fan for the star families, and the leaf count each tail attaches to every core node for the satellite family.</summary>
    internal int Fan { get; }

    /// <summary>The tail count for the satellite family.</summary>
    internal int Satellites { get; }

    /// <summary>The intended out-degree sequence for a profiled family, or <see langword="null"/> for a closed-form family.</summary>
    internal int[]? Degrees { get; }

    /// <summary>The a-priori output predictor: an exact answer size, or a fractional-edge-cover bound.</summary>
    internal long Predictor { get; }

    /// <summary>Whether <see cref="Predictor"/> is a bound rather than an exact count.</summary>
    internal bool PredictorIsBound { get; }

    /// <summary>Whether this fixture is the quick protocol's large-magnitude probe.</summary>
    internal bool SmokeScale { get; }

    /// <summary>The timed repetition target the cells of this fixture aim for.</summary>
    internal int TimedRepetitions { get; }

    /// <summary>Whether a size guard refuses to build this fixture: the exact predictor over its cap, or the cover bound over its own.</summary>
    internal bool Refused => PredictorIsBound ? Predictor > CoverBoundCap : Predictor > ExactRowCap;

    /// <summary>Emits the fixture's triples.</summary>
    /// <returns>The triples.</returns>
    internal List<EncodedTriple> Generate()
    {
        return Kind switch
        {
            CensusFixtureKind.RegularStar => JoinRouteCensusFixtures.StarTriples(NodeCount, Fan),
            CensusFixtureKind.RegularChain => JoinRouteCensusFixtures.ChainTriples(NodeCount),
            CensusFixtureKind.RegularTriangle => JoinRouteCensusFixtures.TriangleTriples(NodeCount),
            CensusFixtureKind.SkewedStar => JoinRouteCensusFixtures.SkewedStarTriples(Degrees!),
            CensusFixtureKind.SkewedChain => JoinRouteCensusFixtures.SkewedChainTriples(Degrees!),
            CensusFixtureKind.SkewedTriangle => JoinRouteCensusFixtures.SkewedDigraphTriples(Degrees!),
            CensusFixtureKind.Satellite => JoinRouteCensusFixtures.SatelliteTriples(NodeCount, Satellites, Fan),
            CensusFixtureKind.FourCycle => JoinRouteCensusFixtures.TriangleTriples(NodeCount),
            CensusFixtureKind.StarChain => JoinRouteCensusFixtures.StarChainTriples(NodeCount, Fan),
            CensusFixtureKind.DisjointChains => JoinRouteCensusFixtures.DisjointChainTriples(NodeCount),
            _ => JoinRouteCensusFixtures.SkewedDisjointChainTriples(Degrees!)
        };
    }

    /// <summary>Builds the fixture's query over a fresh registry.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal BasicGraphPattern BuildQuery(VariableRegistry registry)
    {
        return Kind switch
        {
            CensusFixtureKind.RegularStar => JoinRouteCensusFixtures.StarQuery(registry),
            CensusFixtureKind.SkewedStar => JoinRouteCensusFixtures.StarQuery(registry),
            CensusFixtureKind.RegularChain => JoinRouteCensusFixtures.ChainQuery(registry),
            CensusFixtureKind.SkewedChain => JoinRouteCensusFixtures.ChainQuery(registry),
            CensusFixtureKind.RegularTriangle => JoinRouteCensusFixtures.TriangleQuery(registry),
            CensusFixtureKind.SkewedTriangle => JoinRouteCensusFixtures.TriangleQuery(registry),
            CensusFixtureKind.Satellite => JoinRouteCensusFixtures.SatelliteQuery(registry, Satellites),
            CensusFixtureKind.FourCycle => JoinRouteCensusFixtures.FourCycleQuery(registry),
            CensusFixtureKind.StarChain => JoinRouteCensusFixtures.StarChainQuery(registry),
            _ => JoinRouteCensusFixtures.DisjointChainQuery(registry)
        };
    }
}

/// <summary>
/// The census ladders and their generators: the legacy reproduction rungs, the
/// skew families over three integer degree profiles, the shape ladder, and the
/// crossover ladder that varies the tail fan, the head weight, and the
/// disconnected components' skew against those fixed families. Every
/// generator is closed-form integer arithmetic — no drawn randomness and no
/// floating point — so a degree sequence is analytically known before it is
/// emitted and two boxes agree on every fixture bit for bit. Floating point
/// appears only in the a-priori predictors, which are compared against a cap and
/// printed, never used to build a fixture.
/// </summary>
internal static class JoinRouteCensusFixtures
{
    /// <summary>The first star arm's, the chain's first hop's, and the digraph's predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The second star arm's and the chain's second hop's predicate.</summary>
    private const uint P2 = 20;

    /// <summary>The third star arm's and the star-chain's extended arm's predicate.</summary>
    private const uint P3 = 30;

    /// <summary>The first tail's, the star-chain's extension hop's, and the second disjoint chain's second hop's predicate.</summary>
    private const uint P4 = 40;

    /// <summary>The second tail's predicate.</summary>
    private const uint P5 = 50;

    /// <summary>The third tail's predicate.</summary>
    private const uint P6 = 60;

    /// <summary>The fourth tail's predicate.</summary>
    private const uint P7 = 70;

    /// <summary>The regular digraph out-degree the triangle, lollipop, satellite, and four-cycle fixtures are built at.</summary>
    private const int DigraphOutDegree = 8;

    /// <summary>The leaf count a satellite tail attaches to every core node on the rungs built at the fixed tail fan.</summary>
    private const int TailLeaves = 4;

    /// <summary>The head budget over the regular baseline the profiled families solve their head constant against, at stand sizes.</summary>
    private const int StandHeadBudget = 200;

    /// <summary>The head budget at smoke sizes, chosen so the quadratic profile's star answer stays inside a smoke sitting.</summary>
    private const int SmokeHeadBudget = 60;

    /// <summary>The satellite tails' predicates, in attachment order.</summary>
    private static uint[] TailPredicates { get; } = [P4, P5, P6, P7];

    /// <summary>The core variable each satellite tail attaches to, in tail order. The length-k prefix gives sat-k its attachments: one corner for one tail, two distinct corners for two, and with four tails two land on the first corner and one on each of the others.</summary>
    private static string[] TailAttachments { get; } = ["x", "y", "x", "z"];

    /// <summary>
    /// The census ladder in run order: the legacy reproduction rungs, the skew
    /// families, the shape ladder, and the crossover ladder. Each of the last
    /// two ends with its own disconnected rungs, so an abort there loses nothing
    /// already printed.
    /// </summary>
    /// <param name="quick">Whether the smoke-scale protocol's sizes apply.</param>
    /// <returns>The fixtures in ladder order.</returns>
    internal static List<CensusFixture> Ladder(bool quick)
    {
        List<CensusFixture> ladder = [];

        AppendLegacyLadder(ladder, quick);
        AppendSkewLadder(ladder, quick);
        AppendShapeLadder(ladder, quick);
        AppendCrossoverLadder(ladder, quick);

        return ladder;
    }

    /// <summary>Appends ladder A: the eight legacy reproduction rungs, at stand sizes or at their smoke reductions.</summary>
    /// <param name="ladder">The ladder being built.</param>
    /// <param name="quick">Whether the smoke-scale sizes apply.</param>
    private static void AppendLegacyLadder(List<CensusFixture> ladder, bool quick)
    {
        int starTarget = quick ? 50_000 : 500_000;
        int[] chainSubjects = quick ? [10_000] : [50_000, 500_000];
        int[] triangleNodes = quick ? [1_000, 20_000] : [2_000, 20_000];
        int lollipopNodes = quick ? 1_000 : 2_000;

        foreach(int fan in (int[])[2, 4, 8])
        {
            int subjects = Math.Max(1, starTarget / Math.Max(1, fan * fan * fan));
            ladder.Add(new CensusFixture(
                $"star-f{fan.ToString(CultureInfo.InvariantCulture)}",
                "star",
                CensusFixtureKind.RegularStar,
                P1,
                subjects,
                (long)subjects * fan,
                profileExponent: 0,
                fan,
                satellites: 0,
                degrees: null,
                predictor: (long)subjects * fan * fan * fan,
                predictorIsBound: false,
                smokeScale: false,
                timedRepetitions: 3));
        }

        foreach(int subjects in chainSubjects)
        {
            ladder.Add(new CensusFixture(
                $"chain-n{subjects.ToString(CultureInfo.InvariantCulture)}",
                "chain",
                CensusFixtureKind.RegularChain,
                P1,
                subjects,
                subjects,
                profileExponent: 0,
                fan: 0,
                satellites: 0,
                degrees: null,
                predictor: subjects,
                predictorIsBound: false,
                smokeScale: false,
                timedRepetitions: 3));
        }

        foreach(int nodes in triangleNodes)
        {
            long edges = (long)nodes * DigraphOutDegree;
            ladder.Add(new CensusFixture(
                $"tri-n{nodes.ToString(CultureInfo.InvariantCulture)}",
                "triangle",
                CensusFixtureKind.RegularTriangle,
                P1,
                nodes,
                edges,
                profileExponent: 0,
                fan: 0,
                satellites: 0,
                degrees: null,
                predictor: TriangleCoverBound(edges),
                predictorIsBound: true,
                smokeScale: false,
                timedRepetitions: 3));
        }

        long lollipopEdges = (long)lollipopNodes * DigraphOutDegree;
        ladder.Add(new CensusFixture(
            $"lollipop-n{lollipopNodes.ToString(CultureInfo.InvariantCulture)}",
            "lollipop",
            CensusFixtureKind.Satellite,
            P1,
            lollipopNodes,
            lollipopEdges,
            profileExponent: 0,
            fan: TailLeaves,
            satellites: 1,
            degrees: null,
            predictor: TriangleCoverBound(lollipopEdges) * TailLeaves,
            predictorIsBound: true,
            smokeScale: false,
            timedRepetitions: 3));
    }

    /// <summary>Appends ladder B: the three skew families, each over the regular, harmonic, and quadratic profiles.</summary>
    /// <param name="ladder">The ladder being built.</param>
    /// <param name="quick">Whether the smoke-scale sizes apply.</param>
    private static void AppendSkewLadder(List<CensusFixture> ladder, bool quick)
    {
        int headBudget = quick ? SmokeHeadBudget : StandHeadBudget;
        int[] triangleNodes = quick ? [1_000] : [2_000, 20_000];
        int starNodes = quick ? 2_000 : 20_000;
        int chainNodes = quick ? 5_000 : 50_000;

        foreach(int nodes in triangleNodes)
        {
            long edgeTarget = ((long)nodes * DigraphOutDegree) + headBudget;
            for(int exponent = 0; exponent <= 2; exponent++)
            {
                int[] degrees = DegreeSequence(nodes, DigraphOutDegree, exponent, edgeTarget);
                ladder.Add(new CensusFixture(
                    $"tri-a{exponent.ToString(CultureInfo.InvariantCulture)}-n{nodes.ToString(CultureInfo.InvariantCulture)}",
                    "triangle",
                    CensusFixtureKind.SkewedTriangle,
                    P1,
                    nodes,
                    edgeTarget,
                    exponent,
                    fan: 0,
                    satellites: 0,
                    degrees,
                    predictor: TriangleCoverBound(edgeTarget),
                    predictorIsBound: true,
                    smokeScale: false,
                    timedRepetitions: 3));
            }
        }

        long starEdgeTarget = starNodes + headBudget;
        for(int exponent = 0; exponent <= 2; exponent++)
        {
            int[] degrees = DegreeSequence(starNodes, 1, exponent, starEdgeTarget);
            ladder.Add(new CensusFixture(
                $"starskew-a{exponent.ToString(CultureInfo.InvariantCulture)}-n{starNodes.ToString(CultureInfo.InvariantCulture)}",
                "star",
                CensusFixtureKind.SkewedStar,
                P1,
                starNodes,
                starEdgeTarget,
                exponent,
                fan: 0,
                satellites: 0,
                degrees,
                predictor: StarAnswerSize(degrees),
                predictorIsBound: false,
                smokeScale: false,
                timedRepetitions: 3));
        }

        long chainEdgeTarget = chainNodes + headBudget;
        int[] inDegrees = ChainHubInDegrees(chainNodes);
        for(int exponent = 0; exponent <= 2; exponent++)
        {
            int[] degrees = DegreeSequence(chainNodes, 1, exponent, chainEdgeTarget);
            ladder.Add(new CensusFixture(
                $"chainskew-a{exponent.ToString(CultureInfo.InvariantCulture)}-n{chainNodes.ToString(CultureInfo.InvariantCulture)}",
                "chain",
                CensusFixtureKind.SkewedChain,
                P2,
                chainNodes,
                chainEdgeTarget,
                exponent,
                fan: 0,
                satellites: 0,
                degrees,
                predictor: ChainAnswerSize(inDegrees, degrees),
                predictorIsBound: false,
                smokeScale: false,
                timedRepetitions: 3));
        }
    }

    /// <summary>Appends ladder C: the satellite ladder, the four-cycle, the star-chain hybrid, the smoke's large-magnitude probe, and the disconnected rung last.</summary>
    /// <param name="ladder">The ladder being built.</param>
    /// <param name="quick">Whether the smoke-scale sizes apply.</param>
    private static void AppendShapeLadder(List<CensusFixture> ladder, bool quick)
    {
        int coreNodes = quick ? 1_000 : 2_000;
        int starChainSubjects = quick ? 2_000 : 20_000;
        int disjointSubjects = quick ? 200 : 2_000;
        long coreEdges = (long)coreNodes * DigraphOutDegree;

        foreach(int tails in (int[])[1, 2, 4])
        {
            long multiplier = 1;
            for(int tail = 0; tail < tails; tail++)
            {
                multiplier *= TailLeaves;
            }

            ladder.Add(new CensusFixture(
                $"sat{tails.ToString(CultureInfo.InvariantCulture)}-a0-n{coreNodes.ToString(CultureInfo.InvariantCulture)}",
                $"sat{tails.ToString(CultureInfo.InvariantCulture)}",
                CensusFixtureKind.Satellite,
                P1,
                coreNodes,
                coreEdges,
                profileExponent: 0,
                fan: TailLeaves,
                satellites: tails,
                degrees: null,
                predictor: TriangleCoverBound(coreEdges) * multiplier,
                predictorIsBound: true,
                smokeScale: false,
                timedRepetitions: 3));
        }

        ladder.Add(new CensusFixture(
            $"cycle4-a0-n{coreNodes.ToString(CultureInfo.InvariantCulture)}",
            "cycle4",
            CensusFixtureKind.FourCycle,
            P1,
            coreNodes,
            coreEdges,
            profileExponent: 0,
            fan: 0,
            satellites: 0,
            degrees: null,
            predictor: coreEdges * coreEdges,
            predictorIsBound: true,
            smokeScale: false,
            timedRepetitions: 3));

        const int StarChainFan = 2;
        ladder.Add(new CensusFixture(
            $"starchain-a0-n{starChainSubjects.ToString(CultureInfo.InvariantCulture)}",
            "starchain",
            CensusFixtureKind.StarChain,
            P1,
            starChainSubjects,
            (long)starChainSubjects * StarChainFan,
            profileExponent: 0,
            StarChainFan,
            satellites: 0,
            degrees: null,
            predictor: (long)starChainSubjects * StarChainFan * StarChainFan * StarChainFan * StarChainFan,
            predictorIsBound: false,
            smokeScale: false,
            timedRepetitions: 3));

        if(quick)
        {
            //The smoke reaches the magnitude the stand protocol's largest rungs
            //carry, at one timed repetition per cell, so an arithmetic or format
            //defect that only appears at a million triples cannot hide behind the
            //small fixtures.
            const int ProbeSubjects = 500_000;
            ladder.Add(new CensusFixture(
                $"chain-n{ProbeSubjects.ToString(CultureInfo.InvariantCulture)}",
                "chain",
                CensusFixtureKind.RegularChain,
                P1,
                ProbeSubjects,
                ProbeSubjects,
                profileExponent: 0,
                fan: 0,
                satellites: 0,
                degrees: null,
                predictor: ProbeSubjects,
                predictorIsBound: false,
                smokeScale: true,
                timedRepetitions: 1));
        }

        ladder.Add(new CensusFixture(
            $"disjoint2-a0-n{disjointSubjects.ToString(CultureInfo.InvariantCulture)}",
            "disjoint2",
            CensusFixtureKind.DisjointChains,
            P1,
            disjointSubjects,
            disjointSubjects,
            profileExponent: 0,
            fan: 0,
            satellites: 0,
            degrees: null,
            predictor: (long)disjointSubjects * disjointSubjects,
            predictorIsBound: false,
            smokeScale: false,
            timedRepetitions: 3));
    }

    /// <summary>Appends ladder D: the star rung between the legacy fans, the satellite tail-fan rungs, the heavy-head chain rungs, and the skewed and second-size disconnected rungs last.</summary>
    /// <param name="ladder">The ladder being built.</param>
    /// <param name="quick">Whether the smoke-scale sizes apply.</param>
    private static void AppendCrossoverLadder(List<CensusFixture> ladder, bool quick)
    {
        int starTarget = quick ? 50_000 : 500_000;
        int coreNodes = quick ? 1_000 : 2_000;
        int chainNodes = quick ? 5_000 : 50_000;
        int skewedDisjointSubjects = quick ? 200 : 2_000;
        int regularDisjointSubjects = quick ? 500 : 1_000;
        int headBudget = quick ? SmokeHeadBudget : StandHeadBudget;
        long coreEdges = (long)coreNodes * DigraphOutDegree;

        const int StarFan = 6;
        int starSubjects = Math.Max(1, starTarget / (StarFan * StarFan * StarFan));
        ladder.Add(new CensusFixture(
            $"star-f{StarFan.ToString(CultureInfo.InvariantCulture)}",
            "star",
            CensusFixtureKind.RegularStar,
            P1,
            starSubjects,
            (long)starSubjects * StarFan,
            profileExponent: 0,
            fan: StarFan,
            satellites: 0,
            degrees: null,
            predictor: (long)starSubjects * StarFan * StarFan * StarFan,
            predictorIsBound: false,
            smokeScale: false,
            timedRepetitions: 3));

        //The tail fan multiplies the cover bound once per tail, so the four-tail
        //arm reaches the bound cap at a lower fan than the two-tail arm does:
        //the two-tail arm carries the ladder's high tail fan and the four-tail
        //arm stops below it.
        foreach((int Tails, int TailFan) rung in ((int, int)[])[(2, 2), (2, 6), (4, 2), (4, 3)])
        {
            long multiplier = 1;
            for(int tail = 0; tail < rung.Tails; tail++)
            {
                multiplier *= rung.TailFan;
            }

            ladder.Add(new CensusFixture(
                $"sat{rung.Tails.ToString(CultureInfo.InvariantCulture)}-f{rung.TailFan.ToString(CultureInfo.InvariantCulture)}-a0-n{coreNodes.ToString(CultureInfo.InvariantCulture)}",
                $"sat{rung.Tails.ToString(CultureInfo.InvariantCulture)}",
                CensusFixtureKind.Satellite,
                P1,
                coreNodes,
                coreEdges,
                profileExponent: 0,
                fan: rung.TailFan,
                satellites: rung.Tails,
                degrees: null,
                predictor: TriangleCoverBound(coreEdges) * multiplier,
                predictorIsBound: true,
                smokeScale: false,
                timedRepetitions: 3));
        }

        //The heavy-head sequence is not a power profile, so the alpha column
        //reads zero and the skew these rungs carry is read from the realised
        //maximum degree, heavy threshold, and heavy fraction instead.
        const int HeadHubCount = 10;
        long chainEdgeTarget = chainNodes + headBudget;
        int[] chainInDegrees = ChainHubInDegrees(chainNodes);
        foreach(int share in (int[])[25, 50])
        {
            int[] degrees = HeavyHeadDegreeSequence(chainNodes, HeadHubCount, share, chainEdgeTarget);
            ladder.Add(new CensusFixture(
                $"chainhub-s{share.ToString(CultureInfo.InvariantCulture)}-n{chainNodes.ToString(CultureInfo.InvariantCulture)}",
                "chain",
                CensusFixtureKind.SkewedChain,
                P2,
                chainNodes,
                chainEdgeTarget,
                profileExponent: 0,
                fan: 0,
                satellites: 0,
                degrees,
                predictor: ChainAnswerSize(chainInDegrees, degrees),
                predictorIsBound: false,
                smokeScale: false,
                timedRepetitions: 3));
        }

        //Both components carry the same degree sequence over the same scatter,
        //so one component's answer size squared is the disconnected answer.
        long skewedDisjointEdgeTarget = skewedDisjointSubjects + headBudget;
        int[] skewedDisjointInDegrees = ChainHubInDegrees(skewedDisjointSubjects);
        for(int exponent = 1; exponent <= 2; exponent++)
        {
            int[] degrees = DegreeSequence(skewedDisjointSubjects, 1, exponent, skewedDisjointEdgeTarget);
            long componentRows = ChainAnswerSize(skewedDisjointInDegrees, degrees);
            ladder.Add(new CensusFixture(
                $"disjoint2-a{exponent.ToString(CultureInfo.InvariantCulture)}-n{skewedDisjointSubjects.ToString(CultureInfo.InvariantCulture)}",
                "disjoint2",
                CensusFixtureKind.SkewedDisjointChains,
                P2,
                skewedDisjointSubjects,
                skewedDisjointEdgeTarget,
                exponent,
                fan: 0,
                satellites: 0,
                degrees,
                predictor: componentRows * componentRows,
                predictorIsBound: false,
                smokeScale: false,
                timedRepetitions: 3));
        }

        ladder.Add(new CensusFixture(
            $"disjoint2-a0-n{regularDisjointSubjects.ToString(CultureInfo.InvariantCulture)}",
            "disjoint2",
            CensusFixtureKind.DisjointChains,
            P1,
            regularDisjointSubjects,
            regularDisjointSubjects,
            profileExponent: 0,
            fan: 0,
            satellites: 0,
            degrees: null,
            predictor: (long)regularDisjointSubjects * regularDisjointSubjects,
            predictorIsBound: false,
            smokeScale: false,
            timedRepetitions: 3));
    }

    /// <summary>
    /// The intended out-degree sequence of one profile: every node carries the
    /// family's regular baseline, and a profiled head sits over it. The head
    /// constant is found by an integer search — doubling until the total reaches
    /// the edge target, then a binary search for the smallest constant that does
    /// — so no floating-point power is ever evaluated. A degree is capped at the
    /// distinct out-neighbour count the node set admits, which makes the
    /// generator total; at these sizes the cap never binds.
    /// </summary>
    /// <param name="nodeCount">The node count.</param>
    /// <param name="baseline">The regular out-degree every node carries.</param>
    /// <param name="exponent">The profile exponent: zero regular, one harmonic, two quadratic.</param>
    /// <param name="edgeTarget">The total out-degree the search solves for.</param>
    /// <returns>The out-degree per node, in node order.</returns>
    internal static int[] DegreeSequence(int nodeCount, int baseline, int exponent, long edgeTarget)
    {
        int cap = Math.Max(1, nodeCount - 1);
        int[] degrees = new int[nodeCount];

        if(exponent == 0)
        {
            int regular = Math.Clamp((int)((edgeTarget + (nodeCount / 2)) / nodeCount), 1, cap);
            Array.Fill(degrees, regular);

            return degrees;
        }

        long head = 1;
        while(head < int.MaxValue && ProfileTotal(nodeCount, baseline, exponent, cap, head) < edgeTarget)
        {
            head *= 2;
        }

        long low = 1;
        long high = head;
        while(low < high)
        {
            long middle = low + ((high - low) / 2);
            if(ProfileTotal(nodeCount, baseline, exponent, cap, middle) >= edgeTarget)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        for(int node = 0; node < nodeCount; node++)
        {
            degrees[node] = ProfileDegree(baseline, exponent, cap, low, node);
        }

        return degrees;
    }

    /// <summary>
    /// The intended out-degree sequence of a heavy head: a fixed count of
    /// leading hubs carries a named percentage of the edge target between them
    /// and the remaining nodes share what is left. Each share is an integer
    /// division of its own budget, and that division's remainder is handed out
    /// one edge each to the first nodes of the same share, so a share's nodes
    /// sum to its budget exactly and the two budgets sum to the edge target: the
    /// sequence totals the edge target by construction, with no clamping step
    /// that could add or drop an edge. The leftover budget is smaller than the
    /// count of nodes it spreads over at every size this ladder builds, so its
    /// even share is zero and the nodes past its remainder carry no second-hop
    /// edge at all; that empty tail is the shape a heavy head is measured
    /// against, and it is why the realised heavy fraction reads the hub share.
    /// </summary>
    /// <param name="nodeCount">The node count.</param>
    /// <param name="hubCount">The count of leading nodes the head budget is split between.</param>
    /// <param name="hubSharePercent">The percentage of the edge target those hubs carry.</param>
    /// <param name="edgeTarget">The total out-degree the sequence sums to.</param>
    /// <returns>The out-degree per node, in node order.</returns>
    private static int[] HeavyHeadDegreeSequence(int nodeCount, int hubCount, int hubSharePercent, long edgeTarget)
    {
        int[] degrees = new int[nodeCount];

        long hubBudget = edgeTarget * hubSharePercent / 100;
        long hubShare = hubBudget / hubCount;
        long hubRemainder = hubBudget % hubCount;
        for(int hub = 0; hub < hubCount; hub++)
        {
            degrees[hub] = (int)(hub < hubRemainder ? hubShare + 1 : hubShare);
        }

        int restCount = nodeCount - hubCount;
        long restBudget = edgeTarget - hubBudget;
        long restShare = restBudget / restCount;
        long restRemainder = restBudget % restCount;
        for(int node = 0; node < restCount; node++)
        {
            degrees[hubCount + node] = (int)(node < restRemainder ? restShare + 1 : restShare);
        }

        return degrees;
    }

    /// <summary>One node's profiled out-degree at a head constant.</summary>
    /// <param name="baseline">The regular out-degree every node carries.</param>
    /// <param name="exponent">The profile exponent.</param>
    /// <param name="cap">The distinct out-neighbour cap.</param>
    /// <param name="head">The head constant.</param>
    /// <param name="node">The node index.</param>
    /// <returns>The out-degree.</returns>
    private static int ProfileDegree(int baseline, int exponent, int cap, long head, int node)
    {
        long divisor = node + 1;
        if(exponent == 2)
        {
            divisor *= divisor;
        }

        long degree = Math.Max(head / divisor, baseline);

        return (int)Math.Clamp(degree, 1, cap);
    }

    /// <summary>The profile's total out-degree at a head constant.</summary>
    /// <param name="nodeCount">The node count.</param>
    /// <param name="baseline">The regular out-degree every node carries.</param>
    /// <param name="exponent">The profile exponent.</param>
    /// <param name="cap">The distinct out-neighbour cap.</param>
    /// <param name="head">The head constant.</param>
    /// <returns>The total out-degree.</returns>
    private static long ProfileTotal(int nodeCount, int baseline, int exponent, int cap, long head)
    {
        long total = 0;
        for(int node = 0; node < nodeCount; node++)
        {
            total += ProfileDegree(baseline, exponent, cap, head, node);
        }

        return total;
    }

    /// <summary>The exact answer size of the three-arm star over a degree sequence: each subject contributes the cube of its per-arm fan.</summary>
    /// <param name="degrees">The per-subject out-degree.</param>
    /// <returns>The row count.</returns>
    private static long StarAnswerSize(int[] degrees)
    {
        long rows = 0;
        foreach(int degree in degrees)
        {
            rows += (long)degree * degree * degree;
        }

        return rows;
    }

    /// <summary>The exact answer size of the two-hop chain: each hub contributes its in-degree times its out-degree.</summary>
    /// <param name="inDegrees">The hubs' first-hop in-degrees.</param>
    /// <param name="outDegrees">The hubs' second-hop out-degrees.</param>
    /// <returns>The row count.</returns>
    private static long ChainAnswerSize(int[] inDegrees, int[] outDegrees)
    {
        long rows = 0;
        for(int hub = 0; hub < inDegrees.Length; hub++)
        {
            rows += (long)inDegrees[hub] * outDegrees[hub];
        }

        return rows;
    }

    /// <summary>The triangle's fractional-edge-cover bound at an edge budget: the cover number is three halves, so the bound is the edge count to that power.</summary>
    /// <param name="edges">The intended edge count.</param>
    /// <returns>The bound.</returns>
    private static long TriangleCoverBound(long edges)
    {
        return (long)Math.Round(Math.Pow(edges, 1.5));
    }

    /// <summary>The hubs' first-hop in-degrees under the scatter, known before any triple is emitted.</summary>
    /// <param name="nodeCount">The hub count.</param>
    /// <returns>The in-degree per hub.</returns>
    private static int[] ChainHubInDegrees(int nodeCount)
    {
        int[] inDegrees = new int[nodeCount];
        for(int subject = 0; subject < nodeCount; subject++)
        {
            inDegrees[Scatter(subject, 0, nodeCount)]++;
        }

        return inDegrees;
    }

    /// <summary>The fixed odd-constant scatter the profiled generators place their targets by.</summary>
    /// <param name="source">The source node index.</param>
    /// <param name="step">The edge's index within the source's out-edges.</param>
    /// <param name="nodeCount">The node count the target wraps within.</param>
    /// <returns>The target node index.</returns>
    private static uint Scatter(int source, int step, int nodeCount)
    {
        return (uint)(((((long)source * 1000003) + ((long)step * 65537)) + 12289) % nodeCount);
    }

    /// <summary>A three-arm star: each subject fans out on every arm.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> StarTriples(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint subject = 1_000_000 + s;
            for(uint j = 0; j < fan; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 2_000_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 4_000_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P3, 6_000_000 + (s * 100) + j));
            }
        }

        return triples;
    }

    /// <summary>A three-arm star whose subject carries its profiled out-degree on each arm.</summary>
    /// <param name="degrees">The per-subject out-degree.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> SkewedStarTriples(int[] degrees)
    {
        uint stride = 1;
        foreach(int degree in degrees)
        {
            if((uint)degree > stride)
            {
                stride = (uint)degree;
            }
        }

        List<EncodedTriple> triples = [];
        for(int s = 0; s < degrees.Length; s++)
        {
            uint subject = 1_000_000 + (uint)s;
            uint offset = (uint)s * stride;
            for(uint j = 0; j < (uint)degrees[s]; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 100_000_000 + offset + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 200_000_000 + offset + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P3, 300_000_000 + offset + j));
            }
        }

        return triples;
    }

    /// <summary>The star query <c>?s P1 ?o1 . ?s P2 ?o2 . ?s P3 ?o3</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3")
            ],
            registry);
    }

    /// <summary>A two-hop chain: each subject reaches one link, each link one terminal.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> ChainTriples(int subjects)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint link = 2_000_000 + s;
            triples.Add(EncodedTriple.FromEncoded(1_000_000 + s, P1, link));
            triples.Add(EncodedTriple.FromEncoded(link, P2, 4_000_000 + s));
        }

        return triples;
    }

    /// <summary>A two-hop chain whose hubs carry a profiled out-degree on the second hop — the fan-out the join key pays for.</summary>
    /// <param name="degrees">The per-hub second-hop out-degree.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> SkewedChainTriples(int[] degrees)
    {
        int nodeCount = degrees.Length;
        List<EncodedTriple> triples = [];
        for(int subject = 0; subject < nodeCount; subject++)
        {
            triples.Add(EncodedTriple.FromEncoded(1_000_000 + (uint)subject, P1, 2_000_000 + Scatter(subject, 0, nodeCount)));
        }

        HashSet<uint> emitted = [];
        for(int hub = 0; hub < nodeCount; hub++)
        {
            emitted.Clear();
            int degree = degrees[hub];
            for(int step = 1; step <= degree; step++)
            {
                uint target = Scatter(hub, step, nodeCount);
                if(emitted.Add(target))
                {
                    triples.Add(EncodedTriple.FromEncoded(2_000_000 + (uint)hub, P2, 4_000_000 + target));
                }
            }
        }

        return triples;
    }

    /// <summary>The chain query <c>?s P1 ?o . ?o P2 ?t</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o"),
                EdgePattern(registry, P2, "o", "t")
            ],
            registry);
    }

    /// <summary>A deterministic sparse digraph on one predicate: eight arithmetic out-edges per node, self-loops skipped.</summary>
    /// <param name="nodes">The node count.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> TriangleTriples(int nodes)
    {
        List<EncodedTriple> triples = [];
        for(uint i = 0; i < nodes; i++)
        {
            for(uint j = 1; j <= 8; j++)
            {
                uint target = ((i * 31) + (j * 17)) % (uint)nodes;
                if(target != i)
                {
                    triples.Add(EncodedTriple.FromEncoded(1_000_000 + i, P1, 1_000_000 + target));
                }
            }
        }

        return triples;
    }

    /// <summary>A single-predicate digraph whose nodes carry a profiled out-degree, targets scattered, self-loops and repeated pairs skipped.</summary>
    /// <param name="degrees">The per-node out-degree.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> SkewedDigraphTriples(int[] degrees)
    {
        int nodeCount = degrees.Length;
        List<EncodedTriple> triples = [];
        HashSet<uint> emitted = [];
        for(int source = 0; source < nodeCount; source++)
        {
            emitted.Clear();
            int degree = degrees[source];
            for(int step = 1; step <= degree; step++)
            {
                uint target = Scatter(source, step, nodeCount);
                if(target == (uint)source || !emitted.Add(target))
                {
                    continue;
                }

                triples.Add(EncodedTriple.FromEncoded(1_000_000 + (uint)source, P1, 1_000_000 + target));
            }
        }

        return triples;
    }

    /// <summary>The triangle query <c>?x P1 ?y . ?y P1 ?z . ?z P1 ?x</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern TriangleQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "x", "y"),
                EdgePattern(registry, P1, "y", "z"),
                EdgePattern(registry, P1, "z", "x")
            ],
            registry);
    }

    /// <summary>The four-cycle query <c>?w P1 ?x . ?x P1 ?y . ?y P1 ?z . ?z P1 ?w</c> over the same digraph.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern FourCycleQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "w", "x"),
                EdgePattern(registry, P1, "x", "y"),
                EdgePattern(registry, P1, "y", "z"),
                EdgePattern(registry, P1, "z", "w")
            ],
            registry);
    }

    /// <summary>
    /// The triangle digraph plus one private tail per satellite, each on its own
    /// predicate and each attaching the same leaf fan to every core node. The
    /// leaf identifiers stride ten per core node, so a tail reaches ten leaves
    /// before two core nodes would share one.
    /// </summary>
    /// <param name="nodes">The core node count.</param>
    /// <param name="satellites">The tail count.</param>
    /// <param name="tailFan">The leaf count each tail attaches to every core node.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> SatelliteTriples(int nodes, int satellites, int tailFan)
    {
        List<EncodedTriple> triples = TriangleTriples(nodes);
        for(int tail = 0; tail < satellites; tail++)
        {
            uint predicate = TailPredicates[tail];
            uint leafBase = 8_000_000 + ((uint)tail * 1_000_000);
            for(uint i = 0; i < nodes; i++)
            {
                for(uint j = 0; j < (uint)tailFan; j++)
                {
                    triples.Add(EncodedTriple.FromEncoded(1_000_000 + i, predicate, leafBase + (i * 10) + j));
                }
            }
        }

        return triples;
    }

    /// <summary>The triangle core plus one tail pattern per satellite, attached to the named core corners.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <param name="satellites">The tail count.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern SatelliteQuery(VariableRegistry registry, int satellites)
    {
        List<TriplePattern> patterns =
        [
            EdgePattern(registry, P1, "x", "y"),
            EdgePattern(registry, P1, "y", "z"),
            EdgePattern(registry, P1, "z", "x")
        ];

        for(int tail = 0; tail < satellites; tail++)
        {
            patterns.Add(EdgePattern(registry, TailPredicates[tail], TailAttachments[tail], $"w{tail.ToString(CultureInfo.InvariantCulture)}"));
        }

        return new BasicGraphPattern(patterns, registry);
    }

    /// <summary>Two star arms per subject and a third arm whose objects each fan out one hop further — the shape the factorised route's nested and extension paths need.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The per-arm object count per subject, and the per-branch object count of the extended arm.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> StarChainTriples(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint subject = 1_000_000 + s;
            for(uint j = 0; j < fan; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 100_000_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 200_000_000 + (s * 100) + j));

                uint branch = 300_000_000 + (s * 100) + j;
                triples.Add(EncodedTriple.FromEncoded(subject, P3, branch));
                for(uint c = 0; c < fan; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branch, P4, 500_000_000 + (((s * 100) + j) * 10) + c));
                }
            }
        }

        return triples;
    }

    /// <summary>The star-chain query <c>?s P1 ?o1 . ?s P2 ?o2 . ?s P3 ?b . ?b P4 ?c</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern StarChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "b"),
                EdgePattern(registry, P4, "b", "c")
            ],
            registry);
    }

    /// <summary>Two independent two-hop chains on disjoint predicates — the disconnected shape.</summary>
    /// <param name="subjects">The subject count per chain.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> DisjointChainTriples(int subjects)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint firstLink = 2_000_000 + s;
            triples.Add(EncodedTriple.FromEncoded(1_000_000 + s, P1, firstLink));
            triples.Add(EncodedTriple.FromEncoded(firstLink, P2, 3_000_000 + s));

            uint secondLink = 5_000_000 + s;
            triples.Add(EncodedTriple.FromEncoded(4_000_000 + s, P3, secondLink));
            triples.Add(EncodedTriple.FromEncoded(secondLink, P4, 6_000_000 + s));
        }

        return triples;
    }

    /// <summary>
    /// Two independent two-hop chains on the disconnected shape's predicates and
    /// identifier spaces, each component's first hop scattered and each
    /// component's hubs carrying the profiled second-hop out-degree. Both
    /// components read the same degree sequence over the same scatter, so they
    /// are answer-identical by construction and the disconnected answer is one
    /// component's row count squared.
    /// </summary>
    /// <param name="degrees">The per-hub second-hop out-degree, applied in both components.</param>
    /// <returns>The triples.</returns>
    internal static List<EncodedTriple> SkewedDisjointChainTriples(int[] degrees)
    {
        int nodeCount = degrees.Length;
        List<EncodedTriple> triples = [];
        for(int subject = 0; subject < nodeCount; subject++)
        {
            uint link = Scatter(subject, 0, nodeCount);
            triples.Add(EncodedTriple.FromEncoded(1_000_000 + (uint)subject, P1, 2_000_000 + link));
            triples.Add(EncodedTriple.FromEncoded(4_000_000 + (uint)subject, P3, 5_000_000 + link));
        }

        HashSet<uint> emitted = [];
        for(int hub = 0; hub < nodeCount; hub++)
        {
            emitted.Clear();
            int degree = degrees[hub];
            for(int step = 1; step <= degree; step++)
            {
                uint target = Scatter(hub, step, nodeCount);
                if(emitted.Add(target))
                {
                    triples.Add(EncodedTriple.FromEncoded(2_000_000 + (uint)hub, P2, 3_000_000 + target));
                    triples.Add(EncodedTriple.FromEncoded(5_000_000 + (uint)hub, P4, 6_000_000 + target));
                }
            }
        }

        return triples;
    }

    /// <summary>The disconnected query <c>?s1 P1 ?o1 . ?o1 P2 ?t1 . ?s2 P3 ?o2 . ?o2 P4 ?t2</c> — two components, a cartesian answer.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    internal static BasicGraphPattern DisjointChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s1", "o1"),
                EdgePattern(registry, P2, "o1", "t1"),
                EdgePattern(registry, P3, "s2", "o2"),
                EdgePattern(registry, P4, "o2", "t2")
            ],
            registry);
    }

    /// <summary>The pattern <c>?subject {predicate} ?object</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <param name="predicate">The bound predicate.</param>
    /// <param name="subjectName">The subject variable name.</param>
    /// <param name="objectName">The object variable name.</param>
    /// <returns>The pattern.</returns>
    private static TriplePattern EdgePattern(VariableRegistry registry, uint predicate, string subjectName, string objectName)
    {
        return new TriplePattern(
            PatternPosition.OfVariable(registry.GetOrAdd(subjectName)),
            PatternPosition.Bound(TermId.FromEncoded(predicate)),
            PatternPosition.OfVariable(registry.GetOrAdd(objectName)));
    }
}
