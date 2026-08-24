using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Cbor.CborLd;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.JsonLd.Adapters;

/// <summary>
/// Converts between a JSON-LD-shaped <see cref="JsonNode"/> tree and a
/// format-neutral <see cref="CborLdInputNode"/> tree in both directions:
/// <see cref="FromJsonLd"/> walks a JSON-LD tree and produces the
/// equivalent CBOR-LD input tree, and <see cref="ToJsonLd"/> wraps a
/// CBOR-LD input tree as a <see cref="JsonNode"/> backed by a CBOR-LD-
/// reading navigator (no JSON-bytes intermediate).
/// </summary>
/// <remarks>
/// <para>
/// The forward walk is iterative — a <see cref="Stack{T}"/> of frames per
/// the project's no-recursion rule. JSON numbers are heuristically
/// classified as integer or floating-point by inspecting the raw lexical
/// form: a number with no decimal point or exponent that fits in
/// <see cref="long"/> becomes <see cref="CborLdInputInt"/>; otherwise
/// <see cref="CborLdInputDouble"/>.
/// </para>
/// </remarks>
public static class CborLdInputNodeAdapter
{
    /// <summary>
    /// Converts a <see cref="JsonNode"/> tree to its
    /// <see cref="CborLdInputNode"/> equivalent.
    /// </summary>
    /// <param name="node">The JSON-LD node to convert.</param>
    /// <returns>The equivalent CBOR-LD input node.</returns>
    /// <exception cref="ArgumentException">The node has a default
    /// <see cref="JsonNode.Handle"/> or
    /// <see cref="JsonNode.Navigator"/>.</exception>
    public static CborLdInputNode FromJsonLd(JsonNode node)
    {
        if(node.Handle is null || node.Navigator is null)
        {
            throw new ArgumentException("FromJsonLd requires a fully-initialised JsonNode.", nameof(node));
        }

        Stack<Frame> stack = new();
        CborLdInputNode? rootResult = null;
        stack.Push(new Frame(node, parent: null, parentKey: null, parentIndex: -1));

        while(stack.Count > 0)
        {
            Frame frame = stack.Peek();
            JsonNodeNavigator navigator = frame.Node.Navigator!;
            object handle = frame.Node.Handle!;
            JsonNodeKind kind = navigator.GetKind(handle);

            switch(kind)
            {
                case JsonNodeKind.Null:
                {
                    rootResult = Complete(stack, frame, CborLdInputNull.Instance, rootResult);
                    break;
                }
                case JsonNodeKind.True:
                {
                    rootResult = Complete(stack, frame, new CborLdInputBool(true), rootResult);
                    break;
                }
                case JsonNodeKind.False:
                {
                    rootResult = Complete(stack, frame, new CborLdInputBool(false), rootResult);
                    break;
                }
                case JsonNodeKind.String:
                {
                    rootResult = Complete(stack, frame, new CborLdInputString(navigator.GetString(handle)), rootResult);
                    break;
                }
                case JsonNodeKind.Number:
                {
                    string raw = navigator.GetRawNumber(handle);
                    CborLdInputNode numberNode = ParseNumber(raw);
                    rootResult = Complete(stack, frame, numberNode, rootResult);
                    break;
                }
                case JsonNodeKind.Array:
                {
                    if(!frame.MaterialisedChildren)
                    {
                        List<JsonNode> children = [];
                        foreach(JsonNode child in navigator.EnumerateArray(handle))
                        {
                            children.Add(child);
                        }
                        frame.ArrayChildren = children;
                        frame.MaterialisedChildren = true;
                        frame.Items = new List<CborLdInputNode>(children.Count);
                    }
                    if(frame.NextIndex < frame.ArrayChildren!.Count)
                    {
                        int index = frame.NextIndex++;
                        stack.Push(new Frame(frame.ArrayChildren[index], parent: frame, parentKey: null, parentIndex: index));
                    }
                    else
                    {
                        CborLdInputNode built = new CborLdInputArray(frame.Items!);
                        rootResult = Complete(stack, frame, built, rootResult);
                    }
                    break;
                }
                case JsonNodeKind.Object:
                {
                    if(!frame.MaterialisedChildren)
                    {
                        List<KeyValuePair<string, JsonNode>> children = [];
                        foreach(KeyValuePair<string, JsonNode> child in navigator.EnumerateObject(handle))
                        {
                            children.Add(child);
                        }
                        frame.ObjectChildren = children;
                        frame.MaterialisedChildren = true;
                        frame.Entries = new List<KeyValuePair<string, CborLdInputNode>>(children.Count);
                    }
                    if(frame.NextIndex < frame.ObjectChildren!.Count)
                    {
                        KeyValuePair<string, JsonNode> entry = frame.ObjectChildren[frame.NextIndex];
                        frame.NextIndex++;
                        stack.Push(new Frame(entry.Value, parent: frame, parentKey: entry.Key, parentIndex: -1));
                    }
                    else
                    {
                        CborLdInputNode built = new CborLdInputMap(frame.Entries!);
                        rootResult = Complete(stack, frame, built, rootResult);
                    }
                    break;
                }
                default:
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture, $"Unrecognised JsonNodeKind: {kind}."));
                }
            }
        }

        return rootResult!;
    }

    /// <summary>
    /// Converts a <see cref="CborLdInputNode"/> tree to a
    /// <see cref="JsonNode"/> backed by a navigator that reads CBOR-LD
    /// input nodes directly. The resulting node is independent of any
    /// JSON document; it carries the original <paramref name="node"/> as
    /// its handle and reads through a dedicated navigator without going
    /// through JSON bytes.
    /// </summary>
    /// <param name="node">The CBOR-LD input tree to expose as JSON-LD.</param>
    /// <returns>A JSON-LD node that navigates <paramref name="node"/>.</returns>
    /// <remarks>
    /// <para>
    /// CBOR-LD's <see cref="CborLdInputBytes"/> has no JSON equivalent;
    /// encountering one during navigation raises
    /// <see cref="InvalidOperationException"/>. Callers that need to
    /// expose byte strings as JSON should pre-process the tree (e.g.
    /// base64-encode the bytes into a <see cref="CborLdInputString"/>)
    /// before calling this method.
    /// </para>
    /// <para>
    /// Number round-trip preserves the integer-vs-double distinction:
    /// <see cref="CborLdInputInt"/> surfaces as a JSON number with no
    /// fractional part (parseable as <see cref="long"/>), and
    /// <see cref="CborLdInputDouble"/> surfaces with the round-trip
    /// shortest decimal form ("R") so reparses preserve the value.
    /// </para>
    /// </remarks>
    public static JsonNode ToJsonLd(CborLdInputNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new JsonNode(node, CborLdJsonLdNavigator.Instance);
    }

    private static CborLdInputNode? Complete(
        Stack<Frame> stack,
        Frame frame,
        CborLdInputNode value,
        CborLdInputNode? currentRoot)
    {
        stack.Pop();
        if(frame.Parent is null)
        {
            return value;
        }
        if(frame.Parent.Items is not null)
        {
            frame.Parent.Items.Add(value);
        }
        else if(frame.Parent.Entries is not null && frame.ParentKey is not null)
        {
            frame.Parent.Entries.Add(new KeyValuePair<string, CborLdInputNode>(frame.ParentKey, value));
        }
        return currentRoot;
    }

    private static CborLdInputNode ParseNumber(string raw)
    {
        //If the raw form has no fractional or exponential part, attempt to parse as Int64.
        bool hasFractional = raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0;
        if(!hasFractional && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            return new CborLdInputInt(l);
        }
        if(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            return new CborLdInputDouble(d);
        }
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"JSON number '{raw}' could not be parsed as Int64 or Double."));
    }

    /// <summary>
    /// Adapter-side navigator exposing a <see cref="CborLdInputNode"/>
    /// tree through the <see cref="JsonNodeNavigator"/> contract. The
    /// handle for every node is the <see cref="CborLdInputNode"/> itself;
    /// no boxing, no intermediate buffer, no parallel tree.
    /// </summary>
    internal static class CborLdJsonLdNavigator
    {
        public static JsonNodeNavigator Instance { get; } = new JsonNodeNavigator
        {
            GetKind = GetKind,
            GetString = GetString,
            GetBoolean = GetBoolean,
            GetRawNumber = GetRawNumber,
            TryGetProperty = TryGetProperty,
            EnumerateArray = EnumerateArray,
            EnumerateObject = EnumerateObject,
            Clone = Clone
        };

        private static JsonNodeKind GetKind(object handle)
        {
            return handle switch
            {
                CborLdInputNull => JsonNodeKind.Null,
                CborLdInputBool b => b.Value ? JsonNodeKind.True : JsonNodeKind.False,
                CborLdInputString => JsonNodeKind.String,
                CborLdInputInt => JsonNodeKind.Number,
                CborLdInputDouble => JsonNodeKind.Number,
                CborLdInputArray => JsonNodeKind.Array,
                CborLdInputMap => JsonNodeKind.Object,
                CborLdInputBytes => throw new InvalidOperationException(
                    "CBOR-LD byte strings have no JSON equivalent; pre-encode to base64 before calling ToJsonLd."),
                _ => throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Unrecognised CborLdInputNode subtype: {handle.GetType().Name}."))
            };
        }

        private static string GetString(object handle)
        {
            if(handle is CborLdInputString s)
            {
                return s.Value;
            }
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"GetString called on non-string CBOR-LD node: {handle.GetType().Name}."));
        }

        private static bool GetBoolean(object handle)
        {
            if(handle is CborLdInputBool b)
            {
                return b.Value;
            }
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"GetBoolean called on non-boolean CBOR-LD node: {handle.GetType().Name}."));
        }

        private static string GetRawNumber(object handle)
        {
            //"R" round-trip format ensures double values reparse to the
            //same double. Integers use invariant decimal with no fractional
            //part so consumers can re-classify them as Int64-compatible.
            return handle switch
            {
                CborLdInputInt i => i.Value.ToString(CultureInfo.InvariantCulture),
                CborLdInputDouble d => d.Value.ToString("R", CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"GetRawNumber called on non-number CBOR-LD node: {handle.GetType().Name}."))
            };
        }

        private static bool TryGetProperty(object handle, string name, out JsonNode value)
        {
            if(handle is CborLdInputMap map)
            {
                foreach(KeyValuePair<string, CborLdInputNode> entry in map.Entries)
                {
                    if(string.Equals(entry.Key, name, StringComparison.Ordinal))
                    {
                        value = new JsonNode(entry.Value, Instance);
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static IEnumerable<JsonNode> EnumerateArray(object handle)
        {
            if(handle is not CborLdInputArray array)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"EnumerateArray called on non-array CBOR-LD node: {handle.GetType().Name}."));
            }
            return EnumerateArrayCore(array);
        }

        private static IEnumerable<JsonNode> EnumerateArrayCore(CborLdInputArray array)
        {
            foreach(CborLdInputNode item in array.Items)
            {
                yield return new JsonNode(item, Instance);
            }
        }

        private static IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObject(object handle)
        {
            if(handle is not CborLdInputMap map)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"EnumerateObject called on non-object CBOR-LD node: {handle.GetType().Name}."));
            }
            return EnumerateObjectCore(map);
        }

        private static IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObjectCore(CborLdInputMap map)
        {
            foreach(KeyValuePair<string, CborLdInputNode> entry in map.Entries)
            {
                yield return new KeyValuePair<string, JsonNode>(
                    entry.Key,
                    new JsonNode(entry.Value, Instance));
            }
        }

        private static JsonNode Clone(object handle)
        {
            //CborLdInputNode instances are already independent value
            //carriers; cloning is identity.
            return new JsonNode(handle, Instance);
        }
    }

    private sealed class Frame
    {
        public Frame(JsonNode node, Frame? parent, string? parentKey, int parentIndex)
        {
            Node = node;
            Parent = parent;
            ParentKey = parentKey;
            ParentIndex = parentIndex;
        }

        public JsonNode Node { get; }
        public Frame? Parent { get; }
        public string? ParentKey { get; }
        public int ParentIndex { get; }

        public bool MaterialisedChildren { get; set; }
        public int NextIndex { get; set; }

        public List<JsonNode>? ArrayChildren { get; set; }
        public List<KeyValuePair<string, JsonNode>>? ObjectChildren { get; set; }

        public List<CborLdInputNode>? Items { get; set; }
        public List<KeyValuePair<string, CborLdInputNode>>? Entries { get; set; }
    }
}
