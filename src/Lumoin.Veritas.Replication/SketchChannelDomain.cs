namespace Lumoin.Veritas.Replication;

/// <summary>
/// The reconciliation domain a sketch channel carries, stamped as a single leading byte on every request and
/// response so a contract mismatch is a wire-level named refusal rather than a silent mis-combine. The byte doubles
/// as the frame's format discriminator: only <see cref="Structural"/> and <see cref="ContentHash"/> are valid on the
/// wire, and any other leading byte is refused.
/// </summary>
public enum SketchChannelDomain
{
    /// <summary>The reserved sentinel — never written to the wire (a frame carrying it is refused). It is the "no frame" value an absent-peer <see cref="SketchFetchResult"/> carries, which reports itself unavailable.</summary>
    None = 0,

    /// <summary>The structural domain: items pack a triple's local term identifiers, which are relative to a dictionary, so the epoch stamp is compared for equality and a peer keyed to a different dictionary epoch is refused before any combine.</summary>
    Structural = 1,

    /// <summary>The content-hash domain: items hash the triple's term content and so are epoch-independent, so the epoch stamp MUST be the reserved <c>0</c> and any other value is out of contract.</summary>
    ContentHash = 2,
}
