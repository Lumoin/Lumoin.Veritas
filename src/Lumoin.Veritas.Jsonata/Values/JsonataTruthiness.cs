using System.Collections.Generic;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// Computes JSONata truthiness over an explicit work stack (no recursion). This is the single shared
/// implementation behind the boolean-coercing operators (the conditional, the default operators, the
/// boolean operators <c>and</c>/<c>or</c>, the predicate filter) and the built-in functions
/// <c>$boolean</c> / <c>$not</c>.
/// </summary>
/// <remarks>
/// <para>
/// Undefined is falsy, null is falsy, an empty string is falsy, zero is falsy, an empty array/object is
/// falsy, a function is falsy; a non-empty string/object and a non-zero number are truthy; a singleton
/// array unwraps to the truthiness of its element; an array of length greater than one is truthy when any
/// element is truthy (each element itself may unwrap a singleton spine). The scan short-circuits on the
/// first truthy element and is bounded by <see cref="JsonataLimits.MaxEvaluationDepth"/>.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/boolean-functions">the JSONata boolean-functions reference</see>.</para>
/// </remarks>
internal static class JsonataTruthiness
{
    /// <summary>
    /// Computes JSONata truthiness over an explicit stack (no recursion): undefined is falsy, null is
    /// falsy, an empty string is falsy, zero is falsy, an empty array/object is falsy, a function is
    /// falsy; a singleton array unwraps to the truthiness of its element; an array of length greater than
    /// one is truthy when any element is truthy (each element itself may unwrap a singleton spine); a
    /// non-empty string/object and a non-zero number are truthy. The scan short-circuits on the first
    /// truthy element.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is truthy.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-nesting depth bound was exceeded.</exception>
    public static bool IsTruthy(JsonataValue value)
    {
        Stack<TruthinessCursor> pending = new();
        if(!TryScalarTruthiness(value, pending, depth: 0, out bool scalar))
        {
            return scalar;
        }

        while(pending.Count > 0)
        {
            TruthinessCursor cursor = pending.Peek();
            if(cursor.NextIndex >= cursor.Items.Count)
            {
                //The members were exhausted with no truthy element; this array is falsy.
                pending.Pop();

                continue;
            }

            JsonataValue element = cursor.Items[cursor.NextIndex];
            cursor.NextIndex++;
            if(!TryScalarTruthiness(element, pending, cursor.Depth, out bool elementTruthy))
            {
                if(elementTruthy)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a value's truthiness directly when it is a scalar or container, or schedules an array's
    /// members for the any-truthy scan: a singleton array unwraps to its element's truthiness in place; a
    /// longer array pushes a cursor and yields no immediate verdict.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <param name="pending">The truthiness work stack longer arrays push a cursor onto.</param>
    /// <param name="depth">The array-nesting depth of <paramref name="value"/>.</param>
    /// <param name="truthiness">On a direct resolution, the value's truthiness; otherwise the falsy default.</param>
    /// <returns><see langword="true"/> when a cursor was pushed and no verdict was produced; <see langword="false"/> when <paramref name="truthiness"/> holds the verdict.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-nesting depth bound was exceeded.</exception>
    private static bool TryScalarTruthiness(JsonataValue value, Stack<TruthinessCursor> pending, int depth, out bool truthiness)
    {
        switch(value.Kind)
        {
            case JsonataValueKind.Boolean:
            {
                truthiness = value.AsBoolean;

                return false;
            }
            case JsonataValueKind.Number:
            {
                truthiness = value.AsNumber != 0;

                return false;
            }
            case JsonataValueKind.String:
            {
                truthiness = value.AsString.Length > 0;

                return false;
            }
            case JsonataValueKind.Object:
            {
                truthiness = value.AsObject.Count > 0;

                return false;
            }
            case JsonataValueKind.Array:
            {
                return TryScheduleArrayTruthiness(value.AsArray, pending, depth, out truthiness);
            }
            case JsonataValueKind.TupleStream:
            {
                //The tuple-stream carrier is internal to the path-stream cursor and is consumed by its enclosing
                //tuple step; reaching truthiness means it escaped — fail loud rather than treat it as falsy.
                throw new System.InvalidOperationException("The internal tuple-stream carrier reached truthiness evaluation; it must be consumed by its enclosing path step.");
            }
            default:
            {
                //Undefined, null, and function are all falsy.
                truthiness = false;

                return false;
            }
        }
    }

    /// <summary>Schedules an array's truthiness: empty is falsy in place, a singleton unwraps to its element, a longer array pushes an any-truthy cursor.</summary>
    /// <param name="items">The array items.</param>
    /// <param name="pending">The truthiness work stack a longer array pushes a cursor onto.</param>
    /// <param name="depth">The array-nesting depth of the array.</param>
    /// <param name="truthiness">On a direct resolution, the truthiness; otherwise the falsy default.</param>
    /// <returns><see langword="true"/> when a cursor was pushed (or the singleton element scheduled) with no verdict; <see langword="false"/> when <paramref name="truthiness"/> holds the verdict.</returns>
    /// <exception cref="JsonataEvaluationLimitException">The array-nesting depth bound was exceeded.</exception>
    private static bool TryScheduleArrayTruthiness(IReadOnlyList<JsonataValue> items, Stack<TruthinessCursor> pending, int depth, out bool truthiness)
    {
        int childDepth = depth + 1;
        if(childDepth > JsonataLimits.MaxEvaluationDepth)
        {
            throw new JsonataEvaluationLimitException(JsonataLimit.EvaluationDepth, "JSONata truthiness over nested arrays exceeded the maximum depth.");
        }

        if(items.Count == 0)
        {
            truthiness = false;

            return false;
        }

        if(items.Count == 1)
        {
            //The singleton spine unwraps to the truthiness of its single element.
            return TryScalarTruthiness(items[0], pending, childDepth, out truthiness);
        }

        pending.Push(new TruthinessCursor(items, childDepth));
        truthiness = false;

        return true;
    }

    /// <summary>A cursor over a multi-element array's members during the iterative truthiness any-truthy scan, carrying the array-nesting depth.</summary>
    private sealed class TruthinessCursor
    {
        /// <summary>Initializes a cursor over an array's members at a given nesting depth.</summary>
        /// <param name="items">The array members to scan.</param>
        /// <param name="depth">The array-nesting depth of this array.</param>
        public TruthinessCursor(IReadOnlyList<JsonataValue> items, int depth)
        {
            Items = items;
            Depth = depth;
        }

        /// <summary>Gets the array members being scanned.</summary>
        public IReadOnlyList<JsonataValue> Items { get; }

        /// <summary>Gets the array-nesting depth of this array.</summary>
        public int Depth { get; }

        /// <summary>Gets or sets the next member index to process.</summary>
        public int NextIndex { get; set; }
    }
}
