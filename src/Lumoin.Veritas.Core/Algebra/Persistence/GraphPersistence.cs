using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>Formats a graph node to its text representation for an edge or adjacency list.</summary>
/// <typeparam name="TNode">The node type.</typeparam>
/// <param name="node">The node to format.</param>
/// <returns>The node's text representation.</returns>
public delegate string NodeFormatter<in TNode>(TNode node);

/// <summary>Formats a graph edge label to its text representation for a labeled edge list.</summary>
/// <typeparam name="TLabel">The label type.</typeparam>
/// <param name="label">The label to format.</param>
/// <returns>The label's text representation.</returns>
public delegate string LabelFormatter<in TLabel>(TLabel label);

/// <summary>
/// Streaming writers that emit a <see cref="GraphSource{TNode}"/> to
/// a <see cref="PipeWriter"/> or convenience-overload to a
/// <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// <para>
/// The canonical surface is <see cref="PipeWriter"/>: the writers
/// fetch byte buffers directly from the pipe via
/// <see cref="PipeWriter.GetSpan"/>, write UTF-8 bytes in place
/// without intermediate <see cref="string"/> allocations for integer
/// nodes, and call <see cref="PipeWriter.Advance"/>. A
/// <see cref="Stream"/> overload is provided for callers who already
/// hold a <see cref="Stream"/>; it constructs a
/// <see cref="PipeWriter"/> over the stream with
/// <c>leaveOpen: true</c> and forwards.
/// </para>
/// <para>
/// All writers iterate the graph source exactly once and produce
/// output incrementally. Working memory is bounded by the pipe's
/// internal buffer regardless of graph size — multi-billion edge
/// graphs serialise at I/O speed without materialising in RAM.
/// </para>
/// <para>
/// UTF-8 text is written through the shared
/// <see cref="Utf8BufferWriter"/> extensions over the pipe; the
/// integer fast path <see cref="WriteInt32"/> formats ASCII digits
/// directly via <see cref="Utf8Formatter"/>.
/// </para>
/// </remarks>
public static class GraphPersistence
{
    //Tab and newline as raw UTF-8 bytes for the hot path.
    private static ReadOnlySpan<byte> Tab => "\t"u8;
    private static ReadOnlySpan<byte> Newline => "\n"u8;

    /// <summary>
    /// Writes <paramref name="source"/> as a tab-separated edge list
    /// to <paramref name="writer"/>. One line per edge,
    /// <c>"source\ttarget\n"</c>.
    /// </summary>
    public static async Task WriteEdgeListAsync<TNode>(
        GraphSource<TNode> source,
        PipeWriter writer,
        NodeFormatter<TNode> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatNode);

        await foreach((TNode src, TNode tgt) in source.Edges(cancellationToken).ConfigureAwait(false))
        {
            writer.WriteUtf8(formatNode(src));
            writer.WriteUtf8Literal(Tab);
            writer.WriteUtf8(formatNode(tgt));
            writer.WriteUtf8Literal(Newline);
            FlushResult flush = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if(flush.IsCompleted || flush.IsCanceled)
            {
                break;
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience overload: wraps <paramref name="stream"/> in a
    /// <see cref="PipeWriter"/> with <c>leaveOpen: true</c> and
    /// forwards.
    /// </summary>
    public static Task WriteEdgeListAsync<TNode>(
        GraphSource<TNode> source,
        Stream stream,
        NodeFormatter<TNode> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeWriter writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        return WriteEdgeListAsync(source, writer, formatNode, cancellationToken);
    }

    /// <summary>
    /// Writes the labeled <paramref name="source"/> as a tab-separated
    /// triple list to <paramref name="writer"/>. One line per edge,
    /// <c>"source\tlabel\ttarget\n"</c>.
    /// </summary>
    public static async Task WriteLabeledEdgeListAsync<TNode, TLabel>(
        LabeledGraphSource<TNode, TLabel> source,
        PipeWriter writer,
        NodeFormatter<TNode> formatNode,
        LabelFormatter<TLabel> formatLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatNode);
        ArgumentNullException.ThrowIfNull(formatLabel);

        await foreach((TNode src, TLabel lbl, TNode tgt) in source.Edges(cancellationToken).ConfigureAwait(false))
        {
            writer.WriteUtf8(formatNode(src));
            writer.WriteUtf8Literal(Tab);
            writer.WriteUtf8(formatLabel(lbl));
            writer.WriteUtf8Literal(Tab);
            writer.WriteUtf8(formatNode(tgt));
            writer.WriteUtf8Literal(Newline);
            FlushResult flush = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if(flush.IsCompleted || flush.IsCanceled)
            {
                break;
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience overload writing labeled edges to a
    /// <see cref="Stream"/>.
    /// </summary>
    public static Task WriteLabeledEdgeListAsync<TNode, TLabel>(
        LabeledGraphSource<TNode, TLabel> source,
        Stream stream,
        NodeFormatter<TNode> formatNode,
        LabelFormatter<TLabel> formatLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeWriter writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));

        return WriteLabeledEdgeListAsync(source, writer, formatNode, formatLabel, cancellationToken);
    }

    /// <summary>
    /// Writes <paramref name="source"/> as an adjacency list to
    /// <paramref name="writer"/>: one line per source node containing
    /// the source followed by all targets, tab-separated.
    /// </summary>
    public static async Task WriteAdjacencyListAsync<TNode>(
        GraphSource<TNode> source,
        IEnumerable<TNode> nodes,
        PipeWriter writer,
        NodeFormatter<TNode> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatNode);

        foreach(TNode node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteUtf8(formatNode(node));
            await foreach(TNode neighbour in source.Adjacency(node, cancellationToken).ConfigureAwait(false))
            {
                writer.WriteUtf8Literal(Tab);
                writer.WriteUtf8(formatNode(neighbour));
            }
            writer.WriteUtf8Literal(Newline);
            FlushResult flush = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if(flush.IsCompleted || flush.IsCanceled)
            {
                break;
            }
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience overload writing an adjacency list to a
    /// <see cref="Stream"/>.
    /// </summary>
    public static Task WriteAdjacencyListAsync<TNode>(
        GraphSource<TNode> source,
        IEnumerable<TNode> nodes,
        Stream stream,
        NodeFormatter<TNode> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeWriter writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        return WriteAdjacencyListAsync(source, nodes, writer, formatNode, cancellationToken);
    }

    //WriteInt32 — formats an int as ASCII digits directly into the
    //pipe's buffer via Utf8Formatter. No string allocation. Reference
    //implementation of the int-direct fast path; retained for future
    //int-node specialisations.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "IDE0051:Remove unused private members",
        Justification = "Reference implementation of the int-direct fast path; intentionally retained for future int-node specialisations.")]
    private static void WriteInt32(PipeWriter writer, int value)
    {
        Span<byte> destination = writer.GetSpan(11);
        if(!Utf8Formatter.TryFormat(value, destination, out int written))
        {
            throw new InvalidOperationException("Utf8Formatter.TryFormat for Int32 returned false on an 11-byte buffer.");
        }
        writer.Advance(written);
    }
}
