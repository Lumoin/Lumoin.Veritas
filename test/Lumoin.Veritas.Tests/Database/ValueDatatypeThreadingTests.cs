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
/// The end-to-end value-datatype composition rows:
/// a registry composed on <see cref="VeritasEngineOptions.ValueDatatypes"/> reaches SHACL <c>sh:datatype</c>
/// validation and SPARQL <c>=</c>/<c>!=</c> filtering through the opened database with
/// <see cref="VeritasEngineOptions.Reasoning"/> unwired — the value layer is independent of the reasoner —
/// and the <see cref="ValueDatatypeRegistry.Empty"/> default reproduces the pinned unregistered-datatype
/// baseline semantics exactly.
/// </summary>
[TestClass]
internal sealed class ValueDatatypeThreadingTests
{
    /// <summary>The example-namespace prefix the data, shapes, and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The registered custom datatype's IRI text, for shapes and query constants.</summary>
    private const string CelsiusIriText = Ex + "celsius";

    /// <summary>The registered custom datatype's IRI.</summary>
    private static Utf8String CelsiusIri { get; } = Utf8Strings.From(CelsiusIriText);

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>With reasoning unwired and a lexical-validity definition registered, the engine's SHACL validation consults it: the ill-formed reading now violates <c>sh:datatype</c> where IRI identity alone would conform, and the well-formed reading conforms.</summary>
    [TestMethod]
    public async Task ReasoningNullPopulatedRegistryStillValidatesThroughIt()
    {
        VeritasEngineOptions options = new() { Reasoning = null, ValueDatatypes = LexicalRegistry() };

        VeritasEngine illFormed = await VeritasEngine.OpenAsync(ReadingData("garbage"), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var illFormedScope = illFormed.ConfigureAwait(false);
        ValidationReport violated = await illFormed.ValidateAsync(ReadingShape(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(violated.Conforms, "The registered definition declares LexicalValidity and answers Invalid for the ill-formed reading, so sh:datatype must violate.");
        Assert.IsNotEmpty(violated.Results, "The lexical-validity violation is reported as a result.");

        VeritasEngine wellFormed = await VeritasEngine.OpenAsync(ReadingData("ok"), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var wellFormedScope = wellFormed.ConfigureAwait(false);
        ValidationReport conforming = await wellFormed.ValidateAsync(ReadingShape(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(conforming.Conforms, "The definition answers Valid for the well-formed reading, so the data conforms.");
    }

    /// <summary>With reasoning unwired and a value-equality definition registered, the engine's query path consults it: <c>=</c> over two lexically distinct literals of the registered datatype answers the definition's Same — where term identity alone would answer false — and <c>!=</c> answers its complement.</summary>
    [TestMethod]
    public async Task ReasoningNullPopulatedRegistryStillFiltersThroughIt()
    {
        VeritasEngineOptions options = new() { Reasoning = null, ValueDatatypes = EqualityRegistry() };

        VeritasEngine engine = await VeritasEngine.OpenAsync(ReadingData("1"), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool same = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s1> <{Ex}reading> ?v FILTER(?v = \"2\"^^<{CelsiusIriText}>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(same, "The always-Same definition decides = at the value layer; term identity alone would answer false.");

        bool distinct = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s1> <{Ex}reading> ?v FILTER(?v != \"2\"^^<{CelsiusIriText}>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(distinct, "A decided Same answers != as false through the same value-layer arm.");
    }

    /// <summary>Under the <see cref="ValueDatatypeRegistry.Empty"/> default the composition reproduces the unregistered-datatype baseline exactly: <c>=</c> over lexically distinct literals of the custom datatype is false (term identity, never an error), an identical term still matches, and <c>sh:datatype</c> conforms on IRI identity alone however ill-formed the lexical form.</summary>
    [TestMethod]
    public async Task EmptyDefaultReproducesTheUnregisteredDatatypeBaseline()
    {
        VeritasEngineOptions options = new() { Reasoning = null };

        VeritasEngine engine = await VeritasEngine.OpenAsync(ReadingData("garbage"), [], options, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool crossLexical = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s1> <{Ex}reading> ?v FILTER(?v = \"other\"^^<{CelsiusIriText}>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(crossLexical, "With nothing registered, = over lexically distinct literals of a custom datatype stays term identity: false, not an error.");

        bool identicalTerm = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s1> <{Ex}reading> ?v FILTER(?v = \"garbage\"^^<{CelsiusIriText}>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(identicalTerm, "The identical term still matches, so the filter path is live and the false above is a comparison verdict, not an evaluation error.");

        ValidationReport report = await engine.ValidateAsync(ReadingShape(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(report.Conforms, "With nothing registered, sh:datatype conforms on IRI identity alone however ill-formed the lexical form.");
    }

    /// <summary>The mutable lane threads the registry too: the per-query engine a mutable database derives consults the value-equality definition, and an update's <c>WHERE</c> filter decides through the same registry — so the value-layer marker lands.</summary>
    [TestMethod]
    public async Task MutableLaneThreadsTheRegistryThroughUpdateAndQuery()
    {
        VeritasEngineOptions options = new() { Reasoning = null, ValueDatatypes = EqualityRegistry() };

        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(ReadingData("1"), options, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool same = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s1> <{Ex}reading> ?v FILTER(?v = \"2\"^^<{CelsiusIriText}>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(same, "The mutable database's per-query engine consults the registered value-equality definition.");

        await engine.UpdateAsync(
            Utf8Strings.From($"INSERT {{ <{Ex}s1> <{Ex}hit> <{Ex}yes> }} WHERE {{ <{Ex}s1> <{Ex}reading> ?v FILTER(?v = \"2\"^^<{CelsiusIriText}>) }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        bool inserted = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}s1> <{Ex}hit> <{Ex}yes> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(inserted, "The update's WHERE filter decided = through the registry, so the marker landed.");
    }

    /// <summary>Builds a one-definition registry declaring <see cref="ValueDatatypeFacets.LexicalValidity"/>, asserting the registration is accepted.</summary>
    /// <returns>The registry.</returns>
    private static ValueDatatypeRegistry LexicalRegistry()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(new DelegateBackedValueDatatype(CelsiusIri, ValueDatatypeFacets.LexicalValidity, [], LexicalOracle)).Kind);

        return builder.Build();
    }

    /// <summary>Builds a one-definition registry declaring <see cref="ValueDatatypeFacets.ValueEquality"/>, asserting the registration is accepted.</summary>
    /// <returns>The registry.</returns>
    private static ValueDatatypeRegistry EqualityRegistry()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(new DelegateBackedValueDatatype(CelsiusIri, ValueDatatypeFacets.ValueEquality, [], AlwaysSameOracle)).Kind);

        return builder.Build();
    }

    /// <summary>Answers Valid for the single well-formed exemplar <c>ok</c> and Invalid for every other lexical form.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The lexical-validity answer.</returns>
    private static ValueDatatypeAnswer LexicalOracle(in ValueDatatypeQuestion question)
    {
        return ValueDatatypeAnswer.ForLexicalForm(question.First.Span.SequenceEqual("ok"u8) ? ValueLexicalValidity.Valid : ValueLexicalValidity.Invalid);
    }

    /// <summary>Answers Same for every pair — trivially lawful, and decidedly different from term identity.</summary>
    /// <param name="question">The folded question, unused.</param>
    /// <returns>The Same answer.</returns>
    private static ValueDatatypeAnswer AlwaysSameOracle(in ValueDatatypeQuestion question)
    {
        _ = question;

        return ValueDatatypeAnswer.ForSameValue(ValueIdentity.Same);
    }

    /// <summary>A typed sensor whose reading carries the custom datatype with the given lexical form.</summary>
    /// <param name="lexicalForm">The reading's lexical form.</param>
    /// <returns>The data triples.</returns>
    private static IReadOnlyList<DataTriple> ReadingData(string lexicalForm)
    {
        return
        [
            new DataTriple(Iri(Ex + "s1"), Iri(RdfType), Iri(Ex + "Sensor")),
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "reading"), new Literal(Utf8Strings.From(lexicalForm), Iri(CelsiusIriText))),
        ];
    }

    /// <summary>The SHACL property shape: every sensor's reading must carry the custom datatype.</summary>
    /// <returns>The shapes triples.</returns>
    private static IReadOnlyList<DataTriple> ReadingShape()
    {
        const string Sh = "http://www.w3.org/ns/shacl#";

        return
        [
            new DataTriple(Iri(Ex + "ReadingShape"), Iri(RdfType), Iri(Sh + "PropertyShape")),
            new DataTriple(Iri(Ex + "ReadingShape"), Iri(Sh + "targetClass"), Iri(Ex + "Sensor")),
            new DataTriple(Iri(Ex + "ReadingShape"), Iri(Sh + "path"), Iri(Ex + "reading")),
            new DataTriple(Iri(Ex + "ReadingShape"), Iri(Sh + "datatype"), Iri(CelsiusIriText)),
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
