using System;

namespace Lumoin.Veritas.Core.Epistemics;

/// <summary>
/// One reason-code registration presented to the composition-root ladder — the cold-path record
/// binding a code to its class family, its canonical name, its WHY explanation, and its
/// projection-coverage declaration.
/// </summary>
/// <param name="Family">The class family whose block owns <paramref name="Code"/>.</param>
/// <param name="Code">The reason code being registered.</param>
/// <param name="CanonicalName">The human-facing source of truth, as <c>u8</c> bytes (for example the bytes of <c>RdfsSufficient</c>).</param>
/// <param name="Explanation">The cold WHY-text, as <c>u8</c> bytes; a code without one fails the ladder's shape-sanity rung.</param>
/// <param name="Coverage">The projection-coverage declaration; an explicit deferred declaration is valid, an undeclared absence is not.</param>
public sealed record EpistemicReasonRegistration(
    EpistemicReasonClassFamily Family,
    EpistemicReasonCode Code,
    ReadOnlyMemory<byte> CanonicalName,
    ReadOnlyMemory<byte> Explanation,
    EpistemicProjectionCoverage Coverage);
