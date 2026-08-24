using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The pure object built-in functions: <c>$keys</c>, <c>$lookup</c>, <c>$merge</c>, <c>$type</c>, and
/// <c>$spread</c>. The functions that descend an array argument (<c>$keys</c>, <c>$lookup</c>,
/// <c>$spread</c>) walk nested arrays over an explicit stack bounded by
/// <see cref="JsonataLimits.MaxEvaluationDepth"/>, so a deeply nested input raises a catchable
/// <see cref="JsonataEvaluationLimitException"/> rather than overflowing the call stack.
/// </summary>
/// <remarks>
/// <para>
/// The <c>$type</c> non-finite-number case (D1001) is unreachable: the engine never materialises a
/// non-finite number, so <c>$type</c> has no number it must reject. This is a fragment-relative divergence
/// from the reference.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/object-functions">the JSONata object-functions reference</see>.</para>
/// </remarks>
internal static class JsonataObjectFunctions
{
    /// <summary>The JSONata type name for the JSON null value.</summary>
    private const string TypeNull = "null";

    /// <summary>The JSONata type name for a number.</summary>
    private const string TypeNumber = "number";

    /// <summary>The JSONata type name for a string.</summary>
    private const string TypeString = "string";

    /// <summary>The JSONata type name for a boolean.</summary>
    private const string TypeBoolean = "boolean";

    /// <summary>The JSONata type name for an array.</summary>
    private const string TypeArray = "array";

    /// <summary>The JSONata type name for an object.</summary>
    private const string TypeObject = "object";

    /// <summary>The JSONata type name for a function value.</summary>
    private const string TypeFunction = "function";

    /// <summary>The object built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("keys"), InvokeKeys, JsonataSignature.Parse("<x-:a<s>>")),
        new JsonataBuiltinFunction(Utf8Strings.From("lookup"), InvokeLookup, JsonataSignature.Parse("<x-s:x>")),
        new JsonataBuiltinFunction(Utf8Strings.From("merge"), InvokeMerge, JsonataSignature.Parse("<a<o>:o>")),
        new JsonataBuiltinFunction(Utf8Strings.From("type"), InvokeType, JsonataSignature.Parse("<x:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("spread"), InvokeSpread, JsonataSignature.Parse("<x-:a<o>>"))
    ];

    /// <summary>
    /// <c>$keys(value)</c>: the key names of an object as an array of strings in insertion order; over an
    /// array, the deduplicated union of keys across every object leaf (descending nested arrays) in
    /// first-seen order; a scalar, null, function, or undefined input yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the value is the first argument.</param>
    /// <returns>The key strings, or undefined when there are none.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static JsonataValue InvokeKeys(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        List<string> orderedKeys = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        if(value.Kind == JsonataValueKind.Object)
        {
            CollectObjectKeys(value, orderedKeys, seen);
        }
        else if(value.Kind == JsonataValueKind.Array)
        {
            CollectArrayKeys(value, orderedKeys, seen);
        }

        if(orderedKeys.Count == 0)
        {
            return JsonataValue.Undefined;
        }

        List<JsonataValue> keyValues = new(orderedKeys.Count);
        foreach(string key in orderedKeys)
        {
            keyValues.Add(JsonataValue.String(key));
        }

        return new JsonataSequence(keyValues, KeepArray: false).Normalize();
    }

    /// <summary>
    /// <c>$lookup(value, key)</c>: the value at a key on an object (a present null-valued key yields null,
    /// an absent key or a function input yields undefined); over an array, the per-element lookup flattened
    /// one level (descending nested arrays, spreading an array result, dropping an undefined result); a
    /// scalar, null, or undefined input yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the value and the required key string.</param>
    /// <returns>The looked-up value, a flattened sequence over an array input, or undefined.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static JsonataValue InvokeLookup(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        string key = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.String ? arguments[1].AsString : string.Empty;
        if(value.Kind == JsonataValueKind.Object)
        {
            return LookupObjectKey(value, key);
        }

        if(value.Kind != JsonataValueKind.Array)
        {
            return JsonataValue.Undefined;
        }

        return LookupOverArray(value, key);
    }

    /// <summary>
    /// <c>$merge(arrayOfObjects)</c>: merges an array of objects into one object, last value winning per
    /// duplicate key, keys in first-seen insertion order, nested values copied by reference; a single object
    /// is merged trivially; the empty array merges to the empty object; undefined yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the array of objects is the first argument.</param>
    /// <returns>The merged object, or undefined for an undefined argument.</returns>
    private static JsonataValue InvokeMerge(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> objects = value.AsArray;
        List<string> orderedKeys = [];
        Dictionary<string, JsonataValue> byKey = new(StringComparer.Ordinal);
        foreach(JsonataValue element in objects)
        {
            if(element.Kind != JsonataValueKind.Object)
            {
                continue;
            }

            MergeObjectEntries(element, orderedKeys, byKey);
        }

        List<KeyValuePair<string, JsonataValue>> merged = new(orderedKeys.Count);
        foreach(string key in orderedKeys)
        {
            merged.Add(new KeyValuePair<string, JsonataValue>(key, byKey[key]));
        }

        return JsonataValue.Object(merged);
    }

    /// <summary>
    /// <c>$type(value)</c>: the JSONata type name of a value as a string — <c>"null"</c>, <c>"number"</c>,
    /// <c>"string"</c>, <c>"boolean"</c>, <c>"array"</c>, <c>"object"</c>, or <c>"function"</c>; undefined
    /// yields the undefined value (not the string <c>"undefined"</c>).
    /// </summary>
    /// <param name="arguments">The argument list; the value is the first argument.</param>
    /// <returns>The type name string, or undefined for an undefined argument.</returns>
    private static JsonataValue InvokeType(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);

        return value.Kind switch
        {
            JsonataValueKind.Undefined => JsonataValue.Undefined,
            JsonataValueKind.Null => JsonataValue.String(TypeNull),
            JsonataValueKind.Number => JsonataValue.String(TypeNumber),
            JsonataValueKind.String => JsonataValue.String(TypeString),
            JsonataValueKind.Boolean => JsonataValue.String(TypeBoolean),
            JsonataValueKind.Array => JsonataValue.String(TypeArray),
            JsonataValueKind.Function => JsonataValue.String(TypeFunction),
            _ => JsonataValue.String(TypeObject)
        };
    }

    /// <summary>
    /// <c>$spread(value)</c>: splits an object into an array of single-key objects, one per key in insertion
    /// order; over an array, spreads each element (descending nested arrays) into one flat array of
    /// single-key objects; a scalar, null, function, or undefined input is passed through unchanged.
    /// </summary>
    /// <param name="arguments">The argument list; the value is the first argument.</param>
    /// <returns>The array of single-key objects, or the input value unchanged for a non-array, non-object.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static JsonataValue InvokeSpread(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind == JsonataValueKind.Object)
        {
            List<JsonataValue> singleKeyObjects = [];
            SpreadObject(value, singleKeyObjects);

            return JsonataValue.Array(singleKeyObjects);
        }

        if(value.Kind != JsonataValueKind.Array)
        {
            return value;
        }

        return SpreadOverArray(value);
    }

    /// <summary>Adds an object's not-yet-seen keys to the ordered union, in insertion order.</summary>
    /// <param name="objectValue">The object whose keys are collected.</param>
    /// <param name="orderedKeys">The ordered union of keys, appended to in first-seen order.</param>
    /// <param name="seen">The membership set guarding the ordered union against duplicates.</param>
    private static void CollectObjectKeys(JsonataValue objectValue, List<string> orderedKeys, HashSet<string> seen)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in objectValue.AsObject)
        {
            if(seen.Add(entry.Key))
            {
                orderedKeys.Add(entry.Key);
            }
        }
    }

    /// <summary>Collects the deduplicated union of keys across every object leaf of an array over an explicit-stack depth-first walk, descending nested arrays.</summary>
    /// <param name="arrayValue">The array to walk.</param>
    /// <param name="orderedKeys">The ordered union of keys, appended to in first-seen order.</param>
    /// <param name="seen">The membership set guarding the ordered union against duplicates.</param>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static void CollectArrayKeys(JsonataValue arrayValue, List<string> orderedKeys, HashSet<string> seen)
    {
        Stack<ArrayDescentCursor> stack = new();
        stack.Push(new ArrayDescentCursor(arrayValue.AsArray, depth: 1));
        while(stack.Count > 0)
        {
            ArrayDescentCursor cursor = stack.Peek();
            if(cursor.NextIndex >= cursor.Items.Count)
            {
                stack.Pop();

                continue;
            }

            JsonataValue item = cursor.Items[cursor.NextIndex];
            cursor.NextIndex++;
            switch(item.Kind)
            {
                case JsonataValueKind.Array:
                {
                    stack.Push(new ArrayDescentCursor(item.AsArray, NextDepth(cursor.Depth)));

                    break;
                }
                case JsonataValueKind.Object:
                {
                    CollectObjectKeys(item, orderedKeys, seen);

                    break;
                }
                default:
                {
                    //A scalar element contributes no keys.
                    break;
                }
            }
        }
    }

    /// <summary>Looks up a key on an object focus, comparing keys ordinally; a present null-valued key yields null and an absent key yields undefined.</summary>
    /// <param name="objectValue">The object to look up in.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value at the key, or undefined when the object has no such key.</returns>
    private static JsonataValue LookupObjectKey(JsonataValue objectValue, string key)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in objectValue.AsObject)
        {
            if(string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return JsonataValue.Undefined;
    }

    /// <summary>Maps a key lookup over an array over an explicit-stack depth-first walk, descending nested arrays and flattening each defined per-element result one level.</summary>
    /// <param name="arrayValue">The array to walk.</param>
    /// <param name="key">The key to look up on each object leaf.</param>
    /// <returns>The flattened sequence of looked-up values.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static JsonataValue LookupOverArray(JsonataValue arrayValue, string key)
    {
        List<JsonataValue> mapped = [];
        Stack<ArrayDescentCursor> stack = new();
        stack.Push(new ArrayDescentCursor(arrayValue.AsArray, depth: 1));
        while(stack.Count > 0)
        {
            ArrayDescentCursor cursor = stack.Peek();
            if(cursor.NextIndex >= cursor.Items.Count)
            {
                stack.Pop();

                continue;
            }

            JsonataValue item = cursor.Items[cursor.NextIndex];
            cursor.NextIndex++;
            switch(item.Kind)
            {
                case JsonataValueKind.Array:
                {
                    stack.Push(new ArrayDescentCursor(item.AsArray, NextDepth(cursor.Depth)));

                    break;
                }
                case JsonataValueKind.Object:
                {
                    AppendFlattened(mapped, LookupObjectKey(item, key));

                    break;
                }
                default:
                {
                    //A scalar element contributes nothing to the mapped lookup.
                    break;
                }
            }
        }

        return new JsonataSequence(mapped, KeepArray: false).Normalize();
    }

    /// <summary>Adds an object's entries into the merge accumulator, last value winning per duplicate key, recording each key's first-seen position.</summary>
    /// <param name="objectValue">The object whose entries are merged in.</param>
    /// <param name="orderedKeys">The first-seen key order, appended to when a key is new.</param>
    /// <param name="byKey">The last-wins value per key.</param>
    private static void MergeObjectEntries(JsonataValue objectValue, List<string> orderedKeys, Dictionary<string, JsonataValue> byKey)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in objectValue.AsObject)
        {
            if(!byKey.ContainsKey(entry.Key))
            {
                orderedKeys.Add(entry.Key);
            }

            byKey[entry.Key] = entry.Value;
        }
    }

    /// <summary>Appends one single-key object per entry of an object, in insertion order.</summary>
    /// <param name="objectValue">The object to spread.</param>
    /// <param name="accumulator">The flat array of single-key objects appended to.</param>
    private static void SpreadObject(JsonataValue objectValue, List<JsonataValue> accumulator)
    {
        foreach(KeyValuePair<string, JsonataValue> entry in objectValue.AsObject)
        {
            accumulator.Add(JsonataValue.Object([new KeyValuePair<string, JsonataValue>(entry.Key, entry.Value)]));
        }
    }

    /// <summary>Spreads each element of an array into single-key objects over an explicit-stack depth-first walk, descending nested arrays, accumulating one flat array.</summary>
    /// <param name="arrayValue">The array to walk.</param>
    /// <returns>The flat array of single-key objects.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static JsonataValue SpreadOverArray(JsonataValue arrayValue)
    {
        List<JsonataValue> singleKeyObjects = [];
        Stack<ArrayDescentCursor> stack = new();
        stack.Push(new ArrayDescentCursor(arrayValue.AsArray, depth: 1));
        while(stack.Count > 0)
        {
            ArrayDescentCursor cursor = stack.Peek();
            if(cursor.NextIndex >= cursor.Items.Count)
            {
                stack.Pop();

                continue;
            }

            JsonataValue item = cursor.Items[cursor.NextIndex];
            cursor.NextIndex++;
            switch(item.Kind)
            {
                case JsonataValueKind.Array:
                {
                    stack.Push(new ArrayDescentCursor(item.AsArray, NextDepth(cursor.Depth)));

                    break;
                }
                case JsonataValueKind.Object:
                {
                    SpreadObject(item, singleKeyObjects);

                    break;
                }
                default:
                {
                    //A scalar element is dropped: spreading produces single-key objects only.
                    break;
                }
            }
        }

        return JsonataValue.Array(singleKeyObjects);
    }

    /// <summary>Appends a per-element lookup result to an accumulator, flattening one level (an array spreads its elements; undefined contributes nothing).</summary>
    /// <param name="accumulator">The accumulator to append to.</param>
    /// <param name="value">The value to flatten in.</param>
    private static void AppendFlattened(List<JsonataValue> accumulator, JsonataValue value)
    {
        if(value.IsUndefined)
        {
            return;
        }

        if(value.Kind == JsonataValueKind.Array)
        {
            accumulator.AddRange(value.AsArray);

            return;
        }

        accumulator.Add(value);
    }

    /// <summary>Computes the next array-descent depth, raising the catchable limit exception when the depth bound would be exceeded.</summary>
    /// <param name="depth">The current array-descent depth.</param>
    /// <returns>The child array-descent depth.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-descent depth bound was exceeded.</exception>
    private static int NextDepth(int depth)
    {
        int childDepth = depth + 1;
        if(childDepth > JsonataLimits.MaxEvaluationDepth)
        {
            throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "A JSONata object function over nested arrays exceeded the maximum depth.");
        }

        return childDepth;
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }

    /// <summary>A cursor over an array's elements during an iterative descent, carrying the array-descent depth.</summary>
    private sealed class ArrayDescentCursor
    {
        /// <summary>Initializes a cursor over an array's elements at a given descent depth.</summary>
        /// <param name="items">The array elements to walk.</param>
        /// <param name="depth">The array-descent depth of this array.</param>
        public ArrayDescentCursor(IReadOnlyList<JsonataValue> items, int depth)
        {
            Items = items;
            Depth = depth;
        }

        /// <summary>Gets the array elements being walked.</summary>
        public IReadOnlyList<JsonataValue> Items { get; }

        /// <summary>Gets the array-descent depth of this array.</summary>
        public int Depth { get; }

        /// <summary>Gets or sets the next element index to process.</summary>
        public int NextIndex { get; set; }
    }
}
