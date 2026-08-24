using System.Buffers;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serializes one element of a reconciliation elements message into the channel buffer — the injected half of
/// the envelope framing's elements leg, so the frame layout stays fixed while the element payload is the
/// binding's choice. An add-only binding that never transfers elements injects none and the framing refuses
/// the leg loudly.
/// </summary>
/// <typeparam name="TElement">The element type.</typeparam>
/// <param name="element">The element to serialize.</param>
/// <param name="output">The channel buffer to write into.</param>
internal delegate void WriteReconciliationElementDelegate<TElement>(TElement element, IBufferWriter<byte> output);
