using System;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A deliberately injected network fault, raised by a fault-injection decorator to simulate a transport failure
/// that throws (a connection reset, an IO error) rather than one that declines by value. It is a test/chaos
/// substrate signal — production transports never raise it — used to certify that the engine and replication
/// session behave correctly when a transport faults under it.
/// </summary>
public sealed class InjectedNetworkFaultException : Exception
{
    /// <summary>Initializes a new <see cref="InjectedNetworkFaultException"/> with a default message.</summary>
    public InjectedNetworkFaultException()
        : base("An injected network fault failed the call.")
    {
    }

    /// <summary>Initializes a new <see cref="InjectedNetworkFaultException"/> with the given message.</summary>
    /// <param name="message">A description of the injected fault.</param>
    public InjectedNetworkFaultException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="InjectedNetworkFaultException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the injected fault.</param>
    /// <param name="innerException">The exception that caused this fault.</param>
    public InjectedNetworkFaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
