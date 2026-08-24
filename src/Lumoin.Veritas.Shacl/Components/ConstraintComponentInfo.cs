using System;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Metadata describing a SHACL constraint component: its component IRI
/// (emitted as <c>sh:sourceConstraintComponent</c> on validation results),
/// the primary parameter whose presence on a shape identifies the
/// component, the full set of parameters it accepts, and the factory that
/// constructs the corresponding AST record from parsed parameter values.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §3.2, constraint components are an open set: user
/// code may register additional components beyond the 38 built-ins
/// provided by <see cref="ShaclBuiltInComponents"/>. Each built-in or
/// custom component contributes one <see cref="ConstraintComponentInfo"/>
/// to a <see cref="ShaclComponentRegistry"/>, co-locating the metadata
/// with the factory — a single registration is everything needed to
/// make a component fully usable by the shape loader and validator.
/// </para>
/// <para>
/// <b>Primary vs. all parameters.</b> Most components have a single
/// parameter that both identifies the component and carries its data
/// (<c>sh:minCount</c>, <c>sh:class</c>, <c>sh:datatype</c>). Some have a
/// primary parameter plus optional companions that only take effect when
/// the primary is present: <c>sh:pattern</c> is primary,
/// <c>sh:flags</c> and <c>sh:singleLine</c> are optional companions.
/// A few — notably the qualified-value-shape pair — share one parameter
/// (<c>sh:qualifiedValueShape</c>) across two sibling components, with
/// the component identity determined by which of
/// <c>sh:qualifiedMinCount</c> or <c>sh:qualifiedMaxCount</c> is present.
/// This struct captures that distinction: the primary parameter is what
/// the shape loader dispatches on; the <see cref="AllParameters"/> list
/// is what it collects into the constraint instance.
/// </para>
/// <para>
/// <b>Equality.</b> Two <see cref="ConstraintComponentInfo"/> values are
/// equal when their <see cref="ComponentIri"/> is equal. Parameter lists
/// and factory delegates are treated as associated metadata; identity is
/// the component IRI. Overriding equality this way prevents two
/// registrations with the same IRI but mismatched factories from being
/// considered different entries.
/// </para>
/// <para>
/// <b>Layout.</b> On 64-bit, this struct occupies 64 bytes including the
/// <see cref="Factory"/> field: two <see cref="Utf8String"/> values
/// (24 bytes each — <see cref="System.ReadOnlyMemory{T}"/> is 16 bytes
/// plus a precomputed hash int padded to 8), one
/// <see cref="ImmutableArray{T}"/> reference (8 bytes), and one
/// <see cref="Delegate"/> reference (8 bytes). That is exactly one
/// x86-64 cache line. A 56-byte layout without the factory field would
/// still occupy one full cache line on access — the remaining 8 bytes
/// would be dead space loaded into cache anyway — so co-locating the
/// factory here costs nothing in memory traffic. Pass-by-value struct
/// copies are one cache-line move, single-digit nanoseconds, and occur
/// only a few thousand times across a shape load. This layout is
/// deliberately chosen over a parallel factory registry: atomic
/// registration beats the imperceptible cost.
/// </para>
/// <para>
/// <b>Factory discipline.</b> Factories are expected to be
/// <see langword="static"/> lambdas or static method groups — no
/// closures, no captures. This keeps each factory a single method
/// pointer with no allocation on invocation.
/// </para>
/// </remarks>
/// <param name="ComponentIri">The <c>sh:XxxConstraintComponent</c> IRI.</param>
/// <param name="PrimaryParameter">
/// The parameter IRI whose presence on a shape identifies this component.
/// Dispatched on during shape loading.
/// </param>
/// <param name="AllParameters">
/// Every parameter this component can carry. Includes
/// <paramref name="PrimaryParameter"/> and any optional companions. The
/// shape loader collects values for all of these into the constructed
/// <c>ConstraintComponent</c> record.
/// </param>
/// <param name="Factory">
/// The factory delegate that constructs the concrete
/// <c>ConstraintComponent</c> record from a populated
/// <see cref="ParameterBag"/>. Must be a static lambda or static method
/// group.
/// </param>
public readonly record struct ConstraintComponentInfo(
    Utf8String ComponentIri,
    Utf8String PrimaryParameter,
    ImmutableArray<Utf8String> AllParameters,
    ConstraintComponentFactory Factory)
{
    /// <summary>
    /// Creates a <see cref="ConstraintComponentInfo"/> with the given
    /// metadata and factory. The primary parameter is automatically
    /// included in the <see cref="AllParameters"/> list if not already
    /// present; duplicate companions are ignored.
    /// </summary>
    /// <param name="componentIri">The <c>sh:XxxConstraintComponent</c> IRI.</param>
    /// <param name="primaryParameter">The identifying parameter IRI.</param>
    /// <param name="factory">
    /// The factory that constructs the AST record. Should be a static
    /// lambda or static method group.
    /// </param>
    /// <param name="optionalCompanions">
    /// Optional companion parameter IRIs that the component can carry
    /// alongside its primary. May be empty.
    /// </param>
    /// <returns>The constructed info.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <c>null</c>.</exception>
    public static ConstraintComponentInfo Create(
        Utf8String componentIri,
        Utf8String primaryParameter,
        ConstraintComponentFactory factory,
        params ReadOnlySpan<Utf8String> optionalCompanions)
    {
        ArgumentNullException.ThrowIfNull(factory);

        ImmutableArray<Utf8String>.Builder builder = ImmutableArray.CreateBuilder<Utf8String>(optionalCompanions.Length + 1);
        builder.Add(primaryParameter);
        foreach(Utf8String companion in optionalCompanions)
        {
            if(!companion.Equals(primaryParameter))
            {
                builder.Add(companion);
            }
        }

        return new ConstraintComponentInfo(
            componentIri,
            primaryParameter,
            builder.ToImmutable(),
            factory);
    }

    /// <summary>
    /// Creates a <see cref="ConstraintComponentInfo"/> whose factory
    /// produces <see cref="Constraints.DynamicConstraint"/> instances
    /// — a constraint whose parameter set is captured into
    /// dictionaries rather than into positional fields on a typed
    /// record. Used by interactive authoring tools and hot-reloaded
    /// rule configurations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated factory:
    /// </para>
    /// <list type="number">
    ///   <item><description>Captures the primary parameter's value as a scalar or, if the loader pre-resolved it as a list head, as a list entry under <paramref name="primaryParameter"/>.</description></item>
    ///   <item><description>Walks every companion parameter on the owning shape and captures its value the same way.</description></item>
    ///   <item><description>Populates <see cref="Constraints.DynamicConstraint.ReferencedShapeIdsStorage"/> from captured values whose parameter IRI appears in <paramref name="shapeTypedParameters"/>, consulting both scalar and list storage.</description></item>
    /// </list>
    /// <para>
    /// <b>Graduation.</b> A <see cref="Constraints.DynamicConstraint"/>
    /// produced by this factory carries the same component IRI,
    /// parameter-IRI keys, and term-id values that a hand-written
    /// typed constraint record would. A code-generation tool can
    /// therefore translate a <see cref="Constraints.DynamicConstraint"/>
    /// into an equivalent typed record plus factory without loss of
    /// information; the runtime and compiled forms agree by
    /// construction.
    /// </para>
    /// </remarks>
    /// <param name="componentIri">The component IRI.</param>
    /// <param name="primaryParameter">The identifying parameter IRI.</param>
    /// <param name="shapeTypedParameters">
    /// Parameter IRIs whose captured values should contribute to the
    /// constraint's <see cref="ConstraintComponent.ReferencedShapeIds"/>.
    /// May be empty for constraints that don't reference other shapes.
    /// The primary parameter may appear in this list — it is checked
    /// alongside companions. IRIs not appearing in this list are
    /// captured as ordinary parameters and do not surface as shape
    /// references.
    /// </param>
    /// <param name="optionalCompanions">
    /// Optional companion parameter IRIs that the component can carry
    /// alongside its primary. May be empty.
    /// </param>
    /// <returns>The constructed info; register it into a <see cref="ShaclComponentRegistry"/>.</returns>
    public static ConstraintComponentInfo CreateDynamic(
        Utf8String componentIri,
        Utf8String primaryParameter,
        ImmutableArray<Utf8String> shapeTypedParameters,
        params ReadOnlySpan<Utf8String> optionalCompanions)
    {
        //The shape-typed-parameter set must be captured by the factory;
        //the dispatcher object owns it and exposes its Build method as
        //the ConstraintComponentFactory delegate. This is the one place
        //the library departs from the static-lambda-only factory
        //convention — per-component configuration has to live
        //somewhere, and a dedicated object is cleaner than a captured
        //lambda closure.
        DynamicFactoryDispatcher dispatcher = new(componentIri, shapeTypedParameters);

        return Create(
            componentIri,
            primaryParameter,
            dispatcher.Build,
            optionalCompanions);
    }

    /// <inheritdoc/>
    public bool Equals(ConstraintComponentInfo other) => ComponentIri.Equals(other.ComponentIri);

    /// <inheritdoc/>
    public override int GetHashCode() => ComponentIri.GetHashCode();
}
