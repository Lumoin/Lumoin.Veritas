using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// A quad block: the body of <c>INSERT DATA</c> / <c>DELETE DATA</c> (ground triples and <c>GRAPH</c> groups) and
/// the templates of a <c>DELETE</c>/<c>INSERT</c> modify (the same shape, but variables are allowed). Default-graph
/// triples sit in <see cref="DefaultTriples"/>; each <c>GRAPH g { … }</c> group is a <see cref="QuadsGraphGroup"/>.
/// </summary>
/// <param name="Span">The source extent of the quad block (the braces and their contents).</param>
/// <param name="DefaultTriples">The triples targeting the default graph.</param>
/// <param name="GraphGroups">The <c>GRAPH g { … }</c> groups targeting named graphs.</param>
/// <param name="DefaultStandaloneNodes">The default-graph standalone <c>TriplesNode</c> subjects (a blank-node property list, collection, or reified triple with no enclosing predicate); lowered to their own triples by the normaliser, then empty.</param>
/// <remarks>SPARQL <c>Quads</c> / <c>QuadData</c> / <c>QuadPattern</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rQuads">SPARQL 1.2 Update §19.8 [Quads]</see>.</remarks>
[DebuggerDisplay("Quads default={DefaultTriples.Count} groups={GraphGroups.Count}")]
public sealed record Quads(
    SourceSpan Span,
    IReadOnlyList<TriplePattern> DefaultTriples,
    IReadOnlyList<QuadsGraphGroup> GraphGroups,
    IReadOnlyList<TriplePatternTerm> DefaultStandaloneNodes);

/// <summary>One <c>GRAPH g { … }</c> group inside a <see cref="Quads"/> block.</summary>
/// <param name="Span">The source extent of the group.</param>
/// <param name="Graph">The graph designator (an IRI for data; an IRI or variable for a template).</param>
/// <param name="Triples">The group's triples.</param>
/// <param name="StandaloneNodes">The group's standalone <c>TriplesNode</c> subjects (no enclosing predicate); lowered to their own triples by the normaliser, then empty.</param>
/// <remarks>SPARQL <c>QuadsNotTriples</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rQuadsNotTriples">SPARQL 1.2 Update §19.8 [QuadsNotTriples]</see>.</remarks>
[DebuggerDisplay("GRAPH group triples={Triples.Count}")]
public sealed record QuadsGraphGroup(
    SourceSpan Span,
    GraphTerm Graph,
    IReadOnlyList<TriplePattern> Triples,
    IReadOnlyList<TriplePatternTerm> StandaloneNodes);

/// <summary>
/// A graph reference target for the graph-management operations: a specific <c>GRAPH iri</c>, the <c>DEFAULT</c>
/// graph, all <c>NAMED</c> graphs, or <c>ALL</c> graphs. <c>CLEAR</c>/<c>DROP</c> accept any of the four; the
/// binary <c>ADD</c>/<c>MOVE</c>/<c>COPY</c> accept only <see cref="GraphRefIri"/> or <see cref="GraphRefDefault"/>.
/// </summary>
/// <param name="Span">The source extent of the reference.</param>
/// <remarks>SPARQL <c>GraphRefAll</c> / <c>GraphOrDefault</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rGraphRefAll">SPARQL 1.2 Update §19.8 [GraphRefAll]</see>.</remarks>
public abstract record GraphRefTarget(SourceSpan Span);

/// <summary>A reference to one named graph by IRI.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Iri">The graph IRI.</param>
[DebuggerDisplay("GRAPH <{Iri.Value}>")]
public sealed record GraphRefIri(SourceSpan Span, IriRef Iri) : GraphRefTarget(Span);

/// <summary>A reference to the default graph (<c>DEFAULT</c>).</summary>
/// <param name="Span">The source extent.</param>
[DebuggerDisplay("DEFAULT")]
public sealed record GraphRefDefault(SourceSpan Span) : GraphRefTarget(Span);

/// <summary>A reference to all named graphs (<c>NAMED</c>).</summary>
/// <param name="Span">The source extent.</param>
[DebuggerDisplay("NAMED")]
public sealed record GraphRefNamed(SourceSpan Span) : GraphRefTarget(Span);

/// <summary>A reference to every graph — the default graph and all named graphs (<c>ALL</c>).</summary>
/// <param name="Span">The source extent.</param>
[DebuggerDisplay("ALL")]
public sealed record GraphRefAll(SourceSpan Span) : GraphRefTarget(Span);

/// <summary>One <c>USING</c> / <c>USING NAMED</c> clause of a modify operation, naming a graph for the query dataset the <c>WHERE</c> pattern matches against.</summary>
/// <param name="Span">The source extent of the clause.</param>
/// <param name="Iri">The graph IRI.</param>
/// <param name="IsNamed"><see langword="true"/> for <c>USING NAMED</c> (a named graph), <see langword="false"/> for <c>USING</c> (a default-graph component).</param>
/// <remarks>SPARQL <c>UsingClause</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rUsingClause">SPARQL 1.2 Update §19.8 [UsingClause]</see>.</remarks>
[DebuggerDisplay("USING {IsNamed ? \"NAMED \" : \"\"}<{Iri.Value}>")]
public sealed record UsingClause(SourceSpan Span, IriRef Iri, bool IsNamed);

/// <summary><c>INSERT DATA { … }</c>: adds ground triples to the dataset.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Data">The ground quad block (no variables, no blank-node templates with variables).</param>
/// <remarks>SPARQL <c>InsertData</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rInsertData">SPARQL 1.2 Update §19.8 [InsertData]</see>.</remarks>
[DebuggerDisplay("INSERT DATA")]
public sealed record InsertDataOperation(SourceSpan Span, Quads Data) : UpdateOperation(Span);

/// <summary><c>DELETE DATA { … }</c>: removes ground triples from the dataset.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Data">The ground quad block.</param>
/// <remarks>SPARQL <c>DeleteData</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rDeleteData">SPARQL 1.2 Update §19.8 [DeleteData]</see>.</remarks>
[DebuggerDisplay("DELETE DATA")]
public sealed record DeleteDataOperation(SourceSpan Span, Quads Data) : UpdateOperation(Span);

/// <summary><c>DELETE WHERE { … }</c>: the shorthand whose quad pattern is both the delete template and the <c>WHERE</c> pattern.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Pattern">The quad pattern matched and deleted.</param>
/// <remarks>SPARQL <c>DeleteWhere</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rDeleteWhere">SPARQL 1.2 Update §19.8 [DeleteWhere]</see>.</remarks>
[DebuggerDisplay("DELETE WHERE")]
public sealed record DeleteWhereOperation(SourceSpan Span, Quads Pattern) : UpdateOperation(Span);

/// <summary>
/// The general modify operation: an optional <c>WITH</c> graph, an optional <c>DELETE</c> template, an optional
/// <c>INSERT</c> template, zero or more <c>USING</c> clauses, and a <c>WHERE</c> pattern. Covers
/// <c>DELETE … INSERT … WHERE</c>, <c>DELETE … WHERE</c>, and <c>INSERT … WHERE</c>.
/// </summary>
/// <param name="Span">The source extent.</param>
/// <param name="With">The <c>WITH</c> graph IRI applied to template/where graph-less triples, or <see langword="null"/>.</param>
/// <param name="Delete">The <c>DELETE</c> template, or <see langword="null"/> when the operation only inserts.</param>
/// <param name="Insert">The <c>INSERT</c> template, or <see langword="null"/> when the operation only deletes.</param>
/// <param name="Using">The <c>USING</c> / <c>USING NAMED</c> clauses scoping the <c>WHERE</c> dataset.</param>
/// <param name="Where">The <c>WHERE</c> group graph pattern producing the solutions.</param>
/// <remarks>SPARQL <c>Modify</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rModify">SPARQL 1.2 Update §19.8 [Modify]</see>.</remarks>
[DebuggerDisplay("MODIFY del={Delete != null} ins={Insert != null}")]
public sealed record ModifyOperation(
    SourceSpan Span,
    IriRef? With,
    Quads? Delete,
    Quads? Insert,
    IReadOnlyList<UsingClause> Using,
    GroupGraphPattern Where) : UpdateOperation(Span);

/// <summary><c>LOAD [SILENT] iri [INTO GRAPH iri]</c>: reads the RDF document at a source IRI into the default graph or a named graph.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses a load failure.</param>
/// <param name="Source">The source document IRI.</param>
/// <param name="Into">The destination named graph IRI, or <see langword="null"/> for the default graph.</param>
/// <remarks>SPARQL <c>Load</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rLoad">SPARQL 1.2 Update §19.8 [Load]</see>.</remarks>
[DebuggerDisplay("LOAD <{Source.Value}>")]
public sealed record LoadOperation(SourceSpan Span, bool Silent, IriRef Source, IriRef? Into) : UpdateOperation(Span);

/// <summary><c>CLEAR [SILENT] (DEFAULT | NAMED | ALL | GRAPH iri)</c>: removes all triples from the referenced graph(s), leaving the graph(s) in place.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses an error.</param>
/// <param name="Target">The graph reference.</param>
/// <remarks>SPARQL <c>Clear</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rClear">SPARQL 1.2 Update §19.8 [Clear]</see>.</remarks>
[DebuggerDisplay("CLEAR")]
public sealed record ClearOperation(SourceSpan Span, bool Silent, GraphRefTarget Target) : UpdateOperation(Span);

/// <summary><c>DROP [SILENT] (DEFAULT | NAMED | ALL | GRAPH iri)</c>: removes the referenced graph(s) — the named graph(s) cease to exist.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses an error.</param>
/// <param name="Target">The graph reference.</param>
/// <remarks>SPARQL <c>Drop</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rDrop">SPARQL 1.2 Update §19.8 [Drop]</see>.</remarks>
[DebuggerDisplay("DROP")]
public sealed record DropOperation(SourceSpan Span, bool Silent, GraphRefTarget Target) : UpdateOperation(Span);

/// <summary><c>CREATE [SILENT] GRAPH iri</c>: creates an empty named graph.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses an already-exists error.</param>
/// <param name="Graph">The named graph IRI to create.</param>
/// <remarks>SPARQL <c>Create</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rCreate">SPARQL 1.2 Update §19.8 [Create]</see>.</remarks>
[DebuggerDisplay("CREATE GRAPH <{Graph.Value}>")]
public sealed record CreateOperation(SourceSpan Span, bool Silent, IriRef Graph) : UpdateOperation(Span);

/// <summary><c>ADD [SILENT] (DEFAULT | iri) TO (DEFAULT | iri)</c>: copies all triples from the source graph into the destination, keeping the destination's existing triples.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses an error.</param>
/// <param name="Source">The source graph (an IRI or the default graph).</param>
/// <param name="Destination">The destination graph (an IRI or the default graph).</param>
/// <remarks>SPARQL <c>Add</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rAdd">SPARQL 1.2 Update §19.8 [Add]</see>.</remarks>
[DebuggerDisplay("ADD")]
public sealed record AddOperation(SourceSpan Span, bool Silent, GraphRefTarget Source, GraphRefTarget Destination) : UpdateOperation(Span);

/// <summary><c>MOVE [SILENT] (DEFAULT | iri) TO (DEFAULT | iri)</c>: replaces the destination with the source's triples and removes the source graph.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses an error.</param>
/// <param name="Source">The source graph (an IRI or the default graph).</param>
/// <param name="Destination">The destination graph (an IRI or the default graph).</param>
/// <remarks>SPARQL <c>Move</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rMove">SPARQL 1.2 Update §19.8 [Move]</see>.</remarks>
[DebuggerDisplay("MOVE")]
public sealed record MoveOperation(SourceSpan Span, bool Silent, GraphRefTarget Source, GraphRefTarget Destination) : UpdateOperation(Span);

/// <summary><c>COPY [SILENT] (DEFAULT | iri) TO (DEFAULT | iri)</c>: replaces the destination with the source's triples, leaving the source in place.</summary>
/// <param name="Span">The source extent.</param>
/// <param name="Silent"><see langword="true"/> when <c>SILENT</c> suppresses an error.</param>
/// <param name="Source">The source graph (an IRI or the default graph).</param>
/// <param name="Destination">The destination graph (an IRI or the default graph).</param>
/// <remarks>SPARQL <c>Copy</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rCopy">SPARQL 1.2 Update §19.8 [Copy]</see>.</remarks>
[DebuggerDisplay("COPY")]
public sealed record CopyOperation(SourceSpan Span, bool Silent, GraphRefTarget Source, GraphRefTarget Destination) : UpdateOperation(Span);
