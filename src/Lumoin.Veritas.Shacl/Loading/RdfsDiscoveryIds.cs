using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Minimal RDF/RDFS vocabulary term identifiers used during discovery.
/// </summary>
/// <remarks>
/// Currently only <c>rdf:type</c> is needed — discovery queries the
/// triple store for subjects typed as <c>sh:NodeShape</c>,
/// <c>sh:PropertyShape</c>, <c>sh:Shape</c>, or <c>sh:ShapeClass</c>,
/// all of which pivot on the <c>rdf:type</c> predicate. Kept as a
/// dedicated struct rather than reusing
/// <see cref="Rdf.RdfsVocabularyIds"/> so discovery callers do not need
/// to pre-resolve the full RDFS vocabulary when only
/// <c>rdf:type</c> is required. The broader
/// <see cref="Rdf.RdfsVocabularyIds"/> is used by population-phase
/// factories (<see cref="Constraints.ClassConstraint"/>,
/// <see cref="Constraints.RootClassConstraint"/>).
/// </remarks>
/// <param name="RdfType">The <c>rdf:type</c> predicate identifier.</param>
internal readonly record struct RdfsDiscoveryIds(IriId RdfType);
