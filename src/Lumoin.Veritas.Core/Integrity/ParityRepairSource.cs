using System;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The local-parity restoring source a repair pass borrows: the at-rest-corrupt system-of-record image and the
/// verified parity-block bytes that together restore one lost block. It is the parity analog of the verified
/// system-of-record feed — a data holder the coordinator reads from, not the restore itself; the coordinator
/// drives the restore through <see cref="Lumoin.Veritas.Core.Persistence.Segment.ItemSegment.TryRestoreBlockFromParity"/>.
/// The pass builds it only after the parity artifact itself verifies clean, so a borrowed source is already
/// trustworthy at rest; a stale or geometry-mismatched parity is caught by the restore's own self-check.
/// </summary>
/// <param name="SystemOfRecordImage">The at-rest-corrupt system-of-record image — framing and front matter intact, one block payload corrupt — the surviving blocks are read from.</param>
/// <param name="Parity">The verified parity-block bytes, the block stride wide.</param>
/// <param name="ProtectedBlockCount">The number of system-of-record blocks the parity was folded over; the restore refuses a segment whose block count differs, as a co-version mismatch.</param>
/// <param name="ResolveChecksum">Resolves the system-of-record image's checksum-algorithm id during the restore's self-check; <see langword="null"/> uses the default resolver.</param>
internal readonly record struct ParityRepairSource(
    ReadOnlyMemory<byte> SystemOfRecordImage,
    ReadOnlyMemory<byte> Parity,
    int ProtectedBlockCount,
    ResolveChecksumAlgorithmDelegate? ResolveChecksum);
