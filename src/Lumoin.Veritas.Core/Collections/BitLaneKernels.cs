using System;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// Packs <paramref name="values"/> into <paramref name="payload"/> as
/// consecutive <paramref name="bitWidth"/>-bit lanes: value <c>i</c> occupies
/// payload bits <c>[i·bitWidth, (i+1)·bitWidth)</c>, little-endian within each
/// 64-bit word. Only the low <paramref name="bitWidth"/> bits of each value
/// are written; the payload must arrive zeroed. The succinct sequences accept
/// this so a caller that owns vectorised lane kernels can supply them without
/// the sequences depending on that layer; absent one, they pack portably.
/// </summary>
/// <param name="values">The values to pack.</param>
/// <param name="bitWidth">The lane width in bits.</param>
/// <param name="payload">The zeroed destination words.</param>
public delegate void BitLanePacker(ReadOnlySpan<uint> values, int bitWidth, Span<ulong> payload);

/// <summary>
/// Unpacks <paramref name="destination"/><c>.Length</c> consecutive
/// <paramref name="bitWidth"/>-bit lanes from <paramref name="payload"/>
/// (little-endian within each 64-bit word, lane 0 at payload bit 0) and adds
/// <paramref name="frameBase"/> to each in wrapping 32-bit arithmetic. The
/// counterpart of <see cref="BitLanePacker"/> for bulk reads; a zero base
/// makes it a pure unpack.
/// </summary>
/// <param name="payload">The packed words.</param>
/// <param name="bitWidth">The lane width in bits.</param>
/// <param name="frameBase">The value added to every lane.</param>
/// <param name="destination">Receives the unpacked values.</param>
public delegate void BitLaneUnpacker(ReadOnlySpan<ulong> payload, int bitWidth, uint frameBase, Span<uint> destination);
