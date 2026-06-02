using System.Text.Json;

namespace Lumoin.Veritas.Rdf.Json;

/// <summary>
/// Provides pre-configured <see cref="JsonSerializerOptions"/> for Veritas JSON serialization.
/// </summary>
/// <remarks>
/// <para>
/// All JSON serialization in the Veritas stack uses options obtained from this class.
/// The options are derived from the source-generated <see cref="VeritasJsonContext"/>
/// to ensure AOT compatibility and avoid reflection-based serialization.
/// </para>
/// <para>
/// Domain types across the Veritas projects are clean POCOs with no
/// <c>System.Text.Json</c> attributes. All serialization concerns — property naming,
/// type discrimination, special formatting — are handled by converters registered
/// in <see cref="VeritasJsonContext"/>.
/// </para>
/// </remarks>
public static class VeritasJsonSerializerOptions
{
    /// <summary>
    /// Gets the default <see cref="JsonSerializerOptions"/> configured for Veritas conventions.
    /// </summary>
    /// <remarks>
    /// Returns the options from the source-generated <see cref="VeritasJsonContext"/>.
    /// These use camelCase property naming, omit null values, serialize enums as strings,
    /// and include all Veritas-specific converters. The instance is frozen after first use.
    /// </remarks>
    public static JsonSerializerOptions Default => VeritasJsonContext.Default.Options;
}
