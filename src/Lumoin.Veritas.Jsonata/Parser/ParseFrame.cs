using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Lexer;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// One frame on <see cref="JsonataParser"/>'s explicit work stack. Carries the production the frame is
/// in, the in-progress accumulators it has built so far, and a stage counter the driver uses to advance
/// the production one step at a time.
/// </summary>
/// <remarks>
/// <para>
/// Fields are intentionally optional and indexed by <see cref="Kind"/>: a single frame layout supports
/// every production the driver knows about, avoiding a frame-type hierarchy whose dispatch would still
/// land in a switch.
/// </para>
/// <para>
/// Because a frame is a heap object whose fields survive between <see cref="ParseStatus.NeedMore"/>
/// suspensions, the parser resumes a partially built production from exactly where it stopped when more
/// tokens arrive.
/// </para>
/// </remarks>
[DebuggerDisplay("{Kind} stage={Stage} {StartSpan}")]
internal sealed class ParseFrame
{
    /// <summary>Gets or sets the production this frame represents.</summary>
    public ParseFrameKind Kind { get; set; }

    /// <summary>Gets or sets the sub-stage within <see cref="Kind"/> the driver should resume at.</summary>
    public int Stage { get; set; }

    /// <summary>Gets or sets the source span of the first token that started this production.</summary>
    public SourceSpan StartSpan { get; set; }

    /// <summary>Gets or sets the minimum binding power an <see cref="ParseFrameKind.Expression"/> frame absorbs (the precedence-climbing bound).</summary>
    public int MinBindingPower { get; set; }

    /// <summary>Gets or sets the left-hand expression an <see cref="ParseFrameKind.Expression"/> frame has built so far.</summary>
    public JsonataExpression? Left { get; set; }

    /// <summary>Gets or sets the pending operator token kind an <see cref="ParseFrameKind.Expression"/> frame is combining.</summary>
    public JsonataTokenKind OperatorKind { get; set; }

    /// <summary>Gets or sets the source span the pending operator (binary or unary) was at, used to span the combined node.</summary>
    public SourceSpan OperatorSpan { get; set; }

    /// <summary>Gets or sets the parsed true branch of a conditional an <see cref="ParseFrameKind.Expression"/> frame is assembling, held while its false branch is parsed.</summary>
    public JsonataExpression? ConditionalWhenTrue { get; set; }

    /// <summary>Gets or sets the parsed location pattern of a transform <c>| location | update [, delete] |</c> an <see cref="ParseFrameKind.Expression"/> frame is assembling, held while its update and delete clauses are parsed.</summary>
    public JsonataExpression? TransformPattern { get; set; }

    /// <summary>Gets or sets the parsed update clause of a transform an <see cref="ParseFrameKind.Expression"/> frame is assembling, held while its optional delete clause is parsed.</summary>
    public JsonataExpression? TransformUpdate { get; set; }

    /// <summary>Gets or sets the element expressions a variadic <see cref="ParseFrameKind.ElementList"/> frame accumulates across stages; <see langword="null"/> for non-list frames.</summary>
    public List<JsonataExpression>? Elements { get; set; }

    /// <summary>Gets or sets the key/value member pairs a variadic <see cref="ParseFrameKind.ObjectMemberList"/> frame accumulates across stages; <see langword="null"/> for non-member-list frames.</summary>
    public List<(JsonataExpression Key, JsonataExpression Value)>? Members { get; set; }

    /// <summary>Gets or sets the parsed key of an object member an <see cref="ParseFrameKind.ObjectMemberList"/> frame holds between its <c>:</c> and its value; <see langword="null"/> when no key is pending.</summary>
    public JsonataExpression? PendingKey { get; set; }

    /// <summary>Gets or sets the statement expressions a variadic <see cref="ParseFrameKind.BlockStatementList"/> frame accumulates across stages; <see langword="null"/> for non-block frames.</summary>
    public List<JsonataExpression>? Statements { get; set; }

    /// <summary>Gets or sets the parameter names a <see cref="ParseFrameKind.LambdaDefinition"/> frame accumulates while collecting a lambda's <c>$name</c> parameters; <see langword="null"/> for non-lambda frames.</summary>
    public List<Utf8String>? Parameters { get; set; }

    /// <summary>Gets or sets the buffer a <see cref="ParseFrameKind.LambdaDefinition"/> frame reassembles a lambda's bracketed type signature into, one token's value at a time across <see cref="ParseStatus.NeedMore"/> suspensions; <see langword="null"/> until the signature scan begins.</summary>
    public System.Text.StringBuilder? SignatureBuffer { get; set; }

    /// <summary>Gets or sets the running angle-bracket depth a <see cref="ParseFrameKind.LambdaDefinition"/> frame tracks while scanning a signature: it starts at one on the opening <c>&lt;</c>, rises on each nested <c>&lt;</c>, falls on each <c>&gt;</c>, and the scan ends when it returns to zero. Carried on the frame so the scan resumes across suspensions.</summary>
    public int SignatureDepth { get; set; }

    /// <summary>Gets or sets the lambda keyword's lexeme (<c>function</c> or <c>λ</c>) a <see cref="ParseFrameKind.LambdaDefinition"/> frame carries so a keyword not immediately followed by <c>(</c> recovers as a plain field-name reference (the reference tokenises <c>function</c> / <c>λ</c> as a name and only promotes it to a lambda when an opening <c>(</c> follows); the empty <see cref="Utf8String"/> for non-lambda frames.</summary>
    public Utf8String KeywordLexeme { get; set; }

    /// <summary>Gets or sets the argument expressions a variadic <see cref="ParseFrameKind.ArgumentList"/> frame accumulates across stages; <see langword="null"/> for non-argument-list frames.</summary>
    public List<JsonataExpression>? Arguments { get; set; }

    /// <summary>Gets or sets the order-by terms a variadic <see cref="ParseFrameKind.SortTermList"/> frame accumulates across stages; <see langword="null"/> for non-sort-list frames.</summary>
    public List<SortTerm>? SortTerms { get; set; }

    /// <summary>Gets or sets the direction prefix a <see cref="ParseFrameKind.SortTermList"/> frame read for the term it is currently parsing, held until the term's key expression is popped.</summary>
    public SortDirection PendingSortDirection { get; set; }
}
