using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The reserved built-in functions a <see cref="BuiltInCallExpression"/> can name.
/// </summary>
/// <remarks>
/// <para>
/// This is the closed dispatch set the expression evaluator switches over. The
/// grammar's <c>BOUND</c>, <c>IF</c>, and <c>COALESCE</c> built-ins are deliberately
/// absent: they get dedicated AST nodes (<see cref="BoundExpression"/>,
/// <see cref="IfExpression"/>, <see cref="CoalesceExpression"/>) rather than a
/// <see cref="BuiltInCallExpression"/>. The lexer still interns the canonical
/// upper-case name as the token payload; the parser maps that payload to this enum
/// at the AST-construction site via <see cref="SparqlFunctions.BuiltInFromName"/>.
/// </para>
/// <para>SPARQL <c>BuiltInCall</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</para>
/// </remarks>
public enum BuiltInFunction
{
    /// <summary>The <c>STR</c> built-in.</summary>
    Str,

    /// <summary>The <c>LANG</c> built-in.</summary>
    Lang,

    /// <summary>The <c>LANGDIR</c> built-in.</summary>
    LangDir,

    /// <summary>The <c>LANGMATCHES</c> built-in.</summary>
    LangMatches,

    /// <summary>The <c>DATATYPE</c> built-in.</summary>
    Datatype,

    /// <summary>The <c>IRI</c> built-in.</summary>
    Iri,

    /// <summary>The <c>URI</c> built-in (a synonym for <see cref="Iri"/>).</summary>
    Uri,

    /// <summary>The <c>BNODE</c> built-in.</summary>
    BNode,

    /// <summary>The <c>RAND</c> built-in.</summary>
    Rand,

    /// <summary>The <c>ABS</c> built-in.</summary>
    Abs,

    /// <summary>The <c>CEIL</c> built-in.</summary>
    Ceil,

    /// <summary>The <c>FLOOR</c> built-in.</summary>
    Floor,

    /// <summary>The <c>ROUND</c> built-in.</summary>
    Round,

    /// <summary>The <c>CONCAT</c> built-in.</summary>
    Concat,

    /// <summary>The <c>STRLEN</c> built-in.</summary>
    StrLen,

    /// <summary>The <c>UCASE</c> built-in.</summary>
    UCase,

    /// <summary>The <c>LCASE</c> built-in.</summary>
    LCase,

    /// <summary>The <c>ENCODE_FOR_URI</c> built-in.</summary>
    EncodeForUri,

    /// <summary>The <c>CONTAINS</c> built-in.</summary>
    Contains,

    /// <summary>The <c>STRSTARTS</c> built-in.</summary>
    StrStarts,

    /// <summary>The <c>STRENDS</c> built-in.</summary>
    StrEnds,

    /// <summary>The <c>STRBEFORE</c> built-in.</summary>
    StrBefore,

    /// <summary>The <c>STRAFTER</c> built-in.</summary>
    StrAfter,

    /// <summary>The <c>YEAR</c> built-in.</summary>
    Year,

    /// <summary>The <c>MONTH</c> built-in.</summary>
    Month,

    /// <summary>The <c>DAY</c> built-in.</summary>
    Day,

    /// <summary>The <c>HOURS</c> built-in.</summary>
    Hours,

    /// <summary>The <c>MINUTES</c> built-in.</summary>
    Minutes,

    /// <summary>The <c>SECONDS</c> built-in.</summary>
    Seconds,

    /// <summary>The <c>TIMEZONE</c> built-in.</summary>
    Timezone,

    /// <summary>The <c>TZ</c> built-in.</summary>
    Tz,

    /// <summary>The <c>NOW</c> built-in.</summary>
    Now,

    /// <summary>The <c>UUID</c> built-in.</summary>
    Uuid,

    /// <summary>The <c>STRUUID</c> built-in.</summary>
    StrUuid,

    /// <summary>The <c>MD5</c> built-in.</summary>
    Md5,

    /// <summary>The <c>SHA1</c> built-in.</summary>
    Sha1,

    /// <summary>The <c>SHA256</c> built-in.</summary>
    Sha256,

    /// <summary>The <c>SHA384</c> built-in.</summary>
    Sha384,

    /// <summary>The <c>SHA512</c> built-in.</summary>
    Sha512,

    /// <summary>The <c>STRLANG</c> built-in.</summary>
    StrLang,

    /// <summary>The <c>STRLANGDIR</c> built-in.</summary>
    StrLangDir,

    /// <summary>The <c>STRDT</c> built-in.</summary>
    StrDt,

    /// <summary>The <c>SAMETERM</c> built-in.</summary>
    SameTerm,

    /// <summary>The <c>SAMEVALUE</c> built-in.</summary>
    SameValue,

    /// <summary>The <c>ISIRI</c> built-in.</summary>
    IsIri,

    /// <summary>The <c>ISURI</c> built-in.</summary>
    IsUri,

    /// <summary>The <c>ISBLANK</c> built-in.</summary>
    IsBlank,

    /// <summary>The <c>ISLITERAL</c> built-in.</summary>
    IsLiteral,

    /// <summary>The <c>ISNUMERIC</c> built-in.</summary>
    IsNumeric,

    /// <summary>The <c>ISTRIPLE</c> built-in.</summary>
    IsTriple,

    /// <summary>The <c>HASLANG</c> built-in.</summary>
    HasLang,

    /// <summary>The <c>HASLANGDIR</c> built-in.</summary>
    HasLangDir,

    /// <summary>The <c>SUBSTR</c> built-in.</summary>
    Substr,

    /// <summary>The <c>REPLACE</c> built-in.</summary>
    Replace,

    /// <summary>The <c>REGEX</c> built-in.</summary>
    Regex,

    /// <summary>The <c>TRIPLE</c> built-in.</summary>
    Triple,

    /// <summary>The <c>SUBJECT</c> built-in.</summary>
    Subject,

    /// <summary>The <c>PREDICATE</c> built-in.</summary>
    Predicate,

    /// <summary>The <c>OBJECT</c> built-in.</summary>
    Object
}

/// <summary>
/// The set-function (aggregate) operators an <see cref="AggregateExpression"/> can name.
/// </summary>
/// <remarks>
/// <para>
/// As with <see cref="BuiltInFunction"/>, the lexer interns the canonical upper-case
/// name and the parser maps it to this enum via
/// <see cref="SparqlFunctions.AggregateFromName"/>.
/// </para>
/// <para>SPARQL <c>Aggregate</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</para>
/// </remarks>
public enum AggregateFunction
{
    /// <summary>The <c>COUNT</c> aggregate (including the <c>COUNT(*)</c> form).</summary>
    Count,

    /// <summary>The <c>SUM</c> aggregate.</summary>
    Sum,

    /// <summary>The <c>MIN</c> aggregate.</summary>
    Min,

    /// <summary>The <c>MAX</c> aggregate.</summary>
    Max,

    /// <summary>The <c>AVG</c> aggregate.</summary>
    Avg,

    /// <summary>The <c>SAMPLE</c> aggregate.</summary>
    Sample,

    /// <summary>The <c>GROUP_CONCAT</c> aggregate.</summary>
    GroupConcat
}

/// <summary>
/// Maps between the lexer's canonical upper-case function-name payloads and the
/// <see cref="BuiltInFunction"/> / <see cref="AggregateFunction"/> AST identities.
/// This is the single source of truth for that correspondence; the
/// <c>To…Name</c> methods give each enum value the deterministic lexical form used
/// by the algebra-JSON contract.
/// </summary>
public static class SparqlFunctions
{
    /// <summary>
    /// Maps a canonical built-in name (as interned by the lexer) to its
    /// <see cref="BuiltInFunction"/>.
    /// </summary>
    /// <param name="name">The canonical upper-case name.</param>
    /// <returns>The matching <see cref="BuiltInFunction"/>.</returns>
    /// <exception cref="ArgumentException">The name is not a <see cref="BuiltInCallExpression"/> function (for example <c>BOUND</c>, <c>IF</c>, or <c>COALESCE</c>, which have dedicated nodes, or an unrecognised name).</exception>
    public static BuiltInFunction BuiltInFromName(Utf8String name)
    {
        ReadOnlySpan<byte> span = name.Span;

        if(span.SequenceEqual("STR"u8)) { return BuiltInFunction.Str; }
        if(span.SequenceEqual("LANG"u8)) { return BuiltInFunction.Lang; }
        if(span.SequenceEqual("LANGDIR"u8)) { return BuiltInFunction.LangDir; }
        if(span.SequenceEqual("LANGMATCHES"u8)) { return BuiltInFunction.LangMatches; }
        if(span.SequenceEqual("DATATYPE"u8)) { return BuiltInFunction.Datatype; }
        if(span.SequenceEqual("IRI"u8)) { return BuiltInFunction.Iri; }
        if(span.SequenceEqual("URI"u8)) { return BuiltInFunction.Uri; }
        if(span.SequenceEqual("BNODE"u8)) { return BuiltInFunction.BNode; }
        if(span.SequenceEqual("RAND"u8)) { return BuiltInFunction.Rand; }
        if(span.SequenceEqual("ABS"u8)) { return BuiltInFunction.Abs; }
        if(span.SequenceEqual("CEIL"u8)) { return BuiltInFunction.Ceil; }
        if(span.SequenceEqual("FLOOR"u8)) { return BuiltInFunction.Floor; }
        if(span.SequenceEqual("ROUND"u8)) { return BuiltInFunction.Round; }
        if(span.SequenceEqual("CONCAT"u8)) { return BuiltInFunction.Concat; }
        if(span.SequenceEqual("STRLEN"u8)) { return BuiltInFunction.StrLen; }
        if(span.SequenceEqual("UCASE"u8)) { return BuiltInFunction.UCase; }
        if(span.SequenceEqual("LCASE"u8)) { return BuiltInFunction.LCase; }
        if(span.SequenceEqual("ENCODE_FOR_URI"u8)) { return BuiltInFunction.EncodeForUri; }
        if(span.SequenceEqual("CONTAINS"u8)) { return BuiltInFunction.Contains; }
        if(span.SequenceEqual("STRSTARTS"u8)) { return BuiltInFunction.StrStarts; }
        if(span.SequenceEqual("STRENDS"u8)) { return BuiltInFunction.StrEnds; }
        if(span.SequenceEqual("STRBEFORE"u8)) { return BuiltInFunction.StrBefore; }
        if(span.SequenceEqual("STRAFTER"u8)) { return BuiltInFunction.StrAfter; }
        if(span.SequenceEqual("YEAR"u8)) { return BuiltInFunction.Year; }
        if(span.SequenceEqual("MONTH"u8)) { return BuiltInFunction.Month; }
        if(span.SequenceEqual("DAY"u8)) { return BuiltInFunction.Day; }
        if(span.SequenceEqual("HOURS"u8)) { return BuiltInFunction.Hours; }
        if(span.SequenceEqual("MINUTES"u8)) { return BuiltInFunction.Minutes; }
        if(span.SequenceEqual("SECONDS"u8)) { return BuiltInFunction.Seconds; }
        if(span.SequenceEqual("TIMEZONE"u8)) { return BuiltInFunction.Timezone; }
        if(span.SequenceEqual("TZ"u8)) { return BuiltInFunction.Tz; }
        if(span.SequenceEqual("NOW"u8)) { return BuiltInFunction.Now; }
        if(span.SequenceEqual("UUID"u8)) { return BuiltInFunction.Uuid; }
        if(span.SequenceEqual("STRUUID"u8)) { return BuiltInFunction.StrUuid; }
        if(span.SequenceEqual("MD5"u8)) { return BuiltInFunction.Md5; }
        if(span.SequenceEqual("SHA1"u8)) { return BuiltInFunction.Sha1; }
        if(span.SequenceEqual("SHA256"u8)) { return BuiltInFunction.Sha256; }
        if(span.SequenceEqual("SHA384"u8)) { return BuiltInFunction.Sha384; }
        if(span.SequenceEqual("SHA512"u8)) { return BuiltInFunction.Sha512; }
        if(span.SequenceEqual("STRLANG"u8)) { return BuiltInFunction.StrLang; }
        if(span.SequenceEqual("STRLANGDIR"u8)) { return BuiltInFunction.StrLangDir; }
        if(span.SequenceEqual("STRDT"u8)) { return BuiltInFunction.StrDt; }
        if(span.SequenceEqual("SAMETERM"u8)) { return BuiltInFunction.SameTerm; }
        if(span.SequenceEqual("SAMEVALUE"u8)) { return BuiltInFunction.SameValue; }
        if(span.SequenceEqual("ISIRI"u8)) { return BuiltInFunction.IsIri; }
        if(span.SequenceEqual("ISURI"u8)) { return BuiltInFunction.IsUri; }
        if(span.SequenceEqual("ISBLANK"u8)) { return BuiltInFunction.IsBlank; }
        if(span.SequenceEqual("ISLITERAL"u8)) { return BuiltInFunction.IsLiteral; }
        if(span.SequenceEqual("ISNUMERIC"u8)) { return BuiltInFunction.IsNumeric; }
        if(span.SequenceEqual("ISTRIPLE"u8)) { return BuiltInFunction.IsTriple; }
        if(span.SequenceEqual("HASLANG"u8)) { return BuiltInFunction.HasLang; }
        if(span.SequenceEqual("HASLANGDIR"u8)) { return BuiltInFunction.HasLangDir; }
        if(span.SequenceEqual("SUBSTR"u8)) { return BuiltInFunction.Substr; }
        if(span.SequenceEqual("REPLACE"u8)) { return BuiltInFunction.Replace; }
        if(span.SequenceEqual("REGEX"u8)) { return BuiltInFunction.Regex; }
        if(span.SequenceEqual("TRIPLE"u8)) { return BuiltInFunction.Triple; }
        if(span.SequenceEqual("SUBJECT"u8)) { return BuiltInFunction.Subject; }
        if(span.SequenceEqual("PREDICATE"u8)) { return BuiltInFunction.Predicate; }
        if(span.SequenceEqual("OBJECT"u8)) { return BuiltInFunction.Object; }

        throw new ArgumentException($"'{name}' is not a BuiltInCallExpression function.", nameof(name));
    }

    /// <summary>
    /// Maps a canonical aggregate name (as interned by the lexer) to its
    /// <see cref="AggregateFunction"/>.
    /// </summary>
    /// <param name="name">The canonical upper-case name.</param>
    /// <returns>The matching <see cref="AggregateFunction"/>.</returns>
    /// <exception cref="ArgumentException">The name is not a recognised aggregate.</exception>
    public static AggregateFunction AggregateFromName(Utf8String name)
    {
        ReadOnlySpan<byte> span = name.Span;

        if(span.SequenceEqual("COUNT"u8)) { return AggregateFunction.Count; }
        if(span.SequenceEqual("SUM"u8)) { return AggregateFunction.Sum; }
        if(span.SequenceEqual("MIN"u8)) { return AggregateFunction.Min; }
        if(span.SequenceEqual("MAX"u8)) { return AggregateFunction.Max; }
        if(span.SequenceEqual("AVG"u8)) { return AggregateFunction.Avg; }
        if(span.SequenceEqual("SAMPLE"u8)) { return AggregateFunction.Sample; }
        if(span.SequenceEqual("GROUP_CONCAT"u8)) { return AggregateFunction.GroupConcat; }

        throw new ArgumentException($"'{name}' is not an aggregate function.", nameof(name));
    }

    /// <summary>Returns the canonical upper-case SPARQL name for a <see cref="BuiltInFunction"/>.</summary>
    /// <param name="function">The built-in function.</param>
    /// <returns>The canonical name (for example <c>ENCODE_FOR_URI</c>).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="function"/> is not a defined value.</exception>
    public static string ToCanonicalName(BuiltInFunction function)
    {
        return function switch
        {
            BuiltInFunction.Str => "STR",
            BuiltInFunction.Lang => "LANG",
            BuiltInFunction.LangDir => "LANGDIR",
            BuiltInFunction.LangMatches => "LANGMATCHES",
            BuiltInFunction.Datatype => "DATATYPE",
            BuiltInFunction.Iri => "IRI",
            BuiltInFunction.Uri => "URI",
            BuiltInFunction.BNode => "BNODE",
            BuiltInFunction.Rand => "RAND",
            BuiltInFunction.Abs => "ABS",
            BuiltInFunction.Ceil => "CEIL",
            BuiltInFunction.Floor => "FLOOR",
            BuiltInFunction.Round => "ROUND",
            BuiltInFunction.Concat => "CONCAT",
            BuiltInFunction.StrLen => "STRLEN",
            BuiltInFunction.UCase => "UCASE",
            BuiltInFunction.LCase => "LCASE",
            BuiltInFunction.EncodeForUri => "ENCODE_FOR_URI",
            BuiltInFunction.Contains => "CONTAINS",
            BuiltInFunction.StrStarts => "STRSTARTS",
            BuiltInFunction.StrEnds => "STRENDS",
            BuiltInFunction.StrBefore => "STRBEFORE",
            BuiltInFunction.StrAfter => "STRAFTER",
            BuiltInFunction.Year => "YEAR",
            BuiltInFunction.Month => "MONTH",
            BuiltInFunction.Day => "DAY",
            BuiltInFunction.Hours => "HOURS",
            BuiltInFunction.Minutes => "MINUTES",
            BuiltInFunction.Seconds => "SECONDS",
            BuiltInFunction.Timezone => "TIMEZONE",
            BuiltInFunction.Tz => "TZ",
            BuiltInFunction.Now => "NOW",
            BuiltInFunction.Uuid => "UUID",
            BuiltInFunction.StrUuid => "STRUUID",
            BuiltInFunction.Md5 => "MD5",
            BuiltInFunction.Sha1 => "SHA1",
            BuiltInFunction.Sha256 => "SHA256",
            BuiltInFunction.Sha384 => "SHA384",
            BuiltInFunction.Sha512 => "SHA512",
            BuiltInFunction.StrLang => "STRLANG",
            BuiltInFunction.StrLangDir => "STRLANGDIR",
            BuiltInFunction.StrDt => "STRDT",
            BuiltInFunction.SameTerm => "SAMETERM",
            BuiltInFunction.SameValue => "SAMEVALUE",
            BuiltInFunction.IsIri => "ISIRI",
            BuiltInFunction.IsUri => "ISURI",
            BuiltInFunction.IsBlank => "ISBLANK",
            BuiltInFunction.IsLiteral => "ISLITERAL",
            BuiltInFunction.IsNumeric => "ISNUMERIC",
            BuiltInFunction.IsTriple => "ISTRIPLE",
            BuiltInFunction.HasLang => "HASLANG",
            BuiltInFunction.HasLangDir => "HASLANGDIR",
            BuiltInFunction.Substr => "SUBSTR",
            BuiltInFunction.Replace => "REPLACE",
            BuiltInFunction.Regex => "REGEX",
            BuiltInFunction.Triple => "TRIPLE",
            BuiltInFunction.Subject => "SUBJECT",
            BuiltInFunction.Predicate => "PREDICATE",
            BuiltInFunction.Object => "OBJECT",
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown built-in function.")
        };
    }

    /// <summary>Returns the canonical upper-case SPARQL name for an <see cref="AggregateFunction"/>.</summary>
    /// <param name="function">The aggregate function.</param>
    /// <returns>The canonical name (for example <c>GROUP_CONCAT</c>).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="function"/> is not a defined value.</exception>
    public static string ToCanonicalName(AggregateFunction function)
    {
        return function switch
        {
            AggregateFunction.Count => "COUNT",
            AggregateFunction.Sum => "SUM",
            AggregateFunction.Min => "MIN",
            AggregateFunction.Max => "MAX",
            AggregateFunction.Avg => "AVG",
            AggregateFunction.Sample => "SAMPLE",
            AggregateFunction.GroupConcat => "GROUP_CONCAT",
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown aggregate function.")
        };
    }
}
