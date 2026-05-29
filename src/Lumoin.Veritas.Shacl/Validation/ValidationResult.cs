using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Validation.Evaluators;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// A single SHACL validation result — one unit of feedback produced by a
/// constraint evaluator. A result may represent a conformance violation,
/// a warning, or an informational note depending on
/// <see cref="Severity"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.8, validation results are described in RDF
/// using the <c>sh:ValidationResult</c> vocabulary. The fields of this
/// record correspond to the standard properties:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Field</term>
///     <description>SHACL property</description>
///   </listheader>
///   <item>
///     <term><see cref="FocusNode"/></term>
///     <description><c>sh:focusNode</c></description>
///   </item>
///   <item>
///     <term><see cref="ValueNode"/></term>
///     <description><c>sh:value</c></description>
///   </item>
///   <item>
///     <term><see cref="ResultPath"/></term>
///     <description><c>sh:resultPath</c></description>
///   </item>
///   <item>
///     <term><see cref="Severity"/></term>
///     <description><c>sh:resultSeverity</c></description>
///   </item>
///   <item>
///     <term><see cref="SourceShape"/></term>
///     <description><c>sh:sourceShape</c></description>
///   </item>
///   <item>
///     <term><see cref="SourceConstraintComponent"/></term>
///     <description><c>sh:sourceConstraintComponent</c></description>
///   </item>
///   <item>
///     <term><see cref="SourceConstraint"/></term>
///     <description><c>sh:sourceConstraint</c> (SPARQL-based constraints only)</description>
///   </item>
///   <item>
///     <term><see cref="Messages"/></term>
///     <description><c>sh:resultMessage</c> (possibly multi-lingual)</description>
///   </item>
/// </list>
/// <para>
/// Only results with <see cref="Shacl.Severity.Violation"/> severity
/// affect the overall <see cref="ValidationReport.Conforms"/> flag;
/// warnings and informational results leave conformance untouched.
/// </para>
/// </remarks>
public sealed record ValidationResult
{
    /// <summary>
    /// The focus node being validated when this result was produced.
    /// Always set.
    /// </summary>
    public required TermId FocusNode { get; init; }

    /// <summary>
    /// The specific value node that violated the constraint, when the
    /// constraint is value-node-specific (e.g., a datatype mismatch on
    /// one particular literal). <c>null</c> when the result pertains to
    /// the whole value-node set (e.g., a <c>sh:minCount</c> violation).
    /// </summary>
    public TermId? ValueNode { get; init; }

    /// <summary>
    /// The property path that led from <see cref="FocusNode"/> to the
    /// value nodes being validated, when this result came from a
    /// property shape. <c>null</c> for node-shape results.
    /// </summary>
    public PropertyPath? ResultPath { get; init; }

    /// <summary>
    /// The severity level of this result. Inherited from the shape's
    /// declared severity by most evaluators; may be overridden (e.g.,
    /// <see cref="NotImplementedEvaluator"/> emits
    /// <see cref="Shacl.Severity.Info"/> regardless of shape severity).
    /// </summary>
    public required Severity Severity { get; init; }

    /// <summary>
    /// The term id of the shape that produced this result. Typically an
    /// IRI identifier from the shape graph.
    /// </summary>
    public required TermId SourceShape { get; init; }

    /// <summary>
    /// The IRI of the constraint component that produced this result —
    /// for example <c>sh:MinCountConstraintComponent</c>.
    /// </summary>
    public required Utf8String SourceConstraintComponent { get; init; }

    /// <summary>
    /// The term id of the specific constraint that produced this result,
    /// emitted as <c>sh:sourceConstraint</c>. Set only by SPARQL-based
    /// constraints (SHACL-SPARQL §5.3), where it points at the
    /// <c>sh:sparql</c> constraint node; <c>null</c> for the Core
    /// constraint components, which identify their source by component IRI
    /// and shape alone.
    /// </summary>
    public TermId? SourceConstraint { get; init; }

    /// <summary>
    /// Human-readable messages attached to this result, keyed by
    /// language tag (<c>""</c> for non-tagged). Typically inherited
    /// from the shape's <see cref="Shape.Messages"/>, but evaluators
    /// may supply their own message (for example
    /// <see cref="NotImplementedEvaluator"/> provides a synthetic
    /// message explaining that the constraint is unimplemented).
    /// </summary>
    public ImmutableDictionary<string, string> Messages { get; init; } = ImmutableDictionary<string, string>.Empty;
}