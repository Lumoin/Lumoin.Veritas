using System;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// One incremental checksum computation: bytes are appended window by window and the digest is
/// finished once, so an artifact larger than a single span's range verifies without ever holding
/// the whole image contiguously. A session is single-use — finish it once and dispose it; a new
/// verification opens a new session through its algorithm's
/// <see cref="ChecksumAlgorithm.CreateSession"/> factory.
/// </summary>
public abstract class ChecksumSession : IDisposable
{
    /// <summary>Folds the next window of bytes into the running computation, in image order.</summary>
    /// <param name="data">The next window.</param>
    public abstract void Append(ReadOnlySpan<byte> data);

    /// <summary>Finishes the computation, writing the digest of everything appended.</summary>
    /// <param name="destination">The destination, exactly the algorithm's <see cref="ChecksumAlgorithm.ByteWidth"/> bytes.</param>
    public abstract void Finish(Span<byte> destination);

    /// <summary>Releases whatever the session holds.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the session's resources.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>; <see langword="false"/> from a finalizer.</param>
    protected abstract void Dispose(bool disposing);
}
