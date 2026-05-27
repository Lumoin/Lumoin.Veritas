using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A graph designator in the <c>GRAPH</c> and <c>SERVICE</c> forms: either a
/// concrete IRI or a variable bound per solution (the grammar's <c>VarOrIri</c>).
/// </summary>
/// <param name="Span">The source extent of the graph designator.</param>
/// <remarks>SPARQL <c>VarOrIri</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrIri">SPARQL 1.2 §19.8 [VarOrIri]</see>.</remarks>
public abstract record GraphTerm(SourceSpan Span);

/// <summary>A concrete-IRI graph designator.</summary>
/// <param name="Span">The source extent of the graph designator.</param>
/// <param name="Iri">The graph IRI.</param>
/// <remarks>SPARQL <c>iri</c> in <c>VarOrIri</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrIri">SPARQL 1.2 §19.8 [VarOrIri]</see>.</remarks>
[DebuggerDisplay("GRAPH <{Iri.Value}>")]
public sealed record GraphIriTerm(SourceSpan Span, IriRef Iri) : GraphTerm(Span);

/// <summary>A variable graph designator, bound per solution.</summary>
/// <param name="Span">The source extent of the graph designator.</param>
/// <param name="Variable">The graph variable.</param>
/// <remarks>SPARQL <c>Var</c> in <c>VarOrIri</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrIri">SPARQL 1.2 §19.8 [VarOrIri]</see>.</remarks>
[DebuggerDisplay("GRAPH ?{Variable.Name}")]
public sealed record GraphVariableTerm(SourceSpan Span, SparqlVariable Variable) : GraphTerm(Span);
