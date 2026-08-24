using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for <see cref="ValidationReportSerializer"/>: a hand-built report is
/// serialized and the resulting quads are inspected by following the report
/// and result blank nodes, so the assertions do not depend on synthesised
/// blank-node labels.
/// </summary>
[TestClass]
internal sealed class ValidationReportSerializerTests
{
    private const string ExFocus = "http://example.org/focus";
    private const string ExShape = "http://example.org/Shape";
    private const string ExValue = "http://example.org/value";
    private const string ExPredicate = "http://example.org/p";

    [TestMethod]
    public void SerializesReportResultAndPredicatePath()
    {
        TermDictionary dictionary = new();
        TermId focus = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExFocus)));
        TermId shape = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape)));
        TermId value = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExValue)));
        IriId predicate = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExPredicate)));

        ValidationResult result = new()
        {
            FocusNode = focus,
            ValueNode = value,
            ResultPath = new PredicatePath(predicate),
            Severity = Severity.Violation,
            SourceShape = shape,
            SourceConstraintComponent = ShaclComponentVocabulary.MinCount,
        };
        ValidationReport report = new() { Conforms = false, Results = [result] };

        List<Quad> quads = ValidationReportSerializer.Serialize(report, dictionary);

        RdfTerm reportNode = SingleSubjectOfType(quads, ShaclResultsVocabulary.ValidationReport);
        Assert.IsTrue(HasTriple(quads, reportNode, ShaclResultsVocabulary.Conforms,
            new Literal(Utf8Strings.From("false"), new NamedNode(Vocabulary.Xsd.Boolean))), "sh:conforms false^^xsd:boolean expected.");

        RdfTerm resultNode = SingleObject(quads, reportNode, ShaclResultsVocabulary.Result);
        Assert.IsTrue(HasTriple(quads, resultNode, ShaclResultsVocabulary.FocusNode, new NamedNode(Utf8Strings.From(ExFocus))));
        Assert.IsTrue(HasTriple(quads, resultNode, ShaclResultsVocabulary.Value, new NamedNode(Utf8Strings.From(ExValue))));
        Assert.IsTrue(HasTriple(quads, resultNode, ShaclResultsVocabulary.SourceShape, new NamedNode(Utf8Strings.From(ExShape))));
        Assert.IsTrue(HasTriple(quads, resultNode, ShaclResultsVocabulary.ResultSeverity, new NamedNode(ShaclSeverityVocabulary.Violation)));
        Assert.IsTrue(HasTriple(quads, resultNode, ShaclResultsVocabulary.SourceConstraintComponent, new NamedNode(ShaclComponentVocabulary.MinCount)));

        //A predicate path is the predicate IRI itself, directly as the object.
        Assert.IsTrue(HasTriple(quads, resultNode, ShaclResultsVocabulary.ResultPath, new NamedNode(Utf8Strings.From(ExPredicate))));
    }

    [TestMethod]
    public void SerializesInversePathAsBlankNodeStructure()
    {
        TermDictionary dictionary = new();
        TermId focus = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExFocus)));
        TermId shape = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExShape)));
        IriId predicate = dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(ExPredicate)));

        ValidationResult result = new()
        {
            FocusNode = focus,
            ResultPath = new InversePath(new PredicatePath(predicate)),
            Severity = Severity.Violation,
            SourceShape = shape,
            SourceConstraintComponent = ShaclComponentVocabulary.MinCount,
        };
        ValidationReport report = new() { Conforms = false, Results = [result] };

        List<Quad> quads = ValidationReportSerializer.Serialize(report, dictionary);

        RdfTerm reportNode = SingleSubjectOfType(quads, ShaclResultsVocabulary.ValidationReport);
        RdfTerm resultNode = SingleObject(quads, reportNode, ShaclResultsVocabulary.Result);

        //sh:resultPath points at a blank node carrying sh:inversePath -> <p>.
        RdfTerm pathNode = SingleObject(quads, resultNode, ShaclResultsVocabulary.ResultPath);
        Assert.IsInstanceOfType<BlankNode>(pathNode);
        Assert.IsTrue(HasTriple(quads, pathNode, ShaclPathVocabulary.InversePath, new NamedNode(Utf8Strings.From(ExPredicate))));
    }

    private static RdfTerm SingleSubjectOfType(List<Quad> quads, Utf8String typeIri)
    {
        return quads.Single(q => q.Predicate.Iri.Equals(Vocabulary.Rdf.Type) && q.Object is NamedNode named && named.Iri.Equals(typeIri)).Subject;
    }

    private static RdfTerm SingleObject(List<Quad> quads, RdfTerm subject, Utf8String predicate)
    {
        return quads.Single(q => q.Subject.Equals(subject) && q.Predicate.Iri.Equals(predicate)).Object;
    }

    private static bool HasTriple(List<Quad> quads, RdfTerm subject, Utf8String predicate, RdfTerm @object)
    {
        return quads.Any(q => q.Subject.Equals(subject) && q.Predicate.Iri.Equals(predicate) && q.Object.Equals(@object));
    }
}
