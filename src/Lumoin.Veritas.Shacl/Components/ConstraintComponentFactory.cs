using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Synchronous factory that constructs a <see cref="ConstraintComponent"/>
/// instance from a <see cref="ParameterBag"/>.
/// </summary>
/// <remarks>
/// <para>
/// Factories are pure, synchronous functions from parsed parameter values
/// to a concrete constraint-component AST record. They are invoked once
/// per occurrence of their component's primary parameter on a shape.
/// </para>
/// <para>
/// <b>Factory discipline.</b> Factories must be <see langword="static"/>
/// lambdas or <see langword="static"/> method groups — no closures, no
/// captures. This keeps each factory a single method pointer and avoids
/// per-invocation state-machine allocation. Construction logic must be
/// pure: no I/O, no cancellation, no side effects. Any asynchronous or
/// I/O-bound preparation (walking RDF lists, resolving child shapes) is
/// performed by the shape loader before the factory is called and
/// exposed through the <see cref="ParameterBag"/> as already-resolved
/// data.
/// </para>
/// <para>
/// <b>Error model.</b> A factory may throw synchronously when the shape
/// graph is malformed — a <c>sh:minCount</c> value that is not an
/// integer, a <c>sh:class</c> value that is not an IRI, a missing
/// <c>sh:qualifiedValueShape</c> when <c>sh:qualifiedMinCount</c> is
/// present. The typed accessors on <see cref="ParameterBag"/> throw
/// <see cref="System.FormatException"/> for type mismatches and
/// <see cref="System.InvalidOperationException"/> for
/// required-but-absent parameters, giving uniform error shapes across
/// the built-in factory set.
/// </para>
/// </remarks>
/// <param name="bag">
/// Parsed parameter values for a single invocation. Contains the primary
/// parameter's value for this specific constraint instance plus all
/// companion parameters declared on the owning shape.
/// </param>
/// <returns>The constructed constraint-component record.</returns>
public delegate ConstraintComponent ConstraintComponentFactory(ParameterBag bag);
