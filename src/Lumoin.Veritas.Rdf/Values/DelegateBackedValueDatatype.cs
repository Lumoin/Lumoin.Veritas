using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// A value datatype whose two operations are answered by an operator-supplied
/// <see cref="ValueDatatypeOracleDelegate"/> — the escape hatch for lexical spaces the engine does not
/// model. No recognizer backs its answers, so it is flagged <see cref="SelfCertified"/> and the operator
/// carries the differential-battery obligation; the bounded law check still runs over its declared probes
/// at registration.
/// </summary>
public sealed class DelegateBackedValueDatatype : ValueDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The declared operations.</summary>
    private ValueDatatypeFacets DeclaredFacets { get; }

    /// <summary>The declared probe lexical forms for the registration-time law check.</summary>
    private IReadOnlyList<Utf8String> DeclaredProbes { get; }

    /// <summary>The operator-supplied oracle.</summary>
    private ValueDatatypeOracleDelegate Oracle { get; }

    /// <summary>Creates a delegate-backed value datatype.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="facets">The declared operations.</param>
    /// <param name="probes">The probe lexical forms the registration-time law check exercises; empty declares none.</param>
    /// <param name="oracle">The oracle answering both operations.</param>
    public DelegateBackedValueDatatype(Utf8String datatypeIri, ValueDatatypeFacets facets, IReadOnlyList<Utf8String> probes, ValueDatatypeOracleDelegate oracle)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(oracle);
        Iri = datatypeIri;
        DeclaredFacets = facets;
        DeclaredProbes = probes;
        Oracle = oracle;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => DeclaredFacets;

    /// <inheritdoc/>
    public override IReadOnlyList<Utf8String> Probes => DeclaredProbes;

    /// <inheritdoc/>
    public override bool SelfCertified => true;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        ValueDatatypeQuestion asked = new(ValueDatatypeOperation.ValidateLexicalForm, lexicalForm, default);

        return Oracle(in asked).Validity;
    }

    /// <inheritdoc/>
    public override ValueIdentity SameValue(Utf8String first, Utf8String second)
    {
        ValueDatatypeQuestion asked = new(ValueDatatypeOperation.SameValue, first, second);

        return Oracle(in asked).Identity;
    }
}
