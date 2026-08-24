using System;
using System.IO;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The typed signal a connection-opening seam raises when the peer answered the EXPLICIT unknown-service
/// refusal byte to the dialed service selector — the one evidence on which a dotted exchange may report the
/// peer as remove-aware-unsupported. An absent reply (death, partition, crash) is an ordinary I/O fault and
/// reports peer-unavailable instead; the split is never inferred from silence.
/// </summary>
public sealed class PeerServiceRefusedException: IOException
{
    /// <summary>Creates the signal with the standard message.</summary>
    public PeerServiceRefusedException()
        : base("The peer answered the unknown-service refusal byte: it does not serve the dialed service selector.")
    {
    }

    /// <summary>Creates the signal with a specific message.</summary>
    /// <param name="message">The message.</param>
    public PeerServiceRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the signal with a specific message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying fault.</param>
    public PeerServiceRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
