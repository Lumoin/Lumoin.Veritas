using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The pure array built-in functions: <c>$count</c>, <c>$reverse</c>, <c>$distinct</c>, <c>$append</c>, and <c>$zip</c>,
/// plus the one context-aware array built-in <c>$shuffle</c> (it draws from the evaluation's injected randomness
/// source, exposed through <see cref="ContextualAll"/>).
/// The single-array functions (<c>$count</c>, <c>$reverse</c>, <c>$shuffle</c>) carry the <c>&lt;a&gt;</c> signature, so the
/// validator wraps a lone value in a one-element array and a defined argument reaches the body as an array;
/// <c>$distinct</c> and <c>$append</c> carry the <c>&lt;x&gt;</c>/<c>&lt;xx&gt;</c> signatures and do not
/// singleton-wrap.
/// </summary>
/// <remarks>
/// <para>
/// The sequence-flag / keep-as-array model is not yet built, so <c>$distinct</c> and <c>$append</c> return
/// plain arrays; the reference's <c>createSequence</c>-versus-plain-array distinction is not observable
/// here. The <c>$append</c> runtime sequence-size cap (D2015) is likewise unreachable, as there is no
/// runtime sequence option to overflow. These are fragment-relative divergences from the reference.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/array-functions">the JSONata array-functions reference</see>.</para>
/// </remarks>
internal static class JsonataArrayFunctions
{
    /// <summary>The array built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("count"), InvokeCount, JsonataSignature.Parse("<a:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("reverse"), InvokeReverse, JsonataSignature.Parse("<a:a>")),
        new JsonataBuiltinFunction(Utf8Strings.From("distinct"), InvokeDistinct, JsonataSignature.Parse("<x:x>")),
        new JsonataBuiltinFunction(Utf8Strings.From("append"), InvokeAppend, JsonataSignature.Parse("<xx:a>")),
        new JsonataBuiltinFunction(Utf8Strings.From("zip"), InvokeZip, JsonataSignature.Parse("<:a>"))
    ];

    /// <summary>The context-aware array built-ins (<c>$shuffle</c>), exposed for the registry.</summary>
    public static IReadOnlyList<JsonataContextualBuiltinFunction> ContextualAll { get; } =
    [
        new JsonataContextualBuiltinFunction(Utf8Strings.From("shuffle"), InvokeShuffle, JsonataSignature.Parse("<a:a>"))
    ];

    /// <summary>
    /// <c>$zip(array, ...)</c>: convolves (zips) two or more arrays into an array of tuples, the i-th tuple
    /// holding the i-th element of each argument. The result length is the shortest argument's length, so
    /// longer arguments are truncated; an undefined argument has length zero, collapsing the result to the
    /// empty array. A non-array argument is treated as a one-element array, so <c>$zip(1, 2)</c> is
    /// <c>[[1, 2]]</c>. The signature declares no parameters, so the arguments reach the body verbatim (no
    /// singleton-wrapping or context substitution) and this body does the array coercion itself.
    /// </summary>
    /// <param name="arguments">The argument list; the arrays (or scalars) to zip.</param>
    /// <returns>The array of position-wise tuples, truncated to the shortest argument's length.</returns>
    private static JsonataValue InvokeZip(IReadOnlyList<JsonataValue> arguments)
    {
        if(arguments.Count == 0)
        {
            return JsonataValue.Array([]);
        }

        int length = int.MaxValue;
        foreach(JsonataValue argument in arguments)
        {
            length = System.Math.Min(length, ZipLength(argument));
        }

        List<JsonataValue> result = [];
        for(int i = 0; i < length; i++)
        {
            List<JsonataValue> tuple = new(arguments.Count);
            foreach(JsonataValue argument in arguments)
            {
                tuple.Add(argument.Kind == JsonataValueKind.Array ? argument.AsArray[i] : argument);
            }

            result.Add(JsonataValue.Array(tuple));
        }

        return JsonataValue.Array(result);
    }

    /// <summary>Returns the zip length of an argument: an array's element count, zero for undefined, and one for any other scalar (which zips as a one-element array).</summary>
    /// <param name="argument">The argument to measure.</param>
    /// <returns>The argument's zip length.</returns>
    private static int ZipLength(JsonataValue argument)
    {
        if(argument.Kind == JsonataValueKind.Array)
        {
            return argument.AsArray.Count;
        }

        return argument.IsUndefined ? 0 : 1;
    }

    /// <summary><c>$count(array)</c>: the number of elements in an array; undefined yields <c>0</c>.</summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <returns>The element count as a number.</returns>
    private static JsonataValue InvokeCount(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Number(0);
        }

        return JsonataValue.Number(value.AsArray.Count);
    }

    /// <summary><c>$reverse(array)</c>: a new array with the elements reversed; a 0/1-element array is returned as-is; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <returns>The reversed array, the input for a short array, or undefined.</returns>
    private static JsonataValue InvokeReverse(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        if(items.Count <= 1)
        {
            return value;
        }

        List<JsonataValue> reversed = new(items.Count);
        for(int i = items.Count - 1; i >= 0; i--)
        {
            reversed.Add(items[i]);
        }

        return JsonataValue.Array(reversed);
    }

    /// <summary>
    /// <c>$shuffle(array)</c>: a new array with the elements in a random order, drawn from the evaluation's
    /// injected randomness source. An undefined argument yields undefined; an array of length zero or one is
    /// returned unchanged with no draw made (there is nothing to permute); otherwise a copy of the elements is
    /// shuffled in place by the Fisher-Yates algorithm, each swap index drawn from the context's
    /// <see cref="JsonataContext.Randomness"/>. Because the randomness source is captured once at the top of
    /// the evaluation, a fixed source makes the permutation deterministic.
    /// </summary>
    /// <param name="arguments">The argument list; the array is the first argument.</param>
    /// <param name="context">The evaluation context whose injected randomness source supplies the swap indices.</param>
    /// <returns>The shuffled array, the input for an undefined or 0/1-element array.</returns>
    private static JsonataValue InvokeShuffle(IReadOnlyList<JsonataValue> arguments, JsonataContext context)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        if(items.Count <= 1)
        {
            //Nothing to permute, so no randomness is drawn: a 0/1-element array is its own only ordering.
            return value;
        }

        List<JsonataValue> shuffled = new(items);
        FisherYates(shuffled, context.Randomness);

        return JsonataValue.Array(shuffled);
    }

    /// <summary>
    /// Permutes a list in place by the Fisher-Yates algorithm: walking from the last index down to the first,
    /// each element is swapped with one at a uniformly-chosen index in the not-yet-fixed prefix, so every
    /// permutation is equally likely under a uniform source. The loop is bounded by the list length, so there
    /// is no recursion. Each swap index is drawn from the injected source, with the loop position folded into
    /// the request salt so a deterministic source still yields distinct draws across positions.
    /// </summary>
    /// <param name="items">The list to permute in place.</param>
    /// <param name="randomness">The randomness source each swap index is drawn from.</param>
    private static void FisherYates(List<JsonataValue> items, RandomnessDelegate randomness)
    {
        for(int i = items.Count - 1; i >= 1; i--)
        {
            int j = DrawIndex(randomness, i + 1, i);
            JsonataValue swap = items[i];
            items[i] = items[j];
            items[j] = swap;
        }
    }

    /// <summary>
    /// Draws a uniform index in the half-open range <c>[0, bound)</c> from the injected randomness source: it
    /// requests a uniform double in <c>[0.0, 1.0)</c> and scales it to the bound, clamping the rare floating
    /// point edge that would otherwise land exactly on the bound back to the last valid index. The position is
    /// written into the request's call-site salt so a deterministic (seeded) source produces a distinct draw at
    /// each position rather than the same value for every swap.
    /// </summary>
    /// <param name="randomness">The randomness source to draw from.</param>
    /// <param name="bound">The exclusive upper bound of the drawn index; always at least one.</param>
    /// <param name="position">The Fisher-Yates loop position, folded into the request salt for per-draw distinctness.</param>
    /// <returns>A uniform index in <c>[0, bound)</c>.</returns>
    private static int DrawIndex(RandomnessDelegate randomness, int bound, int position)
    {
        Span<byte> salt = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(salt, position);
        RandomnessRequest request = new(RandomnessKind.UniformDouble, CorrelationId: default, ByteCount: 0, CallSiteSalt: salt.ToArray());
        double unit = randomness(in request).Double;
        int index = (int)(unit * bound);

        return index >= bound ? bound - 1 : index;
    }

    /// <summary>
    /// <c>$distinct(value)</c>: removes duplicate elements from an array using JSONata deep value equality,
    /// preserving first-occurrence order; a non-array value or a 0/1-element array is returned unchanged;
    /// undefined yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the value is the first argument.</param>
    /// <returns>The deduplicated array, the input for a non-array or short array, or undefined.</returns>
    /// <remarks>
    /// Deduplication is a first-occurrence O(n²) scan over <see cref="JsonataValue.DeepEquals(JsonataValue, JsonataValue)"/>,
    /// so structurally equal objects and arrays collapse, not just primitives.
    /// </remarks>
    private static JsonataValue InvokeDistinct(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Array)
        {
            return value;
        }

        IReadOnlyList<JsonataValue> items = value.AsArray;
        if(items.Count <= 1)
        {
            return value;
        }

        List<JsonataValue> kept = [];
        foreach(JsonataValue candidate in items)
        {
            if(!ContainsDeepEqual(kept, candidate))
            {
                kept.Add(candidate);
            }
        }

        return JsonataValue.Array(kept);
    }

    /// <summary>
    /// <c>$append(arg1, arg2)</c>: concatenates two operands one level deep. An undefined operand yields the
    /// other operand verbatim (so two undefined operands yield undefined), and a non-array operand is coerced
    /// to a one-element array before the flat join; nested arrays are not deep-flattened.
    /// </summary>
    /// <param name="arguments">The argument list; the two operands to concatenate.</param>
    /// <returns>The concatenation, the defined operand when the other is undefined, or undefined when both are undefined.</returns>
    private static JsonataValue InvokeAppend(IReadOnlyList<JsonataValue> arguments)
    {
        return AppendOneLevel(First(arguments), arguments.Count > 1 ? arguments[1] : JsonataValue.Undefined);
    }

    /// <summary>
    /// Concatenates two operands one level deep (the reference's <c>fn.append</c>): an undefined operand yields
    /// the other operand verbatim (so two undefined operands yield undefined), and a non-array operand is coerced
    /// to a one-element array before the flat join; nested arrays are not deep-flattened. This is the single
    /// source the <c>$append</c> builtin and the tuple-stream <c>reduceTupleStream</c> merge share.
    /// </summary>
    /// <param name="first">The accumulated value so far (the first operand).</param>
    /// <param name="second">The value to append (the second operand).</param>
    /// <returns>The concatenation, the defined operand when the other is undefined, or undefined when both are undefined.</returns>
    internal static JsonataValue AppendOneLevel(JsonataValue first, JsonataValue second)
    {
        if(first.IsUndefined)
        {
            return second;
        }

        if(second.IsUndefined)
        {
            return first;
        }

        IReadOnlyList<JsonataValue> firstItems = ToItems(first);
        IReadOnlyList<JsonataValue> secondItems = ToItems(second);
        List<JsonataValue> joined = new(firstItems.Count + secondItems.Count);
        joined.AddRange(firstItems);
        joined.AddRange(secondItems);

        return JsonataValue.Array(joined);
    }

    /// <summary>Determines whether an element deep-equal to a candidate has already been kept.</summary>
    /// <param name="kept">The already-kept elements, in first-occurrence order.</param>
    /// <param name="candidate">The candidate element being considered.</param>
    /// <returns><see langword="true"/> when a kept element is deep-equal to the candidate.</returns>
    private static bool ContainsDeepEqual(List<JsonataValue> kept, JsonataValue candidate)
    {
        foreach(JsonataValue existing in kept)
        {
            if(JsonataValue.DeepEquals(existing, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Normalizes a defined argument to its elements: an array yields its elements, any other defined value is a one-element list.</summary>
    /// <param name="value">The defined argument value.</param>
    /// <returns>The elements.</returns>
    private static IReadOnlyList<JsonataValue> ToItems(JsonataValue value)
    {
        if(value.Kind == JsonataValueKind.Array)
        {
            return value.AsArray;
        }

        return [value];
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
