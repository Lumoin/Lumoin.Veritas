using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>Tests a property of an ordered pair of literal term identifiers (distinctness, or a literal lying outside a datatype's value space).</summary>
/// <param name="first">The first literal term identifier.</param>
/// <param name="second">The second term identifier; a literal for the distinctness test, a datatype for the outside-datatype test.</param>
/// <returns><see langword="true"/> when the tested property holds.</returns>
public delegate bool LiteralPairPredicate(TermId first, TermId second);

/// <summary>Returns the datatype-map members whose value space contains the intersection of two datatypes' spaces.</summary>
/// <param name="first">The first datatype term identifier.</param>
/// <param name="second">The second datatype term identifier.</param>
/// <returns>The superset datatype identifiers; empty when unknown.</returns>
public delegate IReadOnlyCollection<TermId> DatatypeRangeIntersection(TermId first, TermId second);

/// <summary>
/// The datatype knowledge the term-id-level RL closure consults from
/// outside — the engine never inspects literal values or value spaces
/// itself. Every member answers unknown (<see langword="false"/> or empty)
/// for terms that are not literals or datatypes, so a <c>null</c>-free
/// oracle is safe over any input.
/// </summary>
/// <param name="LiteralsKnownDistinct">Whether two terms are literals known to denote distinct data values — an <c>owl:sameAs</c> between them is the <c>dt-diff</c> contradiction.</param>
/// <param name="LiteralOutsideDatatype">Whether a term is a literal known to lie outside a datatype's value space — a range or universal-restriction typing onto it is the <c>dt-not-type</c> contradiction.</param>
/// <param name="RangeIntersectionSupersets">The datatype-map members whose value space contains the intersection of two datatypes' spaces — the <c>dt-range-intersection</c> extension derives them as additional ranges; empty means unknown.</param>
[DebuggerDisplay("OwlRlDatatypeOracle")]
public readonly record struct OwlRlDatatypeOracle(
    LiteralPairPredicate LiteralsKnownDistinct,
    LiteralPairPredicate LiteralOutsideDatatype,
    DatatypeRangeIntersection RangeIntersectionSupersets)
{
    /// <summary>The oracle that knows nothing: no literal pair is distinct, no literal is outside any datatype, no intersection is known — the <c>dt-*</c> rules never fire.</summary>
    public static OwlRlDatatypeOracle None { get; } = new(static (_, _) => false, static (_, _) => false, static (_, _) => []);
}
