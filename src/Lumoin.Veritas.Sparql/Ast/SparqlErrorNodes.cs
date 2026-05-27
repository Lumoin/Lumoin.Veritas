using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Lexer;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The error-node placeholders the parser emits during recovery, one per product base the work stack
/// hands up (graph patterns, expressions, paths, terms, graph designators, query-form heads, and
/// annotations). Each stands in for a construct the parser could not parse, slotting into any parent
/// that expected that base so the existing value flow carries it up — no multi-frame unwind.
/// </summary>
/// <remarks>
/// <para>
/// The contributing diagnostics live in the parse-level <see cref="Lumoin.Veritas.Core.Diagnostics.DiagnosticBag"/>
/// (surfaced via <c>ParseToResult</c>); <c>DiagnosticCodes</c> lists the codes that fired while a node was
/// built and <c>SkippedTokens</c> records the tokens the resync logic consumed, for tooling that preserves
/// trivia. <c>ExpectedProduction</c> names the grammar production the parser expected — the deliberate data
/// hook for an editor's quick-fix layer. An error node contributes nothing to the algebra translation.
/// </para>
/// <para>
/// These mirror the Turtle parser's <c>ErrorStatement</c>/<c>ErrorTerm</c>/<c>ErrorAnnotation</c>; SPARQL
/// needs more variants only because its AST products share no single base.
/// </para>
/// </remarks>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorGraphPattern {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorGraphPattern(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : GraphPattern(Span);

/// <summary>A query-form head (SELECT/CONSTRUCT/ASK/DESCRIBE) the parser could not parse.</summary>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorQueryForm {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorQueryForm(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : QueryForm(Span);

/// <summary>An expression the parser could not parse.</summary>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorExpression {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorExpression(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : ExpressionNode(Span);

/// <summary>A property-path expression the parser could not parse.</summary>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorPropertyPath {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorPropertyPath(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : PropertyPathExpression;

/// <summary>A triple-pattern term (in subject, predicate, or object position) the parser could not parse.</summary>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorTriplePatternTerm {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorTriplePatternTerm(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : TriplePatternTerm;

/// <summary>A graph designator (the <c>VarOrIri</c> of a <c>GRAPH</c>/<c>SERVICE</c>) the parser could not parse.</summary>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorGraphTerm {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorGraphTerm(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : GraphTerm(Span);

/// <summary>An RDF 1.2 annotation (a reifier <c>~</c> or an annotation block <c>{| … |}</c>) the parser could not parse.</summary>
/// <param name="Span">The source extent covering the point of failure and the tokens skipped to resynchronise.</param>
/// <param name="ExpectedProduction">The canonical name of the grammar production the parser expected here.</param>
/// <param name="DiagnosticCodes">The diagnostic codes that fired while constructing this node.</param>
/// <param name="SkippedTokens">The tokens the resync logic consumed to settle on this node's span.</param>
[DebuggerDisplay("ErrorAnnotation {ExpectedProduction} ({SkippedTokens.Length} skipped)")]
public sealed record ErrorAnnotation(
    SourceSpan Span,
    Utf8String ExpectedProduction,
    ImmutableArray<Utf8String> DiagnosticCodes,
    ImmutableArray<SparqlToken> SkippedTokens) : Annotation(Span);
