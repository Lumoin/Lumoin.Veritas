using System;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// A factory that produces <see cref="CborConverter"/> instances for types
/// the registry has not seen before. Used to support open-generic
/// converters and converters that depend on runtime configuration.
/// </summary>
/// <remarks>
/// The non-generic factory base is what the registry holds; concrete
/// factories override <see cref="CanConvert"/> and <see cref="CreateConverter"/>
/// to declare which runtime types they handle and produce the converter on
/// demand. AOT-compatibility considerations apply: factories must avoid
/// reflection-emit and must not require unrooted generic instantiations.
/// </remarks>
public abstract class CborConverterFactory: CborConverter
{
    /// <inheritdoc/>
    public override Type HandledType => typeof(object);

    /// <summary>
    /// Returns <c>true</c> if this factory can produce a converter for
    /// values of type <paramref name="typeToConvert"/>.
    /// </summary>
    /// <param name="typeToConvert">The runtime type to test.</param>
    public abstract bool CanConvert(Type typeToConvert);

    /// <summary>
    /// Produces a <see cref="CborConverter"/> for values of type
    /// <paramref name="typeToConvert"/>. Called only when
    /// <see cref="CanConvert"/> returned <c>true</c> for the same type.
    /// </summary>
    /// <param name="typeToConvert">The runtime type the converter handles.</param>
    /// <param name="options">The active serializer options.</param>
    /// <returns>A converter for the requested type.</returns>
    public abstract CborConverter CreateConverter(Type typeToConvert, CborSerializerOptions options);
}
