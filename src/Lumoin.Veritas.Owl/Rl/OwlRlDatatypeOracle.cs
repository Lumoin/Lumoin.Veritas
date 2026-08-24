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

/// <summary>Tests a property of an ordered pair of datatype term identifiers (value-space disjointness, or an alias-and-target retype admission).</summary>
/// <param name="first">The first datatype term identifier.</param>
/// <param name="second">The second datatype term identifier.</param>
/// <returns><see langword="true"/> when the tested property holds.</returns>
public delegate bool DatatypePairPredicate(TermId first, TermId second);

/// <summary>Retypes a literal from an alias datatype IRI onto a recognized datatype-map member, minting the retyped literal term.</summary>
/// <param name="literal">The candidate literal term identifier.</param>
/// <param name="alias">The alias side of the identity — the IRI the literal is typed with.</param>
/// <param name="target">The target side of the identity — the datatype-map member the literal retypes onto.</param>
/// <returns>The minted retyped literal's identifier, or <see cref="TermId.None"/> when the retype is refused.</returns>
public delegate TermId DatatypeLiteralRetype(TermId literal, TermId alias, TermId target);

/// <summary>Reads the nonnegative-integer value and the datatype term of an exact-tower integral literal.</summary>
/// <param name="literal">The candidate literal term identifier.</param>
/// <param name="value">The literal's value, when the read succeeds.</param>
/// <param name="datatype">The literal's datatype term identifier, when the read succeeds.</param>
/// <returns><see langword="true"/> when the term is a plain literal of the exact numeric tower denoting an integral, nonnegative value that fits a <see cref="long"/>; language-tagged, non-literal, float, double, non-integral, negative, and oversized terms refuse.</returns>
public delegate bool LiteralNonNegativeIntegerReader(TermId literal, out long value, out TermId datatype);

/// <summary>Mints the invariant digit literal of a nonnegative integer typed by a datatype.</summary>
/// <param name="value">The nonnegative integer value.</param>
/// <param name="datatype">The datatype term identifier typing the minted literal.</param>
/// <returns>The minted literal's identifier, or <see cref="TermId.None"/> when the datatype is not an integer datatype of the numeric interval map or the value lies outside its value space.</returns>
public delegate TermId NonNegativeIntegerLiteralMinter(long value, TermId datatype);

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
/// <param name="DatatypesKnownDisjoint">Whether two terms are datatypes with known-disjoint value spaces — a property ranged by both has a provably empty extension, the disjoint-range completion's premise; and an <c>owl:sameAs</c> between them is the <c>dt-disjoint-identity</c> contradiction the equality scans consult.</param>
/// <param name="DatatypeAliasRecognized">Whether an ordered pair is an alias-and-target retype admission: the alias an IRI outside every modelled datatype classification, the target a datatype-map member whose lexical validity is genuinely decidable — the datatype-alias-retype completion's per-pair gate.</param>
/// <param name="DatatypeAliasRetype">The minted retype of an alias-typed literal onto the recognized target of an admitted pair, or <see cref="TermId.None"/> when refused — the datatype-alias-retype completion's per-literal step.</param>
/// <param name="LiteralsKnownEqual">Whether two terms are literals known to denote the SAME data value within one value space — the <c>dt-eq</c> direction the entailment surface's value-identity bridge consults.</param>
/// <param name="LiteralNonNegativeInteger">The exact-tower integral read of a bound literal — the <c>fibre-cardinality-certificate</c> completion's value read, surfacing the value and the datatype term every contributing pin carries.</param>
/// <param name="NonNegativeIntegerLiteral">The minted digit literal of a proven count typed by the contributing pins' datatype, or <see cref="TermId.None"/> when refused — the <c>fibre-cardinality-certificate</c> completion's read-back mint.</param>
[DebuggerDisplay("OwlRlDatatypeOracle")]
public readonly record struct OwlRlDatatypeOracle(
    LiteralPairPredicate LiteralsKnownDistinct,
    LiteralPairPredicate LiteralOutsideDatatype,
    DatatypeRangeIntersection RangeIntersectionSupersets,
    DatatypePairPredicate DatatypesKnownDisjoint,
    DatatypePairPredicate DatatypeAliasRecognized,
    DatatypeLiteralRetype DatatypeAliasRetype,
    LiteralPairPredicate LiteralsKnownEqual,
    LiteralNonNegativeIntegerReader LiteralNonNegativeInteger,
    NonNegativeIntegerLiteralMinter NonNegativeIntegerLiteral)
{
    /// <summary>The oracle that knows nothing: no literal pair is distinct or equal, no literal is outside any datatype, no intersection is known, no datatype pair is disjoint, no alias pair is admitted, no retype is minted, no bound reads, no count literal is minted — the <c>dt-*</c> rules never fire.</summary>
    public static OwlRlDatatypeOracle None { get; } = new(
        static (_, _) => false,
        static (_, _) => false,
        static (_, _) => [],
        static (_, _) => false,
        static (_, _) => false,
        static (_, _, _) => TermId.None,
        static (_, _) => false,
        static (TermId _, out long value, out TermId datatype) =>
        {
            value = 0;
            datatype = TermId.None;

            return false;
        },
        static (_, _) => TermId.None);
}
