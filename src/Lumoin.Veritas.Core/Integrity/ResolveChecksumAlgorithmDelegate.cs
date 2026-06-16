namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Resolves a checksum algorithm from the on-disk id a persistence image records, so a reader can
/// verify an image written with any registered algorithm. Returns <see langword="null"/> when the
/// id is unknown to this resolver, which the reader surfaces as an unsupported image rather than a
/// silent skip. A deployment composes its own resolver — typically falling back to
/// <see cref="ChecksumAlgorithm.DefaultResolver"/> — to admit custom algorithms.
/// </summary>
/// <param name="id">The on-disk checksum-algorithm id.</param>
/// <returns>The algorithm, or <see langword="null"/> when the id is unknown.</returns>
public delegate ChecksumAlgorithm? ResolveChecksumAlgorithmDelegate(byte id);
