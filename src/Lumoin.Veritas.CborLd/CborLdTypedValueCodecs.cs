using System;
using System.Threading;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Static, process-wide registry of typed-value codec matchers for
/// CBOR-LD. Configured once via <see cref="Initialize"/> at application
/// start, consulted by the compression encoder and decoder during
/// document conversion.
/// </summary>
/// <remarks>
/// <para>
/// This registry follows the same pattern as
/// <c>CryptoFunctionRegistry&lt;,&gt;</c> and
/// <c>MulticodecHeaderRegistry</c> elsewhere in the project family:
/// initialise once at app start; throw on any resolve before
/// initialisation; reject re-initialisation.
/// </para>
/// </remarks>
public static class CborLdTypedValueCodecs
{
    private static ResolveCborLdTypedValueEncoderDelegate? encoderMatcher;
    private static ResolveCborLdTypedValueDecoderDelegate? decoderMatcher;
    private static Lock InitLock { get; } = new();

    /// <summary>
    /// Initialises the registry. Must be called exactly once per
    /// process, before any encoding or decoding of CBOR-LD documents
    /// that use typed-value codecs.
    /// </summary>
    /// <param name="encoderResolver">Matcher returning the encoder for a given type name.</param>
    /// <param name="decoderResolver">Matcher returning the decoder for a given type name.</param>
    /// <exception cref="ArgumentNullException">Either matcher is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The registry has already been initialised.</exception>
    public static void Initialize(
        ResolveCborLdTypedValueEncoderDelegate encoderResolver,
        ResolveCborLdTypedValueDecoderDelegate decoderResolver)
    {
        ArgumentNullException.ThrowIfNull(encoderResolver);
        ArgumentNullException.ThrowIfNull(decoderResolver);

        lock(InitLock)
        {
            if(encoderMatcher is not null || decoderMatcher is not null)
            {
                throw new InvalidOperationException(
                    "CborLdTypedValueCodecs has already been initialised.");
            }
            encoderMatcher = encoderResolver;
            decoderMatcher = decoderResolver;
        }
    }

    /// <summary>Indicates whether <see cref="Initialize"/> has been called.</summary>
    public static bool IsInitialized => encoderMatcher is not null;

    /// <summary>
    /// Resolves the encoder for the supplied type identifier from the
    /// registered matcher. Internal because consumers of the library
    /// drive the matcher through the encoder; calling this directly is
    /// the test path.
    /// </summary>
    /// <exception cref="InvalidOperationException">The registry has not been initialised.</exception>
    internal static CborLdTypedValueEncodeDelegate ResolveEncoder(
        string typeName,
        CborLdMatcherContext context)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(context);

        ResolveCborLdTypedValueEncoderDelegate? matcher = encoderMatcher;
        if(matcher is null)
        {
            throw new InvalidOperationException(
                "CborLdTypedValueCodecs.Initialize must be called before resolving encoders.");
        }
        return matcher(typeName, context);
    }

    /// <summary>
    /// Resolves the decoder for the supplied type identifier from the
    /// registered matcher.
    /// </summary>
    /// <exception cref="InvalidOperationException">The registry has not been initialised.</exception>
    internal static CborLdTypedValueDecodeDelegate ResolveDecoder(
        string typeName,
        CborLdMatcherContext context)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(context);

        ResolveCborLdTypedValueDecoderDelegate? matcher = decoderMatcher;
        if(matcher is null)
        {
            throw new InvalidOperationException(
                "CborLdTypedValueCodecs.Initialize must be called before resolving decoders.");
        }
        return matcher(typeName, context);
    }
}
