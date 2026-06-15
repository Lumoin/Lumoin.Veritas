using System;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A <see cref="ColumnSource"/> over a managed <c>ulong</c> array —
/// the in-process backing, and the form <see cref="BlockPackedColumn.Build"/>
/// wraps a freshly packed payload in. The whole-column view is the
/// array itself, so <see cref="TryGetMemory"/> hands it out with no
/// copy.
/// </summary>
public sealed class InMemoryColumnSource : ColumnSource
{
    /// <summary>The packed payload words, held as a whole-column view.</summary>
    private ReadOnlyMemory<ulong> Words { get; }

    /// <summary>Wraps <paramref name="words"/> as a column source; the array is held, not copied.</summary>
    /// <param name="words">The packed payload words.</param>
    /// <exception cref="ArgumentNullException"><paramref name="words"/> is <see langword="null"/>.</exception>
    public InMemoryColumnSource(ulong[] words)
    {
        ArgumentNullException.ThrowIfNull(words);

        Words = words;
    }

    /// <summary>Wraps a pre-resolved whole-column view — the native-backed path's entry.</summary>
    /// <param name="words">The whole-column view (over native memory via <see cref="NativeColumnMemoryManager"/>).</param>
    private InMemoryColumnSource(ReadOnlyMemory<ulong> words)
    {
        Words = words;
    }

    /// <summary>Builds a native-backed source: copies <paramref name="words"/> into a 64-byte-aligned off-GC block and views it as the whole-column handle.</summary>
    /// <param name="words">The packed payload words to copy into native memory.</param>
    /// <returns>A source whose payload lives off the managed heap; the block is reclaimed when the source becomes unreachable.</returns>
    public static InMemoryColumnSource CreateNative(ReadOnlySpan<ulong> words)
    {
        AlignedNativeBuffer buffer = AlignedNativeBuffer.Allocate(words.Length);
        words.CopyTo(buffer.Span);
        NativeColumnMemoryManager manager = new(buffer);

        return new InMemoryColumnSource(manager.Memory);
    }

    /// <inheritdoc/>
    public override int LengthInBytes => Words.Length * sizeof(ulong);

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> Bytes => MemoryMarshal.AsBytes(Words.Span);

    /// <inheritdoc/>
    public override bool TryGetMemory(out ReadOnlyMemory<ulong> memory)
    {
        memory = Words;

        return true;
    }
}
