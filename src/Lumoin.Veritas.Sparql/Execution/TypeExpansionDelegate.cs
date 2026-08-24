using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The query-time type-expansion seam: given the class IRI of a bound
/// <c>rdf:type</c> pattern object, returns the class IRIs the pattern
/// matches under the active TBox closure — the class itself and everything
/// below it. The engine evaluates the pattern once per expansion class and
/// unions the solutions, so subclass instances answer superclass queries
/// without materialized typing triples.
/// </summary>
/// <remarks>
/// The supplier owns the closure semantics: an EL classification's
/// subsumee index is the intended producer, but the seam is just a
/// function, so an RDFS-only closure — or a fixed application taxonomy —
/// plugs in identically. The delegate is consulted once per bound type
/// pattern at query setup, never per solution row. Returning an empty
/// collection (or just the input class) leaves the pattern unexpanded.
/// </remarks>
/// <param name="classIri">The bound class IRI of the <c>rdf:type</c> pattern.</param>
/// <returns>The class IRIs the pattern expands to, the input class included.</returns>
public delegate IReadOnlyCollection<Utf8String> TypeExpansionDelegate(Utf8String classIri);
