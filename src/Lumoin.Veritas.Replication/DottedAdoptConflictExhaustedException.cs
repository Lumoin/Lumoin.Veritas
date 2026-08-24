using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The documented fail-closed signal of a dotted session's adopt seam: the write-back behind an apply, drop, or
/// terminal-fold hook exhausted its commit retries against a concurrently-advancing journal head. The session's
/// run loop propagates it and the channel boundary converts it to the NAMED conflict-exhausted value outcome —
/// never a silent success and never a desynchronized context fold; committed prefix commits stand and re-running
/// the session converges.
/// </summary>
public sealed class DottedAdoptConflictExhaustedException: InvalidOperationException
{
    /// <summary>Creates the signal with the standard message.</summary>
    public DottedAdoptConflictExhaustedException()
        : base("A dotted adopt write-back exhausted its commit retries against a concurrently-advancing journal head; committed prefix commits stand, and re-running the session converges.")
    {
    }

    /// <summary>Creates the signal with a specific message.</summary>
    /// <param name="message">The message.</param>
    public DottedAdoptConflictExhaustedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the signal with a specific message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying fault.</param>
    public DottedAdoptConflictExhaustedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
