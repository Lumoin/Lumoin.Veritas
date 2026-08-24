using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Pre-resolved SHACL vocabulary identifiers needed during the
/// population pass. Superset of <see cref="ShaclDiscoveryIds"/>: where
/// discovery only needed class terms, <c>sh:path</c>, and the target
/// predicates as a collective list, population needs individual
/// identifiers for each target predicate, for the metadata vocabulary
/// (<c>sh:severity</c>, <c>sh:deactivated</c>, <c>sh:message</c>), and
/// for each severity-level IRI (<c>sh:Violation</c>, <c>sh:Warning</c>,
/// <c>sh:Info</c>).
/// </summary>
/// <remarks>
/// <para>
/// Resolved once at loader startup from the static
/// <see cref="ShaclCoreVocabulary"/> and <see cref="ShaclSeverityVocabulary"/>
/// <see cref="Lumoin.Base.Utf8String"/> constants, and then passed through the
/// <see cref="ShapePopulationContext"/> to every per-shape population
/// invocation. Every comparison against an incoming triple's predicate
/// or object is then an integer-id equality check, not a
/// byte-content string compare.
/// </para>
/// <para>
/// Kept separate from <see cref="ShaclDiscoveryIds"/> because discovery
/// and population have different needs and growing either struct to
/// cover both would conflate concerns. The two structs share the SHACL
/// Core path predicate <see cref="Path"/>, which is trivially
/// duplicated.
/// </para>
/// </remarks>
/// <param name="Path">The <c>sh:path</c> predicate identifier.</param>
/// <param name="TargetClass">The <c>sh:targetClass</c> predicate identifier.</param>
/// <param name="TargetNode">The <c>sh:targetNode</c> predicate identifier.</param>
/// <param name="TargetSubjectsOf">The <c>sh:targetSubjectsOf</c> predicate identifier.</param>
/// <param name="TargetObjectsOf">The <c>sh:targetObjectsOf</c> predicate identifier.</param>
/// <param name="Severity">The <c>sh:severity</c> predicate identifier.</param>
/// <param name="Deactivated">The <c>sh:deactivated</c> predicate identifier.</param>
/// <param name="Message">The <c>sh:message</c> predicate identifier.</param>
/// <param name="Violation">The <c>sh:Violation</c> severity-level identifier.</param>
/// <param name="Warning">The <c>sh:Warning</c> severity-level identifier.</param>
/// <param name="Info">The <c>sh:Info</c> severity-level identifier.</param>
/// <param name="Sparql">The <c>sh:sparql</c> predicate identifier (links a shape to a SPARQL-based constraint).</param>
/// <param name="Select">The <c>sh:select</c> predicate identifier (the constraint's SELECT query text).</param>
/// <param name="Prefixes">The <c>sh:prefixes</c> predicate identifier (the constraint's prefix-declaration subjects).</param>
/// <param name="Declare">The <c>sh:declare</c> predicate identifier (one namespace declaration on a prefix-declaration subject).</param>
/// <param name="Prefix">The <c>sh:prefix</c> predicate identifier (a declaration's prefix string).</param>
/// <param name="Namespace">The <c>sh:namespace</c> predicate identifier (a declaration's namespace IRI).</param>
/// <param name="RdfsClass">The <c>rdfs:Class</c> identifier — a shape that is also an <c>rdfs:Class</c> implicitly targets its instances (§2.1.3.3).</param>
internal readonly record struct ShaclPopulationIds(
    IriId Path,
    IriId TargetClass,
    IriId TargetNode,
    IriId TargetSubjectsOf,
    IriId TargetObjectsOf,
    IriId Severity,
    IriId Deactivated,
    IriId Message,
    IriId Violation,
    IriId Warning,
    IriId Info,
    IriId Sparql,
    IriId Select,
    IriId Prefixes,
    IriId Declare,
    IriId Prefix,
    IriId Namespace,
    IriId RdfsClass);
