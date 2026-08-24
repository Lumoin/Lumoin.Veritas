using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The end-to-end extension-function composition rows: a registry composed on
/// <see cref="VeritasEngineOptions.ExtensionFunctions"/> reaches SPARQL filtering through the opened
/// database on both the immutable and mutable lanes, the <see cref="SparqlFunctionRegistry.Empty"/> default
/// keeps every extension-function IRI erring, and <see cref="VeritasEngine.ValidateAsync"/> hands the same
/// registry — and the value-datatype registry — to the <c>sh:sparql</c> constraint engine, so a constraint
/// query answers exactly as the host's own queries would.
/// </summary>
[TestClass]
internal sealed class ExtensionFunctionThreadingTests
{
    /// <summary>The example-namespace prefix the data, shapes, and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The SHACL vocabulary prefix.</summary>
    private const string Sh = "http://www.w3.org/ns/shacl#";

    /// <summary>The registered predicate function's IRI text: answers <c>true</c> exactly for the lexical form <c>bob</c>.</summary>
    private const string IsBobFunctionText = Ex + "fn/isBob";

    /// <summary>The registered custom datatype's IRI text for the value-datatype constraint row.</summary>
    private const string CelsiusIriText = Ex + "celsius";

    /// <summary>The XSD boolean datatype IRI.</summary>
    private const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The immutable lane threads the registry: a registered predicate function decides a <c>FILTER</c> through <see cref="VeritasEngine.AskAsync"/>.</summary>
    [TestMethod]
    public async Task ImmutableLaneThreadsTheRegistryThroughAskAsync()
    {
        VeritasEngineOptions options = new() { Reasoning = null, ExtensionFunctions = FunctionRegistry() };

        VeritasEngine engine = await VeritasEngine.OpenAsync(NameData(), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool bob = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ ?s <{Ex}name> ?o FILTER(<{IsBobFunctionText}>(?o)) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(bob, "The registered predicate answers true for the bob row, so the filter keeps it and the ask answers true.");
    }

    /// <summary>Under the <see cref="SparqlFunctionRegistry.Empty"/> default the same IRI errs, the filter drops every row, and the ask answers false — while an unfiltered ask over the same data still answers true, so the false is the error semantics, not missing data.</summary>
    [TestMethod]
    public async Task EmptyDefaultKeepsExtensionFunctionIrisErring()
    {
        VeritasEngineOptions options = new() { Reasoning = null };

        VeritasEngine engine = await VeritasEngine.OpenAsync(NameData(), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool filtered = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ ?s <{Ex}name> ?o FILTER(<{IsBobFunctionText}>(?o)) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(filtered, "With nothing registered the extension function errs, the error condition drops every row, and the ask answers false.");

        bool unfiltered = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ ?s <{Ex}name> ?o }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(unfiltered, "The data is present, so the filtered false above is the error semantics at work.");
    }

    /// <summary>The mutable lane threads the registry too: the per-query engine consults the function, and an update's <c>WHERE</c> filter decides through the same registry — so the marker lands.</summary>
    [TestMethod]
    public async Task MutableLaneThreadsTheRegistryThroughUpdateAndQuery()
    {
        VeritasEngineOptions options = new() { Reasoning = null, ExtensionFunctions = FunctionRegistry() };

        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(NameData(), options, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool bob = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ ?s <{Ex}name> ?o FILTER(<{IsBobFunctionText}>(?o)) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(bob, "The mutable database's per-query engine consults the registered function.");

        await engine.UpdateAsync(
            Utf8Strings.From($"INSERT {{ ?s <{Ex}hit> <{Ex}yes> }} WHERE {{ ?s <{Ex}name> ?o FILTER(<{IsBobFunctionText}>(?o)) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        bool inserted = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s2> <{Ex}hit> <{Ex}yes> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(inserted, "The update's WHERE filter decided the function through the registry, so the marker landed on the bob subject.");
    }

    /// <summary>
    /// <see cref="VeritasEngine.ValidateAsync"/> hands the registry to the <c>sh:sparql</c> constraint engine: a
    /// constraint whose SELECT keeps rows through the registered predicate reports the bob sensor under the
    /// populated registry, and reports nothing under the empty default — where the function errs, the filter drops
    /// every row, and the constraint under-selects.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsyncThreadsTheRegistryIntoShSparqlConstraints()
    {
        VeritasEngineOptions populated = new() { Reasoning = null, ExtensionFunctions = FunctionRegistry() };
        VeritasEngine flagged = await VeritasEngine.OpenAsync(NameData(), [], populated, TestContext.CancellationToken).ConfigureAwait(false);
        await using var flaggedScope = flagged.ConfigureAwait(false);
        ValidationReport violated = await flagged.ValidateAsync(SparqlConstraintShape(FunctionSelect()), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(violated.Conforms, "The constraint's filter keeps the bob row through the registered function, so the sh:sparql constraint reports it.");
        Assert.IsNotEmpty(violated.Results);

        VeritasEngineOptions empty = new() { Reasoning = null };
        VeritasEngine silent = await VeritasEngine.OpenAsync(NameData(), [], empty, TestContext.CancellationToken).ConfigureAwait(false);
        await using var silentScope = silent.ConfigureAwait(false);
        ValidationReport conforming = await silent.ValidateAsync(SparqlConstraintShape(FunctionSelect()), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(conforming.Conforms, "Under the empty default the function errs inside the constraint query, so no row survives and the data conforms.");
    }

    /// <summary>
    /// The <c>sh:sparql</c> constraint engine carries the value-datatype registry too: a constraint filtering
    /// <c>=</c> over lexically distinct literals of a registered always-Same datatype reports the sensor under the
    /// populated registry — where term identity alone, the empty-default answer, reports nothing.
    /// </summary>
    [TestMethod]
    public async Task ShSparqlConstraintsConsultTheValueDatatypeRegistry()
    {
        VeritasEngineOptions populated = new() { Reasoning = null, ValueDatatypes = EqualityRegistry() };
        VeritasEngine flagged = await VeritasEngine.OpenAsync(ReadingData(), [], populated, TestContext.CancellationToken).ConfigureAwait(false);
        await using var flaggedScope = flagged.ConfigureAwait(false);
        ValidationReport violated = await flagged.ValidateAsync(SparqlConstraintShape(EqualitySelect()), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(violated.Conforms, "The always-Same definition decides = inside the constraint query, so the reading row survives the filter and the constraint reports it.");

        VeritasEngineOptions empty = new() { Reasoning = null };
        VeritasEngine silent = await VeritasEngine.OpenAsync(ReadingData(), [], empty, TestContext.CancellationToken).ConfigureAwait(false);
        await using var silentScope = silent.ConfigureAwait(false);
        ValidationReport conforming = await silent.ValidateAsync(SparqlConstraintShape(EqualitySelect()), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(conforming.Conforms, "Under the empty default the comparison is term identity, the lexically distinct constant matches nothing, and the data conforms.");
    }

    /// <summary>
    /// The <c>sh:sparql</c> constraint engine translates under the registry's declared aggregate profile:
    /// a constraint whose SELECT groups per focus node and holds each group through a registered
    /// extension aggregate reports every sensor under the populated registry, and reports nothing under
    /// the empty default — where the call stays a scalar error, <c>HAVING</c> drops every group, and the
    /// constraint under-selects.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsyncThreadsTheAggregateProfileIntoShSparqlConstraints()
    {
        VeritasEngineOptions populated = new() { Reasoning = null, ExtensionFunctions = AggregateRegistry() };
        VeritasEngine flagged = await VeritasEngine.OpenAsync(NameData(), [], populated, TestContext.CancellationToken).ConfigureAwait(false);
        await using var flaggedScope = flagged.ConfigureAwait(false);
        ValidationReport violated = await flagged.ValidateAsync(SparqlConstraintShape(AggregateSelect()), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(violated.Conforms, "The aggregate holds each one-name group through the declared profile, so the constraint reports the sensors.");
        Assert.IsNotEmpty(violated.Results);

        VeritasEngineOptions empty = new() { Reasoning = null };
        VeritasEngine silent = await VeritasEngine.OpenAsync(NameData(), [], empty, TestContext.CancellationToken).ConfigureAwait(false);
        await using var silentScope = silent.ConfigureAwait(false);
        ValidationReport conforming = await silent.ValidateAsync(SparqlConstraintShape(AggregateSelect()), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(conforming.Conforms, "Under the empty default no aggregate is declared, the call errs per row, and the data conforms.");
    }

    /// <summary>Builds the one-function registry: the bob predicate, asserting the registration is accepted.</summary>
    /// <returns>The registry.</returns>
    private static SparqlFunctionRegistry FunctionRegistry()
    {
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(IsBobFunctionText), IsBob).Kind);

        return builder.Build();
    }

    /// <summary>Builds a one-definition value-datatype registry declaring <see cref="ValueDatatypeFacets.ValueEquality"/> with an always-Same oracle, asserting the registration is accepted.</summary>
    /// <returns>The registry.</returns>
    private static ValueDatatypeRegistry EqualityRegistry()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(new DelegateBackedValueDatatype(Utf8Strings.From(CelsiusIriText), ValueDatatypeFacets.ValueEquality, [], AlwaysSameOracle)).Kind);

        return builder.Build();
    }

    /// <summary>Answers the boolean of whether the single argument is a literal with the lexical form <c>bob</c>; a different shape is an error.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The boolean literal, or the error value.</returns>
    private static SparqlFunctionResult IsBob(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || arguments[0] is not Literal literal)
        {
            return SparqlFunctionResult.Error;
        }

        bool bob = literal.Value.Span.SequenceEqual("bob"u8);

        return SparqlFunctionResult.Of(new Literal(Utf8Strings.From(bob ? "true" : "false"), Iri(XsdBoolean)));
    }

    /// <summary>Answers Same for every pair — trivially lawful, and decidedly different from term identity.</summary>
    /// <param name="question">The folded question, unused.</param>
    /// <returns>The Same answer.</returns>
    private static ValueDatatypeAnswer AlwaysSameOracle(in ValueDatatypeQuestion question)
    {
        _ = question;

        return ValueDatatypeAnswer.ForSameValue(ValueIdentity.Same);
    }

    /// <summary>The two-sensor name graph: <c>alice</c> and <c>bob</c> readings on typed sensors.</summary>
    /// <returns>The data triples.</returns>
    private static IReadOnlyList<DataTriple> NameData()
    {
        return
        [
            new DataTriple(Iri(Ex + "s1"), Iri(RdfType), Iri(Ex + "Sensor")),
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "name"), new Literal(Utf8Strings.From("alice"), Iri("http://www.w3.org/2001/XMLSchema#string"))),
            new DataTriple(Iri(Ex + "s2"), Iri(RdfType), Iri(Ex + "Sensor")),
            new DataTriple(Iri(Ex + "s2"), Iri(Ex + "name"), new Literal(Utf8Strings.From("bob"), Iri("http://www.w3.org/2001/XMLSchema#string"))),
        ];
    }

    /// <summary>A typed sensor whose reading carries the custom datatype with the lexical form <c>1</c>.</summary>
    /// <returns>The data triples.</returns>
    private static IReadOnlyList<DataTriple> ReadingData()
    {
        return
        [
            new DataTriple(Iri(Ex + "s1"), Iri(RdfType), Iri(Ex + "Sensor")),
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "reading"), new Literal(Utf8Strings.From("1"), Iri(CelsiusIriText))),
        ];
    }

    /// <summary>The constraint SELECT keeping name rows through the registered predicate function.</summary>
    /// <returns>The SELECT text.</returns>
    private static string FunctionSelect()
    {
        return $"SELECT $this WHERE {{ $this <{Ex}name> ?o FILTER(<{IsBobFunctionText}>(?o)) }}";
    }

    /// <summary>The registered extension aggregate's IRI text for the aggregate-profile constraint row.</summary>
    private const string CountAggregateText = Ex + "fn/aggCount";

    /// <summary>Builds the one-aggregate registry: the count fold, asserting the registration is accepted.</summary>
    /// <returns>The registry.</returns>
    private static SparqlFunctionRegistry AggregateRegistry()
    {
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(new SparqlFunctionEntry(Utf8Strings.From(CountAggregateText), Scalar: null, Aggregate: CountFold)).Kind);

        return builder.Build();
    }

    /// <summary>Folds a group to its value count as <c>xsd:integer</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The count literal.</returns>
    private static SparqlFunctionResult CountFold(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Of(new Literal(Utf8Strings.From(group.Values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), Iri("http://www.w3.org/2001/XMLSchema#integer")));
    }

    /// <summary>The constraint SELECT grouping per focus node and holding each group through the extension aggregate's answer.</summary>
    /// <returns>The SELECT text.</returns>
    private static string AggregateSelect()
    {
        return $"SELECT $this WHERE {{ $this <{Ex}name> ?o }} GROUP BY $this HAVING(<{CountAggregateText}>(?o) = 1)";
    }

    /// <summary>The constraint SELECT keeping reading rows through a value-layer <c>=</c> over a lexically distinct constant of the registered datatype.</summary>
    /// <returns>The SELECT text.</returns>
    private static string EqualitySelect()
    {
        return $"SELECT $this WHERE {{ $this <{Ex}reading> ?v FILTER(?v = \"2\"^^<{CelsiusIriText}>) }}";
    }

    /// <summary>The SHACL node shape carrying one <c>sh:sparql</c> constraint over the given SELECT, targeting the sensor class.</summary>
    /// <param name="select">The constraint's SELECT text.</param>
    /// <returns>The shapes triples.</returns>
    private static IReadOnlyList<DataTriple> SparqlConstraintShape(string select)
    {
        BlankNode constraint = new(Utf8Strings.From("sparqlConstraint"));

        return
        [
            new DataTriple(Iri(Ex + "SensorShape"), Iri(RdfType), Iri(Sh + "NodeShape")),
            new DataTriple(Iri(Ex + "SensorShape"), Iri(Sh + "targetClass"), Iri(Ex + "Sensor")),
            new DataTriple(Iri(Ex + "SensorShape"), Iri(Sh + "sparql"), constraint),
            new DataTriple(constraint, Iri(Sh + "select"), new Literal(Utf8Strings.From(select), Iri("http://www.w3.org/2001/XMLSchema#string"))),
        ];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }
}
