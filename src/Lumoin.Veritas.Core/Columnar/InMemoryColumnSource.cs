using System;
using System.Runtime.InteropServices;

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
    /// <summary>The packed payload words.</summary>
    private readonly ulong[] words;

    /// <summary>Wraps <paramref name="words"/> as a column source; the array is held, not copied.</summary>
    /// <param name="words">The packed payload words.</param>
    /// <exception cref="ArgumentNullException"><paramref name="words"/> is <see langword="null"/>.</exception>
    public InMemoryColumnSource(ulong[] words)
    {
        ArgumentNullException.ThrowIfNull(words);

        this.words = words;
    }

    /// <inheritdoc/>
    public override int LengthInBytes => words.Length * sizeof(ulong);

    /// <inheritdoc/>
    public override ReadOnlySpan<byte> Bytes => MemoryMarshal.AsBytes(new ReadOnlySpan<ulong>(words));

    /// <inheritdoc/>
    public override bool TryGetMemory(out ReadOnlyMemory<ulong> memory)
    {
        memory = words;

        return true;
    }
}
