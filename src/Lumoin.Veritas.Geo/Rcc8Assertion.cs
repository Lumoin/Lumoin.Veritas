using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// One asserted or derived base relation between two spatial things, named by their RDF terms. The
/// composition closure consumes and produces these; no geometry is involved on either side.
/// </summary>
/// <param name="Subject">The subject term.</param>
/// <param name="Relation">The base relation from the subject to the object.</param>
/// <param name="Object">The object term.</param>
public readonly record struct Rcc8Assertion(RdfTerm Subject, Rcc8Relation Relation, RdfTerm Object);
