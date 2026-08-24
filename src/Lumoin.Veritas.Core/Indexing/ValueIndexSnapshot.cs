using System;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// One access method's built state in its serializable form: the payload a durable value-index
/// sidecar persists for the method and hands back to
/// <see cref="ValueAccessMethod.TryInstallSnapshot"/> at recovery. The payload format is owned by
/// the method that built it — the sidecar container treats it as opaque bytes — and carries every
/// configuration stamp the method validates at install (the temporal method stamps its build-time
/// implicit timezone, so a snapshot built under one timezone can never install into a method
/// configured with another).
/// </summary>
public abstract class ValueIndexSnapshot
{
    /// <summary>The serialized payload's byte size.</summary>
    public abstract int PayloadSize { get; }

    /// <summary>Writes the payload into <paramref name="destination"/>, whose length is at least <see cref="PayloadSize"/> bytes.</summary>
    /// <param name="destination">The destination buffer.</param>
    public abstract void WriteTo(Span<byte> destination);
}
