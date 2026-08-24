using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The provenance a single sketch serve was taken at: the maintainer generation and the dataset StateId the served
/// symbol prefix reflects, plus the symbol budget served. The maintained encoder captures these under the same gate
/// as the symbol copy-out, so the receipt and the served bytes describe ONE set version — the generation-pinned
/// prefix a peer can label its convergence against, and the tag a caller pairs with a follow-up exchange that must
/// continue against the same version.
/// </summary>
/// <param name="Generation">The maintainer generation the served prefix reflects; bumped once per committed delta batch, so a later serve carrying a higher generation is strictly newer.</param>
/// <param name="StateId">The dataset StateId the served prefix reflects — the same StateId the reconciliation feed recorded for this generation.</param>
/// <param name="SymbolBudget">The number of coded symbols the serve produced into the image.</param>
public readonly record struct SketchServeReceipt(long Generation, NodeIdentifier StateId, int SymbolBudget);
