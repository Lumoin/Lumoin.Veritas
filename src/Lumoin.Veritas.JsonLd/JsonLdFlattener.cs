using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the JSON-LD 1.1 Flattening Algorithm
/// (<see href="https://www.w3.org/TR/json-ld11-api/#flattening-algorithm"/>):
/// given expanded JSON-LD, produces a single flat array of node objects where
/// every node is named (blank nodes are relabelled <c>_:b0</c>, <c>_:b1</c>, …),
/// nested nodes are hoisted to the top level and replaced by references, and
/// named graphs are nested under <c>@graph</c>.
/// </summary>
/// <remarks>
/// The heavy lifting is Node Map Generation (§8) in <see cref="JsonLdNodeMap"/>;
/// flattening merges the named graphs into the default graph and emits its
/// subjects in sorted order, dropping free-floating node references (objects
/// carrying only <c>@id</c>).
/// </remarks>
public static class JsonLdFlattener
{
    /// <summary>
    /// Flattens expanded JSON-LD into a single array of node objects.
    /// </summary>
    /// <param name="expanded">The expanded document (an array of node objects).</param>
    /// <returns>The flattened expanded JSON-LD as an object graph (a <see cref="List{T}"/> of node objects).</returns>
    public static object? Flatten(IReadOnlyList<object?> expanded)
    {
        ArgumentNullException.ThrowIfNull(expanded);

        Dictionary<string, Dictionary<string, object?>> defaultGraph =
            JsonLdNodeMap.MergeToDefault(JsonLdNodeMap.Generate(expanded));

        List<object?> flattened = new();
        List<string> subjects = new(defaultGraph.Keys);
        subjects.Sort(StringComparer.Ordinal);
        foreach(string subject in subjects)
        {
            Dictionary<string, object?> node = defaultGraph[subject];
            if(!JsonLdNodeMap.IsSubjectReference(node))
            {
                flattened.Add(node);
            }
        }

        return flattened;
    }
}
