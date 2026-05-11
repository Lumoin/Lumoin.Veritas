using Lumoin.Veritas.Core;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Parses a UTF-8 JSON byte sequence into a <see cref="JsonLdNode"/> tree.
/// </summary>
/// <remarks>
/// <para>
/// This delegate is the parsing boundary in the JSON-LD pipeline. The
/// <see cref="ContextResolverDelegate"/> fetches raw UTF-8 bytes; this delegate
/// turns them into the navigator-backed node tree the JSON-LD algorithms
/// consume. Separating I/O from parsing lets either be swapped without
/// affecting the other.
/// </para>
/// <para>
/// The input is a <see cref="Utf8String"/> rather than a .NET <see cref="string"/>
/// so the bytes flow through the pipeline without UTF-8 to UTF-16 transcoding.
/// JSON is UTF-8 by specification (RFC 8259, section 8.1) and the supplying
/// adapter parses UTF-8 directly.
/// </para>
/// <para>
/// The returned <see cref="JsonLdNode"/> must have a lifetime independent of
/// the input bytes, since the input may be released as soon as the parser
/// returns. Adapters whose nodes hold references back into the source bytes
/// must clone or copy as part of parsing.
/// </para>
/// </remarks>
/// <param name="utf8Json">The UTF-8 encoded JSON document.</param>
/// <returns>
/// The root <see cref="JsonLdNode"/> of the parsed document.
/// </returns>
public delegate JsonLdNode JsonLdParserDelegate(Utf8String utf8Json);
