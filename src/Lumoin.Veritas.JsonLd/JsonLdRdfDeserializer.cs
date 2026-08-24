using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the JSON-LD 1.1 "Serialize RDF as JSON-LD" algorithm
/// (<see href="https://www.w3.org/TR/json-ld11-api/#serialize-rdf-as-json-ld-algorithm"/>):
/// given an RDF dataset (a sequence of <see cref="Quad"/>), produces the
/// flattened expanded JSON-LD that re-expanding would round-trip back to the
/// same dataset. The inverse of <see cref="JsonLdRdfSerializer"/>.
/// </summary>
/// <remarks>
/// <para>
/// A per-graph node map is built from the quads (default graph plus one map
/// per named graph), RDF list chains (<c>rdf:first</c>/<c>rdf:rest</c>
/// terminated by <c>rdf:nil</c>) are folded back into <c>@list</c> objects,
/// and the result is emitted as a subject-sorted array with named graphs
/// nested under <c>@graph</c>.
/// </para>
/// <para>
/// Output shape mirrors <c>JsonLdCompactor</c>: object maps to
/// <c>Dictionary&lt;string, object?&gt;</c>, array to <c>List&lt;object?&gt;</c>,
/// and leaves are <see cref="string"/>, <see cref="bool"/>,
/// <see cref="JsonLdJsonNumber"/> (a native numeric token), or a parsed
/// object graph for an <c>@json</c> literal.
/// </para>
/// </remarks>
public static class JsonLdRdfDeserializer
{
    private const string DefaultGraphName = "@default";


    /// <summary>
    /// Serializes an RDF dataset as expanded JSON-LD.
    /// </summary>
    /// <param name="quads">The dataset quads (default graph = <see cref="Quad.Graph"/> <see langword="null"/>).</param>
    /// <param name="useNativeTypes">When <see langword="true"/>, <c>xsd:boolean</c>/<c>xsd:integer</c>/<c>xsd:double</c> literals become native JSON values.</param>
    /// <param name="useRdfType">When <see langword="true"/>, <c>rdf:type</c> stays an ordinary property instead of becoming <c>@type</c>.</param>
    /// <param name="rdfDirection">Direction handling: <c>i18n-datatype</c> recovers <c>@direction</c> from an i18n datatype IRI.</param>
    /// <param name="jsonParser">Parses an <c>rdf:JSON</c> literal's lexical form into a JSON value; required only when the dataset carries JSON literals.</param>
    /// <returns>The expanded JSON-LD as an object graph (a <see cref="List{T}"/> of node objects).</returns>
    public static object? FromRdf(
        IReadOnlyList<Quad> quads,
        bool useNativeTypes = false,
        bool useRdfType = false,
        JsonLdRdfSerializer.DirectionMode rdfDirection = JsonLdRdfSerializer.DirectionMode.None,
        ParseJsonDelegate? jsonParser = null)
    {
        ArgumentNullException.ThrowIfNull(quads);

        Dictionary<string, GraphState> graphMap = new(StringComparer.Ordinal)
        {
            [DefaultGraphName] = new GraphState()
        };
        GraphState defaultGraph = graphMap[DefaultGraphName];

        //Single-reference tracking is dataset-wide (not per-graph): a node referenced in more than one
        //graph is not a list-chain member, so an object id maps to its sole usage or null once seen twice.
        Dictionary<string, Usage?> referencedOnce = new(StringComparer.Ordinal);

        //An RDF dataset is a set; duplicate quads collapse before serialization.
        HashSet<Quad> seen = new();

        foreach(Quad quad in quads)
        {
            if(!seen.Add(quad))
            {
                continue;
            }

            string graphName = quad.Graph is null ? DefaultGraphName : NodeId(quad.Graph)!;
            if(!graphMap.TryGetValue(graphName, out GraphState? graphState))
            {
                graphState = new GraphState();
                graphMap[graphName] = graphState;
            }
            if(!string.Equals(graphName, DefaultGraphName, StringComparison.Ordinal) && !defaultGraph.Nodes.ContainsKey(graphName))
            {
                defaultGraph.Nodes[graphName] = NewNode(graphName);
            }

            string subjectId = NodeId(quad.Subject)!;
            string predicate = quad.Predicate.Iri.ToString();
            RdfTerm objectTerm = quad.Object;

            if(!graphState.Nodes.TryGetValue(subjectId, out Dictionary<string, object?>? node))
            {
                node = NewNode(subjectId);
                graphState.Nodes[subjectId] = node;
            }

            string? objectNodeId = NodeId(objectTerm);
            bool objectIsNode = objectNodeId is not null;
            if(objectIsNode && !graphState.Nodes.ContainsKey(objectNodeId!))
            {
                graphState.Nodes[objectNodeId!] = NewNode(objectNodeId!);
            }

            //A blank-node subject carrying rdf:direction is a compound-literal candidate (W3C JSON-LD 1.1 §10.1).
            if(rdfDirection == JsonLdRdfSerializer.DirectionMode.CompoundLiteral
                && string.Equals(predicate, JsonLdRdfTerms.RdfDirection, StringComparison.Ordinal))
            {
                graphState.CompoundLiteralSubjects.Add(subjectId);
            }

            if(string.Equals(predicate, JsonLdRdfTerms.RdfType, StringComparison.Ordinal) && !useRdfType && objectIsNode)
            {
                AppendValue(node, JsonLdKeywords.Type, objectNodeId);
                continue;
            }

            Dictionary<string, object?> value = RdfToObject(objectTerm, useNativeTypes, rdfDirection, jsonParser);
            AppendValue(node, predicate, value);

            if(objectIsNode)
            {
                if(string.Equals(objectNodeId, JsonLdRdfTerms.RdfNil, StringComparison.Ordinal))
                {
                    graphState.NilUsages.Add(new Usage(node, predicate, value));
                }
                else if(referencedOnce.ContainsKey(objectNodeId!))
                {
                    //A second reference (in any graph) disqualifies the node as a list-chain member.
                    referencedOnce[objectNodeId!] = null;
                }
                else
                {
                    referencedOnce[objectNodeId!] = new Usage(node, predicate, value);
                }
            }
        }

        ConvertCompoundLiterals(graphMap, referencedOnce);
        ConvertListChains(graphMap, referencedOnce);

        return EmitResult(graphMap, defaultGraph);
    }

    /// <summary>Folds each compound-literal blank node (<c>rdf:value</c> + <c>rdf:direction</c>, optionally <c>rdf:language</c>) into the value object that referenced it (W3C JSON-LD 1.1 §10.1).</summary>
    /// <param name="graphMap">The per-graph node maps.</param>
    /// <param name="referencedOnce">The dataset-wide single-reference tracker.</param>
    private static void ConvertCompoundLiterals(Dictionary<string, GraphState> graphMap, Dictionary<string, Usage?> referencedOnce)
    {
        foreach(GraphState graphState in graphMap.Values)
        {
            foreach(string compoundLiteral in graphState.CompoundLiteralSubjects)
            {
                if(!referencedOnce.TryGetValue(compoundLiteral, out Usage? usage) || usage is null
                    || !graphState.Nodes.TryGetValue(compoundLiteral, out Dictionary<string, object?>? clNode))
                {
                    continue;
                }

                Dictionary<string, object?> head = usage.Value;
                head.Remove(JsonLdKeywords.Id);
                graphState.Nodes.Remove(compoundLiteral);

                if(FirstValue(clNode, JsonLdRdfTerms.RdfValue) is { } value)
                {
                    head[JsonLdKeywords.Value] = value;
                }
                if(FirstValue(clNode, JsonLdRdfTerms.RdfLanguage) is { } language)
                {
                    head[JsonLdKeywords.Language] = language;
                }
                if(FirstValue(clNode, JsonLdRdfTerms.RdfDirection) is { } direction)
                {
                    head[JsonLdKeywords.Direction] = direction;
                }
            }
        }
    }

    /// <summary>Returns the <c>@value</c> of the first value object stored under <paramref name="property"/> on a node, or <see langword="null"/>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="property">The property.</param>
    /// <returns>The first value, or <see langword="null"/>.</returns>
    private static object? FirstValue(Dictionary<string, object?> node, string property)
    {
        return node.TryGetValue(property, out object? raw) && raw is List<object?> { Count: > 0 } list
            && list[0] is Dictionary<string, object?> valueObject && valueObject.TryGetValue(JsonLdKeywords.Value, out object? value)
            ? value
            : null;
    }

    /// <summary>Folds <c>rdf:first</c>/<c>rdf:rest</c> chains terminated by <c>rdf:nil</c> back into <c>@list</c> objects, per graph.</summary>
    /// <param name="graphMap">The per-graph node maps.</param>
    /// <param name="referencedOnce">The dataset-wide single-reference tracker.</param>
    private static void ConvertListChains(Dictionary<string, GraphState> graphMap, Dictionary<string, Usage?> referencedOnce)
    {
        foreach(GraphState graphState in graphMap.Values)
        {
            if(!graphState.Nodes.ContainsKey(JsonLdRdfTerms.RdfNil))
            {
                continue;
            }

            foreach(Usage nilUsage in graphState.NilUsages)
            {
                Dictionary<string, object?> node = nilUsage.Node;
                string property = nilUsage.Property;
                Dictionary<string, object?> head = nilUsage.Value;
                List<object?> list = new();
                List<string> listNodes = new();

                //Walk the chain backwards while each node is a well-formed, single-referenced list cell.
                while(string.Equals(property, JsonLdRdfTerms.RdfRest, StringComparison.Ordinal)
                    && node.TryGetValue(JsonLdKeywords.Id, out object? nodeId) && nodeId is string nodeIdText
                    && referencedOnce.TryGetValue(nodeIdText, out Usage? singleUsage) && singleUsage is not null
                    && node.TryGetValue(JsonLdRdfTerms.RdfFirst, out object? first) && first is List<object?> { Count: 1 } firstList
                    && node.TryGetValue(JsonLdRdfTerms.RdfRest, out object? rest) && rest is List<object?> { Count: 1 }
                    && IsWellFormedListCell(node))
                {
                    list.Add(firstList[0]);
                    listNodes.Add(nodeIdText);

                    node = singleUsage.Node;
                    property = singleUsage.Property;
                    head = singleUsage.Value;

                    if(!IsBlankNode(node))
                    {
                        break;
                    }
                }

                head.Remove(JsonLdKeywords.Id);
                list.Reverse();
                head[JsonLdKeywords.List] = list;
                foreach(string listNode in listNodes)
                {
                    graphState.Nodes.Remove(listNode);
                }
            }
        }
    }

    /// <summary>Builds the subject-sorted result array, nesting each named graph under <c>@graph</c> on its default-graph node.</summary>
    /// <param name="graphMap">The per-graph node maps.</param>
    /// <param name="defaultGraph">The default graph state.</param>
    /// <returns>The result array.</returns>
    private static List<object?> EmitResult(Dictionary<string, GraphState> graphMap, GraphState defaultGraph)
    {
        List<object?> result = new();
        List<string> subjects = new(defaultGraph.Nodes.Keys);
        subjects.Sort(StringComparer.Ordinal);

        foreach(string subject in subjects)
        {
            Dictionary<string, object?> node = defaultGraph.Nodes[subject];
            if(graphMap.TryGetValue(subject, out GraphState? namedGraph))
            {
                List<object?> graphArray = new();
                List<string> graphSubjects = new(namedGraph.Nodes.Keys);
                graphSubjects.Sort(StringComparer.Ordinal);
                foreach(string graphSubject in graphSubjects)
                {
                    Dictionary<string, object?> graphNode = namedGraph.Nodes[graphSubject];
                    if(!IsSubjectReference(graphNode))
                    {
                        graphArray.Add(graphNode);
                    }
                }

                node[JsonLdKeywords.Graph] = graphArray;
            }

            if(!IsSubjectReference(node))
            {
                result.Add(node);
            }
        }

        return result;
    }

    /// <summary>Converts an RDF object term to its JSON-LD object form: a node reference for an IRI/blank node, otherwise a value object.</summary>
    /// <param name="term">The object term.</param>
    /// <param name="useNativeTypes">Whether to emit native numeric/boolean values.</param>
    /// <param name="rdfDirection">The direction mode.</param>
    /// <param name="jsonParser">The JSON-literal parser, when needed.</param>
    /// <returns>The JSON-LD object.</returns>
    private static Dictionary<string, object?> RdfToObject(
        RdfTerm term,
        bool useNativeTypes,
        JsonLdRdfSerializer.DirectionMode rdfDirection,
        ParseJsonDelegate? jsonParser)
    {
        if(NodeId(term) is { } nodeId)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = nodeId };
        }

        Literal literal = (Literal)term;
        string lexical = literal.Value.ToString();
        Dictionary<string, object?> result = new(StringComparer.Ordinal) { [JsonLdKeywords.Value] = lexical };

        if(literal.Language is { } language)
        {
            result[JsonLdKeywords.Language] = language.ToString();
            return result;
        }

        string type = literal.Datatype.Iri.ToString();
        if(string.Equals(type, JsonLdRdfTerms.RdfJson, StringComparison.Ordinal))
        {
            result[JsonLdKeywords.Value] = jsonParser is null
                ? lexical
                : MaterializeJson(jsonParser(Utf8Strings.From(lexical)));
            result[JsonLdKeywords.Type] = JsonLdKeywords.Json;
            return result;
        }

        if(useNativeTypes)
        {
            ApplyNativeType(result, type, lexical);
            return result;
        }

        if(rdfDirection == JsonLdRdfSerializer.DirectionMode.I18nDatatype && type.StartsWith(JsonLdRdfTerms.I18nNamespace, StringComparison.Ordinal))
        {
            ApplyI18nDatatype(result, type);
            return result;
        }

        if(!string.Equals(type, JsonLdRdfTerms.XsdString, StringComparison.Ordinal))
        {
            result[JsonLdKeywords.Type] = type;
        }

        return result;
    }

    /// <summary>Applies native-type coercion to a value object: boolean/integer/double become JSON natives when well-formed, otherwise the datatype is retained.</summary>
    /// <param name="result">The value object under construction.</param>
    /// <param name="type">The literal datatype IRI.</param>
    /// <param name="lexical">The literal lexical form.</param>
    private static void ApplyNativeType(Dictionary<string, object?> result, string type, string lexical)
    {
        //A well-formed boolean/integer/double becomes a native JSON value; anything else keeps its datatype.
        object? native = type switch
        {
            JsonLdRdfTerms.XsdBoolean when XsdBooleanLexical.TryParse(lexical, out bool boolean) => boolean,
            JsonLdRdfTerms.XsdInteger when TryParseInteger(lexical, out long integer) => integer,
            JsonLdRdfTerms.XsdDouble when double.TryParse(lexical, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double real) && double.IsFinite(real) => real,
            _ => null
        };

        if(native is not null)
        {
            result[JsonLdKeywords.Value] = native;
        }
        else
        {
            result[JsonLdKeywords.Type] = type;
        }
    }

    /// <summary>Recovers <c>@language</c>/<c>@direction</c> from an i18n datatype IRI of the form <c>…i18n#&lt;language&gt;_&lt;direction&gt;</c>.</summary>
    /// <param name="result">The value object under construction.</param>
    /// <param name="type">The i18n datatype IRI.</param>
    private static void ApplyI18nDatatype(Dictionary<string, object?> result, string type)
    {
        string suffix = type[JsonLdRdfTerms.I18nNamespace.Length..];
        int underscore = suffix.IndexOf('_', StringComparison.Ordinal);
        string language = underscore >= 0 ? suffix[..underscore] : suffix;
        string direction = underscore >= 0 ? suffix[(underscore + 1)..] : string.Empty;

        if(language.Length > 0)
        {
            result[JsonLdKeywords.Language] = language;
        }
        if(direction.Length > 0)
        {
            result[JsonLdKeywords.Direction] = direction;
        }
    }

    /// <summary>Whether a node is a well-formed list cell: exactly <c>@id</c>+<c>rdf:first</c>+<c>rdf:rest</c>, optionally with <c>@type</c> = <c>[rdf:List]</c>.</summary>
    /// <param name="node">The candidate node.</param>
    /// <returns><see langword="true"/> when well-formed.</returns>
    private static bool IsWellFormedListCell(Dictionary<string, object?> node)
    {
        int count = node.Count;
        if(count == 3)
        {
            return true;
        }
        if(count == 4 && node.TryGetValue(JsonLdKeywords.Type, out object? type) && type is List<object?> { Count: 1 } typeList)
        {
            return typeList[0] is string typeIri && string.Equals(typeIri, JsonLdRdfTerms.RdfList, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Whether a node is a blank node: it has no <c>@id</c>, or an <c>@id</c> that is a blank-node identifier.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when a blank node.</returns>
    private static bool IsBlankNode(Dictionary<string, object?> node)
    {
        if(node.TryGetValue(JsonLdKeywords.Id, out object? id))
        {
            return id is string idText && idText.StartsWith("_:", StringComparison.Ordinal);
        }

        return true;
    }

    /// <summary>Whether a node is a bare node reference (exactly one member, <c>@id</c>).</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when a node reference.</returns>
    private static bool IsSubjectReference(Dictionary<string, object?> node)
    {
        return node.Count == 1 && node.ContainsKey(JsonLdKeywords.Id);
    }

    /// <summary>Appends a value to a node's property list, creating the list on first use.</summary>
    /// <param name="node">The node.</param>
    /// <param name="property">The property key.</param>
    /// <param name="value">The value to append.</param>
    private static void AppendValue(Dictionary<string, object?> node, string property, object? value)
    {
        if(node.TryGetValue(property, out object? existing) && existing is List<object?> list)
        {
            list.Add(value);
            return;
        }

        node[property] = new List<object?> { value };
    }

    /// <summary>Creates a fresh node object carrying only its <c>@id</c>.</summary>
    /// <param name="id">The node identifier.</param>
    /// <returns>The node object.</returns>
    private static Dictionary<string, object?> NewNode(string id)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = id };
    }

    /// <summary>The JSON-LD identifier of an RDF term: the IRI for a named node, <c>_:</c>+label for a blank node, the deterministic Skolem IRI for an engine-minted node, <see langword="null"/> for a literal.</summary>
    /// <param name="term">The RDF term.</param>
    /// <returns>The identifier, or <see langword="null"/> when the term is a literal.</returns>
    private static string? NodeId(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => named.Iri.ToString(),
            BlankNode blank => string.Concat("_:", blank.Label.ToString()),
            EngineNode engine => engine.SkolemIri().ToString(),
            _ => null
        };
    }

    /// <summary>Materializes a parsed JSON node into the deserializer's object-graph shape (used for an <c>@json</c> literal value).</summary>
    /// <param name="node">The parsed JSON node.</param>
    /// <returns>The object graph.</returns>
    private static object? MaterializeJson(JsonNode node)
    {
        return node.Kind switch
        {
            JsonNodeKind.Null => null,
            JsonNodeKind.String => node.GetString(),
            JsonNodeKind.True => true,
            JsonNodeKind.False => false,
            JsonNodeKind.Number => new JsonLdJsonNumber(node.GetRawNumber()),
            JsonNodeKind.Array => MaterializeJsonArray(node),
            JsonNodeKind.Object => MaterializeJsonObject(node),
            _ => null
        };
    }

    /// <summary>Materializes a JSON array node.</summary>
    /// <param name="array">The array node.</param>
    /// <returns>The materialized list.</returns>
    private static List<object?> MaterializeJsonArray(JsonNode array)
    {
        List<object?> items = new();
        foreach(JsonNode item in array.EnumerateArray())
        {
            items.Add(MaterializeJson(item));
        }

        return items;
    }

    /// <summary>Materializes a JSON object node (an <c>@json</c> literal preserves member order via the parser).</summary>
    /// <param name="obj">The object node.</param>
    /// <returns>The materialized map.</returns>
    private static Dictionary<string, object?> MaterializeJsonObject(JsonNode obj)
    {
        Dictionary<string, object?> map = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> member in obj.EnumerateObject())
        {
            map[member.Key] = MaterializeJson(member.Value);
        }

        return map;
    }

    /// <summary>Parses a canonical xsd:integer lexical form (it must round-trip), yielding the native value.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="value">The parsed integer when canonical.</param>
    /// <returns><see langword="true"/> when the lexical form is a canonical integer.</returns>
    private static bool TryParseInteger(string lexical, out long value)
    {
        return long.TryParse(lexical, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value)
            && string.Equals(value.ToString(System.Globalization.CultureInfo.InvariantCulture), lexical, StringComparison.Ordinal);
    }

    /// <summary>The accumulating state of one graph during deserialization.</summary>
    private sealed class GraphState
    {
        /// <summary>Gets the node map (subject id → node object).</summary>
        public Dictionary<string, Dictionary<string, object?>> Nodes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the blank-node subjects that carry an <c>rdf:direction</c> — compound-literal candidates (i18n base direction).</summary>
        public HashSet<string> CompoundLiteralSubjects { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the usages of <c>rdf:nil</c> in this graph (each a candidate list tail).</summary>
        public List<Usage> NilUsages { get; } = new();
    }

    /// <summary>A single use of a node as an object: the referencing node, the property, and the value object placed in that property's list.</summary>
    /// <param name="Node">The referencing node.</param>
    /// <param name="Property">The property the reference appears under.</param>
    /// <param name="Value">The value object (a node reference) in the property's list.</param>
    private sealed record Usage(Dictionary<string, object?> Node, string Property, Dictionary<string, object?> Value);
}

