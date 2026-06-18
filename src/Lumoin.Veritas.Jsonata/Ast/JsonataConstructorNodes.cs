using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Jsonata.Ast;

/// <summary>
/// An array constructor <c>[e0, e1, ...]</c>: builds a JSON array from N element expressions evaluated
/// under the current focus. The constructed array is kept verbatim — a singleton <c>[5]</c> stays the
/// one-element array <c>[5]</c> rather than auto-unwrapping to its element. A defined element value is
/// appended with one-level flatten semantics (an array-valued element spreads its items into the result)
/// unless the element's own AST node is itself an <see cref="ArrayConstructorExpression"/>, in which case
/// the value is kept whole as a single nested element; an undefined element value is skipped. A constructor
/// used as a path step carries the <see cref="ConsArray"/> marker, set by the parser when the constructor is
/// either side of a dot/map step, so the following step keeps its value whole rather than flattening it.
/// This is the first variadic node in the hierarchy.
/// </summary>
/// <param name="Span">The source extent from the opening <c>[</c> through the closing <c>]</c>.</param>
/// <param name="Elements">The element expressions, in source order; empty for the empty array <c>[]</c>.</param>
/// <param name="ConsArray">
/// Whether this constructor is a path step (the JSONata <c>consarray</c> flag): when set, the constructed
/// array carries the <c>cons</c> marker so the enclosing dot/map step keeps it whole instead of flattening
/// one level. The parser sets it when the constructor appears as a step of a path, so nested constructor
/// steps (<c>a.[b.[c]]</c>) compose — each level produces a cons array the next-outer step keeps whole.
/// </param>
/// <remarks>
/// <para>JSONata array constructor <c>[ ... ]</c>. See <see href="https://docs.jsonata.org/construction">the JSONata construction reference</see>.</para>
/// </remarks>
[DebuggerDisplay("([] {Elements.Count})")]
public sealed record ArrayConstructorExpression(SourceSpan Span, IReadOnlyList<JsonataExpression> Elements, bool ConsArray = false) : JsonataExpression(Span);

/// <summary>
/// A keep-array marker <c>source[]</c>: the empty-bracket postfix marker (the JSONata <c>keepArray</c> flag)
/// that forces the marked path step's result to stay a JSON array, so a singleton does not auto-unwrap to its
/// element. The empty brackets are not a filter — they select nothing and remove nothing; they only set the
/// keep-singleton marker on the source's result. The marker rides on the resulting array value, so it
/// survives the enclosing dot/map steps to the path's final normalization (any <c>[]</c> in a path keeps the
/// whole path's singleton result an array, matching the reference's path-level <c>keepSingletonArray</c>).
/// </summary>
/// <param name="Span">The source extent from the source through the closing <c>]</c> of the empty brackets.</param>
/// <param name="Source">The step whose result is marked keep-array.</param>
/// <remarks>
/// <para>JSONata keep-array marker <c>[]</c>. See <see href="https://docs.jsonata.org/processing#sequences">the JSONata sequences reference</see>.</para>
/// </remarks>
[DebuggerDisplay("([] keepArray)")]
public sealed record KeepArrayExpression(SourceSpan Span, JsonataExpression Source) : JsonataExpression(Span);

/// <summary>
/// An object constructor <c>{k0:v0, k1:v1, ...}</c>: builds a JSON object by grouping an input sequence and
/// evaluating N key/value expression pairs over the groups. Evaluation always groups — a single-value input
/// is one group (yielding one object), a sequence input buckets its items by each pair's key. A key
/// expression is evaluated under the focus rebound to each item; an undefined key skips that pair for that
/// item, a non-string key is a key-type error. A value expression is evaluated once per group, under the
/// focus rebound to the grouped sub-sequence, so a value can aggregate over its whole group; an undefined
/// value omits its member. Keys are kept in first-seen order. This is a variadic node whose children are its
/// optional grouping source followed by its member key and value expressions.
/// </summary>
/// <param name="Span">The source extent from the opening <c>{</c> through the closing <c>}</c>.</param>
/// <param name="Members">The key/value expression pairs, in source order; empty for the empty object <c>{}</c>.</param>
/// <param name="Source">
/// The grouping-source expression for the led path-step form <c>path{ ... }</c>, whose result is the input
/// sequence the members group over; <see langword="null"/> for the prefix form <c>{ ... }</c>, which groups
/// the current focus instead.
/// </param>
/// <remarks>
/// <para>JSONata object constructor <c>{ ... }</c>. See <see href="https://docs.jsonata.org/construction">the JSONata construction reference</see>.</para>
/// <para>
/// Both grammar positions build this one node: the prefix (nud) form <c>{ ... }</c> carries a
/// <see langword="null"/> <see cref="Source"/> and groups the current focus, while the led path-step form
/// <c>path{ ... }</c> carries the preceding path as <see cref="Source"/> and groups that path's result. The
/// member parsing and the group-by evaluation are identical for both; only the input sequence differs.
/// </para>
/// </remarks>
[DebuggerDisplay("({{}} {Members.Count})")]
public sealed record ObjectConstructorExpression(SourceSpan Span, IReadOnlyList<(JsonataExpression Key, JsonataExpression Value)> Members, JsonataExpression? Source = null) : JsonataExpression(Span);
