using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Components;

/// <summary>
/// Configuration-carrying factory for <see cref="DynamicConstraint"/>.
/// One instance is created per dynamically-registered component; its
/// <see cref="Build"/> method is exposed as the
/// <see cref="ConstraintComponentFactory"/> delegate inside the
/// associated <see cref="ConstraintComponentInfo"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dispatcher rather than a static lambda.</b> The built-in
/// factories are static lambdas by convention: no captures, single
/// method pointer, no per-invocation allocation. A
/// <see cref="DynamicConstraint"/> factory cannot be purely static
/// because it depends on per-component configuration (which parameters
/// name shapes) that must be supplied at registration time.
/// Encapsulating that configuration on a dedicated object — with
/// <see cref="Build"/> as the delegate target — keeps the captured
/// state explicit, localized, and inspectable, rather than baking it
/// into an implicit lambda closure.
/// </para>
/// <para>
/// <b>Per-invocation cost.</b> Invoking <see cref="Build"/> performs
/// one dictionary-builder creation per parameter bucket (scalars,
/// lists), allocates the resulting <see cref="ImmutableDictionary{TKey, TValue}"/>
/// and <see cref="ImmutableArray{T}"/> instances, and returns a
/// <see cref="DynamicConstraint"/> record. This is more work than a
/// built-in factory's direct record construction, and it is exactly
/// the cost the user accepts when choosing a dynamic constraint over
/// a typed one. The graduation path from
/// <see cref="DynamicConstraint"/> to a compiled typed record
/// eliminates this per-invocation allocation.
/// </para>
/// </remarks>
internal sealed class DynamicFactoryDispatcher
{
    /// <summary>
    /// Initializes the dispatcher with the component's identity and
    /// the set of parameter IRIs that, when captured, contribute to
    /// <see cref="DynamicConstraint.ReferencedShapeIdsStorage"/>.
    /// </summary>
    /// <param name="componentIri">The component IRI.</param>
    /// <param name="shapeTypedParameters">
    /// Parameter IRIs whose captured term-id values are shape
    /// references. The set is copied into an
    /// <see cref="ImmutableHashSet{T}"/> keyed by byte-content
    /// <see cref="Utf8String"/> equality.
    /// </param>
    public DynamicFactoryDispatcher(
        Utf8String componentIri,
        ImmutableArray<Utf8String> shapeTypedParameters)
    {
        ComponentIri = componentIri;
        ShapeTypedParameters = shapeTypedParameters.IsDefaultOrEmpty
            ? ImmutableHashSet<Utf8String>.Empty
            : shapeTypedParameters.ToImmutableHashSet();
    }

    /// <summary>The component IRI emitted on constructed constraints.</summary>
    private Utf8String ComponentIri { get; }

    /// <summary>
    /// Parameter IRIs whose captured term-id values contribute to
    /// <see cref="DynamicConstraint.ReferencedShapeIdsStorage"/>.
    /// Lookup uses byte-content equality on <see cref="Utf8String"/>.
    /// </summary>
    private ImmutableHashSet<Utf8String> ShapeTypedParameters { get; }

    /// <summary>
    /// Factory entry point. Captures every declared parameter from the
    /// bag into the two storage dictionaries of the resulting
    /// <see cref="DynamicConstraint"/>, and computes
    /// <see cref="DynamicConstraint.ReferencedShapeIdsStorage"/> from
    /// captures whose parameter IRI names a shape-typed parameter.
    /// </summary>
    /// <param name="bag">The parameter bag for this invocation.</param>
    /// <returns>A populated <see cref="DynamicConstraint"/>.</returns>
    public ConstraintComponent Build(ParameterBag bag)
    {
        ImmutableDictionary<IriId, TermId>.Builder scalars = ImmutableDictionary.CreateBuilder<IriId, TermId>();
        ImmutableDictionary<IriId, ImmutableArray<TermId>>.Builder lists = ImmutableDictionary.CreateBuilder<IriId, ImmutableArray<TermId>>();
        ImmutableArray<TermId>.Builder shapeIds = ImmutableArray.CreateBuilder<TermId>();

        //Capture the primary parameter.
        if(bag.PrimaryValueIsList)
        {
            ImmutableArray<TermId> members = bag.RequirePrimaryListMembers();
            lists.Add(bag.PrimaryParameter, members);

            if(IsShapeTyped(bag.PrimaryParameter, bag.Dictionary))
            {
                shapeIds.AddRange(members);
            }
        }
        else
        {
            scalars.Add(bag.PrimaryParameter, bag.PrimaryValue);

            if(IsShapeTyped(bag.PrimaryParameter, bag.Dictionary))
            {
                shapeIds.Add(bag.PrimaryValue);
            }
        }

        //Capture scalar companions.
        foreach(KeyValuePair<IriId, TermId> entry in bag.EnumerateCompanionScalars())
        {
            scalars.Add(entry.Key, entry.Value);

            if(IsShapeTyped(entry.Key, bag.Dictionary))
            {
                shapeIds.Add(entry.Value);
            }
        }

        //Capture list companions.
        foreach(KeyValuePair<IriId, ImmutableArray<TermId>> entry in bag.EnumerateCompanionLists())
        {
            lists.Add(entry.Key, entry.Value);

            if(IsShapeTyped(entry.Key, bag.Dictionary))
            {
                shapeIds.AddRange(entry.Value);
            }
        }

        return new DynamicConstraint(
            ComponentIri,
            scalars.ToImmutable(),
            lists.ToImmutable(),
            shapeIds.ToImmutable());
    }

    private bool IsShapeTyped(IriId parameter, TermDictionary dictionary)
    {
        if(ShapeTypedParameters.Count == 0)
        {
            return false;
        }

        //IriId → Utf8String lookup via the dictionary. Dictionary.Resolve
        //is the canonical way to get back the IRI bytes.
        if(dictionary.Resolve(parameter) is NamedNode named)
        {
            return ShapeTypedParameters.Contains(named.Iri);
        }

        return false;
    }
}
