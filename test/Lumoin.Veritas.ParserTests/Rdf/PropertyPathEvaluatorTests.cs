using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Verification suite for <see cref="PropertyPathEvaluator"/>. The first
/// seven tests cover the seven AST constructors in isolation. The
/// remainder cover composition cases — operators interacting with each
/// other — that the SPARQL 1.1 §9 and SHACL 1.2 Core §2.3 semantics
/// require but that the single-operator tests do not exercise.
/// </summary>
/// <remarks>
/// <para>
/// Encoded node ids start at <c>10</c> (well clear of the
/// <see cref="TermId.None"/> sentinel at <c>0</c>) so bound-to-X match
/// queries do not alias with unbound queries inside the path evaluator.
/// Encoded predicate ids start at <c>100</c> for the same reason.
/// </para>
/// </remarks>
[TestClass]
internal sealed class PropertyPathEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PredicatePathReachesDirectNeighbours()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(10, 200, 13)
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10),
            new PredicatePath(PredIri(100)),
            store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task SequencePathThreadsResultsBetweenSteps()
    {
        //Graph: 10 -p1-> 11, 11 -p2-> 12, 11 -p2-> 13.
        //Sequence p1/p2 from 10 should reach {12, 13}.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12),
            EncodedTriple.FromEncoded(11, 200, 13)
        ]);

        PropertyPath path = new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(12), TermId.FromEncoded(13) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task AlternativePathUnionsBranches()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 200, 12)
        ]);

        PropertyPath path = new AlternativePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task InversePathFlipsDirection()
    {
        //Graph: 10 -p-> 11. The inverse of p from 11 should reach 10.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);

        PropertyPath path = new InversePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(11), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ZeroOrMoreIncludesStartAndAllReachable()
    {
        //Chain 10 -> 11 -> 12 -> 13. The start is included by reflexivity.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12),
            EncodedTriple.FromEncoded(12, 100, 13)
        ]);

        PropertyPath path = new ZeroOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12), TermId.FromEncoded(13) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task OneOrMoreExcludesStartUnlessReachableByCycle()
    {
        //Chain 10 -> 11 -> 12; start not reachable from itself in an acyclic chain.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12)
        ]);

        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ZeroOrOneIncludesStartAndOneStep()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12)
        ]);

        PropertyPath path = new ZeroOrOnePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task AlternationUnderKleeneTraversesByEitherPredicate()
    {
        //Graph: A -p-> B -q-> C -p-> D. From A, (:p | :q)+ visits each
        //node reached by any alternating sequence of the two predicates.
        //Start excluded (no cycle back to A).
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12),
            EncodedTriple.FromEncoded(12, 100, 13)
        ]);

        PropertyPath path = new OneOrMorePath(new AlternativePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12), TermId.FromEncoded(13) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task InverseKleeneWalksBackwardsAlongChain()
    {
        //Graph: A -p-> B -p-> C. From C, the AST ^(:p+) walks backwards.
        //The evaluator rewrites this to (^:p)+ via InvertPath.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12)
        ]);

        PropertyPath outerInverse = new InversePath(new OneOrMorePath(new PredicatePath(PredIri(100))));
        PropertyPath rewrittenForm = new OneOrMorePath(new InversePath(new PredicatePath(PredIri(100))));

        HashSet<TermId> reachedOuter = await CollectReachableAsync(
            TermId.FromEncoded(12), outerInverse, store).ConfigureAwait(false);
        HashSet<TermId> reachedRewritten = await CollectReachableAsync(
            TermId.FromEncoded(12), rewrittenForm, store).ConfigureAwait(false);

        //Both AST shapes must produce the same set: {A, B}.
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) },
            AsArray(reachedOuter), SequenceOrder.InAnyOrder);
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) },
            AsArray(reachedRewritten), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task SequenceWithTrailingKleeneCollectsTransitiveResult()
    {
        //Graph: A -p-> B, B -q-> C, B -q-> D, D -q-> E.
        //Path :p / :q* from A reaches {B, C, D, E} because the q* step
        //includes its starts (here, B) by reflexivity.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12),
            EncodedTriple.FromEncoded(11, 200, 13),
            EncodedTriple.FromEncoded(13, 200, 14)
        ]);

        PropertyPath path = new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new ZeroOrMorePath(new PredicatePath(PredIri(200)))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12), TermId.FromEncoded(13), TermId.FromEncoded(14) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task SequenceWithLeadingKleeneOnlyFiresWhereStepApplies()
    {
        //Graph: A -p-> B, B -q-> C, B -q-> D, D -q-> E.
        //Path :p* / :q from A. The p* step yields {A, B}; then :q from
        //{A, B} yields {C, D} (A has no outgoing :q, B has two).
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12),
            EncodedTriple.FromEncoded(11, 200, 13),
            EncodedTriple.FromEncoded(13, 200, 14)
        ]);

        PropertyPath path = new SequencePath(
        [
            new ZeroOrMorePath(new PredicatePath(PredIri(100))),
            new PredicatePath(PredIri(200))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(12), TermId.FromEncoded(13) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task SequenceInsideKleeneAdvancesTwoHopsPerIteration()
    {
        //Graph: A -p-> B -q-> C -p-> D -q-> E.
        //Path (:p / :q)+ from A. Each iteration of the outer + takes
        //one :p / :q step, advancing two graph hops. Start excluded.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12),
            EncodedTriple.FromEncoded(12, 100, 13),
            EncodedTriple.FromEncoded(13, 200, 14)
        ]);

        PropertyPath path = new OneOrMorePath(new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(12), TermId.FromEncoded(14) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task OneOrMoreIncludesStartWhenReachableByCycle()
    {
        //Graph: A -p-> B -p-> A. From A, :p+ reaches {A, B} because B
        //then loops back to A — the start is reachable via the cycle.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 10)
        ]);

        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ZeroOrMoreTerminatesOnCyclicGraph()
    {
        //Graph: A -p-> B -p-> A. From A, :p* reaches {A, B}. The BFS
        //must terminate; the visited set blocks the second visit to A.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 10)
        ]);

        PropertyPath path = new ZeroOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task OneOrMoreTraversesHundredElementChainWithoutStackOverflow()
    {
        //Linear chain of one hundred edges. The evaluator is iterative,
        //so the chain depth is a memory concern rather than a stack
        //concern; this test confirms termination and complete coverage.
        const int ChainLength = 100;
        EncodedTriple[] triples = new EncodedTriple[ChainLength];
        for(int i = 0; i < ChainLength; i++)
        {
            triples[i] = EncodedTriple.FromEncoded((uint)(10 + i), 100, (uint)(10 + i + 1));
        }

        InMemoryGraphStore store = InMemoryGraphStore.Build(triples);
        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.HasCount(ChainLength, reached);
        for(int i = 1; i <= ChainLength; i++)
        {
            Assert.Contains(TermId.FromEncoded((uint)(10 + i)), reached);
        }

        //Start excluded — the chain has no cycle back to A0.
        Assert.DoesNotContain(TermId.FromEncoded(10), reached);
    }

    [TestMethod]
    public async Task OneOrMoreWithEmptyStartsYieldsEmpty()
    {
        //Degenerate case: an empty starts collection produces no first
        //step, which short-circuits to an empty result.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11)
        ]);

        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateFromSetAsync(
            Array.Empty<TermId>(), path, store.AsMatchOps(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        Assert.IsEmpty(reached);
    }

    [TestMethod]
    public async Task ZeroOrOneIncludesStartWhenInnerPredicateIsAbsent()
    {
        //Graph: A -q-> B. From A, :p? has no outgoing :p step but the
        //ZeroOrOne semantics include the start unconditionally.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 200, 11)
        ]);

        PropertyPath path = new ZeroOrOnePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task InverseFollowedByForwardReturnsStart()
    {
        //Graph: A -p-> B. Path ^:p / :p from B: ^:p goes B -> A,
        //then :p goes A -> B. Round-trip back to the start.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);

        PropertyPath path = new SequencePath(
        [
            new InversePath(new PredicatePath(PredIri(100))),
            new PredicatePath(PredIri(100))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(11), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task DeeplyNestedKleeneOverAlternationTerminates()
    {
        //Path ((:p | :q)+)? over a small graph. The outer ZeroOrOne
        //dispatches to inner OneOrMore over the alternation, then adds
        //the start. The test verifies the dispatcher handles arbitrary
        //nesting without trapping itself.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 200, 12),
            EncodedTriple.FromEncoded(11, 100, 13)
        ]);

        PropertyPath path = new ZeroOrOnePath(new OneOrMorePath(new AlternativePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ])));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        //Start is included by the outer ?. Inner Kleene over (p|q)
        //reaches {11, 12, 13}.
        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12), TermId.FromEncoded(13) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ForwardFollowedByInverseReturnsStart()
    {
        //Graph: A -p-> B. Path :p / ^:p from A: :p goes A -> B,
        //then ^:p goes B -> A. Round-trip from the other direction.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);

        PropertyPath path = new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new InversePath(new PredicatePath(PredIri(100)))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task DoubleInverseIsEquivalentToBarePredicate()
    {
        //Graph: A -p-> B. Path ^^:p is algebraically the same as :p:
        //the inner inverse cancels with the outer inverse.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);

        PropertyPath doubleInverse = new InversePath(new InversePath(new PredicatePath(PredIri(100))));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), doubleInverse, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task InverseOfSequenceReversesStepOrder()
    {
        //Graph: A -p-> B -q-> C. Path ^(:p / :q) from C must walk
        //backwards through the sequence: ^:q goes C -> B, then ^:p
        //goes B -> A. The InvertPath rewrite reverses the step order.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 200, 12)
        ]);

        PropertyPath path = new InversePath(new SequencePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(200))
        ]));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(12), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task ZeroOrMoreOnSelfLoopIncludesStartOnce()
    {
        //Graph: A -p-> A. The Kleene visited set must deduplicate the
        //start node when the self-loop tries to re-add it.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 10)]);

        PropertyPath path = new ZeroOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task OneOrMoreOnSelfLoopIncludesStartByCycle()
    {
        //Graph: A -p-> A. :p+ from A reaches {A} because the first step
        //yields A (the self-loop), which then seeds the visited set.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 10)]);

        PropertyPath path = new OneOrMorePath(new PredicatePath(PredIri(100)));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(10) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task EvaluateFromSetUnionsResultsAcrossMultipleStarts()
    {
        //Two disjoint chains: 10 -p-> 11 and 20 -p-> 21.
        //EvaluateFromSetAsync over {10, 20} with :p should yield {11, 21}.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 21)
        ]);

        PropertyPath path = new PredicatePath(PredIri(100));

        HashSet<TermId> reached = [];
        TermId[] starts = [TermId.FromEncoded(10), TermId.FromEncoded(20)];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateFromSetAsync(
            starts, path, store.AsMatchOps(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(21) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task DuplicateAlternativeBranchesDeduplicate()
    {
        //Graph: A -p-> B. Path :p | :p must yield {B}, not {B, B} —
        //alternation is a set union, not a multiset bag.
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);

        PropertyPath path = new AlternativePath(
        [
            new PredicatePath(PredIri(100)),
            new PredicatePath(PredIri(100))
        ]);

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task NestedOneOrMoreIsIdempotent()
    {
        //Chain 10 -> 11 -> 12. (:p+)+ from 10 must reach the same
        //{11, 12} as :p+ from 10 — repeated transitive closure
        //of a transitive closure is the same set.
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(11, 100, 12)
        ]);

        PropertyPath path = new OneOrMorePath(new OneOrMorePath(new PredicatePath(PredIri(100))));

        HashSet<TermId> reached = await CollectReachableAsync(
            TermId.FromEncoded(10), path, store).ConfigureAwait(false);

        Assert.AreSequenceEqual(
            new TermId[] { TermId.FromEncoded(11), TermId.FromEncoded(12) },
            AsArray(reached), SequenceOrder.InAnyOrder);
    }

    //Wraps a raw predicate id as an <see cref="IriId"/>. SHACL and SPARQL
    //predicates are always IRIs so an unchecked wrap is appropriate
    //inside the test fixtures.
    private static IriId PredIri(uint encoded) => IriId.FromUnchecked(TermId.FromEncoded(encoded));

    //Copies a hash set into a freshly allocated array. Used by
    //<c>Assert.AreSequenceEqual</c> calls that take an
    //<c>ICollection</c> on the actual side, unordered via
    //<c>SequenceOrder.InAnyOrder</c>.
    private static TermId[] AsArray(HashSet<TermId> set)
    {
        TermId[] arr = new TermId[set.Count];
        set.CopyTo(arr);
        return arr;
    }

    //Drives <see cref="PropertyPathEvaluator.EvaluateAsync"/> for a
    //single start node and collects the yielded set into a hash set.
    //All single-start tests funnel through here.
    private async ValueTask<HashSet<TermId>> CollectReachableAsync(TermId start, PropertyPath path, InMemoryGraphStore store)
    {
        HashSet<TermId> reached = [];
        await foreach(TermId node in PropertyPathEvaluator.EvaluateAsync(
            start, path, store.AsMatchOps(), TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(node);
        }

        return reached;
    }
}
