using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Json;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// JSON value equality as JSON Schema's <c>const</c> and <c>enum</c> keywords define it: structural
/// equality where numbers compare by mathematical value (so <c>1</c>, <c>1.0</c>, and <c>1e0</c> are
/// equal), objects compare order-independently by member, and arrays compare order-sensitively.
/// </summary>
/// <remarks>
/// The comparison is iterative (an explicit work stack of node pairs) rather than recursive, so deeply
/// nested values cannot overflow the call stack.
/// </remarks>
internal static class JsonValueComparer
{
    /// <summary>Determines whether two JSON values are equal under JSON Schema value equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool Equal(JsonNode left, JsonNode right)
    {
        Stack<(JsonNode Left, JsonNode Right)> pending = new();
        pending.Push((left, right));

        while(pending.Count > 0)
        {
            (JsonNode a, JsonNode b) = pending.Pop();
            if(!EqualShallow(a, b, pending))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares two nodes for this level, pushing child pairs onto <paramref name="pending"/> for compound kinds.</summary>
    /// <param name="a">The first node.</param>
    /// <param name="b">The second node.</param>
    /// <param name="pending">The work stack onto which child pairs are pushed.</param>
    /// <returns><see langword="true"/> when the two nodes match at this level.</returns>
    private static bool EqualShallow(JsonNode a, JsonNode b, Stack<(JsonNode, JsonNode)> pending)
    {
        JsonNodeKind kind = a.Kind;
        if(kind != b.Kind)
        {
            //Numbers are the only cross-kind case JSON has none of: each kind is distinct.
            return false;
        }

        return kind switch
        {
            JsonNodeKind.Null => true,
            JsonNodeKind.True => true,
            JsonNodeKind.False => true,
            JsonNodeKind.String => string.Equals(a.GetString(), b.GetString(), System.StringComparison.Ordinal),
            JsonNodeKind.Number => NumbersEqual(a.GetRawNumber(), b.GetRawNumber()),
            JsonNodeKind.Array => PushArray(a, b, pending),
            JsonNodeKind.Object => PushObject(a, b, pending),
            _ => false
        };
    }

    /// <summary>Compares two number lexical forms by mathematical value.</summary>
    /// <param name="left">The first raw number.</param>
    /// <param name="right">The second raw number.</param>
    /// <returns><see langword="true"/> when the two numbers are mathematically equal.</returns>
    private static bool NumbersEqual(string left, string right)
    {
        if(string.Equals(left, right, System.StringComparison.Ordinal))
        {
            return true;
        }

        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if(decimal.TryParse(left, style, CultureInfo.InvariantCulture, out decimal leftDecimal)
            && decimal.TryParse(right, style, CultureInfo.InvariantCulture, out decimal rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        return double.TryParse(left, style, CultureInfo.InvariantCulture, out double leftDouble)
            && double.TryParse(right, style, CultureInfo.InvariantCulture, out double rightDouble)
            && leftDouble == rightDouble;
    }

    /// <summary>Pushes element pairs of two arrays when their lengths match.</summary>
    /// <param name="a">The first array.</param>
    /// <param name="b">The second array.</param>
    /// <param name="pending">The work stack.</param>
    /// <returns><see langword="true"/> when the lengths match (element pairs are deferred to the stack).</returns>
    private static bool PushArray(JsonNode a, JsonNode b, Stack<(JsonNode, JsonNode)> pending)
    {
        List<JsonNode> left = [.. a.EnumerateArray()];
        List<JsonNode> right = [.. b.EnumerateArray()];
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

    /// <summary>Pushes member-value pairs of two objects when their member names match exactly.</summary>
    /// <param name="a">The first object.</param>
    /// <param name="b">The second object.</param>
    /// <param name="pending">The work stack.</param>
    /// <returns><see langword="true"/> when both objects carry the same member names (values are deferred to the stack).</returns>
    private static bool PushObject(JsonNode a, JsonNode b, Stack<(JsonNode, JsonNode)> pending)
    {
        Dictionary<string, JsonNode> left = new(System.StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> member in a.EnumerateObject())
        {
            left[member.Key] = member.Value;
        }

        int rightCount = 0;
        foreach(KeyValuePair<string, JsonNode> member in b.EnumerateObject())
        {
            rightCount++;
            if(!left.TryGetValue(member.Key, out JsonNode leftValue))
            {
                return false;
            }

            pending.Push((leftValue, member.Value));
        }

        return rightCount == left.Count;
    }
}
