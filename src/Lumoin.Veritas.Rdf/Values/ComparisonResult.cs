namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Outcome of comparing two RDF values under SPARQL ordering
/// semantics.
/// </summary>
/// <remarks>
/// <para>
/// Per SPARQL 1.1 §17.4.1, the relational operators are defined for
/// values within the same value space (numeric tower, string,
/// boolean, date/time family, duration family) and produce a type
/// error otherwise. This enum models the type-error case as
/// <see cref="Incomparable"/> rather than throwing, letting callers
/// (SHACL constraint evaluators, future SPARQL filter machinery)
/// decide whether the absence of an ordering should be treated as
/// non-conformance, as a filter rejection, or as something else.
/// </para>
/// <para>
/// <see cref="Incomparable"/> arises in several distinct scenarios:
/// </para>
/// <list type="bullet">
///   <item><description>The two operands lie in different value spaces (e.g. <c>xsd:integer</c> vs <c>xsd:string</c>).</description></item>
///   <item><description>One or both operands have an ill-formed lexical form for their declared datatype.</description></item>
///   <item><description>One operand is not a literal at all (IRI or blank node compared to a numeric threshold).</description></item>
///   <item><description>An operand is <c>NaN</c> (per IEEE 754, <c>NaN</c> is unordered with respect to every value including itself).</description></item>
///   <item><description>A timezone-naive <c>xsd:dateTime</c> is compared with a timezone-aware one and their possible-instant ranges overlap (XSD §3.2.7 indeterminate case).</description></item>
///   <item><description>An <c>xsd:duration</c> comparison whose ordering depends on month length cannot be decided (XSD §3.2.6 partial-order case).</description></item>
/// </list>
/// </remarks>
public enum ComparisonResult
{
    /// <summary>
    /// The first operand is strictly less than the second.
    /// </summary>
    Less,

    /// <summary>
    /// The two operands compare equal in their shared value space.
    /// </summary>
    Equal,

    /// <summary>
    /// The first operand is strictly greater than the second.
    /// </summary>
    Greater,

    /// <summary>
    /// The operands are not orderable. See enum-level remarks for
    /// the conditions that produce this outcome.
    /// </summary>
    Incomparable,
}
