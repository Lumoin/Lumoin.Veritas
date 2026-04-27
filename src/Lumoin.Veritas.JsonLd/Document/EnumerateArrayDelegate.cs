using System.Collections.Generic;

namespace Lumoin.Veritas.JsonLd.Document;

/// <summary>
/// Yields every element of a node whose kind is <see cref="JsonLdNodeKind.Array"/>,
/// in document order.
/// </summary>
/// <remarks>
/// <para>
/// The enumeration is lazy and may not allocate per element. Implementations are
/// free to produce a struct enumerator for zero-allocation iteration; consumers
/// receive an <see cref="IEnumerable{T}"/> for ergonomics.
/// </para>
/// <para>
/// The yielded <see cref="JsonLdNode"/> values share the same
/// <see cref="JsonLdNode.Navigator"/> as the array node, since they are produced
/// by the same adapter.
/// </para>
/// </remarks>
/// <param name="handle">
/// The handle of a node whose kind is <see cref="JsonLdNodeKind.Array"/>.
/// </param>
/// <returns>The array's elements in document order.</returns>
public delegate IEnumerable<JsonLdNode> EnumerateArrayDelegate(object handle);
