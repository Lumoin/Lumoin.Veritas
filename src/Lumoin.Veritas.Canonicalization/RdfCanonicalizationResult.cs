using System.Collections.Generic;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// The result of an RDFC-1.0 canonicalization that also exposes the issued-identifier map: the canonical
/// N-Quads serialization together with the mapping from each input blank-node label to the canonical
/// identifier (<c>c14nN</c>) assigned to it.
/// </summary>
/// <param name="Canonical">The canonical N-Quads serialization (LF terminators, lines sorted).</param>
/// <param name="IssuedIdentifiers">
/// The map from each input blank-node label (as it appeared in the input, without the <c>_:</c> prefix) to its
/// canonical identifier. Empty when the dataset has no blank nodes.
/// </param>
public sealed record RdfCanonicalizationResult(string Canonical, IReadOnlyDictionary<string, string> IssuedIdentifiers);
