using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

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
    Func<TermId, TermId, bool> LiteralsKnownDistinct,
    Func<TermId, TermId, bool> LiteralOutsideDatatype,
    Func<TermId, TermId, IReadOnlyCollection<TermId>> RangeIntersectionSupersets)
{
    /// <summary>The oracle that knows nothing: no literal pair is distinct, no literal is outside any datatype, no intersection is known — the <c>dt-*</c> rules never fire.</summary>
    public static OwlRlDatatypeOracle None { get; } = new(static (_, _) => false, static (_, _) => false, static (_, _) => []);
}
