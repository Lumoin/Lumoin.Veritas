namespace Lumoin.Veritas.Json;

/// <summary>
/// Attempts to locate a property by name within a node whose kind is
/// <see cref="JsonNodeKind.Object"/>.
/// </summary>
/// <remarks>
/// <para>
/// JSON-LD processing locates many properties by name during expansion and
/// context processing — the algorithm is structured around named keywords such
/// as <c>@id</c>, <c>@type</c>, <c>@context</c>, <c>@value</c>, and so on. This
/// delegate provides the fast point-lookup path that avoids enumerating every
/// property when only one is needed.
/// </para>
/// <para>
/// The lookup is case sensitive and uses ordinal comparison, matching the
/// JSON-LD specification's treatment of keyword and term names.
/// </para>
/// </remarks>
/// <param name="handle">
/// The handle of a node whose kind is <see cref="JsonNodeKind.Object"/>.
/// </param>
/// <param name="name">The property name to look up.</param>
/// <param name="value">
/// When the method returns <see langword="true"/>, contains the matching
/// property's value as a <see cref="JsonNode"/>. When the method returns
/// <see langword="false"/>, contains a default <see cref="JsonNode"/> whose
/// <see cref="JsonNode.Handle"/> is <see langword="null"/>.
/// </param>
/// <returns>
/// <see langword="true"/> if the property exists; otherwise, <see langword="false"/>.
/// </returns>
public delegate bool TryGetPropertyDelegate(object handle, string name, out JsonNode value);
