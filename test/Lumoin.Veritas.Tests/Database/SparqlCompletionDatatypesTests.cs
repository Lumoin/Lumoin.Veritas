using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Database.Completion;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Tests the store-backed datatype resolver that completes a <see cref="CompletionContext"/>: for an in-scope
/// variable bound in an object position, <see cref="SparqlCompletionDatatypes.ResolveAsync"/> runs a three-tier
/// ladder against a live <see cref="VeritasEngine"/> — a SHACL <c>sh:datatype</c>, an <c>rdfs:range</c>, then a
/// sampled <c>DATATYPE()</c> — strongest first, and records the datatype IRI and its source. A variable with no
/// object predicate, an IRI-valued object, or no resolvable datatype stays
/// <see cref="DatatypeSource.Unknown"/>, and the other context fields pass through unchanged.
/// </summary>
[TestClass]
internal sealed class SparqlCompletionDatatypesTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdfs:range</c> IRI.</summary>
    private const string RdfsRange = "http://www.w3.org/2000/01/rdf-schema#range";

    /// <summary>The <c>sh:path</c> IRI.</summary>
    private const string ShaclPath = "http://www.w3.org/ns/shacl#path";

    /// <summary>The <c>sh:datatype</c> IRI.</summary>
    private const string ShaclDatatype = "http://www.w3.org/ns/shacl#datatype";

    /// <summary>The <c>xsd:integer</c> IRI.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The <c>xsd:string</c> IRI.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The <c>xsd:date</c> IRI.</summary>
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    /// <summary>The query whose object variable <c>?o</c> the resolver datatypes — bound by the constant predicate <c>:age</c> in the object position.</summary>
    private static string AgeQuery { get; } = $"SELECT ?o WHERE {{ ?s <{Ex}age> ?o }}";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>An IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>A typed literal term.</summary>
    /// <param name="lexical">The lexical value.</param>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal TypedLiteral(string lexical, string datatypeIri)
    {
        return new Literal(Utf8Strings.From(lexical), Iri(datatypeIri));
    }

    /// <summary>Opens an engine over <paramref name="data"/>, describes <paramref name="query"/> at its end, and resolves the datatypes — the full editor pipeline a completion request runs.</summary>
    /// <param name="data">The default-graph triples the engine serves.</param>
    /// <param name="query">The query whose caret-end completion context is resolved.</param>
    /// <returns>The resolved completion context.</returns>
    private async Task<CompletionContext> ResolveAsync(IEnumerable<DataTriple> data, string query)
    {
        VeritasEngine engine = await VeritasEngine
            .OpenAsync(data, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        byte[] bytes = Encoding.UTF8.GetBytes(query);
        CompletionContext described = SparqlCompletion.Describe(bytes, bytes.Length);

        return await SparqlCompletionDatatypes
            .ResolveAsync(engine, described, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The in-scope variable named <paramref name="name"/> (no leading <c>?</c>); fails the test when it is absent.</summary>
    /// <param name="context">The completion context to search.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The in-scope variable.</returns>
    private static ScopeVariable RequireVariable(CompletionContext context, string name)
    {
        SparqlVariable target = new(Utf8Strings.From(name));
        ScopeVariable? found = null;
        foreach(ScopeVariable scope in context.InScopeVariables)
        {
            if(scope.Variable == target)
            {
                found = scope;

                break;
            }
        }

        Assert.IsNotNull(found, $"Variable ?{name} is not in scope.");

        return found.Value;
    }

    /// <summary>A declared <c>rdfs:range</c> resolves the object variable to that datatype, sourced as the range.</summary>
    [TestMethod]
    public async Task RangeDeclarationResolvesToRdfsRange()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri($"{Ex}age"), Iri(RdfsRange), Iri(XsdInteger)),
        ];

        CompletionContext resolved = await ResolveAsync(data, AgeQuery).ConfigureAwait(false);

        ScopeVariable o = RequireVariable(resolved, "o");
        Assert.AreEqual(XsdInteger, o.Datatype?.ToString(), "The range declaration did not resolve the object datatype.");
        Assert.AreEqual(DatatypeSource.RdfsRange, o.DatatypeSource, "The datatype source should be the rdfs:range tier.");

        //The subject variable has no object-position predicate, so it stays unknown.
        ScopeVariable s = RequireVariable(resolved, "s");
        Assert.IsNull(s.Datatype, "A subject-only variable has no inferable literal datatype.");
        Assert.AreEqual(DatatypeSource.Unknown, s.DatatypeSource, "A subject-only variable stays unknown.");
    }

    /// <summary>With no range declared, a sampled object literal resolves the variable to that literal's datatype, sourced as the sample.</summary>
    [TestMethod]
    public async Task DataSampleResolvesWhenNoRangeDeclared()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri($"{Ex}s"), Iri($"{Ex}age"), TypedLiteral("30", XsdInteger)),
        ];

        CompletionContext resolved = await ResolveAsync(data, AgeQuery).ConfigureAwait(false);

        ScopeVariable o = RequireVariable(resolved, "o");
        Assert.AreEqual(XsdInteger, o.Datatype?.ToString(), "The sampled literal did not resolve the object datatype.");
        Assert.AreEqual(DatatypeSource.DataSample, o.DatatypeSource, "The datatype source should be the data-sample tier.");
    }

    /// <summary>A declared range outranks an observed sample of a different datatype.</summary>
    [TestMethod]
    public async Task RangeOutranksSample()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri($"{Ex}age"), Iri(RdfsRange), Iri(XsdDate)),
            new DataTriple(Iri($"{Ex}s"), Iri($"{Ex}age"), TypedLiteral("30", XsdInteger)),
        ];

        CompletionContext resolved = await ResolveAsync(data, AgeQuery).ConfigureAwait(false);

        ScopeVariable o = RequireVariable(resolved, "o");
        Assert.AreEqual(XsdDate, o.Datatype?.ToString(), "The declared range should win over the observed sample.");
        Assert.AreEqual(DatatypeSource.RdfsRange, o.DatatypeSource, "The stronger range source should be recorded.");
    }

    /// <summary>A SHACL property shape's <c>sh:datatype</c> outranks both a declared range and an observed sample.</summary>
    [TestMethod]
    public async Task ShaclDatatypeOutranksRangeAndSample()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri($"{Ex}ageShape"), Iri(ShaclPath), Iri($"{Ex}age")),
            new DataTriple(Iri($"{Ex}ageShape"), Iri(ShaclDatatype), Iri(XsdDate)),
            new DataTriple(Iri($"{Ex}age"), Iri(RdfsRange), Iri(XsdString)),
            new DataTriple(Iri($"{Ex}s"), Iri($"{Ex}age"), TypedLiteral("30", XsdInteger)),
        ];

        CompletionContext resolved = await ResolveAsync(data, AgeQuery).ConfigureAwait(false);

        ScopeVariable o = RequireVariable(resolved, "o");
        Assert.AreEqual(XsdDate, o.Datatype?.ToString(), "The SHACL sh:datatype should win over range and sample.");
        Assert.AreEqual(DatatypeSource.ShaclShape, o.DatatypeSource, "The strongest SHACL source should be recorded.");
    }

    /// <summary>An IRI-valued object yields no datatype — a miss, not a wrong answer — so the variable stays unknown.</summary>
    [TestMethod]
    public async Task IriValuedObjectStaysUnknown()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri($"{Ex}s"), Iri($"{Ex}homepage"), Iri($"{Ex}page")),
        ];

        CompletionContext resolved = await ResolveAsync(data, $"SELECT ?o WHERE {{ ?s <{Ex}homepage> ?o }}").ConfigureAwait(false);

        ScopeVariable o = RequireVariable(resolved, "o");
        Assert.IsNull(o.Datatype, "An IRI-valued object has no literal datatype to resolve.");
        Assert.AreEqual(DatatypeSource.Unknown, o.DatatypeSource, "An IRI-valued object stays unknown.");
    }

    /// <summary>Resolution fills only the datatypes; the expected tokens, enclosing productions, and variable→predicate pairs pass through unchanged.</summary>
    [TestMethod]
    public async Task ResolvePreservesTheOtherContextFields()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri($"{Ex}age"), Iri(RdfsRange), Iri(XsdInteger)),
        ];

        VeritasEngine engine = await VeritasEngine
            .OpenAsync(data, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        byte[] bytes = Encoding.UTF8.GetBytes(AgeQuery);
        CompletionContext described = SparqlCompletion.Describe(bytes, bytes.Length);
        CompletionContext resolved = await SparqlCompletionDatatypes
            .ResolveAsync(engine, described, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);

        //Resolution rebuilds the context with only InScopeVariables replaced; the other fields keep their identity.
        Assert.AreEqual(described.CaretByteOffset, resolved.CaretByteOffset, "The caret offset must pass through unchanged.");
        Assert.AreSame(described.ExpectedTokens, resolved.ExpectedTokens, "The expected tokens must pass through unchanged.");
        Assert.AreSame(described.EnclosingProductions, resolved.EnclosingProductions, "The enclosing productions must pass through unchanged.");
        Assert.AreSame(described.VariablePredicates, resolved.VariablePredicates, "The variable→predicate pairs must pass through unchanged.");
        Assert.HasCount(described.InScopeVariables.Count, resolved.InScopeVariables, "Resolution must not add or drop in-scope variables.");
    }
}
