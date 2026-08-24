using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The whole-generation faithfulness gate: whether the healed set — the survivors plus the recovered items —
/// peels to an empty residual against the generation's own at-rest-verified sketch. This is the authoritative
/// check the sharded rung composes over, the direct generalization of the single-block rung's requirement that
/// the healed set match the generation sketch. It closes over the generation's sketch host-side.
/// </summary>
/// <param name="recoveredItems">The composed peer-only items the rung proposes to re-ingest.</param>
/// <returns><see langword="true"/> when the healed set matches the generation's own sketch exactly.</returns>
public delegate bool VerifyHealedSetAgainstGenerationSketchDelegate(IReadOnlyCollection<ReadOnlyMemory<byte>> recoveredItems);
