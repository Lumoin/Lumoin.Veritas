using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for SHACL validation-report
/// vocabulary — the classes and predicates that make up a
/// <c>sh:ValidationReport</c> graph.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §4. These handles are used when the validator
/// serializes a validation report back as RDF: every result triple's
/// predicate is one of these <see cref="IriId"/>s.
/// </remarks>
/// <param name="ValidationReport"><c>sh:ValidationReport</c></param>
/// <param name="ValidationResult"><c>sh:ValidationResult</c></param>
/// <param name="Conforms"><c>sh:conforms</c></param>
/// <param name="Result"><c>sh:result</c></param>
/// <param name="FocusNode"><c>sh:focusNode</c></param>
/// <param name="Value"><c>sh:value</c></param>
/// <param name="ResultPath"><c>sh:resultPath</c></param>
/// <param name="SourceShape"><c>sh:sourceShape</c></param>
/// <param name="SourceConstraintComponent"><c>sh:sourceConstraintComponent</c></param>
/// <param name="ResultSeverity"><c>sh:resultSeverity</c></param>
/// <param name="ResultMessage"><c>sh:resultMessage</c></param>
public readonly record struct ShaclResultIds(
    IriId ValidationReport,
    IriId ValidationResult,
    IriId Conforms,
    IriId Result,
    IriId FocusNode,
    IriId Value,
    IriId ResultPath,
    IriId SourceShape,
    IriId SourceConstraintComponent,
    IriId ResultSeverity,
    IriId ResultMessage)
{
    /// <summary>
    /// Interns every validation-report SHACL IRI into
    /// <paramref name="dictionary"/> and returns their narrowed
    /// <see cref="IriId"/> handles.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved validation-report IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclResultIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclResultIds(
            ValidationReport: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.ValidationReport)),
            ValidationResult: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.ValidationResult)),
            Conforms: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.Conforms)),
            Result: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.Result)),
            FocusNode: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.FocusNode)),
            Value: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.Value)),
            ResultPath: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.ResultPath)),
            SourceShape: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.SourceShape)),
            SourceConstraintComponent: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.SourceConstraintComponent)),
            ResultSeverity: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.ResultSeverity)),
            ResultMessage: dictionary.GetOrAdd(new NamedNode(ShaclResultsVocabulary.ResultMessage)));
    }
}
