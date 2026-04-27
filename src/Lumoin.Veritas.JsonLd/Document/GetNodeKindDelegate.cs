namespace Lumoin.Veritas.JsonLd.Document;

/// <summary>
/// Returns the <see cref="JsonLdNodeKind"/> of the node referred to by an opaque
/// navigator handle.
/// </summary>
/// <remarks>
/// Every <see cref="JsonLdNode"/> has a single kind that does not change for the
/// lifetime of its handle. Implementations may compute the kind eagerly when the
/// node is constructed or read it from the handle on each call; the JSON-LD
/// library makes no assumption about which.
/// </remarks>
/// <param name="handle">
/// The handle whose underlying type the navigator's adapter understands.
/// </param>
/// <returns>The kind of the node.</returns>
public delegate JsonLdNodeKind GetNodeKindDelegate(object handle);
