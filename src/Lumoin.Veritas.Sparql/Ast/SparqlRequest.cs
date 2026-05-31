using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The root of a parsed SPARQL request: either a <see cref="SparqlQuery"/> (one of
/// the four query forms) or a <see cref="SparqlUpdateRequest"/> (a sequence of
/// update operations). The parser dispatches on the first keyword after the
/// prologue.
/// </summary>
/// <param name="Span">The source extent of the request.</param>
/// <remarks>SPARQL <c>QueryUnit</c> / <c>UpdateUnit</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQueryUnit">SPARQL 1.2 §19.8 [QueryUnit]</see>.</remarks>
public abstract record SparqlRequest(SourceSpan Span);

/// <summary>
/// A parsed SPARQL query: the prologue, the form-specific head, the dataset, the
/// <c>WHERE</c> pattern, the solution modifiers, and an optional trailing
/// <c>VALUES</c> block.
/// </summary>
/// <param name="Span">The source extent of the query.</param>
/// <param name="Prologue">The <c>BASE</c> / <c>PREFIX</c> declarations.</param>
/// <param name="Form">The form-specific head (SELECT / CONSTRUCT / ASK / DESCRIBE).</param>
/// <param name="Dataset">The <c>FROM</c> / <c>FROM NAMED</c> dataset clause.</param>
/// <param name="Where">The <c>WHERE</c> graph pattern.</param>
/// <param name="Modifier">The solution modifiers (group, having, order, slice).</param>
/// <param name="Values">The trailing <c>VALUES</c> block, or <c>null</c>.</param>
/// <remarks>SPARQL <c>Query</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQuery">SPARQL 1.2 §19.8 [Query]</see>.</remarks>
[DebuggerDisplay("SparqlQuery {Form}")]
public sealed record SparqlQuery(
    SourceSpan Span,
    Prologue Prologue,
    QueryForm Form,
    DatasetClause Dataset,
    WhereClause Where,
    SolutionModifier Modifier,
    ValuesClause? Values) : SparqlRequest(Span);

/// <summary>
/// A parsed SPARQL Update request: a prologue and a sequence of update operations applied in order. The prologue
/// holds every <c>BASE</c>/<c>PREFIX</c>/<c>VERSION</c> declaration of the request (which may interleave between the
/// <c>;</c>-separated operations); the operations carry the IRIs already resolved against it.
/// </summary>
/// <param name="Span">The source extent of the update request.</param>
/// <param name="InitialPrologue">The request's accumulated prologue declarations.</param>
/// <param name="Operations">The update operations, in order.</param>
/// <remarks>SPARQL <c>Update</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rUpdate">SPARQL 1.2 Update §19.8 [Update]</see>.</remarks>
[DebuggerDisplay("SparqlUpdateRequest ops={Operations.Count}")]
public sealed record SparqlUpdateRequest(
    SourceSpan Span,
    Prologue InitialPrologue,
    IReadOnlyList<UpdateOperation> Operations) : SparqlRequest(Span);

/// <summary>
/// One operation of a SPARQL Update request. The concrete forms live in <c>UpdateOperations.cs</c>:
/// <see cref="InsertDataOperation"/>, <see cref="DeleteDataOperation"/>, <see cref="DeleteWhereOperation"/>,
/// <see cref="ModifyOperation"/>, <see cref="LoadOperation"/>, <see cref="ClearOperation"/>,
/// <see cref="DropOperation"/>, <see cref="CreateOperation"/>, <see cref="AddOperation"/>,
/// <see cref="MoveOperation"/>, and <see cref="CopyOperation"/>.
/// </summary>
/// <param name="Span">The source extent of the update operation.</param>
/// <remarks>SPARQL <c>Update1</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rUpdate1">SPARQL 1.2 Update §19.8 [Update1]</see>.</remarks>
public abstract record UpdateOperation(SourceSpan Span);
