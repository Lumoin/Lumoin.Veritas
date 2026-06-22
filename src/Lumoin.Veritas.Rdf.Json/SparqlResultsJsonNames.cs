using System;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// The well-known property names and value tokens of the
/// <see href="https://www.w3.org/TR/sparql11-results-json/">SPARQL Query Results JSON Format</see> (<c>.srj</c>) —
/// the single home for these strings, shared by <see cref="SparqlResultsJsonReader"/> (which compares them as
/// <see cref="string"/> through <c>System.Text.Json</c>) and <see cref="SparqlResultsJsonWriter"/> (which emits the
/// value tokens as UTF-8 through the AOT-friendly <c>Utf8JsonWriter</c>). The two encodings of a token live together
/// here so neither serializer re-types the literal.
/// </summary>
internal static class SparqlResultsJsonNames
{
    /// <summary>The <c>head</c> object holding the result's variable declarations.</summary>
    public const string Head = "head";

    /// <summary>The <c>head.vars</c> array of declared variable names.</summary>
    public const string Vars = "vars";

    /// <summary>The <c>results</c> object wrapping a SELECT result's bindings.</summary>
    public const string Results = "results";

    /// <summary>The <c>results.bindings</c> array of binding-set objects.</summary>
    public const string Bindings = "bindings";

    /// <summary>The <c>boolean</c> property of an ASK result.</summary>
    public const string Boolean = "boolean";

    /// <summary>The <c>type</c> discriminator of a binding value object.</summary>
    public const string Type = "type";

    /// <summary>The <c>value</c> property of a binding value object (the lexical form, or a triple's component object).</summary>
    public const string Value = "value";

    /// <summary>The <c>datatype</c> property of a typed literal.</summary>
    public const string Datatype = "datatype";

    /// <summary>The <c>xml:lang</c> property of a language-tagged literal.</summary>
    public const string Language = "xml:lang";

    /// <summary>The <c>its:dir</c> property of a directional language-tagged literal (RDF 1.2).</summary>
    public const string Direction = "its:dir";

    /// <summary>The <c>subject</c> component of a triple value.</summary>
    public const string Subject = "subject";

    /// <summary>The <c>predicate</c> component of a triple value.</summary>
    public const string Predicate = "predicate";

    /// <summary>The <c>object</c> component of a triple value.</summary>
    public const string Object = "object";

    /// <summary>The <c>uri</c> value of the <c>type</c> discriminator (an IRI term).</summary>
    public const string Uri = "uri";

    /// <summary>The <c>bnode</c> value of the <c>type</c> discriminator (a blank node).</summary>
    public const string Bnode = "bnode";

    /// <summary>The <c>literal</c> value of the <c>type</c> discriminator.</summary>
    public const string Literal = "literal";

    /// <summary>The legacy <c>typed-literal</c> value of the <c>type</c> discriminator (read-only; accepted on input).</summary>
    public const string TypedLiteral = "typed-literal";

    /// <summary>The <c>triple</c> value of the <c>type</c> discriminator (an RDF 1.2 triple term).</summary>
    public const string Triple = "triple";

    /// <summary>The <c>type</c> property name as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> TypeUtf8 => "type"u8;

    /// <summary>The <c>value</c> property name as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> ValueUtf8 => "value"u8;

    /// <summary>The <c>datatype</c> property name as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> DatatypeUtf8 => "datatype"u8;

    /// <summary>The <c>xml:lang</c> property name as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> LanguageUtf8 => "xml:lang"u8;

    /// <summary>The <c>its:dir</c> property name as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> DirectionUtf8 => "its:dir"u8;

    /// <summary>The <c>triple</c> type-value as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> TripleUtf8 => "triple"u8;

    /// <summary>The <c>uri</c> type-value as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> UriUtf8 => "uri"u8;

    /// <summary>The <c>bnode</c> type-value as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> BnodeUtf8 => "bnode"u8;

    /// <summary>The <c>literal</c> type-value as UTF-8, for the writer.</summary>
    public static ReadOnlySpan<byte> LiteralUtf8 => "literal"u8;

    /// <summary>The legacy <c>typed-literal</c> type-value as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> TypedLiteralUtf8 => "typed-literal"u8;

    /// <summary>The <c>head</c> property name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> HeadUtf8 => "head"u8;

    /// <summary>The <c>vars</c> property name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> VarsUtf8 => "vars"u8;

    /// <summary>The <c>results</c> property name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> ResultsUtf8 => "results"u8;

    /// <summary>The <c>bindings</c> property name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> BindingsUtf8 => "bindings"u8;

    /// <summary>The <c>boolean</c> property name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> BooleanUtf8 => "boolean"u8;

    /// <summary>The <c>subject</c> component name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> SubjectUtf8 => "subject"u8;

    /// <summary>The <c>predicate</c> component name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> PredicateUtf8 => "predicate"u8;

    /// <summary>The <c>object</c> component name as UTF-8, for the byte-native reader.</summary>
    public static ReadOnlySpan<byte> ObjectUtf8 => "object"u8;
}
