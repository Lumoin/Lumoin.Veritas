using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// The value a <see cref="RandomnessDelegate"/> returns. The field corresponding
/// to the request's <see cref="RandomnessKind"/> is populated; the others are
/// default-valued.
/// </summary>
/// <param name="Kind">The kind of randomness this value carries; matches the request's kind.</param>
/// <param name="Double">The uniform double in [0.0, 1.0), for <see cref="RandomnessKind.UniformDouble"/>.</param>
/// <param name="Uuid">The fresh UUID, for <see cref="RandomnessKind.Uuid"/>.</param>
/// <param name="Bytes">The entropy bytes, for <see cref="RandomnessKind.Bytes"/>.</param>
[DebuggerDisplay("{Kind}")]
public readonly record struct RandomnessValue(
    RandomnessKind Kind,
    double Double,
    Guid Uuid,
    ReadOnlyMemory<byte> Bytes);
