using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The numeric aggregation built-in functions over an array: <c>$sum</c>, <c>$max</c>, <c>$min</c>, and
/// <c>$average</c>. The <c>&lt;a&lt;n&gt;&gt;</c> signature wraps a lone number in a one-element array and
/// rejects a non-numeric element with T0412, so a defined argument reaches the body as an array of numbers;
/// an undefined argument flows through to undefined.
/// </summary>
/// <remarks>
/// <para>
/// The empty-array result is asymmetric, faithful to the reference: <c>$sum([])</c> is <c>0</c>, while
/// <c>$max([])</c>, <c>$min([])</c>, and <c>$average([])</c> are undefined (not an infinity or NaN).
/// </para>
/// <para>See <see href="https://docs.jsonata.org/aggregation-functions">the JSONata aggregation-functions reference</see>.</para>
/// </remarks>
internal static class JsonataAggregationFunctions
{
    /// <summary>The aggregation built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("sum"), InvokeSum, JsonataSignature.Parse("<a<n>:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("max"), InvokeMax, JsonataSignature.Parse("<a<n>:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("min"), InvokeMin, JsonataSignature.Parse("<a<n>:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("average"), InvokeAverage, JsonataSignature.Parse("<a<n>:n>"))
    ];

    /// <summary><c>$sum(array)</c>: the total of the array's numbers; the empty array totals <c>0</c>; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <returns>The sum, or undefined for an undefined argument.</returns>
    private static JsonataValue InvokeSum(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        double total = 0;
        foreach(JsonataValue item in items)
        {
            total += item.AsNumber;
        }

        return JsonataValue.Number(total);
    }

    /// <summary><c>$max(array)</c>: the largest of the array's numbers; the empty array yields undefined; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <returns>The maximum, or undefined for an undefined or empty argument.</returns>
    private static JsonataValue InvokeMax(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        if(items.Count == 0)
        {
            return JsonataValue.Undefined;
        }

        double largest = items[0].AsNumber;
        for(int i = 1; i < items.Count; i++)
        {
            double current = items[i].AsNumber;
            if(current > largest)
            {
                largest = current;
            }
        }

        return JsonataValue.Number(largest);
    }

    /// <summary><c>$min(array)</c>: the smallest of the array's numbers; the empty array yields undefined; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <returns>The minimum, or undefined for an undefined or empty argument.</returns>
    private static JsonataValue InvokeMin(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        if(items.Count == 0)
        {
            return JsonataValue.Undefined;
        }

        double smallest = items[0].AsNumber;
        for(int i = 1; i < items.Count; i++)
        {
            double current = items[i].AsNumber;
            if(current < smallest)
            {
                smallest = current;
            }
        }

        return JsonataValue.Number(smallest);
    }

    /// <summary><c>$average(array)</c>: the arithmetic mean of the array's numbers; the empty array yields undefined; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <returns>The mean, or undefined for an undefined or empty argument.</returns>
    private static JsonataValue InvokeAverage(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        if(items.Count == 0)
        {
            return JsonataValue.Undefined;
        }

        double total = 0;
        foreach(JsonataValue item in items)
        {
            total += item.AsNumber;
        }

        return JsonataValue.Number(total / items.Count);
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
