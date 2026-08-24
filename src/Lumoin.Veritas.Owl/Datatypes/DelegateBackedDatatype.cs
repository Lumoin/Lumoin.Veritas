using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype whose four operations are answered by an operator-supplied
/// <see cref="DatatypeOracleDelegate"/>. It is the computational escape hatch for value spaces the
/// declarative tier cannot express. It cannot be self-tested — there is no compiled automaton to compare
/// against a naive oracle — so it is flagged <see cref="SelfCertified"/>: the operator carries the
/// differential-battery obligation, and the provenance surfaces in module diagnostics the first time it
/// decides. Admissibility does not structurally test it.
/// </summary>
public sealed class DelegateBackedDatatype : RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The operator-supplied oracle.</summary>
    private DatatypeOracleDelegate Oracle { get; }

    /// <summary>Creates a delegate-backed datatype.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="oracle">The oracle answering the four operations.</param>
    public DelegateBackedDatatype(Utf8String datatypeIri, DatatypeOracleDelegate oracle)
    {
        ArgumentNullException.ThrowIfNull(oracle);
        Iri = datatypeIri;
        Oracle = oracle;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override bool SelfCertified => true;

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        DatatypeQuestion asked = new(DatatypeOperation.DecideConjunction, question, null, null);

        return Oracle(in asked).Satisfiability;
    }

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);
        DatatypeQuestion asked = new(DatatypeOperation.Contains, DatatypeConjunction.Empty, value, null);

        return Oracle(in asked).Membership;
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        DatatypeQuestion asked = new(DatatypeOperation.SameValue, DatatypeConjunction.Empty, first, second);

        return Oracle(in asked).Identity;
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        DatatypeQuestion asked = new(DatatypeOperation.DistinctValues, question, null, null);

        return Oracle(in asked).Count;
    }
}
