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
    /// <summary>Lexer diagnostics (<c>LX</c> prefix).</summary>
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
