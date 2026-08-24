using System.Collections.Generic;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// One registered value-layer datatype. A definition owns an IRI and answers the two questions the RDF
/// value layer asks of a lexical space: the lexical validity of one form, and the value identity of two
/// forms under the same datatype IRI. Every answer is three-valued with abstention at ordinal zero, so a
/// definition that cannot decide a question leaves the engine's built-in semantics standing rather than
/// guessing. This is the value layer's own extension surface, distinct by design from the OWL
/// concrete-domain registry (<c>Lumoin.Veritas.Owl.Datatypes.DatatypeRegistry</c>): it never answers
/// facet-conjunction satisfiability or distinct-value counting, and the reasoner arms never consult it.
/// </summary>
public abstract class ValueDatatype
{
    /// <summary>The empty probe list a definition that declares no probes shares.</summary>
    private static IReadOnlyList<Utf8String> EmptyProbes { get; } = [];

    /// <summary>The datatype IRI this definition owns.</summary>
    public abstract Utf8String DatatypeIri { get; }

    /// <summary>The operations this definition declares it answers; registration rejects an empty declaration.</summary>
    public abstract ValueDatatypeFacets Facets { get; }

    /// <summary>
    /// The probe lexical forms the registration-time law check exercises <see cref="SameValue"/> over — at
    /// most <see cref="ValueDatatypeLaws.ProbeBudget"/> of them. The default declares none, so the law
    /// check passes vacuously.
    /// </summary>
    public virtual IReadOnlyList<Utf8String> Probes => EmptyProbes;

    /// <summary>
    /// Whether this definition is trusted on the operator's word — the delegate-backed escape hatch, whose
    /// answers no recognizer backs. The operator carries the differential-battery obligation; the bounded
    /// law check still runs over the declared probes at registration.
    /// </summary>
    public virtual bool SelfCertified => false;

    /// <summary>Decides the three-valued lexical validity of one lexical form in this datatype's lexical space.</summary>
    /// <param name="lexicalForm">The candidate lexical form.</param>
    /// <returns>The validity verdict; <see cref="ValueLexicalValidity.Indeterminate"/> when the definition cannot decide it.</returns>
    public abstract ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm);

    /// <summary>Decides the three-valued value identity of two lexical forms within this datatype.</summary>
    /// <param name="first">The first lexical form.</param>
    /// <param name="second">The second lexical form.</param>
    /// <returns>The identity verdict; <see cref="ValueIdentity.Indeterminate"/> when the definition cannot decide it.</returns>
    public abstract ValueIdentity SameValue(Utf8String first, Utf8String second);
}
