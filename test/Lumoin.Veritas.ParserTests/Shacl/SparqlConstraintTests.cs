using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;
using ValidationResult = Lumoin.Veritas.Shacl.Validation.ValidationResult;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// End-to-end tests for SHACL-SPARQL <c>sh:sparql</c> constraints: that the loader parses a constraint node's
/// <c>sh:select</c>/<c>sh:prefixes</c>/<c>sh:message</c> sub-graph into a <see cref="SparqlConstraint"/>, and that
/// <see cref="SparqlConstraintEvaluator"/> pre-binds <c>$this</c> to the focus node, runs the query, and maps
/// each result row to a violation.
/// </summary>
[TestClass]
internal sealed class SparqlConstraintTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string PersonShape = "http://example.org/PersonShape";
    private const string PersonClass = "http://example.org/Person";
    private const string AgePredicate = "http://example.org/age";
    private const string ExNamespace = "http://example.org/";
    private const string Alice = "http://example.org/alice";
    private const string Bob = "http://example.org/bob";

    //A SPARQL constraint selecting every value of ex:age below zero. With $this pre-bound to the focus node,
    //each below-zero age is one violation row, carrying the offending value as ?value.
    private const string NegativeAgeSelect =
        "SELECT $this ?value WHERE { $this ex:age ?value . FILTER (?value < 0) }";

    [TestMethod]
    public async Task LoaderParsesSparqlConstraintWithPrefixesAndMessage()
    {
        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = BuildShapeGraph();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(registry.TryGetShape(dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(PersonShape))), out Shape? shape));

        SparqlConstraint constraint = shape!.Constraints.OfType<SparqlConstraint>().Single();
        Assert.AreEqual(ShaclComponentVocabulary.SparqlConstraint, constraint.ConstraintComponentIri);
        Assert.IsNotNull(constraint.Query);
        Assert.AreEqual("negative age", constraint.Messages[string.Empty]);
    }

    [TestMethod]
    public async Task ViolatingFocusNodeProducesViolationWithValueAndConformingFocusDoesNot()
    {
        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = BuildShapeGraph();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(),
            dictionary,
            ShaclBuiltInComponents.All,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Data: alice is a Person with a negative age (violates); bob is a Person with a valid age (conforms).
        NamedNode rdfType = new(Vocabulary.Rdf.Type);
        NamedNode personClass = new(Utf8Strings.From(PersonClass));
        NamedNode age = new(Utf8Strings.From(AgePredicate));
        NamedNode alice = new(Utf8Strings.From(Alice));
        NamedNode bob = new(Utf8Strings.From(Bob));
        Literal negativeAge = new(Utf8Strings.From("-5"), new NamedNode(Vocabulary.Xsd.Integer));
        Literal validAge = new(Utf8Strings.From("10"), new NamedNode(Vocabulary.Xsd.Integer));

        List<EncodedTriple> dataTriples =
        [
            new Quad(alice, rdfType, personClass).Encode(dictionary).AsTriple(),
            new Quad(alice, age, negativeAge).Encode(dictionary).AsTriple(),
            new Quad(bob, rdfType, personClass).Encode(dictionary).AsTriple(),
            new Quad(bob, age, validAge).Encode(dictionary).AsTriple(),
        ];
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        ValidationReport report = await ShaclValidator.ValidateAsync(
            registry,
            dataStore.AsMatchOps(),
            dictionary,
            ShaclBuiltInEvaluators.All,
            TimeProvider.System,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        ValidationResult violation = report.Results.Single(r => r.Severity == Severity.Violation);
        Assert.AreEqual(ShaclComponentVocabulary.SparqlConstraint, violation.SourceConstraintComponent);
        Assert.AreEqual(dictionary.GetOrAdd(alice), violation.FocusNode);
        Assert.AreEqual(dictionary.GetOrAdd(negativeAge), violation.ValueNode);
        Assert.AreEqual("negative age", violation.Messages[string.Empty]);
    }

    /// <summary>
    /// Builds the shape graph: a node shape targeting ex:Person with a <c>sh:sparql</c> constraint whose
    /// <c>sh:select</c> uses the <c>ex:</c> prefix declared through <c>sh:prefixes</c>.
    /// </summary>
    /// <returns>The shape store and the dictionary the data graph must share.</returns>
    private static (InMemoryGraphStore Store, TermDictionary Dictionary) BuildShapeGraph()
    {
        ShapeGraphBuilder builder = new();
        ShapeGraphBuilder.ShapeContext shape = builder.NodeShape(PersonShape)
            .With(ShaclCoreVocabulary.TargetClass.ToString(), ShapeGraphBuilder.Iri(PersonClass));

        BlankNode constraintNode = new(Utf8Strings.From("sparqlConstraint"));
        shape.With(ShaclConstraintVocabulary.Sparql.ToString(), constraintNode);
        builder.AddQuad(constraintNode, ShaclConstraintVocabulary.Select.ToString(), ShapeGraphBuilder.StringLiteral(NegativeAgeSelect));
        builder.AddQuad(constraintNode, ShaclCoreVocabulary.Message.ToString(), ShapeGraphBuilder.StringLiteral("negative age"));

        //sh:prefixes -> [ sh:declare [ sh:prefix "ex" ; sh:namespace "http://example.org/" ] ].
        BlankNode prefixesNode = new(Utf8Strings.From("prefixes"));
        BlankNode declarationNode = new(Utf8Strings.From("declaration"));
        builder.AddQuad(constraintNode, ShaclConstraintVocabulary.Prefixes.ToString(), prefixesNode);
        builder.AddQuad(prefixesNode, ShaclConstraintVocabulary.Declare.ToString(), declarationNode);
        builder.AddQuad(declarationNode, ShaclConstraintVocabulary.Prefix.ToString(), ShapeGraphBuilder.StringLiteral("ex"));
        builder.AddQuad(declarationNode, ShaclConstraintVocabulary.Namespace.ToString(), ShapeGraphBuilder.StringLiteral(ExNamespace));

        return builder.Finish();
    }
}
