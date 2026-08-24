using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Common SHACL namespace constants.
/// </summary>
/// <remarks>
/// <para>
/// IRI constants for specific categories are split into separate classes
/// to avoid nested-type constraints (CA1034): <see cref="ShaclCoreVocabulary"/>
/// for shape classes and target predicates, <see cref="ShaclPathVocabulary"/>
/// for path construction, <see cref="ShaclConstraintVocabulary"/> for
/// constraint parameters, <see cref="ShaclSeverityVocabulary"/> for
/// severity levels, <see cref="ShaclResultsVocabulary"/> for validation
/// report terms, and <see cref="ShaclNodeKindVocabulary"/> for node-kind
/// parameter values.
/// </para>
/// <para>
/// Each vocabulary class exposes its terms as <see cref="Utf8String"/>
/// values backed by static byte arrays. This matches the UTF-8-native
/// storage model used across the library and avoids repeatedly encoding
/// UTF-16 strings during shape loading and validation. The original
/// <see cref="string"/>-form namespace constant is kept here for
/// diagnostic messages and for building constraint-component IRIs.
/// </para>
/// </remarks>
public static class ShaclVocabulary
{
    /// <summary>The SHACL namespace IRI, <c>http://www.w3.org/ns/shacl#</c>.</summary>
    public const string Namespace = "http://www.w3.org/ns/shacl#";

    private static byte[] NamespaceBytes { get; } = "http://www.w3.org/ns/shacl#"u8.ToArray();

    /// <summary>
    /// The SHACL namespace IRI as a <see cref="Utf8String"/>. Equivalent in
    /// content to <see cref="Namespace"/> but stored as UTF-8 bytes and
    /// with a precomputed hash, suitable for direct use with the term
    /// dictionary and the string pool.
    /// </summary>
    public static Utf8String NamespaceUtf8 { get; } = new(NamespaceBytes);
}
