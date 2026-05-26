using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The dataset a query runs against: the <c>FROM</c> IRIs forming the default
/// graph and the <c>FROM NAMED</c> IRIs forming the named graphs. Both lists may
/// be empty; both empty means the backend's default dataset.
/// </summary>
/// <param name="DefaultGraphs">The <c>FROM</c> IRIs merged into the default graph.</param>
/// <param name="NamedGraphs">The <c>FROM NAMED</c> IRIs available as named graphs.</param>
[DebuggerDisplay("Dataset From={DefaultGraphs.Count} FromNamed={NamedGraphs.Count}")]
public sealed record DatasetClause(IReadOnlyList<IriRef> DefaultGraphs, IReadOnlyList<IriRef> NamedGraphs);
