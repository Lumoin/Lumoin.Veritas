using System;
using System.Collections.Generic;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Node Map Generation (W3C JSON-LD 1.1 API §8): the shared machinery that
/// collects every subject of an expanded document into per-graph node maps,
/// relabelling blank nodes (<c>_:b0</c>, <c>_:b1</c>, …) by first encounter,
/// hoisting nested nodes to references, and folding <c>@list</c>/<c>@reverse</c>/
/// <c>@included</c>/<c>@graph</c>. Consumed by <see cref="JsonLdFlattener"/>
/// (flattening) and <see cref="JsonLdFramer"/> (framing).
/// </summary>
internal static class JsonLdNodeMap
{
    /// <summary>The default graph's reserved name within a node map.</summary>
    public const string DefaultGraph = "@default";

    /// <summary>The merged graph's reserved name (framing's merged-graph mode).</summary>
    public const string MergedGraph = "@merged";

    /// <summary>Generates the per-graph node maps for an expanded document (graph name → subject id → node).</summary>
    /// <param name="expanded">The expanded document.</param>
    /// <returns>The per-graph node maps.</returns>
    public static Dictionary<string, Dictionary<string, Dictionary<string, object?>>> Generate(IReadOnlyList<object?> expanded)
    {
        BlankNodeIssuer issuer = new();
        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs = new(StringComparer.Ordinal)
        {
            [DefaultGraph] = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal)
        };

        CreateNodeMap(expanded, graphs, DefaultGraph, issuer, name: null, list: null);

        return graphs;
    }

    /// <summary>Merges the named graphs into the default graph (§8 flattening merge): each named graph becomes a <c>@graph</c> array on its default-graph node, full subjects only.</summary>
    /// <param name="graphs">The per-graph node maps.</param>
    /// <returns>The merged default graph.</returns>
    public static Dictionary<string, Dictionary<string, object?>> MergeToDefault(Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs)
    {
        Dictionary<string, Dictionary<string, object?>> defaultGraph = graphs[DefaultGraph];
        List<string> graphNames = new(graphs.Keys);
        graphNames.Sort(StringComparer.Ordinal);

        foreach(string graphName in graphNames)
        {
            if(string.Equals(graphName, DefaultGraph, StringComparison.Ordinal))
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

    /// <summary>Merges all graphs (default + named, in subject-sorted order) into one node map (§8 merged-graph, used by framing): keywords are copied and property values merged, deep-cloned.</summary>
    /// <param name="graphs">The per-graph node maps.</param>
    /// <returns>The merged node map.</returns>
    public static Dictionary<string, Dictionary<string, object?>> MergeAllGraphs(Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs)
    {
        Dictionary<string, Dictionary<string, object?>> merged = new(StringComparer.Ordinal);
        List<string> graphNames = new(graphs.Keys);
        graphNames.Sort(StringComparer.Ordinal);

        foreach(string graphName in graphNames)
        {
            List<string> ids = new(graphs[graphName].Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach(string id in ids)
            {
                Dictionary<string, object?> node = graphs[graphName][id];
                if(!merged.TryGetValue(id, out Dictionary<string, object?>? mergedNode))
                {
                    mergedNode = new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = id };
                    merged[id] = mergedNode;
                }

                List<string> properties = new(node.Keys);
                properties.Sort(StringComparer.Ordinal);
                foreach(string property in properties)
                {
                    if(IriUtils.IsKeyword(property) && !JsonLdKeywords.IsType(property))
                    {
                        mergedNode[property] = Clone(node[property]);
                    }
                    else if(node[property] is IReadOnlyList<object?> values)
                    {
                        foreach(object? value in values)
                        {
                            AddValue(mergedNode, property, Clone(value), propertyIsArray: true, allowDuplicate: false);
                        }
                    }
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Node Map Generation (§8): recursively flattens the expanded
    /// <paramref name="input"/> into the per-graph node maps, naming blank nodes
    /// through <paramref name="issuer"/> and, when <paramref name="list"/> is
    /// non-null, appending list members to it.
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
            foreach(object? item in array)
            {
                CreateNodeMap(item, graphs, graph, issuer, name: null, list);
            }

            return;
        }

        if(input is not IReadOnlyDictionary<string, object?> element)
        {
            //A non-object (scalar) is a list member.
            list?.Add(input);
            return;
        }

        //A value object is a leaf: relabel a blank-node @type, then append to the list when assembling one.
        if(IsValue(element))
        {
            list?.Add(RelabelValueType(element, issuer));
            return;
        }

        if(list is not null && IsList(element))
        {
            List<object?> innerList = new();
            CreateNodeMap(element[JsonLdKeywords.List], graphs, graph, issuer, name, innerList);
            list.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.List] = innerList });
            return;
        }

        //Otherwise the input is a subject node.

        //The spec names @type blank nodes first, so they are issued before the subject's own name.
        if(element.TryGetValue(JsonLdKeywords.Type, out object? typeValue) && typeValue is IReadOnlyList<object?> typeList)
        {
            foreach(object? type in typeList)
            {
                if(type is string typeIri && typeIri.StartsWith("_:", StringComparison.Ordinal))
                {
                    issuer.GetId(typeIri);
                }
            }
        }

        name ??= IsBlankNode(element) ? issuer.GetId(GetId(element)) : GetId(element)!;

        list?.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = name });

        Dictionary<string, Dictionary<string, object?>> subjects = graphs[graph];
        if(!subjects.TryGetValue(name, out Dictionary<string, object?>? subject))
        {
            subject = new Dictionary<string, object?>(StringComparer.Ordinal);
            subjects[name] = subject;
        }
        subject[JsonLdKeywords.Id] = name;

        List<string> properties = new(element.Keys);
        properties.Sort(StringComparer.Ordinal);
        foreach(string rawProperty in properties)
        {
            if(JsonLdKeywords.IsId(rawProperty))
            {
                continue;
            }

            if(JsonLdKeywords.IsReverse(rawProperty))
            {
                AddReverse(element[rawProperty], name, subjects, graphs, graph, issuer);
                continue;
            }

            if(JsonLdKeywords.IsGraph(rawProperty))
            {
                if(!graphs.ContainsKey(name))
                {
                    graphs[name] = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
                }

                CreateNodeMap(element[rawProperty], graphs, name, issuer, name: null, list: null);
                continue;
            }

            if(JsonLdKeywords.IsIncluded(rawProperty))
            {
                CreateNodeMap(element[rawProperty], graphs, graph, issuer, name: null, list: null);
                continue;
            }

            //Non-@type keywords are copied verbatim onto the subject; a second, differing @index conflicts.
            if(!JsonLdKeywords.IsType(rawProperty) && IriUtils.IsKeyword(rawProperty))
            {
                if(string.Equals(rawProperty, JsonLdKeywords.Index, StringComparison.Ordinal)
                    && subject.TryGetValue(JsonLdKeywords.Index, out object? existingIndex)
                    && !ValueEquals(existingIndex, element[rawProperty]))
                {
                    throw new JsonLdProcessingException("Conflicting @index values detected while flattening.");
                }

                subject[rawProperty] = element[rawProperty];
                continue;
            }

            //A blank-node property name is relabelled.
            string property = rawProperty.StartsWith("_:", StringComparison.Ordinal) ? issuer.GetId(rawProperty) : rawProperty;

            if(element[rawProperty] is not IReadOnlyList<object?> objects)
            {
                continue;
            }

            if(objects.Count == 0)
            {
                AddValue(subject, property, new List<object?>(), propertyIsArray: true, allowDuplicate: false);
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
            AddValue(subject, property, typeName, propertyIsArray: true, allowDuplicate: false);
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
            AddValue(subject, property, new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = id }, propertyIsArray: true, allowDuplicate: false);
            CreateNodeMap(objectNode, graphs, graph, issuer, id, list: null);
            return;
        }

        if(rawObject is IReadOnlyDictionary<string, object?> valueObject && IsValue(valueObject))
        {
            AddValue(subject, property, RelabelValueType(valueObject, issuer), propertyIsArray: true, allowDuplicate: false);
            return;
        }

        if(rawObject is IReadOnlyDictionary<string, object?> listObject && IsList(listObject))
        {
            List<object?> innerList = new();
            CreateNodeMap(listObject[JsonLdKeywords.List], graphs, graph, issuer, name, innerList);
            AddValue(subject, property, new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.List] = innerList }, propertyIsArray: true, allowDuplicate: false);
            return;
        }

        //Any other object recurses (and is added as-is).
        CreateNodeMap(rawObject, graphs, graph, issuer, name, list: null);
        AddValue(subject, property, rawObject, propertyIsArray: true, allowDuplicate: false);
    }

    /// <summary>Hoists the subjects under a node's <c>@reverse</c> map, adding the back-reference to each referenced subject (§8 step 6.4).</summary>
    /// <param name="reverseValue">The <c>@reverse</c> map.</param>
    /// <param name="name">The current subject's name.</param>
    /// <param name="subjects">The current graph's subject map.</param>
    /// <param name="graphs">The graph maps.</param>
    /// <param name="graph">The current graph name.</param>
    /// <param name="issuer">The blank-node issuer.</param>
    private static void AddReverse(
        object? reverseValue,
        string name,
        Dictionary<string, Dictionary<string, object?>> subjects,
        Dictionary<string, Dictionary<string, Dictionary<string, object?>>> graphs,
        string graph,
        BlankNodeIssuer issuer)
    {
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
                AddValue(subjects[itemName], reverseProperty.Key, referencedNode, propertyIsArray: true, allowDuplicate: false);
            }
        }
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
            return new Dictionary<string, object?>(valueObject, StringComparer.Ordinal)
            {
                [JsonLdKeywords.Type] = issuer.GetId(typeIri)
            };
        }

        return valueObject;
    }

    /// <summary>Appends a value to a node's property list. Node-map building passes <c>allowDuplicate: false</c> (skip a value already present); framing passes <c>allowDuplicate: true</c> so equal <c>@list</c> members and repeated values are retained.</summary>
    /// <param name="subject">The node.</param>
    /// <param name="property">The property key.</param>
    /// <param name="value">The value to add (or an empty list to ensure the property exists).</param>
    /// <param name="propertyIsArray">Whether the property always holds an array.</param>
    /// <param name="allowDuplicate">Whether a value equal to one already present is still appended.</param>
    public static void AddValue(Dictionary<string, object?> subject, string property, object? value, bool propertyIsArray, bool allowDuplicate)
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

        if(!allowDuplicate)
        {
            foreach(object? present in list)
            {
                if(ValueEquals(present, value))
                {
                    return;
                }
            }
        }

        list.Add(value);
    }

    /// <summary>
    /// Value equality matching the JSON-LD <c>compareValues</c> rule: equal
    /// primitives, value objects equal on <c>@value</c>/<c>@type</c>/
    /// <c>@language</c>/<c>@index</c>, and node references equal on <c>@id</c>.
    /// Two list objects are never equal here (equivalent <c>@list</c> values
    /// are both retained).
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values count as equal.</returns>
    public static bool ValueEquals(object? left, object? right)
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

    /// <summary>Deep-clones an object-graph value (maps, lists, and immutable scalars).</summary>
    /// <param name="value">The value to clone.</param>
    /// <returns>The cloned value.</returns>
    public static object? Clone(object? value)
    {
        return value switch
        {
            IReadOnlyDictionary<string, object?> map => CloneMap(map),
            IReadOnlyList<object?> list => CloneList(list),
            _ => value
        };
    }

    /// <summary>Deep-clones an object map.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The cloned map.</returns>
    private static Dictionary<string, object?> CloneMap(IReadOnlyDictionary<string, object?> map)
    {
        Dictionary<string, object?> clone = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, object?> entry in map)
        {
            clone[entry.Key] = Clone(entry.Value);
        }

        return clone;
    }

    /// <summary>Deep-clones a value list.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The cloned list.</returns>
    private static List<object?> CloneList(IReadOnlyList<object?> list)
    {
        List<object?> clone = new(list.Count);
        foreach(object? item in list)
        {
            clone.Add(Clone(item));
        }

        return clone;
    }

    /// <summary>Whether a node is a subject (an object that is not a value/list/set and has more than just <c>@id</c>).</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a subject.</returns>
    public static bool IsSubject(IReadOnlyDictionary<string, object?> node)
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
    public static bool IsSubjectReference(IReadOnlyDictionary<string, object?> node)
    {
        return node.Count == 1 && node.ContainsKey(JsonLdKeywords.Id);
    }

    /// <summary>Whether a node is a value object.</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a value object.</returns>
    public static bool IsValue(IReadOnlyDictionary<string, object?> node)
    {
        return node.ContainsKey(JsonLdKeywords.Value);
    }

    /// <summary>Whether a node is a list object.</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a list object.</returns>
    public static bool IsList(IReadOnlyDictionary<string, object?> node)
    {
        return node.ContainsKey(JsonLdKeywords.List);
    }

    /// <summary>Whether a node denotes a blank node: it has a blank-node <c>@id</c>, or no <c>@id</c> at all.</summary>
    /// <param name="node">The candidate.</param>
    /// <returns><see langword="true"/> when a blank node.</returns>
    public static bool IsBlankNode(IReadOnlyDictionary<string, object?> node)
    {
        return GetId(node) is not { } id || id.StartsWith("_:", StringComparison.Ordinal);
    }

    /// <summary>Returns a node's <c>@id</c> string, or <see langword="null"/> when absent or non-string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The id, or <see langword="null"/>.</returns>
    public static string? GetId(IReadOnlyDictionary<string, object?> node)
    {
        return node.TryGetValue(JsonLdKeywords.Id, out object? id) ? id as string : null;
    }

    /// <summary>
    /// Allocates stable blank-node identifiers (<c>_:b0</c>, <c>_:b1</c>, …) by
    /// first-encounter order, returning the same identifier for a repeated input
    /// label so references stay consistent across the output.
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
