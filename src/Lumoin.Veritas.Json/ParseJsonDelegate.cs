using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Json;

/// <summary>
/// Parses a UTF-8 JSON byte sequence into a <see cref="JsonNode"/> tree.
/// </summary>
/// <remarks>
/// <para>
/// This delegate is the parsing boundary of the JSON model: it turns raw UTF-8
/// bytes into the navigator-backed node tree that consumers read through
/// <see cref="JsonNodeNavigator"/>. The concrete parser (the
/// <c>System.Text.Json</c>-backed adapter, or any alternative back end) is
/// supplied here, so consumers depend on the node model without binding to a
/// specific JSON library.
/// </para>
/// <para>
/// The input is a <see cref="Utf8String"/> rather than a .NET <see cref="string"/>
/// so the bytes flow through the pipeline without UTF-8 to UTF-16 transcoding.
/// JSON is UTF-8 by specification (RFC 8259, section 8.1) and the supplying
/// adapter parses UTF-8 directly.
/// </para>
/// <para>
/// The returned <see cref="JsonNode"/> must have a lifetime independent of
/// the input bytes, since the input may be released as soon as the parser
/// returns. Adapters whose nodes hold references back into the source bytes
/// must clone or copy as part of parsing.
/// </para>
/// </remarks>
/// <param name="utf8Json">The UTF-8 encoded JSON document.</param>
/// <returns>
/// The root <see cref="JsonNode"/> of the parsed document.
/// </returns>
public delegate JsonNode ParseJsonDelegate(Utf8String utf8Json);
