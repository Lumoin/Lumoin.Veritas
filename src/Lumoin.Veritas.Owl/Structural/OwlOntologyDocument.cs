using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// The result of mapping an RDF graph to OWL 2 structural form: the axioms,
/// the per-kind declaration index the mapping disambiguated with, and the
/// diagnostics the mapping recorded. Mapping is value-based — a graph that is
/// not structurally an OWL 2 ontology yields diagnostics and the axioms that
/// did map, never a throw.
/// </summary>
[DebuggerDisplay("OwlOntologyDocument Axioms={Axioms.Length} Errors={Diagnostics.HasErrors}")]
public sealed class OwlOntologyDocument
{
    /// <summary>The mapped axioms, in graph-traversal order.</summary>
    public ImmutableArray<OwlAxiom> Axioms { get; }

    /// <summary>The ontology IRI, when the graph declares an <c>owl:Ontology</c> node with one; otherwise <c>null</c>.</summary>
    public NamedNode? OntologyIri { get; }

    /// <summary>The diagnostics recorded during mapping. Errors mean the graph is not structurally a well-formed OWL 2 ontology.</summary>
    public DiagnosticBag Diagnostics { get; }

    /// <summary>The IRIs declared as classes (built-ins included).</summary>
    public IReadOnlySet<Utf8String> DeclaredClasses { get; }

    /// <summary>The IRIs declared as object properties.</summary>
    public IReadOnlySet<Utf8String> DeclaredObjectProperties { get; }

    /// <summary>The IRIs declared as data properties.</summary>
    public IReadOnlySet<Utf8String> DeclaredDataProperties { get; }

    /// <summary>The IRIs declared as annotation properties (built-ins included).</summary>
    public IReadOnlySet<Utf8String> DeclaredAnnotationProperties { get; }

    /// <summary>The IRIs declared as datatypes.</summary>
    public IReadOnlySet<Utf8String> DeclaredDatatypes { get; }

    /// <summary>
    /// Initialises the document from the mapper's results.
    /// </summary>
    /// <param name="axioms">The mapped axioms.</param>
    /// <param name="ontologyIri">The ontology IRI, or <c>null</c>.</param>
    /// <param name="diagnostics">The mapping diagnostics.</param>
    /// <param name="declaredClasses">The class declaration index.</param>
    /// <param name="declaredObjectProperties">The object-property declaration index.</param>
    /// <param name="declaredDataProperties">The data-property declaration index.</param>
    /// <param name="declaredAnnotationProperties">The annotation-property declaration index.</param>
    /// <param name="declaredDatatypes">The datatype declaration index.</param>
    public OwlOntologyDocument(
        ImmutableArray<OwlAxiom> axioms,
        NamedNode? ontologyIri,
        DiagnosticBag diagnostics,
        IReadOnlySet<Utf8String> declaredClasses,
        IReadOnlySet<Utf8String> declaredObjectProperties,
        IReadOnlySet<Utf8String> declaredDataProperties,
        IReadOnlySet<Utf8String> declaredAnnotationProperties,
        IReadOnlySet<Utf8String> declaredDatatypes)
    {
        Axioms = axioms;
        OntologyIri = ontologyIri;
        Diagnostics = diagnostics;
        DeclaredClasses = declaredClasses;
        DeclaredObjectProperties = declaredObjectProperties;
        DeclaredDataProperties = declaredDataProperties;
        DeclaredAnnotationProperties = declaredAnnotationProperties;
        DeclaredDatatypes = declaredDatatypes;
    }
}
