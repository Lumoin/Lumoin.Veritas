using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Serializes a <see cref="ValidationReport"/> into the RDF quads of a
/// <c>sh:ValidationReport</c> graph per SHACL 1.2 Core §4.8.
/// </summary>
/// <remarks>
/// <para>
/// The report is rooted at a fresh blank node typed <c>sh:ValidationReport</c>
/// carrying <c>sh:conforms</c> and one <c>sh:result</c> per
/// <see cref="ValidationResult"/>; each result is its own blank node carrying
/// <c>sh:focusNode</c>, <c>sh:resultSeverity</c>,
/// <c>sh:sourceConstraintComponent</c>, <c>sh:sourceShape</c>, and — when
/// present — <c>sh:value</c>, <c>sh:resultPath</c>, and <c>sh:resultMessage</c>.
/// </para>
/// <para>
/// <c>sh:resultPath</c> is written using the SHACL path syntax of §2.3.1: a
/// predicate path is the predicate IRI itself, a sequence path is an RDF list
/// of its steps, and the alternative, inverse, and cardinality paths are
/// blank nodes carrying the corresponding <c>sh:*Path</c> predicate.
/// </para>
/// <para>
/// Blank-node labels are synthesised per call and carry no meaning; consumers
/// comparing two reports should do so under blank-node isomorphism.
/// </para>
/// </remarks>
public static class ValidationReportSerializer
{
    private static readonly NamedNode RdfType = new(Vocabulary.Rdf.Type);
    private static readonly NamedNode RdfFirst = new(RdfVocabulary.Rdf.First);
    private static readonly NamedNode RdfRest = new(RdfVocabulary.Rdf.Rest);
    private static readonly NamedNode RdfNil = new(RdfVocabulary.Rdf.Nil);

    private static readonly NamedNode ValidationReportClass = new(ShaclResultsVocabulary.ValidationReport);
    private static readonly NamedNode ValidationResultClass = new(ShaclResultsVocabulary.ValidationResult);
    private static readonly NamedNode Conforms = new(ShaclResultsVocabulary.Conforms);
    private static readonly NamedNode Result = new(ShaclResultsVocabulary.Result);
    private static readonly NamedNode FocusNode = new(ShaclResultsVocabulary.FocusNode);
    private static readonly NamedNode Value = new(ShaclResultsVocabulary.Value);
    private static readonly NamedNode ResultPath = new(ShaclResultsVocabulary.ResultPath);
    private static readonly NamedNode SourceShape = new(ShaclResultsVocabulary.SourceShape);
    private static readonly NamedNode SourceConstraintComponent = new(ShaclResultsVocabulary.SourceConstraintComponent);
    private static readonly NamedNode SourceConstraint = new(ShaclResultsVocabulary.SourceConstraint);
    private static readonly NamedNode ResultSeverity = new(ShaclResultsVocabulary.ResultSeverity);
    private static readonly NamedNode ResultMessage = new(ShaclResultsVocabulary.ResultMessage);

    private static readonly NamedNode InversePath = new(ShaclPathVocabulary.InversePath);
    private static readonly NamedNode AlternativePath = new(ShaclPathVocabulary.AlternativePath);
    private static readonly NamedNode ZeroOrMorePath = new(ShaclPathVocabulary.ZeroOrMorePath);
    private static readonly NamedNode OneOrMorePath = new(ShaclPathVocabulary.OneOrMorePath);
    private static readonly NamedNode ZeroOrOnePath = new(ShaclPathVocabulary.ZeroOrOnePath);

    private static readonly NamedNode SeverityViolation = new(ShaclSeverityVocabulary.Violation);
    private static readonly NamedNode SeverityWarning = new(ShaclSeverityVocabulary.Warning);
    private static readonly NamedNode SeverityInfo = new(ShaclSeverityVocabulary.Info);

    private static readonly NamedNode XsdBoolean = new(Vocabulary.Xsd.Boolean);
    private static readonly NamedNode XsdString = new(Vocabulary.Xsd.String);
    private static readonly NamedNode RdfLangString = new(Vocabulary.Rdf.LangString);

    private static readonly Utf8String TrueValue = new("true"u8.ToArray());
    private static readonly Utf8String FalseValue = new("false"u8.ToArray());

    /// <summary>
    /// Serializes <paramref name="report"/> into the quads of its
    /// <c>sh:ValidationReport</c> graph.
    /// </summary>
    /// <param name="report">The report to serialize.</param>
    /// <param name="dictionary">The dictionary that resolves the report's <see cref="TermId"/> handles back to terms; must be the dictionary the validation run used.</param>
    /// <param name="includeMessages">When <c>true</c>, emits <c>sh:resultMessage</c> triples; callers comparing against an expected report that omits messages pass <c>false</c>.</param>
    /// <returns>The report graph as a list of quads in the default graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> or <paramref name="dictionary"/> is <c>null</c>.</exception>
    public static List<Quad> Serialize(ValidationReport report, TermDictionary dictionary, bool includeMessages = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(dictionary);

        List<Quad> quads = [];
        int counter = 0;

        BlankNode reportNode = NextBlank(ref counter);
        quads.Add(new Quad(reportNode, RdfType, ValidationReportClass));
        quads.Add(new Quad(reportNode, Conforms, new Literal(report.Conforms ? TrueValue : FalseValue, XsdBoolean)));

        foreach(ValidationResult result in report.Results)
        {
            BlankNode resultNode = NextBlank(ref counter);
            quads.Add(new Quad(reportNode, Result, resultNode));
            quads.Add(new Quad(resultNode, RdfType, ValidationResultClass));
            quads.Add(new Quad(resultNode, FocusNode, dictionary.Resolve(result.FocusNode)));
            quads.Add(new Quad(resultNode, ResultSeverity, SeverityNode(result.Severity)));
            quads.Add(new Quad(resultNode, SourceConstraintComponent, new NamedNode(result.SourceConstraintComponent)));
            quads.Add(new Quad(resultNode, SourceShape, dictionary.Resolve(result.SourceShape)));

            if(result.SourceConstraint is TermId sourceConstraint)
            {
                quads.Add(new Quad(resultNode, SourceConstraint, dictionary.Resolve(sourceConstraint)));
            }

            if(result.ValueNode is TermId valueNode)
            {
                quads.Add(new Quad(resultNode, Value, dictionary.Resolve(valueNode)));
            }

            if(result.ResultPath is not null)
            {
                RdfTerm pathTerm = SerializePath(result.ResultPath, dictionary, quads, ref counter);
                quads.Add(new Quad(resultNode, ResultPath, pathTerm));
            }

            if(includeMessages)
            {
                foreach(KeyValuePair<string, string> message in result.Messages)
                {
                    quads.Add(new Quad(resultNode, ResultMessage, MessageLiteral(message.Key, message.Value)));
                }
            }
        }

        return quads;
    }

    /// <summary>
    /// Serializes a property path to its SHACL path RDF, appending any
    /// helper triples (list cells, operator blank nodes) to
    /// <paramref name="quads"/> and returning the term that stands for the
    /// path as the object of <c>sh:resultPath</c> or <c>sh:path</c>.
    /// </summary>
    /// <param name="path">The path to serialize.</param>
    /// <param name="dictionary">The dictionary resolving predicate identifiers.</param>
    /// <param name="quads">The quad list accumulating the path's structural triples.</param>
    /// <param name="counter">The running blank-node label counter.</param>
    /// <returns>The term representing the path.</returns>
    private static RdfTerm SerializePath(PropertyPath path, TermDictionary dictionary, List<Quad> quads, ref int counter)
    {
        switch(path)
        {
            case PredicatePath predicate:
            {
                return dictionary.Resolve(predicate.Predicate);
            }

            case SequencePath sequence:
            {
                return SerializePathList(sequence.Steps, dictionary, quads, ref counter);
            }

            case AlternativePath alternative:
            {
                BlankNode node = NextBlank(ref counter);
                RdfTerm list = SerializePathList(alternative.Alternatives, dictionary, quads, ref counter);
                quads.Add(new Quad(node, AlternativePath, list));
                return node;
            }

            case InversePath inverse:
            {
                return SerializeOperatorPath(InversePath, inverse.Inner, dictionary, quads, ref counter);
            }

            case ZeroOrMorePath zeroOrMore:
            {
                return SerializeOperatorPath(ZeroOrMorePath, zeroOrMore.Inner, dictionary, quads, ref counter);
            }

            case OneOrMorePath oneOrMore:
            {
                return SerializeOperatorPath(OneOrMorePath, oneOrMore.Inner, dictionary, quads, ref counter);
            }

            case ZeroOrOnePath zeroOrOne:
            {
                return SerializeOperatorPath(ZeroOrOnePath, zeroOrOne.Inner, dictionary, quads, ref counter);
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown property-path kind.");
            }
        }
    }

    /// <summary>
    /// Serializes a single-operator path (<c>sh:inversePath</c>,
    /// <c>sh:zeroOrMorePath</c>, …) as a fresh blank node carrying
    /// <paramref name="operatorPredicate"/> to its inner path.
    /// </summary>
    /// <param name="operatorPredicate">The operator predicate.</param>
    /// <param name="inner">The inner path.</param>
    /// <param name="dictionary">The dictionary resolving predicate identifiers.</param>
    /// <param name="quads">The quad list accumulating the path's structural triples.</param>
    /// <param name="counter">The running blank-node label counter.</param>
    /// <returns>The operator blank node.</returns>
    private static BlankNode SerializeOperatorPath(NamedNode operatorPredicate, PropertyPath inner, TermDictionary dictionary, List<Quad> quads, ref int counter)
    {
        BlankNode node = NextBlank(ref counter);
        RdfTerm innerTerm = SerializePath(inner, dictionary, quads, ref counter);
        quads.Add(new Quad(node, operatorPredicate, innerTerm));
        return node;
    }

    /// <summary>
    /// Serializes a list of paths as an RDF list of their path terms,
    /// returning the list head (or <c>rdf:nil</c> when empty).
    /// </summary>
    /// <param name="paths">The paths to lay out as an RDF list.</param>
    /// <param name="dictionary">The dictionary resolving predicate identifiers.</param>
    /// <param name="quads">The quad list accumulating the list cells.</param>
    /// <param name="counter">The running blank-node label counter.</param>
    /// <returns>The list head term.</returns>
    private static RdfTerm SerializePathList(ImmutableArray<PropertyPath> paths, TermDictionary dictionary, List<Quad> quads, ref int counter)
    {
        RdfTerm head = RdfNil;
        for(int i = paths.Length - 1; i >= 0; i--)
        {
            RdfTerm item = SerializePath(paths[i], dictionary, quads, ref counter);
            BlankNode cell = NextBlank(ref counter);
            quads.Add(new Quad(cell, RdfFirst, item));
            quads.Add(new Quad(cell, RdfRest, head));
            head = cell;
        }

        return head;
    }

    /// <summary>
    /// Maps a <see cref="Severity"/> to its <c>sh:resultSeverity</c> IRI node.
    /// </summary>
    /// <param name="severity">The severity to map.</param>
    /// <returns>The severity IRI node.</returns>
    private static NamedNode SeverityNode(Severity severity)
    {
        return severity switch
        {
            Severity.Warning => SeverityWarning,
            Severity.Info => SeverityInfo,
            _ => SeverityViolation
        };
    }

    /// <summary>
    /// Builds a <c>sh:resultMessage</c> literal: a plain <c>xsd:string</c> when
    /// untagged, or an <c>rdf:langString</c> when a language tag is present.
    /// </summary>
    /// <param name="language">The language tag, or the empty string for none.</param>
    /// <param name="text">The message text.</param>
    /// <returns>The message literal.</returns>
    private static Literal MessageLiteral(string language, string text)
    {
        Utf8String value = Utf8String.FromString(text);
        return language.Length == 0
            ? new Literal(value, XsdString)
            : new Literal(value, RdfLangString, Utf8String.FromString(language));
    }

    /// <summary>
    /// Allocates the next synthetic blank node and advances the label counter.
    /// </summary>
    /// <param name="counter">The running blank-node label counter.</param>
    /// <returns>A fresh blank node.</returns>
    private static BlankNode NextBlank(ref int counter)
    {
        BlankNode node = new(Utf8String.FromString($"report{counter}"));
        counter++;
        return node;
    }
}
