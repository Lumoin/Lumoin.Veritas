using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The Cor-1 data-polarity admission surface: a subclass-position single-property
/// data existential or has-value lowers to its NNF dual — an empty-body
/// disjunctive clause whose head carries a non-value-forcing universal demand
/// marker over the complemented range beside the superclass — while the
/// subclass-position data universal and data cardinality stay fenced with their
/// named remainders. The survey admits exactly what the clausifier lowers (the
/// drift biconditional), the construct census qualifies every data-shape key by
/// polarity, and the positive superclass path is pinned unchanged. The engine
/// decision lane over these duals is a separate stage; these rows certify the
/// admission surface only.
/// </summary>
[TestClass]
internal sealed class ContextDataPolarityLoweringTests
{
    /// <summary>The example namespace the classes and data properties are drawn from.</summary>
    private const string Example = "http://example.org/";

    /// <summary>A subclass-position data existential lowers to the dual: one Universal-kind descriptor over the complemented range, carried on an empty-body two-literal disjunctive head beside the superclass, with no value-forcing companion.</summary>
    [TestMethod]
    public void DataSomeNegativeLowersToUniversalComplementDisjunct()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(DataSome("d", IntegerBelow(4)), Reference("C"))));

        Assert.IsEmpty(result.Remainder, "The lowered dual leaves no remainder.");
        Assert.AreEqual(1, result.NegativePolarityDataMarkers, "Exactly one negative-polarity dual is emitted.");
        Assert.HasCount(1, result.DataDemandDescriptors, "Exactly one demand descriptor is minted.");
        AssertSingleUniversalComplementDescriptor(result);
        AssertDualDisjunctClause(result);
    }

    /// <summary>A subclass-position data has-value lowers to the dual over the complemented one-of enumeration of its literal.</summary>
    [TestMethod]
    public void DataHasValueNegativeLowersToUniversalComplementOneOfDisjunct()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(DataHasValue("d", IntegerLiteral(3)), Reference("C"))));

        Assert.IsEmpty(result.Remainder, "The lowered dual leaves no remainder.");
        Assert.AreEqual(1, result.NegativePolarityDataMarkers, "Exactly one negative-polarity dual is emitted.");
        Assert.HasCount(1, result.DataDemandDescriptors, "Exactly one demand descriptor is minted.");
        foreach(DataDemandDescriptor descriptor in result.DataDemandDescriptors.Values)
        {
            Assert.AreEqual(DataDemandKind.Universal, descriptor.Kind, "The dual demand is a universal.");
            OwlDataComplementOf complement = (OwlDataComplementOf)descriptor.Range;
            Assert.IsInstanceOfType<OwlDataOneOf>(complement.Range, "The complemented range encloses the has-value literal's enumeration.");
        }

        AssertDualDisjunctClause(result);
    }

    /// <summary>The positive superclass path is unchanged by the widening: a superclass-position data existential still lowers to a value-forcing Existential-kind demand with an uncomplemented range and no dual counter movement.</summary>
    [TestMethod]
    public void DataSomePositiveStillLowersToExistentialValueForcingDemand()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(Reference("A"), DataSome("d", IntegerBelow(4)))));

        Assert.IsEmpty(result.Remainder, "The positive demand leaves no remainder.");
        Assert.AreEqual(0, result.NegativePolarityDataMarkers, "No negative-polarity dual is emitted on the positive path.");
        Assert.HasCount(1, result.DataDemandDescriptors, "Exactly one demand descriptor is minted.");
        foreach(DataDemandDescriptor descriptor in result.DataDemandDescriptors.Values)
        {
            Assert.AreEqual(DataDemandKind.Existential, descriptor.Kind, "The positive demand is an existential.");
            Assert.IsNotInstanceOfType<OwlDataComplementOf>(descriptor.Range, "The positive demand's range is not complemented.");
        }
    }

    /// <summary>A subclass-position data universal stays fenced: its NNF dual is a value-forcing existential disjunct, so the clausifier emits the named remainder.</summary>
    [TestMethod]
    public void DataAllNegativeFencedNamedRemainder()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(DataAll("d", IntegerBelow(4)), Reference("C"))));

        Assert.Contains(ContextRemainderNames.DataExpressionRejection(nameof(OwlDataAllValuesFrom), "subclass"), result.Remainder, "The subclass-position universal keeps its named rejection.");
    }

    /// <summary>A subclass-position data min-cardinality stays fenced: its negation is a max shape no demand kind represents, so the clausifier emits the named remainder.</summary>
    [TestMethod]
    public void DataMinCardinalityNegativeFencedNamedRemainder()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(DataMinCard(2, "d", Integer), Reference("C"))));

        Assert.Contains(ContextRemainderNames.DataExpressionRejection(nameof(OwlDataCardinality), "subclass"), result.Remainder, "The subclass-position min-cardinality keeps its named rejection.");
    }

    /// <summary>An n-ary data existential rejects at either polarity — the widening covers the single-property shape only.</summary>
    [TestMethod]
    public void DataSomeNaryRejectsEitherPolarity()
    {
        OwlDataSomeValuesFrom nary = new([new NamedNode(Iri("d")), new NamedNode(Iri("e"))], Integer);
        Assert.IsFalse(ContextModuleSurvey.Survey(Module(SubClassOf(nary, Reference("C")))).Admitted, "An n-ary data existential in subclass position is not survey-admitted.");
        Assert.IsFalse(ContextModuleSurvey.Survey(Module(SubClassOf(Reference("A"), nary))).Admitted, "An n-ary data existential in superclass position is not survey-admitted.");

        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(nary, Reference("C"))));
        Assert.Contains(ContextRemainderNames.DataExpressionRejection(nameof(OwlDataSomeValuesFrom), "subclass"), result.Remainder, "The n-ary subclass shape keeps its named rejection.");
    }

    /// <summary>A dual over a reserved data property is the named reserved-property rejection, exactly as on the positive path.</summary>
    [TestMethod]
    public void DataSomeNegativeReservedPropertyRejects()
    {
        OwlDataSomeValuesFrom reserved = new([new NamedNode(OwlVocabulary.TopDataProperty)], Integer);
        ClausificationResult result = ContextClausifier.Clausify(Module(SubClassOf(reserved, Reference("C"))));

        Assert.Contains(ContextRemainderNames.ReservedDataProperty(OwlVocabulary.TopDataProperty), result.Remainder, "The reserved data property keeps its named rejection on the dual path.");
    }

    /// <summary>Two subclass-position existentials over the same property and range share ONE structurally interned dual marker while each emits its own disjunctive clause.</summary>
    [TestMethod]
    public void DataSomeNegativeSharesMarkerAcrossSuperclasses()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(
            SubClassOf(DataSome("d", IntegerBelow(4)), Reference("C")),
            SubClassOf(DataSome("d", IntegerBelow(4)), Reference("E"))));

        Assert.IsEmpty(result.Remainder, "Both duals lower without remainder.");
        Assert.AreEqual(2, result.NegativePolarityDataMarkers, "Each subclass-position existential emits its own dual clause.");
        Assert.HasCount(1, result.DataDemandDescriptors, "The same complemented range interns to one shared marker.");
    }

    /// <summary>An equivalence over a data existential lowers BOTH directions: the forward positive existential demand and the backward universal dual over the complemented range — the load-bearing corpus shape.</summary>
    [TestMethod]
    public void EquivalentClassesWithDataExistentialLowersBothDirections()
    {
        ClausificationResult result = ContextClausifier.Clausify(Module(Equivalent(Reference("A"), DataSome("d", IntegerBelow(4)))));

        Assert.IsEmpty(result.Remainder, "The equivalence lowers whole.");
        Assert.AreEqual(1, result.NegativePolarityDataMarkers, "The backward direction emits exactly one dual.");
        Assert.HasCount(2, result.DataDemandDescriptors, "The forward existential and the backward universal dual mint distinct descriptors.");
        bool sawExistential = false;
        bool sawUniversalComplement = false;
        foreach(DataDemandDescriptor descriptor in result.DataDemandDescriptors.Values)
        {
            switch(descriptor.Kind)
            {
                case(DataDemandKind.Existential):
                {
                    sawExistential = true;

                    break;
                }
                case(DataDemandKind.Universal):
                {
                    sawUniversalComplement = descriptor.Range is OwlDataComplementOf;

                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        Assert.IsTrue(sawExistential, "The forward direction mints the positive existential demand.");
        Assert.IsTrue(sawUniversalComplement, "The backward direction mints the universal dual over the complemented range.");
    }

    /// <summary>The survey admits the two lowered negative shapes — the drift biconditional's admitting half.</summary>
    [TestMethod]
    public void SurveyAdmitsNegativeDataSomeAndHasValue()
    {
        Assert.IsTrue(ContextModuleSurvey.Survey(Module(SubClassOf(DataSome("d", IntegerBelow(4)), Reference("C")))).Admitted, "The subclass-position data existential is survey-admitted.");
        Assert.IsTrue(ContextModuleSurvey.Survey(Module(SubClassOf(DataHasValue("d", IntegerLiteral(3)), Reference("C")))).Admitted, "The subclass-position data has-value is survey-admitted.");
    }

    /// <summary>The survey rejects the two fenced negative shapes — the drift biconditional's rejecting half.</summary>
    [TestMethod]
    public void SurveyRejectsNegativeDataAllAndMinCardinality()
    {
        Assert.IsFalse(ContextModuleSurvey.Survey(Module(SubClassOf(DataAll("d", IntegerBelow(4)), Reference("C")))).Admitted, "The subclass-position data universal is not survey-admitted.");
        Assert.IsFalse(ContextModuleSurvey.Survey(Module(SubClassOf(DataMinCard(2, "d", Integer), Reference("C")))).Admitted, "The subclass-position data min-cardinality is not survey-admitted.");
    }

    /// <summary>The drift biconditional over the four negative-position data shapes: the survey admits a module exactly when the clausifier lowers it without a data rejection.</summary>
    [TestMethod]
    public void SurveyAdmitsExactlyWhatClausifierLowersNegativeData()
    {
        List<(string Row, OwlClassExpression Sub)> shapes =
        [
            ("existential", DataSome("d", IntegerBelow(4))),
            ("hasValue", DataHasValue("d", IntegerLiteral(3))),
            ("universal", DataAll("d", IntegerBelow(4))),
            ("minCardinality", DataMinCard(2, "d", Integer)),
        ];
        foreach((string row, OwlClassExpression sub) in shapes)
        {
            ReasoningModule module = Module(SubClassOf(sub, Reference("C")));
            bool admitted = ContextModuleSurvey.Survey(module).Admitted;
            bool lowered = !HasDataRejection(ContextClausifier.Clausify(module).Remainder);
            Assert.AreEqual(admitted, lowered, $"Survey and clausifier agree on the negative-position {row} shape.");
        }
    }

    /// <summary>The construct census qualifies a subclass-position data existential with the sub key and never the super key; the positive counterpart is symmetric.</summary>
    [TestMethod]
    public void CensusQualifiesDataShapesByPolarity()
    {
        IReadOnlyList<(string Key, int Count)> negative = OwlConstructCensus.Count(Module(SubClassOf(DataSome("d", IntegerBelow(4)), Reference("C"))));
        Assert.AreEqual(1, CountOf(negative, "DataSomeValuesFrom(sub)"), "The subclass-position existential censuses at sub polarity.");
        Assert.AreEqual(0, CountOf(negative, "DataSomeValuesFrom(super)"), "The subclass-position existential never censuses at super polarity.");

        IReadOnlyList<(string Key, int Count)> positive = OwlConstructCensus.Count(Module(SubClassOf(Reference("A"), DataSome("d", IntegerBelow(4)))));
        Assert.AreEqual(1, CountOf(positive, "DataSomeValuesFrom(super)"), "The superclass-position existential censuses at super polarity.");
        Assert.AreEqual(0, CountOf(positive, "DataSomeValuesFrom(sub)"), "The superclass-position existential never censuses at sub polarity.");

        IReadOnlyList<(string Key, int Count)> hasValue = OwlConstructCensus.Count(Module(SubClassOf(DataHasValue("d", IntegerLiteral(3)), Reference("C"))));
        Assert.AreEqual(1, CountOf(hasValue, "DataHasValue(sub)"), "The subclass-position has-value censuses at sub polarity.");

        IReadOnlyList<(string Key, int Count)> cardinality = OwlConstructCensus.Count(Module(SubClassOf(Reference("A"), DataMinCard(2, "d", Integer))));
        Assert.AreEqual(1, CountOf(cardinality, "DataCardinality(Min,n>=2,super)"), "The superclass-position min-cardinality censuses at super polarity with its bound bucket.");
    }

    /// <summary>An equivalence censuses its data restriction at BOTH polarities — the double-walk that explains a corpus's doubled data-shape counts.</summary>
    [TestMethod]
    public void CensusEquivalentClassesWalksDataAtBothPolarities()
    {
        IReadOnlyList<(string Key, int Count)> census = OwlConstructCensus.Count(Module(Equivalent(Reference("A"), DataSome("d", IntegerBelow(4)))));

        Assert.AreEqual(1, CountOf(census, "DataSomeValuesFrom(sub)"), "The equivalence walks the existential at sub polarity.");
        Assert.AreEqual(1, CountOf(census, "DataSomeValuesFrom(super)"), "The equivalence walks the existential at super polarity.");
    }

    /// <summary>Asserts the result's single descriptor is a non-value-forcing universal over a complemented range.</summary>
    /// <param name="result">The clausification result.</param>
    private static void AssertSingleUniversalComplementDescriptor(ClausificationResult result)
    {
        foreach(DataDemandDescriptor descriptor in result.DataDemandDescriptors.Values)
        {
            Assert.AreEqual(DataDemandKind.Universal, descriptor.Kind, "The dual demand is a universal.");
            Assert.IsInstanceOfType<OwlDataComplementOf>(descriptor.Range, "The dual demand's range is complemented.");
        }
    }

    /// <summary>Asserts the result carries exactly one empty-body clause whose head pairs a demand marker with one other concept literal — the dual disjunct shape.</summary>
    /// <param name="result">The clausification result.</param>
    private static void AssertDualDisjunctClause(ClausificationResult result)
    {
        int dualClauses = 0;
        foreach(DlClause clause in result.Clauses)
        {
            if(clause.Body.Length != 0 || clause.Head.Length != 2)
            {
                continue;
            }

            int markers = 0;
            foreach(DlLiteral literal in clause.Head)
            {
                if(literal.Kind == DlLiteralKind.Concept && result.DataDemandDescriptors.ContainsKey(literal.Symbol))
                {
                    markers++;
                }
            }

            if(markers == 1)
            {
                dualClauses++;
            }
        }

        Assert.AreEqual(1, dualClauses, "Exactly one empty-body two-literal head carries the dual marker beside the superclass.");
    }

    /// <summary>Whether the remainder carries a data-restriction rejection (a data-expression or reserved-data-property name).</summary>
    /// <param name="remainder">The clausification remainder.</param>
    /// <returns><see langword="true"/> when a data rejection is present.</returns>
    private static bool HasDataRejection(IReadOnlyList<string> remainder)
    {
        foreach(string name in remainder)
        {
            if(name.Contains("data", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The census count of a key, zero when absent.</summary>
    /// <param name="census">The censused key counts.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The count.</returns>
    private static int CountOf(IReadOnlyList<(string Key, int Count)> census, string key)
    {
        foreach((string candidate, int count) in census)
        {
            if(candidate == key)
            {
                return count;
            }
        }

        return 0;
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Iri(marker)), new NamedNode(Iri("p")), new NamedNode(Iri("o")), Graph: null);
    }

    /// <summary>A <c>SubClassOf</c> axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>An <c>EquivalentClasses</c> axiom over a pair.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>A single-property data existential (<c>DataSomeValuesFrom</c>).</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Iri(property))], range);
    }

    /// <summary>A single-property data universal (<c>DataAllValuesFrom</c>).</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The constraining range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataAllValuesFrom DataAll(string property, OwlDataRange range)
    {
        return new OwlDataAllValuesFrom([new NamedNode(Iri(property))], range);
    }

    /// <summary>A literal-value data restriction (<c>DataHasValue</c>).</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="value">The required literal value.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataHasValue DataHasValue(string property, Literal value)
    {
        return new OwlDataHasValue(new NamedNode(Iri(property)), value);
    }

    /// <summary>A positive data minimum-cardinality restriction (<c>DataMinCardinality</c>).</summary>
    /// <param name="count">The minimum count.</param>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The qualifying range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataMinCard(int count, string property, OwlDataRange range)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, count, new NamedNode(Iri(property)), range);
    }

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>An integer datatype restriction bounded strictly below the given value.</summary>
    /// <param name="bound">The exclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBelow(int bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxExclusive), IntegerLiteral(bound))]);
    }

    /// <summary>The named <c>xsd:integer</c> data range.</summary>
    private static OwlDatatypeReference Integer { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>A named-class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Reference(string local)
    {
        return new OwlClassReference(new NamedNode(Iri(local)));
    }

    /// <summary>The full IRI of an example-namespace local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string local)
    {
        return Utf8Strings.From(Example + local);
    }
}
