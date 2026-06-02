using System;
using System.Collections.Generic;
using Lumoin.Veritas.LinkedData;

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
/// The core is Node Map Generation (§8): a recursive walk of the expanded
/// input that collects every subject into a per-graph node map. The merged
/// default graph is then emitted in subject-sorted order, dropping
/// free-floating node references (objects carrying only <c>@id</c>).
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

        Dictionary<string, Dictionary<string, object?>> defaultGraph = CreateMergedNodeMap(expanded);

        List<object?> flattened = new();
        List<string> subjects = new(defaultGraph.Keys);
        subjects.Sort(StringComparer.Ordinal);
        foreach(string subject in subjects)
        {
            Dictionary<string, object?> node = defaultGraph[subject];
            if(!IsSubjectReference(node))
            {
                flattened.Add(node);
            }
        }

        return flattened;
    }

    /// <summary>Builds the merged node map: generates per-graph node maps, then folds the named graphs into the default graph under <c>@graph</c>.</summary>
    /// <param name="expanded">The expanded input.</param>
    /// <returns>The merged default-graph node map (subject id → node).</returns>
    private static Dictionary<string, Dictionary<string, object?>> CreateMergedNodeMap(IReadOnlyList<object?> expanded)
    {
        BlankNodeIssuer issuer = new();
        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs = new(StringComparer.Ordinal)
        {
            [GraphNames.Default] = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal)
        };

        CreateNodeMap(expanded, graphs, GraphNames.Default, issuer, name: null, list: null);

        return MergeNodeMaps(graphs);
    }

    /// <summary>
    /// Node Map Generation (§8): recursively flattens the expanded
    /// <paramref name="input"/> into the per-graph node maps in
    /// <paramref name="graphs"/>, naming blank nodes through
    /// <paramref name="issuer"/> and, when <paramref name="list"/> is non-null,
    /// appending list members to it.
    /// </summary>
    /// <param name="input">The expanded element under consideration.</param>
    /// <param name="graphs">The graph-name → (subject id → node) maps.</param>
    /// <param name="graph">The current graph name.</param>
    /// <param name="issuer">The blank-node identifier issuer.</param>
    /// <param name="name">The name assigned to <paramref name="input"/> when it is a subject, or <see langword="null"/>.</param>
    /// <param name="list">The list being assembled, or <see langword="null"/> for none.</param>
    private static void CreateNodeMap(
        object? input,
        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs,
        string graph,
        BlankNodeIssuer issuer,
        string? name,
        List<object?>? list)
    {
        if(input is IReadOnlyList<object?> array)
        {
            foreach(object? element in array)
            {
                CreateNodeMap(element, graphs, graph, issuer, name: null, list);
            }

            return;
        }

        if(input is not IReadOnlyDictionary<string, object?> element1)
        {
            //A non-object (scalar) is a list member.
            if(list is not null)
            {
                list.Add(input);
            }

            return;
        }

        //A value object is a leaf: relabel a blank-node @type, then append to the list when assembling one.
        if(IsValue(element1))
        {
            list?.Add(RelabelValueType(element1, issuer));
            return;
        }

        if(list is not null && IsList(element1))
        {
            List<object?> innerList = new();
            CreateNodeMap(element1[JsonLdKeywords.List], graphs, graph, issuer, name, innerList);
            list.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.List] = innerList });
            return;
        }

        //Otherwise the input is a subject node.

        //The spec names @type blank nodes first, so they are issued before the subject's own name.
        if(element1.TryGetValue(JsonLdKeywords.Type, out object? typeValue) && typeValue is IReadOnlyList<object?> typeList)
        {
            foreach(object? type in typeList)
            {
                if(type is string typeIri && typeIri.StartsWith("_:", StringComparison.Ordinal))
                {
                    issuer.GetId(typeIri);
                }
            }
        }

        name ??= IsBlankNode(element1) ? issuer.GetId(GetId(element1)) : GetId(element1)!;

        list?.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = name });

        Dictionary<string, Dictionary<string, object?>> subjects = graphs[graph];
        if(!subjects.TryGetValue(name, out Dictionary<string, object?>? subject))
        {
            subject = new Dictionary<string, object?>(StringComparer.Ordinal);
            subjects[name] = subject;
        }
        subject[JsonLdKeywords.Id] = name;

        List<string> properties = new(element1.Keys);
        properties.Sort(StringComparer.Ordinal);
        foreach(string rawProperty in properties)
        {
            if(JsonLdKeywords.IsId(rawProperty))
            {
                continue;
            }

            if(JsonLdKeywords.IsReverse(rawProperty))
            {
                AddReverse(element1[rawProperty], name, subject, subjects, graphs, graph, issuer);
                continue;
            }

            if(JsonLdKeywords.IsGraph(rawProperty))
            {
                if(!graphs.ContainsKey(name))
                {
                    graphs[name] = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
                }

                CreateNodeMap(element1[rawProperty], graphs, name, issuer, name: null, list: null);
                continue;
            }

            if(JsonLdKeywords.IsIncluded(rawProperty))
            {
                CreateNodeMap(element1[rawProperty], graphs, graph, issuer, name: null, list: null);
                continue;
            }

            //Non-@type keywords are copied verbatim onto the subject; a second, differing @index conflicts.
            if(!JsonLdKeywords.IsType(rawProperty) && IriUtils.IsKeyword(rawProperty))
            {
                if(string.Equals(rawProperty, JsonLdKeywords.Index, StringComparison.Ordinal)
                    && subject.TryGetValue(JsonLdKeywords.Index, out object? existingIndex)
                    && !ValueEquals(existingIndex, element1[rawProperty]))
                {
                    throw new JsonLdProcessingException("Conflicting @index values detected while flattening.");
                }

                subject[rawProperty] = element1[rawProperty];
                continue;
            }

            //A blank-node property name is relabelled.
            string property = rawProperty.StartsWith("_:", StringComparison.Ordinal) ? issuer.GetId(rawProperty) : rawProperty;

            if(element1[rawProperty] is not IReadOnlyList<object?> objects)
            {
                continue;
            }

            if(objects.Count == 0)
            {
                AddValue(subject, property, new List<object?>(), propertyIsArray: true);
                continue;
            }

            foreach(object? rawObject in objects)
            {
                AddObjectToNodeMap(rawObject, property, name, subject, graphs, graph, issuer);
            }
        }
    }

    /// <summary>Adds one expanded object value to a subject's property in the node map, hoisting embedded subjects and folding lists (§8 step 6.6).</summary>
    /// <param name="rawObject">The object value.</param>
    /// <param name="property">The (possibly relabelled) property name; <c>@type</c> for type values.</param>
    /// <param name="name">The current subject's name.</param>
    /// <param name="subject">The current subject node.</param>
    /// <param name="graphs">The graph maps.</param>
    /// <param name="graph">The current graph name.</param>
    /// <param name="issuer">The blank-node issuer.</param>
    private static void AddObjectToNodeMap(
        object? rawObject,
        string property,
        string name,
        Dictionary<string, object?> subject,
        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs,
        string graph,
        BlankNodeIssuer issuer)
    {
        if(JsonLdKeywords.IsType(property) && rawObject is string typeIri)
        {
            //A blank-node @type value is relabelled.
            string typeName = typeIri.StartsWith("_:", StringComparison.Ordinal) ? issuer.GetId(typeIri) : typeIri;
            AddValue(subject, property, typeName, propertyIsArray: true);
            return;
        }

        if(rawObject is IReadOnlyDictionary<string, object?> objectNode && (IsSubject(objectNode) || IsSubjectReference(objectNode)))
        {
            //A null @id drops the object.
            if(objectNode.TryGetValue(JsonLdKeywords.Id, out object? idValue) && idValue is null)
            {
                return;
            }

            string id = IsBlankNode(objectNode) ? issuer.GetId(GetId(objectNode)) : GetId(objectNode)!;
            AddValue(subject, property, new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = id }, propertyIsArray: true);
            CreateNodeMap(objectNode, graphs, graph, issuer, id, list: null);
            return;
        }

        if(rawObject is IReadOnlyDictionary<string, object?> valueObject && IsValue(valueObject))
        {
            AddValue(subject, property, RelabelValueType(valueObject, issuer), propertyIsArray: true);
            return;
        }

        if(rawObject is IReadOnlyDictionary<string, object?> listObject && IsList(listObject))
        {
            List<object?> innerList = new();
            CreateNodeMap(listObject[JsonLdKeywords.List], graphs, graph, issuer, name, innerList);
            AddValue(subject, property, new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.List] = innerList }, propertyIsArray: true);
            return;
        }

        //Any other object recurses (and is added as-is).
        CreateNodeMap(rawObject, graphs, graph, issuer, name, list: null);
        AddValue(subject, property, rawObject, propertyIsArray: true);
    }

    /// <summary>Hoists the subjects under a node's <c>@reverse</c> map, adding the back-reference to each referenced subject (§8 step 6.4).</summary>
    /// <param name="reverseValue">The <c>@reverse</c> map.</param>
    /// <param name="name">The current subject's name.</param>
    /// <param name="subject">The current subject node.</param>
    /// <param name="subjects">The current graph's subject map.</param>
    /// <param name="graphs">The graph maps.</param>
    /// <param name="graph">The current graph name.</param>
    /// <param name="issuer">The blank-node issuer.</param>
    private static void AddReverse(
        object? reverseValue,
        string name,
        Dictionary<string, object?> subject,
        Dictionary<string, Dictionary<string, object?>> subjects,
        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs,
        string graph,
        BlankNodeIssuer issuer)
    {
        _ = subject;
        if(reverseValue is not IReadOnlyDictionary<string, object?> reverseMap)
        {
            return;
        }

        Dictionary<string, object?> referencedNode = new(StringComparer.Ordinal) { [JsonLdKeywords.Id] = name };
        foreach(KeyValuePair<string, object?> reverseProperty in reverseMap)
        {
            if(reverseProperty.Value is not IReadOnlyList<object?> items)
            {
                continue;
            }

            foreach(object? item in items)
            {
                if(item is not IReadOnlyDictionary<string, object?> itemNode)
                {
                    continue;
                }

                string itemName = IsBlankNode(itemNode) ? issuer.GetId(GetId(itemNode)) : GetId(itemNode)!;
                CreateNodeMap(itemNode, graphs, graph, issuer, itemName, list: null);
                AddValue(subjects[itemName], reverseProperty.Key, referencedNode, propertyIsArray: true);
            }
        }
    }

    /// <summary>Merges the named graphs into the default graph (§8): each named graph becomes a <c>@graph</c> array on its default-graph node, full subjects only.</summary>
    /// <param name="graphs">The per-graph node maps.</param>
    /// <returns>The merged default graph.</returns>
    private static Dictionary<string, Dictionary<string, object?>> MergeNodeMaps(Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs)
    {
        Dictionary<string, Dictionary<string, object?>> defaultGraph = graphs[GraphNames.Default];
        List<string> graphNames = new(graphs.Keys);
        graphNames.Sort(StringComparer.Ordinal);

        foreach(string graphName in graphNames)
        {
            if(string.Equals(graphName, GraphNames.Default, StringComparison.Ordinal))
            {
                continue;
            }

            if(!defaultGraph.TryGetValue(graphName, out Dictionary<string, object?>? graphSubject))
            {
                graphSubject = new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = graphName };
                defaultGraph[graphName] = graphSubject;
            }

            if(graphSubject.TryGetValue(JsonLdKeywords.Graph, out object? existing) && existing is List<object?> existingGraph)
            {
                AppendGraphNodes(graphs[graphName], existingGraph);
            }
            else
            {
                List<object?> graphArray = new();
                AppendGraphNodes(graphs[graphName], graphArray);
                graphSubject[JsonLdKeywords.Graph] = graphArray;
            }
        }

        return defaultGraph;
    }

    /// <summary>Appends a named graph's full subjects (subject references excluded) to a <c>@graph</c> array in subject-sorted order.</summary>
    /// <param name="nodeMap">The named graph's node map.</param>
    /// <param name="graphArray">The destination <c>@graph</c> array.</param>
    private static void AppendGraphNodes(Dictionary<string, Dictionary<string, object?>> nodeMap, List<object?> graphArray)
    {
        List<string> ids = new(nodeMap.Keys);
        ids.Sort(StringComparer.Ordinal);
        foreach(string id in ids)
        {
            Dictionary<string, object?> node = nodeMap[id];
            if(!IsSubjectReference(node))
            {
                graphArray.Add(node);
            }
        }
    }

    /// <summary>Returns a value object with a blank-node <c>@type</c> relabelled through the issuer (the value object is otherwise shared).</summary>
    /// <param name="valueObject">The value object.</param>
    /// <param name="issuer">The blank-node issuer.</param>
    /// <returns>The (possibly relabelled) value object.</returns>
    private static IReadOnlyDictionary<string, object?> RelabelValueType(IReadOnlyDictionary<string, object?> valueObject, BlankNodeIssuer issuer)
    {
        if(valueObject.TryGetValue(JsonLdKeywords.Type, out object? type) && type is string typeIri && typeIri.StartsWith("_:", StringComparison.Ordinal))
        {
            Dictionary<string, object?> relabelled = new(valueObject, StringComparer.Ordinal)
            {
                [JsonLdKeywords.Type] = issuer.GetId(typeIri)
            };
            return relabelled;
        }

        return valueObject;
    }

    /// <summary>Appends a value to a node's property list without introducing duplicates (the node-map building rule, <c>allowDuplicate: false</c>).</summary>
    /// <param name="subject">The node.</param>
    /// <param name="property">The property key.</param>
    /// <param name="value">The value to add (or an empty list to ensure the property exists).</param>
    /// <param name="propertyIsArray">Whether the property always holds an array.</param>
    private static void AddValue(Dictionary<string, object?> subject, string property, object? value, bool propertyIsArray)
    {
        if(value is List<object?> { Count: 0 } && propertyIsArray && !subject.ContainsKey(property))
        {
            subject[property] = new List<object?>();
            return;
        }

        if(!subject.TryGetValue(property, out object? existing) || existing is not List<object?> list)
        {
            list = new List<object?>();
            subject[property] = list;
        }

        foreach(object? present in list)
        {
            if(ValueEquals(present, value))
            {
                return;
            }
        }

        list.Add(value);
    }

    /// <summary>
    /// Value equality for duplicate suppression, matching the JSON-LD
    /// <c>compareValues</c> rule: equal primitives, value objects equal on
    /// <c>@value</c>/<c>@type</c>/<c>@language</c>/<c>@index</c>, and node
    /// references equal on <c>@id</c>. Notably, two list objects are never
    /// equal here (so equivalent <c>@list</c> values are both retained).
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values count as equal.</returns>
    private static bool ValueEquals(object? left, object? right)
    {
        return (left, right) switch
        {
            (null, null) => true,
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            (bool a, bool b) => a == b,
            (long a, long b) => a == b,
            (double a, double b) => a.Equals(b),
            (JsonLdJsonNumber a, JsonLdJsonNumber b) => string.Equals(a.Raw, b.Raw, StringComparison.Ordinal),
            (IReadOnlyDictionary<string, object?> a, IReadOnlyDictionary<string, object?> b) when IsValue(a) && IsValue(b) =>
                ValueEquals(a.GetValueOrDefault(JsonLdKeywords.Value), b.GetValueOrDefault(JsonLdKeywords.Value))
                    && KeyEquals(a, b, JsonLdKeywords.Type)
                    && KeyEquals(a, b, JsonLdKeywords.Language)
                    && KeyEquals(a, b, JsonLdKeywords.Index),
            (IReadOnlyDictionary<string, object?> a, IReadOnlyDictionary<string, object?> b) when a.ContainsKey(JsonLdKeywords.Id) && b.ContainsKey(JsonLdKeywords.Id) =>
                KeyEquals(a, b, JsonLdKeywords.Id),
            _ => false
        };
    }

    /// <summary>Whether two objects agree on a string-valued key (both absent counts as agreement).</summary>
    /// <param name="left">The first object.</param>
    /// <param name="right">The second object.</param>
    /// <param name="key">The key to compare.</param>
    /// <returns><see langword="true"/> when the key matches.</returns>
    private static bool KeyEquals(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right, string key)
    {
        return (left.GetValueOrDefault(key), right.GetValueOrDefault(key)) switch
        {
            (null, null) => true,
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            _ => false
        };
    }

    /// <summary>Whether a node is a subject (an object that is not a value/list/set and has more than just <c>@id</c>).</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a subject.</returns>
    private static bool IsSubject(IReadOnlyDictionary<string, object?> node)
    {
        if(node.ContainsKey(JsonLdKeywords.Value) || node.ContainsKey(JsonLdKeywords.Set) || node.ContainsKey(JsonLdKeywords.List))
        {
            return false;
        }

        return node.Count > 1 || !node.ContainsKey(JsonLdKeywords.Id);
    }

    /// <summary>Whether a node is a bare node reference (exactly one member, <c>@id</c>).</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a node reference.</returns>
    private static bool IsSubjectReference(IReadOnlyDictionary<string, object?> node)
    {
        return node.Count == 1 && node.ContainsKey(JsonLdKeywords.Id);
    }

    /// <summary>Whether a node is a value object.</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a value object.</returns>
    private static bool IsValue(IReadOnlyDictionary<string, object?> node)
    {
        return node.ContainsKey(JsonLdKeywords.Value);
    }

    /// <summary>Whether a node is a list object.</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a list object.</returns>
    private static bool IsList(IReadOnlyDictionary<string, object?> node)
    {
        return node.ContainsKey(JsonLdKeywords.List);
    }

    /// <summary>Whether a node denotes a blank node: it has a blank-node <c>@id</c>, or no <c>@id</c> at all.</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a blank node.</returns>
    private static bool IsBlankNode(IReadOnlyDictionary<string, object?> node)
    {
        return GetId(node) is not { } id || id.StartsWith("_:", StringComparison.Ordinal);
    }

    /// <summary>Returns a node's <c>@id</c> string, or <see langword="null"/> when absent or non-string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The id, or <see langword="null"/>.</returns>
    private static string? GetId(IReadOnlyDictionary<string, object?> node)
    {
        return node.TryGetValue(JsonLdKeywords.Id, out object? id) ? id as string : null;
    }

    /// <summary>The well-known graph names.</summary>
    private static class GraphNames
    {
        /// <summary>The default graph's reserved name within the node map.</summary>
        public const string Default = "@default";
    }

    /// <summary>
    /// Allocates stable blank-node identifiers (<c>_:b0</c>, <c>_:b1</c>, …) by
    /// first-encounter order, returning the same identifier for a repeated input
    /// label so references stay consistent across the flattened output.
    /// </summary>
    private sealed class BlankNodeIssuer
    {
        private readonly Dictionary<string, string> issued = new(StringComparer.Ordinal);
        private int counter;

        /// <summary>Returns the stable identifier for a blank-node label, allocating a fresh one on first sight (an unnamed blank node always gets a fresh identifier).</summary>
        /// <param name="label">The original blank-node label, or <see langword="null"/> for an unnamed blank node.</param>
        /// <returns>The issued identifier.</returns>
        public string GetId(string? label)
        {
            if(label is not null && issued.TryGetValue(label, out string? existing))
            {
                return existing;
            }

            string id = string.Concat("_:b", counter.ToString(System.Globalization.CultureInfo.InvariantCulture));
            counter++;
            if(label is not null)
            {
                issued[label] = id;
            }

            return id;
        }
    }
}
