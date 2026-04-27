namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// OpenTelemetry metric name constants for the Veritas library.
/// </summary>
/// <remarks>
/// All metric names follow the OpenTelemetry naming conventions:
/// lowercase, dot-separated, with the <c>veritas.</c> prefix.
/// </remarks>
public static class VeritasMetrics
{
    /// <summary>The OTel Meter name for Veritas Core instrumentation.</summary>
    public const string MeterName = "Lumoin.Veritas.Core";

    /// <summary>Total number of memory slabs created across all buffer sizes.</summary>
    public const string MemoryPoolTotalSlabs = "veritas.memory_pool.total_slabs";

    /// <summary>Total memory allocated across all slabs.</summary>
    public const string MemoryPoolTotalMemoryAllocated = "veritas.memory_pool.total_memory_allocated";

    /// <summary>Number of currently rented memory segments.</summary>
    public const string MemoryPoolActiveRentals = "veritas.memory_pool.active_rentals";

    /// <summary>Percentage of allocated memory currently in use.</summary>
    public const string MemoryPoolAllocationEfficiency = "veritas.memory_pool.allocation_efficiency";

    /// <summary>Distribution of requested buffer sizes.</summary>
    public const string MemoryPoolBufferSizeDistribution = "veritas.memory_pool.buffer_size_distribution";

    /// <summary>Total number of successful rent operations.</summary>
    public const string MemoryPoolRentOperationsTotal = "veritas.memory_pool.rent_operations_total";

    /// <summary>Total number of memory return operations.</summary>
    public const string MemoryPoolReturnOperationsTotal = "veritas.memory_pool.return_operations_total";

    /// <summary>Number of unique strings interned in a Utf8StringPool.</summary>
    public const string StringPoolUniqueCount = "veritas.string_pool.unique_count";

    /// <summary>Total intern operations (hits + misses).</summary>
    public const string StringPoolInternOperationsTotal = "veritas.string_pool.intern_operations_total";

    /// <summary>Intern cache hit count (existing string returned).</summary>
    public const string StringPoolInternHitsTotal = "veritas.string_pool.intern_hits_total";

    /// <summary>Total bytes interned in the pool.</summary>
    public const string StringPoolTotalBytesInterned = "veritas.string_pool.total_bytes_interned";
}
