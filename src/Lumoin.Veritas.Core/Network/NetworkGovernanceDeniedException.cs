using System;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// Indicates that the network-governance policy denied an outbound endpoint call (a SPARQL <c>SERVICE</c> query or
/// a graph resolve / <c>LOAD</c>). It maps the value-based deny verdict onto the exception-based failure channel
/// those seams already use: a transport or resolver that cannot produce a result throws, and the engine's silent
/// handling (<c>SERVICE SILENT</c>, <c>LOAD SILENT</c>) swallows the failure while a non-silent operation
/// propagates it — so a governance denial behaves exactly like an unreachable endpoint. The replication fetch seam,
/// by contrast, declines by returning an empty image, never by this exception. <see cref="Boundary"/> identifies
/// which boundary was denied.
/// </summary>
public sealed class NetworkGovernanceDeniedException : Exception
{
    /// <summary>Initializes a new <see cref="NetworkGovernanceDeniedException"/> with a default message.</summary>
    public NetworkGovernanceDeniedException()
        : base("A network-governance policy denied the call.")
    {
    }

    /// <summary>Initializes a new <see cref="NetworkGovernanceDeniedException"/> with the given message.</summary>
    /// <param name="message">A description of the denial.</param>
    public NetworkGovernanceDeniedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="NetworkGovernanceDeniedException"/> with the given message and inner exception.</summary>
    /// <param name="message">A description of the denial.</param>
    /// <param name="innerException">The exception that caused this denial.</param>
    public NetworkGovernanceDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new <see cref="NetworkGovernanceDeniedException"/> for a denied boundary.</summary>
    /// <param name="boundary">The boundary the policy denied.</param>
    public NetworkGovernanceDeniedException(NetworkBoundary boundary)
        : base($"The network-governance policy denied a {boundary} call.")
    {
        Boundary = boundary;
    }

    /// <summary>The boundary the policy denied; the enum default when constructed without one.</summary>
    public NetworkBoundary Boundary { get; }
}
