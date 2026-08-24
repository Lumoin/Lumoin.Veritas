using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Tests the <see cref="VeritasEngine"/> database facade: it composes the query
/// engine and the reasoner into one engine you open over data and query. By
/// default reasoning is wired and fully usable, so a query answers over the
/// entailed graph; un-wiring reasoning — the lean-deployment optimisation —
/// serves simple-entailment results.
/// </summary>
[TestClass]
internal sealed class VeritasEngineTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdfs:subClassOf</c> IRI.</summary>
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task DefaultDatabaseReasonsSoEntailmentsAreQueryable()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(SubclassGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //rex is asserted a Dog and Dog is a subclass of Animal, so RDFS entails rex a Animal.
        bool isAnimal = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(isAnimal, "The default database materialises the RDFS entailment rex a Animal.");
    }

    [TestMethod]
    public async Task UnwiredReasoningServesSimpleEntailment()
    {
        VeritasEngineOptions noReasoning = VeritasEngineOptions.Default with { Reasoning = null };
        VeritasEngine database = await VeritasEngine
            .OpenAsync(SubclassGraph(), noReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //With reasoning unwired the entailed triple is never derived.
        bool isAnimal = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(isAnimal, "Without reasoning the entailment is absent.");

        //The asserted triple is of course still served.
        bool isDog = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(isDog, "The asserted triple is served unchanged.");
    }

    [TestMethod]
    public async Task MutableDatabaseAnswersQueriesOffTheCommittedSnapshot()
    {
        //Reasoning unwired: the mutable engine serves the asserted default graph only, derived off the committed
        //snapshot per query (the reasoned mutable lane is exercised by the reasoning-surface tests).
        VeritasEngineOptions noReasoning = VeritasEngineOptions.Default with { Reasoning = null };
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(SubclassGraph(), noReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The mutable engine serves the asserted default graph, derived off the committed snapshot per query.
        bool isDog = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(isDog, "The mutable database serves the asserted default graph.");

        //With reasoning unwired the mutable engine serves the asserted graph only, so the subclass entailment is
        //not materialised.
        bool isAnimal = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(isAnimal, "With reasoning unwired the mutable engine serves the asserted graph; no entailment is materialised.");
    }

    [TestMethod]
    public async Task MutableDatabaseCommitsAnUpdateAndReadsItsOwnWrite()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([], cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        await database
            .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        bool isDog = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(isDog, "A query sees the update's committed triple (read-your-writes).");

        await database
            .UpdateAsync(Utf8Strings.From($"DELETE DATA {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        bool stillDog = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(stillDog, "A query sees the delete's effect.");
    }

    [TestMethod]
    public async Task ImmutableDatabaseRejectsUpdates()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(SubclassGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        await Assert
            .ThrowsAsync<System.InvalidOperationException>(async () => await database
                .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}a> <{RdfType}> <{Ex}b> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ValidateReportsConformanceAgainstShapes()
    {
        //alice is a Person with a name — conforming under "every Person has a name".
        VeritasEngine database = await VeritasEngine
            .OpenAsync(PersonData(withName: true), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ValidationReport report = await database
            .ValidateAsync(EveryPersonHasAName(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(report.Conforms, "alice has a name, so the data conforms.");
    }

    [TestMethod]
    public async Task ValidateReportsAViolationWhenTheDataDoesNotConform()
    {
        //bob is a Person with no name — violating "every Person has a name".
        VeritasEngine database = await VeritasEngine
            .OpenAsync(PersonData(withName: false), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ValidationReport report = await database
            .ValidateAsync(EveryPersonHasAName(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsFalse(report.Conforms, "bob has no name, so the data violates the shape.");
        Assert.IsNotEmpty(report.Results, "The non-conformance is reported as a result.");
    }

    /// <summary>A federated <c>LOAD</c> through the facade resolves via the configured graph-source seam and forwards the caller's opaque <see cref="AccessContext"/> to it — the engine owns federation, and authorization is a protocol-agnostic context the seam carries.</summary>
    [TestMethod]
    public async Task FederatedLoadThroughTheFacadeResolvesAndForwardsTheAccessContext()
    {
        AccessContext? captured = null;
        bool fetched = false;
        GraphSourceResolver resolver = (source, accessContext, cancellationToken) =>
        {
            fetched = true;
            captured = accessContext;

            return SingleTripleStream(new DataTriple(Iri(Ex + "x"), Iri(Ex + "p"), Iri(Ex + "y")));
        };
        TestAccessContext context = new("alice");

        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync([], VeritasEngineOptions.Default with { GraphSource = resolver }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        await database
            .UpdateAsync(Utf8Strings.From($"LOAD <{Ex}doc>"), accessContext: context, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(fetched, "The LOAD reached the configured graph-source resolver through the facade.");
        Assert.AreSame(context, captured, "The caller's opaque AccessContext is forwarded to the resolver unchanged.");

        bool loaded = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}x> <{Ex}p> <{Ex}y> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(loaded, "The resolved document's triple is committed and queryable.");
    }

    /// <summary>A query's <c>FROM</c> clause is resolved through the graph-source seam and becomes the queried dataset, so a read over <c>FROM &lt;g&gt;</c> sees the resolved graph rather than the empty default.</summary>
    [TestMethod]
    public async Task FromClauseResolvesTheDatasetThroughTheGraphSource()
    {
        bool fetched = false;
        GraphSourceResolver resolver = (source, accessContext, cancellationToken) =>
        {
            fetched = true;

            return SingleTripleStream(new DataTriple(Iri(Ex + "x"), Iri(Ex + "p"), Iri(Ex + "y")));
        };

        VeritasEngine database = await VeritasEngine
            .OpenAsync([], VeritasEngineOptions.Default with { GraphSource = resolver }, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The default graph is empty; FROM <g> overrides it with the resolved graph, so the triple is visible.
        bool found = await database
            .AskAsync(Utf8Strings.From($"ASK FROM <{Ex}g> WHERE {{ <{Ex}x> <{Ex}p> <{Ex}y> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(fetched, "The FROM clause reached the configured graph-source resolver through the facade.");
        Assert.IsTrue(found, "A read over FROM <g> queries the resolved graph, not the empty default.");
    }

    /// <summary>A deny-all access-control policy filters every local read through the facade — across ASK and SELECT, on both the mutable and immutable engines. Diagnostic: one run surfaces all four outcomes.</summary>
    [TestMethod]
    public async Task AccessControlPolicyFiltersReadsThroughTheFacade()
    {
        IReadOnlyList<DataTriple> seed = [new DataTriple(Iri(Ex + "x"), Iri(Ex + "p"), Iri(Ex + "y"))];
        AccessControlDelegate denyAll = (request, cancellationToken) => new ValueTask<AccessDecision>(AccessDecision.Deny);
        TestAccessContext context = new("alice");
        VeritasEngineOptions denying = VeritasEngineOptions.Default with { AccessControl = denyAll };

        VeritasEngine mutable = await VeritasEngine
            .OpenMutableAsync(seed, denying, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var mutableScope = mutable.ConfigureAwait(false);
        VeritasEngine immutable = await VeritasEngine
            .OpenAsync(seed, denying, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var immutableScope = immutable.ConfigureAwait(false);

        //Membership reads (fully-bound ASK and constant-only SELECT) and scanning reads (a variable SELECT) must
        //all be filtered by a deny-all policy; the fully-bound forms are the ones a membership-only filter would miss.
        Utf8String membershipAsk = Utf8Strings.From($"ASK {{ <{Ex}x> <{Ex}p> <{Ex}y> }}");
        Utf8String membershipSelect = Utf8Strings.From($"SELECT * WHERE {{ <{Ex}x> <{Ex}p> <{Ex}y> }}");
        Utf8String scanSelect = Utf8Strings.From($"SELECT * WHERE {{ <{Ex}x> <{Ex}p> ?o }}");

        bool mutableAsk = await mutable.AskAsync(membershipAsk, accessContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        int mutableConst = (await mutable.QueryAsync(membershipSelect, accessContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).Bindings!.Solutions.Count;
        int mutableScan = (await mutable.QueryAsync(scanSelect, accessContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).Bindings!.Solutions.Count;
        bool immutableAsk = await immutable.AskAsync(membershipAsk, accessContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        int immutableConst = (await immutable.QueryAsync(membershipSelect, accessContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).Bindings!.Solutions.Count;
        int immutableScan = (await immutable.QueryAsync(scanSelect, accessContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).Bindings!.Solutions.Count;

        Assert.IsTrue(
            !mutableAsk && mutableConst == 0 && mutableScan == 0 && !immutableAsk && immutableConst == 0 && immutableScan == 0,
            $"A deny-all policy must filter every read (false/0 = filtered). mutable(ask={mutableAsk}, const={mutableConst}, scan={mutableScan}) immutable(ask={immutableAsk}, const={immutableConst}, scan={immutableScan}).");
    }

    /// <summary>A concrete access context carrying just a caller name, for the federation tests; the engine treats it as opaque.</summary>
    /// <param name="Who">The caller identity the test threads through.</param>
    private sealed record TestAccessContext(string Who) : AccessContext;

    /// <summary>The SHACL property shape: every Person must have at least one name.</summary>
    /// <returns>The shapes triples.</returns>
    private static IReadOnlyList<DataTriple> EveryPersonHasAName()
    {
        const string Sh = "http://www.w3.org/ns/shacl#";
        const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

        return
        [
            new DataTriple(Iri(Ex + "NameShape"), Iri(RdfType), Iri(Sh + "PropertyShape")),
            new DataTriple(Iri(Ex + "NameShape"), Iri(Sh + "targetClass"), Iri(Ex + "Person")),
            new DataTriple(Iri(Ex + "NameShape"), Iri(Sh + "path"), Iri(Ex + "name")),
            new DataTriple(Iri(Ex + "NameShape"), Iri(Sh + "minCount"), new Literal(Utf8Strings.From("1"), Iri(XsdInteger))),
        ];
    }

    /// <summary>A Person, with or without a name.</summary>
    /// <param name="withName">Whether the person carries a name.</param>
    /// <returns>The data triples.</returns>
    private static IReadOnlyList<DataTriple> PersonData(bool withName)
    {
        const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
        NamedNode person = Iri(Ex + (withName ? "alice" : "bob"));
        DataTriple isPerson = new(person, Iri(RdfType), Iri(Ex + "Person"));

        return withName
            ? [isPerson, new DataTriple(person, Iri(Ex + "name"), new Literal(Utf8Strings.From("Alice"), Iri(XsdString)))]
            : [isPerson];
    }

    /// <summary>The RDFS-shaped graph: rex is a Dog, and Dog is a subclass of Animal.</summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> SubclassGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfsSubClassOf), Iri(Ex + "Animal")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Dog")),
        ];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Yields a single triple as an async stream — the streaming shape a <see cref="GraphSourceResolver"/> delivers a resolved document in.</summary>
    /// <param name="triple">The triple to stream.</param>
    /// <returns>A one-triple async stream.</returns>
    private static async IAsyncEnumerable<DataTriple> SingleTripleStream(DataTriple triple)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return triple;
    }
}
