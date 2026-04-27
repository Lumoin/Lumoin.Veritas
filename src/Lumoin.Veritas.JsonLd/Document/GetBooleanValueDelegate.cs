namespace Lumoin.Veritas.JsonLd.Document;

/// <summary>
/// Returns the boolean value of a node whose kind is <see cref="JsonLdNodeKind.True"/>
/// or <see cref="JsonLdNodeKind.False"/>.
/// </summary>
/// <remarks>
/// Implementations may treat this as a pure dispatch on the kind, since the kind
/// already encodes the boolean value. The delegate exists for symmetry with the
/// other value-retrieval delegates and to keep the call site agnostic about which
/// representation the adapter uses internally.
/// </remarks>
/// <param name="handle">
/// The handle of a node whose kind is <see cref="JsonLdNodeKind.True"/>
/// or <see cref="JsonLdNodeKind.False"/>.
/// </param>
/// <returns>The boolean value.</returns>
public delegate bool GetBooleanValueDelegate(object handle);
