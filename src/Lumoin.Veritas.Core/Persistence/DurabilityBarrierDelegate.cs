namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// Flushes a directory's own metadata to stable storage so a rename published into it survives a
/// power loss — the durability barrier the atomic-publish commit point relies on after the rename.
/// The production default is <see cref="AtomicPublish.DefaultBarrier"/>; a fault-injection harness
/// substitutes a no-op or failing barrier to exercise the degraded-recovery path without a real crash.
/// </summary>
/// <remarks>The default's reach is host-conditional: it flushes the parent directory on Linux (including
/// Android) and the Apple platforms, and is a no-op on Windows, where no public directory-fsync API
/// exists — so on Windows a commit acknowledgement can precede the rename's durability. See
/// <see cref="AtomicPublish"/> for the per-host defaults.</remarks>
/// <param name="directoryPath">The directory whose metadata is flushed.</param>
public delegate void DurabilityBarrierDelegate(string directoryPath);
