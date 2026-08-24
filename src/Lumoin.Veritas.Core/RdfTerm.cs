using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Lumoin.Veritas.Core;

/// <summary>
/// The base type for all RDF terms: named nodes (IRIs), blank nodes, literals, and triple terms.
/// </summary>
/// <remarks>
/// <para>
/// RDF terms are the building blocks of RDF graphs. Every position in a triple (subject,
/// predicate, object) is occupied by an RDF term. The concrete subtypes correspond to
/// the four kinds of RDF term defined in
/// <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Syntax">RDF 1.2 Concepts §3</see>.
/// </para>
/// <para>
/// Use pattern matching to dispatch on the concrete type:
/// </para>
/// <code>
/// var label = term switch
/// {
///     NamedNode(var iri) => iri.ToString(),
///     BlankNode(var id) => $"_:{id}",
///     Literal(var value, _, _, _) => value.ToString(),
///     TripleTerm(var s, var p, var o) => $"&lt;&lt;{s} {p} {o}&gt;&gt;",
///     _ => throw new UnreachableException()
/// };
/// </code>
/// </remarks>
public abstract record RdfTerm;

/// <summary>
/// An IRI-identified resource in the RDF graph.
/// </summary>
/// <remarks>
/// <para>
/// Named nodes (also called IRI nodes) are the primary mechanism for identifying
/// resources. The IRI is stored as raw UTF-8 bytes for zero-copy processing.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-IRIs">RDF 1.2 Concepts §3.1</see>.
/// </para>
/// </remarks>
/// <param name="Iri">The IRI identifying this resource.</param>
[DebuggerDisplay("<{Iri}>")]
public sealed record NamedNode(Utf8String Iri): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"<{Iri}>";
    }
}

/// <summary>
/// A locally-scoped identifier that does not persist beyond the document in which it appears.
/// </summary>
/// <remarks>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-blank-nodes">RDF 1.2 Concepts §3.4</see>.
/// </remarks>
/// <param name="Label">The blank node identifier, without the <c>_:</c> prefix.</param>
[DebuggerDisplay("_:{Label}")]
public sealed record BlankNode(Utf8String Label): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"_:{Label}";
    }
}

/// <summary>
/// The base text direction for directional language-tagged strings.
/// </summary>
/// <remarks>
/// <para>
/// Introduced in RDF 1.2 to support the correct rendering of bidirectional text.
/// A directional language-tagged string contains a base direction component
/// that establishes the initial text direction for presentation by a user agent.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Literal">RDF 1.2 Concepts §3.3</see>.
/// </para>
/// </remarks>
public enum TextDirection
{
    /// <summary>Left-to-right base direction.</summary>
    Ltr,

    /// <summary>Right-to-left base direction.</summary>
    Rtl
}

/// <summary>
/// The canonical lexical tokens for <see cref="TextDirection"/> (the RDF 1.2 base directions <c>"ltr"</c> and
/// <c>"rtl"</c>) and the conversions between the enum and those tokens. The tokens are defined here once so every
/// serializer, parser, and expression evaluator references the same well-known constants rather than re-typing the
/// raw strings.
/// </summary>
/// <remarks>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Literal">RDF 1.2 Concepts §3.3</see>.
/// </remarks>
public static class TextDirections
{
    /// <summary>The canonical lexical token for <see cref="TextDirection.Ltr"/> — <c>"ltr"</c>. A <c>static readonly</c> instance (not a <c>const</c>) so <see cref="GetCanonicalizedValue"/> can hand back one stable reference that callers compare by <see cref="object.ReferenceEquals"/>.</summary>
    public static readonly string Ltr = "ltr";

    /// <summary>The canonical lexical token for <see cref="TextDirection.Rtl"/> — <c>"rtl"</c>.</summary>
    public static readonly string Rtl = "rtl";

    /// <summary>The canonical <see cref="TextDirection.Ltr"/> token as a UTF-8 string (for byte-level serializers and the expression evaluator).</summary>
    public static Utf8String LtrUtf8 { get; } = Utf8Strings.From(Ltr);

    /// <summary>The canonical <see cref="TextDirection.Rtl"/> token as a UTF-8 string.</summary>
    public static Utf8String RtlUtf8 { get; } = Utf8Strings.From(Rtl);

    /// <summary>Returns whether a token is the <see cref="Ltr"/> token.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when <paramref name="token"/> equals <see cref="Ltr"/>.</returns>
    public static bool IsLtr(string token) => Equals(token, Ltr);

    /// <summary>Returns whether a token is the <see cref="Rtl"/> token.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns><see langword="true"/> when <paramref name="token"/> equals <see cref="Rtl"/>.</returns>
    public static bool IsRtl(string token) => Equals(token, Rtl);

    /// <summary>Returns the equivalent canonical static instance of a direction token, or the original instance when it is neither <see cref="Ltr"/> nor <see cref="Rtl"/>. Canonicalizing lets downstream comparisons take the <see cref="object.ReferenceEquals"/> fast-path.</summary>
    /// <param name="token">The token to canonicalize.</param>
    /// <returns>The canonical instance, or <paramref name="token"/> unchanged.</returns>
    public static string GetCanonicalizedValue(string token) => token switch
    {
        _ when IsLtr(token) => Ltr,
        _ when IsRtl(token) => Rtl,
        _ => token
    };

    /// <summary>Returns whether two direction tokens are the same, with a <see cref="object.ReferenceEquals"/> fast-path then an <b>ordinal</b> (case-sensitive) compare — RDF 1.2 base directions are lower-case <c>"ltr"</c>/<c>"rtl"</c> only (unlike case-insensitive RFC media types).</summary>
    /// <param name="tokenA">The first token.</param>
    /// <param name="tokenB">The second token.</param>
    /// <returns><see langword="true"/> when the tokens are equal.</returns>
    public static bool Equals(string tokenA, string tokenB)
    {
        return ReferenceEquals(tokenA, tokenB) || StringComparer.Ordinal.Equals(tokenA, tokenB);
    }

    /// <summary>Returns the canonical lexical token of a base direction as a UTF-8 string.</summary>
    /// <param name="direction">The base direction.</param>
    /// <returns>The <see cref="LtrUtf8"/> or <see cref="RtlUtf8"/> token.</returns>
    public static Utf8String ToToken(TextDirection direction)
    {
        return direction == TextDirection.Ltr ? LtrUtf8 : RtlUtf8;
    }

    /// <summary>Returns the canonical lexical token of a base direction as a string.</summary>
    /// <param name="direction">The base direction.</param>
    /// <returns>The <see cref="Ltr"/> or <see cref="Rtl"/> token.</returns>
    public static string ToText(TextDirection direction)
    {
        return direction == TextDirection.Ltr ? Ltr : Rtl;
    }

    /// <summary>Parses a base-direction lexical token (<c>"ltr"</c> or <c>"rtl"</c>) from its UTF-8 bytes.</summary>
    /// <param name="token">The candidate token's UTF-8 bytes.</param>
    /// <param name="direction">Receives the parsed direction on success.</param>
    /// <returns><see langword="true"/> when the token is a recognised base direction.</returns>
    public static bool TryParse(ReadOnlySpan<byte> token, out TextDirection direction)
    {
        if(token.SequenceEqual(LtrUtf8.Span))
        {
            direction = TextDirection.Ltr;

            return true;
        }

        if(token.SequenceEqual(RtlUtf8.Span))
        {
            direction = TextDirection.Rtl;

            return true;
        }

        direction = default;

        return false;
    }

    /// <summary>Parses a base-direction lexical token (<c>"ltr"</c> or <c>"rtl"</c>) from a string.</summary>
    /// <param name="token">The candidate token, or <see langword="null"/>.</param>
    /// <param name="direction">Receives the parsed direction on success.</param>
    /// <returns><see langword="true"/> when the token is a recognised base direction.</returns>
    public static bool TryParse(string? token, out TextDirection direction)
    {
        (bool recognised, direction) = token switch
        {
            not null when IsLtr(token) => (true, TextDirection.Ltr),
            not null when IsRtl(token) => (true, TextDirection.Rtl),
            _ => (false, default(TextDirection))
        };

        return recognised;
    }
}

/// <summary>
/// An RDF literal value with a datatype IRI, an optional language tag, and an optional base direction.
/// </summary>
/// <remarks>
/// <para>
/// Every literal has a datatype. Plain strings use <c>xsd:string</c>,
/// language-tagged strings use <c>rdf:langString</c>, and directional
/// language-tagged strings use <c>rdf:dirLangString</c> (RDF 1.2).
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-Graph-Literal">RDF 1.2 Concepts §3.3</see>.
/// </para>
/// </remarks>
/// <param name="Value">The lexical value of the literal.</param>
/// <param name="Datatype">The datatype IRI as a <see cref="NamedNode"/>.</param>
/// <param name="Language">
/// The optional BCP47 language tag. Present only when the datatype is
/// <c>rdf:langString</c> or <c>rdf:dirLangString</c>.
/// </param>
/// <param name="BaseDirection">
/// The optional base text direction for directional language-tagged strings (RDF 1.2).
/// Present only when the datatype is <c>rdf:dirLangString</c>.
/// </param>
[DebuggerDisplay("{ToString()}")]
public sealed record Literal(
    Utf8String Value,
    NamedNode Datatype,
    Utf8String? Language = null,
    TextDirection? BaseDirection = null): RdfTerm
{
    /// <inheritdoc/>
    public override string ToString()
    {
        if(Language is { } lang)
        {
            return $"\"{Value}\"@{lang}";
        }

        return $"\"{Value}\"^^<{Datatype.Iri}>";
    }
}

/// <summary>
/// An RDF triple used as a term in the object position of another triple.
/// </summary>
/// <remarks>
/// <para>
/// Triple terms are new in RDF 1.2. They enable statements about statements
/// without the indirection of classic reification. A triple term is an RDF
/// triple that can appear as the object of another triple, typically with
/// the predicate <c>rdf:reifies</c>.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/#section-triple-terms">RDF 1.2 Concepts §3.5</see>.
/// </para>
/// <para>
/// A triple term is not itself an RDF triple in the graph. It is a term that
/// denotes a triple. The components of a triple term follow the same constraints
/// as the components of an RDF triple: the subject must be a named node, blank node,
/// or triple term; the predicate must be a named node; the object can be any RDF term.
/// </para>
/// </remarks>
/// <param name="Subject">The subject of the denoted triple.</param>
/// <param name="Predicate">The predicate of the denoted triple.</param>
/// <param name="Object">The object of the denoted triple.</param>
[DebuggerDisplay("<<{Subject} {Predicate} {Object}>>")]
public sealed record TripleTerm(RdfTerm Subject, NamedNode Predicate, RdfTerm Object): RdfTerm
{
    /// <summary>
    /// Determines structural equality with another triple term. Replaces the compiler-synthesized recursive
    /// comparison with an explicit-stack walk over the quoted-triple spine (leaf components compare through their
    /// own non-recursive equality), so a deeply-nested term cannot overflow the call stack.
    /// </summary>
    /// <param name="other">The triple term to compare with.</param>
    /// <returns><see langword="true"/> when the terms are structurally equal.</returns>
    public bool Equals(TripleTerm? other)
    {
        if(ReferenceEquals(this, other))
        {
            return true;
        }

        if(other is null)
        {
            return false;
        }

        Stack<(RdfTerm Left, RdfTerm Right)> work = new();
        work.Push((this, other));

        while(work.Count > 0)
        {
            (RdfTerm left, RdfTerm right) = work.Pop();
            if(ReferenceEquals(left, right))
            {
                continue;
            }

            if(left is TripleTerm leftTriple && right is TripleTerm rightTriple)
            {
                if(!leftTriple.Predicate.Equals(rightTriple.Predicate))
                {
                    return false;
                }

                work.Push((leftTriple.Subject, rightTriple.Subject));
                work.Push((leftTriple.Object, rightTriple.Object));
            }
            else if(left is TripleTerm || right is TripleTerm)
            {
                //One side is a quoted triple and the other is a leaf — not equal.
                return false;
            }
            else if(!left.Equals(right))
            {
                //Both are leaves; their own equality is not recursive.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a structural hash code consistent with <see cref="Equals(TripleTerm?)"/>, computed over an explicit
    /// stack (a fixed pre-order walk) so a deeply-nested term cannot overflow the call stack.
    /// </summary>
    /// <returns>The structural hash code.</returns>
    public override int GetHashCode()
    {
        HashCode hash = new();
        Stack<RdfTerm> work = new();
        work.Push(this);

        while(work.Count > 0)
        {
            RdfTerm term = work.Pop();
            if(term is TripleTerm triple)
            {
                hash.Add(triple.Predicate);

                //Push object then subject so the walk is the fixed order subject-then-object — the same order two
                //structurally-equal terms produce, keeping the hash consistent with equality.
                work.Push(triple.Object);
                work.Push(triple.Subject);
            }
            else
            {
                hash.Add(term);
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Renders the triple term as <c>&lt;&lt;subject predicate object&gt;&gt;</c>. Replaces the recursive
    /// interpolation with an explicit-stack walk so a deeply-nested term cannot overflow the call stack.
    /// </summary>
    /// <returns>The rendered triple term.</returns>
    public override string ToString()
    {
        StringBuilder builder = new();
        Stack<RenderStep> work = new();
        work.Push(new RenderStep(RenderMarker.Term, this));

        while(work.Count > 0)
        {
            RenderStep step = work.Pop();
            switch(step.Marker)
            {
                case(RenderMarker.Term):
                {
                    if(step.Term is TripleTerm triple)
                    {
                        //Push in reverse so they pop in render order: <<subject predicate object>>. The predicate
                        //is a leaf named node, so it renders through the same Term step.
                        work.Push(RenderStep.Close);
                        work.Push(new RenderStep(RenderMarker.Term, triple.Object));
                        work.Push(RenderStep.Space);
                        work.Push(new RenderStep(RenderMarker.Term, triple.Predicate));
                        work.Push(RenderStep.Space);
                        work.Push(new RenderStep(RenderMarker.Term, triple.Subject));
                        work.Push(RenderStep.Open);
                    }
                    else
                    {
                        builder.Append(step.Term!.ToString());
                    }

                    break;
                }
                case(RenderMarker.Open):
                {
                    builder.Append("<<");
                    break;
                }
                case(RenderMarker.Space):
                {
                    builder.Append(' ');
                    break;
                }
                default:
                {
                    //RenderMarker.Close: the components are rendered; close the term.
                    builder.Append(">>");
                    break;
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>The kind of step on the <see cref="ToString"/> render work-stack.</summary>
    private enum RenderMarker
    {
        /// <summary>Render a term: a leaf directly, or expand a quoted triple into its component steps.</summary>
        Term,

        /// <summary>Render the quoted-triple opening delimiter.</summary>
        Open,

        /// <summary>Render a single component separator.</summary>
        Space,

        /// <summary>Render the quoted-triple closing delimiter and unwind one nesting level.</summary>
        Close
    }

    /// <summary>One step on the <see cref="ToString"/> render work-stack.</summary>
    /// <param name="Marker">The step kind.</param>
    /// <param name="Term">The term to render; set only for a <c>Term</c> step.</param>
    private readonly record struct RenderStep(RenderMarker Marker, RdfTerm? Term)
    {
        /// <summary>The payload-less step that renders the quoted-triple opening delimiter.</summary>
        public static RenderStep Open { get; } = new(RenderMarker.Open, null);

        /// <summary>The payload-less step that renders a single component separator.</summary>
        public static RenderStep Space { get; } = new(RenderMarker.Space, null);

        /// <summary>The payload-less step that renders the quoted-triple closing delimiter and unwinds one nesting level.</summary>
        public static RenderStep Close { get; } = new(RenderMarker.Close, null);
    }
}
