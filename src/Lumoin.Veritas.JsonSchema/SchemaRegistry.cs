using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Json;
using Ptr = Lumoin.Veritas.JsonPointer.JsonPointer;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// Indexes the schemas reachable from a root document so that <c>$ref</c> can be resolved: documents and
/// <c>$id</c>-bearing subschemas by their base URI, and <c>$anchor</c> declarations by their plain-name
/// fragment. Remote documents are pulled in on demand through a <see cref="SchemaDocumentLoader"/>.
/// </summary>
/// <remarks>
/// The schema tree is walked iteratively (an explicit work stack of node/base pairs), never by call-stack
/// recursion. <c>$dynamicRef</c>/<c>$dynamicAnchor</c> are not yet resolved here.
/// </remarks>
internal sealed class SchemaRegistry
{
    private readonly Dictionary<string, JsonNode> resourcesByUri = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode> nodesByAnchor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode> nodesByDynamicAnchor = new(StringComparer.Ordinal);
    private readonly SchemaDocumentLoader? loader;

    /// <summary>The keywords whose value is a single subschema.</summary>
    private static string[] SingleSubschemaKeywords { get; } =
    [
        JsonSchemaKeywords.Items, JsonSchemaKeywords.AdditionalProperties, JsonSchemaKeywords.Contains,
        JsonSchemaKeywords.PropertyNames, JsonSchemaKeywords.Not, JsonSchemaKeywords.If,
        JsonSchemaKeywords.Then, JsonSchemaKeywords.Else
    ];

    /// <summary>The keywords whose value is an object mapping names to subschemas.</summary>
    private static string[] SubschemaMapKeywords { get; } =
    [
        JsonSchemaKeywords.Properties, JsonSchemaKeywords.PatternProperties,
        JsonSchemaKeywords.Defs, JsonSchemaKeywords.DependentSchemas
    ];

    /// <summary>The keywords whose value is an array of subschemas.</summary>
    private static string[] SubschemaArrayKeywords { get; } =
    [
        JsonSchemaKeywords.AllOf, JsonSchemaKeywords.AnyOf, JsonSchemaKeywords.OneOf, JsonSchemaKeywords.PrefixItems
    ];

    /// <summary>Initialises a registry indexing one root document.</summary>
    /// <param name="root">The root schema document.</param>
    /// <param name="baseUri">The document's retrieval base URI (empty when none was supplied).</param>
    /// <param name="loader">The loader for remote documents, or <see langword="null"/>.</param>
    public SchemaRegistry(JsonNode root, string baseUri, SchemaDocumentLoader? loader)
    {
        this.loader = loader;
        Register(root, baseUri);
    }

    /// <summary>Resolves a reference against a base URI to a target schema and the base URI in effect at that target.</summary>
    /// <param name="baseUri">The base URI in effect where the reference appears.</param>
    /// <param name="reference">The reference value (<c>$ref</c>).</param>
    /// <param name="target">On success, the referenced schema node.</param>
    /// <param name="targetBaseUri">On success, the base URI in effect at the target document.</param>
    /// <returns><see langword="true"/> when the reference resolves.</returns>
    public bool TryResolve(string baseUri, string reference, out JsonNode target, out string targetBaseUri)
    {
        target = default;
        targetBaseUri = string.Empty;

        string absolute = SchemaReferenceIris.Resolve(baseUri, reference);
        int hash = absolute.IndexOf('#', StringComparison.Ordinal);
        string documentUri = hash < 0 ? absolute : absolute[..hash];
        string fragment = hash < 0 ? string.Empty : absolute[(hash + 1)..];

        if(!resourcesByUri.TryGetValue(documentUri, out JsonNode document) && !TryLoad(documentUri, out document))
        {
            return false;
        }

        targetBaseUri = documentUri;

        if(fragment.Length == 0)
        {
            target = document;

            return true;
        }

        if(fragment[0] == '/')
        {
            return TryNavigatePointer(document, fragment, out target);
        }

        return nodesByAnchor.TryGetValue(documentUri + "#" + fragment, out target);
    }

    /// <summary>Resolves a <c>$dynamicRef</c> against a base URI and the dynamic scope.</summary>
    /// <param name="baseUri">The base URI in effect where the reference appears.</param>
    /// <param name="reference">The <c>$dynamicRef</c> value.</param>
    /// <param name="dynamicScopeOutermostFirst">The base URIs of the dynamic scope, outermost resource first.</param>
    /// <param name="target">On success, the referenced schema node.</param>
    /// <param name="targetBaseUri">On success, the base URI in effect at the target.</param>
    /// <returns><see langword="true"/> when the reference resolves.</returns>
    public bool TryResolveDynamic(string baseUri, string reference, IReadOnlyList<string> dynamicScopeOutermostFirst, out JsonNode target, out string targetBaseUri)
    {
        target = default;
        targetBaseUri = string.Empty;

        string absolute = SchemaReferenceIris.Resolve(baseUri, reference);
        int hash = absolute.IndexOf('#', StringComparison.Ordinal);
        string documentUri = hash < 0 ? absolute : absolute[..hash];
        string fragment = hash < 0 ? string.Empty : absolute[(hash + 1)..];

        //Dynamic resolution applies only when the reference names a plain-name $dynamicAnchor that the
        //statically-resolved target's resource also declares; then the OUTERMOST such anchor in the
        //dynamic scope wins. Otherwise $dynamicRef behaves exactly like $ref.
        if(fragment.Length > 0 && fragment[0] != '/' && nodesByDynamicAnchor.ContainsKey(documentUri + "#" + fragment))
        {
            foreach(string scopeBase in dynamicScopeOutermostFirst)
            {
                if(nodesByDynamicAnchor.TryGetValue(scopeBase + "#" + fragment, out target))
                {
                    targetBaseUri = scopeBase;

                    return true;
                }
            }
        }

        return TryResolve(baseUri, reference, out target, out targetBaseUri);
    }

    /// <summary>Loads a remote document through the loader and indexes it.</summary>
    /// <param name="documentUri">The absolute document URI.</param>
    /// <param name="document">On success, the loaded document root.</param>
    /// <returns><see langword="true"/> when the document was loaded.</returns>
    private bool TryLoad(string documentUri, out JsonNode document)
    {
        document = default;
        if(loader is null || !loader(documentUri, out JsonNode loaded))
        {
            return false;
        }

        Register(loaded, documentUri);

        return resourcesByUri.TryGetValue(documentUri, out document);
    }

    /// <summary>Walks a document, indexing it and every <c>$id</c>/<c>$anchor</c> it declares.</summary>
    /// <param name="root">The document root.</param>
    /// <param name="baseUri">The document's base URI.</param>
    private void Register(JsonNode root, string baseUri)
    {
        resourcesByUri.TryAdd(baseUri, root);

        Stack<(JsonNode Node, string Base)> pending = new();
        pending.Push((root, baseUri));

        while(pending.Count > 0)
        {
            (JsonNode node, string currentBase) = pending.Pop();
            if(node.Kind != JsonNodeKind.Object)
            {
                continue;
            }

            string effectiveBase = currentBase;
            if(node.TryGetProperty(JsonSchemaKeywords.Id, out JsonNode id) && id.Kind == JsonNodeKind.String)
            {
                effectiveBase = StripFragment(SchemaReferenceIris.Resolve(currentBase, id.GetString()));
                resourcesByUri[effectiveBase] = node;
            }

            if(node.TryGetProperty(JsonSchemaKeywords.Anchor, out JsonNode anchor) && anchor.Kind == JsonNodeKind.String)
            {
                nodesByAnchor[effectiveBase + "#" + anchor.GetString()] = node;
            }

            //A $dynamicAnchor is also a plain-name anchor for ordinary $ref, and additionally a dynamic target.
            if(node.TryGetProperty(JsonSchemaKeywords.DynamicAnchor, out JsonNode dynamicAnchor) && dynamicAnchor.Kind == JsonNodeKind.String)
            {
                string key = effectiveBase + "#" + dynamicAnchor.GetString();
                nodesByAnchor.TryAdd(key, node);
                nodesByDynamicAnchor[key] = node;
            }

            PushSubschemas(node, effectiveBase, pending);
        }
    }

    /// <summary>Pushes a schema node's subschema children onto the walk stack.</summary>
    /// <param name="node">The schema node.</param>
    /// <param name="baseUri">The base URI in effect for the children.</param>
    /// <param name="pending">The walk stack.</param>
    private static void PushSubschemas(JsonNode node, string baseUri, Stack<(JsonNode, string)> pending)
    {
        foreach(string keyword in SingleSubschemaKeywords)
        {
            if(node.TryGetProperty(keyword, out JsonNode subschema))
            {
                pending.Push((subschema, baseUri));
            }
        }

        foreach(string keyword in SubschemaMapKeywords)
        {
            if(node.TryGetProperty(keyword, out JsonNode map) && map.Kind == JsonNodeKind.Object)
            {
                foreach(KeyValuePair<string, JsonNode> member in map.EnumerateObject())
                {
                    pending.Push((member.Value, baseUri));
                }
            }
        }

        foreach(string keyword in SubschemaArrayKeywords)
        {
            if(node.TryGetProperty(keyword, out JsonNode array) && array.Kind == JsonNodeKind.Array)
            {
                foreach(JsonNode subschema in array.EnumerateArray())
                {
                    pending.Push((subschema, baseUri));
                }
            }
        }
    }

    /// <summary>Navigates a JSON-pointer fragment from a document root to a subschema.</summary>
    /// <param name="document">The document root.</param>
    /// <param name="fragment">The fragment (a JSON pointer, possibly percent-encoded).</param>
    /// <param name="target">On success, the node the pointer addresses.</param>
    /// <returns><see langword="true"/> when the pointer resolves to a node.</returns>
    private static bool TryNavigatePointer(JsonNode document, string fragment, out JsonNode target)
    {
        target = default;
        if(!Ptr.TryParse(Uri.UnescapeDataString(fragment), out Ptr pointer))
        {
            return false;
        }

        JsonNode current = document;
        foreach(Lumoin.Veritas.JsonPointer.JsonPointerSegment segment in pointer.Segments)
        {
            if(current.Kind == JsonNodeKind.Object)
            {
                if(!current.TryGetProperty(segment.Value, out current))
                {
                    return false;
                }
            }
            else if(current.Kind == JsonNodeKind.Array && segment.TryGetArrayIndex(out int index))
            {
                if(!TryElementAt(current, index, out current))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        target = current;

        return true;
    }

    /// <summary>Returns the array element at an index.</summary>
    /// <param name="array">The array node.</param>
    /// <param name="index">The zero-based index.</param>
    /// <param name="element">On success, the element.</param>
    /// <returns><see langword="true"/> when the index is in range.</returns>
    private static bool TryElementAt(JsonNode array, int index, out JsonNode element)
    {
        element = default;
        int current = 0;
        foreach(JsonNode candidate in array.EnumerateArray())
        {
            if(current == index)
            {
                element = candidate;

                return true;
            }

            current++;
        }

        return false;
    }

    /// <summary>Removes a fragment from a URI, if present.</summary>
    /// <param name="uri">The URI.</param>
    /// <returns>The URI without its fragment.</returns>
    private static string StripFragment(string uri)
    {
        int hash = uri.IndexOf('#', StringComparison.Ordinal);

        return hash < 0 ? uri : uri[..hash];
    }
}
