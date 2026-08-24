namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One entry of a W3C JSON-LD API test manifest (<c>expand</c> / <c>compact</c> / <c>toRdf</c>), loaded by
/// <see cref="JsonLdManifestLoader"/>. Paths are absolute (resolved against the manifest's directory); the
/// retrieval <see cref="InputUrl"/> and <see cref="BaseIri"/> are the suite's URL space, which the runner's
/// file-backed resolver maps back to <see cref="CorpusDirectory"/>.
/// </summary>
/// <param name="Id">The entry's <c>@id</c> (for example <c>#t0001</c>).</param>
/// <param name="Name">The entry's human-readable name.</param>
/// <param name="Operation">The operation under test: <c>expand</c>, <c>compact</c>, or <c>toRdf</c>.</param>
/// <param name="IsPositive">Whether this is a positive evaluation test (a negative test expects an error).</param>
/// <param name="BaseIri">The manifest's <c>baseIri</c> — the suite's retrieval URL space.</param>
/// <param name="InputPath">The absolute path of the input document.</param>
/// <param name="InputUrl">The input document's retrieval URL (<see cref="BaseIri"/> + the entry's <c>input</c>).</param>
/// <param name="ExpectPath">The absolute path of the expected-result document, or <see langword="null"/> for a negative test.</param>
/// <param name="ContextPath">The absolute path of the compaction context document (compact tests only), or <see langword="null"/>.</param>
/// <param name="ExpectErrorCode">The expected error code (negative tests only), or <see langword="null"/>.</param>
/// <param name="OptionBase">The <c>option.base</c> override, or <see langword="null"/>.</param>
/// <param name="ExpandContextPath">The absolute path of the <c>option.expandContext</c> document, or <see langword="null"/>.</param>
/// <param name="ProcessingMode">The <c>option.processingMode</c> (for example <c>json-ld-1.1</c>), or <see langword="null"/>.</param>
/// <param name="SpecVersion">The <c>option.specVersion</c> (for example <c>json-ld-1.1</c>), or <see langword="null"/>.</param>
/// <param name="CompactArrays">The <c>option.compactArrays</c> flag (defaults to <see langword="true"/> per the API default).</param>
/// <param name="UseNativeTypes">The <c>option.useNativeTypes</c> flag (fromRdf: emit native JSON numbers/booleans; defaults to <see langword="false"/>).</param>
/// <param name="UseRdfType">The <c>option.useRdfType</c> flag (fromRdf: keep <c>rdf:type</c> as a property; defaults to <see langword="false"/>).</param>
/// <param name="FramePath">The absolute path of the frame document (frame tests only), or <see langword="null"/>.</param>
/// <param name="OmitGraph">The <c>option.omitGraph</c> flag (frame tests; defaults to <see langword="true"/> per the 1.1 default).</param>
/// <param name="CorpusDirectory">The absolute <c>Material/JsonLd</c> directory the suite's URLs map back to.</param>
internal sealed record JsonLdTestCase(
    string Id,
    string Name,
    string Operation,
    bool IsPositive,
    string BaseIri,
    string InputPath,
    string InputUrl,
    string? ExpectPath,
    string? ContextPath,
    string? ExpectErrorCode,
    string? OptionBase,
    string? ExpandContextPath,
    string? ProcessingMode,
    string? SpecVersion,
    string? RdfDirection,
    bool CompactArrays,
    bool UseNativeTypes,
    bool UseRdfType,
    string? FramePath,
    bool OmitGraph,
    string CorpusDirectory);
