using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Rdf;

/// <summary>
/// The value-layer datatype registry's acceptance rule and abstention contract
/// for the value-datatype seam: the empty null object, acceptance of
/// an unreserved <c>geo:</c> IRI, the reserved-IRI union gate with its classifier-subset assertion, the
/// duplicate / facet-less / probe-budget rejections, the bounded equality-law check, the accumulated
/// builder outcomes, the delegate escape hatch's oracle routing, and the discovery face that enumerates
/// exactly the accepted IRIs.
/// </summary>
[TestClass]
internal sealed class ValueDatatypeRegistryTests
{
    /// <summary>The GeoSPARQL namespace of the unreserved exemplar IRIs.</summary>
    private const string GeoNamespace = "http://www.opengis.net/ont/geosparql#";

    /// <summary>The empty registry is empty and resolves nothing.</summary>
    [TestMethod]
    public void EmptyRegistryIsEmptyAndResolvesNothing()
    {
        Assert.IsTrue(ValueDatatypeRegistry.Empty.IsEmpty);
        Assert.IsFalse(ValueDatatypeRegistry.Empty.TryGet(GeoIri("wktLiteral"), out _));
    }

    /// <summary>An unreserved <c>geo:</c> IRI registers, and the built registry resolves it.</summary>
    [TestMethod]
    public void GeoIriRegistrationAccepted()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], PointPrefixValidityAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, outcome.Kind);
        ValueDatatypeRegistry registry = builder.Build();
        Assert.IsFalse(registry.IsEmpty);
        Assert.IsTrue(registry.TryGet(GeoIri("wktLiteral"), out ValueDatatype? found));
        Assert.IsTrue(found!.SelfCertified);
    }

    /// <summary>Every reserved class rejects with the reserved-IRI outcome kind: a classifier-modelled XSD type, an unmodelled XSD-namespace type, and the RDF-namespace datatypes.</summary>
    /// <param name="curie">The compact name of the reserved exemplar.</param>
    [TestMethod]
    [DataRow("xsd:integer")]
    [DataRow("xsd:dateTime")]
    [DataRow("xsd:string")]
    [DataRow("xsd:anyURI")]
    [DataRow("xsd:duration")]
    [DataRow("rdf:langString")]
    [DataRow("rdf:dirLangString")]
    public void ReservedIriRejected(string curie)
    {
        Utf8String iri = ResolveReservedExemplar(curie);
        Assert.IsTrue(ValueDatatypeReservations.IsReserved(iri), $"'{curie}' must be reserved.");

        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(iri, ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedReservedIri, outcome.Kind, $"'{curie}' must reject as a reserved IRI.");
        Assert.IsTrue(builder.Build().IsEmpty);
    }

    /// <summary>A second registration of the same IRI is rejected as a duplicate; the first stays registered.</summary>
    [TestMethod]
    public void DuplicateRegistrationRejected()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], PointPrefixValidityAnswer)).Kind);

        ValueDatatypeRegistration second = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedDuplicate, second.Kind);
        Assert.IsTrue(builder.Build().TryGet(GeoIri("wktLiteral"), out _));
    }

    /// <summary>A definition declaring no facet is rejected — nothing could ever consult it.</summary>
    [TestMethod]
    public void FacetlessRegistrationRejected()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.None, [], AbstainAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedFacetless, outcome.Kind);
        Assert.IsTrue(builder.Build().IsEmpty);
    }

    /// <summary>A definition declaring more probes than the law-check budget is rejected with the typed outcome, never silently clamped.</summary>
    [TestMethod]
    public void ProbeBudgetExceededRejected()
    {
        List<Utf8String> probes = new(ValueDatatypeLaws.ProbeBudget + 1);
        for(int i = 0; i <= ValueDatatypeLaws.ProbeBudget; i++)
        {
            probes.Add(Utf8Strings.From("p" + i));
        }

        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.ValueEquality, probes, ByteEqualityAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedProbeBudgetExceeded, outcome.Kind);
        Assert.IsTrue(builder.Build().IsEmpty);
    }

    /// <summary>An equality that decides a probe distinct from itself is rejected as non-reflexive.</summary>
    [TestMethod]
    public void NonReflexiveEqualityRejected()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.ValueEquality, [Utf8Strings.From("a")], ConstantDistinctAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedLawViolation, outcome.Kind);
        Assert.IsTrue(outcome.Violation.HasValue, "A law-driven rejection carries the typed violation.");
        Assert.AreEqual(ValueDatatypeLaw.Reflexivity, outcome.Violation!.Value.Law);
        Assert.AreEqual(0, outcome.Violation.Value.FirstProbeIndex);
    }

    /// <summary>An equality that answers same one way and distinct the other is rejected as non-symmetric.</summary>
    [TestMethod]
    public void NonSymmetricEqualityRejected()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.ValueEquality, [Utf8Strings.From("a"), Utf8Strings.From("b")], AsymmetricAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedLawViolation, outcome.Kind);
        Assert.IsTrue(outcome.Violation.HasValue, "A law-driven rejection carries the typed violation.");
        Assert.AreEqual(ValueDatatypeLaw.Symmetry, outcome.Violation!.Value.Law);
        Assert.AreEqual(0, outcome.Violation.Value.FirstProbeIndex);
        Assert.AreEqual(1, outcome.Violation.Value.SecondProbeIndex);
    }

    /// <summary>An equality whose same-verdicts compose to a decided-distinct pair is rejected as non-transitive.</summary>
    [TestMethod]
    public void NonTransitiveEqualityRejected()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.ValueEquality, [Utf8Strings.From("a"), Utf8Strings.From("b"), Utf8Strings.From("c")], NonTransitiveAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedLawViolation, outcome.Kind);
        Assert.IsTrue(outcome.Violation.HasValue, "A law-driven rejection carries the typed violation.");
        Assert.AreEqual(ValueDatatypeLaw.Transitivity, outcome.Violation!.Value.Law);
        Assert.AreEqual(0, outcome.Violation.Value.FirstProbeIndex);
        Assert.AreEqual(1, outcome.Violation.Value.SecondProbeIndex);
        Assert.AreEqual(2, outcome.Violation.Value.ThirdProbeIndex);
    }

    /// <summary>A decisive byte-equality oracle passes every law over probes that exercise same and distinct compositions, and registers.</summary>
    [TestMethod]
    public void LawfulDecisiveEqualityAccepted()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.ValueEquality, [Utf8Strings.From("a"), Utf8Strings.From("b"), Utf8Strings.From("a")], ByteEqualityAnswer));

        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, outcome.Kind);
        Assert.IsTrue(builder.Build().TryGet(GeoIri("wktLiteral"), out _));
    }

    /// <summary>The subset assertion: every key of the value-space classifier is reserved, and an unmodelled <c>geo:</c> IRI is neither classified nor reserved.</summary>
    [TestMethod]
    public void ClassifierKeysAreAllReserved()
    {
        Utf8String[] classifierKeys =
        [
            Vocabulary.Xsd.Integer,
            Vocabulary.Xsd.Decimal,
            Vocabulary.Xsd.Float,
            Vocabulary.Xsd.Double,
            Vocabulary.Xsd.Long,
            Vocabulary.Xsd.Int,
            Vocabulary.Xsd.Short,
            Vocabulary.Xsd.ByteValue,
            Vocabulary.Xsd.UnsignedLong,
            Vocabulary.Xsd.UnsignedInt,
            Vocabulary.Xsd.UnsignedShort,
            Vocabulary.Xsd.UnsignedByte,
            Vocabulary.Xsd.NonNegativeInteger,
            Vocabulary.Xsd.NonPositiveInteger,
            Vocabulary.Xsd.PositiveInteger,
            Vocabulary.Xsd.NegativeInteger,
            Vocabulary.Xsd.String,
            Vocabulary.Xsd.Boolean,
            Vocabulary.Xsd.DateTime,
            Vocabulary.Xsd.DateTimeStamp,
            Vocabulary.Xsd.Date,
            Vocabulary.Xsd.Time,
            Vocabulary.Xsd.Duration,
            Vocabulary.Xsd.YearMonthDuration,
            Vocabulary.Xsd.DayTimeDuration,
        ];

        foreach(Utf8String key in classifierKeys)
        {
            Assert.AreNotEqual(ValueSpace.Unknown, ValueSpaceClassifier.Classify(key), $"'{key}' must be a classifier key for this row to pin it.");
            Assert.IsTrue(ValueDatatypeReservations.IsReserved(key), $"Classifier key '{key}' must be reserved.");
        }

        Assert.AreEqual(ValueSpace.Unknown, ValueSpaceClassifier.Classify(GeoIri("wktLiteral")));
        Assert.IsFalse(ValueDatatypeReservations.IsReserved(GeoIri("wktLiteral")));
    }

    /// <summary>Abstention at ordinal zero: a defaulted folded answer abstains on both slots — pinning both enums' zero defaults — and each single-slot factory leaves the other slot abstaining.</summary>
    [TestMethod]
    public void AbstentionIsTheZeroDefault()
    {
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, default(ValueDatatypeAnswer).Validity);
        Assert.AreEqual(ValueIdentity.Indeterminate, default(ValueDatatypeAnswer).Identity);
        Assert.AreEqual(ValueIdentity.Indeterminate, ValueDatatypeAnswer.ForLexicalForm(ValueLexicalValidity.Valid).Identity);
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, ValueDatatypeAnswer.ForSameValue(ValueIdentity.Same).Validity);
    }

    /// <summary>The delegate escape hatch routes both operations through its oracle: decided validity verdicts and a sound identity abstention.</summary>
    [TestMethod]
    public void DelegateBackedRoutesThroughOracle()
    {
        ValueDatatypeRegistryBuilder builder = new();
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], PointPrefixValidityAnswer)).Kind);

        Assert.IsTrue(builder.Build().TryGet(GeoIri("wktLiteral"), out ValueDatatype? found));
        Assert.AreEqual(ValueLexicalValidity.Valid, found!.ValidateLexicalForm(Utf8Strings.From("POINT(1 2)")));
        Assert.AreEqual(ValueLexicalValidity.Invalid, found.ValidateLexicalForm(Utf8Strings.From("not a geometry")));
        Assert.AreEqual(ValueIdentity.Indeterminate, found.SameValue(Utf8Strings.From("POINT(1 2)"), Utf8Strings.From("POINT(1.0 2.0)")));
    }

    /// <summary>The builder accumulates every registration outcome in attempt order, declined ones included.</summary>
    [TestMethod]
    public void BuilderOutcomesAccumulateEveryAttempt()
    {
        ValueDatatypeRegistryBuilder builder = new();
        builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], PointPrefixValidityAnswer));
        builder.Add(new DelegateBackedValueDatatype(Vocabulary.Xsd.Integer, ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));
        builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));

        Assert.HasCount(3, builder.Outcomes);
        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, builder.Outcomes[0].Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedReservedIri, builder.Outcomes[1].Kind);
        Assert.AreEqual(ValueDatatypeRegistrationKind.RejectedDuplicate, builder.Outcomes[2].Kind);
    }

    /// <summary>The discovery face enumerates exactly the accepted IRIs — membership pinned under an ordinal sort, so the frozen set's own order is never asserted.</summary>
    [TestMethod]
    public void DatatypeIrisEnumerateEveryAcceptedIri()
    {
        ValueDatatypeRegistryBuilder builder = new();
        builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], PointPrefixValidityAnswer));
        builder.Add(new DelegateBackedValueDatatype(GeoIri("gmlLiteral"), ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));

        List<string> enumerated = SortedDatatypeIris(builder.Build());

        Assert.HasCount(2, enumerated);
        Assert.AreEqual(GeoIri("gmlLiteral").ToString(), enumerated[0]);
        Assert.AreEqual(GeoIri("wktLiteral").ToString(), enumerated[1]);
    }

    /// <summary>The empty registry's discovery face enumerates nothing.</summary>
    [TestMethod]
    public void EmptyRegistryEnumeratesNoDatatypeIri()
    {
        Assert.IsEmpty(ValueDatatypeRegistry.Empty.DatatypeIris);
    }

    /// <summary>A declined registration never reaches the discovery face: the reserved IRI, the duplicate, and the facet-less definition are all absent, and the one accepted IRI stands alone.</summary>
    [TestMethod]
    public void DeclinedRegistrationsNeverAppearInDatatypeIris()
    {
        ValueDatatypeRegistryBuilder builder = new();
        builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], PointPrefixValidityAnswer));
        builder.Add(new DelegateBackedValueDatatype(Vocabulary.Xsd.Integer, ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));
        builder.Add(new DelegateBackedValueDatatype(GeoIri("wktLiteral"), ValueDatatypeFacets.LexicalValidity, [], AbstainAnswer));
        builder.Add(new DelegateBackedValueDatatype(GeoIri("kmlLiteral"), ValueDatatypeFacets.None, [], AbstainAnswer));

        List<string> enumerated = SortedDatatypeIris(builder.Build());

        Assert.HasCount(1, enumerated);
        Assert.AreEqual(GeoIri("wktLiteral").ToString(), enumerated[0]);
    }

    /// <summary>The registry's enumerated datatype IRIs as text, ordinally sorted so a row pins membership rather than the frozen set's own order.</summary>
    /// <param name="registry">The registry to enumerate.</param>
    /// <returns>The sorted IRIs.</returns>
    private static List<string> SortedDatatypeIris(ValueDatatypeRegistry registry)
    {
        List<string> iris = new(registry.DatatypeIris.Count);
        foreach(Utf8String iri in registry.DatatypeIris)
        {
            iris.Add(iri.ToString());
        }

        iris.Sort(StringComparer.Ordinal);

        return iris;
    }

    /// <summary>An IRI in the GeoSPARQL namespace.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String GeoIri(string localName)
    {
        return Utf8Strings.From(GeoNamespace + localName);
    }

    /// <summary>Resolves a reserved-exemplar compact name to its vocabulary constant, so the rows cannot drift from the constants.</summary>
    /// <param name="curie">The compact name.</param>
    /// <returns>The reserved IRI.</returns>
    private static Utf8String ResolveReservedExemplar(string curie)
    {
        return curie switch
        {
            "xsd:integer" => Vocabulary.Xsd.Integer,
            "xsd:dateTime" => Vocabulary.Xsd.DateTime,
            "xsd:string" => Vocabulary.Xsd.String,
            "xsd:anyURI" => Vocabulary.Xsd.AnyUri,
            "xsd:duration" => Vocabulary.Xsd.Duration,
            "rdf:langString" => Vocabulary.Rdf.LangString,
            "rdf:dirLangString" => Vocabulary.Rdf.DirLangString,
            _ => throw new ArgumentException($"Unknown reserved exemplar '{curie}'.", nameof(curie))
        };
    }

    /// <summary>Answers abstention for every operation.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The abstaining answer.</returns>
    private static ValueDatatypeAnswer AbstainAnswer(in ValueDatatypeQuestion question)
    {
        return default;
    }

    /// <summary>Answers validity by a <c>POINT</c> prefix and abstains on identity.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The folded answer.</returns>
    private static ValueDatatypeAnswer PointPrefixValidityAnswer(in ValueDatatypeQuestion question)
    {
        return question.Operation switch
        {
            ValueDatatypeOperation.ValidateLexicalForm => ValueDatatypeAnswer.ForLexicalForm(
                question.First.Span.StartsWith("POINT"u8) ? ValueLexicalValidity.Valid : ValueLexicalValidity.Invalid),
            _ => default
        };
    }

    /// <summary>Answers distinct for every identity question — non-reflexive by construction.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The folded answer.</returns>
    private static ValueDatatypeAnswer ConstantDistinctAnswer(in ValueDatatypeQuestion question)
    {
        return question.Operation switch
        {
            ValueDatatypeOperation.SameValue => ValueDatatypeAnswer.ForSameValue(ValueIdentity.Distinct),
            _ => default
        };
    }

    /// <summary>Answers same when the first operand's bytes are not after the second's, distinct otherwise — reflexive but non-symmetric.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The folded answer.</returns>
    private static ValueDatatypeAnswer AsymmetricAnswer(in ValueDatatypeQuestion question)
    {
        return question.Operation switch
        {
            ValueDatatypeOperation.SameValue => ValueDatatypeAnswer.ForSameValue(
                question.First.Span.SequenceCompareTo(question.Second.Span) <= 0 ? ValueIdentity.Same : ValueIdentity.Distinct),
            _ => default
        };
    }

    /// <summary>Answers distinct for the <c>{a, c}</c> pair and same for everything else — reflexive and symmetric but non-transitive over the probes <c>a</c>, <c>b</c>, <c>c</c>.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The folded answer.</returns>
    private static ValueDatatypeAnswer NonTransitiveAnswer(in ValueDatatypeQuestion question)
    {
        if(question.Operation != ValueDatatypeOperation.SameValue)
        {
            return default;
        }

        bool acPair = (question.First.Span.SequenceEqual("a"u8) && question.Second.Span.SequenceEqual("c"u8))
            || (question.First.Span.SequenceEqual("c"u8) && question.Second.Span.SequenceEqual("a"u8));

        return ValueDatatypeAnswer.ForSameValue(acPair ? ValueIdentity.Distinct : ValueIdentity.Same);
    }

    /// <summary>Answers identity by byte equality — a lawful decisive equality.</summary>
    /// <param name="question">The folded question.</param>
    /// <returns>The folded answer.</returns>
    private static ValueDatatypeAnswer ByteEqualityAnswer(in ValueDatatypeQuestion question)
    {
        return question.Operation switch
        {
            ValueDatatypeOperation.SameValue => ValueDatatypeAnswer.ForSameValue(
                question.First.Span.SequenceEqual(question.Second.Span) ? ValueIdentity.Same : ValueIdentity.Distinct),
            _ => default
        };
    }
}
