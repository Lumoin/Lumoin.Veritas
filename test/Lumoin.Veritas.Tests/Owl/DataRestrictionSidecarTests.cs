using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The data-property sidecar over a <see cref="DataPropertyBox"/>: the §1.3
/// per-node decision procedure of the SROIQ datatype-sidecar
/// battery. Each row builds the node's data demands as an <see cref="AlcConcept"/>
/// list and a box from the module's data-property axioms, then asserts the
/// certified verdict — a datatype clash (unsatisfiable node), a decisive
/// consistent node, or a sound abstention where the sidecar leaves the verdict
/// fragment-relative. The empty-box parity pin fixes that a call carrying no
/// RBox is byte-identical to the original property-in-isolation entry point.
/// </summary>
[TestClass]
internal sealed class DataRestrictionSidecarTests
{
    /// <summary>The example namespace the data properties are drawn from.</summary>
    private const string Example = "http://example.org/";

    /// <summary>R09: a functional property with two overlapping integer ranges shares a value, so the node is consistent.</summary>
    [TestMethod]
    public void FunctionalPropertyWithOverlappingRangesIsConsistent()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", IntegerAtLeast(5)), Some("d", IntegerAtMost(10))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R10: a functional property forces one value into two disjoint integer ranges, so the node clashes.</summary>
    [TestMethod]
    public void FunctionalPropertyWithDisjointRangesClashes()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", IntegerAbove(5)), Some("d", IntegerBelow(3))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R11: without functionality the same two demands take different values under the open-world assumption, so the node is consistent.</summary>
    [TestMethod]
    public void TwoExistentialsWithoutFunctionalityAreConsistent()
    {
        DataPropertyBox box = Box();
        List<AlcConcept> demands = [Some("d", IntegerAbove(5)), Some("d", IntegerBelow(3))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R12: a functional property cannot carry two distinct values, so a minimum cardinality of two clashes.</summary>
    [TestMethod]
    public void FunctionalPropertyWithMinCardinalityTwoClashes()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [MinCard(2, "d", Integer)];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R13: functionality pools a demand on a property and one on its functional super-property into one value across disjoint ranges, so the node clashes.</summary>
    [TestMethod]
    public void FunctionalPoolingViaSubPropertyClashes()
    {
        DataPropertyBox box = Box(Functional("f"), Sub("d", "f"));
        List<AlcConcept> demands = [Some("d", IntegerAbove(5)), Some("f", IntegerBelow(3))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R14: two has-value demands whose literals denote the same integer (5 and 05) agree, so the functional node is consistent.</summary>
    [TestMethod]
    public void FunctionalPropertyWithSameValuedLiteralsIsConsistent()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", OneOf(Lit("5", Vocabulary.Xsd.Integer))), Some("d", OneOf(Lit("05", Vocabulary.Xsd.Integer)))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R15: two has-value demands with distinct literals cannot both hold of a functional property's single value, so the node clashes.</summary>
    [TestMethod]
    public void FunctionalPropertyWithDistinctHasValuesClashes()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", OneOf(Lit("5", Vocabulary.Xsd.Integer))), Some("d", OneOf(Lit("7", Vocabulary.Xsd.Integer)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R16: a functional existential constrained by a same-property universal still admits a value above the bound, so the node is consistent.</summary>
    [TestMethod]
    public void FunctionalExistentialUnderUniversalIsConsistent()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", Integer), All("d", IntegerAbove(10))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R17: an existential whose range is disjoint from the same-property universal clashes without any functionality.</summary>
    [TestMethod]
    public void ExistentialDisjointFromUniversalClashes()
    {
        DataPropertyBox box = Box();
        List<AlcConcept> demands = [Some("d", IntegerBelow(5)), All("d", IntegerAbove(10))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R18: a functional property pooling a string demand and an integer demand crosses disjoint families, so the node clashes.</summary>
    [TestMethod]
    public void FunctionalPropertyAcrossDisjointFamiliesClashes()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", DatatypeRange(Vocabulary.Xsd.String)), Some("d", Integer)];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R19: a vacuous minimum cardinality of zero forces no value, so it never joins a functional pool and the node stays consistent.</summary>
    [TestMethod]
    public void FunctionalPoolExcludesVacuousMinCardinalityZero()
    {
        DataPropertyBox box = Box(Functional("d"));
        List<AlcConcept> demands = [Some("d", Integer), MinCard(0, "d", DatatypeRange(Vocabulary.Xsd.String))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R20: two has-value demands forcing the same value into a disjoint property pair clash (the point-Same rule).</summary>
    [TestMethod]
    public void DisjointPairWithSamePointValueClashes()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"));
        List<AlcConcept> demands = [Some("a", OneOf(Lit("5", Vocabulary.Xsd.Integer))), Some("b", OneOf(Lit("5", Vocabulary.Xsd.Integer)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R21: two has-value demands with distinct values across a disjoint pair co-exist, so the node is consistent (the point-Distinct rule).</summary>
    [TestMethod]
    public void DisjointPairWithDistinctPointValuesIsConsistent()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"));
        List<AlcConcept> demands = [Some("a", OneOf(Lit("5", Vocabulary.Xsd.Integer))), Some("b", OneOf(Lit("7", Vocabulary.Xsd.Integer)))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R22: a single property below both members of a disjoint pair forces one value into both, so a demand on it clashes (the common-subproperty rule).</summary>
    [TestMethod]
    public void CommonSubPropertyOfDisjointPairClashes()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"), Sub("d", "a"), Sub("d", "b"));
        List<AlcConcept> demands = [Some("d", Integer)];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R23: equivalent properties that are also disjoint reduce to a self-disjoint property, so any value-forcing demand clashes (the equivalence-closure rule).</summary>
    [TestMethod]
    public void EquivalentAndDisjointPropertiesClash()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"), Equivalent("a", "b"));
        List<AlcConcept> demands = [Some("a", Integer)];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R24: two unconstrained existentials across a disjoint pair take different integer values, so value-choice freedom keeps the node consistent.</summary>
    [TestMethod]
    public void DisjointPairWithFreeValueChoiceIsConsistent()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"));
        List<AlcConcept> demands = [Some("a", Integer), Some("b", Integer)];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R25: a functional super-property pools demands from both members of a disjoint pair into one shared value, so the node clashes (the functional-forced rule).</summary>
    [TestMethod]
    public void FunctionalForcedSharedValueAcrossDisjointPairClashes()
    {
        DataPropertyBox box = Box(Functional("f"), Sub("a", "f"), Sub("b", "f"), Disjoint("a", "b"));
        List<AlcConcept> demands = [Some("a", Integer), Some("b", Integer)];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R26: a super-property's asserted range constrains a sub-property's demand into an empty conjunction, so the node clashes (range inheritance).</summary>
    [TestMethod]
    public void SubPropertyDemandUnderSuperRangeClashes()
    {
        DataPropertyBox box = Box(Sub("d", "e"), Range("e", IntegerAbove(10)));
        List<AlcConcept> demands = [Some("d", IntegerBelow(5))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R27: an asserted range on an unrelated property does not constrain the demand, so the node is consistent (control).</summary>
    [TestMethod]
    public void RangeOnUnrelatedPropertyDoesNotConstrain()
    {
        DataPropertyBox box = Box(Range("e", IntegerAbove(10)));
        List<AlcConcept> demands = [Some("d", IntegerBelow(5))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>R28: an equivalent property's asserted range flows across the equivalence and empties the demand, so the node clashes.</summary>
    [TestMethod]
    public void EquivalentPropertyRangeConstrainsAndClashes()
    {
        DataPropertyBox box = Box(Equivalent("d", "e"), Range("e", IntegerAbove(10)));
        List<AlcConcept> demands = [Some("d", IntegerBelow(5))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>
    /// R29: a disjoint pair whose two demands are each a degenerate single-point
    /// interval canonicalizes both to the same point enumeration, so the
    /// point-vs-point value identity forces the disjoint clash decisively
    /// (the canonicalization-layer flip of the former abstention).
    /// </summary>
    [TestMethod]
    public void DisjointPairForcedToDegeneratePointClashes()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"));
        List<AlcConcept> demands = [Some("a", IntegerPoint(5)), Some("b", IntegerPoint(5))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>
    /// R46: a functional property pools two existentials each of which survives the
    /// node universal alone, but the pooled conjunction — both existentials AND the
    /// universal together — is empty: the functional-pool clash is DRIVEN BY the
    /// universal, not by the existentials in isolation. The exact-core fix
    /// (construct-review F1): the conflict core must contain the driving
    /// <see cref="AlcDataAll"/> universal together with both pooled demands, never
    /// just the demands, so a clause learned from it does not over-forbid a
    /// combination the universal alone would have permitted.
    /// </summary>
    [TestMethod]
    public void FunctionalPoolClashDrivenByUniversalIncludesUniversalInCore()
    {
        DataPropertyBox box = Box(Functional("d"));
        AlcDataSome below = Some("d", IntegerBelow(5));
        AlcDataSome above = Some("d", IntegerAbove(2));
        AlcDataAll universal = All("d", OneOf(Lit("1", Vocabulary.Xsd.Integer), Lit("6", Vocabulary.Xsd.Integer)));
        List<AlcConcept> demands = [below, above, universal];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out IReadOnlyList<AlcConcept> conflict));
        Assert.Contains(universal, conflict, "The exact-core fix: the conflict core must contain the driving node universal.");
        Assert.Contains(below, conflict, "The pooled existential below five is in the conflict core.");
        Assert.Contains(above, conflict, "The pooled existential above two is in the conflict core.");
    }

    /// <summary>
    /// The empty-box parity pin: a representative set of demand lists through the
    /// original property-in-isolation entry point and the box-carrying overload
    /// with the empty box yield identical verdicts and identical conflict cores,
    /// on both the single-node and the forest surfaces.
    /// </summary>
    [TestMethod]
    public void EmptyBoxIsByteIdenticalToTheOriginalEntryPoints()
    {
        List<AlcConcept> consistentNode = [Some("d", IntegerAbove(5)), Some("d", IntegerBelow(3))];
        List<AlcConcept> clashNode = [Some("d", IntegerBelow(5)), All("d", IntegerAbove(10))];
        List<AlcConcept> plainNode = [Some("d", Integer)];
        List<AlcConcept> undecidedNode = [Some("d", Pattern())];

        AssertParity(consistentNode);
        AssertParity(clashNode);
        AssertParity(plainNode);
        AssertParity(undecidedNode);

        List<List<AlcConcept>> forest = [clashNode, plainNode];
        Assert.AreEqual(DataRestrictionConsistency.DecideForest(forest, DatatypeRegistry.Empty), DataRestrictionConsistency.DecideForest(forest, DataPropertyBox.Empty, DatatypeRegistry.Empty));

        List<List<AlcConcept>> consistentForest = [plainNode, consistentNode];
        Assert.AreEqual(DataRestrictionConsistency.DecideForest(consistentForest, DatatypeRegistry.Empty), DataRestrictionConsistency.DecideForest(consistentForest, DataPropertyBox.Empty, DatatypeRegistry.Empty));
    }

    //Cor-1 negative-polarity dual rows: the NNF dual of a subclass-position data
    //existential is a universal over the complemented range, so refuting that dual
    //against a forced existential IS the range-containment decision the widened
    //data tier rides on. The bounds are the measured GALEN-Heart pairs.

    /// <summary>The self-pairing refutation, the load-bearing GALEN-Heart case: an existential and the same-property universal over the COMPLEMENT of the same range cannot share a value, so the dual marker a data-bearing equivalence emits refutes against its own forward demand.</summary>
    [TestMethod]
    public void SelfPairingExistentialAndItsComplementUniversalClash()
    {
        List<AlcConcept> demands = [Some("d", IntegerBelow(4)), All("d", Complement(IntegerBelow(4)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>The measured GALEN-Heart containment pair <c>(-∞,4) ⊆ (-∞,5)</c>: every value below four is below five, so the existential below four clashes with the universal excluding below-five.</summary>
    [TestMethod]
    public void GalenContainmentPairLessThanFourWithinLessThanFiveClashes()
    {
        List<AlcConcept> demands = [Some("d", IntegerBelow(4)), All("d", Complement(IntegerBelow(5)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>The reverse direction of the containment pair is NOT a containment (<c>(-∞,5) ⊄ (-∞,4)</c>): the value four witnesses below-five outside below-four, so the pair stays consistent — the dual survives and is certified, never refuted.</summary>
    [TestMethod]
    public void GalenContainmentReverseDirectionStaysConsistent()
    {
        List<AlcConcept> demands = [Some("d", IntegerBelow(5)), All("d", Complement(IntegerBelow(4)))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>The measured GALEN-Heart containment pair <c>[1,3] ⊆ (-∞,4)</c>: a closed interval inside an open ray.</summary>
    [TestMethod]
    public void GalenContainmentPairClosedIntervalWithinOpenRayClashes()
    {
        List<AlcConcept> demands = [Some("d", IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 3))), All("d", Complement(IntegerBelow(4)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>The measured GALEN-Heart containment pair <c>[200,∞) ⊆ [10,∞)</c>: a ray inside a wider ray.</summary>
    [TestMethod]
    public void GalenContainmentPairRayWithinRayClashes()
    {
        List<AlcConcept> demands = [Some("d", IntegerAtLeast(200)), All("d", Complement(IntegerAtLeast(10)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>The measured GALEN-Heart containment pair <c>[40,55] ⊆ [39,59]</c>: a closed interval inside a wider closed interval.</summary>
    [TestMethod]
    public void GalenContainmentPairClosedWithinClosedClashes()
    {
        List<AlcConcept> demands = [Some("d", IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 40), (Vocabulary.XsdFacets.MaxInclusive, 55))), All("d", Complement(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 39), (Vocabulary.XsdFacets.MaxInclusive, 59))))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>The measured GALEN-Heart base-mismatch pair: an <c>xsd:integer</c> ray at least five sits inside the <c>owl:real</c> ray at least five, decided through the shared numeric-tower normalization.</summary>
    [TestMethod]
    public void GalenContainmentPairIntegerBaseWithinRealBaseClashes()
    {
        List<AlcConcept> demands = [Some("d", IntegerAtLeast(5)), All("d", Complement(RealAtLeast(5)))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out _));
    }

    /// <summary>Asserts the original and empty-box entry points agree on both the verdict and the conflict core for one demand list.</summary>
    /// <param name="demands">The node's demands.</param>
    private static void AssertParity(List<AlcConcept> demands)
    {
        DataConsistencyStatus original = DataRestrictionConsistency.Decide(demands, DatatypeRegistry.Empty, out IReadOnlyList<AlcConcept> originalConflict);
        DataConsistencyStatus withEmptyBox = DataRestrictionConsistency.Decide(demands, DataPropertyBox.Empty, DatatypeRegistry.Empty, out IReadOnlyList<AlcConcept> emptyBoxConflict);

        Assert.AreEqual(original, withEmptyBox);
        Assert.HasCount(originalConflict.Count, emptyBoxConflict);
        for(int index = 0; index < originalConflict.Count; index++)
        {
            Assert.AreEqual(originalConflict[index], emptyBoxConflict[index]);
        }
    }

    /// <summary>The named integer datatype range.</summary>
    private static OwlDatatypeReference Integer => DatatypeRange(Vocabulary.Xsd.Integer);

    /// <summary>A string pattern restriction the checker leaves undecided.</summary>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction Pattern()
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.String), [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.Pattern), Lit("[0-9]+", Vocabulary.Xsd.String))]);
    }

    /// <summary>The full IRI of an example-namespace local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string local)
    {
        return Utf8Strings.From(Example + local);
    }

    /// <summary>An existential data demand on a property.</summary>
    /// <param name="property">The property local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The concept.</returns>
    private static AlcDataSome Some(string property, OwlDataRange range)
    {
        return new AlcDataSome(Iri(property), range);
    }

    /// <summary>A universal data constraint on a property.</summary>
    /// <param name="property">The property local name.</param>
    /// <param name="range">The constraining range.</param>
    /// <returns>The concept.</returns>
    private static AlcDataAll All(string property, OwlDataRange range)
    {
        return new AlcDataAll(Iri(property), range);
    }

    /// <summary>A minimum-cardinality data demand on a property.</summary>
    /// <param name="count">The minimum count.</param>
    /// <param name="property">The property local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The concept.</returns>
    private static AlcDataMinCard MinCard(int count, string property, OwlDataRange range)
    {
        return new AlcDataMinCard(count, Iri(property), range);
    }

    /// <summary>A typed literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal Lit(string lexical, Utf8String datatypeIri)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(datatypeIri));
    }

    /// <summary>A named-datatype data range.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeReference DatatypeRange(Utf8String datatypeIri)
    {
        return new OwlDatatypeReference(new NamedNode(datatypeIri));
    }

    /// <summary>An enumeration data range.</summary>
    /// <param name="literals">The enumerated literals.</param>
    /// <returns>The data range.</returns>
    private static OwlDataOneOf OneOf(params Literal[] literals)
    {
        return new OwlDataOneOf(literals);
    }

    /// <summary>An integer range bounded below inclusively.</summary>
    /// <param name="bound">The inclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtLeast(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, bound));
    }

    /// <summary>An integer range bounded above inclusively.</summary>
    /// <param name="bound">The inclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtMost(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MaxInclusive, bound));
    }

    /// <summary>An integer range bounded below exclusively.</summary>
    /// <param name="bound">The exclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAbove(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinExclusive, bound));
    }

    /// <summary>An integer range bounded above exclusively.</summary>
    /// <param name="bound">The exclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBelow(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MaxExclusive, bound));
    }

    /// <summary>A degenerate single-point integer interval — a both-inclusive bound at one value.</summary>
    /// <param name="value">The single admitted value.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerPoint(int value)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, value), (Vocabulary.XsdFacets.MaxInclusive, value));
    }

    /// <summary>An integer datatype restriction over the given facet bounds.</summary>
    /// <param name="bounds">The facet–bound pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerRestriction(params (Utf8String Facet, int Bound)[] bounds)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((Utf8String facet, int bound) in bounds)
        {
            facets.Add(new OwlFacetRestriction(new NamedNode(facet), Lit(bound.ToString(System.Globalization.CultureInfo.InvariantCulture), Vocabulary.Xsd.Integer)));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }

    /// <summary>An <c>owl:real</c> datatype restriction bounded at least the given value — the numeric-tower sibling of <see cref="IntegerAtLeast"/>.</summary>
    /// <param name="bound">The inclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction RealAtLeast(int bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(Lumoin.Veritas.Owl.OwlVocabulary.Real), [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), Lit(bound.ToString(System.Globalization.CultureInfo.InvariantCulture), Vocabulary.Xsd.Integer))]);
    }

    /// <summary>The complement of a data range (<c>DataComplementOf</c>).</summary>
    /// <param name="range">The complemented range.</param>
    /// <returns>The data range.</returns>
    private static OwlDataComplementOf Complement(OwlDataRange range)
    {
        return new OwlDataComplementOf(range);
    }

    /// <summary>Builds a data-property box from the given axioms.</summary>
    /// <param name="axioms">The data-property axioms.</param>
    /// <returns>The box.</returns>
    private static DataPropertyBox Box(params OwlAxiom[] axioms)
    {
        return DataPropertyBox.Build(axioms);
    }

    /// <summary>A placeholder origin quad for a constructed axiom.</summary>
    /// <returns>The origin quad.</returns>
    private static Quad Origin()
    {
        return new Quad(new NamedNode(Iri("s")), new NamedNode(Iri("p")), new NamedNode(Iri("o")), Graph: null);
    }

    /// <summary>A <c>SubDataPropertyOf</c> axiom.</summary>
    /// <param name="sub">The sub-property local name.</param>
    /// <param name="super">The super-property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubDataPropertyOfAxiom Sub(string sub, string super)
    {
        return new OwlSubDataPropertyOfAxiom(new NamedNode(Iri(sub)), new NamedNode(Iri(super))) { Origin = Origin() };
    }

    /// <summary>An <c>EquivalentDataProperties</c> axiom over a pair.</summary>
    /// <param name="first">The first property local name.</param>
    /// <param name="second">The second property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentDataPropertiesAxiom Equivalent(string first, string second)
    {
        return new OwlEquivalentDataPropertiesAxiom(new NamedNode(Iri(first)), new NamedNode(Iri(second))) { Origin = Origin() };
    }

    /// <summary>A <c>FunctionalDataProperty</c> axiom.</summary>
    /// <param name="property">The property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlFunctionalDataPropertyAxiom Functional(string property)
    {
        return new OwlFunctionalDataPropertyAxiom(new NamedNode(Iri(property))) { Origin = Origin() };
    }

    /// <summary>A <c>DisjointDataProperties</c> axiom.</summary>
    /// <param name="properties">The mutually disjoint property local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointDataPropertiesAxiom Disjoint(params string[] properties)
    {
        List<NamedNode> operands = [];
        foreach(string property in properties)
        {
            operands.Add(new NamedNode(Iri(property)));
        }

        return new OwlDisjointDataPropertiesAxiom(operands) { Origin = Origin() };
    }

    /// <summary>A <c>DataPropertyRange</c> axiom.</summary>
    /// <param name="property">The property local name.</param>
    /// <param name="range">The asserted range.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyRangeAxiom Range(string property, OwlDataRange range)
    {
        return new OwlDataPropertyRangeAxiom(new NamedNode(Iri(property)), range) { Origin = Origin() };
    }
}
