using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.NQuads;

/// <summary>
/// Serializes <see cref="Quad"/> instances to N-Quads format.
/// </summary>
/// <remarks>
/// <para>
/// N-Quads is a line-oriented format where each line represents exactly one quad.
/// The format is defined at https://www.w3.org/TR/n-quads/.
/// </para>
/// <para>
/// Each line has the form:
/// <c>subject predicate object [graph] .</c>
/// </para>
/// <para>
/// All output is UTF-8, written directly into the pipe via <see cref="Utf8BufferWriter"/> —
/// terms come straight from their <see cref="Utf8String"/> byte representations and no
/// intermediate scratch array or .NET string is allocated.
/// </para>
/// </remarks>
public static class NQuadsWriter
{
    //N-Quads uses \n as the line terminator (spec section 5).
    private static ReadOnlySpan<byte> LineFeed => "\n"u8;
    private static ReadOnlySpan<byte> Space => " "u8;
    private static ReadOnlySpan<byte> StatementEnd => " .\n"u8;
    private static ReadOnlySpan<byte> LessThan => "<"u8;
    private static ReadOnlySpan<byte> GreaterThan => ">"u8;
    private static ReadOnlySpan<byte> BlankNodePrefix => "_:"u8;
    private static ReadOnlySpan<byte> Quote => "\""u8;
    private static ReadOnlySpan<byte> DatatypeMarker => "\"^^<"u8;
    private static ReadOnlySpan<byte> LanguageMarker => "\"@"u8;
    private static ReadOnlySpan<byte> TripleTermOpen => "<<( "u8;
    private static ReadOnlySpan<byte> TripleTermClose => " )>>"u8;
    private static ReadOnlySpan<byte> DirectionLtr => "--ltr"u8;
    private static ReadOnlySpan<byte> DirectionRtl => "--rtl"u8;

    /// <summary>The number of quads written into the pipe between flushes; bounds buffering without a flush per line.</summary>
    private const int FlushEveryQuads = 1024;

    /// <summary>
    /// Writes a sequence of quads to the given pipe in N-Quads format.
    /// </summary>
    /// <param name="quads">The quads to serialize.</param>
    /// <param name="output">The output pipe.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async ValueTask WriteAsync(
        IAsyncEnumerable<Quad> quads,
        PipeWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            //Each quad is written straight into the pipe's buffer; flushing every batch of quads
            //amortises the flush cost while bounding how much accumulates before the reader sees it.
            int sinceFlush = 0;
            await foreach(Quad quad in quads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                WriteQuad(output, quad);

                if(++sinceFlush >= FlushEveryQuads)
                {
                    sinceFlush = 0;

                    FlushResult flush = await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if(flush.IsCanceled || flush.IsCompleted)
                    {
                        break;
                    }
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await output.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a sequence of quads to the given pipe in N-Quads format.
    /// </summary>
    /// <param name="quads">The quads to serialize.</param>
    /// <param name="output">The output pipe.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async ValueTask WriteAsync(
        IEnumerable<Quad> quads,
        PipeWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        await WriteAsync(ToAsyncEnumerable(quads, cancellationToken), output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a single quad — subject, predicate, object, optional graph, and the statement terminator.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="quad">The quad to serialize.</param>
    private static void WriteQuad(IBufferWriter<byte> output, Quad quad)
    {
        WriteTerm(output, quad.Subject);
        output.WriteUtf8Literal(Space);

        WriteNamedNode(output, quad.Predicate);
        output.WriteUtf8Literal(Space);

        WriteTerm(output, quad.Object);

        if(quad.Graph is { } graph)
        {
            output.WriteUtf8Literal(Space);
            WriteTerm(output, graph);
        }

        output.WriteUtf8Literal(StatementEnd);
    }

    /// <summary>
    /// Writes any RDF term. Leaves are written directly; a quoted triple (the only nesting term) is walked
    /// over an explicit work-stack so deep nesting stays off the call stack, with a depth guard that turns a
    /// pathological term into a catchable <see cref="TripleTermDepthLimitException"/> rather than unbounded growth.
    /// </summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="term">The term to serialize.</param>
    /// <exception cref="InvalidOperationException">The term is of an unknown kind.</exception>
    /// <exception cref="TripleTermDepthLimitException">A quoted triple is nested beyond <see cref="QuotedTripleLimits.MaxNestingDepth"/>.</exception>
    private static void WriteTerm(IBufferWriter<byte> output, RdfTerm term)
    {
        if(term is not TripleTerm root)
        {
            WriteLeaf(output, term);
            return;
        }

        Stack<TermStep> work = new();
        work.Push(new TermStep(StepKind.Term, root, null));
        int depth = 0;

        while(work.Count > 0)
        {
            TermStep step = work.Pop();
            switch(step.Kind)
            {
                case(StepKind.Term):
                {
                    if(step.Term is TripleTerm triple)
                    {
                        depth++;
                        if(depth > QuotedTripleLimits.MaxNestingDepth)
                        {
                            throw new TripleTermDepthLimitException(depth, QuotedTripleLimits.MaxNestingDepth);
                        }

                        //Push the components in reverse so they pop in serialization (left-to-right) order:
                        //<<( subject SPACE predicate SPACE object )>>.
                        work.Push(TermStep.Close);
                        work.Push(new TermStep(StepKind.Term, triple.Object, null));
                        work.Push(TermStep.Space);
                        work.Push(new TermStep(StepKind.Predicate, null, triple.Predicate));
                        work.Push(TermStep.Space);
                        work.Push(new TermStep(StepKind.Term, triple.Subject, null));
                        work.Push(TermStep.Open);
                    }
                    else
                    {
                        WriteLeaf(output, step.Term!);
                    }

                    break;
                }
                case(StepKind.Open):
                {
                    output.WriteUtf8Literal(TripleTermOpen);
                    break;
                }
                case(StepKind.Space):
                {
                    output.WriteUtf8Literal(Space);
                    break;
                }
                case(StepKind.Predicate):
                {
                    WriteNamedNode(output, step.Predicate!);
                    break;
                }
                default:
                {
                    //StepKind.Close: the triple's components are written; close it and unwind one nesting level.
                    output.WriteUtf8Literal(TripleTermClose);
                    depth--;
                    break;
                }
            }
        }
    }

    /// <summary>Writes a leaf term (named node, blank node, or literal).</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="term">The leaf term to serialize.</param>
    /// <exception cref="InvalidOperationException">The term is of an unknown kind.</exception>
    private static void WriteLeaf(IBufferWriter<byte> output, RdfTerm term)
    {
        switch(term)
        {
            case(NamedNode namedNode):
            {
                WriteNamedNode(output, namedNode);
                break;
            }
            case(BlankNode blankNode):
            {
                WriteBlankNode(output, blankNode);
                break;
            }
            case(Literal literal):
            {
                WriteLiteral(output, literal);
                break;
            }
            case(EngineNode engine):
            {
                //An engine-minted node serializes as its deterministic Skolem IRI; the rendering re-parses as
                //an ordinary named node, never back into an engine mint.
                output.WriteUtf8Literal(LessThan);
                output.WriteUtf8String(engine.SkolemIri());
                output.WriteUtf8Literal(GreaterThan);
                break;
            }
            default:
            {
                throw new InvalidOperationException($"Unknown RDF term type: {term.GetType().Name}.");
            }
        }
    }

    /// <summary>Writes a named node as <c>&lt;iri&gt;</c> with N-Quads IRI escaping.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="node">The named node to serialize.</param>
    private static void WriteNamedNode(IBufferWriter<byte> output, NamedNode node)
    {
        output.WriteUtf8Literal(LessThan);
        WriteEscapedIri(output, node.Iri.Span);
        output.WriteUtf8Literal(GreaterThan);
    }

    /// <summary>Writes a blank node as <c>_:label</c>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="node">The blank node to serialize.</param>
    private static void WriteBlankNode(IBufferWriter<byte> output, BlankNode node)
    {
        output.WriteUtf8Literal(BlankNodePrefix);
        output.WriteUtf8String(node.Label);
    }

    /// <summary>Writes a literal as a quoted, escaped value with its language tag (and optional base direction) or datatype.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="literal">The literal to serialize.</param>
    private static void WriteLiteral(IBufferWriter<byte> output, Literal literal)
    {
        output.WriteUtf8Literal(Quote);
        WriteEscapedLiteralValue(output, literal.Value.Span);

        if(literal.Language is { } language)
        {
            output.WriteUtf8Literal(LanguageMarker);
            output.WriteUtf8String(language);

            if(literal.BaseDirection is { } direction)
            {
                output.WriteUtf8Literal(direction == TextDirection.Ltr ? DirectionLtr : DirectionRtl);
            }
        }
        else
        {
            output.WriteUtf8Literal(DatatypeMarker);
            WriteEscapedIri(output, literal.Datatype.Iri.Span);
            output.WriteUtf8Literal(GreaterThan);
        }
    }

    /// <summary>
    /// Writes an IRI with N-Quads escape sequences applied: control characters and the backslash
    /// become <c>\\uXXXX</c>, copying maximal runs of safe bytes verbatim. The escaped bytes are
    /// ASCII, so they never split a multi-byte UTF-8 sequence.
    /// </summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="iri">The UTF-8 IRI bytes.</param>
    private static void WriteEscapedIri(IBufferWriter<byte> output, ReadOnlySpan<byte> iri)
    {
        int runStart = 0;
        for(int i = 0; i < iri.Length; i++)
        {
            byte b = iri[i];
            if(b >= 0x20 && b != 0x5C)
            {
                continue;
            }

            output.WriteUtf8Literal(iri[runStart..i]);
            WriteUnicodeEscape(output, b);
            runStart = i + 1;
        }

        output.WriteUtf8Literal(iri[runStart..]);
    }

    /// <summary>
    /// Writes a literal value with N-Quads string escaping (<c>\\t \\n \\r \\" \\\\</c>), copying maximal
    /// runs of safe bytes verbatim. The escaped bytes are ASCII, so multi-byte UTF-8 sequences pass through.
    /// </summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="value">The UTF-8 literal value bytes.</param>
    private static void WriteEscapedLiteralValue(IBufferWriter<byte> output, ReadOnlySpan<byte> value)
    {
        int runStart = 0;
        for(int i = 0; i < value.Length; i++)
        {
            ReadOnlySpan<byte> escape = value[i] switch
            {
                (byte)'\t' => "\\t"u8,
                (byte)'\n' => "\\n"u8,
                (byte)'\r' => "\\r"u8,
                (byte)'"' => "\\\""u8,
                (byte)'\\' => "\\\\"u8,
                _ => default
            };

            if(escape.IsEmpty)
            {
                continue;
            }

            output.WriteUtf8Literal(value[runStart..i]);
            output.WriteUtf8Literal(escape);
            runStart = i + 1;
        }

        output.WriteUtf8Literal(value[runStart..]);
    }

    /// <summary>Writes the six-byte <c>\\u00XX</c> escape for a control byte or backslash, uppercase hex.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="value">The byte to escape; always less than <c>0x20</c> or the backslash <c>0x5C</c>.</param>
    private static void WriteUnicodeEscape(IBufferWriter<byte> output, byte value)
    {
        ReadOnlySpan<byte> hex = "0123456789ABCDEF"u8;
        Span<byte> destination = output.GetSpan(6);
        destination[0] = (byte)'\\';
        destination[1] = (byte)'u';
        destination[2] = (byte)'0';
        destination[3] = (byte)'0';
        destination[4] = hex[(value >> 4) & 0xF];
        destination[5] = hex[value & 0xF];
        output.Advance(6);
    }

    /// <summary>Adapts a synchronous quad sequence to an async one for the streaming writer.</summary>
    /// <param name="source">The quads.</param>
    /// <param name="cancellationToken">A token to cancel enumeration.</param>
    /// <returns>The quads as an async sequence.</returns>
    private static async IAsyncEnumerable<Quad> ToAsyncEnumerable(
        IEnumerable<Quad> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(Quad quad in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return quad;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>The kind of step on the quoted-triple serialization work-stack.</summary>
    private enum StepKind
    {
        /// <summary>Visit a term: write a leaf directly, or expand a quoted triple into its component steps.</summary>
        Term,

        /// <summary>Write the quoted-triple opening delimiter.</summary>
        Open,

        /// <summary>Write a single component separator.</summary>
        Space,

        /// <summary>Write a quoted triple's predicate, which is always a named node.</summary>
        Predicate,

        /// <summary>Write the quoted-triple closing delimiter and unwind one nesting level.</summary>
        Close
    }

    /// <summary>One step on the quoted-triple serialization work-stack.</summary>
    /// <param name="Kind">The step kind.</param>
    /// <param name="Term">The term to visit; set only for a <c>Term</c> step.</param>
    /// <param name="Predicate">The predicate to write; set only for a <c>Predicate</c> step.</param>
    private readonly record struct TermStep(StepKind Kind, RdfTerm? Term, NamedNode? Predicate)
    {
        /// <summary>The payload-less step that writes the quoted-triple opening delimiter.</summary>
        public static TermStep Open { get; } = new(StepKind.Open, null, null);

        /// <summary>The payload-less step that writes a single component separator.</summary>
        public static TermStep Space { get; } = new(StepKind.Space, null, null);

        /// <summary>The payload-less step that writes the quoted-triple closing delimiter and unwinds one nesting level.</summary>
        public static TermStep Close { get; } = new(StepKind.Close, null, null);
    }
}
