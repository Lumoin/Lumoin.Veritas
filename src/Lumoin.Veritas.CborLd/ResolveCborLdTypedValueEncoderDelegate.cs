namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Resolves a typed-value encoder for the supplied type identifier.
/// Implementations are expected to be exhaustive switch expressions
/// over the type names the consumer supports, throwing a clear
/// <see cref="System.ArgumentException"/> for unrecognised type names.
/// </summary>
/// <param name="typeName">The type identifier (e.g. <c>"url"</c>,
/// <c>"http://www.w3.org/2001/XMLSchema#date"</c>).</param>
/// <param name="context">Routing context carrying additional parameters
/// the matcher may need.</param>
/// <returns>The encoder delegate for the supplied type.</returns>
public delegate CborLdTypedValueEncodeDelegate ResolveCborLdTypedValueEncoderDelegate(
    string typeName,
    CborLdMatcherContext context);
