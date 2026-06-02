using System.Text.Json.Serialization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for Veritas types.
/// </summary>
/// <remarks>
/// <para>
/// Provides AOT-compatible and trimming-safe serialization configuration.
/// Domain types are clean POCOs with no <c>System.Text.Json</c> attributes; all
/// serialization behaviour is controlled by custom converters.
/// </para>
/// <para>
/// Core types (<c>RdfTerm</c>, <c>Quad</c>, etc.) contain <c>Utf8String</c> which
/// has a ref-like <c>Span</c> property that the source generator cannot process.
/// These types are serialized entirely by <see cref="RdfTermJsonConverter"/> and
/// <see cref="QuadJsonConverter"/> and are not registered here directly.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true,
    Converters = [typeof(RdfTermJsonConverter), typeof(QuadJsonConverter)])]
[JsonSerializable(typeof(TextDirection))]
public partial class VeritasJsonContext: JsonSerializerContext
{
}
