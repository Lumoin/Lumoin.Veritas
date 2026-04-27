using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for the complete SHACL 1.2
/// Core vocabulary, grouped by category.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Resolve(TermDictionary)"/> once at shape-load time to
/// build this struct; hold it for the rest of the load and pass the
/// relevant sub-struct (for example <see cref="Constraints"/>) into
/// narrower consumers. The grouping by sub-struct lets a function that
/// only needs constraint-parameter IRIs take a
/// <see cref="ShaclConstraintIds"/> rather than the full vocabulary.
/// </para>
/// <para>
/// <b>Dictionary ownership.</b> <see cref="Resolve(TermDictionary)"/>
/// calls <see cref="TermDictionary.GetOrAdd(NamedNode)"/> on every SHACL
/// term, which adds each IRI to the dictionary if it was not already
/// present. This costs a handful of extra dictionary entries but keeps
/// every returned <see cref="IriId"/> valid regardless of which terms
/// the source shape graph happened to reference. Constraint evaluators
/// can therefore compare incoming predicate IDs directly against these
/// handles without guarding against a "missing" sentinel.
/// </para>
/// <para>
/// <b>Pool interning.</b> The vocabulary <see cref="Utf8String"/> values
/// are backed by static byte arrays with precomputed hashes. Dictionary
/// equality is by byte content, so there is no need to intern them
/// through a <see cref="Utf8StringPool"/> first; they are compared
/// directly against pool-interned IRIs coming from the shape graph.
/// </para>
/// </remarks>
/// <param name="Shapes">Shape classes and shape-carried predicates.</param>
/// <param name="Targets">Target predicates.</param>
/// <param name="Constraints">All 43 constraint-component parameter predicates.</param>
/// <param name="NodeKinds">The six node-kind value IRIs.</param>
/// <param name="Severities">The three severity value IRIs.</param>
/// <param name="Paths">Property-path construction predicates.</param>
/// <param name="Results">Validation-report classes and predicates.</param>
public readonly record struct ShaclVocabularyIds(
    ShaclShapeIds Shapes,
    ShaclTargetIds Targets,
    ShaclConstraintIds Constraints,
    ShaclNodeKindIds NodeKinds,
    ShaclSeverityIds Severities,
    ShaclPathIds Paths,
    ShaclResultIds Results)
{
    /// <summary>
    /// Interns the complete SHACL 1.2 Core vocabulary into
    /// <paramref name="dictionary"/> and returns the resolved
    /// <see cref="IriId"/> handles grouped by category.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved SHACL vocabulary handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclVocabularyIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclVocabularyIds(
            Shapes: ShaclShapeIds.Resolve(dictionary),
            Targets: ShaclTargetIds.Resolve(dictionary),
            Constraints: ShaclConstraintIds.Resolve(dictionary),
            NodeKinds: ShaclNodeKindIds.Resolve(dictionary),
            Severities: ShaclSeverityIds.Resolve(dictionary),
            Paths: ShaclPathIds.Resolve(dictionary),
            Results: ShaclResultIds.Resolve(dictionary));
    }
}
