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
        /// <remarks>Turtle <c>directive</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [directive]</see>. Reserved: no lexer emits it (a leading <c>@</c> lexes as a language tag or identifier); held for a parser-level directive check.</remarks>
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

        /// <summary>A backslash escape inside a prefixed name named a character outside the <c>PN_LOCAL_ESC</c> reserved set.</summary>
        /// <remarks>Lexer rule for <c>PN_LOCAL_ESC</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPN_LOCAL_ESC">SPARQL 1.2 §19.8 [PN_LOCAL_ESC]</see>.</remarks>
        public static Utf8String InvalidPrefixedNameEscape { get; } = new("LX0031"u8.ToArray());

        /// <summary>A block comment was opened with <c>/*</c> but never closed with <c>*/</c> before end of input.</summary>
        /// <remarks>Lexer rule for the JSONata block-comment trivia <c>/* … */</c>. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
        public static Utf8String UnterminatedBlockComment { get; } = new("LX0032"u8.ToArray());

        /// <summary>A regular-expression literal had no pattern between its opening and closing slash.</summary>
        /// <remarks>Lexer rule for the JSONata regular-expression literal <c>/pattern/flags</c>. See <see href="https://docs.jsonata.org/regex">the JSONata regular-expressions reference</see>.</remarks>
        public static Utf8String EmptyRegex { get; } = new("LX0033"u8.ToArray());

        /// <summary>A regular-expression literal was opened with <c>/</c> but never closed before end of input.</summary>
        /// <remarks>Lexer rule for the JSONata regular-expression literal <c>/pattern/flags</c>. See <see href="https://docs.jsonata.org/regex">the JSONata regular-expressions reference</see>.</remarks>
        public static Utf8String UnterminatedRegex { get; } = new("LX0034"u8.ToArray());
    }

    /// <summary>Turtle parser diagnostics (<c>TT</c> prefix).</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Turtle.X is the intended usage pattern.")]
    public static class Turtle
    {
        /// <summary>A subject term was expected.</summary>
        /// <remarks>Turtle <c>triples</c> / <c>subject</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [subject]</see>. Reserved: subject position currently reports the position-agnostic <see cref="ExpectedTerm"/>; held for a future position-aware diagnostic.</remarks>
        public static Utf8String ExpectedSubject { get; } = new("TT0001"u8.ToArray());

        /// <summary>A predicate (verb) was expected.</summary>
        /// <remarks>Turtle <c>predicateObjectList</c> / <c>verb</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [verb]</see>.</remarks>
        public static Utf8String ExpectedPredicate { get; } = new("TT0002"u8.ToArray());

        /// <summary>An object term was expected.</summary>
        /// <remarks>Turtle <c>objectList</c> / <c>object</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [object]</see>. Reserved: object position currently reports the position-agnostic <see cref="ExpectedTerm"/>; held for a future position-aware diagnostic.</remarks>
        public static Utf8String ExpectedObject { get; } = new("TT0003"u8.ToArray());

        /// <summary>A statement was not terminated by <c>.</c>.</summary>
        /// <remarks>Turtle <c>triples</c> termination. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [statement]</see>.</remarks>
        public static Utf8String ExpectedDot { get; } = new("TT0004"u8.ToArray());

        /// <summary>A blank-node property list <c>[ … ]</c> was not closed.</summary>
        /// <remarks>Turtle <c>blankNodePropertyList</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [blankNodePropertyList]</see>. Reserved: a malformed Turtle blank-node property list currently reports <see cref="ExpectedPredicate"/> at the offending verb/close position; held for a dedicated unclosed-list diagnostic.</remarks>
        public static Utf8String UnclosedBlankNodePropertyList { get; } = new("TT0005"u8.ToArray());

        /// <summary>An RDF collection <c>( … )</c> was not closed.</summary>
        /// <remarks>Turtle <c>collection</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [collection]</see>.</remarks>
        public static Utf8String UnclosedCollection { get; } = new("TT0006"u8.ToArray());

        /// <summary>A term was expected (in subject, predicate, or object position) but no term-starting token was found.</summary>
        /// <remarks>Turtle <c>object</c> / <c>subject</c> / <c>verb</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [object]</see>.</remarks>
        public static Utf8String ExpectedTerm { get; } = new("TT0007"u8.ToArray());

        /// <summary>A <c>@prefix</c> or <c>@base</c> directive was missing its IRI reference.</summary>
        /// <remarks>Turtle <c>prefixID</c> / <c>base</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [prefixID]</see>.</remarks>
        public static Utf8String ExpectedDirectiveIri { get; } = new("TT0008"u8.ToArray());

        /// <summary>A <c>@prefix</c> directive was missing its namespace label (<c>ns:</c>).</summary>
        /// <remarks>Turtle <c>prefixID</c> / <c>PNAME_NS</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [prefixID]</see>.</remarks>
        public static Utf8String ExpectedPrefixNamespace { get; } = new("TT0009"u8.ToArray());

        /// <summary>A <c>VERSION</c> directive argument was not a short-quoted string literal.</summary>
        /// <remarks>Turtle <c>version</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [version]</see>.</remarks>
        public static Utf8String InvalidVersionArgument { get; } = new("TT0010"u8.ToArray());

        /// <summary>A TriG graph block (the <c>GRAPH</c> keyword or a <c>{</c>) appeared in plain Turtle syntax.</summary>
        /// <remarks>TriG <c>graphStatement</c> is not valid in Turtle. See <see href="https://www.w3.org/TR/rdf12-trig/#sec-grammar-grammar">RDF 1.2 TriG grammar</see>.</remarks>
        public static Utf8String GraphBlockRequiresTriG { get; } = new("TT0011"u8.ToArray());

        /// <summary>A TriG graph block was missing its opening <c>{</c>.</summary>
        /// <remarks>TriG <c>wrappedGraph</c>. See <see href="https://www.w3.org/TR/rdf12-trig/#sec-grammar-grammar">RDF 1.2 TriG grammar [wrappedGraph]</see>.</remarks>
        public static Utf8String ExpectedGraphBlockOpen { get; } = new("TT0012"u8.ToArray());

        /// <summary>A TriG graph block <c>{ … }</c> was not closed.</summary>
        /// <remarks>TriG <c>wrappedGraph</c>. See <see href="https://www.w3.org/TR/rdf12-trig/#sec-grammar-grammar">RDF 1.2 TriG grammar [wrappedGraph]</see>.</remarks>
        public static Utf8String UnclosedGraphBlock { get; } = new("TT0013"u8.ToArray());

        /// <summary>A non-triple statement (a directive) appeared inside a TriG graph block.</summary>
        /// <remarks>TriG <c>wrappedGraph</c> admits only <c>triplesBlock</c>. See <see href="https://www.w3.org/TR/rdf12-trig/#sec-grammar-grammar">RDF 1.2 TriG grammar [wrappedGraph]</see>.</remarks>
        public static Utf8String OnlyTriplesInGraphBlock { get; } = new("TT0014"u8.ToArray());

        /// <summary>A triple term <c>&lt;&lt;( … )&gt;&gt;</c> was used as the subject of an asserted statement.</summary>
        /// <remarks>RDF 1.2 <c>tripleTerm</c> stands only in object position. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [tripleTerm]</see>.</remarks>
        public static Utf8String TripleTermAsSubject { get; } = new("TT0015"u8.ToArray());

        /// <summary>A triple term <c>&lt;&lt;( … )&gt;&gt;</c> was not closed.</summary>
        /// <remarks>RDF 1.2 <c>tripleTerm</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [tripleTerm]</see>.</remarks>
        public static Utf8String UnclosedTripleTerm { get; } = new("TT0016"u8.ToArray());

        /// <summary>A reified triple <c>&lt;&lt; … &gt;&gt;</c> was not closed.</summary>
        /// <remarks>RDF 1.2 <c>reifiedTriple</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [reifiedTriple]</see>.</remarks>
        public static Utf8String UnclosedReifiedTriple { get; } = new("TT0017"u8.ToArray());

        /// <summary>The subject of a triple term was not an IRI or blank node.</summary>
        /// <remarks>RDF 1.2 <c>ttSubject</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [ttSubject]</see>.</remarks>
        public static Utf8String InvalidTripleTermSubject { get; } = new("TT0018"u8.ToArray());

        /// <summary>The object of a triple term was not an IRI, blank node, literal, or triple term.</summary>
        /// <remarks>RDF 1.2 <c>ttObject</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [ttObject]</see>.</remarks>
        public static Utf8String InvalidTripleTermObject { get; } = new("TT0019"u8.ToArray());

        /// <summary>The subject of a reified triple was not an IRI, blank node, or reified triple.</summary>
        /// <remarks>RDF 1.2 <c>rtSubject</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [rtSubject]</see>.</remarks>
        public static Utf8String InvalidReifiedTripleSubject { get; } = new("TT0020"u8.ToArray());

        /// <summary>The object of a reified triple was not an IRI, blank node, literal, triple term, or reified triple.</summary>
        /// <remarks>RDF 1.2 <c>rtObject</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [rtObject]</see>.</remarks>
        public static Utf8String InvalidReifiedTripleObject { get; } = new("TT0021"u8.ToArray());

        /// <summary>An annotation block <c>{| |}</c> contained no predicate-object pair.</summary>
        /// <remarks>RDF 1.2 <c>annotation</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [annotation]</see>.</remarks>
        public static Utf8String EmptyAnnotationBlock { get; } = new("TT0022"u8.ToArray());

        /// <summary>A predicate or the annotation-block close <c>|}</c> was expected.</summary>
        /// <remarks>RDF 1.2 <c>annotation</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [annotation]</see>.</remarks>
        public static Utf8String ExpectedAnnotationVerbOrClose { get; } = new("TT0023"u8.ToArray());

        /// <summary>A datatype IRI was expected after the <c>^^</c> marker.</summary>
        /// <remarks>Turtle <c>RDFLiteral</c> datatype. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [RDFLiteral]</see>.</remarks>
        public static Utf8String ExpectedDatatypeIri { get; } = new("TT0024"u8.ToArray());

        /// <summary>A directional language tag named a base direction other than <c>ltr</c> or <c>rtl</c>.</summary>
        /// <remarks>RDF 1.2 base-direction language form. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar</see>.</remarks>
        public static Utf8String InvalidBaseDirection { get; } = new("TT0025"u8.ToArray());

        /// <summary>A relative IRI could not be resolved to an absolute IRI because no absolute base was in scope.</summary>
        /// <remarks>Emitter resolution (RFC 3986 §5) of an RDF term IRI. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-iri-references">RDF 1.2 Turtle IRI references</see>.</remarks>
        public static Utf8String UnresolvableRelativeIri { get; } = new("TT0026"u8.ToArray());

        /// <summary>A prefixed name used a prefix with no in-scope <c>@prefix</c> declaration.</summary>
        /// <remarks>Emitter expansion of <c>PrefixedName</c>. See <see href="https://www.w3.org/TR/rdf12-turtle/#sec-grammar-grammar">RDF 1.2 Turtle grammar [PrefixedName]</see>.</remarks>
        public static Utf8String UndeclaredPrefix { get; } = new("TT0027"u8.ToArray());
    }

    /// <summary>SPARQL parser diagnostics (<c>SP</c> prefix).</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Sparql.X is the intended usage pattern.")]
    public static class Sparql
    {
        /// <summary>A query form (<c>SELECT</c> / <c>CONSTRUCT</c> / <c>ASK</c> / <c>DESCRIBE</c>) was expected.</summary>
        /// <remarks>SPARQL <c>Query</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQuery">SPARQL 1.2 §19.8 [Query]</see>.</remarks>
        public static Utf8String ExpectedQueryForm { get; } = new("SP0001"u8.ToArray());

        /// <summary>The <c>WHERE</c> keyword (or its optional-keyword group) was expected.</summary>
        /// <remarks>SPARQL <c>WhereClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rWhereClause">SPARQL 1.2 §19.8 [WhereClause]</see>. Reserved: <c>WHERE</c> is optional, so the parser dispatches on the group's <c>{</c> (reporting <see cref="ExpectedGroupGraphPatternOpen"/>); held for completeness.</remarks>
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
        /// <remarks>SPARQL <c>BaseDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBaseDecl">SPARQL 1.2 §19.8 [BaseDecl]</see>. Reserved: <c>BASE</c>-IRI absoluteness is not yet validated at parse; held for that check.</remarks>
        public static Utf8String InvalidBaseIri { get; } = new("SP0007"u8.ToArray());

        /// <summary>A SPARQL Update request was given where this build accepts only queries.</summary>
        /// <remarks>SPARQL <c>Update</c> is out of scope for this query build. See <see href="https://www.w3.org/TR/sparql12-update/">SPARQL 1.2 Update</see>. Reserved: SPARQL Update detection is not implemented in this query-only build; held for it.</remarks>
        public static Utf8String UpdateNotSupported { get; } = new("SP0008"u8.ToArray());

        /// <summary>An aggregate expression was nested inside another aggregate.</summary>
        /// <remarks>SPARQL <c>Aggregate</c> nesting is forbidden. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>. Reserved: aggregate scope analysis is not yet implemented (a known gap); held for it.</remarks>
        public static Utf8String NestedAggregateNotSupported { get; } = new("SP0009"u8.ToArray());

        /// <summary>An aggregate appeared where the grammar does not permit one.</summary>
        /// <remarks>SPARQL aggregate scoping. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>. Reserved: aggregate scope analysis is not yet implemented (a known gap); held for it.</remarks>
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

        /// <summary>An <c>IF</c> call had an argument count other than three.</summary>
        /// <remarks>SPARQL built-in <c>IF</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</remarks>
        public static Utf8String IfArityMismatch { get; } = new("SP0015"u8.ToArray());

        /// <summary>A required structural keyword (for example <c>BY</c>, <c>AS</c>, <c>IN</c>, <c>SEPARATOR</c>, <c>EXISTS</c>) was missing.</summary>
        /// <remarks>SPARQL keyword-introduced productions. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String ExpectedKeyword { get; } = new("SP0016"u8.ToArray());

        /// <summary>An opening <c>{</c> introducing a group graph pattern was expected.</summary>
        /// <remarks>SPARQL <c>GroupGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupGraphPattern">SPARQL 1.2 §19.8 [GroupGraphPattern]</see>.</remarks>
        public static Utf8String ExpectedGroupGraphPatternOpen { get; } = new("SP0017"u8.ToArray());

        /// <summary>A projected variable, an <c>(expr AS ?var)</c> projection, or <c>*</c> was expected.</summary>
        /// <remarks>SPARQL <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
        public static Utf8String ExpectedProjection { get; } = new("SP0018"u8.ToArray());

        /// <summary>A <c>DESCRIBE</c> target (a variable, an IRI, or <c>*</c>) was expected.</summary>
        /// <remarks>SPARQL <c>DescribeQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDescribeQuery">SPARQL 1.2 §19.8 [DescribeQuery]</see>.</remarks>
        public static Utf8String ExpectedDescribeTarget { get; } = new("SP0019"u8.ToArray());

        /// <summary>A variable was expected.</summary>
        /// <remarks>SPARQL <c>Var</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVar">SPARQL 1.2 §19.8 [Var]</see>.</remarks>
        public static Utf8String ExpectedVariable { get; } = new("SP0020"u8.ToArray());

        /// <summary>A graph designator (a variable or an IRI) was expected after <c>GRAPH</c> or <c>SERVICE</c>.</summary>
        /// <remarks>SPARQL <c>VarOrIri</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrIri">SPARQL 1.2 §19.8 [VarOrIri]</see>.</remarks>
        public static Utf8String ExpectedGraphTerm { get; } = new("SP0021"u8.ToArray());

        /// <summary>A subject or object term was expected.</summary>
        /// <remarks>SPARQL <c>VarOrTerm</c> / <c>GraphNode</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrTerm">SPARQL 1.2 §19.8 [VarOrTerm]</see>.</remarks>
        public static Utf8String ExpectedTerm { get; } = new("SP0022"u8.ToArray());

        /// <summary>A predicate or property path was expected.</summary>
        /// <remarks>SPARQL <c>Verb</c> / <c>Path</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPath">SPARQL 1.2 §19.8 [Path]</see>.</remarks>
        public static Utf8String ExpectedVerb { get; } = new("SP0023"u8.ToArray());

        /// <summary>A path primary (an IRI, <c>a</c>, an inverse, a negated set, or a group) was expected.</summary>
        /// <remarks>SPARQL <c>PathPrimary</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathPrimary">SPARQL 1.2 §19.8 [PathPrimary]</see>.</remarks>
        public static Utf8String ExpectedPathPrimary { get; } = new("SP0024"u8.ToArray());

        /// <summary>A grouped path <c>( … )</c> was not closed.</summary>
        /// <remarks>SPARQL <c>PathPrimary</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathPrimary">SPARQL 1.2 §19.8 [PathPrimary]</see>.</remarks>
        public static Utf8String UnclosedPath { get; } = new("SP0025"u8.ToArray());

        /// <summary>A <c>|</c> separator or a closing <c>)</c> was expected in a negated property set.</summary>
        /// <remarks>SPARQL <c>PathNegatedPropertySet</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathNegatedPropertySet">SPARQL 1.2 §19.8 [PathNegatedPropertySet]</see>.</remarks>
        public static Utf8String ExpectedNegatedPathItem { get; } = new("SP0026"u8.ToArray());

        /// <summary>A collection <c>( … )</c> was not closed.</summary>
        /// <remarks>SPARQL <c>Collection</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rCollection">SPARQL 1.2 §19.8 [Collection]</see>.</remarks>
        public static Utf8String UnclosedCollection { get; } = new("SP0027"u8.ToArray());

        /// <summary>A blank-node property list <c>[ … ]</c> was not closed (a <c>;</c> or <c>]</c> was expected).</summary>
        /// <remarks>SPARQL <c>BlankNodePropertyListPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBlankNodePropertyListPath">SPARQL 1.2 §19.8 [BlankNodePropertyListPath]</see>.</remarks>
        public static Utf8String UnclosedBlankNodePropertyList { get; } = new("SP0028"u8.ToArray());

        /// <summary>An RDF 1.2 triple term <c>&lt;&lt;( … )&gt;&gt;</c> was not closed.</summary>
        /// <remarks>SPARQL RDF 1.2 <c>TripleTerm</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTerm">SPARQL 1.2 §19.8 [TripleTerm]</see>.</remarks>
        public static Utf8String UnclosedTripleTerm { get; } = new("SP0029"u8.ToArray());

        /// <summary>An RDF 1.2 reified triple <c>&lt;&lt; … &gt;&gt;</c> was not closed.</summary>
        /// <remarks>SPARQL RDF 1.2 <c>ReifiedTriple</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifiedTriple">SPARQL 1.2 §19.8 [ReifiedTriple]</see>.</remarks>
        public static Utf8String UnclosedReifiedTriple { get; } = new("SP0030"u8.ToArray());

        /// <summary>An RDF 1.2 annotation block <c>{| … |}</c> was not closed or was empty.</summary>
        /// <remarks>SPARQL RDF 1.2 <c>Annotation</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotation">SPARQL 1.2 §19.8 [Annotation]</see>.</remarks>
        public static Utf8String UnclosedAnnotationBlock { get; } = new("SP0031"u8.ToArray());

        /// <summary>An RDF 1.2 annotation followed a property-path predicate, which is not permitted.</summary>
        /// <remarks>SPARQL RDF 1.2 <c>PropertyListPathNotEmpty</c> annotation tail. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotation">SPARQL 1.2 §19.8 [Annotation]</see>.</remarks>
        public static Utf8String AnnotationOnPathVerb { get; } = new("SP0032"u8.ToArray());

        /// <summary>A reifier identity (an IRI, a variable, or a blank node) was expected after <c>~</c>.</summary>
        /// <remarks>SPARQL RDF 1.2 <c>Reifier</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifier">SPARQL 1.2 §19.8 [Reifier]</see>.</remarks>
        public static Utf8String ExpectedReifier { get; } = new("SP0033"u8.ToArray());

        /// <summary>An RDF 1.2 triple-term verb (an IRI, <c>a</c>, or a variable) was expected.</summary>
        /// <remarks>SPARQL RDF 1.2 <c>TripleTerm</c> verb. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTerm">SPARQL 1.2 §19.8 [TripleTerm]</see>.</remarks>
        public static Utf8String ExpectedTripleTermVerb { get; } = new("SP0034"u8.ToArray());

        /// <summary>A datatype IRI was expected after the <c>^^</c> marker.</summary>
        /// <remarks>SPARQL <c>RDFLiteral</c> datatype. See <see href="https://www.w3.org/TR/sparql12-query/#rRDFLiteral">SPARQL 1.2 §19.8 [RDFLiteral]</see>.</remarks>
        public static Utf8String ExpectedDatatypeIri { get; } = new("SP0035"u8.ToArray());

        /// <summary>A <c>VALUES</c> data value (an IRI, a literal, or <c>UNDEF</c>) was expected.</summary>
        /// <remarks>SPARQL <c>DataBlockValue</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDataBlockValue">SPARQL 1.2 §19.8 [DataBlockValue]</see>.</remarks>
        public static Utf8String ExpectedValuesValue { get; } = new("SP0036"u8.ToArray());

        /// <summary>The <c>VALUES</c> data block was malformed (a variable, <c>(</c>, <c>)</c>, <c>{</c>, or <c>}</c> was expected).</summary>
        /// <remarks>SPARQL <c>InlineData</c> / <c>DataBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDataBlock">SPARQL 1.2 §19.8 [DataBlock]</see>.</remarks>
        public static Utf8String MalformedValuesBlock { get; } = new("SP0037"u8.ToArray());

        /// <summary>The query continued past its complete form (trailing tokens after the request).</summary>
        /// <remarks>SPARQL <c>Query</c> end. See <see href="https://www.w3.org/TR/sparql12-query/#rQuery">SPARQL 1.2 §19.8 [Query]</see>.</remarks>
        public static Utf8String ExpectedEndOfQuery { get; } = new("SP0038"u8.ToArray());

        /// <summary>An expected closing token (for example <c>)</c> or <c>}</c>) was missing.</summary>
        /// <remarks>SPARQL bracketed / call productions. See <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.</remarks>
        public static Utf8String ExpectedCloser { get; } = new("SP0039"u8.ToArray());

        /// <summary>An integer value (for example a <c>LIMIT</c> or <c>OFFSET</c>) was expected or malformed.</summary>
        /// <remarks>SPARQL <c>LimitClause</c> / <c>OffsetClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rLimitOffsetClauses">SPARQL 1.2 §19.8 [LimitOffsetClauses]</see>.</remarks>
        public static Utf8String ExpectedInteger { get; } = new("SP0040"u8.ToArray());

        /// <summary>A grouping, having, or ordering condition was expected.</summary>
        /// <remarks>SPARQL <c>GroupCondition</c> / <c>HavingCondition</c> / <c>OrderCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSolutionModifier">SPARQL 1.2 §19.8 [SolutionModifier]</see>.</remarks>
        public static Utf8String ExpectedSolutionCondition { get; } = new("SP0041"u8.ToArray());

        /// <summary>A directional language tag named a base direction other than <c>ltr</c> or <c>rtl</c>.</summary>
        /// <remarks>RDF 1.2 base-direction language form in <c>RDFLiteral</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRDFLiteral">SPARQL 1.2 §19.8 [RDFLiteral]</see>.</remarks>
        public static Utf8String InvalidBaseDirection { get; } = new("SP0042"u8.ToArray());

        /// <summary>A <c>VERSION</c> declaration argument was not a short-quoted string label.</summary>
        /// <remarks>SPARQL <c>VersionDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVersionDecl">SPARQL 1.2 §19.8 [VersionDecl]</see>.</remarks>
        public static Utf8String InvalidVersionArgument { get; } = new("SP0043"u8.ToArray());

        /// <summary>An RDF 1.2 triple-term subject was not permitted in this position: an expression <c>ExprTripleTerm</c> subject must be an IRI or variable, and a <c>VALUES</c> <c>TripleTermData</c> subject must be an IRI (no literal, blank node, or nested triple term).</summary>
        /// <remarks>SPARQL <c>ExprTripleTerm</c> / <c>TripleTermData</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTermData">SPARQL 1.2 §19.8 [TripleTermData]</see>.</remarks>
        public static Utf8String InvalidTripleTermSubject { get; } = new("SP0044"u8.ToArray());

        /// <summary>An RDF 1.2 triple-term object was not permitted in this position: an expression or <c>VALUES</c> triple-term object must be an IRI, a literal, or a nested triple term (and, in an expression, a variable) — not a blank node.</summary>
        /// <remarks>SPARQL <c>ExprTripleTerm</c> / <c>TripleTermData</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTermData">SPARQL 1.2 §19.8 [TripleTermData]</see>.</remarks>
        public static Utf8String InvalidTripleTermObject { get; } = new("SP0045"u8.ToArray());

        /// <summary>A SPARQL Update operation, a <c>;</c> separator, or the end of the request was expected.</summary>
        /// <remarks>SPARQL Update <c>Update1</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rUpdate1">SPARQL 1.2 Update §19.8 [Update1]</see>.</remarks>
        public static Utf8String ExpectedUpdateOperation { get; } = new("SP0046"u8.ToArray());

        /// <summary>A graph reference (<c>DEFAULT</c>, <c>NAMED</c>, <c>ALL</c>, or <c>GRAPH iri</c> / an IRI) was expected.</summary>
        /// <remarks>SPARQL Update <c>GraphRefAll</c> / <c>GraphOrDefault</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rGraphRefAll">SPARQL 1.2 Update §19.8 [GraphRefAll]</see>.</remarks>
        public static Utf8String ExpectedGraphReference { get; } = new("SP0047"u8.ToArray());

        /// <summary>A <c>DELETE</c> template or <c>DELETE DATA</c> block introduced a blank node (a <c>_:b</c>, a <c>[ … ]</c> / collection, or an anonymous reifier from a <c>{| … |}</c> annotation), which SPARQL Update §3.1.3 disallows.</summary>
        /// <remarks>SPARQL Update <c>DeleteClause</c> / <c>DeleteData</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rDeleteClause">SPARQL 1.2 Update §19.8 [DeleteClause]</see>.</remarks>
        public static Utf8String BlankNodeInDeleteTemplate { get; } = new("SP0048"u8.ToArray());

        /// <summary>An <c>INSERT DATA</c> / <c>DELETE DATA</c> block contained a variable (in a triple position or a <c>GRAPH</c> designator), which the ground <c>QuadData</c> grammar disallows.</summary>
        /// <remarks>SPARQL Update <c>QuadData</c>. See <see href="https://www.w3.org/TR/sparql12-update/#rQuadData">SPARQL 1.2 Update §19.8 [QuadData]</see>.</remarks>
        public static Utf8String VariableInQuadData { get; } = new("SP0049"u8.ToArray());

        /// <summary>A blank-node label was reused across two <c>INSERT DATA</c>/<c>DELETE DATA</c> operations of a single request; blank-node labels are scoped to one operation.</summary>
        /// <remarks>SPARQL Update §4.1.2 blank-node scoping. See <see href="https://www.w3.org/TR/sparql12-update/#rUpdate">SPARQL 1.2 Update §19.8 [Update]</see>.</remarks>
        public static Utf8String BlankNodeLabelReusedAcrossOperations { get; } = new("SP0050"u8.ToArray());

        /// <summary>The <c>CONSTRUCT WHERE</c> short form contained a non-triple group element (a <c>FILTER</c>, <c>GRAPH</c>, <c>OPTIONAL</c>, sub-pattern, …); only a triples template is permitted.</summary>
        /// <remarks>SPARQL <c>ConstructQuery</c> short form (<c>CONSTRUCT WHERE { TriplesTemplate }</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rConstructQuery">SPARQL 1.2 §19.8 [ConstructQuery]</see>.</remarks>
        public static Utf8String ConstructShortFormOnlyTriples { get; } = new("SP0051"u8.ToArray());

        /// <summary>An RDF 1.2 quoted triple or reified triple was nested deeper than the permitted maximum.</summary>
        /// <remarks>Parser-internal bound (<c>QuotedTripleLimits.MaxNestingDepth</c>), not a grammar production; the over-deep term is collapsed to an error term and parsing resynchronises past it. Collections and blank-node property lists are not bounded here (their list-valued equality does not recurse). See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTerm">SPARQL 1.2 §19.8 [TripleTerm]</see>.</remarks>
        public static Utf8String QuotedTripleNestingTooDeep { get; } = new("SP0052"u8.ToArray());

        /// <summary>An <c>EXISTS</c> / <c>NOT EXISTS</c> expression was nested deeper than the permitted maximum.</summary>
        /// <remarks>Parser-internal bound (<c>SparqlTranslator.MaxExistsNestingDepth</c>), not a grammar production; the over-deep expression is collapsed to an error expression and parsing resynchronises past it. The evaluator checks the same bound defensively for programmatically-constructed algebra that never passed the parser. See <see href="https://www.w3.org/TR/sparql12-query/#rExistsFunc">SPARQL 1.2 §19.8 [ExistsFunc]</see>.</remarks>
        public static Utf8String ExistsNestingTooDeep { get; } = new("SP0053"u8.ToArray());

        /// <summary>The per-parse diagnostic cap was reached; further parser diagnostics are suppressed (the AST still assembles).</summary>
        /// <remarks>Parser-internal bound (see the parser's <c>MaxDiagnostics</c> option), not a grammar production.</remarks>
        public static Utf8String ExcessDiagnostics { get; } = new("SP9999"u8.ToArray());
    }

    /// <summary>JSONata parser diagnostics (<c>JS</c> prefix).</summary>
    /// <remarks>
    /// One code per distinct parser-error condition the JSONata parser recovers from. The lexer keeps
    /// its shared <c>LX</c> codes (bridged into the same bag); these <c>JS</c> codes cover only the
    /// grammar-level conditions the parser raises. Codes are stable and never renumbered; new conditions
    /// append a higher number. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Jsonata.X is the intended usage pattern.")]
    public static class Jsonata
    {
        /// <summary>An expression was expected at the cursor.</summary>
        /// <remarks>JSONata <c>expression</c>. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
        public static Utf8String ExpectedExpression { get; } = new("JS0001"u8.ToArray());

        /// <summary>A token appeared where the grammar permits none (generic unexpected-token).</summary>
        /// <remarks>No applicable production at the cursor. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
        public static Utf8String UnexpectedToken { get; } = new("JS0002"u8.ToArray());

        /// <summary>A construct the grammar defines but this build does not yet parse was encountered.</summary>
        /// <remarks>Parser coverage bound, not a grammar error; the message names the construct. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
        public static Utf8String UnsupportedConstruct { get; } = new("JS0003"u8.ToArray());

        /// <summary>An expected closing token (for example <c>)</c> or <c>]</c>) was missing.</summary>
        /// <remarks>JSONata bracketed / predicate productions. See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</remarks>
        public static Utf8String MissingCloser { get; } = new("JS0004"u8.ToArray());

        /// <summary>The expression continued past its complete form (trailing tokens after the program).</summary>
        /// <remarks>JSONata <c>expression</c> end. See <see href="https://docs.jsonata.org/">the JSONata language reference</see>.</remarks>
        public static Utf8String ExpectedEndOfExpression { get; } = new("JS0005"u8.ToArray());

        /// <summary>The left side of the bind operator <c>:=</c> was not a variable name (it must start with <c>$</c>).</summary>
        /// <remarks>JSONata bind <c>:=</c>; corresponds to the reference parser's <c>S0212</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
        public static Utf8String BindLeftNotVariable { get; } = new("JS0006"u8.ToArray());

        /// <summary>A lambda parameter was not a variable name (each parameter must be a <c>$name</c>).</summary>
        /// <remarks>JSONata function definition <c>function(...){...}</c>; corresponds to the reference parser's <c>S0208</c>. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
        public static Utf8String LambdaParameterNotVariable { get; } = new("JS0007"u8.ToArray());

        /// <summary>A lambda's bracketed type signature <c>&lt;...&gt;</c> was malformed (a parameterised type on a non-container, a bracket nested in a union, or too many parameters).</summary>
        /// <remarks>JSONata function-signature syntax <c>function(...)&lt;sig&gt;{...}</c>; corresponds to the reference parser's <c>S0401</c> / <c>S0402</c> signature codes. See <see href="https://docs.jsonata.org/programming">the JSONata programming reference</see>.</remarks>
        public static Utf8String InvalidFunctionSignature { get; } = new("JS0008"u8.ToArray());

        /// <summary>A grouping path step <c>path{ ... }</c> was followed by a predicate <c>[ ... ]</c> or by another grouping, both of which are invalid in a single step.</summary>
        /// <remarks>JSONata path grammar; corresponds to the reference parser's <c>S0209</c> (predicate after a grouping) and <c>S0210</c> (more than one grouping in a step) codes. See <see href="https://docs.jsonata.org/sorting-grouping">the JSONata grouping reference</see>.</remarks>
        public static Utf8String InvalidGroupingStep { get; } = new("JS0009"u8.ToArray());

        /// <summary>A path step was a number or a value literal (<c>true</c> / <c>false</c> / <c>null</c>), which is not a valid navigation step.</summary>
        /// <remarks>JSONata path grammar; corresponds to the reference parser's <c>S0213</c>. Raised by the post-parse path-processing pass. See <see href="https://docs.jsonata.org/path-operators">the JSONata path-operators reference</see>.</remarks>
        public static Utf8String PathStepNotNavigable { get; } = new("JS0010"u8.ToArray());

        /// <summary>The right-hand side of a context bind <c>@</c> or a positional bind <c>#</c> was not a variable name (it must be a <c>$name</c>); the message names the offending operator token.</summary>
        /// <remarks>JSONata joins grammar; corresponds to the reference parser's <c>S0214</c>. The bound side of <c>@</c> / <c>#</c> must be a variable. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
        public static Utf8String BindRightNotVariable { get; } = new("JS0011"u8.ToArray());

        /// <summary>A context bind <c>@</c> was applied after a predicate, which the grammar forbids in a single step.</summary>
        /// <remarks>JSONata joins grammar; corresponds to the reference parser's <c>S0215</c>. Raised by the post-parse path-processing pass. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
        public static Utf8String ContextBindAfterPredicate { get; } = new("JS0012"u8.ToArray());

        /// <summary>A context bind <c>@</c> or positional bind <c>#</c> was applied after an order-by clause, which the grammar forbids in a single step.</summary>
        /// <remarks>JSONata joins grammar; corresponds to the reference parser's <c>S0216</c>. Raised by the post-parse path-processing pass. See <see href="https://docs.jsonata.org/sorting-grouping#joins">the JSONata joins reference</see>.</remarks>
        public static Utf8String BindAfterSort { get; } = new("JS0013"u8.ToArray());

        /// <summary>A parent operator <c>%</c> could not derive an ancestor — there is no structural step it can climb to (a bare or over-deep parent, or a parent over a non-navigable step).</summary>
        /// <remarks>JSONata parent-operator grammar; corresponds to the reference parser's <c>S0217</c>. Raised by the post-parse ancestry pass. See <see href="https://docs.jsonata.org/path-operators#navigate-to-the-parent">the JSONata path-operators reference</see>.</remarks>
        public static Utf8String CannotDeriveAncestor { get; } = new("JS0014"u8.ToArray());

        /// <summary>A numeric literal's magnitude is outside the representable IEEE-754 double range.</summary>
        /// <remarks>JSONata number-literal grammar; corresponds to the reference parser's <c>S0102</c> (number out of range). See <see href="https://docs.jsonata.org/simple">the JSONata simple-queries reference</see>.</remarks>
        public static Utf8String NumberOutOfRange { get; } = new("JS0015"u8.ToArray());

        /// <summary>The per-parse diagnostic cap was reached; further parser diagnostics are suppressed (the AST still assembles).</summary>
        /// <remarks>Parser-internal bound (see the parser's <c>MaxDiagnostics</c> option), not a grammar production.</remarks>
        public static Utf8String ExcessDiagnostics { get; } = new("JS9999"u8.ToArray());
    }

    /// <summary>
    /// OWL structural-mapping diagnostics (<c>OW</c> prefix), emitted when an
    /// RDF graph is mapped to OWL 2 axioms.
    /// </summary>
    /// <remarks>
    /// The mapping follows <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/">OWL 2 Mapping to
    /// RDF Graphs</see> in reverse (graph → structural form). Diagnostics carry the offending
    /// triple in the message; the mapping has no source spans because it starts from parsed quads.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "WellKnownDiagnostics.Owl.X is the intended usage pattern.")]
    public static class Owl
    {
        /// <summary>An RDF collection in an OWL structure was malformed — a node lacked <c>rdf:first</c>/<c>rdf:rest</c>, or the chain cycled before reaching <c>rdf:nil</c>.</summary>
        /// <remarks>Mapping of <c>SEQ</c> list patterns. See <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/#Mapping_from_RDF_Graphs_to_the_Structural_Specification">OWL 2 Mapping to RDF §3</see>.</remarks>
        public static Utf8String MalformedList { get; } = new("OW0001"u8.ToArray());

        /// <summary>An <c>owl:Restriction</c> node lacked <c>owl:onProperty</c>, lacked a restriction-defining predicate, or combined incompatible ones.</summary>
        /// <remarks>Mapping of class expressions, restriction patterns. See <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/#Parsing_of_Expressions">OWL 2 Mapping to RDF §3.2.4</see>.</remarks>
        public static Utf8String MalformedRestriction { get; } = new("OW0002"u8.ToArray());

        /// <summary>A node in class-expression position is neither a named class nor a recognisable class-expression structure.</summary>
        /// <remarks>Mapping of class expressions. See <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/#Parsing_of_Expressions">OWL 2 Mapping to RDF §3.2.4</see>.</remarks>
        public static Utf8String MalformedClassExpression { get; } = new("OW0003"u8.ToArray());

        /// <summary>A property occurs in an assertion without a declaration that fixes its kind; it is mapped as an annotation assertion.</summary>
        /// <remarks>Declaration-driven disambiguation. See <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/#Mapping_from_RDF_Graphs_to_the_Structural_Specification">OWL 2 Mapping to RDF §3 (Table 5 typing)</see>.</remarks>
        public static Utf8String UndeclaredProperty { get; } = new("OW0004"u8.ToArray());

        /// <summary>A reified axiom structure (<c>owl:AllDisjointClasses</c>, <c>owl:NegativePropertyAssertion</c>, …) lacked a required member predicate.</summary>
        /// <remarks>Mapping of axiom reification patterns. See <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/#Parsing_of_Axioms">OWL 2 Mapping to RDF §3.2.5</see>.</remarks>
        public static Utf8String MalformedAxiomStructure { get; } = new("OW0005"u8.ToArray());

        /// <summary>The graph used an OWL construct this mapper does not yet parse; the construct's triples are left unmapped.</summary>
        /// <remarks>Mapper coverage bound, not a grammar production; the message names the construct.</remarks>
        public static Utf8String UnsupportedConstruct { get; } = new("OW0006"u8.ToArray());

        /// <summary>A semantically single-valued position (a restriction field, a list cell's <c>rdf:first</c>/<c>rdf:rest</c>, a reification member) carried several distinct values; the reverse mapping determines no value, so the construct is refused.</summary>
        /// <remarks>Exactly-one pattern matching of the reverse mapping. See <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/#Parsing_of_Expressions">OWL 2 Mapping to RDF §3.2.4</see>.</remarks>
        public static Utf8String AmbiguousValue { get; } = new("OW0007"u8.ToArray());
    }
}
