using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// An immutable JSONata value: a <see cref="Kind"/> tag plus the payload for that kind. Scalars
/// (undefined, null, boolean, number) are carried inline with no heap allocation; string, array,
/// object, and function payloads share a single object reference.
/// </summary>
/// <remarks>
/// <para>
/// The value is a <see cref="JsonataValueKind"/>-discriminated union modelled as a
/// <see langword="readonly"/> <see langword="record"/> <see langword="struct"/> per the explicit-memory
/// lens: a boolean is held in <see cref="scalar"/> as 0/1 and a number directly in
/// <see cref="scalar"/>, so neither boxes. <c>default(JsonataValue)</c> is the
/// <see cref="JsonataValueKind.Undefined"/> "nothing" value.
/// </para>
/// <para>
/// Record-struct equality compares the inline fields and the reference identity of
/// <see cref="reference"/>, which is not the JSONata structural equality of arrays/objects. Deep,
/// structural equality (the semantics of the <c>=</c> and <c>!=</c> operators) is
/// <see cref="DeepEquals"/>.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see>.</para>
/// </remarks>
[DebuggerDisplay("{Kind}")]
public readonly record struct JsonataValue
{
    /// <summary>The payload reference (string, array list, object entry list, or function); null for scalars.</summary>
    private readonly object? reference;

    /// <summary>The inline scalar payload: the number for <see cref="JsonataValueKind.Number"/>, 0/1 for <see cref="JsonataValueKind.Boolean"/>, the path-step flag bits for <see cref="JsonataValueKind.Array"/>.</summary>
    private readonly double scalar;

    /// <summary>The <see cref="scalar"/> bit set on an <see cref="JsonataValueKind.Array"/> built by an array constructor used as a path step (the JSONata <c>cons</c> marker): a following dot/map step keeps such an array whole rather than flattening one level into the parent sequence.</summary>
    private const double ConsArrayFlag = 1;

    /// <summary>The <see cref="scalar"/> bit set on an <see cref="JsonataValueKind.Array"/> a <c>[]</c> keep-array marker produced (the JSONata <c>keepSingleton</c> marker): a singleton stays an array through the enclosing dot/map steps and at output rather than auto-unwrapping to its element.</summary>
    private const double KeepSingletonArrayFlag = 2;

    /// <summary>Initializes a value; use the factory members rather than this directly.</summary>
    /// <param name="kind">The discriminating kind.</param>
    /// <param name="reference">The payload reference, or <see langword="null"/> for a scalar.</param>
    /// <param name="scalar">The inline scalar payload.</param>
    private JsonataValue(JsonataValueKind kind, object? reference, double scalar)
    {
        Kind = kind;
        this.reference = reference;
        this.scalar = scalar;
    }

    /// <summary>Gets the discriminating kind.</summary>
    public JsonataValueKind Kind { get; }

    /// <summary>Gets the JSONata "nothing"/undefined value (equal to <c>default(JsonataValue)</c>).</summary>
    public static JsonataValue Undefined => default;

    /// <summary>Gets the JSON null value.</summary>
    public static JsonataValue Null { get; } = new(JsonataValueKind.Null, null, 0);

    /// <summary>Gets a value indicating whether this is the undefined "nothing" value.</summary>
    public bool IsUndefined => Kind == JsonataValueKind.Undefined;

    /// <summary>Wraps a boolean.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>The boolean value.</returns>
    public static JsonataValue Boolean(bool value) => new(JsonataValueKind.Boolean, null, value ? 1 : 0);

    /// <summary>Wraps an IEEE-754 double.</summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The numeric value.</returns>
    public static JsonataValue Number(double value) => new(JsonataValueKind.Number, null, value);

    /// <summary>Wraps a string.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The string value.</returns>
    public static JsonataValue String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new JsonataValue(JsonataValueKind.String, value, 0);
    }

    /// <summary>Wraps an ordered array (the keep-as-array container) with no path-step flag set.</summary>
    /// <param name="items">The array items.</param>
    /// <returns>The array value.</returns>
    public static JsonataValue Array(IReadOnlyList<JsonataValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new JsonataValue(JsonataValueKind.Array, items, 0);
    }

    /// <summary>
    /// Wraps an ordered array built by an array constructor used as a path step, setting the JSONata
    /// <c>cons</c> marker so a following dot/map step keeps the array whole rather than flattening one
    /// level into the parent sequence. The marker rides on the value, so nested constructor steps
    /// (<c>a.[b.[c]]</c>) compose: each level produces a cons array the next-outer step keeps whole.
    /// </summary>
    /// <param name="items">The array items.</param>
    /// <returns>The cons-marked array value.</returns>
    public static JsonataValue ConsArray(IReadOnlyList<JsonataValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new JsonataValue(JsonataValueKind.Array, items, ConsArrayFlag);
    }

    /// <summary>
    /// Wraps an ordered array a <c>[]</c> keep-array marker produced, setting the JSONata
    /// <c>keepSingleton</c> marker so a singleton stays an array through the enclosing dot/map steps and at
    /// output rather than auto-unwrapping to its element. The marker rides on the value, so it survives the
    /// enclosing steps to the final normalization.
    /// </summary>
    /// <param name="items">The array items.</param>
    /// <returns>The keep-singleton-marked array value.</returns>
    public static JsonataValue KeepSingletonArray(IReadOnlyList<JsonataValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new JsonataValue(JsonataValueKind.Array, items, KeepSingletonArrayFlag);
    }

    /// <summary>
    /// Re-tags an existing array value with the JSONata <c>keepSingleton</c> marker, preserving its
    /// <c>cons</c> marker so an array constructor step carrying a trailing <c>[]</c> keeps both: the cons
    /// marker so the enclosing step keeps it whole, and the keep-singleton marker so a singleton stays an
    /// array. The argument must be an array value.
    /// </summary>
    /// <param name="array">The array value to re-tag (its <c>cons</c> marker is preserved).</param>
    /// <returns>The same items tagged keep-singleton, with the existing cons marker preserved.</returns>
    public static JsonataValue AsKeepSingletonArray(JsonataValue array)
    {
        return new JsonataValue(JsonataValueKind.Array, array.AsArray, (int)array.scalar | (int)KeepSingletonArrayFlag);
    }

    /// <summary>Wraps an insertion-ordered object.</summary>
    /// <param name="entries">The object entries, in insertion order.</param>
    /// <returns>The object value.</returns>
    public static JsonataValue Object(IReadOnlyList<KeyValuePair<string, JsonataValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return new JsonataValue(JsonataValueKind.Object, entries, 0);
    }

    /// <summary>Wraps a first-class function value (no JSON representation).</summary>
    /// <param name="function">The function payload.</param>
    /// <returns>The function value.</returns>
    public static JsonataValue Function(object function)
    {
        ArgumentNullException.ThrowIfNull(function);

        return new JsonataValue(JsonataValueKind.Function, function, 0);
    }

    /// <summary>
    /// Wraps an INTERNAL-ONLY tuple-stream carrier (the reference's <c>resultSequence.tupleStream</c>): the
    /// value a nested keep-tuples path produces so an enclosing tuple step can merge each inner tuple's focus and
    /// ancestor bindings. The payload is the inner path's tuple list (an opaque reference here; the evaluator's
    /// tuple-stream cursor is the only producer / consumer). This value is internal-only and MUST never escape to
    /// a user-visible value: see <see cref="JsonataValueKind.TupleStream"/>.
    /// </summary>
    /// <param name="tuples">The inner path's tuple list (the evaluator's per-tuple carrier objects).</param>
    /// <returns>The tuple-stream carrier value.</returns>
    internal static JsonataValue TupleStream(object tuples)
    {
        ArgumentNullException.ThrowIfNull(tuples);

        return new JsonataValue(JsonataValueKind.TupleStream, tuples, 0);
    }

    /// <summary>Gets the tuple-stream payload reference; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.TupleStream"/>.</summary>
    internal object AsTupleStream => reference!;

    /// <summary>Gets a value indicating whether this is the internal tuple-stream carrier.</summary>
    internal bool IsTupleStream => Kind == JsonataValueKind.TupleStream;

    /// <summary>Gets the boolean payload; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.Boolean"/>.</summary>
    public bool AsBoolean => scalar != 0;

    /// <summary>Gets the numeric payload; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.Number"/>.</summary>
    public double AsNumber => scalar;

    /// <summary>Gets the string payload; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.String"/>.</summary>
    public string AsString => (string)reference!;

    /// <summary>Gets the array payload; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.Array"/>.</summary>
    public IReadOnlyList<JsonataValue> AsArray => (IReadOnlyList<JsonataValue>)reference!;

    /// <summary>Gets a value indicating whether this is an array carrying the JSONata <c>cons</c> marker (an array constructor used as a path step); always <see langword="false"/> for a non-array value.</summary>
    public bool IsConsArray => Kind == JsonataValueKind.Array && ((int)scalar & (int)ConsArrayFlag) != 0;

    /// <summary>Gets a value indicating whether this is an array carrying the JSONata <c>keepSingleton</c> marker (a <c>[]</c> keep-array marker); always <see langword="false"/> for a non-array value.</summary>
    public bool IsKeepSingletonArray => Kind == JsonataValueKind.Array && ((int)scalar & (int)KeepSingletonArrayFlag) != 0;

    /// <summary>Gets the object payload; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.Object"/>.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonataValue>> AsObject => (IReadOnlyList<KeyValuePair<string, JsonataValue>>)reference!;

    /// <summary>Gets the function payload; valid only when <see cref="Kind"/> is <see cref="JsonataValueKind.Function"/>.</summary>
    public object AsFunction => reference!;

    /// <summary>
    /// Determines JSONata deep/structural equality (the semantics of the <c>=</c> operator): scalars by
    /// value, arrays by length and element-wise deep equality, objects by key-set and per-key deep
    /// equality (order-independent). Undefined equals only undefined.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are structurally equal.</returns>
    public static bool DeepEquals(JsonataValue left, JsonataValue right)
    {
        Stack<(JsonataValue Left, JsonataValue Right)> pending = new();
        pending.Push((left, right));

        while(pending.Count > 0)
        {
            (JsonataValue a, JsonataValue b) = pending.Pop();
            if(a.Kind != b.Kind)
            {
                return false;
            }

            switch(a.Kind)
            {
                case JsonataValueKind.Undefined:
                case JsonataValueKind.Null:
                {
                    break;
                }
                case JsonataValueKind.Boolean:
                {
                    if(a.AsBoolean != b.AsBoolean)
                    {
                        return false;
                    }

                    break;
                }
                case JsonataValueKind.Number:
                {
                    if(a.AsNumber != b.AsNumber)
                    {
                        return false;
                    }

                    break;
                }
                case JsonataValueKind.String:
                {
                    if(!string.Equals(a.AsString, b.AsString, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    break;
                }
                case JsonataValueKind.Array:
                {
                    if(!ScheduleArrayChildren(a.AsArray, b.AsArray, pending))
                    {
                        return false;
                    }

                    break;
                }
                case JsonataValueKind.Object:
                {
                    if(!ScheduleObjectChildren(a.AsObject, b.AsObject, pending))
                    {
                        return false;
                    }

                    break;
                }
                case JsonataValueKind.TupleStream:
                {
                    //The tuple-stream carrier is internal to the path-stream cursor's nested-step plumbing and is
                    //consumed by its enclosing tuple step; reaching value equality means it escaped — fail loud
                    //rather than return a silently-wrong answer.
                    throw new System.InvalidOperationException("The internal tuple-stream carrier reached deep-equality; it must be consumed by its enclosing path step.");
                }
                default:
                {
                    //Functions have no JSONata value equality; they are never deep-equal.
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Schedules element-wise comparison of two arrays, failing fast on a length mismatch.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <param name="pending">The comparison work stack to push element pairs onto.</param>
    /// <returns><see langword="true"/> when the lengths match and the elements were scheduled.</returns>
    private static bool ScheduleArrayChildren(
        IReadOnlyList<JsonataValue> left,
        IReadOnlyList<JsonataValue> right,
        Stack<(JsonataValue Left, JsonataValue Right)> pending)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        for(int i = 0; i < left.Count; i++)
        {
            pending.Push((left[i], right[i]));
        }

        return true;
    }

    /// <summary>
    /// Schedules per-key comparison of two objects on their distinct key sets (order-independent), so
    /// duplicate-key multiplicity cannot make two differing objects compare equal: the distinct key sets
    /// must match, and the last-write-wins value per distinct key must deep-equal.
    /// </summary>
    /// <param name="left">The first object's entries.</param>
    /// <param name="right">The second object's entries.</param>
    /// <param name="pending">The comparison work stack to push value pairs onto.</param>
    /// <returns><see langword="true"/> when the distinct key sets match and the values were scheduled.</returns>
    private static bool ScheduleObjectChildren(
        IReadOnlyList<KeyValuePair<string, JsonataValue>> left,
        IReadOnlyList<KeyValuePair<string, JsonataValue>> right,
        Stack<(JsonataValue Left, JsonataValue Right)> pending)
    {
        Dictionary<string, JsonataValue> leftByKey = ToDistinctKeyMap(left);
        Dictionary<string, JsonataValue> rightByKey = ToDistinctKeyMap(right);
        if(leftByKey.Count != rightByKey.Count)
        {
            return false;
        }

        foreach(KeyValuePair<string, JsonataValue> entry in leftByKey)
        {
            if(!rightByKey.TryGetValue(entry.Key, out JsonataValue rightValue))
            {
                return false;
            }

            pending.Push((entry.Value, rightValue));
        }

        return true;
    }

    /// <summary>Collapses an object's entries to a distinct-key map (last-write-wins), so duplicate keys count once.</summary>
    /// <param name="entries">The object's entries.</param>
    /// <returns>The distinct-key value map.</returns>
    private static Dictionary<string, JsonataValue> ToDistinctKeyMap(IReadOnlyList<KeyValuePair<string, JsonataValue>> entries)
    {
        Dictionary<string, JsonataValue> byKey = new(entries.Count, StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonataValue> entry in entries)
        {
            byKey[entry.Key] = entry.Value;
        }

        return byKey;
    }
}
