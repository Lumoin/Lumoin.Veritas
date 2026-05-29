using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for the three standard SHACL severity levels.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §4.6, severity is carried by the <c>sh:severity</c>
/// property on a shape and by <c>sh:resultSeverity</c> on a validation
/// result. The SHACL specification defines three standard severity IRIs
/// but also permits a shape to declare any IRI as its severity; the loader
/// accepts custom severity IRIs and carries them through verbatim (see
/// <see cref="Severity"/>). These constants name the three standard ones.
/// </remarks>
public static class ShaclSeverityVocabulary
{
    private static byte[] InfoBytes { get; } = "http://www.w3.org/ns/shacl#Info"u8.ToArray();
    private static byte[] WarningBytes { get; } = "http://www.w3.org/ns/shacl#Warning"u8.ToArray();
    private static byte[] ViolationBytes { get; } = "http://www.w3.org/ns/shacl#Violation"u8.ToArray();

    /// <summary><c>sh:Info</c> — informational finding.</summary>
    public static Utf8String Info { get; } = new(InfoBytes);

    /// <summary><c>sh:Warning</c> — soft failure.</summary>
    public static Utf8String Warning { get; } = new(WarningBytes);

    /// <summary><c>sh:Violation</c> — hard constraint failure (default).</summary>
    public static Utf8String Violation { get; } = new(ViolationBytes);
}
