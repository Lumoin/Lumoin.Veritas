using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>The datatype operation a <see cref="DatatypeQuestion"/> folds.</summary>
public enum DatatypeOperation
{
    /// <summary>The emptiness question over the carried conjunction.</summary>
    DecideConjunction,

    /// <summary>The membership question for the first carried literal.</summary>
    Contains,

    /// <summary>The identity question for the two carried literals.</summary>
    SameValue,

    /// <summary>The counting question over the carried conjunction.</summary>
    DistinctValues,
}

/// <summary>
/// The folded question an operator's datatype oracle answers: an operation kind, the conjunction the
/// emptiness and counting operations range over, and up to two literals the membership and identity
/// operations range over. One carrier folds all four <see cref="RegisteredDatatype"/> operations so the
/// escape hatch is a single delegate binding rather than four.
/// </summary>
/// <param name="Operation">Which operation is asked.</param>
/// <param name="Conjunction">The conjunction, for the emptiness and counting operations.</param>
/// <param name="First">The first literal, for the membership and identity operations.</param>
/// <param name="Second">The second literal, for the identity operation.</param>
public readonly record struct DatatypeQuestion(
    DatatypeOperation Operation,
    DatatypeConjunction Conjunction,
    Literal? First,
    Literal? Second);

/// <summary>
/// The folded answer a datatype oracle returns: the verdict fitting the asked operation, with the other
/// verdict slots left at their abstention defaults, and an optional witness literal a satisfiability or
/// membership answer may carry. The static factories fill exactly the fitting slot.
/// </summary>
/// <param name="Satisfiability">The emptiness verdict, for a <see cref="DatatypeOperation.DecideConjunction"/> answer.</param>
/// <param name="Membership">The membership verdict, for a <see cref="DatatypeOperation.Contains"/> answer.</param>
/// <param name="Identity">The identity verdict, for a <see cref="DatatypeOperation.SameValue"/> answer.</param>
/// <param name="Count">The distinct-value bound, for a <see cref="DatatypeOperation.DistinctValues"/> answer.</param>
/// <param name="Witness">An optional witness literal a satisfiability or membership answer may carry.</param>
public readonly record struct DatatypeAnswer(
    DatatypeSatisfiability Satisfiability,
    DatatypeMembership Membership,
    DatatypeValueIdentity Identity,
    DatatypeCountBound Count,
    Literal? Witness)
{
    /// <summary>An emptiness answer.</summary>
    /// <param name="satisfiability">The emptiness verdict.</param>
    /// <param name="witness">An optional witness value.</param>
    /// <returns>The answer.</returns>
    public static DatatypeAnswer ForConjunction(DatatypeSatisfiability satisfiability, Literal? witness = null)
    {
        return new DatatypeAnswer(satisfiability, DatatypeMembership.Indeterminate, DatatypeValueIdentity.Indeterminate, DatatypeCountBound.Unknown, witness);
    }

    /// <summary>A membership answer.</summary>
    /// <param name="membership">The membership verdict.</param>
    /// <param name="witness">An optional witness value.</param>
    /// <returns>The answer.</returns>
    public static DatatypeAnswer ForContains(DatatypeMembership membership, Literal? witness = null)
    {
        return new DatatypeAnswer(DatatypeSatisfiability.Unknown, membership, DatatypeValueIdentity.Indeterminate, DatatypeCountBound.Unknown, witness);
    }

    /// <summary>An identity answer.</summary>
    /// <param name="identity">The identity verdict.</param>
    /// <returns>The answer.</returns>
    public static DatatypeAnswer ForSameValue(DatatypeValueIdentity identity)
    {
        return new DatatypeAnswer(DatatypeSatisfiability.Unknown, DatatypeMembership.Indeterminate, identity, DatatypeCountBound.Unknown, null);
    }

    /// <summary>A counting answer.</summary>
    /// <param name="count">The distinct-value bound.</param>
    /// <returns>The answer.</returns>
    public static DatatypeAnswer ForCount(DatatypeCountBound count)
    {
        return new DatatypeAnswer(DatatypeSatisfiability.Unknown, DatatypeMembership.Indeterminate, DatatypeValueIdentity.Indeterminate, count, null);
    }
}

/// <summary>
/// The computational escape hatch: an operator-supplied oracle that answers any of the four datatype
/// operations for a registered datatype. Named rather than a bare functional so the binding is a
/// discoverable type; implementors bind their state in an explicit frame and pass a method group, never
/// a capturing lambda.
/// </summary>
/// <param name="question">The folded question.</param>
/// <returns>The folded answer.</returns>
public delegate DatatypeAnswer DatatypeOracleDelegate(in DatatypeQuestion question);
