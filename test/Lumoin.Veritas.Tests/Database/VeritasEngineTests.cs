using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
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
            .AskAsync($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}", TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(isAnimal, "The default database materialises the RDFS entailment rex a Animal.");
    }

    [TestMethod]
    public async Task UnwiredReasoningServesSimpleEntailment()
    {
        VeritasEngineOptions noReasoning = VeritasEngineOptions.Default with { Reasoning = null };
        VeritasEngine database = await VeritasEngine
            .OpenAsync(SubclassGraph(), noReasoning, TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //With reasoning unwired the entailed triple is never derived.
        bool isAnimal = await database
            .AskAsync($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}", TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsFalse(isAnimal, "Without reasoning the entailment is absent.");

        //The asserted triple is of course still served.
        bool isDog = await database
            .AskAsync($"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}", TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(isDog, "The asserted triple is served unchanged.");
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
        return new NamedNode(Utf8String.FromString(iri));
    }
}
