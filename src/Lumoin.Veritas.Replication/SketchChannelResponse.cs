using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A stamped sketch-image response as it crosses the sketch channel outbound: the serving endpoint's domain and
/// dictionary epoch alongside the image bytes (empty for a stamped decline). It is the write-side counterpart of the
/// owning <see cref="SketchFetchResult"/> the read side produces — it borrows the image for the duration of the
/// write and owns nothing — so the server frames one response without a closure over its identity.
/// </summary>
/// <param name="Domain">The serving endpoint's reconciliation domain, stamped so a peer refuses a contract mismatch at the wire.</param>
/// <param name="DictionaryEpoch">The serving endpoint's dictionary epoch — the reserved <c>0</c> in the content-hash domain.</param>
/// <param name="Image">The sketch image bytes, or empty for a stamped decline (a serveable image was not built).</param>
internal readonly record struct SketchChannelResponse(SketchChannelDomain Domain, ulong DictionaryEpoch, ReadOnlyMemory<byte> Image);
