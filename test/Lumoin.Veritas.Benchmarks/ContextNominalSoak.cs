using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak characterising the nominal context-saturation profile: saturation growth
/// on the root-exchange ladder (w independent owner pairs rendezvousing at one shared nominal
/// constant, recording root-context clause growth, root edges, and both root-rule directions) and
/// the generated-nominal mint ladder (the descending anonymous-witness tower, recording Nom
/// applications, generated-nominal counts, and the label-depth curve per rung under a fixed attempt
/// backstop so a deep rung exhausts honestly instead of hanging), beside twice-run Horn and
/// nominal-free disjunctive baselines whose two allocation deltas must be identical and whose
/// nominal counters must all stay zero — the H-T3-4 allocation face on nominal-free input. Read-off
/// verdicts are verified on every rung whose signature fits the shared subsumption sweep cap;
/// larger rungs record growth only. Line-oriented output for hand-collation, the same shape as the
/// other <c>--profile-*</c> soaks.
/// </summary>
internal static class ContextNominalSoak
{
    /// <summary>The example namespace the soak's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier3soak#";

    /// <summary>The attempt backstop bounding every mint-ladder rung — high enough for the shallow rungs to complete and mint their depth chain, finite so a deep rung exhausts and reports instead of running unbounded.</summary>
    private const int MintBackstopAttempts = 1_000_000;

    /// <summary>The mint-ladder budget: no solve or conflict limit, the attempt backstop alone.</summary>
    private static ReasoningBudget MintBackstop { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: MintBackstopAttempts);

    /// <summary>Runs the two nominal ladders and the two alloc-identity baselines.</summary>
    public static void RunContextNominalSoak()
    {
        Console.WriteLine($"[ctx-nominal] host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount} serverGC={System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine("[ctx-nominal] rootx rows: family | rung | verdict | ms | alloc | ClausesDerived | RootContextClauses | RootEdges | RootSucc | RootPred | read-off");
        Console.WriteLine("[ctx-nominal] mint rows: family | rung | outcome | ms | alloc | attempts | Nom | GeneratedNominals | MaxNominalLabelDepth | ClausesDerived");
        Console.WriteLine("[ctx-nominal] baseline rows: family | rung | verdict | ms/ms | alloc vs alloc | ClausesDerived | MaxContextClauses | alloc-identical | nominal-silent");
        Console.WriteLine("[ctx-nominal] root-index rows: family | rung | verdict | ms/ms/ms | alloc vs alloc vs alloc | ClausesDerived | alloc-identical | root-approx-surface(stable) | root-index-contexts(stable)");

        //Unmeasured decisions warm the JIT on BOTH nominal machinery families (the root-exchange
        //path and the Nom mint path) and both baseline families, so no measured rung is a
        //compilation sample.
        Decide(RootExchangeModule(2));
        DecideBounded(NomMintTowerModule(1));
        Decide(CoveringWidthModule(2));
        Decide(HornChainModule(3));

        int[] widths = [2, 4, 8, 16, 32];
        foreach(int width in widths)
        {
            RunRootExchangeRung(width);
        }

        int[] depths = [1, 2, 3, 4];
        foreach(int depth in depths)
        {
            RunNomMintRung(depth);
        }

        int[] chainLengths = [3, 5, 9, 15];
        foreach(int length in chainLengths)
        {
            RunBaselineRung("horn-baseline", "h=" + length, HornChainModule(length));
        }

        RunBaselineRung("nominal-free", "covering w=4", CoveringWidthModule(4));
        RunBaselineRung("nominal-free", "pigeonhole n=3", PigeonholeModule(3));

        int[] rootIndexWidths = [2, 4, 8];
        foreach(int width in rootIndexWidths)
        {
            RunRootIndexRung(width);
        }
    }

    /// <summary>Measures one root-exchange rung: decides the module under a fresh window, prints the row with the root-context growth columns, and verifies the first owner's two read-off subsumptions when the rung's signature fits the shared subsumption sweep cap.</summary>
    /// <param name="width">The owner-pair count w.</param>
    private static void RunRootExchangeRung(int width)
    {
        //The module is constructed before the window opens, so the sample measures the DECISION
        //alone — never the fixture building or string interning.
        ReasoningModule module = RootExchangeModule(width);
        bool underCap = 1 + (6 * width) <= AlcModuleReasoner.SubsumptionSignatureCap;

        SoakWindow window = SoakWindow.Open();
        ModuleDecision decision = Decide(module);
        SoakSample sample = window.Close();

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        bool decided = totals.ContextDecided && decision.Verdict is not null && decision.Verdict.IsConsistent;
        bool exchanged = totals.RootSuccApplications > 0 && totals.RootPredApplications > 0;
        string readOff = underCap
            ? HasSubsumption(decision.Verdict, "A0", "F0") && HasSubsumption(decision.Verdict, "B0", "H0") ? "read-off ok" : "read-off ABSENT"
            : "over-cap";
        string verdict = decided && exchanged ? "ok" : "UNEXPECTED";
        Console.WriteLine($"[ctx-nominal] rootx-width | w={width} | {verdict} | {sample.Milliseconds:F2} ms | {sample.AllocCell} | {totals.ClausesDerived} | {totals.RootContextClauses} | {totals.RootEdges} | {totals.RootSuccApplications} | {totals.RootPredApplications} | {readOff}");
    }

    /// <summary>Measures one mint-ladder rung under the attempt backstop: prints the outcome (a completed decide or an honest budget exhaust), the spent attempts, and the generated-nominal growth columns — the label-depth curve reads down these rows.</summary>
    /// <param name="depth">The tower depth n.</param>
    private static void RunNomMintRung(int depth)
    {
        ReasoningModule module = NomMintTowerModule(depth);

        SoakWindow window = SoakWindow.Open();
        ModuleDecision decision = DecideBounded(module);
        SoakSample sample = window.Close();

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        string outcome = decision.Outcome switch
        {
            ReasoningDecisionOutcome.Decided => decision.Verdict is not null && decision.Verdict.IsConsistent ? "decided ok" : "decided UNEXPECTED",
            ReasoningDecisionOutcome.AbstainedBudget => "exhausted",
            _ => decision.Outcome.ToString(),
        };
        Console.WriteLine($"[ctx-nominal] nomr-mint | n={depth} | {outcome} | {sample.Milliseconds:F2} ms | {sample.AllocCell} | {totals.InferenceAttempts} | {totals.NomApplications} | {totals.GeneratedNominals} | {totals.MaxNominalLabelDepth} | {totals.ClausesDerived}");
    }

    /// <summary>Measures one baseline rung on three post-warm decisions of a pre-built module and prints every sample byte-precise with the allocation-identity verdict and the nominal-silence verdict — the H-T3-4 face: nominal-free input decides with every nominal counter at zero and a steady allocation delta, and three windows separate a one-off lazy-initialization tail from genuine run-to-run variance. The module is built once outside every window and its first decision is discarded as warm-up, so the compared deltas are the engine's own steady-state allocations.</summary>
    /// <param name="family">The baseline family name.</param>
    /// <param name="rung">The rung label.</param>
    /// <param name="module">The nominal-free module.</param>
    private static void RunBaselineRung(string family, string rung, ReasoningModule module)
    {
        Decide(module);

        SoakWindow first = SoakWindow.Open();
        ModuleDecision firstDecision = Decide(module);
        SoakSample firstSample = first.Close();

        SoakWindow second = SoakWindow.Open();
        ModuleDecision secondDecision = Decide(module);
        SoakSample secondSample = second.Close();

        SoakWindow third = SoakWindow.Open();
        ModuleDecision thirdDecision = Decide(module);
        SoakSample thirdSample = third.Close();

        ContextSaturationStatistics totals = firstDecision.Statistics.ContextTotals;
        bool decided = totals.ContextDecided && secondDecision.Statistics.ContextTotals.ContextDecided && thirdDecision.Statistics.ContextTotals.ContextDecided;
        bool hopped = firstSample.ThreadHopped || secondSample.ThreadHopped || thirdSample.ThreadHopped;
        bool allocIdentical = !hopped && firstSample.ThreadAllocatedBytes == secondSample.ThreadAllocatedBytes && secondSample.ThreadAllocatedBytes == thirdSample.ThreadAllocatedBytes;
        bool nominalSilent = NominalSilent(totals) && NominalSilent(secondDecision.Statistics.ContextTotals) && NominalSilent(thirdDecision.Statistics.ContextTotals);
        Console.WriteLine($"[ctx-nominal] {family} | {rung} | {(decided ? "ok" : "UNEXPECTED")} | {firstSample.Milliseconds:F2}/{secondSample.Milliseconds:F2}/{thirdSample.Milliseconds:F2} ms | {firstSample.AllocCellBytes} vs {secondSample.AllocCellBytes} vs {thirdSample.AllocCellBytes} | {totals.ClausesDerived} | {totals.MaxContextClauses} | alloc-identical={allocIdentical} | nominal-silent={nominalSilent}");
    }

    /// <summary>Measures one root-tier index rung across three post-warm decisions of a pre-built nominal module: the root machinery is LIVE here (the ≈-class union-find merges every member into one class, the per-constant index projects the B(o) and S(o, o') families), so the row's allocation is expected to differ from a nominal-free control — but the three measured windows must still land byte-identical to each other (the reconciled-delta discipline: a bounded, steady-state cost, not a leak), and the root-tier diagnostic reads captured off each window's engine (the ≈-class surface allocation bit, the per-constant index's live root-context count) must agree across all three windows too. The module is built once outside every window and its first decision is discarded as warm-up.</summary>
    /// <param name="width">The member count.</param>
    private static void RunRootIndexRung(int width)
    {
        ReasoningModule module = RootIndexModule(width);

        RootTierCapture warmupCapture = new();
        DecideProbed(module, warmupCapture.Handle);

        RootTierCapture firstCapture = new();
        SoakWindow first = SoakWindow.Open();
        ModuleDecision firstDecision = DecideProbed(module, firstCapture.Handle);
        SoakSample firstSample = first.Close();

        RootTierCapture secondCapture = new();
        SoakWindow second = SoakWindow.Open();
        ModuleDecision secondDecision = DecideProbed(module, secondCapture.Handle);
        SoakSample secondSample = second.Close();

        RootTierCapture thirdCapture = new();
        SoakWindow third = SoakWindow.Open();
        ModuleDecision thirdDecision = DecideProbed(module, thirdCapture.Handle);
        SoakSample thirdSample = third.Close();

        ContextSaturationStatistics totals = firstDecision.Statistics.ContextTotals;
        bool decided = totals.ContextDecided && secondDecision.Statistics.ContextTotals.ContextDecided && thirdDecision.Statistics.ContextTotals.ContextDecided;
        bool hopped = firstSample.ThreadHopped || secondSample.ThreadHopped || thirdSample.ThreadHopped;
        bool allocIdentical = !hopped && firstSample.ThreadAllocatedBytes == secondSample.ThreadAllocatedBytes && secondSample.ThreadAllocatedBytes == thirdSample.ThreadAllocatedBytes;

        bool firstApprox = firstCapture.Engine?.HasRootApproxSurface ?? false;
        bool approxStable = firstApprox == (secondCapture.Engine?.HasRootApproxSurface ?? false) && firstApprox == (thirdCapture.Engine?.HasRootApproxSurface ?? false);

        int firstIndexContexts = firstCapture.Engine?.RootConstantIndexContextCount ?? -1;
        bool indexContextsStable = firstIndexContexts == (secondCapture.Engine?.RootConstantIndexContextCount ?? -1) && firstIndexContexts == (thirdCapture.Engine?.RootConstantIndexContextCount ?? -1);

        Console.WriteLine($"[ctx-nominal] root-index | w={width} | {(decided ? "ok" : "UNEXPECTED")} | {firstSample.Milliseconds:F2}/{secondSample.Milliseconds:F2}/{thirdSample.Milliseconds:F2} ms | {firstSample.AllocCellBytes} vs {secondSample.AllocCellBytes} vs {thirdSample.AllocCellBytes} | {totals.ClausesDerived} | alloc-identical={allocIdentical} | root-approx-surface={firstApprox}(stable={approxStable}) | root-index-contexts={firstIndexContexts}(stable={indexContextsStable})");
    }

    /// <summary>Decides a module through the production module reasoner under the same engine axes the public unbounded entry decides — production faces, query-scoped paramodulation, the single-root topology, unrestricted r-Pred relevance, both root-tier lift switches at their production OFF default — with a root-tier diagnostic probe as the sole addition.</summary>
    /// <param name="module">The module.</param>
    /// <param name="probe">The engine capture's probe delegate.</param>
    /// <returns>The decision.</returns>
    private static ModuleDecision DecideProbed(ReasoningModule module, SaturationEngineProbeDelegate probe)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly | EnumerationDeciderFaces.Certifying, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningBudget.Unbounded, probe, CancellationToken.None);
    }

    /// <summary>Captures the saturation engine a root-index rung's decision constructs, so the row can read the root-tier diagnostics — the ≈-class surface allocation bit and the per-constant index's live root-context count — after the decide returns. A re-saturation round would invoke the probe again and the last engine would win, though a root-index rung carries no <c>HasKey</c> descriptor and never re-saturates.</summary>
    private sealed class RootTierCapture
    {
        /// <summary>The last engine the decision constructed, or <see langword="null"/> before the first round.</summary>
        public ContextSaturationEngine? Engine { get; private set; }

        /// <summary>Receives one constructed engine, keeping it for the post-run diagnostic reads.</summary>
        /// <param name="engine">The created engine.</param>
        public void Handle(ContextSaturationEngine engine)
        {
            Engine = engine;
        }
    }

    /// <summary>Whether a decision's totals carry zero across every nominal counter — the root rules, the Nom rule, the mint channel, and the root context itself all untouched.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns><see langword="true"/> when nominal machinery never engaged.</returns>
    private static bool NominalSilent(ContextSaturationStatistics totals)
    {
        return totals.RootSuccApplications == 0
            && totals.RootPredApplications == 0
            && totals.NomApplications == 0
            && totals.GeneratedNominals == 0
            && totals.RootContextClauses == 0
            && totals.RootEdges == 0;
    }

    /// <summary>Decides a module through the production module reasoner under an unbounded budget.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private static ModuleDecision Decide(ReasoningModule module)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, CancellationToken.None);
    }

    /// <summary>Decides a module through the production module reasoner under the mint-ladder attempt backstop.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private static ModuleDecision DecideBounded(ReasoningModule module)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, MintBackstop, progressSampler: null, CancellationToken.None);
    }

    /// <summary>Whether the verdict's subsumption set holds the given pair.</summary>
    /// <param name="verdict">The verdict; a <see langword="null"/> verdict holds nothing.</param>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasSubsumption(ModuleVerdict? verdict, string sub, string super)
    {
        if(verdict is null)
        {
            return false;
        }

        Utf8String subIri = Utf8Strings.From(Example + sub);
        Utf8String superIri = Utf8Strings.From(Example + super);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            if(subClass.Iri.Equals(subIri) && superClass.Iri.Equals(superIri))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The root-exchange module scaled to w owner pairs: one shared enumeration class
    /// <c>C1 ≡ {o}</c> and, per pair i over its OWN roles <c>ri</c>/<c>si</c>, a writer
    /// (<c>Ai ⊑ ∃ri.C1</c>, <c>Ai ⊑ ∀ri.Di</c>, <c>Di ⊑ Ei</c>, <c>Ai(ai)</c>) whose forced
    /// successor lands the <c>Di</c>-then-<c>Ei</c> facts on the constant through the root context,
    /// and a reader (<c>Bi ⊑ ∃si.C1</c>, <c>∃si.Ei ⊑ Hi</c>, plus <c>∃ri.Ei ⊑ Fi</c> back on the
    /// writer) that carries the fact into a different owner's successor context — the ROOTX
    /// exchange, repeated w times over one rendezvous constant so the root context's live clause
    /// count grows with w while each exchange stays per-pair (the roles are distinct).
    /// </summary>
    /// <param name="width">The owner-pair count w.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule RootExchangeModule(int width)
    {
        List<OwlAxiom> axioms = new((7 * width) + 1)
        {
            Equivalent(Class("C1"), OneOf("o")),
        };
        for(int i = 0; i < width; i++)
        {
            axioms.Add(SubClassOf(Class("A" + i), Some("r" + i, Class("C1"))));
            axioms.Add(SubClassOf(Class("A" + i), All("r" + i, Class("D" + i))));
            axioms.Add(SubClassOf(Class("D" + i), Class("E" + i)));
            axioms.Add(ClassAssertion(Class("A" + i), Individual("a" + i)));
            axioms.Add(SubClassOf(Class("B" + i), Some("s" + i, Class("C1"))));
            axioms.Add(SubClassOf(Some("s" + i, Class("E" + i)), Class("H" + i)));
            axioms.Add(SubClassOf(Some("r" + i, Class("E" + i)), Class("F" + i)));
        }

        return Module(axioms);
    }

    /// <summary>
    /// The generated-nominal mint tower (the wedge shape): each level i seeds an
    /// anonymous witness (<c>NwAnchor_i ⊑ ∃s.NwL_i</c>) reaching a per-level counted nominal
    /// (<c>NwL_i ⊑ ∃r.{nwo_i}</c> via hasValue, <c>{nwo_i} ⊑ ≤1 r⁻</c>), so the Nom rule mints a
    /// depth-one generated nominal per level, and the levels thread through a shared
    /// inverse-counting recursion (<c>NwL_i ⊑ ∃r⁻.NwL_{i+1}</c>, <c>NwL_i ⊑ ≤1 r⁻</c>) that
    /// descends the distinct type ladder and grows the minted label depth with the size. The
    /// module carries nominals, object number restrictions, and inverse roles together (the Nom
    /// co-occurrence trigger), is survey-admitted, consistent, and pure saturation work.
    /// </summary>
    /// <param name="depth">The tower depth n.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule NomMintTowerModule(int depth)
    {
        List<OwlAxiom> axioms = new((5 * depth) + 1)
        {
            Inverse("r", "rInv"),
        };
        for(int level = 0; level < depth; level++)
        {
            axioms.Add(SubClassOf(Class("NwAnchor" + level), Some("s", Class("NwL" + level))));
            axioms.Add(SubClassOf(Class("NwL" + level), HasValue("r", "nwo" + level)));
            axioms.Add(SubClassOf(OneOf("nwo" + level), MaxInverse("r", 1, Thing)));
            axioms.Add(SubClassOf(Class("NwL" + level), SomeInverse("r", Class("NwL" + (level + 1)))));
            axioms.Add(SubClassOf(Class("NwL" + level), MaxInverse("r", 1, Thing)));
        }

        return Module(axioms);
    }

    /// <summary>The nominal-free covering-width module: <c>Ash ⊑ B1 ⊔ … ⊔ Bw</c> with every disjunct reaching Oak — disjunctive, non-Horn, no nominal anywhere.</summary>
    /// <param name="width">The union width w.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule CoveringWidthModule(int width)
    {
        List<OwlAxiom> axioms = new(width + 1);
        OwlClassExpression[] operands = new OwlClassExpression[width];
        for(int i = 0; i < width; i++)
        {
            operands[i] = Class("B" + (i + 1));
            axioms.Add(SubClassOf(Class("B" + (i + 1)), Class("Oak")));
        }

        axioms.Add(SubClassOf(Class("Ash"), new OwlObjectUnionOf(operands)));

        return Module(axioms);
    }

    /// <summary>The nominal-free pigeonhole module: <c>Ash ⊑ ≤n feeds.⊤</c> against n+1 pairwise-disjoint forced successors — counting and merge machinery without a nominal in sight.</summary>
    /// <param name="bound">The cardinality bound n.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule PigeonholeModule(int bound)
    {
        List<OwlAxiom> axioms = [SubClassOf(Class("Ash"), new OwlObjectCardinality(OwlCardinalityKind.Max, bound, Property("feeds"), Filler: null))];
        for(int i = 1; i <= bound + 1; i++)
        {
            axioms.Add(SubClassOf(Class("Ash"), new OwlObjectSomeValuesFrom(Property("feeds"), Class("B" + i))));
        }

        for(int i = 1; i <= bound + 1; i++)
        {
            for(int j = i + 1; j <= bound + 1; j++)
            {
                axioms.Add(new OwlDisjointClassesAxiom([Class("B" + i), Class("B" + j)]) { Origin = Origin("disjoint") });
            }
        }

        return Module(axioms);
    }

    /// <summary>The Horn-chain baseline module: <c>C0 ⊑ C1 ⊑ … ⊑ Ch</c> — pure Horn input of h axioms.</summary>
    /// <param name="length">The chain length h.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule HornChainModule(int length)
    {
        List<OwlAxiom> axioms = new(length);
        for(int i = 0; i < length; i++)
        {
            axioms.Add(SubClassOf(Class("C" + i), Class("C" + (i + 1))));
        }

        return Module(axioms);
    }

    /// <summary>
    /// The root-tier index module scaled to width individuals under one nominal-jurisdiction
    /// trigger: a single OneOf nominal
    /// (<c>RiAnchor ≡ {rianchor}</c>) puts the whole module under nominal jurisdiction, so its
    /// ABox lands on the single root context; width class assertions (<c>RiMember(rim_i)</c>)
    /// populate the per-constant index's B(o) family, width-1 role edges chaining each member
    /// to the next populate the S(o, o′) family, and width-1 chained <c>SameIndividual</c>
    /// axioms merge every member into ONE ≈-class — the ≈-class union-find grows with
    /// width while the per-constant index families grow with the same member count.
    /// </summary>
    /// <param name="width">The member count.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule RootIndexModule(int width)
    {
        List<OwlAxiom> axioms = new((3 * width) + 1)
        {
            Equivalent(Class("RiAnchor"), OneOf("rianchor")),
        };
        for(int i = 0; i < width; i++)
        {
            axioms.Add(ClassAssertion(Class("RiMember"), Individual("rim" + i)));
        }

        for(int i = 0; i < width - 1; i++)
        {
            axioms.Add(Edge("rim" + i, "links", "rim" + (i + 1)));
            axioms.Add(Same("rim" + i, "rim" + (i + 1)));
        }

        return Module(axioms);
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(List<OwlAxiom> axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>The fixed-top class reference, <c>owl:Thing</c>.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>An inverse object property expression over a named property in the example namespace.</summary>
    /// <param name="local">The named property's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An enumeration class over named individuals in the example namespace.</summary>
    /// <param name="individuals">The member local names.</param>
    /// <returns>The enumeration expression.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>A hasValue restriction over a forward role and a named individual.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An existential restriction over an inverse role.</summary>
    /// <param name="property">The named role's local name.</param>
    /// <param name="filler">The filler expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(InverseProperty(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A max-cardinality restriction over an inverse role.</summary>
    /// <param name="property">The named role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <param name="filler">The filler expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxInverse(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, InverseProperty(property), filler);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>An equivalence axiom over two class expressions.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equiv") };
    }

    /// <summary>A class-membership assertion.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An inverse-properties axiom binding a named role and its named inverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A told SameIndividual axiom over two named individuals in the example namespace.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A told object-property-assertion role edge between two named individuals in the example namespace.</summary>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string source, string property, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(Utf8Strings.From(Example + property)), Individual(target)) { Origin = Origin("edge") };
    }
}
