namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// How the persistence layer reaches a column's bytes — the
/// memory-mapped-versus-streamed axis, selected on
/// <see cref="ExecutionPolicy"/> and resolved against the running
/// environment by <see cref="ExecutionPolicy.Resolve()"/>.
/// </summary>
/// <remarks>
/// <para>
/// One resolution point governs the choice for every column: the
/// resolved plan carries a concrete backend family
/// (<see cref="MemoryMapped"/> or <see cref="Streamed"/>, never
/// <see cref="Auto"/>), so the byte-source seam reads a settled
/// decision rather than re-deriving it per access.
/// </para>
/// </remarks>
public enum ColumnAccessMode
{
    /// <summary>Derive from the environment: memory-map local files where the operating system supports it, and range-stream where it does not (browser OPFS or HTTP-backed sources). The default.</summary>
    Auto,

    /// <summary>Memory-map the backing file. The whole-file view amortises across random access, at the cost of address-space reservation and page-fault latency the streamed mode avoids.</summary>
    MemoryMapped,

    /// <summary>Fetch byte ranges on demand. The portable choice where no file to map exists — OPFS and HTTP-backed sources — and where reserving an address-space view is undesirable.</summary>
    Streamed,
}
