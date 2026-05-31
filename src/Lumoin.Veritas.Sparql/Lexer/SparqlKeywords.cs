using System;

namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// Classifies a scanned identifier as a SPARQL reserved word — a structural
/// keyword, a built-in or aggregate function name, the <c>a</c> shorthand, or a
/// boolean literal — or reports that it is none of these (so the lexer treats it
/// as the prefix part of a prefixed name or as an unrecognised identifier).
/// </summary>
/// <remarks>
/// <para>
/// SPARQL keywords and function names are case-insensitive; <c>a</c>, <c>true</c>,
/// and <c>false</c> are case-sensitive. Matching is allocation-free: the lexer
/// copies the candidate (only when it is no longer than
/// <see cref="MaxReservedWordLength"/>) into a stack buffer and the comparisons
/// run over spans. The returned <paramref name="canonical"/> is a static UTF-8
/// literal the lexer interns as the token payload, so a query written in any
/// casing yields one canonical handle.
/// </para>
/// </remarks>
internal static class SparqlKeywords
{
    /// <summary>
    /// The byte length of the longest reserved word (<c>ENCODE_FOR_URI</c>). An
    /// identifier longer than this cannot be a reserved word, so the lexer skips
    /// classification for it.
    /// </summary>
    public const int MaxReservedWordLength = 14;

    /// <summary>
    /// Classifies <paramref name="identifier"/> as a reserved word.
    /// </summary>
    /// <param name="identifier">The scanned identifier bytes (ASCII for any reserved word).</param>
    /// <param name="kind">The token kind when the method returns <c>true</c>; otherwise undefined.</param>
    /// <param name="canonical">The canonical UTF-8 payload to intern when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns><c>true</c> when the identifier is a reserved word.</returns>
    public static bool TryClassify(ReadOnlySpan<byte> identifier, out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical)
    {
        //The predicate shorthand `a` is case-sensitive (lower-case only). The boolean literals are case-INSENSITIVE
        //SPARQL keywords (the W3C suite asserts TRUE/False/etc. are valid and canonicalize to lower-case "true"/
        //"false"); match them ignoring case and intern the canonical lower-case payload.
        if(identifier.SequenceEqual("a"u8))
        {
            return Yield(SparqlTokenKind.A, "a"u8, out kind, out canonical);
        }

        if(Ci(identifier, "true"u8))
        {
            return Yield(SparqlTokenKind.BooleanLiteral, "true"u8, out kind, out canonical);
        }

        if(Ci(identifier, "false"u8))
        {
            return Yield(SparqlTokenKind.BooleanLiteral, "false"u8, out kind, out canonical);
        }

        return TryClassifyKeyword(identifier, out kind, out canonical)
            || TryClassifyFunction(identifier, out kind, out canonical)
            || TryClassifyAggregate(identifier, out kind, out canonical)
            || Fail(out kind, out canonical);
    }

    /// <summary>
    /// Matches the case-insensitive structural keywords (clause and modifier words).
    /// </summary>
    /// <param name="id">The candidate identifier.</param>
    /// <param name="kind">The matched keyword kind, on success.</param>
    /// <param name="canonical">The canonical payload, on success.</param>
    /// <returns><c>true</c> on a match.</returns>
    private static bool TryClassifyKeyword(ReadOnlySpan<byte> id, out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical)
    {
        if(Ci(id, "BASE"u8)) { return Yield(SparqlTokenKind.BaseKeyword, "BASE"u8, out kind, out canonical); }
        if(Ci(id, "PREFIX"u8)) { return Yield(SparqlTokenKind.PrefixKeyword, "PREFIX"u8, out kind, out canonical); }
        if(Ci(id, "VERSION"u8)) { return Yield(SparqlTokenKind.VersionKeyword, "VERSION"u8, out kind, out canonical); }
        if(Ci(id, "SELECT"u8)) { return Yield(SparqlTokenKind.SelectKeyword, "SELECT"u8, out kind, out canonical); }
        if(Ci(id, "CONSTRUCT"u8)) { return Yield(SparqlTokenKind.ConstructKeyword, "CONSTRUCT"u8, out kind, out canonical); }
        if(Ci(id, "ASK"u8)) { return Yield(SparqlTokenKind.AskKeyword, "ASK"u8, out kind, out canonical); }
        if(Ci(id, "DESCRIBE"u8)) { return Yield(SparqlTokenKind.DescribeKeyword, "DESCRIBE"u8, out kind, out canonical); }
        if(Ci(id, "WHERE"u8)) { return Yield(SparqlTokenKind.WhereKeyword, "WHERE"u8, out kind, out canonical); }
        if(Ci(id, "FROM"u8)) { return Yield(SparqlTokenKind.FromKeyword, "FROM"u8, out kind, out canonical); }
        if(Ci(id, "NAMED"u8)) { return Yield(SparqlTokenKind.NamedKeyword, "NAMED"u8, out kind, out canonical); }
        if(Ci(id, "ORDER"u8)) { return Yield(SparqlTokenKind.OrderKeyword, "ORDER"u8, out kind, out canonical); }
        if(Ci(id, "BY"u8)) { return Yield(SparqlTokenKind.ByKeyword, "BY"u8, out kind, out canonical); }
        if(Ci(id, "LIMIT"u8)) { return Yield(SparqlTokenKind.LimitKeyword, "LIMIT"u8, out kind, out canonical); }
        if(Ci(id, "OFFSET"u8)) { return Yield(SparqlTokenKind.OffsetKeyword, "OFFSET"u8, out kind, out canonical); }
        if(Ci(id, "DISTINCT"u8)) { return Yield(SparqlTokenKind.DistinctKeyword, "DISTINCT"u8, out kind, out canonical); }
        if(Ci(id, "REDUCED"u8)) { return Yield(SparqlTokenKind.ReducedKeyword, "REDUCED"u8, out kind, out canonical); }
        if(Ci(id, "OPTIONAL"u8)) { return Yield(SparqlTokenKind.OptionalKeyword, "OPTIONAL"u8, out kind, out canonical); }
        if(Ci(id, "UNION"u8)) { return Yield(SparqlTokenKind.UnionKeyword, "UNION"u8, out kind, out canonical); }
        if(Ci(id, "MINUS"u8)) { return Yield(SparqlTokenKind.MinusKeyword, "MINUS"u8, out kind, out canonical); }
        if(Ci(id, "FILTER"u8)) { return Yield(SparqlTokenKind.FilterKeyword, "FILTER"u8, out kind, out canonical); }
        if(Ci(id, "BIND"u8)) { return Yield(SparqlTokenKind.BindKeyword, "BIND"u8, out kind, out canonical); }
        if(Ci(id, "AS"u8)) { return Yield(SparqlTokenKind.AsKeyword, "AS"u8, out kind, out canonical); }
        if(Ci(id, "VALUES"u8)) { return Yield(SparqlTokenKind.ValuesKeyword, "VALUES"u8, out kind, out canonical); }
        if(Ci(id, "UNDEF"u8)) { return Yield(SparqlTokenKind.UndefKeyword, "UNDEF"u8, out kind, out canonical); }
        if(Ci(id, "GROUP"u8)) { return Yield(SparqlTokenKind.GroupKeyword, "GROUP"u8, out kind, out canonical); }
        if(Ci(id, "HAVING"u8)) { return Yield(SparqlTokenKind.HavingKeyword, "HAVING"u8, out kind, out canonical); }
        if(Ci(id, "GRAPH"u8)) { return Yield(SparqlTokenKind.GraphKeyword, "GRAPH"u8, out kind, out canonical); }
        if(Ci(id, "SERVICE"u8)) { return Yield(SparqlTokenKind.ServiceKeyword, "SERVICE"u8, out kind, out canonical); }
        if(Ci(id, "SILENT"u8)) { return Yield(SparqlTokenKind.SilentKeyword, "SILENT"u8, out kind, out canonical); }
        if(Ci(id, "IN"u8)) { return Yield(SparqlTokenKind.InKeyword, "IN"u8, out kind, out canonical); }
        if(Ci(id, "NOT"u8)) { return Yield(SparqlTokenKind.NotKeyword, "NOT"u8, out kind, out canonical); }
        if(Ci(id, "EXISTS"u8)) { return Yield(SparqlTokenKind.ExistsKeyword, "EXISTS"u8, out kind, out canonical); }
        if(Ci(id, "ASC"u8)) { return Yield(SparqlTokenKind.AscKeyword, "ASC"u8, out kind, out canonical); }
        if(Ci(id, "DESC"u8)) { return Yield(SparqlTokenKind.DescKeyword, "DESC"u8, out kind, out canonical); }
        if(Ci(id, "SEPARATOR"u8)) { return Yield(SparqlTokenKind.SeparatorKeyword, "SEPARATOR"u8, out kind, out canonical); }
        if(Ci(id, "INSERT"u8)) { return Yield(SparqlTokenKind.InsertKeyword, "INSERT"u8, out kind, out canonical); }
        if(Ci(id, "DELETE"u8)) { return Yield(SparqlTokenKind.DeleteKeyword, "DELETE"u8, out kind, out canonical); }
        if(Ci(id, "DATA"u8)) { return Yield(SparqlTokenKind.DataKeyword, "DATA"u8, out kind, out canonical); }
        if(Ci(id, "LOAD"u8)) { return Yield(SparqlTokenKind.LoadKeyword, "LOAD"u8, out kind, out canonical); }
        if(Ci(id, "CLEAR"u8)) { return Yield(SparqlTokenKind.ClearKeyword, "CLEAR"u8, out kind, out canonical); }
        if(Ci(id, "DROP"u8)) { return Yield(SparqlTokenKind.DropKeyword, "DROP"u8, out kind, out canonical); }
        if(Ci(id, "CREATE"u8)) { return Yield(SparqlTokenKind.CreateKeyword, "CREATE"u8, out kind, out canonical); }
        if(Ci(id, "ADD"u8)) { return Yield(SparqlTokenKind.AddKeyword, "ADD"u8, out kind, out canonical); }
        if(Ci(id, "MOVE"u8)) { return Yield(SparqlTokenKind.MoveKeyword, "MOVE"u8, out kind, out canonical); }
        if(Ci(id, "COPY"u8)) { return Yield(SparqlTokenKind.CopyKeyword, "COPY"u8, out kind, out canonical); }
        if(Ci(id, "INTO"u8)) { return Yield(SparqlTokenKind.IntoKeyword, "INTO"u8, out kind, out canonical); }
        if(Ci(id, "TO"u8)) { return Yield(SparqlTokenKind.ToKeyword, "TO"u8, out kind, out canonical); }
        if(Ci(id, "WITH"u8)) { return Yield(SparqlTokenKind.WithKeyword, "WITH"u8, out kind, out canonical); }
        if(Ci(id, "USING"u8)) { return Yield(SparqlTokenKind.UsingKeyword, "USING"u8, out kind, out canonical); }
        if(Ci(id, "DEFAULT"u8)) { return Yield(SparqlTokenKind.DefaultKeyword, "DEFAULT"u8, out kind, out canonical); }
        if(Ci(id, "ALL"u8)) { return Yield(SparqlTokenKind.AllKeyword, "ALL"u8, out kind, out canonical); }

        return Fail(out kind, out canonical);
    }

    /// <summary>
    /// Matches the case-insensitive built-in function names. <c>BOUND</c>,
    /// <c>IF</c>, and <c>COALESCE</c> are classified here (called like functions)
    /// rather than as standalone keywords; <c>EXISTS</c>, <c>NOT</c>, and
    /// <c>IN</c> remain distinct keyword kinds because of their special syntax.
    /// </summary>
    /// <param name="id">The candidate identifier.</param>
    /// <param name="kind">Always <see cref="SparqlTokenKind.BuiltInFunctionName"/> on success.</param>
    /// <param name="canonical">The canonical upper-case name, on success.</param>
    /// <returns><c>true</c> on a match.</returns>
    private static bool TryClassifyFunction(ReadOnlySpan<byte> id, out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical)
    {
        ReadOnlySpan<byte> match = MatchFunctionName(id);
        if(match.IsEmpty)
        {
            return Fail(out kind, out canonical);
        }

        return Yield(SparqlTokenKind.BuiltInFunctionName, match, out kind, out canonical);
    }

    /// <summary>
    /// Returns the canonical upper-case name for a built-in function identifier,
    /// or an empty span when the identifier names no built-in.
    /// </summary>
    /// <param name="id">The candidate identifier.</param>
    /// <returns>The canonical name, or empty.</returns>
    private static ReadOnlySpan<byte> MatchFunctionName(ReadOnlySpan<byte> id)
    {
        if(Ci(id, "STR"u8)) { return "STR"u8; }
        if(Ci(id, "LANG"u8)) { return "LANG"u8; }
        if(Ci(id, "LANGDIR"u8)) { return "LANGDIR"u8; }
        if(Ci(id, "LANGMATCHES"u8)) { return "LANGMATCHES"u8; }
        if(Ci(id, "DATATYPE"u8)) { return "DATATYPE"u8; }
        if(Ci(id, "BOUND"u8)) { return "BOUND"u8; }
        if(Ci(id, "IRI"u8)) { return "IRI"u8; }
        if(Ci(id, "URI"u8)) { return "URI"u8; }
        if(Ci(id, "BNODE"u8)) { return "BNODE"u8; }
        if(Ci(id, "RAND"u8)) { return "RAND"u8; }
        if(Ci(id, "ABS"u8)) { return "ABS"u8; }
        if(Ci(id, "CEIL"u8)) { return "CEIL"u8; }
        if(Ci(id, "FLOOR"u8)) { return "FLOOR"u8; }
        if(Ci(id, "ROUND"u8)) { return "ROUND"u8; }
        if(Ci(id, "CONCAT"u8)) { return "CONCAT"u8; }
        if(Ci(id, "STRLEN"u8)) { return "STRLEN"u8; }
        if(Ci(id, "UCASE"u8)) { return "UCASE"u8; }
        if(Ci(id, "LCASE"u8)) { return "LCASE"u8; }
        if(Ci(id, "ENCODE_FOR_URI"u8)) { return "ENCODE_FOR_URI"u8; }
        if(Ci(id, "CONTAINS"u8)) { return "CONTAINS"u8; }
        if(Ci(id, "STRSTARTS"u8)) { return "STRSTARTS"u8; }
        if(Ci(id, "STRENDS"u8)) { return "STRENDS"u8; }
        if(Ci(id, "STRBEFORE"u8)) { return "STRBEFORE"u8; }
        if(Ci(id, "STRAFTER"u8)) { return "STRAFTER"u8; }
        if(Ci(id, "YEAR"u8)) { return "YEAR"u8; }
        if(Ci(id, "MONTH"u8)) { return "MONTH"u8; }
        if(Ci(id, "DAY"u8)) { return "DAY"u8; }
        if(Ci(id, "HOURS"u8)) { return "HOURS"u8; }
        if(Ci(id, "MINUTES"u8)) { return "MINUTES"u8; }
        if(Ci(id, "SECONDS"u8)) { return "SECONDS"u8; }
        if(Ci(id, "TIMEZONE"u8)) { return "TIMEZONE"u8; }
        if(Ci(id, "TZ"u8)) { return "TZ"u8; }
        if(Ci(id, "NOW"u8)) { return "NOW"u8; }
        if(Ci(id, "UUID"u8)) { return "UUID"u8; }
        if(Ci(id, "STRUUID"u8)) { return "STRUUID"u8; }
        if(Ci(id, "MD5"u8)) { return "MD5"u8; }
        if(Ci(id, "SHA1"u8)) { return "SHA1"u8; }
        if(Ci(id, "SHA256"u8)) { return "SHA256"u8; }
        if(Ci(id, "SHA384"u8)) { return "SHA384"u8; }
        if(Ci(id, "SHA512"u8)) { return "SHA512"u8; }
        if(Ci(id, "COALESCE"u8)) { return "COALESCE"u8; }
        if(Ci(id, "IF"u8)) { return "IF"u8; }
        if(Ci(id, "STRLANG"u8)) { return "STRLANG"u8; }
        if(Ci(id, "STRLANGDIR"u8)) { return "STRLANGDIR"u8; }
        if(Ci(id, "STRDT"u8)) { return "STRDT"u8; }
        if(Ci(id, "SAMETERM"u8)) { return "SAMETERM"u8; }
        if(Ci(id, "SAMEVALUE"u8)) { return "SAMEVALUE"u8; }
        if(Ci(id, "ISIRI"u8)) { return "ISIRI"u8; }
        if(Ci(id, "ISURI"u8)) { return "ISURI"u8; }
        if(Ci(id, "ISBLANK"u8)) { return "ISBLANK"u8; }
        if(Ci(id, "ISLITERAL"u8)) { return "ISLITERAL"u8; }
        if(Ci(id, "ISNUMERIC"u8)) { return "ISNUMERIC"u8; }
        if(Ci(id, "ISTRIPLE"u8)) { return "ISTRIPLE"u8; }
        if(Ci(id, "HASLANG"u8)) { return "HASLANG"u8; }
        if(Ci(id, "HASLANGDIR"u8)) { return "HASLANGDIR"u8; }
        if(Ci(id, "SUBSTR"u8)) { return "SUBSTR"u8; }
        if(Ci(id, "REPLACE"u8)) { return "REPLACE"u8; }
        if(Ci(id, "REGEX"u8)) { return "REGEX"u8; }
        if(Ci(id, "TRIPLE"u8)) { return "TRIPLE"u8; }
        if(Ci(id, "SUBJECT"u8)) { return "SUBJECT"u8; }
        if(Ci(id, "PREDICATE"u8)) { return "PREDICATE"u8; }
        if(Ci(id, "OBJECT"u8)) { return "OBJECT"u8; }

        return ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// Matches the case-insensitive aggregate function names.
    /// </summary>
    /// <param name="id">The candidate identifier.</param>
    /// <param name="kind">Always <see cref="SparqlTokenKind.AggregateFunctionName"/> on success.</param>
    /// <param name="canonical">The canonical upper-case name, on success.</param>
    /// <returns><c>true</c> on a match.</returns>
    private static bool TryClassifyAggregate(ReadOnlySpan<byte> id, out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical)
    {
        if(Ci(id, "COUNT"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "COUNT"u8, out kind, out canonical); }
        if(Ci(id, "SUM"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "SUM"u8, out kind, out canonical); }
        if(Ci(id, "MIN"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "MIN"u8, out kind, out canonical); }
        if(Ci(id, "MAX"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "MAX"u8, out kind, out canonical); }
        if(Ci(id, "AVG"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "AVG"u8, out kind, out canonical); }
        if(Ci(id, "SAMPLE"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "SAMPLE"u8, out kind, out canonical); }
        if(Ci(id, "GROUP_CONCAT"u8)) { return Yield(SparqlTokenKind.AggregateFunctionName, "GROUP_CONCAT"u8, out kind, out canonical); }

        return Fail(out kind, out canonical);
    }

    /// <summary>
    /// Compares two byte spans for equality ignoring ASCII case.
    /// </summary>
    /// <param name="candidate">The scanned identifier.</param>
    /// <param name="reserved">The reserved word in canonical casing (ASCII).</param>
    /// <returns><c>true</c> when the spans match ignoring ASCII case.</returns>
    private static bool Ci(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> reserved)
    {
        if(candidate.Length != reserved.Length)
        {
            return false;
        }

        for(int i = 0; i < candidate.Length; i++)
        {
            if(ToAsciiLower(candidate[i]) != ToAsciiLower(reserved[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Folds an ASCII upper-case letter to lower case; other bytes pass through.
    /// </summary>
    /// <param name="b">The byte to fold.</param>
    /// <returns>The lower-cased byte.</returns>
    private static byte ToAsciiLower(byte b)
    {
        return b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b + ('a' - 'A')) : b;
    }

    /// <summary>
    /// Sets the success out-parameters and returns <c>true</c>.
    /// </summary>
    /// <param name="matchedKind">The matched kind.</param>
    /// <param name="matchedCanonical">The canonical payload.</param>
    /// <param name="kind">Receives <paramref name="matchedKind"/>.</param>
    /// <param name="canonical">Receives <paramref name="matchedCanonical"/>.</param>
    /// <returns>Always <c>true</c>.</returns>
    private static bool Yield(SparqlTokenKind matchedKind, ReadOnlySpan<byte> matchedCanonical, out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical)
    {
        kind = matchedKind;
        canonical = matchedCanonical;

        return true;
    }

    /// <summary>
    /// Sets the failure out-parameters and returns <c>false</c>.
    /// </summary>
    /// <param name="kind">Receives <see cref="SparqlTokenKind.EndOfInput"/> as an unused sentinel.</param>
    /// <param name="canonical">Receives an empty span.</param>
    /// <returns>Always <c>false</c>.</returns>
    private static bool Fail(out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical)
    {
        kind = SparqlTokenKind.EndOfInput;
        canonical = ReadOnlySpan<byte>.Empty;

        return false;
    }
}
