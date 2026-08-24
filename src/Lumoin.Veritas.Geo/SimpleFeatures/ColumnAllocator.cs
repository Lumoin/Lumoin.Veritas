using System.Buffers;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Allocates one exactly-sized column rental for a <see cref="FlatGeometry"/> under
/// construction. The contract is exact length: the returned owner's memory must be
/// precisely <paramref name="length"/> elements — the flat model's parts slice by
/// position, so an oversized rental has no meaning here and the builder throws on one.
/// Hosts bind their own pooling through this seam; the default is plain heap arrays
/// whose disposal is a no-op.
/// </summary>
/// <param name="length">The exact element count to allocate.</param>
public delegate IMemoryOwner<T> ColumnAllocator<T>(int length);
