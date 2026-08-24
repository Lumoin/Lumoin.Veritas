using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Pins the extension-function registry's carry through <see cref="SparqlExpressionContext"/>'s per-query
/// derivation: <see cref="SparqlExpressionContext.WithBaseIri"/> must hand the same frozen registry to the
/// derived context — a silent drop to the <see cref="SparqlFunctionRegistry.Empty"/> default would disable
/// every extension function for exactly the queries that declare a <c>BASE</c>, invisibly.
/// </summary>
[TestClass]
internal sealed class SparqlExpressionContextExtensionFunctionsTests
{
    /// <summary>The registered exemplar's function IRI.</summary>
    private static Utf8String ExampleFunctionIri { get; } = Utf8Strings.From("http://example.org/fn/marker");

    /// <summary>The construction carry and the <c>WithBaseIri</c> carry: the registry given to the constructor is the derived context's registry, the same frozen instance, while the base IRI actually changed.</summary>
    [TestMethod]
    public void WithBaseIriCarriesTheExtensionFunctionRegistryThrough()
    {
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(ExampleFunctionIri, AlwaysError).Kind);
        SparqlFunctionRegistry registry = builder.Build();

        SparqlExpressionContext context = new(VeritasRandomness.System, StubHash, new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero), extensionFunctions: registry);
        Assert.AreSame(registry, context.ExtensionFunctions);

        SparqlExpressionContext derived = context.WithBaseIri(Utf8Strings.From("http://example.org/base/"));

        Assert.AreSame(registry, derived.ExtensionFunctions);
        Assert.AreEqual("http://example.org/base/", derived.BaseIri?.ToString());
    }

    /// <summary>The <see cref="SparqlExpressionContext.CreateDefault"/> factory carries the registry too, and defaults to the frozen empty singleton when none is given.</summary>
    [TestMethod]
    public void CreateDefaultCarriesTheRegistryAndDefaultsToEmpty()
    {
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(ExampleFunctionIri, AlwaysError).Kind);
        SparqlFunctionRegistry registry = builder.Build();

        SparqlExpressionContext populated = SparqlExpressionContext.CreateDefault(extensionFunctions: registry);
        Assert.AreSame(registry, populated.ExtensionFunctions);

        SparqlExpressionContext defaulted = SparqlExpressionContext.CreateDefault();
        Assert.AreSame(SparqlFunctionRegistry.Empty, defaulted.ExtensionFunctions, "The dark default is the frozen empty singleton, so the no-op posture is one reference comparison.");
    }

    /// <summary>An always-erring implementation; the rows assert the carry, never an invocation.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments, unused.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The error value.</returns>
    private static SparqlFunctionResult AlwaysError(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Error;
    }

    /// <summary>A digest stub for the required hash seam; the rows never digest.</summary>
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
