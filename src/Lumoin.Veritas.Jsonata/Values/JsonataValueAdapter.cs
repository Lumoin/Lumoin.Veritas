using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// Bridges between the read-only <see cref="JsonNode"/> seam and the JSONata value tree:
/// <see cref="FromJsonNode"/> walks an input node into a <see cref="JsonataValue"/>, and
/// <see cref="ToJsonNode"/> re-exposes a constructed value through the same seam.
/// </summary>
/// <remarks>
/// <para>
/// The read walk is iterative — a <see cref="Stack{T}"/> of frames per the no-recursion rule, mirroring
/// the CBOR-LD input adapter. JSON numbers surface as raw lexemes through the navigator and are parsed
/// to <see cref="double"/> with <see cref="CultureInfo.InvariantCulture"/> at the boundary, since
/// JSONata's number type is a single IEEE-754 double; the <see cref="JsonNodeKind.True"/> and
/// <see cref="JsonNodeKind.False"/> kinds collapse to one <see cref="JsonataValueKind.Boolean"/>.
/// </para>
/// </remarks>
public static class JsonataValueAdapter
{
    /// <summary>Converts an input <see cref="JsonNode"/> tree to its <see cref="JsonataValue"/> equivalent.</summary>
    /// <param name="node">The JSON node to convert.</param>
    /// <returns>The equivalent JSONata value.</returns>
    /// <exception cref="ArgumentException">The node has a default <see cref="JsonNode.Handle"/> or <see cref="JsonNode.Navigator"/>.</exception>
    public static JsonataValue FromJsonNode(JsonNode node)
    {
        if(node.Handle is null || node.Navigator is null)
        {
            throw new ArgumentException("FromJsonNode requires a fully-initialised JsonNode.", nameof(node));
        }

        Stack<Frame> stack = new();
        JsonataValue rootResult = JsonataValue.Undefined;
        stack.Push(new Frame(node, parent: null, parentKey: null));

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
                    rootResult = Complete(stack, frame, JsonataValue.Null, rootResult);

                    break;
                }
                case JsonNodeKind.True:
                {
                    rootResult = Complete(stack, frame, JsonataValue.Boolean(true), rootResult);

                    break;
                }
                case JsonNodeKind.False:
                {
                    rootResult = Complete(stack, frame, JsonataValue.Boolean(false), rootResult);

                    break;
                }
                case JsonNodeKind.String:
                {
                    rootResult = Complete(stack, frame, JsonataValue.String(navigator.GetString(handle)), rootResult);

                    break;
                }
                case JsonNodeKind.Number:
                {
                    double parsed = double.Parse(navigator.GetRawNumber(handle), NumberStyles.Float, CultureInfo.InvariantCulture);
                    rootResult = Complete(stack, frame, JsonataValue.Number(parsed), rootResult);

                    break;
                }
                case JsonNodeKind.Array:
                {
                    rootResult = StepArray(stack, frame, navigator, handle, rootResult);

                    break;
                }
                case JsonNodeKind.Object:
                {
                    rootResult = StepObject(stack, frame, navigator, handle, rootResult);

                    break;
                }
                default:
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture, $"Unrecognised JsonNodeKind: {kind}."));
                }
            }
        }

        return rootResult;
    }

    /// <summary>Re-exposes a constructed <see cref="JsonataValue"/> through the read-only <see cref="JsonNode"/> seam.</summary>
    /// <param name="value">The JSONata value to expose.</param>
    /// <returns>A node that navigates <paramref name="value"/>.</returns>
    public static JsonNode ToJsonNode(JsonataValue value)
    {
        return new JsonNode(value, JsonataJsonNavigator.Instance);
    }

    /// <summary>Advances an array frame: materialises children once, pushes the next child, or builds the array.</summary>
    /// <param name="stack">The walk stack.</param>
    /// <param name="frame">The array frame.</param>
    /// <param name="navigator">The node navigator.</param>
    /// <param name="handle">The array node's handle.</param>
    /// <param name="currentRoot">The root accumulated so far.</param>
    /// <returns>The updated root.</returns>
    private static JsonataValue StepArray(Stack<Frame> stack, Frame frame, JsonNodeNavigator navigator, object handle, JsonataValue currentRoot)
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
            frame.Items = new List<JsonataValue>(children.Count);
        }

        if(frame.NextIndex < frame.ArrayChildren!.Count)
        {
            int index = frame.NextIndex++;
            stack.Push(new Frame(frame.ArrayChildren[index], parent: frame, parentKey: null));

            return currentRoot;
        }

        return Complete(stack, frame, JsonataValue.Array(frame.Items!), currentRoot);
    }

    /// <summary>Advances an object frame: materialises children once, pushes the next value, or builds the object.</summary>
    /// <param name="stack">The walk stack.</param>
    /// <param name="frame">The object frame.</param>
    /// <param name="navigator">The node navigator.</param>
    /// <param name="handle">The object node's handle.</param>
    /// <param name="currentRoot">The root accumulated so far.</param>
    /// <returns>The updated root.</returns>
    private static JsonataValue StepObject(Stack<Frame> stack, Frame frame, JsonNodeNavigator navigator, object handle, JsonataValue currentRoot)
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
            frame.Entries = new List<KeyValuePair<string, JsonataValue>>(children.Count);
        }

        if(frame.NextIndex < frame.ObjectChildren!.Count)
        {
            KeyValuePair<string, JsonNode> entry = frame.ObjectChildren[frame.NextIndex];
            frame.NextIndex++;
            stack.Push(new Frame(entry.Value, parent: frame, parentKey: entry.Key));

            return currentRoot;
        }

        return Complete(stack, frame, JsonataValue.Object(frame.Entries!), currentRoot);
    }

    /// <summary>Pops a completed frame and folds its value into the parent's accumulator, or returns it as the root.</summary>
    /// <param name="stack">The walk stack.</param>
    /// <param name="frame">The completed frame.</param>
    /// <param name="value">The value built for the frame.</param>
    /// <param name="currentRoot">The root accumulated so far.</param>
    /// <returns>The frame's value when it is the root; otherwise the unchanged root.</returns>
    private static JsonataValue Complete(Stack<Frame> stack, Frame frame, JsonataValue value, JsonataValue currentRoot)
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
            frame.Parent.Entries.Add(new KeyValuePair<string, JsonataValue>(frame.ParentKey, value));
        }

        return currentRoot;
    }

    /// <summary>One frame of the iterative read walk: a node plus its parent linkage and per-frame cursor/accumulators.</summary>
    private sealed class Frame
    {
        /// <summary>Initializes a frame for a node under a parent.</summary>
        /// <param name="node">The node this frame walks.</param>
        /// <param name="parent">The parent frame, or <see langword="null"/> at the root.</param>
        /// <param name="parentKey">The object key under which this node sits in its parent, or <see langword="null"/>.</param>
        public Frame(JsonNode node, Frame? parent, string? parentKey)
        {
            Node = node;
            Parent = parent;
            ParentKey = parentKey;
        }

        /// <summary>Gets the node this frame walks.</summary>
        public JsonNode Node { get; }

        /// <summary>Gets the parent frame, or <see langword="null"/> at the root.</summary>
        public Frame? Parent { get; }

        /// <summary>Gets the object key under which this node sits in its parent, or <see langword="null"/>.</summary>
        public string? ParentKey { get; }

        /// <summary>Gets or sets whether this frame's children have been materialised.</summary>
        public bool MaterialisedChildren { get; set; }

        /// <summary>Gets or sets the cursor into the materialised children.</summary>
        public int NextIndex { get; set; }

        /// <summary>Gets or sets the materialised array children awaiting conversion.</summary>
        public List<JsonNode>? ArrayChildren { get; set; }

        /// <summary>Gets or sets the materialised object children awaiting conversion.</summary>
        public List<KeyValuePair<string, JsonNode>>? ObjectChildren { get; set; }

        /// <summary>Gets or sets the converted array items accumulating for this frame.</summary>
        public List<JsonataValue>? Items { get; set; }

        /// <summary>Gets or sets the converted object entries accumulating for this frame.</summary>
        public List<KeyValuePair<string, JsonataValue>>? Entries { get; set; }
    }
}
