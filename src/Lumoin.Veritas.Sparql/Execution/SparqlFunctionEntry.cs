using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One catalog entry of the extension-function seam: the IRI call expressions name the function by,
/// paired with its implementation faces — the scalar face a per-solution call invokes, the aggregate
/// face a recognized aggregate call folds a group through, or both. Function catalogs expose their
/// members as named statics of this type so a composition site registers them by name
/// (<see cref="SparqlFunctionRegistryBuilder.Add(SparqlFunctionEntry)"/>) and a reader discovers the
/// shipped set by browsing the catalog's properties. At least one face must be non-null; the builder
/// throws on an entry carrying neither.
/// </summary>
/// <param name="FunctionIri">The IRI call expressions name the function by.</param>
/// <param name="Scalar">The scalar implementation, or <see langword="null"/> for an aggregate-only entry.</param>
/// <param name="Aggregate">The aggregate implementation, or <see langword="null"/> for a scalar-only entry.</param>
public readonly record struct SparqlFunctionEntry(Utf8String FunctionIri, SparqlFunctionDelegate? Scalar, SparqlAggregateDelegate? Aggregate = null);
