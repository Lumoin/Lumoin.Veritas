using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// A constraint component whose parameter set and component identity
/// are defined at runtime rather than by a compile-time record type.
/// Used by interactive shape-graph authoring tools and hot-reloaded
/// rule configurations where defining a fresh
/// <see cref="ConstraintComponent"/> subclass per rule is impractical.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dynamic constraint.</b> The 37 built-in constraint records
/// (<see cref="MinCountConstraint"/>, <see cref="DatatypeConstraint"/>,
/// and so on) are the right choice when the rule is fixed at build
/// time — they give direct field access on the evaluator's hot path and
/// full compile-time type checking. A GUI-driven editor has a different
/// usage pattern: a user drags parameters onto a shape and the editor
/// needs to produce a validation-capable <see cref="ConstraintComponent"/>
/// without recompiling. <see cref="DynamicConstraint"/> is the record
/// that covers that case.
/// </para>
/// <para>
/// <b>Graduation to compiled form.</b> A <see cref="DynamicConstraint"/>
/// is designed to be semantically equivalent to a hand-written
/// <see cref="ConstraintComponent"/> subclass with the same component
/// IRI and parameter structure. Once a constraint is proven in
/// exploratory use, a code-generation tool can translate a
/// <see cref="DynamicConstraint"/> into a typed record plus factory, at
/// which point the constraint participates in JIT-inlined field access
/// identically to the built-ins. The runtime and compiled forms agree
/// by construction: same <see cref="ComponentIri"/>, same parameter
/// IRIs, same captured term-id values.
/// </para>
/// <para>
/// <b>Parameter storage.</b> Scalar parameters (single-valued) live in
/// <see cref="ScalarParameters"/>, keyed by parameter IRI. List
/// parameters (RDF-list-valued) live in <see cref="ListParameters"/>,
/// each entry holding the pre-walked list members as
/// <see cref="TermId"/>s. The loader pre-walks lists before factory
/// invocation, so the <see cref="DynamicConstraint"/> factory sees
/// resolved members rather than a list head.
/// </para>
/// <para>
/// <b>Shape references.</b> Parameters that reference other shapes
/// (<c>sh:node</c>, <c>sh:and</c> members, <c>sh:qualifiedValueShape</c>)
/// are stored as term ids alongside every other parameter. Whether a
/// given parameter names a shape is a property of the component
/// definition, not of this record — so the record additionally carries
/// <see cref="ReferencedShapeIdsStorage"/>, populated by the factory
/// based on which parameters the component declares as shape-typed.
/// A dynamic factory that does not declare any shape-typed parameters
/// leaves this empty.
/// </para>
/// <para>
/// <b>Equality caveat.</b> As a record, equality is synthesized
/// field-by-field, but the <see cref="ImmutableDictionary{TKey, TValue}"/>
/// and <see cref="ImmutableArray{T}"/> fields compare by reference
/// equality, not structural. Two dynamic constraints built from the
/// same parameter set through separate factory invocations therefore
/// do <em>not</em> compare equal. If structural equality becomes
/// useful in practice — for de-duplication in a constraint registry,
/// for example — override <see cref="Equals(DynamicConstraint?)"/> and
/// <see cref="GetHashCode"/> to compare the dictionaries' key-value
/// contents. That's deferred until a concrete need arises.
/// </para>
/// </remarks>
/// <param name="ComponentIri">The component IRI, emitted as <c>sh:sourceConstraintComponent</c>.</param>
/// <param name="ScalarParameters">
/// Scalar parameter values keyed by parameter IRI. Each value is the
/// raw <see cref="TermId"/> as supplied by the shape graph; the
/// evaluator interprets the term according to the parameter's declared
/// datatype.
/// </param>
/// <param name="ListParameters">
/// List parameter members keyed by parameter IRI. Each entry's value
/// is the pre-walked list contents.
/// </param>
/// <param name="ReferencedShapeIdsStorage">
/// Term ids of shapes structurally referenced by this constraint.
/// Supplied by the factory based on the component's declared
/// shape-typed parameters; empty when the component has no
/// shape-typed parameters.
/// </param>
public sealed record DynamicConstraint(
    Utf8String ComponentIri,
    ImmutableDictionary<IriId, TermId> ScalarParameters,
    ImmutableDictionary<IriId, ImmutableArray<TermId>> ListParameters,
    ImmutableArray<TermId> ReferencedShapeIdsStorage): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ComponentIri;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => ReferencedShapeIdsStorage;

    /// <summary>
    /// Looks up the scalar parameter value associated with
    /// <paramref name="parameter"/>.
    /// </summary>
    /// <param name="parameter">The parameter IRI.</param>
    /// <param name="value">On success, the captured <see cref="TermId"/>.</param>
    /// <returns><c>true</c> if the parameter is present as a scalar; <c>false</c> otherwise.</returns>
    public bool TryGetScalar(IriId parameter, out TermId value)
        => ScalarParameters.TryGetValue(parameter, out value);

    /// <summary>
    /// Looks up the list parameter members associated with
    /// <paramref name="parameter"/>.
    /// </summary>
    /// <param name="parameter">The parameter IRI.</param>
    /// <param name="members">On success, the captured list members.</param>
    /// <returns><c>true</c> if the parameter is present as a list; <c>false</c> otherwise.</returns>
    public bool TryGetList(IriId parameter, out ImmutableArray<TermId> members)
    {
        if(ListParameters.TryGetValue(parameter, out ImmutableArray<TermId> found))
        {
            members = found;
            return true;
        }

        members = default;

        return false;
    }
}
