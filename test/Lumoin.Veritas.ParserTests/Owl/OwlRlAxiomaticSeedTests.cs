using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Pins the axiomatic vocabulary seed table of the RL/RDF closure: the
/// rows are entailed by the empty graph under the OWL 2 RDF-Based
/// Semantics, so an empty-input closure derives every one of them — and
/// nothing of the <c>owl:Class</c>/<c>rdfs:Class</c> metaclass merge,
/// which seeds only under
/// <see cref="OwlAxiomaticVocabulary.MetaclassMerged"/> and never reaches
/// the default table or the maintained engine.
/// </summary>
[TestClass]
internal sealed class OwlRlAxiomaticSeedTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The nine built-in annotation properties are typed owl:AnnotationProperty from the empty graph.</summary>
    [TestMethod]
    public void EmptyClosureCarriesTheAnnotationTypings()
    {
        (OwlRlTerms terms, HashSet<EncodedTriple> derived) = EmptyClosure();

        Assert.HasCount(9, terms.BuiltInAnnotationProperties);
        foreach(TermId annotation in terms.BuiltInAnnotationProperties)
        {
            Assert.Contains(OwlRlBatteryHelpers.Triple(annotation, terms.Type, terms.AnnotationProperty), derived);
        }
    }

    /// <summary>owl:Thing and owl:Nothing are typed owl:Class, and owl:imports carries its typing, domain, and range, from the empty graph.</summary>
    [TestMethod]
    public void EmptyClosureCarriesTheClassAndImportsRows()
    {
        (OwlRlTerms terms, HashSet<EncodedTriple> derived) = EmptyClosure();

        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Thing, terms.Type, terms.ClassTerm), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Nothing, terms.Type, terms.ClassTerm), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Imports, terms.Type, terms.RdfProperty), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Imports, terms.Domain, terms.Ontology), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Imports, terms.Range, terms.Ontology), derived);
    }

    /// <summary>The seven property-characteristic classes are subsumed under owl:ObjectProperty from the empty graph.</summary>
    [TestMethod]
    public void EmptyClosureCarriesTheCharacteristicSubsumptions()
    {
        (OwlRlTerms terms, HashSet<EncodedTriple> derived) = EmptyClosure();

        Assert.HasCount(7, terms.PropertyCharacteristicClasses);
        foreach(TermId characteristic in terms.PropertyCharacteristicClasses)
        {
            Assert.Contains(OwlRlBatteryHelpers.Triple(characteristic, terms.SubClassOf, terms.ObjectPropertyTerm), derived);
        }
    }

    /// <summary>rdf:first and rdf:rest are functional from the empty graph.</summary>
    [TestMethod]
    public void EmptyClosureCarriesTheListFunctionality()
    {
        (OwlRlTerms terms, HashSet<EncodedTriple> derived) = EmptyClosure();

        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.First, terms.Type, terms.FunctionalProperty), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Rest, terms.Type, terms.FunctionalProperty), derived);
    }

    /// <summary>The owl:Class/rdfs:Class metaclass merge stays out of the seed table: neither subsumption nor either self-typing derives from the empty graph.</summary>
    [TestMethod]
    public void EmptyClosureNeverCarriesTheMetaclassMerge()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId rdfsClass = dictionary.GetOrAdd(new NamedNode(Lumoin.Veritas.Rdf.RdfVocabulary.Rdfs.Class));
        HashSet<EncodedTriple> derived = [.. OwlRlClosure.Compute([], terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken).Derived];

        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.SubClassOf, rdfsClass), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(rdfsClass, terms.SubClassOf, terms.ClassTerm), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.Type, terms.ClassTerm), derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(rdfsClass, terms.Type, rdfsClass), derived);
    }

    /// <summary>The merged-mode empty closure carries all four metaclass-merge rows, and cax-sco derives both cross-typings from them.</summary>
    [TestMethod]
    public void MergedVocabularyCarriesTheMetaclassMergeAndCrossTypings()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        HashSet<EncodedTriple> derived = [.. OwlRlClosure.Compute([], terms, OwlRlDatatypeOracles.FromDictionary(dictionary), axiomaticVocabulary: OwlAxiomaticVocabulary.MetaclassMerged, cancellationToken: TestContext.CancellationToken).Derived];

        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.SubClassOf, terms.RdfsClass), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.RdfsClass, terms.SubClassOf, terms.ClassTerm), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.Type, terms.ClassTerm), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.RdfsClass, terms.Type, terms.RdfsClass), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.Type, terms.RdfsClass), derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.RdfsClass, terms.Type, terms.ClassTerm), derived);
    }

    /// <summary>The naive oracle agrees with the semi-naive closure on the merged vocabulary: identical derived sets over the empty graph.</summary>
    [TestMethod]
    public void NaiveClosureAgreesOnTheMergedVocabulary()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        HashSet<EncodedTriple> semiNaive = [.. OwlRlClosure.Compute([], terms, OwlRlDatatypeOracles.FromDictionary(dictionary), axiomaticVocabulary: OwlAxiomaticVocabulary.MetaclassMerged, cancellationToken: TestContext.CancellationToken).Derived];
        HashSet<EncodedTriple> naive = [.. OwlRlClosure.ComputeNaive([], terms, OwlRlDatatypeOracles.FromDictionary(dictionary), axiomaticVocabulary: OwlAxiomaticVocabulary.MetaclassMerged, cancellationToken: TestContext.CancellationToken).Derived];

        Assert.IsTrue(semiNaive.SetEquals(naive));
    }

    /// <summary>The maintained engine never seeds the metaclass merge — dark on the initial build and dark after an incremental Apply.</summary>
    [TestMethod]
    public void MaintainedClosureStaysDarkOnTheMetaclassMerge()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "maintained-merge-probe");
        OwlRlMaintainedClosure maintained = new([], terms, OwlRlDatatypeOracles.FromDictionary(dictionary), TestContext.CancellationToken);

        Assert.IsTrue(maintained.Current.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.SubClassOf, terms.RdfsClass), maintained.Current.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.RdfsClass, terms.SubClassOf, terms.ClassTerm), maintained.Current.Derived);

        OwlRlResult applied = maintained.Apply([OwlRlBatteryHelpers.Triple(instance, terms.Type, terms.ClassTerm)], [], TestContext.CancellationToken);

        Assert.IsTrue(applied.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.ClassTerm, terms.SubClassOf, terms.RdfsClass), applied.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.RdfsClass, terms.SubClassOf, terms.ClassTerm), applied.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(instance, terms.Type, terms.RdfsClass), applied.Derived);
    }

    /// <summary>Computes the closure of the empty graph and answers the vocabulary with the derived set.</summary>
    /// <returns>The resolved vocabulary and the empty-input closure's derived triples.</returns>
    private (OwlRlTerms Terms, HashSet<EncodedTriple> Derived) EmptyClosure()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        HashSet<EncodedTriple> derived = [.. OwlRlClosure.Compute([], terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken).Derived];

        return (terms, derived);
    }
}
