using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The root of a parsed SPARQL request: either a <see cref="SparqlQuery"/> (one of
/// the four query forms) or a <see cref="SparqlUpdateRequest"/> (a sequence of
/// update operations). The parser dispatches on the first keyword after the
/// prologue.
/// </summary>
/// <remarks>SPARQL <c>QueryUnit</c> / <c>UpdateUnit</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQueryUnit">SPARQL 1.2 §19.8 [QueryUnit]</see>.</remarks>
public abstract record SparqlRequest;

/// <summary>
/// A parsed SPARQL query: the prologue, the form-specific head, the dataset, the
/// <c>WHERE</c> pattern, the solution modifiers, and an optional trailing
/// <c>VALUES</c> block.
/// </summary>
/// <param name="Prologue">The <c>BASE</c> / <c>PREFIX</c> declarations.</param>
/// <param name="Form">The form-specific head (SELECT / CONSTRUCT / ASK / DESCRIBE).</param>
/// <param name="Dataset">The <c>FROM</c> / <c>FROM NAMED</c> dataset clause.</param>
/// <param name="Where">The <c>WHERE</c> graph pattern.</param>
/// <param name="Modifier">The solution modifiers (group, having, order, slice).</param>
/// <param name="Values">The trailing <c>VALUES</c> block, or <c>null</c>.</param>
/// <remarks>SPARQL <c>Query</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQuery">SPARQL 1.2 §19.8 [Query]</see>.</remarks>
[DebuggerDisplay("SparqlQuery {Form}")]
public sealed record SparqlQuery(
    Prologue Prologue,
    QueryForm Form,
    DatasetClause Dataset,
    WhereClause Where,
    SolutionModifier Modifier,
    ValuesClause? Values) : SparqlRequest;

/// <summary>
/// A parsed SPARQL Update request: a prologue and a sequence of update operations.
/// </summary>
/// <remarks>
/// SPARQL Update is not implemented in this build; the parser throws
/// <see cref="SparqlParseException"/> on the first update keyword and never
/// constructs this type. It and <see cref="UpdateOperation"/> are declared in
/// skeleton form so the request discriminated union resolves at compile time and
/// the future Update batch fills in the operation hierarchy without changing the
/// public surface.
/// </remarks>
/// <param name="InitialPrologue">The leading prologue shared by the operations.</param>
/// <param name="Operations">The update operations, in order.</param>
/// <remarks>SPARQL <c>Update</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rUpdate">SPARQL 1.2 §19.8 [Update]</see>.</remarks>
[DebuggerDisplay("SparqlUpdateRequest ops={Operations.Count}")]
public sealed record SparqlUpdateRequest(
    Prologue InitialPrologue,
    IReadOnlyList<UpdateOperation> Operations) : SparqlRequest;

/// <summary>
/// One operation of a SPARQL Update request. A skeleton with no concrete subtypes
/// in this build; the future Update batch defines the operation forms (INSERT,
/// DELETE, LOAD, CLEAR, CREATE, DROP, COPY, MOVE, ADD).
/// </summary>
/// <remarks>SPARQL <c>Update1</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rUpdate1">SPARQL 1.2 §19.8 [Update1]</see>.</remarks>
public abstract record UpdateOperation;
