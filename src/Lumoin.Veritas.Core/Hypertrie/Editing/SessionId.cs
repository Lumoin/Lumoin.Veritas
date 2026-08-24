using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// An opaque identifier for one <see cref="EditSession"/>. A
/// fresh <see cref="Guid"/> per session — random, no temporal
/// or content correlation. Routes session lifecycle entries
/// (started, committed, abandoned) in the journal back to the
/// originating session for audit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why opaque.</b> Two distinct sessions can produce the
/// same final state — opening the same buffer of edits twice
/// against the same base, applying them in different orders,
/// committing both. Audit needs to distinguish the two. An
/// opaque identifier guarantees that distinction; a content-
/// addressed identifier would collapse them. The semantic
/// "this commit applied these edits to that base" lives in
/// the journal entry's <c>EditCommitment</c> field — a
/// separate, content-addressed value computed from the
/// effective edits and the base snapshot id.
/// </para>
/// <para>
/// <b>Why a wrapper struct.</b> Type-safety. The library has
/// other <see cref="Guid"/>-shaped values (correlation ids in
/// trace events, request ids in driver code) and a bare
/// <see cref="Guid"/> parameter would invite confusing them at
/// call sites. Wrapping the <see cref="Guid"/> means the
/// compiler rejects passing a correlation id where a session
/// id is expected.
/// </para>
/// </remarks>
[DebuggerDisplay("SessionId({Value})")]
public readonly record struct SessionId(Guid Value)
{
    /// <summary>The default empty identifier; equivalent to <see cref="Guid.Empty"/>.</summary>
    public static SessionId Empty { get; } = new(Guid.Empty);

    /// <summary>
    /// Allocates a fresh session identifier. Each call returns
    /// a distinct value with overwhelmingly negligible collision
    /// probability across realistic horizons.
    /// </summary>
    /// <param name="identifiers">The identifier source; defaults to <see cref="VeritasIdentifiers.System"/>, a fresh <see cref="Guid"/> per call. Pass <see cref="VeritasIdentifiers.Sequential"/> for deterministic session ids in tests.</param>
    /// <returns>A fresh session identifier.</returns>
    public static SessionId NewId(IdentifierDelegate? identifiers = null)
    {
        IdentifierRequest request = new(IdentifierPurpose.Session, default);

        return new SessionId((identifiers ?? VeritasIdentifiers.System)(in request));
    }

    /// <summary>
    /// Returns <c>true</c> when this identifier equals
    /// <see cref="Empty"/>. Diagnostic helper; the journal does
    /// not assign meaning to the empty session id beyond the
    /// uninitialised default.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;
}
