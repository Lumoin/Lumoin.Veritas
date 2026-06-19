using System;
using System.IO;

namespace Lumoin.Veritas.Core.Parsing;

/// <summary>
/// A minimal read-only, forward-only <see cref="Stream"/> over a <see cref="ReadOnlyMemory{T}"/> of bytes, so a
/// <see cref="Stream"/>-only framework API — chiefly <see cref="System.Xml.XmlReader"/> — can consume an in-memory
/// buffer without first copying it into a <see cref="MemoryStream"/>. The source is borrowed, not copied; the caller
/// keeps it alive for the stream's lifetime.
/// </summary>
public sealed class ReadOnlyMemoryStream: Stream
{
    /// <summary>The borrowed source bytes.</summary>
    private readonly ReadOnlyMemory<byte> source;

    /// <summary>The number of bytes already read.</summary>
    private int position;

    /// <summary>Initializes a read-only stream over <paramref name="source"/>, which is borrowed rather than copied.</summary>
    /// <param name="source">The bytes the stream reads from.</param>
    public ReadOnlyMemoryStream(ReadOnlyMemory<byte> source)
    {
        this.source = source;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => source.Length;

    /// <inheritdoc/>
    public override long Position { get => position; set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        int available = Math.Min(buffer.Length, source.Length - position);
        source.Span.Slice(position, available).CopyTo(buffer);
        position += available;

        return available;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
