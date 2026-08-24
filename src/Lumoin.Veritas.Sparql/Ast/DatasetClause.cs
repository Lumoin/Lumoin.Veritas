using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The dataset a query runs against: the <c>FROM</c> IRIs forming the default
/// graph and the <c>FROM NAMED</c> IRIs forming the named graphs. Both lists may
/// be empty; both empty means the backend's default dataset.
/// </summary>
/// <param name="Span">The source extent of the dataset clauses.</param>
/// <param name="DefaultGraphs">The <c>FROM</c> IRIs merged into the default graph.</param>
/// <param name="NamedGraphs">The <c>FROM NAMED</c> IRIs available as named graphs.</param>
/// <remarks>SPARQL <c>DatasetClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDatasetClause">SPARQL 1.2 §19.8 [DatasetClause]</see>.</remarks>
[DebuggerDisplay("Dataset From={DefaultGraphs.Count} FromNamed={NamedGraphs.Count}")]
public sealed record DatasetClause(SourceSpan Span, IReadOnlyList<IriRef> DefaultGraphs, IReadOnlyList<IriRef> NamedGraphs);
