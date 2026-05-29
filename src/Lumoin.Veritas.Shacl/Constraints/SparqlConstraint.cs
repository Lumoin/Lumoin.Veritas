using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// A SHACL-SPARQL constraint: a <c>sh:sparql</c> link from a shape to a SPARQL-based constraint whose
/// <c>sh:select</c> query identifies the value nodes that violate it (SHACL-SPARQL §5.1).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the Core constraint components, a SPARQL constraint is not a parameter scalar on the shape: the
/// <c>sh:sparql</c> object is a separate constraint node whose <c>sh:select</c>, <c>sh:message</c>, and
/// <c>sh:prefixes</c> sub-graph the loader resolves through <see cref="Loading.SparqlConstraintParser"/>. The
/// loader parses (with the <c>sh:prefixes</c> namespace bindings prepended) and normalizes the query once, the
/// way <see cref="PatternConstraint"/> compiles its regex once, so evaluation re-uses the parsed
/// <see cref="SparqlQuery"/>.
/// </para>
/// <para>
/// <b>Evaluation (SHACL-SPARQL §5.2/§5.3).</b> The evaluator pre-binds <c>$this</c> to the focus node and runs
/// the query; each result row is a violation, with the row's <c>?value</c>, <c>?path</c>, and <c>?message</c>
/// bindings mapping to the result's <c>sh:value</c>, <c>sh:resultPath</c>, and <c>sh:resultMessage</c>.
/// </para>
/// </remarks>
/// <param name="ConstraintNode">The <c>sh:sparql</c> constraint node's term id, emitted as <c>sh:sourceConstraint</c> on every produced result (SHACL-SPARQL §5.3).</param>
/// <param name="SelectText">The verbatim <c>sh:select</c> query text (without the prepended prefix declarations), retained for diagnostics.</param>
/// <param name="Query">The parsed and normalized SELECT query, ready for per-focus translation and execution.</param>
/// <param name="Messages">The constraint-node <c>sh:message</c> values keyed by language tag (empty string = no tag); a result with no <c>?message</c> binding falls back to these.</param>
public sealed record SparqlConstraint(
    TermId ConstraintNode,
    Utf8String SelectText,
    SparqlQuery Query,
    ImmutableDictionary<string, string> Messages): ConstraintComponent
{
    /// <summary>The constraint-component IRI emitted as <c>sh:sourceConstraintComponent</c>: <c>sh:SPARQLConstraintComponent</c>.</summary>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.SparqlConstraint;

    /// <summary>A SPARQL constraint references no shapes structurally; always empty.</summary>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
