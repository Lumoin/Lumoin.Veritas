namespace Lumoin.Veritas.Replication;

/// <summary>The class of fault a dotted-difference exchange converted into a value outcome, so the diagnosis on the trace distinguishes a torn transport from a peer violating the protocol, an identity collision, or an exhausted write-back.</summary>
public enum DottedDifferenceFaultKind
{
    /// <summary>The transport itself faulted: a refused or torn connection, an I/O fault, a disposed stream.</summary>
    Transport = 0,

    /// <summary>The peer violated the channel or session protocol: a malformed or truncated frame, an out-of-order envelope, an unknown kind, a posture violation.</summary>
    Protocol = 1,

    /// <summary>The peer presented causal coverage or a dot beyond the local identity axis's own maximum — a second minter under this replica's identity; the exchange refused before applying the colliding knowledge.</summary>
    IdentityCollision = 2,

    /// <summary>An adopt write-back exhausted its commit retries against a concurrently-advancing journal head; committed prefix commits stand and a re-run converges.</summary>
    ConflictExhausted = 3,
}
