using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// The base type for all RDF terms: named nodes (IRIs), blank nodes, literals, and triple terms.
/// </summary>
/// <remarks>
/// <para>
/// RDF terms are the building blocks of RDF graphs. Every position in a triple (subject,
/// predicate, object) is occupied by an RDF term. The concrete subtypes correspond to
/// the four kinds of RDF term defined in
/// <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Syntax">RDF 1.2 Concepts §3</see>.
/// </para>
/// <para>
/// Use pattern matching to dispatch on the concrete type:
/// </para>
/// <code>
/// var label = term switch
/// {
///     NamedNode(var iri) => iri.ToString(),
///     BlankNode(var id) => $"_:{id}",
///     Literal(var value, _, _, _) => value.ToString(),
///     TripleTerm(var s, var p, var o) => $"&lt;&lt;{s} {p} {o}&gt;&gt;",
///     _ => throw new UnreachableException()
/// };
/// </code>
/// </remarks>
public abstract record RdfTerm;

/// <summary>
/// An IRI-identified resource in the RDF graph.
/// </summary>
/// <remarks>
/// <para>
/// Named nodes (also called IRI nodes) are the primary mechanism for identifying
/// resources. The IRI is stored as raw UTF-8 bytes for zero-copy processing.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-IRIs">RDF 1.2 Concepts §3.1</see>.
/// </para>
/// </remarks>
/// <param name="Iri">The IRI identifying this resource.</param>
[DebuggerDisplay("<{Iri}>")]
public sealed record NamedNode(Utf8String Iri): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"<{Iri}>";
    }
}

/// <summary>
/// A locally-scoped identifier that does not persist beyond the document in which it appears.
/// </summary>
/// <remarks>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-blank-nodes">RDF 1.2 Concepts §3.4</see>.
/// </remarks>
/// <param name="Label">The blank node identifier, without the <c>_:</c> prefix.</param>
[DebuggerDisplay("_:{Label}")]
public sealed record BlankNode(Utf8String Label): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"_:{Label}";
    }
}

/// <summary>
/// The base text direction for directional language-tagged strings.
/// </summary>
/// <remarks>
/// <para>
/// Introduced in RDF 1.2 to support the correct rendering of bidirectional text.
/// A directional language-tagged string contains a base direction component
/// that establishes the initial text direction for presentation by a user agent.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Literal">RDF 1.2 Concepts §3.3</see>.
/// </para>
/// </remarks>
public enum TextDirection
{
    /// <summary>Left-to-right base direction.</summary>
    Ltr,

    /// <summary>Right-to-left base direction.</summary>
    Rtl
}

/// <summary>
/// An RDF literal value with a datatype IRI, an optional language tag, and an optional base direction.
/// </summary>
/// <remarks>
/// <para>
/// Every literal has a datatype. Plain strings use <c>xsd:string</c>,
/// language-tagged strings use <c>rdf:langString</c>, and directional
/// language-tagged strings use <c>rdf:dirLangString</c> (RDF 1.2).
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Literal">RDF 1.2 Concepts §3.3</see>.
/// </para>
/// </remarks>
/// <param name="Value">The lexical value of the literal.</param>
/// <param name="Datatype">The datatype IRI as a <see cref="NamedNode"/>.</param>
/// <param name="Language">
/// The optional BCP47 language tag. Present only when the datatype is
/// <c>rdf:langString</c> or <c>rdf:dirLangString</c>.
/// </param>
/// <param name="BaseDirection">
/// The optional base text direction for directional language-tagged strings (RDF 1.2).
/// Present only when the datatype is <c>rdf:dirLangString</c>.
/// </param>
[DebuggerDisplay("{ToString()}")]
public sealed record Literal(
    Utf8String Value,
    NamedNode Datatype,
    Utf8String? Language = null,
    TextDirection? BaseDirection = null): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        if(Language is { } lang)
        {
            return $"\"{Value}\"@{lang}";
        }

        return $"\"{Value}\"^^<{Datatype.Iri}>";
    }
}

/// <summary>
/// An RDF triple used as a term in the object position of another triple.
/// </summary>
/// <remarks>
/// <para>
/// Triple terms are new in RDF 1.2. They enable statements about statements
/// without the indirection of classic reification. A triple term is an RDF
/// triple that can appear as the object of another triple, typically with
/// the predicate <c>rdf:reifies</c>.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-triple-terms">RDF 1.2 Concepts §3.5</see>.
/// </para>
/// <para>
/// A triple term is not itself an RDF triple in the graph. It is a term that
/// denotes a triple. The components of a triple term follow the same constraints
/// as the components of an RDF triple: the subject must be a named node, blank node,
/// or triple term; the predicate must be a named node; the object can be any RDF term.
/// </para>
/// </remarks>
/// <param name="Subject">The subject of the denoted triple.</param>
/// <param name="Predicate">The predicate of the denoted triple.</param>
/// <param name="Object">The object of the denoted triple.</param>
[DebuggerDisplay("<<{Subject} {Predicate} {Object}>>")]
public sealed record TripleTerm(RdfTerm Subject, NamedNode Predicate, RdfTerm Object): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"<<{Subject} {Predicate} {Object}>>";
    }
}
