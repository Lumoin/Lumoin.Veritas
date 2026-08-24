using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// The extension-function registry's acceptance rule and frozen-set semantics: the empty singleton answers
/// no IRI; a non-reserved IRI registers and resolves to its implementation; the whole XSD namespace is
/// reserved — the present constructor-cast IRIs and every future one alike — so the evaluator's built-in
/// cast semantics stay authoritative structurally; duplicates and the empty IRI decline with typed
/// outcomes; and the builder records every attempt in order, declined ones included.
/// </summary>
[TestClass]
internal sealed class SparqlFunctionRegistryTests
{
    /// <summary>A registrable extension-function IRI.</summary>
    private static Utf8String ExtensionIri { get; } = Utf8Strings.From("http://example.org/fn/one");

    /// <summary>A second registrable extension-function IRI.</summary>
    private static Utf8String OtherExtensionIri { get; } = Utf8Strings.From("http://example.org/fn/two");

    /// <summary>The empty registry is empty and answers no lookup.</summary>
    [TestMethod]
    public void EmptyRegistryIsEmptyAndAnswersNoIri()
    {
        Assert.IsTrue(SparqlFunctionRegistry.Empty.IsEmpty);
        Assert.IsFalse(SparqlFunctionRegistry.Empty.TryGet(ExtensionIri, out _));
    }

    /// <summary>A non-reserved IRI registers, and the built registry resolves it to the same delegate instance.</summary>
    [TestMethod]
    public void BuilderAcceptsAnExtensionIriAndTheBuiltRegistryResolvesIt()
    {
        SparqlFunctionRegistryBuilder builder = new();
        SparqlFunctionDelegate implementation = Marker;

        SparqlFunctionRegistration outcome = builder.Add(ExtensionIri, implementation);

        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, outcome.Kind);
        Assert.AreEqual(ExtensionIri, outcome.FunctionIri);

        SparqlFunctionRegistry registry = builder.Build();
        Assert.IsFalse(registry.IsEmpty);
        Assert.IsTrue(registry.TryGet(ExtensionIri, out SparqlFunctionDelegate? resolved));
        Assert.AreSame(implementation, resolved);
    }

    /// <summary>Every XSD constructor-cast IRI the evaluator answers itself declines with the reserved-IRI outcome.</summary>
    /// <param name="castIri">The reserved cast IRI.</param>
    [TestMethod]
    [DataRow("http://www.w3.org/2001/XMLSchema#integer")]
    [DataRow("http://www.w3.org/2001/XMLSchema#decimal")]
    [DataRow("http://www.w3.org/2001/XMLSchema#float")]
    [DataRow("http://www.w3.org/2001/XMLSchema#double")]
    [DataRow("http://www.w3.org/2001/XMLSchema#boolean")]
    [DataRow("http://www.w3.org/2001/XMLSchema#string")]
    public void BuilderRejectsEveryXsdConstructorCastIri(string castIri)
    {
        SparqlFunctionRegistryBuilder builder = new();

        SparqlFunctionRegistration outcome = builder.Add(Utf8Strings.From(castIri), Marker);

        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedReservedIri, outcome.Kind);
        Assert.IsTrue(builder.Build().IsEmpty, "A declined registration never enters the built set.");
    }

    /// <summary>The reservation covers the whole XSD namespace, not merely today's cast set: an XSD IRI the evaluator does not cast yet still declines, so a future built-in cast can never be shadowed by an earlier registration.</summary>
    [TestMethod]
    public void BuilderRejectsAnyXsdNamespaceIriBeyondTheCastSet()
    {
        SparqlFunctionRegistryBuilder builder = new();

        SparqlFunctionRegistration outcome = builder.Add(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#dateTime"), Marker);

        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedReservedIri, outcome.Kind);
    }

    /// <summary>A second registration of the same IRI declines with the duplicate outcome and leaves the first standing.</summary>
    [TestMethod]
    public void BuilderRejectsADuplicateRegistration()
    {
        SparqlFunctionRegistryBuilder builder = new();
        SparqlFunctionDelegate first = Marker;
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(ExtensionIri, first).Kind);

        SparqlFunctionRegistration outcome = builder.Add(ExtensionIri, OtherMarker);

        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedDuplicate, outcome.Kind);
        Assert.IsTrue(builder.Build().TryGet(ExtensionIri, out SparqlFunctionDelegate? resolved));
        Assert.AreSame(first, resolved, "The declined duplicate never displaces the accepted registration.");
    }

    /// <summary>The empty IRI declines with its own typed outcome — no call expression could ever name it.</summary>
    [TestMethod]
    public void BuilderRejectsTheEmptyIri()
    {
        SparqlFunctionRegistryBuilder builder = new();

        SparqlFunctionRegistration outcome = builder.Add(Utf8Strings.From(string.Empty), Marker);

        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedEmptyIri, outcome.Kind);
    }

    /// <summary>The builder records every attempt in order, accepted and declined alike, so a bulk composition site can audit the whole registration after the fact.</summary>
    [TestMethod]
    public void OutcomesRecordEveryAttemptInOrder()
    {
        SparqlFunctionRegistryBuilder builder = new();
        builder.Add(ExtensionIri, Marker);
        builder.Add(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"), Marker);
        builder.Add(ExtensionIri, OtherMarker);
        builder.Add(OtherExtensionIri, OtherMarker);

        Assert.HasCount(4, builder.Outcomes);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Outcomes[0].Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedReservedIri, builder.Outcomes[1].Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedDuplicate, builder.Outcomes[2].Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Outcomes[3].Kind);
    }

    /// <summary>The build freezes exactly the accepted set: the accepted IRIs resolve, the declined one is absent.</summary>
    [TestMethod]
    public void BuildFreezesOnlyAcceptedRegistrations()
    {
        SparqlFunctionRegistryBuilder builder = new();
        builder.Add(ExtensionIri, Marker);
        builder.Add(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"), Marker);
        builder.Add(OtherExtensionIri, OtherMarker);

        SparqlFunctionRegistry registry = builder.Build();

        Assert.IsTrue(registry.TryGet(ExtensionIri, out _));
        Assert.IsTrue(registry.TryGet(OtherExtensionIri, out _));
        Assert.IsFalse(registry.TryGet(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"), out _));
    }

    /// <summary>The default result struct is the error value, so no uninitialized instance can smuggle a term into evaluation.</summary>
    [TestMethod]
    public void DefaultFunctionResultIsTheErrorValue()
    {
        SparqlFunctionResult defaulted = default;

        Assert.IsTrue(defaulted.IsError);
        Assert.IsTrue(SparqlFunctionResult.Error.IsError);
    }

    /// <summary>The empty registry declares no aggregate IRIs and answers no aggregate lookup.</summary>
    [TestMethod]
    public void EmptyRegistryHasNoAggregateIris()
    {
        Assert.IsEmpty(SparqlFunctionRegistry.Empty.AggregateIris);
        Assert.IsFalse(SparqlFunctionRegistry.Empty.TryGetAggregate(ExtensionIri, out _));
    }

    /// <summary>An aggregate-only entry registers, resolves on exactly its aggregate face, and enters the declared aggregate-IRI profile.</summary>
    [TestMethod]
    public void BuilderAcceptsAnAggregateOnlyEntryOnItsOwnFace()
    {
        SparqlFunctionRegistryBuilder builder = new();
        SparqlAggregateDelegate implementation = AggregateMarker;

        SparqlFunctionRegistration outcome = builder.Add(new SparqlFunctionEntry(ExtensionIri, Scalar: null, Aggregate: implementation));

        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, outcome.Kind);

        SparqlFunctionRegistry registry = builder.Build();
        Assert.IsFalse(registry.IsEmpty);
        Assert.IsTrue(registry.TryGetAggregate(ExtensionIri, out SparqlAggregateDelegate? resolved));
        Assert.AreSame(implementation, resolved);
        Assert.IsFalse(registry.TryGet(ExtensionIri, out _), "An aggregate-only entry has no scalar face to resolve.");
        Assert.Contains(ExtensionIri, registry.AggregateIris);
    }

    /// <summary>A both-faces entry registers once and each face resolves to its own delegate.</summary>
    [TestMethod]
    public void BuilderAcceptsABothFacesEntryAndEachFaceResolves()
    {
        SparqlFunctionRegistryBuilder builder = new();
        SparqlFunctionDelegate scalar = Marker;
        SparqlAggregateDelegate aggregate = AggregateMarker;

        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(new SparqlFunctionEntry(ExtensionIri, scalar, aggregate)).Kind);

        SparqlFunctionRegistry registry = builder.Build();
        Assert.IsTrue(registry.TryGet(ExtensionIri, out SparqlFunctionDelegate? resolvedScalar));
        Assert.AreSame(scalar, resolvedScalar);
        Assert.IsTrue(registry.TryGetAggregate(ExtensionIri, out SparqlAggregateDelegate? resolvedAggregate));
        Assert.AreSame(aggregate, resolvedAggregate);
    }

    /// <summary>The duplicate rule is IRI-level across faces: an aggregate-only entry for a scalar-registered IRI declines whole, leaving the IRI without an aggregate face.</summary>
    [TestMethod]
    public void BuilderRejectsADuplicateAcrossFaces()
    {
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(ExtensionIri, Marker).Kind);

        SparqlFunctionRegistration outcome = builder.Add(new SparqlFunctionEntry(ExtensionIri, Scalar: null, Aggregate: AggregateMarker));

        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedDuplicate, outcome.Kind);
        SparqlFunctionRegistry registry = builder.Build();
        Assert.IsTrue(registry.TryGet(ExtensionIri, out _));
        Assert.IsFalse(registry.TryGetAggregate(ExtensionIri, out _));
        Assert.DoesNotContain(ExtensionIri, registry.AggregateIris);
    }

    /// <summary>The reservation gate covers aggregate registrations: an XSD-namespace aggregate-only entry declines reserved.</summary>
    [TestMethod]
    public void BuilderRejectsAReservedAggregateRegistration()
    {
        SparqlFunctionRegistryBuilder builder = new();

        SparqlFunctionRegistration outcome = builder.Add(new SparqlFunctionEntry(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"), Scalar: null, Aggregate: AggregateMarker));

        Assert.AreEqual(SparqlFunctionRegistrationKind.RejectedReservedIri, outcome.Kind);
        Assert.IsEmpty(builder.Build().AggregateIris);
    }

    /// <summary>An entry carrying neither face throws loudly at Add, before any admission — the registration can never silently vanish.</summary>
    [TestMethod]
    public void BuilderThrowsOnAFacelessEntry()
    {
        SparqlFunctionRegistryBuilder builder = new();

        Assert.Throws<ArgumentNullException>(() => builder.Add(default(SparqlFunctionEntry)));
        Assert.IsEmpty(builder.Outcomes);
    }

    /// <summary>The declared aggregate profile names exactly the aggregate-faced IRIs, never the scalar-only ones.</summary>
    [TestMethod]
    public void AggregateIrisNamesExactlyTheAggregateFaces()
    {
        SparqlFunctionRegistryBuilder builder = new();
        builder.Add(ExtensionIri, Marker);
        builder.Add(new SparqlFunctionEntry(OtherExtensionIri, Scalar: null, Aggregate: AggregateMarker));

        SparqlFunctionRegistry registry = builder.Build();

        Assert.HasCount(1, registry.AggregateIris);
        Assert.Contains(OtherExtensionIri, registry.AggregateIris);
    }

    /// <summary>A marker implementation; the rows assert registration outcomes, never an invocation.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments, unused.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The error value.</returns>
    private static SparqlFunctionResult Marker(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Error;
    }

    /// <summary>A second, distinct marker implementation for the duplicate-displacement assertion.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments, unused.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The error value.</returns>
    private static SparqlFunctionResult OtherMarker(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Error;
    }

    /// <summary>A marker aggregate implementation; the rows assert registration outcomes, never a fold.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's values, unused.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The error value.</returns>
    private static SparqlFunctionResult AggregateMarker(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Error;
    }
}
