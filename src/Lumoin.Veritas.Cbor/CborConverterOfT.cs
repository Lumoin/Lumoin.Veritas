using System;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Strongly-typed CBOR converter for values of type <typeparamref name="T"/>.
/// Implementers override <see cref="Write"/> and <see cref="Read"/> to bind
/// a specific runtime shape to its CBOR encoding, including any tags that
/// distinguish the shape on the wire.
/// </summary>
/// <typeparam name="T">The .NET type the converter encodes and decodes.</typeparam>
public abstract class CborConverter<T>: CborConverter
{
    /// <inheritdoc/>
    public override Type HandledType => typeof(T);

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="writer"/> as a
    /// CBOR data item.
    /// </summary>
    /// <param name="writer">The writer to emit into.</param>
    /// <param name="value">The value to encode.</param>
    public abstract void Write(CborWriter writer, T value);

    /// <summary>
    /// Reads a value of type <typeparamref name="T"/> from
    /// <paramref name="reader"/>.
    /// </summary>
    /// <param name="reader">The reader to consume from.</param>
    /// <returns>The decoded value.</returns>
    public abstract T Read(CborReader reader);
}
