using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The header frame a dotted-difference exchange opens its connection with: the requesting endpoint's
/// dictionary epoch (the dotted elements carry encoded term identifiers, so both ends must share one epoch),
/// the offer-shaped declaration of the dotted contract the coded streams will subtract under, and the symbol
/// cap the exchange is bounded by. The serving endpoint answers with a <see cref="DottedDifferenceReplyHeader"/>
/// before any reconciliation envelope crosses, so an epoch, contract, posture, or durability mismatch is
/// refused at the wire with a named reason.
/// </summary>
/// <param name="DictionaryEpoch">The requesting endpoint's dictionary epoch; Core's <see langword="long"/> epoch crosses the wire as this <see langword="ulong"/> by raw bit reinterpretation, the manifest's own convention.</param>
/// <param name="Declaration">The offer-shaped declaration of the requesting endpoint's dotted contract: the item domain, the item and checksum widths, and the key check.</param>
/// <param name="SymbolCap">The symbol ceiling the requesting side bounds the exchange by.</param>
internal sealed record DottedDifferenceRequestHeader(ulong DictionaryEpoch, ReconciliationOffer Declaration, int SymbolCap);
