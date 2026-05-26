using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A graph designator in the <c>GRAPH</c> and <c>SERVICE</c> forms: either a
/// concrete IRI or a variable bound per solution (the grammar's <c>VarOrIri</c>).
/// </summary>
public abstract record GraphTerm;

/// <summary>A concrete-IRI graph designator.</summary>
/// <param name="Iri">The graph IRI.</param>
[DebuggerDisplay("GRAPH <{Iri.Value}>")]
public sealed record GraphIriTerm(IriRef Iri) : GraphTerm;

/// <summary>A variable graph designator, bound per solution.</summary>
/// <param name="Variable">The graph variable.</param>
[DebuggerDisplay("GRAPH ?{Variable.Name}")]
public sealed record GraphVariableTerm(SparqlVariable Variable) : GraphTerm;
