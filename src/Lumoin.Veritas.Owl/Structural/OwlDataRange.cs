using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// An OWL 2 data range: a named datatype or one of the data-range
/// constructors (<c>DataIntersectionOf</c>, <c>DataUnionOf</c>,
/// <c>DataComplementOf</c>, <c>DataOneOf</c>, <c>DatatypeRestriction</c>).
/// </summary>
public abstract record OwlDataRange;

/// <summary>A named datatype used as a data range.</summary>
/// <param name="Datatype">The datatype IRI node.</param>
[DebuggerDisplay("Datatype {Datatype}")]
public sealed record OwlDatatypeReference(NamedNode Datatype): OwlDataRange;

/// <summary>The intersection of data ranges (<c>DataIntersectionOf</c>).</summary>
/// <param name="Ranges">The operand ranges, in declaration order.</param>
[DebuggerDisplay("DataIntersectionOf ({Ranges.Count})")]
public sealed record OwlDataIntersectionOf(IReadOnlyList<OwlDataRange> Ranges): OwlDataRange;

/// <summary>The union of data ranges (<c>DataUnionOf</c>).</summary>
/// <param name="Ranges">The operand ranges, in declaration order.</param>
[DebuggerDisplay("DataUnionOf ({Ranges.Count})")]
public sealed record OwlDataUnionOf(IReadOnlyList<OwlDataRange> Ranges): OwlDataRange;

/// <summary>The complement of a data range (<c>DataComplementOf</c>).</summary>
/// <param name="Range">The complemented range.</param>
[DebuggerDisplay("DataComplementOf")]
public sealed record OwlDataComplementOf(OwlDataRange Range): OwlDataRange;

/// <summary>An enumeration of literals (<c>DataOneOf</c>).</summary>
/// <param name="Literals">The enumerated literals, in declaration order.</param>
[DebuggerDisplay("DataOneOf ({Literals.Count})")]
public sealed record OwlDataOneOf(IReadOnlyList<Literal> Literals): OwlDataRange;

/// <summary>A datatype restricted by facets (<c>DatatypeRestriction</c>).</summary>
/// <param name="Datatype">The restricted datatype IRI node.</param>
/// <param name="Restrictions">The facet–value pairs, in declaration order.</param>
[DebuggerDisplay("DatatypeRestriction {Datatype} ({Restrictions.Count})")]
public sealed record OwlDatatypeRestriction(NamedNode Datatype, IReadOnlyList<OwlFacetRestriction> Restrictions): OwlDataRange;

/// <summary>One facet–value pair of a <c>DatatypeRestriction</c>.</summary>
/// <param name="Facet">The constraining facet IRI node (an <c>xsd:</c> facet such as <c>xsd:minInclusive</c>).</param>
/// <param name="Value">The facet value literal.</param>
[DebuggerDisplay("Facet {Facet}")]
public readonly record struct OwlFacetRestriction(NamedNode Facet, Literal Value);
