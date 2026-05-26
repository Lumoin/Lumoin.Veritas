using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// What a caller needs from an <see cref="IdentifierDelegate"/>: a fresh identity
/// for a stated <see cref="Purpose"/>, optionally derived from <see cref="Salt"/>.
/// </summary>
/// <remarks>
/// The identifier type is <see cref="Guid"/> for now; an abstract identifier type
/// is deferred until a caller actually needs something other than a GUID. Passed by
/// <see langword="in"/>; no allocation.
/// </remarks>
/// <param name="Purpose">Why the identifier is needed.</param>
/// <param name="Salt">An optional caller-supplied salt allowing a delegate to produce a deterministic identifier.</param>
[DebuggerDisplay("{Purpose} salt.len={Salt.Length}")]
public readonly record struct IdentifierRequest(
    IdentifierPurpose Purpose,
    ReadOnlyMemory<byte> Salt);
