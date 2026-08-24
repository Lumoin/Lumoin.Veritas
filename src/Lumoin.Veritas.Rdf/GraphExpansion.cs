using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// The product of a single <see cref="GraphAlgebras.GraphCoalgebra{TSeed}"/> step:
/// optionally an emitted triple, together with the seeds from which the
/// triple's outward neighbours should be expanded.
/// </summary>
/// <remarks>
/// <para>
/// This struct is the carrier type for unfolds. A null <see cref="Triple"/>
/// with any seeds list means "this seed is a grouping node — expand its
/// neighbours but emit nothing for it". A non-null triple with an empty seeds
/// list means "this seed produces a leaf triple".
/// </para>
/// </remarks>
/// <typeparam name="TSeed">The seed value type.</typeparam>
/// <param name="Triple">The triple produced by this seed, or <c>null</c> if none.</param>
/// <param name="Seeds">The seeds from which the neighbours should be expanded.</param>
public readonly record struct GraphExpansion<TSeed>(
    EncodedTriple? Triple,
    IReadOnlyList<TSeed> Seeds);
