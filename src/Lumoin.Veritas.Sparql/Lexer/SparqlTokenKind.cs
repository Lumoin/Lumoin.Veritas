namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// The kind of a <see cref="SparqlToken"/>. Enumerates every terminal the
/// SPARQL 1.2 Query grammar (§19.8) accepts: term syntax, punctuation and
/// operators, the RDF 1.2 reified-triple and triple-term delimiters, the
/// structural keywords, and the reserved built-in-function and aggregate names.
/// </summary>
/// <remarks>
/// <para>
/// SPARQL keywords are case-insensitive; the lexer canonicalises each to a
/// single token kind regardless of source casing. Built-in function names and
/// aggregate names are recognised as distinct kinds — not as prefixed names —
/// because the grammar treats them as terminals of the <c>BuiltInCall</c> and
/// <c>Aggregate</c> productions, so the parser sees a clean token stream.
/// </para>
/// <para>
/// The boolean literals <c>true</c> / <c>false</c> fold into
/// <see cref="BooleanLiteral"/> (matching the Turtle lexer precedent) rather
/// than carrying their own keyword kinds, and the predicate shorthand
/// <c>a</c> is <see cref="A"/>.
/// </para>
/// </remarks>
public enum SparqlTokenKind
{
    /// <summary>An angle-bracketed IRI reference: <c>&lt;http://example.org/&gt;</c>.</summary>
    /// <remarks>SPARQL <c>IRIREF</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rIRIREF">SPARQL 1.2 §19.8 [IRIREF]</see>.</remarks>
    Iri,

    /// <summary>A prefixed name: <c>foaf:name</c> or <c>:local</c>.</summary>
    /// <remarks>SPARQL <c>PNAME_LN</c> / <c>PNAME_NS</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPNAME_LN">SPARQL 1.2 §19.8 [PNAME_LN]</see>.</remarks>
    PrefixedName,

    /// <summary>A namespace prefix label in a <c>PREFIX</c> declaration: <c>foaf:</c> or <c>:</c>.</summary>
    /// <remarks>SPARQL <c>PNAME_NS</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPNAME_NS">SPARQL 1.2 §19.8 [PNAME_NS]</see>.</remarks>
    PrefixNamespace,

    /// <summary>A blank-node label: <c>_:b0</c>.</summary>
    /// <remarks>SPARQL <c>BLANK_NODE_LABEL</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBLANK_NODE_LABEL">SPARQL 1.2 §19.8 [BLANK_NODE_LABEL]</see>.</remarks>
    BlankNodeLabel,

    /// <summary>The anonymous-blank-node sugar <c>[]</c> with only whitespace between the brackets.</summary>
    /// <remarks>SPARQL <c>ANON</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rANON">SPARQL 1.2 §19.8 [ANON]</see>.</remarks>
    AnonymousBlankNode,

    /// <summary>A query variable: <c>?name</c> or <c>$name</c>.</summary>
    /// <remarks>SPARQL <c>VAR1</c> / <c>VAR2</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVAR1">SPARQL 1.2 §19.8 [VAR1]</see>.</remarks>
    Variable,

    /// <summary>A short (single- or double-quoted) string literal, decoded into UTF-8 bytes.</summary>
    /// <remarks>SPARQL <c>STRING_LITERAL1</c> / <c>STRING_LITERAL2</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSTRING_LITERAL1">SPARQL 1.2 §19.8 [STRING_LITERAL1]</see>.</remarks>
    StringLiteral,

    /// <summary>A long (triple-quoted) string literal, decoded into UTF-8 bytes.</summary>
    /// <remarks>SPARQL <c>STRING_LITERAL_LONG1</c> / <c>STRING_LITERAL_LONG2</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSTRING_LITERAL_LONG1">SPARQL 1.2 §19.8 [STRING_LITERAL_LONG1]</see>.</remarks>
    LongStringLiteral,

    /// <summary>An integer literal: <c>42</c>, <c>-17</c>, <c>+0</c>.</summary>
    /// <remarks>SPARQL <c>INTEGER</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rINTEGER">SPARQL 1.2 §19.8 [INTEGER]</see>.</remarks>
    IntegerLiteral,

    /// <summary>A decimal literal with a decimal point and no exponent: <c>1.5</c>.</summary>
    /// <remarks>SPARQL <c>DECIMAL</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDECIMAL">SPARQL 1.2 §19.8 [DECIMAL]</see>.</remarks>
    DecimalLiteral,

    /// <summary>A double literal with an exponent: <c>1.5e10</c>, <c>.5E-3</c>.</summary>
    /// <remarks>SPARQL <c>DOUBLE</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDOUBLE">SPARQL 1.2 §19.8 [DOUBLE]</see>.</remarks>
    DoubleLiteral,

    /// <summary>The boolean keyword <c>true</c> or <c>false</c>.</summary>
    /// <remarks>SPARQL <c>BooleanLiteral</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBooleanLiteral">SPARQL 1.2 §19.8 [BooleanLiteral]</see>.</remarks>
    BooleanLiteral,

    /// <summary>A language tag without direction: <c>@en</c>, <c>@en-GB</c>.</summary>
    /// <remarks>SPARQL <c>LANGTAG</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rLANGTAG">SPARQL 1.2 §19.8 [LANGTAG]</see>.</remarks>
    LangTag,

    /// <summary>A directional language tag: <c>@en--ltr</c>, <c>@en-GB--rtl</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>LANGTAG</c> (RDF 1.2 base-direction form). See <see href="https://www.w3.org/TR/sparql12-query/#rLANGTAG">SPARQL 1.2 §19.8 [LANGTAG]</see>.</remarks>
    DirLangTag,

    /// <summary>The literal datatype marker <c>^^</c>.</summary>
    /// <remarks>SPARQL <c>'^^'</c> datatype marker of <c>RDFLiteral</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRDFLiteral">SPARQL 1.2 §19.8 [RDFLiteral]</see>.</remarks>
    TypeMarker,

    /// <summary>The predicate-position shorthand <c>a</c> for <c>rdf:type</c>.</summary>
    /// <remarks>SPARQL <c>'a'</c> shorthand of <c>PathPrimary</c> / <c>Verb</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathPrimary">SPARQL 1.2 §19.8 [PathPrimary]</see>.</remarks>
    A,

    /// <summary>The statement / triple terminator <c>.</c>.</summary>
    /// <remarks>SPARQL <c>'.'</c> terminator of <c>TriplesBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTriplesBlock">SPARQL 1.2 §19.8 [TriplesBlock]</see>.</remarks>
    Period,

    /// <summary>The object-list separator <c>,</c>.</summary>
    /// <remarks>SPARQL <c>','</c> separator of <c>ObjectListPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rObjectListPath">SPARQL 1.2 §19.8 [ObjectListPath]</see>.</remarks>
    Comma,

    /// <summary>The predicate-object-list separator <c>;</c>.</summary>
    /// <remarks>SPARQL <c>';'</c> separator of <c>PropertyListPathNotEmpty</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPropertyListPathNotEmpty">SPARQL 1.2 §19.8 [PropertyListPathNotEmpty]</see>.</remarks>
    Semicolon,

    /// <summary>The group / call open parenthesis <c>(</c>.</summary>
    /// <remarks>SPARQL <c>'('</c> of <c>BrackettedExpression</c> / <c>Collection</c> / call arguments. See <see href="https://www.w3.org/TR/sparql12-query/#rBrackettedExpression">SPARQL 1.2 §19.8 [BrackettedExpression]</see>.</remarks>
    OpenParen,

    /// <summary>The group / call close parenthesis <c>)</c>.</summary>
    /// <remarks>SPARQL <c>')'</c> of <c>BrackettedExpression</c> / <c>Collection</c> / call arguments. See <see href="https://www.w3.org/TR/sparql12-query/#rBrackettedExpression">SPARQL 1.2 §19.8 [BrackettedExpression]</see>.</remarks>
    CloseParen,

    /// <summary>The blank-node-property-list start <c>[</c>.</summary>
    /// <remarks>SPARQL <c>'['</c> of <c>BlankNodePropertyListPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBlankNodePropertyListPath">SPARQL 1.2 §19.8 [BlankNodePropertyListPath]</see>.</remarks>
    OpenBracket,

    /// <summary>The blank-node-property-list end <c>]</c>.</summary>
    /// <remarks>SPARQL <c>']'</c> of <c>BlankNodePropertyListPath</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBlankNodePropertyListPath">SPARQL 1.2 §19.8 [BlankNodePropertyListPath]</see>.</remarks>
    CloseBracket,

    /// <summary>The group-graph-pattern start <c>{</c>.</summary>
    /// <remarks>SPARQL <c>'{'</c> of <c>GroupGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupGraphPattern">SPARQL 1.2 §19.8 [GroupGraphPattern]</see>.</remarks>
    OpenBrace,

    /// <summary>The group-graph-pattern end <c>}</c>.</summary>
    /// <remarks>SPARQL <c>'}'</c> of <c>GroupGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupGraphPattern">SPARQL 1.2 §19.8 [GroupGraphPattern]</see>.</remarks>
    CloseBrace,

    /// <summary>The multiplication operator / path zero-or-more / <c>SELECT *</c> star <c>*</c>.</summary>
    /// <remarks>SPARQL <c>'*'</c> of <c>MultiplicativeExpression</c> / <c>PathMod</c> / <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rMultiplicativeExpression">SPARQL 1.2 §19.8 [MultiplicativeExpression]</see>.</remarks>
    Star,

    /// <summary>The addition operator / path one-or-more <c>+</c>.</summary>
    /// <remarks>SPARQL <c>'+'</c> of <c>AdditiveExpression</c> / <c>PathMod</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAdditiveExpression">SPARQL 1.2 §19.8 [AdditiveExpression]</see>.</remarks>
    Plus,

    /// <summary>The subtraction / unary-minus operator <c>-</c>.</summary>
    /// <remarks>SPARQL <c>'-'</c> of <c>AdditiveExpression</c> / <c>UnaryExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAdditiveExpression">SPARQL 1.2 §19.8 [AdditiveExpression]</see>.</remarks>
    Minus,

    /// <summary>The division operator / path-sequence separator <c>/</c>.</summary>
    /// <remarks>SPARQL <c>'/'</c> of <c>MultiplicativeExpression</c> / <c>PathSequence</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathSequence">SPARQL 1.2 §19.8 [PathSequence]</see>.</remarks>
    Slash,

    /// <summary>The path-alternative separator <c>|</c>.</summary>
    /// <remarks>SPARQL <c>'|'</c> of <c>PathAlternative</c> / <c>PathNegatedPropertySet</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathAlternative">SPARQL 1.2 §19.8 [PathAlternative]</see>.</remarks>
    Pipe,

    /// <summary>The logical-negation operator / negated-property-set marker <c>!</c>.</summary>
    /// <remarks>SPARQL <c>'!'</c> of <c>UnaryExpression</c> / <c>PathPrimary</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rUnaryExpression">SPARQL 1.2 §19.8 [UnaryExpression]</see>.</remarks>
    Bang,

    /// <summary>The path zero-or-one quantifier <c>?</c> (distinct from a variable's leading <c>?</c>).</summary>
    /// <remarks>SPARQL <c>'?'</c> of <c>PathMod</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathMod">SPARQL 1.2 §19.8 [PathMod]</see>.</remarks>
    Question,

    /// <summary>The inverse-path operator <c>^</c>.</summary>
    /// <remarks>SPARQL <c>'^'</c> of <c>PathEltOrInverse</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPathEltOrInverse">SPARQL 1.2 §19.8 [PathEltOrInverse]</see>.</remarks>
    Caret,

    /// <summary>The equality operator <c>=</c>.</summary>
    /// <remarks>SPARQL <c>'='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    Equals,

    /// <summary>The inequality operator <c>!=</c>.</summary>
    /// <remarks>SPARQL <c>'!='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    NotEquals,

    /// <summary>The less-than operator <c>&lt;</c> (disambiguated from an IRI by context).</summary>
    /// <remarks>SPARQL <c>'&lt;'</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    LessThan,

    /// <summary>The less-than-or-equal operator <c>&lt;=</c>.</summary>
    /// <remarks>SPARQL <c>'&lt;='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    LessOrEqual,

    /// <summary>The greater-than operator <c>&gt;</c>.</summary>
    /// <remarks>SPARQL <c>'&gt;'</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    GreaterThan,

    /// <summary>The greater-than-or-equal operator <c>&gt;=</c>.</summary>
    /// <remarks>SPARQL <c>'&gt;='</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    GreaterOrEqual,

    /// <summary>The logical-and operator <c>&amp;&amp;</c>.</summary>
    /// <remarks>SPARQL <c>'&amp;&amp;'</c> of <c>ConditionalAndExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConditionalAndExpression">SPARQL 1.2 §19.8 [ConditionalAndExpression]</see>.</remarks>
    LogicalAnd,

    /// <summary>The logical-or operator <c>||</c>.</summary>
    /// <remarks>SPARQL <c>'||'</c> of <c>ConditionalOrExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConditionalOrExpression">SPARQL 1.2 §19.8 [ConditionalOrExpression]</see>.</remarks>
    LogicalOr,

    /// <summary>The reified-triple start <c>&lt;&lt;</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>'&lt;&lt;'</c> of <c>ReifiedTriple</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifiedTriple">SPARQL 1.2 §19.8 [ReifiedTriple]</see>.</remarks>
    OpenReifiedTriple,

    /// <summary>The reified-triple end <c>&gt;&gt;</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>'&gt;&gt;'</c> of <c>ReifiedTriple</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifiedTriple">SPARQL 1.2 §19.8 [ReifiedTriple]</see>.</remarks>
    CloseReifiedTriple,

    /// <summary>The triple-term start <c>&lt;&lt;(</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>'&lt;&lt;('</c> of <c>TripleTerm</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTerm">SPARQL 1.2 §19.8 [TripleTerm]</see>.</remarks>
    OpenTripleTerm,

    /// <summary>The triple-term end <c>)&gt;&gt;</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>')&gt;&gt;'</c> of <c>TripleTerm</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rTripleTerm">SPARQL 1.2 §19.8 [TripleTerm]</see>.</remarks>
    CloseTripleTerm,

    /// <summary>The reifier marker <c>~</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>'~'</c> of <c>Reifier</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rReifier">SPARQL 1.2 §19.8 [Reifier]</see>.</remarks>
    Tilde,

    /// <summary>The annotation-block start <c>{|</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>'{|'</c> of <c>AnnotationBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotationBlock">SPARQL 1.2 §19.8 [AnnotationBlock]</see>.</remarks>
    OpenAnnotation,

    /// <summary>The annotation-block end <c>|}</c> (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>'|}'</c> of <c>AnnotationBlock</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAnnotationBlock">SPARQL 1.2 §19.8 [AnnotationBlock]</see>.</remarks>
    CloseAnnotation,

    /// <summary>The <c>VERSION</c> prologue keyword (RDF 1.2).</summary>
    /// <remarks>SPARQL <c>VersionDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rVersionDecl">SPARQL 1.2 §19.8 [VersionDecl]</see>.</remarks>
    VersionKeyword,

    /// <summary>The <c>BASE</c> keyword.</summary>
    /// <remarks>SPARQL <c>BaseDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBaseDecl">SPARQL 1.2 §19.8 [BaseDecl]</see>.</remarks>
    BaseKeyword,

    /// <summary>The <c>PREFIX</c> keyword.</summary>
    /// <remarks>SPARQL <c>PrefixDecl</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rPrefixDecl">SPARQL 1.2 §19.8 [PrefixDecl]</see>.</remarks>
    PrefixKeyword,

    /// <summary>The <c>SELECT</c> keyword.</summary>
    /// <remarks>SPARQL <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
    SelectKeyword,

    /// <summary>The <c>CONSTRUCT</c> keyword.</summary>
    /// <remarks>SPARQL <c>ConstructQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConstructQuery">SPARQL 1.2 §19.8 [ConstructQuery]</see>.</remarks>
    ConstructKeyword,

    /// <summary>The <c>ASK</c> keyword.</summary>
    /// <remarks>SPARQL <c>AskQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAskQuery">SPARQL 1.2 §19.8 [AskQuery]</see>.</remarks>
    AskKeyword,

    /// <summary>The <c>DESCRIBE</c> keyword.</summary>
    /// <remarks>SPARQL <c>DescribeQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDescribeQuery">SPARQL 1.2 §19.8 [DescribeQuery]</see>.</remarks>
    DescribeKeyword,

    /// <summary>The <c>WHERE</c> keyword.</summary>
    /// <remarks>SPARQL <c>WhereClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rWhereClause">SPARQL 1.2 §19.8 [WhereClause]</see>.</remarks>
    WhereKeyword,

    /// <summary>The <c>FROM</c> keyword.</summary>
    /// <remarks>SPARQL <c>DatasetClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDatasetClause">SPARQL 1.2 §19.8 [DatasetClause]</see>.</remarks>
    FromKeyword,

    /// <summary>The <c>NAMED</c> keyword.</summary>
    /// <remarks>SPARQL <c>NamedGraphClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rNamedGraphClause">SPARQL 1.2 §19.8 [NamedGraphClause]</see>.</remarks>
    NamedKeyword,

    /// <summary>The <c>ORDER</c> keyword.</summary>
    /// <remarks>SPARQL <c>OrderClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOrderClause">SPARQL 1.2 §19.8 [OrderClause]</see>.</remarks>
    OrderKeyword,

    /// <summary>The <c>BY</c> keyword.</summary>
    /// <remarks>SPARQL <c>'BY'</c> of <c>OrderClause</c> / <c>GroupClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOrderClause">SPARQL 1.2 §19.8 [OrderClause]</see>.</remarks>
    ByKeyword,

    /// <summary>The <c>LIMIT</c> keyword.</summary>
    /// <remarks>SPARQL <c>LimitClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rLimitClause">SPARQL 1.2 §19.8 [LimitClause]</see>.</remarks>
    LimitKeyword,

    /// <summary>The <c>OFFSET</c> keyword.</summary>
    /// <remarks>SPARQL <c>OffsetClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOffsetClause">SPARQL 1.2 §19.8 [OffsetClause]</see>.</remarks>
    OffsetKeyword,

    /// <summary>The <c>DISTINCT</c> keyword.</summary>
    /// <remarks>SPARQL <c>'DISTINCT'</c> of <c>SelectClause</c> / <c>Aggregate</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
    DistinctKeyword,

    /// <summary>The <c>REDUCED</c> keyword.</summary>
    /// <remarks>SPARQL <c>'REDUCED'</c> of <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
    ReducedKeyword,

    /// <summary>The <c>OPTIONAL</c> keyword.</summary>
    /// <remarks>SPARQL <c>OptionalGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOptionalGraphPattern">SPARQL 1.2 §19.8 [OptionalGraphPattern]</see>.</remarks>
    OptionalKeyword,

    /// <summary>The <c>UNION</c> keyword.</summary>
    /// <remarks>SPARQL <c>GroupOrUnionGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupOrUnionGraphPattern">SPARQL 1.2 §19.8 [GroupOrUnionGraphPattern]</see>.</remarks>
    UnionKeyword,

    /// <summary>The <c>MINUS</c> keyword (distinct from the <c>-</c> operator <see cref="Minus"/>).</summary>
    /// <remarks>SPARQL <c>MinusGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rMinusGraphPattern">SPARQL 1.2 §19.8 [MinusGraphPattern]</see>.</remarks>
    MinusKeyword,

    /// <summary>The <c>FILTER</c> keyword.</summary>
    /// <remarks>SPARQL <c>Filter</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rFilter">SPARQL 1.2 §19.8 [Filter]</see>.</remarks>
    FilterKeyword,

    /// <summary>The <c>BIND</c> keyword.</summary>
    /// <remarks>SPARQL <c>Bind</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBind">SPARQL 1.2 §19.8 [Bind]</see>.</remarks>
    BindKeyword,

    /// <summary>The <c>AS</c> keyword.</summary>
    /// <remarks>SPARQL <c>'AS'</c> of <c>Bind</c> / <c>SelectClause</c> / <c>GroupCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBind">SPARQL 1.2 §19.8 [Bind]</see>.</remarks>
    AsKeyword,

    /// <summary>The <c>VALUES</c> keyword.</summary>
    /// <remarks>SPARQL <c>ValuesClause</c> / <c>InlineData</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rValuesClause">SPARQL 1.2 §19.8 [ValuesClause]</see>.</remarks>
    ValuesKeyword,

    /// <summary>The <c>UNDEF</c> keyword used inside inline data blocks.</summary>
    /// <remarks>SPARQL <c>'UNDEF'</c> of <c>DataBlockValue</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDataBlockValue">SPARQL 1.2 §19.8 [DataBlockValue]</see>.</remarks>
    UndefKeyword,

    /// <summary>The <c>GROUP</c> keyword.</summary>
    /// <remarks>SPARQL <c>GroupClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGroupClause">SPARQL 1.2 §19.8 [GroupClause]</see>.</remarks>
    GroupKeyword,

    /// <summary>The <c>HAVING</c> keyword.</summary>
    /// <remarks>SPARQL <c>HavingClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rHavingClause">SPARQL 1.2 §19.8 [HavingClause]</see>.</remarks>
    HavingKeyword,

    /// <summary>The <c>GRAPH</c> keyword.</summary>
    /// <remarks>SPARQL <c>GraphGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rGraphGraphPattern">SPARQL 1.2 §19.8 [GraphGraphPattern]</see>.</remarks>
    GraphKeyword,

    /// <summary>The <c>SERVICE</c> keyword.</summary>
    /// <remarks>SPARQL <c>ServiceGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rServiceGraphPattern">SPARQL 1.2 §19.8 [ServiceGraphPattern]</see>.</remarks>
    ServiceKeyword,

    /// <summary>The <c>SILENT</c> keyword.</summary>
    /// <remarks>SPARQL <c>'SILENT'</c> of <c>ServiceGraphPattern</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rServiceGraphPattern">SPARQL 1.2 §19.8 [ServiceGraphPattern]</see>.</remarks>
    SilentKeyword,

    /// <summary>The <c>IN</c> keyword.</summary>
    /// <remarks>SPARQL <c>'IN'</c> of <c>RelationalExpression</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    InKeyword,

    /// <summary>The <c>NOT</c> keyword (pairs with <c>IN</c> and <c>EXISTS</c>).</summary>
    /// <remarks>SPARQL <c>'NOT'</c> of <c>RelationalExpression</c> / <c>NotExistsFunc</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rRelationalExpression">SPARQL 1.2 §19.8 [RelationalExpression]</see>.</remarks>
    NotKeyword,

    /// <summary>The <c>EXISTS</c> keyword.</summary>
    /// <remarks>SPARQL <c>ExistsFunc</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rExistsFunc">SPARQL 1.2 §19.8 [ExistsFunc]</see>.</remarks>
    ExistsKeyword,

    /// <summary>The <c>ASC</c> ordering keyword.</summary>
    /// <remarks>SPARQL <c>'ASC'</c> of <c>OrderCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOrderCondition">SPARQL 1.2 §19.8 [OrderCondition]</see>.</remarks>
    AscKeyword,

    /// <summary>The <c>DESC</c> ordering keyword.</summary>
    /// <remarks>SPARQL <c>'DESC'</c> of <c>OrderCondition</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rOrderCondition">SPARQL 1.2 §19.8 [OrderCondition]</see>.</remarks>
    DescKeyword,

    /// <summary>The <c>SEPARATOR</c> keyword used inside <c>GROUP_CONCAT</c>.</summary>
    /// <remarks>SPARQL <c>'SEPARATOR'</c> of <c>Aggregate</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</remarks>
    SeparatorKeyword,

    /// <summary>The <c>INSERT</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rInsertData">SPARQL 1.2 Update §19.8 [InsertData/Modify]</see>.</summary>
    InsertKeyword,

    /// <summary>The <c>DELETE</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rDeleteData">SPARQL 1.2 Update §19.8 [DeleteData/DeleteWhere/Modify]</see>.</summary>
    DeleteKeyword,

    /// <summary>The <c>DATA</c> keyword (<c>INSERT DATA</c> / <c>DELETE DATA</c>). See <see href="https://www.w3.org/TR/sparql12-update/#rInsertData">SPARQL 1.2 Update §19.8 [InsertData]</see>.</summary>
    DataKeyword,

    /// <summary>The <c>LOAD</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rLoad">SPARQL 1.2 Update §19.8 [Load]</see>.</summary>
    LoadKeyword,

    /// <summary>The <c>CLEAR</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rClear">SPARQL 1.2 Update §19.8 [Clear]</see>.</summary>
    ClearKeyword,

    /// <summary>The <c>DROP</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rDrop">SPARQL 1.2 Update §19.8 [Drop]</see>.</summary>
    DropKeyword,

    /// <summary>The <c>CREATE</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rCreate">SPARQL 1.2 Update §19.8 [Create]</see>.</summary>
    CreateKeyword,

    /// <summary>The <c>ADD</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rAdd">SPARQL 1.2 Update §19.8 [Add]</see>.</summary>
    AddKeyword,

    /// <summary>The <c>MOVE</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rMove">SPARQL 1.2 Update §19.8 [Move]</see>.</summary>
    MoveKeyword,

    /// <summary>The <c>COPY</c> keyword (SPARQL Update). See <see href="https://www.w3.org/TR/sparql12-update/#rCopy">SPARQL 1.2 Update §19.8 [Copy]</see>.</summary>
    CopyKeyword,

    /// <summary>The <c>INTO</c> keyword (<c>LOAD … INTO GRAPH</c>). See <see href="https://www.w3.org/TR/sparql12-update/#rLoad">SPARQL 1.2 Update §19.8 [Load]</see>.</summary>
    IntoKeyword,

    /// <summary>The <c>TO</c> keyword (<c>ADD/MOVE/COPY … TO …</c>). See <see href="https://www.w3.org/TR/sparql12-update/#rAdd">SPARQL 1.2 Update §19.8 [Add]</see>.</summary>
    ToKeyword,

    /// <summary>The <c>WITH</c> keyword (<c>WITH … DELETE … INSERT … WHERE</c>). See <see href="https://www.w3.org/TR/sparql12-update/#rModify">SPARQL 1.2 Update §19.8 [Modify]</see>.</summary>
    WithKeyword,

    /// <summary>The <c>USING</c> keyword (<c>USING</c> / <c>USING NAMED</c> in a Modify). See <see href="https://www.w3.org/TR/sparql12-update/#rUsingClause">SPARQL 1.2 Update §19.8 [UsingClause]</see>.</summary>
    UsingKeyword,

    /// <summary>The <c>DEFAULT</c> keyword (a graph reference to the default graph). See <see href="https://www.w3.org/TR/sparql12-update/#rGraphRefAll">SPARQL 1.2 Update §19.8 [GraphRefAll]</see>.</summary>
    DefaultKeyword,

    /// <summary>The <c>ALL</c> keyword (a graph reference to every graph). See <see href="https://www.w3.org/TR/sparql12-update/#rGraphRefAll">SPARQL 1.2 Update §19.8 [GraphRefAll]</see>.</summary>
    AllKeyword,

    /// <summary>
    /// A reserved built-in-function name (a <c>BuiltInCall</c> terminal such as
    /// <c>STR</c>, <c>STRLEN</c>, <c>BOUND</c>, <c>IF</c>, <c>COALESCE</c>,
    /// <c>REGEX</c>, <c>IRI</c>, <c>isIRI</c>). The decoded payload carries the
    /// canonical upper-case name so the parser dispatches without re-scanning.
    /// </summary>
    /// <remarks>SPARQL <c>BuiltInCall</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rBuiltInCall">SPARQL 1.2 §19.8 [BuiltInCall]</see>.</remarks>
    BuiltInFunctionName,

    /// <summary>
    /// A reserved aggregate-function name (<c>COUNT</c>, <c>SUM</c>, <c>MIN</c>,
    /// <c>MAX</c>, <c>AVG</c>, <c>SAMPLE</c>, <c>GROUP_CONCAT</c>). The decoded
    /// payload carries the canonical upper-case name.
    /// </summary>
    /// <remarks>SPARQL <c>Aggregate</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAggregate">SPARQL 1.2 §19.8 [Aggregate]</see>.</remarks>
    AggregateFunctionName,

    /// <summary>
    /// A run of bytes the lexer could not tokenise.
    /// </summary>
    /// <remarks>
    /// Recovery emits this token in place of throwing, with a <see cref="SparqlToken.Span"/> covering
    /// the offending bytes; the matching <see cref="SparqlLexDiagnostic"/> is recorded in
    /// <see cref="SparqlLexer.Diagnostics"/>. The parser treats an <see cref="Error"/> token as a
    /// resync point rather than a grammar terminal. See
    /// <see href="https://www.w3.org/TR/sparql12-query/#sparqlGrammar">SPARQL 1.2 §19 (grammar)</see>.
    /// </remarks>
    Error,

    /// <summary>End of the input stream.</summary>
    /// <remarks>SPARQL end-of-input sentinel for <c>Query</c> / <c>QueryUnit</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rQueryUnit">SPARQL 1.2 §19.8 [QueryUnit]</see>.</remarks>
    EndOfInput
}
