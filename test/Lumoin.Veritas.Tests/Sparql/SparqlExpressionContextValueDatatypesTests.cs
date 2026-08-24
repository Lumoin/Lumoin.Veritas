using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Pins the value-datatype registry's carry through <see cref="SparqlExpressionContext"/>'s per-query
/// derivation:
/// <see cref="SparqlExpressionContext.WithBaseIri"/> must hand the same frozen registry to the derived
/// context — a silent drop to the <see cref="ValueDatatypeRegistry.Empty"/> default would disable every
/// value-layer consult for exactly the queries that declare a <c>BASE</c>, invisibly.
/// </summary>
[TestClass]
internal sealed class SparqlExpressionContextValueDatatypesTests
{
    /// <summary>The registered exemplar's datatype IRI.</summary>
    private static Utf8String WktLiteralIri { get; } = Utf8Strings.From("http://www.opengis.net/ont/geosparql#wktLiteral");

    /// <summary>The construction carry and the <c>WithBaseIri</c> carry: the registry given to the constructor is the derived context's registry, the same frozen instance, while the base IRI actually changed.</summary>
    [TestMethod]
    public void WithBaseIriCarriesTheValueDatatypeRegistryThrough()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(new DelegateBackedValueDatatype(WktLiteralIri, ValueDatatypeFacets.ValueEquality, [], AbstainAnswer)).Kind);
        ValueDatatypeRegistry registry = builder.Build();

        SparqlExpressionContext context = new(VeritasRandomness.System, StubHash, new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero), valueDatatypes: registry);
        Assert.AreSame(registry, context.ValueDatatypes);

        SparqlExpressionContext derived = context.WithBaseIri(Utf8Strings.From("http://example.org/base/"));

        Assert.AreSame(registry, derived.ValueDatatypes);
        Assert.AreEqual("http://example.org/base/", derived.BaseIri?.ToString());
    }

    /// <summary>An always-abstaining oracle; the row asserts the carry, never a verdict.</summary>
    /// <param name="question">The folded question, unused.</param>
    /// <returns>The abstaining answer.</returns>
    private static ValueDatatypeAnswer AbstainAnswer(in ValueDatatypeQuestion question)
    {
        _ = question;

        return ValueDatatypeAnswer.ForSameValue(ValueIdentity.Indeterminate);
    }

    /// <summary>A digest stub for the required hash seam; the row never digests.</summary>
    /// <param name="algorithm">The algorithm, unused.</param>
    /// <param name="data">The bytes, unused.</param>
    /// <returns>An empty digest.</returns>
    private static byte[] StubHash(SparqlHashAlgorithm algorithm, ReadOnlySpan<byte> data)
    {
        _ = algorithm;
        _ = data;

        return [];
    }
}
