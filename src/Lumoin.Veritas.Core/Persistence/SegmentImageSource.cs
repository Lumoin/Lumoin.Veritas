using System;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// The open extension point for WHERE a persisted segment image is read from, decoupled from how it
/// is decoded. A segment image is any whole persisted artifact a reader decodes in place — a
/// system-of-record or named-graph segment, the term dictionary, or the re-derivable columnar
/// query-index sidecar. The built-in sources memory-map a file or hold a pooled buffer; a deployment
/// composes its own — a decrypting, range-fetched, or remote source — by subclassing, and sources
/// compose by decoration. The reader decodes the image in place through the verified read path, so
/// every source is verified per blob without re-implementing detection.
/// </summary>
/// <remarks>
/// <para>
/// A source owns the lifetime of the bytes it exposes: every window returned by
/// <see cref="Slice"/> (and the whole-image <see cref="Image"/>) is valid only between
/// construction and <see cref="Dispose()"/>, so it is held for the duration of one read and released
/// after, and the decoded result copies what it needs out of the span before disposal. The reader
/// that is handed a source borrows it — the caller that created the source disposes it.
/// </para>
/// <para>
/// The image is addressed with <see cref="long"/> offsets through bounded <see cref="Slice"/>
/// windows, so an artifact larger than a single span's range reads without a heap copy: a
/// block-structured reader slices one window per block, and only the window is span-bounded —
/// which every block-structured format already guarantees per block. <see cref="Image"/> remains
/// the whole-image accessor for artifacts that fit one span; past that range it throws rather than
/// truncating, so a reader that has not adopted windows fails loudly instead of decoding a prefix.
/// </para>
/// </remarks>
public abstract class SegmentImageSource : IDisposable
{
    /// <summary>The total image length in bytes.</summary>
    public abstract long Length { get; }

    /// <summary>Returns a read-only window over the image; valid only until <see cref="Dispose()"/>.</summary>
    /// <param name="offset">The zero-based byte offset of the window.</param>
    /// <param name="length">The window length in bytes.</param>
    /// <returns>A span over exactly <paramref name="length"/> bytes at <paramref name="offset"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The window does not lie wholly within the image.</exception>
    /// <exception cref="ObjectDisposedException">The source has been disposed.</exception>
    public abstract ReadOnlySpan<byte> Slice(long offset, int length);

    /// <summary>
    /// The whole segment image as a read-only span; valid only until <see cref="Dispose()"/>. Serves
    /// the small-artifact readers (manifests, sketches, loss records, value-index sidecars) whose
    /// images always fit one span; an image past a span's range throws here — the block-structured
    /// readers address such an image through <see cref="Slice"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The image exceeds a single span's range; read it through <see cref="Slice"/>.</exception>
    /// <exception cref="ObjectDisposedException">The source has been disposed.</exception>
    public ReadOnlySpan<byte> Image
    {
        get
        {
            long length = Length;
            if(length > int.MaxValue)
            {
                throw new InvalidOperationException($"A {length}-byte image exceeds a single span's range; decode it through bounded windows.");
            }

            return Slice(0, (int)length);
        }
    }

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
