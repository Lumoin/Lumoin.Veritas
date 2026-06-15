namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Where a block-packed column's payload words live — a build policy,
/// default <see cref="Managed"/>. It affects only the backing store,
/// not the encoding: a column packed either way is byte-identical and
/// decodes identically. The Elias-Fano modes hold no block payload, so
/// the policy does not apply to them.
/// </summary>
public enum ColumnPayloadBacking
{
    /// <summary>
    /// The payload is a managed <c>ulong</c> array on the GC heap
    /// (an <see cref="InMemoryColumnSource"/>). The default;
    /// browser-safe.
    /// </summary>
    Managed,

    /// <summary>
    /// The payload is copied into a 64-byte-aligned native block off
    /// the GC heap (<see cref="InMemoryColumnSource.CreateNative"/>),
    /// reclaimed when the column becomes unreachable. Keeps a large
    /// long-lived payload out of the GC/LOH.
    /// </summary>
    NativeAligned,
}
