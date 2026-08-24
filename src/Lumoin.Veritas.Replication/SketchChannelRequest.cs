namespace Lumoin.Veritas.Replication;

/// <summary>
/// A stamped sketch-fetch request as it crosses the sketch channel: the requesting endpoint's reconciliation domain
/// and dictionary epoch alongside the symbol budget it asks the peer's sketch to carry. The stamp lets the serving
/// endpoint refuse a contract or epoch mismatch at the wire rather than serving an image the requester would combine
/// against incomparable identifiers.
/// </summary>
/// <param name="Domain">The requesting endpoint's reconciliation domain.</param>
/// <param name="DictionaryEpoch">The requesting endpoint's dictionary epoch — the reserved <c>0</c> in the content-hash domain.</param>
/// <param name="SymbolBudget">The number of coded symbols the peer's sketch must carry.</param>
internal readonly record struct SketchChannelRequest(SketchChannelDomain Domain, ulong DictionaryEpoch, int SymbolBudget);
