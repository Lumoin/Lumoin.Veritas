using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for SHACL validation-report vocabulary.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §4, validation results are RDF graphs rooted at a
/// <c>sh:ValidationReport</c> instance. These IRIs identify the report
/// and result classes and the properties that make up each result.
/// </remarks>
public static class ShaclResultsVocabulary
{
    private static byte[] ValidationReportBytes { get; } = "http://www.w3.org/ns/shacl#ValidationReport"u8.ToArray();
    private static byte[] ValidationResultBytes { get; } = "http://www.w3.org/ns/shacl#ValidationResult"u8.ToArray();
    private static byte[] ConformsBytes { get; } = "http://www.w3.org/ns/shacl#conforms"u8.ToArray();
    private static byte[] ResultBytes { get; } = "http://www.w3.org/ns/shacl#result"u8.ToArray();
    private static byte[] FocusNodeBytes { get; } = "http://www.w3.org/ns/shacl#focusNode"u8.ToArray();
    private static byte[] ValueBytes { get; } = "http://www.w3.org/ns/shacl#value"u8.ToArray();
    private static byte[] ResultPathBytes { get; } = "http://www.w3.org/ns/shacl#resultPath"u8.ToArray();
    private static byte[] SourceShapeBytes { get; } = "http://www.w3.org/ns/shacl#sourceShape"u8.ToArray();
    private static byte[] SourceConstraintComponentBytes { get; } = "http://www.w3.org/ns/shacl#sourceConstraintComponent"u8.ToArray();
    private static byte[] ResultSeverityBytes { get; } = "http://www.w3.org/ns/shacl#resultSeverity"u8.ToArray();
    private static byte[] ResultMessageBytes { get; } = "http://www.w3.org/ns/shacl#resultMessage"u8.ToArray();

    /// <summary><c>sh:ValidationReport</c></summary>
    public static Utf8String ValidationReport { get; } = new(ValidationReportBytes);

    /// <summary><c>sh:ValidationResult</c></summary>
    public static Utf8String ValidationResult { get; } = new(ValidationResultBytes);

    /// <summary><c>sh:conforms</c></summary>
    public static Utf8String Conforms { get; } = new(ConformsBytes);

    /// <summary><c>sh:result</c></summary>
    public static Utf8String Result { get; } = new(ResultBytes);

    /// <summary><c>sh:focusNode</c></summary>
    public static Utf8String FocusNode { get; } = new(FocusNodeBytes);

    /// <summary><c>sh:value</c></summary>
    public static Utf8String Value { get; } = new(ValueBytes);

    /// <summary><c>sh:resultPath</c></summary>
    public static Utf8String ResultPath { get; } = new(ResultPathBytes);

    /// <summary><c>sh:sourceShape</c></summary>
    public static Utf8String SourceShape { get; } = new(SourceShapeBytes);

    /// <summary><c>sh:sourceConstraintComponent</c></summary>
    public static Utf8String SourceConstraintComponent { get; } = new(SourceConstraintComponentBytes);

    /// <summary><c>sh:sourceConstraint</c> — the SPARQL-based constraint node a result came from (SHACL-SPARQL §5.3).</summary>
    public static Utf8String SourceConstraint { get; } = new("http://www.w3.org/ns/shacl#sourceConstraint"u8.ToArray());

    /// <summary><c>sh:resultSeverity</c></summary>
    public static Utf8String ResultSeverity { get; } = new(ResultSeverityBytes);

    /// <summary><c>sh:resultMessage</c></summary>
    public static Utf8String ResultMessage { get; } = new(ResultMessageBytes);
}
