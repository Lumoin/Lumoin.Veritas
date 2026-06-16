using System;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The open extension point for WHERE a persisted columnar index image is read from, decoupled from
/// how it is decoded. The built-in sources memory-map a file or hold a pooled buffer; a deployment
/// composes its own — a decrypting, range-fetched, or remote source — by subclassing, and sources
/// compose by decoration. The reader decodes the image in place through the verified read path, so
/// every source is verified per blob without re-implementing detection.
/// </summary>
/// <remarks>
/// <para>
/// A source owns the lifetime of the bytes it exposes: <see cref="Image"/> is valid only between
/// construction and <see cref="Dispose()"/>, so it is held for the duration of one read and released
/// after, and the decoded columns copy what they need out of the span before disposal. The reader
/// that is handed a source borrows it — the caller that created the source disposes it.
/// </para>
/// <para>
/// The image is exposed as a single contiguous span, the form the warm reader decodes. A source that
/// fetches the image in ranges materialises it into a buffer it owns; per-blob, range-lazy sourcing
/// (an HTTP-range or object-store source that never holds the whole image) is the next extension on
/// this family.
/// </para>
/// </remarks>
public abstract class ColumnarImageSource : IDisposable
{
    /// <summary>The whole index image as a read-only span; valid only until <see cref="Dispose()"/>.</summary>
    public abstract ReadOnlySpan<byte> Image { get; }

    /// <summary>Releases what the source holds — a mapping, a rented buffer.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the source's resources.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>; <see langword="false"/> from a finalizer.</param>
    protected abstract void Dispose(bool disposing);
}
