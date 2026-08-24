using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for the Seam Q type-expansion seam: a bound <c>rdf:type</c> pattern
/// evaluates once per expansion class with cross-variant deduplication, the
/// EL classification's subsumee index plugs in as the producer, and an
/// engine without the seam behaves exactly as before.
/// </summary>
[TestClass]
internal sealed class TypeExpansionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Ex = "http://example.org/";

    /// <summary>An expansion delegate widens a superclass query to subclass instances.</summary>
    [TestMethod]
    public async Task ExpansionWidensTheTypePattern()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            Data(),
            typeExpansion: VehicleExpansion,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyList<SparqlSolution> solutions = await EvaluateVehiclesAsync(engine).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "The Car instance answers the Vehicle query through the expansion.");
    }

    /// <summary>An instance typed under two expansion classes answers once.</summary>
    [TestMethod]
    public async Task CrossVariantDuplicatesCollapse()
    {
        List<DataTriple> data = Data();
        data.Add(new DataTriple(Iri("a"), Iri("type"), Iri("Vehicle")));

        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            data,
            typeExpansion: VehicleExpansion,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyList<SparqlSolution> solutions = await EvaluateVehiclesAsync(engine).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "a is typed Car and Vehicle but answers once.");
    }

    /// <summary>The EL classification's subsumee index is the seam's intended producer.</summary>
    [TestMethod]
    public async Task ElClassificationDrivesTheExpansion()
    {
        OwlOntologyDocument document = new(
            [new OwlSubClassOfAxiom(
                new OwlClassReference(new NamedNode(Utf8Strings.From(Ex + "Car"))),
                new OwlClassReference(new NamedNode(Utf8Strings.From(Ex + "Vehicle"))))
            {
                Origin = new Quad(Iri("s"), Iri("p"), Iri("o"), Graph: null),
            }],
            ontologyIri: null,
            new DiagnosticBag(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>());

        ElClassification classification = ElClassifier.Classify(document, TestContext.CancellationToken);

        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            Data(),
            typeExpansion: classification.SubsumeesOf,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyList<SparqlSolution> solutions = await EvaluateVehiclesAsync(engine).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "The classifier's subsumee index expands the Vehicle query to Car.");
    }

    /// <summary>An expansion class absent from the data dictionary is skipped — it can match nothing.</summary>
    [TestMethod]
    public async Task AbsentExpansionClassesAreSkipped()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            Data(),
            typeExpansion: _ =>
            [
                Utf8Strings.From(Ex + "Vehicle"),
                Utf8Strings.From(Ex + "Car"),
                Utf8Strings.From(Ex + "NeverMentioned"),
            ],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyList<SparqlSolution> solutions = await EvaluateVehiclesAsync(engine).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
    }

    /// <summary>An engine without the seam evaluates the pattern unexpanded.</summary>
    [TestMethod]
    public async Task NoSeamMeansNoExpansion()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            Data(),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        IReadOnlyList<SparqlSolution> solutions = await EvaluateVehiclesAsync(engine).ConfigureAwait(false);

        Assert.HasCount(1, solutions, "Only the directly-typed Vehicle instance answers.");
    }

    //Harness.

    //:a type :Car; :b type :Vehicle.
    private static List<DataTriple> Data()
    {
        return
        [
            new DataTriple(Iri("a"), Iri("type"), Iri("Car")),
            new DataTriple(Iri("b"), Iri("type"), Iri("Vehicle")),
        ];
    }

    private static IReadOnlyCollection<Utf8String> VehicleExpansion(Utf8String classIri)
    {
        return classIri.ToString() == Ex + "Vehicle"
            ? [Utf8Strings.From(Ex + "Vehicle"), Utf8Strings.From(Ex + "Car")]
            : [classIri];
    }

    private async Task<IReadOnlyList<SparqlSolution>> EvaluateVehiclesAsync(SparqlQueryEngine engine)
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(
            "PREFIX ex: <http://example.org/> PREFIX rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> SELECT ?x WHERE { ?x rdf:type ex:Vehicle }",
            pool);

        return await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
    }

    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    private static NamedNode Iri(string local)
    {
        string iri = local == "type" ? "http://www.w3.org/1999/02/22-rdf-syntax-ns#type" : Ex + local;

        return new NamedNode(Utf8Strings.From(iri));
    }
}
