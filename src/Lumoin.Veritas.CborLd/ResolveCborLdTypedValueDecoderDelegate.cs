namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Resolves a typed-value decoder for the supplied type identifier.
/// Implementations are expected to be exhaustive switch expressions
/// over the type names the consumer supports.
/// </summary>
/// <param name="typeName">The type identifier.</param>
/// <param name="context">Routing context carrying additional parameters.</param>
/// <returns>The decoder delegate for the supplied type.</returns>
public delegate CborLdTypedValueDecodeDelegate ResolveCborLdTypedValueDecoderDelegate(
    string typeName,
    CborLdMatcherContext context);
