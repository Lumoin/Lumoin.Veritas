using System.Buffers;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Deserializes one element of a reconciliation elements message from the frame cursor — the injected half of
/// the envelope framing's elements leg. The cursor is positioned at the element's first byte and the delegate
/// advances it past the element; the returned element must own its content (the frame buffer is released after
/// the read).
/// </summary>
/// <typeparam name="TElement">The element type.</typeparam>
/// <param name="reader">The cursor over the frame payload, positioned at the next element.</param>
/// <returns>The deserialized element, owning its content.</returns>
internal delegate TElement ReadReconciliationElementDelegate<TElement>(ref SequenceReader<byte> reader);
