using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The facade world rows: a mutable database seeds the primary world, forks/lists/drops worlds by
/// name with value-based outcomes, scopes queries, updates, streams, and validation to a named world,
/// answers a diff as decoded per-graph transitions, and refuses world operations on an immutable
/// database the way it refuses updates. The discriminating row is the isolation one — an update in a
/// fork never reaches the primary world.
/// </summary>
[TestClass]
internal sealed class VeritasEngineWorldTests
{
    /// <summary>The example-namespace prefix the data, queries, and shapes share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The XSD string datatype IRI.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The XSD integer datatype IRI.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The SHACL vocabulary prefix.</summary>
    private const string Sh = "http://www.w3.org/ns/shacl#";

    /// <summary>The what-if world name the rows fork under.</summary>
    private const string WhatIf = "what-if";

    /// <summary>The ASK that finds the hypothetical value.</summary>
    private const string AskHypothetical = $"ASK {{ <{Ex}s> <{Ex}p> \"hypothetical\" }}";

    /// <summary>The update that commits the hypothetical value.</summary>
    private const string InsertHypothetical = $"INSERT DATA {{ <{Ex}s> <{Ex}p> \"hypothetical\" }}";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A mutable open seeds exactly the primary world under the well-known name.</summary>
    [TestMethod]
    public async Task TheMutableOpenSeedsThePrimaryWorld()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.IsTrue(new HashSet<string>(engine.WorldNames).SetEquals([WellKnownWorlds.Primary]), "A mutable open carries exactly the primary world.");
    }

    /// <summary>Fork, list, and drop round-trip by name with value-based outcomes.</summary>
    [TestMethod]
    public async Task ForkingListingAndDroppingRoundTrip()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsTrue(new HashSet<string>(engine.WorldNames).SetEquals([WellKnownWorlds.Primary, WhatIf]), "The fork registers beside the primary world.");

        Assert.AreEqual(WorldDropOutcome.Dropped, engine.DropWorld(WhatIf));
        Assert.IsTrue(new HashSet<string>(engine.WorldNames).SetEquals([WellKnownWorlds.Primary]), "The drop removes the fork's name and the primary world stays.");
    }

    /// <summary>The isolation row: an update committed into a fork answers there and never reaches the primary world.</summary>
    [TestMethod]
    public async Task AnUpdateInAForkLeavesThePrimaryUntouched()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(await engine.AskAsync(Utf8Strings.From(AskHypothetical), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false), "The fork answers its own committed hypothetical.");
        Assert.IsFalse(await engine.AskAsync(Utf8Strings.From(AskHypothetical), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false), "The primary world never sees the fork's commit.");
    }

    /// <summary>A world diff answers the decoded per-graph transitions, in both directions.</summary>
    [TestMethod]
    public async Task DiffingWorldsAnswersTheDecodedTransitions()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        DataTriple hypothetical = new(Iri(Ex + "s"), Iri(Ex + "p"), Str("hypothetical"));

        WorldDiff forward = engine.DiffWorlds(WhatIf, WellKnownWorlds.Primary);
        Assert.AreEqual(WorldDiffOutcome.Diffed, forward.Outcome);
        Assert.HasCount(1, forward.Transitions);
        Assert.IsNull(forward.Transitions[0].Graph, "The hypothetical landed in the default graph.");
        Assert.IsTrue(new HashSet<DataTriple>(forward.Transitions[0].Additions).SetEquals([hypothetical]), "The forward diff carries the hypothetical as the one decoded addition.");
        Assert.IsEmpty(forward.Transitions[0].Removals);

        WorldDiff reverse = engine.DiffWorlds(WellKnownWorlds.Primary, WhatIf);
        Assert.AreEqual(WorldDiffOutcome.Diffed, reverse.Outcome);
        Assert.HasCount(1, reverse.Transitions);
        Assert.IsEmpty(reverse.Transitions[0].Additions);
        Assert.IsTrue(new HashSet<DataTriple>(reverse.Transitions[0].Removals).SetEquals([hypothetical]), "The reverse diff carries the same triple as the one decoded removal.");
    }

    /// <summary>A world diffed against itself answers empty transitions.</summary>
    [TestMethod]
    public async Task DiffingAWorldAgainstItselfIsEmpty()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        WorldDiff diff = engine.DiffWorlds(WellKnownWorlds.Primary, WellKnownWorlds.Primary);
        Assert.AreEqual(WorldDiffOutcome.Diffed, diff.Outcome);
        Assert.IsEmpty(diff.Transitions);
    }

    /// <summary>Forking from a name no world holds answers the unknown-source outcome and registers nothing.</summary>
    [TestMethod]
    public async Task ForkingFromAnUnknownSourceAnswersUnknownSource()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.UnknownSource, await engine.ForkWorldAsync("missing", WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.HasCount(1, engine.WorldNames);
    }

    /// <summary>Forking under a taken name — another fork's or the primary's — answers the duplicate-name outcome and registers nothing.</summary>
    [TestMethod]
    public async Task ForkingUnderATakenNameAnswersDuplicateName()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(WorldForkOutcome.DuplicateName, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(WorldForkOutcome.DuplicateName, await engine.ForkWorldAsync(WhatIf, WellKnownWorlds.Primary, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.HasCount(2, engine.WorldNames);
    }

    /// <summary>The primary world is never droppable.</summary>
    [TestMethod]
    public async Task DroppingThePrimaryWorldIsRefused()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldDropOutcome.PrimaryWorld, engine.DropWorld(WellKnownWorlds.Primary));
        Assert.IsTrue(new HashSet<string>(engine.WorldNames).SetEquals([WellKnownWorlds.Primary]), "The refused drop changes nothing.");
    }

    /// <summary>Dropping a name no world holds answers the unknown-world outcome.</summary>
    [TestMethod]
    public async Task DroppingAnUnknownWorldAnswersUnknownWorld()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldDropOutcome.UnknownWorld, engine.DropWorld("missing"));
    }

    /// <summary>An immutable database carries no worlds and refuses world operations the way it refuses updates.</summary>
    [TestMethod]
    public async Task WorldOperationsOnAnImmutableDatabaseThrow()
    {
        VeritasEngineOptions options = new() { Reasoning = null };
        VeritasEngine engine = await VeritasEngine.OpenAsync(BaseData(), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.HasCount(0, engine.WorldNames);
        Assert.IsEmpty(engine.DescribeWorlds(), "An immutable database describes no worlds, the same harmless read WorldNames gives it.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.ThrowsExactly<InvalidOperationException>(() => engine.DropWorld(WhatIf));
        Assert.ThrowsExactly<InvalidOperationException>(() => engine.DiffWorlds(WellKnownWorlds.Primary, WhatIf));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await engine.AskAsync(Utf8Strings.From(AskHypothetical), world: WellKnownWorlds.Primary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: WellKnownWorlds.Primary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>Naming a world no registry entry holds on an execution is the caller's contract and throws.</summary>
    [TestMethod]
    public async Task NamingAnUnknownWorldOnAnExecutionThrows()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await engine.AskAsync(Utf8Strings.From(AskHypothetical), world: "missing", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: "missing", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>A streamed SELECT scoped to a fork yields the fork's rows while the primary stream keeps its own.</summary>
    [TestMethod]
    public async Task StreamingASelectInAForkYieldsTheForksRows()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        string select = $"SELECT ?o WHERE {{ <{Ex}s> <{Ex}p> ?o }}";
        Assert.AreEqual(2, await CountStreamedSolutionsAsync(engine, select, WhatIf).ConfigureAwait(false), "The fork streams the base row and the hypothetical.");
        Assert.AreEqual(1, await CountStreamedSolutionsAsync(engine, select, null).ConfigureAwait(false), "The primary world streams only the base row.");
    }

    /// <summary>Validation scoped to a fork validates the fork's data while the primary world keeps conforming.</summary>
    [TestMethod]
    public async Task ValidatingAWorldValidatesTheWorldsData()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        ValidationReport primary = await engine.ValidateAsync(MaxOneValueShape(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(primary.Conforms, "The primary world holds one value under the path, within the max count.");

        ValidationReport fork = await engine.ValidateAsync(MaxOneValueShape(), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(fork.Conforms, "The fork holds two values under the path, violating the max count.");
    }

    /// <summary>
    /// The worlds listing carries lineage and content-addressed state identifiers: the primary world
    /// comes first with no parent and the remaining worlds follow in ordinal name order each naming its
    /// fork source; an undiverged fork shares the primary's state identifier and a committed update
    /// gives it its own.
    /// </summary>
    [TestMethod]
    public async Task DescribingWorldsAnswersLineageAndStateIds()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WhatIf, "another", TestContext.CancellationToken).ConfigureAwait(false));

        ImmutableArray<WorldDescriptor> described = engine.DescribeWorlds();
        Assert.HasCount(3, described);
        Assert.AreEqual(WellKnownWorlds.Primary, described[0].Name, "The primary world leads the listing.");
        Assert.IsNull(described[0].Parent, "The primary world has no fork parent.");
        Assert.AreEqual("another", described[1].Name, "The remaining worlds follow in ordinal name order.");
        Assert.AreEqual(WhatIf, described[1].Parent, "A fork names its source world as its parent.");
        Assert.AreEqual(WhatIf, described[2].Name);
        Assert.AreEqual(WellKnownWorlds.Primary, described[2].Parent);
        Assert.AreEqual(described[0].StateId, described[2].StateId, "An undiverged fork shares the primary world's content-addressed state identifier.");

        await engine.UpdateAsync(Utf8Strings.From(InsertHypothetical), world: WhatIf, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        ImmutableArray<WorldDescriptor> diverged = engine.DescribeWorlds();
        Assert.AreNotEqual(diverged[0].StateId, diverged[2].StateId, "A committed update gives the fork its own state identifier.");
    }

    /// <summary>Lineage is history, not a live reference: a fork keeps its recorded parent name after that parent's name is dropped.</summary>
    [TestMethod]
    public async Task LineageSurvivesDroppingTheParent()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(BaseData(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WellKnownWorlds.Primary, WhatIf, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(WorldForkOutcome.Forked, await engine.ForkWorldAsync(WhatIf, "grandchild", TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(WorldDropOutcome.Dropped, engine.DropWorld(WhatIf));

        ImmutableArray<WorldDescriptor> described = engine.DescribeWorlds();
        Assert.HasCount(2, described);
        Assert.AreEqual("grandchild", described[1].Name);
        Assert.AreEqual(WhatIf, described[1].Parent, "The recorded parent name stands after the parent's name is dropped.");
    }

    /// <summary>Streams one SELECT scoped to a world and counts its solutions.</summary>
    /// <param name="engine">The database.</param>
    /// <param name="select">The SELECT text.</param>
    /// <param name="world">The world to scope to, or <see langword="null"/> for the primary world.</param>
    /// <returns>The number of streamed solutions.</returns>
    private async Task<int> CountStreamedSolutionsAsync(VeritasEngine engine, string select, string? world)
    {
        int count = 0;
        using(VeritasSelectStream stream = await engine.StreamSelectAsync(Utf8Strings.From(select), world: world, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            await foreach(SparqlSolution solution in stream.Solutions.WithCancellation(TestContext.CancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The base default-graph data: one value under the path.</summary>
    /// <returns>The triples.</returns>
    private static List<DataTriple> BaseData()
    {
        return [new DataTriple(Iri(Ex + "s"), Iri(Ex + "p"), Str("real"))];
    }

    /// <summary>A node shape targeting the subject with a max-count-one property shape over the path.</summary>
    /// <returns>The shapes triples.</returns>
    private static List<DataTriple> MaxOneValueShape()
    {
        return
        [
            new DataTriple(Iri(Ex + "Shape"), Iri(RdfType), Iri(Sh + "NodeShape")),
            new DataTriple(Iri(Ex + "Shape"), Iri(Sh + "targetNode"), Iri(Ex + "s")),
            new DataTriple(Iri(Ex + "Shape"), Iri(Sh + "property"), Iri(Ex + "PathShape")),
            new DataTriple(Iri(Ex + "PathShape"), Iri(Sh + "path"), Iri(Ex + "p")),
            new DataTriple(Iri(Ex + "PathShape"), Iri(Sh + "maxCount"), new Literal(Utf8Strings.From("1"), Iri(XsdInteger)))
        ];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Builds an <c>xsd:string</c> literal term.</summary>
    /// <param name="value">The lexical form.</param>
    /// <returns>The literal term.</returns>
    private static Literal Str(string value)
    {
        return new Literal(Utf8Strings.From(value), Iri(XsdString));
    }
}
