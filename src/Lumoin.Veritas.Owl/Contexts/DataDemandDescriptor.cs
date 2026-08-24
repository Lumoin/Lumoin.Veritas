using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The kind of concrete-domain demand a context data-demand marker atom stands
/// for — the four obligation shapes <see cref="Lumoin.Veritas.Owl.Reasoning.DataRestrictionConsistency"/>
/// buckets: an existential value demand, a universal value constraint, a
/// minimum-cardinality counting demand, and a maximum-cardinality counting
/// bound.
/// </summary>
internal enum DataDemandKind
{
    /// <summary>An existential value demand <c>∃d.R</c> (a lowered <c>DataSomeValuesFrom</c> or <c>DataHasValue</c>).</summary>
    Existential,

    /// <summary>A universal value constraint <c>∀d.R</c> (a lowered <c>DataAllValuesFrom</c>).</summary>
    Universal,

    /// <summary>A minimum-cardinality counting demand <c>≥n d.R</c> (a lowered positive <c>DataMinCardinality</c>, or the minimum half of a positive <c>DataExactCardinality</c>).</summary>
    MinCardinality,

    /// <summary>A maximum-cardinality counting bound <c>≤n d.R</c> (a lowered positive <c>DataMaxCardinality</c>, or the maximum half of a positive <c>DataExactCardinality</c>). It forces no value of its own — a node with no filler satisfies it vacuously — so it never carries the per-property value-existence companion.</summary>
    MaxCardinality,
}

/// <summary>
/// The concrete-domain obligation a context data-demand marker atom records: the
/// data property it constrains, the demand kind, the counting bound (a
/// <see cref="DataDemandKind.MinCardinality"/>'s or
/// <see cref="DataDemandKind.MaxCardinality"/>'s <c>n</c>, zero otherwise), and
/// the data range. The context clausifier mints one marker atom per distinct
/// descriptor and rides the descriptor table on <see cref="ClausificationResult"/>;
/// the saturation engine reconstructs the descriptor's obligation as the
/// <c>AlcConcept</c> the shared datatype sidecar decides, so a demand decided
/// through the context arm is byte-identical to the same demand decided through the
/// tableau arms.
/// </summary>
/// <param name="Property">The demanding data-property IRI.</param>
/// <param name="Kind">The demand kind.</param>
/// <param name="Count">The counting bound for a <see cref="DataDemandKind.MinCardinality"/> or <see cref="DataDemandKind.MaxCardinality"/> descriptor; zero for the others.</param>
/// <param name="Range">The data range the demanded value lies in.</param>
internal readonly record struct DataDemandDescriptor(Utf8String Property, DataDemandKind Kind, int Count, OwlDataRange Range);
