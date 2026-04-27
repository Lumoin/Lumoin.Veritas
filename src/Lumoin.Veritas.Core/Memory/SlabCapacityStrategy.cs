namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// Determines the number of segments to allocate per slab based on segment size.
/// </summary>
/// <param name="segmentSize">The size of each segment in elements.</param>
/// <returns>The number of segments to allocate in the new slab.</returns>
public delegate int SlabCapacityStrategy(int segmentSize);
