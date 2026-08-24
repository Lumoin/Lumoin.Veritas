using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A graph pattern: the body of a <c>{ ... }</c> block or one of its members.
/// </summary>
/// <param name="Span">The source extent of the graph pattern.</param>
/// <remarks>
/// <para>
/// Filters are members of their enclosing group (<see cref="FilterPattern"/>)
/// rather than wrappers; the translator collects and lifts them per W3C
/// §18.2.2 when it builds the algebra.
/// </para>
/// <para>SPARQL <c>GraphPatternNotTriples</c> / <c>GroupGraphPatternSub</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGraphPatternNotTriples">SPARQL 1.2 §19.8 [GraphPatternNotTriples]</see>.</para>
/// </remarks>
public abstract record GraphPattern(SourceSpan Span);

/// <summary>A group graph pattern: an ordered list of members within braces.</summary>
/// <param name="Span">The source extent from the opening to the closing brace.</param>
/// <param name="Members">The members in source order (basic blocks, optionals, unions, filters, binds, ...).</param>
/// <remarks>SPARQL <c>GroupGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupGraphPattern">SPARQL 1.2 §19.8 [GroupGraphPattern]</see>.</remarks>
[DebuggerDisplay("{{ {Members.Count} members }}")]
public sealed record GroupGraphPattern(SourceSpan Span, IReadOnlyList<GraphPattern> Members) : GraphPattern(Span);

/// <summary>
/// A contiguous run of triple patterns within a group (a basic graph pattern block), together with any
/// RDF 1.2 standalone reified-triple assertions written in the same run.
/// </summary>
/// <param name="Span">The source extent of the triple run.</param>
/// <param name="Triples">The triple patterns.</param>
/// <param name="StandaloneNodes">
/// The RDF 1.2 reified triples written as subject-only assertions in this run — a <c>&lt;&lt; s p o ~r? &gt;&gt;</c>
/// with an empty property list (<c>ReifiedTripleBlockPath</c>), reifying its inner triple without further
/// properties. Empty in the common case. Kept un-expanded for tooling fidelity; the early normalization pass
/// lowers each to the reification triple <c>reifier rdf:reifies &lt;&lt;( s p o )&gt;&gt;</c>. Per RDF 1.2 the
/// inner triple is not asserted (only the annotation <c>{| … |}</c> form asserts), unless the opt-in
/// <c>SparqlNormalizerOptions.AssertReifiedTripleInnerTriple</c> flag is set.
/// </param>
/// <remarks>SPARQL <c>TriplesBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTriplesBlock">SPARQL 1.2 §19.8 [TriplesBlock]</see>.</remarks>
[DebuggerDisplay("BGP[{Triples.Count}] standalone[{StandaloneNodes.Count}]")]
public sealed record BasicGraphPatternBlock(SourceSpan Span, IReadOnlyList<TriplePattern> Triples, IReadOnlyList<TriplePatternTerm> StandaloneNodes) : GraphPattern(Span);

/// <summary>An <c>OPTIONAL { ... }</c> member.</summary>
/// <param name="Span">The source extent from the <c>OPTIONAL</c> keyword through the inner pattern.</param>
/// <param name="Inner">The optional inner pattern.</param>
/// <remarks>SPARQL <c>OptionalGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOptionalGraphPattern">SPARQL 1.2 §19.8 [OptionalGraphPattern]</see>.</remarks>
[DebuggerDisplay("OPTIONAL")]
public sealed record OptionalPattern(SourceSpan Span, GraphPattern Inner) : GraphPattern(Span);

/// <summary>A <c>MINUS { ... }</c> member.</summary>
/// <param name="Span">The source extent from the <c>MINUS</c> keyword through the inner pattern.</param>
/// <param name="Inner">The subtracted inner pattern.</param>
/// <remarks>SPARQL <c>MinusGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rMinusGraphPattern">SPARQL 1.2 §19.8 [MinusGraphPattern]</see>.</remarks>
[DebuggerDisplay("MINUS")]
public sealed record MinusPattern(SourceSpan Span, GraphPattern Inner) : GraphPattern(Span);

/// <summary>A <c>{ ... } UNION { ... }</c> alternation.</summary>
/// <param name="Span">The source extent from the left alternative through the right.</param>
/// <param name="Left">The left alternative.</param>
/// <param name="Right">The right alternative.</param>
/// <remarks>SPARQL <c>GroupOrUnionGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupOrUnionGraphPattern">SPARQL 1.2 §19.8 [GroupOrUnionGraphPattern]</see>.</remarks>
[DebuggerDisplay("UNION")]
public sealed record UnionPattern(SourceSpan Span, GraphPattern Left, GraphPattern Right) : GraphPattern(Span);

/// <summary>A <c>GRAPH term { ... }</c> named-graph indirection.</summary>
/// <param name="Span">The source extent from the <c>GRAPH</c> keyword through the inner pattern.</param>
/// <param name="GraphTerm">The graph designator (IRI or variable).</param>
/// <param name="Inner">The pattern evaluated against the designated graph.</param>
/// <remarks>SPARQL <c>GraphGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGraphGraphPattern">SPARQL 1.2 §19.8 [GraphGraphPattern]</see>.</remarks>
[DebuggerDisplay("GRAPH")]
public sealed record GraphGraphPattern(SourceSpan Span, GraphTerm GraphTerm, GraphPattern Inner) : GraphPattern(Span);

/// <summary>
/// A <c>SERVICE term { ... }</c> federated pattern. The endpoint is a
/// <see cref="GraphTerm"/> (IRI or variable) to accept the full grammar; this
/// build raises at execution time rather than performing federation.
/// </summary>
/// <param name="Span">The source extent from the <c>SERVICE</c> keyword through the inner pattern.</param>
/// <param name="Endpoint">The service endpoint designator.</param>
/// <param name="IsSilent">Whether <c>SILENT</c> was given.</param>
/// <param name="Inner">The pattern delegated to the service.</param>
/// <remarks>SPARQL <c>ServiceGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rServiceGraphPattern">SPARQL 1.2 §19.8 [ServiceGraphPattern]</see>.</remarks>
[DebuggerDisplay("SERVICE Silent={IsSilent}")]
public sealed record ServicePattern(SourceSpan Span, GraphTerm Endpoint, bool IsSilent, GraphPattern Inner) : GraphPattern(Span);

/// <summary>A <c>BIND(expr AS ?var)</c> member.</summary>
/// <param name="Span">The source extent from the <c>BIND</c> keyword through the closing parenthesis.</param>
/// <param name="Expression">The bound expression.</param>
/// <param name="AsVariable">The variable the expression binds to.</param>
/// <remarks>SPARQL <c>Bind</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBind">SPARQL 1.2 §19.8 [Bind]</see>.</remarks>
[DebuggerDisplay("BIND(... AS ?{AsVariable.Name})")]
public sealed record BindPattern(SourceSpan Span, ExpressionNode Expression, SparqlVariable AsVariable) : GraphPattern(Span);

/// <summary>An inline <c>VALUES</c> data block as a group member.</summary>
/// <param name="Span">The source extent of the inline data block.</param>
/// <param name="Data">The inline data.</param>
/// <remarks>SPARQL <c>InlineData</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rInlineData">SPARQL 1.2 §19.8 [InlineData]</see>.</remarks>
[DebuggerDisplay("VALUES")]
public sealed record ValuesPattern(SourceSpan Span, ValuesClause Data) : GraphPattern(Span);

/// <summary>A <c>FILTER(expr)</c> member, lifted to constrain the enclosing group during translation.</summary>
/// <param name="Span">The source extent from the <c>FILTER</c> keyword through the constraint.</param>
/// <param name="Expression">The filter expression.</param>
/// <remarks>SPARQL <c>Filter</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rFilter">SPARQL 1.2 §19.8 [Filter]</see>.</remarks>
[DebuggerDisplay("FILTER")]
public sealed record FilterPattern(SourceSpan Span, ExpressionNode Expression) : GraphPattern(Span);

/// <summary>A sub-<c>SELECT</c> member: a nested SELECT query joined into the enclosing group.</summary>
/// <param name="Span">The source extent of the nested query.</param>
/// <param name="InnerQuery">The nested SELECT query.</param>
/// <remarks>SPARQL <c>SubSelect</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSubSelect">SPARQL 1.2 §19.8 [SubSelect]</see>.</remarks>
[DebuggerDisplay("SubSelect")]
public sealed record SubSelectPattern(SourceSpan Span, SparqlQuery InnerQuery) : GraphPattern(Span);
