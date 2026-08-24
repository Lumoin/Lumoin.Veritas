namespace Lumoin.Veritas.Core;

/// <summary>
/// Why an identifier is being requested. Lets a delegate vary its strategy by
/// purpose (for example, sequential ids for correlation in tests but real GUIDs
/// for sessions) without separate delegate types per call site.
/// </summary>
public enum IdentifierPurpose
{
    /// <summary>A correlation identifier tying together the steps of one logical operation.</summary>
    Correlation,

    /// <summary>A session identifier (for example, an editing session).</summary>
    Session,

    /// <summary>A general-purpose identifier with no further classification.</summary>
    General
}
