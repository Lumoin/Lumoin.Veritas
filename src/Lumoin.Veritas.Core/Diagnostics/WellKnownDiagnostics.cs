using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// The catalogue of stable diagnostic codes emitted by the lexer and the parsers, grouped by layer.
/// </summary>
/// <remarks>
/// <para>
/// Codes use a two-letter layer prefix plus a four-digit number, stable across versions (never
/// renumbered): <c>LX</c> lexer, <c>TT</c> Turtle parser, <c>SP</c> SPARQL parser (and, later,
/// <c>AL</c> algebra translator, <c>EX</c> executor). Editor surfaces identify the source layer from
/// the prefix and subscribe to specific codes. Each code is an interned, pool-free
/// <see cref="Utf8String"/> built once at static initialisation, mirroring <see cref="Vocabulary"/>.
/// </para>
/// <para>
/// Every parser-side emission references a code by name (no string literals at emission sites), and
/// every code carries a <c>&lt;remarks&gt;</c> note with the grammar production it relates to.
/// </para>
/// </remarks>
public static class WellKnownDiagnostics
{
    /// <summary>
    /// Lexer diagnostics (<c>LX</c> prefix), shared by the Turtle and SPARQL lexers.
    /// </summary>
    /// <remarks>
    /// One code per distinct lexical-error condition (a near 1:1 image of each lexer's internal
    /// error-code enum), so an editor surface can branch on the code alone — squiggle, quick-fix, or
    /// per-code severity — without parsing the human-readable message. The per-lexer enum-to-code
    /// mapping lives in that lexer's diagnostic-bridge helper and nowhere else. Codes are stable and
    /// never renumbered; new conditions append a higher number.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Lexer.X is the intended usage pattern.")]
    public static class Lexer
    {
        /// <summary>An IRI reference was not closed before end-of-input or a forbidden character.</summary>
        /// <remarks>Lexer rule for <c>IRIREF</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rIRIREF">SPARQL 1.2 §19.8 [IRIREF]</see>.</remarks>
        public static Utf8String UnclosedIri { get; } = new("LX0001"u8.ToArray());

        /// <summary>A string literal was not closed before end-of-input or end-of-line.</summary>
        /// <remarks>Lexer rule for <c>String</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rString">SPARQL 1.2 §19.8 [String]</see>.</remarks>
        public static Utf8String UnclosedStringLiteral { get; } = new("LX0002"u8.ToArray());

        /// <summary>An escape sequence in a string or IRI was malformed.</summary>
        /// <remarks>Lexer rules for <c>ECHAR</c> / <c>UCHAR</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rECHAR">SPARQL 1.2 §19.8 [ECHAR]</see>.</remarks>
        public static Utf8String InvalidEscape { get; } = new("LX0003"u8.ToArray());

        /// <summary>A byte that begins no valid token was encountered.</summary>
        /// <remarks>Tokenisation failure with no applicable terminal. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String UnexpectedByte { get; } = new("LX0004"u8.ToArray());

        /// <summary>A directive-introducing token (for example a leading <c>@</c>) named no known directive.</summary>
        /// <remarks>Turtle <c>directive</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [directive]</see>.</remarks>
        public static Utf8String UnknownDirective { get; } = new("LX0005"u8.ToArray());

        /// <summary>A byte not permitted inside an IRI reference appeared between the angle brackets.</summary>
        /// <remarks>Lexer rule for <c>IRIREF</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rIRIREF">SPARQL 1.2 §19.8 [IRIREF]</see>.</remarks>
        public static Utf8String InvalidIriByte { get; } = new("LX0006"u8.ToArray());

        /// <summary>A multi-byte UTF-8 sequence was cut short by end of input.</summary>
        /// <remarks>UTF-8 source encoding. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String TruncatedUtf8 { get; } = new("LX0007"u8.ToArray());

        /// <summary>A byte that cannot begin a UTF-8 sequence was encountered.</summary>
        /// <remarks>UTF-8 source encoding. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String InvalidUtf8LeadByte { get; } = new("LX0008"u8.ToArray());

        /// <summary>An escape sequence was cut short by end of input.</summary>
        /// <remarks>Lexer rules for <c>ECHAR</c> / <c>UCHAR</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rECHAR">SPARQL 1.2 §19.8 [ECHAR]</see>.</remarks>
        public static Utf8String TruncatedEscape { get; } = new("LX0009"u8.ToArray());

        /// <summary>A <c>\u</c> / <c>\U</c> escape contained a non-hexadecimal digit.</summary>
        /// <remarks>Lexer rule for <c>UCHAR</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPN_LOCAL_ESC">SPARQL 1.2 §19.8 (escapes)</see>.</remarks>
        public static Utf8String InvalidHexDigit { get; } = new("LX0010"u8.ToArray());

        /// <summary>A <c>\u</c> / <c>\U</c> escape named a UTF-16 surrogate code point, which is not a scalar value.</summary>
        /// <remarks>Lexer rule for <c>UCHAR</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rString">SPARQL 1.2 §19.8 [String]</see>.</remarks>
        public static Utf8String SurrogateCodePoint { get; } = new("LX0011"u8.ToArray());

        /// <summary>A <c>\U</c> escape named a code point beyond U+10FFFF.</summary>
        /// <remarks>Lexer rule for <c>UCHAR</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rString">SPARQL 1.2 §19.8 [String]</see>.</remarks>
        public static Utf8String CodePointOutOfRange { get; } = new("LX0012"u8.ToArray());

        /// <summary>An unescaped line break appeared inside a short string literal.</summary>
        /// <remarks>Lexer rule for <c>STRING_LITERAL1</c> / <c>STRING_LITERAL2</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rString">SPARQL 1.2 §19.8 [String]</see>.</remarks>
        public static Utf8String UnescapedLineBreak { get; } = new("LX0013"u8.ToArray());

        /// <summary>A long (triple-quoted) string literal was not closed before end of input.</summary>
        /// <remarks>Lexer rule for <c>STRING_LITERAL_LONG1</c> / <c>STRING_LITERAL_LONG2</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rString">SPARQL 1.2 §19.8 [String]</see>.</remarks>
        public static Utf8String UnterminatedLongString { get; } = new("LX0014"u8.ToArray());

        /// <summary>A <c>_</c> was not followed by the <c>:</c> that begins a blank-node label.</summary>
        /// <remarks>Lexer rule for <c>BLANK_NODE_LABEL</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBLANK_NODE_LABEL">SPARQL 1.2 §19.8 [BLANK_NODE_LABEL]</see>.</remarks>
        public static Utf8String ExpectedColonAfterUnderscore { get; } = new("LX0015"u8.ToArray());

        /// <summary>A <c>_:</c> was not followed by a valid blank-node label.</summary>
        /// <remarks>Lexer rule for <c>BLANK_NODE_LABEL</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBLANK_NODE_LABEL">SPARQL 1.2 §19.8 [BLANK_NODE_LABEL]</see>.</remarks>
        public static Utf8String ExpectedBlankNodeLabel { get; } = new("LX0016"u8.ToArray());

        /// <summary>A <c>?</c> or <c>$</c> variable marker was not followed by a valid variable name.</summary>
        /// <remarks>Lexer rule for <c>VARNAME</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVARNAME">SPARQL 1.2 §19.8 [VARNAME]</see>.</remarks>
        public static Utf8String ExpectedVariableName { get; } = new("LX0017"u8.ToArray());

        /// <summary>An <c>@</c> was not followed by a language-tag (or, in Turtle, directive) identifier.</summary>
        /// <remarks>Lexer rule for <c>LANGTAG</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rLANGTAG">SPARQL 1.2 §19.8 [LANGTAG]</see>.</remarks>
        public static Utf8String ExpectedIdentifierAfterAt { get; } = new("LX0018"u8.ToArray());

        /// <summary>A <c>--</c> direction marker was not followed by a direction tag.</summary>
        /// <remarks>RDF 1.2 base-direction language form. See <see href="https://www.w3.org/TR/sparql12-query/#rLANGTAG">SPARQL 1.2 §19.8 [LANGTAG]</see>.</remarks>
        public static Utf8String ExpectedDirectionTag { get; } = new("LX0019"u8.ToArray());

        /// <summary>A <c>-</c> in a language tag was not followed by a subtag.</summary>
        /// <remarks>Lexer rule for <c>LANGTAG</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rLANGTAG">SPARQL 1.2 §19.8 [LANGTAG]</see>.</remarks>
        public static Utf8String ExpectedLanguageSubtag { get; } = new("LX0020"u8.ToArray());

        /// <summary>An identifier resolved to no keyword, function name, boolean, or prefixed name.</summary>
        /// <remarks>Tokenisation failure with no applicable terminal. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String UnrecognisedIdentifier { get; } = new("LX0021"u8.ToArray());

        /// <summary>A reserved-character escape inside a prefixed name was cut short.</summary>
        /// <remarks>Lexer rule for <c>PN_LOCAL_ESC</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPN_LOCAL_ESC">SPARQL 1.2 §19.8 [PN_LOCAL_ESC]</see>.</remarks>
        public static Utf8String TruncatedPrefixedNameEscape { get; } = new("LX0022"u8.ToArray());

        /// <summary>A percent escape inside a prefixed name was not two hex digits.</summary>
        /// <remarks>Lexer rule for <c>PERCENT</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPERCENT">SPARQL 1.2 §19.8 [PERCENT]</see>.</remarks>
        public static Utf8String MalformedPercentEscape { get; } = new("LX0023"u8.ToArray());

        /// <summary>A digit was expected (for example after a sign or decimal point) but not found.</summary>
        /// <remarks>Lexer rules for the numeric literals. See <see href="https://www.w3.org/TR/sparql12-query/#rNumericLiteral">SPARQL 1.2 §19.8 [NumericLiteral]</see>.</remarks>
        public static Utf8String ExpectedDigit { get; } = new("LX0024"u8.ToArray());

        /// <summary>Exponent digits were expected after <c>e</c> / <c>E</c> but not found.</summary>
        /// <remarks>Lexer rule for <c>EXPONENT</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rEXPONENT">SPARQL 1.2 §19.8 [EXPONENT]</see>.</remarks>
        public static Utf8String ExpectedExponentDigits { get; } = new("LX0025"u8.ToArray());

        /// <summary>A numeric literal was malformed.</summary>
        /// <remarks>Lexer rules for the numeric literals. See <see href="https://www.w3.org/TR/sparql12-query/#rNumericLiteral">SPARQL 1.2 §19.8 [NumericLiteral]</see>.</remarks>
        public static Utf8String InvalidNumericLiteral { get; } = new("LX0026"u8.ToArray());

        /// <summary>A single <c>^</c> was not followed by the second <c>^</c> of the datatype marker.</summary>
        /// <remarks>Lexer rule for the <c>^^</c> datatype marker of <c>RDFLiteral</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRDFLiteral">SPARQL 1.2 §19.8 [RDFLiteral]</see>.</remarks>
        public static Utf8String ExpectedTypeMarker { get; } = new("LX0027"u8.ToArray());

        /// <summary>A lone <c>&amp;</c> was encountered; SPARQL has only the <c>&amp;&amp;</c> operator.</summary>
        /// <remarks>Lexer rule for the <c>&amp;&amp;</c> operator of <c>ConditionalAndExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConditionalAndExpression">SPARQL 1.2 §19.8 [ConditionalAndExpression]</see>.</remarks>
        public static Utf8String ExpectedSecondAmpersand { get; } = new("LX0028"u8.ToArray());

        /// <summary>A <c>&gt;</c> appeared where no token begins with it (outside <c>&gt;&gt;</c> and <c>&gt;=</c>).</summary>
        /// <remarks>Tokenisation failure with no applicable terminal. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String UnexpectedGreaterThan { get; } = new("LX0029"u8.ToArray());

        /// <summary>A <c>|</c> appeared where no <c>|}</c> annotation close was expected.</summary>
        /// <remarks>RDF 1.2 annotation-block close <c>|}</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar</see>.</remarks>
        public static Utf8String UnexpectedPipe { get; } = new("LX0030"u8.ToArray());
    }

    /// <summary>Turtle parser diagnostics (<c>TT</c> prefix).</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Turtle.X is the intended usage pattern.")]
    public static class Turtle
    {
        /// <summary>A subject term was expected.</summary>
        /// <remarks>Turtle <c>triples</c> / <c>subject</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [subject]</see>.</remarks>
        public static Utf8String ExpectedSubject { get; } = new("TT0001"u8.ToArray());

        /// <summary>A predicate (verb) was expected.</summary>
        /// <remarks>Turtle <c>predicateObjectList</c> / <c>verb</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [verb]</see>.</remarks>
        public static Utf8String ExpectedPredicate { get; } = new("TT0002"u8.ToArray());

        /// <summary>An object term was expected.</summary>
        /// <remarks>Turtle <c>objectList</c> / <c>object</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [object]</see>.</remarks>
        public static Utf8String ExpectedObject { get; } = new("TT0003"u8.ToArray());

        /// <summary>A statement was not terminated by <c>.</c>.</summary>
        /// <remarks>Turtle <c>triples</c> termination. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [statement]</see>.</remarks>
        public static Utf8String ExpectedDot { get; } = new("TT0004"u8.ToArray());

        /// <summary>A blank-node property list <c>[ … ]</c> was not closed.</summary>
        /// <remarks>Turtle <c>blankNodePropertyList</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [blankNodePropertyList]</see>.</remarks>
        public static Utf8String UnclosedBlankNodePropertyList { get; } = new("TT0005"u8.ToArray());

        /// <summary>An RDF collection <c>( … )</c> was not closed.</summary>
        /// <remarks>Turtle <c>collection</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [collection]</see>.</remarks>
        public static Utf8String UnclosedCollection { get; } = new("TT0006"u8.ToArray());
    }

    /// <summary>SPARQL parser diagnostics (<c>SP</c> prefix).</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Sparql.X is the intended usage pattern.")]
    public static class Sparql
    {
        /// <summary>A query form (<c>SELECT</c> / <c>CONSTRUCT</c> / <c>ASK</c> / <c>DESCRIBE</c>) was expected.</summary>
        /// <remarks>SPARQL <c>Query</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQuery">SPARQL 1.2 §19.8 [Query]</see>.</remarks>
        public static Utf8String ExpectedQueryForm { get; } = new("SP0001"u8.ToArray());

        /// <summary>The <c>WHERE</c> keyword (or its optional-keyword group) was expected.</summary>
        /// <remarks>SPARQL <c>WhereClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rWhereClause">SPARQL 1.2 §19.8 [WhereClause]</see>.</remarks>
        public static Utf8String ExpectedWhereKeyword { get; } = new("SP0002"u8.ToArray());

        /// <summary>A group graph pattern <c>{ … }</c> was not closed.</summary>
        /// <remarks>SPARQL <c>GroupGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupGraphPattern">SPARQL 1.2 §19.8 [GroupGraphPattern]</see>.</remarks>
        public static Utf8String UnclosedGroupGraphPattern { get; } = new("SP0003"u8.ToArray());

        /// <summary>A triple pattern was expected.</summary>
        /// <remarks>SPARQL <c>TriplesSameSubjectPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTriplesSameSubjectPath">SPARQL 1.2 §19.8 [TriplesSameSubjectPath]</see>.</remarks>
        public static Utf8String ExpectedTriplePattern { get; } = new("SP0004"u8.ToArray());

        /// <summary>A token appeared where the grammar permits none (generic unexpected-token).</summary>
        /// <remarks>No applicable production at the cursor. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String UnexpectedToken { get; } = new("SP0005"u8.ToArray());

        /// <summary>A prefixed name used a prefix with no in-scope <c>PREFIX</c> declaration.</summary>
        /// <remarks>SPARQL <c>PrefixedName</c> resolution. See <see href="https://www.w3.org/TR/sparql12-query/#rPrefixedName">SPARQL 1.2 §19.8 [PrefixedName]</see>.</remarks>
        public static Utf8String UnboundPrefix { get; } = new("SP0006"u8.ToArray());

        /// <summary>A <c>BASE</c> IRI was not a valid absolute IRI.</summary>
        /// <remarks>SPARQL <c>BaseDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBaseDecl">SPARQL 1.2 §19.8 [BaseDecl]</see>.</remarks>
        public static Utf8String InvalidBaseIri { get; } = new("SP0007"u8.ToArray());

        /// <summary>A SPARQL Update request was given where this build accepts only queries.</summary>
        /// <remarks>SPARQL <c>Update</c> is out of scope for this query build. See <see href="https://www.w3.org/TR/sparql12-update/">SPARQL 1.2 Update</see>.</remarks>
        public static Utf8String UpdateNotSupported { get; } = new("SP0008"u8.ToArray());

        /// <summary>An aggregate expression was nested inside another aggregate.</summary>
        /// <remarks>SPARQL <c>Aggregate</c> nesting is forbidden. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</remarks>
        public static Utf8String NestedAggregateNotSupported { get; } = new("SP0009"u8.ToArray());

        /// <summary>An aggregate appeared where the grammar does not permit one.</summary>
        /// <remarks>SPARQL aggregate scoping. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</remarks>
        public static Utf8String AggregateOutsideProjection { get; } = new("SP0010"u8.ToArray());

        /// <summary>An expression was expected.</summary>
        /// <remarks>SPARQL <c>Expression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rExpression">SPARQL 1.2 §19.8 [Expression]</see>.</remarks>
        public static Utf8String ExpressionExpected { get; } = new("SP0011"u8.ToArray());

        /// <summary>A bracketed expression <c>( … )</c> was not closed.</summary>
        /// <remarks>SPARQL <c>BrackettedExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBrackettedExpression">SPARQL 1.2 §19.8 [BrackettedExpression]</see>.</remarks>
        public static Utf8String UnclosedExpression { get; } = new("SP0012"u8.ToArray());

        /// <summary>A <c>VALUES</c> block declared the same variable more than once.</summary>
        /// <remarks>SPARQL <c>InlineData</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rInlineData">SPARQL 1.2 §19.8 [InlineData]</see>.</remarks>
        public static Utf8String DuplicateVariableInValues { get; } = new("SP0013"u8.ToArray());

        /// <summary>A <c>VALUES</c> data row had a different arity from the declared variable list.</summary>
        /// <remarks>SPARQL <c>DataBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDataBlock">SPARQL 1.2 §19.8 [DataBlock]</see>.</remarks>
        public static Utf8String ValuesArityMismatch { get; } = new("SP0014"u8.ToArray());

        /// <summary>The per-parse diagnostic cap was reached; remaining input is skipped.</summary>
        /// <remarks>Parser-internal bound (see the parser's <c>MaxDiagnostics</c> option), not a grammar production.</remarks>
        public static Utf8String ExcessDiagnostics { get; } = new("SP9999"u8.ToArray());
    }
}
