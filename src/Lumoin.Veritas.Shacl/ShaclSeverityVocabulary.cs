using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for SHACL severity levels.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §4.6, severity is carried by the <c>sh:severity</c>
/// property on a shape and by <c>sh:resultSeverity</c> on a validation
/// result. The SHACL specification defines three standard severity IRIs;
/// custom severities are permitted by the spec but rejected by the
/// current loader (see <see cref="Severity"/>).
/// </remarks>
public static class ShaclSeverityVocabulary
{
    private static readonly byte[] InfoBytes = "http://www.w3.org/ns/shacl#Info"u8.ToArray();
    private static readonly byte[] WarningBytes = "http://www.w3.org/ns/shacl#Warning"u8.ToArray();
    private static readonly byte[] ViolationBytes = "http://www.w3.org/ns/shacl#Violation"u8.ToArray();

    /// <summary><c>sh:Info</c> — informational finding.</summary>
    public static Utf8String Info { get; } = new(InfoBytes);

    /// <summary><c>sh:Warning</c> — soft failure.</summary>
    public static Utf8String Warning { get; } = new(WarningBytes);

    /// <summary><c>sh:Violation</c> — hard constraint failure (default).</summary>
    public static Utf8String Violation { get; } = new(ViolationBytes);

    /// <summary>
    /// Attempts to map a SHACL severity IRI (as UTF-8 bytes) to the
    /// corresponding <see cref="Severity"/> enum value.
    /// </summary>
    /// <param name="iri">The UTF-8 bytes of a candidate severity IRI.</param>
    /// <param name="severity">
    /// When this method returns <c>true</c>, contains the matched
    /// <see cref="Severity"/>. Otherwise the default enum value.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="iri"/> exactly matches
    /// <c>sh:Info</c>, <c>sh:Warning</c>, or <c>sh:Violation</c>;
    /// otherwise <c>false</c>.
    /// </returns>
    public static bool TryGetSeverity(ReadOnlySpan<byte> iri, out Severity severity)
    {
        if(iri.SequenceEqual(InfoBytes))
        {
            severity = Severity.Info;
            return true;
        }

        if(iri.SequenceEqual(WarningBytes))
        {
            severity = Severity.Warning;
            return true;
        }

        if(iri.SequenceEqual(ViolationBytes))
        {
            severity = Severity.Violation;
            return true;
        }

        severity = default;

        return false;
    }

    /// <summary>
    /// Attempts to map a SHACL severity IRI (as a <see cref="Utf8String"/>)
    /// to the corresponding <see cref="Severity"/> enum value.
    /// </summary>
    /// <param name="iri">The candidate severity IRI.</param>
    /// <param name="severity">
    /// When this method returns <c>true</c>, contains the matched
    /// <see cref="Severity"/>. Otherwise the default enum value.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="iri"/> exactly matches
    /// <c>sh:Info</c>, <c>sh:Warning</c>, or <c>sh:Violation</c>;
    /// otherwise <c>false</c>.
    /// </returns>
    public static bool TryGetSeverity(Utf8String iri, out Severity severity) => TryGetSeverity(iri.Span, out severity);
}
