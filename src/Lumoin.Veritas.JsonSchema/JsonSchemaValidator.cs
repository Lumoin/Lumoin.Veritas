using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Json;
using Ptr = Lumoin.Veritas.JsonPointer.JsonPointer;

namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// Validates a JSON instance against a JSON Schema (draft 2020-12) over the backend-agnostic
/// <see cref="JsonNode"/> model.
/// </summary>
/// <remarks>
/// <para>
/// Evaluation is iterative: a schema and instance are walked with an explicit two-phase work stack
/// (open a frame, then close it once its child frames have reported), never by call-stack recursion.
/// Unknown keywords are treated as annotations and ignored, as the specification requires, so an
/// instance is only rejected by a keyword this validator actually asserts.
/// </para>
/// </remarks>
public static class JsonSchemaValidator
{
    /// <summary>Validates an instance against a schema.</summary>
    /// <param name="schema">The schema node (an object schema, or a boolean schema <c>true</c>/<c>false</c>).</param>
    /// <param name="instance">The instance to validate.</param>
    /// <returns>The validation result.</returns>
    public static ValidationResult Validate(JsonNode schema, JsonNode instance)
    {
        List<ValidationError> errors = [];
        Frame root = new()
        {
            Schema = schema,
            Instance = instance,
            InstanceLocation = Ptr.Root,
            KeywordLocation = Ptr.Root
        };

        Stack<(Frame Frame, bool Closing)> stack = new();
        stack.Push((root, false));

        while(stack.Count > 0)
        {
            (Frame frame, bool closing) = stack.Pop();
            if(closing)
            {
                CloseFrame(frame);
                continue;
            }

            OpenFrame(frame, stack, errors);
        }

        return new ValidationResult(root.Result, errors);
    }

    /// <summary>Opens a frame: evaluates its self-contained keywords and pushes child frames for its applicators.</summary>
    /// <param name="frame">The frame being opened.</param>
    /// <param name="stack">The work stack.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void OpenFrame(Frame frame, Stack<(Frame, bool)> stack, List<ValidationError> errors)
    {
        //A boolean schema asserts directly: true accepts everything, false rejects everything.
        if(frame.Schema.Kind is JsonNodeKind.True or JsonNodeKind.False)
        {
            frame.SelfValid = frame.Schema.Kind == JsonNodeKind.True;
            if(!frame.SelfValid)
            {
                errors.Add(new ValidationError(frame.InstanceLocation, frame.KeywordLocation, "The boolean schema 'false' rejects all instances."));
            }

            stack.Push((frame, true));

            return;
        }

        //A non-object, non-boolean node in schema position carries no assertions.
        if(frame.Schema.Kind != JsonNodeKind.Object)
        {
            stack.Push((frame, true));

            return;
        }

        EvaluateSelfKeywords(frame, errors);

        //The frame's Close must run after every child, so it is pushed first (children pushed above it pop first).
        stack.Push((frame, true));

        List<ChildRequest> children = [];
        CollectApplicatorChildren(frame, children);
        foreach(ChildRequest request in children)
        {
            Frame child = new()
            {
                Schema = request.Schema,
                Instance = request.Instance,
                InstanceLocation = request.InstanceLocation,
                KeywordLocation = request.KeywordLocation,
                Parent = frame,
                SpawnKeyword = request.Keyword
            };

            stack.Push((child, false));
        }
    }

    /// <summary>Closes a frame: combines its self validity with its applicator children and reports the result to its parent.</summary>
    /// <param name="frame">The frame being closed.</param>
    private static void CloseFrame(Frame frame)
    {
        frame.Result = frame.SelfValid && CombineChildren(frame.ChildResults);
        frame.Parent?.ChildResults.Add((frame.SpawnKeyword!, frame.Result));
    }

    /// <summary>Combines a frame's applicator-child results, grouped by the keyword that produced them.</summary>
    /// <param name="childResults">The child results as (keyword, valid) pairs.</param>
    /// <returns><see langword="true"/> when every applicator group is satisfied.</returns>
    private static bool CombineChildren(List<(string Keyword, bool Valid)> childResults)
    {
        if(childResults.Count == 0)
        {
            return true;
        }

        Dictionary<string, (int Total, int Valid)> groups = new(StringComparer.Ordinal);
        foreach((string keyword, bool valid) in childResults)
        {
            (int total, int validCount) = groups.TryGetValue(keyword, out (int Total, int Valid) existing) ? existing : (0, 0);
            groups[keyword] = (total + 1, validCount + (valid ? 1 : 0));
        }

        foreach(KeyValuePair<string, (int Total, int Valid)> group in groups)
        {
            (int total, int valid) = group.Value;
            string keyword = group.Key;
            bool satisfied =
                ReferenceEquals(keyword, JsonSchemaKeywords.AnyOf) ? valid >= 1 :
                ReferenceEquals(keyword, JsonSchemaKeywords.OneOf) ? valid == 1 :
                ReferenceEquals(keyword, JsonSchemaKeywords.Not) ? valid == 0 :
                valid == total;

            if(!satisfied)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Evaluates the keywords whose assertion depends only on this instance node (not on subschemas).</summary>
    /// <param name="frame">The frame whose schema and instance are evaluated.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateSelfKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;

        if(schema.TryGetProperty(JsonSchemaKeywords.Type, out JsonNode typeNode) && !MatchesType(instance, typeNode))
        {
            Fail(frame, errors, JsonSchemaKeywords.Type, "The instance is not of the expected type.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Const, out JsonNode constNode) && !JsonValueComparer.Equal(instance, constNode))
        {
            Fail(frame, errors, JsonSchemaKeywords.Const, "The instance does not equal the constant value.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Enum, out JsonNode enumNode) && enumNode.Kind == JsonNodeKind.Array && !EnumContains(enumNode, instance))
        {
            Fail(frame, errors, JsonSchemaKeywords.Enum, "The instance is not one of the enumerated values.");
        }

        EvaluateNumericKeywords(frame, errors);
        EvaluateStringKeywords(frame, errors);
        EvaluateArraySelfKeywords(frame, errors);
        EvaluateObjectSelfKeywords(frame, errors);
    }

    /// <summary>Evaluates the numeric assertions (<c>multipleOf</c>, the range bounds) when the instance is a number.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateNumericKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.Number)
        {
            return;
        }

        string value = instance.GetRawNumber();

        if(schema.TryGetProperty(JsonSchemaKeywords.MultipleOf, out JsonNode multiple) && multiple.Kind == JsonNodeKind.Number && !IsMultipleOf(value, multiple.GetRawNumber()))
        {
            Fail(frame, errors, JsonSchemaKeywords.MultipleOf, "The number is not a multiple of the required value.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Maximum, out JsonNode maximum) && maximum.Kind == JsonNodeKind.Number && CompareNumbers(value, maximum.GetRawNumber()) > 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.Maximum, "The number exceeds the maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.ExclusiveMaximum, out JsonNode exclusiveMaximum) && exclusiveMaximum.Kind == JsonNodeKind.Number && CompareNumbers(value, exclusiveMaximum.GetRawNumber()) >= 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.ExclusiveMaximum, "The number is not below the exclusive maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Minimum, out JsonNode minimum) && minimum.Kind == JsonNodeKind.Number && CompareNumbers(value, minimum.GetRawNumber()) < 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.Minimum, "The number is below the minimum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.ExclusiveMinimum, out JsonNode exclusiveMinimum) && exclusiveMinimum.Kind == JsonNodeKind.Number && CompareNumbers(value, exclusiveMinimum.GetRawNumber()) <= 0)
        {
            Fail(frame, errors, JsonSchemaKeywords.ExclusiveMinimum, "The number is not above the exclusive minimum.");
        }
    }

    /// <summary>Evaluates the string assertions (length bounds, <c>pattern</c>) when the instance is a string.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateStringKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.String)
        {
            return;
        }

        string text = instance.GetString();

        if(schema.TryGetProperty(JsonSchemaKeywords.MinLength, out JsonNode minLength) && minLength.Kind == JsonNodeKind.Number && CodePointCount(text) < ToInt(minLength))
        {
            Fail(frame, errors, JsonSchemaKeywords.MinLength, "The string is shorter than the minimum length.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.MaxLength, out JsonNode maxLength) && maxLength.Kind == JsonNodeKind.Number && CodePointCount(text) > ToInt(maxLength))
        {
            Fail(frame, errors, JsonSchemaKeywords.MaxLength, "The string is longer than the maximum length.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Pattern, out JsonNode pattern) && pattern.Kind == JsonNodeKind.String && !Regex.IsMatch(text, pattern.GetString()))
        {
            Fail(frame, errors, JsonSchemaKeywords.Pattern, "The string does not match the required pattern.");
        }
    }

    /// <summary>Evaluates the array assertions that do not apply subschemas (length bounds, <c>uniqueItems</c>).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateArraySelfKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.Array)
        {
            return;
        }

        List<JsonNode> items = [.. instance.EnumerateArray()];

        if(schema.TryGetProperty(JsonSchemaKeywords.MinItems, out JsonNode minItems) && minItems.Kind == JsonNodeKind.Number && items.Count < ToInt(minItems))
        {
            Fail(frame, errors, JsonSchemaKeywords.MinItems, "The array has fewer items than the minimum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.MaxItems, out JsonNode maxItems) && maxItems.Kind == JsonNodeKind.Number && items.Count > ToInt(maxItems))
        {
            Fail(frame, errors, JsonSchemaKeywords.MaxItems, "The array has more items than the maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.UniqueItems, out JsonNode unique) && unique.Kind == JsonNodeKind.True && !AllUnique(items))
        {
            Fail(frame, errors, JsonSchemaKeywords.UniqueItems, "The array items are not unique.");
        }
    }

    /// <summary>Evaluates the object assertions that do not apply subschemas (member-count bounds, <c>required</c>).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="errors">The accumulating error list.</param>
    private static void EvaluateObjectSelfKeywords(Frame frame, List<ValidationError> errors)
    {
        JsonNode schema = frame.Schema;
        JsonNode instance = frame.Instance;
        if(instance.Kind != JsonNodeKind.Object)
        {
            return;
        }

        Dictionary<string, JsonNode> members = ReadObject(instance);

        if(schema.TryGetProperty(JsonSchemaKeywords.MinProperties, out JsonNode minProperties) && minProperties.Kind == JsonNodeKind.Number && members.Count < ToInt(minProperties))
        {
            Fail(frame, errors, JsonSchemaKeywords.MinProperties, "The object has fewer members than the minimum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.MaxProperties, out JsonNode maxProperties) && maxProperties.Kind == JsonNodeKind.Number && members.Count > ToInt(maxProperties))
        {
            Fail(frame, errors, JsonSchemaKeywords.MaxProperties, "The object has more members than the maximum.");
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Required, out JsonNode required) && required.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode name in required.EnumerateArray())
            {
                if(name.Kind == JsonNodeKind.String && !members.ContainsKey(name.GetString()))
                {
                    Fail(frame, errors, JsonSchemaKeywords.Required, "A required member is missing.");
                }
            }
        }
    }

    /// <summary>Collects the child frame requests for this frame's applicator keywords.</summary>
    /// <param name="frame">The frame whose applicators are expanded.</param>
    /// <param name="requests">The list to populate with child requests.</param>
    private static void CollectApplicatorChildren(Frame frame, List<ChildRequest> requests)
    {
        JsonNode schema = frame.Schema;

        //allOf / anyOf / oneOf: each array element is a subschema applied to the same instance node.
        CollectSchemaArray(frame, JsonSchemaKeywords.AllOf, requests);
        CollectSchemaArray(frame, JsonSchemaKeywords.AnyOf, requests);
        CollectSchemaArray(frame, JsonSchemaKeywords.OneOf, requests);

        //not: a single subschema applied to the same instance node.
        if(schema.TryGetProperty(JsonSchemaKeywords.Not, out JsonNode not))
        {
            requests.Add(new ChildRequest(JsonSchemaKeywords.Not, not, frame.Instance, frame.InstanceLocation, frame.KeywordLocation.Append(JsonSchemaKeywords.Not)));
        }

        if(frame.Instance.Kind == JsonNodeKind.Object)
        {
            CollectObjectApplicators(frame, requests);
        }

        if(frame.Instance.Kind == JsonNodeKind.Array)
        {
            CollectArrayApplicators(frame, requests);
        }
    }

    /// <summary>Collects child requests for a keyword whose value is an array of subschemas (<c>allOf</c>/<c>anyOf</c>/<c>oneOf</c>).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="keyword">The keyword name.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectSchemaArray(Frame frame, string keyword, List<ChildRequest> requests)
    {
        if(!frame.Schema.TryGetProperty(keyword, out JsonNode array) || array.Kind != JsonNodeKind.Array)
        {
            return;
        }

        int index = 0;
        foreach(JsonNode subschema in array.EnumerateArray())
        {
            requests.Add(new ChildRequest(keyword, subschema, frame.Instance, frame.InstanceLocation, frame.KeywordLocation.Append(keyword).Append(index)));
            index++;
        }
    }

    /// <summary>Collects <c>properties</c> and <c>additionalProperties</c> child requests for an object instance.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectObjectApplicators(Frame frame, List<ChildRequest> requests)
    {
        JsonNode schema = frame.Schema;
        Dictionary<string, JsonNode> members = ReadObject(frame.Instance);

        HashSet<string> covered = new(StringComparer.Ordinal);
        if(schema.TryGetProperty(JsonSchemaKeywords.Properties, out JsonNode properties) && properties.Kind == JsonNodeKind.Object)
        {
            foreach(KeyValuePair<string, JsonNode> definition in properties.EnumerateObject())
            {
                covered.Add(definition.Key);
                if(members.TryGetValue(definition.Key, out JsonNode value))
                {
                    requests.Add(new ChildRequest(
                        JsonSchemaKeywords.Properties,
                        definition.Value,
                        value,
                        frame.InstanceLocation.Append(definition.Key),
                        frame.KeywordLocation.Append(JsonSchemaKeywords.Properties).Append(definition.Key)));
                }
            }
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.AdditionalProperties, out JsonNode additional))
        {
            foreach(KeyValuePair<string, JsonNode> member in members)
            {
                if(!covered.Contains(member.Key))
                {
                    requests.Add(new ChildRequest(
                        JsonSchemaKeywords.AdditionalProperties,
                        additional,
                        member.Value,
                        frame.InstanceLocation.Append(member.Key),
                        frame.KeywordLocation.Append(JsonSchemaKeywords.AdditionalProperties)));
                }
            }
        }
    }

    /// <summary>Collects <c>prefixItems</c> and <c>items</c> child requests for an array instance.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="requests">The list to populate.</param>
    private static void CollectArrayApplicators(Frame frame, List<ChildRequest> requests)
    {
        JsonNode schema = frame.Schema;
        List<JsonNode> elements = [.. frame.Instance.EnumerateArray()];

        int prefixCount = 0;
        if(schema.TryGetProperty(JsonSchemaKeywords.PrefixItems, out JsonNode prefixItems) && prefixItems.Kind == JsonNodeKind.Array)
        {
            List<JsonNode> prefixSchemas = [.. prefixItems.EnumerateArray()];
            prefixCount = prefixSchemas.Count;
            for(int i = 0; i < prefixSchemas.Count && i < elements.Count; i++)
            {
                requests.Add(new ChildRequest(
                    JsonSchemaKeywords.PrefixItems,
                    prefixSchemas[i],
                    elements[i],
                    frame.InstanceLocation.Append(i),
                    frame.KeywordLocation.Append(JsonSchemaKeywords.PrefixItems).Append(i)));
            }
        }

        if(schema.TryGetProperty(JsonSchemaKeywords.Items, out JsonNode items))
        {
            for(int i = prefixCount; i < elements.Count; i++)
            {
                requests.Add(new ChildRequest(
                    JsonSchemaKeywords.Items,
                    items,
                    elements[i],
                    frame.InstanceLocation.Append(i),
                    frame.KeywordLocation.Append(JsonSchemaKeywords.Items)));
            }
        }
    }

    /// <summary>Records an assertion failure on a frame and in the error list.</summary>
    /// <param name="frame">The frame whose self validity is cleared.</param>
    /// <param name="errors">The accumulating error list.</param>
    /// <param name="keyword">The failing keyword.</param>
    /// <param name="message">The failure description.</param>
    private static void Fail(Frame frame, List<ValidationError> errors, string keyword, string message)
    {
        frame.SelfValid = false;
        errors.Add(new ValidationError(frame.InstanceLocation, frame.KeywordLocation.Append(keyword), message));
    }

    /// <summary>Whether the instance matches a <c>type</c> value (a single type name or an array of names).</summary>
    /// <param name="instance">The instance.</param>
    /// <param name="typeNode">The <c>type</c> keyword value.</param>
    /// <returns><see langword="true"/> when the instance matches any named type.</returns>
    private static bool MatchesType(JsonNode instance, JsonNode typeNode)
    {
        if(typeNode.Kind == JsonNodeKind.String)
        {
            return MatchesTypeName(instance, typeNode.GetString());
        }

        if(typeNode.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode name in typeNode.EnumerateArray())
            {
                if(name.Kind == JsonNodeKind.String && MatchesTypeName(instance, name.GetString()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether the instance matches a single JSON Schema primitive type name.</summary>
    /// <param name="instance">The instance.</param>
    /// <param name="typeName">The type name (<c>null</c>, <c>boolean</c>, <c>object</c>, <c>array</c>, <c>string</c>, <c>number</c>, <c>integer</c>).</param>
    /// <returns><see langword="true"/> when the instance is of that type.</returns>
    private static bool MatchesTypeName(JsonNode instance, string typeName)
    {
        return typeName switch
        {
            "null" => instance.Kind == JsonNodeKind.Null,
            "boolean" => instance.Kind is JsonNodeKind.True or JsonNodeKind.False,
            "object" => instance.Kind == JsonNodeKind.Object,
            "array" => instance.Kind == JsonNodeKind.Array,
            "string" => instance.Kind == JsonNodeKind.String,
            "number" => instance.Kind == JsonNodeKind.Number,
            "integer" => instance.Kind == JsonNodeKind.Number && IsIntegral(instance.GetRawNumber()),
            _ => false
        };
    }

    /// <summary>Whether an <c>enum</c> array contains a value equal to the instance.</summary>
    /// <param name="enumNode">The <c>enum</c> array.</param>
    /// <param name="instance">The instance.</param>
    /// <returns><see langword="true"/> when a member equals the instance.</returns>
    private static bool EnumContains(JsonNode enumNode, JsonNode instance)
    {
        foreach(JsonNode candidate in enumNode.EnumerateArray())
        {
            if(JsonValueComparer.Equal(instance, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether every element of a list is distinct under JSON Schema value equality.</summary>
    /// <param name="items">The array elements.</param>
    /// <returns><see langword="true"/> when no two elements are equal.</returns>
    private static bool AllUnique(List<JsonNode> items)
    {
        for(int i = 0; i < items.Count; i++)
        {
            for(int j = i + 1; j < items.Count; j++)
            {
                if(JsonValueComparer.Equal(items[i], items[j]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a number's lexical form denotes an integer (a zero fractional part).</summary>
    /// <param name="raw">The raw number.</param>
    /// <returns><see langword="true"/> when the value is integral.</returns>
    private static bool IsIntegral(string raw)
    {
        if(decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
        {
            return value == Math.Truncate(value);
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
            && double.IsFinite(real)
            && real == Math.Truncate(real);
    }

    /// <summary>Whether <paramref name="value"/> is an exact multiple of <paramref name="factor"/>.</summary>
    /// <param name="value">The instance number's raw form.</param>
    /// <param name="factor">The <c>multipleOf</c> value's raw form.</param>
    /// <returns><see langword="true"/> when the division leaves no remainder.</returns>
    private static bool IsMultipleOf(string value, string factor)
    {
        if(decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal valueDecimal)
            && decimal.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal factorDecimal))
        {
            return factorDecimal != 0 && valueDecimal % factorDecimal == 0;
        }

        if(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double valueDouble)
            && double.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out double factorDouble)
            && factorDouble != 0)
        {
            double quotient = valueDouble / factorDouble;

            return double.IsFinite(quotient) && quotient == Math.Round(quotient);
        }

        return false;
    }

    /// <summary>Compares two number lexical forms by mathematical value.</summary>
    /// <param name="left">The first raw number.</param>
    /// <param name="right">The second raw number.</param>
    /// <returns>A negative value, zero, or a positive value as <paramref name="left"/> is less than, equal to, or greater than <paramref name="right"/>.</returns>
    private static int CompareNumbers(string left, string right)
    {
        if(decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal leftDecimal)
            && decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        double leftDouble = double.Parse(left, NumberStyles.Float, CultureInfo.InvariantCulture);
        double rightDouble = double.Parse(right, NumberStyles.Float, CultureInfo.InvariantCulture);

        return leftDouble.CompareTo(rightDouble);
    }

    /// <summary>Reads a numeric keyword value as an <see cref="int"/>.</summary>
    /// <param name="node">The numeric node.</param>
    /// <returns>The integer value (truncated toward zero).</returns>
    private static int ToInt(JsonNode node)
    {
        return (int)double.Parse(node.GetRawNumber(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Counts the Unicode code points in a string (JSON Schema length is measured in code points).</summary>
    /// <param name="text">The string.</param>
    /// <returns>The code-point count.</returns>
    private static int CodePointCount(string text)
    {
        int count = 0;
        foreach(System.Text.Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    /// <summary>Materialises an object instance's members into a name-to-value map.</summary>
    /// <param name="instance">The object instance.</param>
    /// <returns>The member map.</returns>
    private static Dictionary<string, JsonNode> ReadObject(JsonNode instance)
    {
        Dictionary<string, JsonNode> members = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> member in instance.EnumerateObject())
        {
            members[member.Key] = member.Value;
        }

        return members;
    }

    /// <summary>A pending subschema evaluation produced by an applicator keyword.</summary>
    /// <param name="Keyword">The applicator keyword that produced this child.</param>
    /// <param name="Schema">The subschema node.</param>
    /// <param name="Instance">The instance node the subschema applies to.</param>
    /// <param name="InstanceLocation">The child's instance location.</param>
    /// <param name="KeywordLocation">The child's keyword location.</param>
    private readonly record struct ChildRequest(
        string Keyword,
        JsonNode Schema,
        JsonNode Instance,
        Ptr InstanceLocation,
        Ptr KeywordLocation);

    /// <summary>One node of the evaluation: a schema applied to an instance node, with its place in the result tree.</summary>
    private sealed class Frame
    {
        /// <summary>Gets the schema node being evaluated.</summary>
        public required JsonNode Schema { get; init; }

        /// <summary>Gets the instance node being evaluated.</summary>
        public required JsonNode Instance { get; init; }

        /// <summary>Gets the instance location of this frame.</summary>
        public required Ptr InstanceLocation { get; init; }

        /// <summary>Gets the keyword location of this frame.</summary>
        public required Ptr KeywordLocation { get; init; }

        /// <summary>Gets the parent frame this frame reports its result to, or <see langword="null"/> for the root.</summary>
        public Frame? Parent { get; init; }

        /// <summary>Gets the applicator keyword that produced this frame, or <see langword="null"/> for the root.</summary>
        public string? SpawnKeyword { get; init; }

        /// <summary>Gets or sets whether this frame's self-contained keywords held.</summary>
        public bool SelfValid { get; set; } = true;

        /// <summary>Gets or sets the combined result of this frame, set when it closes.</summary>
        public bool Result { get; set; }

        /// <summary>Gets the results reported by this frame's applicator children, as (keyword, valid) pairs.</summary>
        public List<(string Keyword, bool Valid)> ChildResults { get; } = [];
    }
}
