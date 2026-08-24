using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// The result of <see cref="HypertrieOpsPatching.ApplyDelta"/>.
/// Carries the new canonical depth-3 root, its identifier, and
/// the effective additions and removals — the literal session
/// edits filtered against the base snapshot's contents — so the
/// caller can record exactly what changed on the journal entry.
/// </summary>
/// <param name="Root">The canonical (interned) new root's handle.</param>
/// <param name="Id">The content-addressed identifier of <paramref name="Root"/>.</param>
/// <param name="EffectiveAdditions">Triples actually added — those not already present in the base.</param>
/// <param name="EffectiveRemovals">Triples actually removed — those that were present in the base.</param>
/// <remarks>
/// <para>
/// When the literal delta produces an empty effective delta — every
/// add was already present and every remove was already absent —
/// <see cref="Root"/> and <see cref="Id"/> equal the base
/// snapshot's, and both <see cref="EffectiveAdditions"/> and
/// <see cref="EffectiveRemovals"/> are empty. The caller treats
/// that as the no-op case and writes no commit journal entry.
/// </para>
/// </remarks>
public readonly record struct ApplyDeltaResult(
    NodeHandle Root,
    NodeIdentifier Id,
    IReadOnlyList<EncodedTriple> EffectiveAdditions,
    IReadOnlyList<EncodedTriple> EffectiveRemovals);
