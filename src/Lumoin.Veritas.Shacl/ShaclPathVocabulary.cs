using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// UTF-8 IRI constants for SHACL property-path construction vocabulary.
/// </summary>
/// <remarks>
/// These predicates encode property-path operators per SHACL 1.2 Core
/// §2.3.1. They appear as predicates in the shape graph whose object is
/// the inner path.
/// </remarks>
public static class ShaclPathVocabulary
{
    private static byte[] InversePathBytes { get; } = "http://www.w3.org/ns/shacl#inversePath"u8.ToArray();
    private static byte[] AlternativePathBytes { get; } ="http://www.w3.org/ns/shacl#alternativePath"u8.ToArray();
    private static byte[] ZeroOrMorePathBytes { get; } = "http://www.w3.org/ns/shacl#zeroOrMorePath"u8.ToArray();
    private static byte[] OneOrMorePathBytes { get; } = "http://www.w3.org/ns/shacl#oneOrMorePath"u8.ToArray();
    private static byte[] ZeroOrOnePathBytes { get; } = "http://www.w3.org/ns/shacl#zeroOrOnePath"u8.ToArray();

    /// <summary><c>sh:inversePath</c></summary>
    public static Utf8String InversePath { get; } = new(InversePathBytes);

    /// <summary><c>sh:alternativePath</c></summary>
    public static Utf8String AlternativePath { get; } = new(AlternativePathBytes);

    /// <summary><c>sh:zeroOrMorePath</c></summary>
    public static Utf8String ZeroOrMorePath { get; } = new(ZeroOrMorePathBytes);

    /// <summary><c>sh:oneOrMorePath</c></summary>
    public static Utf8String OneOrMorePath { get; } = new(OneOrMorePathBytes);

    /// <summary><c>sh:zeroOrOnePath</c></summary>
    public static Utf8String ZeroOrOnePath { get; } = new(ZeroOrOnePathBytes);
}
