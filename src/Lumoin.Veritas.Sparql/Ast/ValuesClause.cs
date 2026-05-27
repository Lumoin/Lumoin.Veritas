using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// An inline-data block: a set of variables and the rows of values bound to them.
/// A <c>null</c> entry in a row is the <c>UNDEF</c> marker, leaving that variable
/// unbound in that row. Used both for a trailing <c>VALUES</c> clause after the
/// <c>WHERE</c> block and (via <see cref="ValuesPattern"/>) for an inline
/// <c>VALUES</c> inside a group.
/// </summary>
/// <param name="Span">The source extent of the data block.</param>
/// <param name="Variables">The variables the rows bind, in column order.</param>
/// <param name="Rows">The rows; each row has one entry per variable, <c>null</c> for <c>UNDEF</c>.</param>
/// <remarks>SPARQL <c>DataBlock</c> (<c>ValuesClause</c> / <c>InlineData</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rDataBlock">SPARQL 1.2 §19.8 [DataBlock]</see>.</remarks>
[DebuggerDisplay("VALUES vars={Variables.Count} rows={Rows.Count}")]
public sealed record ValuesClause(
    SourceSpan Span,
    IReadOnlyList<SparqlVariable> Variables,
    IReadOnlyList<IReadOnlyList<RdfTerm?>> Rows);
