using System;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Computes a fixed-width checksum of <paramref name="data"/> into <paramref name="destination"/>.
/// The implementation writes exactly the owning algorithm's byte width and must be a pure function
/// of <paramref name="data"/> — deterministic and free of state, time, or randomness — so the same
/// bytes verify identically on any host.
/// </summary>
/// <param name="data">The bytes to checksum.</param>
/// <param name="destination">The buffer receiving the checksum; exactly the algorithm's byte width.</param>
public delegate void ChecksumComputeDelegate(ReadOnlySpan<byte> data, Span<byte> destination);
