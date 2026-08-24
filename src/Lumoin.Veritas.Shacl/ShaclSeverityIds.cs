using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for the three SHACL severity
/// IRIs that appear as objects of <c>sh:severity</c> and
/// <c>sh:resultSeverity</c>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §4.6, severity IRIs are not closed — implementations
/// may accept custom severities. The current loader accepts only the
/// three standard IRIs captured here; a future refinement will widen this
/// to a Purpose-style struct that preserves unknown IRIs as data.
/// </remarks>
/// <param name="Info"><c>sh:Info</c></param>
/// <param name="Warning"><c>sh:Warning</c></param>
/// <param name="Violation"><c>sh:Violation</c></param>
public readonly record struct ShaclSeverityIds(
    IriId Info,
    IriId Warning,
    IriId Violation)
{
    /// <summary>
    /// Interns every severity SHACL IRI into <paramref name="dictionary"/>
    /// and returns their narrowed <see cref="IriId"/> handles.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved severity IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclSeverityIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclSeverityIds(
            Info: dictionary.GetOrAdd(new NamedNode(ShaclSeverityVocabulary.Info)),
            Warning: dictionary.GetOrAdd(new NamedNode(ShaclSeverityVocabulary.Warning)),
            Violation: dictionary.GetOrAdd(new NamedNode(ShaclSeverityVocabulary.Violation)));
    }
}
