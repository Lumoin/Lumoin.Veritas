using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One relation's build plan: the pattern's scan schema, its column indices in the global descent order,
/// and the trie depth it builds at. The columns past the depth are the relation's leaf columns.
/// </summary>
/// <param name="ScanSchema">The pattern's scan schema, positional against its columns.</param>
/// <param name="Columns">The schema's column indices in global order.</param>
/// <param name="Depth">The trie depth: zero for an empty schema, otherwise between one and the column count.</param>
internal readonly record struct FreeJoinRelationPlan(IReadOnlyList<Variable> ScanSchema, int[] Columns, int Depth);
