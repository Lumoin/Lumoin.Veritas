using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// Flushes a written file's bytes to stable storage so they survive a power loss — the durability
/// primitive a durable write applies before it acknowledges. The production default is
/// <see cref="AtomicPublish.DefaultFlush"/>; a platform the built-in default cannot name (an unmodelled
/// runtime whose storage durability differs from what its moniker suggests) injects its own flush here,
/// and a fault-injection harness substitutes a no-op or failing flush to exercise the degraded-recovery
/// path without a real crash. It is the file-content counterpart of the directory
/// <see cref="DurabilityBarrierDelegate"/>: both are seams so the durability policy is the consumer's,
/// not a closed platform list.
/// </summary>
/// <remarks>The default's mechanism is host-conditional: <c>FlushFileBuffers</c> via the runtime flush
/// on Windows, <c>fsync</c> on Linux (including Android), and <c>fcntl(F_FULLFSYNC)</c> on the Apple
/// mobile platforms, where the runtime flush degrades to a plain <c>fsync</c>. See
/// <see cref="AtomicPublish"/> for the per-host defaults.</remarks>
/// <param name="handle">The open handle to the written file whose bytes are flushed.</param>
public delegate void DurableFlushDelegate(SafeFileHandle handle);
