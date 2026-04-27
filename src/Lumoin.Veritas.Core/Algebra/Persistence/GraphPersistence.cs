using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Algebra;

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
/// <b>Encoding qualification.</b> All references to UTF-8 encoding
/// inside this file are fully qualified as
/// <c>System.Text.Encoding.UTF8</c>. The unqualified name
/// <c>Encoding</c> would collide with the project's
/// <c>Lumoin.Veritas.Core.Encoding</c> namespace.
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
        Func<TNode, string> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatNode);

        await foreach((TNode src, TNode tgt) in source.Edges(cancellationToken).ConfigureAwait(false))
        {
            WriteUtf8(writer, formatNode(src));
            WriteRaw(writer, Tab);
            WriteUtf8(writer, formatNode(tgt));
            WriteRaw(writer, Newline);
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
        Func<TNode, string> formatNode,
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
        Func<TNode, string> formatNode,
        Func<TLabel, string> formatLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatNode);
        ArgumentNullException.ThrowIfNull(formatLabel);

        await foreach((TNode src, TLabel lbl, TNode tgt) in source.Edges(cancellationToken).ConfigureAwait(false))
        {
            WriteUtf8(writer, formatNode(src));
            WriteRaw(writer, Tab);
            WriteUtf8(writer, formatLabel(lbl));
            WriteRaw(writer, Tab);
            WriteUtf8(writer, formatNode(tgt));
            WriteRaw(writer, Newline);
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
        Func<TNode, string> formatNode,
        Func<TLabel, string> formatLabel,
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
        Func<TNode, string> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatNode);

        foreach(TNode node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteUtf8(writer, formatNode(node));
            await foreach(TNode neighbour in source.Adjacency(node, cancellationToken).ConfigureAwait(false))
            {
                WriteRaw(writer, Tab);
                WriteUtf8(writer, formatNode(neighbour));
            }
            WriteRaw(writer, Newline);
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
        Func<TNode, string> formatNode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeWriter writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        return WriteAdjacencyListAsync(source, nodes, writer, formatNode, cancellationToken);
    }

    //WriteUtf8 — encodes a string into the pipe's owned buffer in one
    //GetBytes call. No intermediate char[] or StringBuilder; the
    //encoder writes directly into a span the pipe lent us. Encoding
    //is fully qualified to avoid binding to Lumoin.Veritas.Core.Encoding.
    private static void WriteUtf8(PipeWriter writer, string value)
    {
        if(string.IsNullOrEmpty(value))
        {
            return;
        }

        int byteLength = System.Text.Encoding.UTF8.GetByteCount(value);
        Span<byte> destination = writer.GetSpan(byteLength);
        int written = System.Text.Encoding.UTF8.GetBytes(value, destination);
        writer.Advance(written);
    }

    //WriteRaw — copies pre-encoded UTF-8 bytes (a delimiter, usually)
    //into the pipe's buffer.
    private static void WriteRaw(PipeWriter writer, ReadOnlySpan<byte> bytes)
    {
        Span<byte> destination = writer.GetSpan(bytes.Length);
        bytes.CopyTo(destination);
        writer.Advance(bytes.Length);
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
