using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The context-saturation fourth arm of the Direct-Semantics conformance run.
/// It decides every case through the sentinel-fallback seam
/// (<see cref="ContextArm"/>): a module the context engine admits is decided
/// whole, and one it does not admit falls to an abstaining sentinel, so the
/// seam returns <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> exactly
/// when the module lies beyond the engine's fragment or beyond its practical
/// reach. The two abstentions are told apart by the spent saturation totals the
/// seam reattaches: a non-admitted module carries none (the survey, the
/// reserved-role scan, the clausifier remainder, the second gate, or
/// undecided-data delegation stopped it before any rule fired), while a
/// budget-exhausted admitted module carries its spent rule applications. The
/// arm keeps its own engine-relative census on that split:
/// <see cref="ContextFragmentGaps"/> for premises beyond the fragment,
/// <see cref="ContextPracticalReachGaps"/> for admitted premises whose
/// saturation exhausts the arm's inference budget (the disjunctive
/// combinatorial cases), and <see cref="ContextRefutationGaps"/> for cases
/// whose refutation module hits either boundary.
/// </summary>
internal sealed partial class W3cOwl2DirectTests
{
    /// <summary>The IRI prefix the fourth-arm boundary rows' synthetic classes, roles, and individuals live under; its reserved authority cannot collide with corpus document terms.</summary>
    private const string ContextExample = "urn:lumoin:veritas:conformance:context#";

    /// <summary>
    /// The arm's inference ceiling, the production default's calibration
    /// (<c>ReasoningConfiguration.Default</c>): orders of magnitude above every
    /// measured corpus decision (the decided population spends hundreds of rule
    /// applications), so only a combinatorially explosive saturation reaches it
    /// and lands in <see cref="ContextPracticalReachGaps"/> rather than running
    /// without practical end — the per-application cost itself grows with the
    /// live clause set, so a ceiling far above this one is also a wall-clock
    /// wedge, not more reach.
    /// </summary>
    private const int ContextArmMaxInferences = 50_000;

    /// <summary>
    /// The fourth arm's decision surface: the context-saturation engine under
    /// the arm's inference ceiling composed with an abstaining sentinel
    /// fallback. A module the engine admits decides whole; one it does not
    /// admit falls to <see cref="DecideNotAdmitted"/> and surfaces as
    /// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> with empty
    /// context totals — the beyond-fragment signal — while an admitted module
    /// that exhausts the ceiling surfaces the same outcome with its spent
    /// totals reattached, the beyond-practical-reach signal.
    /// </summary>
    private static DescriptionLogicDelegate ContextArm { get; } =
        ReasoningEngines.ContextSaturation(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ContextArmMaxInferences), DecideNotAdmitted);

    /// <summary>The abstaining sentinel fallback: it decides no module, returning an on-budget abstention so a context-non-admitted module surfaces as beyond-fragment rather than borrowing another engine's verdict.</summary>
    /// <param name="module">The module the context engine did not admit.</param>
    /// <param name="cancellationToken">The token, unused because the sentinel does no work.</param>
    /// <returns>An abstaining decision carrying only the module's axiom count.</returns>
    private static ValueTask<ModuleDecision> DecideNotAdmitted(ReasoningModule module, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return new ValueTask<ModuleDecision>(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty with { ModuleAxiomCount = module.Axioms.Count }));
    }

    /// <summary>
    /// The absolute path a context-census seeding run appends its measured
    /// boundary exits to, or <c>null</c> for the strict census gate. Setting
    /// <c>VERITAS_SEED_CONTEXT_CENSUS</c> to a path re-derives the sets after a
    /// survey widening: every beyond-fragment premise and every refutation gap is
    /// recorded rather than checked. Unset, the census is exact — an unpinned exit,
    /// or a pin the engine now decides, fails the run.
    /// </summary>
    private static string? ContextCensusSeedSink { get; } = Environment.GetEnvironmentVariable("VERITAS_SEED_CONTEXT_CENSUS");

    /// <summary>Serialises appends to the seeding sink across the data-driven rows.</summary>
    private static Lock ContextCensusSeedGate { get; } = new();

    /// <summary>
    /// The premises whose axioms the context-saturation engine does not admit
    /// whole, so the sentinel-fallback seam abstains before any verdict: the
    /// arm's engine-relative fragment boundary, subsuming the module survey, the
    /// reserved-role scan, the second gate, and undecided-data delegation. A
    /// premise beyond the fragment without a pin, or a pinned premise the engine
    /// now decides, fails the run. Seeded by measurement over the corpus.
    /// </summary>
    private static HashSet<string> ContextFragmentGaps { get; } =
    [
        "Inconsistent Disjoint Dataproperties",
        "Inconsistent String Pattern with Disjoint Dataproperties",
        "Minus Infinity is not in owl:real",
        "WebOnt-Restriction-003",
        "WebOnt-Restriction-004",
        "consistent-dataproperty-disjointness",
    ];

    /// <summary>
    /// The premises the context engine admits whole but cannot decide within the
    /// arm's inference ceiling — the disjunctive combinatorial cases whose
    /// saturation is admitted but explosive — detected by
    /// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> carrying non-zero
    /// spent context totals. A practical-reach exit without a pin, a pinned id
    /// the engine now decides, and an id that migrates between this census and
    /// <see cref="ContextFragmentGaps"/> all fail the run. Seeded by measurement
    /// over the corpus.
    /// </summary>
    private static HashSet<string> ContextPracticalReachGaps { get; } =
    [
        "WebOnt-description-logic-201",
        "WebOnt-description-logic-208",
        "WebOnt-description-logic-209",
    ];

    /// <summary>
    /// The cases whose premise the context engine decides but whose refutation
    /// module it does not admit or cannot decide within the arm's inference
    /// ceiling, so the entailment or non-entailment cannot be settled within
    /// the arm's reach — detected by
    /// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> on the refutation
    /// module, never by a fragment-relative verdict (the arm's decided verdicts
    /// are whole). Paired with the reason. Any other refutation-boundary exit
    /// is unpinned and fails the run. The census is EMPTY: every refutation
    /// module the walk poses is decided inside the arm's reach — the
    /// diagonal-pinned symmetry probe pre-engine by the nominal-pinned-role
    /// clash face, whose five-step closed form sidesteps the equivalence
    /// machinery's paramodulation cycle — and the dictionary stays because the
    /// strict gate's pinned-but-decides and unpinned-exit fail shapes read it.
    /// </summary>
    private static Dictionary<string, string> ContextRefutationGaps { get; } = new(StringComparer.Ordinal);

    /// <summary>Runs one approved-status test case through the context-saturation fourth arm.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.DirectSemanticsDl)]
    public async Task RunApprovedContextSaturation(Owl2TestCase testCase)
    {
        await RunAndAssertContextSaturationAsync(testCase).ConfigureAwait(false);
    }

    /// <summary>Runs one proposed-status test case through the context-saturation fourth arm.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.DirectSemanticsDl)]
    public async Task RunProposedContextSaturation(Owl2TestCase testCase)
    {
        await RunAndAssertContextSaturationAsync(testCase).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads and maps the test documents and decides the case through the
    /// sentinel-fallback seam: a premise beyond the engine's fragment abstains and
    /// passes through <see cref="ContextFragmentGaps"/>; an in-fragment premise
    /// decides its consistency, and each entailment or non-entailment settles
    /// through the seam, a beyond-fragment refutation passing through
    /// <see cref="ContextRefutationGaps"/>.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <returns>The asynchronous decision.</returns>
    private async Task RunAndAssertContextSaturationAsync(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        bool isPositive = testCase.Kinds.Contains("PositiveEntailmentTest");
        bool isNegative = testCase.Kinds.Contains("NegativeEntailmentTest");
        bool isInconsistency = testCase.Kinds.Contains("InconsistencyTest");
        bool isConsistency = testCase.Kinds.Contains("ConsistencyTest");

        List<Quad>? maybePremise = LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise);
        if(maybePremise is not List<Quad> premiseQuads)
        {
            Assert.Fail($"{testCase.Identifier}: the test declares no premise document in a syntax the harness reads.");

            return;
        }

        premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
        OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
        if(premise.Diagnostics.HasErrors && MapFunctionalPremise(testCase) is OwlOntologyDocument functionalPremise)
        {
            premise = functionalPremise;
        }

        if(premise.Diagnostics.HasErrors)
        {
            AssertContextRefutationGap(testCase, "the premise does not map to structural form");

            return;
        }

        //The engine-relative scope gate: the premise is decided through the
        //sentinel-fallback seam, and an abstention is the beyond-fragment signal.
        ModuleDecision premiseDecision = await DecideContextAsync(premise, probe: null).ConfigureAwait(false);
        if(AssertContextFragmentBoundary(testCase, premiseDecision))
        {
            return;
        }

        //A decided outcome carries a whole verdict: the context engine's admitted
        //saturation covers the module, so the verdict is never fragment-relative.
        ModuleVerdict premiseVerdict = premiseDecision.Verdict!;

        if(isInconsistency)
        {
            Assert.IsFalse(premiseVerdict.IsConsistent, $"{testCase.Identifier}: the context engine should find the premise inconsistent.");

            return;
        }

        if(isConsistency)
        {
            Assert.IsTrue(premiseVerdict.IsConsistent, $"{testCase.Identifier}: the premise is consistent, but the context engine found a clash.");
        }

        if(isPositive
            && LoadQuads(testCase, testCase.RdfXmlConclusion, testCase.FunctionalConclusion) is List<Quad> conclusionQuads)
        {
            await AssertContextEntailedAsync(testCase, premise, conclusionQuads).ConfigureAwait(false);
        }

        if(isNegative
            && LoadQuads(testCase, testCase.RdfXmlNonConclusion, testCase.FunctionalNonConclusion) is List<Quad> nonConclusionQuads)
        {
            await AssertContextNotEntailedAsync(testCase, premise, nonConclusionQuads).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The positive arm: every logical conclusion axiom must be entailed, each by
    /// all of its refutation probes abstaining into inconsistency. A conclusion
    /// whose non-vacuous axioms share a blank node is a connected anonymous
    /// forest the per-axiom refutations cannot decompose soundly, so it routes
    /// to <see cref="TryFoldAnonymousForest"/> first: an eligible forest rolls
    /// each connected component up onto its named root as one probe and its
    /// axioms leave the per-axiom walk, and an ineligible one gaps the whole
    /// case. A refutation module the engine does not admit abstains and passes
    /// through <see cref="ContextRefutationGaps"/>; a cleanly consistent
    /// refutation over an admitted module is a genuine non-entailment and fails.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="premise">The mapped premise.</param>
    /// <param name="conclusionQuads">The conclusion document's triples.</param>
    /// <returns>The asynchronous decision.</returns>
    private async Task AssertContextEntailedAsync(Owl2TestCase testCase, OwlOntologyDocument premise, List<Quad> conclusionQuads)
    {
        OwlOntologyDocument conclusion = OwlRdfMapper.Map(conclusionQuads);

        //A blank node shared across two or more non-vacuous conclusion axioms is a
        //connected anonymous forest whose shared existential a per-axiom
        //refutation cannot decompose soundly. The rollup states each component as
        //the nested existential on its named root; a component the rollup declines
        //routes to the refutation gap rather than being false-certified.
        HashSet<int> consumed = [];
        List<ContextRefutationProbe> rollups = [];
        if(HasSharedAnonymousIndividual(conclusion.Axioms) && !TryFoldAnonymousForest(conclusion.Axioms, consumed, rollups))
        {
            AssertContextRefutationGap(testCase, "the conclusion is a connected anonymous-individual forest; per-axiom refutation is unsound and it needs an exact rollup");

            return;
        }

        foreach(ContextRefutationProbe rollup in rollups)
        {
            ModuleDecision rollupDecision = await DecideContextAsync(premise, rollup).ConfigureAwait(false);
            if(rollupDecision.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
            {
                AssertContextRefutationGap(testCase, rollupDecision.Statistics.ContextTotals.RuleApplications > 0
                    ? "a conclusion anonymous-forest rollup exhausted the context arm's inference ceiling"
                    : "a conclusion anonymous-forest rollup lies beyond the context engine's fragment");

                return;
            }

            if(rollupDecision.Verdict!.IsConsistent)
            {
                Assert.Fail($"{testCase.Identifier}: a conclusion anonymous-individual forest does not follow from the premise.");
            }
        }

        for(int index = 0; index < conclusion.Axioms.Length; index++)
        {
            OwlAxiom axiom = conclusion.Axioms[index];
            if(IsVacuous(axiom) || consumed.Contains(index))
            {
                continue;
            }

            if(ContextRefutations(axiom) is not List<ContextRefutationProbe> checks)
            {
                AssertContextRefutationGap(testCase, $"a conclusion {axiom.GetType().Name} has no refutation encoding in the context engine's fragment");

                return;
            }

            foreach(ContextRefutationProbe check in checks)
            {
                ModuleDecision decision = await DecideContextAsync(premise, check).ConfigureAwait(false);
                if(decision.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
                {
                    AssertContextRefutationGap(testCase, decision.Statistics.ContextTotals.RuleApplications > 0
                        ? $"a conclusion {axiom.GetType().Name} refutation exhausted the context arm's inference ceiling"
                        : $"a conclusion {axiom.GetType().Name} refutation lies beyond the context engine's fragment");

                    return;
                }

                if(decision.Verdict!.IsConsistent)
                {
                    Assert.Fail($"{testCase.Identifier}: a conclusion {axiom.GetType().Name} does not follow from the premise.");
                }
            }
        }
    }

    /// <summary>
    /// Whether a blank node appears in two or more of the conclusion's non-vacuous
    /// axioms. A shared blank node is a connected anonymous forest whose shared
    /// existential the per-axiom refutations cannot decompose soundly: each axiom's
    /// refutation probes only its own existential, so certifying them independently
    /// is weaker than the connected conclusion and would false-certify. Applied to
    /// the positive walk only; the negative walk refutes a single conjunct's
    /// existential, which is sound with or without a shared blank node.
    /// </summary>
    /// <param name="axioms">The conclusion axioms.</param>
    /// <returns><c>true</c> when a blank node is shared across non-vacuous axioms.</returns>
    private static bool HasSharedAnonymousIndividual(IReadOnlyList<OwlAxiom> axioms)
    {
        Dictionary<Utf8String, int> counts = new();
        List<RdfTerm> individuals = [];
        Stack<OwlClassExpression> expressions = new();
        HashSet<Utf8String> labels = [];
        foreach(OwlAxiom axiom in axioms)
        {
            if(IsVacuous(axiom))
            {
                continue;
            }

            individuals.Clear();
            expressions.Clear();
            labels.Clear();

            //The axiom's direct individual-position terms and its direct class
            //expressions come from the typed enumeration member; draining the
            //expression worklist reaches every nested individual, since a member
            //appends and pushes only its own direct terms and never descends.
            axiom.AppendMentionedIndividuals(individuals, expressions);
            while(expressions.Count > 0)
            {
                expressions.Pop().AppendMentionedIndividuals(individuals, expressions);
            }

            //The labels are distinct per axiom, so a blank node repeated inside one
            //axiom counts once toward the cross-axiom multiplicity the guard measures.
            foreach(RdfTerm individual in individuals)
            {
                if(individual is BlankNode blank)
                {
                    labels.Add(blank.Label);
                }
            }

            foreach(Utf8String label in labels)
            {
                counts.TryGetValue(label, out int seen);
                counts[label] = seen + 1;
            }
        }

        foreach(int seen in counts.Values)
        {
            if(seen >= 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rolls a conclusion's connected anonymous forest up onto its named roots.
    /// The non-vacuous axioms partition into components joined by shared blank
    /// labels; a component of one axiom carries no shared blank and stays in the
    /// per-axiom walk, and each larger component must be a rooted out-tree of
    /// existentially-read blanks — object-property edges plus class assertions on
    /// blanks, exactly one named node in source position, every blank of
    /// in-degree one and reachable from that root — which
    /// <see cref="TryFoldComponent"/> states as the nested existential on the
    /// root and denies with one probe. The rollup is EXACT for such a component:
    /// the nested existential is logically the component itself. An ineligible
    /// component declines the whole conclusion, which then keeps its
    /// refutation-gap exit, so a shape the rollup cannot state is never
    /// certified from its pieces.
    /// </summary>
    /// <param name="axioms">The conclusion axioms.</param>
    /// <param name="consumedToAppendTo">The positional indices of the axioms the rollup consumed, which the per-axiom walk then skips.</param>
    /// <param name="probesToAppendTo">The emitted probes, one per folded component.</param>
    /// <returns><c>true</c> when every component folded, <c>false</c> when one was ineligible.</returns>
    private static bool TryFoldAnonymousForest(IReadOnlyList<OwlAxiom> axioms, HashSet<int> consumedToAppendTo, List<ContextRefutationProbe> probesToAppendTo)
    {
        //The label inventory: per axiom the distinct blank labels it mentions, and
        //per label the axioms mentioning it — the adjacency the partition walks.
        List<HashSet<Utf8String>> axiomLabels = new(axioms.Count);
        Dictionary<Utf8String, List<int>> labelAxioms = new();
        List<RdfTerm> individuals = [];
        Stack<OwlClassExpression> expressions = new();
        for(int index = 0; index < axioms.Count; index++)
        {
            HashSet<Utf8String> labels = [];
            if(!IsVacuous(axioms[index]))
            {
                individuals.Clear();
                expressions.Clear();
                axioms[index].AppendMentionedIndividuals(individuals, expressions);
                while(expressions.Count > 0)
                {
                    expressions.Pop().AppendMentionedIndividuals(individuals, expressions);
                }

                foreach(RdfTerm individual in individuals)
                {
                    if(individual is BlankNode blank)
                    {
                        labels.Add(blank.Label);
                    }
                }
            }

            axiomLabels.Add(labels);
            foreach(Utf8String label in labels)
            {
                if(!labelAxioms.TryGetValue(label, out List<int>? members))
                {
                    members = [];
                    labelAxioms[label] = members;
                }

                members.Add(index);
            }
        }

        HashSet<int> visited = [];
        Stack<int> pending = new();
        List<int> component = [];
        List<int> consumed = [];
        List<ContextRefutationProbe> probes = [];
        for(int index = 0; index < axioms.Count; index++)
        {
            if(axiomLabels[index].Count == 0 || !visited.Add(index))
            {
                continue;
            }

            component.Clear();
            pending.Push(index);
            while(pending.Count > 0)
            {
                int member = pending.Pop();
                component.Add(member);
                foreach(Utf8String label in axiomLabels[member])
                {
                    foreach(int neighbour in labelAxioms[label])
                    {
                        if(visited.Add(neighbour))
                        {
                            pending.Push(neighbour);
                        }
                    }
                }
            }

            if(component.Count < 2)
            {
                //A blank confined to one axiom is the single-axiom anonymous shape
                //the per-axiom arms already encode exactly.
                continue;
            }

            if(TryFoldComponent(axioms, component) is not ContextRefutationProbe probe)
            {
                return false;
            }

            probes.Add(probe);
            consumed.AddRange(component);
        }

        foreach(int member in consumed)
        {
            consumedToAppendTo.Add(member);
        }

        probesToAppendTo.AddRange(probes);

        return true;
    }

    /// <summary>
    /// States one connected component as the denial of the nested existential on
    /// its named root, or declines it. The component is eligible when every axiom
    /// is an object-property edge or a class assertion on a blank individual
    /// whose asserted class mentions no blank, exactly one named node stands in
    /// source position, every blank has in-degree exactly one, and every blank is
    /// reachable from the root — a rooted out-tree, cycles excluded structurally.
    /// In-degree two or more declines: two parents each having SOME successor
    /// never entails a SHARED successor satisfying both, so decomposing such a
    /// component would certify more than the conclusion states. The fold itself
    /// runs an iterative post-order over the tree: a node becomes the
    /// intersection of its asserted classes, one existential per blank-target
    /// edge, and one individual-value restriction per named-target leaf, a
    /// singleton collapsing to its element, an empty node becoming its parent
    /// existential's implicit <c>owl:Thing</c> filler, and an <c>owl:Thing</c>
    /// conjunct dropping as the constraint it is not.
    /// </summary>
    /// <param name="axioms">The conclusion axioms.</param>
    /// <param name="component">The positional indices of the component's axioms.</param>
    /// <returns>The component's probe, or <c>null</c> when it is ineligible.</returns>
    private static ContextRefutationProbe? TryFoldComponent(IReadOnlyList<OwlAxiom> axioms, List<int> component)
    {
        Dictionary<RdfTerm, List<OwlClassExpression>> nodeClasses = new();
        Dictionary<RdfTerm, List<OwlObjectPropertyAssertionAxiom>> outEdges = new();
        Dictionary<Utf8String, int> inDegrees = new();
        HashSet<Utf8String> blanks = [];
        List<RdfTerm> individuals = [];
        Stack<OwlClassExpression> expressions = new();
        NamedNode? root = null;
        foreach(int index in component)
        {
            switch(axioms[index])
            {
                case OwlObjectPropertyAssertionAxiom edge:
                {
                    switch(edge.Source)
                    {
                        case NamedNode named:
                        {
                            if(root is not null && !root.Equals(named))
                            {
                                return null;
                            }

                            root = named;

                            break;
                        }

                        case BlankNode blankSource:
                        {
                            blanks.Add(blankSource.Label);

                            break;
                        }

                        default:
                        {
                            return null;
                        }
                    }

                    switch(edge.Target)
                    {
                        case BlankNode blankTarget:
                        {
                            blanks.Add(blankTarget.Label);
                            inDegrees.TryGetValue(blankTarget.Label, out int seen);
                            inDegrees[blankTarget.Label] = seen + 1;

                            break;
                        }

                        case NamedNode:
                        {
                            break;
                        }

                        default:
                        {
                            return null;
                        }
                    }

                    if(!outEdges.TryGetValue(edge.Source, out List<OwlObjectPropertyAssertionAxiom>? successors))
                    {
                        successors = [];
                        outEdges[edge.Source] = successors;
                    }

                    successors.Add(edge);

                    break;
                }

                case OwlClassAssertionAxiom { Individual: BlankNode blankIndividual } classAssertion:
                {
                    //A blank inside the asserted class expression would read as a
                    //named individual once the expression becomes a conjunct, so a
                    //class position mentioning one declines the component.
                    individuals.Clear();
                    expressions.Clear();
                    expressions.Push(classAssertion.Class);
                    while(expressions.Count > 0)
                    {
                        expressions.Pop().AppendMentionedIndividuals(individuals, expressions);
                    }

                    foreach(RdfTerm mentioned in individuals)
                    {
                        if(mentioned is BlankNode)
                        {
                            return null;
                        }
                    }

                    blanks.Add(blankIndividual.Label);
                    if(!nodeClasses.TryGetValue(blankIndividual, out List<OwlClassExpression>? conjuncts))
                    {
                        conjuncts = [];
                        nodeClasses[blankIndividual] = conjuncts;
                    }

                    conjuncts.Add(classAssertion.Class);

                    break;
                }

                default:
                {
                    return null;
                }
            }
        }

        if(root is not NamedNode namedRoot)
        {
            return null;
        }

        foreach(Utf8String label in blanks)
        {
            if(!inDegrees.TryGetValue(label, out int degree) || degree != 1)
            {
                return null;
            }
        }

        //The pre-order walk from the root: a node reached twice is impossible under
        //in-degree one, and a blank the walk never reaches is not rooted at all.
        HashSet<Utf8String> reached = [];
        List<RdfTerm> order = [];
        Stack<RdfTerm> frontier = new();
        frontier.Push(namedRoot);
        while(frontier.Count > 0)
        {
            RdfTerm node = frontier.Pop();
            order.Add(node);
            if(!outEdges.TryGetValue(node, out List<OwlObjectPropertyAssertionAxiom>? successors))
            {
                continue;
            }

            foreach(OwlObjectPropertyAssertionAxiom edge in successors)
            {
                if(edge.Target is BlankNode blankTarget && reached.Add(blankTarget.Label))
                {
                    frontier.Push(edge.Target);
                }
            }
        }

        if(reached.Count != blanks.Count)
        {
            return null;
        }

        //Reverse pre-order visits every child before its parent, so a node's
        //existential fillers are already folded when the node itself folds.
        Dictionary<RdfTerm, OwlClassExpression?> folds = new();
        for(int position = order.Count - 1; position >= 0; position--)
        {
            RdfTerm node = order[position];
            List<OwlClassExpression> conjuncts = [];
            if(nodeClasses.TryGetValue(node, out List<OwlClassExpression>? classes))
            {
                foreach(OwlClassExpression asserted in classes)
                {
                    if(!IsThingClass(asserted))
                    {
                        conjuncts.Add(asserted);
                    }
                }
            }

            if(outEdges.TryGetValue(node, out List<OwlObjectPropertyAssertionAxiom>? successors))
            {
                foreach(OwlObjectPropertyAssertionAxiom edge in successors)
                {
                    OwlObjectPropertyReference property = new(edge.Property);
                    conjuncts.Add(edge.Target switch
                    {
                        BlankNode blankTarget => new OwlObjectSomeValuesFrom(property, folds[blankTarget] ?? ThingReference),
                        _ => new OwlObjectHasValue(property, edge.Target),
                    });
                }
            }

            folds[node] = conjuncts.Count switch
            {
                0 => null,
                1 => conjuncts[0],
                _ => new OwlObjectIntersectionOf(conjuncts),
            };
        }

        if(folds[namedRoot] is not OwlClassExpression rooted)
        {
            return null;
        }

        return new ContextRefutationProbe([new OwlClassAssertionAxiom(new OwlObjectComplementOf(rooted), namedRoot) { Origin = ContextWitnessQuad() }]);
    }

    /// <summary>Whether a folded conjunct is the universal class: an <c>owl:Thing</c> conjunct constrains nothing, so it is dropped rather than carried into the rolled-up existential.</summary>
    /// <param name="expression">The asserted class expression.</param>
    /// <returns><c>true</c> when the expression is the <c>owl:Thing</c> reference.</returns>
    private static bool IsThingClass(OwlClassExpression expression)
    {
        return expression is OwlClassReference reference
            && reference.Class.Iri.Span.SequenceEqual(OwlVocabulary.Thing.Span);
    }

    /// <summary>
    /// The negative arm: the non-conclusion must not be entailed, which one
    /// logical axiom with a cleanly consistent refutation witnesses. An axiom whose
    /// refutations all clash is entailed; if every axiom is entailed the test
    /// fails, and a walk that could settle no axiom — every refutation module
    /// beyond the fragment or unencodable — passes through
    /// <see cref="ContextRefutationGaps"/>.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="premise">The mapped premise.</param>
    /// <param name="nonConclusionQuads">The non-conclusion document's triples.</param>
    /// <returns>The asynchronous decision.</returns>
    private async Task AssertContextNotEntailedAsync(Owl2TestCase testCase, OwlOntologyDocument premise, List<Quad> nonConclusionQuads)
    {
        OwlOntologyDocument nonConclusion = OwlRdfMapper.Map(nonConclusionQuads);
        bool isUndecided = false;

        foreach(OwlAxiom axiom in nonConclusion.Axioms)
        {
            if(IsVacuous(axiom))
            {
                continue;
            }

            if(ContextRefutations(axiom) is not List<ContextRefutationProbe> checks)
            {
                isUndecided = true;

                continue;
            }

            foreach(ContextRefutationProbe check in checks)
            {
                ModuleDecision decision = await DecideContextAsync(premise, check).ConfigureAwait(false);
                if(decision.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
                {
                    isUndecided = true;

                    break;
                }

                if(!decision.Verdict!.IsConsistent)
                {
                    continue;
                }

                //A cleanly consistent counterexample: this axiom is not entailed,
                //so the non-conclusion does not follow.
                return;
            }
        }

        if(isUndecided)
        {
            AssertContextRefutationGap(testCase, "no non-conclusion axiom settles either way within the context engine's fragment");

            return;
        }

        Assert.Fail($"{testCase.Identifier}: the non-conclusion follows from the premise but must not.");
    }

    /// <summary>
    /// The context arm's refutation checks of a conclusion axiom: the shared
    /// class-assertion encodings, supplemented — for this arm only — by the two
    /// individual-equality encodings: the premise
    /// beside <c>DifferentIndividuals(a, b)</c> is unsatisfiable exactly when
    /// <c>sameAs(a, b)</c> is entailed (the ground key join's face), and the
    /// premise beside <c>SameIndividual(a, b)</c> is unsatisfiable exactly when
    /// <c>a ≉ b</c> is entailed — the DifferentIndividuals-CONCLUSION face,
    /// pairwise over the conclusion's members (an n-ary distinctness is the
    /// conjunction of its pairs, so entailment demands every pairwise probe
    /// inconsistent). Both supplements cover named individuals
    /// only (an anonymous conclusion individual is existentially read and cannot
    /// be skolemised into a told (in)equality). Three further object-assertion arms
    /// cover the property and anonymous-class conclusions: an anonymous-target
    /// object-property assertion on a named source becomes the complement of an
    /// existential over that property (arm A); a named-to-named assertion becomes
    /// the exact-pair <c>NegativeObjectPropertyAssertion</c> (arm C); and an
    /// anonymous-individual class assertion becomes the class forced empty by
    /// <c>SubClassOf(C, owl:Nothing)</c> (arm B). Three further arms skolemize
    /// the role axioms whose negation needs fresh witnesses: a symmetry becomes
    /// one probe carrying an edge and its reverse's denial (arm S), a
    /// transitivity one probe carrying a two-step path and its shortcut's denial
    /// (arm T), and a property equivalence two probes, one per inclusion, since
    /// entailment demands both directions (arm E). Those three take a named
    /// property only; an inverse property expression keeps <c>null</c>, and so do
    /// the remaining characteristics, whose skolemizations no case exercises.
    /// Every arm lives on the context
    /// walk alone: the shared builder and its two tableau consumers are untouched,
    /// so tableau inertness holds structurally rather than by assertion.
    /// </summary>
    /// <param name="axiom">The conclusion axiom.</param>
    /// <returns>The probes, or <c>null</c> when the axiom kind has no context encoding.</returns>
    private static List<ContextRefutationProbe>? ContextRefutations(OwlAxiom axiom)
    {
        if(Refutations(axiom) is List<OwlClassAssertionAxiom> shared)
        {
            List<ContextRefutationProbe> checks = new(shared.Count);
            foreach(OwlClassAssertionAxiom check in shared)
            {
                checks.Add(new ContextRefutationProbe([check]));
            }

            return checks;
        }

        if(axiom is OwlSameIndividualAxiom { First: NamedNode first, Second: NamedNode second })
        {
            return [new ContextRefutationProbe([new OwlDifferentIndividualsAxiom([first, second]) { Origin = ContextWitnessQuad() }])];
        }

        if(axiom is OwlDifferentIndividualsAxiom different && different.Individuals.Count >= 2)
        {
            List<ContextRefutationProbe> pairs = [];
            for(int i = 0; i < different.Individuals.Count; i++)
            {
                if(different.Individuals[i] is not NamedNode)
                {
                    return null;
                }

                for(int j = i + 1; j < different.Individuals.Count; j++)
                {
                    if(different.Individuals[j] is not NamedNode)
                    {
                        return null;
                    }

                    pairs.Add(new ContextRefutationProbe([new OwlSameIndividualAxiom(different.Individuals[i], different.Individuals[j]) { Origin = ContextWitnessQuad() }]));
                }
            }

            return pairs;
        }

        if(axiom is OwlObjectPropertyAssertionAxiom edge)
        {
            if(edge.Source is not NamedNode edgeSource)
            {
                //An anonymous source is not encodable as a per-axiom refutation:
                //its existential is over the edge's subject, not a named root.
                return null;
            }

            if(edge.Target is NamedNode edgeTarget)
            {
                //Arm C: both endpoints are named, so the told pair is a single
                //ground fact its negative assertion denies exactly.
                return [new ContextRefutationProbe([new OwlNegativeObjectPropertyAssertionAxiom(edgeSource, new OwlObjectPropertyReference(edge.Property), edgeTarget) { Origin = ContextWitnessQuad() }])];
            }

            //Arm A: the anonymous target reads existentially, so the named source
            //having no property-successor at all is the exact negation.
            return [new ContextRefutationProbe([new OwlClassAssertionAxiom(new OwlObjectComplementOf(new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(edge.Property), ThingReference)), edgeSource) { Origin = ContextWitnessQuad() }])];
        }

        if(axiom is OwlClassAssertionAxiom { Individual: not NamedNode } anonymousClass)
        {
            //Arm B: an anonymous individual reads existentially, so forcing the
            //asserted class empty is the exact negation.
            return [new ContextRefutationProbe([new OwlSubClassOfAxiom(anonymousClass.Class, NothingReference) { Origin = ContextWitnessQuad() }])];
        }

        if(axiom is OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Symmetric, Property: OwlObjectPropertyReference symmetric })
        {
            //Arm S: symmetry fails exactly when some edge's reverse is absent, so
            //the fresh witnesses carry a told edge beside the denial of its reverse.
            return
            [
                new ContextRefutationProbe(
                [
                    SkolemEdge(symmetric, ContextSkolemU, ContextSkolemV),
                    SkolemEdgeDenial(symmetric, ContextSkolemV, ContextSkolemU),
                ]),
            ];
        }

        if(axiom is OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Transitive, Property: OwlObjectPropertyReference transitive })
        {
            //Arm T: transitivity fails exactly when a two-step path's shortcut is
            //absent, so the fresh witnesses carry the path beside the shortcut's denial.
            return
            [
                new ContextRefutationProbe(
                [
                    SkolemEdge(transitive, ContextSkolemU, ContextSkolemV),
                    SkolemEdge(transitive, ContextSkolemV, ContextSkolemW),
                    SkolemEdgeDenial(transitive, ContextSkolemU, ContextSkolemW),
                ]),
            ];
        }

        if(axiom is OwlEquivalentObjectPropertiesAxiom { First: OwlObjectPropertyReference left, Second: OwlObjectPropertyReference right })
        {
            //Arm E: a property equivalence is the conjunction of two inclusions,
            //so each direction is skolemized on its own and entailment demands
            //both probes inconsistent.
            return
            [
                new ContextRefutationProbe(
                [
                    SkolemEdge(left, ContextSkolemU, ContextSkolemW),
                    SkolemEdgeDenial(right, ContextSkolemU, ContextSkolemW),
                ]),
                new ContextRefutationProbe(
                [
                    SkolemEdge(right, ContextSkolemU, ContextSkolemW),
                    SkolemEdgeDenial(left, ContextSkolemU, ContextSkolemW),
                ]),
            ];
        }

        return null;
    }

    /// <summary>
    /// One inconsistency check of the context walk: the axioms added to the
    /// premise as a single module. A one-axiom probe is the ordinary
    /// class-assertion or ground counterexample; a multi-axiom probe is a
    /// skolemized negation whose fresh witnesses only mean one thing together,
    /// so its axioms are never posed separately.
    /// </summary>
    /// <param name="Axioms">The probe's axioms, added to the premise as one module.</param>
    private sealed record ContextRefutationProbe(IReadOnlyList<OwlAxiom> Axioms);

    /// <summary>A told edge between two fresh skolem witnesses, the positive half of a skolemized role-axiom negation.</summary>
    /// <param name="property">The named property the edge is over.</param>
    /// <param name="source">The edge's source witness.</param>
    /// <param name="target">The edge's target witness.</param>
    /// <returns>The synthesized assertion.</returns>
    private static OwlObjectPropertyAssertionAxiom SkolemEdge(OwlObjectPropertyReference property, NamedNode source, NamedNode target)
    {
        return new OwlObjectPropertyAssertionAxiom(source, property.Named, target) { Origin = ContextWitnessQuad() };
    }

    /// <summary>
    /// The denial of one edge between fresh skolem witnesses, stated in the
    /// concept language: <c>ObjectHasValue(P, y)</c> on <c>x</c> is the exact
    /// concept reading of the ground edge <c>P(x, y)</c>, so its complement on
    /// the source is the exact edge denial. The concept form is deliberate — a
    /// told <c>NegativeObjectPropertyAssertion</c> holds its obligation in the
    /// ground layer, which cannot see a merge-derived edge, while the concept
    /// form is a class fact the clause calculus resolves directly.
    /// </summary>
    /// <param name="property">The named property the denied edge is over.</param>
    /// <param name="source">The denied edge's source witness.</param>
    /// <param name="target">The denied edge's target witness.</param>
    /// <returns>The synthesized assertion.</returns>
    private static OwlClassAssertionAxiom SkolemEdgeDenial(OwlObjectPropertyReference property, NamedNode source, NamedNode target)
    {
        return new OwlClassAssertionAxiom(new OwlObjectComplementOf(new OwlObjectHasValue(property, target)), source) { Origin = ContextWitnessQuad() };
    }

    /// <summary>The <c>owl:Nothing</c> class reference used to force a class empty in an anonymous class-assertion refutation.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(OwlVocabulary.Nothing));

    /// <summary>The arm's degenerate self-referential origin quad for a synthesized refutation axiom.</summary>
    /// <returns>The witness origin quad.</returns>
    private static Quad ContextWitnessQuad()
    {
        return new Quad(ContextWitness, ContextWitness, ContextWitness, Graph: null);
    }

    /// <summary>Decides the premise's axioms, optionally extended by one refutation probe's axioms, as one module through the sentinel-fallback seam.</summary>
    /// <param name="premise">The mapped premise.</param>
    /// <param name="probe">The counterexample probe to add, or <c>null</c> for the premise alone.</param>
    /// <returns>The module decision.</returns>
    private async Task<ModuleDecision> DecideContextAsync(OwlOntologyDocument premise, ContextRefutationProbe? probe)
    {
        List<OwlAxiom> axioms = new(premise.Axioms.Length + (probe?.Axioms.Count ?? 0));
        axioms.AddRange(premise.Axioms);
        if(probe is not null)
        {
            axioms.AddRange(probe.Axioms);
        }

        ReasoningModule module = new(axioms, Violations: []);

        return await ContextArm(module, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a premise-level boundary against the pinned
    /// <see cref="ContextFragmentGaps"/> and
    /// <see cref="ContextPracticalReachGaps"/> censuses: an abstaining seam
    /// decision is a beyond-fragment premise when its context totals are empty
    /// (the sentinel's non-admission) and a beyond-practical-reach premise when
    /// they carry spent rule applications (an admitted saturation the arm's
    /// inference ceiling stopped); either returns <c>true</c> so the caller
    /// abstains. A decided premise is in-reach (returns <c>false</c>). Under the
    /// strict gate an unpinned exit, a pinned premise the engine now decides,
    /// and an id pinned in the wrong census all fail; under a seeding run the
    /// exit is recorded instead.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="decision">The premise's seam decision.</param>
    /// <returns><c>true</c> when the premise is beyond the arm's reach and the caller should abstain.</returns>
    private static bool AssertContextFragmentBoundary(Owl2TestCase testCase, ModuleDecision decision)
    {
        if(decision.Outcome != ReasoningDecisionOutcome.AbstainedBudget)
        {
            if(ContextCensusSeedSink is null && ContextFragmentGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: pinned as a context fragment gap but the context engine now decides the premise whole; remove it from ContextFragmentGaps.");
            }

            if(ContextCensusSeedSink is null && ContextPracticalReachGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: pinned as a context practical-reach gap but the context engine now decides the premise whole; remove it from ContextPracticalReachGaps.");
            }

            return false;
        }

        bool exhausted = decision.Statistics.ContextTotals.RuleApplications > 0;
        if(ContextCensusSeedSink is string sink)
        {
            RecordContextSeed(sink, exhausted ? "PRACTICAL" : "FRAGMENT", testCase.Identifier, reason: null);

            return true;
        }

        if(exhausted)
        {
            if(!ContextPracticalReachGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: the admitted premise exhausted the arm's inference ceiling but is not pinned; add it to ContextPracticalReachGaps.");
            }

            if(ContextFragmentGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: measured beyond practical reach but still pinned as a fragment gap; remove it from ContextFragmentGaps.");
            }
        }
        else
        {
            if(!ContextFragmentGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: the premise lies beyond the context engine's fragment but is not pinned; add it to ContextFragmentGaps.");
            }

            if(ContextPracticalReachGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: measured beyond the fragment but still pinned as a practical-reach gap; remove it from ContextPracticalReachGaps.");
            }
        }

        return true;
    }

    /// <summary>Resolves a conclusion-level or setup-level boundary against the pinned <see cref="ContextRefutationGaps"/> census: a pinned case passes, an unpinned one fails so the entry is added; under a seeding run the gap is recorded instead.</summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="reason">The boundary the case hit, for the failure message and the seeding record.</param>
    private static void AssertContextRefutationGap(Owl2TestCase testCase, string reason)
    {
        if(ContextCensusSeedSink is string sink)
        {
            RecordContextSeed(sink, "REFUTATION", testCase.Identifier, reason);

            return;
        }

        if(!ContextRefutationGaps.ContainsKey(testCase.Identifier))
        {
            Assert.Fail($"{testCase.Identifier}: unpinned context refutation gap ({reason}); extend the context engine or pin it in ContextRefutationGaps.");
        }
    }

    /// <summary>The environment variable naming the enumeration-decider mover probe's absolute output path; unset means the probe passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string EnumerationMoverProbeVariable = "VERITAS_ENUMCSP_W3C_MOVERS";

    /// <summary>
    /// The enumeration-decider W3C mover probe (the census measurement
    /// instrument): measurement scaffolding that runs only when
    /// <see cref="EnumerationMoverProbeVariable"/> names an absolute output
    /// file, and otherwise passes without measuring. When it runs, every
    /// premise in the probe's own population is decided through the arm's
    /// production seam — the lit decider included — and each id's outcome,
    /// spent attempts, recognizer habitat class, window measurements, and
    /// per-face decider counters — the repairing family's five census fields and
    /// the modal-gadget family's six included — are written to the named file: the
    /// adjudication read that attributes why a candidate mover moved or stayed.
    /// That population is a local array, deliberately decoupled from
    /// <see cref="ContextPracticalReachGaps"/>: a probe that read the census it
    /// adjudicates would lose sight of every id the census releases, exactly
    /// where a flip needs measuring, and would never see the one id whose
    /// premise decides today from a refutation-gap home — the id on which a new
    /// non-target decision is exhibited at all. The probe measures decision
    /// movement; habitat labels move with the recognizer and are recorded, not
    /// gated.
    /// </summary>
    /// <returns>The asynchronous read.</returns>
    [TestMethod]
    public async Task EnumerationDeciderMoverProbeWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(EnumerationMoverProbeVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the enumeration-decider mover probe: set " + EnumerationMoverProbeVariable + " to an absolute output path to run it.");

            return;
        }

        //The probe's own population, independent of every census set: the
        //disjunctive combinatorial premises the arm admits but does not decide
        //within its inference ceiling, together with the restriction-rich ground
        //pair the repairing construction certifies, plus the one id whose
        //premise decides today from a refutation-gap home and whose shape could
        //take a restriction-rich ground label — the only id on which a new
        //non-target decision can be exhibited.
        string[] moverProbePopulation =
        [
            "WebOnt-description-logic-201",
            "WebOnt-description-logic-208",
            "WebOnt-description-logic-209",
            "WebOnt-description-logic-623",
            "WebOnt-description-logic-661",
            "WebOnt-miscellaneous-001",
            "WebOnt-miscellaneous-002",
            "WebOnt-SymmetricProperty-002",
        ];

        StringBuilder report = new();
        report.AppendLine("enumeration-decider W3C mover probe: the probe's own population through the production arm (lit decider)");
        foreach(string suite in (string[])["approved", "proposed"])
        {
            foreach(object[] row in new Owl2ManifestDataAttribute(suite, "all.rdf", Owl2TestRemit.DirectSemanticsDl).GetData(typeof(W3cOwl2DirectTests).GetMethod(nameof(EnumerationDeciderMoverProbeWritesTheRead))!))
            {
                if(row is not [Owl2TestCase testCase] || Array.IndexOf(moverProbePopulation, testCase.Identifier) < 0)
                {
                    continue;
                }

                if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
                {
                    report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{suite}/{testCase.Identifier} | premise unreadable");
                    continue;
                }

                premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
                OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
                if(premise.Diagnostics.HasErrors)
                {
                    report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{suite}/{testCase.Identifier} | premise unmapped");
                    continue;
                }

                ModuleDecision decision = await DecideContextAsync(premise, probe: null).ConfigureAwait(false);
                ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                    $"{suite}/{testCase.Identifier} | outcome={decision.Outcome} | verdict={(decision.Verdict is null ? "none" : decision.Verdict.IsConsistent ? "consistent" : "inconsistent")} | attempts={totals.InferenceAttempts} | habitat={totals.EnumerationHabitat} | members={totals.EnumerationMemberUniverse} | population={totals.EnumerationCountedPopulation} | clique={totals.EnumerationDistinctCliqueSize} | cap={totals.EnumerationCapBound} | windowExceeded hops={totals.EnumerationWindowExceededChainHops} population={totals.EnumerationWindowExceededPopulation} members={totals.EnumerationWindowExceededMembers} classes={totals.EnumerationWindowExceededClasses} | faces clashes={totals.EnumerationDeciderClashes} certifications={totals.EnumerationDeciderCertifications} refutations={totals.EnumerationDeciderRefutations} | repairing carriers={totals.RepairingCarrierCount} edges={totals.RepairingCommittedEdgeCount} clashes={totals.RepairingDeciderClashes} certifications={totals.RepairingDeciderCertifications} windowExceeded={totals.RepairingWindowExceededCarriers} | modal spawned={totals.ModalExpansionNodesSpawned} depth={totals.ModalExpansionMaxDepthReached} label={totals.ModalExpansionPeakLabelSize} edges={totals.ModalExpansionEdgesMaterialised} applications={totals.ModalExpansionRuleApplications} clashes={totals.ModalExpansionDeciderClashes} windowExceeded={totals.ModalExpansionWindowSilences} | modalGadget freeAtoms={totals.ModalGadgetFreeAtomCount} signatures={totals.ModalGadgetSignatureCount} nodes={totals.ModalGadgetNodesBuilt} clashes={totals.ModalGadgetDeciderClashes} certifications={totals.ModalGadgetDeciderCertifications} windowExceeded={totals.ModalGadgetWindowSilences} | nominalPinnedRole members={totals.NominalPinnedRoleMemberCount} pinned={totals.NominalPinnedRolePinnedEdgeCount} denied={totals.NominalPinnedRoleDeniedEdgeCount} clashes={totals.NominalPinnedRoleDeciderClashes} windowExceeded={totals.NominalPinnedRoleWindowExceededMembers}");
            }

        }

        await File.WriteAllTextAsync(outputPath, report.ToString(), TestContext.CancellationToken).ConfigureAwait(false);
        TestContext.WriteLine("Enumeration-decider mover probe written to " + outputPath + ".");
    }

    /// <summary>The environment variable naming the whole-corpus habitat-label sweep's absolute output path; unset means the sweep passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string EnumerationHabitatLabelSweepVariable = "VERITAS_ENUMCSP_W3C_LABELS";

    /// <summary>
    /// The whole-corpus habitat-LABEL sweep: measurement scaffolding that runs
    /// only when <see cref="EnumerationHabitatLabelSweepVariable"/> names an
    /// absolute output file, and otherwise passes without measuring. When it runs,
    /// EVERY manifest premise of both suites is surveyed — never decided, so the
    /// sweep costs a syntactic pass and no inference — and each id's recognizer
    /// habitat label is written beside the branching modal-gadget predicate's own
    /// claim on the same module and the composition-layer count that predicate's
    /// threshold is charged against.
    /// The sweep is the ONLY corpus-wide label instrument the ladder has: decision
    /// movement is measured corpus-wide by the strict suite and on a population of
    /// eight by <see cref="EnumerationDeciderMoverProbeWritesTheRead"/>, while a
    /// label stolen from a module a sibling face labels and stays silent on moves
    /// no verdict and is invisible to every other instrument. A recognizer
    /// ordering change is therefore adjudicated by running this sweep before the
    /// change and after it and diffing the two reads by the same instrument.
    /// The output is a SEPARATE file from the seeded census sink, which carries no
    /// habitat column and whose format sibling gates diff byte-wise. The artifact
    /// is LF-terminated throughout per the repository's line-ending direction.
    /// </summary>
    /// <returns>The asynchronous read.</returns>
    [TestMethod]
    public async Task EnumerationHabitatLabelSweepWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(EnumerationHabitatLabelSweepVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the whole-corpus habitat-label sweep: set " + EnumerationHabitatLabelSweepVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.Append("enumeration-CSP whole-corpus habitat-label sweep: every manifest premise's survey label beside the branching modal-gadget predicate's claim").Append('\n');
        foreach(string suite in (string[])["approved", "proposed"])
        {
            foreach(object[] row in new Owl2ManifestDataAttribute(suite, "all.rdf", Owl2TestRemit.DirectSemanticsDl).GetData(typeof(W3cOwl2DirectTests).GetMethod(nameof(EnumerationHabitatLabelSweepWritesTheRead))!))
            {
                if(row is not [Owl2TestCase testCase])
                {
                    continue;
                }

                if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
                {
                    report.Append(System.Globalization.CultureInfo.InvariantCulture, $"{suite}/{testCase.Identifier} | premise unreadable").Append('\n');
                    continue;
                }

                premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
                OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
                if(premise.Diagnostics.HasErrors)
                {
                    report.Append(System.Globalization.CultureInfo.InvariantCulture, $"{suite}/{testCase.Identifier} | premise unmapped").Append('\n');
                    continue;
                }

                ReasoningModule module = new([.. premise.Axioms], Violations: []);
                ContextModuleSurveyResult survey = ContextModuleSurvey.Survey(module);
                report.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{suite}/{testCase.Identifier} | habitat={survey.EnumerationHabitat} | admitted={survey.Admitted} | shapeK={ContextHabitatRecognizer.TryMatchModalGadgetTreeShape(module)} | compositions={ContextHabitatRecognizer.CountModalGadgetCompositions(module)}").Append('\n');
            }
        }

        await File.WriteAllTextAsync(outputPath, report.ToString(), TestContext.CancellationToken).ConfigureAwait(false);
        TestContext.WriteLine("Whole-corpus habitat-label sweep written to " + outputPath + ".");
    }

    /// <summary>The environment variable naming the RL-remit habitat-label sweep's absolute output path; unset means the sweep passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string EnumerationHabitatRlLabelSweepVariable = "VERITAS_ENUMCSP_W3C_RL_LABELS";

    /// <summary>
    /// The RL-remit habitat-LABEL sweep: measurement scaffolding that runs only
    /// when <see cref="EnumerationHabitatRlLabelSweepVariable"/> names an
    /// absolute output file, and otherwise passes without measuring. It is the
    /// <see cref="EnumerationHabitatLabelSweepWritesTheRead"/> read taken over
    /// the RL-marked remit instead of the Direct-Semantics DL remit: every
    /// RL-marked manifest premise of both suites is surveyed — never decided —
    /// and each id's recognizer habitat label is written in the same line shape.
    /// The remit is reachable in production: the RL production-path twin opens
    /// the mutable engine with beyond-RL delegation wired, the delegation gate is
    /// the engine's own profile check over the premise rather than the manifest's
    /// RL marker, and a delegated module reaches the recognizer through the
    /// context seam's survey — so a recognizer ordering change is adjudicated
    /// over this remit by the same before-and-after diff discipline as over the
    /// DL remit. The artifact is LF-terminated throughout per the repository's
    /// line-ending direction.
    /// </summary>
    /// <returns>The asynchronous read.</returns>
    [TestMethod]
    public async Task EnumerationHabitatRlLabelSweepWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(EnumerationHabitatRlLabelSweepVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the RL-remit habitat-label sweep: set " + EnumerationHabitatRlLabelSweepVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.Append("enumeration-CSP RL-remit habitat-label sweep: every RL-marked manifest premise's survey label beside the branching modal-gadget predicate's claim").Append('\n');
        foreach(string suite in (string[])["approved", "proposed"])
        {
            foreach(object[] row in new Owl2ManifestDataAttribute(suite, "all.rdf", Owl2TestRemit.RlMarked).GetData(typeof(W3cOwl2DirectTests).GetMethod(nameof(EnumerationHabitatRlLabelSweepWritesTheRead))!))
            {
                if(row is not [Owl2TestCase testCase])
                {
                    continue;
                }

                if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
                {
                    report.Append(System.Globalization.CultureInfo.InvariantCulture, $"{suite}/{testCase.Identifier} | premise unreadable").Append('\n');
                    continue;
                }

                premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
                OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
                if(premise.Diagnostics.HasErrors)
                {
                    report.Append(System.Globalization.CultureInfo.InvariantCulture, $"{suite}/{testCase.Identifier} | premise unmapped").Append('\n');
                    continue;
                }

                ReasoningModule module = new([.. premise.Axioms], Violations: []);
                ContextModuleSurveyResult survey = ContextModuleSurvey.Survey(module);
                report.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"{suite}/{testCase.Identifier} | habitat={survey.EnumerationHabitat} | admitted={survey.Admitted} | shapeK={ContextHabitatRecognizer.TryMatchModalGadgetTreeShape(module)} | compositions={ContextHabitatRecognizer.CountModalGadgetCompositions(module)}").Append('\n');
            }
        }

        await File.WriteAllTextAsync(outputPath, report.ToString(), TestContext.CancellationToken).ConfigureAwait(false);
        TestContext.WriteLine("RL-remit habitat-label sweep written to " + outputPath + ".");
    }

    /// <summary>The environment variable naming the DL-remit structured habitat baseline's fresh-read output path; set, the instrument writes the read there — the deliberate-rewrite mode — and unset, it asserts against the committed baseline fixture.</summary>
    private const string EnumerationStructuredBaselineVariable = "VERITAS_ENUMCSP_W3C_STRUCTURED";

    /// <summary>The environment variable naming the RL-remit structured habitat baseline's fresh-read output path; set, the instrument writes the read there — the deliberate-rewrite mode — and unset, it asserts against the committed baseline fixture.</summary>
    private const string EnumerationStructuredRlBaselineVariable = "VERITAS_ENUMCSP_W3C_RL_STRUCTURED";

    /// <summary>The committed DL-remit structured baseline fixture's file name under the conformance fixtures, read from the source tree.</summary>
    private const string StructuredBaselineFixture = "context-habitat-structured-baseline.txt";

    /// <summary>The committed RL-remit structured baseline fixture's file name under the conformance fixtures, read from the source tree.</summary>
    private const string StructuredRlBaselineFixture = "context-habitat-structured-rl-baseline.txt";

    /// <summary>The committed DL-remit gate-0 label read's file name under the conformance fixtures — the freeze artifact the baseline's label column is asserted against.</summary>
    private const string GateZeroLabelsFixture = "registry-gate0-labels.txt";

    /// <summary>The committed RL-remit gate-0 label read's file name under the conformance fixtures — the freeze artifact the RL baseline's label column is asserted against.</summary>
    private const string GateZeroRlLabelsFixture = "registry-gate0-rl-labels.txt";

    /// <summary>The DL-remit structured baseline's header line, asserted so the two remits' fixtures cannot be swapped.</summary>
    private const string StructuredBaselineHeader = "enumeration-CSP structured habitat baseline over the Direct-Semantics DL remit: per-id habitat label, admission, passed census bits, and the eleven-row match, census-admitted, and walk-reached masks in registry order";

    /// <summary>The RL-remit structured baseline's header line, asserted so the two remits' fixtures cannot be swapped.</summary>
    private const string StructuredRlBaselineHeader = "enumeration-CSP structured habitat baseline over the RL-marked remit: per-id habitat label, admission, passed census bits, and the eleven-row match, census-admitted, and walk-reached masks in registry order";

    /// <summary>
    /// One manifest id's structured habitat read: the rendered baseline line
    /// beside the per-row columns the coherence and steal-set computations
    /// consume. A candidate-bearing read additionally carries the dry-run
    /// candidate's own two columns, which never enter the rendered line.
    /// </summary>
    private sealed class StructuredHabitatRead
    {
        /// <summary>The suite-qualified manifest id the line is keyed by.</summary>
        public string Key { get; }

        /// <summary>The rendered baseline line, exactly as the committed fixture carries it.</summary>
        public string Line { get; }

        /// <summary>Whether the premise loaded, mapped, and cleared the survey's axiom gate, so the census scan ran and the mask columns are defined.</summary>
        public bool CensusRan { get; }

        /// <summary>The production survey's habitat label; none where the census did not run.</summary>
        public EnumerationHabitatClass Label { get; }

        /// <summary>The first-admitted-matching answer computed by direct table iteration; none where no admitted row matched or the census did not run.</summary>
        public EnumerationHabitatClass WalkAnswer { get; }

        /// <summary>Per registry row, whether the row's match step answered a label for the module — order-free and gate-free; empty where the census did not run.</summary>
        public bool[] Match { get; }

        /// <summary>Per registry row, whether the passed census admits the row for evaluation; empty where the census did not run.</summary>
        public bool[] CensusAdmitted { get; }

        /// <summary>Whether the dry-run candidate's match step answered a label for the module; <see langword="false"/> on a candidate-free read.</summary>
        public bool CandidateMatch { get; }

        /// <summary>Whether the passed census admits the dry-run candidate for evaluation; <see langword="false"/> on a candidate-free read.</summary>
        public bool CandidateAdmitted { get; }

        /// <summary>Initialises one structured habitat read.</summary>
        /// <param name="key">The suite-qualified manifest id.</param>
        /// <param name="line">The rendered baseline line.</param>
        /// <param name="censusRan">Whether the census scan ran for the module.</param>
        /// <param name="label">The production survey's habitat label.</param>
        /// <param name="walkAnswer">The first-admitted-matching answer from direct table iteration.</param>
        /// <param name="match">The per-row match column.</param>
        /// <param name="censusAdmitted">The per-row census-admission column.</param>
        /// <param name="candidateMatch">The dry-run candidate's match answer.</param>
        /// <param name="candidateAdmitted">The dry-run candidate's census admission.</param>
        public StructuredHabitatRead(string key, string line, bool censusRan, EnumerationHabitatClass label, EnumerationHabitatClass walkAnswer, bool[] match, bool[] censusAdmitted, bool candidateMatch, bool candidateAdmitted)
        {
            Key = key;
            Line = line;
            CensusRan = censusRan;
            Label = label;
            WalkAnswer = walkAnswer;
            Match = match;
            CensusAdmitted = censusAdmitted;
            CandidateMatch = candidateMatch;
            CandidateAdmitted = candidateAdmitted;
        }
    }

    /// <summary>
    /// Reads one remit's structured habitat corpus: every manifest premise of
    /// both suites is loaded, expanded, mapped, and surveyed — never decided —
    /// and each id's read carries the production label beside the three
    /// registry masks gathered by iterating
    /// <see cref="ContextHabitatRecognizer.ProbeOrder"/> DIRECTLY, never
    /// through the classification walk's short-circuit. A premise that fails to
    /// load or map keeps the label sweeps' marker line; a module the survey's
    /// axiom gate rejects carries definite absence columns, the classification
    /// walk never receiving it. A dry-run candidate's match step and admission
    /// columns are evaluated standalone beside every read without entering the
    /// rendered line.
    /// </summary>
    /// <param name="remit">The manifest remit to read.</param>
    /// <param name="manifestMethodName">The calling instrument method the manifest data attribute resolves rows against.</param>
    /// <param name="candidate">The dry-run candidate row, or <see langword="null"/> for the baseline shape.</param>
    /// <returns>The per-id reads in manifest order.</returns>
    private static List<StructuredHabitatRead> ReadStructuredCorpus(Owl2TestRemit remit, string manifestMethodName, HabitatProbeEntry? candidate)
    {
        List<StructuredHabitatRead> reads = [];
        foreach(string suite in (string[])["approved", "proposed"])
        {
            foreach(object[] row in new Owl2ManifestDataAttribute(suite, "all.rdf", remit).GetData(typeof(W3cOwl2DirectTests).GetMethod(manifestMethodName)!))
            {
                if(row is not [Owl2TestCase testCase])
                {
                    continue;
                }

                string key = $"{suite}/{testCase.Identifier}";
                if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
                {
                    reads.Add(MarkerRead(key, "premise unreadable"));
                    continue;
                }

                premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
                OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
                if(premise.Diagnostics.HasErrors)
                {
                    reads.Add(MarkerRead(key, "premise unmapped"));
                    continue;
                }

                ReasoningModule module = new([.. premise.Axioms], Violations: []);
                reads.Add(SurveyStructuredRead(key, module, candidate));
            }
        }

        return reads;
    }

    /// <summary>Builds the read for a premise that failed to load or map, keeping the label sweeps' marker line shape.</summary>
    /// <param name="key">The suite-qualified manifest id.</param>
    /// <param name="marker">The load-or-map marker.</param>
    /// <returns>The marker read.</returns>
    private static StructuredHabitatRead MarkerRead(string key, string marker)
    {
        return new StructuredHabitatRead(key, $"{key} | {marker}", censusRan: false, EnumerationHabitatClass.None, EnumerationHabitatClass.None, match: [], censusAdmitted: [], candidateMatch: false, candidateAdmitted: false);
    }

    /// <summary>Surveys one mapped module into its structured read: the production label, the seam-read passed census bits, the three registry masks, and the dry-run candidate's columns where a candidate rides along.</summary>
    /// <param name="key">The suite-qualified manifest id.</param>
    /// <param name="module">The mapped premise module.</param>
    /// <param name="candidate">The dry-run candidate row, or <see langword="null"/> for the baseline shape.</param>
    /// <returns>The structured read.</returns>
    private static StructuredHabitatRead SurveyStructuredRead(string key, ReasoningModule module, HabitatProbeEntry? candidate)
    {
        ContextModuleSurveyResult survey = ContextModuleSurvey.Survey(module);
        bool censusRan = ContextModuleSurvey.TryCensusFor(module, out bool mentionsNominals, out bool mentionsCounting);
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        bool[] match = censusRan ? new bool[rows.Length] : [];
        bool[] censusAdmitted = censusRan ? new bool[rows.Length] : [];
        bool[] walkReached = censusRan ? new bool[rows.Length] : [];
        EnumerationHabitatClass walkAnswer = EnumerationHabitatClass.None;
        bool candidateMatch = false;
        bool candidateAdmitted = false;
        if(censusRan)
        {
            EnumerationHabitatClass[] answers = new EnumerationHabitatClass[rows.Length];
            for(int index = 0; index < rows.Length; index++)
            {
                answers[index] = rows[index].Match(module);
                match[index] = answers[index] != EnumerationHabitatClass.None;
                censusAdmitted[index] = rows[index].Admits(mentionsNominals, mentionsCounting);
            }

            for(int index = 0; index < rows.Length; index++)
            {
                if(!censusAdmitted[index])
                {
                    continue;
                }

                walkReached[index] = true;
                if(match[index])
                {
                    walkAnswer = answers[index];
                    break;
                }
            }

            if(candidate is HabitatProbeEntry candidateRow)
            {
                candidateMatch = candidateRow.Match(module) != EnumerationHabitatClass.None;
                candidateAdmitted = candidateRow.Admits(mentionsNominals, mentionsCounting);
            }
        }

        string nominalsColumn = censusRan ? (mentionsNominals ? "1" : "0") : "-";
        string countingColumn = censusRan ? (mentionsCounting ? "1" : "0") : "-";
        string censusColumn = censusRan ? "ran" : "rejected";
        string line = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{key} | habitat={survey.EnumerationHabitat} | admitted={survey.Admitted} | census={censusColumn} | nominals={nominalsColumn} | counting={countingColumn} | match={RenderMask(match, censusRan)} | censusAdmitted={RenderMask(censusAdmitted, censusRan)} | walkReached={RenderMask(walkReached, censusRan)}");

        return new StructuredHabitatRead(key, line, censusRan, survey.EnumerationHabitat, walkAnswer, match, censusAdmitted, candidateMatch, candidateAdmitted);
    }

    /// <summary>Renders one registry mask as a column of ten cells in registry order, <c>1</c> where the row's bit is set; a census-rejected read renders every cell as the definite absence <c>-</c>.</summary>
    /// <param name="bits">The per-row bits; empty on a census-rejected read.</param>
    /// <param name="censusRan">Whether the census scan ran for the module.</param>
    /// <returns>The ten-character column.</returns>
    private static string RenderMask(bool[] bits, bool censusRan)
    {
        if(!censusRan)
        {
            return new string('-', ContextHabitatRecognizer.ProbeOrder.Length);
        }

        char[] cells = new char[bits.Length];
        for(int index = 0; index < bits.Length; index++)
        {
            cells[index] = bits[index] ? '1' : '0';
        }

        return new string(cells);
    }

    /// <summary>Renders the structured baseline artifact: the header line and every read's line, LF-terminated throughout per the repository's line-ending direction.</summary>
    /// <param name="header">The remit's header line.</param>
    /// <param name="reads">The per-id reads in manifest order.</param>
    /// <returns>The artifact text.</returns>
    private static string RenderStructuredBaseline(string header, List<StructuredHabitatRead> reads)
    {
        StringBuilder artifact = new();
        artifact.Append(header).Append('\n');
        foreach(StructuredHabitatRead read in reads)
        {
            artifact.Append(read.Line).Append('\n');
        }

        return artifact.ToString();
    }

    /// <summary>Reads one committed fixture's lines from the source tree, trimming a carriage-return suffix per line and the trailing empty split so the comparison is column-wise rather than byte-wise.</summary>
    /// <param name="fixtureName">The fixture's file name under the conformance fixtures.</param>
    /// <returns>The fixture's lines, header first.</returns>
    private static string[] ReadCommittedFixtureLines(string fixtureName)
    {
        string path = W3cCorpusPath.FixturePath(fixtureName);
        Assert.IsTrue(File.Exists(path), $"The committed fixture {fixtureName} is absent at {path}; generate it with the instrument's write mode and commit it.");
        string[] split = File.ReadAllText(path).Split('\n');
        int count = split.Length;
        if(count > 0 && split[count - 1].Length == 0)
        {
            count--;
        }

        string[] lines = new string[count];
        for(int index = 0; index < count; index++)
        {
            lines[index] = split[index].TrimEnd('\r');
        }

        return lines;
    }

    /// <summary>
    /// The asserting comparison: the fresh read must equal the committed
    /// baseline line for line under ordinal equality, and a divergence fails
    /// naming the id and the column that moved. The id populations must
    /// coincide exactly — a population change is banked through the
    /// instrument's write mode and a deliberate commit, never absorbed.
    /// </summary>
    /// <param name="fixtureName">The committed baseline fixture's file name.</param>
    /// <param name="header">The remit's header line.</param>
    /// <param name="reads">The fresh per-id reads.</param>
    private static void AssertStructuredBaseline(string fixtureName, string header, List<StructuredHabitatRead> reads)
    {
        string[] committed = ReadCommittedFixtureLines(fixtureName);
        Assert.IsGreaterThan(0, committed.Length, $"The committed baseline {fixtureName} carries a header line.");
        Assert.AreEqual(header, committed[0], $"{fixtureName}: the committed baseline's header is this instrument's own remit header.");
        Dictionary<string, string> committedByKey = new(StringComparer.Ordinal);
        for(int index = 1; index < committed.Length; index++)
        {
            committedByKey.Add(committed[index].Split(" | ", StringSplitOptions.None)[0], committed[index]);
        }

        foreach(StructuredHabitatRead read in reads)
        {
            Assert.IsTrue(committedByKey.Remove(read.Key, out string? committedLine), $"{read.Key}: the id is absent from the committed baseline {fixtureName}.");
            if(!string.Equals(committedLine, read.Line, StringComparison.Ordinal))
            {
                FailNamingTheMovedColumn(read.Key, committedLine!, read.Line, fixtureName);
            }
        }

        Assert.IsEmpty(committedByKey, $"Ids present in the committed baseline {fixtureName} and absent from the fresh read: {string.Join(", ", committedByKey.Keys)}.");
    }

    /// <summary>Fails the asserting comparison naming the id and the first column whose value moved between the committed baseline and the fresh read.</summary>
    /// <param name="key">The suite-qualified manifest id.</param>
    /// <param name="committedLine">The committed baseline's line.</param>
    /// <param name="freshLine">The fresh read's line.</param>
    /// <param name="fixtureName">The committed baseline fixture's file name.</param>
    private static void FailNamingTheMovedColumn(string key, string committedLine, string freshLine, string fixtureName)
    {
        string[] committedColumns = committedLine.Split(" | ", StringSplitOptions.None);
        string[] freshColumns = freshLine.Split(" | ", StringSplitOptions.None);
        int width = Math.Max(committedColumns.Length, freshColumns.Length);
        for(int index = 1; index < width; index++)
        {
            string committedColumn = index < committedColumns.Length ? committedColumns[index] : "(absent)";
            string freshColumn = index < freshColumns.Length ? freshColumns[index] : "(absent)";
            if(!string.Equals(committedColumn, freshColumn, StringComparison.Ordinal))
            {
                Assert.Fail($"{key}: the {committedColumn.Split('=')[0]} column moved against {fixtureName} — committed '{committedColumn}', fresh '{freshColumn}'.");
            }
        }

        Assert.Fail($"{key}: the line moved against {fixtureName} — committed '{committedLine}', fresh '{freshLine}'.");
    }

    /// <summary>
    /// The DL-remit structured habitat baseline instrument: by default the
    /// fresh corpus read ASSERTS against the committed baseline fixture,
    /// failing with the id and the column that moved, and when
    /// <see cref="EnumerationStructuredBaselineVariable"/> names an absolute
    /// output path it writes the fresh read there instead — the deliberate-
    /// rewrite mode a label movement is banked through. The artifact is
    /// LF-terminated and independent of every gap census, and its masks make a
    /// candidate row's steal set computable and the match-without-admission
    /// shadow column readable per id; the standing corpus gate
    /// <see cref="MatchWithoutAdmissionShadowIsEmptyOverBothRemits"/> asserts
    /// that column empty. The whole-corpus label sweep stays beside this
    /// instrument unedited — its artifact chain is the cross-session
    /// byte-comparable read.
    /// </summary>
    /// <returns>The asynchronous read.</returns>
    [TestMethod]
    public async Task StructuredHabitatBaselineHoldsOnTheDlRemit()
    {
        List<StructuredHabitatRead> reads = ReadStructuredCorpus(Owl2TestRemit.DirectSemanticsDl, nameof(StructuredHabitatBaselineHoldsOnTheDlRemit), candidate: null);
        string? outputPath = Environment.GetEnvironmentVariable(EnumerationStructuredBaselineVariable);
        if(!string.IsNullOrWhiteSpace(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, RenderStructuredBaseline(StructuredBaselineHeader, reads), TestContext.CancellationToken).ConfigureAwait(false);
            TestContext.WriteLine("DL-remit structured habitat baseline written to " + outputPath + ".");

            return;
        }

        AssertStructuredBaseline(StructuredBaselineFixture, StructuredBaselineHeader, reads);
    }

    /// <summary>
    /// The RL-remit structured habitat baseline instrument: the
    /// <see cref="StructuredHabitatBaselineHoldsOnTheDlRemit"/> read taken over
    /// the RL-marked remit, asserting by default against its own committed
    /// baseline fixture and writing the fresh read only where
    /// <see cref="EnumerationStructuredRlBaselineVariable"/> names an absolute
    /// output path. The remit is reachable in production through the RL
    /// production-path twin's beyond-RL delegation, so the same asserting
    /// discipline stands over it.
    /// </summary>
    /// <returns>The asynchronous read.</returns>
    [TestMethod]
    public async Task StructuredHabitatBaselineHoldsOnTheRlRemit()
    {
        List<StructuredHabitatRead> reads = ReadStructuredCorpus(Owl2TestRemit.RlMarked, nameof(StructuredHabitatBaselineHoldsOnTheRlRemit), candidate: null);
        string? outputPath = Environment.GetEnvironmentVariable(EnumerationStructuredRlBaselineVariable);
        if(!string.IsNullOrWhiteSpace(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, RenderStructuredBaseline(StructuredRlBaselineHeader, reads), TestContext.CancellationToken).ConfigureAwait(false);
            TestContext.WriteLine("RL-remit structured habitat baseline written to " + outputPath + ".");

            return;
        }

        AssertStructuredBaseline(StructuredRlBaselineFixture, StructuredRlBaselineHeader, reads);
    }

    /// <summary>
    /// The machine-side freeze re-assertion over the DL remit, a hard gate: the
    /// committed structured baseline's habitat-label column equals the
    /// committed gate-0 label read's column id for id — two repository
    /// artifacts compared column-wise with no operator read between them, the
    /// two id populations coinciding exactly and a load-or-map marker row
    /// agreeing verbatim.
    /// </summary>
    [TestMethod]
    public void StructuredBaselineLabelColumnEqualsTheGateZeroReadOnTheDlRemit()
    {
        AssertLabelColumnsAgree(GateZeroLabelsFixture, StructuredBaselineFixture);
    }

    /// <summary>
    /// The machine-side freeze re-assertion over the RL remit, a hard gate: the
    /// committed RL structured baseline's habitat-label column equals the
    /// committed RL gate-0 label read's column id for id, the same column-wise
    /// comparison the DL remit carries.
    /// </summary>
    [TestMethod]
    public void StructuredBaselineLabelColumnEqualsTheGateZeroReadOnTheRlRemit()
    {
        AssertLabelColumnsAgree(GateZeroRlLabelsFixture, StructuredRlBaselineFixture);
    }

    /// <summary>Asserts two committed fixtures agree on the shared habitat-label column id for id: every id present in one is present in the other, and each id's habitat value — or its load-or-map marker — is identical.</summary>
    /// <param name="gateFixtureName">The committed gate-0 label read's file name.</param>
    /// <param name="baselineFixtureName">The committed structured baseline's file name.</param>
    private static void AssertLabelColumnsAgree(string gateFixtureName, string baselineFixtureName)
    {
        Dictionary<string, string> gateLabels = ReadLabelColumn(ReadCommittedFixtureLines(gateFixtureName));
        Dictionary<string, string> baselineLabels = ReadLabelColumn(ReadCommittedFixtureLines(baselineFixtureName));
        foreach((string key, string gateLabel) in gateLabels)
        {
            Assert.IsTrue(baselineLabels.Remove(key, out string? baselineLabel), $"{key}: present in {gateFixtureName} and absent from {baselineFixtureName}.");
            Assert.AreEqual(gateLabel, baselineLabel, $"{key}: the habitat label diverges between {gateFixtureName} and {baselineFixtureName}.");
        }

        Assert.IsEmpty(baselineLabels, $"Ids present in {baselineFixtureName} and absent from {gateFixtureName}: {string.Join(", ", baselineLabels.Keys)}.");
    }

    /// <summary>Reads a fixture's per-id habitat-label column: the habitat segment's value where the line carries one, or the line's whole marker remainder where it does not.</summary>
    /// <param name="lines">The fixture's lines, header first.</param>
    /// <returns>The per-id labels.</returns>
    private static Dictionary<string, string> ReadLabelColumn(string[] lines)
    {
        Dictionary<string, string> labels = new(StringComparer.Ordinal);
        for(int index = 1; index < lines.Length; index++)
        {
            string[] columns = lines[index].Split(" | ", StringSplitOptions.None);
            string label = columns.Length > 1 && columns[1].StartsWith("habitat=", StringComparison.Ordinal)
                ? columns[1]["habitat=".Length..]
                : string.Join(" | ", columns[1..]);
            labels.Add(columns[0], label);
        }

        return labels;
    }

    /// <summary>
    /// The chain-coherence invariant, golden-free: for every corpus module the
    /// census scan reaches, the production survey's habitat label equals the
    /// FIRST row in registry order that the passed census admits and whose
    /// match step does not decline, and a module the axiom gate rejects is
    /// none. The row reads no baseline, so it survives intentional label
    /// movement and stands as the proof that first-match-wins is honoured by
    /// whatever the table holds, over both freeze remits.
    /// </summary>
    [TestMethod]
    public void ClassifyAnswersTheFirstAdmittedMatchingRowOverBothRemits()
    {
        foreach((Owl2TestRemit remit, string remitName) in ((Owl2TestRemit Remit, string Name)[])[(Owl2TestRemit.DirectSemanticsDl, "DirectSemanticsDl"), (Owl2TestRemit.RlMarked, "RlMarked")])
        {
            foreach(StructuredHabitatRead read in ReadStructuredCorpus(remit, nameof(ClassifyAnswersTheFirstAdmittedMatchingRowOverBothRemits), candidate: null))
            {
                if(!read.CensusRan)
                {
                    Assert.AreEqual(EnumerationHabitatClass.None, read.Label, $"{remitName} {read.Key}: a module the survey's axiom gate rejects reaches no probe and is none.");
                    continue;
                }

                Assert.AreEqual(read.WalkAnswer, read.Label, $"{remitName} {read.Key}: the production label is the first row in registry order the census admits and the match step answers.");
            }
        }
    }

    /// <summary>
    /// The corpus shadow gate, golden-free: for every corpus module the census
    /// scan reaches, no registry row matches the module while the passed
    /// census holds it out of evaluation — the match-without-admission shadow
    /// column is empty over both freeze remits. A failure names every
    /// shadowed id with its remit and row, so a corpus addition or an
    /// admission change landing in a shadow fails loudly and is banked as a
    /// deliberate ruling, never absorbed. The in-vitro shadow controls beside
    /// the census contract battery exhibit the class this gate holds at zero.
    /// </summary>
    [TestMethod]
    public void MatchWithoutAdmissionShadowIsEmptyOverBothRemits()
    {
        List<string> shadowed = [];
        foreach((Owl2TestRemit remit, string remitName) in ((Owl2TestRemit Remit, string Name)[])[(Owl2TestRemit.DirectSemanticsDl, "DirectSemanticsDl"), (Owl2TestRemit.RlMarked, "RlMarked")])
        {
            foreach(StructuredHabitatRead read in ReadStructuredCorpus(remit, nameof(MatchWithoutAdmissionShadowIsEmptyOverBothRemits), candidate: null))
            {
                if(!read.CensusRan)
                {
                    continue;
                }

                for(int index = 0; index < read.Match.Length; index++)
                {
                    if(read.Match[index] && !read.CensusAdmitted[index])
                    {
                        shadowed.Add($"{remitName} {read.Key} row {ContextHabitatRecognizer.ProbeOrder[index].Label}");
                    }
                }
            }
        }

        Assert.IsEmpty(shadowed, $"Corpus modules standing in the match-without-admission shadow: {string.Join("; ", shadowed)}.");
    }

    /// <summary>
    /// The candidate-row dry run, the insertion protocol's computable half: a
    /// candidate row's match step and declared admission columns are evaluated
    /// STANDALONE against the reloaded corpus, the widened read minus the
    /// candidate column must reproduce the committed baseline under the same
    /// asserting comparison the instrument runs, and the candidate's steal set
    /// is COMPUTED from the committed baseline rather than read by a human.
    /// Only after this read is a row spliced into the registry, with the
    /// computed set as its movement gate. The inert candidate proves the null
    /// protocol — an empty steal set over an untouched baseline — and the
    /// cloned opening-position candidate proves the formula's reach: at the
    /// table's head its computed steal set is exactly the ids its own match
    /// and admission columns light, the branching modal-gadget flip among them.
    /// </summary>
    [TestMethod]
    public void CandidateRowDryRunComputesTheStealSetFromTheCommittedBaseline()
    {
        HabitatProbeEntry inert = new(
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            HabitatPathAdmission.Always,
            HabitatPathAdmission.Always,
            HabitatSignalCarriers.None,
            EnumerationDeciderFaces.None,
            static _ => EnumerationHabitatClass.None);
        List<StructuredHabitatRead> inertReads = ReadStructuredCorpus(Owl2TestRemit.DirectSemanticsDl, nameof(CandidateRowDryRunComputesTheStealSetFromTheCommittedBaseline), inert);
        AssertStructuredBaseline(StructuredBaselineFixture, StructuredBaselineHeader, inertReads);
        List<string> inertSteals = ComputeStealSet(inertReads, insertionPosition: 0);
        Assert.IsEmpty(inertSteals, "The inert candidate steals nothing: its match step declines every module.");

        HabitatProbeEntry shapeK = ContextHabitatRecognizer.ProbeOrder[2];
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, shapeK.Label, "The cloned candidate is the branching modal-gadget row at registry position two.");
        HabitatProbeEntry clone = new(shapeK.Label, shapeK.AlternateLabel, shapeK.OnNominalFree, shapeK.OnNominal, shapeK.Carriers, shapeK.Faces, shapeK.Match);
        List<StructuredHabitatRead> cloneReads = ReadStructuredCorpus(Owl2TestRemit.DirectSemanticsDl, nameof(CandidateRowDryRunComputesTheStealSetFromTheCommittedBaseline), clone);
        AssertStructuredBaseline(StructuredBaselineFixture, StructuredBaselineHeader, cloneReads);
        List<string> cloneSteals = ComputeStealSet(cloneReads, insertionPosition: 0);
        List<string> litByOwnColumns = [];
        foreach(StructuredHabitatRead read in cloneReads)
        {
            if(read.CensusRan && read.CandidateMatch && read.CandidateAdmitted)
            {
                litByOwnColumns.Add(read.Key);
            }
        }

        Assert.AreSequenceEqual(litByOwnColumns, cloneSteals, "At the table's head the computed steal set is exactly the candidate's own match-and-admission column.");
        Assert.Contains("approved/WebOnt-description-logic-661", cloneSteals, "The branching modal-gadget flip is inside its own clone's computed steal set.");
    }

    /// <summary>
    /// Computes a candidate row's steal set from the committed DL baseline per
    /// the insertion protocol: every id whose read the candidate's match step
    /// answers and the passed census admits, and whose committed current label
    /// sits at the insertion position or later in registry order — a
    /// none-labelled or marker id counting as later than every position, since
    /// a landing row claims such an id from the walk terminal.
    /// </summary>
    /// <param name="reads">The candidate-bearing corpus reads.</param>
    /// <param name="insertionPosition">The registry position the candidate would land at.</param>
    /// <returns>The stolen ids in corpus order.</returns>
    private static List<string> ComputeStealSet(List<StructuredHabitatRead> reads, int insertionPosition)
    {
        Dictionary<string, string> committedLabels = ReadLabelColumn(ReadCommittedFixtureLines(StructuredBaselineFixture));
        List<string> stolen = [];
        foreach(StructuredHabitatRead read in reads)
        {
            if(!read.CensusRan || !read.CandidateMatch || !read.CandidateAdmitted)
            {
                continue;
            }

            Assert.IsTrue(committedLabels.TryGetValue(read.Key, out string? currentLabel), $"{read.Key}: the id is absent from the committed baseline the steal set is computed from.");
            if(RowIndexOfLabel(currentLabel!) >= insertionPosition)
            {
                stolen.Add(read.Key);
            }
        }

        return stolen;
    }

    /// <summary>Resolves a committed habitat label to its registry row position by label or declared non-none alternate; a none label or a load-or-map marker resolves past the table's end — the walk-terminal convention the steal computation counts as later than every insertion position.</summary>
    /// <param name="label">The committed habitat-label column value.</param>
    /// <returns>The registry row position, or the table length past its end.</returns>
    private static int RowIndexOfLabel(string label)
    {
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        for(int index = 0; index < rows.Length; index++)
        {
            if(string.Equals(label, rows[index].Label.ToString(), StringComparison.Ordinal)
                || (rows[index].AlternateLabel != EnumerationHabitatClass.None && string.Equals(label, rows[index].AlternateLabel.ToString(), StringComparison.Ordinal)))
            {
                return index;
            }
        }

        return rows.Length;
    }

    /// <summary>Appends one measured boundary exit to the seeding sink under the append gate.</summary>
    /// <param name="sink">The absolute sink path.</param>
    /// <param name="category">The census the exit belongs to.</param>
    /// <param name="identifier">The test identifier.</param>
    /// <param name="reason">The refutation-gap reason, or <c>null</c> for a fragment gap.</param>
    private static void RecordContextSeed(string sink, string category, string identifier, string? reason)
    {
        string line = reason is null ? $"{category}\t{identifier}" : $"{category}\t{identifier}\t{reason}";
        lock(ContextCensusSeedGate)
        {
            File.AppendAllText(sink, line + Environment.NewLine);
        }
    }

    /// <summary>WA1: a context-decided case. The KC2-shape module decides whole and consistent, and its entailment refutation clashes — the arm reads a decided verdict, never the sentinel abstention.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa1ContextDecidedReadsAWholeVerdict()
    {
        OwlObjectPropertyReference role = ContextRole("wa1rel");
        NamedNode source = ContextIndividual("wa1src");
        NamedNode destination = ContextIndividual("wa1dst");
        OwlClassReference host = ContextClass("Wa1Host");
        OwlClassReference target = ContextClass("Wa1Target");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlObjectAllValuesFrom(role, target)) { Origin = ContextOrigin("wa1sub") },
            new OwlClassAssertionAxiom(host, source) { Origin = ContextOrigin("wa1assert") },
            new OwlObjectPropertyAssertionAxiom(source, role.Named, destination) { Origin = ContextOrigin("wa1edge") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "WA1: the context engine must decide the KC2-shape premise whole, not abstain.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "WA1: the KC2-shape premise is consistent.");

        //Entailment wa1dst : Wa1Target — the refutation module clashes.
        ReasoningModule refuted = Extend(premise, new OwlClassAssertionAxiom(new OwlObjectComplementOf(target), destination) { Origin = ContextOrigin("wa1refute") });
        ModuleDecision refutation = await Awaited(ContextArm(refuted, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, refutation.Outcome, "WA1: the refutation module stays in the context fragment.");
        Assert.IsFalse(refutation.Verdict!.IsConsistent, "WA1: wa1dst : Wa1Target is entailed, so its refutation is inconsistent.");
    }

    /// <summary>WA2: a survey-widening decide row. The positive-union superclass — the disjunctive tier's flagship admission — is surveyed in, clausified to a DL1 multi-literal head, and decided whole and consistent by the context engine; the seam returns the verdict, never the beyond-fragment abstention it returned before the disjunctive widening.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa2PositiveUnionDecides()
    {
        OwlClassReference baseClass = ContextClass("Wa2Base");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(baseClass, new OwlObjectUnionOf([ContextClass("Wa2Left"), ContextClass("Wa2Right")])) { Origin = ContextOrigin("wa2sub") },
            new OwlClassAssertionAxiom(baseClass, ContextIndividual("wa2i")) { Origin = ContextOrigin("wa2assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "WA2: the positive-union superclass is admitted by the disjunctive survey and decides.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "WA2: the covering premise is consistent.");
    }

    /// <summary>WA3: a second-gate exit. The survey admits the reserved-role property assertion unconditionally, and the clausifier's reserved-role scan delegates it at intake, so the seam abstains — a second-gate exit, not a survey exit.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa3SecondGateExitAbstains()
    {
        ReasoningModule premise = new(
        [
            new OwlObjectPropertyAssertionAxiom(ContextIndividual("wa3a"), new NamedNode(OwlVocabulary.TopObjectProperty), ContextIndividual("wa3b")) { Origin = ContextOrigin("wa3edge") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "WA3: the reserved-role assertion delegates at the second gate, so the seam abstains.");
        Assert.IsNull(decision.Verdict, "WA3: a second-gate abstention carries no verdict.");
    }

    /// <summary>
    /// WA4: a refutation-side decide. The premise decides in-fragment, and the
    /// SubClassOf refutation of the conclusion — an unqualified max-1 at negative
    /// polarity under the complement —
    /// is admitted through the negative cardinality dual (max into min plus
    /// one, a positive-union GCI) and decides CONSISTENT: the genuine
    /// non-entailment (nothing constrains the role's count) is witnessed by a
    /// whole verdict instead of a gap.
    /// </summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa4RefutationSideDecidesNonEntailment()
    {
        OwlClassReference sub = ContextClass("Wa4Sub");
        OwlClassReference super = ContextClass("Wa4Super");
        NamedNode individual = ContextIndividual("wa4i");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(sub, super) { Origin = ContextOrigin("wa4sub") },
            new OwlClassAssertionAxiom(sub, individual) { Origin = ContextOrigin("wa4assert") },
        ], Violations: []);

        ModuleDecision premiseDecision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, premiseDecision.Outcome, "WA4: the premise is in the context fragment and decides.");
        Assert.IsTrue(premiseDecision.Verdict!.IsConsistent, "WA4: the premise is consistent.");

        //Refutation of the conclusion Wa4Sub subclassOf <=1 wa4rel: w : Wa4Sub and
        //not(<=1 wa4rel). The complement puts the unqualified max-1 at negative
        //polarity, which lowers through the min-2 dual — the refutation module is
        //admitted and its consistency witnesses the non-entailment.
        OwlClassAssertionAxiom refutation = new(
            new OwlObjectIntersectionOf([sub, new OwlObjectComplementOf(new OwlObjectCardinality(OwlCardinalityKind.Max, 1, ContextRole("wa4rel"), Filler: null))]),
            ContextWitness) { Origin = ContextOrigin("wa4refute") };
        ModuleDecision refutationDecision = await Awaited(ContextArm(Extend(premise, refutation), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, refutationDecision.Outcome, "WA4: the negative-polarity cardinality refutation is admitted through the dual and decides.");
        Assert.IsTrue(refutationDecision.Verdict!.IsConsistent, "WA4: the refutation is satisfiable, witnessing the genuine non-entailment.");
    }

    /// <summary>WA5: the nominal tier's arm-level decide (the DECL-FLIP of the former survey-exit row). The nominal enumeration is admitted, the asserted member collapses onto one of the enumerated constants without a unique-name assumption, and the seam answers a whole consistent verdict — the flipped after-state this walk row now pins.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa5NominalEnumerationDecides()
    {
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(ContextClass("Wa5Sub"), new OwlObjectOneOf([ContextIndividual("wa5a"), ContextIndividual("wa5b")])) { Origin = ContextOrigin("wa5sub") },
            new OwlClassAssertionAxiom(ContextClass("Wa5Sub"), ContextIndividual("wa5i")) { Origin = ContextOrigin("wa5assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "WA5: the nominal enumeration is admitted by the nominal tier and decides.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "WA5: the asserted member collapses onto an enumerated constant, so the premise is consistent.");
    }

    /// <summary>Wa6: arm A — an anonymous-target object-property assertion on a named source routes to the complement-existential rollup, which clashes when a successor is forced and is satisfiable when none is.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa6ArmAAnonymousTargetExistentialRollup()
    {
        OwlObjectPropertyReference role = ContextRole("wa6p");
        NamedNode source = ContextIndividual("wa6a");
        OwlObjectPropertyAssertionAxiom edge = new(source, role.Named, new BlankNode(Utf8Strings.From("wa6x"))) { Origin = ContextOrigin("wa6edge") };

        //Router: an anonymous-target edge on a named source routes to arm A — the
        //complement of an existential over the edge's property, asserted on the source.
        if(ContextRefutations(edge) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("Wa6: the anonymous-target edge must have an arm-A encoding.");

            return;
        }

        Assert.HasCount(1, checks, "Wa6: arm A yields exactly one refutation probe.");
        Assert.HasCount(1, checks[0].Axioms, "Wa6: the arm-A probe carries exactly one axiom.");
        if(checks[0].Axioms[0] is not OwlClassAssertionAxiom
        {
            Individual: NamedNode armSource,
            Class: OwlObjectComplementOf { Operand: OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference armRole, Filler: OwlClassReference armFiller } }
        })
        {
            Assert.Fail("Wa6: arm A must assert the complement of an existential over the edge's property on the named source.");

            return;
        }

        Assert.AreEqual(source, armSource, "Wa6: arm A asserts on the named source individual.");
        Assert.AreEqual(role.Named, armRole.Named, "Wa6: arm A's existential is over the edge's property.");
        Assert.AreEqual(ThingReference, armFiller, "Wa6: arm A's existential filler is owl:Thing.");

        //Clash: the source is forced to have a p-successor, so denying every
        //p-successor is inconsistent.
        ReasoningModule hasSuccessor = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectSomeValuesFrom(role, ThingReference), source) { Origin = ContextOrigin("wa6has") },
        ], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(Extend(hasSuccessor, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa6: the arm-A refutation stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa6: denying every p-successor of a source that has one is inconsistent.");

        //Satisfiable: nothing forces a p-successor, so denying every p-successor is consistent.
        ReasoningModule unrelated = new(
        [
            new OwlClassAssertionAxiom(ContextClass("Wa6Other"), source) { Origin = ContextOrigin("wa6other") },
        ], Violations: []);
        ModuleDecision satisfiable = await Awaited(ContextArm(Extend(unrelated, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, satisfiable.Outcome, "Wa6: the arm-A refutation stays in the context fragment.");
        Assert.IsTrue(satisfiable.Verdict!.IsConsistent, "Wa6: a source with no forced p-successor may deny every p-successor.");
    }

    /// <summary>Wa7: arm B — an anonymous class assertion routes to the class-empty refutation, which is the global unsatisfiability for owl:Thing and consistent for an unpopulated named class.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa7ArmBAnonymousClassEmptiness()
    {
        //Router: an anonymous owl:Thing assertion routes to arm B — SubClassOf(owl:Thing, owl:Nothing).
        OwlClassAssertionAxiom thingOnBlank = new(ThingReference, new BlankNode(Utf8Strings.From("wa7x"))) { Origin = ContextOrigin("wa7thing") };
        if(ContextRefutations(thingOnBlank) is not List<ContextRefutationProbe> globalChecks)
        {
            Assert.Fail("Wa7: the anonymous owl:Thing assertion must have an arm-B encoding.");

            return;
        }

        Assert.HasCount(1, globalChecks, "Wa7: arm B yields exactly one refutation probe.");
        Assert.HasCount(1, globalChecks[0].Axioms, "Wa7: the arm-B probe carries exactly one axiom.");
        if(globalChecks[0].Axioms[0] is not OwlSubClassOfAxiom { SubClass: OwlClassReference armBSub, SuperClass: OwlClassReference armBSuper })
        {
            Assert.Fail("Wa7: arm B must force the asserted class empty with a SubClassOf(..., owl:Nothing).");

            return;
        }

        Assert.AreEqual(ThingReference, armBSub, "Wa7: arm B's subclass is the asserted class.");
        Assert.AreEqual(NothingReference, armBSuper, "Wa7: arm B's superclass is owl:Nothing.");

        //Clash: forcing owl:Thing empty is the global unsatisfiability over a non-empty domain.
        ReasoningModule global = new([globalChecks[0].Axioms[0]], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(global, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa7: the arm-B global refutation stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa7: forcing owl:Thing empty is inconsistent over a non-empty domain.");

        //Satisfiable: forcing an unpopulated named class empty is consistent —
        //nothing forces the class non-empty.
        OwlClassAssertionAxiom namedOnBlank = new(ContextClass("Wa7D"), new BlankNode(Utf8Strings.From("wa7y"))) { Origin = ContextOrigin("wa7named") };
        if(ContextRefutations(namedOnBlank) is not List<ContextRefutationProbe> emptyChecks)
        {
            Assert.Fail("Wa7: the anonymous named-class assertion must have an arm-B encoding.");

            return;
        }

        ReasoningModule populated = new(
        [
            new OwlClassAssertionAxiom(ContextClass("Wa7C"), ContextIndividual("wa7w")) { Origin = ContextOrigin("wa7pop") },
        ], Violations: []);
        ModuleDecision satisfiable = await Awaited(ContextArm(Extend(populated, emptyChecks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, satisfiable.Outcome, "Wa7: the arm-B refutation stays in the context fragment.");
        Assert.IsTrue(satisfiable.Verdict!.IsConsistent, "Wa7: forcing an unpopulated named class empty is consistent.");
    }

    /// <summary>Wa8: arm C — a named-to-named object-property assertion routes to the exact-pair denial, which clashes when an equivalence lifts a told edge onto the denied pair and is satisfiable when nothing forces it.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa8ArmCNamedPairDenial()
    {
        OwlObjectPropertyReference head = ContextRole("wa8h");
        OwlObjectPropertyReference leader = ContextRole("wa8l");
        NamedNode subject = ContextIndividual("wa8x");
        NamedNode target = ContextIndividual("wa8y");

        //Router: a named-to-named edge routes to arm C — the exact told pair denied.
        OwlObjectPropertyAssertionAxiom edge = new(subject, head.Named, target) { Origin = ContextOrigin("wa8edge") };
        if(ContextRefutations(edge) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("Wa8: the named-to-named edge must have an arm-C encoding.");

            return;
        }

        Assert.HasCount(1, checks, "Wa8: arm C yields exactly one refutation probe.");
        Assert.HasCount(1, checks[0].Axioms, "Wa8: the arm-C probe carries exactly one axiom.");
        if(checks[0].Axioms[0] is not OwlNegativeObjectPropertyAssertionAxiom { Source: NamedNode armSource, Property: OwlObjectPropertyReference armProperty, Target: NamedNode armTarget })
        {
            Assert.Fail("Wa8: arm C must deny the exact told pair with a NegativeObjectPropertyAssertion.");

            return;
        }

        Assert.AreEqual(subject, armSource, "Wa8: arm C denies from the edge's source.");
        Assert.AreEqual(head.Named, armProperty.Named, "Wa8: arm C denies over the edge's property.");
        Assert.AreEqual(target, armTarget, "Wa8: arm C denies to the edge's target.");

        //Clash: an equivalence lifts the told leader edge onto the denied head edge.
        ReasoningModule equivalent = new(
        [
            new OwlEquivalentObjectPropertiesAxiom(head, leader) { Origin = ContextOrigin("wa8equiv") },
            new OwlObjectPropertyAssertionAxiom(subject, leader.Named, target) { Origin = ContextOrigin("wa8told") },
        ], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(Extend(equivalent, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa8: the arm-C refutation stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa8: the equivalence lifts the told leader edge onto the denied head edge.");

        //Satisfiable: without the equivalence nothing forces the head edge, so denying it is consistent.
        ReasoningModule loose = new(
        [
            new OwlObjectPropertyAssertionAxiom(subject, leader.Named, target) { Origin = ContextOrigin("wa8loose") },
        ], Violations: []);
        ModuleDecision satisfiable = await Awaited(ContextArm(Extend(loose, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, satisfiable.Outcome, "Wa8: the arm-C refutation stays in the context fragment.");
        Assert.IsTrue(satisfiable.Verdict!.IsConsistent, "Wa8: without the equivalence nothing forces the head edge, so denying it is consistent.");
    }

    /// <summary>Wa9: the self-edge ground path (the SelfRestriction-001 shape) and the connected-blank-node guard — the ghost self-loop clash (Wa9a), the shared-blank guard (Wa9b), and the distinct-role satisfiable twin (Wa9c).</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa9SelfEdgeGroundClashAndForestGuard()
    {
        OwlObjectPropertyReference likes = ContextRole("wa9likes");
        NamedNode peter = ContextIndividual("wa9p");

        //Wa9a: the hasSelf ghost loop meets the denied self-edge in the ground
        //graph — the executable pin the SelfRestriction-001 flip depends on.
        ReasoningModule selfPremise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectHasSelf(likes), peter) { Origin = ContextOrigin("wa9self") },
        ], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(Extend(selfPremise, new OwlNegativeObjectPropertyAssertionAxiom(peter, likes, peter) { Origin = ContextWitnessQuad() }), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa9a: the self-loop ground clash stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa9a: the hasSelf ghost loop meets the denied self-edge in the ground graph.");

        //Wa9b: the connected-blank-node guard fires on a blank node shared across
        //two non-vacuous axioms and stays quiet on a single occurrence.
        OwlObjectPropertyReference parent = ContextRole("wa9parent");
        NamedNode fred = ContextIndividual("wa9fred");
        BlankNode middle = new(Utf8Strings.From("wa9x"));
        BlankNode leaf = new(Utf8Strings.From("wa9y"));
        Assert.IsTrue(
            HasSharedAnonymousIndividual(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa9chain1") },
                new OwlObjectPropertyAssertionAxiom(middle, parent.Named, leaf) { Origin = ContextOrigin("wa9chain2") },
            ]),
            "Wa9b: a blank node shared across two non-vacuous axioms is a connected anonymous forest.");
        Assert.IsFalse(
            HasSharedAnonymousIndividual(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa9single") },
            ]),
            "Wa9b: a blank node confined to one axiom is not a connected forest.");

        //Wa9c: the ghost self-loop on one role carries no obligation onto a
        //distinct role, so denying the distinct role's self-edge is consistent.
        OwlObjectPropertyReference likesA = ContextRole("wa9likesA");
        OwlObjectPropertyReference likesB = ContextRole("wa9likesB");
        ReasoningModule twinPremise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectHasSelf(likesA), peter) { Origin = ContextOrigin("wa9twin") },
        ], Violations: []);
        ModuleDecision twin = await Awaited(ContextArm(Extend(twinPremise, new OwlNegativeObjectPropertyAssertionAxiom(peter, likesB, peter) { Origin = ContextWitnessQuad() }), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, twin.Outcome, "Wa9c: the distinct-role self denial stays in the context fragment.");
        Assert.IsTrue(twin.Verdict!.IsConsistent, "Wa9c: the ghost self-loop on wa9likesA carries no obligation onto wa9likesB.");
    }

    /// <summary>Wa10: arm S — a symmetry conclusion routes to one two-axiom fresh-witness probe (a told edge beside the concept denial of its reverse), which clashes when inverse-functionality over a range nominal pins the extension to its told self-edges and is satisfiable when nothing pins it.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa10ArmSSymmetrySkolemization()
    {
        OwlObjectPropertyReference role = ContextRole("wa10p");
        NamedNode memberA = ContextIndividual("wa10a");
        NamedNode memberB = ContextIndividual("wa10b");

        //Router: a symmetry characteristic over a named property routes to arm S.
        OwlObjectPropertyCharacteristicAxiom conclusion = new(OwlPropertyCharacteristic.Symmetric, role) { Origin = ContextOrigin("wa10sym") };
        if(ContextRefutations(conclusion) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("Wa10: the symmetry conclusion must have an arm-S encoding.");

            return;
        }

        Assert.HasCount(1, checks, "Wa10: arm S yields exactly one probe.");
        Assert.HasCount(2, checks[0].Axioms, "Wa10: the arm-S probe carries the told edge and the reverse edge's denial.");
        if(checks[0].Axioms[0] is not OwlObjectPropertyAssertionAxiom { Source: NamedNode edgeSource, Target: NamedNode edgeTarget } edge
            || checks[0].Axioms[1] is not OwlClassAssertionAxiom
            {
                Individual: NamedNode denialSource,
                Class: OwlObjectComplementOf { Operand: OwlObjectHasValue { Property: OwlObjectPropertyReference denialRole, Individual: NamedNode denialTarget } },
            })
        {
            Assert.Fail("Wa10: arm S must state a told edge between fresh witnesses beside the complement of the reverse edge's has-value reading.");

            return;
        }

        Assert.AreEqual(role.Named, edge.Property, "Wa10: the told edge is over the conclusion's property.");
        Assert.AreEqual(role.Named, denialRole.Named, "Wa10: the denial is over the conclusion's property.");
        Assert.AreEqual(edgeTarget, denialSource, "Wa10: the denial is asserted on the told edge's target.");
        Assert.AreEqual(edgeSource, denialTarget, "Wa10: the denial excludes the told edge's source as a value.");

        //Clash: inverse-functionality over the range nominal pins the extension to
        //the two told self-edges, where every edge's reverse is present.
        ReasoningModule pinned = new(
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, role) { Origin = ContextOrigin("wa10inverse") },
            new OwlObjectPropertyRangeAxiom(role, new OwlObjectOneOf([memberA, memberB])) { Origin = ContextOrigin("wa10range") },
            new OwlObjectPropertyAssertionAxiom(memberA, role.Named, memberA) { Origin = ContextOrigin("wa10aa") },
            new OwlObjectPropertyAssertionAxiom(memberB, role.Named, memberB) { Origin = ContextOrigin("wa10bb") },
        ], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(Extend(pinned, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa10: the arm-S probe stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa10: the pinned extension holds no edge whose reverse is absent — inconsistent.");

        //Control: without inverse-functionality nothing pins the extension, so a
        //fresh edge whose reverse is absent is satisfiable.
        ReasoningModule loose = new(
        [
            new OwlObjectPropertyRangeAxiom(role, new OwlObjectOneOf([memberA, memberB])) { Origin = ContextOrigin("wa10looserange") },
            new OwlObjectPropertyAssertionAxiom(memberA, role.Named, memberA) { Origin = ContextOrigin("wa10looseaa") },
            new OwlObjectPropertyAssertionAxiom(memberB, role.Named, memberB) { Origin = ContextOrigin("wa10loosebb") },
        ], Violations: []);
        ModuleDecision satisfiable = await Awaited(ContextArm(Extend(loose, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, satisfiable.Outcome, "Wa10: the control module stays in the context fragment.");
        Assert.IsTrue(satisfiable.Verdict!.IsConsistent, "Wa10: without the inverse-functional pin the fresh edge's reverse may be absent — consistent.");
    }

    /// <summary>Wa11: arm T — a transitivity conclusion routes to one three-axiom fresh-witness probe (a two-step path beside the concept denial of its shortcut), which clashes when symmetry reflects a range nominal onto the sources and is satisfiable without that reflection.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa11ArmTTransitivitySkolemization()
    {
        OwlObjectPropertyReference role = ContextRole("wa11p");
        NamedNode memberA = ContextIndividual("wa11a");
        NamedNode memberB = ContextIndividual("wa11b");

        //Router: a transitivity characteristic over a named property routes to arm T.
        OwlObjectPropertyCharacteristicAxiom conclusion = new(OwlPropertyCharacteristic.Transitive, role) { Origin = ContextOrigin("wa11trans") };
        if(ContextRefutations(conclusion) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("Wa11: the transitivity conclusion must have an arm-T encoding.");

            return;
        }

        Assert.HasCount(1, checks, "Wa11: arm T yields exactly one probe.");
        Assert.HasCount(3, checks[0].Axioms, "Wa11: the arm-T probe carries the two-step path and the shortcut's denial.");
        if(checks[0].Axioms[0] is not OwlObjectPropertyAssertionAxiom { Source: NamedNode first, Target: NamedNode middle } firstStep
            || checks[0].Axioms[1] is not OwlObjectPropertyAssertionAxiom { Source: NamedNode secondSource, Target: NamedNode last } secondStep
            || checks[0].Axioms[2] is not OwlClassAssertionAxiom
            {
                Individual: NamedNode denialSource,
                Class: OwlObjectComplementOf { Operand: OwlObjectHasValue { Property: OwlObjectPropertyReference denialRole, Individual: NamedNode denialTarget } },
            })
        {
            Assert.Fail("Wa11: arm T must state a two-step path between fresh witnesses beside the complement of the shortcut's has-value reading.");

            return;
        }

        Assert.AreEqual(role.Named, firstStep.Property, "Wa11: the first step is over the conclusion's property.");
        Assert.AreEqual(role.Named, secondStep.Property, "Wa11: the second step is over the conclusion's property.");
        Assert.AreEqual(role.Named, denialRole.Named, "Wa11: the denial is over the conclusion's property.");
        Assert.AreEqual(middle, secondSource, "Wa11: the second step continues from the first step's target.");
        Assert.AreEqual(first, denialSource, "Wa11: the denial is asserted on the path's source.");
        Assert.AreEqual(last, denialTarget, "Wa11: the denial excludes the path's far end as a value.");

        //Clash: symmetry reflects the range nominal onto the sources, confining the
        //extension to a two-element square where every two-step path has its shortcut.
        ReasoningModule pinned = new(
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, role) { Origin = ContextOrigin("wa11sym") },
            new OwlObjectPropertyRangeAxiom(role, new OwlObjectOneOf([memberA, memberB])) { Origin = ContextOrigin("wa11range") },
            new OwlObjectPropertyAssertionAxiom(memberA, role.Named, memberA) { Origin = ContextOrigin("wa11aa") },
            new OwlObjectPropertyAssertionAxiom(memberB, role.Named, memberB) { Origin = ContextOrigin("wa11bb") },
        ], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(Extend(pinned, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa11: the arm-T probe stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa11: the reflected nominal confines the path to the enumerated pair, where the shortcut holds — inconsistent.");
        Assert.AreEqual(0, clash.Statistics.ContextTotals.NominalPinnedRoleDeciderClashes, "Wa11: symmetry over the range nominal BOXES the extension rather than pinning its diagonal, so the module is outside the nominal-pinned-role face's jurisdiction — the ENGINE owns this decision and the face's counter stays at zero.");

        //Control: without symmetry the range never reaches the path's source, so a
        //two-step path missing its shortcut is satisfiable.
        ReasoningModule loose = new(
        [
            new OwlObjectPropertyRangeAxiom(role, new OwlObjectOneOf([memberA, memberB])) { Origin = ContextOrigin("wa11looserange") },
            new OwlObjectPropertyAssertionAxiom(memberA, role.Named, memberA) { Origin = ContextOrigin("wa11looseaa") },
            new OwlObjectPropertyAssertionAxiom(memberB, role.Named, memberB) { Origin = ContextOrigin("wa11loosebb") },
        ], Violations: []);
        ModuleDecision satisfiable = await Awaited(ContextArm(Extend(loose, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, satisfiable.Outcome, "Wa11: the control module stays in the context fragment.");
        Assert.IsTrue(satisfiable.Verdict!.IsConsistent, "Wa11: without symmetry the path's source escapes the nominal and the shortcut may be absent — consistent.");
    }

    /// <summary>Wa12: arm E — a property equivalence routes to TWO fresh-witness probes, one per inclusion, each clashing against the functional has-value premise that forces both extensions equal, and each satisfiable once its direction's functional characteristic is dropped; an unrelated premise settles neither way but consistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa12ArmEPropertyEquivalenceSkolemization()
    {
        OwlObjectPropertyReference left = ContextRole("wa12p");
        OwlObjectPropertyReference right = ContextRole("wa12q");
        OwlClassReference domain = ContextClass("Wa12D");
        NamedNode value = ContextIndividual("wa12v");

        //Router: an equivalence between two named properties routes to arm E.
        OwlEquivalentObjectPropertiesAxiom conclusion = new(left, right) { Origin = ContextOrigin("wa12equiv") };
        if(ContextRefutations(conclusion) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("Wa12: the property equivalence must have an arm-E encoding.");

            return;
        }

        Assert.HasCount(2, checks, "Wa12: arm E yields one probe per inclusion.");
        Assert.HasCount(2, checks[0].Axioms, "Wa12: the forward probe carries the told edge and the mirror's denial.");
        Assert.HasCount(2, checks[1].Axioms, "Wa12: the backward probe carries the told edge and the mirror's denial.");
        if(checks[0].Axioms[0] is not OwlObjectPropertyAssertionAxiom { Source: NamedNode forwardSource, Target: NamedNode forwardTarget } forwardEdge
            || checks[0].Axioms[1] is not OwlClassAssertionAxiom
            {
                Individual: NamedNode forwardDenialSource,
                Class: OwlObjectComplementOf { Operand: OwlObjectHasValue { Property: OwlObjectPropertyReference forwardDenialRole, Individual: NamedNode forwardDenialTarget } },
            }
            || checks[1].Axioms[0] is not OwlObjectPropertyAssertionAxiom backwardEdge
            || checks[1].Axioms[1] is not OwlClassAssertionAxiom
            {
                Class: OwlObjectComplementOf { Operand: OwlObjectHasValue { Property: OwlObjectPropertyReference backwardDenialRole } },
            })
        {
            Assert.Fail("Wa12: each arm-E probe must state a told edge over one property beside the complement of the other property's has-value reading.");

            return;
        }

        Assert.AreEqual(left.Named, forwardEdge.Property, "Wa12: the forward probe's told edge is over the first property.");
        Assert.AreEqual(right.Named, forwardDenialRole.Named, "Wa12: the forward probe denies the second property.");
        Assert.AreEqual(forwardSource, forwardDenialSource, "Wa12: the forward denial is asserted on the told edge's source.");
        Assert.AreEqual(forwardTarget, forwardDenialTarget, "Wa12: the forward denial excludes the told edge's target as a value.");
        Assert.AreEqual(right.Named, backwardEdge.Property, "Wa12: the backward probe's told edge is over the second property.");
        Assert.AreEqual(left.Named, backwardDenialRole.Named, "Wa12: the backward probe denies the first property.");

        //Clash: the domain axioms plus the two has-value equivalences force both
        //edges onto the shared value, and each functional characteristic merges the
        //probe's fresh target onto it, so both directions close.
        ReasoningModule forced = new(
        [
            new OwlObjectPropertyDomainAxiom(left, domain) { Origin = ContextOrigin("wa12pdomain") },
            new OwlObjectPropertyDomainAxiom(right, domain) { Origin = ContextOrigin("wa12qdomain") },
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, left) { Origin = ContextOrigin("wa12pfunctional") },
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, right) { Origin = ContextOrigin("wa12qfunctional") },
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(left, value)) { Origin = ContextOrigin("wa12pvalue") },
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(right, value)) { Origin = ContextOrigin("wa12qvalue") },
            new OwlClassAssertionAxiom(ThingReference, value) { Origin = ContextOrigin("wa12valueassert") },
        ], Violations: []);

        ModuleDecision forwardClash = await Awaited(ContextArm(Extend(forced, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, forwardClash.Outcome, "Wa12: the forward arm-E probe stays in the context fragment.");
        Assert.IsFalse(forwardClash.Verdict!.IsConsistent, "Wa12: the merged target carries the second property's edge, which the forward probe denies — inconsistent.");

        ModuleDecision backwardClash = await Awaited(ContextArm(Extend(forced, checks[1]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, backwardClash.Outcome, "Wa12: the backward arm-E probe stays in the context fragment.");
        Assert.IsFalse(backwardClash.Verdict!.IsConsistent, "Wa12: the merged target carries the first property's edge, which the backward probe denies — inconsistent.");

        //Control: without the first property's functional characteristic the probe's
        //fresh target never merges onto the shared value, so the forward direction
        //is genuinely satisfiable.
        ReasoningModule unmerged = new(
        [
            new OwlObjectPropertyDomainAxiom(left, domain) { Origin = ContextOrigin("wa12loosepdomain") },
            new OwlObjectPropertyDomainAxiom(right, domain) { Origin = ContextOrigin("wa12looseqdomain") },
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, right) { Origin = ContextOrigin("wa12looseqfunctional") },
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(left, value)) { Origin = ContextOrigin("wa12loosepvalue") },
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(right, value)) { Origin = ContextOrigin("wa12looseqvalue") },
            new OwlClassAssertionAxiom(ThingReference, value) { Origin = ContextOrigin("wa12loosevalueassert") },
        ], Violations: []);
        ModuleDecision unmergedDecision = await Awaited(ContextArm(Extend(unmerged, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unmergedDecision.Outcome, "Wa12: the control module stays in the context fragment.");
        Assert.IsTrue(unmergedDecision.Verdict!.IsConsistent, "Wa12: without the merge the fresh target keeps no second-property edge — consistent.");

        //Sanity: a premise that says nothing about the probed properties leaves the
        //fresh witnesses free, so the probe does not over-fire.
        ReasoningModule unrelated = new(
        [
            new OwlObjectPropertyAssertionAxiom(ContextIndividual("wa12x"), ContextRole("wa12r").Named, ContextIndividual("wa12y")) { Origin = ContextOrigin("wa12unrelated") },
        ], Violations: []);
        ModuleDecision unrelatedDecision = await Awaited(ContextArm(Extend(unrelated, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unrelatedDecision.Outcome, "Wa12: the unrelated module stays in the context fragment.");
        Assert.IsTrue(unrelatedDecision.Verdict!.IsConsistent, "Wa12: a bare told edge forces nothing on the probed properties — consistent.");
    }

    /// <summary>Wa13: the anonymous-forest rollup — the rooted chain folds to the nested existential its recursive premise closes and its one-hop sibling does not, an in-degree-two blank, a blank shared with an individual equality, and a blank in a class-expression position all decline, and the accepted shapes fold class conjuncts, named leaves, and disjoint components exactly.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa13AnonymousForestRollup()
    {
        OwlObjectPropertyReference parent = ContextRole("wa13parent");
        OwlObjectPropertyReference other = ContextRole("wa13other");
        NamedNode fred = ContextIndividual("wa13fred");
        NamedNode mary = ContextIndividual("wa13mary");
        NamedNode bob = ContextIndividual("wa13bob");
        BlankNode middle = new(Utf8Strings.From("wa13x"));
        BlankNode leaf = new(Utf8Strings.From("wa13y"));
        OwlClassReference person = ContextClass("Wa13Person");

        //Acceptance: the chain rolls up onto its named root as the nested
        //existential, its owl:Thing conjuncts dropped, and the root's own named
        //class assertion stays in the per-axiom walk.
        OwlAxiom[] chain =
        [
            new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13chain1") },
            new OwlObjectPropertyAssertionAxiom(middle, parent.Named, leaf) { Origin = ContextOrigin("wa13chain2") },
            new OwlClassAssertionAxiom(ThingReference, middle) { Origin = ContextOrigin("wa13thingx") },
            new OwlClassAssertionAxiom(ThingReference, leaf) { Origin = ContextOrigin("wa13thingy") },
            new OwlClassAssertionAxiom(ThingReference, fred) { Origin = ContextOrigin("wa13thingfred") },
        ];
        HashSet<int> consumed = [];
        List<ContextRefutationProbe> rollups = [];
        Assert.IsTrue(TryFoldAnonymousForest(chain, consumed, rollups), "Wa13: a rooted in-degree-one chain is an eligible component.");
        Assert.HasCount(1, rollups, "Wa13: the chain is one connected component, so it emits one probe.");
        Assert.HasCount(1, rollups[0].Axioms, "Wa13: the rollup probe is a single class assertion on the root.");
        Assert.HasCount(4, consumed, "Wa13: the four component axioms are consumed by position.");
        Assert.DoesNotContain(4, consumed, "Wa13: the root's own named class assertion mentions no blank and stays in the per-axiom walk.");
        if(rollups[0].Axioms[0] is not OwlClassAssertionAxiom
        {
            Individual: NamedNode rolledRoot,
            Class: OwlObjectComplementOf
            {
                Operand: OwlObjectSomeValuesFrom
                {
                    Property: OwlObjectPropertyReference outerRole,
                    Filler: OwlObjectSomeValuesFrom { Property: OwlObjectPropertyReference innerRole, Filler: OwlClassReference innerFiller },
                },
            },
        })
        {
            Assert.Fail("Wa13: the chain must roll up to the complement of a twice-nested existential on the named root.");

            return;
        }

        Assert.AreEqual(fred, rolledRoot, "Wa13: the rollup is asserted on the chain's named root.");
        Assert.AreEqual(parent.Named, outerRole.Named, "Wa13: the outer existential is over the chain's property.");
        Assert.AreEqual(parent.Named, innerRole.Named, "Wa13: the inner existential is over the chain's property.");
        Assert.AreEqual(ThingReference, innerFiller, "Wa13: the leaf's dropped owl:Thing conjunct leaves the implicit owl:Thing filler.");

        //Clash: the recursive premise forces the two-step chain from every person,
        //so denying it on an asserted person is inconsistent.
        ReasoningModule recursive = new(
        [
            new OwlEquivalentClassesAxiom(person, new OwlObjectSomeValuesFrom(parent, person)) { Origin = ContextOrigin("wa13recursive") },
            new OwlClassAssertionAxiom(person, fred) { Origin = ContextOrigin("wa13fredperson") },
        ], Violations: []);
        ModuleDecision clash = await Awaited(ContextArm(Extend(recursive, rollups[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, "Wa13: the rollup probe stays in the context fragment.");
        Assert.IsFalse(clash.Verdict!.IsConsistent, "Wa13: the recursive premise forces the whole chain, so denying it clashes — inconsistent.");

        //Control: a one-hop premise forces the first step only, so the second step
        //may be absent and the rollup is satisfiable.
        ReasoningModule oneHop = new(
        [
            new OwlEquivalentClassesAxiom(person, new OwlObjectSomeValuesFrom(parent, ThingReference)) { Origin = ContextOrigin("wa13onehop") },
            new OwlClassAssertionAxiom(person, fred) { Origin = ContextOrigin("wa13onehopfred") },
        ], Violations: []);
        ModuleDecision oneHopDecision = await Awaited(ContextArm(Extend(oneHop, rollups[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, oneHopDecision.Outcome, "Wa13: the one-hop control stays in the context fragment.");
        Assert.IsTrue(oneHopDecision.Verdict!.IsConsistent, "Wa13: only the first step is forced, so the chain's second step may be absent — consistent.");

        //Refusal: a blank reached by two edges is a jointly-witnessed existential no
        //per-component decomposition states, so the whole conclusion declines.
        consumed.Clear();
        rollups.Clear();
        Assert.IsFalse(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13sharedone") },
                new OwlObjectPropertyAssertionAxiom(fred, other.Named, middle) { Origin = ContextOrigin("wa13sharedtwo") },
            ],
            consumed,
            rollups),
            "Wa13: a blank of in-degree two declines the component.");

        //Refusal: two named parents sharing one blank is the same joint witness with
        //two roots, and both structural rules decline it.
        consumed.Clear();
        rollups.Clear();
        Assert.IsFalse(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13twoparentsone") },
                new OwlObjectPropertyAssertionAxiom(mary, parent.Named, middle) { Origin = ContextOrigin("wa13twoparentstwo") },
            ],
            consumed,
            rollups),
            "Wa13: two named parents sharing one blank decline the component.");

        //Refusal: an individual equality on a component blank is neither an edge nor
        //a class assertion, so the component declines.
        consumed.Clear();
        rollups.Clear();
        Assert.IsFalse(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13sameedge") },
                new OwlSameIndividualAxiom(middle, bob) { Origin = ContextOrigin("wa13same") },
            ],
            consumed,
            rollups),
            "Wa13: a blank shared with an individual equality declines the component.");

        //Refusal: a blank standing inside a class expression is not an
        //existentially-read node of the forest, so the component declines.
        consumed.Clear();
        rollups.Clear();
        Assert.IsFalse(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13nestededge") },
                new OwlClassAssertionAxiom(new OwlObjectHasValue(other, middle), mary) { Origin = ContextOrigin("wa13nested") },
            ],
            consumed,
            rollups),
            "Wa13: a blank inside a class-expression position declines the component.");

        //Acceptance: a non-owl:Thing class conjunct folds into the existential's filler.
        consumed.Clear();
        rollups.Clear();
        Assert.IsTrue(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13classedge") },
                new OwlClassAssertionAxiom(person, middle) { Origin = ContextOrigin("wa13classfiller") },
            ],
            consumed,
            rollups),
            "Wa13: a class assertion on a component blank is an eligible conjunct.");
        Assert.HasCount(1, rollups, "Wa13: the class-conjunct component emits one probe.");
        if(rollups[0].Axioms[0] is not OwlClassAssertionAxiom
        {
            Class: OwlObjectComplementOf { Operand: OwlObjectSomeValuesFrom { Filler: OwlClassReference conjunctFiller } },
        })
        {
            Assert.Fail("Wa13: a single class conjunct must collapse into the existential's filler.");

            return;
        }

        Assert.AreEqual(person, conjunctFiller, "Wa13: the blank's asserted class is the existential's filler.");

        //Acceptance: a named target is a leaf, folded as the individual-value restriction.
        consumed.Clear();
        rollups.Clear();
        Assert.IsTrue(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13leafedge") },
                new OwlObjectPropertyAssertionAxiom(middle, other.Named, bob) { Origin = ContextOrigin("wa13leaf") },
            ],
            consumed,
            rollups),
            "Wa13: a named target is an eligible leaf.");
        if(rollups[0].Axioms[0] is not OwlClassAssertionAxiom
        {
            Class: OwlObjectComplementOf
            {
                Operand: OwlObjectSomeValuesFrom { Filler: OwlObjectHasValue { Property: OwlObjectPropertyReference leafRole, Individual: NamedNode leafTarget } },
            },
        })
        {
            Assert.Fail("Wa13: a named leaf must fold to an individual-value restriction.");

            return;
        }

        Assert.AreEqual(other.Named, leafRole.Named, "Wa13: the leaf restriction is over its edge's property.");
        Assert.AreEqual(bob, leafTarget, "Wa13: the leaf restriction names the edge's target.");

        //Acceptance: two components share no blank, so each rolls up on its own root.
        consumed.Clear();
        rollups.Clear();
        Assert.IsTrue(
            TryFoldAnonymousForest(
            [
                new OwlObjectPropertyAssertionAxiom(fred, parent.Named, middle) { Origin = ContextOrigin("wa13firstedge") },
                new OwlClassAssertionAxiom(person, middle) { Origin = ContextOrigin("wa13firstclass") },
                new OwlObjectPropertyAssertionAxiom(mary, parent.Named, leaf) { Origin = ContextOrigin("wa13secondedge") },
                new OwlClassAssertionAxiom(person, leaf) { Origin = ContextOrigin("wa13secondclass") },
            ],
            consumed,
            rollups),
            "Wa13: two blank-disjoint components are both eligible.");
        Assert.HasCount(2, rollups, "Wa13: each component emits its own probe.");
        Assert.HasCount(4, consumed, "Wa13: both components' axioms are consumed.");
        Assert.AreEqual(fred, ((OwlClassAssertionAxiom)rollups[0].Axioms[0]).Individual, "Wa13: the first component rolls up onto its own root.");
        Assert.AreEqual(mary, ((OwlClassAssertionAxiom)rollups[1].Axioms[0]).Individual, "Wa13: the second component rolls up onto its own root.");
    }

    /// <summary>
    /// Wa14: the ground negative-assertion encoding's three measured shapes.
    /// (a) A derived edge that needs no merge meets a told negative assertion, so
    /// the module decides INCONSISTENT. (b) A told edge on a counting role trips
    /// the clausifier's ground-edge guard, so the module is not admitted and the
    /// seam abstains with zero spent rule applications — the conservative
    /// non-admission the guard exists for, told-shapes-only and blind to derived
    /// successors. (c) THE DEFECT PIN: the module is semantically unsatisfiable —
    /// the domain axioms and the two has-value equivalences force the first
    /// property's edge onto the shared value, the functional characteristic merges
    /// the told target onto it, and the merged second-property edge contradicts
    /// the told negative assertion — yet the arm decides it CONSISTENT. The row
    /// asserts that measured verdict: a negative root fact does not meet a
    /// merge-derived positive edge through the nominal branch's equality
    /// rewriting, an OPEN root-tier soundness defect awaiting its own rung. This
    /// is the one place the suite holds a known-wrong decided verdict green, and
    /// it turns red the day the engine closes the hole, forcing the unpin; no
    /// corpus row reaches the shape, so the corpus census stays zero-wrong.
    /// </summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task Wa14GroundNegativeAssertionMergePins()
    {
        OwlObjectPropertyReference left = ContextRole("wa14p");
        OwlObjectPropertyReference right = ContextRole("wa14q");
        OwlClassReference domain = ContextClass("Wa14D");
        NamedNode subject = ContextIndividual("wa14u");
        NamedNode value = ContextIndividual("wa14v");
        NamedNode witness = ContextIndividual("wa14w");

        //Wa14a: no merge is needed, so the derived edge meets the told denial.
        ReasoningModule derived = new(
        [
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(right, value)) { Origin = ContextOrigin("wa14avalue") },
            new OwlClassAssertionAxiom(domain, subject) { Origin = ContextOrigin("wa14aassert") },
            new OwlNegativeObjectPropertyAssertionAxiom(subject, right, value) { Origin = ContextWitnessQuad() },
        ], Violations: []);
        ModuleDecision derivedDecision = await Awaited(ContextArm(derived, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, derivedDecision.Outcome, "Wa14a: the derived-edge module stays in the context fragment.");
        Assert.IsFalse(derivedDecision.Verdict!.IsConsistent, "Wa14a: the has-value equivalence derives the denied edge without a merge — inconsistent.");

        //Wa14b: told edges on a counting role leave a clausifier remainder, so the
        //second gate delegates before any rule fires.
        ReasoningModule guarded = new(
        [
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, left) { Origin = ContextOrigin("wa14bfunctional") },
            new OwlObjectPropertyAssertionAxiom(subject, left.Named, witness) { Origin = ContextOrigin("wa14bfirst") },
            new OwlObjectPropertyAssertionAxiom(subject, left.Named, value) { Origin = ContextOrigin("wa14bsecond") },
            new OwlObjectPropertyAssertionAxiom(subject, right.Named, value) { Origin = ContextOrigin("wa14bthird") },
            new OwlNegativeObjectPropertyAssertionAxiom(subject, right, witness) { Origin = ContextWitnessQuad() },
        ], Violations: []);
        ModuleDecision guardedDecision = await Awaited(ContextArm(guarded, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, guardedDecision.Outcome, "Wa14b: the told-merge shape is not admitted, so the seam abstains.");
        Assert.AreEqual(0L, guardedDecision.Statistics.ContextTotals.RuleApplications, "Wa14b: the non-admission stops the module before any rule fires.");

        //Wa14c: the pinned wrong verdict.
        ReasoningModule merged = new(
        [
            new OwlObjectPropertyDomainAxiom(left, domain) { Origin = ContextOrigin("wa14cpdomain") },
            new OwlObjectPropertyDomainAxiom(right, domain) { Origin = ContextOrigin("wa14cqdomain") },
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, left) { Origin = ContextOrigin("wa14cpfunctional") },
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, right) { Origin = ContextOrigin("wa14cqfunctional") },
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(left, value)) { Origin = ContextOrigin("wa14cpvalue") },
            new OwlEquivalentClassesAxiom(domain, new OwlObjectHasValue(right, value)) { Origin = ContextOrigin("wa14cqvalue") },
            new OwlClassAssertionAxiom(ThingReference, value) { Origin = ContextOrigin("wa14cvalueassert") },
            new OwlObjectPropertyAssertionAxiom(subject, left.Named, witness) { Origin = ContextOrigin("wa14cedge") },
            new OwlNegativeObjectPropertyAssertionAxiom(subject, right, witness) { Origin = ContextWitnessQuad() },
        ], Violations: []);
        ModuleDecision mergedDecision = await Awaited(ContextArm(merged, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, mergedDecision.Outcome, "Wa14c: the nominal-jurisdiction module is admitted and claims a whole verdict.");
        Assert.IsTrue(mergedDecision.Verdict!.IsConsistent, "Wa14c: the arm decides this unsatisfiable module CONSISTENT — the pinned root-tier defect, red the day it is fixed.");
    }

    /// <summary>The alpha/beta clash: a class equivalent to a range-less data min-cardinality of one and a class equivalent to a range-less data max-cardinality of zero over the same property, the first a subclass of the second, populated by one member — the member forces the value-existence atom and forbids it, a Boolean concept clash decided inconsistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityAlphaBetaSamePropertyClashDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("dc1p");
        OwlClassReference cd = ContextClass("Dc1Cd");
        OwlClassReference de = ContextClass("Dc1De");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, property, Range: null)) { Origin = ContextOrigin("dc1alpha") },
            new OwlEquivalentClassesAxiom(de, new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, Range: null)) { Origin = ContextOrigin("dc1beta") },
            new OwlSubClassOfAxiom(cd, de) { Origin = ContextOrigin("dc1sub") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc1i")) { Origin = ContextOrigin("dc1assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The alpha/beta module is admitted through the value-existence route and decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The member forces the value-existence atom and forbids it — a Boolean concept clash, inconsistent.");
    }

    /// <summary>The distinct-property soundness probe (hazard 1): a range-less data min-cardinality of one over one property and a range-less data max-cardinality of zero over a DIFFERENT property manufacture no clash, so the populated module decides consistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityAlphaBetaDistinctPropertiesDecidesConsistent()
    {
        NamedNode propertyP = ContextDataProperty("dc2p");
        NamedNode propertyQ = ContextDataProperty("dc2q");
        OwlClassReference cd = ContextClass("Dc2Cd");
        OwlClassReference de = ContextClass("Dc2De");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, propertyP, Range: null)) { Origin = ContextOrigin("dc2alpha") },
            new OwlEquivalentClassesAxiom(de, new OwlDataCardinality(OwlCardinalityKind.Max, 0, propertyQ, Range: null)) { Origin = ContextOrigin("dc2beta") },
            new OwlSubClassOfAxiom(cd, de) { Origin = ContextOrigin("dc2sub") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc2i")) { Origin = ContextOrigin("dc2assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The distinct-property module is admitted and decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The value-existence markers are keyed per property, so the forced and forbidden markers do not meet — consistent.");
    }

    /// <summary>The gamma decomposition (layer b): a range-less data exact-cardinality of zero lowers identically to the max-of-zero beta shape, so a class forcing a value and a class forbidding it over the same property clash inconsistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityGammaExactZeroDecomposesToBetaClash()
    {
        NamedNode property = ContextDataProperty("dc3p");
        OwlClassReference cd = ContextClass("Dc3Cd");
        OwlClassReference ge = ContextClass("Dc3Ge");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, property, Range: null)) { Origin = ContextOrigin("dc3alpha") },
            new OwlEquivalentClassesAxiom(ge, new OwlDataCardinality(OwlCardinalityKind.Exact, 0, property, Range: null)) { Origin = ContextOrigin("dc3gamma") },
            new OwlSubClassOfAxiom(cd, ge) { Origin = ContextOrigin("dc3sub") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc3i")) { Origin = ContextOrigin("dc3assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The gamma module is admitted through the value-existence route and decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The exact-of-zero reduces to the max-of-zero negation, so the forced and forbidden markers clash — inconsistent.");
    }

    /// <summary>The alpha-alone soundness probe (hazard 1): a range-less data min-cardinality of one over a range-less filler is trivially satisfiable (the literal value space is inhabited), so a populated class equivalent to it decides consistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityMinOneAloneDecidesConsistent()
    {
        NamedNode property = ContextDataProperty("dc4p");
        OwlClassReference cd = ContextClass("Dc4Cd");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, property, Range: null)) { Origin = ContextOrigin("dc4alpha") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc4i")) { Origin = ContextOrigin("dc4assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The alpha-alone module is admitted through the value-existence route and decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A range-less min-of-one is satisfiable over the inhabited literal value space — consistent.");
    }

    /// <summary>The beta-alone soundness probe (hazard 1): a range-less data max-cardinality of zero forbids a value on its members, and nothing forces one, so a populated class equivalent to it decides consistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityMaxZeroAloneDecidesConsistent()
    {
        NamedNode property = ContextDataProperty("dc5p");
        OwlClassReference de = ContextClass("Dc5De");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(de, new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, Range: null)) { Origin = ContextOrigin("dc5beta") },
            new OwlClassAssertionAxiom(de, ContextIndividual("dc5i")) { Origin = ContextOrigin("dc5assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The beta-alone module is admitted through the value-existence route and decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing forces the forbidden value-existence atom — consistent.");
    }

    /// <summary>The mixed object-and-data clash (hazard 2): the alpha/beta data clash fires on a member that ALSO carries an admitted object min-cardinality rider, so the composition of the value-existence route with the object-counting machinery still decides inconsistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityMixedObjectAndDataClashDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("dc6p");
        OwlClassReference cd = ContextClass("Dc6Cd");
        OwlClassReference de = ContextClass("Dc6De");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, property, Range: null)) { Origin = ContextOrigin("dc6alpha") },
            new OwlEquivalentClassesAxiom(de, new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, Range: null)) { Origin = ContextOrigin("dc6beta") },
            new OwlSubClassOfAxiom(cd, new OwlObjectCardinality(OwlCardinalityKind.Min, 1, ContextRole("dc6r"), Filler: null)) { Origin = ContextOrigin("dc6objrider") },
            new OwlSubClassOfAxiom(cd, de) { Origin = ContextOrigin("dc6sub") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc6i")) { Origin = ContextOrigin("dc6assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The mixed object/data module is admitted and decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The data value-existence clash fires alongside the admitted object-counting rider — inconsistent.");
    }

    /// <summary>The mixed object-and-data non-clash (hazard 2): an admitted object min-cardinality rider composes with distinct-property data cardinalities that do not clash, so the populated module decides consistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityMixedObjectAndDataDecidesConsistent()
    {
        NamedNode propertyP = ContextDataProperty("dc7p");
        NamedNode propertyQ = ContextDataProperty("dc7q");
        OwlClassReference cd = ContextClass("Dc7Cd");
        OwlClassReference de = ContextClass("Dc7De");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, propertyP, Range: null)) { Origin = ContextOrigin("dc7alpha") },
            new OwlEquivalentClassesAxiom(de, new OwlDataCardinality(OwlCardinalityKind.Max, 0, propertyQ, Range: null)) { Origin = ContextOrigin("dc7beta") },
            new OwlSubClassOfAxiom(cd, new OwlObjectCardinality(OwlCardinalityKind.Min, 1, ContextRole("dc7r"), Filler: null)) { Origin = ContextOrigin("dc7objrider") },
            new OwlSubClassOfAxiom(cd, de) { Origin = ContextOrigin("dc7sub") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc7i")) { Origin = ContextOrigin("dc7assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The mixed object/data module is admitted and decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The distinct-property markers do not meet under the object-counting rider — consistent.");
    }

    /// <summary>The second-gate grammar closure (layer d): an EquivalentClasses module carrying all three value-existence families clausifies with an empty remainder and passes the second gate — the widened plain-concept heads stay in the head-literal grammar.</summary>
    [TestMethod]
    public void DataCardinalitySecondGateAdmitsValueExistenceHeads()
    {
        NamedNode propertyP = ContextDataProperty("dc8p");
        NamedNode propertyQ = ContextDataProperty("dc8q");
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(ContextClass("Dc8Cd"), new OwlDataCardinality(OwlCardinalityKind.Min, 1, propertyP, Range: null)) { Origin = ContextOrigin("dc8alpha") },
            new OwlEquivalentClassesAxiom(ContextClass("Dc8De"), new OwlDataCardinality(OwlCardinalityKind.Max, 0, propertyP, Range: null)) { Origin = ContextOrigin("dc8beta") },
            new OwlEquivalentClassesAxiom(ContextClass("Dc8Ge"), new OwlDataCardinality(OwlCardinalityKind.Exact, 0, propertyQ, Range: null)) { Origin = ContextOrigin("dc8gamma") },
        ], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        Assert.HasCount(0, clausification.Remainder, "The alpha/beta/gamma families lower through the value-existence route with no remainder.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(clausification), "The widened plain-concept heads stay in the second gate's head-literal grammar, so the module does not delegate.");
    }

    /// <summary>The max-of-one guard negative (hazard 3): a data max-cardinality of ONE is genuine concrete-domain counting outside the {0,1} value-existence collapse, so the survey rejects it and the module delegates (stays beyond the fragment).</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityMaxOneKeepsDelegating()
    {
        NamedNode property = ContextDataProperty("dc9p");
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(ContextClass("Dc9Cd"), new OwlDataCardinality(OwlCardinalityKind.Max, 1, property, Range: null)) { Origin = ContextOrigin("dc9max") },
            new OwlClassAssertionAxiom(ContextClass("Dc9Cd"), ContextIndividual("dc9i")) { Origin = ContextOrigin("dc9assert") },
        ], Violations: []);

        Assert.IsGreaterThan(0, ContextClausifier.Clausify(module).Remainder.Count, "A max-of-one is outside the value-existence collapse, so the clausifier leaves a remainder.");
        ModuleDecision decision = await Awaited(ContextArm(module, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The max-of-one module is not admitted, so the seam abstains.");
        Assert.IsNull(decision.Verdict, "A beyond-fragment abstention carries no verdict.");
    }

    /// <summary>The ranged-filler guard negative (hazard 3): a data max-cardinality of zero over a NON-null range is a ranged counting shape outside the range-less value-existence collapse, so the survey rejects it and the module delegates.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityRangedMaxZeroKeepsDelegating()
    {
        NamedNode property = ContextDataProperty("dc10p");
        OwlDataRange stringRange = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(ContextClass("Dc10De"), new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, stringRange)) { Origin = ContextOrigin("dc10max") },
            new OwlClassAssertionAxiom(ContextClass("Dc10De"), ContextIndividual("dc10i")) { Origin = ContextOrigin("dc10assert") },
        ], Violations: []);

        Assert.IsGreaterThan(0, ContextClausifier.Clausify(module).Remainder.Count, "A ranged max-of-zero is outside the range-less collapse, so the clausifier leaves a remainder.");
        ModuleDecision decision = await Awaited(ContextArm(module, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The ranged max-of-zero module is not admitted, so the seam abstains.");
        Assert.IsNull(decision.Verdict, "A beyond-fragment abstention carries no verdict.");
    }

    /// <summary>The negative min-of-two guard negative (hazard 3): a data min-cardinality of TWO in subclass position is above the value-existence bound, so the survey rejects it at negative polarity and the module delegates.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityNegativeMinTwoKeepsDelegating()
    {
        NamedNode property = ContextDataProperty("dc11p");
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(ContextClass("Dc11Cd"), new OwlDataCardinality(OwlCardinalityKind.Min, 2, property, Range: null)) { Origin = ContextOrigin("dc11min") },
            new OwlClassAssertionAxiom(ContextClass("Dc11Cd"), ContextIndividual("dc11i")) { Origin = ContextOrigin("dc11assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(module, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "A min-of-two at negative polarity is above the value-existence bound, so the module is not admitted and the seam abstains.");
        Assert.IsNull(decision.Verdict, "A beyond-fragment abstention carries no verdict.");
    }

    /// <summary>The reserved-property choice (layer b, G1/C5): a data max-cardinality of zero over a reserved built-in data property fails the clausifier's non-reserved guard, falls to the GENERIC datatype-fragment rejection (never the distinct ReservedDataProperty name), leaves a remainder, and the module delegates.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityReservedPropertyMaxZeroKeepsDelegating()
    {
        NamedNode reserved = new(OwlVocabulary.TopDataProperty);
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(ContextClass("Dc12De"), new OwlDataCardinality(OwlCardinalityKind.Max, 0, reserved, Range: null)) { Origin = ContextOrigin("dc12max") },
            new OwlClassAssertionAxiom(ContextClass("Dc12De"), ContextIndividual("dc12i")) { Origin = ContextOrigin("dc12assert") },
        ], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        Assert.IsGreaterThan(0, clausification.Remainder.Count, "The reserved-property max-of-zero fails the non-reserved guard and leaves a remainder.");
        bool hasGenericRejection = false;
        foreach(string remainder in clausification.Remainder)
        {
            Assert.IsFalse(remainder.StartsWith("ReservedDataProperty(", StringComparison.Ordinal), "The reserved-property max-of-zero rejects through the generic datatype-fragment name, not the distinct ReservedDataProperty name.");
            if(remainder.EndsWith("outside the context datatype fragment.", StringComparison.Ordinal))
            {
                hasGenericRejection = true;
            }
        }

        Assert.IsTrue(hasGenericRejection, "The reserved-property max-of-zero falls to the generic DataExpressionRejection.");
        ModuleDecision decision = await Awaited(ContextArm(module, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The reserved-property module delegates on the remainder, so the seam abstains.");
        Assert.IsNull(decision.Verdict, "A delegated module carries no verdict.");
    }

    /// <summary>The POINT-4 decisiveness check (hazard 5): the alpha/beta clash fires ON the root context as a Boolean concept clash, so the raw engine reads a derived inconsistency with ALL FOUR post-saturation latch flags LOW — distinguishing a decisive inconsistency from an out-of-grammar derivation, a packed-width overflow, or either data-obligation latch.</summary>
    [TestMethod]
    public void DataCardinalityRootClashIsDecisiveNotLatched()
    {
        NamedNode property = ContextDataProperty("dc13p");
        OwlClassReference cd = ContextClass("Dc13Cd");
        OwlClassReference de = ContextClass("Dc13De");
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Min, 1, property, Range: null)) { Origin = ContextOrigin("dc13alpha") },
            new OwlEquivalentClassesAxiom(de, new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, Range: null)) { Origin = ContextOrigin("dc13beta") },
            new OwlSubClassOfAxiom(cd, de) { Origin = ContextOrigin("dc13sub") },
            new OwlClassAssertionAxiom(cd, ContextIndividual("dc13i")) { Origin = ContextOrigin("dc13assert") },
        ], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken);
        engine.RunGroundGhostPass();

        Assert.IsTrue(engine.IsInconsistent, "The value-existence clash fires on the root member's context — a decisive derived inconsistency.");
        Assert.IsFalse(engine.HasOutOfGrammarDerivation, "The new plain-atom heads stay in grammar, so no derivation escapes the second gate at rule-firing time.");
        Assert.IsFalse(engine.HasDataObligationUndecidedOnRoot, "The clash is a Boolean concept clash, not an undischarged root data obligation.");
        Assert.IsFalse(engine.HasPackedWidthOverflow, "The {0,1} block never approaches the packed individual width.");
        Assert.IsFalse(engine.HasUndecidedDataObligation, "No completed-saturation data obligation stays undecided.");
    }

    /// <summary>The state-A positive entailment (667 shape): a class equivalent to a range-less data max-cardinality of zero is the negation of the per-property value-existence atom, and a host subclass of the same max-cardinality forbids that atom on its asserted member, so the disjunctive value-existence head entails the member into the equivalent class — the premise decides consistent, and its ObjectComplementOf refutation on the entailed class decides inconsistent, the positive-entailment verdict.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityEntailmentRefutationDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("dc14p");
        OwlClassReference cd = ContextClass("Dc14Cd");
        OwlClassReference host = ContextClass("Dc14Host");
        NamedNode member = ContextIndividual("dc14v");
        ReasoningModule premise = new(
        [
            new OwlEquivalentClassesAxiom(cd, new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, Range: null)) { Origin = ContextOrigin("dc14beta") },
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Max, 0, property, Range: null)) { Origin = ContextOrigin("dc14host") },
            new OwlClassAssertionAxiom(host, member) { Origin = ContextOrigin("dc14assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The entailment premise is admitted through the value-existence route and decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The host member forbids the value-existence atom, so the disjunctive head settles it into the equivalent class without contradiction — consistent.");

        //Positive entailment dc14v : Dc14Cd — the ObjectComplementOf refutation clashes.
        ReasoningModule refuted = Extend(premise, new OwlClassAssertionAxiom(new OwlObjectComplementOf(cd), member) { Origin = ContextOrigin("dc14refute") });
        ModuleDecision refutation = await Awaited(ContextArm(refuted, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, refutation.Outcome, "The refutation module stays in the context fragment and decides whole.");
        Assert.IsFalse(refutation.Verdict!.IsConsistent, "The forbidden value-existence atom entails dc14v : Dc14Cd through the disjunctive head, so its complement refutation is inconsistent.");
    }

    /// <summary>The F3.1 range-membership clash (spec hazard H4): a told value outside its property's asserted range lowers to a point demand the sidecar conjoins with the range, so the out-of-range assertion decides inconsistent instead of admitting blindly.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task GroundDataAssertionRangeMembershipClashDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("gda1p");
        ReasoningModule premise = new(
        [
            new OwlDataPropertyRangeAxiom(property, new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")))) { Origin = ContextOrigin("gda1range") },
            new OwlDataPropertyAssertionAxiom(ContextIndividual("gda1i"), property, new Literal(Utf8Strings.From("abc"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")))) { Origin = ContextOrigin("gda1assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The lifted range co-occurrence admits the module and the point demand decides.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "A string value cannot inhabit the integer range — inconsistent.");
    }

    /// <summary>The F3.1 functional-pooling clash on a decidable family (spec hazard H2, positive direction): two distinct integer values pooled on one subject through a functional data property clash — the corpus exercises only the abstaining opaque-family side, so this row proves pooling DECIDES when the family is decidable.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task GroundDataAssertionFunctionalPoolingClashDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("gda2p");
        NamedNode subject = ContextIndividual("gda2i");
        NamedNode integer = new(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"));
        ReasoningModule premise = new(
        [
            new OwlFunctionalDataPropertyAxiom(property) { Origin = ContextOrigin("gda2functional") },
            new OwlDataPropertyAssertionAxiom(subject, property, new Literal(Utf8Strings.From("1"), integer)) { Origin = ContextOrigin("gda2one") },
            new OwlDataPropertyAssertionAxiom(subject, property, new Literal(Utf8Strings.From("2"), integer)) { Origin = ContextOrigin("gda2two") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The lifted functional co-occurrence admits the module and the pooled points decide.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Two distinct integers cannot share one functional slot — inconsistent.");
    }

    /// <summary>The F3.1 domain firing on a told value: the assertion's value-existence atom fires the domain GCI, and the domain class is forced empty, so the module decides inconsistent through the ground path.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task GroundDataAssertionDomainFiringClashDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("gda3p");
        OwlClassReference domain = ContextClass("Gda3Domain");
        ReasoningModule premise = new(
        [
            new OwlDataPropertyDomainAxiom(property, domain) { Origin = ContextOrigin("gda3domain") },
            new OwlSubClassOfAxiom(domain, NothingReference) { Origin = ContextOrigin("gda3empty") },
            new OwlDataPropertyAssertionAxiom(ContextIndividual("gda3i"), property, new Literal(Utf8Strings.From("v"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")))) { Origin = ContextOrigin("gda3assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The lifted domain co-occurrence admits the module and the GCI fires on the told value.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The subject is typed the empty domain class — inconsistent.");
    }

    /// <summary>The F3.1 inert-property preservation (spec hazard H7): a told value on a property with no TBox co-occurrence emits no demand and the module decides consistent — the pre-router behavior for every admitted module, byte-identical.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task GroundDataAssertionInertPropertyDecidesConsistent()
    {
        ReasoningModule premise = new(
        [
            new OwlDataPropertyAssertionAxiom(ContextIndividual("gda4i"), ContextDataProperty("gda4p"), new Literal(Utf8Strings.From("v"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")))) { Origin = ContextOrigin("gda4assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The inert assertion admits and decides without any demand.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "An unconstrained told value forces nothing — consistent.");
    }

    /// <summary>The F3.1 nominal-jurisdiction root lowering: the domain-firing clash of the ground row composed with a nominal enumeration, so the told assertion rides the root tier — a fresh atom GCI plus a root fact — and the clash fires on the root individual's approx-class.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task GroundDataAssertionNominalRootDomainClashDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("gda5p");
        OwlClassReference domain = ContextClass("Gda5Domain");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(ContextClass("Gda5B"), new OwlObjectOneOf([ContextIndividual("gda5x")])) { Origin = ContextOrigin("gda5oneof") },
            new OwlDataPropertyDomainAxiom(property, domain) { Origin = ContextOrigin("gda5domain") },
            new OwlSubClassOfAxiom(domain, NothingReference) { Origin = ContextOrigin("gda5empty") },
            new OwlDataPropertyAssertionAxiom(ContextIndividual("gda5i"), property, new Literal(Utf8Strings.From("v"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")))) { Origin = ContextOrigin("gda5assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The nominal module admits whole and the root lowering decides.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The root individual is typed the empty domain class through the told value — inconsistent.");
    }

    /// <summary>RVF-NEAR-MISS (the near-miss hazard): an ordinary object property whose local name resembles the reserved one but lives in a non-owl namespace must NOT fold, so the module admits and decides CONSISTENT through the normal existential path.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task RvfNearMissDecidesConsistent()
    {
        OwlObjectPropertyReference nearMiss = ContextRole("bottomObjectProperty");
        ReasoningModule premise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectSomeValuesFrom(nearMiss, ThingReference), ContextIndividual("rvfnmi")) { Origin = ContextOrigin("rvfnmassert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The example-namespace property is not the reserved bottom object property, so the fold leaves the existential and the module admits.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "An ordinary existential over a non-reserved property is satisfiable — consistent.");
    }

    /// <summary>RVF-BOT-ALL: a universal restriction over the empty <c>owl:bottomObjectProperty</c> folds to <c>owl:Thing</c>, so the folded ClassAssertion(owl:Thing, i) plus an unrelated consistent fact decides CONSISTENT — the fold does not over-clash.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task RvfBotAllDecidesConsistent()
    {
        OwlObjectPropertyReference bottom = new(new NamedNode(OwlVocabulary.BottomObjectProperty));
        ReasoningModule premise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectAllValuesFrom(bottom, ContextClass("RvfBotAllFiller")), ContextIndividual("rvfbai")) { Origin = ContextOrigin("rvfbaassert") },
            new OwlClassAssertionAxiom(ContextClass("RvfBotAllUnrelated"), ContextIndividual("rvfbaj")) { Origin = ContextOrigin("rvfbaunrelated") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The universal over bottom folds to owl:Thing, so the module admits whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A vacuous owl:Thing assertion forces nothing — consistent.");
    }

    /// <summary>RVF-BOT-DATA: the New-Feature-BottomDataProperty-001 shape as a synthetic on the SAT arm — a data existential over the empty <c>owl:bottomDataProperty</c> folds to <c>owl:Nothing</c>, so the class assertion decides INCONSISTENT.</summary>
    [TestMethod]
    public void RvfBotDataDecidesInconsistent()
    {
        OwlDataRange literal = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2000/01/rdf-schema#Literal")));
        ReasoningModule premise = new(
        [
            new OwlClassAssertionAxiom(new OwlDataSomeValuesFrom([new NamedNode(OwlVocabulary.BottomDataProperty)], literal), ContextIndividual("rvfbdi")) { Origin = ContextOrigin("rvfbdassert") },
        ], Violations: []);

        ModuleVerdict verdict = AlcModuleReasoner.Decide(premise, TestContext.CancellationToken);
        Assert.IsFalse(verdict.IsConsistent, "The bottom-data existential folds to owl:Nothing, so the individual is typed the empty class — inconsistent.");
        Assert.IsEmpty(verdict.UnsupportedConstructs, "The folded class assertion carries no reserved remainder, so the verdict covers the module whole.");
    }

    /// <summary>RVF-TOP-HASVALUE: an individual-value restriction over the universal <c>owl:topObjectProperty</c> folds to <c>owl:Thing</c>, so its complement folds to <c>owl:Nothing</c> and the class assertion decides INCONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task RvfTopHasValueDecidesInconsistent()
    {
        OwlObjectPropertyReference top = new(new NamedNode(OwlVocabulary.TopObjectProperty));
        ReasoningModule premise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectComplementOf(new OwlObjectHasValue(top, ContextIndividual("rvfhva"))), ContextIndividual("rvfhvi")) { Origin = ContextOrigin("rvfhvassert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The top has-value folds to owl:Thing and its complement to owl:Nothing, so the module admits whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The individual is typed the complement of owl:Thing — inconsistent.");
    }

    /// <summary>RVF-INVERSE (the near-miss hazard): an existential over <c>ObjectInverseOf(owl:bottomObjectProperty)</c> unwraps to the empty bottom object property, folds to <c>owl:Nothing</c>, and decides INCONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task RvfInverseDecidesInconsistent()
    {
        OwlInverseObjectProperty inverseBottom = new(new NamedNode(OwlVocabulary.BottomObjectProperty));
        ReasoningModule premise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectSomeValuesFrom(inverseBottom, ThingReference), ContextIndividual("rvfinvi")) { Origin = ContextOrigin("rvfinvassert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The inverse of bottom is the empty bottom, so the existential folds to owl:Nothing and the module admits whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The individual is typed the empty class — inconsistent.");
    }

    /// <summary>RVF-KEPT (the P8 sibling at pipeline level): a universal over the universal <c>owl:topObjectProperty</c> with a non-Thing filler is a global inclusion the pointwise fold keeps, so the folded module still carries the reserved role into the scan and the clausifier names <c>ReservedRoleInClassExpression(topObjectProperty)</c>.</summary>
    [TestMethod]
    public void RvfKeptRetainsReservedRoleRemainder()
    {
        OwlObjectPropertyReference top = new(new NamedNode(OwlVocabulary.TopObjectProperty));
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(ContextClass("RvfKeptA"), new OwlObjectAllValuesFrom(top, ContextClass("RvfKeptB"))) { Origin = ContextOrigin("rvfkeptsub") },
            new OwlClassAssertionAxiom(ContextClass("RvfKeptA"), ContextIndividual("rvfkepti")) { Origin = ContextOrigin("rvfkeptassert") },
        ], Violations: []);

        ReasoningModule folded = ReservedVocabularyFold.Apply(premise);
        Assert.AreSame(premise, folded, "A universal over top with a non-Thing filler is kept verbatim, so the fold returns the same module instance.");

        ClausificationResult clausification = ContextClausifier.Clausify(folded);
        Assert.Contains("ReservedRoleInClassExpression(http://www.w3.org/2002/07/owl#topObjectProperty)", clausification.Remainder, "The kept top universal is named on the clausifier remainder, so the pipeline rejects rather than folds.");
    }

    /// <summary>RVF-TOP-ALL-THING: a universal over the universal <c>owl:topObjectProperty</c> whose filler IS syntactically <c>owl:Thing</c> folds to <c>owl:Thing</c>, so its complement folds to <c>owl:Nothing</c> and the class assertion decides INCONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task RvfTopAllThingDecidesInconsistent()
    {
        OwlObjectPropertyReference top = new(new NamedNode(OwlVocabulary.TopObjectProperty));
        ReasoningModule premise = new(
        [
            new OwlClassAssertionAxiom(new OwlObjectComplementOf(new OwlObjectAllValuesFrom(top, ThingReference)), ContextIndividual("rvftati")) { Origin = ContextOrigin("rvftatassert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The top universal over owl:Thing folds to owl:Thing and its complement to owl:Nothing, so the module admits whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The individual is typed the complement of owl:Thing — inconsistent.");
    }

    /// <summary>RVF-NARY-BOTD: an n-ary data existential with <c>owl:bottomDataProperty</c> in ANY slot folds to <c>owl:Nothing</c> and decides INCONSISTENT; the sibling n-ary shape WITHOUT a bottom-data slot stays beyond the fragment and abstains — the kept contrast.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task RvfNaryBotdDecidesInconsistent()
    {
        OwlDataRange literal = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2000/01/rdf-schema#Literal")));
        NamedNode ordinary = ContextDataProperty("rvfnaryd");
        ReasoningModule withBottom = new(
        [
            new OwlClassAssertionAxiom(new OwlDataSomeValuesFrom([ordinary, new NamedNode(OwlVocabulary.BottomDataProperty)], literal), ContextIndividual("rvfnaryi")) { Origin = ContextOrigin("rvfnaryassert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(withBottom, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The bottom-data slot empties the whole tuple relation, so the n-ary existential folds to owl:Nothing and the module admits whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The individual is typed the empty class — inconsistent.");

        //Kept contrast: the same n-ary shape with no bottom-data slot does not fold and stays beyond the fragment.
        ReasoningModule withoutBottom = new(
        [
            new OwlClassAssertionAxiom(new OwlDataSomeValuesFrom([ordinary, ContextDataProperty("rvfnarye")], literal), ContextIndividual("rvfnaryi")) { Origin = ContextOrigin("rvfnaryassert") },
        ], Violations: []);

        ModuleDecision keptDecision = await Awaited(ContextArm(withoutBottom, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, keptDecision.Outcome, "The n-ary data shape without a bottom-data slot does not fold, so it stays beyond the arm's fragment and abstains through the sentinel fallback.");
    }

    /// <summary>F3.3 E1 (the WebOnt-I5.21-002 mini-shape): a range-less data exact-cardinality of ONE beside two distinct told values on the same property pools into the range-less single slot, whose merged conjunction is unsatisfiable — the module decides INCONSISTENT. The hazard-2 tripwire on the context route: a maximum marker reconstructed as a minimum demand would land in the wrong sidecar bucket, the slot would never see the bound, and the clash would be lost.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrExactOneTwoDistinctValuesDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("qcr1p");
        OwlClassReference host = ContextClass("Qcr1C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Exact, 1, property, Range: null)) { Origin = ContextOrigin("qcr1exact") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, StringLiteral("alpha"))) { Origin = ContextOrigin("qcr1alpha") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, StringLiteral("beta"))) { Origin = ContextOrigin("qcr1beta") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr1i")) { Origin = ContextOrigin("qcr1assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The exact-of-one and the two told values all admit, so the module decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "One range-less slot cannot hold two distinct xsd:string values — inconsistent.");
    }

    /// <summary>F3.3 E2 (the hazard-5 tripwire, points-only branch): a range-less data max-cardinality of TWO beside two provably-distinct told values is modelled by those two points themselves, so the module decides CONSISTENT — the single-slot clash primitive must not fire past bound one.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrMaxTwoTwoDistinctValuesStaysConsistent()
    {
        NamedNode property = ContextDataProperty("qcr2p");
        OwlClassReference host = ContextClass("Qcr2C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Max, 2, property, Range: null)) { Origin = ContextOrigin("qcr2max") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, StringLiteral("alpha"))) { Origin = ContextOrigin("qcr2alpha") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, StringLiteral("beta"))) { Origin = ContextOrigin("qcr2beta") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr2i")) { Origin = ContextOrigin("qcr2assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The max-of-two and the two told values all admit, so the module decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Two distinct points fit a bound of two, and the points are the witness model — consistent.");
    }

    /// <summary>F3.3 E2 companion (the hazard-5 tripwire, mixed pool): the same two distinct told values under a range-less EXACT cardinality of two pool a counting demand beside the points, and the max slot's witness construction certifies that pool — the two points fit the bound of two and each inhabits the counting demand's literal-top range, so they ARE the model and the module decides CONSISTENT. The tripwire keeps its clash-guard half: the single-slot clash primitive must never fire past bound one, so this shape is never inconsistent.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrExactTwoTwoDistinctValuesDecidesConsistent()
    {
        NamedNode property = ContextDataProperty("qcr3p");
        OwlClassReference host = ContextClass("Qcr3C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Exact, 2, property, Range: null)) { Origin = ContextOrigin("qcr3exact") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, StringLiteral("alpha"))) { Origin = ContextOrigin("qcr3alpha") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, StringLiteral("beta"))) { Origin = ContextOrigin("qcr3beta") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr3i")) { Origin = ContextOrigin("qcr3assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The exact-of-two and the two told values all admit, and the mixed pool certifies its own model, so the module decides whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The two told points are the witness model: they fit the bound of two and both witness the counting demand — consistent, and never the clash the tripwire guards.");
    }

    /// <summary>E2 M9 (the counting-only arm of the same shape): a range-less EXACT cardinality of two with NO told values pools one counting demand alone in the max slot, and the unconstrained data domain provably holds two distinct values, so the demand's own witnesses model the slot and the module decides CONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrExactTwoRangeLessAloneDecidesConsistent()
    {
        NamedNode property = ContextDataProperty("qcr10p");
        OwlClassReference host = ContextClass("Qcr10C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Exact, 2, property, Range: null)) { Origin = ContextOrigin("qcr10exact") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr10i")) { Origin = ContextOrigin("qcr10assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The range-less exact-of-two admits and both halves decide.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The data domain holds two distinct values, and two fits the bound of two — consistent.");
    }

    /// <summary>E2 M10 (the counting floor with no max slot at all): a bare range-less data MINIMUM cardinality of two is decided by the per-property counting loop alone — the unconstrained domain holds two distinct values — so the module decides CONSISTENT without any bound to certify.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrMinTwoRangeLessAloneDecidesConsistent()
    {
        NamedNode property = ContextDataProperty("qcr11p");
        OwlClassReference host = ContextClass("Qcr11C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Min, 2, property, Range: null)) { Origin = ContextOrigin("qcr11min") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr11i")) { Origin = ContextOrigin("qcr11assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The range-less minimum-of-two admits and the counting loop decides it.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The data domain holds two distinct values — consistent.");
    }

    /// <summary>F3.3 E3: a data exact-cardinality of THREE over <c>xsd:boolean</c> on an asserted individual clashes on its MINIMUM half — three pairwise-distinct values cannot inhabit the two-element boolean space — so the composite decides INCONSISTENT through the existing counting machinery.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrExactThreeBooleanDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("qcr4p");
        OwlClassReference host = ContextClass("Qcr4C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Exact, 3, property, BooleanRange)) { Origin = ContextOrigin("qcr4exact") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr4i")) { Origin = ContextOrigin("qcr4assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The ranged exact-of-three admits and the counting demand decides.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The boolean value space holds two values, not three — inconsistent.");
    }

    /// <summary>F3.3 E4 (the Qualified-cardinality-boolean corpus shape as a synthetic): a data exact-cardinality of TWO over <c>xsd:boolean</c> on an asserted individual rides the single-counting-demand certificate — the two boolean values are the model, and two fits the bound — so the module decides CONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrExactTwoBooleanDecidesConsistent()
    {
        NamedNode property = ContextDataProperty("qcr5p");
        OwlClassReference host = ContextClass("Qcr5C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Exact, 2, property, BooleanRange)) { Origin = ContextOrigin("qcr5exact") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr5i")) { Origin = ContextOrigin("qcr5assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The ranged exact-of-two admits and both halves decide.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The two boolean values meet the minimum and fit the maximum — consistent.");
    }

    /// <summary>F3.3 E5 (spec hazard H4, sound-or-silent): a data exact-cardinality of two over a TEXT-family range is a value space the checker does not size, so both halves come back undecided and the module delegates rather than claiming a verdict — the widened admission deliberately admits shapes the sidecar cannot count.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrRangedUncountableFamilyKeepsDelegating()
    {
        NamedNode property = ContextDataProperty("qcr6p");
        OwlClassReference host = ContextClass("Qcr6C");
        OwlDataRange token = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#token")));
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Exact, 2, property, token)) { Origin = ContextOrigin("qcr6exact") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr6i")) { Origin = ContextOrigin("qcr6assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The unsized text family leaves the counting obligation undecided, so the module delegates.");
        Assert.IsNull(decision.Verdict, "A delegated module carries no verdict.");
    }

    /// <summary>F3.3 E6 (spec hazard H7, pooling parity): a range-less data max-cardinality of ONE on <c>p</c> against two distinct told values reached through a SUB-property <c>q</c> clashes only when the slot sweeps the sub-or-self closure exactly as the functional pool does — a missed sub-property demand would certify the slot satisfiable while a hidden second value forces it.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrMaxOneSubPropertyValueDecidesInconsistent()
    {
        NamedNode super = ContextDataProperty("qcr7p");
        NamedNode sub = ContextDataProperty("qcr7q");
        OwlClassReference host = ContextClass("Qcr7C");
        ReasoningModule premise = new(
        [
            new OwlSubDataPropertyOfAxiom(sub, super) { Origin = ContextOrigin("qcr7hierarchy") },
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Max, 1, super, Range: null)) { Origin = ContextOrigin("qcr7max") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(sub, StringLiteral("alpha"))) { Origin = ContextOrigin("qcr7alpha") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(sub, StringLiteral("beta"))) { Origin = ContextOrigin("qcr7beta") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr7i")) { Origin = ContextOrigin("qcr7assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The sub-property hierarchy and the max-of-one both admit, so the module decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The sub-property's two distinct values are both super-property values, and one range-less slot cannot hold them — inconsistent.");
    }

    /// <summary>F3.3 E7 (the reserved-property guard): a data exact-cardinality of ONE over a reserved built-in data property fails the clausifier's non-reserved guard, falls to the GENERIC datatype-fragment rejection (never the distinct <c>ReservedDataProperty</c> name), leaves a remainder, and the module delegates — the widened admission still declines the reserved vocabulary.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrReservedPropertyExactOneKeepsDelegating()
    {
        NamedNode reserved = new(OwlVocabulary.TopDataProperty);
        ReasoningModule module = new(
        [
            new OwlEquivalentClassesAxiom(ContextClass("Qcr8C"), new OwlDataCardinality(OwlCardinalityKind.Exact, 1, reserved, Range: null)) { Origin = ContextOrigin("qcr8exact") },
            new OwlClassAssertionAxiom(ContextClass("Qcr8C"), ContextIndividual("qcr8i")) { Origin = ContextOrigin("qcr8assert") },
        ], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        Assert.IsGreaterThan(0, clausification.Remainder.Count, "The reserved-property exact-of-one fails the non-reserved guard and leaves a remainder.");
        bool hasGenericRejection = false;
        foreach(string remainder in clausification.Remainder)
        {
            Assert.IsFalse(remainder.StartsWith("ReservedDataProperty(", StringComparison.Ordinal), "The reserved-property exact-of-one rejects through the generic datatype-fragment name, not the distinct ReservedDataProperty name.");
            if(remainder.EndsWith("outside the context datatype fragment.", StringComparison.Ordinal))
            {
                hasGenericRejection = true;
            }
        }

        Assert.IsTrue(hasGenericRejection, "The reserved-property exact-of-one falls to the generic DataExpressionRejection.");
        ModuleDecision decision = await Awaited(ContextArm(module, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The reserved-property module delegates on the remainder, so the seam abstains.");
        Assert.IsNull(decision.Verdict, "A delegated module carries no verdict.");
    }

    /// <summary>F3.3 E9 (spec hazard H9, the qualified slot never merge-pools): a QUALIFIED data max-cardinality of one over <c>integer[1,10]</c> beside two distinct told values OUTSIDE that range is CONSISTENT — no forced value is range-typed, so the bound counts nothing — and the arm must not decide inconsistent. The slot's certificate cannot place two points under a bound of one, so it abstains and the module delegates: a functional-style merge here would falsely close the node.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataQcrQualifiedMaxOneOutOfRangePointsKeepsDelegating()
    {
        NamedNode property = ContextDataProperty("qcr9p");
        OwlClassReference host = ContextClass("Qcr9C");
        ReasoningModule premise = new(
        [
            new OwlSubClassOfAxiom(host, new OwlDataCardinality(OwlCardinalityKind.Max, 1, property, BoundedIntegerRange)) { Origin = ContextOrigin("qcr9max") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, IntegerLiteral("50"))) { Origin = ContextOrigin("qcr9fifty") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(property, IntegerLiteral("60"))) { Origin = ContextOrigin("qcr9sixty") },
            new OwlClassAssertionAxiom(host, ContextIndividual("qcr9i")) { Origin = ContextOrigin("qcr9assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreNotEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The qualified slot claims no verdict on out-of-range points, so the arm never decides this consistent module inconsistent.");
        Assert.IsNull(decision.Verdict, "A delegated module carries no verdict.");
    }

    /// <summary>F3.4 W1 (pooling-sweep parity on the XMLLiteral oracle): a functional <c>p</c> against two XML literals of differing canonical form reached through a SUB-property <c>q</c> clashes only when the functional pool sweeps the sub-or-self closure — a missed sub-property demand would leave the slot certified while a hidden second value forces it.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task XmlLiteralFunctionalSubPropertyPoolDecidesInconsistent()
    {
        NamedNode super = ContextDataProperty("xmllit1p");
        NamedNode sub = ContextDataProperty("xmllit1q");
        OwlClassReference host = ContextClass("XmlLit1C");
        ReasoningModule premise = new(
        [
            new OwlSubDataPropertyOfAxiom(sub, super) { Origin = ContextOrigin("xmllit1hierarchy") },
            new OwlFunctionalDataPropertyAxiom(super) { Origin = ContextOrigin("xmllit1functional") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(sub, XmlLiteralValue("<span><b>Good!</b></span>"))) { Origin = ContextOrigin("xmllit1good") },
            new OwlSubClassOfAxiom(host, new OwlDataHasValue(sub, XmlLiteralValue("<span><b>Bad!</b></span>"))) { Origin = ContextOrigin("xmllit1bad") },
            new OwlClassAssertionAxiom(host, ContextIndividual("xmllit1i")) { Origin = ContextOrigin("xmllit1assert") },
        ], Violations: []);

        ModuleDecision decision = await Awaited(ContextArm(premise, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The sub-property hierarchy and the functional characteristic both admit, so the module decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The sub-property's two values are both super-property values, and their canonical forms differ, so one functional slot cannot hold them — inconsistent.");
    }

    /// <summary>F3.5 R1: a ground data-property assertion on a NAMED source encodes as the data universal over the complement of the told value's singleton enumeration — the concept a told negative data-property assertion lowers to, and the exact denial of the fact.</summary>
    [TestMethod]
    public void GroundDataAssertionRefutationIsDataUniversalComplement()
    {
        NamedNode property = ContextDataProperty("gda1p");
        NamedNode subject = ContextIndividual("gda1a");
        Literal value = StringLiteral("alpha");
        OwlDataPropertyAssertionAxiom fact = new(subject, property, value) { Origin = ContextOrigin("gda1fact") };

        if(ContextRefutations(fact) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("R1: a named-source ground data assertion must have a refutation encoding.");

            return;
        }

        Assert.HasCount(1, checks, "R1: the ground-data-assertion arm yields exactly one refutation probe.");
        Assert.HasCount(1, checks[0].Axioms, "R1: the ground-data-assertion probe carries exactly one axiom.");
        if(checks[0].Axioms[0] is not OwlClassAssertionAxiom { Individual: NamedNode armSubject, Class: OwlDataAllValuesFrom universal }
            || universal.Properties.Count != 1
            || universal.Range is not OwlDataComplementOf { Range: OwlDataOneOf enumeration }
            || enumeration.Literals.Count != 1)
        {
            Assert.Fail("R1: the arm must assert the data universal over the complement of the told value's singleton enumeration.");

            return;
        }

        Assert.AreEqual(subject, armSubject, "R1: the check is asserted on the fact's named source.");
        Assert.AreEqual(property, universal.Properties[0], "R1: the universal ranges over the fact's data property.");
        Assert.AreEqual(value, enumeration.Literals[0], "R1: the excluded value is the fact's told literal.");
    }

    /// <summary>
    /// F3.5 R2 (the polarity guard): a ground data assertion the premise does NOT
    /// force stays refutable — its check is cleanly satisfiable alongside a told
    /// fact carrying a different value, witnessing the genuine non-entailment —
    /// while the told fact's own check closes. An over-refuting encoding would
    /// report the second value as entailed and fail here.
    /// </summary>
    [TestMethod]
    public void GroundDataAssertionNonConclusionFindsModel()
    {
        NamedNode property = ContextDataProperty("gda2p");
        NamedNode subject = ContextIndividual("gda2a");
        ReasoningModule premise = new(
        [
            new OwlDataPropertyAssertionAxiom(subject, property, IntegerLiteral("1")) { Origin = ContextOrigin("gda2told") },
        ], Violations: []);

        OwlDataPropertyAssertionAxiom other = new(subject, property, IntegerLiteral("2")) { Origin = ContextOrigin("gda2other") };
        if(Refutations(other) is not List<OwlClassAssertionAxiom> otherChecks)
        {
            Assert.Fail("R2: the non-conclusion ground data assertion must have a refutation encoding.");

            return;
        }

        Assert.HasCount(1, otherChecks, "R2: the arm yields exactly one refutation axiom.");
        ModuleVerdict otherVerdict = AlcModuleReasoner.DecideConsistency(Extend(premise, otherChecks[0]), TestContext.CancellationToken);
        Assert.IsEmpty(otherVerdict.UnsupportedConstructs, "R2: the check is decided whole, so the verdict is not fragment-relative.");
        Assert.IsTrue(otherVerdict.IsConsistent, "R2: nothing forces a second value, so denying it is satisfiable and the non-conclusion does not follow.");

        OwlDataPropertyAssertionAxiom told = new(subject, property, IntegerLiteral("1")) { Origin = ContextOrigin("gda2same") };
        if(Refutations(told) is not List<OwlClassAssertionAxiom> toldChecks)
        {
            Assert.Fail("R2: the told ground data assertion must have a refutation encoding.");

            return;
        }

        ModuleVerdict toldVerdict = AlcModuleReasoner.DecideConsistency(Extend(premise, toldChecks[0]), TestContext.CancellationToken);
        Assert.IsFalse(toldVerdict.IsConsistent, "R2: denying the told value contradicts the premise, so the told fact is entailed — the encoding bites.");
    }

    /// <summary>F3.5 R3: a conclusion data minimum cardinality of three refutes as the POSITIVE data maximum cardinality of two over the SAME qualifying range — dropping the range would clash nodes whose surplus values lie outside it.</summary>
    [TestMethod]
    public void DataMinCardinalityRefutationIsPositiveMaxCardinality()
    {
        NamedNode property = ContextDataProperty("gda3p");
        NamedNode subject = ContextIndividual("gda3a");
        OwlClassAssertionAxiom conclusion = new(new OwlDataCardinality(OwlCardinalityKind.Min, 3, property, BoundedIntegerRange), subject) { Origin = ContextOrigin("gda3min") };

        if(Refutations(conclusion) is not List<OwlClassAssertionAxiom> checks)
        {
            Assert.Fail("R3: a named-individual data min-cardinality assertion must have a refutation encoding.");

            return;
        }

        Assert.HasCount(1, checks, "R3: the De Morgan branch yields exactly one refutation axiom.");
        if(checks[0].Class is not OwlDataCardinality { Kind: OwlCardinalityKind.Max } dual)
        {
            Assert.Fail("R3: the refutation must be a positive data maximum cardinality, never a complement wrapper.");

            return;
        }

        Assert.AreEqual(subject, checks[0].Individual, "R3: the dual is asserted on the conclusion's individual.");
        Assert.AreEqual(2, dual.Cardinality, "R3: the complement of a minimum of three is a maximum of two.");
        Assert.AreEqual(property, dual.Property, "R3: the dual counts the conclusion's data property.");
        Assert.AreEqual(BoundedIntegerRange, dual.Range, "R3: the qualifying range passes through unchanged.");
    }

    /// <summary>F3.5 R4 (the minimum floor): a conclusion data minimum cardinality of ONE keeps the generic complement route — its dual would be a maximum of zero, a shape the branch deliberately declines — so the produced check is the object complement, byte-identical to the pre-branch encoding.</summary>
    [TestMethod]
    public void DataMinCardinalityOneRefutationKeepsGenericComplement()
    {
        NamedNode property = ContextDataProperty("gda4p");
        NamedNode subject = ContextIndividual("gda4a");
        OwlDataCardinality restriction = new(OwlCardinalityKind.Min, 1, property, Range: null);
        OwlClassAssertionAxiom conclusion = new(restriction, subject) { Origin = ContextOrigin("gda4min") };

        if(Refutations(conclusion) is not List<OwlClassAssertionAxiom> checks)
        {
            Assert.Fail("R4: a named-individual class assertion always has the generic refutation encoding.");

            return;
        }

        Assert.HasCount(1, checks, "R4: the generic class-assertion arm yields exactly one refutation axiom.");
        if(checks[0].Class is not OwlObjectComplementOf complement)
        {
            Assert.Fail("R4: a minimum of one must keep the generic complement route, not take the De Morgan branch.");

            return;
        }

        Assert.AreEqual(restriction, complement.Operand, "R4: the generic route wraps the conclusion's own restriction.");
    }

    /// <summary>F3.5 R5: the symmetric direction — a conclusion data maximum cardinality of two refutes as the POSITIVE data minimum cardinality of three over the same qualifying range.</summary>
    [TestMethod]
    public void DataMaxCardinalityRefutationIsPositiveMinCardinality()
    {
        NamedNode property = ContextDataProperty("gda5p");
        NamedNode subject = ContextIndividual("gda5a");
        OwlClassAssertionAxiom conclusion = new(new OwlDataCardinality(OwlCardinalityKind.Max, 2, property, BoundedIntegerRange), subject) { Origin = ContextOrigin("gda5max") };

        if(Refutations(conclusion) is not List<OwlClassAssertionAxiom> checks)
        {
            Assert.Fail("R5: a named-individual data max-cardinality assertion must have a refutation encoding.");

            return;
        }

        Assert.HasCount(1, checks, "R5: the De Morgan branch yields exactly one refutation axiom.");
        if(checks[0].Class is not OwlDataCardinality { Kind: OwlCardinalityKind.Min } dual)
        {
            Assert.Fail("R5: the refutation must be a positive data minimum cardinality, never a complement wrapper.");

            return;
        }

        Assert.AreEqual(3, dual.Cardinality, "R5: the complement of a maximum of two is a minimum of three.");
        Assert.AreEqual(property, dual.Property, "R5: the dual counts the conclusion's data property.");
        Assert.AreEqual(BoundedIntegerRange, dual.Range, "R5: the qualifying range passes through unchanged.");
    }

    /// <summary>F3.5 R6: a ground data assertion on an ANONYMOUS source has no per-axiom refutation — its existential reading is over the edge's subject, not a named root — so both the shared switch and the context walk decline it.</summary>
    [TestMethod]
    public void GroundDataAssertionAnonymousSourceHasNoEncoding()
    {
        OwlDataPropertyAssertionAxiom fact = new(new BlankNode(Utf8Strings.From("gda6x")), ContextDataProperty("gda6p"), StringLiteral("alpha")) { Origin = ContextOrigin("gda6fact") };

        Assert.IsNull(Refutations(fact), "R6: the shared switch declines an anonymous-source data assertion.");
        Assert.IsNull(ContextRefutations(fact), "R6: the context walk adds no arm for an anonymous-source data assertion either.");
    }

    /// <summary>F3.5 R7 (the maximum floor, R4's mirror): a conclusion data maximum cardinality of ZERO keeps the generic complement route — the range-less shape the context survey already admits and decides at either polarity is never re-routed through a synthesized minimum of one.</summary>
    [TestMethod]
    public void DataMaxCardinalityZeroRefutationKeepsGenericComplement()
    {
        NamedNode property = ContextDataProperty("gda7p");
        NamedNode subject = ContextIndividual("gda7a");
        OwlDataCardinality restriction = new(OwlCardinalityKind.Max, 0, property, Range: null);
        OwlClassAssertionAxiom conclusion = new(restriction, subject) { Origin = ContextOrigin("gda7max") };

        if(Refutations(conclusion) is not List<OwlClassAssertionAxiom> checks)
        {
            Assert.Fail("R7: a named-individual class assertion always has the generic refutation encoding.");

            return;
        }

        Assert.HasCount(1, checks, "R7: the generic class-assertion arm yields exactly one refutation axiom.");
        if(checks[0].Class is not OwlObjectComplementOf complement)
        {
            Assert.Fail("R7: a maximum of zero must keep the generic complement route, not take the De Morgan branch.");

            return;
        }

        Assert.AreEqual(restriction, complement.Operand, "R7: the generic route wraps the conclusion's own restriction.");
    }

    /// <summary>F3.5 W1 (the integer-subtype facet module, end to end): an asserted range of <c>xsd:nonNegativeInteger</c> and a <c>xsd:nonPositiveInteger</c> existential leave the property's value footprint at the single point zero, so the ground-data-assertion refutation of a told zero empties it and the arm decides INCONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task GroundDataAssertionUniversalComplementModuleDecidesInconsistent()
    {
        NamedNode property = ContextDataProperty("gdw1p");
        NamedNode subject = ContextIndividual("gdw1i");
        ReasoningModule premise = new(
        [
            new OwlDataPropertyRangeAxiom(property, NonNegativeIntegerRange) { Origin = ContextOrigin("gdw1range") },
            new OwlClassAssertionAxiom(new OwlDataSomeValuesFrom([property], NonPositiveIntegerRange), subject) { Origin = ContextOrigin("gdw1some") },
        ], Violations: []);

        OwlDataPropertyAssertionAxiom conclusion = new(subject, property, IntLiteral("0")) { Origin = ContextOrigin("gdw1fact") };
        if(ContextRefutations(conclusion) is not List<ContextRefutationProbe> checks)
        {
            Assert.Fail("W1: the named-source ground data assertion must have a refutation encoding.");

            return;
        }

        ModuleDecision decision = await Awaited(ContextArm(Extend(premise, checks[0]), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "W1: the range, the existential, and the universal-complement check all admit, so the module decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "W1: the two integer subtypes intersect at zero alone, and the check removes it — inconsistent.");
    }

    /// <summary>F3.5 W2 (the belt witness): a data cardinality naming an ASSERTED data property is a lifted belt position, so the module admits whole instead of taking the beyond-keys whole-module rejection.</summary>
    [TestMethod]
    public void DataCardinalityBesideAssertedPropertyModuleAdmits()
    {
        ClausificationResult clausification = ContextClausifier.Clausify(CountedGroundValuesModule());

        foreach(string remainder in clausification.Remainder)
        {
            Assert.IsFalse(remainder.StartsWith("AssertedDataPropertyBeyondKeys(", StringComparison.Ordinal), "W2: a data cardinality on an asserted property lifts rather than rejecting the module.");
        }

        Assert.IsEmpty(clausification.Remainder, "W2: nothing else in the module leaves a remainder either.");
    }

    /// <summary>F3.5 W3 (the counting clash the lift enables): the lifted ground facts become point demands beside the qualified maximum on the same property, and two provably-distinct in-range values cannot fit a bound of one, so the arm decides INCONSISTENT.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task DataCardinalityBesideAssertedPropertyDecidesInconsistent()
    {
        ModuleDecision decision = await Awaited(ContextArm(CountedGroundValuesModule(), TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "W3: the lifted assertions and the qualified maximum all admit, so the module decides whole.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "W3: two distinct xsd:string values are both in range and the bound admits one — inconsistent.");
    }

    /// <summary>F3.5 W4 (the belt movement guard): the hierarchy-shaped KEPT positions stay KEPT — a sub-property axiom naming an asserted data property still rejects the whole module, so the arm delegates.</summary>
    /// <returns>The asynchronous decision.</returns>
    [TestMethod]
    public async Task SubDataPropertyBesideAssertedPropertyKeepsDelegating()
    {
        NamedNode super = ContextDataProperty("gdw4p");
        NamedNode sub = ContextDataProperty("gdw4q");
        ReasoningModule module = new(
        [
            new OwlSubDataPropertyOfAxiom(sub, super) { Origin = ContextOrigin("gdw4hierarchy") },
            new OwlDataPropertyAssertionAxiom(ContextIndividual("gdw4i"), sub, StringLiteral("alpha")) { Origin = ContextOrigin("gdw4alpha") },
        ], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        bool rejected = false;
        foreach(string remainder in clausification.Remainder)
        {
            rejected |= remainder.StartsWith("AssertedDataPropertyBeyondKeys(", StringComparison.Ordinal);
        }

        Assert.IsTrue(rejected, "W4: a sub-property axiom on an asserted data property holds the KEPT belt branch.");

        ModuleDecision decision = await Awaited(ContextArm(module, TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "W4: the rejected module delegates, so the seam abstains.");
        Assert.IsNull(decision.Verdict, "W4: a delegated module carries no verdict.");
    }

    /// <summary>The W2/W3 module: two told <c>xsd:string</c> values on one data property beside a qualified maximum of one over that same property — the shape the belt lift admits and the max slot's points-only overflow rule closes.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CountedGroundValuesModule()
    {
        NamedNode property = ContextDataProperty("gdw2p");
        NamedNode subject = ContextIndividual("gdw2i");

        return new ReasoningModule(
        [
            new OwlDataPropertyAssertionAxiom(subject, property, StringLiteral("alpha")) { Origin = ContextOrigin("gdw2alpha") },
            new OwlDataPropertyAssertionAxiom(subject, property, StringLiteral("beta")) { Origin = ContextOrigin("gdw2beta") },
            new OwlClassAssertionAxiom(new OwlDataCardinality(OwlCardinalityKind.Max, 1, property, StringRange), subject) { Origin = ContextOrigin("gdw2max") },
        ], Violations: []);
    }

    /// <summary>
    /// Reconciles the seeded census against the recorded prediction tables:
    /// the disjunctive table, the HasKey ground rung's table, and the nominal
    /// table. The disjunctive-widening movers
    /// are GONE from every census (the covering, union, negative-universal, and
    /// bound-above-one premises decide whole; the negative-polarity cardinality
    /// refutations settle), so are the key rung's movers (the ground key
    /// join decides the told-key premises whole, the counting rider decides the
    /// told pigeonhole and the qualified told-filler refutation, and the
    /// key-scoped data intake decides the bare-assertion premises whole), and so
    /// are the nominal tier's movers (the
    /// enumeration, bnode-class-enumeration, oneOf-on-Thing, and
    /// oneOf-defined-class premises decide whole, and the
    /// DifferentIndividuals-conclusion refutation settles through the
    /// SameIndividual probe). The data-max-cardinality rung's unqualified
    /// exact-of-one case exits every census on both tiers; the two opaque-datatype
    /// oracles (float/double discreteness and rdf:XMLLiteral canonical identity)
    /// take their four ids out of every census on both tiers; the
    /// ground-data-refutation encodings take a further five out on both tiers (the
    /// data universal over a value's complement for a ground data-property-assertion
    /// conclusion, and the positive De Morgan dual for a data-cardinality one);
    /// the
    /// property-characteristic and property-equivalence conclusions leave every
    /// census through the multi-axiom skolemized probes, and the connected
    /// anonymous-existential conclusion through the forest rollup onto its named
    /// root; the set-partition counting template's five premises leave every
    /// census through the closed-form anchor-against-cap comparison, and the
    /// boolean-cardinality-gadget cluster's three premises leave every census
    /// through the bounded walk over their compiled propositional assignments;
    /// the disjunction-breadth pair the anchor-and-pair composition covers
    /// leaves every census through the bounded walk over its assignment vectors,
    /// past the member-universe window the block sweep stops at; and the
    /// spy-point encoding's single premise leaves every census through the
    /// closed-form comparison of its told domain bound — the funnel members'
    /// inverse-linked caps summed — against its told minimum-cardinality demand;
    /// and the bijection-chain cardinality family's four premises leave every
    /// census through the bounded worklist that propagates their told size
    /// variables to a fixpoint, refuting on an impossible size and otherwise
    /// handing the module to whichever of the two whole-module certificate
    /// routes validates it — the all-empty vacuity model or the canonical
    /// grounded-tower fiber model — with the consistency premise's five
    /// refutation probes deciding through the same propagation, so that id
    /// exits the census whole rather than migrating to a refutation gap;
    /// and the told-ground nominal-and-inverse premise leaves every census
    /// through the described model its own told terms spell out — one carrier
    /// per told individual term, the told edge closed under the told inverse
    /// pair, and one least-fixpoint extension per named class — re-checked
    /// axiom by axiom before anything is certified, with its single refutation
    /// probe refuted by the derived membership through the same ground core, so
    /// that id too exits the census whole;
    /// and the restriction-rich ground pair leaves every census through the
    /// repaired described model — the told terms after the told-sameness
    /// quotient, their told edges closed under the closure operator, the edges
    /// the deterministic repair forces, the elements minted only into an open
    /// demand set, and the commitments the bounded choice walk makes over the
    /// closed residue — re-verified axiom by axiom before anything is
    /// certified, neither premise carrying an entailment conclusion, so both
    /// ids exit the census whole;
    /// and the dynamic-blocking premise leaves every census through a bounded
    /// skolem expansion — one fresh successor per unsatisfied existential under
    /// named node, depth, label, edge and step bounds, every edge mirrored under
    /// the told inverse pairs at creation time, and the universal itself pushed
    /// along the told-transitive role rather than any edge relation being closed
    /// — whose level-three fixpoint carries a minimum of one against a maximum of
    /// zero on one declared data property at the anonymous root, so the clash
    /// face refutes the premise pre-engine; the premise carries no entailment
    /// conclusion, so that id exits the census whole as well.
    /// And the branching modal-gadget premise leaves every census through TWO
    /// faces of opposite direction over two syntactically disjoint layers — a
    /// propositional layer of unqualified cardinality gadgets composed by
    /// binary-intersection equivalences over named classes, and a modal layer of
    /// existentials and universals over one characteristic-free role: the
    /// certify face admits the module whole, computes the gadget atoms a second
    /// definition determines rather than enumerating them, pins what the told
    /// types force, tries the minimal modal vector first, mints one successor per
    /// TRUE existential deduped by computed filler signature with a universal
    /// spawning nothing, and re-verifies every admitted axiom against the
    /// finished structure's raw relations, certifying the premise consistent
    /// pre-engine over a tree that is the told frontier alone with its told
    /// universals vacuous; and the monotone clash face composes named
    /// intersections in both directions to a fixpoint, refuting on a derived
    /// membership meeting its own told complement or a bottom, which decides each
    /// of that premise's nine refutation probes pre-engine, so that id exits the
    /// census whole rather than trading its practical-reach entry for a
    /// refutation gap.
    /// Every certified stayer holds its
    /// adjudicated set — the reserved-role class-assertion second-gate exits,
    /// the belt-held key case (its blocker is the
    /// key-scoped data belt, not the key join), and the disjunctive
    /// combinatorial premises that are admitted but beyond the arm's inference
    /// ceiling. A prediction the seeding contradicts fails here rather than
    /// passing silently.
    /// </summary>
    [TestMethod]
    public void ContextCensusReconcilesWithCertifiedStayers()
    {
        string[] widenedMovers =
        [
            "New-Feature-DisjointUnion-001",
            "owl2-rl-invalid-unionof",
            "owl2-rl-invalid-rightside-unionof",
            "owl2-rl-invalid-leftside-allvaluesfrom",
            "owl2-rl-invalid-leftside-maxcard",
            "WebOnt-cardinality-001",
            "WebOnt-cardinality-002",
            "WebOnt-cardinality-003",
            "WebOnt-cardinality-004",
            "WebOnt-description-logic-040",
        ];
        foreach(string identifier in widenedMovers)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Adjudicated mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Adjudicated mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Adjudicated mover {identifier} must have exited ContextRefutationGaps.");
        }

        //The data-cardinality-core flips: thirty WebOnt-description-logic premises
        //whose sole admission blocker was an unqualified data cardinality inside an
        //EquivalentClasses axiom, together with the 665 and 667 positive-entailment
        //cases, admit, lower through the per-property HasValueOf value-existence
        //atom, and decide their sound verdict, so each must have exited every census.
        //A stale pin on a flipped id passes the boundary walk silently, so this named
        //reverse gate holds the census exact.
        string[] dataCardinalityCoreDecided =
        [
            "WebOnt-description-logic-602",
            "WebOnt-description-logic-603",
            "WebOnt-description-logic-604",
            "WebOnt-description-logic-605",
            "WebOnt-description-logic-609",
            "WebOnt-description-logic-610",
            "WebOnt-description-logic-611",
            "WebOnt-description-logic-612",
            "WebOnt-description-logic-613",
            "WebOnt-description-logic-614",
            "WebOnt-description-logic-615",
            "WebOnt-description-logic-616",
            "WebOnt-description-logic-617",
            "WebOnt-description-logic-624",
            "WebOnt-description-logic-625",
            "WebOnt-description-logic-626",
            "WebOnt-description-logic-627",
            "WebOnt-description-logic-628",
            "WebOnt-description-logic-629",
            "WebOnt-description-logic-630",
            "WebOnt-description-logic-631",
            "WebOnt-description-logic-632",
            "WebOnt-description-logic-633",
            "WebOnt-description-logic-634",
            "WebOnt-description-logic-641",
            "WebOnt-description-logic-642",
            "WebOnt-description-logic-643",
            "WebOnt-description-logic-644",
            "WebOnt-description-logic-646",
            "WebOnt-description-logic-650",
            "WebOnt-description-logic-665",
            "WebOnt-description-logic-667",
        ];
        foreach(string identifier in dataCardinalityCoreDecided)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Data-cardinality-core mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Data-cardinality-core mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Data-cardinality-core mover {identifier} must not enter ContextRefutationGaps.");
        }

        //The F3.1 ground-data-assertion movers: the router admits their premises
        //through the lifted belt positions and the arm decides both claims, so
        //each must have exited every census (Keys-007 through the DataHasValue
        //expression lift; DataComplementOf-001 through the range lift, its
        //complement membership decided by the built-in family checker).
        string[] groundDataAssertionMovers =
        [
            "New-Feature-Keys-007",
            "Datatype-DataComplementOf-001",
        ];
        foreach(string identifier in groundDataAssertionMovers)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Ground-data-assertion mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Ground-data-assertion mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Ground-data-assertion mover {identifier} must not enter ContextRefutationGaps.");
        }

        //The F3.1 sound stayers: the disjoint-data-property cases hold the KEPT
        //belt branch.
        string[] groundDataAssertionStayers =
        [
            "Inconsistent Disjoint Dataproperties",
            "Inconsistent String Pattern with Disjoint Dataproperties",
            "consistent-dataproperty-disjointness",
        ];
        foreach(string identifier in groundDataAssertionStayers)
        {
            Assert.Contains(identifier, ContextFragmentGaps, $"Ground-data-assertion stayer {identifier} must stay pinned in ContextFragmentGaps.");
        }

        //The F3.3 data-max-cardinality FULL EXIT: I5.21-002's unqualified data
        //exact-cardinality of one admits on both tiers, its minimum and maximum
        //halves pool into the range-less single slot, and the 66 pairwise
        //disjointness refutations each close on the merged conjunction of the two
        //distinct told family-name values — so the id leaves every census on both
        //tiers (the SAT tier's conclusion rides the DisjointClasses pairwise-overlap
        //arm, which needs no data refutation encoding at all).
        string[] dataQcrFullExit =
        [
            "WebOnt-I5.21-002",
        ];
        foreach(string identifier in dataQcrFullExit)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Data-QCR mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Data-QCR mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Data-QCR mover {identifier} must not enter ContextRefutationGaps.");
            Assert.DoesNotContain(identifier, FragmentGaps, $"Data-QCR mover {identifier} must have exited the tableau tier's FragmentGaps.");
            Assert.DoesNotContain(identifier, RefutationGaps.Keys, $"Data-QCR mover {identifier} must not enter the tableau tier's RefutationGaps.");
        }

        //The F3.4 opaque-datatype-oracle FULL EXIT: the two built-in value-space
        //oracles the shared checker gained — the discrete rank algebra of the
        //xsd:float and xsd:double spaces, and rdf:XMLLiteral value identity by
        //exclusive Canonical XML equality — decide the premise of each of these
        //four ids outright, so each leaves every census on both tiers. The float
        //premise's existential demand ranges over an open interval between two
        //adjacent single-precision values and is empty; the three XMLLiteral
        //premises pool a functional property's two values at the shared sidecar,
        //where canonical identity admits the equal pair and excludes the distinct
        //ones.
        string[] opaqueDatatypeOracleFullExit =
        [
            "Datatype-Float-Discrete-001",
            "WebOnt-miscellaneous-202",
            "WebOnt-miscellaneous-203",
            "WebOnt-miscellaneous-204",
        ];
        foreach(string identifier in opaqueDatatypeOracleFullExit)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Opaque-datatype-oracle mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Opaque-datatype-oracle mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Opaque-datatype-oracle mover {identifier} must not enter ContextRefutationGaps.");
            Assert.DoesNotContain(identifier, FragmentGaps, $"Opaque-datatype-oracle mover {identifier} must have exited the tableau tier's FragmentGaps.");
            Assert.DoesNotContain(identifier, RefutationGaps.Keys, $"Opaque-datatype-oracle mover {identifier} must not enter the tableau tier's RefutationGaps.");
        }

        //The F3.5 ground-data-refutation FULL EXIT: the shared refutation switch
        //encodes both conclusion shapes these five carry. A ground data-property
        //assertion becomes the data universal over the complement of its value —
        //the told-negative-assertion concept, which both fragment gates admit — and
        //a data minimum cardinality becomes its De Morgan dual, a positive maximum
        //of one less over the same qualifying range. The four facet, enumeration,
        //and counting clashes fall out of the shared value machinery; the
        //data-counting case rides the max slot's points-only overflow rule beside
        //the belt's lifted data-cardinality position. Each id therefore leaves
        //every census on both tiers.
        string[] groundDataRefutationFullExit =
        [
            "WebOnt-I5.8-010",
            "WebOnt-oneOf-004",
            "Qualified-cardinality-boolean",
            "Qualified-cardinality-restricted-int",
            "New-Feature-DataQCR-001",
        ];
        foreach(string identifier in groundDataRefutationFullExit)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Ground-data-refutation mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Ground-data-refutation mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Ground-data-refutation mover {identifier} must not enter ContextRefutationGaps.");
            Assert.DoesNotContain(identifier, FragmentGaps, $"Ground-data-refutation mover {identifier} must have exited the tableau tier's FragmentGaps.");
            Assert.DoesNotContain(identifier, RefutationGaps.Keys, $"Ground-data-refutation mover {identifier} must not enter the tableau tier's RefutationGaps.");
        }

        string[] keyRungMovers =
        [
            "New-Feature-Keys-001",
            "New-Feature-Keys-002",
            "WebOnt-maxCardinality-001",
            "New-Feature-ObjectQCR-001",
            "WebOnt-I5.3-008",
        ];
        foreach(string identifier in keyRungMovers)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Key-rung mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Key-rung mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Key-rung mover {identifier} must have exited ContextRefutationGaps.");
        }

        string[] nominalMovers =
        [
            "WebOnt-oneOf-001",
            "WebOnt-unionOf-003",
            "WebOnt-unionOf-004",
            "WebOnt-Thing-004",
            "WebOnt-equivalentClass-009",
            "WebOnt-I4.5-002",
            "owl2-rl-invalid-oneof",
            "WebOnt-disjointWith-001",
        ];
        foreach(string identifier in nominalMovers)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Nominal-tier mover {identifier} must have exited ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Nominal-tier mover {identifier} must not resurface as a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Nominal-tier mover {identifier} must have exited ContextRefutationGaps.");
        }

        //The skolemized-refutation flips: each of these conclusions carries a role
        //axiom whose negation needs fresh witnesses, and the multi-axiom probes
        //state it — a transitivity as a two-step path beside its shortcut's
        //denial, and a property equivalence as one probe per inclusion — so their
        //premises' symmetric / functional machinery closes every probe and each
        //id must have left every census.
        string[] skolemizedRefutationFlips =
        [
            "WebOnt-TransitiveProperty-002",
            "WebOnt-equivalentProperty-004",
        ];
        foreach(string identifier in skolemizedRefutationFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Skolemized-refutation flip {identifier} must have left ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Skolemized-refutation flip {identifier} is not a practical-reach gap.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Skolemized-refutation flip {identifier} must have exited ContextRefutationGaps.");
        }

        //The anonymous-forest rollup flip: the conclusion's connected existential
        //chain rolls up onto its named root as one nested existential, whose
        //complement the recursive premise closes, so the id leaves every context
        //census.
        string[] anonymousForestFlips =
        [
            "WebOnt-someValuesFrom-003",
        ];
        foreach(string identifier in anonymousForestFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Anonymous-forest flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Anonymous-forest flip {identifier} must not enter ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Anonymous-forest flip {identifier} must have exited ContextRefutationGaps.");
        }

        //The context-arm refutation flips: the arm now encodes and decides each of
        //these conclusions, so the id must have left ContextRefutationGaps. A stale
        //pin on a flipped id passes the walk silently, so this named reverse gate
        //holds the census exact.
        string[] refutationFlips =
        [
            "New-Feature-SelfRestriction-001",
            "somevaluesfrom2bnode",
            "WebOnt-AnnotationProperty-002",
            "WebOnt-allValuesFrom-002",
            "WebOnt-equivalentProperty-001",
        ];
        foreach(string identifier in refutationFlips)
        {
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Refutation flip {identifier} must have exited ContextRefutationGaps.");
        }

        //The reserved-vocabulary fold flips: the constant-fold front door turns
        //each premise's reserved-property restriction into owl:Thing / owl:Nothing,
        //so the folded ClassAssertion(owl:Nothing, i) lowers to an immediate engine
        //clash and the arm decides INCONSISTENT whole, so the id must have left
        //ContextFragmentGaps. A stale pin on a flipped id passes the walk silently,
        //so this named reverse gate holds the census exact.
        string[] fragmentFlips =
        [
            "New-Feature-BottomDataProperty-001",
            "New-Feature-BottomObjectProperty-001",
            "New-Feature-TopObjectProperty-001",
        ];
        foreach(string identifier in fragmentFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Reserved-vocabulary fold flip {identifier} must have exited ContextFragmentGaps.");
        }

        //The partition-counting flips: each of these five premises is the
        //set-partition counting template — a named class equivalent to an
        //intersection of existential restrictions and one unqualified maximum
        //cardinality over one role, over a pairwise-disjoint class clique — so
        //the closed-form anchor-against-cap comparison decides it pre-engine and
        //the id leaves every context census. A stale pin on a flipped id passes
        //the boundary walk silently, so this named reverse gate holds the census
        //exact.
        string[] partitionCountingFlips =
        [
            "WebOnt-description-logic-018",
            "WebOnt-description-logic-019",
            "WebOnt-description-logic-020",
            "WebOnt-description-logic-021",
            "WebOnt-description-logic-022",
        ];
        foreach(string identifier in partitionCountingFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Partition-counting flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Partition-counting flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Partition-counting flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The boolean-cardinality-gadget flips: each of these three premises
        //compiles to a propositional theory over one atom per bare 0/1
        //cardinality gadget property and one per free class — the two modal
        //ones carrying a fixed prelude whose whole effect at the typed
        //individual is an at-most-one merge — so the bounded assignment walk
        //decides it pre-engine and the id leaves every context census. A stale
        //pin on a flipped id passes the boundary walk silently, so this named
        //reverse gate holds the census exact.
        string[] booleanGadgetFlips =
        [
            "WebOnt-description-logic-601",
            "WebOnt-description-logic-606",
            "WebOnt-description-logic-608",
        ];
        foreach(string identifier in booleanGadgetFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Boolean-gadget flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Boolean-gadget flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Boolean-gadget flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The enumeration pair-composition flips: each of these two premises
        //equates one named class to a told-distinct anchor pair and to one
        //two-member one-of per further pair, so every model's named universe
        //collapses onto the anchor's two elements and the bounded walk over the
        //composition's assignment vectors decides it pre-engine — past the
        //member-universe window the block sweep cannot reach. A stale pin on a
        //flipped id passes the boundary walk silently, so this named reverse gate
        //holds the census exact.
        string[] enumerationPairFlips =
        [
            "WebOnt-description-logic-501",
            "WebOnt-description-logic-502",
        ];
        foreach(string identifier in enumerationPairFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Enumeration pair-composition flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Enumeration pair-composition flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Enumeration pair-composition flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The spy-point domain-bound flip: this premise funnels the WHOLE domain
        //along one role into a singleton enumeration whose member carries a told
        //max-cardinality cap on the role's told inverse, so the domain is bounded
        //by that one cap, while a told minimum-cardinality demand at an asserted
        //individual asks for more elements than the bound admits. The closed-form
        //comparison decides it pre-engine, with no unique-name assumption and no
        //saturation attempt. A stale pin on a flipped id passes the boundary walk
        //silently, so this named reverse gate holds the census exact.
        string[] spyPointFlips =
        [
            "WebOnt-description-logic-035",
        ];
        foreach(string identifier in spyPointFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Spy-point flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Spy-point flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Spy-point flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The bijection-chain cardinality flips: each of these four premises gives
        //its named classes a size variable the told axioms constrain — a
        //distinctness-covered enumeration or an anchored fan-in grounds a
        //constant, functional and inverse-functional role pairs over opposed
        //existential restrictions make classes equinumerous, and a told disjoint
        //union adds — so the bounded worklist propagates them to a fixpoint and
        //either refutes on an impossible size or hands the module to one of the
        //two whole-module certificate routes: the all-empty vacuity model, or the
        //canonical grounded-tower fiber model whose level constants multiply out.
        //The consistency premise carrying entailment probes leaves every census
        //whole, its five refutation probes deciding through the same propagation.
        //A stale pin on a flipped id passes the boundary walk silently, so this
        //named reverse gate holds the census exact.
        string[] bijectionChainFlips =
        [
            "Consistent-but-all-unsat",
            "WebOnt-description-logic-905",
            "WebOnt-description-logic-908",
            "one=two",
        ];
        foreach(string identifier in bijectionChainFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Bijection-chain flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Bijection-chain flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Bijection-chain flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The told-ground-witness flip: this premise names every individual it
        //reasons about, so its own told terms spell out a finite structure —
        //one carrier per term, the told edge closed under the told inverse pair,
        //and one least-fixpoint extension per named class — that satisfies every
        //axiom on re-check, and the certificate decides the premise consistent
        //pre-engine. The entailment arm's single refutation probe denies a
        //membership the same ground core derives from the mirrored edge, so the
        //probe refutes and the id leaves every census whole rather than trading
        //its practical-reach entry for a refutation gap. A stale pin on a
        //flipped id passes the boundary walk silently, so this named reverse
        //gate holds the census exact.
        string[] toldGroundWitnessFlips =
        [
            "WebOnt-I4.5-001",
        ];
        foreach(string identifier in toldGroundWitnessFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Told-ground-witness flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Told-ground-witness flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Told-ground-witness flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The repairing-certify flips: each of these premises carries a large told
        //ground ABox under a TBox whose obligations are held by value pins,
        //universals and cardinality restrictions the told ground does not
        //satisfy, so no told-only construction exhibits a model — the witnessing
        //edges were never told. The repairing construction proposes one instead:
        //the told closure operator re-applied at every commit, deterministic
        //forced-value repair to a fixpoint, bounded witness supply that mints
        //only into an open demand set, and a bounded choice walk over the closed
        //residue; the full re-verification pass then decides, so the certify face
        //certifies the premise consistent pre-engine over the repaired described
        //model. Neither premise carries an entailment conclusion, so each leaves
        //every census whole rather than trading its practical-reach entry for a
        //refutation gap. A stale pin on a flipped id passes the boundary walk
        //silently, so this named reverse gate holds the census exact.
        string[] repairingCertifyFlips =
        [
            "WebOnt-miscellaneous-001",
            "WebOnt-miscellaneous-002",
        ];
        foreach(string identifier in repairingCertifyFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Repairing-certify flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Repairing-certify flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Repairing-certify flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The modal role-expansion flip: this premise's contradiction is reachable
        //only by CREATING existential witnesses and propagating a fact back UP
        //through told inverse roles into the node where a numeric bound lives, so
        //no told-only face reaches it and the saturation arm walks the infinite
        //chain a transitive role builds beside an existential on that same role.
        //The bounded skolem expansion sidesteps that chain whole: it spawns one
        //fresh successor per unsatisfied existential under named bounds, mirrors
        //every edge under the told inverse pairs, pushes the universal itself
        //along the told-transitive role rather than closing any edge relation, and
        //reaches a minimum of one against a maximum of zero on one declared data
        //property at the anonymous root three levels up, so the clash face decides
        //the premise inconsistent pre-engine. The transitivity axiom the case is
        //named for plays no part in the contradiction. The premise carries no
        //entailment conclusion, so the id leaves every census whole rather than
        //trading its practical-reach entry for a refutation gap. A stale pin on a
        //flipped id passes the boundary walk silently, so this named reverse gate
        //holds the census exact.
        string[] modalRoleExpansionFlips =
        [
            "WebOnt-description-logic-623",
        ];
        foreach(string identifier in modalRoleExpansionFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Modal-role-expansion flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Modal-role-expansion flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Modal-role-expansion flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The modal-gadget flip: this premise carries a shared-role diamond and box
        //core beside its entailment obligations, and the two layers it is built
        //from are syntactically disjoint — a propositional layer of unqualified
        //cardinality gadgets composed by binary-intersection equivalences over
        //named classes, and a modal layer of existentials and universals over ONE
        //characteristic-free role — under a told ABox carrying class assertions
        //and no property assertion at all. What defeats the saturation arm is
        //breadth: every minimum-of-one gadget is an existential generator, and a
        //large bidirectional equivalence set over a large class name set makes the
        //queue grow monotonically and never collapse. The two modal-gadget faces
        //sidestep that breadth from opposite directions. The certify face admits
        //the module whole, eliminates the gadget atoms a second definition
        //determines, pins what the told types force, walks the minimal modal
        //vector first, mints one successor per TRUE existential deduped by
        //computed filler signature — a universal spawning nothing and only pushing
        //its filler onto children the existentials already create — and then
        //re-evaluates every admitted axiom against the finished structure's RAW
        //relations, so the premise is certified consistent pre-engine over a tree
        //that is the told frontier alone, its told universals vacuous because
        //there is no edge for them to range over. The clash face composes named
        //intersections in both directions to a fixpoint over told and derived
        //membership and refutes on a derived membership meeting its own told
        //complement or a bottom, which decides each of the premise's nine
        //refutation probes pre-engine. The id therefore leaves every census whole
        //rather than trading its practical-reach entry for a refutation gap. A
        //stale pin on a flipped id passes the boundary walk silently, so this
        //named reverse gate holds the census exact.
        string[] modalGadgetTreeFlips =
        [
            "WebOnt-description-logic-661",
        ];
        foreach(string identifier in modalGadgetTreeFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Modal-gadget flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Modal-gadget flip {identifier} must have exited ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Modal-gadget flip {identifier} must not enter ContextRefutationGaps.");
        }

        //The nominal-pinned-role flip: the symmetry probe's module is the
        //diagonal-pinned shape — told inverse-functionality with a told
        //self-loop at every member of the role's one-hop-resolved nominal range
        //pins the extension into the identity diagonal, so the probe's fresh
        //edge collapses and the told denial of its reverse is contradicted —
        //which the twelfth family's clash face decides pre-engine, keeping the
        //id out of every census while its premise stays engine-decided. A stale
        //pin on a flipped id passes the walk silently, so this named reverse
        //gate holds the census exact.
        string[] nominalPinnedRoleFlips =
        [
            "WebOnt-SymmetricProperty-002",
        ];
        foreach(string identifier in nominalPinnedRoleFlips)
        {
            Assert.DoesNotContain(identifier, ContextFragmentGaps, $"Nominal-pinned-role flip {identifier} must not enter ContextFragmentGaps.");
            Assert.DoesNotContain(identifier, ContextPracticalReachGaps, $"Nominal-pinned-role flip {identifier} must not enter ContextPracticalReachGaps.");
            Assert.DoesNotContain(identifier, ContextRefutationGaps.Keys, $"Nominal-pinned-role flip {identifier} must have exited ContextRefutationGaps.");
        }

        string[] practicalReachStayers =
        [
            "WebOnt-description-logic-201",
            "WebOnt-description-logic-208",
            "WebOnt-description-logic-209",
        ];
        foreach(string identifier in practicalReachStayers)
        {
            Assert.Contains(identifier, ContextPracticalReachGaps, $"Certified stayer {identifier} must seed into ContextPracticalReachGaps.");
        }
    }

    /// <summary>
    /// WebOnt-miscellaneous-001 and -002 (the Wine/Food premise pair) clausify
    /// WHOLE under the F3.1 router: each premise's <c>yearValue</c> datatype
    /// property carries domain and range TBox axioms — LIFTED co-occurrence
    /// positions — so the key-data belt admits the module, the premise's named
    /// <c>owl:oneOf</c> takes nominal jurisdiction, and the told assertion
    /// lowers on the root tier as a value-forcing demand. This pins the pair's
    /// clausification-level attribution so a router or nominal-guard change
    /// cannot silently reclassify the two manifest rows.
    /// </summary>
    [TestMethod]
    public void MiscellaneousWineFoodClausifyWholeUnderTheLiftedBelt()
    {
        ImmutableArray<Owl2TestCase> approved = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        string[] identifiers = ["WebOnt-miscellaneous-001", "WebOnt-miscellaneous-002"];
        foreach(string identifier in identifiers)
        {
            if(FindContextCase(approved, identifier) is not Owl2TestCase testCase)
            {
                Assert.Fail($"{identifier}: the approved corpus does not declare this test case.");

                return;
            }

            if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
            {
                Assert.Fail($"{identifier}: the premise document did not load.");

                return;
            }

            premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
            OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
            Assert.IsFalse(premise.Diagnostics.HasErrors, $"{identifier}: the premise did not map to structural form.");

            ReasoningModule module = new(premise.Axioms, Violations: []);
            ClausificationResult result = ContextClausifier.Clausify(module);

            Assert.IsEmpty(result.Remainder, $"{identifier}: the lifted domain/range co-occurrence admits the module whole.");
            Assert.IsTrue(result.NominalJurisdiction, $"{identifier}: the named oneOf takes jurisdiction once the router admits the module.");
            Assert.IsNotEmpty(result.RootFacts, $"{identifier}: the told yearValue assertion lowers on the root tier.");
            Assert.IsNotEmpty(result.DataDemandDescriptors, $"{identifier}: the lowering mints the value-forcing demand.");
        }
    }

    /// <summary>Finds the manifest case with the given identifier, or <see langword="null"/> when the corpus does not declare it.</summary>
    /// <param name="cases">The loaded manifest cases.</param>
    /// <param name="identifier">The test identifier to find.</param>
    /// <returns>The matching case, or <see langword="null"/>.</returns>
    private static Owl2TestCase? FindContextCase(ImmutableArray<Owl2TestCase> cases, string identifier)
    {
        foreach(Owl2TestCase testCase in cases)
        {
            if(string.Equals(testCase.Identifier, identifier, StringComparison.Ordinal))
            {
                return testCase;
            }
        }

        return null;
    }

    /// <summary>The fresh witness individual the fourth-arm refutation concepts are asserted on.</summary>
    private static NamedNode ContextWitness { get; } = new(Utf8Strings.From(ContextExample + "witness"));

    /// <summary>The first fresh skolem witness of a multi-axiom role-axiom refutation: the source of the probe's told edge, a rigid designator the reserved authority keeps clear of every corpus term.</summary>
    private static NamedNode ContextSkolemU { get; } = new(Utf8Strings.From(ContextExample + "skolemU"));

    /// <summary>The second fresh skolem witness: the target of the probe's told edge, and the middle node of the transitivity path.</summary>
    private static NamedNode ContextSkolemV { get; } = new(Utf8Strings.From(ContextExample + "skolemV"));

    /// <summary>The third fresh skolem witness: the far end of the transitivity path, whose shortcut from the first witness the probe denies.</summary>
    private static NamedNode ContextSkolemW { get; } = new(Utf8Strings.From(ContextExample + "skolemW"));

    /// <summary>A distinct origin quad for a fourth-arm boundary-row marker.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad ContextOrigin(string marker)
    {
        NamedNode node = new(Utf8Strings.From(ContextExample + marker));

        return new Quad(node, node, node, Graph: null);
    }

    /// <summary>A named class reference in the fourth-arm boundary namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference ContextClass(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(ContextExample + local)));
    }

    /// <summary>A named object-property reference in the fourth-arm boundary namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property reference.</returns>
    private static OwlObjectPropertyReference ContextRole(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(ContextExample + local)));
    }

    /// <summary>A named individual in the fourth-arm boundary namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode ContextIndividual(string local)
    {
        return new NamedNode(Utf8Strings.From(ContextExample + local));
    }

    /// <summary>A named data-property node in the fourth-arm boundary namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The data-property node.</returns>
    private static NamedNode ContextDataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(ContextExample + local));
    }

    /// <summary>The <c>xsd:boolean</c> data range — the two-element value space the counting rows size exactly.</summary>
    private static OwlDataRange BooleanRange { get; } = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#boolean")));

    /// <summary>The <c>xsd:integer</c> range faceted to the closed interval one through ten — a qualifying range whose ten-value footprint the checker sizes exactly.</summary>
    private static OwlDataRange BoundedIntegerRange { get; } = new OwlDatatypeRestriction(
        new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")),
        [
            new OwlFacetRestriction(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#minInclusive")), IntegerLiteral("1")),
            new OwlFacetRestriction(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#maxInclusive")), IntegerLiteral("10")),
        ]);

    /// <summary>The <c>xsd:string</c> data range — the read-time type of a plain corpus literal, and the qualifying range of the ground-value counting rows.</summary>
    private static OwlDataRange StringRange { get; } = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));

    /// <summary>The <c>xsd:nonNegativeInteger</c> data range — the integer subtype bounded below by zero.</summary>
    private static OwlDataRange NonNegativeIntegerRange { get; } = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#nonNegativeInteger")));

    /// <summary>The <c>xsd:nonPositiveInteger</c> data range — the integer subtype bounded above by zero, whose intersection with its non-negative sibling is the single point zero.</summary>
    private static OwlDataRange NonPositiveIntegerRange { get; } = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#nonPositiveInteger")));

    /// <summary>An <c>xsd:int</c> literal — a derived integer type whose values the checker identifies with their <c>xsd:integer</c> counterparts by value, never by datatype IRI.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The typed literal.</returns>
    private static Literal IntLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#int")));
    }

    /// <summary>An <c>xsd:string</c> literal — the read-time type of a plain corpus literal, whose differing lexicals the checker reads as distinct values.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The typed literal.</returns>
    private static Literal StringLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));
    }

    /// <summary>An <c>xsd:integer</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The typed literal.</returns>
    private static Literal IntegerLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
    }

    /// <summary>An <c>rdf:XMLLiteral</c> literal, whose value identity is exclusive Canonical XML equality of its fragment.</summary>
    /// <param name="lexical">The XML fragment lexical form.</param>
    /// <returns>The typed literal.</returns>
    private static Literal XmlLiteralValue(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#XMLLiteral")));
    }

    /// <summary>Extends a module with one further axiom, leaving the original untouched.</summary>
    /// <param name="module">The base module.</param>
    /// <param name="axiom">The axiom to append.</param>
    /// <returns>The extended module.</returns>
    private static ReasoningModule Extend(ReasoningModule module, OwlAxiom axiom)
    {
        List<OwlAxiom> axioms = new(module.Axioms.Count + 1);
        axioms.AddRange(module.Axioms);
        axioms.Add(axiom);

        return new ReasoningModule(axioms, Violations: []);
    }

    /// <summary>Extends a module with every axiom of one refutation probe, leaving the original untouched.</summary>
    /// <param name="module">The base module.</param>
    /// <param name="probe">The probe whose axioms are appended.</param>
    /// <returns>The extended module.</returns>
    private static ReasoningModule Extend(ReasoningModule module, ContextRefutationProbe probe)
    {
        List<OwlAxiom> axioms = new(module.Axioms.Count + probe.Axioms.Count);
        axioms.AddRange(module.Axioms);
        axioms.AddRange(probe.Axioms);

        return new ReasoningModule(axioms, Violations: []);
    }

    /// <summary>Awaits a synchronously-completed seam decision without a sync-over-async block.</summary>
    /// <param name="decision">The seam's decision task.</param>
    /// <returns>The awaited decision.</returns>
    private static async Task<ModuleDecision> Awaited(ValueTask<ModuleDecision> decision)
    {
        return await decision.ConfigureAwait(false);
    }

    /// <summary>
    /// The defined-atom-elimination head-to-head over the shipped Shape G corpus
    /// ids and the gadget battery's decided fixtures. On every module BOTH
    /// constructions must decide, with the identical verdict; the atoms
    /// surviving elimination may never exceed the raw total; the suppressing
    /// construction's surviving count must equal the raw total exactly; and the
    /// production walk may never evaluate more assignments than the raw one —
    /// on a refutation because the free space is a subspace, and on a
    /// certification because packing the surviving bits never increases an
    /// assignment's enumeration index, so the first passing index only moves
    /// down. The wall-clock comparison rides the test log as a measurement
    /// beside the asserted invariants, never as an assert of its own.
    /// </summary>
    [TestMethod]
    public void BooleanGadgetEliminationHeadToHeadHoldsOverCorpusAndBattery()
    {
        string[] corpusIdentifiers =
        [
            "WebOnt-description-logic-601",
            "WebOnt-description-logic-606",
            "WebOnt-description-logic-608",
            "WebOnt-description-logic-643",
        ];
        List<(string Key, ReasoningModule Module)> modules = [];
        foreach(string suite in (string[])["approved", "proposed"])
        {
            foreach(object[] row in new Owl2ManifestDataAttribute(suite, "all.rdf", Owl2TestRemit.DirectSemanticsDl).GetData(typeof(W3cOwl2DirectTests).GetMethod(nameof(BooleanGadgetEliminationHeadToHeadHoldsOverCorpusAndBattery))!))
            {
                if(row is not [Owl2TestCase testCase] || Array.IndexOf(corpusIdentifiers, testCase.Identifier) < 0 || ContainsKey(modules, testCase.Identifier))
                {
                    continue;
                }

                if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
                {
                    Assert.Fail($"Head-to-head: the corpus premise {testCase.Identifier} failed to load.");

                    return;
                }

                premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
                OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);

                Assert.IsFalse(premise.Diagnostics.HasErrors, $"Head-to-head: the corpus premise {testCase.Identifier} failed to map.");
                modules.Add((testCase.Identifier, new ReasoningModule([.. premise.Axioms], Violations: [])));
            }
        }

        Assert.HasCount(corpusIdentifiers.Length, modules, "Head-to-head: all four shipped Shape G corpus ids are loaded.");
        foreach((string name, ReasoningModule module, bool _) in ContextGadgetDeciderTests.GadgetRows())
        {
            modules.Add((name, module));
        }

        GadgetConstruction suppressed = new(SuppressDefinedAtomElimination: true);
        StringBuilder report = new();
        report.AppendLine("module | B_raw | B_free | vectors elim/raw | microseconds elim/raw");
        foreach((string key, ReasoningModule module) in modules)
        {
            Assert.IsTrue(ContextBooleanGadgetDecider.TryMeasureAtomSpace(module, construction: default, out int rawAtoms, out int freeAtoms), $"Head-to-head {key}: the gadget jurisdiction admits the module.");
            Assert.IsTrue(ContextBooleanGadgetDecider.TryMeasureAtomSpace(module, suppressed, out int suppressedRaw, out int suppressedFree), $"Head-to-head {key}: the suppressing construction admits identically.");
            Assert.IsLessThanOrEqualTo(rawAtoms, freeAtoms, $"Head-to-head {key}: the surviving free atoms never exceed the raw total.");
            Assert.AreEqual(rawAtoms, suppressedRaw, $"Head-to-head {key}: the raw atom total is construction-invariant.");
            Assert.AreEqual(suppressedRaw, suppressedFree, $"Head-to-head {key}: suppression leaves every atom free.");

            BooleanGadgetOutcome production = ContextBooleanGadgetDecider.Run(module);
            BooleanGadgetOutcome raw = ContextBooleanGadgetDecider.Run(module, suppressed);

            Assert.IsNotNull(production.Consistent, $"Head-to-head {key}: the production faces decide the module.");
            Assert.AreEqual(raw.Consistent, production.Consistent, $"Head-to-head {key}: the two constructions decide the identical verdict.");
            Assert.IsLessThanOrEqualTo(raw.Window.EvaluatedVectorCount, production.Window.EvaluatedVectorCount, $"Head-to-head {key}: the production walk never evaluates more assignments than the raw one.");

            double productionMicroseconds = MeasureRunMicroseconds(module, construction: default);
            double rawMicroseconds = MeasureRunMicroseconds(module, suppressed);
            report.AppendLine(CultureInfo.InvariantCulture, $"{key} | {rawAtoms} | {freeAtoms} | {production.Window.EvaluatedVectorCount}/{raw.Window.EvaluatedVectorCount} | {productionMicroseconds:F1}/{rawMicroseconds:F1}");
        }

        TestContext.WriteLine(report.ToString());
    }

    /// <summary>Whether the module list already carries one loaded under the key — the corpus loader takes the first suite occurrence of each id.</summary>
    /// <param name="modules">The loaded modules.</param>
    /// <param name="key">The manifest identifier.</param>
    /// <returns><see langword="true"/> when the key is already loaded.</returns>
    private static bool ContainsKey(List<(string Key, ReasoningModule Module)> modules, string key)
    {
        for(int i = 0; i < modules.Count; i++)
        {
            if(string.Equals(modules[i].Key, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Times the gadget faces' whole run — jurisdiction admission, compilation, elimination planning, and walk — over one module under one construction, averaged across a fixed iteration count so the figure is stable at microsecond scale. The caller has already run both constructions once, so the timed loop is warm.</summary>
    /// <param name="module">The module to time.</param>
    /// <param name="construction">The construction variation.</param>
    /// <returns>The mean whole-run time in microseconds.</returns>
    private static double MeasureRunMicroseconds(ReasoningModule module, GadgetConstruction construction)
    {
        const int Iterations = 200;
        Stopwatch stopwatch = Stopwatch.StartNew();
        for(int i = 0; i < Iterations; i++)
        {
            _ = ContextBooleanGadgetDecider.Run(module, construction);
        }

        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMicroseconds / Iterations;
    }
}
