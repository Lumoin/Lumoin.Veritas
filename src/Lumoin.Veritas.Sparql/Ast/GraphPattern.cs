using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A graph pattern: the body of a <c>{ ... }</c> block or one of its members.
/// </summary>
/// <remarks>
/// <para>
/// Filters are members of their enclosing group (<see cref="FilterPattern"/>)
/// rather than wrappers; the translator collects and lifts them per W3C
/// §18.2.2 when it builds the algebra.
/// </para>
/// <para>SPARQL <c>GraphPatternNotTriples</c> / <c>GroupGraphPatternSub</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGraphPatternNotTriples">SPARQL 1.2 §19.8 [GraphPatternNotTriples]</see>.</para>
/// </remarks>
public abstract record GraphPattern;

/// <summary>A group graph pattern: an ordered list of members within braces.</summary>
/// <param name="Members">The members in source order (basic blocks, optionals, unions, filters, binds, ...).</param>
/// <remarks>SPARQL <c>GroupGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupGraphPattern">SPARQL 1.2 §19.8 [GroupGraphPattern]</see>.</remarks>
[DebuggerDisplay("{{ {Members.Count} members }}")]
public sealed record GroupGraphPattern(IReadOnlyList<GraphPattern> Members) : GraphPattern;

/// <summary>A contiguous run of triple patterns within a group (a basic graph pattern block).</summary>
/// <param name="Triples">The triple patterns.</param>
/// <remarks>SPARQL <c>TriplesBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTriplesBlock">SPARQL 1.2 §19.8 [TriplesBlock]</see>.</remarks>
[DebuggerDisplay("BGP[{Triples.Count}]")]
public sealed record BasicGraphPatternBlock(IReadOnlyList<TriplePattern> Triples) : GraphPattern;

/// <summary>An <c>OPTIONAL { ... }</c> member.</summary>
/// <param name="Inner">The optional inner pattern.</param>
/// <remarks>SPARQL <c>OptionalGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOptionalGraphPattern">SPARQL 1.2 §19.8 [OptionalGraphPattern]</see>.</remarks>
[DebuggerDisplay("OPTIONAL")]
public sealed record OptionalPattern(GraphPattern Inner) : GraphPattern;

/// <summary>A <c>MINUS { ... }</c> member.</summary>
/// <param name="Inner">The subtracted inner pattern.</param>
/// <remarks>SPARQL <c>MinusGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rMinusGraphPattern">SPARQL 1.2 §19.8 [MinusGraphPattern]</see>.</remarks>
[DebuggerDisplay("MINUS")]
public sealed record MinusPattern(GraphPattern Inner) : GraphPattern;

/// <summary>A <c>{ ... } UNION { ... }</c> alternation.</summary>
/// <param name="Left">The left alternative.</param>
/// <param name="Right">The right alternative.</param>
/// <remarks>SPARQL <c>GroupOrUnionGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupOrUnionGraphPattern">SPARQL 1.2 §19.8 [GroupOrUnionGraphPattern]</see>.</remarks>
[DebuggerDisplay("UNION")]
public sealed record UnionPattern(GraphPattern Left, GraphPattern Right) : GraphPattern;

/// <summary>A <c>GRAPH term { ... }</c> named-graph indirection.</summary>
/// <param name="GraphTerm">The graph designator (IRI or variable).</param>
/// <param name="Inner">The pattern evaluated against the designated graph.</param>
/// <remarks>SPARQL <c>GraphGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGraphGraphPattern">SPARQL 1.2 §19.8 [GraphGraphPattern]</see>.</remarks>
[DebuggerDisplay("GRAPH")]
public sealed record GraphGraphPattern(GraphTerm GraphTerm, GraphPattern Inner) : GraphPattern;

/// <summary>
/// A <c>SERVICE term { ... }</c> federated pattern. The endpoint is a
/// <see cref="GraphTerm"/> (IRI or variable) to accept the full grammar; this
/// build raises at execution time rather than performing federation.
/// </summary>
/// <param name="Endpoint">The service endpoint designator.</param>
/// <param name="IsSilent">Whether <c>SILENT</c> was given.</param>
/// <param name="Inner">The pattern delegated to the service.</param>
/// <remarks>SPARQL <c>ServiceGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rServiceGraphPattern">SPARQL 1.2 §19.8 [ServiceGraphPattern]</see>.</remarks>
[DebuggerDisplay("SERVICE Silent={IsSilent}")]
public sealed record ServicePattern(GraphTerm Endpoint, bool IsSilent, GraphPattern Inner) : GraphPattern;

/// <summary>A <c>BIND(expr AS ?var)</c> member.</summary>
/// <param name="Expression">The bound expression.</param>
/// <param name="AsVariable">The variable the expression binds to.</param>
/// <remarks>SPARQL <c>Bind</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBind">SPARQL 1.2 §19.8 [Bind]</see>.</remarks>
[DebuggerDisplay("BIND(... AS ?{AsVariable.Name})")]
public sealed record BindPattern(ExpressionNode Expression, SparqlVariable AsVariable) : GraphPattern;

/// <summary>An inline <c>VALUES</c> data block as a group member.</summary>
/// <param name="Data">The inline data.</param>
/// <remarks>SPARQL <c>InlineData</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rInlineData">SPARQL 1.2 §19.8 [InlineData]</see>.</remarks>
[DebuggerDisplay("VALUES")]
public sealed record ValuesPattern(ValuesClause Data) : GraphPattern;

/// <summary>A <c>FILTER(expr)</c> member, lifted to constrain the enclosing group during translation.</summary>
/// <param name="Expression">The filter expression.</param>
/// <remarks>SPARQL <c>Filter</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rFilter">SPARQL 1.2 §19.8 [Filter]</see>.</remarks>
[DebuggerDisplay("FILTER")]
public sealed record FilterPattern(ExpressionNode Expression) : GraphPattern;

/// <summary>A sub-<c>SELECT</c> member: a nested SELECT query joined into the enclosing group.</summary>
/// <param name="InnerQuery">The nested SELECT query.</param>
/// <remarks>SPARQL <c>SubSelect</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSubSelect">SPARQL 1.2 §19.8 [SubSelect]</see>.</remarks>
[DebuggerDisplay("SubSelect")]
public sealed record SubSelectPattern(SparqlQuery InnerQuery) : GraphPattern;
