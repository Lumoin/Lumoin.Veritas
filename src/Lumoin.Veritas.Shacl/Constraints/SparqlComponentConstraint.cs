using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// One parameter of a SPARQL-based constraint component (SHACL-SPARQL §6): the shape property that supplies the
/// parameter value, the SPARQL variable that value is pre-bound to, and whether the parameter is optional.
/// </summary>
/// <param name="Path">The parameter's <c>sh:path</c> predicate — the property a shape uses to provide the value.</param>
/// <param name="VariableName">The SPARQL variable the value is pre-bound to (the local name of <see cref="Path"/>, e.g. <c>ex:lang</c> → <c>$lang</c>).</param>
/// <param name="Optional">Whether the parameter is <c>sh:optional</c>; a component activates on a shape only when every non-optional parameter is provided.</param>
public sealed record SparqlComponentParameter(IriId Path, Utf8String VariableName, bool Optional);

/// <summary>
/// A SPARQL validator of a constraint component (SHACL-SPARQL §6.2): an <c>sh:SPARQLAskValidator</c> (each value
/// node must satisfy the ASK) or an <c>sh:SPARQLSelectValidator</c> (each SELECT result row is a violation).
/// </summary>
/// <param name="IsAsk">Whether this is an ASK validator (otherwise SELECT).</param>
/// <param name="Query">The parsed and normalized validator query (an ASK or SELECT), ready for per-evaluation pre-binding.</param>
/// <param name="Messages">The validator's <c>sh:message</c> values keyed by language tag (empty string = no tag).</param>
public sealed record SparqlComponentValidator(bool IsAsk, SparqlQuery Query, ImmutableDictionary<string, string> Messages);

/// <summary>
/// A SPARQL-based constraint component definition (SHACL-SPARQL §6): its IRI, its parameters, and up to three
/// validators (the generic <c>sh:validator</c>, the <c>sh:nodeValidator</c>, and the <c>sh:propertyValidator</c>).
/// Shared across every shape that uses the component; the per-shape parameter values live on
/// <see cref="SparqlComponentConstraint"/>.
/// </summary>
/// <param name="ComponentIri">The component's IRI, emitted as <c>sh:sourceConstraintComponent</c>.</param>
/// <param name="Parameters">The component's parameters.</param>
/// <param name="GenericValidator">The <c>sh:validator</c> (used for both node and property shapes), or <see langword="null"/>.</param>
/// <param name="NodeValidator">The <c>sh:nodeValidator</c> (preferred on a node shape), or <see langword="null"/>.</param>
/// <param name="PropertyValidator">The <c>sh:propertyValidator</c> (preferred on a property shape), or <see langword="null"/>.</param>
public sealed record SparqlComponentDefinition(
    Utf8String ComponentIri,
    ImmutableArray<SparqlComponentParameter> Parameters,
    SparqlComponentValidator? GenericValidator,
    SparqlComponentValidator? NodeValidator,
    SparqlComponentValidator? PropertyValidator)
{
    /// <summary>Selects the validator to use for a shape: the property/node validator when present, else the generic one.</summary>
    /// <param name="isPropertyShape">Whether the enclosing shape is a property shape.</param>
    /// <returns>The applicable validator, or <see langword="null"/> when none applies.</returns>
    public SparqlComponentValidator? SelectValidator(bool isPropertyShape)
    {
        return isPropertyShape
            ? PropertyValidator ?? GenericValidator
            : NodeValidator ?? GenericValidator;
    }
}

/// <summary>
/// A use of a SPARQL-based constraint component on a shape (SHACL-SPARQL §6): the shared
/// <see cref="SparqlComponentDefinition"/> plus the parameter values this shape provides. Evaluated by
/// <see cref="Validation.Evaluators.SparqlComponentConstraintEvaluator"/>, which pre-binds the parameter values
/// (and <c>$this</c>/<c>$value</c>/<c>$PATH</c>) into the validator query.
/// </summary>
/// <param name="Definition">The shared component definition (parameters + validators).</param>
/// <param name="ParameterValues">The parameter values this shape provides, keyed by parameter <c>sh:path</c>.</param>
public sealed record SparqlComponentConstraint(
    SparqlComponentDefinition Definition,
    ImmutableDictionary<IriId, TermId> ParameterValues): ConstraintComponent
{
    /// <summary>The component's IRI, emitted as <c>sh:sourceConstraintComponent</c>.</summary>
    public override Utf8String ConstraintComponentIri => Definition.ComponentIri;

    /// <summary>A SPARQL-based constraint component references no shapes structurally; always empty.</summary>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
