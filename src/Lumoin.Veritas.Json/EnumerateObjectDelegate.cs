using System.Collections.Generic;

namespace Lumoin.Veritas.Json;

/// <summary>
/// Yields every property of a node whose kind is <see cref="JsonNodeKind.Object"/>,
/// in document order, as a sequence of name-and-value pairs.
/// </summary>
/// <remarks>
/// <para>
/// The enumeration order matters for JSON-LD: the specification's term-definition
/// algorithm iterates context entries in document order so that earlier definitions
/// are visible to later ones. Implementations must preserve insertion order from
/// the source document.
/// </para>
/// <para>
/// Property names are returned as .NET strings because JSON-LD uses them as
/// dictionary keys throughout context processing and expansion. The yielded
/// <see cref="JsonNode"/> values share the same
/// <see cref="JsonNode.Navigator"/> as the object node.
/// </para>
/// </remarks>
/// <param name="handle">
/// The handle of a node whose kind is <see cref="JsonNodeKind.Object"/>.
/// </param>
/// <returns>The object's properties in document order.</returns>
public delegate IEnumerable<KeyValuePair<string, JsonNode>> EnumerateObjectDelegate(object handle);
