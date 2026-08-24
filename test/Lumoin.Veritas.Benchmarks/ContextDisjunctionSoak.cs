using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak characterising the disjunctive context-saturation profile: saturation
/// growth on the covering families — a covering-width ladder (one union of w disjuncts, every
/// disjunct reaching a shared superclass) and a covering-depth ladder (d chained coverings) — and
/// the DL4 merge ladder (max-n against n+1 pairwise-disjoint forced successors, the pigeonhole
/// refutation), each rung recording wall time, allocation, derived clauses, and the peak per-context
/// clause count beside a Horn-chain baseline of matching axiom count. The Horn baseline rows run
/// TWICE and report whether the two allocation deltas are identical — the no-regression stability
/// face on Horn input (the engine lift is behavior-preserving where selected == head[0]). Read-off
/// verdicts are verified on every rung whose signature fits the shared subsumption sweep cap;
/// larger rungs record growth only. Line-oriented output for hand-collation, the same shape as the
/// other <c>--profile-*</c> soaks.
/// </summary>
internal static class ContextDisjunctionSoak
{
    /// <summary>The example namespace the soak's classes and roles are drawn from.</summary>
    private const string Example = "http://example.org/tier2soak#";

    /// <summary>Runs the three ladders and the Horn baseline.</summary>
    public static void RunContextDisjunctionSoak()
    {
        Console.WriteLine($"[ctx-disjunction] host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount} serverGC={System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine("[ctx-disjunction] rows: family | rung | verdict | ms | alloc | ClausesDerived | MaxContextClauses | read-off");

        //Unmeasured decisions warm the JIT on BOTH machinery families (the covering resolution path
        //and the DL4 equality/merge path) so no measured rung is a compilation sample.
        Decide(CoveringWidthModule(2));
        Decide(PigeonholeModule(2));

        int[] widths = [2, 4, 8, 14];
        foreach(int width in widths)
        {
            RunRung("covering-width", "w=" + width, CoveringWidthModule(width), expectConsistent: true, readOffSub: "Ash", readOffSuper: "Oak");
        }

        int[] depths = [2, 4, 7];
        foreach(int depth in depths)
        {
            RunRung("covering-depth", "d=" + depth, CoveringDepthModule(depth), expectConsistent: true, readOffSub: "A0", readOffSuper: "Oak");
        }

        int[] bounds = [2, 3, 4, 5];
        foreach(int bound in bounds)
        {
            RunRung("dl4-pigeonhole", "n=" + bound, PigeonholeModule(bound), expectConsistent: true, readOffSub: "Ash", readOffSuper: "B1");
        }

        int[] chainLengths = [3, 5, 9, 15];
        foreach(int length in chainLengths)
        {
            RunHornBaselineRung(length);
        }
    }

    /// <summary>Measures one disjunctive rung: decides the module under a fresh window, prints the row, and reports the expected consistency and (within the sweep cap) the expected read-off pair.</summary>
    /// <param name="family">The ladder name.</param>
    /// <param name="rung">The rung label.</param>
    /// <param name="module">The module to decide.</param>
    /// <param name="expectConsistent">The expected module consistency.</param>
    /// <param name="readOffSub">The expected read-off subsumption's subclass local name.</param>
    /// <param name="readOffSuper">The expected read-off subsumption's superclass local name.</param>
    private static void RunRung(string family, string rung, ReasoningModule module, bool expectConsistent, string readOffSub, string readOffSuper)
    {
        //The module is constructed before the window opens, so the sample measures the DECISION
        //alone — never the fixture building or string interning.
        SoakWindow window = SoakWindow.Open();
        ModuleDecision decision = Decide(module);
        SoakSample sample = window.Close();

        bool decided = decision.Statistics.ContextTotals.ContextDecided;
        bool consistent = decision.Verdict!.IsConsistent;
        string readOff = HasSubsumption(decision.Verdict, readOffSub, readOffSuper) ? "read-off ok" : "read-off ABSENT";
        string verdict = decided && consistent == expectConsistent ? "ok" : "UNEXPECTED";
        Console.WriteLine($"[ctx-disjunction] {family} | {rung} | {verdict} | {sample.Milliseconds:F2} ms | {sample.AllocCell} | {decision.Statistics.ContextTotals.ClausesDerived} | {decision.Statistics.ContextTotals.MaxContextClauses} | {readOff}");
    }

    /// <summary>Measures one Horn-chain baseline rung on two POST-WARM decisions of a pre-built module and prints both samples with the allocation-identity verdict — the no-regression stability face on Horn input. The module is built once outside every window and its first decision is discarded as warm-up, so the compared deltas are the engine's own steady-state allocations.</summary>
    /// <param name="length">The chain length in subclass axioms.</param>
    private static void RunHornBaselineRung(int length)
    {
        ReasoningModule module = HornChainModule(length);
        Decide(module);

        SoakWindow first = SoakWindow.Open();
        ModuleDecision firstDecision = Decide(module);
        SoakSample firstSample = first.Close();

        SoakWindow second = SoakWindow.Open();
        ModuleDecision secondDecision = Decide(module);
        SoakSample secondSample = second.Close();

        bool decided = firstDecision.Statistics.ContextTotals.ContextDecided && secondDecision.Statistics.ContextTotals.ContextDecided;
        bool allocIdentical = !firstSample.ThreadHopped && !secondSample.ThreadHopped && firstSample.ThreadAllocatedBytes == secondSample.ThreadAllocatedBytes;
        Console.WriteLine($"[ctx-disjunction] horn-baseline | h={length} | {(decided ? "ok" : "UNEXPECTED")} | {firstSample.Milliseconds:F2}/{secondSample.Milliseconds:F2} ms | {firstSample.AllocCell} vs {secondSample.AllocCell} | {firstDecision.Statistics.ContextTotals.ClausesDerived} | {firstDecision.Statistics.ContextTotals.MaxContextClauses} | alloc-identical={allocIdentical}");
    }

    /// <summary>Decides a module through the production module reasoner under an unbounded budget.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private static ModuleDecision Decide(ReasoningModule module)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, CancellationToken.None);
    }

    /// <summary>Whether the verdict's subsumption set holds the given pair.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasSubsumption(ModuleVerdict verdict, string sub, string super)
    {
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

    /// <summary>The covering-width module: <c>Ash ⊑ B1 ⊔ … ⊔ Bw</c> with every disjunct reaching Oak — case analysis over w branches.</summary>
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

    /// <summary>The covering-depth module: d chained coverings <c>Ai ⊑ Ai+1 ⊔ Ci+1</c> with every leaf reaching Oak — nested case analysis of depth d.</summary>
    /// <param name="depth">The chain depth d.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule CoveringDepthModule(int depth)
    {
        List<OwlAxiom> axioms = new(2 * depth + 1);
        for(int i = 0; i < depth; i++)
        {
            axioms.Add(SubClassOf(Class("A" + i), new OwlObjectUnionOf([Class("A" + (i + 1)), Class("C" + (i + 1))])));
            axioms.Add(SubClassOf(Class("C" + (i + 1)), Class("Oak")));
        }

        axioms.Add(SubClassOf(Class("A" + depth), Class("Oak")));

        return Module(axioms);
    }

    /// <summary>The DL4 pigeonhole module: <c>Ash ⊑ ≤n feeds.⊤</c> against n+1 pairwise-disjoint forced successors — Ash unsatisfiable by the merge clash.</summary>
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

    /// <summary>The Horn-chain baseline module: <c>C0 ⊑ C1 ⊑ … ⊑ Ch</c> — pure Horn input of h axioms through the lifted engine.</summary>
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

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }
}
