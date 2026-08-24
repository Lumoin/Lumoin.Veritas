using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:DatatypeConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.3.2: each value node must be a literal whose
/// datatype IRI equals <see cref="DatatypeConstraint.DatatypeId"/>.
/// Non-literal value nodes (IRIs, blank nodes) fail automatically.
/// </para>
/// <para>
/// Violations are per-value-node: each value whose term kind or
/// datatype does not match emits its own result.
/// </para>
/// <para>
/// <b>Lexical-form validity.</b> The SHACL spec additionally requires
/// the literal's lexical form to be a valid value of the datatype (for
/// example, <c>"abc"^^xsd:integer</c> fails even though the datatype
/// IRI matches). This is enforced via
/// <see cref="XsdLexicalValidity.IsValidLexicalForm"/>, which validates
/// the lexical form against the datatype's lexical space (and the
/// value-range bounds of the derived integer types). For a datatype IRI
/// outside the modelled XSD set the run's
/// <see cref="ShaclValidatorOptions.ValueDatatypes"/> registry is
/// consulted; under the empty default every unmodelled datatype is
/// accepted on IRI identity alone.
/// </para>
/// </remarks>
public static class DatatypeEvaluator
{
    /// <summary>
    /// The evaluator function. Matches the
    /// <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    public static ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DatatypeConstraint datatype = (DatatypeConstraint)constraint;

        //Resolve the datatype IRI once per evaluation; reuse it for
        //every value-node comparison. The dictionary round-trip is
        //cheap (single lookup) but still worth lifting out of the loop.
        NamedNode expectedDatatype = (NamedNode)context.Dictionary.Resolve(datatype.DatatypeId.Value);

        //The value-datatype registry is per-run state: one read here
        //serves every value-node consult in the loop.
        ValueDatatypeRegistry valueDatatypes = context.Options.ValueDatatypes;

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            bool matches = term is Literal literal
                && literal.Datatype == expectedDatatype
                && XsdLexicalValidity.IsValidLexicalForm(literal.Value, literal.Datatype.Iri, valueDatatypes);
            if(matches)
            {
                continue;
            }

            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ValueNode = value,
                ResultPath = path,
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = shape.Messages,
            });
        }

        return ValueTask.FromResult(builder.ToImmutable());
    }
}
