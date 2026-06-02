namespace Lumoin.Veritas.JsonSchema;

/// <summary>
/// The canonical home for the JSON Schema (draft 2020-12) keyword tokens. Every part of the validator
/// that reads or dispatches on a keyword references these members rather than re-typing the raw literal,
/// so the keyword set has one source of truth.
/// </summary>
/// <remarks>
/// Each token is a <see langword="static"/> <see langword="readonly"/> <see cref="string"/> — one stable
/// instance in the assembly's data section, matching <see cref="Lumoin.Veritas.LinkedData.JsonLdKeywords"/>.
/// They are deliberately not <see langword="const"/>: a <c>const</c> is copied into every use site and can
/// never be reference-matched.
/// </remarks>
/// <seealso href="https://www.w3.org/TR/json-schema-validation/"/>
public static class JsonSchemaKeywords
{
    /// <summary>The <c>type</c> keyword — the permitted primitive type(s).</summary>
    public static readonly string Type = "type";

    /// <summary>The <c>const</c> keyword — the single permitted value.</summary>
    public static readonly string Const = "const";

    /// <summary>The <c>enum</c> keyword — the permitted set of values.</summary>
    public static readonly string Enum = "enum";

    /// <summary>The <c>multipleOf</c> keyword — the required divisor.</summary>
    public static readonly string MultipleOf = "multipleOf";

    /// <summary>The <c>maximum</c> keyword — the inclusive upper bound.</summary>
    public static readonly string Maximum = "maximum";

    /// <summary>The <c>exclusiveMaximum</c> keyword — the exclusive upper bound.</summary>
    public static readonly string ExclusiveMaximum = "exclusiveMaximum";

    /// <summary>The <c>minimum</c> keyword — the inclusive lower bound.</summary>
    public static readonly string Minimum = "minimum";

    /// <summary>The <c>exclusiveMinimum</c> keyword — the exclusive lower bound.</summary>
    public static readonly string ExclusiveMinimum = "exclusiveMinimum";

    /// <summary>The <c>minLength</c> keyword — the minimum string length, in code points.</summary>
    public static readonly string MinLength = "minLength";

    /// <summary>The <c>maxLength</c> keyword — the maximum string length, in code points.</summary>
    public static readonly string MaxLength = "maxLength";

    /// <summary>The <c>pattern</c> keyword — the required regular-expression match.</summary>
    public static readonly string Pattern = "pattern";

    /// <summary>The <c>minItems</c> keyword — the minimum array length.</summary>
    public static readonly string MinItems = "minItems";

    /// <summary>The <c>maxItems</c> keyword — the maximum array length.</summary>
    public static readonly string MaxItems = "maxItems";

    /// <summary>The <c>uniqueItems</c> keyword — whether array items must be distinct.</summary>
    public static readonly string UniqueItems = "uniqueItems";

    /// <summary>The <c>minProperties</c> keyword — the minimum object member count.</summary>
    public static readonly string MinProperties = "minProperties";

    /// <summary>The <c>maxProperties</c> keyword — the maximum object member count.</summary>
    public static readonly string MaxProperties = "maxProperties";

    /// <summary>The <c>required</c> keyword — the member names that must be present.</summary>
    public static readonly string Required = "required";

    /// <summary>The <c>allOf</c> keyword — every subschema must hold.</summary>
    public static readonly string AllOf = "allOf";

    /// <summary>The <c>anyOf</c> keyword — at least one subschema must hold.</summary>
    public static readonly string AnyOf = "anyOf";

    /// <summary>The <c>oneOf</c> keyword — exactly one subschema must hold.</summary>
    public static readonly string OneOf = "oneOf";

    /// <summary>The <c>not</c> keyword — the subschema must not hold.</summary>
    public static readonly string Not = "not";

    /// <summary>The <c>properties</c> keyword — per-member subschemas.</summary>
    public static readonly string Properties = "properties";

    /// <summary>The <c>additionalProperties</c> keyword — the subschema for members not covered by <c>properties</c>.</summary>
    public static readonly string AdditionalProperties = "additionalProperties";

    /// <summary>The <c>prefixItems</c> keyword — positional subschemas for the leading array elements.</summary>
    public static readonly string PrefixItems = "prefixItems";

    /// <summary>The <c>items</c> keyword — the subschema for array elements beyond <c>prefixItems</c>.</summary>
    public static readonly string Items = "items";

    /// <summary>The <c>patternProperties</c> keyword — subschemas applied to members whose name matches a pattern.</summary>
    public static readonly string PatternProperties = "patternProperties";

    /// <summary>The <c>propertyNames</c> keyword — the subschema each member name must satisfy.</summary>
    public static readonly string PropertyNames = "propertyNames";

    /// <summary>The <c>dependentRequired</c> keyword — members required when a triggering member is present.</summary>
    public static readonly string DependentRequired = "dependentRequired";

    /// <summary>The <c>dependentSchemas</c> keyword — subschemas applied when a triggering member is present.</summary>
    public static readonly string DependentSchemas = "dependentSchemas";

    /// <summary>The <c>if</c> keyword — the condition subschema that selects <c>then</c> or <c>else</c>.</summary>
    public static readonly string If = "if";

    /// <summary>The <c>then</c> keyword — applied when <c>if</c> holds.</summary>
    public static readonly string Then = "then";

    /// <summary>The <c>else</c> keyword — applied when <c>if</c> does not hold.</summary>
    public static readonly string Else = "else";

    /// <summary>The <c>contains</c> keyword — the subschema at least one array element must satisfy.</summary>
    public static readonly string Contains = "contains";

    /// <summary>The <c>minContains</c> keyword — the minimum number of <c>contains</c> matches.</summary>
    public static readonly string MinContains = "minContains";

    /// <summary>The <c>maxContains</c> keyword — the maximum number of <c>contains</c> matches.</summary>
    public static readonly string MaxContains = "maxContains";

    /// <summary>The <c>$ref</c> keyword — a reference to another schema.</summary>
    public static readonly string Ref = "$ref";

    /// <summary>The <c>$id</c> keyword — the base URI a schema establishes for its subschemas and references.</summary>
    public static readonly string Id = "$id";

    /// <summary>The <c>$anchor</c> keyword — a plain-name fragment that can be referenced.</summary>
    public static readonly string Anchor = "$anchor";

    /// <summary>The <c>$defs</c> keyword — a container for reusable subschemas (reached only through <c>$ref</c>).</summary>
    public static readonly string Defs = "$defs";

    /// <summary>The <c>unevaluatedProperties</c> keyword — the subschema for members no other keyword evaluated.</summary>
    public static readonly string UnevaluatedProperties = "unevaluatedProperties";

    /// <summary>The <c>unevaluatedItems</c> keyword — the subschema for array elements no other keyword evaluated.</summary>
    public static readonly string UnevaluatedItems = "unevaluatedItems";

    /// <summary>The <c>$dynamicAnchor</c> keyword — a fragment that can be the target of a dynamic reference.</summary>
    public static readonly string DynamicAnchor = "$dynamicAnchor";

    /// <summary>The <c>$dynamicRef</c> keyword — a reference resolved against the dynamic scope.</summary>
    public static readonly string DynamicRef = "$dynamicRef";

    /// <summary>The <c>$schema</c> keyword — the dialect (metaschema) the schema is written against.</summary>
    public static readonly string Schema = "$schema";

    /// <summary>The <c>$vocabulary</c> keyword — a metaschema's declared vocabularies and whether each is required.</summary>
    public static readonly string Vocabulary = "$vocabulary";

    /// <summary>The URI of the draft 2020-12 Validation vocabulary, whose keywords (type, minimum, …) are assertions only when it is in effect.</summary>
    public static readonly string ValidationVocabularyUri = "https://json-schema.org/draft/2020-12/vocab/validation";

    /// <summary>The URI of the draft 2020-12 standard dialect metaschema.</summary>
    public static readonly string StandardDialectUri = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>The pure-annotation keywords (Meta-Data + Format-Annotation + Content vocabularies) — they assert nothing and produce their value as an annotation.</summary>
    public static string[] AnnotationKeywords { get; } =
    [
        "title", "description", "default", "deprecated", "readOnly", "writeOnly",
        "examples", "format", "contentEncoding", "contentMediaType", "contentSchema"
    ];
}
